using System.Collections;
using UnityEngine;

/// <summary>
/// Brief anchored-position shake for HUD <see cref="RectTransform"/>s; uses unscaled time so it plays during hit-stop.
/// </summary>
public static class UiRectShake
{
    public static IEnumerator AnchoredShake(RectTransform rt, Vector2 restoreAnchored, float duration, float magnitudePixels)
    {
        if (rt == null || duration <= 0f || magnitudePixels <= 0f)
            yield break;

        float elapsed = 0f;
        float seed = Random.value * 1000f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float damp = 1f - Mathf.Clamp01(elapsed / duration);
            float w = elapsed * 48f + seed;
            var off = new Vector2(
                (Mathf.PerlinNoise(w, seed) - 0.5f) * 2f,
                (Mathf.PerlinNoise(seed, w) - 0.5f) * 2f) * magnitudePixels * damp;
            rt.anchoredPosition = restoreAnchored + off;
            yield return null;
        }

        rt.anchoredPosition = restoreAnchored;
    }
}
