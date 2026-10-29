using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// "Compressed" audio mode: a feed-forward compressor on the final mix (it sits next to the
    /// AudioListener). Peaks above the threshold are turned down 4:1, then make-up gain lifts the
    /// whole mix, so quiet sounds come up and loud ones come down - good for laptop speakers.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class DynamicRangeCompressor : MonoBehaviour
    {
        const float ThresholdDb = -20f;
        const float Ratio = 4f;
        const float MakeupDb = 8f;
        const float AttackSeconds = 0.005f;
        const float ReleaseSeconds = 0.15f;

        // Written on the main thread, read on the audio thread.
        volatile bool active;
        float attackCoef, releaseCoef;
        float envelope;

        void Awake()
        {
            int rate = AudioSettings.outputSampleRate;
            attackCoef = Mathf.Exp(-1f / (AttackSeconds * rate));
            releaseCoef = Mathf.Exp(-1f / (ReleaseSeconds * rate));
        }

        void Update() => active = GameSettings.CompressedAudio;

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!active) return;
            float makeup = Mathf.Pow(10f, MakeupDb / 20f);
            for (int i = 0; i < data.Length; i += channels)
            {
                // Linked stereo: one gain for all channels from the loudest one.
                float peak = 0f;
                for (int c = 0; c < channels; c++) peak = Mathf.Max(peak, Mathf.Abs(data[i + c]));
                envelope = peak > envelope ? attackCoef * envelope + (1f - attackCoef) * peak : releaseCoef * envelope + (1f - releaseCoef) * peak;

                float levelDb = 20f * Mathf.Log10(Mathf.Max(envelope, 1e-6f));
                float overDb = levelDb - ThresholdDb;
                float gainDb = overDb > 0f ? -overDb * (1f - 1f / Ratio) : 0f;
                float gain = Mathf.Pow(10f, gainDb / 20f) * makeup;

                for (int c = 0; c < channels; c++) data[i + c] = Mathf.Clamp(data[i + c] * gain, -1f, 1f);
            }
        }
    }
}
