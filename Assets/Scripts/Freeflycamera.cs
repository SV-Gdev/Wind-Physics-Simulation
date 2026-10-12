using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class FreeFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 20f;
    public float fastMultiplier = 3f;
    public float slowMultiplier = 0.25f;

    [Header("Speed Adjust (Mouse Wheel)")]
    public float scrollStep = 2f;
    public float minSpeed = 1f;
    public float maxSpeed = 300f;

    [Header("Look")]
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    public float lookSensitivity = 0.15f;
    public bool invertY = false;
    [Tooltip("If true, hold right mouse button to look/move (like the Scene view).")]
    public bool requireRightClick = true;

    [Header("Smoothing")]
    [Range(0f, 30f)] public float movementSmoothing = 10f;

    [Header("Wind - Source")]
    [Tooltip("Tick to keep the camera completely STABLE: wind will not tilt, shake or push it.")]
    public bool stableCamera = false;
    [Tooltip("Master switch for camera wind effects (also used to find the WindZone).")]
    public bool enableWind = true;
    [Tooltip("Leave empty to auto-find the WindZone in the scene.")]
    public WindZone windZone;
    [Tooltip("Used only if no WindZone exists in the scene (handy for testing).")]
    public float manualWindStrength = 0f;
    public Vector3 manualWindDirection = Vector3.right;

    [Header("Wind - Effect")]
    [Tooltip("Wind strength (WindZone 'Wind Main' incl. pulse/turbulence) at which effects reach 100%.")]
    public float strengthForMaxEffect = 5f;
    [Tooltip("Max sideways tilt (roll) in degrees at full wind. Camera tilts AWAY from the wind direction.")]
    public float maxTiltAngle = 15f;
    [Tooltip("Max forward/back pitch change in degrees (headwind lifts the nose, tailwind dips it).")]
    public float maxPitchAngle = 6f;
    [Tooltip("Random shake/wobble in degrees at full wind.")]
    public float shakeAmount = 2.5f;
    public float shakeSpeed = 2.5f;
    [Tooltip("Units/second the camera drifts downwind at full wind. 0 = no push.")]
    public float windPushSpeed = 4f;
    [Tooltip("How quickly the camera reacts to wind changes.")]
    public float windResponsiveness = 4f;

    [Header("Debug (read only)")]
    [SerializeField] float currentWindStrength;
    [SerializeField] float currentWindEffect;

    float yaw;
    float pitch;
    Vector3 currentVelocity;

    float smoothedRoll;
    float smoothedPitchOffset;
    float smoothedEffect;

    void Start()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x;

        if (enableWind && windZone == null)
            windZone = FindFirstObjectByType<WindZone>();
    }

    void OnDisable()
    {
        SetCursorLocked(false);
    }

    void Update()
    {
        bool active = !requireRightClick || IsRightMouseHeld();
        SetCursorLocked(active && requireRightClick);

        if (active)
        {
            HandleLook();
            HandleSpeedScroll();
        }

        HandleMovement(active);
        ApplyWindAndRotation();
    }

    void HandleLook()
    {
        Vector2 delta = GetMouseDelta();
        yaw += delta.x * lookSensitivity;
        pitch += (invertY ? delta.y : -delta.y) * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);
    }

    void HandleMovement(bool active)
    {
        Vector3 input = active ? GetMoveInput() : Vector3.zero;

        float speed = moveSpeed;
        if (active && IsFastHeld()) speed *= fastMultiplier;
        if (active && IsSlowHeld()) speed *= slowMultiplier;

        Vector3 targetVelocity =
            (transform.forward * input.z +
             transform.right * input.x +
             Vector3.up * input.y) * speed;

        if (movementSmoothing <= 0f)
            currentVelocity = targetVelocity;
        else
            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity,
                1f - Mathf.Exp(-movementSmoothing * Time.unscaledDeltaTime));

        transform.position += currentVelocity * Time.unscaledDeltaTime;
    }

    void HandleSpeedScroll()
    {
        float scroll = GetScroll();
        if (Mathf.Abs(scroll) > 0.01f)
            moveSpeed = Mathf.Clamp(moveSpeed + Mathf.Sign(scroll) * scrollStep, minSpeed, maxSpeed);
    }

    void ApplyWindAndRotation()
    {
        float dt = Time.unscaledDeltaTime;
        float t = Time.time;

        float strength = 0f;
        Vector3 dir = Vector3.zero;

        if (enableWind && !stableCamera)
        {
            if (windZone != null && windZone.gameObject.activeInHierarchy)
            {
                float pulse = 1f + windZone.windPulseMagnitude *
                              Mathf.Sin(t * windZone.windPulseFrequency * Mathf.PI * 2f);
                float turbulence = (Mathf.PerlinNoise(t * 0.7f, 3.1f) - 0.5f) * 2f *
                                   windZone.windTurbulence;
                strength = Mathf.Max(0f, windZone.windMain * pulse + turbulence);

                if (windZone.mode == WindZoneMode.Directional)
                {
                    dir = windZone.transform.forward;
                }
                else
                {
                    Vector3 away = transform.position - windZone.transform.position;
                    float d = away.magnitude;
                    if (d > windZone.radius || d < 0.001f)
                    {
                        strength = 0f;
                    }
                    else
                    {
                        strength *= 1f - d / windZone.radius;
                        dir = away / d;
                    }
                }
            }
            else if (windZone == null)
            {
                strength = Mathf.Max(0f, manualWindStrength);
                dir = manualWindDirection.sqrMagnitude > 0.0001f
                    ? manualWindDirection.normalized
                    : Vector3.zero;
            }
        }

        currentWindStrength = strength;

        float rawEffect = Mathf.Clamp01(strength / Mathf.Max(0.0001f, strengthForMaxEffect));
        float k = 1f - Mathf.Exp(-windResponsiveness * dt);
        smoothedEffect = Mathf.Lerp(smoothedEffect, rawEffect, k);
        currentWindEffect = smoothedEffect;

        float effect = smoothedEffect;
        float strong = effect * effect;

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 flatRight = yawRot * Vector3.right;
        Vector3 flatForward = yawRot * Vector3.forward;

        float sideAmount = Vector3.Dot(dir, flatRight);
        float frontAmount = Vector3.Dot(dir, flatForward);

        float targetRoll = -sideAmount * effect * maxTiltAngle;
        float targetPitchOffset = frontAmount * effect * maxPitchAngle;

        smoothedRoll = Mathf.Lerp(smoothedRoll, targetRoll, k);
        smoothedPitchOffset = Mathf.Lerp(smoothedPitchOffset, targetPitchOffset, k);

        float st = t * shakeSpeed;
        float shakeRoll = (Mathf.PerlinNoise(st, 10.3f) - 0.5f) * 2f * shakeAmount * strong;
        float shakePitch = (Mathf.PerlinNoise(st, 20.7f) - 0.5f) * 2f * shakeAmount * 0.6f * strong;
        float shakeYaw = (Mathf.PerlinNoise(st, 30.1f) - 0.5f) * 2f * shakeAmount * 0.6f * strong;

        if (windPushSpeed > 0f && dir != Vector3.zero)
            transform.position += dir * (strong * windPushSpeed * dt);

        transform.rotation = Quaternion.Euler(
            pitch + smoothedPitchOffset + shakePitch,
            yaw + shakeYaw,
            smoothedRoll + shakeRoll);
    }

    public void SetStableCamera(bool value)
    {
        stableCamera = value;
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

#if ENABLE_INPUT_SYSTEM
    static bool IsRightMouseHeld() =>
        Mouse.current != null && Mouse.current.rightButton.isPressed;

    static Vector2 GetMouseDelta() =>
        Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

    static float GetScroll() =>
        Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

    static bool IsFastHeld() =>
        Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

    static bool IsSlowHeld() =>
        Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;

    static Vector3 GetMoveInput()
    {
        var k = Keyboard.current;
        if (k == null) return Vector3.zero;

        Vector3 v = Vector3.zero;
        if (k.wKey.isPressed) v.z += 1f;
        if (k.sKey.isPressed) v.z -= 1f;
        if (k.dKey.isPressed) v.x += 1f;
        if (k.aKey.isPressed) v.x -= 1f;
        if (k.eKey.isPressed) v.y += 1f;
        if (k.qKey.isPressed) v.y -= 1f;
        return v.normalized;
    }
#else
    static bool IsRightMouseHeld() => Input.GetMouseButton(1);

    static Vector2 GetMouseDelta() =>
        new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;

    static float GetScroll() => Input.mouseScrollDelta.y;

    static bool IsFastHeld() => Input.GetKey(KeyCode.LeftShift);

    static bool IsSlowHeld() => Input.GetKey(KeyCode.LeftControl);

    static Vector3 GetMoveInput()
    {
        Vector3 v = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) v.z += 1f;
        if (Input.GetKey(KeyCode.S)) v.z -= 1f;
        if (Input.GetKey(KeyCode.D)) v.x += 1f;
        if (Input.GetKey(KeyCode.A)) v.x -= 1f;
        if (Input.GetKey(KeyCode.E)) v.y += 1f;
        if (Input.GetKey(KeyCode.Q)) v.y -= 1f;
        return v.normalized;
    }
#endif
}