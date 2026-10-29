using UnityEngine;

namespace NeonRunner
{
    /// <summary>What a mode is for; the menu colours its card by it.</summary>
    public enum LevelType { Tutorial, Chaos }

    /// <summary>The modes shown in the main menu, plus saved best times.</summary>
    public static class LevelCatalog
    {
        public const string MenuScene = "MainMenu";

        public readonly struct Entry
        {
            public readonly int number;
            public readonly string name;
            public readonly string scene;
            public readonly LevelType type;
            public readonly string blurb;

            public Entry(int number, string name, string scene, LevelType type, string blurb)
            {
                this.number = number;
                this.name = name;
                this.scene = scene;
                this.type = type;
                this.blurb = blurb;
            }

            public string SceneName => scene;
        }

        public static readonly Entry[] Levels =
        {
            new Entry(1, "Tutorial", "Tutorial", LevelType.Tutorial, "Learn the flow: slide, vault, wall-run, long-jump"),
            new Entry(2, "Chaos Mode", "Chaos", LevelType.Chaos, "Everything at once, full speed, the wall is coming"),
        };

        public static Color TypeColor(LevelType type) => type switch
        {
            LevelType.Tutorial => Theme.Accent,
            _ => Theme.Danger,
        };

        static string Key(int number, Difficulty d) => $"NeonRunner.Best.{d}.Mode{number}";

        /// <summary>Best clear time on a difficulty, or a negative number if never cleared.</summary>
        public static float BestTime(int number, Difficulty d) => PlayerPrefs.GetFloat(Key(number, d), -1f);

        public static void RecordClear(int number, Difficulty d, float time)
        {
            float best = BestTime(number, d);
            if (best < 0f || time < best) PlayerPrefs.SetFloat(Key(number, d), time);
            PlayerPrefs.Save();
        }

        public static bool MenuInBuild => Application.CanStreamedLevelBeLoaded(MenuScene);
    }
}
