using System.Collections.Generic;
using NeonRunner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The title / level-select scene, first in Build Settings so the game boots into it. Behind the
/// UI floats a music-reactive neural network lit by bloom.
/// </summary>
public static partial class NeonRunner25DBuilder
{
    const string MenuScenePath = Root + "/Scenes/" + LevelCatalog.MenuScene + ".unity";
    const string MenuProfilePath = Root + "/Settings/Neon25DMenu.asset";
    const string NeuralMaterialPath = Mats + "/NeuralGlow.mat";
    const string MenuMusicPath = Root + "/Audio/DeltaForTheDelta.mp3";
    const float MenuMusicStart = 60f;

    [MenuItem("Neon Runner/Build Main Menu")]
    public static void BuildMainMenu()
    {
        // Built additively so whatever scene is open (and its unsaved edits) is left alone.
        var previous = EditorSceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(scene);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.024f);
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 60f;
        cam.allowHDR = true;
        camGo.transform.position = new Vector3(0f, 0f, -10.5f);
        camGo.AddComponent<AudioListener>();
        // Post-processing on: bloom makes the network glow, and the colourblind filter applies here too.
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = MenuProfile();

        var network = new GameObject("Neural Network");
        var backdrop = network.AddComponent<NeuralNetworkBackdrop>();
        Set(backdrop, "cam", cam);
        Set(backdrop, "material", NeuralMaterial());

        var menuGo = new GameObject("MainMenu");
        menuGo.AddComponent<MainMenu>();
        menuGo.AddComponent<AudioSource>().playOnAwake = false;
        // The menu has its own track (Camellia - δ for the DELTA), intro skipped; levels keep Crystallized.
        var menuMusic = menuGo.AddComponent<MusicController>();
        Set(menuMusic, "music", AssetDatabase.LoadAssetAtPath<AudioClip>(MenuMusicPath));
        Set(menuMusic, "startTime", MenuMusicStart);

        EditorSceneManager.SaveScene(scene, MenuScenePath);
        if (previous.IsValid())
        {
            EditorSceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == MenuScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("[NeonRunner25D] Built the main menu and put it first in Build Settings.");
    }

    static Material NeuralMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(NeuralMaterialPath);
        if (mat) return mat;
        mat = new Material(Shader.Find("NeonRunner/NeuralGlow")) { name = "NeuralGlow" };
        AssetDatabase.CreateAsset(mat, NeuralMaterialPath);
        return mat;
    }

    /// <summary>Heavy, wide bloom so the neon bleeds into the void; a little fringing and a dark vignette.</summary>
    static VolumeProfile MenuProfile()
    {
        AssetDatabase.DeleteAsset(MenuProfilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, MenuProfilePath);

        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.75f);
        bloom.intensity.Override(2.4f);
        bloom.scatter.Override(0.78f);
        bloom.highQualityFiltering.Override(true);

        var aberration = profile.Add<ChromaticAberration>(true);
        aberration.intensity.Override(0.25f);

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.42f);
        vignette.smoothness.Override(0.5f);
        vignette.color.Override(Color.black);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }
}
