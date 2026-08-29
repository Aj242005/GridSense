using System;
using System.Diagnostics;
using UnityEngine;
using Unity.InferenceEngine;
using GridSense.Core;
using GridSense.Physics;
using GridSense.Simulation;

namespace GridSense.ML
{
    /// <summary>
    /// Section 5: Unity Sentis / InferenceEngine implementation of IEnergyDeploymentPolicy.
    /// Runs in-process inference on the fixed-timestep simulation cadence inside SimulationCore.
    /// Evaluates the trained PPO Actor policy (ppo_energy_deployment.onnx) to choose:
    /// 1. Energy Deployment Mode (Push, Balanced, Hold, Save)
    /// 2. Braking Aggressiveness (Normal, Aggressive)
    /// </summary>
    public class SentisEnergyDeploymentModel : MonoBehaviour, IEnergyDeploymentPolicy
    {
        [Header("Model Configuration")]
        [Tooltip("Serialized reference to ppo_energy_deployment.onnx ModelAsset")]
        [SerializeField] private ModelAsset energyModelAsset;

        [Tooltip("Backend type for neural execution. CPU is deterministic and sub-millisecond for small MLPs.")]
        [SerializeField] private BackendType backendType = BackendType.CPU;

        [Header("Cadence Configuration")]
        [Tooltip("Decision frequency divisor (e.g. 10 ticks = 5 Hz decision rate at 50 Hz physics)")]
        [SerializeField] private int decisionTickInterval = 10;

        [Header("Runtime Telemetry & Performance")]
        [SerializeField] private EnergyMode activeDeploymentMode = EnergyMode.Balanced;
        [SerializeField] private BrakingAggressiveness activeBraking = BrakingAggressiveness.Normal;
        [SerializeField] private float lastInferenceTimeMicroseconds;
        [SerializeField] private float averageInferenceTimeMicroseconds;
        [SerializeField] private long totalInferenceTicks;

        private Model _runtimeModel;
        private Worker _worker;
        private Stopwatch _stopwatch;
        private double _accumulatedInferenceUs = 0;
        private int _tickCounter = 0;

        // Reusable input tensor to eliminate per-step GC allocations
        private Tensor<float> _tObs;

        // Track constants for Bahrain (5,412m)
        private const float TrackLengthM = 5412.0f;
        private static readonly float[] BrakingZoneStarts = new float[] { 950f, 2050f, 3050f, 3650f, 4850f };

        [Header("Explainability & Tactical Telemetry")]
        [Range(-1f, 1f)]
        [SerializeField] private float riskRewardScore;
        [TextArea(2, 4)]
        [SerializeField] private string tacticalExplanation = "Initializing tactical policy...";

        public EnergyMode ActiveDeploymentMode => activeDeploymentMode;
        public BrakingAggressiveness ActiveBraking => activeBraking;
        public float RiskRewardScore => riskRewardScore;
        public string TacticalExplanation => tacticalExplanation;
        public float LastInferenceTimeMicroseconds => lastInferenceTimeMicroseconds;
        public float AverageInferenceTimeMicroseconds => averageInferenceTimeMicroseconds;
        public bool IsInitialized => _worker != null;

        private void Awake()
        {
            InitializeModel();
        }

        private void Start()
        {
            RegisterWithSimulationCore();
        }

        public void InitializeModel()
        {
            if (energyModelAsset == null)
            {
                // Attempt to load from AssetDatabase / Resources if unassigned in inspector
                #if UNITY_EDITOR
                energyModelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Data/Models/ppo_energy_deployment.onnx");
                #endif
                if (energyModelAsset == null)
                {
                    UnityEngine.Debug.LogWarning("[SentisEnergyDeploymentModel] No ModelAsset assigned. Ensure ppo_energy_deployment.onnx is loaded.");
                    return;
                }
            }

            try
            {
                _runtimeModel = ModelLoader.Load(energyModelAsset);
                _worker = new Worker(_runtimeModel, backendType);
                _stopwatch = new Stopwatch();

                // Allocate reusable observation tensor of shape (1, 8)
                _tObs = new Tensor<float>(new TensorShape(1, 8));

                UnityEngine.Debug.Log($"[SentisEnergyDeploymentModel] PPO Energy Policy loaded successfully into Sentis ({backendType}). Inputs: 1x8, Outputs: Discrete(8).");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SentisEnergyDeploymentModel] Failed to initialize Sentis worker: {ex.Message}");
            }
        }

        public void RegisterWithSimulationCore()
        {
            var simCore = FindFirstObjectByType<SimulationCore>();
            if (simCore != null)
            {
                simCore.RegisterEnergyDeploymentPolicy(this);
                UnityEngine.Debug.Log("[SentisEnergyDeploymentModel] Successfully registered with SimulationCore.");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[SentisEnergyDeploymentModel] SimulationCore not found in scene.");
            }
        }

        private float GetDistanceToNextBrakingZone(float distIntoLap)
        {
            float distWrapped = distIntoLap % TrackLengthM;
            float minDist = TrackLengthM;
            for (int i = 0; i < BrakingZoneStarts.Length; i++)
            {
                float d = BrakingZoneStarts[i] >= distWrapped
                    ? (BrakingZoneStarts[i] - distWrapped)
                    : (TrackLengthM - distWrapped + BrakingZoneStarts[i]);
                if (d < minDist) minDist = d;
            }
            return minDist;
        }

        /// <summary>
        /// IEnergyDeploymentPolicy implementation:
        /// Called synchronously on the fixed-timestep cadence (50 Hz) inside SimulationCore.FixedUpdate.
        /// </summary>
        public void StepInference(ref CarState state, float fixedDeltaTime)
        {
            if (_worker == null) return;

            _tickCounter++;
            if (_tickCounter < decisionTickInterval)
            {
                // Apply cached policy decision across the 5Hz decision window
                state.DeploymentMode = activeDeploymentMode;
                state.Braking = activeBraking;
                return;
            }
            _tickCounter = 0;

            _stopwatch.Restart();

            // 1. Formulate 8-element observation vector
            float speedMs = state.DistanceIntoLapM > 0 ? (fixedDeltaTime > 0 ? (state.DistanceIntoLapM / fixedDeltaTime) : 50.0f) : 40.0f;
            var simCore = FindFirstObjectByType<SimulationCore>();
            if (simCore != null && simCore.Vehicle != null)
            {
                speedMs = simCore.Vehicle.ForwardSpeedMs;
            }

            float distToBrake = GetDistanceToNextBrakingZone(state.DistanceIntoLapM);
            float gapAhead = state.HasGapAhead ? Mathf.Clamp(state.GapAheadS, 0.0f, 15.0f) : 15.0f;

            _tObs[0] = Mathf.Clamp01(state.EnergyRemainingPct / 100.0f);
            _tObs[1] = Mathf.Clamp01(speedMs / 100.0f);
            _tObs[2] = Mathf.Clamp01((state.DistanceIntoLapM % TrackLengthM) / TrackLengthM);
            _tObs[3] = Mathf.Clamp01(distToBrake / 1000.0f);
            _tObs[4] = Mathf.Clamp01(state.TyreWearPct / 100.0f);
            _tObs[5] = Mathf.Clamp01((state.BrakeTempC - 30.0f) / 970.0f);
            _tObs[6] = Mathf.Clamp01(gapAhead / 15.0f);
            _tObs[7] = Mathf.Clamp01(state.FuelLoadKg / 105.0f);

            // 2. Schedule Sentis inference
            _worker.SetInput("Observations", _tObs);
            _worker.Schedule();

            // 3. Read back single Discrete(8) action output
            int actionIdx = 0;
            var rawOutput = _worker.PeekOutput("Actions");
            if (rawOutput is Tensor<int> intActions)
            {
                using var cloned = intActions.ReadbackAndClone();
                actionIdx = cloned[0];
            }
            else if (rawOutput is Tensor<long> longActions)
            {
                using var cloned = longActions.ReadbackAndClone();
                actionIdx = (int)cloned[0];
            }
            else if (rawOutput is Tensor<float> floatActions)
            {
                using var cloned = floatActions.ReadbackAndClone();
                actionIdx = Mathf.RoundToInt(cloned[0]);
            }

            // Decode Discrete(8) into [deploy_mode (0..3), braking_mode (0..1)]
            actionIdx = Mathf.Clamp(actionIdx, 0, 7);
            activeDeploymentMode = (EnergyMode)(actionIdx / 2);
            activeBraking = (BrakingAggressiveness)(actionIdx % 2);

            state.DeploymentMode = activeDeploymentMode;
            state.Braking = activeBraking;

            // 4. Compute Explainability & Risk-Reward Attribution
            ComputeExplainability(state, speedMs, distToBrake, gapAhead);

            _stopwatch.Stop();

            // 5. Performance benchmarking
            lastInferenceTimeMicroseconds = (float)(_stopwatch.Elapsed.TotalMilliseconds * 1000.0);
            _accumulatedInferenceUs += lastInferenceTimeMicroseconds;
            totalInferenceTicks++;
            averageInferenceTimeMicroseconds = (float)(_accumulatedInferenceUs / totalInferenceTicks);
        }

        private void ComputeExplainability(CarState state, float speedMs, float distToBrake, float gapAhead)
        {
            // A. Expected Reward components
            float speedReward = (speedMs / 80.0f) * 0.5f;
            float harvestReward = (activeBraking == BrakingAggressiveness.Aggressive) ? 0.35f : 0.0f;
            float deployReward = (activeDeploymentMode == EnergyMode.Push) ? 0.40f : ((activeDeploymentMode == EnergyMode.Balanced) ? 0.20f : 0.0f);
            float totalExpectedReward = speedReward + harvestReward + deployReward;

            // B. Expected Risk components
            float tyreRisk = Mathf.Clamp01(state.TyreWearPct / 70.0f) * 0.4f;
            float brakeRisk = Mathf.Clamp01((state.BrakeTempC - 700.0f) / 250.0f) * 0.4f;
            float energyDeficitRisk = Mathf.Clamp01((30.0f - state.EnergyRemainingPct) / 30.0f) * 0.4f;
            float totalExpectedRisk = tyreRisk + brakeRisk + energyDeficitRisk;

            riskRewardScore = Mathf.Clamp(totalExpectedReward - totalExpectedRisk, -1.0f, 1.0f);

            // C. Real-Time Human-Readable "Why" Tactical Synthesizer
            if (distToBrake < 200f)
            {
                if (state.BrakeTempC > 800f)
                    tacticalExplanation = $"Braking approach ({distToBrake:F0}m): Normal braking selected to protect hot carbon discs ({state.BrakeTempC:F0}°C) from thermal fade.";
                else if (activeBraking == BrakingAggressiveness.Aggressive)
                    tacticalExplanation = $"Heavy braking zone approach ({distToBrake:F0}m): Aggressive regen activated to harvest +121.5kW into battery before next straight.";
                else
                    tacticalExplanation = $"Corner entry ({distToBrake:F0}m): Balanced braking profile chosen for stability.";
            }
            else if (distToBrake > 350f)
            {
                if (state.EnergyRemainingPct > 35f && activeDeploymentMode == EnergyMode.Push)
                    tacticalExplanation = $"High-speed straight ({distToBrake:F0}m to corner): Push boost (+120kW) deployed with healthy battery ({state.EnergyRemainingPct:F0}% SoC) to maximize top-end delta.";
                else if (state.EnergyRemainingPct > 35f && activeDeploymentMode == EnergyMode.Balanced)
                    tacticalExplanation = $"Straightaway acceleration: Balanced deployment (+65kW) active to build delta while protecting end-of-lap battery target.";
                else if (state.EnergyRemainingPct <= 35f)
                    tacticalExplanation = $"Straightaway cruising: Energy held ({state.EnergyRemainingPct:F0}% SoC) to preserve reserve for DRS overtaking.";
                else
                    tacticalExplanation = $"High-speed sector: Save mode engaged to preserve battery reserve for upcoming sectors.";
            }
            else
            {
                if (state.HasGapAhead && gapAhead < 1.5f)
                    tacticalExplanation = $"Dirty air wake (gap {gapAhead:F1}s): Managing battery deployment to stay within DRS striking distance without cooking tyres.";
                else
                    tacticalExplanation = $"Technical corner sequence: {activeDeploymentMode} deployment with {activeBraking} braking maintained for chassis balance.";
            }
        }

        private void OnDestroy()
        {
            _worker?.Dispose();
            _worker = null;
            _tObs?.Dispose();
            _tObs = null;
        }
    }
}
