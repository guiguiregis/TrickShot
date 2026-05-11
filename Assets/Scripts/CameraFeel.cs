using UnityEngine;

public class CameraFeel : MonoBehaviour
{
    public static CameraFeel Instance { get; private set; }

    [SerializeField] float shakeDecay = 14f;
    [SerializeField] float rimShakeMag = 0.035f;
    [SerializeField] float boardShakeMag = 0.05f;
    [SerializeField] float basketShakeMag = 0.072f;

    Vector3 _shake;
    Vector3 _baseLocalPosition;

    void Awake()
    {
        Instance = this;
        _baseLocalPosition = transform.localPosition;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void LateUpdate()
    {
        _shake = Vector3.Lerp(_shake, Vector3.zero, Time.deltaTime * shakeDecay);
        transform.localPosition = _baseLocalPosition + _shake;
    }
}
