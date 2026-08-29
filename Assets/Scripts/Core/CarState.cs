using System;

namespace GridSense.Core
{
    /// <summary>
    /// Tyre compound selection for Formula 1 tyres.
    /// </summary>
    public enum TyreCompound
    {
        Soft = 0,
        Medium = 1,
        Hard = 2
    }

    /// <summary>
    /// ERS (Energy Recovery System) battery deployment strategy mode.
    /// </summary>
    public enum EnergyMode
    {
        Push,
        Balanced,
        Hold,
        Save
    }

    /// <summary>
    /// Braking aggressiveness level affecting deceleration, tyre lockup risk, and thermal load.
    /// </summary>
    public enum BrakingAggressiveness
    {
        Normal,
        Aggressive
    }

    /// <summary>
    /// The single shared runtime state struct for the GridSense simulation.
    /// Vehicle physics, tyre degradation model, energy deployment policy, and the pit-wall dashboard
    /// all read from and write to instances of this struct in the main simulation loop.
    /// </summary>
    [Serializable]
    public struct CarState
    {
        public int Lap;
        public int Sector;
        public float DistanceIntoLapM;
        public TyreCompound Compound;         // enum: Soft, Medium, Hard
        public float TyreWearPct;             // 0-100, from degradation model
        public float TyreTempC;
        public float BrakeTempC;
        public float TyreWearRateCurrent;     // instantaneous slope, feeds energy model's risk calc
        public float EnergyRemainingPct;
        public EnergyMode DeploymentMode;     // enum: Push, Balanced, Hold, Save
        public BrakingAggressiveness Braking; // enum: Normal, Aggressive
        public float FuelLoadKg;
        public float GapAheadS;
        public bool HasGapAhead;
        public float GapBehindS;
        public bool HasGapBehind;
        public bool DirtyAir;
        public bool DrsOpen;
        public float TrackEvolutionFactor;
    }
}
