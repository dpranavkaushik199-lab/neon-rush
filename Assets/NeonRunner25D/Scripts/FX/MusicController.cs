using UnityEngine;

namespace NeonRunner
{
    /// <summary>
    /// Plays the level music and pitch-shifts it on Sling-Boost. With no clip
    /// assigned it synthesises a placeholder synthwave loop so the effect is audible.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class MusicController : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] AudioClip music;
        [SerializeField, Range(0f, 1f)] float volume = 0.35f;
        [SerializeField] float boostPitch = 1.35f;
        [SerializeField] float perfectBoostPitch = 1.5f;
        [SerializeField] float pitchRecoverSpeed = 1.2f;
        [SerializeField] float chasePitch = 1.06f;
        [Tooltip("Seconds into the track to start, and to loop back to (skips the intro). Ignored for clips shorter than this.")]
        [SerializeField] float startTime = 40f;
        [Tooltip("Seconds to fade in from silence when the track starts and when it loops back.")]
        [SerializeField] float fadeInDuration = 3f;

        AudioSource source;
        float pitch = 1f;
        float lastTime;
        float fadeStart;

        /// <summary>0 → 1 over the fade, eased so it swells in rather than ramping.</summary>
        float Fade => fadeInDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - fadeStart) / fadeInDuration);

        float LoopStart => source.clip && source.clip.length > startTime ? startTime : 0f;

        float OutputVolume => volume * GameSettings.MusicVolume * Fade;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            source.clip = music ? music : BuildPlaceholderLoop();
        }

        void Start()
        {
            source.time = LoopStart;
            fadeStart = Time.unscaledTime;
            lastTime = source.time;
            source.Play();
        }

        // No player in the main menu: just the music.
        void OnEnable() { if (player) player.SlingBoosted += OnBoost; }
        void OnDisable() { if (player) player.SlingBoosted -= OnBoost; }

        void OnBoost(TimingGrade grade, Vector2 dir) => pitch = grade == TimingGrade.Perfect ? perfectBoostPitch : boostPitch;

        void Update()
        {
            float rest = CopBase.AnyChasing ? chasePitch : 1f;
            pitch = Mathf.MoveTowards(pitch, rest, pitchRecoverSpeed * Time.unscaledDeltaTime);
            source.pitch = pitch;
            source.volume = OutputVolume;

            // The AudioSource loops back to 0:00; jump past the intro again when it wraps.
            if (source.isPlaying && source.time < lastTime && source.time < LoopStart)
            {
                source.time = LoopStart;
                fadeStart = Time.unscaledTime;
            }
            lastTime = source.time;
        }

        static AudioClip BuildPlaceholderLoop()
        {
            const int rate = 44100;
            const float bpm = 112f;
            float step = 60f / bpm / 4f;          // 16th notes
            int steps = 64;                      // 4 bars
            int length = Mathf.RoundToInt(step * steps * rate);
            var data = new float[length];

            float[] bassLine = { 36, 36, 48, 36, 36, 36, 46, 36, 34, 34, 46, 34, 31, 31, 43, 33 };
            float[] arp = { 60, 63, 67, 70, 72, 70, 67, 63 };
            var rng = new System.Random(7);

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                int s = (int)(t / step);
                float inStep = t - s * step;
                float sample = 0f;

                // Saw bass, one note per 8th
                float bassNote = bassLine[(s / 2) % bassLine.Length];
                float bf = Midi(bassNote);
                float saw = 2f * (t * bf - Mathf.Floor(t * bf + 0.5f));
                sample += saw * 0.22f * Mathf.Exp(-inStep * 6f);

                // Square arpeggio
                float af = Midi(arp[s % arp.Length] + (s / 16 % 2 == 1 ? -2 : 0));
                float sq = Mathf.Sin(2f * Mathf.PI * af * t) > 0f ? 1f : -1f;
                sample += sq * 0.06f * Mathf.Exp(-inStep * 18f);

                // Kick on beats, hat on offbeats
                if (s % 4 == 0)
                {
                    float kf = 110f * Mathf.Exp(-inStep * 30f) + 40f;
                    sample += Mathf.Sin(2f * Mathf.PI * kf * inStep) * 0.5f * Mathf.Exp(-inStep * 12f);
                }
                if (s % 4 == 2) sample += ((float)rng.NextDouble() * 2f - 1f) * 0.08f * Mathf.Exp(-inStep * 60f);
                if (s % 8 == 4) sample += ((float)rng.NextDouble() * 2f - 1f) * 0.18f * Mathf.Exp(-inStep * 20f);

                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            var clip = AudioClip.Create("PlaceholderSynthwave", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Midi(float note) => 440f * Mathf.Pow(2f, (note - 69f) / 12f);
    }
}
