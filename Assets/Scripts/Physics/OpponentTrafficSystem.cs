using System;
using UnityEngine;
using GridSense.Core;

namespace GridSense.Physics
{
    /// <summary>
    /// Configuration for a rule-based opponent vehicle on track.
    /// </summary>
    [Serializable]
    public class RuleBasedOpponent
    {
        public string driverName = "CAR #01";
        [Tooltip("Relative time gap in seconds relative to the player (+ ahead, - behind)")]
        public float relativeGapSeconds = 1.8f;
        [Tooltip("Relative pace delta in seconds/lap compared to player (+ faster, - slower)")]
        public float paceDeltaSecondsPerLap = -0.3f;
        [Tooltip("Whether this car is actively active on track")]
        public bool isActive = true;
    }

    /// <summary>
    /// Section 3.7: Opponent Traffic System:
    /// Rule-based opponent cars that generate real dirty-air and overtake/defense scenarios.
    /// Feeds CarState.GapAheadS, CarState.HasGapAhead, CarState.GapBehindS, and CarState.HasGapBehind.
    /// The dirty-air downforce loss and slipstream effects are then computed automatically
    /// by Section 3.3 Aerodynamics without building a redundant gap detection system.
    /// </summary>
    public class OpponentTrafficSystem : MonoBehaviour
    {
        [Header("Scenario Presets")]
        [Tooltip("Select preset traffic scenario")]
        [SerializeField] private TrafficScenario activeScenario = TrafficScenario.OvertakeTargetAhead;

        [Header("Configured Opponents")]
        [SerializeField] private RuleBasedOpponent carAhead = new RuleBasedOpponent
        {
            driverName = "CAR #02",
            relativeGapSeconds = 1.4f,
            paceDeltaSecondsPerLap = -0.4f, // 0.4s slower per lap, so player catches them
            isActive = true
        };

        [SerializeField] private RuleBasedOpponent carBehind = new RuleBasedOpponent
        {
            driverName = "CAR #03",
            relativeGapSeconds = -2.5f,
            paceDeltaSecondsPerLap = 0.2f, // 0.2s faster, slowly chasing
            isActive = true
        };

        [Header("Traffic Detection Horizons")]
        [Tooltip("Maximum gap horizon in seconds beyond which opponent is considered out of range")]
        [SerializeField] private float maxDetectionHorizonS = 8.0f;

        public enum TrafficScenario
        {
            CleanAir,             // No cars in proximity
            OvertakeTargetAhead,  // Catching slower car ahead (dirty air + DRS overtake window)
            DefendingFromBehind,  // Faster car closing from behind (defense scenario)
            TrafficPack           // Car immediately ahead and car pressing from behind
        }

        private void Awake()
        {
            ApplyScenario(activeScenario);
        }

        public void ApplyScenario(TrafficScenario scenario)
        {
            activeScenario = scenario;
            switch (scenario)
            {
                case TrafficScenario.CleanAir:
                    carAhead.isActive = false;
                    carBehind.isActive = false;
                    break;

                case TrafficScenario.OvertakeTargetAhead:
                    carAhead.isActive = true;
                    carAhead.relativeGapSeconds = 1.35f; // inside 2.0s dirty-air window
                    carAhead.paceDeltaSecondsPerLap = -0.5f;
                    carBehind.isActive = false;
                    break;

                case TrafficScenario.DefendingFromBehind:
                    carAhead.isActive = false;
                    carBehind.isActive = true;
                    carBehind.relativeGapSeconds = -0.85f; // within 1.0s DRS threat
                    carBehind.paceDeltaSecondsPerLap = 0.4f;
                    break;

                case TrafficScenario.TrafficPack:
                    carAhead.isActive = true;
                    carAhead.relativeGapSeconds = 0.75f; // heavy dirty air
                    carAhead.paceDeltaSecondsPerLap = -0.2f;
                    carBehind.isActive = true;
                    carBehind.relativeGapSeconds = -0.65f;
                    carBehind.paceDeltaSecondsPerLap = 0.3f;
                    break;
            }
        }

        /// <summary>
        /// Updates relative gaps and writes them into CarState.
        /// </summary>
        public void UpdateTraffic(ref CarState state, float lapProgressNormalised, float deltaTime)
        {
            // Simulate relative progress over time
            float lapFractionDelta = deltaTime / 90.0f; // based on ~90s nominal lap

            // 1. Process Car Ahead
            if (carAhead.isActive)
            {
                // Closing or opening gap based on pace delta
                carAhead.relativeGapSeconds += carAhead.paceDeltaSecondsPerLap * lapFractionDelta;

                // When overtaken (gap drops to <= 0), it becomes a car behind
                if (carAhead.relativeGapSeconds < 0.1f)
                {
                    carAhead.relativeGapSeconds = 0.1f; // maintain close battle or completed pass
                }

                if (carAhead.relativeGapSeconds <= maxDetectionHorizonS)
                {
                    state.HasGapAhead = true;
                    state.GapAheadS = Mathf.Max(0.05f, carAhead.relativeGapSeconds);
                }
                else
                {
                    state.HasGapAhead = false;
                    state.GapAheadS = 0f;
                }
            }
            else
            {
                state.HasGapAhead = false;
                state.GapAheadS = 0f;
            }

            // 2. Process Car Behind
            if (carBehind.isActive)
            {
                float absGapBehind = Mathf.Abs(carBehind.relativeGapSeconds);
                absGapBehind -= carBehind.paceDeltaSecondsPerLap * lapFractionDelta;

                carBehind.relativeGapSeconds = -Mathf.Max(0.1f, absGapBehind);

                if (absGapBehind <= maxDetectionHorizonS)
                {
                    state.HasGapBehind = true;
                    state.GapBehindS = absGapBehind;
                }
                else
                {
                    state.HasGapBehind = false;
                    state.GapBehindS = 0f;
                }
            }
            else
            {
                state.HasGapBehind = false;
                state.GapBehindS = 0f;
            }
        }
    }
}
