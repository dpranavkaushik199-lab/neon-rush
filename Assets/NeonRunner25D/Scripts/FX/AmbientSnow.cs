using UnityEngine;

namespace NeonRunner
{
    /// <summary>Celeste-style drifting pixel flakes in front of and behind the level, wrapping around the view.</summary>
    public class AmbientSnow : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] Sprite pixelSprite;
        [SerializeField] Material material;
        [SerializeField] int count = 70;
        [SerializeField] Vector2 wind = new Vector2(-2.2f, -0.6f);
        [SerializeField] Color color = new Color(0.85f, 0.9f, 1f, 0.75f);
        [SerializeField] float pixelsPerUnit = 30f;
        [Tooltip("Streak length in pixels along the wind (1 = round flakes, >1 = rain).")]
        [SerializeField] float streakLength = 1f;
        [Tooltip("Depth range (z) flakes are scattered over, for 2.5D parallax.")]
        [SerializeField] Vector2 depthRange = Vector2.zero;

        float[] zDepth;
        Transform[] flakes;
        SpriteRenderer[] renderers;
        float[] depth;
        float[] phase;
        Vector2[] positions;

        void Start()
        {
            if (PerformanceManager.LowEnd) count /= 2;
            flakes = new Transform[count];
            renderers = new SpriteRenderer[count];
            depth = new float[count];
            phase = new float[count];
            positions = new Vector2[count];
            zDepth = new float[count];
            Vector2 half = HalfExtents();
            Vector2 origin = cam.transform.position;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Flake");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = pixelSprite;
                sr.sharedMaterial = material;
                depth[i] = Random.Range(0.35f, 1f);
                phase[i] = Random.value * 10f;
                bool front = depth[i] > 0.75f;
                sr.sortingOrder = front ? 95 : -25;
                var c = color;
                c.a *= Mathf.Lerp(0.35f, 1f, depth[i]);
                sr.color = c;
                float px = (front ? 2 : 1) / pixelsPerUnit;
                float native = pixelSprite.bounds.size.x;
                go.transform.localScale = new Vector3(px / native, px * streakLength / native, 1f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(wind.y, wind.x) * Mathf.Rad2Deg + 90f);
                zDepth[i] = Mathf.Lerp(depthRange.x, depthRange.y, Random.value);
                positions[i] = origin + new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
                flakes[i] = go.transform;
                renderers[i] = sr;
            }
        }

        Vector2 HalfExtents() => CameraFollow2D.HalfExtentsAtPlane(cam) + Vector2.one;

        void LateUpdate()
        {
            Vector2 center = cam.transform.position;
            Vector2 half = HalfExtents();
            float dt = Time.deltaTime;
            float snap = 1f / pixelsPerUnit;

            for (int i = 0; i < flakes.Length; i++)
            {
                Vector2 v = wind * depth[i] + new Vector2(0f, Mathf.Sin(Time.time * 1.7f + phase[i]) * 0.4f);
                Vector2 p = positions[i] + v * dt;

                // Wrap around the camera view so flakes are always on screen.
                Vector2 rel = p - center;
                if (rel.x < -half.x) p.x += half.x * 2f;
                else if (rel.x > half.x) p.x -= half.x * 2f;
                if (rel.y < -half.y) p.y += half.y * 2f;
                else if (rel.y > half.y) p.y -= half.y * 2f;

                positions[i] = p;
                flakes[i].position = new Vector3(Mathf.Round(p.x / snap) * snap, Mathf.Round(p.y / snap) * snap, zDepth[i]);
            }
        }
    }
}
