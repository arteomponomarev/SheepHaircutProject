using ShearAndGrow.Economy;
using ShearAndGrow.Farm;
using UnityEngine;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Who may run developer commands. Layer 1 (strongest): this whole assembly only exists in the Editor and in
    /// Development builds (asmdef define constraint) – release builds contain no panel, no commands, no dev economy
    /// methods (those are #if-compiled out of WoolEconomy / BuildingService / FarmSheepStates too). Layer 2: a runtime
    /// re-check here. Layer 3 (future backend): when the economy is server-authoritative, client-side economy
    /// commands refuse and the server test functions must authorise the call (test accounts / test title only).
    /// </summary>
    public static class DevAccess
    {
        public static bool IsAllowed(out string reason)
        {
            reason = null;
            if (Application.isEditor || Debug.isDebugBuild) return true;
            reason = "Developer tools are disabled in this build";
            return false;
        }

        public static string BuildType => Application.isEditor ? "Editor" : Debug.isDebugBuild ? "Development build" : "Release build";
    }

    /// <summary>Economy operations of the current environment (local authority today, server test functions later).</summary>
    public interface IDevEconomyBackend
    {
        string Name { get; }
        DevResult SetCoins(long value);
        DevResult AddCoins(long amount);
        DevResult ResetCoins();
        DevResult SetWool(WoolType type, int value);
        DevResult AddWool(WoolType type, int amount);
    }

    /// <summary>Resets the TEST farm of the current environment.</summary>
    public interface IDevFarmResetBackend
    {
        string Name { get; }
        string Availability();
        DevResult ResetToFreshSave();
    }

    /// <summary>
    /// Local prototype backend: operations go through <see cref="WoolEconomy"/> (the single runtime authority, atomic
    /// save, Changed event → every HUD updates). Refuses if a server-authoritative backend is ever connected, so a
    /// local cheat can never diverge from a server economy.
    /// </summary>
    public sealed class LocalDevEconomyBackend : IDevEconomyBackend
    {
        public string Name => "Local WoolEconomy (device save)";

        static bool Ready(out WoolEconomy e, out DevResult fail)
        {
            e = WoolEconomy.Instance; fail = default;
            if (BackendStatus.ServerAuthoritative) { fail = DevResult.Fail("Economy is server-authoritative – use the authorised server test functions"); return false; }
            if (e == null) { fail = DevResult.Fail("Economy not initialised"); return false; }
            return true;
        }

        public DevResult SetCoins(long value)
        {
            if (!Ready(out var e, out var f)) return f;
            return e.DevSetCoins(value) ? DevResult.Success("Coins = " + e.Coins.ToString("N0")) : DevResult.Fail("Economy save failed");
        }

        public DevResult AddCoins(long amount)
        {
            if (!Ready(out var e, out var f)) return f;
            if (amount <= 0) return DevResult.Fail("Amount must be positive");
            return e.DevAddCoinsClamped(amount) ? DevResult.Success("+" + amount.ToString("N0") + " coins → " + e.Coins.ToString("N0")) : DevResult.Fail("Economy save failed");
        }

        public DevResult ResetCoins()
        {
            if (!Ready(out var e, out var f)) return f;
            return e.DevResetCoins() ? DevResult.Success("Coins reset to starting value " + e.Coins.ToString("N0")) : DevResult.Fail("Economy save failed");
        }

        public DevResult SetWool(WoolType type, int value)
        {
            if (!Ready(out var e, out var f)) return f;
            return e.DevSetWool(type, value) ? DevResult.Success(type + " Wool = " + e.Get(type)) : DevResult.Fail("Economy save failed");
        }

        public DevResult AddWool(WoolType type, int amount)
        {
            if (!Ready(out var e, out var f)) return f;
            if (amount <= 0) return DevResult.Fail("Amount must be positive");
            return e.DevAddWoolClamped(type, amount) ? DevResult.Success("+" + amount + " " + type + " Wool → " + e.Get(type)) : DevResult.Fail("Economy save failed");
        }
    }

    /// <summary>
    /// Local reset of the device's TEST saves. Only from the Farm Hub (other scenes keep sheep / batches in memory and
    /// would write them back). There is no server account to touch: PlayFab is not integrated.
    /// </summary>
    public sealed class LocalFarmResetBackend : IDevFarmResetBackend
    {
        public string Name => "Local device saves";

        public string Availability()
        {
            if (BackendStatus.ServerAuthoritative) return "Server-backed farm – needs an authorised server test reset";
            if (FarmHubController.Active == null) return "Open the Farm Hub first";
            return null;
        }

        public DevResult ResetToFreshSave()
        {
            var hub = FarmHubController.Active;
            if (hub == null) return DevResult.Fail("Open the Farm Hub first");
            return hub.DevResetFarmToFreshSave() ? DevResult.Success("Farm reset – reloading as a new player") : DevResult.Fail("Farm is busy (leaving / loading)");
        }
    }

    /// <summary>Active environment backends (swap in server-backed implementations when the backend exists).</summary>
    public static class DevBackends
    {
        public static IDevEconomyBackend Economy = new LocalDevEconomyBackend();
        public static IDevFarmResetBackend FarmReset = new LocalFarmResetBackend();
    }
}
