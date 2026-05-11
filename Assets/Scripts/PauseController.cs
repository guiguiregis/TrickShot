using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Escape toggles pause: freezes time, shows pause UI, unlocks cursor.
/// </summary>
public class PauseController : MonoBehaviour
{
    public static bool IsPaused { get; private set; }

    /// <summary>
    /// Clears static pause state and restores <see cref="Time.timeScale"/> before reloading the scene.
    /// Call from pause Restart and game-over Start over so time scale and pause flag cannot carry over.
    /// </summary>
    public static void ResetTimeScaleAndPauseState()
    {
        IsPaused = false;
        Time.timeScale = 1f;
    }

    [SerializeField] GameObject pausePanel;
    [SerializeField] bool createPauseUiIfMissing = true;

    [Header("Pause menu buttons")]
    [SerializeField] Button resumeButton;
    [SerializeField] Button restartButton;
    [SerializeField] Button closeApplicationButton;

    float _timeScaleBeforePause = 1f;

    void Awake()
    {
        if (pausePanel == null && createPauseUiIfMissing)
            pausePanel = BuildDefaultPausePanel();

        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    void OnEnable() => RegisterButtonListeners();

    void OnDisable() => UnregisterButtonListeners();

    void OnDestroy()
    {
        if (IsPaused)
            Time.timeScale = _timeScaleBeforePause > 0f ? _timeScaleBeforePause : 1f;
        IsPaused = false;
    }

    void RegisterButtonListeners()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveListener(Resume);
            resumeButton.onClick.AddListener(Resume);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(Restart);
            restartButton.onClick.AddListener(Restart);
        }

        if (closeApplicationButton != null)
        {
            closeApplicationButton.onClick.RemoveListener(CloseApplication);
            closeApplicationButton.onClick.AddListener(CloseApplication);
        }
    }

    void UnregisterButtonListeners()
    {
        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(Resume);
        if (restartButton != null)
            restartButton.onClick.RemoveListener(Restart);
        if (closeApplicationButton != null)
            closeApplicationButton.onClick.RemoveListener(CloseApplication);
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
            return;

        if (GameManager.Instance != null && !GameManager.Instance.HasRoundStarted)
            return;

        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame)
            return;

        if (IsPaused)
            Resume();
        else
            Pause();
    }

    void Pause()
    {
        if (IsPaused)
            return;

        _timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;
        IsPaused = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        Time.timeScale = _timeScaleBeforePause > 0f ? _timeScaleBeforePause : 1f;
        IsPaused = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>Reloads the active scene (time scale reset).</summary>
    public void Restart()
    {
        ResetTimeScaleAndPauseState();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
    }

    public void CloseApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    GameObject BuildDefaultPausePanel()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        var canvasGo = new GameObject("PauseCanvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
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
        dimImg.color = new Color(0f, 0f, 0f, 0.55f);

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(canvasGo.transform, false);
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.58f);
        titleRt.anchorMax = new Vector2(0.5f, 0.58f);
        titleRt.sizeDelta = new Vector2(800f, 120f);
        titleRt.anchoredPosition = Vector2.zero;
        var title = titleGo.AddComponent<Text>();
        title.fontSize = 56;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.text = "PAUSED";

        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(canvasGo.transform, false);
        var hintRt = hintGo.AddComponent<RectTransform>();
        hintRt.anchorMin = new Vector2(0.5f, 0.42f);
        hintRt.anchorMax = new Vector2(0.5f, 0.42f);
        hintRt.sizeDelta = new Vector2(900f, 80f);
        hintRt.anchoredPosition = Vector2.zero;
        var hint = hintGo.AddComponent<Text>();
        hint.fontSize = 28;
        hint.alignment = TextAnchor.MiddleCenter;
        hint.color = new Color(1f, 1f, 1f, 0.85f);
        hint.text = "Press Esc to resume";

        resumeButton = CreateMenuButton(canvasGo.transform, "ResumeButton", "Resume", new Vector2(0.5f, 0.28f));
        restartButton = CreateMenuButton(canvasGo.transform, "RestartButton", "Restart", new Vector2(0.5f, 0.20f));
        closeApplicationButton = CreateMenuButton(canvasGo.transform, "QuitButton", "Quit", new Vector2(0.5f, 0.12f));

        return canvasGo;
    }

    static Button CreateMenuButton(Transform parent, string objectName, string label, Vector2 anchorCenter)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorCenter;
        rt.anchorMax = anchorCenter;
        rt.sizeDelta = new Vector2(220f, 44f);
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
        tx.fontSize = 22;
        tx.alignment = TextAnchor.MiddleCenter;
        tx.color = Color.white;

        return btn;
    }
}
