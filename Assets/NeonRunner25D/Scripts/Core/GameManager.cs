using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonRunner
{
    /// <summary>Respawns, checkpoints, pause, hit-stop and the HUD.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] PlayerController player;
        [SerializeField] PlayerAbilities abilities;
        [SerializeField] Transform initialSpawn;
        [SerializeField] float respawnDelay = 0.6f;
        [SerializeField] float hitStopTimeScale = 0.02f;

        [Header("Level")]
        [SerializeField] string levelName;
        [SerializeField] int levelNumber = 1;
        [SerializeField] int levelCount = 1;

        public event Action Respawned;
        bool IsLastLevel => levelNumber >= levelCount;
        public PlayerController Player => player;
        public Vector2 RespawnPoint { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsFinished { get; private set; }

        float levelTime;
        int deaths;
        int shards;
        float hitStopUntil;
        string popupText;
        Color popupColor;
        float popupStart, popupUntil;
        string hintText;
        float hintStart, hintUntil;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;

            // Bodies never shove each other; contact damage is checked by bounds instead.
            int playerLayer = LayerMask.NameToLayer("Player");
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (playerLayer >= 0 && enemyLayer >= 0)
            {
                Physics2D.IgnoreLayerCollision(playerLayer, enemyLayer, true);
                Physics2D.IgnoreLayerCollision(enemyLayer, enemyLayer, true);
            }

            RespawnPoint = initialSpawn ? (Vector2)initialSpawn.position : (Vector2)player.transform.position;
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(levelName))
                ShowPopup(levelName.ToUpperInvariant(), Theme.Accent, 2.5f);
            player.Stunned += OnPlayerStunned;
            player.ShieldBroken += OnShieldBroken;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (player) player.Stunned -= OnPlayerStunned;
            if (player) player.ShieldBroken -= OnShieldBroken;
            Time.timeScale = 1f;
        }

        float stunFlashStart = -10f;

        void OnPlayerStunned()
        {
            stunFlashStart = Time.unscaledTime;
            ShowPopup("TASED", Theme.Danger, 0.7f);
            HitStop(0.06f);
            Camera.main?.GetComponent<CameraFollow2D>()?.AddTrauma(0.4f);
        }

        void OnShieldBroken() => ShowPopup($"SHIELD HIT  ·  {player.ShieldsLeft} LEFT", Theme.Success, 1f);

        void Update()
        {
            var input = player.Input;
            if (IsFinished)
            {
                if (Time.unscaledTime - finishTime > 1f && (input.JumpPressedThisFrame || input.PausePressed))
                {
                    // Levels are consecutive in Build Settings; after the last one, back to the menu (or start over).
                    int current = SceneManager.GetActiveScene().buildIndex;
                    if (IsLastLevel && LevelCatalog.MenuInBuild) LoadMainMenu();
                    else SceneManager.LoadScene(IsLastLevel ? current - (levelCount - 1) : current + 1);
                }
                return;
            }

            if (input.PausePressed && !settingsMenu.ConsumesInput)
            {
                if (settingsOpen) CloseSettings();
                else SetPaused(!IsPaused);
            }
            if (IsPaused) return;

            levelTime += Time.unscaledDeltaTime * (Time.timeScale > 0.5f ? 1f : 0f);
            Time.timeScale = Time.unscaledTime < hitStopUntil ? hitStopTimeScale : 1f;
        }

        public void SetPaused(bool paused)
        {
            if (!paused && settingsOpen) CloseSettings();
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
        }

        readonly SettingsMenu settingsMenu = new SettingsMenu();
        bool settingsOpen;

        void CloseSettings()
        {
            settingsMenu.Close();
            settingsOpen = false;
        }

        void Restart()
        {
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void LoadMainMenu()
        {
            AudioListener.pause = false;
            SceneManager.LoadScene(LevelCatalog.MenuScene);
        }

        public void HitStop(float duration)
        {
            if (duration <= 0f || IsPaused) return;
            hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + duration);
        }

        public void SetCheckpoint(Vector2 point, bool announce = true)
        {
            if ((point - RespawnPoint).sqrMagnitude < 0.01f) return;
            RespawnPoint = point;
            if (announce) ShowPopup("CHECKPOINT", Theme.Accent);
        }

        public void CollectShard()
        {
            shards++;
            ShowPopup($"DATA SHARD  {shards}/{DataShard.Total}", Theme.Success, 1.2f);
            HitStop(0.05f);
        }

        public void ShowPopup(string text, Color color, float duration = 1.1f)
        {
            popupText = text;
            popupColor = color;
            popupStart = Time.unscaledTime;
            popupUntil = Time.unscaledTime + duration;
        }

        /// <summary>Tutorial line in a quiet card at the bottom of the screen.</summary>
        public void ShowHint(string text, float duration = 3.5f)
        {
            hintText = text;
            hintStart = Time.unscaledTime;
            hintUntil = Time.unscaledTime + duration;
        }

        public void OnPlayerDied()
        {
            deaths++;
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnDelay);
            CopBase.ResetAll();
            player.RespawnAt(RespawnPoint);
            Respawned?.Invoke();
        }

        float finishTime;

        public void Finish()
        {
            if (IsFinished) return;
            IsFinished = true;
            finishTime = Time.unscaledTime;
            LevelCatalog.RecordClear(levelNumber, GameSettings.Difficulty, levelTime);
            Time.timeScale = 0.25f;
        }

        // ===================================================================== HUD
        // Malfunctioning military HUD (see NeonGUI): chamfered charcoal slabs, system readouts,
        // pink for system state, red for hazards and the player's resources, white for type.

        static float s => NeonGUI.S;

        static Texture2D edgeRamp;

        /// <summary>Soft colour bleeding in from all four screen edges.</summary>
        static void EdgeGlow(Color c, float w, float h, float depth)
        {
            if (!edgeRamp)
            {
                edgeRamp = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int i = 0; i < 64; i++)
                {
                    float a = 1f - i / 63f;
                    edgeRamp.SetPixel(i, 0, new Color(1f, 1f, 1f, a * a));
                }
                edgeRamp.Apply();
            }
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, depth, h), edgeRamp, new Rect(0, 0, 1, 1));
            GUI.DrawTextureWithTexCoords(new Rect(w - depth, 0, depth, h), edgeRamp, new Rect(1, 0, -1, 1));
            // Top and bottom: rotate the ramp a quarter turn.
            var pivot = new Vector2(w * 0.5f, h * 0.5f);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(90f, pivot);
            GUI.DrawTextureWithTexCoords(new Rect(pivot.x - h * 0.5f, pivot.y - w * 0.5f, depth, w), edgeRamp, new Rect(0, 0, 1, 1));
            GUI.DrawTextureWithTexCoords(new Rect(pivot.x + h * 0.5f - depth, pivot.y - w * 0.5f, depth, w), edgeRamp, new Rect(1, 0, -1, 1));
            GUI.matrix = matrix;
            GUI.color = prev;
        }

        static string Clock(float seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
        }

        /// <summary>Row of square cells: lit = available.</summary>
        static void Cells(float x, float y, int filled, int total, Color on)
        {
            float w = 16f * s, h = 8f * s, gap = 4f * s;
            for (int i = 0; i < total; i++)
                NeonGUI.Fill(new Rect(x + i * (w + gap), y, w, h), i < filled ? on : Theme.Faint);
        }

        static void Slab(Rect r, float cut)
        {
            NeonGUI.Chamfer(r, Theme.Panel, cut);
            NeonGUI.ChamferOutline(r, Theme.Faint, cut, Mathf.Max(1f, s));
        }

        /// <summary>Small labelled counter in the top-right cluster.</summary>
        static void Readout(Rect r, string label, string value, Color valueColor)
        {
            Slab(r, 8f * s);
            NeonGUI.Mono(new Rect(r.x + 12f * s, r.y, r.width, r.height), label, 11, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            NeonGUI.Mono(new Rect(r.x, r.y, r.width - 12f * s, r.height), value, 20, valueColor, TextAnchor.MiddleRight, FontStyle.Bold);
        }

        /// <summary>Ability chip: key block, name, and a segmented cooldown bar that refills.</summary>
        static void Chip(Rect r, string key, string name, bool ready, float cooldownLeft, float cooldown, Color accent)
        {
            Slab(r, 10f * s);
            var keyRect = new Rect(r.x + 8f * s, r.y + 8f * s, 32f * s, r.height - 16f * s);
            NeonGUI.Fill(keyRect, ready ? accent : Theme.Faint);
            NeonGUI.Mono(keyRect, key, 14, ready ? Theme.Bg : Theme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            float x = keyRect.xMax + 10f * s, right = r.xMax - 12f * s;
            NeonGUI.Text(new Rect(x, r.y + 7f * s, right - x, 20f * s), name, 14, ready ? Theme.Text : Theme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            NeonGUI.Mono(new Rect(x, r.y + 9f * s, right - x, 16f * s), ready ? "RDY" : $"{cooldownLeft:0.0}", 11, ready ? accent : Theme.Muted, TextAnchor.UpperRight, FontStyle.Bold);
            float frac = ready ? 1f : 1f - Mathf.Clamp01(cooldownLeft / Mathf.Max(0.01f, cooldown));
            const int cells = 8;
            float cw = (right - x) / cells;
            for (int i = 0; i < cells; i++)
                NeonGUI.Fill(new Rect(x + i * cw, r.yMax - 16f * s, cw - 2f * s, 5f * s), (i + 1) / (float)cells <= frac + 0.001f ? (ready ? accent : Theme.Muted) : Theme.Faint);
        }

        void OnGUI()
        {
            NeonGUI.Begin();
            float w = Screen.width, h = Screen.height, m = 28f * s;

            // ---- Screen-edge alarms: red pulse that strengthens and quickens as a chaser closes in, pink crackle while tased
            if (GameSettings.ProximityWarning && !IsPaused && !player.IsDead)
            {
                float near = Threat.Closeness(player.transform.position);
                if (near > 0f)
                {
                    float pulse = Mathf.Sin(Time.unscaledTime * Mathf.Lerp(5f, 14f, near));
                    EdgeGlow(new Color(1f, 0.08f, 0.12f, (0.15f + 0.4f * near) * (0.75f + 0.25f * pulse)), w, h, Mathf.Lerp(120f, 260f, near) * s);
                }
            }
            float stunAge = Time.unscaledTime - stunFlashStart;
            if (stunAge < 0.9f)
            {
                EdgeGlow(new Color(1f, 0.6f, 0.85f, (1f - stunAge / 0.9f) * (Mathf.Repeat(Time.unscaledTime, 0.08f) < 0.04f ? 0.9f : 0.5f)), w, h, 240f * s);
                NeonGUI.DataMosh(new Rect(0, 0, w, h), 3f, 0.6f);
            }

            // ---- Top left: operation card (level, name, clock)
            var card = new Rect(m, m, 340f * s, 104f * s);
            Slab(card, 14f * s);
            NeonGUI.Fill(new Rect(card.x, card.y + 14f * s, 5f * s, card.height - 14f * s), Theme.Accent2);
            NeonGUI.Hazard(new Rect(card.xMax - 64f * s, card.y + 10f * s, 50f * s, 8f * s), NeonGUI.WithAlpha(Theme.Accent2, 0.7f), 3f, 0.4f);
            NeonGUI.Mono(new Rect(card.x + 20f * s, card.y + 10f * s, 260f * s, 18f * s),
                $"OP {levelNumber:00}/{levelCount:00} // {GameSettings.Difficulty.ToString().ToUpperInvariant()}", 11, Theme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            NeonGUI.GlitchText(new Rect(card.x + 20f * s, card.y + 28f * s, 300f * s, 30f * s), string.IsNullOrEmpty(levelName) ? "" : levelName.ToUpperInvariant(), 22, Theme.Text, TextAnchor.UpperLeft, 1f, 1f, intensity: 0.5f);
            NeonGUI.Mono(new Rect(card.x + 20f * s, card.y + 62f * s, 200f * s, 32f * s), Clock(levelTime), 26, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            NeonGUI.Mono(new Rect(card.x, card.y + 74f * s, card.width - 14f * s, 16f * s), "T-ELAPSED", 10, Theme.Muted, TextAnchor.UpperRight);

            // ---- Top right: deaths, shards, shield
            float chipW = 132f * s, chipH = 42f * s;
            var deathChip = new Rect(w - m - chipW, m, chipW, chipH);
            Readout(deathChip, "KIA", deaths.ToString("00"), deaths > 0 ? Theme.Danger : Theme.Text);
            float chipX = deathChip.x;
            if (DataShard.Total > 0)
            {
                chipX -= chipW + 8f * s;
                Readout(new Rect(chipX, m, chipW, chipH), "SHARDS", $"{shards}/{DataShard.Total}", shards == DataShard.Total ? Theme.Accent2 : Theme.Text);
            }
            if (GameSettings.ExtraHits > 0)
            {
                chipX -= chipW + 8f * s;
                var shieldChip = new Rect(chipX, m, chipW, chipH);
                Slab(shieldChip, 8f * s);
                NeonGUI.Mono(new Rect(shieldChip.x + 12f * s, shieldChip.y, chipW, chipH), "SHIELD", 11, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                Cells(shieldChip.xMax - 12f * s - GameSettings.ExtraHits * 20f * s + 4f * s, shieldChip.center.y - 4f * s, player.ShieldsLeft, GameSettings.ExtraHits, Theme.Accent2);
            }
            NeonGUI.Mono(new Rect(w - m - 400f * s, m + chipH + 6f * s, 400f * s, 16f * s), $"UPLINK {NeonGUI.Hex(2f)} // SIG {(CopBase.AnyChasing ? "COMPROMISED" : "NOMINAL")}", 10,
                CopBase.AnyChasing ? Theme.Danger : Theme.Muted, TextAnchor.UpperRight);

            // ---- Top centre: wanted
            if (CopBase.AnyChasing)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f);
                var banner = new Rect(w * 0.5f - 170f * s, m, 340f * s, 44f * s);
                NeonGUI.Fill(banner, NeonGUI.WithAlpha(Theme.Danger, 0.14f + 0.14f * pulse));
                NeonGUI.Hazard(new Rect(banner.x, banner.y, 48f * s, banner.height), Theme.Danger, 6f, 1.2f);
                NeonGUI.Hazard(new Rect(banner.xMax - 48f * s, banner.y, 48f * s, banner.height), Theme.Danger, 6f, -1.2f);
                NeonGUI.ChamferOutline(banner, Theme.Danger, 0f, 2f * s);
                NeonGUI.GlitchText(banner, "WANTED", 24, Theme.Danger, TextAnchor.MiddleCenter, 5f, 2f, intensity: 2f);
                NeonGUI.Mono(new Rect(banner.x, banner.yMax + 2f * s, banner.width, 16f * s), "!! PURSUIT ACTIVE !!", 10, NeonGUI.WithAlpha(Theme.Danger, 0.5f + 0.5f * pulse), TextAnchor.UpperCenter, FontStyle.Bold);
            }

            // ---- Top centre (below): timed breaker countdown
            float power = 0f;
            foreach (var sw in PowerSwitch.All) power = Mathf.Max(power, sw.TimeLeft);
            if (power > 0f)
            {
                var pill = new Rect(w * 0.5f - 110f * s, m + 70f * s, 220f * s, 38f * s);
                bool low = power < 3f && Mathf.Repeat(Time.unscaledTime, 0.3f) < 0.15f;
                var c = low ? Theme.Danger : Theme.Accent2;
                Slab(pill, 8f * s);
                NeonGUI.Fill(new Rect(pill.x, pill.y, 5f * s, pill.height), c);
                NeonGUI.Mono(new Rect(pill.x + 16f * s, pill.y, pill.width, pill.height), "PWR", 11, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                NeonGUI.Mono(new Rect(pill.x, pill.y, pill.width - 14f * s, pill.height), $"{power:00.0}s", 20, c, TextAnchor.MiddleRight, FontStyle.Bold);
            }

            // ---- Bottom left: movement meters and abilities
            var meters = new Rect(m, h - m - 98f * s, 200f * s, 98f * s);
            Slab(meters, 10f * s);
            NeonGUI.Mono(new Rect(meters.x + 14f * s, meters.y + 12f * s, 60f * s, 18f * s), "DASH", 11, Theme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            Cells(meters.x + 70f * s, meters.y + 16f * s, player.DashesLeft, player.Config.maxDashes, Theme.Accent);
            NeonGUI.Mono(new Rect(meters.x + 14f * s, meters.y + 40f * s, 60f * s, 18f * s), "GRIP", 11, Theme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            Cells(meters.x + 70f * s, meters.y + 44f * s, player.ClimbJumpsLeft, player.Config.climbJumps, Theme.Accent2);
            // Flow: momentum bar, flashes when full (top speed, walls give way).
            NeonGUI.Mono(new Rect(meters.x + 14f * s, meters.y + 68f * s, 60f * s, 18f * s), "FLOW", 11, Theme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            var flowBar = new Rect(meters.x + 70f * s, meters.y + 72f * s, meters.width - 86f * s, 8f * s);
            NeonGUI.Fill(flowBar, Theme.Faint);
            bool maxed = player.Flow >= 0.999f;
            var flowColor = maxed && Mathf.Repeat(Time.unscaledTime, 0.2f) < 0.1f ? Theme.Text : Theme.Accent;
            NeonGUI.Fill(new Rect(flowBar.x, flowBar.y, flowBar.width * player.Flow, flowBar.height), flowColor);
            if (abilities)
            {
                var taser = new Rect(meters.xMax + 8f * s, meters.yMax - 70f * s, 156f * s, 70f * s);
                Chip(taser, player.Input.KeyLabel("Taser"), "TASER", abilities.TaserReady, abilities.TaserCooldownLeft, player.Config.taserCooldown, Theme.Accent);
                var smoke = new Rect(taser.xMax + 8f * s, taser.y, 156f * s, 70f * s);
                Chip(smoke, player.Input.KeyLabel("Smoke"), "SMOKE", abilities.SmokeReady, abilities.SmokeCooldownLeft, player.Config.smokeCooldown, Theme.Accent2);
            }

            // ---- Popup: a banner that tears in under the top bar
            if (Time.unscaledTime < popupUntil && !string.IsNullOrEmpty(popupText))
            {
                float age = Time.unscaledTime - popupStart, left = popupUntil - Time.unscaledTime;
                float a = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01(left / 0.25f);
                Vector2 size = NeonGUI.Style(26, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold).CalcSize(new GUIContent(popupText));
                float pw = size.x + 110f * s, ph = 56f * s;
                var pr = new Rect(w * 0.5f - pw * 0.5f, h * 0.16f, pw, ph);
                NeonGUI.Chamfer(pr, NeonGUI.WithAlpha(Theme.PanelSolid, 0.9f * a), 14f * s);
                NeonGUI.Fill(new Rect(pr.x, pr.y + 14f * s, 8f * s, pr.height - 14f * s), NeonGUI.WithAlpha(popupColor, a));
                NeonGUI.Hazard(new Rect(pr.xMax - 40f * s, pr.y + 8f * s, 28f * s, pr.height - 22f * s), NeonGUI.WithAlpha(popupColor, 0.5f * a), 4f, 0.6f);
                NeonGUI.Fill(new Rect(pr.x + 8f * s, pr.yMax - 2f * s, pr.width - 8f * s, 2f * s), NeonGUI.WithAlpha(popupColor, 0.6f * a));
                // Arrives broken: a wide split and data-mosh for the first few frames.
                bool arriving = age < 0.22f;
                NeonGUI.GlitchText(pr, popupText, 26, NeonGUI.WithAlpha(Theme.Text, a), TextAnchor.MiddleCenter, 9f, arriving ? 7f : 1.5f, intensity: arriving ? 3f : 0.6f);
                if (arriving) NeonGUI.DataMosh(new Rect(pr.x - 40f * s, pr.y - 10f * s, pr.width + 80f * s, pr.height + 20f * s), 9f, 0.8f);
            }

            // ---- Hint card at the bottom centre: a terminal line
            if (Time.unscaledTime < hintUntil && !string.IsNullOrEmpty(hintText))
            {
                float a = Mathf.Clamp01((Time.unscaledTime - hintStart) / 0.25f) * Mathf.Clamp01((hintUntil - Time.unscaledTime) / 0.4f);
                Vector2 size = NeonGUI.Style(17, Theme.Text, TextAnchor.MiddleCenter).CalcSize(new GUIContent(hintText));
                float hw = size.x + 90f * s, hh = 50f * s;
                var hr = new Rect(w * 0.5f - hw * 0.5f, h - m - hh - 100f * s, hw, hh);
                NeonGUI.Chamfer(hr, NeonGUI.WithAlpha(Theme.PanelSolid, 0.88f * a), 12f * s);
                NeonGUI.ChamferOutline(hr, NeonGUI.WithAlpha(Theme.Accent, 0.35f * a), 12f * s, Mathf.Max(1f, s));
                bool caret = Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;
                NeonGUI.Mono(new Rect(hr.x + 18f * s, hr.y, 40f * s, hr.height), caret ? ">_" : "> ", 17, NeonGUI.WithAlpha(Theme.Accent2, a), TextAnchor.MiddleLeft, FontStyle.Bold);
                NeonGUI.Text(new Rect(hr.x + 30f * s, hr.y, hr.width - 30f * s, hr.height), hintText, 17, NeonGUI.WithAlpha(Theme.Text, a), TextAnchor.MiddleCenter);
            }

            if (IsPaused) DrawPause(w, h);
            if (IsFinished) DrawFinish(w, h);
        }

        static void Dim(float w, float h, float alpha)
        {
            NeonGUI.Fill(new Rect(0, 0, w, h), NeonGUI.WithAlpha(Theme.Bg, alpha));
            NeonGUI.Scanlines(new Rect(0, 0, w, h), 0.3f * alpha);
        }

        void DrawPause(float w, float h)
        {
            Dim(w, h, 0.78f);
            if (settingsOpen)
            {
                float pw = Mathf.Min(1000f * s, w - 32f), ph = Mathf.Min(800f * s, h - 32f);
                if (settingsMenu.Draw(new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph))) settingsOpen = false;
                return;
            }

            var panel = new Rect(w * 0.5f - 300f * s, h * 0.5f - 270f * s, 600f * s, 540f * s);
            NeonGUI.Panel(panel, $"SYS.HALT // {NeonGUI.Hex(4f)}");
            NeonGUI.GlitchText(new Rect(panel.x + 48f * s, panel.y + 34f * s, panel.width, 56f * s), "PAUSED", 48, Theme.Text, TextAnchor.MiddleLeft, 4f, 2f);
            NeonGUI.Hazard(new Rect(panel.x + 48f * s, panel.y + 94f * s, 120f * s, 8f * s), Theme.Accent2, 4f, 0.5f);
            NeonGUI.Mono(new Rect(panel.x + 48f * s, panel.y + 110f * s, panel.width - 96f * s, 20f * s),
                $"OP {levelNumber:00}/{levelCount:00} · {levelName.ToUpperInvariant()} · {GameSettings.Difficulty.ToString().ToUpperInvariant()}", 13, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);

            float bw = panel.width - 96f * s, bh = 52f * s, gap = 10f * s;
            float x = panel.x + 48f * s, y = panel.y + 156f * s;
            if (NeonGUI.Button(new Rect(x, y, bw, bh), "RESUME", 18, true)) SetPaused(false);
            y += bh + gap;
            if (NeonGUI.Button(new Rect(x, y, bw, bh), "RESTART LEVEL", 17)) Restart();
            y += bh + gap;
            if (NeonGUI.Button(new Rect(x, y, bw, bh), "SETTINGS", 17)) settingsOpen = true;
            y += bh + gap;
            if (LevelCatalog.MenuInBuild && NeonGUI.Button(new Rect(x, y, bw, bh), "MAIN MENU", 17, false, Theme.Danger)) LoadMainMenu();

            NeonGUI.Text(new Rect(panel.x + 48f * s, panel.yMax - 70f * s, bw, 22f * s),
                "Keep moving to build <b>Flow</b>  ·  hold <b>Down</b> to slide, jump out of it to long-jump", 14, Theme.Accent2, TextAnchor.MiddleLeft);
            NeonGUI.Mono(new Rect(panel.x + 48f * s, panel.yMax - 44f * s, bw, 20f * s), "[ESC / START] RESUME  ·  [F9] QUALITY MODE", 12, Theme.Muted, TextAnchor.MiddleLeft);
        }

        void DrawFinish(float w, float h)
        {
            float a = Mathf.Clamp01((Time.unscaledTime - finishTime) / 0.4f);
            Dim(w, h, 0.7f * a);
            var panel = new Rect(w * 0.5f - 290f * s, h * 0.5f - 190f * s, 580f * s, 380f * s);
            NeonGUI.Panel(panel, $"OP COMPLETE // {NeonGUI.Hex(6f)}", Theme.Accent2, a);
            bool arriving = Time.unscaledTime - finishTime < 0.35f;
            NeonGUI.GlitchText(new Rect(panel.x + 48f * s, panel.y + 36f * s, panel.width, 60f * s), IsLastLevel ? "ESCAPED" : "LEVEL CLEAR", 50,
                NeonGUI.WithAlpha(Theme.Accent2, a), TextAnchor.MiddleLeft, 6f, arriving ? 8f : 2f, intensity: arriving ? 3f : 1f);
            if (arriving) NeonGUI.DataMosh(panel, 6f, 0.9f);
            NeonGUI.Mono(new Rect(panel.x + 48f * s, panel.y + 98f * s, panel.width, 20f * s), $"{levelName.ToUpperInvariant()} // {GameSettings.Difficulty.ToString().ToUpperInvariant()}",
                13, NeonGUI.WithAlpha(Theme.Muted, a), TextAnchor.MiddleLeft, FontStyle.Bold);

            (string, string)[] stats =
            {
                ("TIME", Clock(levelTime)),
                ("DEATHS", deaths.ToString("00")),
                ("DATA SHARDS", DataShard.Total > 0 ? $"{shards} / {DataShard.Total}" : "--"),
            };
            float y = panel.y + 140f * s;
            foreach (var (k, v) in stats)
            {
                NeonGUI.Fill(new Rect(panel.x + 48f * s, y + 38f * s, panel.width - 96f * s, Mathf.Max(1f, s)), NeonGUI.WithAlpha(Theme.Faint, Theme.Faint.a * a));
                NeonGUI.Mono(new Rect(panel.x + 48f * s, y, 220f * s, 36f * s), k, 14, NeonGUI.WithAlpha(Theme.Muted, a), TextAnchor.MiddleLeft, FontStyle.Bold);
                NeonGUI.Mono(new Rect(panel.x + 48f * s, y, panel.width - 96f * s, 36f * s), v, 24, NeonGUI.WithAlpha(Theme.Text, a), TextAnchor.MiddleRight, FontStyle.Bold);
                y += 44f * s;
            }
            string next = IsLastLevel ? (LevelCatalog.MenuInBuild ? "PRESS JUMP // RETURN TO MENU" : "PRESS JUMP // REPLAY FROM OP 01") : "PRESS JUMP // CONTINUE";
            if (Mathf.Repeat(Time.unscaledTime, 1f) < 0.7f)
                NeonGUI.Mono(new Rect(panel.x + 48f * s, panel.yMax - 54f * s, panel.width - 96f * s, 26f * s), next, 15, NeonGUI.WithAlpha(Theme.Accent, a), TextAnchor.MiddleLeft, FontStyle.Bold);
        }
    }
}
