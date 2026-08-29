"""
GridSense - Section 4: Tyre Degradation Isolation Model
Explainable Boosting Machine (EBM) Training & Interpretability Analysis.

Decomposes observed lap time variations into isolated components:
- Tyre degradation per lap in stint (LapInStint)
- Fuel mass burn effect (FuelRemainingKg)
- Dirty-air traffic penalty (GapAheadSec)
- Track evolution & session progression (SessionProgression)
- Track temperature sensitivity (TrackTemp)
- Compound baseline offset (Compound: SOFT, MEDIUM, HARD)
"""

import os
import json
import pickle
import numpy as np
import pandas as pd
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

from sklearn.model_selection import train_test_split
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score
from interpret.glassbox import ExplainableBoostingRegressor

def train_tyre_degradation_ebm(data_csv: str, output_dir: str):
    print("=" * 70)
    print("GRID-SENSE SECTION 4: EXPLAINABLE BOOSTING MACHINE (EBM) TRAINING")
    print("=" * 70)

    if not os.path.exists(data_csv):
        raise FileNotFoundError(f"Dataset not found at: {data_csv}")

    df = pd.read_csv(data_csv)
    print(f"Loaded dataset: {len(df)} laps across {df['Circuit'].nunique()} circuits.")
    print(f"Circuits: {list(df['Circuit'].unique())}")
    print(f"Compounds: {list(df['Compound'].unique())}")

    # Feature definitions
    feature_cols = [
        'LapInStint',
        'FuelRemainingKg',
        'GapAheadSec',
        'SessionProgression',
        'TrackTemp',
        'Compound'
    ]

    target_col = 'ObservedLapDeltaSec'

    # Filter complete cases
    df_clean = df.dropna(subset=feature_cols + [target_col]).copy()
    print(f"Clean samples for training: {len(df_clean)}")

    X = df_clean[feature_cols]
    y = df_clean[target_col]

    # Train / Test split (80/20 stratified by Compound)
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.20, random_state=42, stratify=df_clean['Compound']
    )
    print(f"Train set: {len(X_train)} laps | Test set: {len(X_test)} laps\n")

    # Initialize Explainable Boosting Regressor (EBM)
    # Categorical feature: Compound
    ebm = ExplainableBoostingRegressor(
        interactions=4,              # allow top 4 pairwise interactions (e.g. LapInStint x Compound)
        random_state=42,
        max_bins=128,
        outer_bags=8,
        inner_bags=4,
        learning_rate=0.04
    )

    print("Training Explainable Boosting Machine...")
    ebm.fit(X_train, y_train)
    print("Training complete!\n")

    # Evaluation on Holdout Test Set
    y_pred_train = ebm.predict(X_train)
    y_pred_test = ebm.predict(X_test)

    train_r2 = r2_score(y_train, y_pred_train)
    test_r2 = r2_score(y_test, y_pred_test)
    train_mae = mean_absolute_error(y_train, y_pred_train)
    test_mae = mean_absolute_error(y_test, y_pred_test)
    test_rmse = np.sqrt(mean_squared_error(y_test, y_pred_test))

    print("-" * 50)
    print("PERFORMANCE METRICS:")
    print(f"  Train R^2: {train_r2:.4f} | Train MAE: {train_mae:.4f}s")
    print(f"  Test  R^2: {test_r2:.4f} | Test  MAE: {test_mae:.4f}s | Test RMSE: {test_rmse:.4f}s")
    print("-" * 50)

    # Global Interpretability & Feature Importances
    ebm_global = ebm.explain_global(name="EBM Tyre Degradation Model")
    feature_names = ebm_global.data()['names']
    importances = ebm_global.data()['scores']

    importance_dict = dict(zip(feature_names, [float(x) for x in importances]))
    sorted_importances = sorted(importance_dict.items(), key=lambda x: x[1], reverse=True)

    print("\nGLOBAL FEATURE IMPORTANCE RANKING (Mean Absolute Impact in seconds):")
    for feat, imp in sorted_importances:
        print(f"  {feat:<28}: {imp:.4f} s/lap")

    os.makedirs(output_dir, exist_ok=True)

    # Save Model Artifact
    model_path = os.path.join(output_dir, 'ebm_tyre_degradation.pkl')
    with open(model_path, 'wb') as f:
        pickle.dump(ebm, f)
    print(f"\nTrained EBM model saved to: {model_path}")

    # Generate Interpretability Report JSON
    report = {
        'model_type': 'ExplainableBoostingRegressor (EBM)',
        'circuits_trained': list(df['Circuit'].unique()),
        'total_samples': len(df_clean),
        'metrics': {
            'train_r2': float(train_r2),
            'test_r2': float(test_r2),
            'train_mae_sec': float(train_mae),
            'test_mae_sec': float(test_mae),
            'test_rmse_sec': float(test_rmse)
        },
        'feature_importances': dict(sorted_importances),
        'isolation_summary': {
            'fuel_effect_range_sec': float(np.ptp(ebm.eval_terms(X_test)[:, feature_names.index('FuelRemainingKg')])),
            'traffic_dirty_air_range_sec': float(np.ptp(ebm.eval_terms(X_test)[:, feature_names.index('GapAheadSec')])),
            'tyre_life_range_sec': float(np.ptp(ebm.eval_terms(X_test)[:, feature_names.index('LapInStint')])),
            'track_evolution_range_sec': float(np.ptp(ebm.eval_terms(X_test)[:, feature_names.index('SessionProgression')])),
            'track_temp_range_sec': float(np.ptp(ebm.eval_terms(X_test)[:, feature_names.index('TrackTemp')]))
        }
    }

    report_path = os.path.join(output_dir, 'interpretability_summary.json')
    with open(report_path, 'w') as f:
        json.dump(report, f, indent=2)
    print(f"Interpretability summary report saved to: {report_path}")

    # Generate Feature Curves Plot
    plot_path = os.path.join(output_dir, 'degradation_isolated_components.png')
    plot_isolated_effects(ebm, X_test, feature_names, plot_path)
    print(f"Isolated component visualization saved to: {plot_path}")

    return report

def plot_isolated_effects(ebm, X_test, feature_names, output_path: str):
    fig, axes = plt.subplots(2, 3, figsize=(16, 10))
    fig.suptitle("GridSense - EBM Isolated Component Attributions (Lap Time Delta in Seconds)", fontsize=14, fontweight='bold')

    plots = [
        ('LapInStint', 'Tyre Life in Stint (Pure Tyre Wear)', axes[0, 0], 'crimson'),
        ('FuelRemainingKg', 'Fuel Remaining (Fuel Load Confound)', axes[0, 1], 'navy'),
        ('GapAheadSec', 'Gap Ahead (Dirty Air / Traffic Confound)', axes[0, 2], 'purple'),
        ('SessionProgression', 'Session Progression (Track Evolution Confound)', axes[1, 0], 'forestgreen'),
        ('TrackTemp', 'Track Temperature (°C)', axes[1, 1], 'darkorange'),
    ]

    terms = ebm.eval_terms(X_test)

    for feat, label, ax, color in plots:
        if feat in feature_names:
            idx = feature_names.index(feat)
            x_vals = X_test[feat].values
            y_vals = terms[:, idx]
            sort_order = np.argsort(x_vals)
            ax.scatter(x_vals, y_vals, alpha=0.15, s=10, color=color)
            # Rolling mean line
            df_plot = pd.DataFrame({'x': x_vals, 'y': y_vals}).sort_values('x')
            rolling = df_plot.groupby(pd.cut(df_plot['x'], bins=20), observed=False).mean()
            ax.plot(rolling['x'], rolling['y'], color='black', linewidth=2.5, label='EBM Shape Function')
            ax.set_title(label, fontsize=11, fontweight='bold')
            ax.set_xlabel(feat)
            ax.set_ylabel("Impact on Lap Time (s)")
            ax.grid(True, alpha=0.3)
            ax.axhline(0, color='gray', linestyle='--', alpha=0.5)

    # Plot Compound offset in the 6th subplot
    if 'Compound' in feature_names:
        c_idx = feature_names.index('Compound')
        c_vals = X_test['Compound'].values
        c_impacts = terms[:, c_idx]
        df_c = pd.DataFrame({'Compound': c_vals, 'Impact': c_impacts}).groupby('Compound').mean()
        axes[1, 2].bar(df_c.index, df_c['Impact'], color=['lightcoral', 'gold', 'lightslategray'])
        axes[1, 2].set_title("Compound Pace Offset", fontsize=11, fontweight='bold')
        axes[1, 2].set_ylabel("Baseline Delta vs Mean (s)")
        axes[1, 2].grid(True, alpha=0.3)
        axes[1, 2].axhline(0, color='gray', linestyle='--', alpha=0.5)

    plt.tight_layout()
    plt.savefig(output_path, dpi=150)
    plt.close()

if __name__ == '__main__':
    base_dir = os.path.dirname(__file__)
    data_csv = os.path.join(base_dir, 'data', 'stint_dataset.csv')
    out_dir = os.path.join(base_dir, 'models')
    train_tyre_degradation_ebm(data_csv, out_dir)
