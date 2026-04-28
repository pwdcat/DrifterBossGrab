#nullable enable
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Core
{
    public static class MultiTeleporterTracker
    {
        private static List<TeleporterInteraction> _cachedTeleporters = new();
        private static bool _teleportersDirty = true;
        private static TeleporterInteraction? _primaryTeleporter;
        private static readonly HashSet<TeleporterInteraction> _secondaryTeleporters = new();
        private static readonly HashSet<TeleporterInteraction> _pendingInit = new();
        private static readonly object _lock = new object();

        public static void RegisterPrimary(TeleporterInteraction teleporter)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return;
            if (teleporter == null) return;
            lock (_lock)
            {
                _primaryTeleporter = teleporter;
                Log.Info($"[MultiTeleporterTracker] Registered primary teleporter: {teleporter.gameObject.name} (InstanceID: {teleporter.GetInstanceID()})");
            }
        }

        public static void RegisterSecondary(TeleporterInteraction teleporter)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return;
            if (teleporter == null) return;
            lock (_lock)
            {
                // Ensure it's not the primary
                if (teleporter == _primaryTeleporter) return;

                _secondaryTeleporters.Add(teleporter);
                Log.Info($"[MultiTeleporterTracker] Registered secondary teleporter: {teleporter.gameObject.name} (InstanceID: {teleporter.GetInstanceID()})");
            }
        }

        public static void UnregisterSecondary(TeleporterInteraction teleporter)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return;
            if (teleporter == null) return;
            lock (_lock)
            {
                if (_secondaryTeleporters.Remove(teleporter))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[MultiTeleporterTracker] Unregistered secondary teleporter: {teleporter.gameObject.name}");
                    }
                }
            }
        }

        public static TeleporterInteraction? GetPrimary()
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return null;
            lock (_lock)
            {
                return _primaryTeleporter;
            }
        }



        public static bool IsSecondary(TeleporterInteraction teleporter)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return false;
            lock (_lock)
            {
                return _secondaryTeleporters.Contains(teleporter);
            }
        }



        public static void MarkPendingInit(TeleporterInteraction teleporter)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return;
            if (teleporter == null) return;
            lock (_lock) { _pendingInit.Add(teleporter); }
        }





        public static void Clear()
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return;
            lock (_lock)
            {
                _primaryTeleporter = null;
                _secondaryTeleporters.Clear();
                _pendingInit.Clear();
                InvalidateCache();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[MultiTeleporterTracker] Cleared teleporter registry");
                }
            }
        }

        public static void InvalidateCache()
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return;
            lock (_lock)
            {
                _teleportersDirty = true;
            }
        }

        public static List<TeleporterInteraction> GetTeleporters()
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value) return new List<TeleporterInteraction>();
            lock (_lock)
            {
                if (_teleportersDirty)
                {
                    _cachedTeleporters = UnityEngine.Object.FindObjectsByType<TeleporterInteraction>(FindObjectsSortMode.None).ToList();
                    _teleportersDirty = false;
                }
                return _cachedTeleporters;
            }
        }
    }
}
