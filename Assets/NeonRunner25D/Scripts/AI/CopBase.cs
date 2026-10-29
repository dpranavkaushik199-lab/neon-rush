using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonRunner
{
    /// <summary>
    /// Shared cop brain: sight-cone detection, alerting nearby units, losing the
    /// trail, stuns (IStunnable) and smoke confusion. Subclasses only move.
    /// </summary>
    public abstract class CopBase : MonoBehaviour, IStunnable
    {
        public enum Awareness { Patrol, Chase }

        public static readonly List<CopBase> All = new List<CopBase>();

        public static bool AnyChasing
        {
            get
            {
                foreach (var cop in All)
                    if (cop.State == Awareness.Chase && !cop.IsStunned) return true;
                return false;
            }
        }

        [Header("Senses")]
        [SerializeField] protected float sightRange = 8f;
        [SerializeField] protected float sightAngle = 70f;
        [SerializeField] protected float reactionTime = 0.25f;
        [SerializeField] protected float loseSightTime = 2.5f;
        [SerializeField] protected float alertRadius = 14f;
        [SerializeField] protected LayerMask obstacleMask;

        [Header("Presentation")]
        [SerializeField] protected Light sightLight;
        [Tooltip("Flat fan mesh in the gameplay plane that shows exactly what the cop can see.")]
        [SerializeField] protected MeshFilter sightCone;
        [SerializeField] protected Renderer[] bodyRenderers = new Renderer[0];
        [SerializeField] protected GameObject alertIcon;
        [SerializeField] protected Collider2D hurtbox;
        [SerializeField] protected Color patrolLightColor = new Color(1f, 0.85f, 0.45f);
        [SerializeField] protected Color chaseLightColor = new Color(1f, 0.1f, 0.15f);
        [SerializeField] protected Color stunnedTint = new Color(0.3f, 1f, 1f);

        [Header("Taser (armed cops)")]
        [Tooltip("Armed cops fire taser bolts at the player while chasing and in sight.")]
        [SerializeField] protected bool canShoot;
        [SerializeField] protected float shootInterval = 1.6f;
        [Tooltip("Telegraph before each shot: the sight cone flashes white so the player can react.")]
        [SerializeField] protected float shootWindup = 0.45f;
        [SerializeField] protected float boltSpeed = 17f;
        [SerializeField] protected float boltRange = 12f;
        [SerializeField] protected float playerStunDuration = 0.9f;
        [Tooltip("Seconds of player motion to lead the shot by.")]
        [SerializeField] protected float aimLead = 0.1f;
        [SerializeField] protected LayerMask boltMask;
        [SerializeField] protected Sprite boltSprite;
        [SerializeField] protected Material boltMaterial;
        [SerializeField] protected Color boltColor = new Color(1f, 0.3f, 0.22f);

        public Awareness State { get; private set; }
        public bool IsStunned => stunTimer > 0f;
        protected bool IsConfused => Time.time < confusedUntil;
        protected PlayerController Player { get; private set; }
        protected Vector2 LastKnownPlayerPosition { get; private set; }
        protected float StunTimeLeft => stunTimer;

        protected abstract Vector2 EyePosition { get; }
        protected abstract Vector2 LookDirection { get; }
        protected virtual bool LethalOnContact => true;
        /// <summary>When true this cop can't see, be alerted to, or hurt the player.</summary>
        protected virtual bool IsPlayerHiddenFromMe => false;
        /// <summary>Spotters (drones) radio every unit in the level instead of only those within alertRadius.</summary>
        protected virtual bool AlertsWholeLevel => false;

        const float RelayInterval = 0.25f;
        float nextRelayTime;
        float seeTimer;
        float lastSeenTime;
        float stunTimer;
        float confusedUntil;
        float baseLightIntensity;
        Renderer coneRenderer;
        float nextShotTime;
        float windupTimer = -1f;
        protected bool IsWindingUp => windupTimer >= 0f;

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        protected virtual void Start()
        {
            Player = GameManager.Instance ? GameManager.Instance.Player : FindAnyObjectByType<PlayerController>();
            if (sightLight) baseLightIntensity = sightLight.intensity;
            if (sightCone)
            {
                sightCone.sharedMesh = BuildConeMesh(sightAngle, sightRange);
                coneRenderer = sightCone.GetComponent<Renderer>();
            }
            if (alertIcon) alertIcon.SetActive(false);
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;

            if (IsStunned)
            {
                stunTimer -= dt;
                if (stunTimer <= 0f)
                {
                    stunTimer = 0f;
                    OnStunEnded();
                }
                UpdatePresentation();
                TickMovement(dt);
                return;
            }

            bool sees = CanSeePlayer();
            if (sees)
            {
                lastSeenTime = Time.time;
                LastKnownPlayerPosition = Player.transform.position;
                if (State != Awareness.Chase)
                {
                    seeTimer += dt;
                    if (seeTimer >= reactionTime * GameSettings.ReactionTimeScale) Spot();
                }
                else if (AlertsWholeLevel && Time.time >= nextRelayTime)
                {
                    // Keep feeding the live position so every unit tracks the player, not a stale spot.
                    nextRelayTime = Time.time + RelayInterval;
                    Broadcast();
                }
            }
            else
            {
                seeTimer = Mathf.Max(0f, seeTimer - dt);
                if (State == Awareness.Chase && Time.time - lastSeenTime > loseSightTime) ReturnToPatrol();
            }

            if (LethalOnContact && !IsPlayerHiddenFromMe && TouchingPlayer()) Player.Hurt();
            if (canShoot) TickShooting(sees, dt);

            UpdatePresentation();
            TickMovement(dt);
        }

        protected abstract void TickMovement(float dt);

        /// <summary>Wind up (telegraphed), then fire a taser bolt at where the player is heading.</summary>
        void TickShooting(bool sees, float dt)
        {
            bool canFire = sees && State == Awareness.Chase && !IsConfused && !IsPlayerHiddenFromMe && !Player.IsStunned;
            if (IsWindingUp)
            {
                if (!canFire)
                {
                    windupTimer = -1f;
                    return;
                }
                windupTimer -= dt;
                if (windupTimer > 0f) return;
                windupTimer = -1f;
                nextShotTime = Time.time + shootInterval;
                Vector2 target = (Vector2)Player.transform.position + Player.Velocity * aimLead;
                Vector2 dir = (target - EyePosition).normalized;
                TaserBolt.Spawn(EyePosition, dir, boltSpeed, boltRange, playerStunDuration, boltMask, boltSprite, boltMaterial, boltColor);
            }
            else if (canFire && Time.time >= nextShotTime)
            {
                windupTimer = shootWindup;
            }
        }

        public bool CanSeePlayer()
        {
            if (!Player || Player.IsDead || IsPlayerHiddenFromMe) return false;
            Vector2 eye = EyePosition;
            Vector2 target = Player.transform.position;
            Vector2 to = target - eye;
            if (to.magnitude > sightRange) return false;
            if (Vector2.Angle(LookDirection, to) > sightAngle * 0.5f) return false;
            if (Physics2D.Linecast(eye, target, obstacleMask)) return false;
            return !SmokeScreen.BlocksSight(eye, target);
        }

        bool TouchingPlayer()
        {
            return hurtbox && Player && !Player.IsDead && hurtbox.bounds.Intersects(Player.Body.bounds);
        }

        /// <summary>This cop saw the player: chase and radio every unit in range.</summary>
        public void Spot()
        {
            EnterChase();
            Broadcast();
            nextRelayTime = Time.time + RelayInterval;
            if (AlertsWholeLevel)
                GameManager.Instance?.ShowPopup("DRONE SPOTTED YOU  ·  ALL UNITS RESPONDING", new Color(1f, 0.3f, 0.2f), 1.8f);
        }

        void Broadcast()
        {
            foreach (var other in All)
            {
                if (other == this) continue;
                if (AlertsWholeLevel || Vector2.Distance(other.transform.position, transform.position) <= alertRadius)
                    other.ReceiveAlert(LastKnownPlayerPosition);
            }
        }

        public void ReceiveAlert(Vector2 playerPosition)
        {
            if (IsStunned || IsConfused || IsPlayerHiddenFromMe) return;
            LastKnownPlayerPosition = playerPosition;
            lastSeenTime = Time.time;
            EnterChase();
        }

        public void BreakLineOfSight(float confuseDuration)
        {
            ReturnToPatrol();
            seeTimer = 0f;
            confusedUntil = Time.time + confuseDuration;
        }

        public void Stun(float duration)
        {
            windupTimer = -1f;
            bool wasStunned = IsStunned;
            stunTimer = Mathf.Max(stunTimer, duration);
            seeTimer = 0f;
            if (!wasStunned) OnStunStarted();
        }

        void EnterChase()
        {
            if (State == Awareness.Chase) return;
            State = Awareness.Chase;
            OnChaseStarted();
        }

        protected void ReturnToPatrol()
        {
            State = Awareness.Patrol;
        }

        protected virtual void OnChaseStarted() { }
        protected virtual void OnStunStarted() { }
        protected virtual void OnStunEnded() { }
        protected abstract void ResetToSpawn();

        public static void ResetAll()
        {
            foreach (var cop in All)
            {
                cop.State = Awareness.Patrol;
                cop.seeTimer = 0f;
                cop.windupTimer = -1f;
                cop.nextShotTime = Time.time + 1f;
                cop.confusedUntil = 0f;
                if (cop.IsStunned)
                {
                    cop.stunTimer = 0f;
                    cop.OnStunEnded();
                }
                cop.ResetToSpawn();
            }
        }

        protected virtual void UpdatePresentation()
        {
            Vector2 dir = LookDirection;
            Color lookColor = State == Awareness.Chase ? chaseLightColor : patrolLightColor;

            if (sightLight)
            {
                // A 3D spot light aimed along the gameplay plane, slightly into the scene so it lights the rooftops.
                sightLight.transform.position = (Vector3)EyePosition + new Vector3(0f, 0f, -0.5f);
                sightLight.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, dir.y, 0.25f));
                sightLight.spotAngle = sightAngle;
                sightLight.innerSpotAngle = sightAngle * 0.6f;
                sightLight.range = sightRange * 1.3f;
                sightLight.color = lookColor;
                sightLight.intensity = IsStunned ? 0f : baseLightIntensity * (State == Awareness.Chase ? 1.4f : 1f);
            }

            if (sightCone)
            {
                sightCone.transform.position = EyePosition;
                sightCone.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                coneRenderer.enabled = !IsStunned;
                var c = lookColor;
                c.a = State == Awareness.Chase ? 0.28f : 0.16f;
                // Charging a taser: the cone strobes white so the shot is always readable.
                if (IsWindingUp && Mathf.Repeat(Time.time, 0.1f) < 0.05f) c = new Color(1f, 1f, 1f, 0.5f);
                Tint.Set(coneRenderer, c);
            }

            bool flash = IsStunned && (stunTimer > 0.75f || Mathf.Repeat(Time.time, 0.15f) < 0.075f);
            foreach (var r in bodyRenderers)
            {
                if (flash) Tint.Set(r, stunnedTint);
                else Tint.Clear(r);
            }

            if (alertIcon) alertIcon.SetActive(State == Awareness.Chase && !IsStunned);
        }

        /// <summary>A flat fan pointing along local +X, matching the sight test exactly.</summary>
        static Mesh BuildConeMesh(float angle, float range)
        {
            const int segments = 16;
            var verts = new Vector3[segments + 2];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(-angle * 0.5f, angle * 0.5f, i / (float)segments) * Mathf.Deg2Rad;
                verts[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * range;
            }
            for (int i = 0; i < segments; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 2;
                tris[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "SightCone", vertices = verts, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
