using System.Collections.Generic;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// A Celeste-style screen: the camera stays inside its bounds, and entering it
    /// moves the respawn point to its spawn so deaths restart only this room.
    /// </summary>
    public class Room : MonoBehaviour
    {
        public static readonly List<Room> All = new List<Room>();

        [SerializeField] Rect bounds;
        [SerializeField] Transform spawnPoint;
        [SerializeField] string roomName;

        public Rect Bounds => bounds;
        public Vector2 Spawn => spawnPoint ? (Vector2)spawnPoint.position : bounds.center;
        public string RoomName => roomName;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector2 point, float margin = 0f)
        {
            return point.x >= bounds.xMin - margin && point.x <= bounds.xMax + margin &&
                   point.y >= bounds.yMin - margin && point.y <= bounds.yMax + margin;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
