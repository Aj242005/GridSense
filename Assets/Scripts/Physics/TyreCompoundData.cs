using System;
using UnityEngine;
using GridSense.Core;

namespace GridSense.Physics
{
    /// <summary>
    /// Configuration data for a single tyre compound (Soft, Medium, Hard).
    /// Defines base grip, thermal window, wear rate, and Pacejka coefficients.
    /// All values are documented reasonable placeholders for F1 simulation.
    /// </summary>
    [Serializable]
    public class TyreCompoundParameters
    {
        [Header("Identity")]
        public TyreCompound compound;
        public string displayName;

        [Header("Grip Properties")]
        [Tooltip("Peak static coefficient of friction mu_base (Soft > Medium > Hard)")]
        public float baseGrip = 1.70f;

        [Tooltip("Relative wear rate multiplier (Soft wears fastest, Hard slowest)")]
        public float wearRateMultiplier = 1.0f;

        [Header("Thermal Operating Window (Celsius)")]
        [Tooltip("Minimum temperature below which tyre suffers from cold glaze/poor bite")]
        public float tempMin = 60f;

        [Tooltip("Lower bound of optimal grip operating window")]
        public float tempOptMin = 90f;

        [Tooltip("Upper bound of optimal grip operating window")]
        public float tempOptMax = 115f;

        [Tooltip("Maximum temperature above which severe thermal degradation occurs")]
        public float tempMax = 145f;

        [Header("Pacejka Magic Formula Coefficients")]
        [Tooltip("B - Stiffness factor (longitudinal)")]
        public float B_long = 10.0f;
        [Tooltip("C - Shape factor (longitudinal)")]
        public float C_long = 1.65f;
        [Tooltip("D - Peak factor multiplier")]
        public float D_long = 1.0f;
        [Tooltip("E - Curvature factor (longitudinal)")]
        public float E_long = -0.5f;

        [Tooltip("B - Stiffness factor (lateral)")]
        public float B_lat = 8.5f;
        [Tooltip("C - Shape factor (lateral)")]
        public float C_lat = 1.30f;
        [Tooltip("D - Peak factor multiplier")]
        public float D_lat = 1.0f;
        [Tooltip("E - Curvature factor (lateral)")]
        public float E_lat = -0.7f;

        [Header("Thermal Response")]
        [Tooltip("Thermal mass specific heat capacity proxy")]
        public float thermalCapacity = 1200f;

        [Tooltip("Heat generation coefficient from slip energy")]
        public float heatGenCoefficient = 0.00035f;

        [Tooltip("Base convective cooling rate in ambient air")]
        public float coolingRateBase = 0.08f;

        [Tooltip("Speed-dependent cooling rate factor (W / (m^2 * K * (m/s)))")]
        public float coolingRateSpeedFactor = 0.003f;
    }

    /// <summary>
    /// Factory for compound parameter profiles.
    /// Provides baseline F1 parameters:
    /// - Soft (C3/C4/C5): High grip (1.80), fast wear (1.5x), narrow window (90-110C)
    /// - Medium (C2/C3): Balanced grip (1.65), medium wear (1.0x), standard window (95-120C)
    /// - Hard (C1/C2): Lower grip (1.50), durable wear (0.65x), wider/hotter window (100-130C)
    /// </summary>
    public static class TyreCompoundDatabase
    {
        public static TyreCompoundParameters GetPreset(TyreCompound compound)
        {
            switch (compound)
            {
                case TyreCompound.Soft:
                    return new TyreCompoundParameters
                    {
                        compound = TyreCompound.Soft,
                        displayName = "Pirelli Red (Soft)",
                        baseGrip = 1.82f,
                        wearRateMultiplier = 1.50f,
                        tempMin = 65f,
                        tempOptMin = 90f,
                        tempOptMax = 110f,
                        tempMax = 140f,
                        B_long = 11.0f,
                        C_long = 1.65f,
                        D_long = 1.0f,
                        E_long = -0.5f,
                        B_lat = 9.2f,
                        C_lat = 1.35f,
                        D_lat = 1.0f,
                        E_lat = -0.7f,
                        thermalCapacity = 1100f,
                        heatGenCoefficient = 0.00042f,
                        coolingRateBase = 0.08f,
                        coolingRateSpeedFactor = 0.0032f
                    };

                case TyreCompound.Hard:
                    return new TyreCompoundParameters
                    {
                        compound = TyreCompound.Hard,
                        displayName = "Pirelli White (Hard)",
                        baseGrip = 1.50f,
                        wearRateMultiplier = 0.65f,
                        tempMin = 75f,
                        tempOptMin = 100f,
                        tempOptMax = 130f,
                        tempMax = 160f,
                        B_long = 9.0f,
                        C_long = 1.60f,
                        D_long = 1.0f,
                        E_long = -0.45f,
                        B_lat = 7.8f,
                        C_lat = 1.25f,
                        D_lat = 1.0f,
                        E_lat = -0.65f,
                        thermalCapacity = 1350f,
                        heatGenCoefficient = 0.00028f,
                        coolingRateBase = 0.075f,
                        coolingRateSpeedFactor = 0.0028f
                    };

                case TyreCompound.Medium:
                default:
                    return new TyreCompoundParameters
                    {
                        compound = TyreCompound.Medium,
                        displayName = "Pirelli Yellow (Medium)",
                        baseGrip = 1.66f,
                        wearRateMultiplier = 1.00f,
                        tempMin = 70f,
                        tempOptMin = 95f,
                        tempOptMax = 120f,
                        tempMax = 150f,
                        B_long = 10.0f,
                        C_long = 1.65f,
                        D_long = 1.0f,
                        E_long = -0.5f,
                        B_lat = 8.5f,
                        C_lat = 1.30f,
                        D_lat = 1.0f,
                        E_lat = -0.7f,
                        thermalCapacity = 1200f,
                        heatGenCoefficient = 0.00035f,
                        coolingRateBase = 0.08f,
                        coolingRateSpeedFactor = 0.0030f
                    };
            }
        }
    }
}
