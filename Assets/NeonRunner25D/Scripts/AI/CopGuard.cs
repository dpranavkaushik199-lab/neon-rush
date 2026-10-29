using UnityEngine;

namespace NeonRunner
{
    /// <summary>Ground cop: patrols a strip, chases faster once alerted, stops at ledges.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class CopGuard : CopBase
    {
        [Header("Movement")]
        [SerializeField] float patrolSpeed = 2.5f;
        [SerializeField] float chaseSpeed = 6.5f;
        [SerializeField] float waitAtEnds = 0.8f;
        [SerializeField] float patrolMinX;
        [SerializeField] float patrolMaxX;
        [SerializeField] LayerMask groundMask;

        [Header("Model")]
        [SerializeField] Transform model;
        [SerializeField] Animator animator;
        [Tooltip("Yaw when facing right; mirrored for left. Under 90 turns the face a little toward the camera.")]
        [SerializeField] float facingYaw = 70f;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int GroundedParam = Animator.StringToHash("Grounded");
        Rigidbody2D rb;
        int facing = 1;
        float waitTimer;
        float lookAroundTimer;
        Vector3 spawnPosition;

        protected override Vector2 EyePosition => (Vector2)transform.position + new Vector2(facing * 0.25f, 0.5f);
        protected override Vector2 LookDirection => new Vector2(facing, -0.1f).normalized;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            spawnPosition = transform.position;
        }

        public void SetPatrol(float minX, float maxX)
        {
            patrolMinX = minX;
            patrolMaxX = maxX;
        }

        protected override void TickMovement(float dt)
        {
            float vx = 0f;

            if (IsStunned)
            {
                vx = 0f;
            }
            else if (IsConfused)
            {
                lookAroundTimer -= dt;
                if (lookAroundTimer <= 0f)
                {
                    facing = -facing;
                    lookAroundTimer = 0.5f;
                }
            }
            else if (State == Awareness.Chase)
            {
                float dx = LastKnownPlayerPosition.x - transform.position.x;
                if (Mathf.Abs(dx) > 0.3f)
                {
                    facing = dx > 0f ? 1 : -1;
                    vx = facing * chaseSpeed * GameSettings.ChaserSpeedScale;
                }
                if (!GroundAhead() || WallAhead()) vx = 0f;
            }
            else if (waitTimer > 0f)
            {
                waitTimer -= dt;
            }
            else
            {
                vx = facing * patrolSpeed;
                float x = transform.position.x;
                bool pastEnd = facing > 0 ? x >= patrolMaxX : x <= patrolMinX;
                if (pastEnd || !GroundAhead() || WallAhead())
                {
                    facing = -facing;
                    waitTimer = waitAtEnds;
                    vx = 0f;
                }
            }

            rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
            if (model)
            {
                var targetRot = Quaternion.Euler(0f, facing > 0 ? facingYaw : -facingYaw, 0f);
                model.localRotation = Quaternion.RotateTowards(model.localRotation, targetRot, 900f * dt);
            }
            if (animator)
            {
                animator.SetFloat(SpeedParam, Mathf.Abs(vx));
                animator.SetBool(GroundedParam, true);
                animator.speed = IsStunned ? 0f : 1f;
            }
        }

        bool GroundAhead()
        {
            Vector2 probe = (Vector2)transform.position + new Vector2(facing * 0.6f, -0.6f);
            return Physics2D.Raycast(probe, Vector2.down, 0.8f, groundMask);
        }

        bool WallAhead()
        {
            return Physics2D.Raycast(transform.position, new Vector2(facing, 0f), 0.6f, groundMask);
        }

        protected override void ResetToSpawn()
        {
            rb.position = spawnPosition;
            transform.position = spawnPosition;
            rb.linearVelocity = Vector2.zero;
            facing = 1;
            waitTimer = 0f;
        }
    }
}
