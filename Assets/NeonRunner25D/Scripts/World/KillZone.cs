using UnityEngine;

namespace NeonRunner
{
    static class TriggerUtil
    {
        public static PlayerController GetPlayer(Collider2D other)
        {
            var player = other.GetComponentInParent<PlayerController>();
            return player && !player.IsDead ? player : null;
        }
    }

    /// <summary>Instant death volume (pits, lasers, spikes).</summary>
    [RequireComponent(typeof(Collider2D))]
    public class KillZone : MonoBehaviour
    {
        void OnTriggerEnter2D(Collider2D other) => TriggerUtil.GetPlayer(other)?.Kill();
        void OnTriggerStay2D(Collider2D other) => TriggerUtil.GetPlayer(other)?.Kill();
    }
}
