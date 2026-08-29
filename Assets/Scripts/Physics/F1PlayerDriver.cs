using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using GridSense.Physics;

namespace GridSense.Simulation
{
    /// <summary>
    /// Interactive Driver & Stint Controller for GridSense standalone runtime.
    /// Supports both Unity New Input System and Legacy Input Manager.
    /// Handles:
    /// - Player keyboard driving inputs (WASD / Arrows, Space for DRS).
    /// - Autonomous AI Autopilot mode (Toggle via 'T') for automatic stint evaluation.
    /// - Circuit switching (Keys '1': Bahrain, '2': Shanghai, '3': Suzuka, '4': Yas Marina).
    /// - Reset car to starting grid (Key 'R').
    /// - Dashboard toggle (Key 'Tab' or 'H').
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class F1PlayerDriver : MonoBehaviour
    {
        [Header("Autopilot Configuration")]
        [SerializeField] private bool aiAutopilotEnabled = false; // Manual player driving by default!
        [SerializeField] private float autopilotTargetSpeedKmh = 260.0f;

        [Header("Car Reset")]
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 0.45f, 0f);
        [SerializeField] private Quaternion spawnRotation = Quaternion.identity;

        private VehicleController _vehicle;
        private Rigidbody _rb;
        private UIDocument _dashboardDoc;
        private string _logPath;
        private float _lastLogTime;

        public bool IsAutopilotEnabled => aiAutopilotEnabled;

        private void Awake()
        {
            Application.runInBackground = true;
            _vehicle = GetComponent<VehicleController>();
            _rb = GetComponent<Rigidbody>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            _logPath = Path.Combine(Application.dataPath, "../runtime_input_log.txt");
            
            try
            {
                File.WriteAllText(_logPath, $"=== GRIDSENSE RUNTIME INPUT LOG INITIALIZED: {DateTime.Now} ===\nMode: Manual WASD Driving Default\n");
            }
            catch {}
        }

        private void Start()
        {
            _dashboardDoc = FindAnyObjectByType<UIDocument>();
        }

        private bool IsPressed(
#if ENABLE_INPUT_SYSTEM
            Key newKey, 
#endif
            KeyCode legacyKey)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current[newKey].isPressed) return true;
#endif
            try
            {
                if (Input.GetKey(legacyKey)) return true;
            }
            catch {}
            return false;
        }

        private bool WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
            Key newKey, 
#endif
            KeyCode legacyKey)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current[newKey].wasPressedThisFrame) return true;
#endif
            try
            {
                if (Input.GetKeyDown(legacyKey)) return true;
            }
            catch {}
            return false;
        }

        private void Update()
        {
            if (Time.frameCount % 50 == 0)
            {
                bool testW = IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.W,
#endif
                    KeyCode.W);
                LogToFile($"[HEARTBEAT] Frame={Time.frameCount}, Time={Time.time:F2}, aiAuto={aiAutopilotEnabled}, W={testW}, Speed={_vehicle?.SpeedKmh:F1}");
            }

            // 1. Toggle AI Autopilot (Key 'T')
            if (WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.T,
#endif
                KeyCode.T))
            {
                aiAutopilotEnabled = !aiAutopilotEnabled;
                string msg = $"[F1PlayerDriver] AI Autopilot toggled: {(aiAutopilotEnabled ? "ENABLED (Autonomous Stint)" : "DISABLED (Manual Driving)")}";
                Debug.Log(msg);
                LogToFile(msg);
            }

            // 2. Reset car to starting grid (Key 'R')
            if (WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.R,
#endif
                KeyCode.R))
            {
                ResetCar();
            }

            // 3. Toggle UI Dashboard (Key 'Tab' or 'H')
            if (WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.Tab,
#endif
                KeyCode.Tab) || 
                WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.H,
#endif
                KeyCode.H))
            {
                if (_dashboardDoc == null)
                    _dashboardDoc = FindAnyObjectByType<UIDocument>();

                if (_dashboardDoc != null && _dashboardDoc.rootVisualElement != null)
                {
                    bool isVisible = _dashboardDoc.rootVisualElement.style.display != DisplayStyle.None;
                    _dashboardDoc.rootVisualElement.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
                    LogToFile($"[F1PlayerDriver] Dashboard visibility toggled to: {!isVisible}");
                }
            }

            // 4. Quick Circuit Switcher (Keys '1'..'4')
            if (WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.Digit1,
#endif
                KeyCode.Alpha1)) SwitchScene("Bahrain_Occlusion");
            else if (WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.Digit2,
#endif
                KeyCode.Alpha2)) SwitchScene("Shanghai_Occlusion");
            else if (WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.Digit3,
#endif
                KeyCode.Alpha3)) SwitchScene("Suzuka_Occlusion");
            else if (WasPressedThisFrame(
#if ENABLE_INPUT_SYSTEM
                Key.Digit4,
#endif
                KeyCode.Alpha4)) SwitchScene("YasMarina_Occlusion");

            // 5. Compute inputs
            float throttle = 0f;
            float steer = 0f;
            float brake = 0f;
            bool drs = false;

            if (aiAutopilotEnabled)
            {
                // Intelligent Autonomous AI Autopilot: Multi-Ray Corridor Navigation & Corner Speed Governor
                Vector3 origin = transform.position + (transform.up * 0.6f) + (transform.forward * 2.0f);
                
                float maxRayDist = 60.0f;
                float dCenter = maxRayDist;
                float dLeft15 = maxRayDist;
                float dRight15 = maxRayDist;
                float dLeft45 = 30.0f;
                float dRight45 = 30.0f;

                RaycastHit hit;
                if (UnityEngine.Physics.Raycast(origin, transform.forward, out hit, maxRayDist))
                    dCenter = hit.distance;

                Vector3 dirL15 = Quaternion.Euler(0f, -18f, 0f) * transform.forward;
                if (UnityEngine.Physics.Raycast(origin, dirL15, out hit, maxRayDist))
                    dLeft15 = hit.distance;

                Vector3 dirR15 = Quaternion.Euler(0f, 18f, 0f) * transform.forward;
                if (UnityEngine.Physics.Raycast(origin, dirR15, out hit, maxRayDist))
                    dRight15 = hit.distance;

                Vector3 dirL45 = Quaternion.Euler(0f, -50f, 0f) * transform.forward;
                if (UnityEngine.Physics.Raycast(origin, dirL45, out hit, 30.0f))
                    dLeft45 = hit.distance;

                Vector3 dirR45 = Quaternion.Euler(0f, 50f, 0f) * transform.forward;
                if (UnityEngine.Physics.Raycast(origin, dirR45, out hit, 30.0f))
                    dRight45 = hit.distance;

                // 2. Track Centering Steering (PD control)
                float corridorError = (dLeft15 + dLeft45 * 0.6f) - (dRight15 + dRight45 * 0.6f);
                float desiredSteer = Mathf.Clamp(-corridorError * 0.07f, -1.0f, 1.0f);

                // Barrier repulsion
                if (dLeft45 < 4.5f) desiredSteer = Mathf.Max(desiredSteer, 0.45f);
                if (dRight45 < 4.5f) desiredSteer = Mathf.Min(desiredSteer, -0.45f);

                steer = desiredSteer;

                // 3. Dynamic Corner Speed Governor
                float forwardClearance = Mathf.Min(dCenter, Mathf.Min(dLeft15, dRight15));
                float targetSpeedKmh;

                if (forwardClearance > 35.0f && Mathf.Abs(steer) < 0.2f)
                {
                    // Straightaway: full throttle + DRS
                    targetSpeedKmh = 270.0f;
                    drs = true;
                }
                else if (forwardClearance > 22.0f)
                {
                    // Medium sweep / turn entry
                    targetSpeedKmh = 160.0f;
                    drs = false;
                }
                else
                {
                    // Sharp turn / chicane: heavy braking
                    targetSpeedKmh = 100.0f;
                    drs = false;
                }

                float currentSpeed = _vehicle.SpeedKmh;
                if (currentSpeed < targetSpeedKmh)
                {
                    throttle = Mathf.Clamp01((targetSpeedKmh - currentSpeed) / 25.0f);
                    brake = 0f;
                }
                else
                {
                    throttle = 0f;
                    brake = Mathf.Clamp01((currentSpeed - targetSpeedKmh) / 15.0f);
                }
            }
            else
            {
                // Manual Player Controls: W/Up, S/Down, A/Left, D/Right, Space
                bool keyW = IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.W,
#endif
                    KeyCode.W) || IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.UpArrow,
#endif
                    KeyCode.UpArrow);

                bool keyS = IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.S,
#endif
                    KeyCode.S) || IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.DownArrow,
#endif
                    KeyCode.DownArrow);

                bool keyA = IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.A,
#endif
                    KeyCode.A) || IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.LeftArrow,
#endif
                    KeyCode.LeftArrow);

                bool keyD = IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.D,
#endif
                    KeyCode.D) || IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.RightArrow,
#endif
                    KeyCode.RightArrow);

                drs = IsPressed(
#if ENABLE_INPUT_SYSTEM
                    Key.Space,
#endif
                    KeyCode.Space);

                throttle = keyW ? 1.0f : 0.0f;
                brake = keyS ? 1.0f : 0.0f;

                if (keyA) steer = -1.0f;
                else if (keyD) steer = 1.0f;

                // Log whenever input is active or periodically while moving
                if (throttle > 0f || brake > 0f || Mathf.Abs(steer) > 0f || _vehicle.SpeedKmh > 1.0f)
                {
                    if (Time.time - _lastLogTime > 0.25f)
                    {
                        _lastLogTime = Time.time;
                        LogToFile($"[ACTIVE INPUT] Throttle={throttle:F1}, Steer={steer:F1}, Brake={brake:F1}, DRS={drs}, Speed={_vehicle.SpeedKmh:F1} km/h, Pos={transform.position}");
                    }
                }
            }

            _vehicle.SetInputs(throttle, steer, brake);
            _vehicle.SetDrs(drs);
        }

        private void ResetCar()
        {
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            transform.position = spawnPosition;
            transform.rotation = spawnRotation;
            string msg = "[F1PlayerDriver] Reset car to start line.";
            Debug.Log(msg);
            LogToFile(msg);
        }

        private void SwitchScene(string sceneName)
        {
            string msg = $"[F1PlayerDriver] Loading Circuit: {sceneName}";
            Debug.Log(msg);
            LogToFile(msg);
            SceneManager.LoadScene(sceneName);
        }

        private void LogToFile(string line)
        {
            try
            {
                if (!string.IsNullOrEmpty(_logPath))
                {
                    File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
                }
            }
            catch {}
        }
    }
}
