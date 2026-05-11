using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Rigidbody))]
public class BallFeel : MonoBehaviour
{
    public bool IsHeld { get; private set; }
    public bool ScoredThisLaunch { get; private set; }
    public bool HadRimContactRecently => Time.time - _lastRimHitTime < 0.22f;

    [SerializeField] PhysicsMaterial bounceMaterial;
    [SerializeField] float airBallCheckDelay = 3.2f;
    [SerializeField] float airBallHeight = 1.6f;
    [Tooltip("If the ball center goes below this world height, it is removed from play.")]
    [SerializeField] float belowGroundWorldY = -0.35f;
    [Tooltip("Real-time wait after ground contact (or below-ground threshold) before the ball is recalled to the hand.")]
    [SerializeField, Min(0f)] float delayBeforeRecallFromGround = 0.45f;
    [Tooltip("Real time after miss VFX appears before returning the ball (avoids recall hiding the VFX).")]
    [SerializeField, Min(0f)] float delayAfterMissShotVfxBeforeBallRecall = 0.55f;

    [Header("Ground — VFX")]
    [Tooltip("Prefab spawned at ground contact (e.g. ripple). Leave empty to disable.")]
    [SerializeField] GameObject rippleVfx;
    [Tooltip("Minimum relative speed to play the ripple (avoids micro-collisions).")]
    [SerializeField, Min(0f)] float rippleMinImpactSpeed = 0.35f;
    [Tooltip("Offset along ground normal (positive = above surface, reduces z-fighting).")]
    [SerializeField] float rippleSurfaceOffset = 0.02f;
    [Tooltip("Local rotation after aligning to the normal (e.g. 90,0,0 if the prefab is oriented differently).")]
    [SerializeField] Vector3 ripplePrefabEulerOffset;

    [Header("Flight trail")]
    [SerializeField] bool motionTrailEnabled = true;
    [Tooltip("TrailRenderer material (e.g. particles / default trail). Leave empty for Unity's default trail material.")]
    [SerializeField] Material trailMaterial;
    [SerializeField, Min(0.01f)] float trailTime = 0.38f;
    [SerializeField, Min(0.001f)] float trailStartWidth = 0.14f;
    [SerializeField, Min(0.001f)] float trailEndWidth = 0.02f;
    [SerializeField, Min(0.001f)] float trailMinVertexDistance = 0.025f;
    [Header("Impacts — Audio")]
    [SerializeField] AudioClip rimClangClip;
    [SerializeField] AudioClip backboardClangClip;
    [SerializeField] AudioClip playGroundclip;
    [SerializeField, Range(0f, 1f)] float clangVolume = 0.7f;
    [Tooltip("Random pitch on each ground bounce (variation on the same clip).")]
    [SerializeField, Min(0.05f)] float groundContactPitchMin = 0.88f;
    [SerializeField, Min(0.05f)] float groundContactPitchMax = 1.12f;

    Rigidbody _rb;
    SphereCollider _col;
    BasketballShoot _shooter;
    int _groundLayer = -1;
    float _releaseTime;
    bool _airBallNotified;
    bool _groundRecallDone;
    bool _groundRecallScheduled;
    Coroutine _delayedGroundRecallRoutine;
    float _lastRimHitTime = -100f;
    Vector3 _baseScale;
    bool _baseScaleCaptured;
    TrailRenderer _trail;
    NetBallRegistrar _netBallReg;

    void CaptureBaseScaleIfNeeded()
    {
        if (_baseScaleCaptured)
            return;
        _baseScale = transform.localScale;
        _baseScaleCaptured = true;
    }

    public void RegisterShooter(BasketballShoot shooter) => _shooter = shooter;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<SphereCollider>();
        if (bounceMaterial != null && _col != null)
            _col.material = bounceMaterial;
        CaptureBaseScaleIfNeeded();
        _groundLayer = LayerMask.NameToLayer("Ground");
        SetupMotionTrail();
        _netBallReg = GetComponent<NetBallRegistrar>();
    }

    void OnEnable()
    {
        if (_col != null && _netBallReg != null)
            _netBallReg.RegisterBall(_col);
    }

    void OnDisable()
    {
        if (_col != null && _netBallReg != null)
            _netBallReg.UnregisterBall(_col);
    }

    void Update()
    {
        if (!IsHeld && !_groundRecallDone && transform.position.y < belowGroundWorldY)
            ScheduleGroundRecall();

        if (IsHeld || ScoredThisLaunch || _airBallNotified)
            return;
        if (Time.time - _releaseTime < airBallCheckDelay)
            return;
        if (transform.position.y > airBallHeight)
            return;

        _airBallNotified = true;
        GameManager.Instance?.NotifyAirBall();
    }

    public void SetHeld(bool held)
    {
        // Another component's OnEnable can call this before our Awake (cross-object order).
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        IsHeld = held;
        if (_trail != null)
        {
            if (held)
            {
                _trail.emitting = false;
                _trail.Clear();
            }
            else if (motionTrailEnabled)
                _trail.emitting = true;
        }

        if (held)
        {
            ScoredThisLaunch = false;
            _airBallNotified = false;
            _groundRecallDone = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            CancelDelayedGroundRecall();
        }
    }

    public void NotifyReleased()
    {
        _releaseTime = Time.time;
        _airBallNotified = false;
        _groundRecallDone = false;
        CancelDelayedGroundRecall();
    }

    public void NotifyScored()
    {
        ScoredThisLaunch = true;
        _airBallNotified = true;
    }

    public void SetChargeSquash(float charge01)
    {
        CaptureBaseScaleIfNeeded();
        float s = 1f - charge01 * 0.08f;
        float sy = 1f + charge01 * 0.1f;
        transform.localScale = new Vector3(_baseScale.x * s, _baseScale.y * sy, _baseScale.z * s);
    }

    public void ResetSquash()
    {
        CaptureBaseScaleIfNeeded();
        transform.localScale = _baseScale;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (IsHeld)
            return;

        if (_groundLayer >= 0 && collision.gameObject.layer == _groundLayer)
        {
            SpawnGroundRippleIfNeeded(collision);
            PlayClang(playGroundclip, randomPitch: true);
            ScheduleGroundRecall();
            return;
        }

        if (HasTagInHierarchy(collision.collider, "Rim"))
        {
            _lastRimHitTime = Time.time;
            PlayClang(rimClangClip);
            return;
        }

        if (HasTagInHierarchy(collision.collider, "Backboard"))
        {
            
            PlayClang(backboardClangClip);
        }
    }

    void ScheduleGroundRecall()
    {
        if (IsHeld || _groundRecallDone || _groundRecallScheduled)
            return;

        if (delayBeforeRecallFromGround <= 0f)
        {
            ExecuteGroundRecall();
            return;
        }

        _groundRecallScheduled = true;
        _delayedGroundRecallRoutine = StartCoroutine(DelayedGroundRecallRoutine());
    }

    IEnumerator DelayedGroundRecallRoutine()
    {
        yield return new WaitForSecondsRealtime(delayBeforeRecallFromGround);
        _delayedGroundRecallRoutine = null;
        _groundRecallScheduled = false;
        ExecuteGroundRecall();
    }

    void ExecuteGroundRecall()
    {
        if (IsHeld || _groundRecallDone)
            return;
        _groundRecallDone = true;
        bool landedAfterScore = ScoredThisLaunch;
        if (landedAfterScore)
        {
            _shooter?.RecallBallFromGround();
            GameManager.Instance?.NotifyScoredBallReturnedFromGround();
            return;
        }

        GameManager.Instance?.NotifyMissedShot(transform.position);
        if (delayAfterMissShotVfxBeforeBallRecall > 0f)
            StartCoroutine(MissedShotVfxThenRecallRoutine());
        else
            _shooter?.RecallBallFromGround();
    }

    IEnumerator MissedShotVfxThenRecallRoutine()
    {
        yield return new WaitForSecondsRealtime(delayAfterMissShotVfxBeforeBallRecall);
        if (IsHeld)
            yield break;
        _shooter?.RecallBallFromGround();
    }

    void CancelDelayedGroundRecall()
    {
        if (_delayedGroundRecallRoutine != null)
        {
            StopCoroutine(_delayedGroundRecallRoutine);
            _delayedGroundRecallRoutine = null;
        }
        _groundRecallScheduled = false;
    }

    void SpawnGroundRippleIfNeeded(Collision collision)
    {
        if (rippleVfx == null || collision.contactCount == 0)
            return;
        if (collision.relativeVelocity.magnitude < rippleMinImpactSpeed)
            return;

        var cp = collision.GetContact(0);
        Vector3 spawnPos = cp.point + new Vector3(0f, 0.05f, 0f);
        Quaternion rot = Quaternion.Euler(90f, 0f, 0f);
        Object.Instantiate(rippleVfx, spawnPos, rot);
    }

    void PlayClang(AudioClip clip, bool randomPitch = false)
    {
        if (clip == null || GameManager.Instance == null)
            return;
        var src = GameManager.Instance.GetComponent<AudioSource>();
        if (src == null)
            return;
        float savedPitch = src.pitch;
        if (randomPitch)
        {
            float lo = Mathf.Min(groundContactPitchMin, groundContactPitchMax);
            float hi = Mathf.Max(groundContactPitchMin, groundContactPitchMax);
            src.pitch = Random.Range(lo, hi);
        }
        src.PlayOneShot(clip, clangVolume);
        src.pitch = savedPitch;
    }

    static bool HasTagInHierarchy(Component c, string tag)
    {
        if (c == null || string.IsNullOrEmpty(tag))
            return false;

        Transform t = c.transform;
        while (t != null)
        {
            if (t.CompareTag(tag))
                return true;
            t = t.parent;
        }

        return false;
    }

    void SetupMotionTrail()
    {
        if (!motionTrailEnabled)
            return;

        _trail = GetComponent<TrailRenderer>();
        if (_trail == null)
            _trail = gameObject.AddComponent<TrailRenderer>();

        _trail.emitting = false;
        _trail.time = trailTime;
        float widthMul = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        _trail.startWidth = trailStartWidth * widthMul;
        _trail.endWidth = trailEndWidth * widthMul;
        _trail.minVertexDistance = trailMinVertexDistance * widthMul;
        _trail.numCornerVertices = 4;
        _trail.numCapVertices = 2;
        _trail.shadowCastingMode = ShadowCastingMode.Off;
        _trail.generateLightingData = false;
        _trail.autodestruct = false;
        if (trailMaterial != null)
            _trail.sharedMaterial = trailMaterial;

        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.52f, 0.18f), 0f),
                new GradientColorKey(new Color(1f, 0.72f, 0.35f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.75f, 0f),
                new GradientAlphaKey(0f, 1f),
            });
        _trail.colorGradient = g;
    }
}
