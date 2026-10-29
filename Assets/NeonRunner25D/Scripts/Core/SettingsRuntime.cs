using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace NeonRunner
{
    /// <summary>
    /// Applies <see cref="GameSettings"/> to whatever scene is loaded: master volume, the
    /// compressor, post-processing toggles and the colourblind filter.
    /// Creates itself before the first scene, so no scene needs to reference it.
    /// </summary>
    public class SettingsRuntime : MonoBehaviour
    {
        // Each effect's state as the scene set it up (after the low-end trims), so turning a
        // setting back on restores the scene's choice instead of forcing the effect on.
        readonly Dictionary<VolumeComponent, bool> sceneDefaults = new Dictionary<VolumeComponent, bool>();
        ChannelMixer colorblindMixer;
        Volume colorblindVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            var go = new GameObject("[Settings]");
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsRuntime>();
            ApplyResolution();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameSettings.Changed += Apply;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameSettings.Changed -= Apply;
        }

        // sceneLoaded doesn't always fire for the first scene (e.g. editor play mode without a scene reload).
        void Start() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Additive);

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) sceneDefaults.Clear();

            foreach (var listener in FindObjectsByType<AudioListener>())
            {
                if (listener.GetComponent<DynamicRangeCompressor>()) continue;
                var host = listener.gameObject;
                // An audio filter script can't share a GameObject with an AudioSource (the camera
                // plays the music), so the listener moves to a child at the same spot.
                if (host.GetComponent<AudioSource>())
                {
                    var child = new GameObject("Listener");
                    child.transform.SetParent(host.transform, false);
                    DestroyImmediate(listener);
                    child.AddComponent<AudioListener>();
                    host = child;
                }
                host.AddComponent<DynamicRangeCompressor>();
            }

            Apply();
        }

        void Apply()
        {
            AudioListener.volume = GameSettings.MasterVolume;

            foreach (var volume in FindObjectsByType<Volume>())
            {
                if (volume == colorblindVolume || !volume.HasInstantiatedProfile() && !volume.sharedProfile) continue;
                // .profile makes a per-scene copy, so the shared profile assets are never edited.
                var profile = volume.profile;
                Toggle<MotionBlur>(profile, GameSettings.MotionBlur);
                Toggle<ChromaticAberration>(profile, GameSettings.ChromaticAberration);
                Toggle<LensDistortion>(profile, GameSettings.ChromaticAberration);
                Toggle<Bloom>(profile, GameSettings.Bloom);
            }

            ApplyColorblind();
        }

        void Toggle<T>(VolumeProfile profile, bool enabled) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var effect)) return;
            if (!sceneDefaults.TryGetValue(effect, out bool sceneDefault))
                sceneDefaults[effect] = sceneDefault = effect.active;
            effect.active = sceneDefault && enabled;
        }

        /// <summary>
        /// Daltonization through a URP Channel Mixer: C = I + M·(I − S), where S simulates the
        /// colour vision deficiency and M moves the colour information it loses into channels
        /// the player can still tell apart.
        /// </summary>
        void ApplyColorblind()
        {
            var mode = GameSettings.Colorblind;
            if (!colorblindVolume)
            {
                if (mode == ColorblindMode.Off) return;
                var go = new GameObject("Colorblind Filter");
                DontDestroyOnLoad(go);
                colorblindVolume = go.AddComponent<Volume>();
                colorblindVolume.isGlobal = true;
                colorblindVolume.priority = 100f;
                colorblindVolume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                colorblindMixer = colorblindVolume.sharedProfile.Add<ChannelMixer>(true);
            }
            colorblindVolume.weight = mode == ColorblindMode.Off ? 0f : 1f;
            if (mode == ColorblindMode.Off) return;

            float[,] sim = mode switch
            {
                ColorblindMode.Protanopia => new[,] { { 0.567f, 0.433f, 0f }, { 0.558f, 0.442f, 0f }, { 0f, 0.242f, 0.758f } },
                ColorblindMode.Deuteranopia => new[,] { { 0.625f, 0.375f, 0f }, { 0.7f, 0.3f, 0f }, { 0f, 0.3f, 0.7f } },
                _ => new[,] { { 0.95f, 0.05f, 0f }, { 0f, 0.433f, 0.567f }, { 0f, 0.475f, 0.525f } },
            };
            float[,] shift = { { 0f, 0f, 0f }, { 0.7f, 1f, 0f }, { 0.7f, 0f, 1f } };
            var c = new float[3, 3];
            for (int r = 0; r < 3; r++)
            for (int col = 0; col < 3; col++)
            {
                float sum = 0f;
                for (int k = 0; k < 3; k++) sum += shift[r, k] * ((k == col ? 1f : 0f) - sim[k, col]);
                c[r, col] = (r == col ? 1f : 0f) + sum;
            }

            // Channel Mixer takes percentages.
            var m = colorblindMixer;
            m.redOutRedIn.Override(c[0, 0] * 100f); m.redOutGreenIn.Override(c[0, 1] * 100f); m.redOutBlueIn.Override(c[0, 2] * 100f);
            m.greenOutRedIn.Override(c[1, 0] * 100f); m.greenOutGreenIn.Override(c[1, 1] * 100f); m.greenOutBlueIn.Override(c[1, 2] * 100f);
            m.blueOutRedIn.Override(c[2, 0] * 100f); m.blueOutGreenIn.Override(c[2, 1] * 100f); m.blueOutBlueIn.Override(c[2, 2] * 100f);
        }

        public static void ApplyResolution()
        {
            var mode = GameSettings.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var size = GameSettings.Resolution;
            if (size.x > 0 && size.y > 0) Screen.SetResolution(size.x, size.y, mode);
            else Screen.fullScreenMode = mode;
        }
    }
}
