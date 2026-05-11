using UnityEngine;

public class NetBallRegistrar : MonoBehaviour
{
    [SerializeField] private Cloth netCloth;

    public void RegisterBall(SphereCollider ballCollider)
    {
        var existing = netCloth.sphereColliders;
        var updated = new ClothSphereColliderPair[existing.Length + 1];
        existing.CopyTo(updated, 0);
        updated[existing.Length] = new ClothSphereColliderPair(ballCollider);
        netCloth.sphereColliders = updated;
    }

    public void UnregisterBall(SphereCollider ballCollider)
    {
        var filtered = System.Array.FindAll(
            netCloth.sphereColliders,
            p => p.first != ballCollider);
        netCloth.sphereColliders = filtered;
    }
}