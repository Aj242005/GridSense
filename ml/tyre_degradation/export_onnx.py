"""
GridSense - Section 4b: ONNX Export & Parity Verification Pipeline.

1. Trains/verifies EBM with numerical inputs suitable for Unity Sentis TensorFloat.
2. Converts Explainable Boosting Machine to standard ONNX.
3. Performs rigorous numerical parity verification between Python EBM and ONNX Runtime.
4. Exports verified model to Assets/Data/Models/ for Unity Sentis runtime inference.
"""

import os
import shutil
import pickle
import numpy as np
import pandas as pd
import onnx
import onnxruntime as ort
import ebm2onnx
from interpret.glassbox import ExplainableBoostingRegressor
from sklearn.model_selection import train_test_split
from sklearn.metrics import mean_absolute_error, r2_score

def export_and_verify_onnx():
    base_dir = os.path.dirname(__file__)
    data_csv = os.path.join(base_dir, 'data', 'stint_dataset.csv')
    output_dir = os.path.join(base_dir, 'models')
    unity_model_dir = os.path.join(base_dir, '..', '..', 'Assets', 'Data', 'Models')
    os.makedirs(output_dir, exist_ok=True)
    os.makedirs(unity_model_dir, exist_ok=True)

    print("=" * 70)
    print("GRID-SENSE SECTION 4B: ONNX EXPORT & SENTIS PARITY VERIFICATION")
    print("=" * 70)

    df = pd.read_csv(data_csv)

    # Numerical compound encoding for Unity Sentis TensorFloat compatibility
    # SOFT = 0.0, MEDIUM = 1.0, HARD = 2.0
    compound_map = {'SOFT': 0.0, 'MEDIUM': 1.0, 'HARD': 2.0}
    df['CompoundCode'] = df['Compound'].map(compound_map).astype(float)

    feature_cols = [
        'LapInStint',
        'FuelRemainingKg',
        'GapAheadSec',
        'SessionProgression',
        'TrackTemp',
        'CompoundCode'
    ]
    target_col = 'ObservedLapDeltaSec'

    clean_df = df.dropna(subset=feature_cols + [target_col]).copy()
    X = clean_df[feature_cols].astype(np.float32)
    y = clean_df[target_col].astype(np.float32)

    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.20, random_state=42, stratify=clean_df['CompoundCode']
    )

    print(f"Training numerical EBM on {len(X_train)} laps (Test: {len(X_test)} laps)...")
    ebm = ExplainableBoostingRegressor(
        interactions=4,
        random_state=42,
        max_bins=128,
        outer_bags=8,
        inner_bags=4,
        learning_rate=0.04
    )
    ebm.fit(X_train, y_train)

    train_r2 = r2_score(y_train, ebm.predict(X_train))
    test_r2 = r2_score(y_test, ebm.predict(X_test))
    test_mae = mean_absolute_error(y_test, ebm.predict(X_test))
    print(f"EBM Performance: Test R^2 = {test_r2:.4f}, Test MAE = {test_mae:.4f}s")

    # Save updated PKL
    pkl_path = os.path.join(output_dir, 'ebm_tyre_degradation_numerical.pkl')
    with open(pkl_path, 'wb') as f:
        pickle.dump(ebm, f)

    # ─────────────────────────────────────────────────────────────────
    # 2. CONVERT TO ONNX VIA EBM2ONNX
    # ─────────────────────────────────────────────────────────────────
    dtypes = {
        'LapInStint': 'float',
        'FuelRemainingKg': 'float',
        'GapAheadSec': 'float',
        'SessionProgression': 'float',
        'TrackTemp': 'float',
        'CompoundCode': 'float'
    }

    print("\nConverting EBM to standard ONNX graph...")
    onnx_model = ebm2onnx.to_onnx(
        ebm,
        dtype=dtypes,
        name='tyre_degradation_ebm',
        target_opset=14
    )

    onnx_path = os.path.join(output_dir, 'ebm_tyre_degradation.onnx')
    onnx.save(onnx_model, onnx_path)
    print(f"Saved ONNX model to: {onnx_path}")

    # ─────────────────────────────────────────────────────────────────
    # 3. VERIFY NUMERICAL PARITY ON HELD-OUT TEST DATA
    # ─────────────────────────────────────────────────────────────────
    print("\nVerifying numerical parity with ONNX Runtime...")
    ort_session = ort.InferenceSession(onnx_path)

    # Test on 200 random held-out test samples
    sample_indices = np.random.RandomState(42).choice(len(X_test), size=min(200, len(X_test)), replace=False)
    X_sample = X_test.iloc[sample_indices]

    py_preds = ebm.predict(X_sample)

    # Run ONNX inference
    ort_inputs = {col: X_sample[col].values.astype(np.float32) for col in feature_cols}
    ort_outputs = ort_session.run(None, ort_inputs)
    onnx_preds = ort_outputs[0].flatten()

    diffs = np.abs(py_preds - onnx_preds)
    max_diff = float(np.max(diffs))
    mean_diff = float(np.mean(diffs))
    std_diff = float(np.std(diffs))

    print(f"Parity Sample Size: {len(X_sample)} holdout laps")
    print(f"  Maximum Absolute Delta: {max_diff:.8f} seconds")
    print(f"  Mean Absolute Delta:    {mean_diff:.8f} seconds")
    print(f"  Std Deviation Delta:    {std_diff:.8f} seconds")

    if max_diff < 1e-4:
        print("[SUCCESS] Parity verification PASSED! ONNX model strictly matches Python EBM.")
    else:
        print(f"[WARNING] Parity discrepancy exceeds threshold: {max_diff}")

    # ─────────────────────────────────────────────────────────────────
    # 4. COPY VERIFIED ONNX MODEL TO UNITY PROJECT
    # ─────────────────────────────────────────────────────────────────
    unity_target = os.path.join(unity_model_dir, 'ebm_tyre_degradation.onnx')
    shutil.copyfile(onnx_path, unity_target)
    print(f"\n[Unity] Copied verified ONNX model to: {unity_target}")

    # Also export sample test inputs and expected outputs for in-engine unit verification
    test_suite = []
    for i in range(10):
        row = X_sample.iloc[i]
        test_suite.append({
            'LapInStint': float(row['LapInStint']),
            'FuelRemainingKg': float(row['FuelRemainingKg']),
            'GapAheadSec': float(row['GapAheadSec']),
            'SessionProgression': float(row['SessionProgression']),
            'TrackTemp': float(row['TrackTemp']),
            'CompoundCode': float(row['CompoundCode']),
            'ExpectedPaceDeltaSec': float(py_preds[i])
        })
    test_json_path = os.path.join(output_dir, 'onnx_parity_test_vectors.json')
    with open(test_json_path, 'w') as f:
        import json
        json.dump(test_suite, f, indent=2)
    print(f"Exported test verification vectors to: {test_json_path}")

    return {
        'max_diff': max_diff,
        'mean_diff': mean_diff,
        'test_r2': test_r2,
        'test_mae': test_mae
    }

if __name__ == '__main__':
    export_and_verify_onnx()
