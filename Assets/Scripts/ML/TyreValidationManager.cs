using System;
using System.Collections.Generic;
using UnityEngine;
using GridSense.Core;
using GridSense.Physics;

namespace GridSense.ML
{
    [Serializable]
    public class StintLapValidationEntry
    {
        public int LapInStint;
        public float RawLapTimeSec;
        public float ObservedPaceDeltaSec;
        public float PredictedIsolatedDeltaSec;
        public float LowerErrorBoundSec;
        public float UpperErrorBoundSec;
        public float ErrorBandWidthSec;
        public float FuelRemainingKg;
        public float GapAheadSec;
    }

    [Serializable]
    public class HoldoutStint
    {
        public string StintId;
        public string Title;
        public string Circuit;
        public string Driver;
        public string Compound;
        public int TotalLaps;
        public List<StintLapValidationEntry> Laps;
    }

    [Serializable]
    public class HoldoutStintPayload
    {
        public string Description;
        public List<HoldoutStint> Stints;
    }

    /// <summary>
    /// Section 4c: Manages the dual validation pipeline:
    /// 1. Real-world validation: Holdout F1 race-day stints with EBM prediction curves and wide ±1σ error bands.
    /// 2. In-engine validation: Live Sentis predicted wear vs TyreModel physical slip-energy ground truth.
    /// </summary>
    public class TyreValidationManager : MonoBehaviour
    {
        [Header("Validation Data Source")]
        [SerializeField] private TextAsset holdoutStintJsonAsset;

        [Header("Runtime References")]
        [SerializeField] private VehicleController vehicle;
        [SerializeField] private SentisTyreDegradationModel sentisModel;

        [Header("Active Selection")]
        [SerializeField] private int activeStintIndex = 0;

        private HoldoutStintPayload _payload;

        public HoldoutStint ActiveStint => (_payload != null && _payload.Stints != null && activeStintIndex < _payload.Stints.Count)
            ? _payload.Stints[activeStintIndex] 
            : null;

        public int StintCount => _payload?.Stints?.Count ?? 0;

        public float LiveAiWearPct => vehicle != null ? vehicle.State.TyreWearPct : 0f;
        public float LivePhysicsGroundTruthWearPct => vehicle != null && vehicle.TyreModel != null ? vehicle.TyreModel.GetAverageTrueWearPct() : 0f;
        public float LiveWearResidualPct => Mathf.Abs(LiveAiWearPct - LivePhysicsGroundTruthWearPct);

        private void Awake()
        {
            if (vehicle == null)
                vehicle = FindAnyObjectByType<VehicleController>();
            if (sentisModel == null)
                sentisModel = FindAnyObjectByType<SentisTyreDegradationModel>();

            LoadValidationData();
        }

        public void LoadValidationData()
        {
            if (holdoutStintJsonAsset != null)
            {
                try
                {
                    _payload = JsonUtility.FromJson<HoldoutStintPayload>(holdoutStintJsonAsset.text);
                    Debug.Log($"[TyreValidationManager] Loaded {_payload?.Stints?.Count ?? 0} holdout stints from TextAsset.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TyreValidationManager] Failed to parse TextAsset: {ex.Message}");
                }
            }
            else
            {
                // Load from file on disk as fallback
                string path = System.IO.Path.Combine(Application.dataPath, "Data", "Validation", "holdout_stint_data.json");
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    _payload = JsonUtility.FromJson<HoldoutStintPayload>(json);
                    Debug.Log($"[TyreValidationManager] Loaded {_payload?.Stints?.Count ?? 0} holdout stints from disk: {path}");
                }
            }
        }

        public void SelectStint(int index)
        {
            if (_payload != null && _payload.Stints != null && index >= 0 && index < _payload.Stints.Count)
            {
                activeStintIndex = index;
            }
        }

        public List<HoldoutStint> GetAllStints()
        {
            return _payload?.Stints ?? new List<HoldoutStint>();
        }
    }
}
