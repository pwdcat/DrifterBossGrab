using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using RoR2;
using RoR2.Networking;
using RoR2.Projectile;

namespace DrifterBossGrabMod
{
    // Network message for broadcasting bagged objects for persistence
    public class BaggedObjectsPersistenceMessage : MessageBase
    {
        public List<NetworkInstanceId> baggedObjectNetIds = new List<NetworkInstanceId>();

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write((int)baggedObjectNetIds.Count);
            foreach (var netId in baggedObjectNetIds)
            {
                writer.Write(netId);
            }
        }

        public override void Deserialize(NetworkReader reader)
        {
            int count = reader.ReadInt32();
            baggedObjectNetIds.Clear();
            for (int i = 0; i < count; i++)
            {
                baggedObjectNetIds.Add(reader.ReadNetworkId());
            }
        }
    }

    // Network message for removing objects from persistence
    public class RemoveFromPersistenceMessage : MessageBase
    {
        public NetworkInstanceId objectNetId;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(objectNetId);
        }

        public override void Deserialize(NetworkReader reader)
        {
            objectNetId = reader.ReadNetworkId();
        }
    }
    public static class PersistenceManager
    {
        // Singleton instance
        private static GameObject _persistenceContainer;
        private static readonly HashSet<GameObject> _persistedObjects = new HashSet<GameObject>();
        private static readonly object _lock = new object();

        // Cached config values for performance
        private static bool _cachedEnablePersistence;
        private static bool _cachedEnableAutoGrab;


        // Tracking for objects that should have TeleporterInteraction disabled
        private static readonly HashSet<GameObject> _teleportersToDisable = new HashSet<GameObject>();

        // Flag to prevent adding objects to persistence after restoration
        private static bool _hasRestoredThisScene = false;

        // Constants
        private const string PERSISTENCE_CONTAINER_NAME = "DBG_PersistenceContainer";
        private const short BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE = 85;

        // Initialization
        public static void Initialize()
        {
            if (_persistenceContainer != null) return;

            _persistenceContainer = new GameObject(PERSISTENCE_CONTAINER_NAME);
            UnityEngine.Object.DontDestroyOnLoad(_persistenceContainer);

            UpdateCachedConfig();
        }

        // Handle incoming bagged objects persistence messages
        public static void HandleBaggedObjectsPersistenceMessage(NetworkMessage netMsg)
        {

            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            message.Deserialize(netMsg.reader);

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Received bagged objects persistence message with {message.baggedObjectNetIds.Count} objects");
            }

            // Add the received objects to persistence
            foreach (var netId in message.baggedObjectNetIds)
            {
                GameObject obj = ClientScene.FindLocalObject(netId);
                if (obj != null && IsValidForPersistence(obj))
                {
                    AddPersistedObject(obj);
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Added object {obj.name} (netId: {netId}) to persistence from network message");
                    }
                }
                else if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Object with netId {netId} not found or invalid for persistence");
                }
            }
        }

        // Send bagged objects persistence message to all clients
        public static void SendBaggedObjectsPersistenceMessage(List<GameObject> baggedObjects)
        {
            if (baggedObjects == null || baggedObjects.Count == 0) return;

            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();

            foreach (var obj in baggedObjects)
            {
                if (obj != null)
                {
                    NetworkIdentity identity = obj.GetComponent<NetworkIdentity>();
                    if (identity != null)
                    {
                        message.baggedObjectNetIds.Add(identity.netId);
                    }
                }
            }

            if (message.baggedObjectNetIds.Count > 0)
            {
                NetworkServer.SendToAll(BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, message);

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Sent bagged objects persistence message with {message.baggedObjectNetIds.Count} objects");
                }
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
                if (_persistedObjects.Add(obj))
                {
                    // Move to persistence container
                    obj.transform.SetParent(_persistenceContainer.transform, true);

                    // Also persist the model if it exists and ModelLocator is enabled
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.enabled && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;
                        if (modelObj != obj) // Only if it's a separate object
                        {
                            modelObj.transform.SetParent(_persistenceContainer.transform, true);
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Also persisted model {modelObj.name} for {obj.name}");
                            }
                        }
                    }

                    // Also persist the master for CharacterBody objects
                    var characterBody = obj.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.master != null && characterBody.master.gameObject != null && IsValidForPersistence(characterBody.master.gameObject))
                    {
                        AddPersistedObject(characterBody.master.gameObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Also persisted master {characterBody.master.name} for {obj.name}");
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
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());

                    // Also remove model from persistence if it exists
                    var modelLocator = obj.GetComponent<ModelLocator>();
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var modelObj = modelLocator.modelTransform.gameObject;
                        if (modelObj.transform.parent == _persistenceContainer.transform)
                        {
                            modelObj.transform.SetParent(null, true);
                            SceneManager.MoveGameObjectToScene(modelObj, SceneManager.GetActiveScene());
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Also removed model {modelObj.name} from persistence for {obj.name}");
                            }
                        }
                    }

                    // Also remove master from persistence if it exists
                    var characterBody = obj.GetComponent<CharacterBody>();
                    if (characterBody != null && characterBody.master != null && characterBody.master.gameObject != null)
                    {
                        var masterObj = characterBody.master.gameObject;
                        if (masterObj.transform.parent == _persistenceContainer.transform)
                        {
                            masterObj.transform.SetParent(null, true);
                            SceneManager.MoveGameObjectToScene(masterObj, SceneManager.GetActiveScene());
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Also removed master {masterObj.name} from persistence for {obj.name}");
                            }
                        }
                    }

                    // Re-attach model if it exists
                    if (modelLocator != null && modelLocator.modelTransform != null)
                    {
                        var temp = modelLocator.modelTransform;
                        modelLocator.modelTransform = null;
                        modelLocator.modelTransform = temp;
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Re-attached model for {obj.name} after removal from persistence");
                        }
                    }

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

        // Check if the current scene has any teleporters
        private static bool SceneHasTeleporter()
        {
            var teleporters = UnityEngine.Object.FindObjectsOfType<TeleporterInteraction>(false);
            return teleporters.Length > 0;
        }

        // Check if there's another active teleporter in the scene
        private static bool HasActiveTeleporterInScene(GameObject excludeTeleporter)
        {
            var allTeleporters = UnityEngine.Object.FindObjectsOfType<RoR2.TeleporterInteraction>(false);
            foreach (var teleporter in allTeleporters)
            {
                if (teleporter.gameObject != excludeTeleporter && teleporter.enabled && !ShouldDisableTeleporter(teleporter.gameObject))
                {
                    return true;
                }
            }
            return false;
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

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Capturing {baggedObjects.Count} currently bagged objects for persistence");
            }

            foreach (var obj in baggedObjects)
            {
                if (IsValidForPersistence(obj))
                {
                    AddPersistedObject(obj);
                }
            }

            // Synchronize persistence across clients in multiplayer
            if (UnityEngine.Networking.NetworkServer.active)
            {
                SendBaggedObjectsPersistenceMessage(baggedObjects);
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Sent bagged objects persistence message for {baggedObjects.Count} objects");
                }
            }
        }


        // Get all objects currently in Drifter bags
        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            // Use the centralized detection method
            return Patches.BaggedObjectsOnlyDetection.GetCurrentlyBaggedObjects();
        }

        // Check if object is valid for persistence
        private static bool IsValidForPersistence(GameObject obj)
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

            // Reset restoration flag for new scene
            _hasRestoredThisScene = false;

            // Register network message handler if client is available
            if (UnityEngine.Networking.NetworkManager.singleton?.client != null)
            {
                UnityEngine.Networking.NetworkManager.singleton.client.RegisterHandler(BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, HandleBaggedObjectsPersistenceMessage);
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Registered bagged objects persistence message handler");
                }
            }

            if (!_cachedEnablePersistence)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Persistence disabled, skipping scene change handling");
                }
                return;
            }


            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Scene changed from {oldScene.name} to {newScene.name}, restoring {GetPersistedObjectsCount()} persisted objects");
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
                runner = UnityEngine.Object.FindObjectOfType<PersistenceCoroutineRunner>();
                
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

                // Mark that restoration has completed for this scene
                _hasRestoredThisScene = true;
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
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} PersistenceCoroutineRunner destroyed - cleanup completed");
                }
            }
        }


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
                    // Spawn on network if server
                    var networkIdentity = obj.GetComponent<NetworkIdentity>();
                    if (networkIdentity != null && NetworkServer.active)
                    {
                        NetworkServer.Spawn(obj);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Spawned persisted object {obj.name} on network");
                        }
                    }
                    // Position near player
                    PositionNearPlayer(obj);

                    // Removed GrabbedObjectState persistence state restoration - testing if SpecialObjectAttributes handles this automatically

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

            if (players.Count > 0)
            {
                var playerMaster = players[0].master;
                var playerBody = playerMaster.GetBody();

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
                        Log.Info($"{Constants.LogPrefix} Positioned {obj.name} near player");
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
                            Log.Info($"{Constants.LogPrefix} Used camera fallback positioning for {obj.name}");
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
            if (obj == null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} TryAutoGrabObject called with null object");
                }
                return;
            }

            // Skip CharacterMaster objects (AI controllers) but allow environment objects
            if (obj.GetComponent<CharacterMaster>() != null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Skipping auto-grab for {obj.name} - is CharacterMaster");
                }
                return;
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Attempting auto-grab for restored object {obj.name}");
            }

            // Find Drifter player
            var drifterPlayers = PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master.GetBody()?.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody"))
                .ToList();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Found {drifterPlayers.Count} Drifter players for auto-grab");
            }

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
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Checking Drifter player {drifter.master.name} for auto-grab");
                }

                // Get the character body - the bag state machine is on the body, not the master
                var body = drifter.master.GetBody();
                if (body == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} No character body found for Drifter player");
                    }
                    continue;
                }

                // Try to find bag controller on the body
                var bagController = body.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} No DrifterBagController found on Drifter body");
                    }
                    continue;
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Found DrifterBagController, bagFull: {bagController.bagFull}");
                }

                if (!bagController.bagFull)
                {
                    // Use the proper EntityStateMachine approach like RepossessExit does
                    // The bag state machine is on the character body
                    var bagStateMachine = EntityStateMachine.FindByCustomName(body.gameObject, "Bag");
                    if (bagStateMachine != null)
                    {
                        try
                        {
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Found Bag state machine, setting BaggedObject state for {obj.name}");
                            }

                            // Create BaggedObject state and set target
                            var baggedObject = new EntityStates.Drifter.Bag.BaggedObject();
                            baggedObject.targetObject = obj;

                            // Set the next state on the bag state machine
                            bagStateMachine.SetNextState(baggedObject);

                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Successfully initiated auto-grab for {obj.name} using proper state machine");
                            }
                            return; // Successfully grabbed, exit
                        }
                        catch (System.Exception ex)
                        {
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Auto-grab failed for {obj.name}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Could not find Bag state machine on character body for auto-grab");
                        }
                    }
                }
                else
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

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Executing immediate auto-grab for Drifter");
            }

            // Get the Drifter's body and bag controller
            var body = master.GetBody();
            if (body == null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} No body found for Drifter during auto-grab");
                }
                return;
            }

            // Try to find bag controller on the master first (same logic as GetCurrentlyBaggedObjects)
            var bagController = master.GetComponent<DrifterBagController>();

            // If not found on master, try to find it on the body
            if (bagController == null)
            {
                bagController = body.GetComponent<DrifterBagController>();
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Found bag controller on body during auto-grab");
                }
            }
            else
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Found bag controller on master during auto-grab");
                }
            }

            if (bagController == null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} No DrifterBagController found on Drifter master or body");
                }
                return;
            }

            // Find all persisted objects in the current scene
            var persistedObjectsInScene = new List<GameObject>();
            lock (_lock)
            {
                foreach (var obj in _persistedObjects)
                {
                    if (obj != null && obj.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                    {
                        persistedObjectsInScene.Add(obj);
                    }
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Found {persistedObjectsInScene.Count} persisted objects in scene for auto-grab");
            }

            // Also find currently bagged objects in the scene (for same-stage respawns)
            var currentlyBaggedObjectsInScene = new List<GameObject>();
            var allCurrentlyBagged = GetCurrentlyBaggedObjects();
            foreach (var obj in allCurrentlyBagged)
            {
                if (obj != null && obj.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                {
                    currentlyBaggedObjectsInScene.Add(obj);
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Found {currentlyBaggedObjectsInScene.Count} currently bagged objects in scene for auto-grab");
            }

            // Combine both lists, preferring persisted objects first
            var objectsToGrab = new List<GameObject>();
            objectsToGrab.AddRange(persistedObjectsInScene);
            objectsToGrab.AddRange(currentlyBaggedObjectsInScene);

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Total objects to attempt auto-grab: {objectsToGrab.Count}");
            }

            // Try to grab each object
            foreach (var obj in objectsToGrab)
            {
                // Skip CharacterMaster objects (AI controllers) but allow environment objects
                if (obj.GetComponent<CharacterMaster>() != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Skipping auto-grab for {obj.name} - is CharacterMaster");
                    }
                    continue;
                }

                if (bagController.bagFull)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Drifter bag is full, stopping auto-grab");
                    }
                    break;
                }

                // Use the proper EntityStateMachine approach like RepossessExit does
                // The bag state machine is on the character body, not the master
                var bagStateMachine = EntityStateMachine.FindByCustomName(body.gameObject, "Bag");
                if (bagStateMachine != null)
                {
                    try
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Found Bag state machine, setting BaggedObject state for {obj.name}");
                        }

                        // Create BaggedObject state and set target
                        var baggedObject = new EntityStates.Drifter.Bag.BaggedObject();
                        baggedObject.targetObject = obj;

                        // Set the next state on the bag state machine
                        bagStateMachine.SetNextState(baggedObject);

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Successfully initiated auto-grab for {obj.name} using proper state machine");
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
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Could not find Bag state machine on character body for auto-grab");
                    }
                }
            }
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
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Marked {obj.name} for teleporter disabling, total marked: {_teleportersToDisable.Count}");
                }
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

            // Handle teleporters - disable if there's another active teleporter
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Checking for TeleporterInteraction on persisted object {obj.name}");
            }
            var teleporterInteraction = obj.GetComponentInChildren<RoR2.TeleporterInteraction>();
            if (teleporterInteraction != null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Found TeleporterInteraction on {teleporterInteraction.gameObject.name} for persisted object {obj.name}");
                }

                // Check if there's another teleporter in the scene that is not disabled
                bool hasActiveTeleporter = HasActiveTeleporterInScene(teleporterInteraction.gameObject);
                if (hasActiveTeleporter)
                {
                    teleporterInteraction.enabled = false;
                    // Mark the GameObject that has the TeleporterInteraction for disabling in FixedUpdate
                    MarkTeleporterForDisabling(teleporterInteraction.gameObject);

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Disabled TeleporterInteraction on persisted teleporter {obj.name}, marked {teleporterInteraction.gameObject.name} for FixedUpdate disabling - active teleporter found");
                    }
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Left TeleporterInteraction enabled on persisted teleporter {obj.name} - no active teleporter found");
                    }
                }
            }
            else
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} No TeleporterInteraction found on persisted object {obj.name}");
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
        }
    }
}