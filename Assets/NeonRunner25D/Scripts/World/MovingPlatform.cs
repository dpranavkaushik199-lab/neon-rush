using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Solid platform that carries the player. Two behaviours:
    ///  - Shuttle: eases back and forth between A and B on a fixed rhythm (ferries, elevators).
    ///  - Zip: rests at A; stepping on it arms it (short shake), then it rockets to B,
    ///    holds a moment, and crawls back. Jumping just as it stops keeps its speed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingPlatform : MonoBehaviour
    {
        public enum Mode { Shuttle, Zip }

        [SerializeField] Mode mode = Mode.Shuttle;
        [SerializeField] Vector2 pointA;
        [SerializeField] Vector2 pointB;
        [Tooltip("Shuttle: seconds for a full A -> B -> A cycle.")]
        [SerializeField] float period = 5f;
        [Range(0f, 1f)] [SerializeField] float phase;
        [Header("Zip")]
        [SerializeField] float armTime = 0.2f;
        [SerializeField] float zipSpeed = 22f;
        [SerializeField] float holdTime = 0.5f;
        [SerializeField] float returnSpeed = 3f;
        [SerializeField] Transform visual;
        [SerializeField] Light statusLight;

        enum ZipPhase { Rest, Arming, Zipping, Holding, Returning }

        Rigidbody2D rb;
        Collider2D col;
        PlayerController player;
        ZipPhase zip;
        float timer;
        Vector3 visualBase;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            col = GetComponent<Collider2D>();
            if (visual) visualBase = visual.localPosition;
        }

        void Start()
        {
            player = GameManager.Instance ? GameManager.Instance.Player : FindAnyObjectByType<PlayerController>();
            rb.position = mode == Mode.Shuttle ? ShuttlePosition(Time.fixedTime) : pointA;
            if (GameManager.Instance) GameManager.Instance.Respawned += ResetZip;
        }

        void OnDestroy()
        {
            if (GameManager.Instance) GameManager.Instance.Respawned -= ResetZip;
        }

        void ResetZip()
        {
            if (mode != Mode.Zip) return;
            zip = ZipPhase.Rest;
            rb.position = pointA;
            transform.position = pointA;
        }

        Vector2 ShuttlePosition(float t)
        {
            float u = Mathf.PingPong((t / period + phase) * 2f, 1f);
            return Vector2.Lerp(pointA, pointB, u * u * (3f - 2f * u));
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector2 next = mode == Mode.Shuttle ? ShuttlePosition(Time.fixedTime + dt) : ZipStep(dt);
            Vector2 delta = next - rb.position;
            if (PlayerOnTop()) player.Carry(delta);
            rb.MovePosition(next);
        }

        Vector2 ZipStep(float dt)
        {
            Vector2 pos = rb.position;
            switch (zip)
            {
                case ZipPhase.Rest:
                    if (PlayerOnTop())
                    {
                        zip = ZipPhase.Arming;
                        timer = armTime;
                    }
                    return pos;
                case ZipPhase.Arming:
                    timer -= dt;
                    if (timer <= 0f) zip = ZipPhase.Zipping;
                    return pos;
                case ZipPhase.Zipping:
                    pos = Vector2.MoveTowards(pos, pointB, zipSpeed * dt);
                    if (pos == pointB)
                    {
                        zip = ZipPhase.Holding;
                        timer = holdTime;
                        GameManager.Instance?.HitStop(0.03f);
                    }
                    return pos;
                case ZipPhase.Holding:
                    timer -= dt;
                    if (timer <= 0f) zip = ZipPhase.Returning;
                    return pos;
                default:
                    pos = Vector2.MoveTowards(pos, pointA, returnSpeed * dt);
                    if (pos == pointA) zip = ZipPhase.Rest;
                    return pos;
            }
        }

        void Update()
        {
            if (mode != Mode.Zip) return;
            if (visual)
                visual.localPosition = visualBase + (zip == ZipPhase.Arming ? (Vector3)(Random.insideUnitCircle * 0.06f) : Vector3.zero);
            if (statusLight)
                statusLight.color = zip == ZipPhase.Rest ? new Color(0.4f, 1f, 0.8f) : zip == ZipPhase.Returning ? new Color(1f, 0.6f, 0.2f) : Color.white;
        }

        bool PlayerOnTop()
        {
            if (!player || player.IsDead || !player.IsGrounded) return false;
            Bounds me = col.bounds, p = player.Body.bounds;
            return Mathf.Abs(p.min.y - me.max.y) < 0.2f && p.max.x > me.min.x && p.min.x < me.max.x;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.8f, 0.6f);
            Gizmos.DrawLine(pointA, pointB);
        }
    }
}
