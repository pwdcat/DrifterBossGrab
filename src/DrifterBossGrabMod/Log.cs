using BepInEx.Logging;

namespace DrifterBossGrabMod
{
    internal static class Log
    {
        private static ManualLogSource? _logSource;
        internal static bool EnableDebugLogs { get; set; }

        internal static void Init(ManualLogSource logSource)
        {
            _logSource = logSource;
        }

        internal static void Error(object data) => _logSource?.LogMessage($"[ERROR] {data}");
        internal static void Fatal(object data) => _logSource?.LogMessage($"[FATAL] {data}");
        internal static void Info(object data) { if (EnableDebugLogs) _logSource?.LogMessage($"[INFO] {data}"); }
        internal static void Message(object data) => _logSource?.LogMessage(data);
        internal static void Warning(object data) => _logSource?.LogMessage($"[WARNING] {data}");
    }
}