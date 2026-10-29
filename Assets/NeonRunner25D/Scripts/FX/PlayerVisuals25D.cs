using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// 2.5D presentation for the 3D character: animator parameters, facing turn
    /// (angled toward the camera), lean into motion, squash &amp; stretch, a dash
    /// indicator light (red = dash ready, blue = spent, white flash on refill),
    /// and a motion trail during dashes and boosts.
    /// </summary>
    public class PlayerVisuals25D : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [Tooltip("Pivot at the feet; receives squash/stretch and lean.")]
        [SerializeField] Transform squashRoot;
        [Tooltip("The character model; receives the facing turn.")]
        [SerializeField] Transform model;
        [SerializeField] Animator animator;
        [SerializeField] Light dashLight;
        [SerializeField] TrailRenderer trail;

        [Header("Facing & Lean")]
        [SerializeField] float facingYaw = 70f;
        [SerializeField] float turnSpeed = 1100f;
        [SerializeField] float leanPerSpeed = 1.2f;
        [SerializeField] float maxLean = 14f;

        [Header("Squash & Stretch")]
        [SerializeField] Vector3 jumpStretch = new Vector3(0.8f, 1.25f, 0.8f);
        [SerializeField] Vector3 landSquash = new Vector3(1.3f, 0.7f, 1.3f);
        [SerializeField] Vector3 dashStretch = new Vector3(1.3f, 0.8f, 1.3f);
        [SerializeField] Vector3 slideSquash = new Vector3(1.25f, 0.5f, 1.25f);
        [SerializeField] Vector3 vaultStretch = new Vector3(1.2f, 0.75f, 1.2f);
        [SerializeField] float recoverSpeed = 12f;

        [Header("Dash Colours")]
        [SerializeField] Color dashReady = new Color(1f, 0.25f, 0.3f);
        [SerializeField] Color dashSpent = new Color(0.27f, 0.72f, 1f);
        [SerializeField] Color refillFlash = Color.white;
        [Tooltip("Boost leaves a pale wake of air rather than a hard streak.")]
        [SerializeField] Color boostTrail = new Color(0.82f, 0.9f, 0.96f);
        [Tooltip("Faint mint wake while at full Flow, sliding or wall running.")]
        [SerializeField] Color flowTrail = new Color(0.45f, 1f, 0.78f);

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int GroundedParam = Animator.StringToHash("Grounded");

        Vector3 scale = Vector3.one;
        float lean;
        int lastDashes;
        float flashTimer;

        /// <summary>Current dash-state colour (used by death/respawn effects).</summary>
        public Color DashColor { get; private set; }

        void OnEnable()
        {
            player.Jumped += OnJumped;
            player.Landed += OnLanded;
            player.Dashed += OnDashed;
            player.SlingBoosted += OnBoosted;
            player.Vaulted += OnVaulted;
            player.Died += OnDied;
            player.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            player.Jumped -= OnJumped;
            player.Landed -= OnLanded;
            player.Dashed -= OnDashed;
            player.SlingBoosted -= OnBoosted;
            player.Vaulted -= OnVaulted;
            player.Died -= OnDied;
            player.Respawned -= OnRespawned;
        }

        void Start() => lastDashes = player.DashesLeft;

        void OnJumped() => scale = jumpStretch;

        void OnLanded(float impact) => scale = Vector3.Lerp(Vector3.one, landSquash, Mathf.InverseLerp(4f, 20f, impact));

        void OnDashed(Vector2 dir) => scale = dashStretch;

        void OnBoosted(TimingGrade grade, Vector2 dir) => scale = new Vector3(1.5f, 0.65f, 1.5f);

        void OnVaulted() => scale = vaultStretch;

        void OnDied()
        {
            squashRoot.gameObject.SetActive(false);
            if (trail) trail.Clear();
        }

        void OnRespawned()
        {
            squashRoot.gameObject.SetActive(true);
            scale = new Vector3(0.5f, 1.5f, 0.5f);
            if (trail) trail.Clear();
        }

        void Update()
        {
            if (player.IsDead) return;
            float dt = Time.deltaTime;
            Vector2 v = player.Velocity;

            // Facing: turn toward travel direction, angled a little toward the camera.
            var targetYaw = Quaternion.Euler(0f, player.Facing > 0 ? facingYaw : -facingYaw, 0f);
            model.localRotation = Quaternion.RotateTowards(model.localRotation, targetYaw, turnSpeed * dt);

            // Lean into horizontal motion (and away from a wall while sliding).
            float targetLean = Mathf.Clamp(-v.x * leanPerSpeed, -maxLean, maxLean);
            if (player.IsWallSliding) targetLean = player.WallSide * 10f;
            // Wall run: lean hard into the wall, body near-horizontal against it.
            if (player.IsWallRunning) targetLean = player.WallSide * 35f;
            lean = Mathf.Lerp(lean, targetLean, 1f - Mathf.Exp(-10f * dt));
            squashRoot.localRotation = Quaternion.Euler(0f, 0f, lean);

            // A slide holds the body low and long; everything else relaxes back to neutral.
            Vector3 rest = player.IsCrouched ? slideSquash : Vector3.one;
            scale = Vector3.Lerp(scale, rest, 1f - Mathf.Exp(-recoverSpeed * dt));
            squashRoot.localScale = scale;

            if (animator)
            {
                bool grounded = player.IsGrounded && !player.IsDashing;
                animator.SetFloat(SpeedParam, grounded ? Mathf.Abs(v.x) : 0f);
                animator.SetBool(GroundedParam, grounded && !player.IsSliding);
                // Run cycle keeps pace with actual speed (base run speed = 1x).
                animator.speed = grounded && Mathf.Abs(v.x) > 0.5f
                    ? Mathf.Clamp(Mathf.Abs(v.x) / player.Config.maxRunSpeed, 0.7f, 1.8f)
                    : 1f;
            }

            // Dash indicator.
            if (player.DashesLeft > lastDashes) flashTimer = 0.12f;
            lastDashes = player.DashesLeft;
            flashTimer -= dt;
            DashColor = flashTimer > 0f ? refillFlash : player.DashesLeft > 0 ? dashReady : dashSpent;
            if (dashLight) dashLight.color = DashColor;

            if (trail)
            {
                bool flowing = player.Flow >= 0.95f || player.IsSliding || player.IsWallRunning;
                bool emitting = player.IsDashing || player.IsBoosting || flowing;
                trail.emitting = emitting;
                Color c = player.IsBoosting ? boostTrail : player.IsDashing ? DashColor : flowTrail;
                trail.startColor = new Color(c.r, c.g, c.b, player.IsDashing ? 0.8f : player.IsBoosting ? 0.22f : 0.35f);
                trail.endColor = new Color(c.r, c.g, c.b, 0f);
            }
        }
    }
}
