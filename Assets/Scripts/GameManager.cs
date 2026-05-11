using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    /// <summary>True after the round timer hits zero until the scene is restarted.</summary>
    public bool IsGameOver { get; private set; }

    [SerializeField] AudioSource audioSource;
    [Header("Basket audio (clips)")]
    [Tooltip("Played on every basket, before swish / rim-contact basket.")]
    [SerializeField] AudioClip scoreImpactClip;
    public GameObject impactEffectVFX;
    [SerializeField] float impactEffectVisibleSeconds = 1.25f;
    [Header("Swish VFX")]
    [Tooltip("Enabled only when the basket is a swish (no rim contact).")]
    [SerializeField] GameObject swishVFX;
    [SerializeField, Min(0f)] float swishVfxVisibleSeconds = 1.25f;
    [Header("Normal basket VFX")]
    [Tooltip("Enabled when the basket scores normal points (not a swish).")]
    [SerializeField] GameObject goodScoreVFX;
    [SerializeField, Min(0f)] float goodScoreVfxVisibleSeconds = 1.25f;
    [Tooltip("Swish basket (no rim touch).")]
    [SerializeField] AudioClip swishClip;
    [Tooltip("Basket after rim contact. If empty, swishClip is played again.")]
    [SerializeField] AudioClip scoredWithRimClip;
    [SerializeField] float swishPitchMin = 0.95f;
    [SerializeField] float swishPitchMax = 1.08f;
    [Header("Scoring")]
    [SerializeField, Min(0)] int swishScorePoints = 3;
    [SerializeField, Min(0)] int normalScorePoints = 1;
    [Header("Freeze frame (basket)")]
    [Tooltip("Real time (s) that time is frozen when the ball enters the net. Min and max: a random value between them on each basket.")]
    [SerializeField, Min(0f)] float scoreFreezeMinRealtime = 0.05f;
    [SerializeField, Min(0f)] float scoreFreezeMaxRealtime = 0.1f;
    [Header("Shot Camera (made basket)")]
    [Tooltip("Player camera. If empty: Camera.main at basket time.")]
    [SerializeField] Camera gameplayCamera;
    [SerializeField] Camera shotCamera;
    [SerializeField, Min(0.05f)] float shotCameraHoldSeconds = 1.35f;
    [Header("Audio air ball")]
    [SerializeField] AudioClip airBallClip;
    [Header("Miss VFX")]
    [Tooltip("Parent object for miss VFX (particles etc.). Enabled on miss; disabled again after missShotVfxLifetime if > 0.")]
    [SerializeField] Transform missShotVfxSpawnPoint;
    [SerializeField, Min(0f)] float missShotVfxLifetime = 2f;
    [Header("Background music (after Start)")]
    [Tooltip("Optional: assign a dedicated 2D AudioSource; if empty, one is created when a clip is set.")]
    [SerializeField] AudioSource backgroundMusicSource;
    [SerializeField] AudioClip backgroundMusicClip;
    [SerializeField, Range(0f, 1f)] float backgroundMusicVolume = 0.22f;
    [Header("Start screen")]
    [Tooltip("Root object to show until Start is pressed (e.g. your Start panel). Optional if the button lives on an always-visible canvas.")]
    [SerializeField] GameObject startPanel;
    [Tooltip("When assigned, the round timer and gameplay stay disabled until this button is clicked.")]
    [SerializeField] Button startButton;
    [Header("UI")]
    public TextMeshProUGUI scoreTxt;
    [SerializeField] string scoreLabel = "Score: ";
    [Tooltip("HUD countdown (e.g. Timer object on the Canvas). Uses real time; pauses when the pause menu is open.")]
    [SerializeField] TextMeshProUGUI timerTxt;
    [Tooltip("Played once each time the displayed countdown drops a second while at 10s or below (same window as the red pulse). Leave empty to disable.")]
    [SerializeField] AudioClip timerLowSecondTickClip;
    [SerializeField, Range(0f, 1f)] float timerLowSecondTickVolume = 0.85f;
    [Tooltip("While the round timer is at 10 seconds or below (same window as red pulse / low ticks), drain this many times faster than real time. 1 = no change.")]
    [SerializeField, Min(1f)] float timerLowDrainSpeedMultiplier = 2f;
    [SerializeField, Min(1f)] float roundDurationSeconds = 90f;
    [Tooltip("Score text shake on basket (real time, visible even during freeze frame).")]
    [SerializeField, Min(0f)] float scoreUiShakeDuration = 0.32f;
    [SerializeField, Min(0f)] float scoreUiShakeMagnitude = 14f;
    [SerializeField, Min(0f)] float scoreUiShakeMagnitudeSwish = 20f;
    [Header("Game over")]
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] TextMeshProUGUI gameOverScoreTxt;
    [SerializeField] Button gameOverRestartButton;
    [SerializeField] Button gameOverQuitButton;
    [Tooltip("If no game over panel is assigned, one is created at runtime (full-screen overlay + score + Restart / Quit).")]
    [SerializeField] bool createGameOverUiIfMissing = true;
    [Header("Directive hint (idle)")]
    [Tooltip("UI root (e.g. Canvas child \"Directive\"). Inactive until idle; after the pulse, stays off.")]
    [SerializeField] GameObject directiveRoot;
    [SerializeField, Min(0f)] float directiveInactivitySeconds = 3f;
    [SerializeField, Min(0.05f)] float directivePulseDurationSeconds = 2.25f;
    [SerializeField, Min(0f)] float directivePulseScaleAmplitude = 0.07f;
    [SerializeField, Min(0.01f)] float directivePulseSpeed = 7f;

    public int Score { get; private set; }
    /// <summary>False until the optional start button is pressed; then true for the rest of the round.</summary>
    public bool HasRoundStarted { get; private set; }
    /// <summary>True when a start button must be pressed before the round timer and movement run.</summary>
    public bool WaitsForStartButton => startButton != null;
    public event Action OnRoundStarted;
    public event Action<int> OnScoreChanged;
    public event Action<bool> OnBasket;
    /// <summary>The ball touched the ground (or was recalled) after a made basket on this shot.</summary>
    public event Action OnScoredBallGrounded;

    bool _firstBasketCelebrationDone;
    Coroutine _hitStopRoutine;
    Coroutine _impactVfxRoutine;
    Coroutine _swishVfxRoutine;
    Coroutine _goodVfxRoutine;
    Coroutine _missShotVfxRoutine;
    Coroutine _shotCameraRoutine;
    Coroutine _scoreShakeRoutine;
    RectTransform _scoreShakeRt;
    Vector2 _scoreShakeRestoreAnchored;
    float _roundTimeRemaining;
    bool _backgroundMusicStarted;

    static readonly Color TimerLowPulseRed = new Color(1f, 0.22f, 0.22f);
    const float TimerLowPulseSpeed = 7.5f;
    const float TimerLowScalePulseSpeed = 9f;
    const float TimerLowScaleAmplitude = 0.065f;
    Color _timerColorDefault;
    Vector3 _timerScaleDefault;
    bool _timerVisualDefaultsCached;
    int _lastTimerDisplayedCeilForTick = int.MinValue;

    bool _directiveConsumed;
    float _directiveIdleAccum;
    Coroutine _directiveCoroutine;
    RectTransform _directiveRt;
    Vector3 _directiveBaseScale = Vector3.one;

    struct ShotCamBackup
    {
        public bool Valid;
        public Camera Gameplay;
        public Camera Shot;
        public bool GameplayCamEnabled;
        public bool ShotCamEnabled;
        public bool ShotRootActive;
        public string GameplayTag;
        public string ShotTag;
        public bool GameplayListenerEnabled;
        public bool ShotListenerEnabled;
    }

    ShotCamBackup _shotCamBackup;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        _roundTimeRemaining = roundDurationSeconds;
        _lastTimerDisplayedCeilForTick = int.MinValue;
        if (gameOverPanel == null && createGameOverUiIfMissing)
            BuildDefaultGameOverPanel();

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        RegisterGameOverButtonListeners();
        EnsureBackgroundMusicSource();
        ConfigureStartScreen();
        RegisterStartButtonListeners();
        UpdateScoreText();
        UpdateTimerText();
        BootstrapDirectiveHint();
    }

    void OnEnable()
    {
        RegisterGameOverButtonListeners();
        RegisterStartButtonListeners();
    }

    void Update()
    {
        UpdateDirectiveHintIdle();

        if (IsGameOver || PauseController.IsPaused || !HasRoundStarted)
            return;

        float dt = Time.unscaledDeltaTime;
        if (_roundTimeRemaining <= 10f)
            dt *= timerLowDrainSpeedMultiplier;
        _roundTimeRemaining -= dt;
        if (_roundTimeRemaining <= 0f)
        {
            _roundTimeRemaining = 0f;
            UpdateTimerText();
            TriggerGameOver();
            return;
        }

        UpdateTimerText();
    }

    public void RegisterBasket(bool swish)
    {
        if (IsGameOver || !HasRoundStarted)
            return;

        int points = swish ? swishScorePoints : normalScorePoints;
        Score += points;
        UpdateScoreText();
        PlayScoreUiShake(swish);
        OnScoreChanged?.Invoke(Score);
        OnBasket?.Invoke(swish);

        PlayImpactVfxPulse();
        if (swish)
            PlaySwishVfxPulse();
        else
            PlayGoodVfxPulse();

        bool firstBasket = !_firstBasketCelebrationDone;
        if (!_firstBasketCelebrationDone)
            _firstBasketCelebrationDone = true;

        if (_hitStopRoutine != null)
            StopCoroutine(_hitStopRoutine);
        _hitStopRoutine = StartCoroutine(ScoreBasketTimeRoutine(firstBasket));

        if (shotCamera != null)
        {
            if (_shotCameraRoutine != null)
            {
                StopCoroutine(_shotCameraRoutine);
                RestoreShotCameraView();
            }

            _shotCameraRoutine = StartCoroutine(ShotCameraCelebrationRoutine());
        }

        PlayBasketClips(swish);
        var msg = swish
            ? "SWISH. The net didn't even twitch."
            : "It goes in. Even a plastic rim flinched.";
        Debug.Log($"[Basket] {msg} (+{points}, Score: {Score})");
    }

    void UpdateScoreText()
    {
        if (scoreTxt == null)
            return;

        scoreTxt.text = $"{scoreLabel}{Score}";
    }

    void PlayScoreUiShake(bool swish)
    {
        if (scoreTxt == null || scoreUiShakeDuration <= 0f)
            return;

        var rt = scoreTxt.rectTransform;
        float magnitude = swish ? scoreUiShakeMagnitudeSwish : scoreUiShakeMagnitude;
        if (magnitude <= 0f)
            return;

        if (_scoreShakeRoutine != null)
        {
            StopCoroutine(_scoreShakeRoutine);
            if (_scoreShakeRt != null)
                _scoreShakeRt.anchoredPosition = _scoreShakeRestoreAnchored;
        }

        _scoreShakeRt = rt;
        _scoreShakeRestoreAnchored = rt.anchoredPosition;
        _scoreShakeRoutine = StartCoroutine(ScoreUiShakeRoutine(magnitude));
    }

    IEnumerator ScoreUiShakeRoutine(float magnitude)
    {
        yield return UiRectShake.AnchoredShake(_scoreShakeRt, _scoreShakeRestoreAnchored, scoreUiShakeDuration, magnitude);
        _scoreShakeRoutine = null;
        _scoreShakeRt = null;
    }

    void UpdateTimerText()
    {
        if (timerTxt == null)
            return;

        int t = Mathf.CeilToInt(Mathf.Max(0f, _roundTimeRemaining));
        int m = t / 60;
        int s = t % 60;
        timerTxt.text = $"{m:00}:{s:00}";
        TryPlayTimerLowSecondTick(t);
        ApplyTimerHudVisuals(t);
    }

    void TryPlayTimerLowSecondTick(int displayedSecondsCeiled)
    {
        if (timerLowSecondTickClip == null || audioSource == null)
            return;
        if (!HasRoundStarted || IsGameOver || PauseController.IsPaused)
            return;

        if (_lastTimerDisplayedCeilForTick == int.MinValue)
        {
            _lastTimerDisplayedCeilForTick = displayedSecondsCeiled;
            return;
        }

        if (displayedSecondsCeiled >= _lastTimerDisplayedCeilForTick)
        {
            _lastTimerDisplayedCeilForTick = displayedSecondsCeiled;
            return;
        }

        if (displayedSecondsCeiled <= 10)
            PlayTimerLowSecondTick();

        _lastTimerDisplayedCeilForTick = displayedSecondsCeiled;
    }

    void PlayTimerLowSecondTick()
    {
        if (timerLowSecondTickClip == null || audioSource == null)
            return;
        float p = audioSource.pitch;
        audioSource.pitch = 1f;
        audioSource.PlayOneShot(timerLowSecondTickClip, timerLowSecondTickVolume);
        audioSource.pitch = p;
    }

    void EnsureTimerHudDefaultsCached()
    {
        if (timerTxt == null || _timerVisualDefaultsCached)
            return;
        _timerColorDefault = timerTxt.color;
        _timerScaleDefault = timerTxt.rectTransform.localScale;
        _timerVisualDefaultsCached = true;
    }

    void ResetTimerHudVisuals()
    {
        EnsureTimerHudDefaultsCached();
        if (timerTxt == null || !_timerVisualDefaultsCached)
            return;
        timerTxt.color = _timerColorDefault;
        timerTxt.rectTransform.localScale = _timerScaleDefault;
    }

    void ApplyTimerHudVisuals(int displayedSecondsCeiled)
    {
        EnsureTimerHudDefaultsCached();
        if (timerTxt == null || !_timerVisualDefaultsCached)
            return;

        if (IsGameOver || !HasRoundStarted || displayedSecondsCeiled > 10)
        {
            timerTxt.color = _timerColorDefault;
            timerTxt.rectTransform.localScale = _timerScaleDefault;
            return;
        }

        float tUnscaled = Time.unscaledTime;
        float colorWave = Mathf.Sin(tUnscaled * TimerLowPulseSpeed) * 0.5f + 0.5f;
        timerTxt.color = Color.Lerp(_timerColorDefault, TimerLowPulseRed, colorWave);
        float scaleMul = 1f + TimerLowScaleAmplitude * Mathf.Sin(tUnscaled * TimerLowScalePulseSpeed);
        timerTxt.rectTransform.localScale = _timerScaleDefault * scaleMul;
    }

    void UpdateGameOverScoreText()
    {
        if (gameOverScoreTxt == null)
            return;

        gameOverScoreTxt.text = $"{scoreLabel}{Score}";
    }

    void TriggerGameOver()
    {
        if (IsGameOver)
            return;

        IsGameOver = true;
        Time.timeScale = 0f;
        ResetTimerHudVisuals();
        StopRoundAudio();
        CancelDirectiveHintBecauseGameOver();

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        UpdateGameOverScoreText();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>Same as pause menu Restart: reload the active scene.</summary>
    public void GameOverRestart()
    {
        var pause = FindFirstObjectByType<PauseController>();
        if (pause != null)
            pause.Restart();
        else
        {
            PauseController.ResetTimeScaleAndPauseState();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
        }
    }

    public void GameOverQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void RegisterGameOverButtonListeners()
    {
        if (gameOverRestartButton != null)
        {
            gameOverRestartButton.onClick.RemoveListener(GameOverRestart);
            gameOverRestartButton.onClick.AddListener(GameOverRestart);
        }

        if (gameOverQuitButton != null)
        {
            gameOverQuitButton.onClick.RemoveListener(GameOverQuit);
            gameOverQuitButton.onClick.AddListener(GameOverQuit);
        }
    }

    void UnregisterGameOverButtonListeners()
    {
        if (gameOverRestartButton != null)
            gameOverRestartButton.onClick.RemoveListener(GameOverRestart);
        if (gameOverQuitButton != null)
            gameOverQuitButton.onClick.RemoveListener(GameOverQuit);
    }

    void BuildDefaultGameOverPanel()
    {
        var canvasGo = new GameObject("GameOverCanvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 550;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = new GameObject("Dim");
        dim.transform.SetParent(canvasGo.transform, false);
        var dimRt = dim.AddComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.65f);

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(canvasGo.transform, false);
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.62f);
        titleRt.anchorMax = new Vector2(0.5f, 0.62f);
        titleRt.sizeDelta = new Vector2(900f, 100f);
        titleRt.anchoredPosition = Vector2.zero;
        var title = titleGo.AddComponent<Text>();
        title.fontSize = 52;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.text = "GAME OVER";

        var scoreGo = new GameObject("FinalScore");
        scoreGo.transform.SetParent(canvasGo.transform, false);
        var scoreRt = scoreGo.AddComponent<RectTransform>();
        scoreRt.anchorMin = new Vector2(0.5f, 0.48f);
        scoreRt.anchorMax = new Vector2(0.5f, 0.48f);
        scoreRt.sizeDelta = new Vector2(900f, 72f);
        scoreRt.anchoredPosition = Vector2.zero;
        gameOverScoreTxt = scoreGo.AddComponent<TextMeshProUGUI>();
        if (scoreTxt != null)
        {
            gameOverScoreTxt.font = scoreTxt.font;
            gameOverScoreTxt.fontSharedMaterial = scoreTxt.fontSharedMaterial;
        }
        gameOverScoreTxt.fontSize = 36f;
        gameOverScoreTxt.alignment = TextAlignmentOptions.Center;
        gameOverScoreTxt.color = Color.white;

        gameOverRestartButton = CreateGameOverMenuButton(canvasGo.transform, "RestartButton", "Start over", new Vector2(0.5f, 0.28f));
        gameOverQuitButton = CreateGameOverMenuButton(canvasGo.transform, "QuitButton", "Quit", new Vector2(0.5f, 0.18f));

        gameOverPanel = canvasGo;
    }

    static Button CreateGameOverMenuButton(Transform parent, string objectName, string label, Vector2 anchorCenter)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorCenter;
        rt.anchorMax = anchorCenter;
        rt.sizeDelta = new Vector2(260f, 48f);
        rt.anchoredPosition = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.22f, 0.95f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var tx = textGo.AddComponent<Text>();
        tx.text = label;
        tx.fontSize = 24;
        tx.alignment = TextAnchor.MiddleCenter;
        tx.color = Color.white;

        return btn;
    }

    public void NotifyScoredBallReturnedFromGround()
    {
        OnScoredBallGrounded?.Invoke();
    }

    public void NotifyAirBall()
    {
        string[] lines =
        {
            "Air ball. The ceiling crows are applauding.",
            "You aimed for the moon and hit nothing.",
            "No basket, but a great throw… of invisible confetti."
        };
        Debug.Log($"[Basket] {lines[UnityEngine.Random.Range(0, lines.Length)]}");
        PlayClip(airBallClip);
    }

    public void NotifyMissedShot(Vector3 missWorldPosition)
    {
        if (missShotVfxSpawnPoint == null)
            return;

        if (_missShotVfxRoutine != null)
            StopCoroutine(_missShotVfxRoutine);

        missShotVfxSpawnPoint.gameObject.SetActive(true);
        if (missShotVfxLifetime > 0f)
            _missShotVfxRoutine = StartCoroutine(MissShotVfxRoutine());
    }

    IEnumerator MissShotVfxRoutine()
    {
        yield return new WaitForSecondsRealtime(missShotVfxLifetime);
        if (missShotVfxSpawnPoint != null)
            missShotVfxSpawnPoint.gameObject.SetActive(false);
        _missShotVfxRoutine = null;
    }

    public void RequestHitStop(float realtimeSeconds, float timeScale)
    {
        if (_hitStopRoutine != null)
            StopCoroutine(_hitStopRoutine);
        _hitStopRoutine = StartCoroutine(HitStopRoutine(realtimeSeconds, timeScale));
    }

    IEnumerator HitStopRoutine(float realtimeSeconds, float timeScale)
    {
        Time.timeScale = Mathf.Clamp(timeScale, 0f, 1f);
        yield return new WaitForSecondsRealtime(realtimeSeconds);
        if (!PauseController.IsPaused && !IsGameOver && HasRoundStarted)
            Time.timeScale = 1f;
        _hitStopRoutine = null;
    }

    /// <summary>
    /// Freeze at the net, then first basket (slow-mo) or a light hit-stop on later ones.
    /// </summary>
    IEnumerator ScoreBasketTimeRoutine(bool firstBasket)
    {
        // Let RegisterBasket finish (audio / rest of frame) before Time.timeScale = 0.
        yield return null;

        float hi = Mathf.Max(scoreFreezeMinRealtime, scoreFreezeMaxRealtime);
        float lo = Mathf.Min(scoreFreezeMinRealtime, scoreFreezeMaxRealtime);
        if (hi > 0f)
        {
            float freezeDur = lo >= hi ? lo : UnityEngine.Random.Range(lo, hi);
            if (freezeDur > 0f)
            {
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(freezeDur);
            }
        }

        if (firstBasket)
        {
            if (!PauseController.IsPaused && !IsGameOver && HasRoundStarted)
                Time.timeScale = 0.45f;
            yield return new WaitForSecondsRealtime(0.38f);
        }
        else
        {
            if (!PauseController.IsPaused && !IsGameOver && HasRoundStarted)
                Time.timeScale = 0.18f;
            yield return new WaitForSecondsRealtime(0.04f);
        }

        if (!PauseController.IsPaused && !IsGameOver && HasRoundStarted)
            Time.timeScale = 1f;
        _hitStopRoutine = null;
    }

    void PlayImpactVfxPulse()
    {
        if (impactEffectVFX == null)
            return;
        if (_impactVfxRoutine != null)
            StopCoroutine(_impactVfxRoutine);
        _impactVfxRoutine = StartCoroutine(ImpactVfxRoutine());
    }

    IEnumerator ImpactVfxRoutine()
    {
        impactEffectVFX.SetActive(true);
        yield return new WaitForSecondsRealtime(impactEffectVisibleSeconds);
        impactEffectVFX.SetActive(false);
        _impactVfxRoutine = null;
    }

    void PlaySwishVfxPulse()
    {
        if (swishVFX == null)
            return;
        if (_swishVfxRoutine != null)
            StopCoroutine(_swishVfxRoutine);
        _swishVfxRoutine = StartCoroutine(SwishVfxRoutine());
    }

    IEnumerator SwishVfxRoutine()
    {
        swishVFX.SetActive(true);
        yield return new WaitForSecondsRealtime(swishVfxVisibleSeconds);
        if (swishVFX != null)
            swishVFX.SetActive(false);
        _swishVfxRoutine = null;
    }

    void PlayGoodVfxPulse()
    {
        if (goodScoreVFX == null)
            return;
        if (_goodVfxRoutine != null)
            StopCoroutine(_goodVfxRoutine);
        _goodVfxRoutine = StartCoroutine(GoodVfxRoutine());
    }

    IEnumerator GoodVfxRoutine()
    {
        goodScoreVFX.SetActive(true);
        yield return new WaitForSecondsRealtime(goodScoreVfxVisibleSeconds);
        if (goodScoreVFX != null)
            goodScoreVFX.SetActive(false);
        _goodVfxRoutine = null;
    }

    void PlayBasketClips(bool swish)
    {
        PlayClip(scoreImpactClip);
        AudioClip main = swish ? swishClip : (scoredWithRimClip != null ? scoredWithRimClip : swishClip);
        PlayClip(main);
    }

    void PlayClip(AudioClip clip, float volumeScale = 1f)
    {
        if (audioSource == null || clip == null)
            return;
        audioSource.pitch = UnityEngine.Random.Range(swishPitchMin, swishPitchMax);
        audioSource.PlayOneShot(clip, volumeScale);
        audioSource.pitch = 1f;
    }

    IEnumerator ShotCameraCelebrationRoutine()
    {
        Camera play = gameplayCamera != null ? gameplayCamera : Camera.main;
        if (play == null || shotCamera == null || play == shotCamera)
        {
            _shotCameraRoutine = null;
            yield break;
        }

        var playListener = play.GetComponent<AudioListener>();
        var shotListener = shotCamera.GetComponent<AudioListener>();

        _shotCamBackup = new ShotCamBackup
        {
            Valid = true,
            Gameplay = play,
            Shot = shotCamera,
            GameplayCamEnabled = play.enabled,
            ShotCamEnabled = shotCamera.enabled,
            ShotRootActive = shotCamera.gameObject.activeSelf,
            GameplayTag = play.tag,
            ShotTag = shotCamera.tag,
            GameplayListenerEnabled = playListener != null && playListener.enabled,
            ShotListenerEnabled = shotListener != null && shotListener.enabled
        };

        if (!shotCamera.gameObject.activeSelf)
            shotCamera.gameObject.SetActive(true);

        play.enabled = false;
        if (playListener != null)
            playListener.enabled = false;

        shotCamera.enabled = true;
        if (shotListener != null)
            shotListener.enabled = true;

        if (play.CompareTag("MainCamera"))
            play.tag = "Untagged";
        if (!shotCamera.CompareTag("MainCamera"))
            shotCamera.tag = "MainCamera";


        yield return new WaitForSecondsRealtime(shotCameraHoldSeconds);

        //RestoreShotCameraView();
        _shotCameraRoutine = null;
    }

    void RestoreShotCameraView()
    {
        if (!_shotCamBackup.Valid)
            return;

        Camera play = _shotCamBackup.Gameplay;
        Camera shot = _shotCamBackup.Shot;
        if (play != null)
        {
            play.enabled = _shotCamBackup.GameplayCamEnabled;
            play.tag = _shotCamBackup.GameplayTag;
            var playListener = play.GetComponent<AudioListener>();
            if (playListener != null)
                playListener.enabled = _shotCamBackup.GameplayListenerEnabled;
        }

        if (shot != null)
        {
            shot.enabled = _shotCamBackup.ShotCamEnabled;
            shot.tag = _shotCamBackup.ShotTag;
            var shotListener = shot.GetComponent<AudioListener>();
            if (shotListener != null)
                shotListener.enabled = _shotCamBackup.ShotListenerEnabled;
            if (!_shotCamBackup.ShotRootActive)
                shot.gameObject.SetActive(false);
        }

        _shotCamBackup.Valid = false;
    }

    void EnsureBackgroundMusicSource()
    {
        if (backgroundMusicClip == null)
            return;

        if (backgroundMusicSource == null)
        {
            var child = transform.Find("BackgroundMusicAudio");
            if (child != null)
                backgroundMusicSource = child.GetComponent<AudioSource>();
            if (backgroundMusicSource == null)
            {
                var go = new GameObject("BackgroundMusicAudio");
                go.transform.SetParent(transform, false);
                backgroundMusicSource = go.AddComponent<AudioSource>();
            }
        }

        backgroundMusicSource.playOnAwake = false;
        backgroundMusicSource.loop = true;
        backgroundMusicSource.clip = backgroundMusicClip;
        backgroundMusicSource.spatialBlend = 0f;
        backgroundMusicSource.volume = backgroundMusicVolume;
    }

    void TryStartBackgroundMusic()
    {
        if (backgroundMusicClip == null || backgroundMusicSource == null || _backgroundMusicStarted)
            return;

        backgroundMusicSource.volume = backgroundMusicVolume;
        backgroundMusicSource.Play();
        _backgroundMusicStarted = true;
    }

    void StopRoundAudio()
    {
        if (backgroundMusicSource != null)
            backgroundMusicSource.Stop();
        if (audioSource != null)
            audioSource.Stop();
    }

    void ConfigureStartScreen()
    {
        if (startButton == null)
        {
            HasRoundStarted = true;
            TryStartBackgroundMusic();
            return;
        }

        HasRoundStarted = false;
        if (startPanel != null)
            startPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void BootstrapDirectiveHint()
    {
        if (directiveRoot == null)
        {
            var found = GameObject.Find("Directive");
            if (found != null)
                directiveRoot = found;
        }

        if (directiveRoot == null)
            return;

        _directiveRt = directiveRoot.GetComponent<RectTransform>();
        _directiveBaseScale = _directiveRt != null ? _directiveRt.localScale : directiveRoot.transform.localScale;
        directiveRoot.SetActive(false);
        _directiveConsumed = false;
        _directiveIdleAccum = 0f;

        if (HasRoundStarted)
            ResetDirectiveIdleTimer();
    }

    void ResetDirectiveIdleTimer()
    {
        if (_directiveConsumed || directiveRoot == null)
            return;
        _directiveIdleAccum = 0f;
    }

    /// <summary>Call from player input (move, look, shoot, etc.) so the Directive idle hint stays reset.</summary>
    public void NotifyDirectiveGameplayInput()
    {
        if (_directiveConsumed || directiveRoot == null || _directiveCoroutine != null)
            return;
        _directiveIdleAccum = 0f;
    }

    void UpdateDirectiveHintIdle()
    {
        if (_directiveConsumed || directiveRoot == null)
            return;
        if (!HasRoundStarted || IsGameOver || PauseController.IsPaused)
            return;
        if (_directiveCoroutine != null)
            return;

        _directiveIdleAccum += Time.unscaledDeltaTime;
        if (_directiveIdleAccum >= directiveInactivitySeconds)
        {
            _directiveIdleAccum = 0f;
            _directiveCoroutine = StartCoroutine(DirectivePulseRoutine());
        }
    }

    IEnumerator DirectivePulseRoutine()
    {
        if (directiveRoot == null)
        {
            _directiveCoroutine = null;
            yield break;
        }

        directiveRoot.SetActive(true);
        float end = Time.unscaledTime + directivePulseDurationSeconds;

        while (Time.unscaledTime < end)
        {
            if (IsGameOver)
                break;

            float w = Mathf.Sin(Time.unscaledTime * directivePulseSpeed);
            float mul = 1f + directivePulseScaleAmplitude * w;
            if (_directiveRt != null)
                _directiveRt.localScale = _directiveBaseScale * mul;
            else
                directiveRoot.transform.localScale = _directiveBaseScale * mul;
            yield return null;
        }

        if (_directiveRt != null)
            _directiveRt.localScale = _directiveBaseScale;
        else
            directiveRoot.transform.localScale = _directiveBaseScale;

        directiveRoot.SetActive(false);
        _directiveConsumed = true;
        _directiveCoroutine = null;
    }

    void CancelDirectiveHintBecauseGameOver()
    {
        if (_directiveCoroutine != null)
        {
            StopCoroutine(_directiveCoroutine);
            _directiveCoroutine = null;
        }

        if (directiveRoot == null)
        {
            _directiveConsumed = true;
            return;
        }

        if (_directiveRt != null)
            _directiveRt.localScale = _directiveBaseScale;
        else
            directiveRoot.transform.localScale = _directiveBaseScale;
        directiveRoot.SetActive(false);
        _directiveConsumed = true;
    }

    public void BeginRoundFromStartUi()
    {
        if (HasRoundStarted || startButton == null)
            return;

        HasRoundStarted = true;
        TryStartBackgroundMusic();
        if (startPanel != null)
            startPanel.SetActive(false);

        UnregisterStartButtonListeners();

        if (!PauseController.IsPaused && !IsGameOver)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        OnRoundStarted?.Invoke();
        ResetDirectiveIdleTimer();
    }

    void RegisterStartButtonListeners()
    {
        if (startButton == null || HasRoundStarted)
            return;

        startButton.onClick.RemoveListener(BeginRoundFromStartUi);
        startButton.onClick.AddListener(BeginRoundFromStartUi);
    }

    void UnregisterStartButtonListeners()
    {
        if (startButton == null)
            return;
        startButton.onClick.RemoveListener(BeginRoundFromStartUi);
    }

    void OnDisable()
    {
        UnregisterGameOverButtonListeners();
        UnregisterStartButtonListeners();

        if (_directiveCoroutine != null)
        {
            StopCoroutine(_directiveCoroutine);
            _directiveCoroutine = null;
        }

        if (_shotCameraRoutine != null)
        {
            StopCoroutine(_shotCameraRoutine);
            _shotCameraRoutine = null;
            //RestoreShotCameraView();
        }
    }
}