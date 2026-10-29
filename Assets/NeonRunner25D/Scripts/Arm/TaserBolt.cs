using UnityEngine;

namespace NeonRunner
{
    /// <summary>Fast, short-range projectile that stuns the first IStunnable it hits.</summary>
    public class TaserBolt : MonoBehaviour
    {
        Vector2 direction;
        float speed;
        float remaining;
        float stunDuration;
        LayerMask hitMask;

        public static TaserBolt Spawn(Vector2 origin, Vector2 direction, float speed, float range, float stun,
            LayerMask mask, Sprite sprite, Material material, Color color)
        {
            var go = new GameObject("TaserBolt");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            go.transform.localScale = new Vector3(0.7f, 0.18f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = material;
            sr.color = color;
            sr.sortingOrder = 30;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 4f;
            light.range = 3f;

            var bolt = go.AddComponent<TaserBolt>();
            bolt.direction = direction.normalized;
            bolt.speed = speed;
            bolt.remaining = range;
            bolt.stunDuration = stun;
            bolt.hitMask = mask;
            return bolt;
        }

        void Update()
        {
            float step = speed * Time.deltaTime;
            var hit = Physics2D.CircleCast(transform.position, 0.2f, direction, step, hitMask);
            if (hit)
            {
                var target = hit.collider.GetComponentInParent<IStunnable>();
                target?.Stun(stunDuration);
                Destroy(gameObject);
                return;
            }

            transform.position += (Vector3)(direction * step);
            remaining -= step;
            if (remaining <= 0f) Destroy(gameObject);
        }
    }
}
