#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoR2;
using RoR2.Projectile;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod
{
    // Handles life-cycle and state management for objects that need to survive stage transitions.
    public static class PersistenceObjectManager
    {
        private static GameObject? _persistenceContainer;
        private static readonly HashSet<GameObject> _persistedObjects = new HashSet<GameObject>();
        private static readonly Dictionary<GameObject, string> _persistedObjectOwnerPlayerIds = new Dictionary<GameObject, string>();
        private static readonly Dictionary<GameObject, CharacterMaster> _bodyToMasterMap = new Dictionary<GameObject, CharacterMaster>();
        private static readonly object _lock = new object();
        private static readonly PersistenceCommandInvoker _commandInvoker = new PersistenceCommandInvoker();

        private static bool _cachedEnablePersistence;
        private static bool _cachedEnableAutoGrab;

        private const string PERSISTENCE_CONTAINER_NAME = "DBG_PersistenceContainer";

        // We use a dedicated container in DontDestroyOnLoad to act as a safe harbor for persisted objects.
        public static void Initialize()
        {
            if (_persistenceContainer != null) return;
            _persistenceContainer = new GameObject(PERSISTENCE_CONTAINER_NAME);
            UnityEngine.Object.DontDestroyOnLoad(_persistenceContainer);
        }

        public static void Cleanup()
        {
            ClearPersistedObjects();
            _bodyToMasterMap.Clear();
            if (_persistenceContainer != null)
            {
                UnityEngine.Object.Destroy(_persistenceContainer);
                _persistenceContainer = null;
            }
        }

        public static void UpdateCachedConfig()
        {
            _cachedEnablePersistence = PluginConfig.Instance.EnableObjectPersistence.Value;
            _cachedEnableAutoGrab = PluginConfig.Instance.EnableAutoGrab.Value;
        }

        public static void AddPersistedObject(GameObject obj, string? ownerPlayerId = null)
        {
            if (obj == null) return;

            if (PluginConfig.IsPersistenceBlacklisted(obj))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AddPersistedObject] Refusing to add {obj.name}: Object is blacklisted.");
                }
                return;
            }
            var command = new AddPersistedObjectCommand(obj, ownerPlayerId);
            _commandInvoker.ExecuteCommand(command);
        }

        internal static void AddPersistedObjectInternal(GameObject obj, string? ownerPlayerId = null)
        {
            if (obj == null || !_cachedEnablePersistence) return;

            lock (_lock)
            {
                if (_persistedObjects.Add(obj))
                {
                    // Track who owned this object so we can give it back after scene transitions.
                    if (!string.IsNullOrEmpty(ownerPlayerId))
                    {
                        _persistedObjectOwnerPlayerIds[obj] = ownerPlayerId;
                    }

                    obj.transform.SetParent(_persistenceContainer!.transform, true);
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        modelLocator.dontDetatchFromParent = true;
                        if (modelLocator.modelTransform.parent != obj.transform)
                        {
                            modelLocator.modelTransform.SetParent(obj.transform, true);
                        }
                    }

                    // Also persist the AI master
                    var characterBody = obj.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.master != null && IsValidForPersistence(characterBody.master.gameObject))
                    {
                        AddPersistedObjectInternal(characterBody.master.gameObject, ownerPlayerId);
                    }
                }
            }
        }

        public static void RemovePersistedObject(GameObject obj, bool isDestroying = false)
        {
            var command = new RemovePersistedObjectCommand(obj, isDestroying);
            _commandInvoker.ExecuteCommand(command);
        }

        internal static void RemovePersistedObjectInternal(GameObject obj, bool isDestroying = false)
        {
            if (ReferenceEquals(obj, null)) return;

            lock (_lock)
            {
                if (_persistedObjects.Remove(obj))
                {
                    _persistedObjectOwnerPlayerIds.Remove(obj);

                    if (!isDestroying)
                    {
                        obj.transform.SetParent(null, true);
                        SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());
                    }

                    // Clean up associated master if this was a body.
                    var master = GetMasterForBody(obj);
                    if (master != null && master.gameObject != null)
                    {
                        _persistedObjects.Remove(master.gameObject);
                        _persistedObjectOwnerPlayerIds.Remove(master.gameObject);

                        if (master.gameObject.transform.parent == _persistenceContainer!.transform)
                        {
                            master.gameObject.transform.SetParent(null, true);
                            SceneManager.MoveGameObjectToScene(master.gameObject, SceneManager.GetActiveScene());
                        }
                    }
                    _bodyToMasterMap.Remove(obj);
                }
            }
        }

        public static void ClearPersistedObjects()
        {
            var command = new ClearPersistedObjectsCommand();
            _commandInvoker.ExecuteCommand(command);
        }

        internal static void ClearPersistedObjectsInternal()
        {
            lock (_lock)
            {
                foreach (var obj in _persistedObjects.ToArray())
                {
                    if (obj != null) UnityEngine.Object.Destroy(obj);
                }
                _persistedObjects.Clear();
                _persistedObjectOwnerPlayerIds.Clear();
            }
        }

        // Validation prevents transient objects like projectiles from being saved and causing state bloat.
        public static bool IsValidForPersistence(GameObject obj)
        {
            if (obj == null) return false;
            if (_persistedObjects.Contains(obj)) return false;

            var projectileController = obj.GetComponent<ThrownObjectProjectileController>();
            if (projectileController != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[IsValidForPersistence] Rejected {obj.name}: Is a transient projectile.");
                return false;
            }

            if (PluginConfig.IsPersistenceBlacklisted(obj))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[IsValidForPersistence] Rejected {obj.name}: Matched persistence blacklist.");
                return false;
            }

            return true;
        }

        public static void CaptureCurrentlyBaggedObjects()
        {
            if (!_cachedEnablePersistence) return;

            // Prune existing persisted objects that are blacklisted (config change)
            lock (_lock)
            {
                var toRemove = _persistedObjects.Where(obj => obj != null && PluginConfig.IsPersistenceBlacklisted(obj)).ToList();
                foreach (var obj in toRemove)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[CaptureCurrentlyBaggedObjects] Pruning now-blacklisted object: {obj.name}");
                    RemovePersistedObjectInternal(obj, false);
                }
            }

            // Find all objects held by any Drifter.
            var baggedObjects = PersistenceObjectsTracker.GetCurrentlyBaggedObjects();

            foreach (var obj in baggedObjects)
            {
                if (IsValidForPersistence(obj))
                {
                    var ownerPlayerId = GetBaggedObjectOwnerPlayerId(obj);
                    AddPersistedObject(obj, ownerPlayerId);
                }
            }
        }

        internal static string? GetBaggedObjectOwnerPlayerId(GameObject obj)
        {
            if (obj == null) return null;

            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            if (bagControllers == null) return null;

            foreach (var bagController in bagControllers)
            {
                // Check the primary bag seat.
                if (bagController.vehicleSeat != null && bagController.vehicleSeat.NetworkpassengerBodyObject == obj)
                {
                    var master = bagController.GetComponent<CharacterMaster>() ?? bagController.GetComponentInParent<CharacterMaster>();
                    if (master?.playerCharacterMasterController?.networkUser != null)
                    {
                        return Networking.NetworkUtils.GetPlayerIdString(master.playerCharacterMasterController.networkUser.id);
                    }
                }

                var seatDict = Patches.BagPatches.GetState(bagController).AdditionalSeats;
                if (seatDict != null)
                {
                    foreach (var kvp in seatDict)
                    {
                        if (kvp.Value.NetworkpassengerBodyObject == obj)
                        {
                            var master = bagController.GetComponent<CharacterMaster>() ?? bagController.GetComponentInParent<CharacterMaster>();
                            if (master?.playerCharacterMasterController?.networkUser != null)
                            {
                                return Networking.NetworkUtils.GetPlayerIdString(master.playerCharacterMasterController.networkUser.id);
                            }
                        }
                    }
                }
            }
            return null;
        }

        internal static CharacterMaster? GetMasterForBody(GameObject bodyObj)
        {
            if (bodyObj == null) return null;
            lock (_lock)
            {
                if (_bodyToMasterMap.TryGetValue(bodyObj, out var master)) return master;

                var cb = bodyObj.GetComponent<CharacterBody>();
                if (cb != null && cb.master != null)
                {
                    _bodyToMasterMap[bodyObj] = cb.master;
                    return cb.master;
                }
                return null;
            }
        }

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

        public static bool IsObjectPersisted(GameObject obj)
        {
            lock (_lock) return _persistedObjects.Contains(obj);
        }

        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            return PersistenceObjectsTracker.GetCurrentlyBaggedObjects();
        }

        public static int GetPersistedObjectsCount() { lock (_lock) return _persistedObjects.Count; }

        public static GameObject[] GetPersistedObjects() { lock (_lock) return _persistedObjects.ToArray(); }

        internal static HashSet<GameObject> GetPersistedObjectsSet() => _persistedObjects;

        internal static object GetLock() => _lock;

        internal static string? GetPersistedObjectOwnerPlayerId(GameObject obj)
        {
            lock (_lock) return _persistedObjectOwnerPlayerIds.TryGetValue(obj, out var id) ? id : null;
        }

        internal static bool GetCachedEnablePersistence() => _cachedEnablePersistence;

        internal static bool GetCachedEnableAutoGrab() => _cachedEnableAutoGrab;
    }
}
