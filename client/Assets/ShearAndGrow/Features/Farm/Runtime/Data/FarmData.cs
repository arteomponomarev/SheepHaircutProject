using System;
using System.Collections.Generic;
using System.IO;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    public enum FarmBuildingKind { House = 0, Barn = 1, ProcessingShed = 2, Trader = 3, Decoration = 4 }

    /// <summary>A sheep living on the farm and the pen cell it grazes around.</summary>
    [Serializable]
    public struct FarmSheepSpawn
    {
        public string SheepId;
        public string DisplayName;
        public Vector2 PenOffset;
    }

    /// <summary>Upgrading = completed and functional at its current level while the builder upgrades it.</summary>
    public enum BuildingStatus { UnderConstruction = 0, Completed = 1, Stored = 2, Upgrading = 3 }

    /// <summary>A sheep owned by the player (flock membership). Wool state lives in that sheep's own save file.</summary>
    [Serializable]
    public sealed class HerdEntry
    {
        public string sheepId;
        public string displayName;
        /// <summary>Economy receipt of the purchase (empty for starting sheep).</summary>
        public string purchaseId;
        public string acquiredUtc;
    }

    /// <summary>
    /// SAVED state of one placed building / decoration instance. The single source of truth for its type, level,
    /// grid position and construction. Scene transforms are derived from this and never saved.
    /// </summary>
    [Serializable]
    public sealed class BuildingInstanceState
    {
        public string instanceId;
        public string typeId;
        public int level = 1;
        /// <summary>Min corner of the footprint in grid cells.</summary>
        public Vector2Int cell;
        /// <summary>Prepared for rotation (always 0 in this version).</summary>
        public int rotationSteps;
        public BuildingStatus status = BuildingStatus.Completed;
        /// <summary>UTC ticks (DateTime.Ticks) when construction started / will end.</summary>
        public long constructionStartUtcTicks;
        public long constructionEndUtcTicks;
        public float constructionSeconds;
        /// <summary>Economy receipt id of the purchase that created this instance (empty for starting buildings).</summary>
        public string purchaseId;
        /// <summary>The player has seen the "ready" feedback for this instance.</summary>
        public bool completionSeen = true;
        /// <summary>Level reached when the running upgrade completes (0 = no upgrade).</summary>
        public int upgradeTargetLevel;
        /// <summary>Economy receipt of the latest upgrade (kept after completion for reconciliation).</summary>
        public string upgradePurchaseId;

        public bool IsPlaced => status != BuildingStatus.Stored;
        /// <summary>Completed or upgrading: offers its function at the current level.</summary>
        public bool IsFunctional => status == BuildingStatus.Completed || status == BuildingStatus.Upgrading;
        /// <summary>Occupies the builder (construction or upgrade).</summary>
        public bool IsBuilderBusy => status == BuildingStatus.UnderConstruction || status == BuildingStatus.Upgrading;
        public BuildingInstanceState Clone() => (BuildingInstanceState)MemberwiseClone();
    }

    /// <summary>PLAYER SAVE STATE of the farm world (schema 2): every building / decoration instance.</summary>
    [Serializable]
    public sealed class FarmState
    {
        public int schemaVersion;
        public int saveEpoch;
        public int layoutVersion;
        public List<BuildingInstanceState> instances = new List<BuildingInstanceState>();
        /// <summary>Schema 3: the player's flock (sheep ids are permanent and never reused).</summary>
        public List<HerdEntry> herd = new List<HerdEntry>();
        /// <summary>Schema 3: next number for "sheep_NNN" ids (only increases).</summary>
        public int nextSheepNumber;
        /// <summary>Last time (UTC ticks) the farm was saved while running (clock rollback diagnostics).</summary>
        public long lastSeenUtcTicks;
        public string createdUtc;
        public string lastSaveUtc;

        public BuildingInstanceState Find(string instanceId)
        {
            for (int i = 0; i < instances.Count; i++) if (instances[i].instanceId == instanceId) return instances[i];
            return null;
        }

        public BuildingInstanceState FirstOfType(string typeId)
        {
            for (int i = 0; i < instances.Count; i++) if (instances[i].typeId == typeId) return instances[i];
            return null;
        }

        /// <summary>The new-player farm: ONLY the layout's starting buildings (completed).</summary>
        public static FarmState NewPlayer(FarmLayout layout, int epoch)
        {
            var s = new FarmState
            {
                schemaVersion = FarmStateStore.CurrentSchema, saveEpoch = epoch, layoutVersion = layout.LayoutVersion,
                createdUtc = DateTime.UtcNow.ToString("o"),
            };
            foreach (var b in layout.StartingBuildings)
            {
                var def = layout.Definition(b.TypeId);
                s.instances.Add(new BuildingInstanceState
                {
                    instanceId = b.InstanceId, typeId = b.TypeId, cell = b.Cell, level = def != null ? def.StartLevel : 1,
                    status = BuildingStatus.Completed, completionSeen = true,
                });
            }
            return s;
        }
    }

    /// <summary>Atomic JSON persistence of <see cref="FarmState"/> (same folders and pattern as the other saves).</summary>
    public static class FarmStateStore
    {
        /// <summary>
        /// 2 = instance-based buildings with construction (Stage 4). 3 = + flock (herd, sheep id counter) and
        /// building upgrades (Stage 5). 2 → 3 is migrated in place; schema 1 is discarded (owner's decision).
        /// </summary>
        public const int CurrentSchema = 3;
        public static string RootOverride;
        static string Root => RootOverride ?? Path.Combine(Application.persistentDataPath, "ShearAndGrow", Application.isEditor ? "EditorSaves" : "Saves");
        public static string FilePath => Path.Combine(Root, "farm.json");
        public static string LastLoadInfo { get; private set; }

        public static bool TryLoad(int minEpoch, out FarmState state)
        {
            state = null;
            LastLoadInfo = "no farm save";
            foreach (var p in new[] { FilePath, FilePath + ".bak" })
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    var s = JsonUtility.FromJson<FarmState>(File.ReadAllText(p));
                    if (s == null || s.schemaVersion <= 0) { LastLoadInfo = "unreadable " + Path.GetFileName(p); continue; }
                    if (s.schemaVersion < CurrentSchema && !Migrate(s))
                    {
                        LastLoadInfo = "farm save schema " + s.schemaVersion + " discarded (new building system, clean start)";
                        return false;
                    }
                    if (s.saveEpoch < minEpoch) { LastLoadInfo = "farm save epoch " + s.saveEpoch + " < " + minEpoch + " discarded"; return false; }
                    if (s.instances == null) s.instances = new List<BuildingInstanceState>();
                    if (s.herd == null) s.herd = new List<HerdEntry>();
                    state = s;
                    LastLoadInfo = "loaded " + Path.GetFileName(p) + " (" + s.instances.Count + " instances)";
                    return true;
                }
                catch (Exception e) { Debug.LogWarning("[Farm] Could not read " + p + ": " + e.Message); }
            }
            return false;
        }

        /// <summary>
        /// Migration hook for future schema changes. Schema 1 (pre-construction farm keyed by type id) is
        /// intentionally NOT migrated: the project owner chose a clean start for this version.
        /// </summary>
        static bool Migrate(FarmState s)
        {
            if (s.schemaVersion == 2)
            {
                // New fields take neutral defaults (no upgrades running, levels as saved). The herd is seeded from
                // FarmLayout.Sheep by BuildingService.Load (it needs the layout), keeping sheep_001/002 and their
                // existing wool files.
                if (s.herd == null) s.herd = new List<HerdEntry>();
                s.schemaVersion = 3;
                return true;
            }
            return false;
        }

        public static void Save(FarmState s)
        {
            s.schemaVersion = CurrentSchema;
            s.lastSaveUtc = DateTime.UtcNow.ToString("o");
            Directory.CreateDirectory(Root);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(s));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
            else File.Move(tmp, FilePath);
        }

        public static int DeleteAll()
        {
            int n = 0;
            foreach (var p in new[] { FilePath, FilePath + ".bak", FilePath + ".tmp" }) if (File.Exists(p)) { File.Delete(p); n++; }
            return n;
        }
    }

    /// <summary>What the farm shows about a sheep, read from its persistent shearing save (cheap: no mask decode).</summary>
    public struct FarmSheepInfo
    {
        public string SheepId;
        public string DisplayName;
        /// <summary>Wool still on the sheep, 0..1 (1 = never sheared).</summary>
        public float RemainingWool;
        public bool Finished;
        public bool HasSave;
        /// <summary>Hairstyle locked: shape kept, no regrowth.</summary>
        public bool Preserved;
        /// <summary>Seconds until fully regrown (0 = full, &lt;0 = not growing).</summary>
        public float SecondsToFull;
        /// <summary>Feed boost time left (growth clock seconds) and its multiplier.</summary>
        public float BoostSecondsLeft;
        public float BoostMultiplier;
        public bool Boosted => BoostSecondsLeft > 0f && BoostMultiplier > 1f;

        public int WoolPercent => Mathf.RoundToInt(RemainingWool * 100f);

        public string StatusText =>
            Preserved ? "Hairstyle saved · no regrowth" :
            RemainingWool >= 0.995f ? "Full fleece · ready to shear" :
            SecondsToFull > 0f ? "Regrowing · full in " + FormatEta(SecondsToFull) :
            "Partly sheared";

        public static string FormatEta(float seconds)
        {
            if (seconds < 60f) return Mathf.CeilToInt(seconds) + " s";
            if (seconds < 3600f) return Mathf.CeilToInt(seconds / 60f) + " min";
            return (seconds / 3600f).ToString("0.#") + " h";
        }
    }

    public static class FarmSheepRoster
    {
        public static FarmSheepInfo Read(string sheepId, string displayName, int saveEpoch)
        {
            var info = new FarmSheepInfo { SheepId = sheepId, DisplayName = displayName, RemainingWool = 1f };
            if (SheepWoolStateStore.TryLoad(sheepId, saveEpoch, out var s))
            {
                info.HasSave = true;
                info.RemainingWool = Mathf.Clamp01(1f - s.creditedCutArea);
                info.Finished = (s.completionFlags & SheepCompletionFlags.FinishSettled) != 0;
            }
            return info;
        }
    }
}
