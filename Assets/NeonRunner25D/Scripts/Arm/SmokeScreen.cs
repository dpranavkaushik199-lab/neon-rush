using System.Collections.Generic;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// AoE cloud that blocks cop line-of-sight and knocks nearby cops back to patrol.
    /// Drawn as layered fog: a dense core, billows that burst outward and slow down,
    /// a low skirt hugging the ground, and thin wisps that curl upward and peel off,
    /// spread over depth so it reads as a volume. Shading runs lighter on top.
    /// </summary>
    public class SmokeScreen : MonoBehaviour
    {
        public static readonly List<SmokeScreen> Active = new List<SmokeScreen>();

        const float ExpandTime = 0.35f;
        const float FadeTime = 1.1f;
        const float ConfuseTime = 1.5f;

        enum Layer { Core, Billow, Skirt, Wisp }

        struct Puff
        {
            public SpriteRenderer sr;
            public Layer layer;
            public Vector2 start, target;
            public float size, spin, phase, z, shade, delay;
        }

        float radius;
        float duration;
        float age;
        Color color;
        Puff[] puffs;

        public Vector2 Center => transform.position;
        public float CurrentRadius => radius * Mathf.Clamp01(age / ExpandTime);

        public static SmokeScreen Spawn(Vector2 position, float radius, float duration, Sprite sprite, Material material, Color color)
        {
            var go = new GameObject("SmokeScreen");
            go.transform.position = position;
            var smoke = go.AddComponent<SmokeScreen>();
            smoke.radius = radius;
            smoke.duration = duration;
            smoke.color = color;

            bool low = PerformanceManager.LowEnd;
            var list = new List<Puff>();
            void Add(Layer layer, int count, float minDist, float maxDist, Vector2 sizeRange, Vector2 squash, float zSpread)
            {
                for (int i = 0; i < (low ? (count + 1) / 2 : count); i++)
                {
                    var p = new GameObject("Puff_" + layer);
                    p.transform.SetParent(go.transform, false);
                    var sr = p.AddComponent<SpriteRenderer>();
                    sr.sprite = sprite;
                    sr.sharedMaterial = material;
                    float ang = Random.value * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(ang) * squash.x, Mathf.Sin(ang) * squash.y);
                    float dist = Random.Range(minDist, maxDist) * radius;
                    list.Add(new Puff
                    {
                        sr = sr,
                        layer = layer,
                        start = dir * dist * 0.15f,
                        target = dir * dist,
                        size = Random.Range(sizeRange.x, sizeRange.y) * radius,
                        spin = Random.Range(-35f, 35f),
                        phase = Random.value * 10f,
                        z = Random.Range(-zSpread, zSpread),
                        delay = layer == Layer.Wisp ? Random.Range(0.1f, duration * 0.6f) : Random.Range(0f, 0.08f),
                    });
                }
            }

            Add(Layer.Skirt, 5, 0.3f, 0.95f, new Vector2(0.7f, 1.0f), new Vector2(1.25f, 0.12f), 1.2f);
            Add(Layer.Core, 6, 0f, 0.25f, new Vector2(0.9f, 1.25f), new Vector2(1f, 0.8f), 0.6f);
            Add(Layer.Billow, 16, 0.35f, 0.85f, new Vector2(0.45f, 0.8f), new Vector2(1.1f, 0.85f), 1.8f);
            Add(Layer.Wisp, 10, 0.5f, 1.05f, new Vector2(0.18f, 0.32f), new Vector2(1f, 0.6f), 1.5f);

            smoke.puffs = list.ToArray();
            for (int i = 0; i < smoke.puffs.Length; i++)
            {
                ref var p = ref smoke.puffs[i];
                // Back-to-front draw order by depth so overlapping puffs layer believably.
                p.sr.sortingOrder = 25 + Mathf.RoundToInt(-p.z * 4f);
                p.sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.value * 360f);
                p.sr.color = Color.clear;
            }
            return smoke;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Start()
        {
            foreach (var cop in CopBase.All)
            {
                bool inCloud = Vector2.Distance(cop.transform.position, Center) <= radius;
                if (inCloud || cop.State == CopBase.Awareness.Chase) cop.BreakLineOfSight(ConfuseTime);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= duration)
            {
                Destroy(gameObject);
                return;
            }

            float fadeOut = Mathf.Clamp01((duration - age) / FadeTime);
            for (int i = 0; i < puffs.Length; i++)
            {
                ref var p = ref puffs[i];
                var tr = p.sr.transform;
                float local = age - p.delay;
                if (local < 0f) continue;

                // Burst out fast, then drag to a stop; the whole cloud slowly sinks and spreads.
                float burst = 1f - Mathf.Exp(-local / ExpandTime * 2.2f);
                Vector2 pos = Vector2.Lerp(p.start, p.target, burst);
                float breathe = 1f + Mathf.Sin(Time.time * 1.6f + p.phase) * 0.07f;
                float size = p.size * Mathf.Lerp(0.35f, 1f, burst) * breathe * (1f + age * 0.06f);
                float alpha;

                switch (p.layer)
                {
                    case Layer.Core:
                        alpha = 0.75f;
                        break;
                    case Layer.Skirt:
                        pos.y -= radius * 0.35f;
                        size *= 1f + age * 0.08f;
                        alpha = 0.55f;
                        break;
                    case Layer.Wisp:
                    {
                        // Tendrils peel off the edge, rise and curl, and are gone in about a second.
                        float life = Mathf.Clamp01(local / 1.3f);
                        pos += new Vector2(Mathf.Sin(local * 2.4f + p.phase) * 0.4f, local * 0.9f);
                        size *= Mathf.Lerp(0.8f, 1.6f, life);
                        alpha = Mathf.Sin(life * Mathf.PI) * 0.5f;
                        if (life >= 1f) p.delay = age + Random.Range(0.05f, 0.5f);
                        break;
                    }
                    default:
                        pos += new Vector2(Mathf.Sin(Time.time * 0.7f + p.phase), Mathf.Cos(Time.time * 0.6f + p.phase)) * 0.12f;
                        alpha = 0.6f;
                        break;
                }

                tr.localPosition = new Vector3(pos.x, pos.y, p.z);
                tr.localScale = new Vector3(size, size, 1f);
                tr.Rotate(0f, 0f, p.spin * Time.deltaTime);

                // Fake top light: upper puffs are paler, lower ones darker and denser.
                float height = Mathf.Clamp(pos.y / radius, -1f, 1f);
                Color c = Color.Lerp(color * 0.7f, Color.Lerp(color, Color.white, 0.35f), height * 0.5f + 0.5f);
                // Outer puffs thin out first as the cloud dissipates.
                float edge = Mathf.Clamp01(pos.magnitude / radius);
                float dissolve = Mathf.Clamp01(fadeOut * (1.6f - edge * 0.6f));
                c.a = color.a * alpha * Mathf.Clamp01(burst * 3f) * dissolve;
                p.sr.color = c;
            }
        }

        /// <summary>True if any active cloud covers either point or the segment between them.</summary>
        public static bool BlocksSight(Vector2 from, Vector2 to)
        {
            foreach (var s in Active)
            {
                float r = s.CurrentRadius;
                if (r <= 0f) continue;
                Vector2 c = s.Center;
                Vector2 seg = to - from;
                float t = seg.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(c - from, seg) / seg.sqrMagnitude) : 0f;
                if ((from + seg * t - c).sqrMagnitude <= r * r) return true;
            }
            return false;
        }
    }
}
