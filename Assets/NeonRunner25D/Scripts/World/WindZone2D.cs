using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Rooftop updraft / crosswind: while the gust blows, everything inside drifts at a steady
    /// speed (independent of how the player is moving, so it can carry a jump or fight a run).
    /// Gusts can pulse on and off; a soft, misty current of air shows direction and strength.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class WindZone2D : MonoBehaviour
    {
        [SerializeField] Vector2 wind = new Vector2(-5f, 0f);
        [Tooltip("0 = always blowing; otherwise seconds per on/off cycle.")]
        [SerializeField] float pulsePeriod;
        [Range(0f, 1f)] [SerializeField] float phase;
        [SerializeField] Sprite wispSprite;
        [SerializeField] Material material;
        [SerializeField] int wispCount = 26;
        [SerializeField] Color color = new Color(0.8f, 0.9f, 1f, 0.12f);

        BoxCollider2D area;
        PlayerController player;
        Transform[] wisps;
        SpriteRenderer[] renderers;
        Vector2[] offsets;
        float strength;

        public bool Blowing => pulsePeriod <= 0f || Mathf.Repeat(Time.time / pulsePeriod + phase, 1f) < 0.55f;

        void Awake() => area = GetComponent<BoxCollider2D>();

        void Start()
        {
            player = GameManager.Instance ? GameManager.Instance.Player : FindAnyObjectByType<PlayerController>();
            int count = PerformanceManager.LowEnd ? wispCount / 2 : wispCount;
            wisps = new Transform[count];
            renderers = new SpriteRenderer[count];
            offsets = new Vector2[count];
            Vector2 size = area.size;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Gust");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = wispSprite;
                sr.sharedMaterial = material;
                sr.sortingOrder = 20;
                wisps[i] = go.transform;
                renderers[i] = sr;
                offsets[i] = new Vector2(Random.Range(-0.5f, 0.5f) * size.x, Random.Range(-0.5f, 0.5f) * size.y);
            }
        }

        void FixedUpdate()
        {
            strength = Mathf.MoveTowards(strength, Blowing ? 1f : 0f, Time.fixedDeltaTime * 3f);
            if (strength <= 0f || !player || player.IsDead) return;
            if (area.bounds.Intersects(player.Body.bounds)) player.Carry(wind * strength * Time.fixedDeltaTime);
        }

        void LateUpdate()
        {
            if (wisps == null) return;
            Vector2 size = area.size;
            Vector2 half = size * 0.5f;
            float angle = Mathf.Atan2(wind.y, wind.x) * Mathf.Rad2Deg;
            Vector2 native = wispSprite ? (Vector2)wispSprite.bounds.size : Vector2.one;
            for (int i = 0; i < wisps.Length; i++)
            {
                // Wisps stream through the box and wrap around, faster than the drift so the current reads.
                offsets[i] += wind * (2.2f * strength + 0.15f) * Time.deltaTime;
                if (offsets[i].x > half.x) offsets[i].x -= size.x;
                if (offsets[i].x < -half.x) offsets[i].x += size.x;
                if (offsets[i].y > half.y) offsets[i].y -= size.y;
                if (offsets[i].y < -half.y) offsets[i].y += size.y;
                wisps[i].localPosition = new Vector3(area.offset.x + offsets[i].x, area.offset.y + offsets[i].y, (i % 3 - 1) * 1.2f);
                wisps[i].localRotation = Quaternion.Euler(0f, 0f, angle);
                wisps[i].localScale = new Vector3(3.5f / native.x, 0.5f / native.y, 1f);
                // Fade near the box edges so wrap-around never pops.
                float edge = Mathf.Min(half.x - Mathf.Abs(offsets[i].x), half.y - Mathf.Abs(offsets[i].y));
                var c = color;
                c.a *= Mathf.Clamp01(edge / 1.5f) * (0.25f + 0.75f * strength);
                renderers[i].color = c;
            }
        }
    }
}
