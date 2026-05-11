using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FpsLookAndMove : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] Transform lookPivot;
    [Tooltip("Pivot for arms/hands/ball. Moves within the limit first; overflow goes to the camera.")]
    [SerializeField] Transform armsPivot;
    [SerializeField] float lookSensitivity = 0.12f;
    [Tooltip("Sensitivity for local hand movement (local units per mouse pixel).")]
    [SerializeField] float handSensitivity = 0.0035f;
    [Tooltip("Arm offset limits: X = left only (no limit to the right), Y = up only (not downward).")]
    [SerializeField] Vector2 maxArmsOffset = new Vector2(0.18f, 0.12f);
    [Tooltip("Return speed toward center when there is no input (0 = no return).")]
    [SerializeField] float armsReturnSpeed = 0f;
    [SerializeField] float pitchMin = -88f;
    [SerializeField] float pitchMax = 88f;
    [SerializeField] float moveSpeed = 4.2f;
    [SerializeField] float sprintMultiplier = 1.55f;
    [SerializeField] float jumpHeight = 1.2f; // default 1.2m
    [SerializeField] float gravity = -18f;

    [Tooltip("After each warp to a launch point, yaw + pitch aim at this target (e.g. hoop).")]
    [SerializeField] Transform lookAtTarget;

    [Header("Launch spots")]
    [Tooltip("Order = progress toward the hoop. Index 0 = starting position.")]
    [SerializeField] Transform[] launchPoints;
    [Tooltip("After a basket: delay (s) once the ball hits the ground and is recalled before warping to the next spot.")]
    [SerializeField, Min(0f)] float delayAfterGroundBeforeWarp = 0.12f;
    [Tooltip("After warping to the next spot: delay (s) before you can shoot again.")]
    [SerializeField, Min(0f)] float delayBeforeNextShotAfterWarp = 0.45f;
    [Tooltip("Shoot tuning / per-spot multipliers. If empty: resolved on this GameObject or its children.")]
    [SerializeField] BasketballShoot basketballShoot;

    CharacterController _controller;
    InputActionMap _map;
    InputAction _move;
    InputAction _look;
    InputAction _jump;
    InputAction _sprint;

    float _pitch;
    float _yVelocity;
    Vector2 _armsOffset;
    Vector3 _armsRestPosition;
    int _launchIndex;
    bool _pendingLaunchAdvanceAfterScore;
    Coroutine _postScoreRoutine;

    /// <summary>Current launch spot index (matches the order of <see cref="launchPoints"/>). Used for shot multipliers.</summary>
    public int CurrentLaunchIndex => _launchIndex;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (lookPivot == null && Camera.main != null)
            lookPivot = Camera.main.transform.parent;
        if (armsPivot != null)
            _armsRestPosition = armsPivot.localPosition;
        if (basketballShoot == null)
            basketballShoot = GetComponent<BasketballShoot>();
        if (basketballShoot == null)
            basketballShoot = GetComponentInChildren<BasketballShoot>(true);
        if (basketballShoot == null)
            basketballShoot = GetComponentInParent<BasketballShoot>();
    }

    void OnEnable()
    {
        if (inputActions == null)
            return;
        _map = inputActions.FindActionMap("Player");
        _move = _map.FindAction("Move");
        _look = _map.FindAction("Look");
        _jump = _map.FindAction("Jump");
        _sprint = _map.FindAction("Sprint");
        _map.Enable();
        TrySubscribeBasket();
    }

    void OnDisable()
    {
        UnsubscribeBasket();
        _map?.Disable();
    }

    void Start()
    {
        if (GameManager.Instance == null || GameManager.Instance.HasRoundStarted)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        TrySnapToInitialLaunch();
        // With no launch points: camera pivot may stay tilted as placed in the scene.
        if (launchPoints == null || launchPoints.Length == 0)
        {
            _pitch = 0f;
            if (lookPivot != null)
                lookPivot.localRotation = Quaternion.Euler(0f, 0f, 0f);
        }
        TrySubscribeBasket();
    }

    void Update()
    {
        bool roundLive = !PauseController.IsPaused
            && GameManager.Instance != null
            && GameManager.Instance.HasRoundStarted
            && !GameManager.Instance.IsGameOver;
        if (roundLive)
            ReportDirectiveGameplayInputIfAny();

        if (PauseController.IsPaused
            || (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            || (GameManager.Instance != null && !GameManager.Instance.HasRoundStarted))
            return;

        if (lookPivot != null && _look != null)
        {
            Vector2 delta = _look.ReadValue<Vector2>();
            float yawOverflow = ConsumeYaw(delta.x);
            float pitchOverflow = ConsumeAxis(delta.y, ref _armsOffset.y, 0f, maxArmsOffset.y);

            float yaw = yawOverflow * lookSensitivity;
            float pitchDelta = pitchOverflow * lookSensitivity;

            if (Mathf.Abs(yaw) > 0f)
                transform.Rotate(0f, yaw, 0f);

            _pitch -= pitchDelta;
            _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
            lookPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            if (armsReturnSpeed > 0f && delta.sqrMagnitude < 0.0001f)
                _armsOffset = Vector2.MoveTowards(_armsOffset, Vector2.zero, armsReturnSpeed * Time.deltaTime);
            _armsOffset.x = Mathf.Clamp(_armsOffset.x, -maxArmsOffset.x, 0f);
            _armsOffset.y = Mathf.Max(0f, _armsOffset.y);

            if (armsPivot != null)
                armsPivot.localPosition = _armsRestPosition + new Vector3(_armsOffset.x, _armsOffset.y, 0f);
        }

        if (_move == null)
            return;

        Vector2 m = _move.ReadValue<Vector2>();
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        float sp = moveSpeed * (_sprint != null && _sprint.IsPressed() ? sprintMultiplier : 1f);
        Vector3 wish = (forward * m.y + right * m.x) * sp;

        if (_controller.isGrounded && _yVelocity < 0f)
            _yVelocity = -2f;

        if (_controller.isGrounded && _jump != null && _jump.WasPressedThisFrame())
            _yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _yVelocity += gravity * Time.deltaTime;
        wish.y = _yVelocity;
        _controller.Move(wish * Time.deltaTime);
    }

    void ReportDirectiveGameplayInputIfAny()
    {
        const float moveEpsSq = 0.0004f;
        const float lookEpsSq = 0.01f;
        if (_move != null && _move.ReadValue<Vector2>().sqrMagnitude > moveEpsSq)
            GameManager.Instance.NotifyDirectiveGameplayInput();
        if (_look != null && _look.ReadValue<Vector2>().sqrMagnitude > lookEpsSq)
            GameManager.Instance.NotifyDirectiveGameplayInput();
        if (_jump != null && _jump.WasPressedThisFrame())
            GameManager.Instance.NotifyDirectiveGameplayInput();
        if (_sprint != null && _sprint.IsPressed())
            GameManager.Instance.NotifyDirectiveGameplayInput();
    }

    float ConsumeYaw(float mouseDeltaX)
    {
        if (mouseDeltaX <= 0f)
            return ConsumeAxis(mouseDeltaX, ref _armsOffset.x, -maxArmsOffset.x, 0f);

        float requested = mouseDeltaX * handSensitivity;
        if (_armsOffset.x >= -1e-6f)
            return mouseDeltaX;

        float roomToCenter = -_armsOffset.x;
        float used = Mathf.Min(requested, roomToCenter);
        _armsOffset.x += used;
        float remainder = requested - used;
        return remainder / Mathf.Max(handSensitivity, 1e-5f);
    }

    float ConsumeAxis(float mouseDelta, ref float armsOffset, float minLimit, float maxLimit)
    {
        if (maxLimit <= minLimit)
            return mouseDelta;

        float requested = mouseDelta * handSensitivity;
        float target = armsOffset + requested;
        float clamped = Mathf.Clamp(target, minLimit, maxLimit);
        float overflowLocal = target - clamped;
        armsOffset = clamped;
        return overflowLocal / Mathf.Max(handSensitivity, 1e-5f);
    }

    void TrySubscribeBasket()
    {
        if (GameManager.Instance == null)
            return;
        GameManager.Instance.OnBasket -= OnBasketScored;
        GameManager.Instance.OnBasket += OnBasketScored;
        GameManager.Instance.OnScoredBallGrounded -= OnScoredBallGrounded;
        GameManager.Instance.OnScoredBallGrounded += OnScoredBallGrounded;
    }

    void UnsubscribeBasket()
    {
        if (GameManager.Instance == null)
            return;
        GameManager.Instance.OnBasket -= OnBasketScored;
        GameManager.Instance.OnScoredBallGrounded -= OnScoredBallGrounded;
    }

    void OnBasketScored(bool _)
    {
        if (launchPoints == null || launchPoints.Length == 0)
            return;
        _pendingLaunchAdvanceAfterScore = true;
    }

    void OnScoredBallGrounded()
    {
        if (!_pendingLaunchAdvanceAfterScore || launchPoints == null || launchPoints.Length == 0)
            return;
        if (_postScoreRoutine != null)
            StopCoroutine(_postScoreRoutine);
        _postScoreRoutine = StartCoroutine(PostScoreAdvanceRoutine());
    }

    IEnumerator PostScoreAdvanceRoutine()
    {
        if (delayAfterGroundBeforeWarp > 0f)
            yield return new WaitForSecondsRealtime(delayAfterGroundBeforeWarp);

        _launchIndex = (_launchIndex + 1) % launchPoints.Length;
        var t = launchPoints[_launchIndex];
        if (t != null)
            WarpToTransform(t, resetVerticalLook: false);

        basketballShoot?.SetLaunchPosition(_launchIndex);
        if (delayBeforeNextShotAfterWarp > 0f)
            basketballShoot?.SetShootLockedForSeconds(delayBeforeNextShotAfterWarp);

        _pendingLaunchAdvanceAfterScore = false;
        _postScoreRoutine = null;
    }

    void TrySnapToInitialLaunch()
    {
        if (launchPoints == null || launchPoints.Length == 0)
            return;
        _launchIndex = 0;
        var t = launchPoints[0];
        if (t != null)
            WarpToTransform(t, resetVerticalLook: true);
        basketballShoot?.SetLaunchPosition(_launchIndex);
    }

    void WarpToTransform(Transform launch, bool resetVerticalLook)
    {
        if (launch == null || _controller == null)
            return;

        _controller.enabled = false;
        transform.position = launch.position;

        if (lookAtTarget != null)
            ApplyLookAtTarget(launch);
        else
        {
            // Only use the launch transform's yaw: eulerAngles.x on an empty
            // can give a bogus pitch (editor defaults, hierarchy, quaternion ambiguity).
            float yaw = launch.rotation.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (resetVerticalLook)
                _pitch = 0f;
            _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
            if (lookPivot != null)
                lookPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        _armsOffset = Vector2.zero;
        if (armsPivot != null)
            armsPivot.localPosition = _armsRestPosition;

        _yVelocity = -2f;
        _controller.enabled = true;
    }

    /// <summary>Orients body yaw toward the target on the horizontal plane, then look-pivot pitch.</summary>
    void ApplyLookAtTarget(Transform launchFallback)
    {
        Vector3 origin = transform.position;
        Vector3 toTarget = lookAtTarget.position - origin;
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        if (flat.sqrMagnitude > 1e-8f)
            transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
        else if (launchFallback != null)
            transform.rotation = Quaternion.Euler(0f, launchFallback.rotation.eulerAngles.y, 0f);

        if (lookPivot == null)
            return;

        Vector3 eyePos = lookPivot.position;
        Vector3 aimWorld = lookAtTarget.position - eyePos;
        if (aimWorld.sqrMagnitude < 1e-8f)
        {
            _pitch = 0f;
            lookPivot.localRotation = Quaternion.identity;
            return;
        }

        Vector3 localAim = transform.InverseTransformDirection(aimWorld.normalized);
        float horiz = Mathf.Sqrt(localAim.x * localAim.x + localAim.z * localAim.z);
        _pitch = -Mathf.Atan2(localAim.y, horiz) * Mathf.Rad2Deg;
        _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
        lookPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}
