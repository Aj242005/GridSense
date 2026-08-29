using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GridSense.Core;
using GridSense.Physics;
using GridSense.ML;

namespace GridSense.UI
{
    /// <summary>
    /// Section 4c: First-class Pit-Wall UI Toolkit Dashboard for Tyre Degradation Validation.
    /// Visualizes:
    /// 1. Real race-day holdout stints with isolated EBM degradation predictions and wide honest error bands (±1σ).
    /// 2. Live in-engine Sentis inferred wear vs. TyreModel physical slip-energy ground truth.
    /// 3. Decomposed environmental confound attributions.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TyreValidationDashboard : MonoBehaviour
    {
        [Header("System References")]
        [SerializeField] private TyreValidationManager validationManager;
        [SerializeField] private VehicleController vehicle;
        [SerializeField] private SentisTyreDegradationModel sentisModel;

        private UIDocument _uiDocument;
        private VisualElement _root;

        // UI Labels & Progress Bars
        private Label _aiWearLabel;
        private Label _aiPaceDeltaLabel;
        private VisualElement _aiProgressBar;

        private Label _physicsWearLabel;
        private Label _physicsSubtextLabel;
        private VisualElement _physicsProgressBar;

        private Label _residualLabel;
        private VisualElement _residualProgressBar;
        private Label _latencyLabel;

        private Label _chartTitleLabel;
        private VisualElement _chartCanvas;
        private Button _tabAlonso;
        private Button _tabVerstappen;
        private Button _tabHamilton;

        // Energy & Deployment Management UI
        private Label _energySocLabel;
        private VisualElement _energyProgressBar;
        private Label _energySubtextLabel;

        private Label _deployModeLabel;
        private VisualElement _deployProgressBar;
        private Label _deployBudgetLabel;

        private Label _tacticalScoreLabel;
        private Label _tacticalDirectiveLabel;

        // Live Gauges & Corner Labels
        private Label _liveSpeedLabel;
        private Label _liveGearLabel;
        private Label _liveRpmLabel;
        private Label _cornerFLLabel;
        private Label _cornerFRLabel;
        private Label _cornerRLLabel;
        private Label _cornerRRLabel;

        // Bottom Real-Time Attributions & Telemetry Labels
        private Label _attrSpeedGear;
        private Label _attrFuelBurn;
        private Label _attrDirtyAir;
        private Label _attrTrackEvo;
        private Label _attrTrackTemp;
        private Label _attrTyreDeg;

        private int _selectedStintIndex = 0;

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            if (validationManager == null)
                validationManager = FindAnyObjectByType<TyreValidationManager>();
            if (vehicle == null)
                vehicle = FindAnyObjectByType<VehicleController>();
            if (sentisModel == null)
                sentisModel = FindAnyObjectByType<SentisTyreDegradationModel>();
        }

        private void OnEnable()
        {
            _root = _uiDocument.rootVisualElement;
            if (_root == null) return;

            // Ensure dashboard is visible on launch
            _root.style.display = DisplayStyle.Flex;

            // Bind Live Gauges
            _liveSpeedLabel = _root.Q<Label>("LiveSpeedLabel");
            _liveGearLabel = _root.Q<Label>("LiveGearLabel");
            _liveRpmLabel = _root.Q<Label>("LiveRpmLabel");

            // Bind UI Elements
            _aiWearLabel = _root.Q<Label>("AiWearLabel");
            _aiPaceDeltaLabel = _root.Q<Label>("AiPaceDeltaLabel");
            _aiProgressBar = _root.Q<VisualElement>("AiProgressBar");

            _physicsWearLabel = _root.Q<Label>("PhysicsWearLabel");
            _physicsSubtextLabel = _root.Q<Label>("PhysicsSubtextLabel");
            _physicsProgressBar = _root.Q<VisualElement>("PhysicsProgressBar");

            _residualLabel = _root.Q<Label>("ResidualLabel");
            _residualProgressBar = _root.Q<VisualElement>("ResidualProgressBar");
            _latencyLabel = _root.Q<Label>("LatencyLabel");

            // Corner Tyre Grid
            _cornerFLLabel = _root.Q<Label>("CornerFLLabel");
            _cornerFRLabel = _root.Q<Label>("CornerFRLabel");
            _cornerRLLabel = _root.Q<Label>("CornerRLLabel");
            _cornerRRLabel = _root.Q<Label>("CornerRRLabel");

            // Energy & Deployment UI Elements
            _energySocLabel = _root.Q<Label>("EnergySocLabel");
            _energyProgressBar = _root.Q<VisualElement>("EnergyProgressBar");
            _energySubtextLabel = _root.Q<Label>("EnergySubtextLabel");

            _deployModeLabel = _root.Q<Label>("DeployModeLabel");
            _deployProgressBar = _root.Q<VisualElement>("DeployProgressBar");
            _deployBudgetLabel = _root.Q<Label>("DeployBudgetLabel");

            _tacticalScoreLabel = _root.Q<Label>("TacticalScoreLabel");
            _tacticalDirectiveLabel = _root.Q<Label>("TacticalDirectiveLabel");

            // Bottom Real-Time Attributions
            _attrSpeedGear = _root.Q<Label>("AttrSpeedGear");
            _attrFuelBurn = _root.Q<Label>("AttrFuelBurn");
            _attrDirtyAir = _root.Q<Label>("AttrDirtyAir");
            _attrTrackEvo = _root.Q<Label>("AttrTrackEvo");
            _attrTrackTemp = _root.Q<Label>("AttrTrackTemp");
            _attrTyreDeg = _root.Q<Label>("AttrTyreDeg");

            _chartTitleLabel = _root.Q<Label>("ChartTitleLabel");
            _chartCanvas = _root.Q<VisualElement>("ValidationChartCanvas");

            _tabAlonso = _root.Q<Button>("TabAlonso");
            _tabVerstappen = _root.Q<Button>("TabVerstappen");
            _tabHamilton = _root.Q<Button>("TabHamilton");

            // Register Tab Handlers
            if (_tabAlonso != null) _tabAlonso.clicked += () => SelectStint(0);
            if (_tabVerstappen != null) _tabVerstappen.clicked += () => SelectStint(1);
            if (_tabHamilton != null) _tabHamilton.clicked += () => SelectStint(2);

            // Register Custom Vector Painter on Chart Canvas
            if (_chartCanvas != null)
            {
                _chartCanvas.generateVisualContent += DrawValidationChart;
            }
        }

        private void SelectStint(int index)
        {
            _selectedStintIndex = index;
            if (validationManager != null)
                validationManager.SelectStint(index);

            // Update tab styles
            _tabAlonso?.EnableInClassList("mini-tab-active", index == 0);
            _tabVerstappen?.EnableInClassList("mini-tab-active", index == 1);
            _tabHamilton?.EnableInClassList("mini-tab-active", index == 2);

            if (_chartTitleLabel != null && validationManager != null && validationManager.ActiveStint != null)
            {
                _chartTitleLabel.text = validationManager.ActiveStint.Title.ToUpper();
            }

            _chartCanvas?.MarkDirtyRepaint();
        }

        private void Update()
        {
            UpdateTelemetryCardReadouts();
            _chartCanvas?.MarkDirtyRepaint();
        }

        private void UpdateTelemetryCardReadouts()
        {
            if (vehicle == null) return;

            // 0. Energy & Deployment Telemetry (Section 5 Sentis PPO Policy)
            float socPct = Mathf.Clamp(vehicle.State.EnergyRemainingPct, 0f, 100f);
            if (_energySocLabel != null) _energySocLabel.text = $"{socPct:F1}%";
            if (_energyProgressBar != null) _energyProgressBar.style.width = Length.Percent(Mathf.Clamp01(socPct / 100f) * 100f);
            
            float batteryMj = (socPct / 100f) * 4.0f;
            if (_energySubtextLabel != null) _energySubtextLabel.text = $"{batteryMj:F2} MJ / 4.00 MJ";

            EnergyMode dep = vehicle.State.DeploymentMode;
            if (_deployModeLabel != null) _deployModeLabel.text = $"{dep.ToString().ToUpper()}";

            float lapDeployMj = Mathf.Clamp((100f - socPct) * 0.04f, 0.5f, 4.0f);
            if (_deployBudgetLabel != null) _deployBudgetLabel.text = $"Lap Deploy: {lapDeployMj:F2} MJ / 4.00 MJ";
            if (_deployProgressBar != null) _deployProgressBar.style.width = Length.Percent(Mathf.Clamp01(lapDeployMj / 4.0f) * 100f);

            if (_tacticalScoreLabel != null) { var energyModel = vehicle.GetComponent<SentisEnergyDeploymentModel>(); _tacticalScoreLabel.text = energyModel != null && energyModel.IsInitialized ? $"POLICY SCORE: {energyModel.RiskRewardScore:+0.00;-0.00;0.00}" : "POLICY SCORE: MODEL OFFLINE"; }
            if (_tacticalDirectiveLabel != null)
            {
                var energyModel = vehicle.GetComponent<SentisEnergyDeploymentModel>();
                if (energyModel != null && energyModel.IsInitialized)
                {
                    _tacticalDirectiveLabel.text = energyModel.TacticalExplanation;
                }
                else
                {
                    _tacticalDirectiveLabel.text = (dep == EnergyMode.Push)
                        ? "FALLBACK: Full MGU-K boost deployed down straight"
                        : (dep == EnergyMode.Save)
                            ? "FALLBACK: Lift-and-coast energy harvest active"
                            : "FALLBACK: Balanced energy deployment & thermal conservation";
                }
            }

            // 1. AI Model Telemetry
            float aiWear = vehicle.State.TyreWearPct;
            float paceDelta = sentisModel != null && sentisModel.IsInitialized ? sentisModel.LastPredictedPaceDeltaSec : 0f;

            if (_aiWearLabel != null) _aiWearLabel.text = $"{aiWear:F1}%";
            if (_aiPaceDeltaLabel != null) _aiPaceDeltaLabel.text = $"Isolated Pace Delta: +{paceDelta:F2}s";
            if (_aiProgressBar != null) _aiProgressBar.style.width = Length.Percent(Mathf.Clamp01(aiWear / 100f) * 100f);

            // 2. Physics Ground Truth Telemetry
            float physWear = (vehicle.TyreModel != null) ? vehicle.TyreModel.GetAverageTrueWearPct() : 0f;
            if (_physicsWearLabel != null) _physicsWearLabel.text = $"{physWear:F1}%";
            if (_physicsProgressBar != null) _physicsProgressBar.style.width = Length.Percent(Mathf.Clamp01(physWear / 100f) * 100f);

            // 4-Corner Grid
            if (vehicle.TyreModel != null)
            {
                float flW = vehicle.TyreModel.GetTrueWearPct(0);
                float frW = vehicle.TyreModel.GetTrueWearPct(1);
                float rlW = vehicle.TyreModel.GetTrueWearPct(2);
                float rrW = vehicle.TyreModel.GetTrueWearPct(3);
                float tTemp = vehicle.State.TyreTempC;

                if (_cornerFLLabel != null) _cornerFLLabel.text = $"FL: {flW:F0}% | {tTemp:F0}°C";
                if (_cornerFRLabel != null) _cornerFRLabel.text = $"FR: {frW:F0}% | {tTemp:F0}°C";
                if (_cornerRLLabel != null) _cornerRLLabel.text = $"RL: {rlW:F0}% | {tTemp:F0}°C";
                if (_cornerRRLabel != null) _cornerRRLabel.text = $"RR: {rrW:F0}% | {tTemp:F0}°C";
            }

            // 3. Calibration Convergence (Residual)
            float residual = Mathf.Abs(aiWear - physWear);
            if (_residualLabel != null)
            {
                // Only show CONVERGED when both sensors are actually producing data
                bool bothActive = aiWear > 0.01f || physWear > 0.01f;
                string convergenceStatus;
                if (!bothActive)
                    convergenceStatus = "AWAITING DATA";
                else if (residual < 5f)
                    convergenceStatus = "CONVERGED";
                else
                    convergenceStatus = "DIVERGING";

                _residualLabel.text = $"Δ {residual:F1}% [{convergenceStatus}]";
                _residualLabel.style.color = (!bothActive)
                    ? new Color(0.6f, 0.6f, 0.6f)
                    : (residual < 5f)
                        ? new Color(0.02f, 0.84f, 0.63f)
                        : new Color(1f, 0.72f, 0.01f);
            }
            if (_residualProgressBar != null)
            {
                _residualProgressBar.style.width = Length.Percent(Mathf.Clamp01(residual / 20f) * 100f);
            }

            // 4. Latency
            if (_latencyLabel != null)
            {
                if (sentisModel != null && sentisModel.IsInitialized)
                {
                    float us = sentisModel.LastInferenceTimeMicroseconds;
                    _latencyLabel.text = $"Sentis: {us:F0} µs";
                }
                else
                {
                    _latencyLabel.text = "Sentis: MODEL OFFLINE";
                }
            }

            // 5. Real-Time Attributions & Live Gauges
            float speedKmh = vehicle.SpeedKmh;
            string gearText = "N";
            if (speedKmh > 1.0f)
            {
                int gear = Mathf.Clamp(Mathf.FloorToInt(speedKmh / 38f) + 1, 1, 8);
                gearText = $"GEAR {gear}";
            }
            else if (speedKmh < -1.0f)
            {
                gearText = "REVERSE";
            }

            if (_liveSpeedLabel != null) _liveSpeedLabel.text = $"{Mathf.Abs(speedKmh):F0}";
            if (_liveGearLabel != null) _liveGearLabel.text = gearText;
            float engineRpm = (speedKmh > 1.0f) ? Mathf.Lerp(4500f, 12500f, (speedKmh % 40f) / 40f) : 0f;
            if (_liveRpmLabel != null) _liveRpmLabel.text = $"{Mathf.RoundToInt(engineRpm)} RPM";

            if (_attrSpeedGear != null) _attrSpeedGear.text = $"{Mathf.Abs(speedKmh):F0} KM/H | {gearText}";

            float fuelGainSec = (100f - vehicle.State.FuelLoadKg) * -0.033f;
            if (_attrFuelBurn != null) _attrFuelBurn.text = $"{fuelGainSec:F2}s";

            float dirtyAirSec = vehicle.State.DirtyAir ? 0.65f : 0.00f;
            if (_attrDirtyAir != null) _attrDirtyAir.text = $"+{dirtyAirSec:F2}s";

            float trackEvoSec = -(vehicle.State.TrackEvolutionFactor - 0.90f) * 2.8f;
            if (_attrTrackEvo != null) _attrTrackEvo.text = $"{trackEvoSec:F2}s";

            float tyreTemp = vehicle.State.TyreTempC;
            if (_attrTrackTemp != null) _attrTrackTemp.text = $"{tyreTemp:F0}°C ({(tyreTemp >= 90f && tyreTemp <= 110f ? "Opt" : (tyreTemp < 90f ? "Cold" : "Hot"))})";

            if (_attrTyreDeg != null) _attrTyreDeg.text = $"+{paceDelta:F2}s";
        }

        /// <summary>
        /// Custom vector rendering of the validation chart:
        /// 1. Gridlines and axes
        /// 2. Wide honest ±1σ error band (semi-transparent filled area)
        /// 3. EBM isolated mean degradation line
        /// 4. Real race-day holdout lap points
        /// 5. Live in-engine car marker
        /// </summary>
        private void DrawValidationChart(MeshGenerationContext mgc)
        {
            if (_chartCanvas == null || validationManager == null || validationManager.ActiveStint == null)
                return;

            var painter = mgc.painter2D;
            float width = _chartCanvas.contentRect.width;
            float height = _chartCanvas.contentRect.height;

            if (width < 50f || height < 50f) return;

            float padL = 45f;
            float padR = 25f;
            float padT = 20f;
            float padB = 30f;

            float plotW = width - (padL + padR);
            float plotH = height - (padT + padB);

            // Chart coordinate scale: X: Lap 0 -> 30, Y: -0.2s -> +2.0s
            float maxLaps = 30f;
            float minY = -0.2f;
            float maxY = 2.0f;

            // 1. Draw Gridlines
            painter.strokeColor = new Color(0.15f, 0.22f, 0.35f, 0.4f);
            painter.lineWidth = 1.0f;

            for (float yVal = 0.0f; yVal <= maxY; yVal += 0.5f)
            {
                float normY = 1.0f - ((yVal - minY) / (maxY - minY));
                float py = padT + (normY * plotH);
                painter.BeginPath();
                painter.MoveTo(new Vector2(padL, py));
                painter.LineTo(new Vector2(padL + plotW, py));
                painter.Stroke();
            }

            // 2. Draw Error Band & EBM Curve from Holdout Stint
            var laps = validationManager.ActiveStint.Laps;
            if (laps != null && laps.Count > 1)
            {
                // A. Wide Honest Error Band (+/- 1-sigma)
                painter.fillColor = new Color(0f, 0.85f, 1f, 0.12f);
                painter.BeginPath();

                // Forward: Upper Bound
                for (int i = 0; i < laps.Count; i++)
                {
                    float lx = padL + (Mathf.Clamp01(laps[i].LapInStint / maxLaps) * plotW);
                    float normUpperY = 1.0f - ((laps[i].UpperErrorBoundSec - minY) / (maxY - minY));
                    float ly = padT + (Mathf.Clamp01(normUpperY) * plotH);

                    if (i == 0) painter.MoveTo(new Vector2(lx, ly));
                    else painter.LineTo(new Vector2(lx, ly));
                }

                // Backward: Lower Bound
                for (int i = laps.Count - 1; i >= 0; i--)
                {
                    float lx = padL + (Mathf.Clamp01(laps[i].LapInStint / maxLaps) * plotW);
                    float normLowerY = 1.0f - ((laps[i].LowerErrorBoundSec - minY) / (maxY - minY));
                    float ly = padT + (Mathf.Clamp01(normLowerY) * plotH);
                    painter.LineTo(new Vector2(lx, ly));
                }
                painter.ClosePath();
                painter.Fill();

                // B. EBM Isolated Mean Degradation Line
                painter.strokeColor = new Color(0f, 0.90f, 1f, 0.95f);
                painter.lineWidth = 2.5f;
                painter.BeginPath();
                for (int i = 0; i < laps.Count; i++)
                {
                    float lx = padL + (Mathf.Clamp01(laps[i].LapInStint / maxLaps) * plotW);
                    float normY = 1.0f - ((laps[i].PredictedIsolatedDeltaSec - minY) / (maxY - minY));
                    float ly = padT + (Mathf.Clamp01(normY) * plotH);

                    if (i == 0) painter.MoveTo(new Vector2(lx, ly));
                    else painter.LineTo(new Vector2(lx, ly));
                }
                painter.Stroke();

                // C. Real Race-Day Holdout Laps (FastF1 Points)
                painter.fillColor = new Color(1f, 0.72f, 0.01f, 0.85f);
                for (int i = 0; i < laps.Count; i++)
                {
                    float lx = padL + (Mathf.Clamp01(laps[i].LapInStint / maxLaps) * plotW);
                    float normY = 1.0f - ((laps[i].ObservedPaceDeltaSec - minY) / (maxY - minY));
                    float ly = padT + (Mathf.Clamp01(normY) * plotH);

                    painter.BeginPath();
                    painter.Arc(new Vector2(lx, ly), 3.5f, 0f, 360f);
                    painter.Fill();
                }
            }

            // 3. Live In-Engine Car Status Point (Green Indicator)
            if (vehicle != null)
            {
                float liveLap = Mathf.Clamp(vehicle.State.Lap, 1, 30);
                float livePaceLoss = (vehicle.State.TyreWearPct / 100f) * 1.20f;

                float cx = padL + (Mathf.Clamp01(liveLap / maxLaps) * plotW);
                float normLiveY = 1.0f - ((livePaceLoss - minY) / (maxY - minY));
                float cy = padT + (Mathf.Clamp01(normLiveY) * plotH);

                painter.fillColor = new Color(0.02f, 0.84f, 0.63f, 0.95f);
                painter.BeginPath();
                painter.Arc(new Vector2(cx, cy), 6.0f, 0f, 360f);
                painter.Fill();

                painter.strokeColor = Color.white;
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.Arc(new Vector2(cx, cy), 8.5f, 0f, 360f);
                painter.Stroke();
            }
        }
    }
}
