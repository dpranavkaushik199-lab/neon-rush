using UnityEngine;
using UnityEngine.Rendering;

namespace NeonRunner
{
    /// <summary>
    /// Feedback hub for big movement moments: sonic-boom rings, camera shake,
    /// the boost post-process volume (chromatic aberration + lens distortion as
    /// the motion-blur stand-in) and timing popups.
    /// </summary>
    public class BoostFX : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] CameraFollow2D cameraFollow;
        [SerializeField] Volume boostVolume;
        [SerializeField] Sprite ringSprite;
        [SerializeField] Material unlitMaterial;
        [SerializeField] Color ringColor = new Color(0.4f, 1f, 1f, 0.9f);
        [SerializeField] Color perfectRingColor = new Color(1f, 0.3f, 0.9f, 1f);
        [SerializeField] float ringLifetime = 0.4f;
        [SerializeField] float ringMaxScale = 7f;

        struct Ring { public SpriteRenderer sr; public float born; public float delay; public Color color; }
        readonly System.Collections.Generic.List<Ring> rings = new System.Collections.Generic.List<Ring>();

        void OnEnable()
        {
            player.SlingBoosted += OnBoost;
            player.Dashed += OnDash;
            player.Died += OnDied;
            if (player.Grapple) player.Grapple.Hacked += OnHacked;
        }

        void OnDisable()
        {
            player.SlingBoosted -= OnBoost;
            player.Dashed -= OnDash;
            player.Died -= OnDied;
            if (player.Grapple) player.Grapple.Hacked -= OnHacked;
        }

        void OnBoost(TimingGrade grade, Vector2 dir)
        {
            bool perfect = grade == TimingGrade.Perfect;
            Color c = perfect ? perfectRingColor : ringColor;
            SpawnRing(player.transform.position, c, 0f, dir);
            SpawnRing(player.transform.position, c, 0.07f, dir);
            if (perfect) SpawnRing(player.transform.position, Color.white, 0.14f, dir);

            if (cameraFollow) cameraFollow.AddTrauma(perfect ? 0.75f : 0.55f);
            if (boostVolume) boostVolume.weight = 1f;
            GameManager.Instance?.ShowPopup(perfect ? "PERFECT SLING!" : "SLING-BOOST", c, 0.8f);
        }

        void OnDash(Vector2 dir)
        {
            if (cameraFollow) cameraFollow.AddTrauma(0.15f);
            if (boostVolume) boostVolume.weight = Mathf.Max(boostVolume.weight, 0.3f);
        }

        void OnHacked(GrappleTarget target)
        {
            SpawnRing(target.Anchor, new Color(0.3f, 1f, 0.6f, 0.9f), 0f, Vector2.right, 0.5f);
            if (cameraFollow) cameraFollow.AddTrauma(0.25f);
            GameManager.Instance?.ShowPopup("DRONE HACKED", new Color(0.3f, 1f, 0.6f), 0.7f);
        }

        void OnDied()
        {
            SpawnRing(player.transform.position, new Color(1f, 0.15f, 0.3f, 1f), 0f, Vector2.right, 0.6f);
            if (cameraFollow) cameraFollow.AddTrauma(0.6f);
        }

        void SpawnRing(Vector2 position, Color color, float delay, Vector2 dir, float scale = 1f)
        {
            var go = new GameObject("SonicBoom");
            go.transform.position = position;
            // Squash the ring along the boost direction for a shockwave look.
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ringSprite;
            sr.sharedMaterial = unlitMaterial;
            sr.sortingOrder = 40;
            sr.color = Color.clear;
            go.transform.localScale = Vector3.one * 0.3f * scale;
            rings.Add(new Ring { sr = sr, born = Time.unscaledTime, delay = delay, color = color });
        }

        void Update()
        {
            if (boostVolume)
            {
                float rest = player.IsBoosting ? 0.6f : 0f;
                boostVolume.weight = Mathf.MoveTowards(boostVolume.weight, rest, Time.unscaledDeltaTime * 2.5f);
            }

            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                float t = (Time.unscaledTime - r.born - r.delay) / ringLifetime;
                if (t < 0f) continue;
                if (t >= 1f)
                {
                    Destroy(r.sr.gameObject);
                    rings.RemoveAt(i);
                    continue;
                }
                float ease = 1f - (1f - t) * (1f - t);
                float s = Mathf.Lerp(0.3f, ringMaxScale, ease);
                r.sr.transform.localScale = new Vector3(s * 0.55f, s, 1f);
                var c = r.color;
                c.a *= 1f - t;
                r.sr.color = c;
            }
        }
    }
}
