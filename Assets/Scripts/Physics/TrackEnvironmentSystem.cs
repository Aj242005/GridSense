using System;
using UnityEngine;
using GridSense.Core;
using GridSense.Data;

namespace GridSense.Physics
{
    /// <summary>
    /// Section 3.6: Track & Environment System:
    /// 1. Lap, Sector, and DistanceIntoLapM tracking using TrackMetadata and Start/Finish line coordinates
    /// 2. Track evolution model: Genuine environmental confound where rubber deposits over time/laps,
    ///    increasing grip (0.94 green track -> 1.05 rubbered-in track) and partially masking tyre wear
    /// 3. Writes Lap, Sector, DistanceIntoLapM, and TrackEvolutionFactor into CarState
    /// </summary>
    public class TrackEnvironmentSystem : MonoBehaviour
    {
        [Header("Circuit Reference")]
        [SerializeField] private TrackMetadata activeTrack;

        [Header("Track Evolution Configuration")]
        [Tooltip("Starting evolution on a green / unrubbered track (nominal ~0.94)")]
        [Range(0.85f, 1.0f)]
        [SerializeField] private float greenTrackEvolution = 0.94f;

        [Tooltip("Maximum rubbered-in track evolution factor (~1.05)")]
        [Range(1.0f, 1.15f)]
        [SerializeField] private float maxRubberedEvolution = 1.05f;

        [Tooltip("Rubber deposition rate per car lap completed")]
        [SerializeField] private float evolutionPerLap = 0.0028f;

        [Tooltip("Ambient track evolution drift (e.g. other cars running on circuit) per minute")]
        [SerializeField] private float ambientRubberRatePerMinute = 0.0035f;

        [Header("Runtime State")]
        [SerializeField] private int currentLap = 1;
        [SerializeField] private int currentSector = 1;
        [SerializeField] private float distanceIntoLapM = 0f;
        [SerializeField] private float trackEvolutionFactor = 0.94f;

        // Session tracking
        private float _sessionElapsedSeconds;
        private Vector3 _lastCarPosition;
        private bool _isFirstFrame = true;

        public TrackMetadata ActiveTrack => activeTrack;
        public float TrackEvolutionFactor => trackEvolutionFactor;

        private void Awake()
        {
            trackEvolutionFactor = greenTrackEvolution;
        }

        public void SetActiveTrack(TrackMetadata track)
        {
            activeTrack = track;
            currentLap = 1;
            currentSector = 1;
            distanceIntoLapM = 0f;
        }

        /// <summary>
        /// Updates track progression, lap/sector detection, and rubber deposition confound.
        /// </summary>
        public void UpdateTrackStep(ref CarState state, Vector3 carWorldPosition, float forwardSpeedMs, float deltaTime)
        {
            if (activeTrack == null) return;

            _sessionElapsedSeconds += deltaTime;

            // 1. Accumulate distance into lap
            if (!_isFirstFrame)
            {
                float deltaDist = Vector3.Distance(carWorldPosition, _lastCarPosition);
                // Filter out teleports or large physics corrections
                if (deltaDist < 20.0f)
                {
                    distanceIntoLapM += deltaDist;
                }
            }
            _lastCarPosition = carWorldPosition;
            _isFirstFrame = false;

            float trackLen = Mathf.Max(activeTrack.trackLengthMetres, 1000f);

            // 2. Start / Finish Line Crossing Detection
            if (distanceIntoLapM >= trackLen)
            {
                distanceIntoLapM -= trackLen;
                currentLap++;
            }

            // 3. Sector Detection based on normalised boundaries from TrackMetadata
            float normalisedLapPos = Mathf.Clamp01(distanceIntoLapM / trackLen);
            if (normalisedLapPos < activeTrack.sectorBoundaries.sector1End)
            {
                currentSector = 1;
            }
            else if (normalisedLapPos < activeTrack.sectorBoundaries.sector2End)
            {
                currentSector = 2;
            }
            else
            {
                currentSector = 3;
            }

            // 4. Track Evolution Confound
            // Rubber deposits from both player laps and session-wide ambient traffic
            float lapEvolution = (currentLap - 1) * evolutionPerLap;
            float sessionEvolution = (_sessionElapsedSeconds / 60.0f) * ambientRubberRatePerMinute;
            trackEvolutionFactor = Mathf.Clamp(greenTrackEvolution + lapEvolution + sessionEvolution, greenTrackEvolution, maxRubberedEvolution);

            // 5. Update CarState
            state.Lap = currentLap;
            state.Sector = currentSector;
            state.DistanceIntoLapM = distanceIntoLapM;
            state.TrackEvolutionFactor = trackEvolutionFactor;
        }
    }
}
