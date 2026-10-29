using System;
using UnityEngine;

namespace NeonRunner
{
    public enum Difficulty { Casual, Standard, Nightmare }
    public enum ColorblindMode { Off, Protanopia, Deuteranopia, Tritanopia }
    public enum InputBufferMode { Tight, Standard, Generous }

    /// <summary>
    /// Player options, saved in PlayerPrefs. Gameplay reads these live; <see cref="Changed"/>
    /// fires after every edit so scene-level things (volumes, bindings) can re-apply.
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "NeonRunner.Settings.";

        public static event Action Changed;

        // ---- Audio
        static float masterVolume = Load("MasterVolume", 1f);
        static float musicVolume = Load("MusicVolume", 0.7f);
        static bool compressedAudio = Load("CompressedAudio", false);

        public static float MasterVolume { get => masterVolume; set => Save("MasterVolume", ref masterVolume, Mathf.Clamp01(value)); }
        /// <summary>The soundtrack is mastered loud, so it gets its own slider.</summary>
        public static float MusicVolume { get => musicVolume; set => Save("MusicVolume", ref musicVolume, Mathf.Clamp01(value)); }
        /// <summary>Squash the dynamic range: loud peaks quieter, quiet sounds louder.</summary>
        public static bool CompressedAudio { get => compressedAudio; set => Save("CompressedAudio", ref compressedAudio, value); }

        // ---- Video
        static bool fullscreen = Load("Fullscreen", true);
        static float fieldOfView = Load("FieldOfView", 40f);
        static float screenShake = Load("ScreenShake", 1f);
        static bool motionBlur = Load("MotionBlur", true);
        static bool chromaticAberration = Load("ChromaticAberration", true);
        static bool bloom = Load("Bloom", true);

        public const float MinFov = 32f, MaxFov = 60f, DefaultFov = 40f;
        public static bool Fullscreen { get => fullscreen; set => Save("Fullscreen", ref fullscreen, value); }
        /// <summary>Chosen screen size, or (0, 0) for the desktop's.</summary>
        public static Vector2Int Resolution
        {
            get => new Vector2Int(Load("ResolutionWidth", 0), Load("ResolutionHeight", 0));
            set
            {
                if (value == Resolution) return;
                PlayerPrefs.SetInt(Prefix + "ResolutionWidth", value.x);
                PlayerPrefs.SetInt(Prefix + "ResolutionHeight", value.y);
                Changed?.Invoke();
            }
        }
        /// <summary>Vertical field of view in degrees; the Sling-Boost zoom widens from here.</summary>
        public static float FieldOfView { get => fieldOfView; set => Save("FieldOfView", ref fieldOfView, Mathf.Clamp(value, MinFov, MaxFov)); }
        public static float ScreenShake { get => screenShake; set => Save("ScreenShake", ref screenShake, Mathf.Clamp01(value)); }
        public static bool MotionBlur { get => motionBlur; set => Save("MotionBlur", ref motionBlur, value); }
        public static bool ChromaticAberration { get => chromaticAberration; set => Save("ChromaticAberration", ref chromaticAberration, value); }
        public static bool Bloom { get => bloom; set => Save("Bloom", ref bloom, value); }

        // ---- Controls
        static string bindingOverrides = Load("BindingOverrides", "");
        static InputBufferMode inputBuffer = (InputBufferMode)Load("InputBuffer", (int)InputBufferMode.Standard);
        static float stickDeadzone = Load("StickDeadzone", 0.25f);

        /// <summary>Rebound keys and buttons, written by <see cref="PlayerInputReader.SaveOverrides"/> (empty = defaults).</summary>
        public static string BindingOverrides { get => bindingOverrides; set => Save("BindingOverrides", ref bindingOverrides, value ?? ""); }
        public static InputBufferMode InputBuffer { get => inputBuffer; set => SaveEnum("InputBuffer", ref inputBuffer, value); }
        public static float StickDeadzone { get => stickDeadzone; set => Save("StickDeadzone", ref stickDeadzone, Mathf.Clamp(value, 0.05f, 0.6f)); }

        /// <summary>Multiplier on the jump / dash / grapple buffer windows.</summary>
        public static float InputBufferScale => inputBuffer switch
        {
            InputBufferMode.Tight => 0.5f,
            InputBufferMode.Generous => 2f,
            _ => 1f,
        };

        // ---- Gameplay
        static Difficulty difficulty = (Difficulty)Load("Difficulty", (int)NeonRunner.Difficulty.Standard);
        static bool proximityWarning = Load("ProximityWarning", true);
        static ColorblindMode colorblind = (ColorblindMode)Load("Colorblind", (int)ColorblindMode.Off);

        public static Difficulty Difficulty { get => difficulty; set => SaveEnum("Difficulty", ref difficulty, value); }
        /// <summary>Screen edges flash red as a chaser closes in.</summary>
        public static bool ProximityWarning { get => proximityWarning; set => Save("ProximityWarning", ref proximityWarning, value); }
        public static ColorblindMode Colorblind { get => colorblind; set => SaveEnum("Colorblind", ref colorblind, value); }

        /// <summary>Chase speed of cops, drones and the Security Wall.</summary>
        public static float ChaserSpeedScale => difficulty switch
        {
            Difficulty.Casual => 0.8f,
            Difficulty.Nightmare => 1.2f,
            _ => 1f,
        };

        /// <summary>How long a cop needs to see you before giving chase.</summary>
        public static float ReactionTimeScale => difficulty switch
        {
            Difficulty.Casual => 1.6f,
            Difficulty.Nightmare => 0.6f,
            _ => 1f,
        };

        /// <summary>Cop hits survived per life (pits and lasers still kill).</summary>
        public static int ExtraHits => difficulty == Difficulty.Casual ? 2 : 0;

        public static string Describe(Difficulty d) => d switch
        {
            Difficulty.Casual => "Slower chasers, cops react late, survive 2 cop hits per life",
            Difficulty.Nightmare => "Fast chasers, cops react almost instantly",
            _ => "The intended experience",
        };

        public static void ResetToDefaults()
        {
            foreach (var key in new[]
            {
                "MasterVolume", "MusicVolume", "CompressedAudio",
                "Fullscreen", "FieldOfView", "ScreenShake", "MotionBlur", "ChromaticAberration", "Bloom",
                "BindingOverrides", "InputBuffer", "StickDeadzone", "ProximityWarning", "Colorblind",
            }) PlayerPrefs.DeleteKey(Prefix + key);

            masterVolume = 1f; musicVolume = 0.7f; compressedAudio = false;
            fullscreen = true; fieldOfView = DefaultFov; screenShake = 1f; motionBlur = chromaticAberration = bloom = true;
            bindingOverrides = ""; inputBuffer = InputBufferMode.Standard; stickDeadzone = 0.25f;
            proximityWarning = true; colorblind = ColorblindMode.Off;
            // Difficulty is chosen on the level select, so a settings reset leaves it alone.
            Changed?.Invoke();
        }

        public static void Flush() => PlayerPrefs.Save();

        static float Load(string key, float fallback) => PlayerPrefs.GetFloat(Prefix + key, fallback);
        static int Load(string key, int fallback) => PlayerPrefs.GetInt(Prefix + key, fallback);
        static bool Load(string key, bool fallback) => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) == 1;
        static string Load(string key, string fallback) => PlayerPrefs.GetString(Prefix + key, fallback);

        static void Save(string key, ref float field, float value)
        {
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat(Prefix + key, value);
            Changed?.Invoke();
        }

        static void Save(string key, ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
            Changed?.Invoke();
        }

        static void Save(string key, ref string field, string value)
        {
            if (field == value) return;
            field = value;
            PlayerPrefs.SetString(Prefix + key, value);
            Changed?.Invoke();
        }

        static void SaveEnum<T>(string key, ref T field, T value) where T : struct, Enum
        {
            if (field.Equals(value)) return;
            field = value;
            PlayerPrefs.SetInt(Prefix + key, Convert.ToInt32(value));
            Changed?.Invoke();
        }
    }
}
