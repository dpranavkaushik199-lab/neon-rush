using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonRunner
{
    /// <summary>
    /// The main menu's backdrop: a cluster of glowing nodes joined by neon filaments that
    /// reacts to the music, wrapped around an NCS-style circular spectrum.
    ///  - Bass (kicks): the whole network beats (scale), the glow spikes, the core thumps,
    ///    the camera jolts and the void flashes red.
    ///  - Mids (synths, vocals): bright pulses race along the filaments.
    ///  - Highs (snares, hats): random nodes flash like firing neurons.
    ///  - Loudness: the cluster spreads out and spins faster; quiet passages just breathe.
    ///  - Full spectrum: the ring's mirrored bars, and the web bulges outward to match their shape.
    /// Everything is one dynamic mesh of camera-facing quads drawn with an additive HDR shader,
    /// so bloom does the glow. A second low-res camera feeds <see cref="NeonGUI.Glass"/> so the
    /// menu panels read as frosted glass over the network.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NeuralNetworkBackdrop : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] Material material;
        [SerializeField] int nodeCount = 240;
        [SerializeField] float radius = 4.2f;
        [SerializeField] int neighbours = 3;
        [SerializeField] float cameraDistance = 10.5f;
        [Tooltip("Where the cluster sits: pushed right so the title column on the left stays readable.")]
        [SerializeField] Vector3 center = new Vector3(3.6f, 0.6f, 0f);

        [Header("Spectrum ring (NCS-style)")]
        [Tooltip("Bars per half; the ring is mirrored left/right with the bass at the top.")]
        [SerializeField] int spectrumBars = 48;
        [SerializeField] float ringRadius = 1.55f;
        [SerializeField] float maxBarLength = 1.3f;
        [SerializeField] int moteCount = 260;

        static readonly Color Pink = new Color(1f, 0.36f, 0.72f);
        static readonly Color White = new Color(0.94f, 0.94f, 0.97f);
        static readonly Color Red = new Color(1f, 0.16f, 0.22f);

        struct Pulse { public int edge; public float t, speed; public bool reverse; public Color color; }
        struct Mote { public Vector3 pos, dir; public float speed, size; public Color color; }

        float[] bars, barCaps, rawBars;
        float spectrumMax = 1e-7f;
        Mote[] motes;
        float moteSpeed;
        Color baseBackground;

        Vector3[] basePos, nodePos;
        Color[] nodeColor;
        float[] nodeFlash, nodeSize, nodePhase;
        readonly List<Vector2Int> edges = new List<Vector2Int>();
        readonly List<Pulse> pulses = new List<Pulse>();

        Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>(), glow = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();

        // Audio
        AudioSource music;
        readonly float[] spectrum = new float[1024];
        readonly float[] samples = new float[1024];
        float bassPeak = 1e-7f, midPeak = 1e-7f, highPeak = 1e-7f, loudPeak = 1e-7f;
        float bassAvg, lastBeat = -1f;
        public float Bass { get; private set; }
        public float Mid { get; private set; }
        public float High { get; private set; }
        public float Loudness { get; private set; }
        /// <summary>1 on a detected kick, decaying to 0.</summary>
        public float Beat { get; private set; }

        float spin, spinX, pulseBudget, flashBudget;

        // Glass
        Camera glassCam;
        RenderTexture glassRT;

        void Awake() => Build();

        /// <summary>Lays out the nodes and their links, and creates the mesh and its material.</summary>
        void Build()
        {
            edges.Clear();
            pulses.Clear();
            var rng = new System.Random(4242);
            float R() => (float)rng.NextDouble();

            basePos = new Vector3[nodeCount];
            nodePos = new Vector3[nodeCount];
            nodeColor = new Color[nodeCount];
            nodeFlash = new float[nodeCount];
            nodeSize = new float[nodeCount];
            nodePhase = new float[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                // Fibonacci shell, jittered inward so it reads as a dense cluster rather than a ball.
                // The middle stays hollow: that's where the spectrum ring and its core sit.
                float y = 1f - 2f * (i + 0.5f) / nodeCount;
                float ring = Mathf.Sqrt(1f - y * y);
                float a = i * 2.39996323f;
                var dir = new Vector3(Mathf.Cos(a) * ring, y, Mathf.Sin(a) * ring);
                float depth = Mathf.Lerp(0.66f, 1.1f, Mathf.Pow(R(), 0.6f));
                basePos[i] = dir * radius * depth + new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 0.6f;
                float pick = R();
                nodeColor[i] = pick < 0.62f ? Pink : pick < 0.95f ? White : Red;
                nodeSize[i] = Mathf.Lerp(0.09f, 0.2f, R() * R());
                nodePhase[i] = R() * 100f;
            }

            // Each node links to its nearest few: a web, not a hairball.
            var seen = new HashSet<long>();
            var nearest = new List<(float d, int j)>();
            for (int i = 0; i < nodeCount; i++)
            {
                nearest.Clear();
                for (int j = 0; j < nodeCount; j++)
                    if (j != i) nearest.Add(((basePos[i] - basePos[j]).sqrMagnitude, j));
                nearest.Sort((p, q) => p.d.CompareTo(q.d));
                for (int k = 0; k < neighbours && k < nearest.Count; k++)
                {
                    int j = nearest[k].j;
                    long key = Mathf.Min(i, j) * 100000L + Mathf.Max(i, j);
                    if (seen.Add(key)) edges.Add(new Vector2Int(i, j));
                }
            }

            bars = new float[spectrumBars];
            barCaps = new float[spectrumBars];
            rawBars = new float[spectrumBars];
            motes = new Mote[moteCount];
            for (int i = 0; i < moteCount; i++) SpawnMote(ref motes[i], Random.Range(0.5f, 12f));

            mesh = new Mesh { name = "NeuralNetwork" };
            mesh.MarkDynamic();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            // A per-instance copy, so the generated glow texture never ends up in the material asset.
            if (material) mr.sharedMaterial = new Material(material) { mainTexture = GlowTexture() };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void Start()
        {
            var controller = FindAnyObjectByType<MusicController>();
            if (controller) music = controller.GetComponent<AudioSource>();
            if (cam) baseBackground = cam.backgroundColor;
            CreateGlassCamera();
        }

        void OnDisable()
        {
            if (blurOut.Length > 1 && NeonGUI.Glass == blurOut[1]) NeonGUI.Glass = null;
        }

        void OnDestroy()
        {
            ReleaseGlass();
            if (mesh) Destroy(mesh);
        }

        static Texture2D GlowTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                // Hot core plus a soft halo.
                float a = Mathf.Clamp01(Mathf.Exp(-d * d * 18f) + 0.35f * Mathf.Exp(-d * d * 4f) - 0.02f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }

        // ===================================================================== Audio

        void Analyse(float dt)
        {
            // A muted track is skipped by the audio engine, so its spectrum is stale: treat it as silence (idle breathing).
            if (!music || !music.isPlaying || music.volume < 0.002f)
            {
                Bass = Mathf.MoveTowards(Bass, 0f, dt);
                Mid = Mathf.MoveTowards(Mid, 0f, dt);
                High = Mathf.MoveTowards(High, 0f, dt);
                Loudness = Mathf.MoveTowards(Loudness, 0f, dt);
                Beat = Mathf.Max(0f, Beat - dt * 4f);
                for (int i = 0; i < bars.Length; i++)
                {
                    bars[i] = Mathf.MoveTowards(bars[i], 0f, dt * 1.5f);
                    barCaps[i] = Mathf.MoveTowards(barCaps[i], 0f, dt * 0.6f);
                }
                return;
            }

            music.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
            music.GetOutputData(samples, 0);
            float binHz = AudioSettings.outputSampleRate * 0.5f / spectrum.Length;
            float Band(float lo, float hi)
            {
                int a = Mathf.Clamp(Mathf.FloorToInt(lo / binHz), 0, spectrum.Length - 1);
                int b = Mathf.Clamp(Mathf.CeilToInt(hi / binHz), a + 1, spectrum.Length);
                float sum = 0f;
                for (int i = a; i < b; i++) sum += spectrum[i];
                return sum / (b - a);
            }
            float rms = 0f;
            foreach (var x in samples) rms += x * x;
            rms = Mathf.Sqrt(rms / samples.Length);

            // Each band is judged against its own recent peak, so the volume sliders don't change the show.
            float Normalise(float v, ref float peak)
            {
                peak = Mathf.Max(v, peak * Mathf.Exp(-dt * 0.25f), 1e-7f);
                return Mathf.Clamp01(v / peak);
            }
            float bass = Band(25f, 140f), mid = Band(400f, 3000f), high = Band(5000f, 14000f);
            float Smooth(float current, float target, float attack, float release) =>
                Mathf.Lerp(current, target, 1f - Mathf.Exp(-(target > current ? attack : release) * dt));
            Bass = Smooth(Bass, Normalise(bass, ref bassPeak), 30f, 6f);
            Mid = Smooth(Mid, Normalise(mid, ref midPeak), 20f, 5f);
            High = Smooth(High, Normalise(high, ref highPeak), 40f, 8f);
            Loudness = Smooth(Loudness, Normalise(rms, ref loudPeak), 4f, 1.5f);

            // Kick detection: bass jumping well above its running average.
            bassAvg = Mathf.Lerp(bassAvg, bass, 1f - Mathf.Exp(-dt * 3f));
            if (bass > bassAvg * 1.45f && bass > bassPeak * 0.35f && Time.unscaledTime - lastBeat > 0.2f)
            {
                lastBeat = Time.unscaledTime;
                Beat = 1f;
            }
            else Beat = Mathf.Max(0f, Beat - dt * 5f);

            UpdateSpectrumBars(binHz, dt);
        }

        /// <summary>
        /// NCS-style bars: log-spaced bands from 40 Hz to 16 kHz, tilted up toward the treble (music
        /// has far less energy up there), softened across neighbours, judged against one shared peak
        /// so the ring keeps the track's real shape. They shoot up instantly and fall back smoothly,
        /// with peak caps that hang and then drift down.
        /// </summary>
        void UpdateSpectrumBars(float binHz, float dt)
        {
            int n = bars.Length;
            float frameMax = 1e-9f;
            for (int i = 0; i < n; i++)
            {
                float lo = 40f * Mathf.Pow(16000f / 40f, i / (float)n);
                float hi = 40f * Mathf.Pow(16000f / 40f, (i + 1f) / n);
                int a = Mathf.Clamp(Mathf.FloorToInt(lo / binHz), 0, spectrum.Length - 1);
                int b = Mathf.Clamp(Mathf.CeilToInt(hi / binHz), a + 1, spectrum.Length);
                float peak = 0f;
                for (int k = a; k < b; k++) peak = Mathf.Max(peak, spectrum[k]);
                rawBars[i] = Mathf.Sqrt(peak) * (1f + 3f * Mathf.Pow(i / (float)n, 1.3f));
                frameMax = Mathf.Max(frameMax, rawBars[i]);
            }
            spectrumMax = Mathf.Max(frameMax, spectrumMax * Mathf.Exp(-dt * 0.3f), 1e-7f);

            for (int i = 0; i < n; i++)
            {
                float smoothed = (rawBars[Mathf.Max(0, i - 1)] + rawBars[i] * 2f + rawBars[Mathf.Min(n - 1, i + 1)]) * 0.25f;
                float target = Mathf.Clamp01(smoothed / spectrumMax);
                bars[i] = target > bars[i] ? target : Mathf.MoveTowards(bars[i], target, dt * 2.2f);
                barCaps[i] = bars[i] >= barCaps[i] ? bars[i] : Mathf.MoveTowards(barCaps[i], bars[i], dt * 0.5f);
            }
        }

        /// <summary>Spectrum level at a direction around the ring: 0 = top (bass) to 1 = bottom (treble), mirrored.</summary>
        float SpectrumAt(float x, float y)
        {
            float u = Mathf.Abs(Mathf.Atan2(x, y)) / Mathf.PI;
            return bars[Mathf.Clamp(Mathf.RoundToInt(u * (bars.Length - 1)), 0, bars.Length - 1)];
        }

        void SpawnMote(ref Mote m, float startDistance)
        {
            var dir = Random.onUnitSphere;
            // Mostly outward across the screen, some toward the viewer, like dust rushing past.
            dir.z = dir.z * 0.6f - 0.25f;
            dir.Normalize();
            m.dir = dir;
            m.pos = center + dir * startDistance;
            m.speed = Random.Range(0.6f, 1.4f);
            m.size = Random.Range(0.012f, 0.03f);
            float pick = Random.value;
            m.color = pick < 0.7f ? Pink : pick < 0.95f ? White : Red;
        }

        // ===================================================================== Simulation and mesh

        void LateUpdate()
        {
            // A script reload in play mode wipes runtime state without calling Awake again.
            if (edges.Count == 0 || !mesh) Build();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f), t = Time.unscaledTime;
            Analyse(dt);

            // Chaos: loudness spreads the cluster and spins it faster; a kick punches the scale.
            spin += dt * (4f + 22f * Loudness);
            spinX = Mathf.Sin(t * 0.11f) * 14f;
            float breathe = 1f + 0.025f * Mathf.Sin(t * 0.9f);
            float scale = breathe * (1f + 0.09f * Beat + 0.12f * Loudness);
            var rot = Quaternion.Euler(spinX, spin, Mathf.Sin(t * 0.07f) * 6f);
            float drift = 0.06f + 0.35f * Loudness;
            MoveCamera(t);
            Vector3 camRight = cam ? cam.transform.right : Vector3.right, camUp = cam ? cam.transform.up : Vector3.up;
            for (int i = 0; i < nodeCount; i++)
            {
                float p = nodePhase[i];
                var wobble = new Vector3(Mathf.Sin(t * 0.7f + p), Mathf.Sin(t * 0.53f + p * 1.3f), Mathf.Sin(t * 0.61f + p * 0.7f)) * drift;
                var offset = rot * (basePos[i] * scale + wobble);
                // The web takes the spectrum's shape: each node is pushed out by the bar at its angle on screen.
                float push = SpectrumAt(Vector3.Dot(offset, camRight), Vector3.Dot(offset, camUp));
                nodePos[i] = center + offset * (1f + 0.22f * push);
                nodeFlash[i] = Mathf.Max(0f, nodeFlash[i] - dt * 6f);
            }

            // Dust streams out of the core; kicks and loud passages turn it into warp streaks.
            moteSpeed = Mathf.Lerp(moteSpeed, 0.35f + 7f * Bass * Bass + 9f * Beat + 2f * Loudness, 1f - Mathf.Exp(-dt * 8f));
            for (int i = 0; i < motes.Length; i++)
            {
                motes[i].pos += motes[i].dir * (motes[i].speed * moteSpeed * dt);
                if ((motes[i].pos - center).sqrMagnitude > 14f * 14f) SpawnMote(ref motes[i], ringRadius * 0.5f);
            }

            // Kicks wash the void red for a moment.
            if (cam) cam.backgroundColor = Color.Lerp(baseBackground, new Color(0.13f, 0.02f, 0.04f), Beat * 0.7f);

            // Neural spikes: highs fire random nodes.
            flashBudget += dt * (1.5f + 70f * High * High);
            while (flashBudget >= 1f)
            {
                flashBudget -= 1f;
                nodeFlash[Random.Range(0, nodeCount)] = 1f;
            }

            // Data flow: mids launch pulses along the filaments.
            pulseBudget += dt * (2f + 90f * Mid * Mid) + Beat * 0.6f;
            while (pulseBudget >= 1f && pulses.Count < 260)
            {
                pulseBudget -= 1f;
                float pick = Random.value;
                pulses.Add(new Pulse
                {
                    edge = Random.Range(0, edges.Count),
                    speed = Random.Range(1.6f, 3.2f) * (0.8f + Mid),
                    reverse = Random.value < 0.5f,
                    color = pick < 0.6f ? Pink : pick < 0.92f ? White : Red,
                });
            }
            pulseBudget = Mathf.Min(pulseBudget, 1f);
            for (int i = pulses.Count - 1; i >= 0; i--)
            {
                var pulse = pulses[i];
                pulse.t += dt * pulse.speed;
                if (pulse.t >= 1f)
                {
                    // Arriving lights up the target node, so flows read as signals between neurons.
                    var e = edges[pulse.edge];
                    nodeFlash[pulse.reverse ? e.x : e.y] = Mathf.Max(nodeFlash[pulse.reverse ? e.x : e.y], 0.6f);
                    pulses.RemoveAt(i);
                }
                else pulses[i] = pulse;
            }

            BuildMesh();
            UpdateGlass();
        }

        void MoveCamera(float t)
        {
            if (!cam) return;
            // Lazy orbit plus a handheld float, always looking just past the centre.
            float yaw = Mathf.Sin(t * 0.05f) * 18f, pitch = Mathf.Sin(t * 0.037f) * 7f;
            var hand = new Vector3(Mathf.PerlinNoise(t * 0.25f, 0f) - 0.5f, Mathf.PerlinNoise(0f, t * 0.25f) - 0.5f, 0f) * 0.35f;
            // Drop shake: a sharp jolt on each kick, scaled by the player's Screen Shake setting.
            float jolt = Beat * Beat * 0.22f * GameSettings.ScreenShake;
            var shake = new Vector3(Mathf.PerlinNoise(t * 31f, 1f) - 0.5f, Mathf.PerlinNoise(2f, t * 31f) - 0.5f, 0f) * jolt;
            cam.transform.position = Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -cameraDistance * (1f - 0.03f * Beat)) + hand + shake;
            float roll = (Mathf.PerlinNoise(t * 0.1f, 5f) - 0.5f) * 4f + (Mathf.PerlinNoise(t * 29f, 9f) - 0.5f) * jolt * 6f;
            cam.transform.rotation = Quaternion.LookRotation(-cam.transform.position + hand * 0.5f, Quaternion.Euler(0f, 0f, roll) * Vector3.up);
        }

        void Quad(Vector3 center, Vector3 right, Vector3 up, Color color, float intensity)
        {
            int v = verts.Count;
            verts.Add(center - right - up); verts.Add(center - right + up); verts.Add(center + right + up); verts.Add(center + right - up);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
            for (int k = 0; k < 4; k++) { colors.Add(color); glow.Add(new Vector2(intensity, 0f)); }
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2); tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }

        /// <summary>A camera-facing ribbon from a to b; the glow texture's cross-section gives it a soft core.</summary>
        void Ribbon(Vector3 a, Vector3 b, Vector3 view, float width, Color ca, Color cb, float intensity)
        {
            var side = Vector3.Cross(b - a, view).normalized * width;
            int v = verts.Count;
            verts.Add(a - side); verts.Add(a + side); verts.Add(b + side); verts.Add(b - side);
            uvs.Add(new Vector2(0f, 0.5f)); uvs.Add(new Vector2(1f, 0.5f)); uvs.Add(new Vector2(1f, 0.5f)); uvs.Add(new Vector2(0f, 0.5f));
            colors.Add(ca); colors.Add(ca); colors.Add(cb); colors.Add(cb);
            for (int k = 0; k < 4; k++) glow.Add(new Vector2(intensity, 0f));
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2); tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }

        void BuildMesh()
        {
            verts.Clear(); uvs.Clear(); glow.Clear(); colors.Clear(); tris.Clear();
            var camT = cam ? cam.transform : transform;
            Vector3 right = camT.right, up = camT.up, view = camT.forward;
            float t = Time.unscaledTime;
            float heartbeat = 1f + 1.6f * Beat + 0.5f * Bass;

            // Filaments: faint, pulsing gently along their length with the bass.
            float edgeGlow = (0.55f + 0.6f * Bass) * heartbeat;
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                float shimmer = 0.75f + 0.25f * Mathf.Sin(t * 2.3f + i * 0.37f);
                var ca = nodeColor[e.x] * 0.55f; ca.a = 0.5f;
                var cb = nodeColor[e.y] * 0.55f; cb.a = 0.5f;
                Ribbon(nodePos[e.x], nodePos[e.y], view, 0.018f, ca, cb, edgeGlow * shimmer);
            }

            // Pulses: bright beads with a short tail.
            foreach (var pulse in pulses)
            {
                var e = edges[pulse.edge];
                Vector3 from = nodePos[pulse.reverse ? e.y : e.x], to = nodePos[pulse.reverse ? e.x : e.y];
                var head = Vector3.Lerp(from, to, pulse.t);
                var tail = Vector3.Lerp(from, to, Mathf.Max(0f, pulse.t - 0.22f));
                Ribbon(tail, head, view, 0.035f, new Color(pulse.color.r, pulse.color.g, pulse.color.b, 0f), pulse.color, 3.2f);
                Quad(head, right * 0.09f, up * 0.09f, pulse.color, 4.5f);
            }

            // Nodes: hot cores; flashes overdrive them past white into bloom.
            for (int i = 0; i < nodeCount; i++)
            {
                float flash = nodeFlash[i];
                float size = nodeSize[i] * (1f + 0.35f * Beat + 1.2f * flash);
                var color = Color.Lerp(nodeColor[i], Color.white, flash * 0.6f);
                Quad(nodePos[i], right * size, up * size, color, (1.9f + 3f * Bass) * heartbeat * 0.8f + flash * 10f);
            }

            BuildVisualiser(right, up, view, t);

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, glow);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(center, Vector3.one * 32f);
        }

        /// <summary>
        /// The NCS-style construct at the heart of the web: a mirrored circular spectrum (bass at the
        /// top, treble at the bottom) with hanging peak caps, a thin base ring, a glowing core with a
        /// spinning hexagon frame that thumps on every kick, and dust streaking out past the camera.
        /// </summary>
        void BuildVisualiser(Vector3 right, Vector3 up, Vector3 view, float t)
        {
            int n = bars.Length;
            float ring = ringRadius * (1f + 0.14f * Beat + 0.05f * Bass);
            float kickWhite = Beat * 0.55f;

            // Base ring.
            const int segments = 96;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 p0 = center + (right * Mathf.Sin(a0) + up * Mathf.Cos(a0)) * (ring - 0.06f);
                Vector3 p1 = center + (right * Mathf.Sin(a1) + up * Mathf.Cos(a1)) * (ring - 0.06f);
                Ribbon(p0, p1, view, 0.022f, Pink, Pink, 1.4f + 3f * Beat);
            }

            // Bars and caps, mirrored left and right.
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                float theta = u * Mathf.PI;
                var color = Color.Lerp(Color.Lerp(Pink, White, u), Color.white, kickWhite);
                float length = 0.03f + bars[i] * maxBarLength;
                float cap = 0.03f + barCaps[i] * maxBarLength + 0.07f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var dir = right * (Mathf.Sin(theta) * side) + up * Mathf.Cos(theta);
                    var inner = center + dir * ring;
                    Ribbon(inner, inner + dir * length, view, 0.045f, color, color, 2.2f + 4f * bars[i]);
                    Quad(center + dir * (ring + cap), right * 0.03f, up * 0.03f, Red, 2.5f);
                }
            }

            // Core: a hot orb inside a spinning hexagon frame.
            float coreSize = 0.75f * (1f + 0.45f * Beat + 0.2f * Bass);
            Quad(center, right * coreSize, up * coreSize, Color.Lerp(Pink, Color.white, 0.35f + kickWhite), 1.2f + 6f * Beat + 1.5f * Bass);
            float hex = ring * 0.62f, spinHex = t * (0.4f + 2f * Loudness);
            for (int k = 0; k < 6; k++)
            {
                float a0 = spinHex + k * Mathf.PI / 3f, a1 = spinHex + (k + 1) * Mathf.PI / 3f;
                Vector3 p0 = center + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * hex;
                Vector3 p1 = center + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * hex;
                Ribbon(p0, p1, view, 0.03f, Red, Red, 2f + 5f * Beat);
            }

            // Dust: dots when calm, streaks when the music drives them.
            float streak = Mathf.Clamp(moteSpeed * 0.05f, 0.02f, 0.9f);
            foreach (var m in motes)
            {
                float fade = Mathf.Clamp01(((m.pos - center).magnitude - ringRadius * 0.5f) / 1.5f);
                var tail = m.pos - m.dir * streak * m.speed;
                var c = m.color;
                Ribbon(tail, m.pos, view, m.size, new Color(c.r, c.g, c.b, 0f), new Color(c.r, c.g, c.b, fade), 2.4f + 2f * Beat);
            }
        }

        // ===================================================================== Glass

        void CreateGlassCamera()
        {
            if (!cam) return;
            var go = new GameObject("GlassCamera");
            go.transform.SetParent(cam.transform, false);
            glassCam = go.AddComponent<Camera>();
            glassCam.CopyFrom(cam);
            glassCam.depth = cam.depth - 1;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
        }

        /// <summary>
        /// Frosted glass: the glass camera renders a quarter-res view, which is halved three times and
        /// then doubled back up twice with bilinear filtering. Each step averages neighbours, so the
        /// result is a smooth, round blur (one big magnification would leave square blobs).
        /// </summary>
        void UpdateGlass()
        {
            if (!glassCam) return;
            int w = Mathf.Max(32, Screen.width / 4), h = Mathf.Max(32, Screen.height / 4);
            if (!glassRT || glassRT.width != w || glassRT.height != h)
            {
                ReleaseGlass();
                glassRT = NewRT(w, h, 16, "MenuGlassSource");
                glassCam.targetTexture = glassRT;
                for (int i = 0; i < blurChain.Length; i++) blurChain[i] = NewRT(w >> (i + 1), h >> (i + 1), 0, "MenuGlassBlur" + i);
                blurOut = new[] { NewRT(w >> 2, h >> 2, 0, "MenuGlassUp1"), NewRT(w >> 1, h >> 1, 0, "MenuGlassUp0") };
            }
            glassCam.fieldOfView = cam.fieldOfView;
            glassCam.backgroundColor = cam.backgroundColor;

            // Last frame's render (the camera draws after LateUpdate): down 1/8, 1/16, 1/32, then up 1/16, 1/8.
            Graphics.Blit(glassRT, blurChain[0]);
            Graphics.Blit(blurChain[0], blurChain[1]);
            Graphics.Blit(blurChain[1], blurChain[2]);
            Graphics.Blit(blurChain[2], blurOut[0]);
            Graphics.Blit(blurOut[0], blurOut[1]);
            NeonGUI.Glass = blurOut[1];
        }

        readonly RenderTexture[] blurChain = new RenderTexture[3];
        RenderTexture[] blurOut = new RenderTexture[0];

        static RenderTexture NewRT(int w, int h, int depth, string name) =>
            new RenderTexture(Mathf.Max(4, w), Mathf.Max(4, h), depth, RenderTextureFormat.ARGB32)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name };

        void ReleaseGlass()
        {
            if (glassRT) glassRT.Release();
            for (int i = 0; i < blurChain.Length; i++) if (blurChain[i]) blurChain[i].Release();
            foreach (var rt in blurOut) if (rt) rt.Release();
        }
    }
}
