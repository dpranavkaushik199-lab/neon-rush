using System.Collections.Generic;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Celeste-style feedback: pixel dust on jumps, landings, dashes and wall
    /// slides, and a ring of circles that bursts out on death and gathers back
    /// in on respawn.
    /// </summary>
    public class PlayerJuiceFX : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] PlayerVisuals25D visuals;
        [SerializeField] Sprite pixelSprite;
        [SerializeField] Sprite circleSprite;
        [SerializeField] Material material;
        [SerializeField] float pixelsPerUnit = 30f;

        [Header("Dust")]
        [SerializeField] Color dustColor = new Color(0.85f, 0.86f, 0.95f, 0.9f);
        [SerializeField] float wallDustInterval = 0.08f;

        [Header("Death / Respawn Ring")]
        [SerializeField] int ringCount = 8;
        [SerializeField] float ringRadius = 2.2f;
        [SerializeField] float ringTime = 0.5f;
        [SerializeField] float ringCircleSize = 0.4f;

        struct Particle
        {
            public SpriteRenderer sr;
            public Vector2 pos, vel;
            public float age, life, drag, size;
            public Color color;
        }

        struct Ring
        {
            public SpriteRenderer[] circles;
            public Vector2 center;
            public float born;
            public bool outward;
            public Color color;
        }

        readonly List<Particle> particles = new List<Particle>();
        readonly List<Ring> rings = new List<Ring>();
        readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
        float wallDustTimer;

        void OnEnable()
        {
            player.Jumped += OnJumped;
            player.Landed += OnLanded;
            player.Dashed += OnDashed;
            player.Died += OnDied;
            player.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            player.Jumped -= OnJumped;
            player.Landed -= OnLanded;
            player.Dashed -= OnDashed;
            player.Died -= OnDied;
            player.Respawned -= OnRespawned;
        }

        Vector2 Feet => new Vector2(player.Body.bounds.center.x, player.Body.bounds.min.y);

        void OnJumped()
        {
            // A wall jump kicks dust off the wall; a ground jump off the floor.
            int wall = player.WallSide;
            if (wall != 0)
                Puff(new Vector2(wall > 0 ? player.Body.bounds.max.x : player.Body.bounds.min.x, player.transform.position.y), new Vector2(-wall, 0.3f), 5);
            else
                Puff(Feet, Vector2.up, 5);
        }

        void OnLanded(float impact)
        {
            if (impact > 5f) Puff(Feet, Vector2.up, impact > 12f ? 8 : 5);
        }

        void OnDashed(Vector2 dir)
        {
            if (player.IsGrounded || dir.y > 0f) Puff(Feet, -dir + Vector2.up * 0.3f, 6);
        }

        void OnDied() => SpawnRing(player.transform.position, true);
        void OnRespawned() => SpawnRing(player.transform.position, false);

        void Puff(Vector2 at, Vector2 direction, int count)
        {
            direction = direction.normalized;
            for (int i = 0; i < count; i++)
            {
                Vector2 spread = Quaternion.Euler(0f, 0f, Random.Range(-70f, 70f)) * direction;
                Vector2 side = new Vector2(-direction.y, direction.x) * Random.Range(-0.25f, 0.25f);
                SpawnParticle(at + side, spread * Random.Range(1.5f, 3.5f), Random.Range(0.3f, 0.5f),
                    Random.Range(2, 4) / pixelsPerUnit, dustColor);
            }
        }

        void SpawnParticle(Vector2 pos, Vector2 vel, float life, float size, Color color)
        {
            var sr = Get(pixelSprite, 18);
            particles.Add(new Particle { sr = sr, pos = pos, vel = vel, life = life, drag = 6f, size = size, color = color });
        }

        void SpawnRing(Vector2 center, bool outward)
        {
            var circles = new SpriteRenderer[ringCount];
            for (int i = 0; i < ringCount; i++) circles[i] = Get(circleSprite, 45);
            Color c = visuals ? visuals.DashColor : new Color(0.675f, 0.196f, 0.196f);
            if (!outward) c = new Color(0.675f, 0.196f, 0.196f);
            rings.Add(new Ring { circles = circles, center = center, born = Time.time, outward = outward, color = c });
        }

        SpriteRenderer Get(Sprite sprite, int order)
        {
            SpriteRenderer sr;
            if (pool.Count > 0)
            {
                sr = pool.Pop();
                sr.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("FX");
                go.transform.SetParent(transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = material;
            }
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        void Release(SpriteRenderer sr)
        {
            sr.gameObject.SetActive(false);
            pool.Push(sr);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (player.IsWallSliding)
            {
                wallDustTimer -= dt;
                if (wallDustTimer <= 0f)
                {
                    wallDustTimer = wallDustInterval;
                    float x = player.WallSide > 0 ? player.Body.bounds.max.x : player.Body.bounds.min.x;
                    SpawnParticle(new Vector2(x, player.Body.bounds.max.y - 0.2f), new Vector2(-player.WallSide * 0.6f, 1.2f),
                        0.35f, 2f / pixelsPerUnit, dustColor);
                }
            }

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.age += dt;
                if (p.age >= p.life)
                {
                    Release(p.sr);
                    particles.RemoveAt(i);
                    continue;
                }
                p.vel *= Mathf.Exp(-p.drag * dt);
                p.pos += p.vel * dt;
                float t = p.age / p.life;
                SetPixel(p.sr, p.pos, p.size * (1f - t * 0.5f));
                var c = p.color;
                c.a *= 1f - t;
                p.sr.color = c;
                particles[i] = p;
            }

            for (int r = rings.Count - 1; r >= 0; r--)
            {
                var ring = rings[r];
                float t = (Time.time - ring.born) / ringTime;
                if (t >= 1f)
                {
                    foreach (var sr in ring.circles) Release(sr);
                    rings.RemoveAt(r);
                    continue;
                }
                float ease = 1f - (1f - t) * (1f - t);
                float radius = ringRadius * (ring.outward ? ease : 1f - ease);
                float size = ringCircleSize * (ring.outward ? 1f - t * 0.7f : 0.3f + t * 0.7f);
                for (int i = 0; i < ring.circles.Length; i++)
                {
                    float angle = (i / (float)ring.circles.Length + t * 0.5f) * Mathf.PI * 2f;
                    Vector2 pos = ring.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    SetPixel(ring.circles[i], pos, size);
                    // Flicker between hair colour and white, like Celeste's death bubbles.
                    bool white = Mathf.Repeat(Time.time * 20f + i, 2f) < 1f;
                    ring.circles[i].color = white ? Color.white : ring.color;
                }
            }
        }

        void SetPixel(SpriteRenderer sr, Vector2 pos, float size)
        {
            float snap = 1f / pixelsPerUnit;
            sr.transform.position = new Vector3(Mathf.Round(pos.x / snap) * snap, Mathf.Round(pos.y / snap) * snap, 0f);
            float native = sr.sprite.bounds.size.x;
            sr.transform.localScale = Vector3.one * (Mathf.Max(snap, size) / native);
        }
    }
}
