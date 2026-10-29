using System;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// The prosthetic arm's grapple: auto-targets the best point in the aim cone,
    /// reels the player in, then tethers them on a pendulum. It also tracks the
    /// pull's "peak" moments for the Sling-Boost timing window.
    /// </summary>
    public class GrappleHook : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] Transform handPoint;
        [SerializeField] LineRenderer rope;
        [SerializeField] Transform reticle;
        [SerializeField] LayerMask obstacleMask;
        [SerializeField] float mouseAimCone = 40f;

        public event Action<GrappleTarget> Attached;
        public event Action<GrappleTarget> Hacked;

        public TimedInputWindow BoostWindow { get; } = new TimedInputWindow();
        public bool IsAttached => target != null;
        public GrappleTarget Target => target;
        public float PullSpeed { get; private set; }
        /// <summary>True while a boost peak could still arrive soon (pulling in, or swinging fast).</summary>
        public bool PeakImminent { get; private set; }

        MovementConfig Config => player.Config;
        Vector2 Origin => handPoint ? (Vector2)handPoint.position : (Vector2)transform.position;

        GrappleTarget target;
        float ropeLength;
        bool reachedEnd;
        bool peakScheduled;
        float lastSide;
        Camera cam;

        void Awake()
        {
            cam = Camera.main;
            if (rope) rope.enabled = false;
        }

        void Start()
        {
            BoostWindow.Configure(Config.boostWindowBeforePeak, Config.boostWindowAfterPeak, Config.perfectTolerance);
        }

        void LateUpdate()
        {
            if (IsAttached)
            {
                rope.SetPosition(0, Origin);
                rope.SetPosition(1, target.Anchor);
                SetReticle(target);
                return;
            }

            SetReticle(player.IsDead ? null : FindBestTarget(CurrentAim(), player.Input.GrappleAimedWithMouse));
        }

        void SetReticle(GrappleTarget t)
        {
            if (!reticle) return;
            reticle.gameObject.SetActive(t != null);
            if (t == null) return;
            reticle.position = t.Anchor;
            reticle.Rotate(0f, 0f, -180f * Time.deltaTime);
        }

        Vector2 CurrentAim()
        {
            var input = player.Input;
            if (input.GrappleAimedWithMouse && cam)
                return PlayerInputReader.MouseWorldPosition(cam) - Origin;

            Vector2 m = input.Move;
            return new Vector2(m.x != 0f ? m.x : player.Facing * 0.6f, m.y != 0f ? m.y : 0.8f).normalized;
        }

        public GrappleTarget FindBestTarget(Vector2 aim, bool fromMouse)
        {
            float halfCone = (fromMouse ? mouseAimCone : Config.grappleAimCone) * 0.5f;
            GrappleTarget best = null;
            float bestScore = float.MaxValue;

            foreach (var t in GrappleTarget.All)
            {
                if (!t.IsAvailable) continue;
                Vector2 to = t.Anchor - Origin;
                float dist = to.magnitude;
                if (dist > Config.grappleRange || dist < 0.5f) continue;
                float angle = Vector2.Angle(aim, to);
                if (angle > halfCone) continue;
                if (!HasLineOfSight(t, dist)) continue;

                float score = dist / Config.grappleRange + angle / halfCone * 0.8f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        bool HasLineOfSight(GrappleTarget t, float dist)
        {
            var hit = Physics2D.Linecast(Origin, t.Anchor, obstacleMask);
            return !hit || hit.collider.transform.IsChildOf(t.transform) || hit.distance >= dist - 0.6f;
        }

        public bool TryAttach()
        {
            var t = FindBestTarget(CurrentAim(), player.Input.GrappleAimedWithMouse);
            if (t == null) return false;

            target = t;
            Vector2 to = t.Anchor - (Vector2)player.transform.position;
            ropeLength = to.magnitude;
            PullSpeed = Mathf.Max(0f, Vector2.Dot(player.Velocity, to.normalized));
            reachedEnd = false;
            peakScheduled = false;
            lastSide = 0f;
            PeakImminent = true;
            BoostWindow.Clear();
            rope.enabled = true;
            t.Pulse();
            Attached?.Invoke(t);
            return true;
        }

        public void Release(bool hackTarget = false)
        {
            if (target == null) return;
            if (hackTarget && target.Kind == GrappleTarget.TargetKind.Drone) Hack(target);
            target = null;
            rope.enabled = false;
            PeakImminent = false;
        }

        void Hack(GrappleTarget t)
        {
            var stunnable = t.GetComponentInParent<IStunnable>();
            stunnable?.Stun(Config.droneHackDuration);
            t.Pulse();
            Hacked?.Invoke(t);
        }

        /// <summary>Advances the tether one physics step and returns the player's new velocity.</summary>
        public Vector2 Tick(Vector2 v, Vector2 move, float dt)
        {
            float now = Time.time;
            Vector2 pos = player.transform.position;
            Vector2 anchor = target.Anchor;
            Vector2 to = anchor - pos;
            float dist = Mathf.Max(to.magnitude, 0.001f);
            Vector2 dir = to / dist;

            if (target.Kind == GrappleTarget.TargetKind.Drone)
                return TickDronePull(v, dir, dist, now, dt);

            if (!reachedEnd)
                return TickPull(v, move, dir, dist, now, dt);

            return TickSwing(v, move, pos, anchor, now, dt);
        }

        Vector2 PullTowards(Vector2 v, Vector2 dir, float tangentialDamping, float dt)
        {
            PullSpeed = Mathf.MoveTowards(PullSpeed, Config.grapplePullMaxSpeed, Config.grapplePullAcceleration * dt);
            float radialNow = Vector2.Dot(v, dir);
            float radial = Mathf.MoveTowards(radialNow, PullSpeed, Config.grapplePullAcceleration * 2f * dt);
            Vector2 tangential = (v - dir * radialNow) * (1f - Mathf.Min(1f, tangentialDamping * dt));
            return dir * radial + tangential;
        }

        void SchedulePeak(float distanceToPeak, float radialSpeed, float now)
        {
            if (peakScheduled) return;
            float timeToPeak = distanceToPeak / Mathf.Max(radialSpeed, 0.1f);
            if (timeToPeak > BoostWindow.before) return;
            BoostWindow.SetPeak(now + timeToPeak);
            peakScheduled = true;
        }

        Vector2 TickDronePull(Vector2 v, Vector2 dir, float dist, float now, float dt)
        {
            if (dist <= Config.droneArriveDistance)
            {
                if (!peakScheduled) BoostWindow.SetPeak(now);
                var drone = target;
                Hack(drone);
                Release();
                LandOnTop(drone);
                return Vector2.zero;
            }

            v = PullTowards(v, dir, 6f, dt);
            SchedulePeak(dist - Config.droneArriveDistance, Vector2.Dot(v, dir), now);
            PeakImminent = true;
            return v;
        }

        /// <summary>Plants the player's feet on top of a hacked drone.</summary>
        void LandOnTop(GrappleTarget drone)
        {
            var droneCollider = drone.GetComponentInChildren<Collider2D>();
            Bounds top = droneCollider ? droneCollider.bounds : new Bounds(drone.Anchor, Vector3.zero);
            float halfHeight = player.Body.bounds.extents.y;
            player.PlaceAt(new Vector2(top.center.x, top.max.y + halfHeight + 0.02f), Config.droneLandHoldTime);
        }

        Vector2 TickPull(Vector2 v, Vector2 move, Vector2 dir, float dist, float now, float dt)
        {
            v = PullTowards(v, dir, 3f, dt);
            v += new Vector2(move.x * Config.swingForce * 0.5f * dt, 0f);

            float radial = Vector2.Dot(v, dir);
            float remaining = dist - Config.minRopeLength;
            SchedulePeak(remaining, radial, now);
            PeakImminent = true;

            if (remaining <= Mathf.Max(radial * dt, 0.05f))
            {
                // End of the reel: kill the inward momentum and hang from the rope.
                reachedEnd = true;
                ropeLength = Config.minRopeLength;
                if (!peakScheduled)
                {
                    BoostWindow.SetPeak(now);
                    peakScheduled = true;
                }
                // Damp the leftover swing so the player settles under the point instead of looping over it.
                v -= dir * Mathf.Max(0f, radial);
                v *= 0.5f;
            }
            return v;
        }

        Vector2 TickSwing(Vector2 v, Vector2 move, Vector2 pos, Vector2 anchor, float now, float dt)
        {
            v.y -= Config.Gravity * dt;
            v.x += move.x * Config.swingForce * dt;

            // Rope constraint: never further than ropeLength from the anchor.
            Vector2 next = pos + v * dt;
            Vector2 fromAnchor = next - anchor;
            if (fromAnchor.magnitude > ropeLength)
            {
                Vector2 n = fromAnchor.normalized;
                float outward = Vector2.Dot(v, n);
                if (outward > 0f) v -= n * outward;
                float excess = (pos + v * dt - anchor).magnitude - ropeLength;
                if (excess > 0f) v -= n * (excess / dt);
            }

            // Swinging through the bottom of the arc is another boost peak.
            float side = Mathf.Sign(pos.x - anchor.x);
            float speed = v.magnitude;
            if (lastSide != 0f && side != lastSide && pos.y < anchor.y && speed >= Config.swingPeakMinSpeed)
                BoostWindow.SetPeak(now);
            lastSide = side;
            PeakImminent = speed >= Config.swingPeakMinSpeed * 0.7f;
            return v;
        }
    }
}
