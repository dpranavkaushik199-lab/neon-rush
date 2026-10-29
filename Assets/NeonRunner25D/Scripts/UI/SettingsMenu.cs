using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRunner
{
    /// <summary>
    /// The tabbed settings panel (Audio / Video / Controls / Gameplay), drawn with <see cref="NeonGUI"/>.
    /// Shared by the main menu and the pause screen. Edits save immediately.
    /// </summary>
    public class SettingsMenu
    {
        enum Tab { Audio, Video, Controls, Gameplay }

        /// <summary>A rebindable keyboard/mouse binding: the action and which of its bindings.</summary>
        readonly struct Rebindable
        {
            public readonly string label, action, compositePart;
            public Rebindable(string label, string action, string compositePart = null)
            {
                this.label = label;
                this.action = action;
                this.compositePart = compositePart;
            }
        }

        static readonly Rebindable[] Rebindables =
        {
            new Rebindable("Move Left", "Move", "left"),
            new Rebindable("Move Right", "Move", "right"),
            new Rebindable("Look Up", "Move", "up"),
            new Rebindable("Fast Fall / Down", "Move", "down"),
            new Rebindable("Jump", "Jump"),
            new Rebindable("Dash / Boost", "Dash"),
            new Rebindable("Grapple (hold)", "Grapple"),
            new Rebindable("Taser", "Taser"),
            new Rebindable("Smoke", "Smoke"),
        };

        static readonly string[] BufferNames = { "Tight (½×)", "Standard", "Generous (2×)" };
        static readonly string[] ColorblindNames = { "Off", "Protanopia", "Deuteranopia", "Tritanopia" };

        Tab tab;
        string hint;
        InputActionMap rebindMap;
        InputActionRebindingExtensions.RebindingOperation rebind;
        InputAction rebindAction;
        int rebindIndex;
        int rebindEndedFrame = -1;
        Resolution[] resolutions;

        /// <summary>
        /// True while waiting for a key (and on the frame it arrives), so the pause screen
        /// doesn't treat the Escape that cancels a rebind as "close".
        /// </summary>
        public bool ConsumesInput => rebind != null || Time.frameCount == rebindEndedFrame;

        float s => NeonGUI.S;

        public void Close()
        {
            CancelRebind();
            rebindMap?.Dispose();
            rebindMap = null;
            NeonGUI.NavLocked = false;
            GameSettings.Flush();
        }

        /// <summary>Draws the panel; returns true when the player pressed Back.</summary>
        public bool Draw(Rect panel)
        {
            hint = null;
            NeonGUI.NavLocked = ConsumesInput;
            NeonGUI.Panel(panel, $"CFG.SYS // {NeonGUI.Hex(8f)}");
            NeonGUI.GlitchText(new Rect(panel.x + 56f * s, panel.y + 26f * s, panel.width, 48f * s), "SETTINGS", 40, Theme.Text, TextAnchor.MiddleLeft, 8f, 1.8f);
            NeonGUI.Hazard(new Rect(panel.x + 56f * s, panel.y + 78f * s, 110f * s, 7f * s), Theme.Accent2, 3.5f, 0.5f);

            // Tabs: numbered, left-aligned, the active one flooded pink
            var names = (Tab[])Enum.GetValues(typeof(Tab));
            float tw = 170f * s, gap = 8f * s, tx = panel.x + 56f * s;
            for (int i = 0; i < names.Length; i++)
            {
                var t = names[i];
                if (NeonGUI.Button(new Rect(tx, panel.y + 100f * s, tw, 40f * s), $"{i + 1:00}  {t.ToString().ToUpperInvariant()}", 15, tab == t) && tab != t)
                {
                    CancelRebind();
                    tab = t;
                }
                tx += tw + gap;
            }
            NeonGUI.Fill(new Rect(panel.x + 56f * s, panel.y + 148f * s, panel.width - 112f * s, Mathf.Max(1f, s)), Theme.Faint);

            var body = new Rect(panel.x + 56f * s, panel.y + 164f * s, panel.width - 112f * s, panel.height - 260f * s);
            float y = body.y;
            switch (tab)
            {
                case Tab.Audio: DrawAudio(body, ref y); break;
                case Tab.Video: DrawVideo(body, ref y); break;
                case Tab.Controls: DrawControls(body, ref y); break;
                case Tab.Gameplay: DrawGameplay(body, ref y); break;
            }

            // Footer: hint for the hovered row, then Reset / Back
            var footer = new Rect(panel.x + 56f * s, panel.yMax - 96f * s, panel.width - 112f * s, Mathf.Max(1f, s));
            NeonGUI.Fill(footer, Theme.Faint);
            NeonGUI.Mono(new Rect(footer.x, footer.y + 8f * s, footer.width, 22f * s),
                string.IsNullOrEmpty(hint) ? "> AWAITING INPUT_" : "> " + hint, 13, string.IsNullOrEmpty(hint) ? Theme.Muted : Theme.Accent, TextAnchor.MiddleLeft);

            float bw = 180f * s, bh = 42f * s, by = panel.yMax - 60f * s;
            if (NeonGUI.Button(new Rect(panel.x + 56f * s, by, bw, bh), "RESET ALL", 14, false, Theme.Danger))
            {
                CancelRebind();
                GameSettings.ResetToDefaults();
                rebindMap?.Dispose();
                rebindMap = null;
                SettingsRuntime.ApplyResolution();
            }
            bool back = NeonGUI.Button(new Rect(panel.xMax - 56f * s - bw, by, bw, bh), "BACK", 15, true);
            if (back) Close();
            return back;
        }

        // ===================================================================== Rows

        /// <param name="widgets">How many focusable widgets the row holds, so controller focus on any of them lights it.</param>
        Rect Row(Rect body, ref float y, string label, string rowHint, float height = 46f, int widgets = 1)
        {
            var row = new Rect(body.x, y, body.width, height * s);
            bool hover = NeonGUI.PadNavigating ? NeonGUI.NavAhead(widgets) : NeonGUI.Hovered(row);
            if (hover) hint = rowHint;
            if (Event.current.type == EventType.Repaint)
            {
                if (hover)
                {
                    NeonGUI.Fill(new Rect(row.x - 14f * s, row.y + 2f * s, row.width + 28f * s, row.height - 4f * s), NeonGUI.WithAlpha(Theme.Accent, 0.06f));
                    NeonGUI.Fill(new Rect(row.x - 14f * s, row.y + 2f * s, 3f * s, row.height - 4f * s), Theme.Accent2);
                }
                NeonGUI.Text(new Rect(row.x + (hover ? 6f * s : 0f), row.y, row.width * 0.45f, row.height), label.ToUpperInvariant(), 15, hover ? Theme.Text : NeonGUI.WithAlpha(Theme.Text, 0.82f), TextAnchor.MiddleLeft, FontStyle.Bold);
            }
            y += height * s;
            return new Rect(row.x + row.width * 0.45f, row.y + 6f * s, row.width * 0.55f, row.height - 12f * s);
        }

        void Header(Rect body, ref float y, string text)
        {
            NeonGUI.Mono(new Rect(body.x, y + 8f * s, body.width, 22f * s), "// " + text, 12, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 34f * s;
        }

        float PercentSlider(Rect body, ref float y, string label, string rowHint, float value)
        {
            var r = Row(body, ref y, label, rowHint);
            NeonGUI.Mono(new Rect(r.xMax - 56f * s, r.y, 56f * s, r.height), $"{Mathf.RoundToInt(value * 100f):000}%", 15, Theme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            return NeonGUI.Slider(new Rect(r.x, r.y, r.width - 72f * s, r.height), value, 0f, 1f);
        }

        bool Switch(Rect body, ref float y, string label, string rowHint, bool value) =>
            NeonGUI.Toggle(Row(body, ref y, label, rowHint), value);

        // ===================================================================== Tabs

        void DrawAudio(Rect body, ref float y)
        {
            GameSettings.MasterVolume = PercentSlider(body, ref y, "Master Volume", "Everything", GameSettings.MasterVolume);
            GameSettings.MusicVolume = PercentSlider(body, ref y, "Music Volume", "The soundtrack is loud by design - turn it down here", GameSettings.MusicVolume);
            y += 10f * s;
            var r = Row(body, ref y, "Dynamic Range", "Compressed: loud sounds quieter, quiet sounds louder. Good for laptop speakers or noisy rooms");
            GameSettings.CompressedAudio = NeonGUI.Cycler(r, GameSettings.CompressedAudio ? 1 : 0, 2, GameSettings.CompressedAudio ? "Compressed" : "Standard") == 1;
        }

        void DrawVideo(Rect body, ref float y)
        {
            resolutions ??= Screen.resolutions
                .Select(res => new Resolution { width = res.width, height = res.height })
                .GroupBy(res => (res.width, res.height)).Select(g => g.First())
                .OrderBy(res => res.width).ThenBy(res => res.height).ToArray();

            var rr = Row(body, ref y, "Resolution", "Screen size (applies in a built game, not in the editor)");
            if (resolutions.Length > 0)
            {
                var want = GameSettings.Resolution;
                if (want.x <= 0) want = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
                int index = Array.FindIndex(resolutions, res => res.width == want.x && res.height == want.y);
                if (index < 0) index = resolutions.Length - 1;
                int picked = NeonGUI.Cycler(rr, index, resolutions.Length, $"{resolutions[index].width} × {resolutions[index].height}");
                if (picked != index)
                {
                    GameSettings.Resolution = new Vector2Int(resolutions[picked].width, resolutions[picked].height);
                    SettingsRuntime.ApplyResolution();
                }
            }

            bool fullscreen = Switch(body, ref y, "Fullscreen", "Borderless fullscreen or a window", GameSettings.Fullscreen);
            if (fullscreen != GameSettings.Fullscreen)
            {
                GameSettings.Fullscreen = fullscreen;
                SettingsRuntime.ApplyResolution();
            }

            var fr = Row(body, ref y, "Field of View", "Wider shows more of the level and can reduce motion sickness");
            NeonGUI.Mono(new Rect(fr.xMax - 56f * s, fr.y, 56f * s, fr.height), $"{Mathf.RoundToInt(GameSettings.FieldOfView)}°", 15, Theme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            GameSettings.FieldOfView = Mathf.Round(NeonGUI.Slider(new Rect(fr.x, fr.y, fr.width - 72f * s, fr.height), GameSettings.FieldOfView, GameSettings.MinFov, GameSettings.MaxFov));

            GameSettings.ScreenShake = PercentSlider(body, ref y, "Screen Shake", "Camera shake on impacts, boosts and lightning. 0% turns it off", GameSettings.ScreenShake);
            y += 10f * s;
            Header(body, ref y, "EFFECTS");
            GameSettings.MotionBlur = Switch(body, ref y, "Motion Blur", "Sells the speed, but blurs platform edges", GameSettings.MotionBlur);
            GameSettings.ChromaticAberration = Switch(body, ref y, "Chromatic Aberration", "Colour fringing and lens warp during a Sling-Boost", GameSettings.ChromaticAberration);
            GameSettings.Bloom = Switch(body, ref y, "Bloom", "Neon glow. Off gives a cleaner, flatter image", GameSettings.Bloom);
        }

        void DrawControls(Rect body, ref float y)
        {
            if (rebindMap == null)
            {
                rebindMap = PlayerInputReader.CreateMap();
                PlayerInputReader.LoadOverrides(rebindMap, GameSettings.BindingOverrides);
            }

            float bw = 200f * s, gap = 16f * s;
            float padX = body.xMax - bw, keyX = padX - gap - bw;
            // A controller rebind only cancels through Start, so Esc is handled here for mouse users.
            if (rebind != null && Keyboard.current is { escapeKey: { wasPressedThisFrame: true } }) CancelRebind();

            Header(body, ref y, "BINDINGS");
            NeonGUI.Mono(new Rect(keyX, y - 34f * s, bw, 34f * s), "KEYBOARD & MOUSE", 12, Theme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            NeonGUI.Mono(new Rect(padX, y - 34f * s, bw, 34f * s), "CONTROLLER", 12, Theme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            foreach (var item in Rebindables)
            {
                var action = rebindMap.FindAction(item.action);
                var r = Row(body, ref y, item.label, item.compositePart == null
                    ? "Click, then press the new key or button  ·  Esc, Start or a second click cancels  ·  a key already in use swaps over"
                    : "Moving on a controller uses the left stick or the D-pad", 40f, item.compositePart == null ? 2 : 1);

                BindingButton(new Rect(keyX, r.y, bw, r.height), action, BindingIndex(action, item.compositePart, false), false);
                if (item.compositePart == null)
                    BindingButton(new Rect(padX, r.y, bw, r.height), action, BindingIndex(action, null, true), true);
                else
                    NeonGUI.Mono(new Rect(padX, r.y, bw, r.height), "L-STICK / D-PAD", 14, Theme.Muted, TextAnchor.MiddleCenter);
            }

            y += 8f * s;
            var br = Row(body, ref y, "Input Buffer", "How early a jump, dash or grapple press still counts before you can act on it");
            GameSettings.InputBuffer = (InputBufferMode)NeonGUI.Cycler(br, (int)GameSettings.InputBuffer, 3, BufferNames[(int)GameSettings.InputBuffer]);

            var dr = Row(body, ref y, "Stick Deadzone", "Stick movement smaller than this is ignored. Raise it if your character drifts");
            NeonGUI.Mono(new Rect(dr.xMax - 56f * s, dr.y, 56f * s, dr.height), $"{Mathf.RoundToInt(GameSettings.StickDeadzone * 100f):000}%", 15, Theme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            GameSettings.StickDeadzone = NeonGUI.Slider(new Rect(dr.x, dr.y, dr.width - 72f * s, dr.height), GameSettings.StickDeadzone, 0.05f, 0.6f);
        }

        void DrawGameplay(Rect body, ref float y)
        {
            var r = Row(body, ref y, "Difficulty", GameSettings.Describe(GameSettings.Difficulty));
            GameSettings.Difficulty = (Difficulty)NeonGUI.Cycler(r, (int)GameSettings.Difficulty, 3, GameSettings.Difficulty.ToString());
            NeonGUI.Mono(new Rect(body.x, y, body.width, 22f * s), GameSettings.Describe(GameSettings.Difficulty), 14, Theme.Muted, TextAnchor.MiddleRight);
            y += 34f * s;

            GameSettings.ProximityWarning = Switch(body, ref y, "Visual Proximity Warning",
                "Screen edges flash red, stronger and faster as a chaser closes in", GameSettings.ProximityWarning);

            var cr = Row(body, ref y, "Colourblind Mode", "Shifts colours so red (danger) and green/blue (safe) stay distinct");
            GameSettings.Colorblind = (ColorblindMode)NeonGUI.Cycler(cr, (int)GameSettings.Colorblind, 4, ColorblindNames[(int)GameSettings.Colorblind]);
        }

        // ===================================================================== Rebinding

        void BindingButton(Rect r, InputAction action, int index, bool gamepad)
        {
            bool waiting = rebind != null && rebindAction == action && rebindIndex == index;
            string text = waiting ? gamepad ? "Press a button…" : "Press a key…"
                        : gamepad ? GamepadLabel(action.bindings[index].effectivePath) : action.GetBindingDisplayString(index);
            if (!NeonGUI.Button(r, text, 14, waiting)) return;
            if (waiting) CancelRebind();
            else StartRebind(action, index, gamepad);
        }

        /// <summary>
        /// The keyboard binding (first keyboard key, or the WASD part of the Move composite) or the
        /// controller binding (first gamepad button) of an action.
        /// </summary>
        static int BindingIndex(InputAction action, string compositePart, bool gamepad)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                bool match = compositePart != null
                    ? b.isPartOfComposite && string.Equals(b.name, compositePart, StringComparison.OrdinalIgnoreCase)
                    : !b.isComposite && !b.isPartOfComposite && b.path.StartsWith(gamepad ? "<Gamepad>" : "<Keyboard>");
                if (match) return i;
            }
            return 0;
        }

        /// <summary>Every binding the Controls tab can change, keyboard and controller.</summary>
        IEnumerable<(InputAction action, int index)> Slots()
        {
            foreach (var item in Rebindables)
            {
                var action = rebindMap.FindAction(item.action);
                yield return (action, BindingIndex(action, item.compositePart, false));
                if (item.compositePart == null) yield return (action, BindingIndex(action, null, true));
            }
        }

        // Xbox-style names; the bindings themselves are generic, so any controller works.
        static readonly Dictionary<string, string> PadNames = new Dictionary<string, string>
        {
            ["buttonSouth"] = "A", ["buttonEast"] = "B", ["buttonWest"] = "X", ["buttonNorth"] = "Y",
            ["leftShoulder"] = "LB", ["rightShoulder"] = "RB", ["leftTrigger"] = "LT", ["rightTrigger"] = "RT",
            ["leftStickPress"] = "Left Stick Click", ["rightStickPress"] = "Right Stick Click",
            ["select"] = "View", ["start"] = "Menu",
            ["dpad/up"] = "D-Pad Up", ["dpad/down"] = "D-Pad Down", ["dpad/left"] = "D-Pad Left", ["dpad/right"] = "D-Pad Right",
        };

        static string GamepadLabel(string path)
        {
            int slash = path.IndexOf('/');
            string control = slash >= 0 ? path.Substring(slash + 1) : path;
            return PadNames.TryGetValue(control, out var name) ? name : InputControlPath.ToHumanReadableString(path);
        }

        void StartRebind(InputAction action, int index, bool gamepad)
        {
            CancelRebind();
            rebindAction = action;
            rebindIndex = index;
            string before = action.bindings[index].effectivePath;
            var op = action.PerformInteractiveRebinding(index).OnMatchWaitForAnother(0.05f);
            if (gamepad)
            {
                // Buttons, bumpers and triggers only: the sticks and D-pad move, Start pauses.
                op.WithControlsHavingToMatchPath("<Gamepad>")
                    .WithControlsExcluding("<Gamepad>/leftStick")
                    .WithControlsExcluding("<Gamepad>/rightStick")
                    .WithControlsExcluding("<Gamepad>/dpad")
                    .WithCancelingThrough("<Gamepad>/start")
                    // Save "<Gamepad>/buttonSouth", not "<XInputControllerWindows>/buttonSouth", so the
                    // binding keeps working on any controller.
                    .OnGeneratePath(control => "<Gamepad>" + control.path.Substring(control.device.path.Length));
            }
            else
            {
                op.WithControlsHavingToMatchPath("<Keyboard>")
                    .WithControlsHavingToMatchPath("<Mouse>/leftButton")
                    .WithControlsHavingToMatchPath("<Mouse>/rightButton")
                    .WithControlsHavingToMatchPath("<Mouse>/middleButton")
                    .WithCancelingThrough("<Keyboard>/escape");
            }
            rebind = op
                .OnComplete(_ =>
                {
                    SwapConflict(action, index, before);
                    EndRebind();
                    GameSettings.BindingOverrides = PlayerInputReader.SaveOverrides(rebindMap);
                })
                .OnCancel(_ => EndRebind())
                .Start();
        }

        /// <summary>If the new key or button was already used by another rebindable control, give that control the old one.</summary>
        void SwapConflict(InputAction changed, int changedIndex, string oldPath)
        {
            string newPath = changed.bindings[changedIndex].effectivePath;
            foreach (var (action, index) in Slots())
            {
                if (action == changed && index == changedIndex) continue;
                if (action.bindings[index].effectivePath == newPath) action.ApplyBindingOverride(index, oldPath);
            }
        }

        void EndRebind()
        {
            rebind?.Dispose();
            rebind = null;
            rebindEndedFrame = Time.frameCount;
        }

        void CancelRebind()
        {
            if (rebind == null) return;
            rebind.Cancel();
            EndRebind();
        }
    }
}
