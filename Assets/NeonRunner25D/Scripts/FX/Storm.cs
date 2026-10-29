using UnityEngine;

namespace NeonRunner
{
    /// <summary>Lightning: double flashes that blow out the moonlight, then a rumble of camera shake.</summary>
    public class Storm : MonoBehaviour
    {
        [SerializeField] Light moon;
        [SerializeField] CameraFollow2D cameraFollow;
        [SerializeField] Vector2 interval = new Vector2(5f, 12f);
        [SerializeField] float flashIntensity = 7f;

        float baseIntensity;
        Color baseAmbient;
        float nextStrike;
        float strikeTime = -10f;
        bool rumbled = true;

        void Start()
        {
            if (moon) baseIntensity = moon.intensity;
            baseAmbient = RenderSettings.ambientLight;
            nextStrike = Time.time + Random.Range(interval.x, interval.y);
        }

        void Update()
        {
            if (Time.time >= nextStrike)
            {
                strikeTime = Time.time;
                rumbled = false;
                nextStrike = Time.time + Random.Range(interval.x, interval.y);
            }

            float t = Time.time - strikeTime;
            // Two quick strobes and a fading afterglow.
            float flash = t < 0.06f ? 1f : t < 0.12f ? 0.15f : t < 0.2f ? 0.8f : Mathf.Clamp01(1f - (t - 0.2f) / 0.5f) * 0.3f;
            if (moon) moon.intensity = baseIntensity + flash * flashIntensity;
            RenderSettings.ambientLight = baseAmbient + new Color(0.5f, 0.55f, 0.65f) * flash;

            if (!rumbled && t > 0.6f)
            {
                rumbled = true;
                if (cameraFollow) cameraFollow.AddTrauma(0.22f);
            }
        }

        void OnDisable()
        {
            if (moon) moon.intensity = baseIntensity;
            RenderSettings.ambientLight = baseAmbient;
        }
    }
}
