using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShearAndGrow.Shearing.Editor
{
    /// <summary>
    /// Enforces the split between DEFAULT GAME STATE (shipped) and PLAYER SAVE STATE (device-only):
    ///  • fails the build if any sheep save / economy save data sits inside Assets (StreamingAssets, Resources, anywhere),
    ///  • fails a release build if the default state does not start with full wool / zero starting wool,
    ///  • logs where Editor saves live (they are outside the project and never packaged).
    /// </summary>
    public sealed class SaveDataBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public const string DefaultStatePath = "Assets/ShearAndGrow/Features/Shearing/Config/Save/DefaultGameState.asset";
        public const string BalancePath = "Assets/ShearAndGrow/Core/Config/GameBalanceData.asset";

        public void OnPreprocessBuild(BuildReport report)
        {
            var problems = Validate(isDevelopment: (report.summary.options & BuildOptions.Development) != 0);
            if (problems.Count > 0)
                throw new BuildFailedException("[Shearing] Save-data validation failed:\n - " + string.Join("\n - ", problems));
            Debug.Log("[Shearing] Save-data validation passed. Player saves are written at runtime to the device's " +
                      "Application.persistentDataPath only; Editor test saves live in '" + Application.persistentDataPath +
                      "/ShearAndGrow/EditorSaves' (outside the project, never packaged).");
        }

        [MenuItem("Shear & Grow/Saves/Validate Build Save Data")]
        public static void ValidateFromMenu()
        {
            var problems = Validate(isDevelopment: false);
            if (problems.Count == 0) Debug.Log("[Shearing] Save-data validation passed: no player progress in build inputs, default state = full wool, no starting wool.");
            else Debug.LogError("[Shearing] Save-data problems:\n - " + string.Join("\n - ", problems));
        }

        [MenuItem("Shear & Grow/Saves/Open Editor Save Folder")]
        public static void OpenEditorSaves()
        {
            string dir = Path.Combine(Application.persistentDataPath, "ShearAndGrow", "EditorSaves");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem("Shear & Grow/Saves/Delete Editor Saves (dev)")]
        public static void DeleteEditorSaves()
        {
            foreach (var sub in new[] { "EditorSaves/Sheep", "Sheep" })
            {
                string dir = Path.Combine(Application.persistentDataPath, "ShearAndGrow", sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
            }
            int econ = ShearAndGrow.Economy.EconomyStore.DeleteAll();
            ShearAndGrow.Economy.WoolEconomy.ResetInstanceForTests();
            Debug.Log("[Shearing] Editor sheep saves deleted (+ " + econ + " economy file(s)).");
        }

        public static List<string> Validate(bool isDevelopment)
        {
            var problems = new List<string>();
            var state = AssetDatabase.LoadAssetAtPath<DefaultGameState>(DefaultStatePath);
            if (state == null) problems.Add("DefaultGameState asset missing at " + DefaultStatePath);
            else if (!isDevelopment && state.StartingCutFraction > 0f)
                problems.Add("DefaultGameState.StartingCutFraction must be 0 (full wool) in release builds.");

            var balance = AssetDatabase.LoadAssetAtPath<ShearAndGrow.Economy.GameBalanceData>(BalancePath);
            if (balance == null) problems.Add("GameBalanceData asset missing at " + BalancePath);
            else if (!isDevelopment && (balance.StartingDirtyWool > 0 || balance.StartingWetWool > 0 || balance.StartingCleanWool > 0))
                problems.Add("GameBalanceData starting wool must be 0 in release builds (test values would ship to every new player).");

            // Any file that looks like a player save inside the project would be shipped.
            foreach (var file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".json" && ext != ".txt" && ext != ".bytes" && ext != ".dat" && ext != ".sav") continue;
                string name = Path.GetFileName(file);
                bool suspicious = name.StartsWith("sheep_") || name.StartsWith("economy") || name.StartsWith("farm.json") ||
                                  name.StartsWith("clock.json") || name.EndsWith(".sav");
                if (!suspicious && new FileInfo(file).Length < 4 * 1024 * 1024)
                {
                    string text = File.ReadAllText(file);
                    suspicious = text.Contains("\"cutMask\"") || text.Contains("\"creditedCutArea\"") ||
                                 text.Contains("\"sheepLedger\"") || text.Contains("\"dirtyWool\"") ||
                                 text.Contains("\"growthClockStamp\"") || text.Contains("\"gameSeconds\"");
                }
                if (suspicious) problems.Add("Player save data inside the project (would ship in the build): " + file);
            }
            return problems;
        }
    }
}
