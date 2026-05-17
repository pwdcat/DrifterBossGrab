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
        private static bool isRestoringFromSceneChange = false;
        public static bool IsRestoringFromSceneChange() => isRestoringFromSceneChange;
        public static PersistenceSceneHandler Instance { get; } = new PersistenceSceneHandler();
        private static readonly System.Reflection.FieldInfo _clientSceneObjectsField =
            HarmonyLib.AccessTools.Field(typeof(ClientScene), "objects") ??
            HarmonyLib.AccessTools.Field(typeof(ClientScene), "s_LocalObjects");
        private static IDictionary<NetworkInstanceId, NetworkIdentity>? _clientSceneObjects;

        static PersistenceSceneHandler()
        {

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

            foreach (var user in users)
            {
                if (Networking.NetworkUtils.GetPlayerIdString(user.id) == playerId)
                {
                    return user;
                }
            }

            if (users.Count == 1)
            {
                var onlyUser = users[0];
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[FindNetworkUserById] ID mismatch '{playerId}' != '{Networking.NetworkUtils.GetPlayerIdString(onlyUser.id)}', but only one player found. Using fallback.");
                }
                return onlyUser;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning($"[FindNetworkUserById] Failed to find owner {playerId} among {users.Count} players.");
            }

            return null;
        }

        public void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            if (!PersistenceObjectManager.GetCachedEnablePersistence())
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Persistence disabled, skipping scene change handling for {newScene.name}");
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Any player body found after {framesWaited} frames, proceeding with restoration");
                        }
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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" PersistenceCoroutineRunner destroyed - cleanup completed");
                }
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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Starting restoration of {persistedObjects.Count} persisted objects");
                }
                var objectsToRemove = new List<GameObject>();
                var successfullyRestoredObjects = new List<GameObject>();

                var persistedArray = persistedObjects.ToArray();

                foreach (var obj in persistedArray)
                {
                    if (obj == null)
                    {
                        objectsToRemove.Add(null!);
                        continue;
                    }

                    if (PluginConfig.IsPersistenceBlacklisted(obj))
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[RestorePersistedObjects] Skipping restoration of {obj.name}: Object is blacklisted.");

                        UnityEngine.Object.Destroy(obj);
                        objectsToRemove.Add(obj);
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

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Restoring object {obj.name} to scene (currently parented to: {obj.transform.parent?.name ?? "null"}) from {obj.scene.name}");
                    }
                    obj.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());

                    var colliderCache = obj.GetComponent<BodyColliderCache>();
                    if (colliderCache != null)
                    {
                        colliderCache.RefreshCache();
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RestorePersistedObjects] Refreshed BodyColliderCache for {obj.name}");
                        }
                    }

                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;

                        if (modelObj.transform.parent != obj.transform)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[RestorePersistedObjects] Re-parenting model {modelObj.name} to body {obj.name}");

                            modelObj.transform.SetParent(obj.transform, true);
                            modelObj.transform.localPosition = Vector3.zero;
                            modelObj.transform.localRotation = Quaternion.identity;
                        }

                        VisualRefreshUtility.Refresh(obj);
                    }

                    if (NetworkServer.active)
                    {

                        bool positionedCorrectly = PositionNearPlayer(obj);
                        var ownerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);

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

                            PositionNearPlayer(obj);

                            RegisterLocalObjectReflectively(networkIdentity);

                            if (obj.TryGetComponent<Rigidbody>(out var rb))
                            {
                                rb.isKinematic = true;
                                var coroutineRunner = new GameObject("ClientSafetyFloatRunner_" + obj.name);
                                var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
                                runner.StartCoroutine(ClientSafetyFloat(obj, runner));
                            }
                        }
                        else
                        {

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

                    obj.SetActive(true);
                    RestoreRenderers(obj);

                    HandleSpecialObjectRestoration(obj, duringSceneRestoration: true);

                    if (PersistenceObjectManager.GetCachedEnableAutoGrab())
                    {
                        if (NetworkServer.active)
                        {
                            ScheduleAutoGrabForObject(obj);
                        }
                    }

                    successfullyRestoredObjects.Add(obj);
                }

                foreach (var obj in objectsToRemove)
                {
                    persistedObjects.Remove(obj);
                }

                foreach (var obj in successfullyRestoredObjects)
                {
                    persistedObjects.Remove(obj);
                }

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
                    var existingState = BaggedObjectPatches.FindStateForObject(obj);
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

                    NetworkUser? matchedUser = FindNetworkUserById(_ownerPlayerId);

                    if (matchedUser != null)
                    {
                        var targetBody = matchedUser.master?.GetBody();
                        if (targetBody != null)
                        {

                            var playerPos = targetBody.transform.position;
                            var playerForward = targetBody.transform.forward;
                            var targetPos = playerPos + playerForward * Constants.Limits.PositionOffset + Vector3.up * Constants.Limits.PositionOffset;

                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[PersistedObjectSeeker] Found owner {targetBody.name} after {elapsed:F2}s. Teleporting {name} to {targetPos}");

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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[PersistedObjectSeeker] Timeout seeking owner for {name}. Staying at current position.");
                Destroy(this);
            }
        }

        private static bool PositionNearPlayer(GameObject obj)
        {

            var ownerPlayerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);
            CharacterBody? targetBody = null;
            bool ownerFound = false;

            if (!string.IsNullOrEmpty(ownerPlayerId))
            {

                var matchedUser = FindNetworkUserById(ownerPlayerId);
                if (matchedUser != null)
                {
                    targetBody = matchedUser.master?.GetBody();
                    if (targetBody != null) ownerFound = true;
                }
            }

            if (targetBody == null)
            {

                var hostUser = NetworkUser.readOnlyInstancesList.FirstOrDefault(nu => nu.isServer);
                if (hostUser != null && hostUser.master != null)
                {
                    targetBody = hostUser.master.GetBody();
                }
            }

            if (targetBody == null)
            {

                targetBody = NetworkUser.readOnlyLocalPlayersList.Count > 0 ? NetworkUser.readOnlyLocalPlayersList[0]?.master?.GetBody() : null;
            }

            if (targetBody != null)
            {

                var playerPos = targetBody.transform.position;
                var playerForward = targetBody.transform.forward;
                var targetPos = playerPos + playerForward * Constants.Limits.PositionOffset + Vector3.up * Constants.Limits.PositionOffset;
                obj.transform.position = targetPos;
                obj.transform.rotation = Quaternion.identity;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Positioned {obj.name} at {targetPos} near {((ownerFound) ? "owner" : "fallback")} body {targetBody.name} (Pos: {playerPos})");
                }
                return ownerFound;
            }
            else
            {

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

        public static void ScheduleAutoGrabForObject(GameObject obj, string? ownerPlayerId = null)
        {
            if (!NetworkServer.active) return;
            if (obj == null) return;

            var coroutineRunner = new GameObject("ServerAutoGrabRunner_" + obj.name);
            var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
            runner.StartCoroutine(DelayedAutoGrab(obj, ownerPlayerId, runner, PluginConfig.Instance.AutoGrabDelay.Value));
        }

        private static System.Collections.IEnumerator DelayedAutoGrab(GameObject obj, string? ownerPlayerId, PersistenceCoroutineRunner runner, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (obj != null)
            {
                TryAutoGrabObject(obj, ownerPlayerId);
            }

            if (runner != null && runner.gameObject != null) UnityEngine.Object.Destroy(runner.gameObject);
        }

        public static void TryAutoGrabObject(GameObject obj, string? ownerPlayerId = null)
        {
            if (!NetworkServer.active) return;

            if (obj == null) return;

            if (PluginConfig.IsPersistenceBlacklisted(obj))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[TryAutoGrabObject] Aborting auto-grab for {obj.name}: Object is blacklisted.");
                return;
            }

            if (obj.GetComponent<CharacterMaster>() != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Skipping auto-grab for {obj.name} - is CharacterMaster");
                }
                return;
            }

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

            CharacterBody? targetBody = null;
            if (string.IsNullOrEmpty(ownerPlayerId))
            {
                ownerPlayerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);
            }

            if (!string.IsNullOrEmpty(ownerPlayerId))
            {

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
            else
            {

                var users = NetworkUser.readOnlyInstancesList;
                if (users.Count == 1)
                {
                    var onlyUser = users[0];
                    targetBody = onlyUser.master?.GetBody();
                }
            }

            if (targetBody == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    if (!string.IsNullOrEmpty(ownerPlayerId))
                    {
                        var user = FindNetworkUserById(ownerPlayerId);
                        Log.Info($"[TryAutoGrabObject DEBUG] ownerPlayerId: {ownerPlayerId}");
                        Log.Info($"[TryAutoGrabObject DEBUG] user found: {user != null}");
                        if (user != null)
                        {
                            Log.Info($"[TryAutoGrabObject DEBUG] user.master found: {user.master != null}");
                            if (user.master != null)
                            {
                                Log.Info($"[TryAutoGrabObject DEBUG] user.master.GetBody() found: {user.master.GetBody() != null}");
                            }
                        }

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

                    var specAttr = obj.GetComponent<SpecialObjectAttributes>();
                    if (specAttr != null)
                    {
                        specAttr.childSpecialObjectAttributes?.RemoveAll(s => s == null);
                        specAttr.renderersToDisable?.RemoveAll(r => r == null);
                        specAttr.behavioursToDisable?.RemoveAll(b => b == null);
                        specAttr.childObjectsToDisable?.RemoveAll(c => c == null);
                        specAttr.pickupDisplaysToDisable?.RemoveAll(p => p == null);
                        specAttr.lightsToDisable?.RemoveAll(l => l == null);
                        specAttr.objectsToDetach?.RemoveAll(o => o == null);
                        specAttr.skillHighlightRenderers?.RemoveAll(r => r == null);
                    }

                    bagController.AssignPassenger(obj);

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
                    Log.Error($"[TryAutoGrabObject] Error assigning passenger: {ex}");
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

        // ========================================================================================
        // AUTO GRAB HELPERS
        // ========================================================================================
        public void ScheduleAutoGrab(CharacterMaster master)
        {
            if (!NetworkServer.active) return;
            if (!PersistenceObjectManager.GetCachedEnableAutoGrab()) return;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Executing immediate auto-grab for Drifter");
            }

            var body = master.GetBody();
            if (body == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" No body found for Drifter during auto-grab");
                }
                return;
            }

            var bagController = master.GetComponent<DrifterBagController>();

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

            string? drifterPlayerId = null;
            var characterBody = body.GetComponent<CharacterBody>();
            if (characterBody != null && characterBody.master != null && characterBody.master.playerCharacterMasterController != null)
            {
                var networkUserId = characterBody.master.playerCharacterMasterController.networkUser.id;

                drifterPlayerId = networkUserId.strValue != null
                    ? networkUserId.strValue
                    : $"{networkUserId.value}_{networkUserId.subId}";
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Drifter player ID: {drifterPlayerId}");
                }
            }

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

            var objectsToGrab = new List<GameObject>();
            objectsToGrab.AddRange(persistedObjectsInScene);
            objectsToGrab.AddRange(currentlyBaggedObjectsInScene);

            var filteredObjectsToGrab = new List<GameObject>();
            foreach (var obj in objectsToGrab)
            {
                var objOwnerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj);

                if (string.IsNullOrEmpty(objOwnerId) || objOwnerId == drifterPlayerId)
                {
                    filteredObjectsToGrab.Add(obj);
                }
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Total objects to attempt auto-grab for Drifter {drifterPlayerId}: {filteredObjectsToGrab.Count} (filtered from {objectsToGrab.Count} total)");
            }

            foreach (var obj in filteredObjectsToGrab)
            {

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

                    bool bagIsEmpty = BagCapacityCalculator.GetCurrentBaggedCount(bagController) == 0;
                    if (bagIsEmpty)
                    {

                        var bagStateMachine = EntityStateMachine.FindByCustomName(body.gameObject, "Bag");
                        if (bagStateMachine != null)
                        {
                            try
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" Found Bag state machine, setting BaggedObject state for {obj.name}");
                                }

                                var baggedObject = new BaggedObject();
                                baggedObject.targetObject = obj;

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

                        try
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Manually assigning CharacterBody {obj.name} to additional seat");
                            }

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

                            newSeat.AssignPassenger(obj);

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

                    try
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Directly assigning {obj.name} to bag for auto-grab (Suppression Enabled)");
                        }

                        bagController.AssignPassenger(obj);

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

        // ========================================================================================
        // SPECIAL OBJECT HANDLING
        // ========================================================================================
        public static void HandleSpecialObjectRestoration(GameObject obj, bool duringSceneRestoration = false)
        {
            if (obj == null) return;
            if (PluginConfig.IsPersistenceBlacklisted(obj))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[HandleSpecialObjectRestoration] Destroying blacklisted object {obj.name}");
                }
                UnityEngine.Object.Destroy(obj);
                return;
            }

            var teleporterInteraction = obj.GetComponent<RoR2.TeleporterInteraction>();

            string objName = obj.name.ToLower();

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

                if (isRestoringFromSceneChange)
                {
                    TeleporterPatches.PatchStaleReferences(teleporterInteraction);
                }

                MultiTeleporterTracker.RegisterSecondary(teleporterInteraction);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[HandleSpecialObjectRestoration] Successfully patched stale references for {obj.name}");
                }
            }

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

            var characterMaster = obj.GetComponent<CharacterMaster>();
            if (characterMaster != null)
            {
                var characterBody = characterMaster.GetBody();
                if (characterBody != null)
                {

                    var coroutineRunner = new GameObject("BossGroupCleanupRunner");
                    var runner = coroutineRunner.AddComponent<BossGroupCleanupRunner>();
                    runner.Initialize(characterMaster, obj.name);
                }
            }

            var animator = obj.GetComponent<Animator>();
            if (animator != null)
            {
                try
                {

                    if (animator.runtimeAnimatorController == null)
                    {

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

        // ========================================================================================
        // NETWORKING HELPERS
        // ========================================================================================
        private static void RegisterLocalObjectReflectively(NetworkIdentity networkIdentity)
        {
            try
            {

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
