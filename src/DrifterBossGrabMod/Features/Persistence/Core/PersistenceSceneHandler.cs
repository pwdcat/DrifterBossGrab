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
    // ========================================================================================
    // PERSISTENCE SCENE HANDLER
    // ========================================================================================

    public class PersistenceSceneHandler
    {
        private static readonly System.Reflection.FieldInfo _clientSceneObjectsField =
            HarmonyLib.AccessTools.Field(typeof(ClientScene), "objects") ??
            HarmonyLib.AccessTools.Field(typeof(ClientScene), "s_LocalObjects");
        private static IDictionary<NetworkInstanceId, NetworkIdentity>? _clientSceneObjects;
        public static PersistenceSceneHandler Instance { get; } = new PersistenceSceneHandler();
        private static bool isRestoringFromSceneChange = false;
        public static bool IsRestoringFromSceneChange() => isRestoringFromSceneChange;

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
        }

        private static NetworkUser? FindNetworkUserById(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;

            var users = NetworkUser.readOnlyInstancesList;

            // First attempt: Exact match against live IDs
            foreach (var user in users)
            {
                if (Networking.NetworkUtils.GetPlayerIdString(user.id) == playerId)
                {
                    return user;
                }
            }

            // Fallback for single-player
            if (users.Count == 1)
            {
                var onlyUser = users[0];
                Log.DebugIfEnabled("[FindNetworkUserById] ID mismatch '{0}' != '{1}', but only one player found. Using fallback.", playerId, Networking.NetworkUtils.GetPlayerIdString(onlyUser.id));
                return onlyUser;
            }

            Log.DebugIfEnabled("[FindNetworkUserById] Failed to find owner {0} among {1} players.", playerId, users.Count);

            return null;
        }

        public void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            if (!PersistenceObjectManager.GetCachedEnablePersistence())
            {
                Log.DebugIfEnabled(" Persistence disabled, skipping scene change handling for {0}", newScene.name);
                return;
            }

            Log.DebugIfEnabled(" Scene changed from {0} to {1}, restoring {2} persisted objects", oldScene.name, newScene.name, PersistenceObjectManager.GetPersistedObjectsCount());

            var coroutineRunner = new GameObject("PersistenceCoroutineRunner");
            var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
            isRestoringFromSceneChange = true;
            runner.StartCoroutine(DelayedRestorePersistedObjects());
        }

        // Coroutine to delay restoration until player is ready.
        private static System.Collections.IEnumerator DelayedRestorePersistedObjects()
        {
            PersistenceCoroutineRunner? runner = null;
            try
            {
                runner = UnityEngine.Object.FindFirstObjectByType<PersistenceCoroutineRunner>();
                yield return null;

                int maxWaitFrames = 120;
                int framesWaited = 0;
                while (framesWaited < maxWaitFrames)
                {
                    if (AnyPlayerHasBody())
                    {
                        Log.DebugIfEnabled(" Any player body found after {0} frames, proceeding with restoration", framesWaited);
                        break;
                    }

                    framesWaited++;
                    yield return null;
                }

                RestorePersistedObjects();
            }
            finally
            {
                if (runner != null)
                {
                    UnityEngine.Object.Destroy(runner.gameObject);
                }
            }
        }

        private static bool AnyPlayerHasBody()
        {
            foreach (var nu in NetworkUser.readOnlyInstancesList)
            {
                if (nu.master?.GetBody() != null) return true;
            }
            return false;
        }

        private class PersistenceCoroutineRunner : MonoBehaviour
        {
            private void OnDestroy()
            {
                Log.DebugIfEnabled(" PersistenceCoroutineRunner destroyed - cleanup completed");
            }
        }

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
                            Log.DebugIfEnabled(" Removed persisted boss {0} from BossGroup to prevent teleporter interference", _objectName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[DelayedBossGroupCleanup] Error: {ex.Message}");
                }
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        // ========================================================================================
        // CORE PERSISTENCE LOGIC
        // ========================================================================================

        public static void RestorePersistedObjects()
        {
            var persistedObjects = PersistenceObjectManager.GetPersistedObjectsSet();
            var _lock = PersistenceObjectManager.GetLock();
            lock (_lock)
            {
                Log.DebugIfEnabled(" Starting restoration of {0} persisted objects", persistedObjects.Count);
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

                    // Check if blacklisted before restoration logic
                    if (PluginConfig.IsPersistenceBlacklisted(obj))
                    {
                        Log.DebugIfEnabled("[RestorePersistedObjects] Skipping restoration of {0}: Object is blacklisted.", obj.name);

                        // Destroy it if it's already in the persistence container to prevent scene clutter
                        UnityEngine.Object.Destroy(obj);
                        objectsToRemove.Add(obj);
                        continue;
                    }

                    var healthComp = obj.GetComponent<RoR2.HealthComponent>();
                    Log.DebugIfEnabled("[debug] [RestorePersistedObjects LOOP] {0}: alive={1}, activeInHierarchy={2}", obj.name, healthComp?.alive, obj.activeInHierarchy);

                    bool isAlreadyInScene = obj.scene == SceneManager.GetActiveScene();
                    var networkIdentity = obj.GetComponent<NetworkIdentity>();

                    if (isAlreadyInScene)
                    {
                        Log.DebugIfEnabled("[RestorePersistedObjects] Skipping object {0} (NetID: {1}) - already in active scene.", obj.name, networkIdentity?.netId);
                        if (healthComp != null && !healthComp.alive)
                        {
                            Log.DebugIfEnabled("[debug] [RestorePersistedObjects] skipped object {0} is already in scene but is dead! alive={1}", obj.name, healthComp.alive);
                        }
                        continue;
                    }

                    Log.DebugIfEnabled(" Restoring object {0} to scene (currently parented to: {1}) from {2}", obj.name, obj.transform.parent?.name ?? "null", obj.scene.name);
                    obj.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());

                    var colliderCache = obj.GetComponent<BodyColliderCache>();
                    if (colliderCache != null)
                    {
                        colliderCache.RefreshCache();
                        Log.DebugIfEnabled("[RestorePersistedObjects] Refreshed BodyColliderCache for {0}", obj.name);
                    }

                    // Re-parent model if it was detached during persistence
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;
                        // Check if model is still in persistence container or detached
                        if (modelObj.transform.parent != obj.transform)
                        {
                            Log.DebugIfEnabled("[RestorePersistedObjects] Re-parenting model {0} to body {1}", modelObj.name, obj.name);

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
                            Log.DebugIfEnabled("[RestorePersistedObjects] Specific owner for {0} not found. Attaching PersistedObjectSeeker.", obj.name);
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
                            Log.DebugIfEnabled("[RestorePersistedObjects] Client: preserving object {0} (NetID: {1}). Re-registering with ClientScene.", obj.name, networkIdentity.netId);

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
                                Log.DebugIfEnabled("[RestorePersistedObjects] Enabled Kinematic Safety for local object {0}", obj.name);
                            }

                            API.DrifterBagAPI.ScheduleAutoGrab(obj, delay: 0.1f);
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
                            var ownerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);
                            API.DrifterBagAPI.ScheduleAutoGrab(obj, ownerId);
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

                                Log.DebugIfEnabled("[RestorePersistedObjects] Re-linked master {0} to body {1}", master.name, bodyObj.name);
                            }
                        }
                    }
                }

                Log.DebugIfEnabled("[RestorePersistedObjects] Restoration complete. {0} objects restored.", successfullyRestoredObjects.Count);
                isRestoringFromSceneChange = false;
            }
        }

        // Renderers are re-enabled to fix visual bugs
        // ========================================================================================
        // RESTORATION HELPERS
        // ========================================================================================

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

        // TODO: Needs more testing. This is just to prevent objects from falling through the floor after scene transition.
        private static System.Collections.IEnumerator ClientSafetyFloat(GameObject obj, PersistenceCoroutineRunner runner)
        {
            yield return new WaitForSeconds(Constants.Timeouts.OverencumbranceDebuffRemovalDelay);

            if (obj != null)
            {
                var rb = obj.GetComponent<Rigidbody>();
                if (rb)
                {
                    var existingState = API.DrifterBagAPI.FindStateForObject(obj);
                    if (existingState != null && existingState.hasCapturedRigidbodyState)
                    {
                        rb.isKinematic = existingState.originalIsKinematic;
                        rb.useGravity = existingState.originalUseGravity;
                        rb.mass = existingState.originalMass;
                        rb.drag = existingState.originalDrag;
                        rb.angularDrag = existingState.originalAngularDrag;
                        rb.detectCollisions = true;
                    }
                    else
                    {
                        rb.isKinematic = false;
                        rb.detectCollisions = true;
                    }

                    rb.velocity = Vector3.zero;
                }

            }

            if (runner != null && runner.gameObject != null) UnityEngine.Object.Destroy(runner.gameObject);
        }

        // If the owner hasn't spawned yet, we keep the object in limbo and periodically check for their appearance.
        // ========================================================================================
        // PERSISTENCE COMPONENTS
        // ========================================================================================

        private class PersistedObjectSeeker : MonoBehaviour
        {
            private string _ownerPlayerId = string.Empty;

            public void Initialize(string ownerId)
            {
                _ownerPlayerId = ownerId;
                StartCoroutine(SeekOwnerCoroutine());
            }

            private System.Collections.IEnumerator SeekOwnerCoroutine()
            {
                float elapsed = 0f;
                const float checkInterval = 0.5f;
                const float timeout = 60f;

                while (elapsed < timeout)
                {
                    if (!NetworkServer.active)
                    {
                        Destroy(this);
                        yield break;
                    }

                    yield return new WaitForSeconds(checkInterval);
                    elapsed += checkInterval;

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

                            Log.DebugIfEnabled("[PersistedObjectSeeker] Found owner {0} after {1:F2}s. Teleporting {2} to {3}", targetBody.name, elapsed, name, targetPos);

                            transform.position = targetPos;
                            transform.rotation = Quaternion.identity;

                            if (TryGetComponent<Rigidbody>(out var rb))
                            {
                                rb.velocity = Vector3.zero;
                                rb.angularVelocity = Vector3.zero;
                            }

                            Destroy(this);
                            yield break;
                        }
                    }
                }
                Log.DebugIfEnabled("[PersistedObjectSeeker] Timeout seeking owner for {0}. Staying at current position.", name);
                Destroy(this);
            }
        }

        // Objects are placed in front of the player
        private static bool PositionNearPlayer(GameObject obj)
        {
            // First, try to find the owner Drifter by player id
            var ownerPlayerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);
            CharacterBody? targetBody = null;
            bool ownerFound = false;

            if (!string.IsNullOrEmpty(ownerPlayerId))
            {
                // Find the NetworkUser associated with this player id.
                // FindNetworkUserById handles single-player fallbacks. Needs more testing.
                var matchedUser = FindNetworkUserById(ownerPlayerId);
                if (matchedUser != null)
                {
                    targetBody = matchedUser.master?.GetBody();
                    if (targetBody != null) ownerFound = true;
                }
            }

            if (targetBody == null)
            {
                // Fallback to host's body if no specific owner is found.
                var hostUser = NetworkUser.readOnlyInstancesList.FirstOrDefault(nu => nu.isServer);
                if (hostUser != null && hostUser.master != null)
                {
                    targetBody = hostUser.master.GetBody();
                }
            }

            if (targetBody == null)
            {
                // Final resort to local player if all else fails.
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
                Log.DebugIfEnabled(" Positioned {0} at {1} near {2} body {3} (Pos: {4})", obj.name, targetPos, ((ownerFound) ? "owner" : "fallback"), targetBody.name, playerPos);
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
                    Log.DebugIfEnabled(" Used camera fallback positioning for {0} at {1}", obj.name, fallbackPos);
                }
                else
                {
                    // Last resort: position at origin with offset
                    obj.transform.position = new Vector3(0, Constants.Limits.OriginYOffset, 0);
                    obj.transform.rotation = Quaternion.identity;
                    Log.DebugIfEnabled(" Used origin fallback positioning for {0}", obj.name);
                }
                return false;
            }
        }

        // Immediate auto-grab is used when a Drifter respawns mid-stage to recover their previously held items.
        // ========================================================================================
        // AUTO GRAB HELPERS
        // ========================================================================================

        public void ScheduleAutoGrab(CharacterMaster master)
        {
            if (!NetworkServer.active) return;
            if (!PersistenceObjectManager.GetCachedEnableAutoGrab()) return;
            Log.DebugIfEnabled(" Executing immediate auto-grab for Drifter");
            // Get the Drifter's body and bag controller
            var body = master.GetBody();
            if (body == null)
            {
                Log.DebugIfEnabled(" No body found for Drifter during auto-grab");
                return;
            }
            // Try to find bag controller on the master first (same logic as GetCurrentlyBaggedObjects)
            var bagController = master.GetComponent<DrifterBagController>();
            // If not found on master, try to find it on the body
            if (bagController == null)
            {
                bagController = body.GetComponent<DrifterBagController>();
                Log.DebugIfEnabled(" Found bag controller on body during auto-grab");
            }
            else
            {
                Log.DebugIfEnabled(" Found bag controller on master during auto-grab");
            }
            if (bagController == null)
            {
                Log.DebugIfEnabled(" No DrifterBagController found on Drifter master or body");
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
                Log.DebugIfEnabled(" Drifter player ID: {0}", drifterPlayerId);
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
            Log.DebugIfEnabled(" Found {0} persisted objects in scene for auto-grab", persistedObjectsInScene.Count);
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
            Log.DebugIfEnabled(" Found {0} currently bagged objects in scene for auto-grab", currentlyBaggedObjectsInScene.Count);
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

            // Total objects to attempt auto-grab for Drifter
            Log.DebugIfEnabled(" Total objects to attempt auto-grab for Drifter {0}: {1} (filtered from {2} total)", drifterPlayerId, filteredObjectsToGrab.Count, objectsToGrab.Count);
            // Try to grab each object using the API
            foreach (var obj in filteredObjectsToGrab)
            {
                if (!API.DrifterBagAPI.HasRoom(bagController))
                {
                    Log.DebugIfEnabled(" Drifter bag is full, stopping auto-grab");
                    break;
                }

                API.DrifterBagAPI.TryAutoGrab(obj, drifterPlayerId);
            }
        }

        // Certain objects like Teleporters and Bosses require specialized cleanup to prevent breaking the core game loop in the new stage.
        // ========================================================================================
        // SPECIAL OBJECT HANDLING
        // ========================================================================================

        public static void HandleSpecialObjectRestoration(GameObject obj, bool duringSceneRestoration = false)
        {
            if (obj == null) return;
            if (PluginConfig.IsPersistenceBlacklisted(obj))
            {
                Log.DebugIfEnabled("[HandleSpecialObjectRestoration] Destroying blacklisted object {0}", obj.name);
                UnityEngine.Object.Destroy(obj);
                return;
            }

            var teleporterInteraction = obj.GetComponent<RoR2.TeleporterInteraction>();

            string objName = obj.name.ToLower();
            // Handle teleporters - disable if there's another active teleporter
            Log.DebugIfEnabled(" Checking for TeleporterInteraction on persisted object {0}", obj.name);
            if (teleporterInteraction != null)
            {
                Log.DebugIfEnabled(" Found TeleporterInteraction on {0} for persisted object {1}. Registering as secondary and patching references.", teleporterInteraction.gameObject.name, obj.name);

                // Patch stale references to destroyed Unity objects ONLY during scene restoration, not during cycling
                if (isRestoringFromSceneChange)
                {
                    TeleporterPatches.PatchStaleReferences(teleporterInteraction);
                }

                // Register as secondary and protect primary singleton
                MultiTeleporterTracker.RegisterSecondary(teleporterInteraction);

                Log.DebugIfEnabled("[HandleSpecialObjectRestoration] Successfully patched stale references for {0}", obj.name);
            }

            // Only refresh visuals during scene restoration, not during cycling
            if (duringSceneRestoration)
            {
                VisualRefreshUtility.Refresh(obj);
            }
            else
            {
                Log.DebugIfEnabled(" No TeleporterInteraction found on persisted object {0}", obj.name);
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
                                Log.DebugIfEnabled(" Restored Animator controller on {0} from model", obj.name);
                            }
                        }
                        // If still broken, disable animator to prevent errors
                        if (animator.runtimeAnimatorController == null)
                        {
                            animator.enabled = false;
                            Log.DebugIfEnabled(" Disabled broken Animator on {0} to prevent NullReferenceException spam", obj.name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[HandleSpecialObjectRestoration] Error fixing animator: {ex.Message}");
                }
            }
        }

        // Client-side registration
        // ========================================================================================
        // NETWORKING HELPERS
        // ========================================================================================

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
                        Log.DebugIfEnabled("[RegisterLocalObjectReflectively] Successfully registered NetID {0} with ClientScene via cached reflection.", networkIdentity.netId);
                    }
                    else
                    {
                        Log.DebugIfEnabled("[RegisterLocalObjectReflectively] NetID {0} already registered in ClientScene.", networkIdentity.netId);
                    }
                }
                else
                {
                    // Fallback to direct reflection if cache is unavailable
                    if (_clientSceneObjectsField == null)
                    {
                        Log.DebugIfEnabled("[RegisterLocalObjectReflectively] Could not find 'objects' dictionary in ClientScene");
                        return;
                    }

                    var dictionary = _clientSceneObjectsField.GetValue(null) as IDictionary<NetworkInstanceId, NetworkIdentity>;
                    if (dictionary != null)
                    {
                        if (!dictionary.ContainsKey(networkIdentity.netId))
                        {
                            dictionary.Add(networkIdentity.netId, networkIdentity);
                            Log.DebugIfEnabled("[RegisterLocalObjectReflectively] Successfully registered NetID {0} with ClientScene via fallback Reflection.", networkIdentity.netId);
                        }
                        else
                        {
                            Log.DebugIfEnabled("[RegisterLocalObjectReflectively] NetID {0} already registered in ClientScene.", networkIdentity.netId);
                        }
                    }
                    else
                    {
                        Log.DebugIfEnabled("[RegisterLocalObjectReflectively] Field found but value is null or not IDictionary<NetworkInstanceId, NetworkIdentity>");
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
