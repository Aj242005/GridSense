using System;
using UnityEngine;
using GridSense.Core;

namespace GridSense.Physics
{
    /// <summary>
    /// Section 3.1: Complete Tyre Model implementing:
    /// 1. Combined-slip Pacejka "Magic Formula" (longitudinal & lateral forces)
    /// 2. Thermodynamic carcass/surface temperature response
    /// 3. Slip-energy driven wear model with strict separation between true internal wear and AI-visible wear
    /// 4. Soft / Medium / Hard compound switching
    /// 5. Secondary tyre pressure multiplier
    /// </summary>
    public class TyreModel : MonoBehaviour
    {
        [Header("Compound & Pressure")]
        [SerializeField] private TyreCompound activeCompound = TyreCompound.Medium;
        [Tooltip("Cold tyre inflation pressure in PSI (nominal F1 ~22.5 to 24.5 PSI)")]
        [SerializeField] private float tyrePressurePsi = 23.0f;
        [Tooltip("Nominal design tyre pressure in PSI where contact patch is optimal")]
        [SerializeField] private float nominalPressurePsi = 23.0f;

        [Header("Environmental Conditions")]
        [Tooltip("Track surface temperature in Celsius")]
        [SerializeField] private float trackTempC = 35.0f;
        [Tooltip("Ambient air temperature in Celsius")]
        [SerializeField] private float ambientTempC = 28.0f;

        [Header("WheelColliders (FL=0, FR=1, RL=2, RR=3)")]
        [SerializeField] private WheelCollider[] wheels = new WheelCollider[4];

        // Active compound parameters
        private TyreCompoundParameters compoundParams;

        // Per-wheel thermodynamic temperatures (Celsius)
        private readonly float[] _tyreTemps = new float[4];

        // ═════════════════════════════════════════════════════════════════════════
        // GROUND TRUTH BOUNDARY:
        // These private fields represent the true physical cumulative wear (0-100%).
        // They are driven by actual integrated slip energy dissipated at each contact patch.
        // In accordance with the spec, the AI inference model (Sections 4 & 5) has ZERO
        // direct access to this array. Validation scripts can read it via GetTrueWearPct().
        // ═════════════════════════════════════════════════════════════════════════
        private readonly float[] _trueCumulativeWearPct = new float[4];

        // Instantaneous wear rates (% wear / sec) per wheel
        private readonly float[] _instantaneousWearRate = new float[4];

        // Wheel velocities and slip states cached from WheelCollider.GetGroundHit
        private readonly WheelHit[] _wheelHits = new WheelHit[4];
        private readonly bool[] _isGrounded = new bool[4];

        public TyreCompound ActiveCompound => activeCompound;
        public float TrackTempC => trackTempC;
        public float AmbientTempC => ambientTempC;

        private void Awake()
        {
            if (wheels == null || wheels.Length < 4 || wheels[0] == null)
            {
                wheels = new WheelCollider[4];
                wheels[0] = transform.Find("Wheel_FL")?.GetComponent<WheelCollider>();
                wheels[1] = transform.Find("Wheel_FR")?.GetComponent<WheelCollider>();
                wheels[2] = transform.Find("Wheel_RL")?.GetComponent<WheelCollider>();
                wheels[3] = transform.Find("Wheel_RR")?.GetComponent<WheelCollider>();
            }

            SetCompound(activeCompound);
            InitializeTemperatures();
        }

        /// <summary>
        /// Switch tyre compound (e.g. during pit stop).
        /// </summary>
        public void SetCompound(TyreCompound compound)
        {
            activeCompound = compound;
            compoundParams = TyreCompoundDatabase.GetPreset(compound);
        }

        public void SetTyrePressure(float psi)
        {
            tyrePressurePsi = Mathf.Clamp(psi, 18.0f, 32.0f);
        }

        public void SetTrackTemp(float tempC)
        {
            trackTempC = Mathf.Clamp(tempC, 5.0f, 70.0f);
        }

        private void InitializeTemperatures()
        {
            // Initial tyre temp warmers start near optimal lower window
            float initTemp = compoundParams != null ? compoundParams.tempOptMin : 90.0f;
            for (int i = 0; i < 4; i++)
            {
                _tyreTemps[i] = initTemp;
                _trueCumulativeWearPct[i] = 0.0f;
                _instantaneousWearRate[i] = 0.0f;
            }
        }

        /// <summary>
        /// Evaluates Pacejka Magic Formula for pure slip:
        /// y(x) = D * sin(C * atan(B*x - E*(B*x - atan(B*x))))
        /// </summary>
        public static float EvaluatePacejka(float slip, float B, float C, float D, float E)
        {
            float Bx = B * slip;
            return D * Mathf.Sin(C * Mathf.Atan(Bx - E * (Bx - Mathf.Atan(Bx))));
        }

        /// <summary>
        /// Calculates combined slip longitudinal and lateral forces using friction ellipse weighting.
        /// </summary>
        public void ComputeCombinedForces(
            float forwardSlip, 
            float lateralSlip, 
            float normalLoadN, 
            float effectiveMu,
            out float forceLongitudinal, 
            out float forceLateral)
        {
            if (normalLoadN <= 0.01f)
            {
                forceLongitudinal = 0.0f;
                forceLateral = 0.0f;
                return;
            }

            // Slip thresholds:
            // 1. peakLongSlip = 0.12 (12%): The peak adhesion limit where tyre generates maximum frictional grip.
            // 2. lockupThreshold = 0.15 (15%): The incipient lockup threshold where the tyre enters the negative-slope
            //    sliding regime, producing severe scrubbing, thermal spikes, and flat-spot wear.
            float peakLongSlip = 0.12f; // Peak adhesion limit
            float peakLatSlip = 0.10f;  // ~5.7 deg peak slip angle in radians

            float normSx = forwardSlip / peakLongSlip;
            float normSy = lateralSlip / peakLatSlip;
            float totalSlip = Mathf.Sqrt(normSx * normSx + normSy * normSy);

            if (totalSlip < 0.0001f)
            {
                forceLongitudinal = 0.0f;
                forceLateral = 0.0f;
                return;
            }

            // Peak available frictional force
            float peakForceN = normalLoadN * effectiveMu;

            // Pure slip evaluations
            float pureLong = EvaluatePacejka(totalSlip * peakLongSlip, compoundParams.B_long, compoundParams.C_long, compoundParams.D_long, compoundParams.E_long);
            float pureLat = EvaluatePacejka(totalSlip * peakLatSlip, compoundParams.B_lat, compoundParams.C_lat, compoundParams.D_lat, compoundParams.E_lat);

            // Friction ellipse combination
            forceLongitudinal = (normSx / totalSlip) * pureLong * peakForceN;
            forceLateral = (normSy / totalSlip) * pureLat * peakForceN;
        }

        /// <summary>
        /// Computes grip multiplier based on temperature window distance.
        /// Optimal window yields 1.0; falls off parabolic outside window.
        /// </summary>
        public float EvaluateThermalGripMultiplier(float tempC)
        {
            if (tempC >= compoundParams.tempOptMin && tempC <= compoundParams.tempOptMax)
                return 1.0f;

            if (tempC < compoundParams.tempOptMin)
            {
                // Cold tyre: grip drops down to ~65%
                float delta = compoundParams.tempOptMin - tempC;
                float drop = (delta / (compoundParams.tempOptMin - compoundParams.tempMin + 0.001f));
                return Mathf.Clamp(1.0f - 0.35f * drop * drop, 0.60f, 1.0f);
            }
            else
            {
                // Overheated tyre: severe degradation down to ~50%
                float delta = tempC - compoundParams.tempOptMax;
                float drop = (delta / (compoundParams.tempMax - compoundParams.tempOptMax + 0.001f));
                return Mathf.Clamp(1.0f - 0.50f * drop * drop, 0.50f, 1.0f);
            }
        }

        /// <summary>
        /// Computes grip degradation multiplier as a function of cumulative wear (0-100%).
        /// Non-linear: gradual drop followed by sharp performance cliff near end-of-life.
        /// </summary>
        public float EvaluateWearGripMultiplier(float wearPct)
        {
            float w = Mathf.Clamp01(wearPct / 100.0f);
            // Linear wear degradation plus sharp quartic drop-off representing tread strip / cliff
            return Mathf.Clamp(1.0f - 0.20f * w - 0.60f * Mathf.Pow(w, 4.0f), 0.20f, 1.0f);
        }

        /// <summary>
        /// Secondary multiplier for tyre pressure variance from nominal PSI.
        /// </summary>
        public float EvaluatePressureMultiplier()
        {
            float delta = (tyrePressurePsi - nominalPressurePsi) / nominalPressurePsi;
            // Over- or under-inflation slightly reduces contact patch area/evenness
            return Mathf.Clamp(1.0f - 0.05f * delta * delta, 0.90f, 1.0f);
        }

        /// <summary>
        /// Executes thermodynamic and wear updates for all 4 wheels during FixedUpdate.
        /// </summary>
        public void UpdatePhysicsStep(float deltaTime, float vehicleSpeedMs, float trackEvolutionFactor = 1.0f)
        {
            if (compoundParams == null)
                compoundParams = TyreCompoundDatabase.GetPreset(activeCompound);

            float pressureMult = EvaluatePressureMultiplier();

            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null) continue;

                _isGrounded[i] = wheels[i].GetGroundHit(out _wheelHits[i]);
                if (!_isGrounded[i]) continue;

                float forwardSlip = _wheelHits[i].forwardSlip;
                float sidewaysSlip = _wheelHits[i].sidewaysSlip;
                float normalLoadN = _wheelHits[i].force;

                // 1. Current temperature, wear, pressure, and track evolution modifiers
                float tempMult = EvaluateThermalGripMultiplier(_tyreTemps[i]);
                float wearMult = EvaluateWearGripMultiplier(_trueCumulativeWearPct[i]);

                // TrackEvolutionFactor acts as an environmental confound directly scaling available friction
                float effectiveMu = compoundParams.baseGrip * tempMult * wearMult * pressureMult * trackEvolutionFactor;

                // 2. Combined slip forces
                ComputeCombinedForces(forwardSlip, sidewaysSlip, normalLoadN, effectiveMu, out float fx, out float fy);

                // 3. Slip energy dissipation rate: P_slip = |Fx * v_slip_x| + |Fy * v_slip_y| (Watts)
                float slipVelX = Mathf.Abs(forwardSlip * vehicleSpeedMs);
                float slipVelY = Mathf.Abs(sidewaysSlip * vehicleSpeedMs);
                float slipPowerWatts = (Mathf.Abs(fx) * slipVelX + Mathf.Abs(fy) * slipVelY);

                // 4. Thermodynamic update
                // Heat generation from slip energy + conduction from hot track asphalt
                float trackConduction = 0.02f * (trackTempC - _tyreTemps[i]);
                float heatGen = (slipPowerWatts * compoundParams.heatGenCoefficient) + trackConduction;

                // Convective cooling from forward air speed + ambient radiation
                float coolingCoeff = compoundParams.coolingRateBase + (compoundParams.coolingRateSpeedFactor * vehicleSpeedMs);
                float cooling = coolingCoeff * (_tyreTemps[i] - ambientTempC);

                float deltaTemp = (heatGen - cooling) * deltaTime; // rate per step
                _tyreTemps[i] = Mathf.Clamp(_tyreTemps[i] + deltaTemp, ambientTempC, compoundParams.tempMax + 20.0f);

                // 5. Cumulative Wear Update (Physical Ground Truth)
                // Wear rate proportional to slip energy, compound hardness, and thermal penalty if overheated
                float thermalWearPenalty = _tyreTemps[i] > compoundParams.tempOptMax ? 1.0f + 0.03f * (_tyreTemps[i] - compoundParams.tempOptMax) : 1.0f;
                float wearIncrement = (slipPowerWatts * 1e-6f) * compoundParams.wearRateMultiplier * thermalWearPenalty * deltaTime * 1.5f;

                _trueCumulativeWearPct[i] = Mathf.Clamp(_trueCumulativeWearPct[i] + wearIncrement, 0.0f, 100.0f);
                _instantaneousWearRate[i] = wearIncrement / Mathf.Max(deltaTime, 0.001f);

                // 6. Dynamically update WheelCollider friction curves to match effectiveMu
                UpdateWheelColliderFriction(wheels[i], effectiveMu, vehicleSpeedMs);
            }
        }

        private void UpdateWheelColliderFriction(WheelCollider wc, float effectiveMu, float speedMs)
        {
            float normGrip = Mathf.Clamp(effectiveMu / 1.65f, 0.8f, 1.25f);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(speedMs) / 3.5f);

            // Forward friction curve (smooth at stationary, high bite at speed)
            WheelFrictionCurve forwardCurve = wc.forwardFriction;
            forwardCurve.extremumSlip = 0.15f;
            forwardCurve.extremumValue = 2.2f * normGrip;
            forwardCurve.asymptoteSlip = 0.50f;
            forwardCurve.asymptoteValue = 1.6f * normGrip;
            forwardCurve.stiffness = Mathf.Lerp(1.2f, 2.2f * normGrip, speedFactor);
            wc.forwardFriction = forwardCurve;

            // Sideways cornering curve (smooth at stationary, crisp racing grip at speed)
            WheelFrictionCurve sidewaysCurve = wc.sidewaysFriction;
            sidewaysCurve.extremumSlip = 0.12f;
            sidewaysCurve.extremumValue = 2.6f * normGrip;
            sidewaysCurve.asymptoteSlip = 0.40f;
            sidewaysCurve.asymptoteValue = 1.9f * normGrip;
            sidewaysCurve.stiffness = Mathf.Lerp(1.2f, 2.5f * normGrip, speedFactor);
            wc.sidewaysFriction = sidewaysCurve;
        }

        /// <summary>
        /// Quasi-steady tyre physical step for simulated driving cycles (used when WheelColliders are unattached or in trajectory benchmarks).
        /// Computes Pacejka slip, slip power dissipation, thermodynamics, and cumulative wear.
        /// </summary>
        public void UpdateQuasiSteady(float totalDriveTorqueNm, float totalBrakingTorqueNm, float vehicleSpeedMs, float deltaTime, float trackEvolutionFactor = 1.0f)
        {
            if (compoundParams == null)
                compoundParams = TyreCompoundDatabase.GetPreset(activeCompound);

            float pressureMult = EvaluatePressureMultiplier();
            float wheelRadius = 0.36f;
            float aeroDownforceN = 0.5f * 1.225f * 3.8f * (vehicleSpeedMs * vehicleSpeedMs);
            float normalLoadN = ((798f + 100f) * 9.81f + aeroDownforceN) * 0.25f;

            for (int i = 0; i < 4; i++)
            {
                // Drive torque to rears (i=2,3); Brake torque split 58/42
                float driveT = (i >= 2) ? (totalDriveTorqueNm * 0.5f) : 0f;
                float brakeT = (i < 2) ? (totalBrakingTorqueNm * 0.58f * 0.5f) : (totalBrakingTorqueNm * 0.42f * 0.5f);
                float netTorque = driveT - brakeT;

                float demandedFx = netTorque / wheelRadius;

                float tempMult = EvaluateThermalGripMultiplier(_tyreTemps[i]);
                float wearMult = EvaluateWearGripMultiplier(_trueCumulativeWearPct[i]);
                float effectiveMu = compoundParams.baseGrip * tempMult * wearMult * pressureMult * trackEvolutionFactor;

                float maxGripForceN = normalLoadN * effectiveMu;
                float gripRatio = Mathf.Clamp(demandedFx / Mathf.Max(maxGripForceN, 1.0f), -1.2f, 1.2f);

                // Invert linear Pacejka to find slip ratio
                float forwardSlip = gripRatio * 0.09f;
                float sidewaysSlip = 0.0f;

                ComputeCombinedForces(forwardSlip, sidewaysSlip, normalLoadN, effectiveMu, out float fx, out float fy);

                float slipVelX = Mathf.Abs(forwardSlip * vehicleSpeedMs);
                float slipPowerWatts = Mathf.Abs(fx) * slipVelX;

                // Thermal
                float trackConduction = 0.02f * (trackTempC - _tyreTemps[i]);
                float heatGen = (slipPowerWatts * compoundParams.heatGenCoefficient) + trackConduction;
                float coolingCoeff = compoundParams.coolingRateBase + (compoundParams.coolingRateSpeedFactor * vehicleSpeedMs);
                float cooling = coolingCoeff * (_tyreTemps[i] - ambientTempC);
                float deltaTemp = (heatGen - cooling) * deltaTime;
                _tyreTemps[i] = Mathf.Clamp(_tyreTemps[i] + deltaTemp, ambientTempC, compoundParams.tempMax + 20.0f);

                // Wear (Physical Ground Truth)
                float thermalWearPenalty = _tyreTemps[i] > compoundParams.tempOptMax ? 1.0f + 0.03f * (_tyreTemps[i] - compoundParams.tempOptMax) : 1.0f;
                float wearIncrement = (slipPowerWatts * 1e-6f) * compoundParams.wearRateMultiplier * thermalWearPenalty * deltaTime * 1.5f;

                _trueCumulativeWearPct[i] = Mathf.Clamp(_trueCumulativeWearPct[i] + wearIncrement, 0.0f, 100.0f);
                _instantaneousWearRate[i] = wearIncrement / Mathf.Max(deltaTime, 0.001f);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // EXPORT TO CARSTATE & VALIDATION ACCESSORS
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes aggregated physical outputs into CarState.
        /// </summary>
        public void WriteToCarState(ref CarState state)
        {
            state.Compound = activeCompound;
            state.TyreTempC = GetAverageTyreTempC();
            state.TyreWearRateCurrent = GetAverageInstantaneousWearRate();
            // Note: state.TyreWearPct is deliberately NOT populated with true wear here;
            // Section 4's degradation model estimates TyreWearPct for the AI observer.
        }

        /// <summary>
        /// Average tyre temperature across all 4 wheels.
        /// </summary>
        public float GetAverageTyreTempC()
        {
            return (_tyreTemps[0] + _tyreTemps[1] + _tyreTemps[2] + _tyreTemps[3]) * 0.25f;
        }

        /// <summary>
        /// Average instantaneous wear rate (% / sec).
        /// </summary>
        public float GetAverageInstantaneousWearRate()
        {
            return (_instantaneousWearRate[0] + _instantaneousWearRate[1] + _instantaneousWearRate[2] + _instantaneousWearRate[3]) * 0.25f;
        }

        public float GetTyreTempC(int wheelIndex) => _tyreTemps[wheelIndex];

        // ═════════════════════════════════════════════════════════════════════════
        // GROUND TRUTH VALIDATION ONLY (Section 4 Dataset / Test Fixtures)
        // AI models must NOT access these methods at runtime.
        // ═════════════════════════════════════════════════════════════════════════
        public float GetTrueWearPct(int wheelIndex) => _trueCumulativeWearPct[wheelIndex];
        public float GetAverageTrueWearPct() => (_trueCumulativeWearPct[0] + _trueCumulativeWearPct[1] + _trueCumulativeWearPct[2] + _trueCumulativeWearPct[3]) * 0.25f;

        /// <summary>
        /// Used by pit stop logic or testing to install a fresh set of tyres.
        /// </summary>
        public void ResetTyres(TyreCompound compound)
        {
            SetCompound(compound);
            InitializeTemperatures();
        }
    }
}
