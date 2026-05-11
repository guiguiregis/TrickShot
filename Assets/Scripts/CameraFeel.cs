using UnityEngine;

public class CameraFeel : MonoBehaviour
{
    public static CameraFeel Instance { get; private set; }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
