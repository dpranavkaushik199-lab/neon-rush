using UnityEngine;

namespace NeonRunner
{
    /// <summary>Buzzing neon: subtle noise plus occasional hard drop-outs on a light and its glowing tubes.</summary>
    public class NeonFlicker : MonoBehaviour
    {
        [SerializeField] Light lightSource;
        [SerializeField] Renderer[] tubes = new Renderer[0];
        [SerializeField] Color tubeColor = Color.magenta;
        [SerializeField] float noiseAmount = 0.15f;
        [SerializeField] float dropoutChancePerSecond = 0.35f;
        [SerializeField] Vector2 dropoutDuration = new Vector2(0.04f, 0.18f);

        float baseIntensity;
        float offUntil;
        float seed;

        void Start()
        {
            seed = Random.value * 100f;
            if (lightSource) baseIntensity = lightSource.intensity;
        }

        void Update()
        {
            if (Time.time >= offUntil && Random.value < dropoutChancePerSecond * Time.deltaTime)
                offUntil = Time.time + Random.Range(dropoutDuration.x, dropoutDuration.y);

            bool off = Time.time < offUntil;
            float k = off ? 0.08f : 1f - noiseAmount + Mathf.PerlinNoise(seed, Time.time * 12f) * noiseAmount * 2f;

            if (lightSource) lightSource.intensity = baseIntensity * k;
            var c = tubeColor * Mathf.Lerp(0.2f, 1f, k);
            c.a = 1f;
            foreach (var tube in tubes) Tint.Set(tube, c);
        }
    }
}
