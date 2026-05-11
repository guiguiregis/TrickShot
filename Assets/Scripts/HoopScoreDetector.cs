using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HoopScoreDetector : MonoBehaviour
{
    [SerializeField] float minDownwardSpeed = 0.35f;
    [Tooltip("Margin (m): ball center had to be above the entry plane on the previous Fixed step.")]
    [SerializeField] float approachEpsilon = 0.03f;
    [SerializeField] bool useCustomEntryPlane;
    [Tooltip("World Y of the top entry plane (if useCustomEntryPlane). Otherwise: top of the trigger bounds.")]
    [SerializeField] float customEntryPlaneWorldY = 3f;

    Collider _trigger;

    void Awake()
    {
        _trigger = GetComponent<Collider>();
    }

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    float EntryPlaneWorldY()
    {
        if (useCustomEntryPlane)
            return customEntryPlaneWorldY;
        return _trigger != null ? _trigger.bounds.max.y : transform.position.y;
    }

    void OnTriggerEnter(Collider other)
    {
        var feel = other.GetComponent<BallFeel>();
        if (feel == null || feel.IsHeld || feel.ScoredThisLaunch)
            return;
        var ball = other.attachedRigidbody;
        if (ball == null)
            return;

        if (ball.linearVelocity.y > -minDownwardSpeed)
            return;

        float planeY = EntryPlaneWorldY();
        Vector3 prevCenter = ball.position - ball.linearVelocity * Time.fixedDeltaTime;
        if (prevCenter.y < planeY - approachEpsilon)
            return;

        feel.NotifyScored();
        bool swish = !feel.HadRimContactRecently;
        GameManager.Instance?.RegisterBasket(swish);
    }
}
