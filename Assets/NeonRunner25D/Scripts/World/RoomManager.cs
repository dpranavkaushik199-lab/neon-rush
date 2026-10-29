using UnityEngine;

namespace NeonRunner
{
    /// <summary>Tracks which room the player is in and moves the respawn point on entry (the camera follows freely).</summary>
    public class RoomManager : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] CameraFollow2D cameraFollow;
        [Tooltip("The player must be this far into a new room before it takes over (avoids flicker on edges).")]
        [SerializeField] float hysteresis = 0.4f;

        public Room Current { get; private set; }

        void Start() => Refresh(true);

        void LateUpdate()
        {
            if (!player.IsDead) Refresh(false);
        }

        void Refresh(bool instant)
        {
            Vector2 p = player.transform.position;
            if (Current && Current.Contains(p, hysteresis)) return;

            Room next = null;
            foreach (var room in Room.All)
                if (room.Contains(p)) { next = room; break; }
            if (!next || next == Current) return;

            Current = next;
            cameraFollow.SetBounds(next.Bounds, instant);
            GameManager.Instance?.SetCheckpoint(next.Spawn, announce: false);
        }
    }
}
