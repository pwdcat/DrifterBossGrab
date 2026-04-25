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

        // Add object to persistence - delegate to object manager
        public static void AddPersistedObject(GameObject obj)
        {
            PersistenceObjectManager.AddPersistedObject(obj);
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

        // Get current persisted objects count - delegate to object manager
        public static int GetPersistedObjectsCount()
        {
            return PersistenceObjectManager.GetPersistedObjectsCount();
        }

        // Get current persisted objects - delegate to object manager
        public static GameObject[] GetPersistedObjects()
        {
            return PersistenceObjectManager.GetPersistedObjects();
        }

        // Undo the last persistence command - delegate to object manager
        public static void UndoLastCommand()
        {
            PersistenceObjectManager.UndoLastCommand();
        }

        // Get the command history count - delegate to object manager
        public static int GetCommandHistoryCount()
        {
            return PersistenceObjectManager.GetCommandHistoryCount();
        }

        // Clear command history - delegate to object manager
        public static void ClearCommandHistory()
        {
            PersistenceObjectManager.ClearCommandHistory();
        }

        // Check if object is persisted - delegate to object manager
        public static bool IsObjectPersisted(GameObject obj)
        {
            return PersistenceObjectManager.IsObjectPersisted(obj);
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

        // Clear all bagged teleporters marks
        public static void ClearBaggedTeleporters()
        {
            lock (_teleporterLock)
            {
                _teleportersCurrentlyBagged.Clear();
            }
        }
    }
}
