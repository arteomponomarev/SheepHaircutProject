using ShearAndGrow.Economy;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>Coins and Dirty / Wet / Clean wool through <see cref="DevBackends.Economy"/> (never the HUD text).</summary>
    public sealed class EconomyTab : DevTab
    {
        public override string Title => "Economy";
        Text _summary;

        static string EconomyReady() => WoolEconomy.IsReady ? null : "Economy not initialised";

        public override void RegisterCommands()
        {
            foreach (long amount in new long[] { 100, 1000, 10000 })
            {
                long a = amount;
                DevCommands.Register(new DevCommand { Id = "economy.coins.add" + a, Label = "Add " + a.ToString("N0") + " Coins", Category = Title, Availability = EconomyReady,
                    Execute = _ => DevBackends.Economy.AddCoins(a) });
            }
            DevCommands.Register(new DevCommand { Id = "economy.coins.set", Label = "Set Coins", Category = Title, Availability = EconomyReady,
                Execute = arg => DevCommands.TryParseLong(arg, 0, WoolEconomy.DevMaxCoins, out long v, out string err) ? DevBackends.Economy.SetCoins(v) : DevResult.Fail(err) });
            DevCommands.Register(new DevCommand { Id = "economy.coins.reset", Label = "Reset Coins", Category = Title, Availability = EconomyReady,
                Execute = _ => DevBackends.Economy.ResetCoins() });

            for (int i = 0; i < 3; i++)
            {
                var t = (WoolType)i;
                string key = "economy.wool." + t.ToString().ToLowerInvariant();
                DevCommands.Register(new DevCommand { Id = key + ".add10", Label = "Add 10 " + t + " Wool", Category = Title, Availability = EconomyReady, Execute = _ => DevBackends.Economy.AddWool(t, 10) });
                DevCommands.Register(new DevCommand { Id = key + ".add100", Label = "Add 100 " + t + " Wool", Category = Title, Availability = EconomyReady, Execute = _ => DevBackends.Economy.AddWool(t, 100) });
                DevCommands.Register(new DevCommand { Id = key + ".set", Label = "Set " + t + " Wool", Category = Title, Availability = EconomyReady,
                    Execute = arg => DevCommands.TryParseLong(arg, 0, WoolEconomy.DevMaxWool, out long v, out string err) ? DevBackends.Economy.SetWool(t, (int)v) : DevResult.Fail(err) });
                DevCommands.Register(new DevCommand { Id = key + ".clear", Label = "Clear " + t + " Wool", Category = Title, Availability = EconomyReady, Execute = _ => DevBackends.Economy.SetWool(t, 0) });
            }
        }

        public override void Build(DevUi ui)
        {
            _summary = ui.Label("", 30);
            ui.Label("Backend: " + DevBackends.Economy.Name + "  ·  changes are saved atomically and every HUD updates through WoolEconomy.Changed.", 24, Processing.UiKit.TextMuted);

            ui.Header("Coins");
            ui.Buttons(("+100", () => Run("economy.coins.add100")), ("+1,000", () => Run("economy.coins.add1000")), ("+10,000", () => Run("economy.coins.add10000")));
            ui.InputWithButton("Exact amount", InputField.ContentType.IntegerNumber, "Set Coins", v => Run("economy.coins.set", v));
            ui.Button("Reset Coins (starting value)", () => Run("economy.coins.reset"), DevUi.Danger);

            for (int i = 0; i < 3; i++)
            {
                var t = (WoolType)i;
                string key = "economy.wool." + t.ToString().ToLowerInvariant();
                ui.Header(t + " Wool");
                ui.Buttons(("+10", () => Run(key + ".add10")), ("+100", () => Run(key + ".add100")), ("Clear", () => Run(key + ".clear")));
                ui.InputWithButton("Exact amount", InputField.ContentType.IntegerNumber, "Set", v => Run(key + ".set", v));
            }
            ui.Label("Debug wool never comes from sheep: shearing payouts keep using the per-sheep ledger (no duplicates).", 24, Processing.UiKit.TextMuted);
        }

        public override void Refresh()
        {
            if (_summary == null) return;
            var e = WoolEconomy.Instance;
            _summary.text = e == null ? "Economy not initialised" :
                "Coins " + e.Coins.ToString("N0") + "   ·   Dirty " + e.Get(WoolType.Dirty) + "   Wet " + e.Get(WoolType.Wet) + "   Clean " + e.Get(WoolType.Clean) +
                (e.OpenBatch != null ? "\n(" + e.OpenBatch.Amount + " " + e.OpenBatch.Input + " reserved by a running mini-game)" : "");
        }
    }
}
