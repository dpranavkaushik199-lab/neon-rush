using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Flying sentinel: loops a grid of waypoints sweeping a searchlight. When
    /// hacked (stunned) it locks in place and becomes a solid platform.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class CopDrone : CopBase
    {
        [Header("Movement")]
        [SerializeField] Vector2[] waypoints = new Vector2[0];
        [SerializeField] float patrolSpeed = 5f;
        [SerializeField] float chaseSpeed = 9f;
        [SerializeField] Vector2 chaseHoverOffset = new Vector2(0f, 3.5f);
        [SerializeField] float sweepAngle = 30f;
        [SerializeField] float sweepSpeed = 1.6f;

        [Header("Hack")]
        [SerializeField] Collider2D bodyCollider;
        [SerializeField] GameObject hackedVisual;
        [SerializeField] string platformLayer = "Ground";
        [SerializeField] string enemyLayer = "Enemy";
        [Tooltip("Seconds the hack lasts after the player steps off (it never expires while ridden).")]
        [SerializeField] float hackLingerAfterLeaving = 1.5f;

        /// <summary>The hacked drone the player is standing on, if any. While set, no drone can see them.</summary>
        public static CopDrone Ridden { get; private set; }

        Rigidbody2D rb;
        int waypointIndex;
        Vector2 velocity;
        int facing = 1;
        Vector3 spawnPosition;

        protected override Vector2 EyePosition => (Vector2)transform.position + Vector2.down * 0.3f;

        protected override Vector2 LookDirection
        {
            get
            {
                float sweep = Mathf.Sin(Time.time * sweepSpeed) * sweepAngle;
                float angle = -90f + facing * 25f + (State == Awareness.Chase ? 0f : sweep);
                if (State == Awareness.Chase && Player)
                {
                    Vector2 to = (Vector2)Player.transform.position - EyePosition;
                    return to.normalized;
                }
                float rad = angle * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            }
        }

        // Drones are sentinels: they only hurt once they're hunting you.
        protected override bool LethalOnContact => State == Awareness.Chase;

        // Riding a hacked drone hides you from the whole drone network.
        protected override bool IsPlayerHiddenFromMe => Ridden != null;

        // Drones are spotters: what one sees, every unit in the level hears about.
        protected override bool AlertsWholeLevel => true;

        protected override void Update()
        {
            UpdateRider();
            if (Ridden != null && State == Awareness.Chase && !IsStunned) BreakLineOfSight(0.5f);
            base.Update();
        }

        void UpdateRider()
        {
            if (IsStunned && PlayerStandingOnMe())
            {
                if (Ridden != this)
                {
                    Ridden = this;
                    GameManager.Instance?.ShowPopup("RIDING HACKED DRONE - DRONES CAN'T SEE YOU", new Color(0.3f, 1f, 0.6f), 1.6f);
                }
                Stun(hackLingerAfterLeaving);
            }
            else if (Ridden == this)
            {
                Ridden = null;
            }
        }

        bool PlayerStandingOnMe()
        {
            if (!Player || Player.IsDead || !Player.IsGrounded || !bodyCollider) return false;
            Bounds me = bodyCollider.bounds;
            Bounds p = Player.Body.bounds;
            return Mathf.Abs(p.min.y - me.max.y) <= 0.2f && p.max.x > me.min.x && p.min.x < me.max.x;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Ridden == this) Ridden = null;
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            spawnPosition = transform.position;
            if (hackedVisual) hackedVisual.SetActive(false);
        }

        public void SetWaypoints(Vector2[] points) => waypoints = points;

        protected override void TickMovement(float dt)
        {
            if (IsStunned)
            {
                velocity = Vector2.zero;
                if (hackedVisual) hackedVisual.SetActive(StunTimeLeft > 0.75f || Mathf.Repeat(Time.time, 0.15f) < 0.075f);
                return;
            }

            Vector2 pos = transform.position;
            Vector2 target = pos;
            float speed = patrolSpeed;

            if (IsConfused)
            {
                target = pos;
            }
            else if (State == Awareness.Chase)
            {
                target = LastKnownPlayerPosition + chaseHoverOffset;
                speed = chaseSpeed * GameSettings.ChaserSpeedScale;
            }
            else if (waypoints.Length > 0)
            {
                target = waypoints[waypointIndex];
                if (Vector2.Distance(pos, target) < 0.1f)
                {
                    waypointIndex = (waypointIndex + 1) % waypoints.Length;
                    target = waypoints[waypointIndex];
                }
            }

            velocity = Vector2.ClampMagnitude(target - pos, 1f) * speed;
            if (Mathf.Abs(target.x - pos.x) > 0.2f) facing = target.x > pos.x ? 1 : -1;
        }

        void FixedUpdate()
        {
            rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);
        }

        protected override void OnStunStarted()
        {
            velocity = Vector2.zero;
            SetLayer(platformLayer);
            if (hackedVisual) hackedVisual.SetActive(true);
        }

        protected override void OnStunEnded()
        {
            SetLayer(enemyLayer);
            if (hackedVisual) hackedVisual.SetActive(false);
        }

        void SetLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0 || !bodyCollider) return;
            bodyCollider.gameObject.layer = layer;
        }

        protected override void ResetToSpawn()
        {
            if (Ridden == this) Ridden = null;
            rb.position = spawnPosition;
            transform.position = spawnPosition;
            velocity = Vector2.zero;
            waypointIndex = 0;
        }
    }
}
