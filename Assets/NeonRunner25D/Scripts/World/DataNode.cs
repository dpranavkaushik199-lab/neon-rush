using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonRunner
{
    /// <summary>Floating crystal that refills the dash, then recharges.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DataNode : MonoBehaviour
    {
        [SerializeField] float rechargeTime = 2.5f;
        [SerializeField] Transform visual;
        [SerializeField] Light glow;

        float readyAt;
        Vector3 basePosition;
        float baseIntensity;

        bool IsReady => Time.time >= readyAt;

        void Awake()
        {
            if (visual) basePosition = visual.localPosition;
            if (glow) baseIntensity = glow.intensity;
        }

        void OnTriggerEnter2D(Collider2D other) => TryConsume(other);
        void OnTriggerStay2D(Collider2D other) => TryConsume(other);

        void TryConsume(Collider2D other)
        {
            if (!IsReady) return;
            var player = TriggerUtil.GetPlayer(other);
            if (!player || !player.NeedsDash) return;
            player.RefillDash();
            readyAt = Time.time + rechargeTime;
            GameManager.Instance?.HitStop(0.04f);
        }

        void Update()
        {
            if (visual)
            {
                visual.gameObject.SetActive(IsReady || Mathf.Repeat(Time.time, 0.3f) < 0.05f);
                visual.localPosition = basePosition + Vector3.up * Mathf.Sin(Time.time * 3f) * 0.15f;
                visual.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
            }
            if (glow) glow.intensity = IsReady ? baseIntensity : baseIntensity * 0.15f;
        }
    }
}
