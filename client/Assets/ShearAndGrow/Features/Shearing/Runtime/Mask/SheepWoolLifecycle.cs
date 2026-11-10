using System;
using ShearAndGrow.Economy;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Wool lifecycle of one sheep, owned by the sheep (not by a scene). Pure logic, no Unity objects:
    ///  • growth = integer length steps applied to every non-full texel, from (clockNow − stamp) with a carried
    ///    remainder, so applying time in chunks or at once gives identical masks;
    ///  • preserved hairstyles never grow; unlocking resumes from the saved lengths without paying back the pause;
    ///  • legacy saves (schema ≤ 2) are migrated without changing their cut shape or paying anything twice.
    /// </summary>
    public static class SheepWoolLifecycle
    {
        /// <summary>Growth stamp of a save that has never been stamped (JSON-safe, unlike NaN).</summary>
        public const double Unstamped = -1.0;
        /// <summary>
        /// Brings a freshly loaded legacy save up to schema 3. Returns true when something changed.
        /// Binary masks (0/255) are valid length masks already; growth starts "now" (no fake instant regrowth).
        /// </summary>
        public static bool MigrateLoaded(SheepWoolState s)
        {
            if (s.maskEncoding >= 2 && s.saveSchemaVersion >= 3) return false;
            if (s.maskEncoding < 2)
            {
                s.maskEncoding = 2;
                // Everything cut so far was cut exactly once and already paid via creditedCutArea → same basis.
                if (s.lifetimeRemovedWool < s.creditedCutArea) s.lifetimeRemovedWool = s.creditedCutArea;
                s.woolAmountAtSave = 1f - s.creditedCutArea;
                s.growthClockStamp = Unstamped; // stamped on first catch-up with the current clock
                s.growthCarrySeconds = 0;
                s.hairstylePreserved = false;
            }
            return true;
        }

        /// <summary>
        /// Applies growth accumulated since the save's stamp (unless preserved / disabled) and moves the stamp to
        /// <paramref name="clockNow"/>. Returns the number of length steps applied.
        /// </summary>
        public static int CatchUp(SheepWoolState s, ShearMaskAuthority mask, WoolGrowthConfig cfg, double clockNow)
        {
            if (double.IsNaN(s.growthClockStamp) || s.growthClockStamp < 0 || s.growthClockStamp > clockNow + 1)
            {
                // first time / migrated / clock reset (e.g. new install with copied save): start growing from now
                s.growthClockStamp = clockNow;
                s.growthCarrySeconds = 0;
                return 0;
            }
            double from = s.growthClockStamp;
            double delta = clockNow - from;
            s.growthClockStamp = clockNow;
            if (cfg == null || !cfg.RegrowthEnabled || s.hairstylePreserved) { s.growthCarrySeconds = 0; return 0; }
            // feed boost: the part of [from, now] before feedBoostUntil grows × multiplier (split is exact, so
            // chunked and single catch-ups give identical masks)
            double boosted = BoostedSeconds(s, from, clockNow);
            if (cfg.TimeMode == WoolGrowthTimeMode.RealTime && delta > cfg.MaxOfflineCatchUp)
            {
                double k = cfg.MaxOfflineCatchUp / Math.Max(1e-6, delta);
                delta *= k; boosted *= k;
            }
            double effective = delta + boosted * (Math.Max(1.0, s.feedBoostMultiplier) - 1.0);
            double grow = effective * cfg.EffectiveMultiplier + s.growthCarrySeconds;
            double perStep = Math.Max(1e-6, cfg.SecondsPerStep);
            int steps = (int)Math.Floor(grow / perStep);
            if (steps <= 0) { s.growthCarrySeconds = grow; return 0; }
            if (steps >= ShearMaskAuthority.Full)
            {
                steps = ShearMaskAuthority.Full; // fully regrown – carry is meaningless beyond that
                s.growthCarrySeconds = 0;
            }
            else s.growthCarrySeconds = grow - steps * perStep;
            mask.ApplyGrowth(steps);
            return steps;
        }

        /// <summary>Restart the growth clock without growing (end of a shearing session, unlock, after loading).</summary>
        public static void Restamp(SheepWoolState s, double clockNow) { s.growthClockStamp = clockNow; }

        /// <summary>Locks / unlocks the hairstyle. Locking first applies growth up to now (so the frozen shape is current).</summary>
        public static void SetPreserved(SheepWoolState s, ShearMaskAuthority mask, WoolGrowthConfig cfg, double clockNow, bool preserved)
        {
            if (s.hairstylePreserved == preserved) return;
            if (preserved)
            {
                if (mask != null) CatchUp(s, mask, cfg, clockNow);
                s.hairstylePreserved = true;
                s.preservedUtc = DateTime.UtcNow.ToString("o");
                s.growthCarrySeconds = 0;
            }
            else
            {
                s.hairstylePreserved = false;
                s.preservedUtc = null;
                s.growthCarrySeconds = 0;
            }
            s.growthClockStamp = clockNow; // no compensation for the paused period
        }

        /// <summary>Seconds until this mask is fully regrown (0 if full or preserved/disabled → -1).</summary>
        public static float SecondsToFull(ShearMaskAuthority mask, WoolGrowthConfig cfg, bool preserved)
        {
            if (preserved || cfg == null || !cfg.RegrowthEnabled) return -1f;
            int minLen = ShearMaskAuthority.Full;
            var o = mask.Occupancy;
            for (int i = 0; i < o.Length; i++) if (o[i] < minLen) { minLen = o[i]; if (minLen == 0) break; }
            if (minLen >= ShearMaskAuthority.Full) return 0f;
            return (float)((ShearMaskAuthority.Full - minLen) * cfg.SecondsPerStep / Math.Max(1e-3, cfg.EffectiveMultiplier));
        }

        /// <summary>Same as <see cref="SecondsToFull(ShearMaskAuthority, WoolGrowthConfig, bool)"/> but accounting for an active feed boost.</summary>
        public static float SecondsToFull(SheepWoolState s, ShearMaskAuthority mask, WoolGrowthConfig cfg, double clockNow)
        {
            float plain = SecondsToFull(mask, cfg, s.hairstylePreserved);
            if (plain <= 0f) return plain;
            double left = BoostSecondsLeft(s, clockNow), m = Math.Max(1.0, s.feedBoostMultiplier);
            if (left <= 0 || m <= 1.0) return plain;
            return (float)(left * m >= plain ? plain / m : left + (plain - left * m));
        }

        /// <summary>Seconds of [from, to] that fall inside the feed boost window.</summary>
        public static double BoostedSeconds(SheepWoolState s, double from, double to)
        {
            if (s.feedBoostMultiplier <= 1f || s.feedBoostUntil <= from) return 0.0;
            return Math.Max(0.0, Math.Min(to, s.feedBoostUntil) - from);
        }

        public static double BoostSecondsLeft(SheepWoolState s, double clockNow) =>
            s.feedBoostMultiplier > 1f ? Math.Max(0.0, s.feedBoostUntil - clockNow) : 0.0;

        public static bool IsBoosted(SheepWoolState s, double clockNow) => BoostSecondsLeft(s, clockNow) > 0.0;

        /// <summary>
        /// Starts a feed boost. Applies growth up to now first (so earlier time is not boosted retroactively).
        /// Refused for preserved hairstyles (returns false, nothing changes).
        /// </summary>
        public static bool ApplyFeedBoost(SheepWoolState s, ShearMaskAuthority mask, WoolGrowthConfig cfg, double clockNow, float multiplier, float seconds, string purchaseId)
        {
            if (s == null || s.hairstylePreserved) return false;
            if (mask != null) CatchUp(s, mask, cfg, clockNow);
            s.feedBoostMultiplier = Math.Max(1f, multiplier);
            s.feedBoostUntil = clockNow + Math.Max(0f, seconds);
            s.feedPurchaseId = purchaseId;
            return true;
        }
    }
}
