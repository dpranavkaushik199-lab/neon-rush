using UnityEngine;

namespace NeonRunner
{
    /// <summary>Optional collectible on a harder side route (the level's strawberries).</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DataShard : MonoBehaviour
    {
        [SerializeField] Transform visual;

        public static int Total { get; private set; }
        Vector3 basePos;

        // Statics survive play sessions when domain reload is off; start each session from zero.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Total = 0;

        // Counted for the life of the object, so collecting (disabling) doesn't lower the total.
        void Awake()
        {
            Total++;
            if (visual) basePos = visual.localPosition;
        }

        void OnDestroy() => Total--;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!TriggerUtil.GetPlayer(other)) return;
            GameManager.Instance?.CollectShard();
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (!visual) return;
            visual.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 2.5f) * 0.15f;
            visual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
        }
    }
}
