using System;

namespace ShearAndGrow.Economy
{
    /// <summary>
    /// The only link from shipping game code to the Developer Panel. The panel lives in the ShearAndGrow.DevTools
    /// assembly, which has the define constraint "UNITY_EDITOR || DEVELOPMENT_BUILD" and therefore does not exist in
    /// release builds. Game UI asks <see cref="IsAvailable"/> to decide whether to show a DEV entry point; in a
    /// release build nothing ever registers (and <see cref="Register"/> is a no-op), so the entry point never appears.
    /// </summary>
    public static class DevToolsGate
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public const bool CompiledIn = true;
#else
        public const bool CompiledIn = false;
#endif
        static Action _open;

        public static event Action AvailabilityChanged;

        public static bool IsAvailable => CompiledIn && _open != null;

        /// <summary>Called by the DevTools bootstrap (dev builds only).</summary>
        public static void Register(Action open)
        {
            if (!CompiledIn) return;
            _open = open;
            AvailabilityChanged?.Invoke();
        }

        public static void Open() { if (IsAvailable) _open(); }
    }

    public enum BackendKind { LocalDevice = 0, PlayFab = 1 }

    /// <summary>
    /// What is authoritative for economy / progress in this build. PlayFab is NOT integrated yet: every save is a
    /// local file and every economy operation runs in <see cref="WoolEconomy"/>. Diagnostics show this honestly;
    /// a future backend sets these values after a real login.
    /// </summary>
    public static class BackendStatus
    {
        public static BackendKind Kind { get; private set; } = BackendKind.LocalDevice;
        public static bool IsConnected { get; private set; }
        public static bool ServerAuthoritative { get; private set; }
        public static string Detail { get; private set; } = "Offline prototype – PlayFab not integrated; saves are local files";

        /// <summary>Hook for the future backend integration (after authentication).</summary>
        public static void Set(BackendKind kind, bool connected, bool serverAuthoritative, string detail)
        {
            Kind = kind; IsConnected = connected; ServerAuthoritative = serverAuthoritative; Detail = detail ?? "";
        }
    }
}
