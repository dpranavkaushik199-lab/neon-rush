using System;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Fast-paced parkour on a "Celeste-plus" core: snappy run, variable jump, coyote time,
    /// jump buffering, wall cling/jump, 8-way dash, grapple tether and Sling-Boost, plus the
    /// flow layer: momentum (Flow) that raises top speed while you keep moving, slides and
    /// slide long-jumps, speed vaults over low cover and wall runs up tall walls.
    /// Gravity is applied manually; Rigidbody2D only resolves collisions.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour, IStunnable
    {
        public enum State { Normal, Dash, Grapple, Dead }

        [SerializeField] MovementConfig config;
        [SerializeField] PlayerInputReader input;
        [SerializeField] GrappleHook grapple;
        [SerializeField] Collider2D body;
        [SerializeField] LayerMask groundMask;
        [SerializeField] float groundCheckDepth = 0.08f;
        [SerializeField] float wallCheckDistance = 0.1f;

        public event Action Jumped;
        public event Action<float> Landed;
        public event Action<Vector2> Dashed;
        public event Action<TimingGrade, Vector2> SlingBoosted;
        public event Action Died;
        public event Action Respawned;
        public event Action Stunned;
        public event Action ShieldBroken;
        public event Action Slid;
        public event Action Vaulted;
        public event Action WallRan;

        public MovementConfig Config => config;
        public PlayerInputReader Input => input;
        public GrappleHook Grapple => grapple;
        public Collider2D Body => body;
        public State CurrentState { get; private set; }
        public bool IsGrounded { get; private set; }
        public int WallSide { get; private set; }
        public int Facing { get; private set; } = 1;
        public int DashesLeft { get; private set; }
        public int ClimbJumpsLeft { get; private set; }
        public bool IsBoosting => boostTimer > 0f;
        public bool IsDashing => CurrentState == State.Dash;
        public bool IsDead => CurrentState == State.Dead;
        public bool IsWallSliding { get; private set; }
        public Vector2 Velocity => rb.linearVelocity;
        /// <summary>Cop hits left this life (Casual difficulty only).</summary>
        public int ShieldsLeft { get; private set; }
        public bool IsInvulnerable => Time.time < invulnerableUntil;
        /// <summary>Momentum, 0..1: builds while you keep moving fast, raises top speed toward flowMaxSpeed.</summary>
        public float Flow { get; private set; }
        public float TopSpeed => Mathf.Lerp(config.maxRunSpeed, config.flowMaxSpeed, Flow);
        public bool IsSliding { get; private set; }
        public bool IsCrouched { get; private set; }
        public bool IsWallRunning => wallRunTimer > 0f;

        Rigidbody2D rb;
        CapsuleCollider2D capsule;
        Vector2 standSize, standOffset;
        float lastSlideBoostTime = float.NegativeInfinity;
        float groundedSince;
        float vaultTimer;
        float wallRunTimer;
        float wallRunSpeed;
        int wallRunSide;
        int wallRunUsedSide;
        float lastGroundedTime = float.NegativeInfinity;
        float lastWallTime = float.NegativeInfinity;
        int lastWallSide;
        bool isJumping;
        float wallJumpLockTimer;
        float moveLockTimer;
        float dashTimer;
        Vector2 dashDirection;
        float boostTimer;
        float pendingTetherDash = float.NegativeInfinity;
        float lastVelocityY;
        float lastVelocityX;
        float currentMaxFall;
        float retainedSpeedX;
        float retainTimer;
        float invulnerableUntil;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            capsule = body as CapsuleCollider2D;
            if (capsule)
            {
                standSize = capsule.size;
                standOffset = capsule.offset;
            }
            DashesLeft = config.maxDashes;
            ClimbJumpsLeft = config.climbJumps;
            ShieldsLeft = GameSettings.ExtraHits;
            currentMaxFall = config.maxFallSpeed;
        }

        void FixedUpdate()
        {
            if (CurrentState == State.Dead) return;

            float dt = Time.fixedDeltaTime;
            Vector2 v = rb.linearVelocity;
            UpdateContacts(v);

            if (IsCrouched && !IsSliding && CanStand()) SetCrouched(false);
            if (wallJumpLockTimer > 0f) wallJumpLockTimer -= dt;
            if (moveLockTimer > 0f) moveLockTimer -= dt;
            if (vaultTimer > 0f) vaultTimer -= dt;
            UpdateFlow(v, dt);
            if (boostTimer > 0f)
            {
                boostTimer -= dt;
                if (WallSide != 0 && WallSide == Math.Sign(v.x)) boostTimer = 0f;
            }

            if (CurrentState == State.Normal) v = RetainWallSpeed(v, dt);

            switch (CurrentState)
            {
                case State.Normal: v = TickNormal(v, dt); break;
                case State.Dash: v = TickDash(v, dt); break;
                case State.Grapple: v = TickGrapple(v, dt); break;
            }

            if (CurrentState != State.Grapple) CorrectCorners(v, dt);

            rb.linearVelocity = v;
            lastVelocityY = v.y;
            lastVelocityX = v.x;
        }

        // --------------------------------------------------------- Forgiveness

        /// <summary>
        /// Celeste-style corner correction: rising into the edge of a ceiling slides
        /// you around it, and dashing into the lip of a ledge pops you up onto it.
        /// </summary>
        void CorrectCorners(Vector2 v, float dt)
        {
            Bounds b = body.bounds;
            Vector2 center = b.center;
            Vector2 size = (Vector2)b.size * 0.95f;

            if (v.y > 0f && config.ceilingCornerCorrection > 0f)
            {
                var rise = new Vector2(0f, v.y * dt + 0.02f);
                if (Blocked(center + rise, size))
                {
                    int preferred = v.x != 0f ? Math.Sign(v.x) : Facing;
                    foreach (int side in new[] { preferred, -preferred })
                        if (TryNudge(center, size, rise, new Vector2(side, 0f), config.ceilingCornerCorrection)) break;
                }
            }

            bool fastHorizontal = (IsDashing || IsBoosting || IsSliding) && Mathf.Abs(v.x) > config.maxRunSpeed * 0.5f && v.y <= 0.5f;
            bool vaulting = vaultTimer > 0f && Mathf.Abs(v.x) > 1f;
            if ((fastHorizontal || vaulting) && config.dashLedgeCorrection > 0f)
            {
                var ahead = new Vector2(v.x * dt + Math.Sign(v.x) * 0.02f, 0f);
                if (Blocked(center + ahead, size))
                    TryNudge(center, size, ahead, Vector2.up, vaulting ? 0.6f : config.dashLedgeCorrection);
            }
        }

        bool TryNudge(Vector2 center, Vector2 size, Vector2 move, Vector2 direction, float maxDistance)
        {
            for (float d = 0.05f; d <= maxDistance + 0.001f; d += 0.05f)
            {
                Vector2 shifted = center + direction * d;
                if (Blocked(shifted, size) || Blocked(shifted + move, size)) continue;
                rb.position += direction * d;
                return true;
            }
            return false;
        }

        bool Blocked(Vector2 center, Vector2 size) => Physics2D.OverlapBox(center, size, 0f, groundMask);

        /// <summary>
        /// Celeste's wall speed retention: if you slam into a wall and the way clears
        /// a moment later (you rose past its top), your horizontal speed comes back.
        /// </summary>
        Vector2 RetainWallSpeed(Vector2 v, float dt)
        {
            if (retainTimer > 0f)
            {
                retainTimer -= dt;
                int dir = Math.Sign(retainedSpeedX);
                bool pushingAway = input.Move.x != 0f && Math.Sign(input.Move.x) != dir;
                if (pushingAway) retainTimer = 0f;
                else if (WallSide != dir)
                {
                    v.x = retainedSpeedX;
                    retainTimer = 0f;
                }
            }
            else if (WallSide != 0 && Math.Sign(lastVelocityX) == WallSide &&
                     Mathf.Abs(lastVelocityX) > config.maxRunSpeed * 0.5f && Mathf.Abs(v.x) < 0.5f)
            {
                retainedSpeedX = lastVelocityX;
                retainTimer = config.wallSpeedRetentionTime;
                TryStartWallRun(Mathf.Abs(lastVelocityX));
            }
            return v;
        }

        /// <summary>
        /// Wall run: slamming into a tall wall at speed while holding toward it turns the impact into
        /// a run up the wall. Once per wall side until you touch the ground (so wall-to-wall chains work).
        /// The held speed comes back when you crest the top.
        /// </summary>
        void TryStartWallRun(float impactSpeed)
        {
            bool toward = input.Move.x != 0f && Math.Sign(input.Move.x) == WallSide;
            if (!toward || IsSliding || WallSide == wallRunUsedSide || impactSpeed < config.wallRunMinSpeed) return;
            if (!SideHit(WallSide, 0.6f, 1.05f)) return; // low cover is vaulted, not run up
            wallRunUsedSide = WallSide;
            wallRunSide = WallSide;
            wallRunTimer = config.wallRunDuration;
            wallRunSpeed = Mathf.Clamp(impactSpeed * config.wallRunConversion, config.wallRunUpSpeedMin, config.wallRunUpSpeedMax);
            retainTimer = config.wallRunDuration + 0.3f;
            isJumping = false;
            WallRan?.Invoke();
        }

        // ------------------------------------------------------------------ Flow

        void UpdateFlow(Vector2 v, float dt)
        {
            float speed = CurrentState == State.Grapple ? v.magnitude : Mathf.Abs(v.x);
            bool holding = input.Move.x != 0f && Math.Sign(input.Move.x) == Math.Sign(v.x);
            bool flowing = IsWallRunning || IsDashing || IsBoosting || CurrentState == State.Grapple && speed > config.maxRunSpeed ||
                           speed >= config.maxRunSpeed * 0.9f && (holding || IsSliding || !IsGrounded);
            Flow = flowing
                ? Mathf.Min(1f, Flow + dt / Mathf.Max(0.01f, config.flowBuildTime))
                : Mathf.Max(0f, Flow - config.flowDecay * dt);
        }

        // ----------------------------------------------------------------- Slide

        void SetCrouched(bool crouched)
        {
            IsCrouched = crouched;
            if (!capsule) return;
            if (crouched)
            {
                float h = Mathf.Min(config.slideHeight, standSize.y);
                capsule.size = new Vector2(standSize.x, h);
                capsule.offset = standOffset - new Vector2(0f, (standSize.y - h) * 0.5f);
            }
            else
            {
                capsule.size = standSize;
                capsule.offset = standOffset;
            }
        }

        /// <summary>Room to stand up: nothing solid where the full-height collider would be.</summary>
        bool CanStand()
        {
            if (!capsule || !IsCrouched) return true;
            Vector2 center = rb.position + standOffset + new Vector2(0f, 0.04f);
            return !Physics2D.OverlapBox(center, new Vector2(standSize.x * 0.9f, standSize.y * 0.92f), 0f, groundMask);
        }

        void StartSlide(ref Vector2 v)
        {
            IsSliding = true;
            SetCrouched(true);
            int dir = Math.Sign(v.x);
            Facing = dir;
            // The boost only fires once per cooldown, so slide-spamming can't farm speed.
            if (Time.time - lastSlideBoostTime > config.slideCooldown)
            {
                v.x = dir * Mathf.Min(Mathf.Abs(v.x) + config.slideBoost, config.flowMaxSpeed + config.slideBoost);
                lastSlideBoostTime = Time.time;
            }
            Slid?.Invoke();
        }

        /// <summary>Slide physics: no run acceleration, gentle friction, forced crawl under a low ceiling.</summary>
        Vector2 TickSlide(Vector2 v, Vector2 move, float dt)
        {
            int dir = v.x != 0f ? Math.Sign(v.x) : Facing;
            bool stuck = !CanStand();
            v.x = Mathf.MoveTowards(v.x, 0f, config.slideFriction * dt);
            if (stuck && Mathf.Abs(v.x) < config.slideCrawlSpeed) v.x = dir * config.slideCrawlSpeed;

            bool released = move.y > -0.5f;
            bool tooSlow = Mathf.Abs(v.x) < config.slideMinSpeed * 0.6f;
            bool turned = move.x != 0f && Math.Sign(move.x) != dir;
            if (!stuck && (released || tooSlow || turned || !IsGrounded)) IsSliding = false;
            return v;
        }

        bool TrySlideJump(ref Vector2 v)
        {
            if (!IsSliding || !JumpBuffered || Time.time - lastGroundedTime > config.coyoteTime || !CanStand()) return false;
            IsSliding = false;
            SetCrouched(false);
            int dir = v.x != 0f ? Math.Sign(v.x) : Facing;
            float keptX = Mathf.Abs(v.x) + config.slideJumpBoost;
            DoJump(ref v);
            v.y *= config.slideJumpHeightScale;
            v.x = dir * keptX;
            return true;
        }

        // ----------------------------------------------------------------- Vault

        /// <summary>Running into low cover hops you over it without touching your speed.</summary>
        void TryVault(ref Vector2 v, Vector2 move, float dt)
        {
            if (!IsGrounded || IsSliding || move.x == 0f || Math.Sign(move.x) != Math.Sign(v.x) || Mathf.Abs(v.x) < config.vaultMinSpeed) return;
            int side = Math.Sign(move.x);
            // Look far enough ahead to clear the cover at the current speed.
            float reach = Mathf.Abs(v.x) * 0.11f + 0.1f;
            float top = config.vaultMaxHeight01;
            if (!SideHit(side, 0.05f, top, reach) || SideHit(side, top + 0.05f, 1.05f, reach + 0.4f)) return;
            v.y = config.vaultUpVelocity;
            isJumping = false;
            IsGrounded = false;
            lastGroundedTime = float.NegativeInfinity;
            vaultTimer = 0.25f;
            Vaulted?.Invoke();
        }

        /// <summary>Side of a wall within <paramref name="distance"/> (+1 right, -1 left, 0 none).</summary>
        int WallWithin(float distance)
        {
            Bounds b = body.bounds;
            var size = new Vector2(distance, b.size.y * 0.6f);
            if (Physics2D.OverlapBox(new Vector2(b.max.x + distance * 0.5f, b.center.y), size, 0f, groundMask)) return 1;
            if (Physics2D.OverlapBox(new Vector2(b.min.x - distance * 0.5f, b.center.y), size, 0f, groundMask)) return -1;
            return 0;
        }

        void UpdateContacts(Vector2 v)
        {
            Bounds b = body.bounds;
            var feet = new Vector2(b.center.x, b.min.y - groundCheckDepth * 0.5f);
            bool groundHit = Physics2D.OverlapBox(feet, new Vector2(b.size.x * 0.9f, groundCheckDepth), 0f, groundMask);

            bool wasGrounded = IsGrounded;
            IsGrounded = groundHit && v.y <= 0.01f;
            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                wallRunUsedSide = 0;
                if (!wasGrounded)
                {
                    groundedSince = Time.time;
                    Landed?.Invoke(-lastVelocityY);
                }
                if (CurrentState != State.Dash) DashesLeft = config.maxDashes;
                ClimbJumpsLeft = config.climbJumps;
            }

            var sideSize = new Vector2(wallCheckDistance, b.size.y * 0.6f);
            bool left = Physics2D.OverlapBox(new Vector2(b.min.x - wallCheckDistance * 0.5f, b.center.y), sideSize, 0f, groundMask);
            bool right = Physics2D.OverlapBox(new Vector2(b.max.x + wallCheckDistance * 0.5f, b.center.y), sideSize, 0f, groundMask);
            WallSide = right ? 1 : left ? -1 : 0;
            if (WallSide != 0 && !IsGrounded)
            {
                lastWallTime = Time.time;
                lastWallSide = WallSide;
            }
        }

        // ---------------------------------------------------------------- Normal

        Vector2 TickNormal(Vector2 v, float dt)
        {
            Vector2 move = input.Move;
            if (move.x != 0f && wallJumpLockTimer <= 0f) Facing = move.x > 0f ? 1 : -1;

            if (TryLateSlingBoost(ref v)) return v;
            if (TryStartGrapple()) return v;
            if (TryStartDash(ref v)) return v;

            if (!IsSliding && IsGrounded && move.y < -0.5f && Mathf.Abs(v.x) >= config.slideMinSpeed)
                StartSlide(ref v);
            if (!TrySlideJump(ref v) && !IsSliding) TryJump(ref v);

            // Horizontal
            float top = TopSpeed;
            float target = moveLockTimer > 0f ? 0f : move.x * top;
            bool overspeed = Mathf.Abs(v.x) > top &&
                             (target == 0f && !IsGrounded || target != 0f && Math.Sign(target) == Math.Sign(v.x));
            if (IsSliding)
            {
                v = TickSlide(v, move, dt);
            }
            else if (IsBoosting)
            {
                // Hold Sling-Boost momentum; allow only gentle steering.
                if (target != 0f && Math.Sign(target) != Math.Sign(v.x))
                    v.x = Mathf.MoveTowards(v.x, target, config.airDeceleration * 0.5f * dt);
            }
            else if (overspeed)
            {
                // Just landed: hold the speed for a moment so chained jumps don't bleed it.
                bool grace = IsGrounded && Time.time - groundedSince < config.landingGrace;
                float decel = grace ? 0f : IsGrounded ? config.groundOverspeedDeceleration : config.airOverspeedDeceleration;
                v.x = Mathf.MoveTowards(v.x, Mathf.Sign(v.x) * top, decel * dt);
            }
            else
            {
                float accel = IsGrounded
                    ? (target != 0f ? config.groundAcceleration : config.groundDeceleration)
                    : (target != 0f ? config.airAcceleration : config.airDeceleration);
                if (wallJumpLockTimer > 0f) accel *= 0.25f;
                v.x = Mathf.MoveTowards(v.x, target, accel * dt);
            }

            // Variable jump height
            if (isJumping && v.y <= 0f) isJumping = false;
            if (isJumping && !input.JumpHeld)
            {
                v.y *= config.jumpCutMultiplier;
                isJumping = false;
            }

            TryVault(ref v, move, dt);

            // Wall run: hold the climb speed (no gravity) while you keep pushing into the wall.
            bool wallRunning = false;
            if (wallRunTimer > 0f)
            {
                wallRunTimer -= dt;
                wallRunning = WallSide == wallRunSide && move.x != 0f && Math.Sign(move.x) == wallRunSide;
                if (wallRunning) v.y = Mathf.Max(v.y, wallRunSpeed);
                else wallRunTimer = 0f;
            }

            // Gravity
            if (!wallRunning)
            {
                float g = config.Gravity;
                if (IsBoosting) g *= config.boostGravityMultiplier;
                else if (v.y < 0f) g *= config.fallGravityMultiplier;
                if (!IsGrounded && !IsBoosting && input.JumpHeld && Mathf.Abs(v.y) < config.apexHangThreshold)
                    g *= config.apexGravityMultiplier;
                v.y -= g * dt;
            }

            // Wall cling: pushing into a wall brakes the fall smoothly down to a slide.
            bool pushingWall = WallSide != 0 && move.x != 0f && Math.Sign(move.x) == WallSide;
            IsWallSliding = !IsGrounded && !wallRunning && pushingWall && v.y < 0f;
            if (IsWallSliding)
            {
                float slide = ClimbJumpsLeft > 0 ? config.wallSlideSpeed : config.tiredWallSlideSpeed;
                if (v.y < -slide) v.y = Mathf.MoveTowards(v.y, -slide, config.wallSlideFriction * dt);
            }

            // Ledge hop: reaching the top of a wall pops you up and over it, keeping any speed you had.
            if (!IsGrounded && move.x != 0f && v.y < 8f && wallJumpLockTimer <= 0f)
            {
                int side = Math.Sign(move.x);
                if (SideHit(side, 0.05f, 0.45f) && !SideHit(side, 0.55f, 1.05f))
                {
                    float keep = Math.Sign(v.x) == side ? Mathf.Abs(v.x) : 0f;
                    v = new Vector2(side * Mathf.Max(config.ledgeHopVelocity.x, keep), config.ledgeHopVelocity.y);
                    wallJumpLockTimer = 0.12f;
                    isJumping = false;
                    vaultTimer = 0.2f;
                }
            }

            // Fast fall: holding down while falling raises the terminal velocity gradually.
            bool fastFalling = move.y < -0.5f && !IsGrounded && !IsWallSliding && !IsSliding && v.y < 0f;
            float targetMaxFall = fastFalling ? config.fastFallMaxSpeed : config.maxFallSpeed;
            currentMaxFall = Mathf.MoveTowards(currentMaxFall, targetMaxFall, config.fastFallAcceleration * dt);
            v.y = Mathf.Max(v.y, -currentMaxFall);
            return v;
        }

        bool JumpBuffered => Time.time - input.LastJumpPressTime <= config.jumpBufferTime * GameSettings.InputBufferScale;

        bool TryJump(ref Vector2 v)
        {
            if (!JumpBuffered) return false;

            if (Time.time - lastGroundedTime <= config.coyoteTime)
            {
                DoJump(ref v);
                return true;
            }
            if (Time.time - lastWallTime <= config.wallCoyoteTime)
            {
                DoWallJump(ref v, lastWallSide);
                return true;
            }
            // Like Celeste, a wall a few pixels away still counts.
            int nearWall = WallWithin(config.wallJumpCheckDistance);
            if (nearWall != 0)
            {
                DoWallJump(ref v, nearWall);
                return true;
            }
            return false;
        }

        void DoJump(ref Vector2 v)
        {
            input.ConsumeJump();
            wallRunTimer = 0f;
            v.y = config.JumpVelocity;
            if (input.Move.x != 0f) v.x += Mathf.Sign(input.Move.x) * config.jumpHorizontalBoost;
            isJumping = true;
            IsGrounded = false;
            lastGroundedTime = float.NegativeInfinity;
            Jumped?.Invoke();
        }

        /// <summary>
        /// Holding toward the wall = climb jump (mostly up, costs grip, you re-grab the same wall).
        /// Holding away / neutral, or out of grip = wall jump that kicks you off the wall.
        /// </summary>
        void DoWallJump(ref Vector2 v, int wallSide)
        {
            input.ConsumeJump();
            wallRunTimer = 0f;
            bool intoWall = input.Move.x != 0f && Math.Sign(input.Move.x) == wallSide;
            if (intoWall && ClimbJumpsLeft > 0)
            {
                ClimbJumpsLeft--;
                v = new Vector2(-wallSide * config.climbJumpVelocity.x, config.climbJumpVelocity.y);
                Facing = wallSide;
                wallJumpLockTimer = config.climbJumpControlLock;
                // Fixed height: a quick tap still climbs a full step (no variable-jump cut).
                isJumping = false;
            }
            else
            {
                v = new Vector2(-wallSide * config.wallJumpVelocity.x, config.wallJumpVelocity.y);
                Facing = -wallSide;
                wallJumpLockTimer = config.wallJumpControlLock;
                isJumping = true;
            }
            lastWallTime = float.NegativeInfinity;
            Jumped?.Invoke();
        }

        /// <summary>Solid geometry beside the body on <paramref name="side"/>, within a vertical slice (0 = feet, 1 = head).</summary>
        bool SideHit(int side, float from01, float to01, float reach = -1f)
        {
            if (reach <= 0f) reach = wallCheckDistance;
            // Slices are measured against standing height, so a crouched body still reads walls correctly.
            Bounds b = body.bounds;
            float height = capsule ? standSize.y : b.size.y;
            float y0 = b.min.y + height * from01;
            float y1 = b.min.y + height * to01;
            float x = side > 0 ? b.max.x + reach * 0.5f : b.min.x - reach * 0.5f;
            return Physics2D.OverlapBox(new Vector2(x, (y0 + y1) * 0.5f), new Vector2(reach, y1 - y0), 0f, groundMask);
        }

        // ------------------------------------------------------------------ Dash

        bool TryStartDash(ref Vector2 v)
        {
            if (Time.time - input.LastDashPressTime > config.dashBufferTime * GameSettings.InputBufferScale || DashesLeft <= 0) return false;
            StartDash(ref v);
            return true;
        }

        void StartDash(ref Vector2 v)
        {
            input.ConsumeDash();
            DashesLeft--;
            IsSliding = false;
            wallRunTimer = 0f;
            dashDirection = EightWay(input.Move);
            if (dashDirection == Vector2.zero) dashDirection = new Vector2(Facing, 0f);
            if (dashDirection.x != 0f) Facing = dashDirection.x > 0f ? 1 : -1;

            CurrentState = State.Dash;
            dashTimer = config.dashDuration;
            boostTimer = 0f;
            isJumping = false;
            v = dashDirection * config.dashSpeed;
            Dashed?.Invoke(dashDirection);
            GameManager.Instance?.HitStop(config.dashHitStop);
        }

        Vector2 TickDash(Vector2 v, float dt)
        {
            dashTimer -= dt;

            // Super wall jump (Celeste's "wallbounce"): dash straight up beside a wall, then jump.
            if (JumpBuffered && dashDirection.x == 0f && dashDirection.y > 0f)
            {
                int side = WallWithin(config.superWallJumpCheckDistance);
                if (side != 0)
                {
                    CurrentState = State.Normal;
                    input.ConsumeJump();
                    v = new Vector2(-side * config.superWallJumpHorizontal, config.superWallJumpSpeed);
                    Facing = -side;
                    isJumping = true;
                    wallJumpLockTimer = config.wallJumpControlLock * 1.5f;
                    Jumped?.Invoke();
                    return v;
                }
            }

            // Wall jump straight out of a dash.
            if (JumpBuffered && Time.time - lastGroundedTime > config.coyoteTime)
            {
                int side = WallWithin(config.wallJumpCheckDistance);
                if (side != 0)
                {
                    CurrentState = State.Normal;
                    DoWallJump(ref v, side);
                    return v;
                }
            }

            // Super-dash: jumping out of a grounded dash keeps most of its speed.
            if (JumpBuffered && Time.time - lastGroundedTime <= config.coyoteTime)
            {
                CurrentState = State.Normal;
                float keptX = dashDirection.x * config.dashSpeed * config.superDashMultiplier;
                DoJump(ref v);
                if (dashDirection.y < 0f)
                {
                    v.y *= 0.6f;
                    keptX *= 1.25f;
                }
                v.x = keptX;
                return v;
            }

            if (TryStartGrapple()) return v;

            v = dashDirection * config.dashSpeed;
            if (dashTimer <= 0f)
            {
                CurrentState = State.Normal;
                v *= config.dashEndSpeedMultiplier;
                if (v.y > 0f) v.y *= config.dashEndUpMultiplier;
            }
            return v;
        }

        static Vector2 EightWay(Vector2 m)
        {
            if (m.sqrMagnitude < 0.01f) return Vector2.zero;
            float angle = Mathf.Round(Mathf.Atan2(m.y, m.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
            var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            return new Vector2(Mathf.Round(d.x), Mathf.Round(d.y)).normalized;
        }

        // --------------------------------------------------------------- Grapple

        bool TryStartGrapple()
        {
            if (Time.time - input.LastGrapplePressTime > config.grappleBufferTime * GameSettings.InputBufferScale || !input.GrappleHeld) return false;
            if (!grapple.TryAttach()) return false;

            input.ConsumeGrapple();
            CurrentState = State.Grapple;
            IsSliding = false;
            wallRunTimer = 0f;
            boostTimer = 0f;
            isJumping = false;
            pendingTetherDash = float.NegativeInfinity;
            return true;
        }

        Vector2 TickGrapple(Vector2 v, float dt)
        {
            float now = Time.time;
            if (!grapple.IsAttached)
            {
                CurrentState = State.Normal;
                return v;
            }

            if (!input.GrappleHeld)
            {
                grapple.Release();
                CurrentState = State.Normal;
                return v;
            }

            if (JumpBuffered)
            {
                grapple.Release();
                CurrentState = State.Normal;
                input.ConsumeJump();
                v.y = Mathf.Max(v.y, config.JumpVelocity * 0.9f);
                isJumping = true;
                Jumped?.Invoke();
                return v;
            }

            // Dash while tethered: Sling-Boost if timed to the pull's peak, else a normal dash.
            if (now - input.LastDashPressTime <= config.dashBufferTime)
            {
                pendingTetherDash = input.LastDashPressTime;
                input.ConsumeDash();
            }
            if (!float.IsNegativeInfinity(pendingTetherDash))
            {
                var grade = grapple.BoostWindow.Evaluate(pendingTetherDash);
                if (grade != TimingGrade.Miss)
                {
                    pendingTetherDash = float.NegativeInfinity;
                    return SlingBoost(grade, v);
                }
                if (!grapple.PeakImminent || grapple.BoostWindow.HasExpiredFor(pendingTetherDash, now))
                {
                    pendingTetherDash = float.NegativeInfinity;
                    if (DashesLeft > 0)
                    {
                        grapple.Release();
                        StartDash(ref v);
                        return v;
                    }
                }
            }

            v = grapple.Tick(v, input.Move, dt);
            // Arrived at (and hacked) a drone: the grapple let go and popped us on top of it.
            if (!grapple.IsAttached) CurrentState = State.Normal;
            return v;
        }

        /// <summary>Dash pressed just after letting go of the rope, still inside the peak window.</summary>
        bool TryLateSlingBoost(ref Vector2 v)
        {
            if (Time.time - input.LastDashPressTime > config.dashBufferTime) return false;
            var grade = grapple.BoostWindow.Evaluate(input.LastDashPressTime);
            if (grade == TimingGrade.Miss) return false;
            input.ConsumeDash();
            v = SlingBoost(grade, v);
            return true;
        }

        Vector2 SlingBoost(TimingGrade grade, Vector2 v)
        {
            Vector2 move = input.Move;
            int dirX = move.x != 0f ? Math.Sign(move.x) : Mathf.Abs(v.x) > 1f ? Math.Sign(v.x) : Facing;
            float angle = move.y > 0.3f ? config.boostDiagonalAngle
                        : move.y < -0.3f ? -config.boostDiagonalAngle
                        : config.boostNeutralAngle;
            float rad = angle * Mathf.Deg2Rad;
            var dir = new Vector2(dirX * Mathf.Cos(rad), Mathf.Sin(rad));

            float baseSpeed = Mathf.Max(v.magnitude, grapple.PullSpeed, config.grapplePullMaxSpeed * 0.75f);
            float speed = baseSpeed * config.boostMultiplier * (grade == TimingGrade.Perfect ? config.perfectBoostBonus : 1f);
            speed = Mathf.Min(speed, config.maxBoostSpeed);

            grapple.Release(hackTarget: true);
            grapple.BoostWindow.Clear();
            CurrentState = State.Normal;
            Facing = dirX;
            boostTimer = config.boostDuration;
            DashesLeft = 0;
            isJumping = false;

            GameManager.Instance?.HitStop(config.boostHitStop);
            SlingBoosted?.Invoke(grade, dir);
            return dir * speed;
        }

        // ------------------------------------------------------- Life & helpers

        public bool NeedsDash => DashesLeft < config.maxDashes;

        public void RefillDash()
        {
            DashesLeft = config.maxDashes;
            ClimbJumpsLeft = config.climbJumps;
            wallRunUsedSide = 0;
        }

        /// <summary>External launch (springs): cancels dash/grapple, sets velocity, refills the dash.</summary>
        public void Launch(Vector2 velocity)
        {
            if (CurrentState == State.Dead) return;
            grapple.Release();
            CurrentState = State.Normal;
            IsSliding = false;
            wallRunTimer = 0f;
            boostTimer = 0f;
            isJumping = false;
            wallJumpLockTimer = Mathf.Abs(velocity.x) > 0.1f ? config.wallJumpControlLock : 0f;
            RefillDash();
            IsGrounded = false;
            lastGroundedTime = float.NegativeInfinity;
            rb.linearVelocity = velocity;
            Jumped?.Invoke();
        }

        /// <summary>Hit by a cop taser: drops off the grapple, cancels dash and boost, and locks input briefly.</summary>
        public bool IsStunned => !IsDead && input.Suppressed;

        public void Stun(float duration)
        {
            if (CurrentState == State.Dead || GameManager.Instance && GameManager.Instance.IsFinished) return;
            grapple.Release();
            CurrentState = State.Normal;
            IsSliding = false;
            wallRunTimer = 0f;
            Flow = 0f;
            boostTimer = 0f;
            isJumping = false;
            rb.linearVelocity *= 0.3f;
            input.Suppress(duration);
            Stunned?.Invoke();
        }

        /// <summary>Moved by whatever the player stands on (moving platforms, elevators).</summary>
        public void Carry(Vector2 delta)
        {
            if (CurrentState != State.Dead) rb.position += delta;
        }

        public void Kill()
        {
            if (CurrentState == State.Dead || GameManager.Instance && GameManager.Instance.IsFinished) return;
            grapple.Release();
            CurrentState = State.Dead;
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
            Died?.Invoke();
            GameManager.Instance?.OnPlayerDied();
        }

        /// <summary>Cop contact: spends a shield (Casual difficulty) if there is one, otherwise kills.</summary>
        public void Hurt()
        {
            if (CurrentState == State.Dead || Time.time < invulnerableUntil) return;
            if (ShieldsLeft <= 0)
            {
                Kill();
                return;
            }
            ShieldsLeft--;
            invulnerableUntil = Time.time + 1.5f;
            Stun(0.4f);
            ShieldBroken?.Invoke();
        }

        /// <summary>
        /// Moves the player without resetting their state (e.g. landing on a hacked drone).
        /// <paramref name="holdStill"/> ignores run input briefly so a held direction doesn't walk them off.
        /// </summary>
        public void PlaceAt(Vector2 position, float holdStill = 0f)
        {
            rb.position = position;
            transform.position = position;
            rb.linearVelocity = Vector2.zero;
            isJumping = false;
            moveLockTimer = holdStill;
        }

        public void RespawnAt(Vector2 position)
        {
            rb.simulated = true;
            rb.position = position;
            transform.position = position;
            rb.linearVelocity = Vector2.zero;
            CurrentState = State.Normal;
            IsSliding = false;
            SetCrouched(false);
            wallRunTimer = 0f;
            wallRunUsedSide = 0;
            Flow = 0f;
            DashesLeft = config.maxDashes;
            ClimbJumpsLeft = config.climbJumps;
            ShieldsLeft = GameSettings.ExtraHits;
            invulnerableUntil = 0f;
            boostTimer = 0f;
            isJumping = false;
            Facing = 1;
            input.ClearSuppress();
            input.ConsumeJump();
            input.ConsumeDash();
            input.ConsumeGrapple();
            Respawned?.Invoke();
        }
    }
}
