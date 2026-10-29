using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonRunner
{
    /// <summary>
    /// Picks a quality tier once per install from the hardware (low-end = the "Mobile"
    /// quality level: lighter URP asset, half-res textures, no soft shadows) and trims
    /// the expensive extras on that tier: depth of field, film grain, motion blur and
    /// SMAA are swapped out, and effects spawn fewer particles. F9 toggles the tier.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PerformanceManager : MonoBehaviour
    {
        const string PrefKey = "NeonRunner.QualityTier";
        const string LowTierName = "Mobile", HighTierName = "PC";

        [SerializeField] Volume globalVolume;
        [SerializeField] Volume boostVolume;
        [SerializeField] Camera cam;

        static bool tierChosen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => tierChosen = false;

        // The quality list differs per platform (and in the editor's play mode), so tiers are found by name.
        public static bool LowEnd => QualitySettings.names[QualitySettings.GetQualityLevel()] == LowTierName;

        static void SetTier(bool low)
        {
            int index = System.Array.IndexOf(QualitySettings.names, low ? LowTierName : HighTierName);
            if (index >= 0 && index != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(index, true);
        }

        void Awake()
        {
            if (!tierChosen)
            {
                tierChosen = true;
                SetTier(PlayerPrefs.HasKey(PrefKey) ? PlayerPrefs.GetInt(PrefKey) == 1 : DetectLowEnd());
            }
            if (LowEnd) ApplyLowEnd();
        }

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current is { f9Key: { wasPressedThisFrame: true } })
            {
                bool low = !LowEnd;
                PlayerPrefs.SetInt(PrefKey, low ? 1 : 0);
                SetTier(low);
                GameManager.Instance?.ShowPopup(LowEnd ? "PERFORMANCE MODE" : "QUALITY MODE", Theme.Accent, 1.2f);
                // Volume and camera trims are per-scene; reload so everything is rebuilt for the tier.
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
        }

        /// <summary>Integrated / older GPUs, little memory or few cores get the light tier.</summary>
        static bool DetectLowEnd()
        {
            bool weakGpu = SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize < 3000;
            bool integrated = SystemInfo.graphicsDeviceName.Contains("Intel") && !SystemInfo.graphicsDeviceName.Contains("Arc");
            bool lowRam = SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 8000;
            bool fewCores = SystemInfo.processorCount <= 4;
            return weakGpu || integrated || lowRam || fewCores || Application.isMobilePlatform;
        }

        void ApplyLowEnd()
        {
            // .profile instantiates a per-scene copy, so the shared assets are never touched.
            if (globalVolume)
            {
                var p = globalVolume.profile;
                if (p.TryGet<DepthOfField>(out var dof)) dof.active = false;
                if (p.TryGet<FilmGrain>(out var grain)) grain.active = false;
                if (p.TryGet<MotionBlur>(out var blur)) blur.active = false;
                if (p.TryGet<Bloom>(out var bloom)) bloom.highQualityFiltering.Override(false);
            }
            if (boostVolume && boostVolume.profile.TryGet<MotionBlur>(out var boostBlur))
                boostBlur.quality.Override(MotionBlurQuality.Low);

            if (cam && cam.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            // Decorative lights don't need to shadow on this tier; the moon still does.
            foreach (var l in FindObjectsByType<Light>())
                if (l.type != LightType.Directional) l.shadows = LightShadows.None;
            foreach (var probe in FindObjectsByType<ReflectionProbe>())
                probe.resolution = 64;
        }
    }
}
