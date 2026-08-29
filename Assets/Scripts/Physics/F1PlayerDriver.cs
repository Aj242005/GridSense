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
        private float _airborneSince = -1f;
        private bool _hasSnappedOnStart = false;

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
            catch (Exception ex)
            {
                Debug.LogWarning($"[F1PlayerDriver] Could not init log file: {ex.Message}");
            }
        }

        private void Start()
        {
            _dashboardDoc = FindAnyObjectByType<UIDocument>();

            // Snap to track surface on first load so the car doesn't spawn in mid-air
            // or below the track.  Also fixes the backward-facing rotation if detected.
            SnapToTrackSurface();
            _hasSnappedOnStart = true;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            // Check if any wheel is grounded
            bool grounded = false;
            foreach (var wheel in GetComponentsInChildren<WheelCollider>())
                grounded |= wheel.isGrounded;

            if (grounded)
                _airborneSince = -1f;
            else if (_airborneSince < 0f)
                _airborneSince = Time.time;

            // Abyss detection: raycast downward to find ground
            bool hasGroundBelow = UnityEngine.Physics.Raycast(
                transform.position, Vector3.down, 20f);

            // Reset conditions:
            // 1. No ground within 20m below the car (drove off edge / gap in colliders)
            // 2. Airborne for >1.5s and falling fast (launched off track or flipped)
            if (!hasGroundBelow ||
                (_airborneSince > 0f && Time.time - _airborneSince > 1.5f && _rb.linearVelocity.y < -5f))
            {
                ResetCar();
            }
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
            // No try/catch — if legacy input throws, we need to know about it.
            // activeInputHandler: 2 means both systems are enabled.
            if (Input.GetKey(legacyKey)) return true;
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
            if (Input.GetKeyDown(legacyKey)) return true;
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
                ComputeAutopilotInputs(out throttle, out steer, out brake, out drs);
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

        /// <summary>
        /// Improved autopilot with longer ray distances, lower steering gain,
        /// and downward edge detection to prevent driving off track edges.
        /// </summary>
        private void ComputeAutopilotInputs(out float throttle, out float steer, out float brake, out bool drs)
        {
            Vector3 origin = transform.position + (transform.up * 0.6f) + (transform.forward * 2.0f);
            
            float maxRayDist = 80.0f;
            float dCenter = maxRayDist;
            float dLeft15 = maxRayDist;
            float dRight15 = maxRayDist;
            float dLeft45 = 50.0f;
            float dRight45 = 50.0f;

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
            if (UnityEngine.Physics.Raycast(origin, dirL45, out hit, 50.0f))
                dLeft45 = hit.distance;

            Vector3 dirR45 = Quaternion.Euler(0f, 50f, 0f) * transform.forward;
            if (UnityEngine.Physics.Raycast(origin, dirR45, out hit, 50.0f))
                dRight45 = hit.distance;

            // Downward ground probe: detect track drop-offs / edges ahead
            Vector3 groundProbePos = origin + transform.forward * 12.0f;
            bool hasGroundAhead = UnityEngine.Physics.Raycast(groundProbePos, Vector3.down, 15.0f);

            // Track Centering Steering (reduced gain to prevent overcorrection)
            float corridorError = (dLeft15 + dLeft45 * 0.6f) - (dRight15 + dRight45 * 0.6f);
            float desiredSteer = Mathf.Clamp(-corridorError * 0.04f, -1.0f, 1.0f);

            // Barrier repulsion
            if (dLeft45 < 5.0f) desiredSteer = Mathf.Max(desiredSteer, 0.4f);
            if (dRight45 < 5.0f) desiredSteer = Mathf.Min(desiredSteer, -0.4f);

            steer = desiredSteer;

            // Dynamic Corner Speed Governor
            float forwardClearance = Mathf.Min(dCenter, Mathf.Min(dLeft15, dRight15));
            float targetSpeedKmh;

            // If no ground ahead, emergency brake
            if (!hasGroundAhead)
            {
                targetSpeedKmh = 30.0f;
                drs = false;
            }
            else if (forwardClearance > 50.0f && Mathf.Abs(steer) < 0.15f)
            {
                // Straightaway: full throttle + DRS — NO artificial speed cap
                targetSpeedKmh = autopilotTargetSpeedKmh;
                drs = true;
            }
            else if (forwardClearance > 30.0f)
            {
                // Medium sweep / turn entry
                targetSpeedKmh = 120.0f;
                drs = false;
            }
            else
            {
                // Sharp turn / chicane
                targetSpeedKmh = 70.0f;
                drs = false;
            }

            float currentSpeed = _vehicle.SpeedKmh;
            if (currentSpeed < targetSpeedKmh)
            {
                throttle = Mathf.Clamp01((targetSpeedKmh - currentSpeed) / 30.0f);
                brake = 0f;
            }
            else
            {
                throttle = 0f;
                brake = Mathf.Clamp01((currentSpeed - targetSpeedKmh) / 20.0f);
            }
        }

        /// <summary>
        /// Snaps the car to the actual track surface by raycasting downward.
        /// Prevents spawning in mid-air or below the track.
        /// Also validates that the car faces roughly forward along the track
        /// by checking which direction has more open space ahead.
        /// </summary>
        private void SnapToTrackSurface()
        {
            if (_rb == null) return;

            // Raycast down from well above the spawn position to find the actual track surface
            Vector3 probeOrigin = spawnPosition + Vector3.up * 50f;
            if (UnityEngine.Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, 200f))
            {
                Vector3 surfacePos = hit.point + Vector3.up * 0.5f;
                _rb.position = surfacePos;
                spawnPosition = surfacePos; // update so future resets use corrected position

                // Check if car is facing backward by probing forward clearance
                // in both the current facing direction and 180° opposite
                Vector3 fwd = spawnRotation * Vector3.forward;
                float fwdClearance = 0f;
                float revClearance = 0f;

                Vector3 probePos = surfacePos + Vector3.up * 0.5f;
                if (UnityEngine.Physics.Raycast(probePos, fwd, out RaycastHit fwdHit, 100f))
                    fwdClearance = fwdHit.distance;
                else
                    fwdClearance = 100f;

                if (UnityEngine.Physics.Raycast(probePos, -fwd, out RaycastHit revHit, 100f))
                    revClearance = revHit.distance;
                else
                    revClearance = 100f;

                // If the reverse direction has significantly more clearance, the car is facing backward
                if (revClearance > fwdClearance * 1.5f)
                {
                    spawnRotation = Quaternion.LookRotation(-fwd, Vector3.up);
                    Debug.Log($"[F1PlayerDriver] Detected backward-facing car. Flipped rotation. fwdClear={fwdClearance:F1}m, revClear={revClearance:F1}m");
                }

                _rb.rotation = spawnRotation;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;

                Debug.Log($"[F1PlayerDriver] Snapped to track surface at {surfacePos} (surface hit at {hit.point})");
            }
            else
            {
                Debug.LogWarning("[F1PlayerDriver] SnapToTrackSurface: No ground found below spawn position!");
                _rb.position = spawnPosition;
                _rb.rotation = spawnRotation;
            }
        }

        private void ResetCar()
        {
            _airborneSince = -1f;
            SnapToTrackSurface();
            string msg = "[F1PlayerDriver] Reset car to track surface.";
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
            catch (Exception ex)
            {
                Debug.LogWarning($"[F1PlayerDriver] Log write failed: {ex.Message}");
            }
        }
    }
}
