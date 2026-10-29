using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeonRunner;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Realistic presentation for the 2.5D builder: Poly Haven PBR surfaces (CC0) mapped in
/// real-world units, reflective office windows, a concrete skyline, sodium street lamps,
/// reflection probes and Microsoft Rocketbox characters (MIT) with their own animations.
/// </summary>
public static partial class NeonRunner25DBuilder
{
    const string PolyHavenDir = Root + "/ThirdParty/PolyHaven";
    const string RocketboxDir = Root + "/ThirdParty/Rocketbox";
    const string ThiefAvatar = "Male_Adult_18";
    const string CopAvatar = "Police_Male_03";

    static Material wallPBR, roofPBR, rustPBR, platePBR, corrugatedPBR, towerPBR, windowsMat, hdriSky;
    static RuntimeAnimatorController humanController;
    static readonly List<Rect> roofBlocks = new List<Rect>();

    // ---------------------------------------------------------------- Materials

    static void CreateRealisticMaterials()
    {
        wallPBR = PbrMaterial("PBR_Wall", "precast_concrete_wall", 3f, 0f, 1f, new Color(0.62f, 0.62f, 0.64f));
        // Rooftops are rain-soaked: darker and much glossier than the dry scan.
        roofPBR = PbrMaterial("PBR_WetRoof", "concrete_floor_worn_001", 2.5f, 0f, 1.9f, new Color(0.42f, 0.43f, 0.45f));
        rustPBR = PbrMaterial("PBR_Rust", "rusty_metal_02", 1.5f, 0.7f, 1f, new Color(0.8f, 0.8f, 0.8f));
        platePBR = PbrMaterial("PBR_MetalPlate", "metal_plate", 1.2f, 0.9f, 1.1f, new Color(0.38f, 0.4f, 0.43f));
        corrugatedPBR = PbrMaterial("PBR_Corrugated", "corrugated_iron", 2f, 0.75f, 1f, new Color(0.62f, 0.64f, 0.66f));
        towerPBR = PbrMaterial("PBR_Tower", "precast_concrete_wall", 5f, 0f, 1f, new Color(0.38f, 0.4f, 0.43f));
        windowsMat = WindowsMaterial();
        hdriSky = SkyMaterial();
    }

    /// <summary>URP Lit from a Poly Haven set. Meshes carry UVs in metres; tiling turns that into texture repeats.</summary>
    static Material PbrMaterial(string name, string set, float metresPerTile, float metallic, float smoothBoost, Color tint)
    {
        string dir = $"{PolyHavenDir}/{set}";
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{set}_diff.jpg");
        var normal = NormalMap($"{dir}/{set}_nor.jpg");
        var metalSmooth = MetalSmoothMap($"{dir}/{set}_rough.jpg", set, metallic, smoothBoost);
        return SaveMat(name, new Material(Shader.Find("Universal Render Pipeline/Lit")), m =>
        {
            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", metalSmooth);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 1f);
            m.SetTextureScale("_BaseMap", Vector2.one / metresPerTile);
        });
    }

    static Texture2D NormalMap(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>URP wants metallic in R and smoothness in A; Poly Haven ships roughness. Bake the conversion once.</summary>
    static Texture2D MetalSmoothMap(string roughPath, string set, float metallic, float smoothBoost)
    {
        string outPath = $"{Gen}/{set}_metalsmooth.png";
        if (!regen && AssetDatabase.LoadAssetAtPath<Texture2D>(outPath) is Texture2D existing) return existing;

        var importer = (TextureImporter)AssetImporter.GetAtPath(roughPath);
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
        const int size = 2048;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[size * size];
        byte m = (byte)(Mathf.Clamp01(metallic) * 255f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = rough.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size).r;
                float smooth = Mathf.Clamp01((1f - r) * smoothBoost);
                pixels[y * size + x] = new Color32(m, 0, 0, (byte)(smooth * 255f));
            }
        tex.SetPixels32(pixels);
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
        var outImporter = (TextureImporter)AssetImporter.GetAtPath(outPath);
        outImporter.sRGBTexture = false;
        outImporter.wrapMode = TextureWrapMode.Repeat;
        outImporter.maxTextureSize = 2048;
        outImporter.anisoLevel = 8;
        outImporter.textureCompression = TextureImporterCompression.CompressedHQ;
        outImporter.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    /// <summary>
    /// Office glass for building fronts: an 8x8 grid of panes (12 m square) cut out with alpha clipping,
    /// mostly dark reflective glass, some lit warm/fluorescent with blinds at different heights.
    /// </summary>
    static Material WindowsMaterial()
    {
        // 2048 px: 256 px per pane, enough for mullions, slat lines on the blinds and room depth falloff.
        const int size = 2048, cells = 8, cell = size / cells;
        const int insetX = cell * 7 / 64, insetBottom = cell * 9 / 64, insetTop = cell * 7 / 64;
        var albedo = new Color[size * size];
        var emission = new Color[size * size];
        var rng = new System.Random(11);
        Color[] interiors = { new Color(1f, 0.74f, 0.45f), new Color(1f, 0.84f, 0.62f), new Color(0.82f, 0.9f, 1f), new Color(1f, 0.66f, 0.38f) };

        for (int cy = 0; cy < cells; cy++)
            for (int cx = 0; cx < cells; cx++)
            {
                bool lit = rng.NextDouble() < 0.3;
                Color interior = interiors[rng.Next(interiors.Length)] * (float)(0.45 + rng.NextDouble() * 0.55);
                float blinds = (float)rng.NextDouble() * 0.6f; // portion of the pane covered from the top
                float seed = (float)rng.NextDouble() * 100f;
                int x0 = cx * cell + insetX, x1 = cx * cell + cell - insetX;
                int y0 = cy * cell + insetBottom, y1 = cy * cell + cell - insetTop;
                int mullion = (x0 + x1) / 2;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        float u = (x - x0) / (float)(x1 - x0);
                        float v = (y - y0) / (float)(y1 - y0);
                        int i = y * size + x;
                        // Thin vertical mullion splitting each pane in two.
                        if (Mathf.Abs(x - mullion) < 3)
                        {
                            albedo[i] = new Color(0.12f, 0.13f, 0.14f, 1f);
                            continue;
                        }
                        bool covered = v > 1f - blinds;
                        // Faint reflection streak across dark glass.
                        float streak = Mathf.Clamp01(1f - Mathf.Abs(u - v * 0.6f - 0.2f) * 6f) * 0.03f;
                        Color glass = new Color(0.035f + streak, 0.04f + streak, 0.05f + streak, 1f);
                        if (lit && !covered)
                        {
                            // Brighter toward the ceiling, with a little room clutter in the light.
                            float falloff = Mathf.Lerp(0.7f, 1f, v) * Mathf.Lerp(0.85f, 1f, Mathf.PerlinNoise(seed + u * 3f, v * 2f));
                            albedo[i] = new Color(interior.r * 0.4f, interior.g * 0.4f, interior.b * 0.4f, 1f);
                            emission[i] = interior * falloff;
                        }
                        else if (lit && covered)
                        {
                            // Horizontal slats.
                            float slat = (y % 12) < 2 ? 0.7f : 1f;
                            albedo[i] = new Color(0.16f, 0.14f, 0.12f, 1f) * slat;
                            emission[i] = interior * 0.12f * slat;
                        }
                        else
                        {
                            albedo[i] = glass;
                        }
                    }
            }

        var albedoTex = SaveTexture("windows_albedo", albedo, size);
        var emissionTex = SaveTexture("windows_emission", emission, size);
        return SaveMat("Windows", new Material(Shader.Find("Universal Render Pipeline/Lit")), m =>
        {
            m.SetTexture("_BaseMap", albedoTex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Smoothness", 0.93f);
            m.SetFloat("_Metallic", 0.15f);
            m.SetTexture("_EmissionMap", emissionTex);
            m.SetColor("_EmissionColor", Color.white * 2.6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetTextureScale("_BaseMap", Vector2.one / 12f);
        });
    }

    static Material SkyMaterial()
    {
        string path = $"{PolyHavenDir}/dresden_station_night_2k.hdr";
        var hdr = AssetDatabase.LoadAssetAtPath<Texture>(path);
        return SaveMat("NightSky", new Material(Shader.Find("Skybox/Panoramic")), m =>
        {
            m.SetTexture("_MainTex", hdr);
            m.SetFloat("_Exposure", 0.45f);
            m.SetFloat("_Rotation", 120f);
        });
    }

    // -------------------------------------------------------------- Geometry

    /// <summary>
    /// Box with UVs in world metres (so textures keep real scale and line up across blocks).
    /// Submesh 0 = sides and bottom, submesh 1 = top.
    /// </summary>
    static Mesh WorldBox(Vector3 size, Vector3 center)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var sides = new List<int>();
        var top = new List<int>();
        Vector3 h = size * 0.5f;

        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, System.Func<Vector3, Vector2> uv, List<int> tris)
        {
            int i = verts.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                verts.Add(p);
                uvs.Add(uv(p + center));
            }
            tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        Vector2 XY(Vector3 w) => new Vector2(w.x, w.y);
        Vector2 ZY(Vector3 w) => new Vector2(w.z, w.y);
        Vector2 XZ(Vector3 w) => new Vector2(w.x, w.z);

        Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, -h.y, -h.z), XY, sides);   // front (-z)
        Face(new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, -h.y, h.z), XY, sides);       // back (+z)
        Face(new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), ZY, sides);   // left
        Face(new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, -h.y, h.z), ZY, sides);       // right
        Face(new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), XZ, sides);   // bottom
        Face(new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), XZ, top);         // top

        var mesh = new Mesh { name = "WorldBox" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(sides, 0);
        mesh.SetTriangles(top, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Flat panel facing the camera (-z) with world-metre UVs, used for window glass.</summary>
    static GameObject WindowPanel(Transform parent, float xMin, float yMin, float xMax, float yMax, float z, Quaternion? rotation = null, Vector3? pivot = null)
    {
        var go = new GameObject("Windows");
        go.transform.SetParent(parent, false);
        Vector3 center = pivot ?? new Vector3((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, z);
        go.transform.position = center;
        if (rotation.HasValue) go.transform.rotation = rotation.Value;
        float w = xMax - xMin, hgt = yMax - yMin;
        var mesh = new Mesh { name = "WindowPanel" };
        mesh.vertices = new[] { new Vector3(-w / 2, -hgt / 2), new Vector3(-w / 2, hgt / 2), new Vector3(w / 2, hgt / 2), new Vector3(w / 2, -hgt / 2) };
        mesh.uv = new[] { new Vector2(xMin, yMin), new Vector2(xMin, yMax), new Vector2(xMax, yMax), new Vector2(xMax, yMin) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = windowsMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;
        return go;
    }

    // ---------------------------------------------------------------- Skyline

    /// <summary>
    /// The city behind the play line, in three depth rows sized to the level. Uses the buildings from
    /// "Demo City By Versatile Studio" when that asset is imported; otherwise procedural concrete towers
    /// with setbacks, a crown and glazing on every face the camera can see.
    /// </summary>
    static void BuildRealisticSkyline(Rect area)
    {
        var skyline = new GameObject("Skyline").transform;
        var aviation = NeonMat(new Color(1f, 0.08f, 0.05f) * 4f);
        var demoCity = FindDemoCityBuildings();
        // Taller levels get a taller city so the skyline still rises above the highest rooftops.
        float extra = Mathf.Max(0f, area.yMax - 30f);
        float baseY = Mathf.Min(-40f, area.yMin - 38f);
        (float zMin, float zMax, Vector2 width, Vector2 height, float gap, bool shadows)[] rows =
        {
            (14f, 20f, new Vector2(7f, 13f), new Vector2(30f, 60f) + Vector2.one * extra, 2f, true),
            (30f, 42f, new Vector2(11f, 19f), new Vector2(45f, 95f) + Vector2.one * extra, 3f, false),
            (65f, 90f, new Vector2(18f, 30f), new Vector2(70f, 130f) + Vector2.one * extra, 5f, false),
        };

        foreach (var row in rows)
        {
            float x = area.xMin - 40f;
            while (x < area.xMax + 60f)
            {
                float hgt = Random.Range(row.height.x, row.height.y);
                float z = Random.Range(row.zMin, row.zMax);
                float w = demoCity.Count > 0
                    ? PlaceDemoCityBuilding(skyline, demoCity, x, baseY, z, hgt, row.shadows)
                    : ProceduralTower(skyline, aviation, x, baseY, z, Random.Range(row.width.x, row.width.y), hgt, row.shadows);
                x += w + Random.Range(row.gap, row.gap * 3f);
            }
        }
    }

    static float ProceduralTower(Transform parent, Material aviation, float x, float baseY, float z, float w, float hgt, bool castShadows)
    {
        float d = Random.Range(8f, 16f);
        var tower = new GameObject("Tower");
        tower.transform.SetParent(parent);
        tower.isStatic = true;

        // Up to two setback tiers, each narrower and shallower than the one below.
        int tiers = hgt > 50f ? Random.Range(1, 3) : Random.Range(0, 2);
        float tierBase = baseY, tierW = w, tierD = d, tierX = x, tierZ = z;
        float remaining = hgt;
        for (int t = 0; t <= tiers; t++)
        {
            float tierH = t == tiers ? remaining : remaining * Random.Range(0.55f, 0.75f);
            remaining -= tierH;
            float top = tierBase + tierH;
            var center = new Vector3(tierX + tierW * 0.5f, tierBase + tierH * 0.5f, tierZ + tierD * 0.5f);

            var body = new GameObject(t == 0 ? "Body" : "Setback");
            body.transform.SetParent(tower.transform);
            body.transform.position = center;
            body.AddComponent<MeshFilter>().sharedMesh = WorldBox(new Vector3(tierW, tierH, tierD), center);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { towerPBR, roofPBR };
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            body.isStatic = true;

            WindowPanel(body.transform, tierX + 0.8f, tierBase, tierX + tierW - 0.8f, top - 2f, tierZ - 0.03f);
            WindowPanel(body.transform, tierZ + 0.8f, tierBase, tierZ + tierD - 0.8f, top - 2f, 0f,
                Quaternion.Euler(0f, 90f, 0f), new Vector3(tierX - 0.03f, (tierBase + top - 2f) * 0.5f, tierZ + tierD * 0.5f));
            WindowPanel(body.transform, tierZ + 0.8f, tierBase, tierZ + tierD - 0.8f, top - 2f, 0f,
                Quaternion.Euler(0f, -90f, 0f), new Vector3(tierX + tierW + 0.03f, (tierBase + top - 2f) * 0.5f, tierZ + tierD * 0.5f));

            // A concrete cornice band caps each tier.
            Box("Cornice", new Vector3(center.x, top - 0.4f, tierZ - 0.15f), new Vector3(tierW + 0.4f, 0.8f, 0.3f), towerPBR).transform.SetParent(body.transform, true);

            float shrinkW = tierW * Random.Range(0.12f, 0.25f), shrinkD = tierD * Random.Range(0.1f, 0.25f);
            tierX += shrinkW;
            tierW -= shrinkW * 2f;
            tierZ += shrinkD;
            tierD -= shrinkD * 2f;
            tierBase = top;
        }

        float roof = baseY + hgt;
        float cx = tierX + tierW * 0.5f, cz = tierZ + tierD * 0.5f;
        // Roof plant: a mechanical penthouse and sometimes a water tank.
        Box("Penthouse", new Vector3(cx, roof + 1.2f, cz), new Vector3(tierW * 0.45f, 2.4f, tierD * 0.45f), towerPBR).transform.SetParent(tower.transform, true);
        if (Random.value < 0.4f)
            Box("Tank", new Vector3(cx + tierW * 0.3f, roof + 1.5f, cz), new Vector3(1.6f, 3f, 1.6f), rustPBR).transform.SetParent(tower.transform, true);
        if (Random.value < 0.6f)
        {
            Box("Mast", new Vector3(cx, roof + 3.4f, cz), new Vector3(0.15f, 2.2f, 0.15f), platePBR).transform.SetParent(tower.transform, true);
            Box("AviationLight", new Vector3(cx, roof + 4.6f, cz), Vector3.one * 0.5f, aviation).transform.SetParent(tower.transform, true);
        }
        return w;
    }

    // ------------------------------------------------------------- Demo City

    /// <summary>
    /// Building prefabs from "Demo City By Versatile Studio", if the asset is in the project
    /// (any folder whose path mentions Demo City or Versatile). Each entry carries its measured size.
    /// </summary>
    static List<(GameObject prefab, Bounds bounds)> FindDemoCityBuildings()
    {
        var found = new List<(GameObject, Bounds)>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string lower = path.ToLowerInvariant().Replace(" ", "").Replace("_", "");
            if (!lower.Contains("democity") && !lower.Contains("versatile")) continue;
            string file = Path.GetFileNameWithoutExtension(lower);
            if (!(file.Contains("build") || file.Contains("tower") || file.Contains("house") || file.Contains("skyscraper") || file.Contains("apartment") || file.Contains("office")))
                continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var temp = (GameObject)Object.Instantiate(prefab);
            temp.transform.position = Vector3.zero;
            var renderers = temp.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                // Whole buildings only: skip props, single walls and huge multi-block set pieces.
                if (b.size.y >= 6f && b.size.x is > 3f and < 80f && b.size.z < 80f) found.Add((prefab, b));
            }
            Object.DestroyImmediate(temp);
        }
        if (found.Count > 0) Debug.Log($"[NeonRunner25D] Skyline uses {found.Count} Demo City buildings.");
        return found;
    }

    /// <summary>Places one Demo City building with its base at baseY, scaled toward the row's height. Returns its width.</summary>
    static float PlaceDemoCityBuilding(Transform parent, List<(GameObject prefab, Bounds bounds)> set, float x, float baseY, float z, float targetHeight, bool castShadows)
    {
        var (prefab, b) = set[Random.Range(0, set.Count)];
        // Stretch tall buildings a little, but never squash a low-rise into a tower.
        float s = Mathf.Clamp(targetHeight / b.size.y, 1f, 3.5f);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localScale = prefab.transform.localScale * s;
        go.transform.position = new Vector3(x - b.min.x * s, baseY - b.min.y * s, z - b.min.z * s);
        // Lift short buildings so their roofline still shows above the play area's fog.
        float lift = Mathf.Max(0f, targetHeight - b.size.y * s);
        go.transform.position += Vector3.up * lift;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        return b.size.x * s;
    }

    // --------------------------------------------------------- Render quality

    /// <summary>
    /// High tier: 4K soft cascaded shadows for the moon and lamps. Low tier ("Mobile"): no MSAA, FSR
    /// upscaling from 80 % resolution, two cheap cascades, half-resolution textures, fewer per-object lights.
    /// Also sharpens every scanned surface with anisotropic filtering.
    /// </summary>
    static void ConfigureRenderQuality()
    {
        void Edit(string path, System.Action<SerializedObject> change)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (!asset) return;
            var so = new SerializedObject(asset);
            change(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        Edit("Assets/Settings/PC_RPAsset.asset", so =>
        {
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 4096;
            so.FindProperty("m_ShadowDistance").floatValue = 70f;
            so.FindProperty("m_ShadowCascadeCount").intValue = 4;
            so.FindProperty("m_Cascade4Split").vector3Value = new Vector3(0.08f, 0.2f, 0.45f);
            so.FindProperty("m_ShadowDepthBias").floatValue = 0.6f;
            so.FindProperty("m_ShadowNormalBias").floatValue = 0.4f;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = 3; // High
        });
        Edit("Assets/Settings/Mobile_RPAsset.asset", so =>
        {
            so.FindProperty("m_MSAA").intValue = 1;               // off; the camera uses FXAA on this tier
            so.FindProperty("m_RenderScale").floatValue = 0.8f;
            so.FindProperty("m_UpscalingFilter").intValue = 3;    // FSR 1 upscale back to native
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 1024;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.FindProperty("m_ShadowDistance").floatValue = 35f;
            so.FindProperty("m_ShadowCascadeCount").intValue = 2;
            so.FindProperty("m_Cascade2Split").floatValue = 0.3f;
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 2;
        });
        // The low tier composites AO straight after opaques (no extra pass in the lighting).
        Edit("Assets/Settings/Mobile_Renderer.asset", so =>
        {
            var features = so.FindProperty("m_RendererFeatures");
            for (int i = 0; i < features.arraySize; i++)
                if (features.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableObject f && f.GetType().Name == "ScreenSpaceAmbientOcclusion")
                {
                    var fso = new SerializedObject(f);
                    fso.FindProperty("m_Settings.AfterOpaque").boolValue = true;
                    fso.ApplyModifiedPropertiesWithoutUndo();
                }
        });

        // Desktop builds may drop to the light tier on weak hardware, so it must not be excluded there.
        var qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
        var qso = new SerializedObject(qualityAsset);
        var levels = qso.FindProperty("m_QualitySettings");
        for (int i = 0; i < levels.arraySize; i++)
        {
            var level = levels.GetArrayElementAtIndex(i);
            if (level.FindPropertyRelative("name").stringValue == "Mobile")
                level.FindPropertyRelative("excludedTargetPlatforms").arraySize = 0;
        }
        qso.ApplyModifiedPropertiesWithoutUndo();

        // Per-tier texture resolution and filtering.
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            bool low = QualitySettings.names[i] == "Mobile";
            QualitySettings.globalTextureMipmapLimit = low ? 1 : 0;
            QualitySettings.anisotropicFiltering = low ? AnisotropicFiltering.Enable : AnisotropicFiltering.ForceEnable;
            QualitySettings.lodBias = low ? 0.7f : 2f;
        }
        QualitySettings.SetQualityLevel(current, false);

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PolyHavenDir }))
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if (!importer || importer.anisoLevel >= 8 && importer.textureCompression == TextureImporterCompression.CompressedHQ) continue;
            importer.anisoLevel = 8;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------- Lamps & reflections

    /// <summary>Sodium street lamps on the wider rooftops: real shadows and pools of warm light on wet concrete.</summary>
    static void PlaceStreetLamps()
    {
        var lampHead = NeonMat(new Color(1f, 0.72f, 0.42f) * 3f);
        foreach (var roof in roofBlocks)
        {
            if (roof.width < 6f) continue;
            int count = roof.width > 14f ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(roof.xMin + 2f, roof.xMax - 2f, count == 1 ? 0.5f : i);
                float y = roof.yMax;
                var lamp = new GameObject("StreetLamp");
                lamp.transform.SetParent(decorRoot);
                lamp.transform.position = new Vector3(x, y, 0f);
                Box("Post", new Vector3(x, y + 2.4f, 2.2f), new Vector3(0.12f, 4.8f, 0.12f), platePBR).transform.SetParent(lamp.transform, true);
                Box("Arm", new Vector3(x, y + 4.75f, 1.45f), new Vector3(0.1f, 0.1f, 1.6f), platePBR).transform.SetParent(lamp.transform, true);
                Box("Head", new Vector3(x, y + 4.68f, 0.7f), new Vector3(0.35f, 0.08f, 0.5f), lampHead).transform.SetParent(lamp.transform, true);

                var spot = new GameObject("LampLight").AddComponent<Light>();
                spot.transform.SetParent(lamp.transform, false);
                spot.transform.position = new Vector3(x, y + 4.6f, 0.6f);
                spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                spot.type = LightType.Spot;
                spot.color = new Color(1f, 0.7f, 0.42f);
                spot.intensity = 30f;
                spot.range = 9f;
                spot.spotAngle = 95f;
                spot.innerSpotAngle = 40f;
                spot.shadows = LightShadows.Soft;
                spot.shadowStrength = 0.85f;
            }
        }
    }

    /// <summary>One box-projected reflection probe per room, captured at start, so wet roofs and glass reflect the scene.</summary>
    static void AddReflectionProbes()
    {
        foreach (var room in Object.FindObjectsByType<Room>())
        {
            Rect b = room.Bounds;
            var go = new GameObject("ReflectionProbe");
            go.transform.SetParent(room.transform, false);
            go.transform.position = new Vector3(b.center.x, b.center.y, 0f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(b.width + 6f, b.height + 6f, 24f);
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.hdr = true;
            probe.intensity = 1f;
        }
    }

    // ------------------------------------------------- High-quality Kenney skin

    /// <summary>
    /// Rebuilds a flat-colour Kenney skin as a 2048 px PBR set. Each pixel is classified from the
    /// Kenney UV layout (hair, skin, shirt, trousers, trainers) and gets real material detail:
    /// cotton-jersey knit on the shirt, denim weave on the trousers (both CC0 Poly Haven scans),
    /// hair strands, subtle skin grain, plus matching normal and smoothness maps.
    /// </summary>
    static Material KenneyHQMaterial(string skin)
    {
        string baseName = $"{Gen}/kenney_{skin}_hq";
        string albedoPath = baseName + "_albedo.png", normalPath = baseName + "_normal.png", msPath = baseName + "_ms.png";
        bool exists = File.Exists(albedoPath) && File.Exists(normalPath) && File.Exists(msPath);
        if (regen || !exists) BakeKenneyHQ(skin, albedoPath, normalPath, msPath);

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        var normal = NormalMap(normalPath);
        var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(msPath);
        return SaveMat($"Kenney_{skin}_HQ", new Material(Shader.Find("Universal Render Pipeline/Lit")), m =>
        {
            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", ms);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 1f);
        });
    }

    enum SkinRegion { Skin, Hair, Shirt, Trousers, Shoes, Other }

    static void BakeKenneyHQ(string skin, string albedoPath, string normalPath, string msPath)
    {
        var src = ReadableTexture($"{Root}/ThirdParty/Kenney/AnimatedCharacters2/Skins/{skin}.png");
        var jerseyDiff = ReadableTexture($"{PolyHavenDir}/cotton_jersey/cotton_jersey_diff.jpg");
        var jerseyNor = ReadableTexture($"{PolyHavenDir}/cotton_jersey/cotton_jersey_nor.jpg");
        var denimDiff = ReadableTexture($"{PolyHavenDir}/denim_fabric/denim_fabric_diff.jpg");
        var denimNor = ReadableTexture($"{PolyHavenDir}/denim_fabric/denim_fabric_nor.jpg");
        float jerseyMean = MeanLuminance(jerseyDiff), denimMean = MeanLuminance(denimDiff);

        const int size = 2048;
        var albedo = new Color32[size * size];
        var normal = new Color32[size * size];
        var ms = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            float top = 1f - v; // Kenney layout measured top-down
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                Color c = src.GetPixelBilinear(u, v);
                var region = Classify(c, u, top);

                Color outC = c;
                Vector3 n = Vector3.forward;
                float smooth = 0.3f;

                switch (region)
                {
                    case SkinRegion.Shirt:
                    {
                        float l = Luminance(jerseyDiff.GetPixelBilinear(u * 7f, v * 7f)) / jerseyMean;
                        outC = c * Mathf.Lerp(1f, l, 0.7f) + Color.white * (l - 1f) * 0.05f;
                        n = Unpack(jerseyNor.GetPixelBilinear(u * 7f, v * 7f), 2.4f);
                        smooth = 0.14f;
                        break;
                    }
                    case SkinRegion.Trousers:
                    {
                        float l = Luminance(denimDiff.GetPixelBilinear(u * 9f, v * 9f)) / denimMean;
                        outC = c * Mathf.Lerp(1f, l, 0.6f) + new Color(0.02f, 0.025f, 0.04f) * (l - 0.6f);
                        n = Unpack(denimNor.GetPixelBilinear(u * 9f, v * 9f), 1.8f);
                        smooth = 0.18f;
                        break;
                    }
                    case SkinRegion.Hair:
                    {
                        float strand = Mathf.Sin(u * 1400f + Mathf.PerlinNoise(u * 40f, v * 6f) * 18f);
                        outC = c * (1f + strand * 0.16f);
                        n = new Vector3(Mathf.Cos(u * 1400f) * 0.22f, 0f, 1f).normalized;
                        smooth = 0.42f + strand * 0.08f;
                        break;
                    }
                    case SkinRegion.Skin:
                    {
                        float grain = Mathf.PerlinNoise(u * 520f, v * 520f) - 0.5f;
                        outC = c * (1f + grain * 0.07f);
                        n = new Vector3(grain * 0.08f, (Mathf.PerlinNoise(v * 520f, u * 520f) - 0.5f) * 0.08f, 1f).normalized;
                        smooth = 0.38f;
                        break;
                    }
                    case SkinRegion.Shoes:
                    {
                        n = Unpack(jerseyNor.GetPixelBilinear(u * 14f, v * 14f), 0.45f);
                        smooth = 0.24f;
                        break;
                    }
                }

                int i = y * size + x;
                outC.a = 1f;
                albedo[i] = outC;
                normal[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                ms[i] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(smooth) * 255f));
            }
        }

        WritePng(albedoPath, albedo, size, true);
        WritePng(normalPath, normal, size, false);
        WritePng(msPath, ms, size, false);
    }

    static SkinRegion Classify(Color c, float u, float top)
    {
        bool peach = c.r > 0.8f && c.g > 0.45f && c.g < 0.7f && c.b > 0.3f && c.b < 0.58f;
        if (u > 0.624f && u < 0.805f && top > 0.13f && top < 0.512f) return SkinRegion.Shoes;
        if (top < 0.478f && u < 0.624f)
        {
            if (peach) return SkinRegion.Skin;
            return Luminance(c) < 0.22f || c.g > 0.6f && c.b > 0.45f ? SkinRegion.Hair : SkinRegion.Skin;
        }
        if (peach) return SkinRegion.Skin;
        if (u >= 0.597f && top >= 0.746f) return SkinRegion.Trousers;
        if (u < 0.624f && top >= 0.478f) return SkinRegion.Shirt;
        return SkinRegion.Other;
    }

    static Vector3 Unpack(Color c, float strength)
    {
        var n = new Vector3(c.r * 2f - 1f, c.g * 2f - 1f, c.b * 2f - 1f);
        n.x *= strength;
        n.y *= strength;
        return n.normalized;
    }

    static float Luminance(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;

    static float MeanLuminance(Texture2D t)
    {
        float sum = 0f;
        const int samples = 64;
        for (int y = 0; y < samples; y++)
            for (int x = 0; x < samples; x++)
                sum += Luminance(t.GetPixelBilinear((x + 0.5f) / samples, (y + 0.5f) / samples));
        return Mathf.Max(0.01f, sum / (samples * samples));
    }

    /// <summary>Loads a texture as raw readable pixels (default type, so normal maps aren't decoded).</summary>
    static Texture2D ReadableTexture(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!importer.isReadable || importer.textureType != TextureImporterType.Default)
        {
            importer.isReadable = true;
            importer.textureType = TextureImporterType.Default;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void WritePng(string path, Color32[] pixels, int size, bool srgb)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
        tex.SetPixels32(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = srgb;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    // -------------------------------------------------------------- Characters

    /// <summary>
    /// A Rocketbox character (Humanoid) under a facing holder, fitted to CharacterHeight with feet on the
    /// holder origin, driven by the shared humanoid controller (idle / run / airborne pose).
    /// </summary>
    static Transform MakeRealisticCharacter(Transform parent, string avatar, out Animator animator)
    {
        var holder = new GameObject("Model").transform;
        holder.SetParent(parent, false);

        string fbxPath = $"{RocketboxDir}/{avatar}/{avatar}.fbx";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var character = (GameObject)PrefabUtility.InstantiatePrefab(source, holder);
        ApplyRocketboxMaterials(character, avatar);

        var (height, minY) = MeasureCharacter(source);
        float scale = CharacterHeight / height;
        character.transform.localScale = Vector3.one * scale;
        character.transform.localPosition = new Vector3(0f, -minY * scale, 0f);

        animator = character.GetComponent<Animator>();
        if (!animator) animator = character.AddComponent<Animator>();
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
        animator.runtimeAnimatorController = humanController;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var r in character.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
        return holder;
    }

    static void ImportRocketbox(string avatar)
    {
        string fbxPath = $"{RocketboxDir}/{avatar}/{avatar}.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }
        if (importer.importAnimation)
        {
            importer.importAnimation = false;
            dirty = true;
        }
        if (dirty) importer.SaveAndReimport();

        foreach (var tga in Directory.GetFiles($"{RocketboxDir}/{avatar}/Textures", "*.tga"))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(tga.Replace('\\', '/'));
            bool isNormal = tga.Contains("_normal");
            if (ti.maxTextureSize != 2048 || (isNormal && ti.textureType != TextureImporterType.NormalMap))
            {
                ti.maxTextureSize = 2048;
                if (isNormal) ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
        }
    }

    /// <summary>
    /// Rocketbox materials are named after their texture sets (e.g. m172_body, m172_equipment, m172_pistol).
    /// Each gets a URP Lit material from its _color and _normal maps; opacity atlases (hair, lashes) are alpha-clipped.
    /// The FBX carries four LOD meshes, so only the high-poly one is kept visible.
    /// </summary>
    static void ApplyRocketboxMaterials(GameObject character, string avatar)
    {
        string texDir = $"{RocketboxDir}/{avatar}/Textures";
        string[] parts = { "body", "head", "equipment", "pistol", "opacity" };
        Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{texDir}/{file}.tga");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var made = new Dictionary<string, Material>();

        Material For(string materialName)
        {
            string n = materialName.ToLowerInvariant();
            string set = n.Length >= 4 ? n.Substring(0, 4) : n;                 // e.g. "m172"
            string part = parts.FirstOrDefault(p => n.Contains(p)) ?? (n.Contains("equ") ? "equipment" : "body");
            string key = $"{set}_{part}";
            if (made.TryGetValue(key, out var existing)) return existing;

            var color = Tex($"{key}_color");
            var normal = Tex($"{key}_normal");
            bool clip = part == "opacity";
            var mat = SaveMat($"RB_{avatar}_{part}", new Material(lit), m =>
            {
                m.SetTexture("_BaseMap", color);
                m.SetColor("_BaseColor", Color.white);
                if (normal)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.EnableKeyword("_NORMALMAP");
                }
                m.SetFloat("_Smoothness", part == "head" ? 0.38f : part == "pistol" ? 0.6f : 0.25f);
                if (clip)
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", 0.35f);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cull", 0f);
                }
            });
            return made[key] = mat;
        }

        foreach (var r in character.GetComponentsInChildren<Renderer>(true))
        {
            string rn = r.name.ToLowerInvariant();
            bool isLod = rn.Contains("poly");
            r.gameObject.SetActive(!isLod || rn.Contains("hipoly"));
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = For(mats[i] ? mats[i].name : "body");
            r.sharedMaterials = mats;
        }
    }

    /// <summary>Idle and run from the Rocketbox library (humanoid, in place); in the air we hold a mid-stride frame.</summary>
    static RuntimeAnimatorController CreateHumanController()
    {
        string path = Anim + "/RocketboxCharacter.controller";
        if (!regen && AssetDatabase.LoadAssetAtPath<AnimatorController>(path) is AnimatorController existing) return existing;

        string thiefFbx = $"{RocketboxDir}/{ThiefAvatar}/{ThiefAvatar}.fbx";
        var avatar = AssetDatabase.LoadAllAssetsAtPath(thiefFbx).OfType<Avatar>().First();
        var idle = HumanClip("m_idle_neutral_01.max.fbx", avatar);
        var run = HumanClip("m_run_fast_01.max.fbx", avatar);

        AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("AirPose", AnimatorControllerParameterType.Float);
        var sm = controller.layers[0].stateMachine;

        var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
        tree.blendParameter = "Speed";
        tree.AddChild(idle, 0f);
        tree.AddChild(run, 7f);
        var air = sm.AddState("Air");
        air.motion = run;
        air.timeParameterActive = true;
        air.timeParameter = "AirPose";
        sm.defaultState = locomotion;

        var toAir = locomotion.AddTransition(air);
        toAir.hasExitTime = false;
        toAir.duration = 0.08f;
        toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        var toGround = air.AddTransition(locomotion);
        toGround.hasExitTime = false;
        toGround.duration = 0.1f;
        toGround.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        // The Air state reads its frame from AirPose; default it to a leaping mid-stride frame.
        var parameters = controller.parameters;
        parameters.First(p => p.name == "AirPose").defaultFloat = 0.32f;
        controller.parameters = parameters;
        return controller;
    }

    static AnimationClip HumanClip(string file, Avatar sourceAvatar)
    {
        string path = $"{RocketboxDir}/Animations/{file}";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = sourceAvatar;
        importer.importAnimation = true;
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.loopTime = true;
            c.loopPose = true;
            c.lockRootRotation = true;
            c.keepOriginalOrientation = true;
            c.lockRootHeightY = true;
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true;
            c.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
    }
}
