using System;
using UnityEngine;
using GridSense.Core;

namespace GridSense.Physics
{
    /// <summary>
    /// Section 3.3: Aerodynamics:
    /// 1. Quadratic aerodynamic downforce and drag forces (1/2 * rho * v^2 * C * A)
    /// 2. Aero balance distribution between front and rear axles
    /// 3. Ride height sensitivity (ground effect underfloor scaling)
    /// 4. DRS (Drag Reduction System) actuation
    /// 5. Dirty air wake model: following distance downforce loss and slipstream tow,
    ///    directly feeding CarState.DirtyAir.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(VehicleDynamics))]
    public class Aerodynamics : MonoBehaviour
    {
        [Header("Aero Coefficients (Clean Air)")]
        [Tooltip("Nominal downforce coefficient (Cl * A) in clean air (~4.2 to 5.0 m^2 for modern F1)")]
        [SerializeField] private float baseDownforceCoeff = 4.60f;

        [Tooltip("Nominal drag coefficient (Cd * A) in clean air (~1.20 to 1.50 m^2)")]
        [SerializeField] private float baseDragCoeff = 1.35f;

        [Tooltip("Air density rho in kg/m^3 at sea level standard atmosphere")]
        [SerializeField] private float airDensity = 1.225f;

        [Tooltip("Aero balance: fraction of total downforce acting on front axle (nominal ~43%)")]
        [Range(0.35f, 0.55f)]
        [SerializeField] private float frontAeroBalance = 0.43f;

        [Header("DRS (Drag Reduction System)")]
        [Tooltip("Fractional reduction in total drag when DRS flap is open (~22%)")]
        [Range(0.10f, 0.35f)]
        [SerializeField] private float drsDragReduction = 0.22f;

        [Tooltip("Fractional reduction in total downforce when DRS flap is open (~18%)")]
        [Range(0.10f, 0.30f)]
        [SerializeField] private float drsDownforceReduction = 0.18f;

        [SerializeField] private bool drsOpen = false;

        [Header("Dirty Air Model (Following Car Wake)")]
        [Tooltip("Time gap threshold in seconds below which car enters dirty air wake")]
        [SerializeField] private float dirtyAirThresholdSeconds = 2.0f;

        [Tooltip("Maximum downforce loss percentage when directly behind leader (<0.5s gap)")]
        [Range(0.15f, 0.45f)]
        [SerializeField] private float maxDirtyAirDownforceLoss = 0.32f;

        [Tooltip("Maximum slipstream drag reduction percentage when directly behind leader")]
        [Range(0.05f, 0.25f)]
        [SerializeField] private float maxSlipstreamDragReduction = 0.16f;

        [Header("Ride Height / Ground Effect Sensitivity")]
        [Tooltip("Optimal front ride height in metres for peak underfloor venturi suction")]
        [SerializeField] private float optimalFrontRideHeightM = 0.030f;

        [Tooltip("Optimal rear ride height in metres (rake angle effect)")]
        [SerializeField] private float optimalRearRideHeightM = 0.065f;

        private Rigidbody rb;
        private VehicleDynamics dynamics;

        // Current real-time aero metrics
        private float _currentDownforceN;
        private float _currentDragN;
        private float _currentAeroLossFactor;
        private float _currentSlipstreamFactor;

        public bool DrsOpen
        {
            get => drsOpen;
            set => drsOpen = value;
        }

        public float CurrentDownforceN => _currentDownforceN;
        public float CurrentDragN => _currentDragN;
        public float CurrentAeroLossFactor => _currentAeroLossFactor;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            dynamics = GetComponent<VehicleDynamics>();
        }

        /// <summary>
        /// Updates aerodynamic downforce and drag, processes dirty air wake, and updates CarState.DirtyAir.
        /// </summary>
        public void UpdateAero(ref CarState state, float vehicleSpeedMs, float deltaTime)
        {
            if (vehicleSpeedMs < 1.0f)
            {
                _currentDownforceN = 0f;
                _currentDragN = 0f;
                state.DirtyAir = false;
                return;
            }

            float dynamicPressure = 0.5f * airDensity * (vehicleSpeedMs * vehicleSpeedMs);

            // 1. Evaluate Dirty Air Wake from CarState GapAhead
            EvaluateDirtyAirWake(ref state, out _currentAeroLossFactor, out _currentSlipstreamFactor);

            // 2. Ground Effect Ride Height Scaling
            float groundEffectMultiplier = EvaluateGroundEffectScaling();

            // 3. Effective Coefficients with DRS and Dirty Air
            float effectiveClA = baseDownforceCoeff * groundEffectMultiplier * (1.0f - _currentAeroLossFactor);
            float effectiveCdA = baseDragCoeff * (1.0f - _currentSlipstreamFactor);

            if (state.DrsOpen)
            {
                effectiveClA *= (1.0f - drsDownforceReduction);
                effectiveCdA *= (1.0f - drsDragReduction);
            }

            // 4. Compute Total Forces
            _currentDownforceN = dynamicPressure * effectiveClA;
            _currentDragN = dynamicPressure * effectiveCdA;

            // 5. Apply Forces to Rigidbody
            // Check if car is grounded
            bool isGrounded = (dynamics != null) && (dynamics.FrontRideHeightM < 0.25f || dynamics.RearRideHeightM < 0.25f);

            if (isGrounded)
            {
                // Grounded: Apply authentic downforce split between front and rear axle locations
                float frontDownforce = _currentDownforceN * frontAeroBalance;
                float rearDownforce = _currentDownforceN * (1.0f - frontAeroBalance);

                Vector3 frontAxlePos = transform.position + (transform.forward * (dynamics.WheelbaseM * 0.5f));
                Vector3 rearAxlePos = transform.position - (transform.forward * (dynamics.WheelbaseM * 0.5f));

                rb.AddForceAtPosition(-transform.up * frontDownforce, frontAxlePos);
                rb.AddForceAtPosition(-transform.up * rearDownforce, rearAxlePos);
            }
            else
            {
                // Airborne: Ground effect stalls and downforce acts purely downward (prevents pitch/roll flips in the air!)
                rb.AddForce(Vector3.down * (_currentDownforceN * 0.4f));
            }

            // Apply Drag opposing velocity direction
            if (rb.linearVelocity.sqrMagnitude > 0.1f)
            {
                rb.AddForce(-rb.linearVelocity.normalized * _currentDragN);
            }
        }

        /// <summary>
        /// Evaluates dirty air downforce penalty and slipstream tow from gap ahead.
        /// Feeds CarState.DirtyAir directly for Section 5 overtake logic.
        /// </summary>
        private void EvaluateDirtyAirWake(ref CarState state, out float downforceLoss, out float dragReduction)
        {
            if (state.HasGapAhead && state.GapAheadS > 0.001f && state.GapAheadS < dirtyAirThresholdSeconds)
            {
                // Car is within wake threshold of leading car
                state.DirtyAir = true;

                // Normalized proximity factor: 1.0 at 0.0s gap, 0.0 at threshold
                float proximity = Mathf.Clamp01(1.0f - (state.GapAheadS / dirtyAirThresholdSeconds));
                // Non-linear falloff (turbulent wake decays with distance)
                float wakeIntensity = proximity * proximity;

                downforceLoss = maxDirtyAirDownforceLoss * wakeIntensity;
                dragReduction = maxSlipstreamDragReduction * wakeIntensity;
            }
            else
            {
                state.DirtyAir = false;
                downforceLoss = 0.0f;
                dragReduction = 0.0f;
            }
        }

        /// <summary>
        /// Modulates downforce efficiency based on underfloor ride height clearance.
        /// Peak efficiency occurs at optimal rake; stalls if too low (plank bottoming) or drops if too high.
        /// </summary>
        private float EvaluateGroundEffectScaling()
        {
            if (dynamics == null) return 1.0f;

            float frontH = dynamics.FrontRideHeightM;
            float rearH = dynamics.RearRideHeightM;

            // Bottoming out penalty: if front ride height drops below 18mm, underfloor flow stalls
            if (frontH < 0.018f)
            {
                float stallSeverity = Mathf.Clamp01((0.018f - frontH) / 0.008f);
                return Mathf.Lerp(1.0f, 0.70f, stallSeverity);
            }

            // Normal operating variance from optimal ride height
            float frontDelta = Mathf.Abs(frontH - optimalFrontRideHeightM);
            float rearDelta = Mathf.Abs(rearH - optimalRearRideHeightM);
            float rideHeightVariance = (frontDelta * 8.0f) + (rearDelta * 4.0f);

            return Mathf.Clamp(1.0f - rideHeightVariance, 0.75f, 1.05f);
        }
    }
}
