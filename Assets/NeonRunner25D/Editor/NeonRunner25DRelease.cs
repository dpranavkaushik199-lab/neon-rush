using System.IO;
using System.Linq;
using NeonRunner;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Packages the game as a standalone Windows player: menu, Tutorial and Chaos Mode only,
/// booting into the main menu, fullscreen at native resolution.
/// </summary>
public static class NeonRunner25DRelease
{
    const string OutputDir = "Builds/Windows";
    const string ExeName = "NeonRunner.exe";

    [MenuItem("Neon Rush/Build Game (Windows)")]
    public static void BuildWindows()
    {
        var scenes = new[]
        {
            NeonRunner25DScenes.Menu,
            NeonRunner25DScenes.Tutorial,
            NeonRunner25DScenes.Chaos,
        };
        foreach (var scene in scenes)
            if (!File.Exists(scene))
            {
                Debug.LogError($"[NeonRunner25D] {scene} is missing - run Neon Rush/Build Tutorial + Chaos Mode first.");
                return;
            }

        // Only the game's scenes ship, menu first so the game boots into it.
        EditorBuildSettings.scenes = scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();

        PlayerSettings.productName = "Neon Rush";
        PlayerSettings.companyName = "Neon Rush";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = false;
        PlayerSettings.visibleInBackground = false;

        Directory.CreateDirectory(OutputDir);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(OutputDir, ExeName),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
            Debug.Log($"[NeonRunner25D] Game built: {Path.GetFullPath(Path.Combine(OutputDir, ExeName))} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0}s)");
        else
            Debug.LogError($"[NeonRunner25D] Game build {summary.result}: {summary.totalErrors} error(s). See the Console.");
    }
}

/// <summary>Scene paths shared by the release build.</summary>
static class NeonRunner25DScenes
{
    const string Dir = "Assets/NeonRunner25D/Scenes/";
    public const string Menu = Dir + LevelCatalog.MenuScene + ".unity";
    public const string Tutorial = Dir + "Tutorial.unity";
    public const string Chaos = Dir + "Chaos.unity";
}
