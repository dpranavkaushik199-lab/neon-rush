using UnityEngine;

namespace NeonRunner
{
    /// <summary>Launches the player along its local up and refills the dash.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class Spring : MonoBehaviour
    {
        [SerializeField] float launchSpeed = 21f;
        [SerializeField] Transform visual;
        [SerializeField] float cooldown = 0.2f;

        float readyAt;
        float squash;
        Vector3 visualScale;

        void Awake()
        {
            if (visual) visualScale = visual.localScale;
        }

        void OnTriggerEnter2D(Collider2D other) => TryLaunch(other);
        void OnTriggerStay2D(Collider2D other) => TryLaunch(other);

        void TryLaunch(Collider2D other)
        {
            if (Time.time < readyAt) return;
            var player = TriggerUtil.GetPlayer(other);
            if (!player) return;
            readyAt = Time.time + cooldown;
            player.Launch(transform.up * launchSpeed);
            squash = 1f;
        }

        void Update()
        {
            if (!visual) return;
            squash = Mathf.MoveTowards(squash, 0f, Time.deltaTime * 5f);
            float s = 1f - Mathf.Sin(squash * Mathf.PI) * 0.4f;
            visual.localScale = new Vector3(visualScale.x, visualScale.y * s, visualScale.z);
        }
    }
}
