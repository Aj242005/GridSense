"""
GridSense - Section 4: Tyre Degradation Isolation Model
Data Ingestion & Feature Engineering Pipeline using FastF1.

Fetches real stint telemetry from clean F1 Grand Prix races:
1. 2023 Bahrain GP (Sakhir) - High abrasion / thermal wear, night cooling
2. 2023 Spanish GP (Barcelona) - Benchmark high lateral tyre energy
3. 2023 Austrian GP (Red Bull Ring) - Heavy longitudinal traction degradation
4. 2023 Abu Dhabi GP (Yas Marina) - Day-to-night traction & medium wear

Builds the feature set required by Section 4:
- lap_in_stint (TyreLife)
- fuel_corrected_pace (fuel burn adjustment: ~0.033 s/kg)
- gap_to_car_ahead (dirty-air & traffic confound)
- session_progression (rubbering-in / track evolution proxy: 0.0 -> 1.0)
- compound (SOFT, MEDIUM, HARD)
- track_temp (surface temperature in °C)
"""

import os
import fastf1
import pandas as pd
import numpy as np

def setup_fastf1_cache(cache_dir: str):
    os.makedirs(cache_dir, exist_ok=True)
    fastf1.Cache.enable_cache(cache_dir)
    print(f"[FastF1] Cache directory enabled at: {cache_dir}")

def process_race_session(year: int, race_name: str) -> pd.DataFrame:
    print(f"\n[FastF1] Ingesting {year} {race_name} Grand Prix...")
    session = fastf1.get_session(year, race_name, 'R')
    session.load(laps=True, telemetry=False, weather=True, messages=False)

    laps = session.laps.copy()
    weather = session.weather_data.copy()

    total_laps_race = int(laps['LapNumber'].max())
    print(f"  Total laps scheduled: {total_laps_race}, total lap entries: {len(laps)}")

    # 1. Clean data filtering
    # Exclude in-laps, out-laps, safety cars (TrackStatus '1' = Green only)
    clean_laps = laps[
        (laps['LapTime'].notna()) &
        (laps['PitInTime'].isna()) &
        (laps['PitOutTime'].isna()) &
        (laps['TrackStatus'] == '1') &
        (laps['IsAccurate'] == True) &
        (laps['Compound'].isin(['SOFT', 'MEDIUM', 'HARD']))
    ].copy()

    clean_laps['LapTimeSec'] = clean_laps['LapTime'].dt.total_seconds()
    clean_laps['TimeSec'] = clean_laps['Time'].dt.total_seconds()

    # 2. Compute Gap to Car Ahead
    # Sort by LapNumber and crossing Time to determine track position and gap
    clean_laps = clean_laps.sort_values(['LapNumber', 'TimeSec'])
    clean_laps['GapAheadSec'] = clean_laps.groupby('LapNumber')['TimeSec'].diff()
    # Leader has no car ahead -> set to 15.0s (clean air sentinel)
    clean_laps['GapAheadSec'] = clean_laps['GapAheadSec'].fillna(15.0).clip(0.0, 15.0)

    # 3. Merge Track Temperature from Weather data
    if not weather.empty and 'TrackTemp' in weather.columns:
        weather['WeatherTimeSec'] = weather['Time'].dt.total_seconds()
        # Merge on nearest timestamp
        clean_laps = pd.merge_asof(
            clean_laps.sort_values('TimeSec'),
            weather[['WeatherTimeSec', 'TrackTemp', 'AirTemp']].sort_values('WeatherTimeSec'),
            left_on='TimeSec',
            right_on='WeatherTimeSec',
            direction='nearest'
        )
    else:
        clean_laps['TrackTemp'] = 35.0
        clean_laps['AirTemp'] = 25.0

    # 4. Fuel Correction Model
    # FIA standard: ~105 kg starting race fuel, 1.8 kg/lap burn rate
    # Fuel penalty sensitivity: ~0.033 s/kg
    fuel_remaining_kg = np.maximum(5.0, 105.0 - (clean_laps['LapNumber'] * 1.8))
    fuel_mass_burned_kg = 105.0 - fuel_remaining_kg
    clean_laps['FuelRemainingKg'] = fuel_remaining_kg
    clean_laps['FuelPenaltySec'] = fuel_remaining_kg * 0.033
    # Fuel-corrected lap time = Raw lap time - (Fuel burned * 0.033 s/kg)
    clean_laps['FuelCorrectedPaceSec'] = clean_laps['LapTimeSec'] - (fuel_mass_burned_kg * 0.033)

    # 5. Session Progression (0.0 at start, 1.0 at finish)
    clean_laps['SessionProgression'] = clean_laps['LapNumber'] / float(total_laps_race)

    # 6. Lap in Stint (TyreLife)
    clean_laps['LapInStint'] = clean_laps['TyreLife'].fillna(1.0).astype(float)

    # 7. Compute Relative Degradation Pace Delta
    # Compute driver baseline (5th percentile lap time per driver per race to remove traffic/slow laps)
    driver_baselines = clean_laps.groupby(['Driver', 'Compound'])['LapTimeSec'].transform(lambda x: x.quantile(0.10))
    clean_laps['ObservedLapDeltaSec'] = clean_laps['LapTimeSec'] - driver_baselines

    # Remove extreme pace outliers (spins, punctures, off-tracks > 5.0s delta)
    clean_laps = clean_laps[
        (clean_laps['ObservedLapDeltaSec'] >= -1.0) & 
        (clean_laps['ObservedLapDeltaSec'] <= 4.5)
    ].copy()

    clean_laps['Circuit'] = race_name
    clean_laps['Year'] = year

    print(f"  Retained {len(clean_laps)} clean, valid stint laps.")
    return clean_laps

def build_tyre_degradation_dataset(output_csv: str) -> pd.DataFrame:
    base_dir = os.path.dirname(__file__)
    cache_dir = os.path.join(base_dir, 'cache')
    setup_fastf1_cache(cache_dir)

    races = [
        (2023, 'Bahrain'),
        (2023, 'Spain'),
        (2023, 'Austria'),
        (2023, 'Abu Dhabi'),
    ]

    all_dfs = []
    for year, race in races:
        try:
            df = process_race_session(year, race)
            all_dfs.append(df)
        except Exception as e:
            print(f"[ERROR] Failed to ingest {year} {race}: {e}")

    if not all_dfs:
        raise RuntimeError("No race data could be loaded.")

    combined_df = pd.concat(all_dfs, ignore_index=True)
    os.makedirs(os.path.dirname(output_csv), exist_ok=True)
    
    # Feature columns subset
    feature_cols = [
        'Circuit', 'Year', 'Driver', 'LapNumber', 'Stint',
        'LapInStint', 'Compound', 'LapTimeSec', 'FuelRemainingKg',
        'FuelPenaltySec', 'FuelCorrectedPaceSec', 'GapAheadSec',
        'SessionProgression', 'TrackTemp', 'AirTemp', 'ObservedLapDeltaSec'
    ]
    export_df = combined_df[feature_cols].copy()
    export_df.to_csv(output_csv, index=False)
    print(f"\n[FastF1] Successfully generated consolidated dataset: {output_csv}")
    print(f"Total valid laps: {len(export_df)}")
    print(export_df.groupby(['Circuit', 'Compound'])['LapInStint'].count())
    return export_df

if __name__ == '__main__':
    data_dir = os.path.join(os.path.dirname(__file__), 'data')
    output_path = os.path.join(data_dir, 'stint_dataset.csv')
    build_tyre_degradation_dataset(output_path)
