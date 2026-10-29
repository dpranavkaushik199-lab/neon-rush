using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// 2.5D follow cam: a perspective camera at a fixed depth tracks the player
    /// continuously through the whole level (the city parallaxes past as you run,
    /// no room-to-room cuts), with velocity look-ahead, trauma shake, and a wider
    /// field of view during a Sling-Boost. The view only stops at the level's outer
    /// edges and never drops below its lowest floor.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : MonoBehaviour
    {
        [SerializeField] PlayerController target;
        [SerializeField] Vector2 offset = new Vector2(0f, 1.6f);
        [SerializeField] float smoothTime = 0.12f;
        [Tooltip("Vertical smoothing; a little slower than horizontal so jumps don't bob the city.")]
        [SerializeField] float verticalSmoothTime = 0.2f;
        [SerializeField] float lookAheadPerSpeed = 0.32f;
        [SerializeField] float maxLookAhead = 7f;
        [SerializeField] float baseFieldOfView = 40f;
        [SerializeField] float boostFieldOfView = 52f;
        [SerializeField] float zoomSpeed = 4f;
        [SerializeField] float maxShake = 0.6f;

        Rect levelBounds;
        bool hasBounds;
        Camera cam;
        Vector3 followPosition;
        Vector3 velocity;
        float lookAhead;
        float trauma;

        /// <summary>Resting field of view: the scene's value, shifted by the player's FOV setting.</summary>
        float BaseFov => baseFieldOfView + GameSettings.FieldOfView - GameSettings.DefaultFov;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.fieldOfView = BaseFov;
        }

        void Start()
        {
            // The whole level is one continuous space: its bounds are the union of all rooms.
            foreach (var room in Room.All)
            {
                levelBounds = hasBounds ? Union(levelBounds, room.Bounds) : room.Bounds;
                hasBounds = true;
            }
            Snap();
            // Cut straight to the respawn point: a long whip-pan would smear under motion blur.
            if (GameManager.Instance) GameManager.Instance.Respawned += Snap;
        }

        void OnDestroy()
        {
            if (GameManager.Instance) GameManager.Instance.Respawned -= Snap;
        }

        static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        public void Snap()
        {
            Vector2 p = Clamp((Vector2)target.transform.position + offset);
            followPosition = new Vector3(p.x, p.y, transform.position.z);
            velocity = Vector3.zero;
            transform.position = followPosition;
        }

        /// <summary>Kept for room bookkeeping; the camera no longer locks to rooms. Instant snaps (spawn) still apply.</summary>
        public void SetBounds(Rect roomBounds, bool instant)
        {
            if (instant) Snap();
        }

        Vector2 Clamp(Vector2 goal)
        {
            if (!hasBounds) return goal;
            // Clamp with the resting field of view so the boost zoom doesn't fight the bounds.
            float distance = Mathf.Abs(transform.position.z);
            float halfH = distance * Mathf.Tan(BaseFov * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * (cam ? cam.aspect : 16f / 9f);
            goal.x = levelBounds.width <= halfW * 2f ? levelBounds.center.x : Mathf.Clamp(goal.x, levelBounds.xMin + halfW, levelBounds.xMax - halfW);
            goal.y = Mathf.Max(goal.y, levelBounds.yMin + halfH);
            return goal;
        }

        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.unscaledDeltaTime;

            Vector2 v = target.IsDead ? Vector2.zero : target.Velocity;
            float desiredLook = Mathf.Clamp(v.x * lookAheadPerSpeed, -maxLookAhead, maxLookAhead);
            lookAhead = Mathf.Lerp(lookAhead, desiredLook, 1f - Mathf.Exp(-3f * dt));

            Vector3 goal = Clamp((Vector2)target.transform.position + offset + new Vector2(lookAhead, 0f));
            followPosition.x = Mathf.SmoothDamp(followPosition.x, goal.x, ref velocity.x, smoothTime, Mathf.Infinity, dt);
            followPosition.y = Mathf.SmoothDamp(followPosition.y, goal.y, ref velocity.y, verticalSmoothTime, Mathf.Infinity, dt);

            trauma = Mathf.Max(0f, trauma - dt * 1.5f);
            float s = trauma * trauma * maxShake * GameSettings.ScreenShake;
            float t = Time.unscaledTime * 25f;
            var shake = new Vector3((Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * s, (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * s, 0f);
            transform.position = followPosition + shake;

            bool fast = target.IsBoosting || target.Flow >= 0.95f || Mathf.Abs(v.x) > target.Config.flowMaxSpeed * 1.2f;
            float fov = fast ? BaseFov + boostFieldOfView - baseFieldOfView : BaseFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-zoomSpeed * dt));
        }

        /// <summary>Half-size of the view at the gameplay plane (z = 0).</summary>
        public static Vector2 HalfExtentsAtPlane(Camera camera, float planeZ = 0f)
        {
            if (camera.orthographic) return new Vector2(camera.orthographicSize * camera.aspect, camera.orthographicSize);
            float distance = Mathf.Abs(planeZ - camera.transform.position.z);
            float halfHeight = distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(halfHeight * camera.aspect, halfHeight);
        }
    }
}
