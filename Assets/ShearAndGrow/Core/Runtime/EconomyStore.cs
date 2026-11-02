using System;
using System.IO;
using UnityEngine;

namespace ShearAndGrow.Economy
{
    /// <summary>
    /// Atomic JSON persistence of <see cref="PlayerEconomyState"/> in Application.persistentDataPath (never part of a
    /// build). Editor and players use separate folders. Writes go to a temp file that replaces the main file; the
    /// previous version is kept as .bak and used if the main file is ever unreadable (interrupted write).
    /// </summary>
    public static class EconomyStore
    {
        public const int CurrentSchema = 1;

        /// <summary>Overrides the root folder (tests). Null = default.</summary>
        public static string RootOverride;

        static string Root => RootOverride ?? Path.Combine(Application.persistentDataPath, "ShearAndGrow", Application.isEditor ? "EditorSaves" : "Saves");
        public static string FilePath => Path.Combine(Root, "economy.json");
        static string BackupPath => FilePath + ".bak";
        static string TempPath => FilePath + ".tmp";

        public static string LastLoadInfo { get; private set; } = "";

        public static bool TryLoad(int minEpoch, out PlayerEconomyState state)
        {
            state = null;
            if (TryRead(FilePath, out state, out string why))
            {
                if (state.saveEpoch < minEpoch) { LastLoadInfo = $"economy save ignored: epoch {state.saveEpoch} < {minEpoch}"; state = null; return false; }
                LastLoadInfo = "economy loaded";
                return true;
            }
            if (TryRead(BackupPath, out state, out string whyBak))
            {
                if (state.saveEpoch < minEpoch) { state = null; LastLoadInfo = "backup ignored (epoch)"; return false; }
                LastLoadInfo = "economy restored from backup (" + why + ")";
                Debug.LogWarning("[Economy] " + LastLoadInfo);
                return true;
            }
            LastLoadInfo = File.Exists(FilePath) || File.Exists(BackupPath) ? "economy unreadable: " + why + " / " + whyBak : "no economy save (new player)";
            return false;
        }

        static bool TryRead(string path, out PlayerEconomyState state, out string why)
        {
            state = null; why = "";
            if (!File.Exists(path)) { why = "missing"; return false; }
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) { why = "empty"; return false; }
                state = JsonUtility.FromJson<PlayerEconomyState>(json);
                if (state == null || state.schemaVersion <= 0) { why = "invalid"; state = null; return false; }
                if (state.sheepLedger == null) state.sheepLedger = new System.Collections.Generic.List<SheepWoolLedger>();
                if (state.recentBatchIds == null) state.recentBatchIds = new System.Collections.Generic.List<string>();
                return true;
            }
            catch (Exception e)
            {
                why = e.GetType().Name;
                return false;
            }
        }

        public static void Save(PlayerEconomyState state)
        {
            state.schemaVersion = CurrentSchema;
            state.lastSaveUtc = DateTime.UtcNow.ToString("o");
            state.lastAppVersion = Application.version;
            if (string.IsNullOrEmpty(state.platform)) state.platform = Application.isEditor ? "Editor" : Application.platform.ToString();
            Directory.CreateDirectory(Root);
            File.WriteAllText(TempPath, JsonUtility.ToJson(state));
            if (File.Exists(FilePath)) File.Replace(TempPath, FilePath, BackupPath);
            else File.Move(TempPath, FilePath);
        }

        /// <summary>Development only: removes the economy save (main + backup).</summary>
        public static int DeleteAll()
        {
            int n = 0;
            foreach (var p in new[] { FilePath, BackupPath, TempPath })
                if (File.Exists(p)) { File.Delete(p); n++; }
            return n;
        }
    }
}
