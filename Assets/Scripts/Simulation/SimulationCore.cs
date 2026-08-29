using System;
using UnityEngine;
using GridSense.Core;
using GridSense.Physics;

namespace GridSense.Simulation
{
    /// <summary>
    /// Contract for Section 4 Tyre Degradation ML Model inference (in-process via Unity Sentis).
    /// Executes on the fixed-timestep simulation cadence to predict CarState.TyreWearPct.
    /// </summary>
    public interface ITyreDegradationModel
    {
        void StepInference(ref CarState state, float fixedDeltaTime);
    }

    /// <summary>
    /// Contract for Section 5 Energy Deployment Policy RL Model inference (in-process via Unity Sentis).
    /// Executes on the fixed-timestep simulation cadence to select CarState.DeploymentMode and CarState.Braking.
    /// </summary>
    public interface IEnergyDeploymentPolicy
    {
        void StepInference(ref CarState state, float fixedDeltaTime);
    }

    /// <summary>
    /// Section 3.8: Simulation Core:
    /// Coordinates the deterministic fixed-timestep simulation loop:
    /// 1. Pre-physics: Input / traffic updates
    /// 2. Physics step: VehicleController (Dynamics, Aero, Powertrain, Brakes, Tyre Pacejka & slip)
    /// 3. Sentis ML Inference step: Executes Tyre Degradation (Track 3) and Energy Deployment (Track 1)
    ///    on the EXACT same fixed-timestep cadence as physics — never gated by render framerate.
    /// 4. Decoupled render loop: Update() handles rendering and UI dashboard updates with interpolation.
    /// 5. Time acceleration: Supports 1x, 2x, 5x, 10x simulation speeds for fast evaluation.
    /// </summary>
    public class SimulationCore : MonoBehaviour
    {
        [Header("Vehicle Reference")]
        [SerializeField] private VehicleController vehicleController;

        [Header("Fixed Timestep Configuration")]
        [Tooltip("Target fixed simulation frequency (Hz)")]
        [SerializeField] private float fixedFrequencyHz = 50f;

        [Header("Simulation Speed & Control")]
        [Range(0.1f, 10.0f)]
        [SerializeField] private float simulationTimeScale = 1.0f;
        [SerializeField] private bool isSimulationPaused = false;

        [Header("Active ML Models (In-Process Sentis)")]
        // Assigned by Section 4 and Section 5 components
        private ITyreDegradationModel _tyreDegradationModel;
        private IEnergyDeploymentPolicy _energyDeploymentPolicy;

        // Baseline fallback wear estimator (active until Section 4 Sentis model is loaded)
        private bool _useFallbackWearEstimator = true;
        private float _fallbackEstimatedWearPct = 0f;

        // Statistics
        private long _fixedTickCount = 0;
        private float _simulationElapsedTime = 0f;

        public long FixedTickCount => _fixedTickCount;
        public float SimulationElapsedTime => _simulationElapsedTime;
        public VehicleController Vehicle => vehicleController;
        public float TimeScale => simulationTimeScale;

        private void Awake()
        {
            Time.fixedDeltaTime = 1.0f / fixedFrequencyHz;
            Time.timeScale = simulationTimeScale;

            if (vehicleController == null)
                vehicleController = GetComponentInChildren<VehicleController>();
        }

        public void RegisterTyreDegradationModel(ITyreDegradationModel model)
        {
            _tyreDegradationModel = model;
            _useFallbackWearEstimator = (model == null);
            Debug.Log($"[SimulationCore] Registered Tyre Degradation Model: {model?.GetType().Name ?? "Fallback"}");
        }

        public void RegisterEnergyDeploymentPolicy(IEnergyDeploymentPolicy policy)
        {
            _energyDeploymentPolicy = policy;
            Debug.Log($"[SimulationCore] Registered Energy Deployment Policy: {policy?.GetType().Name ?? "None"}");
        }

        public void SetTimeScale(float scale)
        {
            simulationTimeScale = Mathf.Clamp(scale, 0.1f, 10.0f);
            if (!isSimulationPaused)
            {
                Time.timeScale = simulationTimeScale;
            }
        }

        public void SetPaused(bool paused)
        {
            isSimulationPaused = paused;
            Time.timeScale = isSimulationPaused ? 0f : simulationTimeScale;
        }

        /// <summary>
        /// Master deterministic fixed-timestep loop:
        /// Runs physics and in-process Sentis ML inference on the exact same fixed cadence.
        /// </summary>
        private void FixedUpdate()
        {
            if (isSimulationPaused || vehicleController == null) return;

            float dt = Time.fixedDeltaTime;
            _fixedTickCount++;
            _simulationElapsedTime += dt;

            // ─────────────────────────────────────────────────────────────────
            // 1. VEHICLE CONTROLLER PHYSICS TICK
            // (Dynamics, Aero, Powertrain, Brakes, Tyre combined slip & thermal wear)
            // ─────────────────────────────────────────────────────────────────
            // VehicleController executes its FixedUpdate on the GameObject.
            CarState currentState = vehicleController.State;

            // ─────────────────────────────────────────────────────────────────
            // 2. IN-PROCESS SENTIS ML INFERENCE TICK (Fixed Timestep Synchronized)
            // ─────────────────────────────────────────────────────────────────

            // A. Track 3: Tyre Degradation Model (Section 4)
            // Infers and writes CarState.TyreWearPct from observable telemetry
            if (_tyreDegradationModel != null)
            {
                _tyreDegradationModel.StepInference(ref currentState, dt);
            }
            else if (_useFallbackWearEstimator)
            {
                // Baseline heuristic estimator until Section 4 Sentis model is hooked up:
                // Integrates wear rate over time to populate CarState.TyreWearPct
                _fallbackEstimatedWearPct = Mathf.Clamp(_fallbackEstimatedWearPct + (currentState.TyreWearRateCurrent * dt), 0f, 100f);
                currentState.TyreWearPct = _fallbackEstimatedWearPct;
            }

            // B. Track 1: Energy Deployment RL Policy (Section 5)
            // Infers tactical CarState.DeploymentMode and CarState.Braking
            if (_energyDeploymentPolicy != null)
            {
                _energyDeploymentPolicy.StepInference(ref currentState, dt);
            }

            // ─────────────────────────────────────────────────────────────────
            // 3. SYNC STATE BACK TO VEHICLE CONTROLLER
            // ─────────────────────────────────────────────────────────────────
            vehicleController.UpdateCarStateFromExternal(currentState);
        }

        /// <summary>
        /// Decoupled frame update: strictly for camera tracking, UI rendering,
        /// and telemetry dashboard refresh. Never gates physics or ML inference.
        /// </summary>
        private void Update()
        {
            // Frame rate independent rendering / UI hook
        }
    }
}
