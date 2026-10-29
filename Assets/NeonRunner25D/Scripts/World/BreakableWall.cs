using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Cracked drywall panel: solid to walking and jumping, but a dash, Sling-Boost, slide or full-Flow sprint
    /// smashes straight through it and keeps the player's speed. Rebuilds when the
    /// player respawns so a room can always be retried.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class BreakableWall : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] Material debrisMaterial;
        [SerializeField] int debrisCount = 14;

        Collider2D col;
        PlayerController player;
        Vector2 lastPlayerVelocity;
        bool broken;

        void Awake() => col = GetComponent<Collider2D>();

        void Start()
        {
            player = GameManager.Instance ? GameManager.Instance.Player : FindAnyObjectByType<PlayerController>();
            if (GameManager.Instance) GameManager.Instance.Respawned += Rebuild;
        }

        void OnDestroy()
        {
            if (GameManager.Instance) GameManager.Instance.Respawned -= Rebuild;
        }

        void FixedUpdate()
        {
            if (broken || !player || player.IsDead) return;
            // Parkour momentum counts too: a slide or a full-Flow sprint goes straight through.
            bool fast = player.IsDashing || player.IsBoosting || player.IsSliding || player.Flow >= 1f;
            var b = col.bounds;
            b.Expand(new Vector3(0.35f, 0.1f, 0f));
            if (fast && b.Intersects(player.Body.bounds)) Smash(lastPlayerVelocity);
            // The velocity from before the impact, so the player keeps their momentum through the hole.
            lastPlayerVelocity = player.Velocity;
        }

        void Smash(Vector2 carry)
        {
            broken = true;
            col.enabled = false;
            if (visual) visual.gameObject.SetActive(false);
            if (carry.sqrMagnitude > 1f) player.Launch(carry);
            GameManager.Instance?.HitStop(0.05f);
            Camera.main?.GetComponent<CameraFollow2D>()?.AddTrauma(0.3f);
            SpawnDebris(carry);
        }

        void SpawnDebris(Vector2 dir)
        {
            var size = col.bounds.size;
            for (int i = 0; i < (PerformanceManager.LowEnd ? debrisCount / 2 : debrisCount); i++)
            {
                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(chunk.GetComponent<BoxCollider>());
                chunk.transform.position = transform.position + new Vector3(Random.Range(-0.5f, 0.5f) * size.x, Random.Range(-0.5f, 0.5f) * size.y, Random.Range(-0.6f, 0.6f));
                chunk.transform.localScale = Vector3.one * Random.Range(0.12f, 0.35f);
                chunk.transform.rotation = Random.rotation;
                if (debrisMaterial) chunk.GetComponent<Renderer>().sharedMaterial = debrisMaterial;
                var body = chunk.AddComponent<Rigidbody>();
                body.linearVelocity = (Vector3)(dir.normalized * Random.Range(4f, 10f)) + Random.insideUnitSphere * 3f + Vector3.up * 2f;
                body.angularVelocity = Random.insideUnitSphere * 10f;
                Destroy(chunk, 2.5f);
            }
        }

        void Rebuild()
        {
            broken = false;
            col.enabled = true;
            if (visual) visual.gameObject.SetActive(true);
        }
    }
}
