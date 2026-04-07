#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoR2;
using RoR2.Projectile;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod
{
    public static class PersistenceObjectManager
    {
        // Singleton instance
        private static GameObject? _persistenceContainer;
        private static readonly HashSet<GameObject> _persistedObjects = new HashSet<GameObject>();
        private static readonly Dictionary<GameObject, string> _persistedObjectOwnerPlayerIds = new Dictionary<GameObject, string>();
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
        }

        // Update cached configuration values
        public static void UpdateCachedConfig()
        {
            _cachedEnablePersistence = PluginConfig.Instance.EnableObjectPersistence.Value;
            _cachedEnableAutoGrab = PluginConfig.Instance.EnableAutoGrab.Value;
        }

        // Add object to persistence
        public static void AddPersistedObject(GameObject obj, string? ownerPlayerId = null)
        {
            var command = new AddPersistedObjectCommand(obj, ownerPlayerId);
            _commandInvoker.ExecuteCommand(command);
        }

        // Internal method for adding object to persistence
        internal static void AddPersistedObjectInternal(GameObject obj, string? ownerPlayerId = null)
        {
            if (obj == null || !_cachedEnablePersistence)
            {
                return;
            }
            lock (_lock)
            {
                if (_persistedObjects.Add(obj))
                {
                    // Store the owner player id if provided
                    if (!string.IsNullOrEmpty(ownerPlayerId))
                    {
                        _persistedObjectOwnerPlayerIds[obj] = ownerPlayerId;
                    }
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
                        }
                    }
                    // Also persist the master for CharacterBody objects
                    var characterBody = obj.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.master != null && characterBody.master.gameObject != null && IsValidForPersistence(characterBody.master.gameObject))
                    {
                        AddPersistedObjectInternal(characterBody.master.gameObject, ownerPlayerId);
                    }
                }
            }
        }

        // Remove object from persistence
        public static void RemovePersistedObject(GameObject obj, bool isDestroying = false)
        {
            var command = new RemovePersistedObjectCommand(obj, isDestroying);
            _commandInvoker.ExecuteCommand(command);
        }

        // Internal method for removing object from persistence (used by commands)
        internal static void RemovePersistedObjectInternal(GameObject obj, bool isDestroying = false)
        {
            if (ReferenceEquals(obj, null)) return;
            lock (_lock)
            {
                if (_persistedObjects.Remove(obj))
                {
                    // Remove from owners dictionary
                    _persistedObjectOwnerPlayerIds.Remove(obj);
                    // Remove from persistence container
                if (!isDestroying)
                {
                    obj.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());
                }
                    // Also remove model from persistence if it exists
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;
                        if (modelObj.transform.parent == _persistenceContainer!.transform)
                        {
                            modelObj.transform.SetParent(null, true);
                            SceneManager.MoveGameObjectToScene(modelObj, SceneManager.GetActiveScene());
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
                        }
                    }
                    // Re-attach model if it exists
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var temp = modelLocator.modelTransform;
                        modelLocator.modelTransform = null;
                        modelLocator.modelTransform = temp;
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
                _persistedObjectOwnerPlayerIds.Clear();
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
            foreach (var obj in baggedObjects)
            {
                if (IsValidForPersistence(obj))
                {
                    // Get the owner player ID from the bagged object
                    var ownerPlayerId = GetBaggedObjectOwnerPlayerId(obj);
                    AddPersistedObject(obj, ownerPlayerId);
                }
            }
        }

        // Get all objects currently in Drifter bags
        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            // Use the centralized tracking system that tracks ALL bagged objects (main + additional seats)
            return PersistenceObjectsTracker.GetCurrentlyBaggedObjects();
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

        // Get the owner player id of a persisted object
        internal static string? GetPersistedObjectOwnerPlayerId(GameObject obj)
        {
            lock (_lock)
            {
                return _persistedObjectOwnerPlayerIds.TryGetValue(obj, out var playerId) ? playerId : null;
            }
        }

        // Get the owner player id of a currently bagged object
        // This is used when capturing objects for persistence to determine which player owns each object
        internal static string? GetBaggedObjectOwnerPlayerId(GameObject obj)
        {
            if (obj == null) return null;

            // Try to find which DrifterBagController currently has this object
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            if (bagControllers == null || bagControllers.Length == 0)
            {
                return null;
            }

            foreach (var bagController in bagControllers)
            {
                // Check if this object is in the bag controller's main seat
                if (bagController.vehicleSeat != null && bagController.vehicleSeat.NetworkpassengerBodyObject  == obj)
                {
                    // Found it in main seat - get player ID from this Drifter
                    var characterMaster = bagController.GetComponent<CharacterMaster>();
                    if (characterMaster == null)
                    {
                        characterMaster = bagController.GetComponentInParent<CharacterMaster>();
                    }
                    if (characterMaster != null && characterMaster.playerCharacterMasterController != null)
                    {
                        var playerId = characterMaster.playerCharacterMasterController.networkUser.id.ToString();
                        return playerId;
                    }
                }

                // Check if this object is in any additional seats
                var seatDict = Patches.BagPatches.GetState(bagController).AdditionalSeats;
                if (seatDict != null)
                {
                    foreach (var kvp in seatDict)
                    {
                        if (kvp.Value.NetworkpassengerBodyObject == obj)
                        {
                            // Found it in additional seat - get player ID from this Drifter
                            var characterMaster = bagController.GetComponent<CharacterMaster>();
                            if (characterMaster == null)
                            {
                                characterMaster = bagController.GetComponentInParent<CharacterMaster>();
                            }
                            if (characterMaster != null && characterMaster.playerCharacterMasterController != null)
                            {
                                var playerId = characterMaster.playerCharacterMasterController.networkUser.id.ToString();
                                return playerId;
                            }
                        }
                    }
                }
            }

            return null;
        }

    }
}
