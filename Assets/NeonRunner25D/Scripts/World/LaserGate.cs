using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Lethal laser curtain. Timed gates cycle on and off with a blinking warning before
    /// they fire; powered gates stay on until every linked switch is on.
    /// </summary>
    public class LaserGate : MonoBehaviour
    {
        [SerializeField] Collider2D killCollider;
        [SerializeField] Renderer[] beams = new Renderer[0];
        [SerializeField] Light glow;
        [SerializeField] Color color = new Color(1f, 0.15f, 0.25f);
        [Header("Timing (when no switches are linked)")]
        [SerializeField] float onTime = 1.2f;
        [SerializeField] float offTime = 1.3f;
        [Range(0f, 1f)] [SerializeField] float phase;
        [SerializeField] float warnTime = 0.45f;
        [Header("Puzzle")]
        [Tooltip("If set, the gate is on until all of these are on (timing is ignored).")]
        [SerializeField] PowerSwitch[] switches = new PowerSwitch[0];

        float baseGlow;

        void Start()
        {
            if (glow) baseGlow = glow.intensity;
        }

        bool AllSwitchesOn()
        {
            foreach (var s in switches)
                if (!s || !s.IsOn) return false;
            return true;
        }

        void Update()
        {
            bool on;
            float strength;
            if (switches.Length > 0)
            {
                on = !AllSwitchesOn();
                strength = on ? 1f + Mathf.Sin(Time.time * 40f) * 0.1f : 0f;
            }
            else
            {
                float cycle = onTime + offTime;
                float t = Mathf.Repeat(Time.time + phase * cycle, cycle);
                on = t < onTime;
                bool warn = !on && t > cycle - warnTime;
                strength = on ? 1f + Mathf.Sin(Time.time * 40f) * 0.1f : warn ? (Mathf.Repeat(Time.time, 0.12f) < 0.06f ? 0.45f : 0.1f) : 0.05f;
            }

            if (killCollider) killCollider.enabled = on;
            foreach (var b in beams)
            {
                if (!b) continue;
                b.enabled = strength > 0.01f;
                Tint.Set(b, color * (3f * strength));
            }
            if (glow) glow.intensity = baseGlow * strength;
        }
    }
}
