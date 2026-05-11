using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

[System.Serializable]
public struct LaunchPositionTuning
{
    [Tooltip("Indice d’emplacement de tir (0, 1, 2…). Doit correspondre à « launch position » sur le composant.")]
    public int launchPosition;
    [Tooltip("Multiplie la vitesse vers l’avant pour cet emplacement.")]
    [Min(0.01f)] public float launchDistanceMultiplier;
    [Tooltip("Multiplie le boost vertical pour cet emplacement.")]
    [Min(0f)] public float launchHeightMultiplier;
}

public class BasketballShoot : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] Rigidbody ball;
    [SerializeField] Transform ballHoldPoint;
    [FormerlySerializedAs("oscillationSpeed")]
    [Tooltip("Rapidité d'oscillation du slider de puissance (-1↔+1). Plus la valeur est grande, plus le curseur va vite.")]
    [SerializeField, Range(0.05f, 10f)]
    float sliderOscillationSpeed = 1.25f;
    [SerializeField] float minLaunchImpulse = 3.5f;
    [SerializeField] float maxLaunchImpulse = 10f;
    [Tooltip("Hauteur verticale ajoutée au tir (m/s) lorsque la courbe ci-dessous vaut 1.")]
    [FormerlySerializedAs("arcUpFromCharge")]
    [SerializeField] float maxLaunchHeight = 5f;
    [Tooltip("Vitesse verticale minimale ajoutée au tir, même quand le slider est à 0.")]
    [SerializeField] float minLaunchHeight = 0.4f;
    [Tooltip("Courbe qui mappe la charge normalisée (0–1, dérivée du slider -1…+1) sur la hauteur de tir (0–1). Linéaire par défaut.")]
    [SerializeField] AnimationCurve heightByCharge = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Si vrai : le slider contrôle à la fois la force (impulsion) et la hauteur du tir. Si faux : la hauteur est fixe (voir charge fixe ci-dessous), seule la force suit le slider.")]
    [SerializeField] bool sliderControlsLaunchHeight = true;
    [Tooltip("Valeur 0–1 utilisée pour la hauteur lorsque « slider contrôle la hauteur » est désactivé (passée dans heightByCharge comme une charge fictive).")]
    [SerializeField, Range(0f, 1f)] float fixedHeightCharge = 0.5f;

    [Header("Multiplicateurs par emplacement de tir")]
    [Tooltip("Si renseigné, l’index de tir vient toujours de ce composant (spot actuel après warp). Sinon on utilise « launch position » ci-dessous.")]
    [SerializeField] FpsLookAndMove launchIndexSource;
    [Tooltip("Emplacement actif sans FpsLookAndMove : ligne du tableau dont « launch position » est égal à cette valeur.")]
    [SerializeField] int launchPosition;
    [Tooltip("Tableau : une ligne par emplacement. Ex. position 0 → distance 0,22 et hauteur 1,89.")]
    [SerializeField] LaunchPositionTuning[] launchMultiplierTable;
    [Tooltip("Si aucune ligne ne correspond à « launch position », ces valeurs sont utilisées.")]
    [SerializeField, Min(0.01f)] float defaultLaunchDistanceMultiplier = 1f;
    [SerializeField, Min(0f)] float defaultLaunchHeightMultiplier = 1f;

    [SerializeField] float pumpOffset = 0.12f;
    [SerializeField] float pumpDuration = 0.18f;
    [Header("Idle — avant maintien du tir")]
    [Tooltip("Amplitude verticale (m, espace local du point de prise) pendant l’attente du clic maintenu.")]
    [SerializeField, Min(0f)] float idleBobAmplitude = 0.028f;
    [Tooltip("Vitesse d’oscillation verticale (plus haut = rebonds plus rapides).")]
    [SerializeField, Min(0.01f)] float idleBobSpeed = 2.4f;
    [Tooltip("Rotation continue (°/s) sur l’axe local Y pendant la même attente (0 = pas de spin).")]
    [SerializeField, Min(0f)] float idleSpinDegreesPerSecond = 96f;
    [Tooltip("Slider affichant la puissance (-1 faible, +1 fort). Laisser vide pour en créer un automatiquement.")]
    [SerializeField] Slider powerSlider;
    [SerializeField] bool createPowerSliderIfMissing = true;
    [Tooltip("Secousse du slider de puissance au panier (temps réel).")]
    [SerializeField, Min(0f)] float powerSliderShakeDuration = 0.32f;
    [SerializeField, Min(0f)] float powerSliderShakeMagnitude = 12f;
    [SerializeField, Min(0f)] float powerSliderShakeMagnitudeSwish = 17f;
    [Header("Audio")]
    [SerializeField] AudioClip LaunchBallAudio;
    [Tooltip("Joué quand le ballon revient à la main (début de partie ou après rappel au sol).")]
    [SerializeField] AudioClip ballSpawnAudioClip;

    [Header("Spawn VFX (ballon repris / départ)")]
    [Tooltip("Optionnel : objet VFX sous le ballon. Si vide, recherche d’un enfant nommé « Spawn ».")]
    [SerializeField] GameObject spawnVfxRoot;
    [Tooltip("Temps réel sans charge ni lâcher après l’apparition du ballon (VFX actif d’abord).")]
    [SerializeField, Min(0f)] float spawnVfxBlockShootSeconds = 0.4f;
    [Tooltip("Désactive le VFX après ce délai (maintenu en main). 0 = pas de masquage automatique (masqué au tir ou à la désactivation).")]
    [SerializeField, Min(0f)] float spawnVfxAutoHideSeconds = 1.2f;

    [Header("Transparence pendant la charge")]
    [Tooltip("Racine contenant les renderers à rendre transparents (mains + ballon). Si vide : ballHoldPoint.parent (ArmsPivot).")]
    [SerializeField] Transform fadeRoot;
    [Tooltip("Opacité minimum atteinte au pic de la charge (0 = totalement invisible).")]
    [SerializeField, Range(0f, 1f)] float chargedAlpha = 0.3f;
    [Tooltip("Vitesse du fondu (plus haut = plus rapide).")]
    [SerializeField, Range(1f, 30f)] float fadeSpeed = 12f;

    [Header("Trajectoire prédite")]
    [Tooltip("Active l'affichage de la trajectoire prédite pendant la charge du tir.")]
    [SerializeField] bool showTrajectory = true;
    [Tooltip("LineRenderer utilisé pour dessiner la trajectoire. Laisser vide pour en créer un automatiquement.")]
    [SerializeField] LineRenderer trajectoryLine;
    [Tooltip("Nombre de points utilisés pour échantillonner la trajectoire.")]
    [SerializeField, Range(8, 128)] int trajectoryResolution = 40;
    [Tooltip("Durée maximale (en secondes) simulée pour la trajectoire.")]
    [SerializeField, Range(0.2f, 5f)] float trajectoryMaxTime = 2.5f;
    [Tooltip("Si vrai, la ligne s'arrête au premier obstacle physique rencontré.")]
    [SerializeField] bool trajectoryStopOnHit = true;
    [Tooltip("Layers pris en compte pour stopper la trajectoire sur collision.")]
    [SerializeField] LayerMask trajectoryCollisionMask = ~0;
    [Tooltip("Épaisseur de la ligne au début (côté joueur).")]
    [SerializeField] float trajectoryStartWidth = 0.05f;
    [Tooltip("Épaisseur de la ligne à la fin (côté panier).")]
    [SerializeField] float trajectoryEndWidth = 0.02f;
    [Tooltip("Couleur de départ de la ligne.")]
    [SerializeField] Color trajectoryStartColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Couleur de fin de la ligne (s'estompe).")]
    [SerializeField] Color trajectoryEndColor = new Color(1f, 1f, 1f, 0f);

    InputActionMap _map;
    InputAction _shoot;
    InputAction _pump;

    float _lastOscillationValue;
    float _releasedPowerSliderValue;
    bool _sliderShowsReleasedShot;
    bool _oscillationActive;
    float _oscillationStartTime;
    bool _pumpAnim;
    BallFeel _feel;
    Collider _ballCollider;

    Renderer[] _fadeRenderers;
    Material[][] _originalMaterials;
    Material[][] _ghostMaterials;
    bool[] _ghostActive;
    float _currentAlpha = 1f;
    float _shootUnlockRealtime = -999f;
    GameObject _spawnVfxResolved;
    Coroutine _spawnVfxHideRoutine;
    Coroutine _sliderShakeRoutine;
    RectTransform _sliderShakeRt;
    Vector2 _sliderShakeRestoreAnchored;

    void Awake()
    {
        if (launchIndexSource == null)
            launchIndexSource = GetComponent<FpsLookAndMove>();
        if (launchIndexSource == null)
            launchIndexSource = GetComponentInParent<FpsLookAndMove>();

        if (createPowerSliderIfMissing && powerSlider == null)
            EnsurePowerSliderUi();
        else if (powerSlider != null)
            ConfigureSlider(powerSlider);

        EnsureTrajectoryLine();
        SubscribeBasketUiShake();
    }

    /// <summary>Index utilisé pour choisir la ligne du tableau des multiplicateurs (spot actuel sur le terrain).</summary>
    int ActiveLaunchPositionIndex =>
        launchIndexSource != null ? launchIndexSource.CurrentLaunchIndex : launchPosition;

    void OnEnable()
    {
        if (inputActions == null || ball == null || ballHoldPoint == null)
            return;
        _map = inputActions.FindActionMap("Player");
        _shoot = _map.FindAction("Shoot");
        _pump = _map.FindAction("Pump");
        _map.Enable();
        _feel = ball.GetComponent<BallFeel>();
        _ballCollider = ball.GetComponent<Collider>();
        _feel.RegisterShooter(this);
        BuildFadeMaterials();
        if (spawnVfxRoot == null)
            _spawnVfxResolved = null;
        bool suppressSpawnSfx = GameManager.Instance != null
            && GameManager.Instance.WaitsForStartButton
            && !GameManager.Instance.HasRoundStarted;
        HoldBall(playSpawnSfx: !suppressSpawnSfx);
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundStarted -= OnRoundStartedPlaySpawnSfx;
            GameManager.Instance.OnRoundStarted += OnRoundStartedPlaySpawnSfx;
        }
        ApplyAlpha(1f, true);
    }

    void OnDisable()
    {
        UnsubscribeBasketUiShake();
        if (GameManager.Instance != null)
            GameManager.Instance.OnRoundStarted -= OnRoundStartedPlaySpawnSfx;
        StopSpawnVfxHideRoutine();
        DisableSpawnVfxImmediate();
        _map?.Disable();
    }

    void Start()
    {
        launchMultiplierTable = new LaunchPositionTuning[] {
            new LaunchPositionTuning { launchPosition = 0, launchDistanceMultiplier = 0.22f, launchHeightMultiplier = 1.49f },
            new LaunchPositionTuning { launchPosition = 1, launchDistanceMultiplier = 0.37f, launchHeightMultiplier = 1.59f },
            new LaunchPositionTuning { launchPosition = 2, launchDistanceMultiplier = 1.12f, launchHeightMultiplier = 1.57f },
            new LaunchPositionTuning { launchPosition = 3, launchDistanceMultiplier = 1f, launchHeightMultiplier = 1f },
            new LaunchPositionTuning { launchPosition = 4, launchDistanceMultiplier = 1.12f, launchHeightMultiplier = 1.57f },
            new LaunchPositionTuning { launchPosition = 5, launchDistanceMultiplier = 0.37f, launchHeightMultiplier = 1.59f },
            new LaunchPositionTuning { launchPosition = 6, launchDistanceMultiplier = 0.22f, launchHeightMultiplier = 1.49f },
        };
        SubscribeBasketUiShake();
    }

    void Update()
    {
        if (PauseController.IsPaused
            || (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            || (GameManager.Instance != null && !GameManager.Instance.HasRoundStarted))
            return;

        if (ball == null || ballHoldPoint == null || _shoot == null || _feel == null)
            return;

        if (!_feel.IsHeld)
            return;

        if (!_pumpAnim && _pump != null && _pump.WasPressedThisFrame() && !_shoot.IsPressed())
            StartCoroutine(PumpRoutine());

        if (_pumpAnim)
        {
            UpdateFade(1f);
            return;
        }

        if (Time.realtimeSinceStartup < _shootUnlockRealtime)
        {
            if (_oscillationActive)
            {
                _oscillationActive = false;
                _feel.ResetSquash();
                _sliderShowsReleasedShot = false;
                SetSliderDisplay(0f);
                UpdateFade(1f);
                HideTrajectory();
            }
            return;
        }

        if (_shoot.WasPressedThisFrame())
            _sliderShowsReleasedShot = false;

        bool shootHeld = _shoot.IsPressed();

        if (shootHeld)
        {
            if (!_oscillationActive)
            {
                _oscillationActive = true;
                _oscillationStartTime = Time.time;
            }

            _lastOscillationValue = Mathf.Lerp(-1f, 1f, Mathf.PingPong((Time.time - _oscillationStartTime) * sliderOscillationSpeed, 1f));
            float charge01 = SignedPowerToCharge01(_lastOscillationValue);
            _feel.SetChargeSquash(charge01);
            SetSliderDisplay(_lastOscillationValue);
            float targetAlpha = Mathf.Lerp(1f, chargedAlpha, charge01);
            UpdateFade(targetAlpha);
            UpdateTrajectory(_lastOscillationValue);
        }
        else
        {
            if (_oscillationActive)
            {
                _releasedPowerSliderValue = Mathf.Clamp(_lastOscillationValue, -1f, 1f);
                _sliderShowsReleasedShot = true;
                TryReleaseShot(_lastOscillationValue);
            }

            _oscillationActive = false;
            _feel.ResetSquash();
            if (_sliderShowsReleasedShot)
                SetSliderDisplay(_releasedPowerSliderValue);
            else
                SetSliderDisplay(0f);
            UpdateFade(1f);
            HideTrajectory();
        }
    }

    void LateUpdate()
    {
        if (PauseController.IsPaused
            || (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            || (GameManager.Instance != null && !GameManager.Instance.HasRoundStarted))
            return;

        if (ball == null || ballHoldPoint == null || _feel == null || !_feel.IsHeld || _pumpAnim)
            return;

        bool charging = _shoot != null && _shoot.IsPressed();
        if (charging)
        {
            ball.transform.localPosition = Vector3.zero;
            ball.transform.localRotation = Quaternion.identity;
            return;
        }

        float bob = idleBobAmplitude > 0f
            ? Mathf.Sin(Time.time * idleBobSpeed) * idleBobAmplitude
            : 0f;
        ball.transform.localPosition = new Vector3(0f, bob, 0f);
        ball.transform.localRotation = idleSpinDegreesPerSecond > 0f
            ? Quaternion.AngleAxis(Time.time * idleSpinDegreesPerSecond, Vector3.up)
            : Quaternion.identity;
    }

    IEnumerator PumpRoutine()
    {
        _pumpAnim = true;
        Vector3 start = ball.transform.localPosition;
        Vector3 punch = start + Vector3.forward * pumpOffset;
        float t = 0f;
        while (t < pumpDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin((t / pumpDuration) * Mathf.PI);
            ball.transform.localPosition = Vector3.Lerp(start, punch, k);
            yield return null;
        }

        ball.transform.localPosition = start;
        _pumpAnim = false;
    }

    void TryReleaseShot(float signedPower)
    {
        signedPower = Mathf.Clamp(signedPower, -1f, 1f);
        float charge01 = SignedPowerToCharge01(signedPower);
        if (charge01 < 0.02f)
            return;

        float impulse = Mathf.Lerp(minLaunchImpulse, maxLaunchImpulse, charge01);
        Vector3 dir = ballHoldPoint.forward;
        float heightCharge01 = sliderControlsLaunchHeight ? charge01 : fixedHeightCharge;
        float heightCurve = heightByCharge != null ? Mathf.Clamp01(heightByCharge.Evaluate(heightCharge01)) : heightCharge01;
        GetLaunchMultipliers(out float distMul, out float heightMul);
        float upBoost = Mathf.Lerp(minLaunchHeight, maxLaunchHeight, heightCurve) * heightMul;
        Vector3 vel = dir * (impulse * distMul) + Vector3.up * upBoost;

        ball.transform.SetParent(null);
        if (_ballCollider != null)
            _ballCollider.enabled = true;
        ball.isKinematic = false;
        _feel.SetHeld(false);
        _feel.NotifyReleased();
        ball.linearVelocity = vel;
        ball.angularVelocity = Random.insideUnitSphere * 3f * charge01;
        if (LaunchBallAudio != null)
            AudioSource.PlayClipAtPoint(LaunchBallAudio, ball.position);

        StopSpawnVfxHideRoutine();
        DisableSpawnVfxImmediate();

        _feel.ResetSquash();
        ApplyAlpha(1f, true);
        HideTrajectory();
    }

    public void RecallBallFromGround()
    {
        HoldBall();
    }

    void OnRoundStartedPlaySpawnSfx() => PlayBallSpawnSfx();

    void PlayBallSpawnSfx()
    {
        if (ballSpawnAudioClip == null || ballHoldPoint == null)
            return;
        AudioSource.PlayClipAtPoint(ballSpawnAudioClip, ballHoldPoint.position);
    }

    void HoldBall(bool playSpawnSfx = true)
    {
        if (ball == null || ballHoldPoint == null)
            return;
        ball.isKinematic = true;
        ball.transform.SetParent(ballHoldPoint);
        ball.transform.localPosition = Vector3.zero;
        ball.transform.localRotation = Quaternion.identity;
        if (_ballCollider != null)
            _ballCollider.enabled = false;
        ball.linearVelocity = Vector3.zero;
        ball.angularVelocity = Vector3.zero;
        _feel?.SetHeld(true);
        _feel?.ResetSquash();
        _sliderShowsReleasedShot = false;
        SetSliderDisplay(0f);
        ApplyAlpha(1f, true);
        PlaySpawnVfxAndGateShoot();
        if (playSpawnSfx)
            PlayBallSpawnSfx();
    }

    void SetSliderDisplay(float value)
    {
        if (powerSlider == null)
            return;
        powerSlider.SetValueWithoutNotify(Mathf.Clamp(value, -1f, 1f));
    }

    void SubscribeBasketUiShake()
    {
        if (GameManager.Instance == null)
            return;
        GameManager.Instance.OnBasket -= OnBasketUiShake;
        GameManager.Instance.OnBasket += OnBasketUiShake;
    }

    void UnsubscribeBasketUiShake()
    {
        if (GameManager.Instance == null)
            return;
        GameManager.Instance.OnBasket -= OnBasketUiShake;
    }

    void OnBasketUiShake(bool swish)
    {
        if (powerSlider == null || powerSliderShakeDuration <= 0f)
            return;

        float magnitude = swish ? powerSliderShakeMagnitudeSwish : powerSliderShakeMagnitude;
        if (magnitude <= 0f)
            return;

        var rt = powerSlider.transform as RectTransform;
        if (rt == null)
            return;

        if (_sliderShakeRoutine != null)
        {
            StopCoroutine(_sliderShakeRoutine);
            if (_sliderShakeRt != null)
                _sliderShakeRt.anchoredPosition = _sliderShakeRestoreAnchored;
        }

        _sliderShakeRt = rt;
        _sliderShakeRestoreAnchored = rt.anchoredPosition;
        _sliderShakeRoutine = StartCoroutine(SliderPowerShakeRoutine(magnitude));
    }

    IEnumerator SliderPowerShakeRoutine(float magnitude)
    {
        yield return UiRectShake.AnchoredShake(_sliderShakeRt, _sliderShakeRestoreAnchored, powerSliderShakeDuration, magnitude);
        _sliderShakeRoutine = null;
        _sliderShakeRt = null;
    }

    static float SignedPowerToCharge01(float signedPower) => Mathf.Clamp01((signedPower + 1f) * 0.5f);

    static void ConfigureSlider(Slider s)
    {
        s.minValue = -1f;
        s.maxValue = 1f;
        s.wholeNumbers = false;
        s.interactable = false;
    }

    void UpdateFade(float targetAlpha)
    {
        float a = Mathf.MoveTowards(_currentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);
        ApplyAlpha(a, false);
    }

    void ApplyAlpha(float alpha, bool snap)
    {
        _currentAlpha = snap ? alpha : alpha;
        if (_fadeRenderers == null || _fadeRenderers.Length == 0)
            return;

        bool wantsGhost = alpha < 0.999f;
        for (int i = 0; i < _fadeRenderers.Length; i++)
        {
            var r = _fadeRenderers[i];
            if (r == null) continue;

            if (wantsGhost)
            {
                if (!_ghostActive[i])
                {
                    r.sharedMaterials = _ghostMaterials[i];
                    _ghostActive[i] = true;
                }
                var mats = _ghostMaterials[i];
                for (int j = 0; j < mats.Length; j++)
                    SetMaterialAlpha(mats[j], alpha);
            }
            else if (_ghostActive[i])
            {
                r.sharedMaterials = _originalMaterials[i];
                _ghostActive[i] = false;
            }
        }
    }

    void BuildFadeMaterials()
    {
        var root = fadeRoot != null ? fadeRoot : (ballHoldPoint != null ? ballHoldPoint.parent : null);
        if (root == null)
        {
            _fadeRenderers = new Renderer[0];
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        var list = new List<Renderer>(renderers.Length);
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            if (r.sharedMaterials == null || r.sharedMaterials.Length == 0) continue;
            list.Add(r);
        }
        _fadeRenderers = list.ToArray();
        _originalMaterials = new Material[_fadeRenderers.Length][];
        _ghostMaterials = new Material[_fadeRenderers.Length][];
        _ghostActive = new bool[_fadeRenderers.Length];

        for (int i = 0; i < _fadeRenderers.Length; i++)
        {
            var origs = _fadeRenderers[i].sharedMaterials;
            _originalMaterials[i] = origs;
            var ghosts = new Material[origs.Length];
            for (int j = 0; j < origs.Length; j++)
            {
                ghosts[j] = origs[j] != null ? CreateGhost(origs[j]) : null;
            }
            _ghostMaterials[i] = ghosts;
        }
    }

    static Material CreateGhost(Material src)
    {
        var m = new Material(src);
        m.name = src.name + " (Ghost)";
        ConfigureForTransparency(m);
        return m;
    }

    static void ConfigureForTransparency(Material m)
    {
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        m.SetOverrideTag("RenderType", "Transparent");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    static void SetMaterialAlpha(Material m, float alpha)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor"))
        {
            var c = m.GetColor("_BaseColor");
            c.a = alpha;
            m.SetColor("_BaseColor", c);
        }
        if (m.HasProperty("_Color"))
        {
            var c = m.GetColor("_Color");
            c.a = alpha;
            m.SetColor("_Color", c);
        }
    }

    void EnsurePowerSliderUi()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        var canvasGo = new GameObject("ShootPowerCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var sliderRoot = new GameObject("PowerSlider");
        sliderRoot.transform.SetParent(canvasGo.transform, false);
        var rootRt = sliderRoot.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.22f, 0.055f);
        rootRt.anchorMax = new Vector2(0.78f, 0.095f);
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var slider = sliderRoot.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.interactable = false;
        slider.minValue = -1f;
        slider.maxValue = 1f;
        slider.direction = Slider.Direction.LeftToRight;

        var bg = new GameObject("Background");
        bg.transform.SetParent(sliderRoot.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        StretchRect(bgRt);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.12f, 0.14f, 0.92f);

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderRoot.transform, false);
        var fillAreaRt = fillArea.AddComponent<RectTransform>();
        StretchRect(fillAreaRt);
        fillAreaRt.offsetMin = new Vector2(6, 6);
        fillAreaRt.offsetMax = new Vector2(-6, -6);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillRt = fill.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.95f, 0.5f, 0.12f, 1f);

        slider.fillRect = fillRt;
        slider.targetGraphic = fillImg;

        var handleSlide = new GameObject("Handle Slide Area");
        handleSlide.transform.SetParent(sliderRoot.transform, false);
        var hsRt = handleSlide.AddComponent<RectTransform>();
        StretchRect(hsRt);
        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleSlide.transform, false);
        var hRt = handle.AddComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0, 0);
        hRt.anchorMax = new Vector2(0, 1);
        hRt.pivot = new Vector2(0.5f, 0.5f);
        hRt.sizeDelta = new Vector2(18f, 0f);
        var hImg = handle.AddComponent<Image>();
        hImg.color = new Color(1f, 1f, 1f, 0f);
        slider.handleRect = hRt;

        powerSlider = slider;
        ConfigureSlider(powerSlider);
    }

    static void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void EnsureTrajectoryLine()
    {
        if (trajectoryLine == null)
        {
            var go = new GameObject("ShootTrajectory");
            go.transform.SetParent(transform, false);
            trajectoryLine = go.AddComponent<LineRenderer>();
            trajectoryLine.material = new Material(Shader.Find("Sprites/Default"));
        }

        trajectoryLine.useWorldSpace = true;
        trajectoryLine.textureMode = LineTextureMode.Stretch;
        trajectoryLine.alignment = LineAlignment.View;
        trajectoryLine.numCapVertices = 4;
        trajectoryLine.numCornerVertices = 4;
        trajectoryLine.shadowCastingMode = ShadowCastingMode.Off;
        trajectoryLine.receiveShadows = false;
        trajectoryLine.startWidth = trajectoryStartWidth;
        trajectoryLine.endWidth = trajectoryEndWidth;
        trajectoryLine.startColor = trajectoryStartColor;
        trajectoryLine.endColor = trajectoryEndColor;
        trajectoryLine.positionCount = 0;
        trajectoryLine.enabled = false;
    }

    float TrajectoryProbeWorldRadius()
    {
        if (ball == null)
            return 0f;
        if (_ballCollider is SphereCollider sc)
        {
            Vector3 ls = sc.transform.lossyScale;
            return sc.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        }

        if (_ballCollider != null)
        {
            Vector3 e = _ballCollider.bounds.extents;
            return Mathf.Max(e.x, e.y, e.z);
        }

        return 0f;
    }

    void UpdateTrajectory(float signedPower)
    {
        if (!showTrajectory || trajectoryLine == null || ball == null || ballHoldPoint == null)
        {
            HideTrajectory();
            return;
        }

        float charge01 = SignedPowerToCharge01(Mathf.Clamp(signedPower, -1f, 1f));
        float impulse = Mathf.Lerp(minLaunchImpulse, maxLaunchImpulse, charge01);
        Vector3 dir = ballHoldPoint.forward;
        float heightCharge01 = sliderControlsLaunchHeight ? charge01 : fixedHeightCharge;
        float heightCurve = heightByCharge != null ? Mathf.Clamp01(heightByCharge.Evaluate(heightCharge01)) : heightCharge01;
        GetLaunchMultipliers(out float distMul, out float heightMul);
        float upBoost = Mathf.Lerp(minLaunchHeight, maxLaunchHeight, heightCurve) * heightMul;
        Vector3 velocity = dir * (impulse * distMul) + Vector3.up * upBoost;

        Vector3 origin = ball.transform.position;
        Vector3 gravity = Physics.gravity;

        int steps = Mathf.Max(2, trajectoryResolution);
        float dt = trajectoryMaxTime / (steps - 1);

        if (trajectoryLine.positionCount != steps)
            trajectoryLine.positionCount = steps;

        trajectoryLine.startWidth = trajectoryStartWidth;
        trajectoryLine.endWidth = trajectoryEndWidth;
        trajectoryLine.startColor = trajectoryStartColor;
        trajectoryLine.endColor = trajectoryEndColor;

        Vector3 prev = origin;
        trajectoryLine.SetPosition(0, prev);

        int written = 1;
        bool clipped = false;
        for (int i = 1; i < steps; i++)
        {
            float t = i * dt;
            Vector3 next = origin + velocity * t + 0.5f * gravity * (t * t);

            if (trajectoryStopOnHit)
            {
                Vector3 segment = next - prev;
                float dist = segment.magnitude;
                if (dist > 0.0001f)
                {
                    Vector3 segDir = segment / dist;
                    float probeR = TrajectoryProbeWorldRadius();
                    bool hitEnv = probeR > 1e-4f
                        ? Physics.SphereCast(prev, probeR, segDir, out RaycastHit hit, dist, trajectoryCollisionMask, QueryTriggerInteraction.Ignore)
                        : Physics.Raycast(prev, segDir, out hit, dist, trajectoryCollisionMask, QueryTriggerInteraction.Ignore);
                    if (hitEnv && (_ballCollider == null || hit.collider != _ballCollider))
                    {
                        trajectoryLine.SetPosition(i, hit.point);
                        written = i + 1;
                        clipped = true;
                        break;
                    }
                }
            }

            trajectoryLine.SetPosition(i, next);
            written = i + 1;
            prev = next;
        }

        if (clipped && written < steps)
            trajectoryLine.positionCount = written;

        trajectoryLine.enabled = true;
    }

    void HideTrajectory()
    {
        if (trajectoryLine == null)
            return;
        trajectoryLine.enabled = false;
        trajectoryLine.positionCount = 0;
    }

    /// <summary>Change l’emplacement de tir pour les scènes sans <see cref="launchIndexSource"/> ; sinon miroir Inspector / fallback.</summary>
    public void SetLaunchPosition(int value) => launchPosition = value;

    /// <summary>Bloque le tir (charge + lancer) pendant <paramref name="seconds"/> en temps réel.</summary>
    public void SetShootLockedForSeconds(float seconds)
    {
        if (seconds <= 0f)
            return;
        _shootUnlockRealtime = Mathf.Max(_shootUnlockRealtime, Time.realtimeSinceStartup + seconds);
    }

    GameObject GetSpawnVfxRoot()
    {
        if (spawnVfxRoot != null)
            return spawnVfxRoot;
        if (_spawnVfxResolved != null)
            return _spawnVfxResolved;
        if (ball == null)
            return null;
        foreach (var tr in ball.GetComponentsInChildren<Transform>(true))
        {
            if (tr.name != "Spawn")
                continue;
            _spawnVfxResolved = tr.gameObject;
            return _spawnVfxResolved;
        }

        return null;
    }

    void PlaySpawnVfxAndGateShoot()
    {
        var root = GetSpawnVfxRoot();
        if (root == null)
            return;

        StopSpawnVfxHideRoutine();
        root.SetActive(true);
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }

        if (spawnVfxBlockShootSeconds > 0f)
            SetShootLockedForSeconds(spawnVfxBlockShootSeconds);

        if (spawnVfxAutoHideSeconds > 0f)
            _spawnVfxHideRoutine = StartCoroutine(HideSpawnVfxAfterDelay(spawnVfxAutoHideSeconds));
    }

    IEnumerator HideSpawnVfxAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        _spawnVfxHideRoutine = null;
        if (_feel != null && _feel.IsHeld)
            DisableSpawnVfxImmediate();
    }

    void StopSpawnVfxHideRoutine()
    {
        if (_spawnVfxHideRoutine == null)
            return;
        StopCoroutine(_spawnVfxHideRoutine);
        _spawnVfxHideRoutine = null;
    }

    void DisableSpawnVfxImmediate()
    {
        var root = spawnVfxRoot != null ? spawnVfxRoot : _spawnVfxResolved;
        if (root == null)
            return;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        root.SetActive(false);
    }

    void GetLaunchMultipliers(out float distanceMul, out float heightMul)
    {
        distanceMul = defaultLaunchDistanceMultiplier;
        heightMul = defaultLaunchHeightMultiplier;
        if (launchMultiplierTable == null || launchMultiplierTable.Length == 0){
            Debug.Log("Launch multiplier table is not set");
            return;
        }

        distanceMul = launchMultiplierTable[launchPosition].launchDistanceMultiplier;
        heightMul = launchMultiplierTable[launchPosition].launchHeightMultiplier;
    }
}
