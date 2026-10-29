using UnityEngine;

namespace NeonRunner
{
    /// <summary>Every tunable number for the player's movement and arm lives here.</summary>
    [CreateAssetMenu(menuName = "Neon Rush/Movement Config", fileName = "MovementConfig")]
    public class MovementConfig : ScriptableObject
    {
        [Header("Run")]
        public float maxRunSpeed = 13f;
        public float groundAcceleration = 170f;
        public float groundDeceleration = 190f;
        public float airAcceleration = 120f;
        public float airDeceleration = 60f;
        [Tooltip("How fast speed above the current top speed bleeds off on the ground.")]
        public float groundOverspeedDeceleration = 40f;
        [Tooltip("How fast speed above the current top speed bleeds off in the air (keeps Sling-Boost momentum).")]
        public float airOverspeedDeceleration = 10f;

        [Header("Flow (momentum)")]
        [Tooltip("Top speed once Flow is full. Flow builds while you keep moving fast in one direction.")]
        public float flowMaxSpeed = 19f;
        [Tooltip("Seconds of uninterrupted running to fill Flow.")]
        public float flowBuildTime = 1.4f;
        [Tooltip("Flow lost per second while stopped, turning around or slowed down.")]
        public float flowDecay = 2.5f;
        [Tooltip("After landing, ground overspeed isn't bled off for this long, so chained jumps keep their speed.")]
        public float landingGrace = 0.12f;

        [Header("Jump")]
        public float jumpHeight = 3.4f;
        public float timeToJumpApex = 0.34f;
        public float fallGravityMultiplier = 1.7f;
        [Tooltip("Upward velocity is multiplied by this when jump is released early (variable jump height).")]
        public float jumpCutMultiplier = 0.45f;
        public float apexHangThreshold = 1.8f;
        public float apexGravityMultiplier = 0.6f;
        public float maxFallSpeed = 26f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.12f;
        [Tooltip("Horizontal speed added in the held direction when jumping off the ground (Celeste's JumpHBoost).")]
        public float jumpHorizontalBoost = 2.5f;

        [Header("Fast Fall (hold down)")]
        public float fastFallMaxSpeed = 34f;
        public float fastFallAcceleration = 60f;

        [Header("Slide (hold down while running)")]
        public float slideMinSpeed = 6f;
        [Tooltip("Speed added when a slide starts (only once per slideCooldown).")]
        public float slideBoost = 4f;
        public float slideFriction = 7f;
        public float slideCooldown = 0.45f;
        [Tooltip("Forced crawl speed while stuck under a low ceiling.")]
        public float slideCrawlSpeed = 6f;
        [Tooltip("Collider height while sliding (standing is 1.4).")]
        public float slideHeight = 0.7f;
        [Tooltip("Jumping out of a slide: long jump. Horizontal speed added, and the jump's height scale.")]
        public float slideJumpBoost = 3f;
        public float slideJumpHeightScale = 0.85f;

        [Header("Vault & Wall Run")]
        [Tooltip("Running into an obstacle up to this tall (fraction of body height) vaults it without losing speed.")]
        public float vaultMaxHeight01 = 0.75f;
        public float vaultMinSpeed = 5f;
        public float vaultUpVelocity = 13f;
        [Tooltip("Hitting a tall wall at least this fast while holding toward it runs you up it.")]
        public float wallRunMinSpeed = 10f;
        public float wallRunUpSpeedMin = 11f;
        public float wallRunUpSpeedMax = 16f;
        [Tooltip("Fraction of impact speed converted to climbing speed.")]
        public float wallRunConversion = 0.85f;
        public float wallRunDuration = 0.38f;

        [Header("Corner Correction")]
        [Tooltip("Rising into a ceiling corner slides you around it by up to this distance.")]
        public float ceilingCornerCorrection = 0.35f;
        [Tooltip("Dashing/boosting into a ledge lip pops you up onto it by up to this distance.")]
        public float dashLedgeCorrection = 0.35f;
        [Tooltip("After hitting a wall, speed is given back if the path clears within this time.")]
        public float wallSpeedRetentionTime = 0.06f;

        [Header("Wall")]
        public float wallSlideSpeed = 3f;
        public Vector2 wallJumpVelocity = new Vector2(13f, 17.3f);
        [Tooltip("Seconds of reduced air control after a wall jump so it doesn't get cancelled.")]
        public float wallJumpControlLock = 0.15f;
        public float wallCoyoteTime = 0.08f;
        [Tooltip("Wall jumps work from this far away (also while dashing).")]
        public float wallJumpCheckDistance = 0.3f;

        [Header("Wall Climb")]
        [Tooltip("Climb jumps (jump while holding toward a wall) before you need ground or a data node.")]
        public int climbJumps = 4;
        [Tooltip("Climb jump: small push off the wall, mostly upward, so holding toward the wall re-grabs it.")]
        public Vector2 climbJumpVelocity = new Vector2(2.9f, 17.9f);
        public float climbJumpControlLock = 0.08f;
        [Tooltip("How quickly a fast fall is braked down to the slide speed when you grab a wall.")]
        public float wallSlideFriction = 160f;
        [Tooltip("Slide speed once your grip (climb jumps) is used up.")]
        public float tiredWallSlideSpeed = 7f;
        [Tooltip("Pop up and over a ledge when the wall top is below your chest (horizontal speed is kept if higher).")]
        public Vector2 ledgeHopVelocity = new Vector2(6f, 11.5f);

        [Header("Super Wall Jump (dash up beside a wall, then jump)")]
        public float superWallJumpSpeed = 25.3f;
        public float superWallJumpHorizontal = 10f;
        public float superWallJumpCheckDistance = 0.5f;

        [Header("Omni-Dash")]
        public int maxDashes = 1;
        public float dashSpeed = 30f;
        public float dashDuration = 0.14f;
        public float dashEndSpeedMultiplier = 0.7f;
        [Tooltip("Extra multiplier on upward speed when an upward dash ends.")]
        public float dashEndUpMultiplier = 0.7f;
        public float dashBufferTime = 0.08f;
        public float dashHitStop = 0.02f;
        [Tooltip("Horizontal speed kept when jumping out of a grounded dash (super-dash).")]
        public float superDashMultiplier = 0.9f;

        [Header("Grapple")]
        public float grappleRange = 12f;
        public float grappleAimCone = 80f;
        public float grapplePullAcceleration = 85f;
        public float grapplePullMaxSpeed = 26f;
        public float minRopeLength = 1.6f;
        public float droneArriveDistance = 1.4f;
        public float swingForce = 25f;
        public float droneHackDuration = 3f;
        [Tooltip("After landing on a hacked drone, run input is ignored this long so you don't walk off.")]
        public float droneLandHoldTime = 0.35f;
        public float grappleBufferTime = 0.1f;

        [Header("Sling-Boost")]
        public float boostMultiplier = 1.6f;
        public float perfectBoostBonus = 1.2f;
        public float maxBoostSpeed = 50f;
        [Tooltip("Seconds before the pull's peak that the timing window opens.")]
        public float boostWindowBeforePeak = 0.15f;
        [Tooltip("Seconds after the pull's peak that the timing window stays open.")]
        public float boostWindowAfterPeak = 0.2f;
        [Tooltip("|press - peak| under this is graded Perfect.")]
        public float perfectTolerance = 0.08f;
        public float boostDuration = 0.45f;
        public float boostGravityMultiplier = 0.25f;
        public float boostDiagonalAngle = 35f;
        public float boostNeutralAngle = 15f;
        public float swingPeakMinSpeed = 6f;
        public float boostHitStop = 0.06f;

        [Header("Tactical")]
        public float taserCooldown = 0.5f;
        public float taserSpeed = 30f;
        public float taserRange = 5.5f;
        public float taserStunDuration = 2f;
        public float smokeCooldown = 6f;
        public float smokeRadius = 5.2f;
        public float smokeDuration = 4f;

        public float Gravity => 2f * jumpHeight / (timeToJumpApex * timeToJumpApex);
        public float JumpVelocity => Gravity * timeToJumpApex;
    }
}
