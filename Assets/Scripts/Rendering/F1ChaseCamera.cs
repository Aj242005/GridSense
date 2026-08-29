using System;
using UnityEngine;

namespace GridSense.Rendering
{
    /// <summary>
    /// Smooth dynamic camera controller for F1 car inspection and driving.
    /// Supports Chase Cam, Cockpit / T-Cam POV, and Trackside Broadcast modes.
    /// Toggle views at runtime with key 'C'.
    /// </summary>
    public class F1ChaseCamera : MonoBehaviour
    {
        public enum CameraMode
        {
            Chase = 0,
            Cockpit = 1,
            Trackside = 2
        }

        [Header("Target Tracking")]
        [SerializeField] private Transform targetCar;

        [Header("Camera Mode")]
        [SerializeField] private CameraMode mode = CameraMode.Chase;

        [Header("Chase Cam Tuning")]
        [SerializeField] private float chaseDistance = 5.5f;
        [SerializeField] private float chaseHeight = 1.8f;
        [SerializeField] private float chaseDamping = 8.0f;
        [SerializeField] private float rotationDamping = 10.0f;

        [Header("Cockpit / T-Cam Tuning")]
        [SerializeField] private Vector3 cockpitOffset = new Vector3(0f, 1.15f, -0.1f);

        [Header("Trackside Broadcast Tuning")]
        [SerializeField] private Vector3 tracksideStaticPos = new Vector3(12f, 3.5f, 25f);

        public CameraMode CurrentMode => mode;

        private void Awake()
        {
            if (targetCar == null)
            {
                var carGo = GameObject.Find("F1_PlayerCar");
                if (carGo != null) targetCar = carGo.transform;
            }
        }

        private void Start()
        {
            if (targetCar != null)
            {
                // Snap immediately to chase position on frame 0
                Vector3 targetForward = targetCar.forward;
                transform.position = targetCar.position - targetForward * chaseDistance + Vector3.up * chaseHeight;
                Vector3 lookTarget = targetCar.position + Vector3.up * 0.9f + targetForward * 3.0f;
                transform.rotation = Quaternion.LookRotation(lookTarget - transform.position);
            }
        }

        public void CycleCameraMode()
        {
            mode = (CameraMode)(((int)mode + 1) % 3);
            Debug.Log($"[F1ChaseCamera] Switched Camera Mode to: {mode}");
        }

        private void Update()
        {
            bool toggleCam = false;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.cKey.wasPressedThisFrame)
                toggleCam = true;
#endif
            try
            {
                if (Input.GetKeyDown(KeyCode.C)) toggleCam = true;
            }
            catch {}

            if (toggleCam)
            {
                CycleCameraMode();
            }
        }

        private void LateUpdate()
        {
            if (targetCar == null) return;

            switch (mode)
            {
                case CameraMode.Chase:
                    UpdateChaseCam();
                    break;
                case CameraMode.Cockpit:
                    UpdateCockpitCam();
                    break;
                case CameraMode.Trackside:
                    UpdateTracksideCam();
                    break;
            }
        }

        private void UpdateChaseCam()
        {
            Vector3 targetForward = targetCar.forward;
            Vector3 desiredPosition = targetCar.position - targetForward * chaseDistance + Vector3.up * chaseHeight;

            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * chaseDamping);

            Vector3 lookTarget = targetCar.position + Vector3.up * 0.9f + targetForward * 3.0f;
            Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.deltaTime * rotationDamping);
        }

        private void UpdateCockpitCam()
        {
            transform.position = targetCar.TransformPoint(cockpitOffset);
            transform.rotation = targetCar.rotation;
        }

        private void UpdateTracksideCam()
        {
            Vector3 lookTarget = targetCar.position + Vector3.up * 0.5f;
            transform.LookAt(lookTarget);
        }
    }
}
