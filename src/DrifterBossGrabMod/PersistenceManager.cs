using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoR2;
using RoR2.Projectile;

namespace DrifterBossGrabMod
{
    public static class PersistenceManager
    {
        // Singleton instance
        private static GameObject _persistenceContainer;
        private static readonly HashSet<GameObject> _persistedObjects = new HashSet<GameObject>();
        private static readonly object _lock = new object();

        // Cached config values for performance
        private static bool _cachedEnablePersistence;
        private static bool _cachedEnableAutoGrab;
        private static float _cachedAutoGrabDelay;
        private static int _cachedMaxPersistedObjects;
        private static bool _cachedOnlyPersistCurrentlyBagged;

        // Persistence window tracking for thrown objects
        private static bool _persistenceWindowActive = false;
        private static readonly HashSet<GameObject> _thrownObjectsMarkedForPersistence = new HashSet<GameObject>();

        // Tracking for objects that should have TeleporterInteraction disabled
        private static readonly HashSet<GameObject> _teleportersToDisable = new HashSet<GameObject>();

        // Constants
        private const string PERSISTENCE_CONTAINER_NAME = "DBG_PersistenceContainer";

        // Initialization
        public static void Initialize()
        {
            if (_persistenceContainer != null) return;

            _persistenceContainer = new GameObject(PERSISTENCE_CONTAINER_NAME);
            UnityEngine.Object.DontDestroyOnLoad(_persistenceContainer);

            UpdateCachedConfig();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} PersistenceManager initialized");
            }
        }

        // Cleanup
        public static void Cleanup()
        {
            ClearPersistedObjects();
            if (_persistenceContainer != null)
            {
                UnityEngine.Object.Destroy(_persistenceContainer);
                _persistenceContainer = null;
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} PersistenceManager cleaned up");
            }
        }

        // Update cached configuration values
        public static void UpdateCachedConfig()
        {
            _cachedEnablePersistence = PluginConfig.EnableObjectPersistence.Value;
            _cachedEnableAutoGrab = PluginConfig.EnableAutoGrab.Value;
            _cachedAutoGrabDelay = PluginConfig.AutoGrabDelay.Value;
            _cachedMaxPersistedObjects = PluginConfig.MaxPersistedObjects.Value;
            _cachedOnlyPersistCurrentlyBagged = PluginConfig.OnlyPersistCurrentlyBagged.Value;
        }

        // Add object to persistence
        public static void AddPersistedObject(GameObject obj)
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} AddPersistedObject called for {obj?.name ?? "null"} - EnablePersistence: {_cachedEnablePersistence}");
            }

            if (obj == null || !_cachedEnablePersistence)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Object is null or persistence disabled - cannot add to persistence");
                }
                return;
            }

            lock (_lock)
            {
                if (_persistedObjects.Count >= _cachedMaxPersistedObjects)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Max persisted objects limit reached ({_cachedMaxPersistedObjects}), cannot add {obj.name}");
                    }
                    return;
                }

                if (_persistedObjects.Add(obj))
                {
                    // Move to persistence container
                    obj.transform.SetParent(_persistenceContainer.transform, true);

                    // Capture persistence state
                    var grabbedState = obj.GetComponent<GrabbedObjectState>();
                    if (grabbedState != null)
                    {
                        grabbedState.CapturePersistenceState();
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Captured persistence state for {obj.name}");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No GrabbedObjectState found on {obj.name} - persistence state not captured");
                        }
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Successfully added {obj.name} to persistence (total: {_persistedObjects.Count})");
                    }
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Object {obj.name} was already in persisted objects set");
                    }
                }
            }
        }

        // Remove object from persistence
        public static void RemovePersistedObject(GameObject obj)
        {
            if (obj == null) return;

            lock (_lock)
            {
                if (_persistedObjects.Remove(obj))
                {
                    // Remove from persistence container
                    obj.transform.SetParent(null, true);

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Removed {obj.name} from persistence (total: {_persistedObjects.Count})");
                    }
                }
            }
        }

        // Clear all persisted objects
        public static void ClearPersistedObjects()
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

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Cleared all persisted objects");
                }
            }
        }

        // Activate persistence window (called when teleporter completes)
        public static void ActivatePersistenceWindow()
        {
            _persistenceWindowActive = true;
            _thrownObjectsMarkedForPersistence.Clear(); // Clear any stale marks

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Persistence window activated - thrown objects can now be marked for persistence");
            }
        }

        // Deactivate persistence window (called on scene change)
        public static void DeactivatePersistenceWindow()
        {
            _persistenceWindowActive = false;
            _thrownObjectsMarkedForPersistence.Clear();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Persistence window deactivated");
            }
        }

        // Mark a thrown object for persistence (called when thrown object impacts)
        public static void MarkThrownObjectForPersistence(GameObject obj)
        {
            if (obj == null) return;

            // Special case: persist teleporters immediately when thrown
            if (obj.name.ToLower().Contains("teleporter") && _persistenceWindowActive)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Teleporter {obj.name} thrown - persisting immediately");
                }
                AddPersistedObject(obj);
                return;
            }

            if (!_persistenceWindowActive) return;

            lock (_lock)
            {
                _thrownObjectsMarkedForPersistence.Add(obj);

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Marked thrown object {obj.name} for persistence (total marked: {_thrownObjectsMarkedForPersistence.Count})");
                }
            }
        }

        // Check if persistence window is active
        public static bool IsPersistenceWindowActive()
        {
            return _persistenceWindowActive;
        }

        // Capture currently bagged objects
        public static void CaptureCurrentlyBaggedObjects()
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} CaptureCurrentlyBaggedObjects called - EnablePersistence: {_cachedEnablePersistence}, OnlyPersistCurrentlyBagged: {_cachedOnlyPersistCurrentlyBagged}");
            }

            if (!_cachedEnablePersistence)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Persistence disabled, skipping capture");
                }
                return;
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Starting capture of currently bagged objects");
            }

            // Capture bagged objects if configured to do so
            if (_cachedOnlyPersistCurrentlyBagged)
            {
                var baggedObjects = GetCurrentlyBaggedObjects();
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Found {baggedObjects.Count} bagged objects to potentially persist");
                    for (int i = 0; i < baggedObjects.Count; i++)
                    {
                        Log.Info($"{Constants.LogPrefix} Bagged object {i}: {baggedObjects[i]?.name ?? "null"}");
                    }
                }

                foreach (var obj in baggedObjects)
                {
                    if (IsValidForPersistence(obj))
                    {
                        AddPersistedObject(obj);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Successfully added bagged object {obj.name} to persistence");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Bagged object {obj.name} failed validation for persistence");
                        }
                    }
                }
            }
            else
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} OnlyPersistCurrentlyBagged is false, skipping bagged object capture");
                }
            }

            // Always capture thrown objects marked for persistence
            CaptureMarkedThrownObjects();
        }

        // Capture thrown objects that were marked for persistence
        private static void CaptureMarkedThrownObjects()
        {
            lock (_lock)
            {
                foreach (var obj in _thrownObjectsMarkedForPersistence.ToArray())
                {
                    if (obj != null && IsValidForPersistence(obj))
                    {
                        AddPersistedObject(obj);
                    }
                }
                _thrownObjectsMarkedForPersistence.Clear();
            }
        }

        // Get all objects currently in Drifter bags
        private static List<GameObject> GetCurrentlyBaggedObjects()
        {
            // Use the centralized detection method
            return Patches.BaggedObjectsOnlyDetection.GetCurrentlyBaggedObjects();
        }

        // Check if object is valid for persistence
        private static bool IsValidForPersistence(GameObject obj)
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Checking if {obj?.name ?? "null"} is valid for persistence");
            }

            if (obj == null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Object is null - invalid for persistence");
                }
                return false;
            }

            // Check if already persisted
            if (_persistedObjects.Contains(obj))
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Object {obj.name} is already persisted - skipping");
                }
                return false;
            }

            // If OnlyPersistCurrentlyBagged is enabled, exclude thrown objects
            if (_cachedOnlyPersistCurrentlyBagged)
            {
                var projectileController = obj.GetComponent<ThrownObjectProjectileController>();
                if (projectileController != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Object {obj.name} has ThrownObjectProjectileController - excluding from bagged-only persistence");
                    }
                    return false;
                }
            }

            // Check blacklist
            if (PluginConfig.IsBlacklisted(obj.name))
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Object {obj.name} is blacklisted - invalid for persistence");
                }
                return false;
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Object {obj.name} passed all validation checks");
            }

            return true;
        }

        // Handle scene exit
        public static void OnSceneExit(SceneExitController exitController)
        {
            if (!_cachedEnablePersistence) return;

            CaptureCurrentlyBaggedObjects();

            // Move objects to persistence container
            MoveObjectsToPersistenceContainer();
        }

        // Move objects to persistence container
        public static void MoveObjectsToPersistenceContainer()
        {
            lock (_lock)
            {
                foreach (var obj in _persistedObjects)
                {
                    if (obj != null && obj.transform.parent != _persistenceContainer.transform)
                    {
                        obj.transform.SetParent(_persistenceContainer.transform, true);
                    }
                }
            }
        }

        // Handle scene change
        public static void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} OnSceneChanged called - EnablePersistence: {_cachedEnablePersistence}, from {oldScene.name} to {newScene.name}");
            }

            if (!_cachedEnablePersistence)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Persistence disabled, skipping scene change handling");
                }
                return;
            }

            // Deactivate persistence window when scene changes
            DeactivatePersistenceWindow();


            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Scene changed from {oldScene.name} to {newScene.name}, restoring {GetPersistedObjectsCount()} persisted objects");
            }

            // Delay restoration to ensure player is fully spawned
            // Use a coroutine to wait for the next frame when player bodies are available
            var coroutineRunner = new GameObject("PersistenceCoroutineRunner");
            UnityEngine.Object.DontDestroyOnLoad(coroutineRunner);
            var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
            runner.StartCoroutine(DelayedRestorePersistedObjects());
        }

        // Coroutine to delay restoration until player is ready
        private static System.Collections.IEnumerator DelayedRestorePersistedObjects()
        {
            // Wait one frame for initial scene setup
            yield return null;

            // Wait additional frames until player body is available
            // TODO, revisit. Works for right now
            int maxWaitFrames = 30;
            int framesWaited = 0;

            while (framesWaited < maxWaitFrames)
            {
                var players = PlayerCharacterMasterController.instances;
                if (players.Count > 0)
                {
                    var playerBody = players[0].master.GetBody();
                    if (playerBody != null)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Player body found after {framesWaited} frames, proceeding with restoration");
                        }
                        break;
                    }
                }

                framesWaited++;
                yield return null;
            }

            // Restore persisted objects to new scene
            RestorePersistedObjects();

            // Clean up the coroutine runner
            var runner = UnityEngine.Object.FindObjectOfType<PersistenceCoroutineRunner>();
            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
            }
        }

        // Helper class for running coroutines
        private class PersistenceCoroutineRunner : MonoBehaviour { }

        // Helper class for delayed BossGroup cleanup to avoid InvalidCastException during scene loading
        private class BossGroupCleanupRunner : MonoBehaviour
        {
            private CharacterMaster _characterMaster;
            private string _objectName;

            public void Initialize(CharacterMaster characterMaster, string objectName)
            {
                _characterMaster = characterMaster;
                _objectName = objectName;
                StartCoroutine(DelayedBossGroupCleanup());
            }

            private System.Collections.IEnumerator DelayedBossGroupCleanup()
            {
                // Wait one frame for scene initialization to complete
                yield return null;

                try
                {
                    var characterBody = _characterMaster.GetBody();
                    if (characterBody != null)
                    {
                        var bossGroup = RoR2.BossGroup.FindBossGroup(characterBody);
                        if (bossGroup != null)
                        {
                            bossGroup.ForgetBoss(_characterMaster);
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Removed persisted boss {_objectName} from BossGroup to prevent teleporter interference");
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Error during BossGroup cleanup for {_objectName}: {ex.Message}");
                    }
                }

                // Clean up this runner
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        // Helper class to re-enable teleporter after restoration to prevent FixedUpdate errors
        private class TeleporterEnabler : MonoBehaviour
        {
            private RoR2.TeleporterInteraction _teleporterInteraction;

            public void Initialize(RoR2.TeleporterInteraction teleporterInteraction)
            {
                _teleporterInteraction = teleporterInteraction;
                StartCoroutine(DelayedEnable());
            }

            private System.Collections.IEnumerator DelayedEnable()
            {
                // Wait longer for the teleporter to fully initialize and state machine to settle
                for (int i = 0; i < 10; i++)
                {
                    yield return null;
                }

                if (_teleporterInteraction != null)
                {
                    // Debug: Check teleporter state before re-enabling
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Pre-re-enable check for {_teleporterInteraction.name}:");
                        Log.Info($"{Constants.LogPrefix} - enabled: {_teleporterInteraction.enabled}");
                        Log.Info($"{Constants.LogPrefix} - gameObject.activeInHierarchy: {_teleporterInteraction.gameObject.activeInHierarchy}");
                        Log.Info($"{Constants.LogPrefix} - mainStateMachine: {_teleporterInteraction.mainStateMachine}");
                        if (_teleporterInteraction.mainStateMachine != null)
                        {
                            Log.Info($"{Constants.LogPrefix} - currentState: {_teleporterInteraction.mainStateMachine.state}");
                            Log.Info($"{Constants.LogPrefix} - stateMachine enabled: {_teleporterInteraction.mainStateMachine.enabled}");
                        }
                        Log.Info($"{Constants.LogPrefix} - activationState: {_teleporterInteraction.activationState}");

                        // Check for common null references that might cause FixedUpdate errors
                        var sceneExitController = _teleporterInteraction.GetComponent<RoR2.SceneExitController>();
                        Log.Info($"{Constants.LogPrefix} - SceneExitController: {sceneExitController}");
                        if (sceneExitController != null)
                        {
                            Log.Info($"{Constants.LogPrefix} - SceneExitController enabled: {sceneExitController.enabled}");
                        }
                    }

                    _teleporterInteraction.enabled = true;
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Re-enabled TeleporterInteraction on {_teleporterInteraction.name} after restoration");
                    }
                }

                // Clean up this component
                UnityEngine.Object.Destroy(this);
            }
        }

        // Restore persisted objects
        private static void RestorePersistedObjects()
        {
            lock (_lock)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Starting restoration of {_persistedObjects.Count} persisted objects");
                }

                var objectsToRemove = new List<GameObject>();

                foreach (var obj in _persistedObjects.ToArray())
                {
                    if (obj == null)
                    {
                        objectsToRemove.Add(obj);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Removing null object from persisted objects");
                        }
                        continue;
                    }

                    string objName = obj.name.ToLower();


                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Restoring object {obj.name} to scene (currently parented to: {obj.transform.parent?.name ?? "null"})");
                    }

                    // Move back to scene and remove DontDestroyOnLoad
                    obj.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} After SetParent and MoveGameObjectToScene, {obj.name} is now in scene: {obj.scene.name}, parented to: {obj.transform.parent?.name ?? "null"}");
                    }

                    // Position near player
                    PositionNearPlayer(obj);

                    // Restore persistence state
                    var grabbedState = obj.GetComponent<GrabbedObjectState>();
                    if (grabbedState != null)
                    {
                        grabbedState.RestorePersistenceState();
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Restored persistence state for {obj.name}");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} No GrabbedObjectState found on {obj.name}");
                        }
                    }

                    // Special handling for teleporters and portals
                    HandleSpecialObjectRestoration(obj);

                    // Attempt auto-grab if enabled
                    if (_cachedEnableAutoGrab)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Attempting auto-grab for {obj.name}");
                        }
                        TryAutoGrabObject(obj);
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Auto-regab disabled, skipping auto-grab for {obj.name}");
                        }
                    }

                    // Remove from persistence tracking since object is now in the scene
                    _persistedObjects.Remove(obj);

                    // Mark as available for re-grabbing
                    // Objects are now in the scene and can be grabbed again

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Successfully restored {obj.name} to new scene at position {obj.transform.position}");
                    }
                }

                // Remove null objects
                foreach (var obj in objectsToRemove)
                {
                    _persistedObjects.Remove(obj);
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    int totalProcessed = _persistedObjects.Count + objectsToRemove.Count;
                    Log.Info($"{Constants.LogPrefix} Finished restoring persisted objects. {totalProcessed} total objects processed, {objectsToRemove.Count} null objects removed. Remaining persisted: {_persistedObjects.Count}");
                }
            }
        }

        // Position object near player
        private static void PositionNearPlayer(GameObject obj)
        {
            var players = PlayerCharacterMasterController.instances;
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Positioning object near player. Found {players.Count} player instances.");
            }

            if (players.Count > 0)
            {
                var playerMaster = players[0].master;
                var playerBody = playerMaster.GetBody();

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Player master: {playerMaster}, body: {playerBody}");
                    if (playerBody != null)
                    {
                        Log.Info($"{Constants.LogPrefix} Player position: {playerBody.transform.position}, forward: {playerBody.transform.forward}");
                    }
                }

                if (playerBody != null)
                {
                    // Position very close to player (0.5 units in front)
                    var playerPos = playerBody.transform.position;
                    var playerForward = playerBody.transform.forward;
                    var targetPos = playerPos + playerForward * 0.5f + Vector3.up * 0.5f;

                    obj.transform.position = targetPos;
                    obj.transform.rotation = Quaternion.identity; // Reset rotation

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Positioned {obj.name} at {targetPos} (player was at {playerPos})");
                    }
                }
                else
                {
                    // Fallback: position at scene center or camera position
                    var camera = Camera.main;
                    if (camera != null)
                    {
                        var cameraPos = camera.transform.position;
                        var cameraForward = camera.transform.forward;
                        var fallbackPos = cameraPos + cameraForward * 2f;

                        obj.transform.position = fallbackPos;
                        obj.transform.rotation = Quaternion.identity;

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Used camera fallback positioning for {obj.name} at {fallbackPos}");
                        }
                    }
                    else
                    {
                        // Last resort: position at origin with offset
                        obj.transform.position = new Vector3(0, 1, 0);
                        obj.transform.rotation = Quaternion.identity;

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Used origin fallback positioning for {obj.name}");
                        }
                    }
                }
            }
            else
            {
                // No players found - this shouldn't happen in normal gameplay
                obj.transform.position = new Vector3(0, 1, 0);
                obj.transform.rotation = Quaternion.identity;

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} No players found, positioned {obj.name} at origin");
                }
            }
        }

        // Try to auto-grab a restored object
        private static void TryAutoGrabObject(GameObject obj)
        {
            if (obj == null) return;

            // Find Drifter player
            var drifterPlayers = PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master.GetBody()?.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody"))
                .ToList();

            if (drifterPlayers.Count == 0)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} No Drifter players found for auto-grab of {obj.name}");
                }
                return;
            }

            // Try to grab with each Drifter (in case of multiple players)
            foreach (var drifter in drifterPlayers)
            {
                var bagController = drifter.GetComponent<DrifterBagController>();
                if (bagController != null && !bagController.bagFull)
                {
                    try
                    {
                        // Use reflection to call the private AssignPassenger method
                        var assignPassengerMethod = typeof(DrifterBagController).GetMethod("AssignPassenger",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                        if (assignPassengerMethod != null)
                        {
                            assignPassengerMethod.Invoke(bagController, new object[] { obj });

                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Successfully auto-grabbed {obj.name} for Drifter");
                            }
                            return; // Successfully grabbed, exit
                        }
                        else
                        {
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Could not find AssignPassenger method for auto-grab");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Auto-grab failed for {obj.name}: {ex.Message}");
                        }
                    }
                }
                else if (bagController != null && bagController.bagFull)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Drifter bag is full, cannot auto-grab {obj.name}");
                    }
                }
            }

            // If we get here, auto-grab failed
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Auto-grab failed for {obj.name} - all Drifter bags full or unavailable");
            }
        }

        // Schedule auto-grab for Drifter
        public static void ScheduleAutoGrab(CharacterMaster master)
        {
            if (!_cachedEnableAutoGrab) return;
        }

        // Get current persisted objects count
        public static int GetPersistedObjectsCount()
        {
            lock (_lock)
            {
                return _persistedObjects.Count;
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

        // Check if teleporter should be disabled
        public static bool ShouldDisableTeleporter(GameObject obj)
        {
            lock (_lock)
            {
                return _teleportersToDisable.Contains(obj);
            }
        }

        // Mark teleporter for disabling
        public static void MarkTeleporterForDisabling(GameObject obj)
        {
            if (obj == null) return;
            lock (_lock)
            {
                _teleportersToDisable.Add(obj);
            }
        }

        // Clear teleporter disabling marks
        public static void ClearTeleporterDisablingMarks()
        {
            lock (_lock)
            {
                _teleportersToDisable.Clear();
            }
        }

        // Handle special restoration logic
        private static void HandleSpecialObjectRestoration(GameObject obj)
        {
            if (obj == null) return;

            string objName = obj.name.ToLower();

            // Handle teleporters - mark for disabling to prevent FixedUpdate errors
            var teleporterInteraction = obj.GetComponent<RoR2.TeleporterInteraction>();
            if (teleporterInteraction != null)
            {
                // Mark this teleporter to be disabled in FixedUpdate
                MarkTeleporterForDisabling(obj);
            }

            // Handle DitherModel objects first (chests, purchasables)
             var ditherModel = obj.GetComponent<RoR2.DitherModel>();
             if (ditherModel != null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Found DitherModel on {obj.name}, beginning restoration");
                }

                // Ensure all colliders are properly restored before DitherModel tries to recalculate bounds
                var allColliders = obj.GetComponentsInChildren<Collider>();
                bool hasValidBounds = false;
                int enabledColliders = 0;
                int totalColliders = 0;

                foreach (var collider in allColliders)
                {
                    totalColliders++;
                    if (collider != null)
                    {
                        if (collider.enabled && !collider.isTrigger)
                        {
                            hasValidBounds = true;
                            enabledColliders++;
                        }
                        else if (!collider.isTrigger)
                        {
                            // Force enable non-trigger colliders that are disabled
                            collider.enabled = true;
                            hasValidBounds = true;
                            enabledColliders++;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Enabled disabled collider {collider.name} for DitherModel bounds on {obj.name}");
                            }
                        }
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} DitherModel bounds check: {totalColliders} total colliders, {enabledColliders} enabled non-trigger, hasValidBounds: {hasValidBounds}");
                }

                // If still no valid bounds, try to find and enable any available collider
                if (!hasValidBounds && allColliders.Length > 0)
                {
                    foreach (var collider in allColliders)
                    {
                        if (collider != null)
                        {
                            collider.enabled = true;
                            hasValidBounds = true;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Force-enabled any collider {collider.name} for DitherModel bounds on {obj.name}");
                            }
                            break;
                        }
                    }
                }

                // If object has no colliders at all, destroy DitherModel to prevent errors
                if (!hasValidBounds && allColliders.Length == 0)
                {
                    UnityEngine.Object.Destroy(ditherModel);
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Destroyed DitherModel on {obj.name} - no colliders available for bounds calculation");
                    }
                }

                // Try multiple approaches to refresh DitherModel
                try
                {
                    // Disable/re-enable
                    ditherModel.enabled = false;
                    ditherModel.enabled = true;

                    // Force Awake/OnEnable cycle by destroying and recreating if needed
                    // This is more aggressive but ensures clean state
                    if (!hasValidBounds)
                    {
                        // As a last resort, try to manually set bounds if we can access it
                        var boundsField = typeof(RoR2.DitherModel).GetField("bounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        if (boundsField != null)
                        {
                            // Try to recalculate bounds from enabled colliders
                            Bounds newBounds = new Bounds(obj.transform.position, Vector3.one);
                            bool boundsSet = false;

                            foreach (var collider in allColliders)
                            {
                                if (collider != null && collider.enabled)
                                {
                                    if (collider is BoxCollider box)
                                    {
                                        newBounds = box.bounds;
                                        boundsSet = true;
                                        break;
                                    }
                                    else if (collider is SphereCollider sphere)
                                    {
                                        newBounds = sphere.bounds;
                                        boundsSet = true;
                                        break;
                                    }
                                    else if (collider is CapsuleCollider capsule)
                                    {
                                        newBounds = capsule.bounds;
                                        boundsSet = true;
                                        break;
                                    }
                                }
                            }

                            if (boundsSet)
                            {
                                boundsField.SetValue(ditherModel, newBounds);
                                if (PluginConfig.EnableDebugLogs.Value)
                                {
                                    Log.Info($"{Constants.LogPrefix} Manually set DitherModel bounds for {obj.name}: {newBounds}");
                                }
                            }
                        }
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Special DitherModel restoration completed for {obj.name}");
                    }
                }
                catch (System.Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Error during DitherModel restoration for {obj.name}: {ex.Message}");
                    }
                }
            }

            // Remove persisted bosses from BossGroups to prevent teleporter interference
            // Delay this operation to avoid interfering with scene loading/teleporter initialization
            var characterMaster = obj.GetComponent<CharacterMaster>();
            if (characterMaster != null)
            {
                var characterBody = characterMaster.GetBody();
                if (characterBody != null)
                {
                    // Schedule BossGroup removal for next frame to avoid InvalidCastException during scene loading
                    var coroutineRunner = new GameObject("BossGroupCleanupRunner");
                    UnityEngine.Object.DontDestroyOnLoad(coroutineRunner);
                    var runner = coroutineRunner.AddComponent<BossGroupCleanupRunner>();
                    runner.Initialize(characterMaster, obj.name);
                }
            }

            // Fix Animator component issues that cause NullReferenceException spam
            var animator = obj.GetComponent<Animator>();
            if (animator != null)
            {
                try
                {
                    // Check if animator is in a bad state (null controller)
                    if (animator.runtimeAnimatorController == null)
                    {
                        // Try to restore animator controller from model
                        var modelLocator = obj.GetComponent<ModelLocator>();
                        if (modelLocator != null && modelLocator.modelTransform != null)
                        {
                            var modelAnimator = modelLocator.modelTransform.GetComponent<Animator>();
                            if (modelAnimator != null && modelAnimator.runtimeAnimatorController != null)
                            {
                                animator.runtimeAnimatorController = modelAnimator.runtimeAnimatorController;
                                if (PluginConfig.EnableDebugLogs.Value)
                                {
                                    Log.Info($"{Constants.LogPrefix} Restored Animator controller on {obj.name} from model");
                                }
                            }
                        }

                        // If still broken, disable animator to prevent errors
                        if (animator.runtimeAnimatorController == null)
                        {
                            animator.enabled = false;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Disabled broken Animator on {obj.name} to prevent NullReferenceException spam");
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // If animator is corrupted, disable it
                    animator.enabled = false;
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Disabled corrupted Animator on {obj.name} due to error: {ex.Message}");
                    }
                }
            }

            // General IInteractable re-enabling as fallback
            var interactable = obj.GetComponent<IInteractable>();
            if (interactable != null)
            {
                var interactableMB = interactable as MonoBehaviour;
                if (interactableMB != null && !interactableMB.enabled)
                {
                    interactableMB.enabled = true;

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Re-enabled IInteractable on {obj.name}");
                    }
                }
            }
        }
    }
}