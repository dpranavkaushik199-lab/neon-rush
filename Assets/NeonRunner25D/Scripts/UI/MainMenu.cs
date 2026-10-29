using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NeonRunner
{
    /// <summary>
    /// Title screen: Play opens the mode select (difficulty preset, Tutorial and Chaos Mode
    /// with best times on that difficulty, and a parkour cheat sheet), plus Settings and Quit.
    /// Styled as a malfunctioning military terminal (see <see cref="NeonGUI"/>).
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        enum Page { Title, Levels, Settings }

        Page page, drawnPage;
        readonly SettingsMenu settings = new SettingsMenu();
        float s => NeonGUI.S;

        void Awake()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void Update()
        {
            bool back = Keyboard.current is { escapeKey: { wasPressedThisFrame: true } } ||
                        Gamepad.current is { buttonEast: { wasPressedThisFrame: true } };
            if (back && !settings.ConsumesInput) Back();
        }

        void Back()
        {
            if (page == Page.Settings) settings.Close();
            page = Page.Title;
        }

        void OnGUI()
        {
            NeonGUI.Begin();
            if (page != drawnPage)
            {
                // Controller focus lands on the first mode card, past the difficulty row.
                NeonGUI.ResetFocus(page == Page.Levels ? Enum.GetValues(typeof(Difficulty)).Length : 0);
                drawnPage = page;
            }
            float w = Screen.width, h = Screen.height;
            DrawBackdrop(w, h);
            switch (page)
            {
                case Page.Title: DrawTitle(w, h); break;
                case Page.Levels: DrawLevels(w, h); break;
                case Page.Settings:
                    if (settings.Draw(CenteredPanel(w, h, 1000f, 800f))) page = Page.Title;
                    break;
            }
            DrawFrame(w, h);
        }

        Rect CenteredPanel(float w, float h, float pw, float ph)
        {
            pw = Mathf.Min(pw * s, w - 32f);
            ph = Mathf.Min(ph * s, h - 32f);
            return new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        }

        // ===================================================================== Backdrop and frame

        void DrawBackdrop(float w, float h)
        {
            if (Event.current.type != EventType.Repaint) return;
            // The 3D neural network (NeuralNetworkBackdrop) is the background; without it, fall back to solid charcoal.
            if (!NeonGUI.Glass) NeonGUI.Fill(new Rect(0, 0, w, h), Theme.Bg);
            // Shade the left column so the title and menu stay readable over the brightest flashes.
            else if (page == Page.Title) NeonGUI.FadeRight(new Rect(0, 0, w * 0.62f, h), NeonGUI.WithAlpha(Theme.Bg, 0.82f));

            // Survey grid of small crosses.
            float step = 80f * s, arm = 4f * s, t = Mathf.Max(1f, s);
            var cross = NeonGUI.WithAlpha(Theme.Accent, 0.07f);
            for (float x = step; x < w; x += step)
            for (float y = step; y < h; y += step)
            {
                NeonGUI.Fill(new Rect(x - arm, y - t * 0.5f, arm * 2f, t), cross);
                NeonGUI.Fill(new Rect(x - t * 0.5f, y - arm, t, arm * 2f), cross);
            }

            // Structural diagonals: hairlines and one red slash.
            for (int i = 0; i < 6; i++)
            {
                float x0 = w * (0.35f + i * 0.13f);
                NeonGUI.Line(new Vector2(x0, h), new Vector2(x0 + h * 0.58f, 0f), Mathf.Max(1f, s), NeonGUI.WithAlpha(Theme.Accent, i == 2 ? 0.16f : 0.05f));
            }
            // Kept below the frame's bottom readout line.
            NeonGUI.Line(new Vector2(-40f * s, h * 0.985f), new Vector2(w * 0.3f, h * 1.25f), 34f * s, NeonGUI.WithAlpha(Theme.Accent2, 0.85f));
            NeonGUI.Line(new Vector2(-40f * s, h * 0.955f), new Vector2(w * 0.3f, h * 1.22f), 3f * s, NeonGUI.WithAlpha(Theme.Accent2, 0.6f));
            NeonGUI.Hazard(new Rect(w * 0.6f, h - 18f * s, w * 0.4f, 18f * s), NeonGUI.WithAlpha(Theme.Accent2, 0.5f), 7f, 0.25f);

            // Watermark.
            NeonGUI.Mono(new Rect(0, h * 0.08f, w - 60f * s, 300f * s), "NR//25", 260, NeonGUI.WithAlpha(Theme.Accent, 0.035f), TextAnchor.UpperRight, FontStyle.Bold);

            NeonGUI.DataMosh(new Rect(0, 0, w, h), 1f, 0.12f);
            NeonGUI.Scanlines(new Rect(0, 0, w, h), 0.2f);
        }

        void DrawFrame(float w, float h)
        {
            float m = 22f * s;
            var frame = new Rect(m, m, w - m * 2f, h - m * 2f);
            NeonGUI.Brackets(frame, NeonGUI.WithAlpha(Theme.Accent, 0.7f), 34f * s, 2f * s);
            var muted = NeonGUI.WithAlpha(Theme.Muted, 0.9f);
            NeonGUI.Mono(new Rect(frame.x + 46f * s, frame.y + 6f * s, 700f * s, 16f * s), "NEON RUSH // TACTICAL EVASION INTERFACE", 11, muted, TextAnchor.UpperLeft, FontStyle.Bold);
            NeonGUI.Mono(new Rect(frame.xMax - 746f * s, frame.y + 6f * s, 700f * s, 16f * s),
                $"SYS {NeonGUI.Hex(1f)} // {DateTime.Now:HH:mm:ss}", 11, muted, TextAnchor.UpperRight, FontStyle.Bold);
            bool pad = Gamepad.current != null;
            NeonGUI.Mono(new Rect(frame.x + 46f * s, frame.yMax - 22f * s, 700f * s, 16f * s),
                $"INPUT: {(pad ? "CONTROLLER LINKED  ·  [A] SELECT  ·  [B] BACK" : "KEYBOARD + MOUSE  ·  [ESC] BACK")}", 11, pad ? Theme.Accent : muted, TextAnchor.UpperLeft, FontStyle.Bold);
            int bars = 3 + (NeonGUI.Glitch(2f, 0.3f) != 0f ? -2 : 1);
            NeonGUI.Mono(new Rect(frame.xMax - 746f * s, frame.yMax - 22f * s, 700f * s, 16f * s),
                "SIGNAL " + new string('|', bars) + new string('.', 5 - bars), 11, muted, TextAnchor.UpperRight, FontStyle.Bold);
        }

        // ===================================================================== Title

        void DrawTitle(float w, float h)
        {
            float x = w * 0.08f, top = h * 0.17f;
            NeonGUI.Mono(new Rect(x, top, 600f * s, 20f * s), "// EVASION PROTOCOL  v2.5", 14, Theme.Accent2, TextAnchor.UpperLeft, FontStyle.Bold);
            NeonGUI.GlitchText(new Rect(x - 6f * s, top + 18f * s, w, 150f * s), "NEON", 150, Theme.Text, TextAnchor.UpperLeft, 1f, 3f, titleFace: true, intensity: 1.4f);
            NeonGUI.GlitchText(new Rect(x - 6f * s, top + 150f * s, w, 150f * s), "RUSH", 150, Theme.Accent, TextAnchor.UpperLeft, 2f, 3f, titleFace: true, intensity: 1.4f);
            float bar = top + 318f * s;
            NeonGUI.Hazard(new Rect(x, bar, 260f * s, 14f * s), Theme.Accent2, 5f, 0.6f);
            NeonGUI.Fill(new Rect(x + 270f * s, bar + 6f * s, 220f * s, 2f * s), NeonGUI.WithAlpha(Theme.Accent2, 0.6f));
            NeonGUI.Mono(new Rect(x, bar + 22f * s, 600f * s, 22f * s), "OUTRUN THE CITY.", 18, Theme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);

            float y = h * 0.6f, iw = 480f * s, ih = 62f * s;
            if (MenuItem(new Rect(x, y, iw, ih), "01", "PLAY", "TUTORIAL · CHAOS MODE", Theme.Accent2)) page = Page.Levels;
            y += ih + 8f * s;
            if (MenuItem(new Rect(x, y, iw, ih), "02", "SETTINGS", "AUDIO · VIDEO · CONTROLS", Theme.Accent)) page = Page.Settings;
            y += ih + 8f * s;
            if (MenuItem(new Rect(x, y, iw, ih), "03", "QUIT", "TERMINATE SESSION", Theme.Danger))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }

            // Bottom-right, out from under the spectrum construct.
            float sw = Mathf.Min(460f * s, w * 0.28f), sh = 262f * s;
            DrawStatus(new Rect(w - 70f * s - sw, h - 66f * s - sh, sw, sh));
        }

        /// <summary>Numbered menu line: on hover an accent block slams in behind the index and the label tears.</summary>
        bool MenuItem(Rect r, string index, string label, string sub, Color accent)
        {
            bool focused = NeonGUI.NavFocus();
            bool hover = NeonGUI.Lit(r, focused);
            if (Event.current.type == EventType.Repaint)
            {
                var idx = new Rect(r.x, r.y, 62f * s, r.height);
                if (hover)
                {
                    NeonGUI.Chamfer(r, NeonGUI.WithAlpha(accent, 0.1f), 12f * s);
                    NeonGUI.Chamfer(idx, accent, 12f * s);
                    NeonGUI.Brackets(new Rect(r.x - 6f * s, r.y - 6f * s, r.width + 12f * s, r.height + 12f * s), accent, 10f * s, 2f * s);
                    NeonGUI.DataMosh(new Rect(r.xMax - 160f * s, r.y, 160f * s, r.height), r.y, 0.5f);
                }
                else NeonGUI.Fill(new Rect(r.x, r.yMax - Mathf.Max(1f, s), r.width, Mathf.Max(1f, s)), Theme.Faint);

                NeonGUI.Mono(idx, index, 18, hover ? Theme.Bg : Theme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
                var labelRect = new Rect(idx.xMax + (hover ? 22f : 14f) * s, r.y - 6f * s, r.width, r.height);
                if (hover) NeonGUI.GlitchText(labelRect, label, 34, Theme.Text, TextAnchor.MiddleLeft, r.y * 0.01f, 2f, intensity: 1.5f);
                else NeonGUI.Text(labelRect, label, 34, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                NeonGUI.Mono(new Rect(labelRect.x + 2f * s, r.yMax - 22f * s, r.width, 16f * s), sub, 11, hover ? accent : Theme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
                NeonGUI.Mono(new Rect(r.x, r.y, r.width - 14f * s, r.height), hover ? ">>" : "", 16, accent, TextAnchor.MiddleRight, FontStyle.Bold);
            }
            return NeonGUI.Pressed(r, focused);
        }

        /// <summary>Operator readout: the player's real setup, dressed as telemetry.</summary>
        void DrawStatus(Rect r)
        {
            NeonGUI.Panel(r, $"TELEMETRY // {NeonGUI.Hex(3f)}");
            NeonGUI.Text(new Rect(r.x + 32f * s, r.y + 30f * s, r.width, 30f * s), "OPERATOR STATUS", 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);

            int cleared = 0;
            foreach (var level in LevelCatalog.Levels)
                if (LevelCatalog.BestTime(level.number, GameSettings.Difficulty) >= 0f) cleared++;
            var d = GameSettings.Difficulty;
            (string, string, Color)[] rows =
            {
                ("DIFFICULTY", d.ToString().ToUpperInvariant(), d == Difficulty.Nightmare ? Theme.Danger : d == Difficulty.Casual ? Theme.Text : Theme.Accent),
                ("MODES CLEARED", $"{cleared:00} / {LevelCatalog.Levels.Length:00}", Theme.Text),
                ("MUSIC", $"{Mathf.RoundToInt(GameSettings.MusicVolume * 100f):000}%", Theme.Text),
                ("PROXIMITY ALERT", GameSettings.ProximityWarning ? "ARMED" : "OFF", GameSettings.ProximityWarning ? Theme.Accent : Theme.Muted),
            };
            float y = r.y + 80f * s, x = r.x + 32f * s, rw = r.width - 64f * s;
            foreach (var (k, v, c) in rows)
            {
                NeonGUI.Mono(new Rect(x, y, rw, 30f * s), k, 13, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                NeonGUI.Mono(new Rect(x, y, rw, 30f * s), v, 17, c, TextAnchor.MiddleRight, FontStyle.Bold);
                NeonGUI.Fill(new Rect(x, y + 32f * s, rw, Mathf.Max(1f, s)), Theme.Faint);
                y += 42f * s;
            }
        }

        // ===================================================================== Level select

        void DrawLevels(float w, float h)
        {
            var panel = CenteredPanel(w, h, 1320f, 950f);
            NeonGUI.Panel(panel, $"OPS.INDEX // {NeonGUI.Hex(5f)}");
            float x = panel.x + 48f * s, right = panel.xMax - 48f * s;
            NeonGUI.GlitchText(new Rect(x, panel.y + 24f * s, panel.width, 50f * s), "SELECT MODE", 40, Theme.Text, TextAnchor.MiddleLeft, 5f, 1.8f);
            NeonGUI.Hazard(new Rect(x, panel.y + 78f * s, 110f * s, 7f * s), Theme.Accent2, 3.5f, 0.5f);

            // Difficulty presets
            float y = panel.y + 104f * s;
            NeonGUI.Mono(new Rect(x, y, 150f * s, 40f * s), "// DIFFICULTY", 12, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            float dw = 170f * s, dx = x + 150f * s;
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var accent = d == Difficulty.Casual ? Theme.Text : d == Difficulty.Nightmare ? Theme.Danger : Theme.Accent;
                if (NeonGUI.Button(new Rect(dx, y, dw, 40f * s), d.ToString().ToUpperInvariant(), 15, GameSettings.Difficulty == d, accent))
                {
                    GameSettings.Difficulty = d;
                    GameSettings.Flush();
                }
                dx += dw + 8f * s;
            }
            NeonGUI.Mono(new Rect(dx + 12f * s, y, right - dx - 12f * s, 40f * s), GameSettings.Describe(GameSettings.Difficulty).ToUpperInvariant(), 12, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Mode cards side by side
            y += 72f * s;
            int columns = LevelCatalog.Levels.Length;
            float gap = 16f * s;
            float cw = (right - x - gap * (columns - 1)) / columns, ch = 200f * s;
            int shown = 0;
            foreach (var level in LevelCatalog.Levels)
            {
                var card = new Rect(x + shown * (cw + gap), y, cw, ch);
                if (LevelCard(card, level)) Load(level.SceneName);
                shown++;
            }

            // Controls cheat sheet for the parkour moves.
            y += ch + 28f * s;
            NeonGUI.Mono(new Rect(x, y, right - x, 20f * s), "// PARKOUR", 12, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] moves =
            {
                "RUN  ·  keep moving to build FLOW, top speed climbs",
                "SLIDE  ·  hold DOWN while running  ·  JUMP out of it to LONG-JUMP",
                "VAULT  ·  run into low cover, no slowdown",
                "WALL RUN  ·  sprint into a tall wall while holding toward it",
                "DASH / GRAPPLE  ·  smash cracked walls, swing and Sling-Boost",
            };
            foreach (var line in moves)
            {
                y += 26f * s;
                NeonGUI.Text(new Rect(x + 16f * s, y, right - x, 24f * s), line, 15, Theme.Muted);
            }

            if (NeonGUI.Button(new Rect(x, panel.yMax - 64f * s, 180f * s, 42f * s), "« BACK", 15)) page = Page.Title;
            NeonGUI.Mono(new Rect(x + 200f * s, panel.yMax - 64f * s, right - x - 200f * s, 42f * s),
                $"{shown:00} MODES LISTED", 12, Theme.Muted, TextAnchor.MiddleRight, FontStyle.Bold);
        }

        bool LevelCard(Rect r, LevelCatalog.Entry level)
        {
            var color = LevelCatalog.TypeColor(level.type);
            bool focused = NeonGUI.NavFocus();
            bool hover = NeonGUI.Lit(r, focused);
            if (Event.current.type == EventType.Repaint)
            {
                float cut = 14f * s;
                NeonGUI.Chamfer(r, hover ? NeonGUI.WithAlpha(color, 0.1f) : Theme.Panel, cut);
                NeonGUI.ChamferOutline(r, hover ? color : Theme.Faint, cut, Mathf.Max(1f, (hover ? 2f : 1f) * s));
                NeonGUI.Fill(new Rect(r.x, r.y + cut, 4f * s, r.height - cut), color);
                // Big index watermark.
                NeonGUI.Mono(new Rect(r.x, r.y - 6f * s, r.width - 10f * s, r.height), $"{level.number:00}", 72, NeonGUI.WithAlpha(color, hover ? 0.18f : 0.08f), TextAnchor.UpperRight, FontStyle.Bold);

                // Type tag block.
                string tag = level.type.ToString().ToUpperInvariant();
                float tw = NeonGUI.Style(10, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).CalcSize(new GUIContent(tag)).x + 14f * s;
                var tagRect = new Rect(r.x + 18f * s, r.y + 12f * s, tw, 18f * s);
                NeonGUI.Fill(tagRect, color);
                NeonGUI.Mono(tagRect, tag, 10, Theme.Bg, TextAnchor.MiddleCenter, FontStyle.Bold);
                NeonGUI.Mono(new Rect(tagRect.xMax + 8f * s, tagRect.y, 80f * s, 18f * s), $"OP-{level.number:00}", 10, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);

                var nameRect = new Rect(r.x + 18f * s, r.y + 34f * s, r.width - 30f * s, 28f * s);
                if (hover) NeonGUI.GlitchText(nameRect, level.name.ToUpperInvariant(), 21, Theme.Text, TextAnchor.UpperLeft, level.number, 1.5f, intensity: 1.5f);
                else NeonGUI.Text(nameRect, level.name.ToUpperInvariant(), 21, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                NeonGUI.Text(new Rect(r.x + 18f * s, r.y + 62f * s, r.width - 30f * s, 20f * s), level.blurb, 13, Theme.Muted);

                float best = LevelCatalog.BestTime(level.number, GameSettings.Difficulty);
                NeonGUI.Mono(new Rect(r.x + 18f * s, r.yMax - 26f * s, r.width - 34f * s, 18f * s),
                    best < 0f ? "NO RECORD" : $"BEST {TimeSpan.FromSeconds(best):mm\\:ss\\.ff}", 12, best < 0f ? NeonGUI.WithAlpha(Theme.Muted, 0.7f) : Theme.Accent2, TextAnchor.UpperLeft, FontStyle.Bold);
                if (hover)
                {
                    NeonGUI.Mono(new Rect(r.x, r.yMax - 26f * s, r.width - 16f * s, 18f * s), "DEPLOY >>", 12, color, TextAnchor.UpperRight, FontStyle.Bold);
                    NeonGUI.Brackets(new Rect(r.x - 5f * s, r.y - 5f * s, r.width + 10f * s, r.height + 10f * s), color, 10f * s, 2f * s);
                    NeonGUI.DataMosh(r, level.number, 0.35f);
                }
            }
            return NeonGUI.Pressed(r, focused);
        }

        static void Load(string scene)
        {
            GameSettings.Flush();
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogError($"[NeonRunner] {scene} is not in Build Settings.");
        }
    }
}
