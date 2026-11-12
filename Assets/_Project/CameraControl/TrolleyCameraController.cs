using IndustrialConveyor.Simulation.Movement;
using IndustrialConveyor.Simulation.Track;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IndustrialConveyor.Simulation.CameraControl
{
    /// <summary>Provides WASD camera movement and right-mouse-drag look controls.</summary>
    public sealed class TrolleyCameraController : MonoBehaviour
    {
        private const float MinimumOrthographicSize = 1.5f;
        private const float MaximumOrthographicSize = 600f;

        [SerializeField, Min(0.1f)] private float moveSpeedMetersPerSecond = 25f;
        [SerializeField, Min(0.1f)] private float fastMoveSpeedMetersPerSecond = 75f;
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Min(0.01f)] private float zoomSensitivity = 0.22f;
        [SerializeField, Range(1f, 89f)] private float maximumPitchDegrees = 85f;

        private Camera controlledCamera;
        private float yawDegrees;
        private float pitchDegrees;
        private bool isLooking;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisibility;

        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
            if (controlledCamera != null && controlledCamera.orthographic)
            {
                // Ortho depth is measured along the view axis, so geometry beside/behind the camera needs a negative near plane.
                controlledCamera.nearClipPlane = -2000f;
                controlledCamera.farClipPlane = 4000f;
            }
            var angles = transform.eulerAngles;
            yawDegrees = angles.y;
            pitchDegrees = NormalizePitch(angles.x);
        }

        private void Start()
        {
            // A track loaded from a file has its own extents, so start with the whole network in view.
            if (string.IsNullOrEmpty(TrackSource.LoadedFilePath) || controlledCamera == null || !controlledCamera.orthographic)
                return;
            var manager = FindAnyObjectByType<TrolleySimulationManager>();
            if (manager == null || manager.Network == null)
                return;

            var network = manager.Network;
            var minimum = network.BoundsMinimum;
            var maximum = network.BoundsMaximum;
            var target = new Vector3((minimum.x + maximum.x) * 0.5f, 8f, (minimum.z + maximum.z) * 0.5f);
            transform.position = target - transform.forward * 100f;
            var halfWidth = (maximum.x - minimum.x) * 0.5f;
            var halfDepth = (maximum.z - minimum.z) * 0.5f;
            var size = Mathf.Max(halfWidth / Mathf.Max(0.1f, controlledCamera.aspect), halfDepth) * 1.15f;
            controlledCamera.orthographicSize = Mathf.Clamp(size, MinimumOrthographicSize, MaximumOrthographicSize);
        }

        private void Update()
        {
            UpdateLook();
            UpdateMovement();
            UpdateZoom();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                EndLook();
        }

        private void OnDisable()
        {
            EndLook();
        }

        private void UpdateLook()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
            {
                EndLook();
                return;
            }

            if (!isLooking)
            {
                previousCursorLockMode = Cursor.lockState;
                previousCursorVisibility = Cursor.visible;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                isLooking = true;
            }

            var mouseDelta = mouse.delta.ReadValue();
            yawDegrees += mouseDelta.x * mouseSensitivity;
            pitchDegrees = Mathf.Clamp(pitchDegrees - mouseDelta.y * mouseSensitivity,
                -maximumPitchDegrees, maximumPitchDegrees);
            transform.rotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
        }

        private void UpdateMovement()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            var movement = Vector2.zero;
            if (keyboard.wKey.isPressed)
                movement.y += 1f;
            if (keyboard.sKey.isPressed)
                movement.y -= 1f;
            if (keyboard.dKey.isPressed)
                movement.x += 1f;
            if (keyboard.aKey.isPressed)
                movement.x -= 1f;
            if (movement.sqrMagnitude < 0.001f)
                return;

            var forward = transform.forward;
            forward.Normalize();
            var right = transform.right;
            right.y = 0f;
            right.Normalize();
            var direction = (forward * movement.y + right * movement.x).normalized;
            var speed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                ? fastMoveSpeedMetersPerSecond
                : moveSpeedMetersPerSecond;
            transform.position += direction * speed * Time.unscaledDeltaTime;
        }

        private void UpdateZoom()
        {
            var mouse = Mouse.current;
            if (controlledCamera == null || !controlledCamera.orthographic || mouse == null)
                return;

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f)
                return;

            var notches = Mathf.Clamp(scroll / 120f, -1f, 1f);
            controlledCamera.orthographicSize = Mathf.Clamp(
                controlledCamera.orthographicSize * Mathf.Exp(-notches * zoomSensitivity),
                MinimumOrthographicSize,
                MaximumOrthographicSize);
        }

        private void EndLook()
        {
            if (!isLooking)
                return;

            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisibility;
            isLooking = false;
        }

        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
