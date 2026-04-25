#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using RoR2;
using RoR2.Projectile;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Patches;
using DrifterBossGrabMod.Core;
using HarmonyLib;

namespace DrifterBossGrabMod
{
    public class PersistenceSceneHandler
    {
        private static bool isRestoringFromSceneChange = false;
        public static bool IsRestoringFromSceneChange() => isRestoringFromSceneChange;
        public static PersistenceSceneHandler Instance { get; } = new PersistenceSceneHandler();
        private static readonly System.Reflection.FieldInfo _clientSceneObjectsField =
            HarmonyLib.AccessTools.Field(typeof(ClientScene), "objects") ??
            HarmonyLib.AccessTools.Field(typeof(ClientScene), "s_LocalObjects");
        private static IDictionary<NetworkInstanceId, NetworkIdentity>? _clientSceneObjects;

        // Cached NetworkUser dictionary for O(1) lookups
        private static readonly System.Collections.Generic.Dictionary<string, NetworkUser> _networkUserCache = new System.Collections.Generic.Dictionary<string, NetworkUser>();

        // Initialize static fields
        static PersistenceSceneHandler()
        {
            // Cache reflection result at initialization
            if (_clientSceneObjectsField != null)
            {
                try
                {
                    _clientSceneObjects = _clientSceneObjectsField.GetValue(null) as IDictionary<NetworkInstanceId, NetworkIdentity>;
                }
                catch
                {
                    _clientSceneObjects = null;
                }
            }

            // Subscribe to NetworkUser events for cache management
            NetworkUser.onNetworkUserDiscovered += OnNetworkUserDiscovered;
            NetworkUser.onNetworkUserLost += OnNetworkUserLost;

            // Initialize cache with existing NetworkUsers
            RefreshNetworkUserCache();
        }

        // Helper methods for NetworkUser cache management
        private static void RefreshNetworkUserCache()
        {
            _networkUserCache.Clear();
            foreach (var nu in NetworkUser.readOnlyInstancesList)
            {
                var id = nu.id;
                var idString = id.strValue != null ? id.strValue : $"{id.value}_{id.subId}";
                _networkUserCache[idString] = nu;
            }
        }

        private static void OnNetworkUserDiscovered(NetworkUser user)
        {
            var id = user.id;
            var idString = id.strValue != null ? id.strValue : $"{id.value}_{id.subId}";
            _networkUserCache[idString] = user;
        }

        private static void OnNetworkUserLost(NetworkUser user)
        {
            var id = user.id;
            var idString = id.strValue != null ? id.strValue : $"{id.value}_{id.subId}";
            _networkUserCache.Remove(idString);
        }

        private static NetworkUser? FindNetworkUserById(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            _networkUserCache.TryGetValue(playerId, out var user);
            return user;
        }

        // Handle scene change
        public void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" OnSceneChanged called - EnablePersistence: {PersistenceObjectManager.GetCachedEnablePersistence()}, from {oldScene.name} to {newScene.name}");
            }
            // Network handlers are now registered via [NetworkMessageHandler] attributes
            // Invalidate teleporter registry and cache on scene change
            MultiTeleporterTracker.Clear();
            if (!PersistenceObjectManager.GetCachedEnablePersistence())
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Persistence disabled, skipping scene change handling");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Scene changed from {oldScene.name} to {newScene.name}, restoring {PersistenceObjectManager.GetPersistedObjectsCount()} persisted objects");
            }
            var coroutineRunner = new GameObject("PersistenceCoroutineRunner");
            var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
            isRestoringFromSceneChange = true;
            runner.StartCoroutine(DelayedRestorePersistedObjects());
        }

        // Coroutine to delay restoration until player is ready
        private static System.Collections.IEnumerator DelayedRestorePersistedObjects()
        {
            PersistenceCoroutineRunner? runner = null;
            try
            {
                // Get the current runner reference for cleanup
                runner = UnityEngine.Object.FindFirstObjectByType<PersistenceCoroutineRunner>();
                // Wait one frame for initial scene setup
                yield return null;
                // Wait additional frames until any player body is available
                int maxWaitFrames = 120;
                int framesWaited = 0;
                while (framesWaited < maxWaitFrames)
                {
                    CharacterBody? anyBody = null;
                    foreach (var nu in _networkUserCache.Values)
                    {
                        var body = nu.master?.GetBody();
                        if (body != null)
                        {
                            anyBody = body;
                            break;
                        }
                    }
                    if (anyBody != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Any player body found after {framesWaited} frames, proceeding with restoration");
                        }
                        break;
                    }

                    // Early exit if all players are ready
                    if (AllPlayersReady())
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" All players ready after {framesWaited} frames, proceeding with restoration");
                        }
                        break;
                    }

                    framesWaited++;
                    yield return null;
                }
                if (framesWaited >= maxWaitFrames)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Timeout waiting for any player body after {maxWaitFrames} frames, proceeding with restoration anyway");
                    }
                }
                // Restore persisted objects to new scene
                RestorePersistedObjects();
            }
            finally
            {
                // Always clean up the coroutine runner, even if an exception occurs
                if (runner != null)
                {
                    UnityEngine.Object.Destroy(runner.gameObject);
                }
            }
        }

        // Check if all players are ready (early exit condition)
        private static bool AllPlayersReady()
        {
            // Check if we have any persisted objects to restore
            var persistedCount = PersistenceObjectManager.GetPersistedObjectsCount();
            if (persistedCount == 0)
            {
                return true; // Nothing to restore, can proceed
            }

            // Check if all network users have a valid body
            bool allUsersHaveBodies = false;
            foreach (var nu in _networkUserCache.Values)
            {
                if (nu.master?.GetBody() != null)
                {
                    allUsersHaveBodies = true;
                    break; // Early exit once found
                }
            }

            if (allUsersHaveBodies && _networkUserCache.Count > 0)
            {
                return true;
            }

            return allUsersHaveBodies;
        }

        // Helper class for running coroutines
        private class PersistenceCoroutineRunner : MonoBehaviour
        {
            private void OnDestroy()
            {
                // Ensure cleanup even if coroutine fails
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" PersistenceCoroutineRunner destroyed - cleanup completed");
                }
            }
        }

        // Helper class for delayed BossGroup cleanup to avoid InvalidCastException during scene loading
        private class BossGroupCleanupRunner : MonoBehaviour
        {
            private CharacterMaster? _characterMaster;
            private string? _objectName;
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
                    var characterBody = _characterMaster!.GetBody();
                    if (characterBody != null)
                    {
                        var bossGroup = RoR2.BossGroup.FindBossGroup(characterBody);
                        if (bossGroup != null)
                        {
                            bossGroup.ForgetBoss(_characterMaster);
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Removed persisted boss {_objectName} from BossGroup to prevent teleporter interference");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[DelayedBossGroupCleanup] Error: {ex.Message}");
                }
                // Clean up this runner
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        // Restore persisted objects
        public static void RestorePersistedObjects()
        {
            var persistedObjects = PersistenceObjectManager.GetPersistedObjectsSet();
            var _lock = PersistenceObjectManager.GetLock();
            lock (_lock)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Starting restoration of {persistedObjects.Count} persisted objects");
                }
                var objectsToRemove = new List<GameObject>();
                var successfullyRestoredObjects = new List<GameObject>();
                // Create a copy to iterate safely
                var persistedArray = persistedObjects.ToArray();

                foreach (var obj in persistedArray)
                {
                    if (obj == null)
                    {
                        objectsToRemove.Add(null!);
                        continue;
                    }

                    var healthComp = obj.GetComponent<RoR2.HealthComponent>();
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[DEBUG] [RestorePersistedObjects LOOP] {obj.name}: alive={healthComp?.alive}, activeInHierarchy={obj.activeInHierarchy}");
                    }

                    bool isAlreadyInScene = obj.scene == SceneManager.GetActiveScene();
                    var networkIdentity = obj.GetComponent<NetworkIdentity>();

                    if (isAlreadyInScene)
                    {
                        // Already in scene (Fresh Networked Object), skip restoration
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RestorePersistedObjects] Skipping object {obj.name} (NetID: {networkIdentity?.netId}) - already in active scene.");
                        }
                        if (PluginConfig.Instance.EnableDebugLogs.Value && healthComp != null && !healthComp.alive)
                        {
                            Log.Warning($"[DEBUG] [RestorePersistedObjects] SKIPPED object {obj.name} is already in scene but is DEAD! alive={healthComp.alive}");
                        }
                        continue;
                    }

                    // Object is in DontDestroyOnLoad (Stale/Persistence)
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Restoring object {obj.name} to scene (currently parented to: {obj.transform.parent?.name ?? "null"}) from {obj.scene.name}");
                    }

                    // Move back to scene and remove from DontDestroyOnLoad root
                    obj.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());

                    // Refresh BodyColliderCache after model re-parenting to ensure it has valid collider references
                    var colliderCache = obj.GetComponent<BodyColliderCache>();
                    if (colliderCache != null)
                    {
                        colliderCache.RefreshCache();
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RestorePersistedObjects] Refreshed BodyColliderCache for {obj.name}");
                        }
                    }

                    // Re-parent model if it was detached during persistence
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;
                        // Check if model is still in persistence container or detached
                        if (modelObj.transform.parent != obj.transform)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[RestorePersistedObjects] Re-parenting model {modelObj.name} to body {obj.name}");

                            modelObj.transform.SetParent(obj.transform, true);
                            modelObj.transform.localPosition = Vector3.zero;
                            modelObj.transform.localRotation = Quaternion.identity;
                        }

                        // Refresh visual state to clear pink textures or shader artifacts after stage transition
                        VisualRefreshUtility.Refresh(obj);
                    }

                    // Position Logic
                    if (NetworkServer.active)
                    {
                        // Server is authoritative, position immediately (best effort)
                        bool positionedCorrectly = PositionNearPlayer(obj);
                        var ownerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);

                        // If we didn't find the specific owner and we have an owner ID, attach seeker
                        if (!positionedCorrectly && !string.IsNullOrEmpty(ownerId))
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[RestorePersistedObjects] Specific owner for {obj.name} not found. Attaching PersistedObjectSeeker.");
                            var seeker = obj.AddComponent<PersistedObjectSeeker>();
                            seeker.Initialize(ownerId);
                        }

                        try
                        {
                            if (networkIdentity != null)
                            {
                                // Fresh NetID refresh pass
                                NetworkServer.UnSpawn(obj);
                                NetworkServer.Spawn(obj);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[RestorePersistedObjects] Error refreshing network identity: {ex.Message}");
                        }
                    }
                    else
                    {
                        if (networkIdentity != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[RestorePersistedObjects] Client: preserving object {obj.name} (NetID: {networkIdentity.netId}). Re-registering with ClientScene.");
                            }

                            // 1. Position it
                            PositionNearPlayer(obj);

                            // 2. Re-register with ClientScene via Reflection
                            RegisterLocalObjectReflectively(networkIdentity);

                            // 3. Ensure renderers/components are active
                            if (obj.TryGetComponent<Rigidbody>(out var rb))
                            {
                                rb.isKinematic = true; // Safety float
                                var coroutineRunner = new GameObject("ClientSafetyFloatRunner_" + obj.name);
                                var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
                                runner.StartCoroutine(ClientSafetyFloat(obj, runner));
                            }
                        }
                        else
                        {
                            // Non-networked object (local visual?). Restore it.
                            PositionNearPlayer(obj);
                            var rb = obj.GetComponent<Rigidbody>();
                            if (rb)
                            {
                                rb.isKinematic = true;
                                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[RestorePersistedObjects] Enabled Kinematic Safety for local object {obj.name}");
                            }

                            var coroutineRunner = new GameObject("ClientSafetyFloatRunner_" + obj.name);
                            var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
                            runner.StartCoroutine(ClientSafetyFloat(obj, runner));
                        }
                    }

                    // Ensure the object and its model are active/rendered
                    obj.SetActive(true);
                    RestoreRenderers(obj);

                    // Special handling for teleporters and portals
                    HandleSpecialObjectRestoration(obj, duringSceneRestoration: true);

                    // Attempt auto-grab if enabled
                    if (PersistenceObjectManager.GetCachedEnableAutoGrab())
                    {
                        if (NetworkServer.active)
                        {
                            // Server: Delay AutoGrab to allow Spawn message to propagate to Clients
                            var coroutineRunner = new GameObject("ServerAutoGrabRunner_" + obj.name);
                            var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
                            runner.StartCoroutine(DelayedAutoGrab(obj, runner, PluginConfig.Instance.AutoGrabDelay.Value));
                        }
                        else
                        {
                            // Client: AutoGrab is handled by Network Sync
                        }
                    }

                    // Track successfully restored objects to remove from persistence set
                    successfullyRestoredObjects.Add(obj);
                }

                // Cleanup nulls and remove successfully restored objects from persistence set
                // This allows them to be re-persisted on next scene change if they're still bagged
                foreach (var obj in objectsToRemove)
                {
                    persistedObjects.Remove(obj);
                }

                // Remove successfully restored objects from persistence set
                // They will be re-added by CaptureCurrentlyBaggedObjects() on next scene change if still bagged
                foreach (var obj in successfullyRestoredObjects)
                {
                    persistedObjects.Remove(obj);
                }

                // This ensures that for enemies, the freshly spawned Master is linked to the freshly spawned Body
                if (NetworkServer.active)
                {
                    foreach (var bodyObj in successfullyRestoredObjects)
                    {
                        if (bodyObj == null) continue;

                        var master = PersistenceObjectManager.GetMasterForBody(bodyObj);
                        if (master != null)
                        {
                            var body = bodyObj.GetComponent<CharacterBody>();
                            if (body != null)
                            {
                                // Force link update
                                master.bodyInstanceObject = bodyObj;

                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[RestorePersistedObjects] Re-linked master {master.name} to body {bodyObj.name}");
                            }
                        }
                    }
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[RestorePersistedObjects] Restoration complete. {successfullyRestoredObjects.Count} objects restored.");
                }
                isRestoringFromSceneChange = false;
            }
        }

        // Helper to restore renderers
        private static void RestoreRenderers(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) r.enabled = true;

            var modelLocator = obj.GetComponent<ModelLocator>();
            if (modelLocator != null && modelLocator.modelTransform != null)
            {
                modelLocator.modelTransform.gameObject.SetActive(true);
                var modelRenderers = modelLocator.modelTransform.GetComponentsInChildren<Renderer>(true);
                foreach (var r in modelRenderers) r.enabled = true;
            }
        }

        // Coroutine to ensure object doesn't fall while waiting for Server Sync
        private static System.Collections.IEnumerator ClientSafetyFloat(GameObject obj, PersistenceCoroutineRunner runner)
        {
            yield return new WaitForSeconds(Constants.Timeouts.OverencumbranceDebuffRemovalDelay);

            if (obj != null)
            {
                var rb = obj.GetComponent<Rigidbody>();
                if (rb)
                {
                    rb.isKinematic = false; // Re-enable physics
                    rb.velocity = Vector3.zero; // Reset velocity just in case
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[ClientSafetyFloat] Re-enabled physics for {obj.name} at {obj.transform.position}");
                    }
                }
            }

            if (runner != null && runner.gameObject != null) UnityEngine.Object.Destroy(runner.gameObject);
        }

        // Helper class to seek owner body if not immediately found (Server Side)
        private class PersistedObjectSeeker : MonoBehaviour
        {
            private string _ownerPlayerId = string.Empty;
            private float _timeout = 60f;
            private float _timer = 0f;

            public void Initialize(string ownerId)
            {
                _ownerPlayerId = ownerId;
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    Destroy(this);
                    return;
                }

                _timer += Time.fixedDeltaTime;
                if (_timer > _timeout)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[PersistedObjectSeeker] Timeout seeking owner for {name}. Staying at current position.");
                    Destroy(this);
                    return;
                }

                // Try to find the NetworkUser associated with this player id using cached lookup
                NetworkUser? matchedUser = FindNetworkUserById(_ownerPlayerId);

                if (matchedUser != null)
                {
                    var targetBody = matchedUser.master?.GetBody();
                    if (targetBody != null)
                    {
                        // Found owner body! Teleport.
                        var playerPos = targetBody.transform.position;
                        var playerForward = targetBody.transform.forward;
                        var targetPos = playerPos + playerForward * Constants.Limits.PositionOffset + Vector3.up * Constants.Limits.PositionOffset;

                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[PersistedObjectSeeker] Found owner {targetBody.name} after {_timer:F2}s. Teleporting {name} to {targetPos}");

                        transform.position = targetPos;
                        transform.rotation = Quaternion.identity;

                        if (TryGetComponent<Rigidbody>(out var rb))
                        {
                            rb.velocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }

                        Destroy(this);
                    }
                }
            }
        }

        // Position object near player. Returns true if specific owner found, false if fallback used.
        private static bool PositionNearPlayer(GameObject obj)
        {
            // First, try to find the owner Drifter by player id
            var ownerPlayerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);
            CharacterBody? targetBody = null;
            bool ownerFound = false;

            if (!string.IsNullOrEmpty(ownerPlayerId))
            {
                // Find the NetworkUser associated with this player id using cached lookup
                var matchedUser = FindNetworkUserById(ownerPlayerId);
                if (matchedUser != null)
                {
                    targetBody = matchedUser.master?.GetBody();
                    if (targetBody != null) ownerFound = true;
                }
            }

            if (targetBody == null)
            {
                // Fallback to host's body (any body)
                var hostUser = _networkUserCache.Values.FirstOrDefault(nu => nu.isServer);
                if (hostUser != null && hostUser.master != null)
                {
                    targetBody = hostUser.master.GetBody();
                }
            }
            if (targetBody == null)
            {
                // Last resort to local player
                targetBody = NetworkUser.readOnlyLocalPlayersList.Count > 0 ? NetworkUser.readOnlyLocalPlayersList[0]?.master?.GetBody() : null;
            }

            if (targetBody != null)
            {
                // Position very close to player (0.5 units in front)
                var playerPos = targetBody.transform.position;
                var playerForward = targetBody.transform.forward;
                var targetPos = playerPos + playerForward * Constants.Limits.PositionOffset + Vector3.up * Constants.Limits.PositionOffset;
                obj.transform.position = targetPos;
                obj.transform.rotation = Quaternion.identity; // Reset rotation
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Positioned {obj.name} at {targetPos} near {((ownerFound) ? "owner" : "fallback")} body {targetBody.name} (Pos: {playerPos})");
                }
                return ownerFound;
            }
            else
            {
                // Fallback: position at scene center or camera position
                var camera = Camera.main;
                if (camera != null)
                {
                    var cameraPos = camera.transform.position;
                    var cameraForward = camera.transform.forward;
                    var fallbackPos = cameraPos + cameraForward * Constants.Limits.CameraForwardOffset;
                    obj.transform.position = fallbackPos;
                    obj.transform.rotation = Quaternion.identity;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Used camera fallback positioning for {obj.name} at {fallbackPos}");
                    }
                }
                else
                {
                    // Last resort: position at origin with offset
                    obj.transform.position = new Vector3(0, Constants.Limits.OriginYOffset, 0);
                    obj.transform.rotation = Quaternion.identity;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Used origin fallback positioning for {obj.name}");
                    }
                }
                return false;
            }
        }

        // Coroutine to delay auto-grab to allow client network spawn to complete
        private static System.Collections.IEnumerator DelayedAutoGrab(GameObject obj, PersistenceCoroutineRunner runner, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (obj != null)
            {
                TryAutoGrabObject(obj);
            }

            if (runner != null && runner.gameObject != null) UnityEngine.Object.Destroy(runner.gameObject);
        }

        // Try to auto-grab a restored object
        private static void TryAutoGrabObject(GameObject obj)
        {
            if (!NetworkServer.active) return; // only Host handles auto-grab assignment

            if (obj == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" TryAutoGrabObject called with null object");
                }
                return;
            }
            // Skip CharacterMaster objects
            if (obj.GetComponent<CharacterMaster>() != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Skipping auto-grab for {obj.name} - is CharacterMaster");
                }
                return;
            }

            // Skip dead objects
            var healthComp = obj.GetComponent<RoR2.HealthComponent>();
            if (healthComp != null && !healthComp.alive)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Skipping auto-grab for {obj.name} - object is dead (alive={healthComp.alive})");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[DEBUG] [TryAutoGrabObject ENTRY] {obj.name}: alive={healthComp?.alive}, activeInHierarchy={obj.activeInHierarchy}");
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Attempting auto-grab for restored object {obj.name}");
            }

            // Find the owner Drifter body
            CharacterBody? targetBody = null;
            var ownerPlayerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);

            if (!string.IsNullOrEmpty(ownerPlayerId))
            {
                // Find the Drifter body associated with this player id using cached lookup
                var ownerUser = FindNetworkUserById(ownerPlayerId);
                if (ownerUser != null && ownerUser.master != null)
                {
                    targetBody = ownerUser.master.GetBody();
                    if (PluginConfig.Instance.EnableDebugLogs.Value && targetBody != null)
                    {
                        Log.Info($" Found owner body {targetBody.name} for object {obj.name} via player ID {ownerPlayerId}");
                    }
                }
            }

            if (targetBody == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    if (!string.IsNullOrEmpty(ownerPlayerId))
                    {
                        Log.Info($" Owner Drifter (player ID: {ownerPlayerId}) not found in scene yet for {obj.name}. Object will remain ungrabbed until owner spawns.");
                    }
                    else
                    {
                        Log.Info($" No owner assigned to {obj.name}. Object will remain ungrabbed (backward compatibility for unowned objects).");
                    }
                }
                return;
            }

            if (targetBody == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" No Drifter body found in scene to auto-grab {obj.name}");
                }
                return;
            }

            // Try to find bag controller on the body
            var bagController = targetBody.GetComponent<DrifterBagController>();
            if (bagController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" No DrifterBagController found on target body {targetBody.name}");
                }
                return;
            }

            if (BagCapacityCalculator.HasRoomForGrab(bagController))
            {
                try
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Server assigning {obj.name} to {targetBody.name}'s bag (Suppression Enabled)");
                    }

                    // Suppress the accidental throw during scene initialization
                    Patches.BaggedObjectPatches.SuppressExitForObject(obj);

                    bagController.AssignPassenger(obj);

                    // If this object is now in the main seat, we must transition the state machine
                    // to BaggedObject so skill overrides are applied.
                    if (Patches.BagPatches.GetMainSeatObject(bagController) == obj)
                    {
                        var bagStateMachine = EntityStateMachine.FindByCustomName(targetBody.gameObject, "Bag");
                        if (bagStateMachine != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Setting BaggedObject state on {targetBody.name} for {obj.name}");
                            }
                            var baggedObject = new BaggedObject();
                            baggedObject.targetObject = obj;
                            bagStateMachine.SetNextState(baggedObject);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[TryAutoGrabObject] Error assigning passenger: {ex.Message}");
                }
            }
            else
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Drifter bag for {targetBody.name} is full, cannot auto-grab {obj.name}");
                }
            }
        }

        // Schedule auto-grab for Drifter
        public void ScheduleAutoGrab(CharacterMaster master)
        {
            if (!NetworkServer.active) return;
            if (!PersistenceObjectManager.GetCachedEnableAutoGrab()) return;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Executing immediate auto-grab for Drifter");
            }
            // Get the Drifter's body and bag controller
            var body = master.GetBody();
            if (body == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" No body found for Drifter during auto-grab");
                }
                return;
            }
            // Try to find bag controller on the master first (same logic as GetCurrentlyBaggedObjects)
            var bagController = master.GetComponent<DrifterBagController>();
            // If not found on master, try to find it on the body
            if (bagController == null)
            {
                bagController = body.GetComponent<DrifterBagController>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Found bag controller on body during auto-grab");
                }
            }
            else
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Found bag controller on master during auto-grab");
                }
            }
            if (bagController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" No DrifterBagController found on Drifter master or body");
                }
                return;
            }

            // Get the player ID for this Drifter to filter objects by owner
            string? drifterPlayerId = null;
            var characterBody = body.GetComponent<CharacterBody>();
            if (characterBody != null && characterBody.master != null && characterBody.master.playerCharacterMasterController != null)
            {
                var networkUserId = characterBody.master.playerCharacterMasterController.networkUser.id;
                // NetworkUserId doesn't have ToString() override, so we need to manually serialize it
                drifterPlayerId = networkUserId.strValue != null
                    ? networkUserId.strValue
                    : $"{networkUserId.value}_{networkUserId.subId}";
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Drifter player ID: {drifterPlayerId}");
                }
            }

            // Find all persisted objects in the current scene
            var persistedObjectsInScene = new List<GameObject>();
            var _lock = PersistenceObjectManager.GetLock();
            lock (_lock)
            {
                foreach (var obj in PersistenceObjectManager.GetPersistedObjectsSet())
                {
                    if (obj != null && obj.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                    {
                        persistedObjectsInScene.Add(obj);
                    }
                }
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Found {persistedObjectsInScene.Count} persisted objects in scene for auto-grab");
            }
            // Also find currently bagged objects in the scene (for same-stage respawns)
            var currentlyBaggedObjectsInScene = new List<GameObject>();
            var allCurrentlyBagged = PersistenceObjectManager.GetCurrentlyBaggedObjects();
            foreach (var obj in allCurrentlyBagged)
            {
                if (obj != null && obj.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                {
                    currentlyBaggedObjectsInScene.Add(obj);
                }
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Found {currentlyBaggedObjectsInScene.Count} currently bagged objects in scene for auto-grab");
            }
            // Combine both lists, preferring persisted objects first
            var objectsToGrab = new List<GameObject>();
            objectsToGrab.AddRange(persistedObjectsInScene);
            objectsToGrab.AddRange(currentlyBaggedObjectsInScene);

            // Filter objects by owner - only grab objects that belong to this Drifter
            var filteredObjectsToGrab = new List<GameObject>();
            foreach (var obj in objectsToGrab)
            {
                var objOwnerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);
                // If object has an owner, only grab if it matches this Drifter
                if (string.IsNullOrEmpty(objOwnerId) || objOwnerId == drifterPlayerId)
                {
                    filteredObjectsToGrab.Add(obj);
                }
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Total objects to attempt auto-grab for Drifter {drifterPlayerId}: {filteredObjectsToGrab.Count} (filtered from {objectsToGrab.Count} total)");
            }
            // Try to grab each object
            foreach (var obj in filteredObjectsToGrab)
            {
                // Skip CharacterMaster objects (AI controllers) but allow environment objects
                if (obj.GetComponent<CharacterMaster>() != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Skipping auto-grab for {obj.name} - is CharacterMaster");
                    }
                    continue;
                }
                if (!BagCapacityCalculator.HasRoomForGrab(bagController))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Drifter bag is full, stopping auto-grab");
                    }
                    break;
                }

                bool isCharacterBody = obj.GetComponent<CharacterBody>() != null;

                if (isCharacterBody)
                {
                    // For CharacterBodies, use EntityStateMachine for main seat, or manual additional seat assignment
                    bool bagIsEmpty = BagCapacityCalculator.GetCurrentBaggedCount(bagController) == 0;
                    if (bagIsEmpty)
                    {
                        // Use EntityStateMachine for main seat
                        var bagStateMachine = EntityStateMachine.FindByCustomName(body.gameObject, "Bag");
                        if (bagStateMachine != null)
                        {
                            try
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" Found Bag state machine, setting BaggedObject state for {obj.name}");
                                }

                                // Suppress the accidental throw during state transition
                                Patches.BaggedObjectPatches.SuppressExitForObject(obj);

                                // Create BaggedObject state and set target
                                var baggedObject = new BaggedObject();
                                baggedObject.targetObject = obj;
                                // Set the next state on the bag state machine
                                bagStateMachine.SetNextState(baggedObject);
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" Successfully initiated auto-grab for {obj.name} using EntityStateMachine");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"[ScheduleAutoGrab] Error setting EntityStateMachine: {ex.Message}");
                            }
                        }
                        else
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Could not find Bag state machine for CharacterBody {obj.name}");
                            }
                        }
                    }
                    else
                    {
                        // Manually assign to additional seat for CharacterBodies
                        try
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Manually assigning CharacterBody {obj.name} to additional seat");
                            }
                            // Create additional seat
                            var seatObject = new GameObject($"AdditionalSeat_AutoGrab_{DateTime.Now.Ticks}");
                            seatObject.transform.SetParent(bagController.transform);
                            seatObject.transform.localPosition = Vector3.zero;
                            seatObject.transform.localRotation = Quaternion.identity;
                            var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
                            newSeat.seatPosition = bagController.vehicleSeat.seatPosition;
                            newSeat.exitPosition = bagController.vehicleSeat.exitPosition;
                            newSeat.ejectOnCollision = bagController.vehicleSeat.ejectOnCollision;
                            newSeat.hidePassenger = bagController.vehicleSeat.hidePassenger;
                            newSeat.exitVelocityFraction = bagController.vehicleSeat.exitVelocityFraction;
                            newSeat.disablePassengerMotor = bagController.vehicleSeat.disablePassengerMotor;
                            newSeat.isEquipmentActivationAllowed = bagController.vehicleSeat.isEquipmentActivationAllowed;
                            newSeat.shouldProximityHighlight = bagController.vehicleSeat.shouldProximityHighlight;
                            newSeat.disableInteraction = bagController.vehicleSeat.disableInteraction;
                            newSeat.shouldSetIdle = bagController.vehicleSeat.shouldSetIdle;
                            newSeat.additionalExitVelocity = bagController.vehicleSeat.additionalExitVelocity;
                            newSeat.disableAllCollidersAndHurtboxes = bagController.vehicleSeat.disableAllCollidersAndHurtboxes;
                            newSeat.disableColliders = bagController.vehicleSeat.disableColliders;
                            newSeat.disableCharacterNetworkTransform = bagController.vehicleSeat.disableCharacterNetworkTransform;
                            newSeat.ejectFromSeatOnMapEvent = bagController.vehicleSeat.ejectFromSeatOnMapEvent;
                            newSeat.inheritRotation = bagController.vehicleSeat.inheritRotation;
                            newSeat.holdPassengerAfterDeath = bagController.vehicleSeat.holdPassengerAfterDeath;
                            newSeat.ejectPassengerToGround = bagController.vehicleSeat.ejectPassengerToGround;
                            newSeat.ejectRayDistance = bagController.vehicleSeat.ejectRayDistance;
                            newSeat.handleExitTeleport = bagController.vehicleSeat.handleExitTeleport;
                            newSeat.setCharacterMotorPositionToCurrentPosition = bagController.vehicleSeat.setCharacterMotorPositionToCurrentPosition;
                            newSeat.passengerState = bagController.vehicleSeat.passengerState;

                            // Assign to the new seat
                            newSeat.AssignPassenger(obj);

                            // Track the object
                            var list = Patches.BagPatches.GetState(bagController).BaggedObjects;
                            if (list == null)
                            {
                                list = new List<GameObject>();
                                Patches.BagPatches.GetState(bagController).BaggedObjects = list;
                            }
                            if (!list.Contains(obj))
                            {
                                list.Add(obj);
                            }
                            var seatDict = Patches.BagPatches.GetState(bagController).AdditionalSeats;
                            seatDict[obj] = newSeat;

                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Successfully auto-grabbed CharacterBody {obj.name} to additional seat");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[ScheduleAutoGrab] Error assigning to additional seat: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // For non-CharacterBodies, use AssignPassenger
                    try
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Directly assigning {obj.name} to bag for auto-grab (Suppression Enabled)");
                        }

                        // Suppress the accidental throw during assignment
                        Patches.BaggedObjectPatches.SuppressExitForObject(obj);

                        bagController.AssignPassenger(obj);
                        // Update UI if this object is now in the main seat
                        if (Patches.BagPatches.GetMainSeatObject(bagController) == obj)
                        {
                            Patches.BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, obj);
                        }
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Successfully auto-grabbed {obj.name} using direct assignment");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[ScheduleAutoGrab] Error assigning passenger: {ex.Message}");
                    }
                }
            }
        }

        // Handle special restoration logic
        public static void HandleSpecialObjectRestoration(GameObject obj, bool duringSceneRestoration = false)
        {
            if (obj == null) return;

            // Skip objects with TeleporterInteraction if Teleporter is blacklisted
            var teleporterInteraction = obj.GetComponent<RoR2.TeleporterInteraction>();
            if (teleporterInteraction != null && PluginConfig.IsPersistenceBlacklisted("Teleporter"))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[HandleSpecialObjectRestoration] Skipping teleporter object {obj.name} (Teleporter is blacklisted)");
                }
                return;
            }

            string objName = obj.name.ToLower();
            // Handle teleporters - disable if there's another active teleporter
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Checking for TeleporterInteraction on persisted object {obj.name}");
            }
            if (teleporterInteraction != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Found TeleporterInteraction on {teleporterInteraction.gameObject.name} for persisted object {obj.name}. Registering as secondary and patching references.");
                }

                // Patch stale references to destroyed Unity objects ONLY during scene restoration, not during cycling
                if (isRestoringFromSceneChange)
                {
                    TeleporterPatches.PatchStaleReferences(teleporterInteraction);
                }

                // Register as secondary and protect primary singleton
                MultiTeleporterTracker.RegisterSecondary(teleporterInteraction);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[HandleSpecialObjectRestoration] Successfully patched stale references for {obj.name}");
                }
            }

            // Only refresh visuals during scene restoration, not during cycling
            if (duringSceneRestoration)
            {
                VisualRefreshUtility.Refresh(obj);
            }
            else
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" No TeleporterInteraction found on persisted object {obj.name}");
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
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" Restored Animator controller on {obj.name} from model");
                                }
                            }
                        }
                        // If still broken, disable animator to prevent errors
                        if (animator.runtimeAnimatorController == null)
                        {
                            animator.enabled = false;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Disabled broken Animator on {obj.name} to prevent NullReferenceException spam");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[HandleSpecialObjectRestoration] Error fixing animator: {ex.Message}");
                }
            }
        }



        // Helper method to register object with ClientScene using cached Reflection
        private static void RegisterLocalObjectReflectively(NetworkIdentity networkIdentity)
        {
            try
            {
                // Use cached reflection result for better performance
                if (_clientSceneObjects != null)
                {
                    if (!_clientSceneObjects.ContainsKey(networkIdentity.netId))
                    {
                        _clientSceneObjects.Add(networkIdentity.netId, networkIdentity);
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[RegisterLocalObjectReflectively] Successfully registered NetID {networkIdentity.netId} with ClientScene via cached reflection.");
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[RegisterLocalObjectReflectively] NetID {networkIdentity.netId} already registered in ClientScene.");
                    }
                }
                else
                {
                    // Fallback to direct reflection if cache is unavailable
                    if (_clientSceneObjectsField == null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Warning("[RegisterLocalObjectReflectively] Could not find 'objects' dictionary in ClientScene");
                        return;
                    }

                    var dictionary = _clientSceneObjectsField.GetValue(null) as IDictionary<NetworkInstanceId, NetworkIdentity>;
                    if (dictionary != null)
                    {
                        if (!dictionary.ContainsKey(networkIdentity.netId))
                        {
                            dictionary.Add(networkIdentity.netId, networkIdentity);
                            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[RegisterLocalObjectReflectively] Successfully registered NetID {networkIdentity.netId} with ClientScene via fallback Reflection.");
                        }
                        else
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[RegisterLocalObjectReflectively] NetID {networkIdentity.netId} already registered in ClientScene.");
                        }
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Warning($"[RegisterLocalObjectReflectively] Field found but value is null or not IDictionary<NetworkInstanceId, NetworkIdentity>");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RegisterLocalObjectReflectively] Error: {ex.Message}");
            }
        }
    }
}
