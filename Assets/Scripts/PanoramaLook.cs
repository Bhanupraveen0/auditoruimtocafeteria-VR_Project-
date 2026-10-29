using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Controls 360 viewing direction using the Meta Quest Left Touch controller joystick.
/// - Left joystick left/right: continuous, smooth 360 horizontal rotation (yaw) without arbitrary limits.
/// - Left joystick up/down: smooth vertical tilt (pitch) clamped to configurable limits (-85 to +85)
///   to prevent flipping upside down.
/// - Diagonal input supported.
/// - Stops immediately when returning to neutral.
/// - Rotates the camera-parent transform (Camera Offset) so headset physical tracking remains natural.
/// </summary>
public class PanoramaLook : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Transform to rotate. Defaults to Camera Offset or camera parent to preserve HMD head tracking.")]
    [SerializeField] private Transform targetTransform;

    [Header("Rotation Speed (degrees / sec)")]
    [Range(10f, 180f)] public float yawSpeed = 60f;
    [Range(10f, 180f)] public float pitchSpeed = 45f;

    [Header("Pitch Limits (degrees)")]
    [Range(-89f, 0f)] public float minPitch = -85f;
    [Range(0f, 89f)] public float maxPitch = 85f;

    [Header("Joystick Settings")]
    [Range(0.05f, 0.5f)] public float deadzone = 0.15f;

    [Header("Editor Testing Controls")]
    public float mouseSensitivity = 2f;
    public bool enableEditorKeyboardControls = true;

    [Header("Dependencies")]
    [SerializeField] private RouteManager routeManager;

    private UnityEngine.XR.InputDevice leftController;
    private float currentYaw;
    private float currentPitch;
    private bool isSuspended;

    public float CurrentYaw => currentYaw;
    public float CurrentPitch => currentPitch;
    public Transform TargetTransform => targetTransform;
    public bool IsSuspended => isSuspended || (routeManager != null && !routeManager.ControlsEnabled);

    private void Awake()
    {
        ResolveTargetTransform();
        if (routeManager == null)
            routeManager = FindFirstObjectByType<RouteManager>();
    }

    private void Start()
    {
        ResolveTargetTransform();
        if (routeManager == null)
            routeManager = FindFirstObjectByType<RouteManager>();

        InitializeAnglesFromTarget();
    }

    /// <summary>
    /// Finds the camera-parent transform (Camera Offset in XR Origin).
    /// NEVER overwrites Main Camera transform directly so TrackedPoseDriver head tracking is preserved.
    /// </summary>
    public void ResolveTargetTransform()
    {
        if (targetTransform != null)
            return;

        var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null)
        {
            targetTransform = xrOrigin.CameraFloorOffsetObject.transform;
            return;
        }

        if (Camera.main != null && Camera.main.transform.parent != null)
        {
            targetTransform = Camera.main.transform.parent;
            return;
        }

        GameObject originGo = GameObject.Find("XR Origin (VR)");
        if (originGo != null)
        {
            Transform offset = originGo.transform.Find("Camera Offset");
            if (offset != null)
            {
                targetTransform = offset;
                return;
            }
            targetTransform = originGo.transform;
            return;
        }

        targetTransform = transform;
    }

    public void InitializeAnglesFromTarget()
    {
        if (targetTransform == null)
            return;

        Vector3 euler = targetTransform.localEulerAngles;
        currentYaw = euler.y;
        float pitchEuler = euler.x;
        if (pitchEuler > 180f)
            pitchEuler -= 360f;
        // In Unity, negative Euler X tilts upward; currentPitch is positive when tilted upward
        currentPitch = Mathf.Clamp(-pitchEuler, minPitch, maxPitch);
    }

    private void Update()
    {
        if (IsSuspended)
            return;

        EnsureLeftController();
        Vector2 stickInput = ReadLeftStick();

#if UNITY_EDITOR
        stickInput = ReadEditorStickFallback(stickInput);
        HandleEditorMouseLook();
#endif

        ProcessStickInput(stickInput, Time.deltaTime);
    }

    private void EnsureLeftController()
    {
        if (!leftController.isValid)
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
    }

    private Vector2 ReadLeftStick()
    {
        if (leftController.isValid && leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick))
        {
            return stick;
        }
        return Vector2.zero;
    }

    /// <summary>
    /// Updates yaw and pitch based on 2D stick input and applies rotation to targetTransform.
    /// stick.x: negative = left, positive = right
    /// stick.y: negative = down, positive = up
    /// </summary>
    public void ProcessStickInput(Vector2 stick, float deltaTime)
    {
        float x = stick.x;
        float y = stick.y;

        bool hasInput = false;

        // Horizontal rotation (Yaw)
        if (Mathf.Abs(x) >= deadzone)
        {
            hasInput = true;
            // Hold stick right: rotate view right (+yaw)
            // Hold stick left: rotate view left (-yaw)
            currentYaw += x * yawSpeed * deltaTime;
            currentYaw = Mathf.Repeat(currentYaw, 360f);
        }

        // Vertical rotation (Pitch)
        if (Mathf.Abs(y) >= deadzone)
        {
            hasInput = true;
            // Hold stick up: tilt view up (+pitch)
            // Hold stick down: tilt view down (-pitch)
            currentPitch += y * pitchSpeed * deltaTime;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }

        if (hasInput && targetTransform != null)
        {
            ApplyCurrentRotation();
        }
    }

    public void ApplyCurrentRotation()
    {
        if (targetTransform != null)
        {
            targetTransform.localRotation = Quaternion.Euler(-currentPitch, currentYaw, 0f);
        }
    }

    public void SetRotation(float yaw, float pitch)
    {
        currentYaw = Mathf.Repeat(yaw, 360f);
        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        ApplyCurrentRotation();
    }

    public void SetSuspended(bool suspended)
    {
        isSuspended = suspended;
    }

#if UNITY_EDITOR
    private Vector2 ReadEditorStickFallback(Vector2 currentStick)
    {
        if (currentStick != Vector2.zero)
            return currentStick;

        if (!enableEditorKeyboardControls)
            return Vector2.zero;

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return Vector2.zero;

        Vector2 stick = Vector2.zero;
        if (kb.leftArrowKey.isPressed)
            stick.x = -1f;
        else if (kb.rightArrowKey.isPressed)
            stick.x = 1f;

        if (kb.upArrowKey.isPressed)
            stick.y = 1f;
        else if (kb.downArrowKey.isPressed)
            stick.y = -1f;

        return stick;
    }

    private void HandleEditorMouseLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.isPressed)
            return;

        Vector2 delta = mouse.delta.ReadValue();
        if (Mathf.Abs(delta.x) > 0.01f)
        {
            currentYaw = Mathf.Repeat(currentYaw + delta.x * mouseSensitivity * 0.1f, 360f);
        }

        if (Mathf.Abs(delta.y) > 0.01f)
        {
            currentPitch += delta.y * mouseSensitivity * 0.1f;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }

        ApplyCurrentRotation();
    }
#endif
}
