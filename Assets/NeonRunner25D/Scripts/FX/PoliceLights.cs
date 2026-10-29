using UnityEngine;

namespace NeonRunner
{
    /// <summary>Red/blue light bar on a rooftop. Idles slowly; strobes hard while the player is wanted.</summary>
    public class PoliceLights : MonoBehaviour
    {
        [SerializeField] Light red;
        [SerializeField] Light blue;
        [SerializeField] Renderer redBar;
        [SerializeField] Renderer blueBar;
        [SerializeField] float intensity = 6f;

        float offset;

        void Start() => offset = Random.value * 2f;

        void Update()
        {
            bool alarm = CopBase.AnyChasing;
            float rate = alarm ? 7f : 1.6f;
            float t = Mathf.Repeat((Time.time + offset) * rate, 2f);
            // Each side double-flashes in its half of the cycle.
            bool redOn = t < 1f && Mathf.Repeat(t, 0.5f) < 0.3f;
            bool blueOn = t >= 1f && Mathf.Repeat(t, 0.5f) < 0.3f;
            float k = alarm ? 1f : 0.45f;
            if (red) red.intensity = redOn ? intensity * k : 0f;
            if (blue) blue.intensity = blueOn ? intensity * k : 0f;
            if (redBar) Tint.Set(redBar, new Color(1f, 0.1f, 0.1f) * (redOn ? 4f : 0.3f));
            if (blueBar) Tint.Set(blueBar, new Color(0.15f, 0.35f, 1f) * (blueOn ? 4f : 0.3f));
        }
    }
}
