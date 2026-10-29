using System.Collections.Generic;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Breaker switch for puzzles. Flips on when the player touches it or hits it with
    /// the taser (so it can be thrown from across a gap). Latching switches stay on;
    /// timed ones count down, blinking faster as they run out, then flip back off.
    /// Gates and laser curtains read <see cref="IsOn"/>.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class PowerSwitch : MonoBehaviour, IStunnable
    {
        public static readonly List<PowerSwitch> All = new List<PowerSwitch>();

        [Tooltip("0 = stays on once thrown.")]
        [SerializeField] float activeDuration;
        [SerializeField] Renderer indicator;
        [SerializeField] Light lamp;
        [SerializeField] Color offColor = new Color(1f, 0.2f, 0.2f);
        [SerializeField] Color onColor = new Color(0.35f, 1f, 0.7f);

        public bool IsOn { get; private set; }
        public float TimeLeft => IsOn && activeDuration > 0f ? Mathf.Max(0f, offAt - Time.time) : 0f;
        bool IStunnable.IsStunned => IsOn;

        Collider2D col;
        PlayerController player;
        float offAt;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake() => col = GetComponent<Collider2D>();
        void Start() => player = GameManager.Instance ? GameManager.Instance.Player : FindAnyObjectByType<PlayerController>();

        public void Stun(float duration) => Activate();

        void Activate()
        {
            bool was = IsOn;
            IsOn = true;
            offAt = Time.time + activeDuration;
            if (was) return;

            GameManager.Instance?.HitStop(0.04f);
            GameManager.Instance?.ShowPopup(activeDuration > 0f ? "POWER ON  ·  RUN" : "BREAKER THROWN", onColor, 1.1f);
        }

        void Update()
        {
            if (!IsOn && Touching()) Activate();
            if (IsOn && activeDuration > 0f && Time.time >= offAt)
            {
                IsOn = false;
                GameManager.Instance?.ShowPopup("POWER LOST", offColor, 0.8f);
            }

            Color c = IsOn ? onColor : offColor;
            // A timed switch blinks faster and faster in its last three seconds.
            float left = TimeLeft;
            if (IsOn && activeDuration > 0f && left < 3f)
            {
                float rate = Mathf.Lerp(0.08f, 0.35f, left / 3f);
                if (Mathf.Repeat(Time.time, rate * 2f) < rate) c = offColor;
            }
            if (indicator) Tint.Set(indicator, c * 3f);
            if (lamp) lamp.color = c;
        }

        bool Touching()
        {
            if (!player || player.IsDead) return false;
            var b = col.bounds;
            b.Expand(0.1f);
            return b.Intersects(player.Body.bounds);
        }
    }
}
