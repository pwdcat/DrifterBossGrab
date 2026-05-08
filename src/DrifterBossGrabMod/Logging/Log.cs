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
        internal static void DebugIfEnabled(object data)
        {
            if (_enableDebugLogs && _logger != null)
                _logger.LogDebug(data.ToString());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugIfEnabled(string message)
        {
            if (_enableDebugLogs && _logger != null)
                _logger.LogDebug(message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugIfEnabled(string format, params object?[] args)
        {
            if (_enableDebugLogs && _logger != null)
                _logger.LogDebug(string.Format(format, args));
        }
    }
}
