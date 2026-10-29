using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeonRunner;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Builds the 2.5D Neon Runner demo: gameplay on the XY plane (Physics2D, same
/// code as the 2D version) presented with 3D models, a perspective camera, fog
/// and real depth. Uses Kenney's CC0 Animated Characters 2 and City Kit
/// (Commercial). Safe to re-run.
/// </summary>
public static partial class NeonRunner25DBuilder
{
    const string Root = "Assets/NeonRunner25D";
    const string Gen = Root + "/Generated";
    const string Mats = Root + "/Materials";
    const string Anim = Root + "/Animation";
    const string ScenePath = Root + "/Scenes/NeonRunner25D_Demo.unity";
    const string ConfigPath = Root + "/Settings/MovementConfig.asset";
    const string Kenney = Root + "/ThirdParty/Kenney";
    const string CharModel = Kenney + "/AnimatedCharacters2/Model/characterMedium.fbx";
    const string MusicPath = Root + "/Audio/Crystallized.mp3";
    const float CharacterHeight = 1.75f;
    const float BlockFrontZ = -1f;
    const float BlockDepth = 4f;

    // Rain-soaked sodium-lit night: slate-teal fog, amber street lamps, warm windows, one mint accent.
    static readonly Color Sodium = new Color(1f, 0.58f, 0.18f);
    static readonly Color Mint = new Color(0.45f, 1f, 0.78f);
    static readonly Color Amber = new Color(1f, 0.82f, 0.55f);
    static readonly Color Danger = new Color(1f, 0.22f, 0.12f);
    static readonly Color FogColor = new Color(0.07f, 0.1f, 0.115f);

    static Sprite square, circle, soft, ring, wisp, smokePuff;
    static Material spriteMat, roofMat, facadeMat, neonMat, coneMat, glassMat, metalMat, thiefSkin, copSkin;
    static AnimatorController characterController;
    static int groundLayer, playerLayer, enemyLayer;
    static Transform levelRoot, decorRoot;
    static int trimIndex;

    struct LevelSpec
    {
        public string name;
        public string scene;
        public Color fog;
        public Color ambient;
        public Vector2 spawn;
        public Action build;
        /// <summary>Layout seed; 0 = derived from the level's index.</summary>
        public int seed;
    }

    // The Tutorial, then Chaos Mode (see NeonRunner25DLevels). Order = Build Settings order after the menu.
    static LevelSpec[] Levels => new[] { TutorialSpec, ChaosSpec };

    static Color fogColor = FogColor;
    /// <summary>True only for the first level of a build: shared assets are regenerated once, then reused.</summary>
    static bool regen;

    static string LevelPath(LevelSpec spec) => $"{Root}/Scenes/{spec.scene}.unity";

    [MenuItem("Neon Runner/Build Tutorial + Chaos Mode")]
    public static void Build()
    {
        foreach (var dir in new[] { Root, Gen, Mats, Anim, Root + "/Scenes", Root + "/Settings" }) Directory.CreateDirectory(dir);
        AssetDatabase.Refresh();
        SetupLayers();
        ConfigureRenderQuality();

        // The old numbered levels are gone for good.
        foreach (var old in AssetDatabase.FindAssets("t:Scene Level", new[] { Root + "/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
            if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(old), @"^Level\d+$"))
                AssetDatabase.DeleteAsset(old);
        AssetDatabase.DeleteAsset(Root + "/Scenes/NeonRunner25D_Demo.unity");

        var levels = Levels;
        for (int i = 0; i < levels.Length; i++)
        {
            regen = i == 0;
            BuildLevelScene(i, levels[i], levels.Length);
        }
        RegisterScenes(levels);
        BuildMainMenu();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(LevelPath(levels[0]));
        Debug.Log("[NeonRunner25D] Built the Tutorial and Chaos Mode. The Tutorial is open - press Play.");
    }

    /// <summary>Rebuilds only Chaos Mode with a fresh random layout.</summary>
    [MenuItem("Neon Runner/Reroll Chaos Mode Layout")]
    public static void RerollChaos()
    {
        chaosSeed = Environment.TickCount;
        SetupLayers();
        var levels = Levels;
        regen = false;
        BuildLevelScene(1, levels[1], levels.Length);
        RegisterScenes(levels);
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(LevelPath(levels[1]));
        Debug.Log($"[NeonRunner25D] Chaos Mode rerolled (seed {chaosSeed}).");
    }

    /// <summary>Levels in order straight after the menu: the game manager advances by build index.</summary>
    static void RegisterScenes(LevelSpec[] levels)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path.StartsWith(Root + "/Scenes/") && s.path != MenuScenePath);
        int at = scenes.FindIndex(s => s.path == MenuScenePath) + 1;
        for (int i = levels.Length - 1; i >= 0; i--) scenes.Insert(at, new EditorBuildSettingsScene(LevelPath(levels[i]), true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void BuildLevelScene(int index, LevelSpec spec, int levelCount)
    {
        Random.InitState(spec.seed != 0 ? spec.seed : 2525 + index * 97);
        fogColor = spec.fog;

        // New scene first: NewScene unloads unreferenced assets loaded before it.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        matCache.Clear();
        trimIndex = 0;

        CreateSprites();
        CreateMaterials();
        CreateRealisticMaterials();
        metalMat = platePBR;
        roofBlocks.Clear();
        ImportRocketbox(ThiefAvatar);
        ImportRocketbox(CopAvatar);
        humanController = CreateHumanController();
        characterController = CreateCharacterController();
        var config = AssetDatabase.LoadAssetAtPath<MovementConfig>(ConfigPath);
        if (!config)
        {
            config = ScriptableObject.CreateInstance<MovementConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }

        levelRoot = new GameObject("Level").transform;
        decorRoot = new GameObject("Decor").transform;
        var spawn = new GameObject("SpawnPoint").transform;
        spawn.position = spec.spawn;

        SetupLighting(spec.ambient);

        // Camera
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.SetPositionAndRotation(new Vector3(spawn.position.x, spawn.position.y + 1.2f, -16f), Quaternion.identity);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = fogColor;
        camGo.AddComponent<AudioListener>();
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        var gv = new GameObject("Global Volume").AddComponent<Volume>();
        gv.isGlobal = true;
        gv.sharedProfile = CreateGlobalProfile();
        var bv = new GameObject("Boost Volume").AddComponent<Volume>();
        bv.isGlobal = true;
        bv.priority = 1;
        bv.weight = 0f;
        bv.sharedProfile = CreateBoostProfile();

        var player = BuildPlayer(spawn.position, config, out var abilities, out var visuals);

        var follow = camGo.AddComponent<CameraFollow2D>();
        Set(follow, "target", player);
        Set(follow, "offset", new Vector2(0f, 1.2f));

        var perf = camGo.AddComponent<PerformanceManager>();
        Set(perf, "globalVolume", gv);
        Set(perf, "boostVolume", bv);
        Set(perf, "cam", cam);

        camGo.AddComponent<AudioSource>().playOnAwake = false;
        var music = camGo.AddComponent<MusicController>();
        Set(music, "player", player);
        Set(music, "music", AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath));

        var speedLinesGo = new GameObject("SpeedLines");
        speedLinesGo.transform.SetParent(camGo.transform, false);
        var speedLines = speedLinesGo.AddComponent<SpeedLines>();
        Set(speedLines, "player", player);
        Set(speedLines, "cam", cam);
        Set(speedLines, "lineSprite", wisp);
        Set(speedLines, "material", spriteMat);

        var rainGo = new GameObject("Rain");
        var rain = rainGo.AddComponent<AmbientSnow>();
        Set(rain, "cam", cam);
        Set(rain, "pixelSprite", square);
        Set(rain, "material", spriteMat);
        Set(rain, "count", 260);
        Set(rain, "wind", new Vector2(-3f, -24f));
        Set(rain, "color", new Color(0.78f, 0.82f, 0.82f, 0.3f));
        Set(rain, "pixelsPerUnit", 40f);
        Set(rain, "streakLength", 14f);
        Set(rain, "depthRange", new Vector2(-6f, 12f));

        var fx = new GameObject("BoostFX").AddComponent<BoostFX>();
        Set(fx, "player", player);
        Set(fx, "cameraFollow", follow);
        Set(fx, "boostVolume", bv);
        Set(fx, "ringSprite", ring);
        Set(fx, "unlitMaterial", spriteMat);

        var juice = new GameObject("PlayerJuiceFX").AddComponent<PlayerJuiceFX>();
        Set(juice, "player", player);
        Set(juice, "visuals", visuals);
        Set(juice, "pixelSprite", square);
        Set(juice, "circleSprite", circle);
        Set(juice, "material", spriteMat);
        Set(juice, "pixelsPerUnit", 40f);

        var gm = new GameObject("GameManager").AddComponent<GameManager>();
        Set(gm, "player", player);
        Set(gm, "abilities", abilities);
        Set(gm, "initialSpawn", spawn);
        Set(gm, "levelName", spec.name);
        Set(gm, "levelNumber", index + 1);
        Set(gm, "levelCount", levelCount);

        var rooms = new GameObject("RoomManager").AddComponent<RoomManager>();
        Set(rooms, "player", player);
        Set(rooms, "cameraFollow", follow);

        ResetLevelFeatures();
        spec.build();
        PlaceStreetLamps();
        AddReflectionProbes();

        // Background signage, a couple per room.
        foreach (var room in Object.FindObjectsByType<Room>())
        {
            Rect b = room.Bounds;
            for (int i = 0; i < 2; i++)
                NeonSign(new Vector3(Random.Range(b.xMin + 3f, b.xMax - 3f), Random.Range(b.yMin + 6f, b.yMax - 2f), Random.Range(5f, 9f)));
        }

        // The level's overall extent sizes the safety net and the skyline.
        var allRooms = Object.FindObjectsByType<Room>();
        Rect area = allRooms[0].Bounds;
        foreach (var room in allRooms)
            area = Rect.MinMaxRect(Mathf.Min(area.xMin, room.Bounds.xMin), Mathf.Min(area.yMin, room.Bounds.yMin),
                Mathf.Max(area.xMax, room.Bounds.xMax), Mathf.Max(area.yMax, room.Bounds.yMax));

        // Safety net below everything.
        var pit = new GameObject("KillZone_Void");
        pit.transform.SetParent(levelRoot);
        pit.transform.position = new Vector3(area.center.x, area.yMin - 12f, 0);
        var pitBox = pit.AddComponent<BoxCollider2D>();
        pitBox.isTrigger = true;
        pitBox.size = new Vector2(area.width + 200f, 4);
        pit.AddComponent<KillZone>();

        BuildRealisticSkyline(area);
        EditorSceneManager.SaveScene(scene, LevelPath(spec));
    }

    // ================================================================= Lighting

    static void SetupLighting(Color ambient)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.011f;
        // HDRI night sky: what reflection probes see behind the city (the camera itself clears to fog colour).
        RenderSettings.skybox = hdriSky;
        RenderSettings.fogColor = fogColor;

        // A stronger, lower moon gives long, crisp-edged shadows that soften with distance.
        var moon = new GameObject("Moonlight").AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.76f, 0.84f, 0.92f);
        moon.intensity = 0.85f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 1f;
        moon.shadowBias = 0.05f;
        moon.shadowNormalBias = 0.35f;
        moon.transform.rotation = Quaternion.Euler(34f, -30f, 0f);
        var moonData = moon.gameObject.AddComponent<UniversalAdditionalLightData>();
        moonData.softShadowQuality = SoftShadowQuality.High;
    }

    // =================================================================== Player

    static PlayerController BuildPlayer(Vector3 position, MovementConfig config, out PlayerAbilities abilities, out PlayerVisuals25D visuals)
    {
        var go = new GameObject("Player") { layer = playerLayer };
        go.transform.position = position;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var noFriction = NoFriction();
        rb.sharedMaterial = noFriction;
        var capsule = go.AddComponent<CapsuleCollider2D>();
        capsule.size = new Vector2(0.7f, 1.4f);
        capsule.sharedMaterial = noFriction;

        var input = go.AddComponent<PlayerInputReader>();
        var grapple = go.AddComponent<GrappleHook>();
        abilities = go.AddComponent<PlayerAbilities>();
        var controller = go.AddComponent<PlayerController>();
        visuals = go.AddComponent<PlayerVisuals25D>();

        // Visual rig: feet pivot (squash/lean) -> facing holder -> Kenney character.
        var squashRoot = new GameObject("Visual").transform;
        squashRoot.SetParent(go.transform, false);
        squashRoot.localPosition = new Vector3(0f, -0.7f, 0f);
        // Kenney character (stylised) with a baked high-quality PBR skin; the world stays realistic.
        var model = MakeCharacter(squashRoot, KenneyHQMaterial("skaterFemaleA"), out var animator);
        model.localRotation = Quaternion.Euler(0f, 70f, 0f);

        var hand = new GameObject("HandPoint").transform;
        hand.SetParent(go.transform, false);
        hand.localPosition = new Vector3(0f, 0.25f, -0.3f);

        var keyLight = AddLight(go.transform, new Vector3(0.4f, 0.8f, -1.6f), new Color(0.85f, 0.88f, 1f), 7f, 9f);
        keyLight.name = "PlayerLight";
        // Mint rim from behind so the dark outfit always separates from the city.
        var rimLight = AddLight(go.transform, new Vector3(-0.3f, 1f, 1.2f), Mint, 3.5f, 10f);
        rimLight.name = "RimLight";
        var dashLight = AddLight(go.transform, new Vector3(0f, 0.9f, -0.6f), Danger, 2.5f, 3f);
        dashLight.name = "DashLight";

        var trailGo = new GameObject("Trail");
        trailGo.transform.SetParent(go.transform, false);
        var trail = trailGo.AddComponent<TrailRenderer>();
        trail.sharedMaterial = spriteMat;
        trail.time = 0.22f;
        trail.widthMultiplier = 0.9f;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        trail.emitting = false;
        trail.minVertexDistance = 0.05f;

        var rope = new GameObject("Rope").AddComponent<LineRenderer>();
        rope.transform.SetParent(go.transform, false);
        rope.sharedMaterial = spriteMat;
        rope.positionCount = 2;
        rope.useWorldSpace = true;
        rope.widthMultiplier = 0.06f;
        rope.startColor = Sodium * 1.5f;
        rope.endColor = Mint * 1.5f;

        var reticle = MakeSprite("GrappleReticle", null, Vector3.zero, Vector2.one * 1.2f, Mint, ring);
        reticle.SetActive(false);

        int groundMask = 1 << groundLayer;
        Set(grapple, "player", controller);
        Set(grapple, "handPoint", hand);
        Set(grapple, "rope", rope);
        Set(grapple, "reticle", reticle.transform);
        Set(grapple, "obstacleMask", (LayerMask)groundMask);

        Set(abilities, "player", controller);
        Set(abilities, "handPoint", hand);
        Set(abilities, "taserHitMask", (LayerMask)(groundMask | (1 << enemyLayer)));
        Set(abilities, "boltSprite", square);
        Set(abilities, "smokeSprite", smokePuff);
        Set(abilities, "smokeColor", new Color(0.62f, 0.64f, 0.74f, 0.92f));
        Set(abilities, "unlitMaterial", spriteMat);

        Set(controller, "config", config);
        Set(controller, "input", input);
        Set(controller, "grapple", grapple);
        Set(controller, "body", capsule);
        Set(controller, "groundMask", (LayerMask)groundMask);

        Set(visuals, "player", controller);
        Set(visuals, "squashRoot", squashRoot);
        Set(visuals, "model", model);
        Set(visuals, "animator", animator);
        Set(visuals, "dashLight", dashLight);
        Set(visuals, "trail", trail);
        return controller;
    }

    /// <summary>Kenney's rigged character under a facing holder, scaled to CharacterHeight with feet at the holder origin.</summary>
    static Transform MakeCharacter(Transform parent, Material skin, out Animator animator)
    {
        var holder = new GameObject("Model").transform;
        holder.SetParent(parent, false);

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CharModel);
        var character = (GameObject)PrefabUtility.InstantiatePrefab(source, holder);
        foreach (var r in character.GetComponentsInChildren<Renderer>()) r.sharedMaterial = skin;

        // Fit height and put the feet on the holder origin, using a bind-pose measurement of the source model.
        var (height, minY) = MeasureCharacter(source);
        float scale = CharacterHeight / height;
        character.transform.localScale = Vector3.one * scale;
        character.transform.localPosition = new Vector3(0f, -minY * scale, 0f);

        // Explicit check: in the editor a missing component is a "fake null" that ?? doesn't catch.
        animator = character.GetComponent<Animator>();
        if (!animator) animator = character.AddComponent<Animator>();
        animator.runtimeAnimatorController = characterController;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return holder;
    }

    /// <summary>
    /// Bind-pose height and lowest point of the model at its own import scale. Measured on a free
    /// standing copy: a freshly parented skinned instance can report stale bounds.
    /// </summary>
    static (float height, float minY) MeasureCharacter(GameObject source)
    {
        var temp = (GameObject)Object.Instantiate(source);
        temp.transform.position = Vector3.zero;
        var bounds = temp.GetComponentsInChildren<Renderer>()[0].bounds;
        foreach (var r in temp.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        float rootScale = temp.transform.localScale.y;
        Object.DestroyImmediate(temp);
        // Express relative to a root scale of 1 so it composes with the instance's own root scale.
        return (bounds.size.y / rootScale, bounds.min.y / rootScale);
    }

    static AnimatorController CreateCharacterController()
    {
        if (!regen && AssetDatabase.LoadAssetAtPath<AnimatorController>(Anim + "/KenneyCharacter.controller") is AnimatorController existing)
            return existing;
        var idle = LoadClip("idle", true);
        var run = LoadClip("run", true);
        var jump = LoadClip("jump", false);

        string path = Anim + "/KenneyCharacter.controller";
        AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        var sm = controller.layers[0].stateMachine;

        var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
        tree.blendParameter = "Speed";
        tree.AddChild(idle, 0f);
        tree.AddChild(run, 6f);
        var air = sm.AddState("Air");
        air.motion = jump;
        sm.defaultState = sm.states.First(s => s.state.name == "Locomotion").state;

        var toAir = sm.states.First(s => s.state.name == "Locomotion").state.AddTransition(air);
        toAir.hasExitTime = false;
        toAir.duration = 0.05f;
        toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        var toGround = air.AddTransition(sm.states.First(s => s.state.name == "Locomotion").state);
        toGround.hasExitTime = false;
        toGround.duration = 0.08f;
        toGround.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        return controller;
    }

    /// <summary>Configures the Kenney animation FBX (main clip only, looping if asked) and returns its clip.</summary>
    static AnimationClip LoadClip(string file, bool loop)
    {
        string path = $"{Kenney}/AnimatedCharacters2/Animations/{file}.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        var clips = importer.defaultClipAnimations.Where(c => !c.name.Contains("Targeting")).ToArray();
        foreach (var c in clips)
        {
            c.loopTime = loop;
            c.lockRootPositionXZ = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .First(c => !c.name.StartsWith("__preview") && !c.name.Contains("Targeting"));

        // Kenney's animation files are rooted at the armature ("Root"), while the character model has
        // the armature one level down. Re-root every curve under "Root/" so it reaches the bones (the
        // file's own root curves, including its export scale of 100, belong to that armature node).
        var clip = new AnimationClip { name = file, frameRate = source.frameRate };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            var remapped = binding;
            remapped.path = binding.path == "" ? "Root" : "Root/" + binding.path;
            AnimationUtility.SetEditorCurve(clip, remapped, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string clipPath = $"{Anim}/{file}.anim";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.CreateAsset(clip, clipPath);
        return clip;
    }

    // ==================================================================== Level

    // Level grammar: each room is one parkour segment that teaches or tests one move,
    // sized from the fast-parkour metrics (see NeonRunner25DLevels for the numbers), with an optional
    // data shard on a riskier line. Dying restarts the current room.

    // Rooftops run 12 m deep so the free-roaming camera never sees under a building.
    static void Ground(float xMin, float xMax, float top) => Block(xMin, top - 12f, xMax, top);

    static void Pit(float xMin, float xMax, float top)
    {
        Block(xMin, top - 12f, xMax, top - 4f);
        Spikes(xMin, xMax, top - 4f);
    }

    /// <summary>Room ceiling: collision only. With a continuous camera a floating slab would read as scenery.</summary>
    static void Ceil(float xMin, float xMax, float y)
    {
        var go = new GameObject("Ceiling") { layer = groundLayer, isStatic = true };
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3((xMin + xMax) * 0.5f, y + 1f, 0f);
        go.AddComponent<BoxCollider2D>().size = new Vector2(xMax - xMin, 2f);
    }

    static void Room(string name, float xMin, float yMin, float xMax, float yMax, float spawnX, float spawnY)
    {
        var go = new GameObject("Room " + name);
        go.transform.SetParent(levelRoot);
        var spawn = new GameObject("Spawn").transform;
        spawn.SetParent(go.transform, false);
        spawn.position = new Vector3(spawnX, spawnY, 0f);
        var room = go.AddComponent<Room>();
        var so = new SerializedObject(room);
        so.FindProperty("bounds").rectValue = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        so.FindProperty("spawnPoint").objectReferenceValue = spawn;
        so.FindProperty("roomName").stringValue = name;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A strip of spikes standing on y (Celeste's most common hazard).</summary>
    static void Spikes(float xMin, float xMax, float y)
    {
        var go = new GameObject("Spikes");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3((xMin + xMax) * 0.5f, y + 0.22f, 0f);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(xMax - xMin - 0.1f, 0.4f);
        go.AddComponent<KillZone>();

        var spikeMat = platePBR;
        for (float x = xMin + 0.25f; x < xMax; x += 0.5f)
        {
            var spike = Box("Spike", new Vector3(x, y, 0.4f), new Vector3(0.33f, 0.33f, 2.2f), spikeMat);
            spike.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            spike.transform.SetParent(go.transform, true);
        }
        NeonBar(new Vector3((xMin + xMax) * 0.5f, y + 0.02f, -0.9f), new Vector3(xMax - xMin, 0.04f, 0.04f), Danger * 0.6f, go.transform);
    }

    static void Crumble(float xMin, float yMin, float xMax, float yMax)
    {
        float w = xMax - xMin, h = yMax - yMin;
        var go = new GameObject("CrumbleBlock") { layer = groundLayer };
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(xMin + w * 0.5f, yMin + h * 0.5f, 0f);
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(w, h);

        var visual = new GameObject("Visual").transform;
        visual.SetParent(go.transform, false);
        var slab = Box("Slab", go.transform.position + new Vector3(0f, 0f, 0.6f), new Vector3(w, h, 2.6f), corrugatedPBR);
        slab.transform.SetParent(visual, true);
        for (float x = xMin + 0.5f; x < xMax; x += 1f)
            NeonBar(new Vector3(x, yMax + 0.01f, -0.7f), new Vector3(0.5f, 0.05f, 0.05f), Sodium * 0.8f, visual);

        var crumble = go.AddComponent<CrumbleBlock>();
        Set(crumble, "visual", visual);
    }

    static void Spring(float x, float y, float launchSpeed = 21f)
    {
        var go = new GameObject("Spring");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, y, 0f);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.offset = new Vector2(0f, 0.3f);
        col.size = new Vector2(1f, 0.6f);

        var visual = new GameObject("Visual").transform;
        visual.SetParent(go.transform, false);
        Box("Base", go.transform.position + new Vector3(0f, 0.1f, 0f), new Vector3(1.1f, 0.2f, 1f), metalMat).transform.SetParent(visual, true);
        NeonBar(go.transform.position + new Vector3(0f, 0.35f, 0f), new Vector3(1f, 0.1f, 0.9f), Mint, visual);
        AddLight(go.transform, new Vector3(0f, 0.6f, -0.8f), Mint, 3f, 3f);

        var spring = go.AddComponent<Spring>();
        Set(spring, "visual", visual);
        Set(spring, "launchSpeed", launchSpeed);
    }

    static void Shard(float x, float y)
    {
        var go = new GameObject("DataShard");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, y, 0f);
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.6f;

        var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(gem.GetComponent<BoxCollider>());
        gem.name = "Gem";
        gem.transform.SetParent(go.transform, false);
        gem.transform.localRotation = Quaternion.Euler(35f, 0f, 45f);
        gem.transform.localScale = new Vector3(0.38f, 0.38f, 0.38f);
        gem.GetComponent<Renderer>().sharedMaterial = NeonMat(Amber * 3f);
        AddLight(go.transform, new Vector3(0f, 0f, -0.6f), Amber, 3f, 3f);

        var shard = go.AddComponent<DataShard>();
        Set(shard, "visual", gem.transform);
    }

    /// <summary>A rooftop chunk: solid body (Physics2D collider), lit-window facade, neon trim, roof clutter.</summary>
    static void Block(float xMin, float yMin, float xMax, float yMax)
    {
        float w = xMax - xMin, h = yMax - yMin;
        var center = new Vector3(xMin + w * 0.5f, yMin + h * 0.5f, BlockFrontZ + BlockDepth * 0.5f);

        // Concrete walls and a wet roof, textured in real-world metres.
        var body = new GameObject("Block") { layer = groundLayer, isStatic = true };
        body.transform.SetParent(levelRoot);
        body.transform.position = center;
        body.AddComponent<MeshFilter>().sharedMesh = WorldBox(new Vector3(w, h, BlockDepth), center);
        body.AddComponent<MeshRenderer>().sharedMaterials = new[] { wallPBR, roofPBR };
        body.AddComponent<BoxCollider2D>().size = new Vector2(w, h);

        // Office glass on the front of anything building-sized.
        if (h >= 3f && w >= 2.5f)
            WindowPanel(levelRoot, xMin + 0.4f, yMin, xMax - 0.4f, yMax - 1.1f, BlockFrontZ - 0.02f);
        if (h >= 3f) roofBlocks.Add(Rect.MinMaxRect(xMin, yMin, xMax, yMax));

        // Steel parapet cap with a faint warm edge light so the walkable edge still reads at night.
        Box("Parapet", new Vector3(center.x, yMax - 0.06f, BlockFrontZ + 0.05f), new Vector3(w, 0.12f, 0.2f), platePBR);
        NeonBar(new Vector3(center.x, yMax - 0.13f, BlockFrontZ - 0.06f), new Vector3(w, 0.025f, 0.02f), Sodium * 0.35f);

        // Roof clutter sits behind the play line so it never reads as an obstacle.
        if (w > 5f && h > 3f)
        {
            int count = Mathf.FloorToInt(w / 4f);
            for (int i = 0; i < count; i++)
            {
                float x = Random.Range(xMin + 0.5f, xMax - 0.5f);
                if (Random.value < 0.6f)
                    Box("Vent", new Vector3(x, yMax + 0.25f, Random.Range(1.2f, 2.6f)), new Vector3(Random.Range(0.6f, 1.4f), 0.5f, 0.7f), metalMat);
                else
                {
                    float hh = Random.Range(1f, 2.5f);
                    Box("Antenna", new Vector3(x, yMax + hh * 0.5f, Random.Range(1.5f, 2.8f)), new Vector3(0.06f, hh, 0.06f), metalMat);
                    NeonBar(new Vector3(x, yMax + hh, 2f), Vector3.one * 0.12f, Danger);
                }
            }
        }
    }

    static GameObject Box(string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        go.transform.SetParent(levelRoot);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    static GameObject NeonBar(Vector3 pos, Vector3 size, Color color, Transform parent = null)
    {
        var go = Box("Neon", pos, size, NeonMat(color * 3f));
        if (parent) go.transform.SetParent(parent, true);
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    // Property blocks aren't saved with scenes, so baked colours/tilings use small cached material assets.
    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    static string Key(Color c) => $"{c.r:0.##}_{c.g:0.##}_{c.b:0.##}_{c.a:0.##}";

    static Material NeonMat(Color hdr)
    {
        string key = "Neon_" + Key(hdr);
        if (matCache.TryGetValue(key, out var m)) return m;
        return matCache[key] = SaveMat(key, new Material(neonMat), x => x.SetColor("_BaseColor", hdr));
    }

    static Material TransparentMat(Color c)
    {
        string key = "Glass_" + Key(c);
        if (matCache.TryGetValue(key, out var m)) return m;
        return matCache[key] = SaveMat(key, new Material(coneMat), x => x.SetColor("_BaseColor", c));
    }

    static Material FacadeMat(float tilesX, float tilesY)
    {
        tilesX = Mathf.Max(1f, Mathf.Round(tilesX * 2f) / 2f);
        tilesY = Mathf.Max(1f, Mathf.Round(tilesY * 2f) / 2f);
        string key = $"Facade_{tilesX:0.#}x{tilesY:0.#}";
        if (matCache.TryGetValue(key, out var m)) return m;
        return matCache[key] = SaveMat(key, new Material(facadeMat), x => x.SetTextureScale("_BaseMap", new Vector2(tilesX, tilesY)));
    }

    static void Hint(float x, float groundY, float width, string message)
    {
        var go = new GameObject("Hint");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, groundY + 2f, 0);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(width, 4f);
        var hint = go.AddComponent<HintZone>();
        Set(hint, "message", message);
        Set(hint, "duration", 3.5f);
    }

    static void Checkpoint(float x, float groundY)
    {
        var go = new GameObject("Checkpoint");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, groundY, 0);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.offset = new Vector2(0, 2f);
        box.size = new Vector2(1.5f, 4f);

        var pole = Box("Pole", go.transform.position + new Vector3(0, 1f, 0.6f), new Vector3(0.1f, 2f, 0.1f), metalMat);
        pole.transform.SetParent(go.transform, true);
        var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(beacon.GetComponent<SphereCollider>());
        beacon.name = "Beacon";
        beacon.transform.SetParent(go.transform, false);
        beacon.transform.localPosition = new Vector3(0, 2.1f, 0.6f);
        beacon.transform.localScale = Vector3.one * 0.35f;
        beacon.GetComponent<Renderer>().sharedMaterial = NeonMat(new Color(0.6f, 0.6f, 0.7f));
        var light = AddLight(go.transform, new Vector3(0, 2.1f, 0.2f), new Color(0.6f, 0.6f, 0.8f), 4f, 1.5f);

        var spawn = new GameObject("Spawn").transform;
        spawn.SetParent(go.transform, false);
        spawn.localPosition = new Vector3(0, 1.5f, 0);

        var cp = go.AddComponent<Checkpoint>();
        Set(cp, "spawnPoint", spawn);
        Set(cp, "beacon", beacon.GetComponent<Renderer>());
        Set(cp, "beaconLight", light);
    }

    static void GrapplePoint(float x, float y)
    {
        var go = new GameObject("GrapplePoint");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, y, 0);
        var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(gem.GetComponent<BoxCollider>());
        gem.name = "Gem";
        gem.transform.SetParent(go.transform, false);
        gem.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        gem.transform.localScale = Vector3.one * 0.45f;
        gem.GetComponent<Renderer>().sharedMaterial = NeonMat(Mint * 3f);
        Box("Cable", new Vector3(x, y + 6f, 0.3f), new Vector3(0.05f, 12f, 0.05f), metalMat).transform.SetParent(go.transform, true);
        AddLight(go.transform, new Vector3(0, 0, -0.6f), Mint, 4.5f, 3f);
        var target = go.AddComponent<GrappleTarget>();
        Set(target, "kind", (int)GrappleTarget.TargetKind.Point);
        Set(target, "pulseVisual", gem.transform);
    }

    static void DataNode(float x, float y)
    {
        var go = new GameObject("DataNode");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, y, 0);
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.7f;
        var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(crystal.GetComponent<BoxCollider>());
        crystal.name = "Crystal";
        crystal.transform.SetParent(go.transform, false);
        crystal.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        crystal.transform.localScale = new Vector3(0.4f, 0.6f, 0.4f);
        crystal.GetComponent<Renderer>().sharedMaterial = NeonMat(new Color(0.4f, 1f, 0.6f) * 3f);
        var glow = AddLight(go.transform, new Vector3(0, 0, -0.5f), new Color(0.4f, 1f, 0.6f), 3.5f, 3f);
        var node = go.AddComponent<DataNode>();
        Set(node, "visual", crystal.transform);
        Set(node, "glow", glow);
    }

    static CopGuard Guard(float x, float groundY, float minX, float maxX)
    {
        var go = new GameObject("CopGuard") { layer = enemyLayer };
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, groundY + 0.86f, 0);
        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 4f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var box = go.AddComponent<BoxCollider2D>();
        box.size = new Vector2(0.8f, 1.7f);

        var feet = new GameObject("Feet").transform;
        feet.SetParent(go.transform, false);
        feet.localPosition = new Vector3(0, -0.85f, 0);
        var model = MakeRealisticCharacter(feet, CopAvatar, out var animator);
        var alert = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(alert.GetComponent<SphereCollider>());
        alert.name = "Alert";
        alert.transform.SetParent(go.transform, false);
        alert.transform.localPosition = new Vector3(0, 1.4f, 0);
        alert.transform.localScale = Vector3.one * 0.3f;
        alert.GetComponent<Renderer>().sharedMaterial = NeonMat(Danger * 4f);

        var (sight, cone) = SightRig(go.transform, 8f);
        var guard = go.AddComponent<CopGuard>();
        Set(guard, "obstacleMask", (LayerMask)(1 << groundLayer));
        Set(guard, "groundMask", (LayerMask)(1 << groundLayer));
        Set(guard, "sightLight", sight);
        Set(guard, "sightCone", cone);
        Set(guard, "bodyRenderers", model.GetComponentsInChildren<Renderer>().Cast<Object>().ToArray());
        Set(guard, "alertIcon", alert);
        Set(guard, "hurtbox", box);
        Set(guard, "patrolMinX", minX);
        Set(guard, "patrolMaxX", maxX);
        Set(guard, "model", model);
        Set(guard, "animator", animator);
        TuneCop(guard, true);
        return guard;
    }

    static (Light, MeshFilter) SightRig(Transform parent, float range)
    {
        var sight = AddLight(parent, Vector3.zero, new Color(1f, 0.85f, 0.45f), range, 6f);
        sight.type = LightType.Spot;
        sight.name = "SightLight";
        var coneGo = new GameObject("SightCone");
        coneGo.transform.SetParent(parent, false);
        var mf = coneGo.AddComponent<MeshFilter>();
        var mr = coneGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = coneMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return (sight, mf);
    }

    static CopDrone Drone(params Vector2[] waypoints)
    {
        var go = new GameObject("CopDrone") { layer = enemyLayer };
        go.transform.SetParent(levelRoot);
        go.transform.position = waypoints[0];
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(body.GetComponent<BoxCollider>());
        body.name = "Body";
        body.layer = enemyLayer;
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(1.9f, 0.45f, 1.2f);
        body.GetComponent<Renderer>().sharedMaterial = metalMat;
        var col = body.AddComponent<BoxCollider2D>();

        foreach (float side in new[] { -0.8f, 0.8f })
        {
            var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(rotor.GetComponent<CapsuleCollider>());
            rotor.name = "Rotor";
            rotor.transform.SetParent(go.transform, false);
            rotor.transform.localPosition = new Vector3(side, 0.3f, 0f);
            rotor.transform.localScale = new Vector3(0.9f, 0.03f, 0.9f);
            rotor.GetComponent<Renderer>().sharedMaterial = glassMat;
        }
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(eye.GetComponent<SphereCollider>());
        eye.name = "Eye";
        eye.transform.SetParent(go.transform, false);
        eye.transform.localPosition = new Vector3(0f, -0.2f, -0.45f);
        eye.transform.localScale = Vector3.one * 0.25f;
        eye.GetComponent<Renderer>().sharedMaterial = NeonMat(Danger * 4f);

        var hacked = NeonBar(go.transform.position + new Vector3(0f, 0.25f, 0f), new Vector3(2.1f, 0.08f, 1.3f), Mint, go.transform);
        hacked.name = "HackedPlatform";
        AddLight(hacked.transform, Vector3.zero, Mint, 3f, 3f);
        var ringVis = MakeSprite("GrappleRing", go.transform, new Vector3(0, 0, -0.7f), Vector2.one * 2f, new Color(0.1f, 0.95f, 1f, 0.35f), ring);
        var alert = MakeSprite("Alert", go.transform, new Vector3(0, 0.9f, -0.5f), new Vector2(0.35f, 0.5f), Danger, square);

        var (sight, cone) = SightRig(go.transform, 7f);
        var target = go.AddComponent<GrappleTarget>();
        Set(target, "kind", (int)GrappleTarget.TargetKind.Drone);
        Set(target, "pulseVisual", ringVis.transform);

        var drone = go.AddComponent<CopDrone>();
        Set(drone, "obstacleMask", (LayerMask)(1 << groundLayer));
        Set(drone, "sightLight", sight);
        Set(drone, "sightCone", cone);
        Set(drone, "sightRange", 7f);
        Set(drone, "sightAngle", 50f);
        Set(drone, "bodyRenderers", new Object[] { body.GetComponent<Renderer>() });
        Set(drone, "alertIcon", alert);
        Set(drone, "hurtbox", col);
        Set(drone, "bodyCollider", col);
        Set(drone, "hackedVisual", hacked);
        Set(drone, "waypoints", waypoints);
        TuneCop(drone, false);
        return drone;
    }

    static SecurityWall SecurityWall(float startX, float activationX, float centerY = 10f)
    {
        var go = new GameObject("SecurityWall");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(startX, centerY, 0);
        const float height = 70f;

        var field = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(field.GetComponent<BoxCollider>());
        field.name = "Field";
        field.transform.SetParent(go.transform, false);
        field.transform.localPosition = new Vector3(-4f, 0f, 1f);
        field.transform.localScale = new Vector3(8f, height, 6f);
        field.GetComponent<Renderer>().sharedMaterial = TransparentMat(new Color(1f, 0.05f, 0.15f, 0.18f));
        NeonBar(go.transform.position + new Vector3(-0.1f, 0f, -2f), new Vector3(0.2f, height, 0.2f), Danger, go.transform);

        var lines = new List<Object>();
        for (int i = 0; i < 8; i++)
        {
            var line = NeonBar(go.transform.position + new Vector3(-4f, 0f, -1.9f), new Vector3(8f, 0.05f, 0.05f), new Color(1f, 0.4f, 0.5f), go.transform);
            lines.Add(line.transform);
        }
        var glow = AddLight(go.transform, new Vector3(0f, 0f, -3f), Danger, 16f, 6f);
        var wall = go.AddComponent<SecurityWall>();
        Set(wall, "activationX", activationX);
        Set(wall, "glow", glow);
        Set(wall, "scanLines", lines.ToArray());
        Set(wall, "scanHeight", height * 0.6f);
        return wall;
    }

    static void Goal(float x, float groundY)
    {
        var go = new GameObject("Goal");
        go.transform.SetParent(levelRoot);
        go.transform.position = new Vector3(x, groundY + 1.5f, 0);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(2.5f, 3f);
        MakeSprite("Portal", go.transform, Vector3.zero, new Vector2(2.2f, 3.2f), Sodium * 2f, ring);
        MakeSprite("PortalInner", go.transform, Vector3.zero, new Vector2(1.5f, 2.4f), Mint * 2f, ring);
        AddLight(go.transform, new Vector3(0, 0, -1f), Sodium, 8f, 5f);
        go.AddComponent<LevelGoal>();
    }

    static void NeonSign(Vector3 pos)
    {
        var go = new GameObject("NeonSign");
        go.transform.SetParent(decorRoot);
        go.transform.position = pos;
        Color c = Random.value < 0.5f ? Sodium : Random.value < 0.6f ? Mint : Amber;
        var tubes = new List<Object>();
        bool vertical = Random.value < 0.5f;
        int bars = Random.Range(2, 5);
        for (int i = 0; i < bars; i++)
        {
            Vector3 size = vertical ? new Vector3(0.14f, Random.Range(1f, 3f), 0.1f) : new Vector3(Random.Range(1f, 3.2f), 0.14f, 0.1f);
            Vector3 off = vertical ? new Vector3(i * 0.5f, Random.Range(-0.4f, 0.4f), 0) : new Vector3(Random.Range(-0.4f, 0.4f), i * 0.5f, 0);
            tubes.Add(NeonBar(pos + off, size, c, go.transform).GetComponent<Renderer>());
        }
        var light = AddLight(go.transform, new Vector3(0, 0, -1f), c, 9f, 4f);
        var flicker = go.AddComponent<NeonFlicker>();
        Set(flicker, "lightSource", light);
        Set(flicker, "tubes", tubes.ToArray());
        Set(flicker, "tubeColor", c * 3f);
        Set(flicker, "dropoutChancePerSecond", Random.Range(0.1f, 0.6f));
    }

    // ================================================================== Skyline

    /// <summary>Kenney City Kit buildings in three depth rows: real parallax from the perspective camera.</summary>
    static void BuildSkyline()
    {
        var skyline = new GameObject("Skyline").transform;
        var tall = Directory.GetFiles(Kenney + "/CityKitCommercial/Models", "building-*.fbx").Select(p => p.Replace('\\', '/')).ToArray();
        var far = Directory.GetFiles(Kenney + "/CityKitCommercial/Models", "low-detail-building-*.fbx").Select(p => p.Replace('\\', '/')).ToArray();

        (float z, float scale, string[] set, float baseY)[] rows =
        {
            (9f, 5.5f, tall, -14f),
            (22f, 8f, tall, -16f),
            (45f, 13f, far, -22f),
        };

        foreach (var row in rows)
        {
            float x = -40f;
            while (x < 300f)
            {
                string path = row.set[Random.Range(0, row.set.Length)];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab, skyline);
                float s = row.scale * Random.Range(0.85f, 1.25f);
                b.transform.localScale = new Vector3(s, s * Random.Range(1f, 1.6f), s);
                b.transform.rotation = Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f);
                b.transform.position = new Vector3(x, row.baseY, row.z + Random.Range(-2f, 2f));
                foreach (var r in b.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                b.isStatic = true;

                // Occasional neon light washing the facade.
                if (Random.value < 0.18f)
                {
                    Color c = Random.value < 0.5f ? Sodium : Mint;
                    AddLight(skyline, new Vector3(x, row.baseY + Random.Range(8f, 18f), row.z - 3f), c, row.scale * 2.5f, 6f);
                }
                x += s * 1.15f + Random.Range(0.5f, 3f);
            }
        }
    }

    // ================================================================== Helpers

    static GameObject MakeSprite(string name, Transform parent, Vector3 localPos, Vector2 size, Color color, Sprite sprite)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        Vector2 native = sprite.bounds.size;
        go.transform.localScale = new Vector3(size.x / native.x, size.y / native.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sharedMaterial = spriteMat;
        return go;
    }

    static Light AddLight(Transform parent, Vector3 localPos, Color color, float range, float intensity)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        return light;
    }

    static void Set(Object target, string field, object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[NeonRunner25D] {target.GetType().Name} has no serialized field '{field}'");
            return;
        }
        switch (value)
        {
            case Object o: p.objectReferenceValue = o; break;
            case float f: p.floatValue = f; break;
            case int i: p.intValue = i; break;
            case bool b: p.boolValue = b; break;
            case string s: p.stringValue = s; break;
            case Color c: p.colorValue = c; break;
            case Vector2 v: p.vector2Value = v; break;
            case LayerMask m: p.intValue = m.value; break;
            case Vector2[] arr:
                p.arraySize = arr.Length;
                for (int i = 0; i < arr.Length; i++) p.GetArrayElementAtIndex(i).vector2Value = arr[i];
                break;
            case Object[] objs:
                p.arraySize = objs.Length;
                for (int i = 0; i < objs.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = objs[i];
                break;
            default: throw new ArgumentException($"Unsupported value type {value.GetType()} for {field}");
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetupLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        groundLayer = EnsureLayer(layers, "Ground", 8);
        playerLayer = EnsureLayer(layers, "Player", 9);
        enemyLayer = EnsureLayer(layers, "Enemy", 10);
        tagManager.ApplyModifiedPropertiesWithoutUndo();
        Physics2D.IgnoreLayerCollision(playerLayer, enemyLayer, true);
        Physics2D.IgnoreLayerCollision(enemyLayer, enemyLayer, true);
    }

    static int EnsureLayer(SerializedProperty layers, string name, int preferred)
    {
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        for (int i = preferred; i < layers.arraySize; i++)
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = name;
                return i;
            }
        }
        throw new InvalidOperationException($"No free layer slot for {name}");
    }

    static PhysicsMaterial2D NoFriction()
    {
        string path = $"{Gen}/NoFriction.physicsMaterial2D";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
        if (mat) return mat;
        mat = new PhysicsMaterial2D("NoFriction") { friction = 0f, bounciness = 0f };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // -------------------------------------------------------------- Materials

    static void CreateMaterials()
    {
        spriteMat = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");

        roofMat = SaveMat("Roof", new Material(lit) { color = new Color(0.2f, 0.21f, 0.21f) }, m => m.SetFloat("_Smoothness", 0.45f));
        metalMat = SaveMat("Metal", new Material(lit) { color = new Color(0.26f, 0.27f, 0.27f) }, m =>
        {
            m.SetFloat("_Metallic", 0.7f);
            m.SetFloat("_Smoothness", 0.55f);
        });
        glassMat = SaveMat("RotorGlass", new Material(lit) { color = new Color(0.4f, 0.45f, 0.6f) }, m => m.SetFloat("_Smoothness", 0.9f));

        var facadeTex = CreateFacadeTexture();
        facadeMat = SaveMat("Facade", new Material(lit), m =>
        {
            m.SetTexture("_BaseMap", facadeTex.albedo);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_EmissionMap", facadeTex.emission);
            m.SetColor("_EmissionColor", Color.white * 3.5f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetFloat("_Smoothness", 0.4f);
        });

        neonMat = SaveMat("Neon", new Material(unlitShader), m => m.SetColor("_BaseColor", Color.white * 3f));
        coneMat = SaveMat("TransparentUnlit", new Material(unlitShader), MakeTransparent);

        // Black outfit with red sleeves reads as the hooded thief; the white-suit "criminal" skin didn't.
        thiefSkin = SkinMaterial("skaterFemaleA");
        copSkin = SkinMaterial("cyborgFemaleA");
    }

    static Material SkinMaterial(string skin)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Kenney}/AnimatedCharacters2/Skins/{skin}.png");
        return SaveMat("Skin_" + skin, new Material(Shader.Find("Universal Render Pipeline/Lit")), m =>
        {
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.15f);
        });
    }

    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.2f));
    }

    static Material SaveMat(string name, Material m, Action<Material> setup)
    {
        string path = $"{Mats}/{name}.mat";
        setup(m);
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing)
        {
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;
            existing.renderQueue = m.renderQueue;
            existing.SetOverrideTag("RenderType", m.GetTag("RenderType", false));
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>Night-time window grid: dark wall with randomly lit windows (and a matching emission map).</summary>
    static (Texture2D albedo, Texture2D emission) CreateFacadeTexture()
    {
        const int size = 128, cells = 8;
        var albedo = new Color[size * size];
        var emission = new Color[size * size];
        var wall = new Color(0.1f, 0.11f, 0.115f);
        // Mostly warm tungsten, a few cool fluorescent windows.
        Color[] lit = { new Color(1f, 0.78f, 0.48f), new Color(1f, 0.86f, 0.64f), new Color(1f, 0.7f, 0.4f), new Color(0.78f, 0.92f, 0.95f) };
        var rng = new System.Random(7);
        for (int i = 0; i < albedo.Length; i++) albedo[i] = wall;

        int cell = size / cells;
        for (int cy = 0; cy < cells; cy++)
            for (int cx = 0; cx < cells; cx++)
            {
                bool on = rng.NextDouble() < 0.35;
                Color c = on ? lit[rng.Next(lit.Length)] * (float)(0.5 + rng.NextDouble() * 0.5) : new Color(0.04f, 0.05f, 0.08f);
                for (int y = cy * cell + 3; y < cy * cell + cell - 4; y++)
                    for (int x = cx * cell + 3; x < cx * cell + cell - 3; x++)
                    {
                        albedo[y * size + x] = on ? c * 0.6f : c;
                        if (on) emission[y * size + x] = c;
                    }
            }

        return (SaveTexture("facade_albedo", albedo, size), SaveTexture("facade_emission", emission, size));
    }

    static Texture2D SaveTexture(string name, Color[] pixels, int size)
    {
        string path = $"{Gen}/{name}.png";
        if (!regen && AssetDatabase.LoadAssetAtPath<Texture2D>(path) is Texture2D existing) return existing;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------------------------------------------------------- Sprites

    static void CreateSprites()
    {
        square = SpriteTex("square", 4, (x, y, n) => Color.white, FilterMode.Point);
        circle = SpriteTex("circle", 64, (x, y, n) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
            return new Color(1, 1, 1, Mathf.Clamp01((1f - d) * n * 0.5f));
        });
        soft = SpriteTex("soft", 64, (x, y, n) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
            float a = Mathf.Clamp01(1f - d);
            return new Color(1, 1, 1, a * a);
        });
        ring = SpriteTex("ring", 128, (x, y, n) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
            return new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Abs(d - 0.88f) / 0.08f));
        });
        // Wind wisp: a feathered band of air with fine streaks running along it (stretched long at runtime).
        wisp = SpriteTex("wisp", 256, (x, y, n) =>
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float along = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 1.6f);
            float across = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.2f, 2f));
            float streaks = 0.55f + 0.45f * Fbm(u * 2.5f, v * 14f, 4);
            return new Color(1, 1, 1, Mathf.Clamp01(along * across * streaks * 1.3f));
        });
        // Smoke puff: a billowy cloud (fractal noise) with a soft, broken edge.
        smokePuff = SpriteTex("smoke_puff", 256, (x, y, n) =>
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) / 0.5f;
            float cloud = Fbm(u * 4f + 3.1f, v * 4f + 7.7f, 5);
            float edge = Mathf.Clamp01((1f - d) * 1.6f - (1f - cloud) * 0.7f);
            float shade = Mathf.Lerp(0.78f, 1f, Fbm(u * 7f, v * 7f, 3));
            return new Color(shade, shade, shade, edge * edge * (0.65f + 0.35f * cloud));
        });
    }

    /// <summary>Fractal Perlin noise in [0, 1].</summary>
    static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x, y) * amp;
            norm += amp;
            x *= 2.03f;
            y *= 2.03f;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    static Sprite SpriteTex(string name, int n, Func<int, int, int, Color> pixel, FilterMode filter = FilterMode.Bilinear)
    {
        string path = $"{Gen}/{name}.png";
        if (!regen && AssetDatabase.LoadAssetAtPath<Sprite>(path) is Sprite existing) return existing;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                tex.SetPixel(x, y, pixel(x, y, n));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = n;
        importer.filterMode = filter;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ----------------------------------------------------------------- Post FX

    static VolumeProfile NewProfile(string path)
    {
        if (!regen && AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) is VolumeProfile existing) return existing;
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        return profile;
    }

    static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var c = profile.Add<T>(true);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, profile);
        return c;
    }

    static VolumeProfile CreateGlobalProfile()
    {
        var p = NewProfile($"{Root}/Settings/Neon25DGlobal.asset");
        if (p.components.Count > 0) return p;
        var bloom = AddOverride<Bloom>(p);
        bloom.intensity.Override(0.9f);
        bloom.threshold.Override(1.1f);
        bloom.scatter.Override(0.7f);
        var tone = AddOverride<Tonemapping>(p);
        tone.mode.Override(TonemappingMode.ACES);
        var color = AddOverride<ColorAdjustments>(p);
        color.postExposure.Override(0.45f);
        color.contrast.Override(15f);
        color.saturation.Override(-5f);
        var vignette = AddOverride<Vignette>(p);
        vignette.intensity.Override(0.32f);
        vignette.smoothness.Override(0.6f);
        vignette.color.Override(new Color(0.02f, 0.035f, 0.04f));
        var grain = AddOverride<FilmGrain>(p);
        grain.intensity.Override(0.2f);
        // Subtle always-on blur from camera and object motion; the boost volume pushes it harder.
        var blur = AddOverride<MotionBlur>(p);
        blur.mode.Override(MotionBlurMode.CameraAndObjects);
        blur.quality.Override(MotionBlurQuality.Medium);
        blur.intensity.Override(0.2f);
        blur.clamp.Override(0.025f);
        // A real lens: the gameplay plane (16 m away) is sharp, the skyline falls off into bokeh.
        var dof = AddOverride<DepthOfField>(p);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(16f);
        dof.focalLength.Override(55f);
        dof.aperture.Override(5.6f);
        EditorUtility.SetDirty(p);
        return p;
    }

    static VolumeProfile CreateBoostProfile()
    {
        var p = NewProfile($"{Root}/Settings/Neon25DBoost.asset");
        if (p.components.Count > 0) return p;
        AddOverride<ChromaticAberration>(p).intensity.Override(1f);
        var lens = AddOverride<LensDistortion>(p);
        lens.intensity.Override(-0.3f);
        lens.scale.Override(1f);
        var blur = AddOverride<MotionBlur>(p);
        blur.mode.Override(MotionBlurMode.CameraAndObjects);
        blur.intensity.Override(0.85f);
        blur.quality.Override(MotionBlurQuality.High);
        blur.clamp.Override(0.06f);
        AddOverride<Bloom>(p).intensity.Override(2.5f);
        EditorUtility.SetDirty(p);
        return p;
    }
}
