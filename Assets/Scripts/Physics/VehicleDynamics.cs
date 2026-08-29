using System;
using UnityEngine;
using GridSense.Core;

namespace GridSense.Physics
{
    /// <summary>
    /// Section 3.2: Vehicle Dynamics:
    /// 1. Rigidbody mass management (dry mass + dynamic FuelLoadKg from CarState)
    /// 2. Center of gravity (CG) height, wheelbase, and track width load-transfer configuration
    /// 3. Per-corner spring/damper suspension tuning
    /// 4. Tunable Anti-Roll Bar (ARB) stiffness (front & rear)
    /// 5. Real-time ride height tracking (input for Section 3.3 Aerodynamics)
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleDynamics : MonoBehaviour
    {
        [Header("Chassis & Mass")]
        [Tooltip("FIA minimum dry car mass in kg (without fuel)")]
        [SerializeField] private float baseDryMassKg = 798f;

        [Tooltip("Center of mass local offset. F1 cars have a very low CG (~0.30m) with ~45/55 front/rear bias")]
        [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.22f, -0.15f);

        [Header("Geometry")]
        [Tooltip("Wheelbase in metres (nominal ~3.60m)")]
        [SerializeField] private float wheelbaseM = 3.60f;

        [Tooltip("Front track width in metres (nominal ~1.60m)")]
        [SerializeField] private float frontTrackWidthM = 1.60f;

        [Tooltip("Rear track width in metres (nominal ~1.55m)")]
        [SerializeField] private float rearTrackWidthM = 1.55f;

        [Tooltip("Static ground clearance / nominal ride height in metres (~0.035m front, ~0.075m rear)")]
        [SerializeField] private float staticRideHeightM = 0.045f;

        [Header("Suspension Parameters (WheelColliders)")]
        [Tooltip("Suspension spring rate in N/m (tuned for WheelCollider stability with 80mm travel)")]
        [SerializeField] private float springRateNm = 35000f;

        [Tooltip("Suspension damper rate in N*s/m (tuned to prevent oscillation)")]
        [SerializeField] private float damperRateNsm = 4500f;

        [Tooltip("Total suspension travel in metres (~0.08m = 80mm for stable ground contact)")]
        [SerializeField] private float suspensionDistanceM = 0.080f;

        [Header("Anti-Roll Bars (ARB)")]
        [Tooltip("Front Anti-Roll Bar stiffness (N)")]
        [SerializeField] private float frontArbStiffness = 6000f;

        [Tooltip("Rear Anti-Roll Bar stiffness (N)")]
        [SerializeField] private float rearArbStiffness = 4000f;

        [Header("Wheel Colliders")]
        [SerializeField] private WheelCollider wheelFL;
        [SerializeField] private WheelCollider wheelFR;
        [SerializeField] private WheelCollider wheelRL;
        [SerializeField] private WheelCollider wheelRR;

        private Rigidbody rb;

        // Current real-time ride heights (metres) — feed into Section 3.3 Aero
        private float _frontRideHeightM;
        private float _rearRideHeightM;

        public float FrontRideHeightM => _frontRideHeightM;
        public float RearRideHeightM => _rearRideHeightM;
        public float AverageRideHeightM => (_frontRideHeightM + _rearRideHeightM) * 0.5f;

        public float WheelbaseM => wheelbaseM;
        public float FrontTrackWidthM => frontTrackWidthM;
        public float RearTrackWidthM => rearTrackWidthM;
        public Rigidbody RigidbodyComponent => rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

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

            ApplyChassisConfiguration();
        }

        /// <summary>
        /// Applies Rigidbody mass, inertia, and suspension setup to WheelColliders.
        /// </summary>
        public void ApplyChassisConfiguration()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();

            rb.centerOfMass = centerOfMassOffset;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.angularDamping = 0.5f;
            rb.linearDamping = 0.01f;

            // Thin bodywork-only BoxCollider positioned just above wheel height
            // to avoid snagging on track geometry or kerbs
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                box.center = new Vector3(0f, 0.55f, 0f);
                box.size = new Vector3(0.8f, 0.2f, 2.0f);
            }

            // NOTE: Do NOT mutate global Physics settings from a per-car component.
            // Project-level solver iterations and contact offset belong in
            // ProjectSettings/DynamicsManager, not in per-object code.

            ConfigureWheelSuspension(wheelFL);
            ConfigureWheelSuspension(wheelFR);
            ConfigureWheelSuspension(wheelRL);
            ConfigureWheelSuspension(wheelRR);
        }

        private void ConfigureWheelSuspension(WheelCollider wc)
        {
            if (wc == null) return;

            wc.suspensionDistance = suspensionDistanceM;
            wc.forceAppPointDistance = 0.0f; // At wheel centre — most stable config

            JointSpring spring = wc.suspensionSpring;
            spring.spring = springRateNm;
            spring.damper = damperRateNsm;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;

            // Sane friction curves: extremumValue ~1.2-1.5 is the realistic WheelCollider range.
            // Higher values cause violent grip spikes and jitter.
            WheelFrictionCurve fFriction = wc.forwardFriction;
            fFriction.extremumSlip = 0.20f;
            fFriction.extremumValue = 1.4f;
            fFriction.asymptoteSlip = 0.80f;
            fFriction.asymptoteValue = 1.0f;
            fFriction.stiffness = 1.0f;
            wc.forwardFriction = fFriction;

            WheelFrictionCurve sFriction = wc.sidewaysFriction;
            sFriction.extremumSlip = 0.15f;
            sFriction.extremumValue = 1.5f;
            sFriction.asymptoteSlip = 0.60f;
            sFriction.asymptoteValue = 1.1f;
            sFriction.stiffness = 1.0f;
            wc.sidewaysFriction = sFriction;
        }

        /// <summary>
        /// Updates dynamic mass from CarState fuel load and applies Anti-Roll Bar (ARB) forces.
        /// </summary>
        public void UpdateVehicleDynamics(ref CarState state, float deltaTime)
        {
            // 1. Sync dynamic total mass with fuel load from CarState
            float currentFuel = Mathf.Clamp(state.FuelLoadKg, 0.0f, 115.0f);
            rb.mass = baseDryMassKg + currentFuel;

            // 2. Anti-Roll Bar Force application (gated to moving speeds to avoid stationary rocking)
            if (rb.linearVelocity.sqrMagnitude > 0.5f)
            {
                ApplyArbPair(wheelFL, wheelFR, frontArbStiffness);
                ApplyArbPair(wheelRL, wheelRR, rearArbStiffness);
            }

            // 3. Compute current dynamic ride height from suspension compression
            ComputeRideHeights();

            // 4. Airborne Auto-Leveling & Anti-Flip Stabilization
            bool anyGrounded = (wheelFL != null && wheelFL.isGrounded) || 
                               (wheelFR != null && wheelFR.isGrounded) || 
                               (wheelRL != null && wheelRL.isGrounded) || 
                               (wheelRR != null && wheelRR.isGrounded);

            if (!anyGrounded)
            {
                // When in the air: Gently level the car horizontally to ensure clean 4-wheel landings
                Vector3 projectedUp = Vector3.ProjectOnPlane(Vector3.up, transform.forward).normalized;
                Vector3 rollCorrection = Vector3.Cross(transform.up, projectedUp);
                rb.AddTorque((rollCorrection * 8000f) - (rb.angularVelocity * 3000f));
            }
        }

        /// <summary>
        /// Applies opposing vertical suspension forces between left and right wheels on an axle
        /// to counteract chassis lateral roll under cornering load transfer.
        /// </summary>
        private void ApplyArbPair(WheelCollider leftWheel, WheelCollider rightWheel, float arbStiffness)
        {
            if (leftWheel == null || rightWheel == null) return;

            float travelL = 1.0f;
            float travelR = 1.0f;

            bool groundedL = leftWheel.GetGroundHit(out WheelHit hitL);
            if (groundedL)
            {
                travelL = (-leftWheel.transform.InverseTransformPoint(hitL.point).y - leftWheel.radius) / leftWheel.suspensionDistance;
            }

            bool groundedR = rightWheel.GetGroundHit(out WheelHit hitR);
            if (groundedR)
            {
                travelR = (-rightWheel.transform.InverseTransformPoint(hitR.point).y - rightWheel.radius) / rightWheel.suspensionDistance;
            }

            // Clamped ARB force to prevent abrupt physics impulse spikes
            float arbForce = Mathf.Clamp((travelL - travelR) * arbStiffness, -2000f, 2000f);

            if (groundedL)
                rb.AddForceAtPosition(leftWheel.transform.up * -arbForce, leftWheel.transform.position);

            if (groundedR)
                rb.AddForceAtPosition(rightWheel.transform.up * arbForce, rightWheel.transform.position);
        }

        /// <summary>
        /// Computes current front and rear ride heights from ground hit clearance.
        /// Feeds directly into Section 3.3 Aerodynamic ground effect downforce.
        /// </summary>
        private void ComputeRideHeights()
        {
            float travelFL = GetWheelTravel(wheelFL);
            float travelFR = GetWheelTravel(wheelFR);
            float travelRL = GetWheelTravel(wheelRL);
            float travelRR = GetWheelTravel(wheelRR);

            float avgTravelFront = (travelFL + travelFR) * 0.5f;
            float avgTravelRear = (travelRL + travelRR) * 0.5f;

            // Compression reduces ride height from static setting
            _frontRideHeightM = Mathf.Max(0.015f, staticRideHeightM - (avgTravelFront * suspensionDistanceM));
            _rearRideHeightM = Mathf.Max(0.025f, (staticRideHeightM + 0.030f) - (avgTravelRear * suspensionDistanceM));
        }

        private float GetWheelTravel(WheelCollider wc)
        {
            if (wc == null) return 0f;
            if (wc.GetGroundHit(out WheelHit hit))
            {
                return Mathf.Clamp01((-wc.transform.InverseTransformPoint(hit.point).y - wc.radius) / wc.suspensionDistance);
            }
            return 0f;
        }
    }
}
