using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoR2;
using RoR2.Projectile;

namespace DrifterBossGrabMod
{
    public static class PersistenceObjectManager
    {
        // Singleton instance
        private static GameObject? _persistenceContainer;
        private static readonly HashSet<GameObject> _persistedObjects = new HashSet<GameObject>();
        private static readonly object _lock = new object();
        private static readonly PersistenceCommandInvoker _commandInvoker = new PersistenceCommandInvoker();
        // Cached config values for performance
        private static bool _cachedEnablePersistence;
        private static bool _cachedEnableAutoGrab;
        // Constants
        private const string PERSISTENCE_CONTAINER_NAME = "DBG_PersistenceContainer";

        // Initialization
        public static void Initialize()
        {
            if (_persistenceContainer != null) return;
            _persistenceContainer = new GameObject(PERSISTENCE_CONTAINER_NAME);
            UnityEngine.Object.DontDestroyOnLoad(_persistenceContainer);
        }

        // Cleanup
        public static void Cleanup()
        {
            ClearPersistedObjects();
            if (_persistenceContainer != null)
            {
                UnityEngine.Object.Destroy(_persistenceContainer);
                _persistenceContainer = null!;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[PersistenceObjectManager.Cleanup] PersistenceObjectManager cleaned up");
            }
        }

        // Update cached configuration values
        public static void UpdateCachedConfig()
        {
            _cachedEnablePersistence = PluginConfig.Instance.EnableObjectPersistence.Value;
            _cachedEnableAutoGrab = PluginConfig.Instance.EnableAutoGrab.Value;
        }

        // Add object to persistence
        public static void AddPersistedObject(GameObject obj)
        {
            var command = new AddPersistedObjectCommand(obj);
            _commandInvoker.ExecuteCommand(command);
        }

        // Internal method for adding object to persistence (used by commands)
        internal static void AddPersistedObjectInternal(GameObject obj)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[AddPersistedObject] AddPersistedObject called for {obj?.name ?? "null"} - EnablePersistence: {_cachedEnablePersistence}");
            }
            if (obj == null || !_cachedEnablePersistence)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AddPersistedObject] Object is null or persistence disabled - cannot add to persistence");
                }
                return;
            }
            lock (_lock)
            {
                if (_persistedObjects.Add(obj))
                {
                    // Move to persistence container
                    obj.transform.SetParent(_persistenceContainer!.transform, true);
                    // Also persist the model if it exists and ModelLocator is enabled
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.enabled && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;
                        if (modelObj != obj) // Only if it's a separate object
                        {
                            modelObj.transform.SetParent(_persistenceContainer.transform, true);
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[AddPersistedObject] Also persisted model {modelObj.name} for {obj.name}");
                            }
                        }
                    }
                    // Also persist the master for CharacterBody objects
                    var characterBody = obj.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.master != null && characterBody.master.gameObject != null && IsValidForPersistence(characterBody.master.gameObject))
                    {
                        AddPersistedObjectInternal(characterBody.master.gameObject);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[AddPersistedObject] Also persisted master {characterBody.master.name} for {obj.name}");
                        }
                    }
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AddPersistedObject] Successfully added {obj.name} to persistence (total: {_persistedObjects.Count})");
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[AddPersistedObject] Object {obj.name} was already in persisted objects set");
                    }
                }
            }
        }

        // Remove object from persistence
        public static void RemovePersistedObject(GameObject obj)
        {
            var command = new RemovePersistedObjectCommand(obj);
            _commandInvoker.ExecuteCommand(command);
        }

        // Internal method for removing object from persistence (used by commands)
        internal static void RemovePersistedObjectInternal(GameObject obj)
        {
            if (obj == null) return;
            lock (_lock)
            {
                if (_persistedObjects.Remove(obj))
                {
                    // Remove from persistence container
                    obj.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());
                    // Also remove model from persistence if it exists
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;
                        if (modelObj.transform.parent == _persistenceContainer!.transform)
                        {
                            modelObj.transform.SetParent(null, true);
                            SceneManager.MoveGameObjectToScene(modelObj, SceneManager.GetActiveScene());
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[RemovePersistedObject] Also removed model {modelObj.name} from persistence for {obj.name}");
                            }
                        }
                    }
                    // Also remove master from persistence if it exists
                    var characterBody = obj.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.master != null && characterBody.master.gameObject != null)
                    {
                        var masterObj = characterBody.master.gameObject;
                        if (masterObj.transform.parent == _persistenceContainer!.transform)
                        {
                            masterObj.transform.SetParent(null, true);
                            SceneManager.MoveGameObjectToScene(masterObj, SceneManager.GetActiveScene());
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[RemovePersistedObject] Also removed master {masterObj.name} from persistence for {obj.name}");
                            }
                        }
                    }
                    // Re-attach model if it exists
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var temp = modelLocator.modelTransform;
                        modelLocator.modelTransform = null;
                        modelLocator.modelTransform = temp;
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RemovePersistedObject] Re-attached model for {obj.name} after removal from persistence");
                        }
                    }
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[RemovePersistedObject] Removed {obj.name} from persistence (total: {_persistedObjects.Count})");
                    }
                }
            }
        }

        // Clear all persisted objects
        public static void ClearPersistedObjects()
        {
            var command = new ClearPersistedObjectsCommand();
            _commandInvoker.ExecuteCommand(command);
        }

        // Internal method for clearing all persisted objects (used by commands)
        internal static void ClearPersistedObjectsInternal()
        {
            lock (_lock)
            {
                foreach (var obj in _persistedObjects.ToArray())
                {
                    if (obj != null)
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                }
                _persistedObjects.Clear();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ClearPersistedObjects] Cleared all persisted objects");
                }
            }
        }

        // Check if object is valid for persistence
        public static bool IsValidForPersistence(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }
            // Check if already persisted
            if (_persistedObjects.Contains(obj))
            {
                return false;
            }
            // Always exclude thrown objects from persistence
            var projectileController = obj.GetComponent<ThrownObjectProjectileController>();
            if (projectileController != null)
            {
                return false;
            }
            // Check blacklist
            if (PluginConfig.IsBlacklisted(obj.name))
            {
                return false;
            }
            return true;
        }

        // Move objects to persistence container
        public static void MoveObjectsToPersistenceContainer()
        {
            lock (_lock)
            {
                foreach (var obj in _persistedObjects)
                {
                    if (obj != null && obj.transform.parent != _persistenceContainer!.transform)
                    {
                        obj.transform.SetParent(_persistenceContainer!.transform, true);
                    }
                }
            }
        }

        // Capture currently bagged objects
        public static void CaptureCurrentlyBaggedObjects()
        {
            if (!_cachedEnablePersistence)
            {
                return;
            }
            // Always capture currently bagged objects for persistence
            var baggedObjects = GetCurrentlyBaggedObjects();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CaptureCurrentlyBaggedObjects] Capturing {baggedObjects.Count} currently bagged objects for persistence");
            }
            foreach (var obj in baggedObjects)
            {
                if (IsValidForPersistence(obj))
                {
                    AddPersistedObject(obj);
                }
            }
        }

        // Get all objects currently in Drifter bags
        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            // Use the centralized detection method
            return Patches.BaggedObjectsOnlyDetection.GetCurrentlyBaggedObjects();
        }

        // Get current persisted objects count
        public static int GetPersistedObjectsCount()
        {
            lock (_lock)
            {
                return _persistedObjects.Count;
            }
        }

        // Get current persisted objects
        public static GameObject[] GetPersistedObjects()
        {
            lock (_lock)
            {
                return _persistedObjects.ToArray();
            }
        }

        // Check if object is persisted
        public static bool IsObjectPersisted(GameObject obj)
        {
            lock (_lock)
            {
                return _persistedObjects.Contains(obj);
            }
        }

        // Undo the last persistence command
        public static void UndoLastCommand()
        {
            _commandInvoker.UndoLastCommand();
        }

        // Get the command history count
        public static int GetCommandHistoryCount()
        {
            return _commandInvoker.GetHistoryCount();
        }

        // Clear command history
        public static void ClearCommandHistory()
        {
            _commandInvoker.ClearHistory();
        }

        // Get persisted objects (internal access for other handlers)
        internal static HashSet<GameObject> GetPersistedObjectsSet()
        {
            return _persistedObjects;
        }

        // Get lock object (internal access for other handlers)
        internal static object GetLock()
        {
            return _lock;
        }

        // Get cached enable auto grab
        internal static bool GetCachedEnableAutoGrab()
        {
            return _cachedEnableAutoGrab;
        }

        // Get cached enable persistence
        internal static bool GetCachedEnablePersistence()
        {
            return _cachedEnablePersistence;
        }

    }
}
