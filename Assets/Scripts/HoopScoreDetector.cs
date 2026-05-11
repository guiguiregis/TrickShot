using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HoopScoreDetector : MonoBehaviour
{
    [SerializeField] float minDownwardSpeed = 0.35f;
    [Tooltip("Marge (m) : le centre du ballon devait être au-dessus du plan d'entrée à l'étape Fixed précédente.")]
    [SerializeField] float approachEpsilon = 0.03f;
    [SerializeField] bool useCustomEntryPlane;
    [Tooltip("Hauteur monde Y du plan « par le haut » (si useCustomEntryPlane). Sinon : sommet des bounds du trigger.")]
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
