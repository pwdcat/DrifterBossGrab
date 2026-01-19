using System.Collections.Generic;
using UnityEngine;

namespace DrifterBossGrabMod
{
    // Facade/coordinator class for persistence functionality.
    // Delegates to specialized handlers for network messaging, scene handling, and object management.
    public static class PersistenceManager
    {
        // Tracking for objects that should have TeleporterInteraction disabled
        private static readonly HashSet<GameObject> _teleportersToDisable = new HashSet<GameObject>();
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
        public static void RemovePersistedObject(GameObject obj)
        {
            PersistenceObjectManager.RemovePersistedObject(obj);
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

        // Check if teleporter should be disabled
        public static bool ShouldDisableTeleporter(GameObject obj)
        {
            lock (_teleporterLock)
            {
                return _teleportersToDisable.Contains(obj);
            }
        }

        // Mark teleporter for disabling
        public static void MarkTeleporterForDisabling(GameObject obj)
        {
            if (obj == null) return;
            lock (_teleporterLock)
            {
                _teleportersToDisable.Add(obj);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Marked {obj.name} for teleporter disabling, total marked: {_teleportersToDisable.Count}");
                }
            }
        }

        // Clear teleporter disabling marks
        public static void ClearTeleporterDisablingMarks()
        {
            lock (_teleporterLock)
            {
                _teleportersToDisable.Clear();
            }
        }
    }
}
