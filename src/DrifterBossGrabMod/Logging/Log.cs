using BepInEx.Logging;
using System.Runtime.CompilerServices;
namespace DrifterBossGrabMod
{
    internal static class Log
    {
        private static ILogger _logger = new DebugLogger();
        internal static bool EnableDebugLogs
        {
            get => _logger is DebugLogger;
            set => _logger = value ? new DebugLogger() : new InfoLogger();
        }

        internal static void Init(ManualLogSource logSource)
        {
            // Keep for compatibility, but not used
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Error(object data) => _logger.Log(LogLevel.Error, data.ToString());
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Fatal(object data) => _logger.Log(LogLevel.Fatal, data.ToString());
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Info(object data) => _logger.Log(LogLevel.Info, data.ToString());
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Message(object data) => _logger.Log(LogLevel.Info, data.ToString());
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Warning(object data) => _logger.Log(LogLevel.Warning, data.ToString());
    }
}