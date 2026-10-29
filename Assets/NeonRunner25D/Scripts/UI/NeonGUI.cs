using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRunner
{
    /// <summary>
    /// "Cyber-Glitch" palette: graphite greys, white type, hot pink for the system, red for
    /// hazards and threats. A malfunctioning military HUD: sharp, industrial, no rounded corners.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Bg = new Color(0.085f, 0.085f, 0.095f);
        public static readonly Color Panel = new Color(0.15f, 0.15f, 0.165f, 0.82f);
        public static readonly Color PanelSolid = new Color(0.12f, 0.12f, 0.13f, 0.97f);
        public static readonly Color Text = new Color(0.98f, 0.98f, 0.98f);
        public static readonly Color Muted = new Color(0.6f, 0.6f, 0.64f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.13f);
        /// <summary>Hot pink: the system's own voice (focus, values, selection).</summary>
        public static readonly Color Accent = new Color(1f, 0.36f, 0.72f);
        /// <summary>Signal red: hazard bars, warnings, the player's resources.</summary>
        public static readonly Color Accent2 = new Color(1f, 0.22f, 0.26f);
        /// <summary>White: clears, records, completed counts.</summary>
        public static readonly Color Success = Text;
        public static readonly Color Danger = new Color(1f, 0.1f, 0.16f);
        /// <summary>Second colour of the chromatic split (the first is pink).</summary>
        public static readonly Color Split = new Color(1f, 0.12f, 0.14f);
    }

    /// <summary>
    /// Immediate-mode drawing kit for the Cyber-Glitch HUD: chamfered (diagonal-cut) panels,
    /// angled lines, hazard stripes, scanlines, chromatic-split and slice-glitched type, data-mosh
    /// blocks, and the menu widgets built from them. Call <see cref="Begin"/> at the top of OnGUI.
    /// Everything is plain GUI.DrawTexture, so it needs no extra shaders in a build.
    /// </summary>
    public static class NeonGUI
    {
        static Font body, title;
        static GUIStyle label;
        static Texture2D tri, stripes, scan, ramp;

        /// <summary>UI scale (1 at 1080p, 2 at 4K).</summary>
        public static float S { get; private set; } = 1f;

        /// <summary>
        /// A blurred render of whatever is behind the UI (set by the menu's backdrop). When present,
        /// panels become frosted glass instead of solid slabs.
        /// </summary>
        public static Texture Glass { get; set; }

        public static void Begin()
        {
            // Runtime-made textures die with play mode, but statics survive when the editor skips the
            // domain reload, so check the objects themselves, not just the style.
            if (label == null || !body || !title || !tri)
            {
                // Invincible for all UI text, Nechao Sharp for the game's title (Resources/Fonts).
                body = Resources.Load<Font>("Fonts/Invincible-Regular");
                if (!body) body = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Arial" }, 32);
                title = Resources.Load<Font>("Fonts/NechaoSharp");
                if (!title) title = body;
                label = new GUIStyle(GUI.skin.label) { font = body, wordWrap = false, richText = true, clipping = TextClipping.Overflow };
                BuildTextures();
            }
            S = Mathf.Max(0.6f, Screen.height / 1080f);
            PollNav();
        }

        // ===================================================================== Controller navigation
        // Widgets register in draw order. The D-pad / left stick (or arrow keys) walks that list, A (or
        // Enter) presses the focused one, and left/right adjusts a focused slider, switch or cycler; on
        // any other widget left/right moves focus too. Moving the mouse hands control back to hover.

        static int navCount, navLastCount, navFocus = -1, navFrame = -1;
        static int navAdjust, navHeld;
        static bool navSubmit;
        static float navRepeatAt;

        /// <summary>True while a controller (or the arrow keys) is driving the menu instead of the mouse.</summary>
        public static bool PadNavigating => navFocus >= 0;

        /// <summary>Set while a rebind is listening, so the pressed button doesn't also navigate.</summary>
        public static bool NavLocked { get; set; }

        /// <summary>Moves controller focus to widget <paramref name="index"/> (call when the page changes).</summary>
        public static void ResetFocus(int index = 0)
        {
            if (navFocus >= 0) navFocus = index;
        }

        static void PollNav()
        {
            // OnGUI runs several passes a frame; each draws the same widgets, so the last pass's count is the page's.
            navLastCount = navCount;
            navCount = 0;
            if (navFocus >= navLastCount && navLastCount > 0) navFocus = navLastCount - 1;
            if (navFrame == Time.frameCount) return;
            navFrame = Time.frameCount;

            int leftover = navAdjust;
            navAdjust = 0;
            navSubmit = false;
            if (Mouse.current is { } mouse && mouse.delta.ReadValue().sqrMagnitude > 4f)
            {
                navFocus = -1;
                return;
            }
            if (NavLocked) return;

            var dir = Vector2.zero;
            bool submit = false;
            if (Gamepad.current is { } pad)
            {
                dir = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
                submit = pad.buttonSouth.wasPressedThisFrame;
            }
            if (Keyboard.current is { } kb)
            {
                dir.x += (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                dir.y += (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
                submit |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            }

            // ±2 is vertical (down = next), ±1 horizontal; holding repeats after a short delay.
            int held = Mathf.Abs(dir.y) >= Mathf.Abs(dir.x)
                ? dir.y < -0.5f ? 2 : dir.y > 0.5f ? -2 : 0
                : dir.x > 0.5f ? 1 : dir.x < -0.5f ? -1 : 0;
            float now = Time.unscaledTime;
            bool fire = held != 0 && (held != navHeld || now >= navRepeatAt);
            if (fire) navRepeatAt = now + (held != navHeld ? 0.4f : 0.09f);
            navHeld = held;

            // The first press only wakes focus up on the first widget.
            if (navFocus < 0)
            {
                if (fire || submit) navFocus = 0;
                return;
            }
            // Left/right that no widget used to adjust a value last frame moves focus instead.
            if (leftover != 0) Step(leftover);
            if (fire)
            {
                if (held % 2 == 0) Step(held / 2);
                else navAdjust = held;
            }
            navSubmit = submit;
        }

        static void Step(int delta)
        {
            int n = navLastCount;
            if (n > 0) navFocus = ((navFocus + delta) % n + n) % n;
        }

        /// <summary>Registers the next focusable widget; true if it has controller focus.</summary>
        public static bool NavFocus() => navCount++ == navFocus;

        /// <summary>True if one of the next <paramref name="widgets"/> widgets to register has focus (to light up their row).</summary>
        public static bool NavAhead(int widgets = 1) => navFocus >= navCount && navFocus < navCount + widgets;

        /// <summary>Highlight state: controller focus while navigating with a pad, mouse hover otherwise.</summary>
        public static bool Lit(Rect r, bool focused) => PadNavigating ? focused : Hovered(r);

        /// <summary>A click on <paramref name="r"/>, or A pressed while it has focus.</summary>
        public static bool Pressed(Rect r, bool focused) => GUI.Button(r, GUIContent.none, GUIStyle.none) | Submitted(focused);

        static bool Submitted(bool focused)
        {
            if (!focused || !navSubmit) return false;
            navSubmit = false;
            return true;
        }

        static int Adjusted(bool focused)
        {
            if (!focused || navAdjust == 0) return 0;
            int a = navAdjust;
            navAdjust = 0;
            return a;
        }

        static void BuildTextures()
        {
            // Right triangle filling the lower-right half: a chamfered corner.
            tri = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                tri.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(x - y + 0.5f)));
            tri.Apply();

            // 45° stripes, half on half off, tiling.
            stripes = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                stripes.SetPixel(x, y, new Color(1f, 1f, 1f, (x + y) % 32 < 16 ? 1f : 0f));
            stripes.Apply();

            // One dark row in three.
            scan = new Texture2D(1, 3, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            scan.SetPixels(new[] { new Color(1, 1, 1, 1f), new Color(1, 1, 1, 0f), new Color(1, 1, 1, 0f) });
            scan.Apply();

            // Opaque on the left, eased out to nothing on the right.
            ramp = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 64; i++)
            {
                float t = i / 63f;
                ramp.SetPixel(i, 0, new Color(1f, 1f, 1f, 1f - t * t * (3f - 2f * t)));
            }
            ramp.Apply();
        }

        /// <summary>A scrim fading from <paramref name="c"/> at the left edge to clear at the right.</summary>
        public static void FadeRight(Rect r, Color c)
        {
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, ramp, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
        }

        // ===================================================================== Type

        public static GUIStyle Style(float size, Color color, TextAnchor anchor = TextAnchor.UpperLeft, FontStyle weight = FontStyle.Normal, bool titleFace = false)
        {
            label.font = titleFace ? title : body;
            label.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S));
            // A label under the mouse draws with its hover state, so every state gets the same colour.
            Tint(label, color);
            label.alignment = anchor;
            label.fontStyle = weight;
            return label;
        }

        static void Tint(GUIStyle style, Color color) =>
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;

        public static void Text(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.UpperLeft, FontStyle weight = FontStyle.Normal) =>
            GUI.Label(r, text, Style(size, color, anchor, weight));

        /// <summary>Readout type for numbers, codes and system chatter (same face as body text).</summary>
        public static void Mono(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.UpperLeft, FontStyle weight = FontStyle.Normal) =>
            GUI.Label(r, text, Style(size, color, anchor, weight));

        /// <summary>
        /// Display type with a pink/red chromatic split. Every so often (per <paramref name="seed"/>)
        /// the split widens and a horizontal slice of the word tears sideways.
        /// </summary>
        public static void GlitchText(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.UpperLeft,
            float seed = 0f, float split = 1.5f, bool titleFace = false, float intensity = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            float g = Glitch(seed) * intensity;
            float d = (split + Mathf.Abs(g) * 7f) * S;
            var style = Style(size, WithAlpha(Theme.Accent, 0.6f * color.a), anchor, FontStyle.Bold, titleFace);
            GUI.Label(new Rect(r.x - d, r.y, r.width, r.height), text, style);
            Tint(style, WithAlpha(Theme.Split, 0.6f * color.a));
            GUI.Label(new Rect(r.x + d, r.y + (g != 0f ? d * 0.4f : 0f), r.width, r.height), text, style);
            Tint(style, color);
            GUI.Label(r, text, style);

            if (g == 0f) return;
            // Torn slice: a band of the word redrawn displaced.
            float band = r.height * Mathf.Lerp(0.12f, 0.3f, Hash(seed + Slot(12f) * 0.37f));
            float top = r.y + (r.height - band) * Hash(seed * 1.7f + Slot(12f));
            float pad = 120f * S;
            GUI.BeginClip(new Rect(r.x - pad, top, r.width + pad * 2f, band));
            Tint(style, g > 0f ? Theme.Accent : Theme.Accent2);
            GUI.Label(new Rect(pad + g * 18f * S, r.y - top, r.width, r.height), text, style);
            GUI.EndClip();
        }

        // ===================================================================== Glitch timing

        static float Hash(float n) => Mathf.Repeat(Mathf.Sin(n * 127.1f + 311.7f) * 43758.547f, 1f);
        static float Slot(float rate) => Mathf.Floor(Time.unscaledTime * rate);

        /// <summary>0 most of the time; a burst in -1..1 for a few frames now and then.</summary>
        public static float Glitch(float seed, float chance = 0.07f)
        {
            float slot = Slot(12f);
            return Hash(slot * 0.731f + seed * 19.3f) < chance ? Hash(slot * 1.37f + seed) * 2f - 1f : 0f;
        }

        /// <summary>A hex word that changes a few times a second: decorative system chatter.</summary>
        public static string Hex(float seed, float rate = 3f) =>
            "0x" + ((int)(Hash(Slot(rate) + seed * 7.1f) * 0xFFFF)).ToString("X4");

        // ===================================================================== Shapes

        public static void Fill(Rect r, Color c)
        {
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
        }

        public static void Line(Vector2 a, Vector2 b, float thickness, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
            GUI.matrix = matrix;
        }

        /// <summary>Rectangle with its top-left and bottom-right corners cut at 45°.</summary>
        public static void Chamfer(Rect r, Color c, float cut)
        {
            if (Event.current.type != EventType.Repaint) return;
            cut = Mathf.Min(cut, r.width * 0.5f, r.height * 0.5f);
            Fill(new Rect(r.x + cut, r.y, r.width - cut, cut), c);
            Fill(new Rect(r.x, r.y + cut, r.width, r.height - cut * 2f), c);
            Fill(new Rect(r.x, r.yMax - cut, r.width - cut, cut), c);
            GUI.DrawTexture(new Rect(r.x, r.y, cut, cut), tri, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
            // The flipped corner needs DrawTextureWithTexCoords, which has no tint parameter: tint through GUI.color.
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - cut, r.yMax - cut, cut, cut), tri, new Rect(1f, 1f, -1f, -1f));
            GUI.color = prev;
        }

        public static void ChamferOutline(Rect r, Color c, float cut, float t)
        {
            cut = Mathf.Min(cut, r.width * 0.5f, r.height * 0.5f);
            float h = t * 0.5f;
            Fill(new Rect(r.x + cut, r.y, r.width - cut, t), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height - cut), c);
            Fill(new Rect(r.x, r.yMax - t, r.width - cut, t), c);
            Fill(new Rect(r.x, r.y + cut, t, r.height - cut), c);
            Line(new Vector2(r.x + h, r.y + cut), new Vector2(r.x + cut, r.y + h), t, c);
            Line(new Vector2(r.xMax - cut, r.yMax - h), new Vector2(r.xMax - h, r.yMax - cut), t, c);
        }

        /// <summary>Targeting brackets on all four corners.</summary>
        public static void Brackets(Rect r, Color c, float length, float t)
        {
            Fill(new Rect(r.x, r.y, length, t), c); Fill(new Rect(r.x, r.y, t, length), c);
            Fill(new Rect(r.xMax - length, r.y, length, t), c); Fill(new Rect(r.xMax - t, r.y, t, length), c);
            Fill(new Rect(r.x, r.yMax - t, length, t), c); Fill(new Rect(r.x, r.yMax - length, t, length), c);
            Fill(new Rect(r.xMax - length, r.yMax - t, length, t), c); Fill(new Rect(r.xMax - t, r.yMax - length, t, length), c);
        }

        /// <summary>Diagonal hazard stripes, drifting sideways when <paramref name="scroll"/> is non-zero.</summary>
        public static void Hazard(Rect r, Color c, float stripe = 10f, float scroll = 0f)
        {
            if (Event.current.type != EventType.Repaint) return;
            float tile = stripe * 2f * S;
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(r, stripes, new Rect(Time.unscaledTime * scroll, 0f, r.width / tile, r.height / tile));
            GUI.color = prev;
        }

        /// <summary>CRT scanlines (and a faint rolling bar) over a region.</summary>
        public static void Scanlines(Rect r, float alpha = 0.22f)
        {
            if (Event.current.type != EventType.Repaint) return;
            float pitch = Mathf.Max(2f, Mathf.Round(3f * S));
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTextureWithTexCoords(r, scan, new Rect(0f, 0f, 1f, r.height / pitch));
            GUI.color = prev;
            float bar = Mathf.Repeat(Time.unscaledTime * 0.18f, 1.4f) - 0.2f;
            Fill(new Rect(r.x, r.y + r.height * bar, r.width, r.height * 0.06f), new Color(1f, 1f, 1f, 0.025f));
        }

        /// <summary>Data-mosh: smeared blocks and torn lines flickering inside an area.</summary>
        public static void DataMosh(Rect area, float seed, float density = 0.3f)
        {
            if (Event.current.type != EventType.Repaint) return;
            float slot = Slot(15f);
            for (int k = 0; k < 9; k++)
            {
                float h = Hash(slot * 0.91f + k * 3.17f + seed * 11.3f);
                if (h > density) continue;
                float x = area.x + Hash(h * 91.7f + k) * area.width;
                float y = area.y + Hash(h * 37.1f + k * 2.3f) * area.height;
                float w = Mathf.Lerp(12f, 140f, Hash(h * 13.7f)) * S;
                float bh = Mathf.Lerp(1.5f, 9f, Hash(h * 5.3f)) * S;
                var c = k % 3 == 0 ? Theme.Accent : k % 3 == 1 ? Theme.Accent2 : Theme.Split;
                Fill(new Rect(Mathf.Min(x, area.xMax - w), y, w, bh), WithAlpha(c, Mathf.Lerp(0.15f, 0.6f, Hash(h * 2.9f))));
            }
        }

        /// <summary>
        /// The standard surface: chamfered charcoal slab, scanlines, a hairline border with a
        /// chromatic split, accent ticks on the cut corners and an optional system tag.
        /// </summary>
        public static void Panel(Rect r, string tag = null, Color? accent = null, float alpha = 1f)
        {
            var a = accent ?? Theme.Accent;
            float cut = 22f * S, t = Mathf.Max(1f, 1.5f * S);
            if (Glass)
            {
                GlassFill(r, cut, alpha);
                Chamfer(r, WithAlpha(Theme.PanelSolid, 0.55f * alpha), cut);
            }
            else Chamfer(r, WithAlpha(Theme.PanelSolid, Theme.PanelSolid.a * alpha), cut);
            Scanlines(r, 0.18f * alpha);
            // Split border: red ghost offset right, pink ghost offset left, then the true edge.
            ChamferOutline(new Rect(r.x + 2f * S, r.y, r.width, r.height), WithAlpha(Theme.Split, 0.18f * alpha), cut, t);
            ChamferOutline(new Rect(r.x - 2f * S, r.y, r.width, r.height), WithAlpha(Theme.Accent, 0.18f * alpha), cut, t);
            ChamferOutline(r, WithAlpha(Theme.Faint, Theme.Faint.a * 1.6f * alpha), cut, t);
            // Heavy ticks along the cut corners.
            Line(new Vector2(r.x, r.y + cut + 26f * S), new Vector2(r.x, r.y + cut), 4f * S, WithAlpha(a, alpha));
            Line(new Vector2(r.x, r.y + cut), new Vector2(r.x + cut, r.y), 4f * S, WithAlpha(a, alpha));
            Line(new Vector2(r.x + cut, r.y), new Vector2(r.x + cut + 60f * S, r.y), 4f * S, WithAlpha(a, alpha));
            Fill(new Rect(r.xMax - 70f * S - cut, r.yMax - 3f * S, 70f * S, 3f * S), WithAlpha(Theme.Accent2, alpha));
            if (!string.IsNullOrEmpty(tag))
                Mono(new Rect(r.x, r.y + 8f * S, r.width - 16f * S, 16f * S), tag, 10, WithAlpha(Theme.Muted, alpha), TextAnchor.UpperRight);
        }

        /// <summary>
        /// The blurred backdrop, mapped so the patch under <paramref name="r"/> lines up with the screen.
        /// Drawn in the chamfer's three bands; the cut corners keep only the tint.
        /// </summary>
        static void GlassFill(Rect r, float cut, float alpha)
        {
            if (Event.current.type != EventType.Repaint) return;
            cut = Mathf.Min(cut, r.width * 0.5f, r.height * 0.5f);
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            void Band(Rect b) => GUI.DrawTextureWithTexCoords(b, Glass,
                new Rect(b.x / Screen.width, 1f - b.yMax / Screen.height, b.width / Screen.width, b.height / Screen.height));
            Band(new Rect(r.x + cut, r.y, r.width - cut, cut));
            Band(new Rect(r.x, r.y + cut, r.width, r.height - cut * 2f));
            Band(new Rect(r.x, r.yMax - cut, r.width - cut, cut));
            GUI.color = prev;
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static bool Hovered(Rect r) => r.Contains(Event.current.mousePosition);

        // ===================================================================== Widgets

        /// <summary>Chamfered slab button; <paramref name="selected"/> floods it with the accent.</summary>
        public static bool Button(Rect r, string text, float size = 16f, bool selected = false, Color? accent = null)
        {
            var a = accent ?? Theme.Accent;
            bool focused = NavFocus();
            bool hover = Lit(r, focused);
            if (Event.current.type == EventType.Repaint)
            {
                float cut = Mathf.Min(10f * S, r.height * 0.35f);
                if (selected)
                {
                    Chamfer(r, a, cut);
                    Hazard(new Rect(r.xMax - 22f * S, r.y + 4f * S, 14f * S, r.height - 8f * S), WithAlpha(Theme.Bg, 0.35f), 3f);
                    Text(r, text, size, Theme.Bg, TextAnchor.MiddleCenter, FontStyle.Bold);
                }
                else
                {
                    Chamfer(r, hover ? WithAlpha(a, 0.12f) : Theme.Panel, cut);
                    ChamferOutline(r, hover ? a : Theme.Faint, cut, Mathf.Max(1f, (hover ? 2f : 1.5f) * S));
                    if (hover)
                    {
                        Fill(new Rect(r.x, r.y + cut, 4f * S, r.height - cut), a);
                        GlitchText(r, text, size, Theme.Text, TextAnchor.MiddleCenter, r.x * 0.01f, 1.2f, intensity: 0.6f);
                    }
                    else Text(r, text, size, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                }
            }
            return Pressed(r, focused);
        }

        /// <summary>Segmented power-bar slider with an red index needle; drag anywhere on it.</summary>
        public static float Slider(Rect r, float value, float min, float max)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            bool focused = NavFocus();
            int adjust = Adjusted(focused);
            if (adjust != 0)
            {
                value = Mathf.Clamp(value + adjust * (max - min) / 20f, min, max);
                GUI.changed = true;
            }
            var e = Event.current;
            float FromMouse() => Mathf.Lerp(min, max, Mathf.InverseLerp(r.x, r.xMax, e.mousePosition.x));
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && r.Contains(e.mousePosition):
                    GUIUtility.hotControl = id;
                    value = FromMouse();
                    GUI.changed = true;
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    value = FromMouse();
                    GUI.changed = true;
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.Repaint:
                    float t = Mathf.InverseLerp(min, max, value);
                    int n = Mathf.Max(8, Mathf.RoundToInt(r.width / (11f * S)));
                    float seg = r.width / n, bh = 10f * S;
                    int lit = Mathf.RoundToInt(t * n);
                    bool active = GUIUtility.hotControl == id || Lit(r, focused);
                    if (focused) Brackets(new Rect(r.x - 6f * S, r.y - 4f * S, r.width + 12f * S, r.height + 8f * S), Theme.Accent2, 6f * S, Mathf.Max(1f, 1.5f * S));
                    for (int i = 0; i < n; i++)
                    {
                        // Segments step up in height toward the top end, like a level meter.
                        float sh = bh * Mathf.Lerp(0.55f, 1f, (float)i / (n - 1));
                        var c = i < lit ? (i == lit - 1 ? Theme.Text : Theme.Accent) : Theme.Faint;
                        Fill(new Rect(r.x + i * seg, r.center.y + bh * 0.5f - sh, seg - 2f * S, sh), c);
                    }
                    float nx = r.x + r.width * t;
                    Fill(new Rect(nx - 1.5f * S, r.center.y - 13f * S, 3f * S, 26f * S), active ? Theme.Accent2 : WithAlpha(Theme.Accent2, 0.85f));
                    Fill(new Rect(nx - 5f * S, r.center.y - 15f * S, 10f * S, 3f * S), Theme.Accent2);
                    break;
            }
            return value;
        }

        /// <summary>OFF | ON block switch, right-aligned in the row.</summary>
        public static bool Toggle(Rect r, bool value)
        {
            float cw = 56f * S, ch = Mathf.Min(28f * S, r.height);
            var on = new Rect(r.xMax - cw, r.center.y - ch * 0.5f, cw, ch);
            var off = new Rect(on.x - cw - 4f * S, on.y, cw, ch);
            bool focused = NavFocus();
            int adjust = Adjusted(focused);
            if (adjust != 0) value = adjust > 0;
            if (Event.current.type == EventType.Repaint)
            {
                Fill(off, value ? Theme.Panel : WithAlpha(Theme.Muted, 0.45f));
                Fill(on, value ? Theme.Accent : Theme.Panel);
                ChamferOutline(off, Theme.Faint, 0f, Mathf.Max(1f, S));
                ChamferOutline(on, value ? Theme.Accent : Theme.Faint, 0f, Mathf.Max(1f, S));
                Mono(off, "OFF", 13, value ? Theme.Muted : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                Mono(on, "ON", 13, value ? Theme.Bg : Theme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
                if (Lit(r, focused)) Brackets(new Rect(off.x - 4f * S, on.y - 4f * S, on.xMax - off.x + 8f * S, ch + 8f * S), Theme.Accent2, 6f * S, Mathf.Max(1f, 1.5f * S));
            }
            if (Pressed(r, focused)) value = !value;
            return value;
        }

        /// <summary>« value » selector that wraps around.</summary>
        public static int Cycler(Rect r, int index, int count, string text)
        {
            float b = r.height;
            var left = new Rect(r.x, r.y, b, b);
            var right = new Rect(r.xMax - b, r.y, b, b);
            bool focused = NavFocus();
            int adjust = Adjusted(focused);
            if (adjust != 0) index = (index + adjust + count) % count;
            if (Submitted(focused)) index = (index + 1) % count;
            if (Event.current.type == EventType.Repaint)
            {
                float cut = Mathf.Min(8f * S, r.height * 0.3f);
                Chamfer(r, Theme.Panel, cut);
                ChamferOutline(r, Lit(r, focused) ? WithAlpha(Theme.Accent, focused ? 1f : 0.6f) : Theme.Faint, cut, Mathf.Max(1f, (focused ? 2f : 1.5f) * S));
                Fill(new Rect(left.xMax, r.y + 6f * S, Mathf.Max(1f, S), b - 12f * S), Theme.Faint);
                Fill(new Rect(right.x, r.y + 6f * S, Mathf.Max(1f, S), b - 12f * S), Theme.Faint);
                Mono(left, "«", 18, Hovered(left) ? Theme.Accent2 : Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                Mono(right, "»", 18, Hovered(right) ? Theme.Accent2 : Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                Mono(r, text.ToUpperInvariant(), 14, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            if (GUI.Button(left, GUIContent.none, GUIStyle.none)) index = (index + count - 1) % count;
            if (GUI.Button(new Rect(left.xMax, r.y, r.width - 2f * b, b), GUIContent.none, GUIStyle.none) ||
                GUI.Button(right, GUIContent.none, GUIStyle.none)) index = (index + 1) % count;
            return index;
        }
    }
}
