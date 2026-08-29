using System;
using UnityEngine;
using GridSense.Core;

namespace GridSense.Physics
{
    /// <summary>
    /// Section 3.4: Powertrain & Energy System:
    /// 1. Internal Combustion Engine (ICE): Torque generation and real-time fuel mass consumption (depleting CarState.FuelLoadKg).
    /// 2. Hybrid ERS (MGU-K & Battery): Electric boost deployment across Push, Balanced, Hold, and Save modes (depleting CarState.EnergyRemainingPct).
    /// 3. Regenerative Braking (MGU-K): Kinetic energy harvesting on rear axle recharging CarState.EnergyRemainingPct.
    /// 4. Concrete BrakingAggressiveness coupling: Aggressive braking harvests +35% more electrical energy,
    ///    at the cost of heightened tyre lockup/slip wear and thermal rotor stress.
    /// </summary>
    public class PowertrainSystem : MonoBehaviour
    {
        [Header("ICE (Internal Combustion Engine)")]
        [Tooltip("Maximum ICE power output in Kilowatts (~580 kW / 780 hp)")]
        [SerializeField] private float maxIcePowerKw = 580f;

        [Tooltip("Nominal maximum wheel drive torque from ICE (Nm)")]
        [SerializeField] private float maxIceDriveTorqueNm = 4200f;

        [Tooltip("Peak fuel flow rate in kg/hour at wide-open throttle (FIA regulation ~100 kg/h)")]
        [SerializeField] private float maxFuelFlowKgPerHour = 100f;

        [Header("Hybrid ERS (MGU-K Electric Motor)")]
        [Tooltip("Maximum MGU-K electric motor power in Kilowatts (FIA regulation: 120 kW / 160 hp)")]
        [SerializeField] private float maxMgukPowerKw = 120f;

        [Tooltip("Usable battery energy capacity in Megajoules (FIA standard ~4.0 MJ usable per lap)")]
        [SerializeField] private float batteryCapacityMj = 4.0f;

        [Header("Deployment Power Profiles (kW by Mode)")]
        [SerializeField] private float deployPowerPushKw = 120f;     // Full 120 kW boost
        [SerializeField] private float deployPowerBalancedKw = 65f;  // Balanced race pace
        [SerializeField] private float deployPowerHoldKw = 35f;      // Energy conservation
        [SerializeField] private float deployPowerSaveKw = 0f;       // Zero boost, pure battery preservation

        [Header("Regenerative Braking (MGU-K Harvesting)")]
        [Tooltip("Nominal baseline MGU-K regen power in kW under normal braking (~90 kW)")]
        [SerializeField] private float baseRegenPowerKw = 90f;

        [Tooltip("Harvesting power multiplier under Aggressive braking (+35% more recovery)")]
        [SerializeField] private float aggressiveRegenMultiplier = 1.35f;

        // Current real-time telemetry metrics
        private float _currentIceTorqueNm;
        private float _currentMgukTorqueNm;
        private float _currentFuelBurnRateKgPerSec;
        private float _currentElectricalPowerKw; // positive = deploying, negative = harvesting

        public float CurrentIceTorqueNm => _currentIceTorqueNm;
        public float CurrentMgukTorqueNm => _currentMgukTorqueNm;
        public float TotalDriveTorqueNm => _currentIceTorqueNm + _currentMgukTorqueNm;
        public float CurrentElectricalPowerKw => _currentElectricalPowerKw;

        /// <summary>
        /// Updates ICE and MGU-K powertrains, consumes fuel, deploys or harvests electrical energy,
        /// and updates CarState.FuelLoadKg and CarState.EnergyRemainingPct.
        /// </summary>
        public void UpdatePowertrain(
            ref CarState state, 
            float throttleInput, 
            float brakeInput, 
            float vehicleSpeedMs, 
            float deltaTime)
        {
            throttleInput = Mathf.Clamp01(throttleInput);
            brakeInput = Mathf.Clamp01(brakeInput);

            // ─────────────────────────────────────────────────────────────────
            // 1. ICE POWER & FUEL MASS CONSUMPTION
            // ─────────────────────────────────────────────────────────────────
            if (state.FuelLoadKg > 0.001f)
            {
                _currentIceTorqueNm = throttleInput * maxIceDriveTorqueNm;

                // Fuel burn rate in kg/s: flow is proportional to throttle demand
                _currentFuelBurnRateKgPerSec = (maxFuelFlowKgPerHour / 3600f) * throttleInput;
                float fuelConsumed = _currentFuelBurnRateKgPerSec * deltaTime;

                state.FuelLoadKg = Mathf.Max(0.0f, state.FuelLoadKg - fuelConsumed);
            }
            else
            {
                // Engine out of fuel
                _currentIceTorqueNm = 0f;
                _currentFuelBurnRateKgPerSec = 0f;
                state.FuelLoadKg = 0f;
            }

            // ─────────────────────────────────────────────────────────────────
            // 2. HYBRID ERS DEPLOYMENT (Under Acceleration)
            // ─────────────────────────────────────────────────────────────────
            if (throttleInput > 0.05f && brakeInput < 0.05f && state.EnergyRemainingPct > 0.01f)
            {
                float targetDeployPowerKw = GetDeploymentPowerForMode(state.DeploymentMode);

                // Calculate MGU-K boost torque: Torque = Power / omega
                float wheelRadius = 0.36f; // F1 18-inch wheel + tyre radius ~360mm
                float wheelRpm = (vehicleSpeedMs / (2.0f * Mathf.PI * wheelRadius)) * 60f;
                float wheelOmega = Mathf.Max(wheelRpm * (Mathf.PI / 30f), 10.0f); // avoid div by zero

                float maxBoostTorque = (targetDeployPowerKw * 1000f) / wheelOmega;
                _currentMgukTorqueNm = throttleInput * Mathf.Min(maxBoostTorque, 1200f);

                // Battery depletion in Megajoules: E = P (MW) * dt (sec)
                float energyDepletedMj = (targetDeployPowerKw * 0.001f * throttleInput) * deltaTime;
                float pctDepleted = (energyDepletedMj / batteryCapacityMj) * 100.0f;

                state.EnergyRemainingPct = Mathf.Clamp(state.EnergyRemainingPct - pctDepleted, 0.0f, 100.0f);
                _currentElectricalPowerKw = targetDeployPowerKw * throttleInput;
            }
            else
            {
                _currentMgukTorqueNm = 0f;
                _currentElectricalPowerKw = 0f;
            }

            // ─────────────────────────────────────────────────────────────────
            // 3. REGENERATIVE BRAKING & BRAKING-AGGRESSIVENESS COUPLING
            // ─────────────────────────────────────────────────────────────────
            if (brakeInput > 0.05f && vehicleSpeedMs > 2.0f)
            {
                // Harder/later braking (Aggressive) yields +35% higher peak regen harvesting
                float regenMultiplier = (state.Braking == BrakingAggressiveness.Aggressive) 
                    ? aggressiveRegenMultiplier 
                    : 1.0f;

                float currentHarvestPowerKw = baseRegenPowerKw * brakeInput * regenMultiplier;

                // Battery recharge in Megajoules
                float energyHarvestedMj = (currentHarvestPowerKw * 0.001f) * deltaTime;
                float pctHarvested = (energyHarvestedMj / batteryCapacityMj) * 100.0f;

                state.EnergyRemainingPct = Mathf.Clamp(state.EnergyRemainingPct + pctHarvested, 0.0f, 100.0f);
                _currentElectricalPowerKw = -currentHarvestPowerKw; // negative denotes harvesting
            }
        }

        private float GetDeploymentPowerForMode(EnergyMode mode)
        {
            switch (mode)
            {
                case EnergyMode.Push:
                    return deployPowerPushKw;
                case EnergyMode.Hold:
                    return deployPowerHoldKw;
                case EnergyMode.Save:
                    return deployPowerSaveKw;
                case EnergyMode.Balanced:
                default:
                    return deployPowerBalancedKw;
            }
        }
    }
}
