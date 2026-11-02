using System;
using System.IO;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    [Flags]
    public enum SheepCompletionFlags
    {
        None = 0,
        ReachedThreshold = 1,
        FinishSettled = 2,
    }

    /// <summary>
    /// PERSISTENT state owned by the sheep id (not the scene). Floor clumps are NOT part of it (spec §11).
    /// </summary>
    [Serializable]
    public sealed class SheepWoolState
    {
        public string sheepId;
        /// <summary>Identifies the current wool growth cycle (new GUID when the wool regrows). Payment of sheared
        /// wool is tracked per cycle in the economy ledger, so each part of a cycle is paid exactly once.</summary>
        public string cycleId;
        public string sheepTypeId = "sheep_fluffy_default";
        public int woolSurfaceVersion;
        public int maskWidth;
        public int maskHeight;
        /// <summary>RLE + base64 authoritative occupancy.</summary>
        public string cutMask;
        /// <summary>Reserved for future regrowth (spec: future-compatible).</summary>
        public int[] localGrowthStages = Array.Empty<int>();
        /// <summary>Unique cut area already committed (normalized 0..1).</summary>
        public float creditedCutArea;
        /// <summary>Economy hook: yield already credited. Not balanced here (economy out of scope).</summary>
        public float creditedYield;
        public SheepCompletionFlags completionFlags;
        public int commitCount;
        public string lastCommitUtc;

        // ---- save metadata (diagnostics + compatibility); missing in legacy files = default values
        /// <summary>Save file format version.</summary>
        public int saveSchemaVersion;
        /// <summary><see cref="DefaultGameState.SaveEpoch"/> at creation. Lower than the build's epoch = ignored.</summary>
        public int saveEpoch;
        public string createdUtc;
        public string createdAppVersion;
        public string lastAppVersion;
        /// <summary>"Editor" or the runtime platform. Lets support/QA see where a save was produced.</summary>
        public string platform;
    }

    /// <summary>
    /// PLAYER SAVE STATE storage. Files live only in Application.persistentDataPath (the app's private sandbox on
    /// Android/iOS), which is never part of a build. The Editor writes to a separate folder, so developer test
    /// sessions can never mix with player data even when they share a machine (e.g. macOS standalone builds).
    /// </summary>
    public static class SheepWoolStateStore
    {
        public const int CurrentSchema = 2;

        static string Root => Path.Combine(Application.persistentDataPath, "ShearAndGrow");
        /// <summary>Location used by versions before schema 2 (kept only for migration).</summary>
        static string LegacyDirectory => Path.Combine(Root, "Sheep");

        public static string Directory => Path.Combine(Root, Application.isEditor ? "EditorSaves" : "Saves", "Sheep");

        public static string LastLoadInfo { get; private set; } = "";

        static string PathFor(string sheepId) => Path.Combine(Directory, sheepId + ".json");

        /// <summary>
        /// Moves saves from the legacy location into the current one (app update keeps progress). Idempotent:
        /// never overwrites a newer file in the current location.
        /// </summary>
        public static void MigrateLegacy()
        {
            try
            {
                if (!System.IO.Directory.Exists(LegacyDirectory)) return;
                System.IO.Directory.CreateDirectory(Directory);
                foreach (var file in System.IO.Directory.GetFiles(LegacyDirectory, "*.json"))
                {
                    string dst = Path.Combine(Directory, Path.GetFileName(file));
                    if (!File.Exists(dst)) File.Move(file, dst);
                }
                if (System.IO.Directory.GetFiles(LegacyDirectory).Length == 0) System.IO.Directory.Delete(LegacyDirectory);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Shearing] Legacy save migration failed: " + e.Message);
            }
        }

        public static bool TryLoad(string sheepId, int minEpoch, out SheepWoolState state)
        {
            state = null;
            MigrateLegacy();
            string path = PathFor(sheepId);
            if (!File.Exists(path)) { LastLoadInfo = "no save (new player state)"; return false; }
            try
            {
                state = JsonUtility.FromJson<SheepWoolState>(File.ReadAllText(path));
                if (state == null || state.sheepId != sheepId) { LastLoadInfo = "save rejected: wrong id"; state = null; return false; }
                if (state.saveEpoch < minEpoch)
                {
                    LastLoadInfo = $"save rejected: epoch {state.saveEpoch} < build epoch {minEpoch}";
                    Debug.Log("[Shearing] " + LastLoadInfo + " -> starting from the default game state.");
                    state = null;
                    return false;
                }
                LastLoadInfo = $"loaded (schema {state.saveSchemaVersion}, created {state.createdUtc ?? "legacy"} on {state.platform ?? "?"})";
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Shearing] Could not read sheep state '{sheepId}': {e.Message}");
                LastLoadInfo = "save unreadable";
                return false;
            }
        }

        /// <summary>Fills metadata of a brand-new save.</summary>
        public static void Stamp(SheepWoolState state, int epoch)
        {
            state.saveSchemaVersion = CurrentSchema;
            state.saveEpoch = epoch;
            state.createdUtc = DateTime.UtcNow.ToString("o");
            state.createdAppVersion = Application.version;
            state.platform = Application.isEditor ? "Editor" : Application.platform.ToString();
        }

        /// <summary>Development-only: removes every sheep save of this install (current + legacy location).</summary>
        public static int DeleteAll()
        {
            int n = 0;
            foreach (var dir in new[] { Directory, LegacyDirectory })
            {
                if (!System.IO.Directory.Exists(dir)) continue;
                foreach (var f in System.IO.Directory.GetFiles(dir)) { File.Delete(f); n++; }
            }
            return n;
        }

        /// <summary>Atomic save: write temp file then replace.</summary>
        public static void Save(SheepWoolState state)
        {
            state.saveSchemaVersion = CurrentSchema;
            state.lastAppVersion = Application.version;
            if (string.IsNullOrEmpty(state.platform)) state.platform = Application.isEditor ? "Editor" : Application.platform.ToString();
            System.IO.Directory.CreateDirectory(Directory);
            string path = PathFor(state.sheepId);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(state));
            if (File.Exists(path))
            {
                string bak = path + ".bak";
                File.Replace(tmp, path, bak);
                if (File.Exists(bak)) File.Delete(bak);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        public static void Delete(string sheepId)
        {
            string path = PathFor(sheepId);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
