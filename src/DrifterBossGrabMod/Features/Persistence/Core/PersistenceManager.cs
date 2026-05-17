#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace DrifterBossGrabMod
{
    // Facade/coordinator class for persistence functionality.
    // Delegates to specialized handlers for network messaging, scene handling, and object management.
    public static class PersistenceManager
    {
        // Tracking for objects that are currently inside a Drifter bag
        private static readonly HashSet<GameObject> _teleportersCurrentlyBagged = new HashSet<GameObject>();
        private static readonly object _teleporterLock = new object();

        // Initialization - delegate to object manager
        public static void Initialize()
        {
            PersistenceObjectManager.Initialize();
        }

        // Cleanup - delegate to object manager
        public static void Cleanup()
        {
            PersistenceObjectManager.Cleanup();
        }

        // Update cached configuration values - delegate to object manager
        public static void UpdateCachedConfig()
        {
            PersistenceObjectManager.UpdateCachedConfig();
        }

        // Remove object from persistence - delegate to object manager
        public static void RemovePersistedObject(GameObject obj, bool isDestroying = false)
        {
            PersistenceObjectManager.RemovePersistedObject(obj, isDestroying);
        }

        // Clear all persisted objects - delegate to object manager
        public static void ClearPersistedObjects()
        {
            PersistenceObjectManager.ClearPersistedObjects();
        }

        // Capture currently bagged objects - delegate to object manager
        public static void CaptureCurrentlyBaggedObjects()
        {
            PersistenceObjectManager.CaptureCurrentlyBaggedObjects();
        }

        // Get all objects currently in Drifter bags - delegate to object manager
        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            return PersistenceObjectManager.GetCurrentlyBaggedObjects();
        }

        // Move objects to persistence container - delegate to object manager
        public static void MoveObjectsToPersistenceContainer()
        {
            PersistenceObjectManager.MoveObjectsToPersistenceContainer();
        }

        // Handle scene change - delegate to scene handler
        public static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            PersistenceSceneHandler.Instance.OnSceneChanged(oldScene, newScene);
        }

        // Schedule auto-grab for Drifter - delegate to scene handler
        public static void ScheduleAutoGrab(RoR2.CharacterMaster master)
        {
            PersistenceSceneHandler.Instance.ScheduleAutoGrab(master);
        }

        // Check if teleporter is currently in a bag
        public static bool IsTeleporterCurrentlyBagged(GameObject obj)
        {
            lock (_teleporterLock)
            {
                return _teleportersCurrentlyBagged.Contains(obj);
            }
        }

        // Mark teleporter as currently bagged
        public static void MarkTeleporterAsBagged(GameObject obj)
        {
            if (obj == null) return;
            lock (_teleporterLock)
            {
                _teleportersCurrentlyBagged.Add(obj);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Marked {obj.name} as bagged, total bagged: {_teleportersCurrentlyBagged.Count}");
                }
            }
        }

        // Unmark teleporter as bagged (when ejected)
        public static void UnmarkTeleporterAsBagged(GameObject obj)
        {
            if (obj == null) return;
            lock (_teleporterLock)
            {
                if (_teleportersCurrentlyBagged.Remove(obj))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Unmarked {obj.name} as bagged, total remaining: {_teleportersCurrentlyBagged.Count}");
                    }
                }
            }
        }

    }
}
