using System;
using UnityEngine;
using GridSense.Core;

namespace GridSense.Physics
{
    /// <summary>
    /// Section 3.5: Brakes (minimal thermal and torque model):
    /// 1. Stopping power modulated by CarState.BrakingAggressiveness (Normal vs Aggressive)
    /// 2. Front/rear brake bias distribution
    /// 3. Thermal model: heating from friction power dissipation, speed-dependent duct cooling
    /// 4. Brake fade curve: stopping power degrades outside optimal thermal window (cold glaze & overheat fade)
    /// </summary>
    public class BrakeSystem : MonoBehaviour
    {
        [Header("Brake Capabilities")]
        [Tooltip("Nominal maximum total braking torque (Nm) across all 4 wheels")]
        [SerializeField] private float maxBrakingTorqueTotal = 13000f;

        [Tooltip("Front brake bias ratio (0.58 = 58% front, 42% rear)")]
        [Range(0.50f, 0.70f)]
        [SerializeField] private float frontBrakeBias = 0.58f;

        [Tooltip("Aggressive braking torque multiplier")]
        [SerializeField] private float aggressiveTorqueMultiplier = 1.25f;

        [Header("Thermal Model (Carbon-Carbon Discs)")]
        [Tooltip("Lower bound of optimal temperature window (Celsius)")]
        [SerializeField] private float tempOptMin = 350f;

        [Tooltip("Upper bound of optimal temperature window (Celsius)")]
        [SerializeField] private float tempOptMax = 800f;

        [Tooltip("Severe fade onset temperature (Celsius)")]
        [SerializeField] private float tempFadeOnset = 900f;

        [Tooltip("Specific heat capacity proxy for carbon-carbon rotor assembly")]
        [SerializeField] private float rotorHeatCapacity = 850f;

        [Tooltip("Brake duct cooling efficiency coefficient (calibrated for carbon-carbon thermal dissipation)")]
        [SerializeField] private float ductCoolingCoefficient = 0.0018f;

        [Tooltip("Base stationary cooling rate to ambient air")]
        [SerializeField] private float ambientCoolingRate = 0.015f;

        [Header("WheelColliders (FL=0, FR=1, RL=2, RR=3)")]
        [SerializeField] private WheelCollider[] wheels = new WheelCollider[4];

        // Per-rotor temperatures (Celsius)
        private readonly float[] _rotorTemps = new float[4];

        // Current fade efficiency multiplier (0.0 to 1.0)
        private float _currentFadeEfficiency = 1.0f;

        // Current average brake rotor temperature
        public float AverageBrakeTempC => (_rotorTemps[0] + _rotorTemps[1] + _rotorTemps[2] + _rotorTemps[3]) * 0.25f;
        public float CurrentFadeEfficiency => _currentFadeEfficiency;

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

            // Initialize brakes to ambient / warm lap temperature
            for (int i = 0; i < 4; i++)
            {
                _rotorTemps[i] = 250f; // warm tyre blanket / outlap temp
            }
        }

        /// <summary>
        /// Computes stopping efficiency based on carbon-carbon rotor temperature window.
        /// </summary>
        public float EvaluateThermalFade(float tempC)
        {
            // Optimal operating window
            if (tempC >= tempOptMin && tempC <= tempOptMax)
                return 1.0f;

            // Cold glaze: below 350C, carbon discs lack optimum friction bite
            if (tempC < tempOptMin)
            {
                float factor = Mathf.Clamp01((tempC - 50f) / (tempOptMin - 50f));
                return Mathf.Lerp(0.80f, 1.0f, factor);
            }
            // Overheat fade: above 800C-900C, thermal degradation causes fade
            else
            {
                if (tempC < tempFadeOnset)
                {
                    float factor = (tempC - tempOptMax) / (tempFadeOnset - tempOptMax);
                    return Mathf.Lerp(1.0f, 0.85f, factor);
                }
                else
                {
                    // Extreme fade down to minimum 40% stopping power
                    float overTemp = tempC - tempFadeOnset;
                    return Mathf.Max(0.40f, 0.85f - (overTemp * 0.002f));
                }
            }
        }

        /// <summary>
        /// Applies braking torque to WheelColliders and integrates thermal state.
        /// </summary>
        /// <param name="brakeInput">Pedal demand in [0, 1]</param>
        /// <param name="aggressiveness">Braking mode from CarState</param>
        /// <param name="vehicleSpeedMs">Forward velocity in m/s</param>
        /// <param name="ambientTempC">Air temperature</param>
        /// <param name="deltaTime">Fixed delta time</param>
        public void ApplyBraking(
            float brakeInput, 
            BrakingAggressiveness aggressiveness, 
            float vehicleSpeedMs, 
            float ambientTempC, 
            float deltaTime)
        {
            brakeInput = Mathf.Clamp01(brakeInput);

            // Scale demand by aggressiveness mode
            float modeMultiplier = (aggressiveness == BrakingAggressiveness.Aggressive) ? aggressiveTorqueMultiplier : 1.0f;

            // Total available commanded torque
            float commandedTorque = brakeInput * maxBrakingTorqueTotal * modeMultiplier;

            // Bias split
            float frontTorquePerWheel = (commandedTorque * frontBrakeBias) * 0.5f;
            float rearTorquePerWheel = (commandedTorque * (1.0f - frontBrakeBias)) * 0.5f;

            float avgTemp = 0.0f;

            for (int i = 0; i < 4; i++)
            {
                float wheelTargetTorque = (i < 2) ? frontTorquePerWheel : rearTorquePerWheel;

                // Evaluate fade for this wheel's rotor
                float fadeMult = EvaluateThermalFade(_rotorTemps[i]);
                float actualTorque = wheelTargetTorque * fadeMult;

                // Apply to WheelCollider if attached
                if (wheels[i] != null)
                {
                    wheels[i].brakeTorque = actualTorque;
                }

                // ── Thermal Integration ──
                // Frictional power dissipated: P = Torque * angular_velocity
                float wheelAngVel = (wheels[i] != null) 
                    ? Mathf.Abs(wheels[i].rpm * (Mathf.PI / 30f)) 
                    : (vehicleSpeedMs / 0.36f); // 360mm F1 wheel radius proxy

                float brakingPowerWatts = actualTorque * wheelAngVel;

                // Heat generated in rotor
                float heatInput = (brakingPowerWatts / rotorHeatCapacity) * deltaTime;

                // Extra heat from aggressive mode brake friction
                if (aggressiveness == BrakingAggressiveness.Aggressive && brakeInput > 0.1f)
                {
                    heatInput *= 1.20f; // 20% higher thermal load under aggressive trail/late braking
                }

                // Duct cooling: increases with forward vehicle speed
                float ductAirflow = ductCoolingCoefficient * Mathf.Max(vehicleSpeedMs, 0.0f);
                float totalCoolingRate = ambientCoolingRate + ductAirflow;
                float coolingDelta = totalCoolingRate * (_rotorTemps[i] - ambientTempC) * deltaTime;

                _rotorTemps[i] = Mathf.Clamp(_rotorTemps[i] + heatInput - coolingDelta, ambientTempC, 1200f);
                avgTemp += _rotorTemps[i];
            }

            avgTemp *= 0.25f;
            _currentFadeEfficiency = EvaluateThermalFade(avgTemp);
        }

        public void ReleaseBrakes(float vehicleSpeedMs, float ambientTempC, float deltaTime)
        {
            ApplyBraking(0.0f, BrakingAggressiveness.Normal, vehicleSpeedMs, ambientTempC, deltaTime);
        }

        /// <summary>
        /// Writes brake telemetry into CarState.
        /// </summary>
        public void WriteToCarState(ref CarState state)
        {
            state.BrakeTempC = AverageBrakeTempC;
        }
    }
}
