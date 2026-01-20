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

namespace DrifterBossGrabMod
{
    public class PersistenceSceneHandler : IPersistenceManager
    {
        public static IPersistenceManager Instance { get; } = new PersistenceSceneHandler();
        // Handle scene change
        public void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" OnSceneChanged called - EnablePersistence: {PersistenceObjectManager.GetCachedEnablePersistence()}, from {oldScene.name} to {newScene.name}");
            }
            // Register network message handler if client is available
            PersistenceNetworkHandler.RegisterNetworkHandlers();
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
            // Delay restoration to ensure player is fully spawned
            // Use a coroutine to wait for the next frame when player bodies are available
            var coroutineRunner = new GameObject("PersistenceCoroutineRunner");
            var runner = coroutineRunner.AddComponent<PersistenceCoroutineRunner>();
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
                // Wait additional frames until local player body is available
                int maxWaitFrames = 300;
                int framesWaited = 0;
                while (framesWaited < maxWaitFrames)
                {
                    var localPlayerBody = NetworkUser.readOnlyLocalPlayersList.Count > 0 ? NetworkUser.readOnlyLocalPlayersList[0]?.master?.GetBody() : null;
                    if (localPlayerBody != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Local player body found after {framesWaited} frames, proceeding with restoration");
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
                        Log.Info($" Timeout waiting for local player body after {maxWaitFrames} frames, proceeding with restoration anyway");
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
                catch (System.Exception ex)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Error during BossGroup cleanup for {_objectName}: {ex.Message}");
                    }
                }
                // Clean up this runner
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        // Restore persisted objects
        private static void RestorePersistedObjects()
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
                foreach (var obj in persistedObjects.ToArray())
                {
                    if (obj == null)
                    {
                        objectsToRemove.Add(null!);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Removing null object from persisted objects");
                        }
                        continue;
                    }
                    string objName = obj.name.ToLower();
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Restoring object {obj.name} to scene (currently parented to: {obj.transform.parent?.name ?? "null"})");
                    }
                    // Move back to scene and remove DontDestroyOnLoad
                    obj.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" After SetParent and MoveGameObjectToScene, {obj.name} is now in scene: {obj.scene.name}, parented to: {obj.transform.parent?.name ?? "null"}");
                    }
                    // Spawn on network if server
                    var networkIdentity = obj.GetComponent<NetworkIdentity>();
                    if (networkIdentity != null && NetworkServer.active)
                    {
                        NetworkServer.Spawn(obj);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Spawned persisted object {obj.name} on network");
                        }
                    }
                    // Position near player
                    PositionNearPlayer(obj);
                    // Removed GrabbedObjectState persistence state restoration - testing if SpecialObjectAttributes handles this automatically
                    // Special handling for teleporters and portals
                    HandleSpecialObjectRestoration(obj);
                    // Attempt auto-grab if enabled
                    if (PersistenceObjectManager.GetCachedEnableAutoGrab())
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Attempting auto-grab for {obj.name}");
                        }
                        TryAutoGrabObject(obj);
                    }
                    else
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Auto-regab disabled, skipping auto-grab for {obj.name}");
                        }
                    }
                    // Remove from persistence tracking since object is now in the scene
                    persistedObjects.Remove(obj);
                    // Mark as available for re-grabbing
                    // Objects are now in the scene and can be grabbed again
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Successfully restored {obj.name} to new scene at position {obj.transform.position}");
                    }
                }
                // Remove null objects
                foreach (var obj in objectsToRemove)
                {
                    persistedObjects.Remove(obj);
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    int totalProcessed = persistedObjects.Count + objectsToRemove.Count;
                    Log.Info($" Finished restoring persisted objects. {totalProcessed} total objects processed, {objectsToRemove.Count} null objects removed. Remaining persisted: {persistedObjects.Count}");
                }
            }
        }

        // Position object near player
        private static void PositionNearPlayer(GameObject obj)
        {
            var localPlayerBody = NetworkUser.readOnlyLocalPlayersList.Count > 0 ? NetworkUser.readOnlyLocalPlayersList[0]?.master?.GetBody() : null;
            if (localPlayerBody != null)
            {
                // Position very close to player (0.5 units in front)
                var playerPos = localPlayerBody.transform.position;
                var playerForward = localPlayerBody.transform.forward;
                var targetPos = playerPos + playerForward * 0.5f + Vector3.up * 0.5f;
                obj.transform.position = targetPos;
                obj.transform.rotation = Quaternion.identity; // Reset rotation
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Positioned {obj.name} near local player");
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Used camera fallback positioning for {obj.name}");
                    }
                }
                else
                {
                    // Last resort: position at origin with offset
                    obj.transform.position = new Vector3(0, 1, 0);
                    obj.transform.rotation = Quaternion.identity;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Used origin fallback positioning for {obj.name}");
                    }
                }
            }
        }

        // Try to auto-grab a restored object
        private static void TryAutoGrabObject(GameObject obj)
        {
            if (obj == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" TryAutoGrabObject called with null object");
                }
                return;
            }
            // Skip CharacterMaster objects (AI controllers) but allow environment objects
            if (obj.GetComponent<CharacterMaster>() != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Skipping auto-grab for {obj.name} - is CharacterMaster");
                }
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Attempting auto-grab for restored object {obj.name}");
            }
            // Find Drifter player
            var drifterPlayers = PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master.GetBody()?.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody"))
                .ToList();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Found {drifterPlayers.Count} Drifter players for auto-grab");
            }
            if (drifterPlayers.Count == 0)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" No Drifter players found for auto-grab of {obj.name}");
                }
                return;
            }
            // Try to grab with each Drifter (in case of multiple players)
            foreach (var drifter in drifterPlayers)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Checking Drifter player {drifter.master.name} for auto-grab");
                }
                // Get the character body - the bag state machine is on the body, not the master
                var body = drifter.master.GetBody();
                if (body == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" No character body found for Drifter player");
                    }
                    continue;
                }
                // Try to find bag controller on the body
                var bagController = body.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" No DrifterBagController found on Drifter body");
                    }
                    continue;
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Found DrifterBagController, hasRoom: {Patches.BagPatches.HasRoomForGrab(bagController)}");
                }
                if (Patches.BagPatches.HasRoomForGrab(bagController))
                {
                    try
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Assigning {obj.name} to bag using AssignPassenger");
                        }
                        bagController.AssignPassenger(obj);
                        // Update UI if this object is now in the main seat
                        if (Patches.BagPatches.GetMainSeatObject(bagController) == obj)
                        {
                            Patches.BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, obj);
                        }
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Successfully auto-grabbed {obj.name} using AssignPassenger");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Auto-grab failed for {obj.name}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Drifter bag is full, cannot auto-grab {obj.name}");
                    }
                }
            }
            // If we get here, auto-grab failed
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Auto-grab failed for {obj.name} - all Drifter bags full or unavailable");
            }
        }

        // Schedule auto-grab for Drifter
        public void ScheduleAutoGrab(CharacterMaster master)
        {
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
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Total objects to attempt auto-grab: {objectsToGrab.Count}");
            }
            // Try to grab each object
            foreach (var obj in objectsToGrab)
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
                if (!Patches.BagPatches.HasRoomForGrab(bagController))
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
                    bool bagIsEmpty = Patches.BagPatches.GetCurrentBaggedCount(bagController) == 0;
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
                            catch (System.Exception ex)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($" Auto-grab failed for {obj.name}: {ex.Message}");
                                }
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
                            if (!Patches.BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list))
                            {
                                list = new List<GameObject>();
                                Patches.BagPatches.baggedObjectsDict[bagController] = list;
                            }
                            if (!list.Contains(obj))
                            {
                                list.Add(obj);
                            }
                            if (!Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
                            {
                                seatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>();
                                Patches.BagPatches.additionalSeatsDict[bagController] = seatDict;
                            }
                            seatDict[obj] = newSeat;

                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Successfully auto-grabbed CharacterBody {obj.name} to additional seat");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($" Auto-grab failed for CharacterBody {obj.name}: {ex.Message}");
                            }
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
                            Log.Info($" Directly assigning {obj.name} to bag for auto-grab");
                        }
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
                    catch (System.Exception ex)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Auto-grab failed for {obj.name}: {ex.Message}");
                        }
                    }
                }
            }
        }

        // Handle special restoration logic
        private static void HandleSpecialObjectRestoration(GameObject obj)
        {
            if (obj == null) return;
            string objName = obj.name.ToLower();
            // Handle teleporters - disable if there's another active teleporter
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($" Checking for TeleporterInteraction on persisted object {obj.name}");
            }
            var teleporterInteraction = obj.GetComponentInChildren<RoR2.TeleporterInteraction>();
            if (teleporterInteraction != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Found TeleporterInteraction on {teleporterInteraction.gameObject.name} for persisted object {obj.name}");
                }
                // Check if there's another teleporter in the scene that is not disabled
                bool hasActiveTeleporter = HasActiveTeleporterInScene(teleporterInteraction.gameObject);
                if (hasActiveTeleporter)
                {
                    teleporterInteraction.enabled = false;
                    // Mark the GameObject that has the TeleporterInteraction for disabling in FixedUpdate
                    PersistenceManager.MarkTeleporterForDisabling(teleporterInteraction.gameObject);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Disabled TeleporterInteraction on persisted teleporter {obj.name}, marked {teleporterInteraction.gameObject.name} for FixedUpdate disabling - active teleporter found");
                    }
                }
                else
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Left TeleporterInteraction enabled on persisted teleporter {obj.name} - no active teleporter found");
                    }
                }
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
                catch (System.Exception ex)
                {
                    // If animator is corrupted, disable it
                    animator.enabled = false;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Disabled corrupted Animator on {obj.name} due to error: {ex.Message}");
                    }
                }
            }
        }

        // Check if there's another active teleporter in the scene
        private static bool HasActiveTeleporterInScene(GameObject excludeTeleporter)
        {
            var allTeleporters = UnityEngine.Object.FindObjectsByType<RoR2.TeleporterInteraction>(FindObjectsSortMode.None);
            foreach (var teleporter in allTeleporters)
            {
                if (teleporter.gameObject != excludeTeleporter && teleporter.enabled && !PersistenceManager.ShouldDisableTeleporter(teleporter.gameObject))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

