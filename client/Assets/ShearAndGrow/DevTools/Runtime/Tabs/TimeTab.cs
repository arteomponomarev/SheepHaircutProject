using System;
using System.Globalization;
using ShearAndGrow.Economy;
using ShearAndGrow.Farm;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Isolated test timeline: shifts <see cref="GameClock"/> (construction timers, RealTime wool growth, feed boosts) and
    /// the GameTime growth counter for THIS install only. The device clock, any server and other players are untouched.
    /// </summary>
    public sealed class TimeTab : DevTab
    {
        public override string Title => "Time & Regrowth";
        Text _clock;

        static FarmHubController Hub => FarmHubController.Active;

        static DevResult Advance(double seconds)
        {
            GameClock.DevAdvance(seconds);
            WoolGrowthClock.DevAdvanceGameSeconds(seconds);
            // apply the jump right away (otherwise systems pick it up on their next tick)
            if (Hub != null)
            {
                if (Hub.SheepStates != null) Hub.SheepStates.TickNow(Time.unscaledTime);
                Hub.RefreshSheepVisuals();
                Hub.Buildings.RefreshConstructionVisuals();
                Hub.RefreshBuildingUi();
            }
            return DevResult.Success("Test time +" + PlacementText.Time(seconds) + "  (offset now " + PlacementText.Time(GameClock.DevOffsetSeconds) + ")");
        }

        public override void RegisterCommands()
        {
            DevCommands.Register(new DevCommand { Id = "time.advance", Label = "Advance Test Time", Category = Title, Execute = arg =>
                DevCommands.TryParseDuration(arg, out double s, out string err) ? Advance(s) : DevResult.Fail(err) });
            DevCommands.Register(new DevCommand { Id = "time.resetOffset", Label = "Reset Test Timeline", Category = Title, Execute = _ =>
            {
                GameClock.DevResetOffset();
                return DevResult.Success("Test timeline back to real time (future-stamped saves resume when real time catches up)");
            }});
        }

        public override void Build(DevUi ui)
        {
            ui.Header("Advance game time");
            ui.Buttons(("+1 min", () => Run("time.advance", "1m")), ("+5 min", () => Run("time.advance", "5m")), ("+30 min", () => Run("time.advance", "30m")));
            ui.Buttons(("+1 hour", () => Run("time.advance", "1h")), ("+24 hours", () => Run("time.advance", "24h")));
            ui.InputWithButton("Custom e.g. 90s, 15m, 3h, 2d", InputField.ContentType.Standard, "Advance", v => Run("time.advance", v));
            ui.Label("Moves only this install's test timeline (GameClock offset) – never the device clock, a server or other players. Used by construction timers, offline wool regrowth and feed boosts.", 22, Processing.UiKit.TextMuted);
            ui.Header("Clock");
            _clock = ui.Label("", 26);
            ui.Button("Reset test timeline to real time", () => Run("time.resetOffset"), DevUi.Danger);
            ui.Label("Per-sheep growth diagnostics are on the Sheep & Wool page.", 22, Processing.UiKit.TextMuted);
        }

        public override void Refresh()
        {
            if (_clock == null) return;
            var growth = Hub != null ? Hub.GrowthConfig : null;
            string mode = growth != null ? growth.TimeMode.ToString() : "?";
            _clock.text =
                "Device UTC: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n" +
                "Effective game time: " + GameClock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC\n" +
                "Test offset: " + PlacementText.Time(GameClock.DevOffsetSeconds) + (GameClock.RollbackHoldSeconds > 1 ? "  ·  clock rollback hold " + PlacementText.Time(GameClock.RollbackHoldSeconds) : "") + "\n" +
                "Time source: " + GameClock.Source.Name + (GameClock.Source.IsTrusted ? " (trusted)" : " (untrusted – local prototype)") + "\n" +
                "Wool growth mode: " + mode + (growth != null ? "  ·  full regrowth " + PlacementText.Time(growth.FullRegrowthDuration) + "  ·  debug ×" + growth.EffectiveMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "  ·  offline cap " + PlacementText.Time(growth.MaxOfflineCatchUp) : "") + "\n" +
                "GameTime counter: " + WoolGrowthClock.GameSeconds.ToString("N0", CultureInfo.InvariantCulture) + " s played";
        }
    }
}
