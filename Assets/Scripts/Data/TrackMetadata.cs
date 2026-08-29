using UnityEngine;
using System;

namespace GridSense.Data
{
    /// <summary>
    /// Defines the direction of a corner/turn.
    /// </summary>
    public enum CornerDirection
    {
        Left,
        Right
    }

    /// <summary>
    /// Classifies corner speed category based on typical F1 approach speeds.
    /// </summary>
    public enum CornerSpeedCategory
    {
        Slow,       // < 120 km/h apex
        Medium,     // 120-200 km/h apex
        Fast        // > 200 km/h apex
    }

    /// <summary>
    /// Defines a single corner/turn on the track.
    /// </summary>
    [Serializable]
    public struct CornerDefinition
    {
        [Tooltip("Turn number as used in official F1 designation (e.g. Turn 1, Turn 2)")]
        public int turnNumber;

        [Tooltip("Human-readable name if the corner has one (e.g. 'Sakhir', 'Degner')")]
        public string name;

        [Tooltip("Left or Right")]
        public CornerDirection direction;

        [Tooltip("Speed category: Slow (<120 km/h), Medium (120-200 km/h), Fast (>200 km/h)")]
        public CornerSpeedCategory speedCategory;

        [Tooltip("Approximate corner radius in metres (0 if unknown)")]
        public float radiusMetres;
    }

    /// <summary>
    /// Defines the three timing sectors of a track.
    /// Sector boundaries are stored as normalised (0-1) positions along the track centreline.
    /// </summary>
    [Serializable]
    public struct SectorBoundaries
    {
        [Tooltip("Normalised position (0-1) where Sector 1 ends / Sector 2 begins")]
        public float sector1End;

        [Tooltip("Normalised position (0-1) where Sector 2 ends / Sector 3 begins")]
        public float sector2End;

        [Tooltip("Human-readable description of Sector 1 boundary")]
        public string sector1Description;

        [Tooltip("Human-readable description of Sector 2 boundary")]
        public string sector2Description;

        [Tooltip("Human-readable description of Sector 3 boundary")]
        public string sector3Description;
    }

    /// <summary>
    /// ScriptableObject containing metadata for a single race circuit.
    /// Values sourced from official F1/FIA data and public reference materials.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTrackMetadata", menuName = "GridSense/Track Metadata")]
    public class TrackMetadata : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Official circuit name")]
        public string circuitName;

        [Tooltip("Country where the circuit is located")]
        public string country;

        [Tooltip("City/region where the circuit is located")]
        public string location;

        [Header("Dimensions")]
        [Tooltip("Official track length in metres")]
        public float trackLengthMetres;

        [Tooltip("Number of laps in a standard F1 race")]
        public int raceLaps;

        [Tooltip("Total race distance in metres")]
        public float raceDistanceMetres;

        [Header("Corners")]
        [Tooltip("Total number of corners")]
        public int totalCorners;

        [Tooltip("Detailed definitions for each corner")]
        public CornerDefinition[] corners;

        [Header("Sectors")]
        [Tooltip("Three-sector timing boundaries")]
        public SectorBoundaries sectorBoundaries;

        [Header("Start/Finish")]
        [Tooltip("Position of the start/finish line as a world-space offset from track origin. " +
                 "Set via mesh inspection or manual placement.")]
        public Vector3 startFinishPosition;

        [Tooltip("Forward direction at the start/finish line (unit vector)")]
        public Vector3 startFinishForward = Vector3.forward;

        [Header("Data Source Tracking")]
        [Tooltip("Which values came from public reference data vs mesh-derived extraction")]
        [TextArea(3, 10)]
        public string dataSourceNotes;
    }
}
