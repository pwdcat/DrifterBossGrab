#nullable enable
using BepInEx.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
namespace DrifterBossGrabMod
{
    internal static class Log
    {
        private static ManualLogSource? _logger;
        private static bool _enableDebugLogs;

        internal static bool EnableDebugLogs
        {
            get => _enableDebugLogs;
            set => _enableDebugLogs = value;
        }

        internal static void Init(ManualLogSource logSource) => _logger = logSource;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Error(object data) => _logger?.LogError(data.ToString());
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Info(object data) => _logger?.LogInfo(data.ToString());
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Warning(object data) => _logger?.LogWarning(data.ToString());

        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Debug(object data)
        {
            if (_enableDebugLogs && _logger != null)
                _logger.LogDebug(data.ToString());
        }
    }
}
