using UnityEngine;

namespace NeonRunner
{
    /// <summary>Platform that shakes when stood on, falls away, then reforms once the space is clear.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class CrumbleBlock : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] float shakeTime = 0.45f;
        [SerializeField] float respawnTime = 2f;
        [SerializeField] float shakeAmount = 0.06f;

        enum Phase { Solid, Shaking, Gone }

        Collider2D col;
        PlayerController player;
        Phase phase;
        float timer;
        Vector3 visualBase;

        void Awake()
        {
            col = GetComponent<Collider2D>();
            if (visual) visualBase = visual.localPosition;
        }

        void Start() => player = GameManager.Instance ? GameManager.Instance.Player : FindAnyObjectByType<PlayerController>();

        void Update()
        {
            switch (phase)
            {
                case Phase.Solid:
                    if (PlayerOnTop())
                    {
                        phase = Phase.Shaking;
                        timer = shakeTime;
                    }
                    break;

                case Phase.Shaking:
                    timer -= Time.deltaTime;
                    if (visual) visual.localPosition = visualBase + (Vector3)(Random.insideUnitCircle * shakeAmount);
                    if (timer <= 0f) SetSolid(false);
                    break;

                case Phase.Gone:
                    timer -= Time.deltaTime;
                    if (timer <= 0f && !PlayerOverlaps()) SetSolid(true);
                    break;
            }
        }

        void SetSolid(bool solid)
        {
            phase = solid ? Phase.Solid : Phase.Gone;
            timer = respawnTime;
            col.enabled = solid;
            if (visual)
            {
                visual.localPosition = visualBase;
                visual.gameObject.SetActive(solid);
            }
        }

        bool PlayerOnTop()
        {
            if (!player || player.IsDead || !player.IsGrounded) return false;
            Bounds me = col.bounds, p = player.Body.bounds;
            return Mathf.Abs(p.min.y - me.max.y) < 0.15f && p.max.x > me.min.x && p.min.x < me.max.x;
        }

        bool PlayerOverlaps()
        {
            if (!player) return false;
            var b = col.bounds;
            b.Expand(0.2f);
            return b.Intersects(player.Body.bounds);
        }
    }
}
