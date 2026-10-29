using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonRunner
{
    /// <summary>
    /// The "Deadline": a lethal laser grid that sweeps right once the player
    /// passes the activation line, rubber-banding so it never falls too far back.
    /// The transform's x is the wall's leading edge.
    /// </summary>
    public class SecurityWall : MonoBehaviour
    {
        [SerializeField] float activationX;
        [SerializeField] float speed = 4.5f;
        [SerializeField] float catchUpSpeed = 9f;
        [SerializeField] float catchUpDistance = 22f;
        [SerializeField] float respawnGap = 12f;
        [SerializeField] float startDelay = 0.6f;
        [SerializeField] Light glow;
        [SerializeField] Transform[] scanLines = new Transform[0];
        [SerializeField] float scanHeight = 30f;

        public static readonly List<SecurityWall> All = new List<SecurityWall>();

        PlayerController player;
        float startX;
        bool active;
        float delay;
        float baseGlow;

        /// <summary>Sweeping toward the player (not waiting behind its activation line).</summary>
        public bool IsActive => active;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            startX = transform.position.x;
            player = GameManager.Instance.Player;
            GameManager.Instance.Respawned += OnRespawned;
            if (glow) baseGlow = glow.intensity;
        }

        void OnDestroy()
        {
            if (GameManager.Instance) GameManager.Instance.Respawned -= OnRespawned;
        }

        public void Configure(float activateAtX) => activationX = activateAtX;

        void Update()
        {
            AnimateLasers();
            if (player.IsDead || GameManager.Instance.IsFinished) return;

            if (!active)
            {
                if (player.transform.position.x < activationX) return;
                active = true;
                delay = startDelay;
                GameManager.Instance.ShowPopup("SECURITY WALL ONLINE - RUN", new Color(1f, 0.2f, 0.3f), 1.6f);
            }

            if (delay > 0f)
            {
                delay -= Time.deltaTime;
                return;
            }

            float gap = player.transform.position.x - transform.position.x;
            float s = (gap > catchUpDistance ? catchUpSpeed : speed) * GameSettings.ChaserSpeedScale;
            transform.position += Vector3.right * s * Time.deltaTime;

            if (player.Body.bounds.min.x <= transform.position.x) player.Kill();
        }

        void OnRespawned()
        {
            float respawnX = GameManager.Instance.RespawnPoint.x;
            if (respawnX < activationX)
            {
                active = false;
                SetX(startX);
                return;
            }
            SetX(Mathf.Max(startX, respawnX - respawnGap));
            delay = 1f;
        }

        void SetX(float x)
        {
            var p = transform.position;
            p.x = x;
            transform.position = p;
        }

        void AnimateLasers()
        {
            for (int i = 0; i < scanLines.Length; i++)
            {
                var line = scanLines[i];
                var lp = line.localPosition;
                lp.y = Mathf.Repeat(Time.time * (3f + i) + i * 7f, scanHeight) - scanHeight * 0.5f;
                line.localPosition = lp;
            }
            if (glow) glow.intensity = baseGlow * (0.85f + Mathf.PerlinNoise(Time.time * 8f, 0f) * 0.3f);
        }
    }
}
