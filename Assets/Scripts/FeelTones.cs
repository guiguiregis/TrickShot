using UnityEngine;

public static class FeelTones
{
    public static AudioClip Ring(float frequency, float duration, float amplitude = 0.2f, int sampleRate = 44100)
    {
        int samples = Mathf.Max(1, Mathf.CeilToInt(sampleRate * duration));
        var data = new float[samples];
        float decay = 14f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            float env = Mathf.Exp(-t * decay);
            float wobble = Mathf.Sin(2f * Mathf.PI * (frequency * 0.5f) * t) * 0.15f;
            data[i] = Mathf.Sin(2f * Mathf.PI * (frequency + wobble) * t) * amplitude * env;
        }

        var clip = AudioClip.Create("feel_tone", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
