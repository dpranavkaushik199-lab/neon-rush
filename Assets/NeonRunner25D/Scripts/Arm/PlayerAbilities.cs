using System;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>The arm's tactical tools: Taser Shot and Smoke Screen.</summary>
    public class PlayerAbilities : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] Transform handPoint;
        [SerializeField] LayerMask taserHitMask;
        [SerializeField] Sprite boltSprite;
        [SerializeField] Sprite smokeSprite;
        [SerializeField] Material unlitMaterial;
        [SerializeField] Color boltColor = new Color(0.3f, 1f, 1f);
        [SerializeField] Color smokeColor = new Color(0.45f, 0.3f, 0.6f, 0.85f);

        float taserReadyAt;
        float smokeReadyAt;
        Camera cam;

        MovementConfig Config => player.Config;
        public event Action<Vector2> TaserFired;

        public bool TaserReady =>Time.time >= taserReadyAt;
        public bool SmokeReady => Time.time >= smokeReadyAt;
        public float TaserCooldownLeft => Mathf.Max(0f, taserReadyAt - Time.time);
        public float SmokeCooldownLeft => Mathf.Max(0f, smokeReadyAt - Time.time);

        void Awake() => cam = Camera.main;

        void Update()
        {
            if (player.IsDead || GameManager.Instance && GameManager.Instance.IsPaused) return;
            var input = player.Input;

            if (input.TaserPressed && TaserReady) FireTaser(input.TaserAimedWithMouse);
            if (input.SmokePressed && SmokeReady) DeploySmoke();
        }

        void FireTaser(bool aimWithMouse)
        {
            taserReadyAt = Time.time + Config.taserCooldown;
            Vector2 origin = handPoint ? (Vector2)handPoint.position : (Vector2)transform.position;
            Vector2 dir = new Vector2(player.Facing, 0f);
            if (aimWithMouse && cam)
            {
                Vector2 toMouse = PlayerInputReader.MouseWorldPosition(cam) - origin;
                if (toMouse.sqrMagnitude > 0.01f) dir = toMouse.normalized;
            }
            else if (player.Input.Move.y != 0f)
            {
                dir = new Vector2(player.Input.Move.x != 0f ? Mathf.Sign(player.Input.Move.x) : 0f, Mathf.Sign(player.Input.Move.y)).normalized;
            }

            TaserBolt.Spawn(origin, dir, Config.taserSpeed, Config.taserRange, Config.taserStunDuration,
                taserHitMask, boltSprite, unlitMaterial, boltColor);
            TaserFired?.Invoke(dir);
        }

        void DeploySmoke()
        {
            smokeReadyAt = Time.time + Config.smokeCooldown;
            SmokeScreen.Spawn(player.transform.position, Config.smokeRadius, Config.smokeDuration,
                smokeSprite, unlitMaterial, smokeColor);
        }
    }
}
