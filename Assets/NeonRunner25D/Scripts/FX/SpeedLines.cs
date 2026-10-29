using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Gust of wind while boosting or moving very fast: soft, foggy wisps of air that
    /// stream past against the direction of travel, swell in and thin out over their
    /// life, and drift with a little turbulence. Two depth bands (one close to the lens,
    /// one around the player) give the gust volume instead of flat screen streaks.
    /// </summary>
    public class SpeedLines : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] Camera cam;
        [Tooltip("Soft, elongated wisp texture (bright in the middle, feathered on every edge).")]
        [SerializeField] Sprite lineSprite;
        [SerializeField] Material material;
        [SerializeField] int lineCount = 22;
        [SerializeField] Color color = new Color(0.86f, 0.92f, 0.98f, 0.16f);
        [SerializeField] float lineSpeed = 38f;

        struct Wisp
        {
            public Transform t;
            public SpriteRenderer sr;
            public Vector2 pos;
            public float length, thickness, age, life, alpha, depth, phase;
        }

        Wisp[] wisps;
        float intensity;
        Vector2 flowDir = Vector2.left;

        void Start()
        {
            if (PerformanceManager.LowEnd) lineCount = Mathf.Max(8, lineCount / 2);
            wisps = new Wisp[lineCount];
            for (int i = 0; i < lineCount; i++)
            {
                var go = new GameObject("WindWisp");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = lineSprite;
                sr.sharedMaterial = material;
                sr.sortingOrder = 100;
                sr.enabled = false;
                wisps[i] = new Wisp { t = go.transform, sr = sr };
                Respawn(ref wisps[i], true);
            }
        }

        // Near band sits 6-10 units in front of the camera, far band around the player plane.
        float Distance(float depth) => Mathf.Lerp(6f, 15f, depth);

        Vector2 HalfView(float distance)
        {
            float h = cam.orthographic ? cam.orthographicSize : distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(h * cam.aspect, h);
        }

        void Respawn(ref Wisp w, bool anywhere)
        {
            w.depth = Random.value;
            Vector2 half = HalfView(Distance(w.depth));
            w.length = Random.Range(3f, 7.5f) * Mathf.Lerp(1.2f, 0.8f, w.depth);
            w.thickness = Random.Range(0.35f, 1.1f) * Mathf.Lerp(1.3f, 0.8f, w.depth);
            w.life = Random.Range(0.45f, 0.9f);
            w.age = anywhere ? Random.value * w.life : 0f;
            w.alpha = Random.Range(0.5f, 1f) * Mathf.Lerp(0.7f, 1f, w.depth);
            w.phase = Random.value * 10f;
            // New wisps enter from the side the wind blows from, anywhere along it.
            Vector2 across = new Vector2(-flowDir.y, flowDir.x);
            float along = anywhere ? Random.Range(-1f, 1f) : Random.Range(0.1f, 1f);
            w.pos = -flowDir * along * (half.x + w.length * 0.5f) * 0.9f + across * Random.Range(-half.y, half.y) * 1.1f;
        }

        void LateUpdate()
        {
            bool fast = !player.IsDead && (player.IsBoosting || player.Flow >= 0.95f || player.Velocity.magnitude > player.Config.flowMaxSpeed * 1.3f);
            intensity = Mathf.MoveTowards(intensity, fast ? 1f : 0f, Time.deltaTime * (fast ? 5f : 1.6f));

            Vector2 v = player.Velocity;
            if (v.sqrMagnitude > 4f) flowDir = Vector2.Lerp(flowDir, -v.normalized, 1f - Mathf.Exp(-10f * Time.deltaTime)).normalized;
            float angle = Mathf.Atan2(flowDir.y, flowDir.x) * Mathf.Rad2Deg;

            bool visible = intensity > 0.01f;
            for (int i = 0; i < wisps.Length; i++)
            {
                ref var w = ref wisps[i];
                w.sr.enabled = visible;
                if (!visible) continue;

                w.age += Time.deltaTime;
                if (w.age >= w.life) Respawn(ref w, false);

                // Closer wisps move faster across the screen (parallax), with a slow curl.
                float speed = lineSpeed * Mathf.Lerp(1.25f, 0.65f, w.depth);
                Vector2 across = new Vector2(-flowDir.y, flowDir.x);
                float curl = Mathf.Sin(Time.time * 3f + w.phase) * 0.6f;
                w.pos += (flowDir * speed + across * curl) * Time.deltaTime;

                // Swell in, stretch while alive, thin out at the end.
                float t = w.age / w.life;
                float fade = Mathf.Sin(t * Mathf.PI);
                float stretch = Mathf.Lerp(0.7f, 1.25f, t);
                Vector2 native = lineSprite.bounds.size;
                w.t.localPosition = new Vector3(w.pos.x, w.pos.y, Distance(w.depth));
                w.t.localRotation = Quaternion.Euler(0f, 0f, angle);
                w.t.localScale = new Vector3(w.length * stretch / native.x, w.thickness * Mathf.Lerp(1f, 1.6f, t) / native.y, 1f);

                var c = color;
                c.a *= intensity * fade * w.alpha;
                w.sr.color = c;
            }
        }
    }
}
