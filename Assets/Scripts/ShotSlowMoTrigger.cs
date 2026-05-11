using System.Collections;
using UnityEngine;

/// <summary>
/// Place on a GameObject with a <b>trigger</b> collider wrapping the rim arc and/or backboard approach.
/// When the basketball enters the volume, <see cref="GameManager.RequestHitStop"/> runs for a short slow-mo.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShotSlowMoTrigger : MonoBehaviour
{
    public enum Preset
    {
        /// <summary>Uses <see cref="customRealtimeSeconds"/> and <see cref="customTimeScale"/>.</summary>
        Custom,
        /// <summary>Tuned default for “almost at the rim” volumes.</summary>
        RimNearMiss,
        /// <summary>Tuned default for backboard approach / glass line volumes.</summary>
        BackboardPass,
    }

    [SerializeField] Preset preset = Preset.Custom;
    [SerializeField, Min(0.02f)] float customRealtimeSeconds = 0.22f;
    [SerializeField, Range(0.05f, 1f)] float customTimeScale = 0.28f;
    [Tooltip("Minimum real time between retriggers while the same ball can bounce in/out of the volume.")]
    [SerializeField, Min(0f)] float retriggerCooldownSeconds = 0.4f;

    [Header("Rim near miss — OOPS VFX")]
    [Tooltip("Shown when this volume fires with preset Rim Near Miss (e.g. root of your OOPS particles). Leave empty to skip.")]
    [SerializeField] GameObject oopsVfx;
    [SerializeField, Min(0f)] float oopsVfxVisibleSeconds = 1.25f;

    const float RimDefaultRealtime = 0.22f;
    const float RimDefaultScale = 0.28f;
    const float BoardDefaultRealtime = 0.16f;
    const float BoardDefaultScale = 0.32f;

    Collider _collider;
    float _lastTriggerRealtime = -1000f;
    Coroutine _oopsVfxRoutine;

    void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null)
            c.isTrigger = true;
    }

    void OnValidate()
    {
        var c = GetComponent<Collider>();
        if (c != null && !c.isTrigger)
            c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!enabled || _collider == null || !_collider.enabled)
            return;

        var feel = other.attachedRigidbody != null
            ? other.attachedRigidbody.GetComponent<BallFeel>()
            : other.GetComponentInParent<BallFeel>();
        if (feel == null || feel.IsHeld || feel.ScoredThisLaunch)
            return;

        var gm = GameManager.Instance;
        if (gm == null || gm.IsGameOver || !gm.HasRoundStarted || PauseController.IsPaused)
            return;

        if (Time.realtimeSinceStartup - _lastTriggerRealtime < retriggerCooldownSeconds)
            return;

        _lastTriggerRealtime = Time.realtimeSinceStartup;

        float tReal;
        float tScale;
        switch (preset)
        {
            case Preset.RimNearMiss:
                tReal = RimDefaultRealtime;
                tScale = RimDefaultScale;
                break;
            case Preset.BackboardPass:
                tReal = BoardDefaultRealtime;
                tScale = BoardDefaultScale;
                break;
            default:
                tReal = customRealtimeSeconds;
                tScale = customTimeScale;
                break;
        }

        gm.RequestHitStop(tReal, tScale);

        if (preset == Preset.RimNearMiss)
            PlayOopsVfxPulse();
    }

    void OnDisable()
    {
        if (_oopsVfxRoutine != null)
        {
            StopCoroutine(_oopsVfxRoutine);
            _oopsVfxRoutine = null;
        }
    }

    void PlayOopsVfxPulse()
    {
        if (oopsVfx == null)
            return;
        if (_oopsVfxRoutine != null)
            StopCoroutine(_oopsVfxRoutine);
        _oopsVfxRoutine = StartCoroutine(OopsVfxRoutine());
    }

    IEnumerator OopsVfxRoutine()
    {
        oopsVfx.SetActive(true);
        if (oopsVfxVisibleSeconds > 0f)
            yield return new WaitForSecondsRealtime(oopsVfxVisibleSeconds);
        if (oopsVfx != null)
            oopsVfx.SetActive(false);
        _oopsVfxRoutine = null;
    }
}
