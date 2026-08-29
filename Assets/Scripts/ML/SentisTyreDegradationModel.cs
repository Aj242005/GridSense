using System;
using System.Diagnostics;
using UnityEngine;
using Unity.InferenceEngine;
using GridSense.Core;
using GridSense.Simulation;

namespace GridSense.ML
{
    /// <summary>
    /// Section 4b: Unity Sentis / InferenceEngine implementation of ITyreDegradationModel.
    /// Runs in-process inference on the exact fixed-timestep cadence (50 Hz / 0.02s) within SimulationCore.
    /// Evaluates the trained Explainable Boosting Machine (EBM) ONNX model to isolate tyre degradation
    /// and populates CarState.TyreWearPct, replacing the heuristic fallback estimator.
    /// </summary>
    public class SentisTyreDegradationModel : MonoBehaviour, ITyreDegradationModel
    {
        [Header("Model Configuration")]
        [Tooltip("Serialized reference to ebm_tyre_degradation.onnx ModelAsset")]
        [SerializeField] private ModelAsset tyreModelAsset;

        [Tooltip("Backend type for neural execution. CPU is deterministic and sub-millisecond for EBMs.")]
        [SerializeField] private BackendType backendType = BackendType.CPU;

        [Header("Degradation Scaling")]
        [Tooltip("Calibrated pace loss delta in seconds corresponding to 100% tyre wear (calibrated to 1.20s based on FastF1 stint cliff data and TyreModel physics)")]
        [SerializeField] private float cliffPaceDeltaThresholdSec = 1.20f;

        [Header("Runtime Telemetry & Performance")]
        [SerializeField] private float lastPredictedPaceDeltaSec;
        [SerializeField] private float lastInferenceTimeMicroseconds;
        [SerializeField] private float averageInferenceTimeMicroseconds;
        [SerializeField] private long totalInferenceTicks;

        private Model _runtimeModel;
        private Worker _worker;
        private Stopwatch _stopwatch;
        private double _accumulatedInferenceUs = 0;

        // Reusable input tensors to minimize GC allocation
        private Tensor<float> _tLapInStint;
        private Tensor<float> _tFuelRemaining;
        private Tensor<float> _tGapAhead;
        private Tensor<float> _tSessionProg;
        private Tensor<float> _tTrackTemp;
        private Tensor<float> _tCompoundCode;

        public float LastPredictedPaceDeltaSec => lastPredictedPaceDeltaSec;
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
            if (tyreModelAsset == null)
            {
                UnityEngine.Debug.LogWarning("[SentisTyreDegradationModel] No ModelAsset assigned. Attempting to load from Resources/Data...");
                return;
            }

            try
            {
                _runtimeModel = ModelLoader.Load(tyreModelAsset);
                _worker = new Worker(_runtimeModel, backendType);
                _stopwatch = new Stopwatch();

                // Allocate persistent input tensors (1 element each)
                _tLapInStint = new Tensor<float>(new TensorShape(1), new float[] { 1f });
                _tFuelRemaining = new Tensor<float>(new TensorShape(1), new float[] { 100f });
                _tGapAhead = new Tensor<float>(new TensorShape(1), new float[] { 15f });
                _tSessionProg = new Tensor<float>(new TensorShape(1), new float[] { 0f });
                _tTrackTemp = new Tensor<float>(new TensorShape(1), new float[] { 35f });
                _tCompoundCode = new Tensor<float>(new TensorShape(1), new float[] { 0f });

                UnityEngine.Debug.Log($"[SentisTyreDegradationModel] Initialized Sentis worker successfully on backend: {backendType}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SentisTyreDegradationModel] Failed to initialize Sentis model: {ex.Message}");
            }
        }

        public void RegisterWithSimulationCore()
        {
            var simCore = GetComponentInParent<SimulationCore>();
            if (simCore == null)
                simCore = UnityEngine.Object.FindAnyObjectByType<SimulationCore>();

            if (simCore != null)
            {
                simCore.RegisterTyreDegradationModel(this);
                UnityEngine.Debug.Log("[SentisTyreDegradationModel] Successfully registered with SimulationCore. Fallback wear estimator disabled.");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[SentisTyreDegradationModel] SimulationCore not found in scene. Make sure to register manually.");
            }
        }

        /// <summary>
        /// ITyreDegradationModel implementation:
        /// Called synchronously on the fixed-timestep cadence (50 Hz) inside SimulationCore.FixedUpdate.
        /// </summary>
        public void StepInference(ref CarState state, float fixedDeltaTime)
        {
            if (_worker == null) return;

            _stopwatch.Restart();

            // 1. Map CarState into feature representations
            float lapInStint = Mathf.Max(1.0f, (float)state.Lap);
            float fuelRemaining = Mathf.Max(5.0f, state.FuelLoadKg);
            // Clean-air sentinel: When HasGapAhead is false, substitute 15.0s.
            // When HasGapAhead is true, clamp to [0, 15.0] so gaps > 15s don't become out-of-distribution.
            float gapAhead = state.HasGapAhead ? Mathf.Clamp(state.GapAheadS, 0.0f, 15.0f) : 15.0f;
            float sessionProg = Mathf.Clamp01((float)state.Lap / 58.0f);   // 58 lap reference distance
            float trackTemp = 35.0f; // nominal asphalt temperature

            // Compound encoding: directly cast from TyreCompound enum (Soft=0, Medium=1, Hard=2)
            // Prevents silent behavioral drift if enum order is modified.
            float compoundCode = (float)(int)state.Compound;

            // 2. Populate input tensors
            _tLapInStint[0] = lapInStint;
            _tFuelRemaining[0] = fuelRemaining;
            _tGapAhead[0] = gapAhead;
            _tSessionProg[0] = sessionProg;
            _tTrackTemp[0] = trackTemp;
            _tCompoundCode[0] = compoundCode;

            // 3. Set Inputs
            _worker.SetInput("LapInStint", _tLapInStint);
            _worker.SetInput("FuelRemainingKg", _tFuelRemaining);
            _worker.SetInput("GapAheadSec", _tGapAhead);
            _worker.SetInput("SessionProgression", _tSessionProg);
            _worker.SetInput("TrackTemp", _tTrackTemp);
            _worker.SetInput("CompoundCode", _tCompoundCode);

            // 4. Execute Inference
            _worker.Schedule();

            // 5. Read Output
            var rawTensor = _worker.PeekOutput("prediction") as Tensor<float>;
            if (rawTensor != null)
            {
                using var predTensor = rawTensor.ReadbackAndClone();
                lastPredictedPaceDeltaSec = predTensor[0];

                // Map isolated pace delta into physical tyre wear (0 - 100%)
                float normalizedWear = Mathf.Clamp01(lastPredictedPaceDeltaSec / cliffPaceDeltaThresholdSec);
                state.TyreWearPct = normalizedWear * 100.0f;
            }

            _stopwatch.Stop();

            // Profiling metrics
            totalInferenceTicks++;
            lastInferenceTimeMicroseconds = (float)(_stopwatch.Elapsed.TotalMilliseconds * 1000.0);
            _accumulatedInferenceUs += lastInferenceTimeMicroseconds;
            averageInferenceTimeMicroseconds = (float)(_accumulatedInferenceUs / totalInferenceTicks);
        }

        private void OnDestroy()
        {
            _tLapInStint?.Dispose();
            _tFuelRemaining?.Dispose();
            _tGapAhead?.Dispose();
            _tSessionProg?.Dispose();
            _tTrackTemp?.Dispose();
            _tCompoundCode?.Dispose();

            _worker?.Dispose();
            _worker = null;
        }
    }
}
