using System;
using UnityEngine;
using GridSense.Core;
using GridSense.Data;

namespace GridSense.Physics
{
    /// <summary>
    /// Master vehicle coordinator layered on top of Unity's WheelCollider and Rigidbody foundation.
    /// Integrates:
    /// - Section 3.1: TyreModel (Pacejka combined slip, thermodynamics, cumulative wear)
    /// - Section 3.2: VehicleDynamics (Rigid body mass/fuel, CG load transfer, ARBs, ride heights)
    /// - Section 3.3: Aerodynamics (Downforce, drag, ground effect, DRS, dirty-air wake model)
    /// - Section 3.4: PowertrainSystem (ICE, fuel consumption, MGU-K deploy/regen, braking-aggressiveness coupling)
    /// - Section 3.5: BrakeSystem (Carbon-carbon thermal model, fade curve, front/rear bias)
    /// - Section 3.6: TrackEnvironmentSystem (Lap/sector/distance tracking, track evolution confound)
    /// - Section 3.7: OpponentTrafficSystem (Rule-based traffic, gap calculation feeding dirty air)
    /// 
    /// All inputs and outputs read from and write to the single shared CarState contract.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(VehicleDynamics))]
    [RequireComponent(typeof(Aerodynamics))]
    [RequireComponent(typeof(PowertrainSystem))]
    [RequireComponent(typeof(TyreModel))]
    [RequireComponent(typeof(BrakeSystem))]
    [RequireComponent(typeof(TrackEnvironmentSystem))]
    [RequireComponent(typeof(OpponentTrafficSystem))]
    public class VehicleController : MonoBehaviour
    {
        [Header("WheelColliders (FL=0, FR=1, RL=2, RR=3)")]
        [SerializeField] private WheelCollider wheelFL;
        [SerializeField] private WheelCollider wheelFR;
        [SerializeField] private WheelCollider wheelRL;
        [SerializeField] private WheelCollider wheelRR;

        [Header("Steering Parameters")]
        [Tooltip("Maximum steering angle in degrees (nominal F1 ~18-20 deg)")]
        [SerializeField] private float maxSteerAngle = 19.5f;

        [Tooltip("Steering angle speed-sensitive reduction factor")]
        [SerializeField] private float highSpeedSteerReduction = 0.45f;

        [Header("Current Runtime State")]
        [Tooltip("The canonical single-source-of-truth CarState instance")]
        [SerializeField] private CarState carState;

        // Subsystem references
        private Rigidbody rb;
        private VehicleDynamics dynamics;
        private Aerodynamics aero;
        private PowertrainSystem powertrain;
        private TyreModel tyreModel;
        private BrakeSystem brakeSystem;
        private TrackEnvironmentSystem trackEnv;
        private OpponentTrafficSystem traffic;

        // Driver inputs [0, 1] / [-1, 1]
        private float _throttleInput;
        private float _steerInput;
        private float _brakeInput;

        public CarState State => carState;
        public Rigidbody Rigidbody => rb;
        public VehicleDynamics Dynamics => dynamics;
        public Aerodynamics Aero => aero;
        public PowertrainSystem Powertrain => powertrain;
        public TyreModel TyreModel => tyreModel;
        public BrakeSystem BrakeSystem => brakeSystem;
        public TrackEnvironmentSystem TrackEnv => trackEnv;
        public OpponentTrafficSystem Traffic => traffic;

        public float ForwardSpeedMs => rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
        public float SpeedKmh => ForwardSpeedMs * 3.6f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            dynamics = GetComponent<VehicleDynamics>();
            aero = GetComponent<Aerodynamics>();
            powertrain = GetComponent<PowertrainSystem>();
            tyreModel = GetComponent<TyreModel>();
            brakeSystem = GetComponent<BrakeSystem>();
            trackEnv = GetComponent<TrackEnvironmentSystem>();
            traffic = GetComponent<OpponentTrafficSystem>();

            if (wheelFL == null || wheelFR == null || wheelRL == null || wheelRR == null)
            {
                foreach (var wc in GetComponentsInChildren<WheelCollider>())
                {
                    if (wc.name.Contains("FL")) wheelFL = wc;
                    else if (wc.name.Contains("FR")) wheelFR = wc;
                    else if (wc.name.Contains("RL")) wheelRL = wc;
                    else if (wc.name.Contains("RR")) wheelRR = wc;
                }
            }

            // Initialize default CarState values
            carState.Compound = tyreModel.ActiveCompound;
            carState.FuelLoadKg = 100.0f; // starting race fuel (100 kg)
            carState.DeploymentMode = EnergyMode.Balanced;
            carState.Braking = BrakingAggressiveness.Normal;
            carState.EnergyRemainingPct = 100.0f;
            carState.TrackEvolutionFactor = 0.94f; // green track start
            carState.DirtyAir = false;
            carState.DrsOpen = false;
        }

        public void SetInputs(float throttle, float steer, float brake)
        {
            _throttleInput = Mathf.Clamp01(throttle);
            _steerInput = Mathf.Clamp(steer, -1.0f, 1.0f);
            _brakeInput = Mathf.Clamp01(brake);
        }

        public void SetDrs(bool open)
        {
            carState.DrsOpen = open;
            if (aero != null)
                aero.DrsOpen = open;
        }

        private float _currentSteerAngle;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float currentSpeedMs = ForwardSpeedMs;

            // 1. Update Track & Environment (Section 3.6: Lap, Sector, Distance, Track Evolution Confound)
            trackEnv.UpdateTrackStep(ref carState, transform.position, currentSpeedMs, dt);

            // 2. Update Opponent Traffic (Section 3.7: GapAhead, GapBehind)
            float normProgress = trackEnv.ActiveTrack != null 
                ? carState.DistanceIntoLapM / Mathf.Max(trackEnv.ActiveTrack.trackLengthMetres, 1f) 
                : 0f;
            traffic.UpdateTraffic(ref carState, normProgress, dt);

            // 3. Update Vehicle Dynamics (Section 3.2: Mass from FuelLoadKg, CG, Anti-Roll Bars, Ride Heights)
            dynamics.UpdateVehicleDynamics(ref carState, dt);

            // 4. Update Aerodynamics (Section 3.3: Downforce, Drag, Ground effect, Dirty-air wake from Traffic gaps)
            aero.UpdateAero(ref carState, currentSpeedMs, dt);

            // 5. Apply Steering (with speed-sensitive reduction and smooth rate limiting)
            float speedFactor = Mathf.Clamp01(Mathf.Abs(currentSpeedMs) / 75.0f); // ~270 km/h
            float targetSteerAngle = _steerInput * Mathf.Lerp(maxSteerAngle, maxSteerAngle * 0.35f, speedFactor);
            _currentSteerAngle = Mathf.MoveTowards(_currentSteerAngle, targetSteerAngle, 75.0f * dt);

            if (wheelFL != null) wheelFL.steerAngle = _currentSteerAngle;
            if (wheelFR != null) wheelFR.steerAngle = _currentSteerAngle;

            // 5b. F1 High-Speed Aerodynamic Lateral Grip & Heading Stability (Smoothly speed-gated)
            if (Mathf.Abs(currentSpeedMs) > 4.0f && rb != null)
            {
                float speedFade = Mathf.Clamp01((Mathf.Abs(currentSpeedMs) - 4.0f) / 10.0f);

                // Smooth lateral drift damping proportional to forward speed
                Vector3 lateralVelocity = Vector3.Dot(rb.linearVelocity, transform.right) * transform.right;
                rb.AddForce(-lateralVelocity * (rb.mass * 3.5f * speedFade));

                // Active yaw damping: locks straight-line stability and prevents snap-oversteer
                float yawVelocity = Vector3.Dot(rb.angularVelocity, transform.up);
                if (Mathf.Abs(_steerInput) < 0.05f)
                {
                    rb.AddTorque(-transform.up * (yawVelocity * 2000f * speedFade));
                }
                else
                {
                    rb.AddTorque(-transform.up * (yawVelocity * 600f * speedFade));
                }
            }
            else if (Mathf.Abs(currentSpeedMs) < 0.08f && _throttleInput < 0.01f && _brakeInput < 0.01f && rb != null)
            {
                // Rock-solid stationary rest (eliminates sub-millimeter physics jitter and vibrations)
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // 6. Update Powertrain & Energy (Section 3.4: ICE torque, fuel burn, MGU-K deploy/regen)
            powertrain.UpdatePowertrain(ref carState, _throttleInput, _brakeInput, currentSpeedMs, dt);

            // 7. High-Performance F1 Driving, Braking & Reversing Engine
            float fwdSpeed = ForwardSpeedMs;

            if (_throttleInput > 0.01f)
            {
                if (fwdSpeed < -0.2f)
                {
                    // Rolling backwards: Hard friction brakes to stop reverse motion immediately
                    brakeSystem.ApplyBraking(_throttleInput * 2.0f, carState.Braking, currentSpeedMs, tyreModel.AmbientTempC, dt);
                    // Also deliver forward torque so the moment speed crosses 0, it pushes forward instantly!
                    SetDriveTorque(powertrain.TotalDriveTorqueNm * _throttleInput);
                }
                else
                {
                    // Driving forward: release brakes and deliver full powertrain torque
                    brakeSystem.ReleaseBrakes(currentSpeedMs, tyreModel.AmbientTempC, dt);
                    SetDriveTorque(powertrain.TotalDriveTorqueNm * _throttleInput);
                }
            }
            else if (_brakeInput > 0.01f)
            {
                if (fwdSpeed > 0.2f)
                {
                    // Moving forward: Apply full carbon-carbon disc braking
                    brakeSystem.ApplyBraking(_brakeInput * 1.5f, carState.Braking, currentSpeedMs, tyreModel.AmbientTempC, dt);
                    SetDriveTorque(0f);
                }
                else
                {
                    // Stationary or reversing: release brakes and deliver smooth reverse torque
                    brakeSystem.ReleaseBrakes(currentSpeedMs, tyreModel.AmbientTempC, dt);
                    SetDriveTorque(-_brakeInput * 3500f);
                }
            }
            else
            {
                // Coasting: release brakes, zero drive torque
                brakeSystem.ReleaseBrakes(currentSpeedMs, tyreModel.AmbientTempC, dt);
                SetDriveTorque(0f);
            }

            // 8. Update Tyre Model (Section 3.1: Pacejka, Thermodynamics, Slip-energy wear, Track Evolution Confound)
            tyreModel.UpdatePhysicsStep(dt, currentSpeedMs, carState.TrackEvolutionFactor);

            // 9. Write telemetry into shared CarState contract
            tyreModel.WriteToCarState(ref carState);
            brakeSystem.WriteToCarState(ref carState);
        }

        private void SetDriveTorque(float torquePerAxle)
        {
            // Traction Control System (TCS): prevents wheelspin from breaking traction
            float tcsMultiplier = 1.0f;
            if (torquePerAxle > 0.01f && wheelRL != null && wheelRR != null)
            {
                float rearRpm = (wheelRL.rpm + wheelRR.rpm) * 0.5f;
                float rearWheelSpeed = rearRpm * (Mathf.PI / 30f) * 0.36f;
                float carSpeed = Mathf.Max(0.5f, ForwardSpeedMs);
                float slipSpeed = rearWheelSpeed - carSpeed;
                if (slipSpeed > 2.0f)
                {
                    tcsMultiplier = Mathf.Clamp01(1.0f - ((slipSpeed - 2.0f) / 4.0f));
                }
            }

            float torquePerWheel = (torquePerAxle * tcsMultiplier) * 0.5f;
            if (wheelRL != null) wheelRL.motorTorque = torquePerWheel;
            if (wheelRR != null) wheelRR.motorTorque = torquePerWheel;
        }

        /// <summary>
        /// Mutates shared state for downstream controllers (e.g. AI strategy mode updates).
        /// </summary>
        public void UpdateCarStateFromExternal(CarState updatedState)
        {
            carState.DeploymentMode = updatedState.DeploymentMode;
            carState.Braking = updatedState.Braking;
            carState.DrsOpen = updatedState.DrsOpen;
        }
    }
}
