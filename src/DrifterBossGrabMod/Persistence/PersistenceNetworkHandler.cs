using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2.Networking;
using RoR2;

namespace DrifterBossGrabMod
{
    // Network message for broadcasting bagged objects for persistence
    public class BaggedObjectsPersistenceMessage : MessageBase
    {
        public List<NetworkInstanceId> baggedObjectNetIds = new List<NetworkInstanceId>();
        public List<string> ownerPlayerIds = new List<string>();
        public override void Serialize(NetworkWriter writer)
        {
            writer.Write((int)baggedObjectNetIds.Count);
            foreach (var netId in baggedObjectNetIds)
            {
                writer.Write(netId);
            }
            writer.Write((int)ownerPlayerIds.Count);
            foreach (var playerId in ownerPlayerIds)
            {
                writer.Write(playerId);
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
            count = reader.ReadInt32();
            ownerPlayerIds.Clear();
            for (int i = 0; i < count; i++)
            {
                ownerPlayerIds.Add(reader.ReadString());
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

    public static class PersistenceNetworkHandler
    {
        private const short BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE = 85;

        // Handle incoming bagged objects persistence messages
        public static void HandleBaggedObjectsPersistenceMessage(NetworkMessage netMsg)
        {
            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            message.Deserialize(netMsg.reader);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[HandleBaggedObjectsPersistenceMessage] Received bagged objects persistence message with {message.baggedObjectNetIds.Count} objects");
            }
            // Add the received objects to persistence
            for (int i = 0; i < message.baggedObjectNetIds.Count; i++)
            {
                var netId = message.baggedObjectNetIds[i];
                string? ownerPlayerId = null;
                if (i < message.ownerPlayerIds.Count)
                {
                    ownerPlayerId = message.ownerPlayerIds[i];
                }
                GameObject obj = ClientScene.FindLocalObject(netId);
                if (obj != null && PersistenceObjectManager.IsValidForPersistence(obj))
                {
                    PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[HandleBaggedObjectsPersistenceMessage] Added object {obj.name} (netId: {netId}) to persistence from network message");
                    }
                }
                else
                {
                    // Start retry coroutine for unfound objects
                    DrifterBossGrabPlugin.Instance.StartCoroutine(RetryFindObject(netId, ownerPlayerId));
                }
            }
        }

        private static System.Collections.IEnumerator RetryFindObject(NetworkInstanceId netId, string? ownerPlayerId = null)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                // Wait 10 frames
                for (int frame = 0; frame < 10; frame++)
                {
                    yield return null;
                }
                GameObject obj = ClientScene.FindLocalObject(netId);
                if (obj != null && PersistenceObjectManager.IsValidForPersistence(obj))
                {
                    PersistenceObjectManager.AddPersistedObject(obj, ownerPlayerId);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[RetryFindObject] Added object {obj.name} (netId: {netId}) to persistence after retry");
                    }
                    yield break;
                }
            }
            Log.Error($"[RetryFindObject] Failed to find object with netId {netId} after 5 retries");
        }

        // Send bagged objects persistence message to all clients
        public static void SendBaggedObjectsPersistenceMessage(List<GameObject> baggedObjects, DrifterBagController? owner = null)
        {
            if (baggedObjects == null || baggedObjects.Count == 0) return;
            BaggedObjectsPersistenceMessage message = new BaggedObjectsPersistenceMessage();
            foreach (var obj in baggedObjects)
            {
                if (obj != null)
                {
                    NetworkIdentity? identity = obj.GetComponent<NetworkIdentity>();
                    if (identity != null)
                    {
                        message.baggedObjectNetIds.Add(identity.netId);
                        // Add owner player id if available
                        if (owner != null)
                        {
                            var ownerBody = owner.GetComponent<CharacterBody>();
                            if (ownerBody != null && ownerBody.master != null && ownerBody.master.playerCharacterMasterController != null)
                            {
                                message.ownerPlayerIds.Add(ownerBody.master.playerCharacterMasterController.networkUser.id.ToString());
                            }
                            else
                            {
                                message.ownerPlayerIds.Add(string.Empty);
                            }
                        }
                        else
                        {
                            message.ownerPlayerIds.Add(string.Empty);
                        }
                    }
                }
            }
            if (message.baggedObjectNetIds.Count > 0)
            {
                NetworkServer.SendToAll(BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, message);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[SendBaggedObjectsPersistenceMessage] Sent bagged objects persistence message with {message.baggedObjectNetIds.Count} objects");
                }
            }
        }

        // Register network message handler
        public static void RegisterNetworkHandlers()
        {
            if (UnityEngine.Networking.NetworkManager.singleton?.client != null)
            {
                UnityEngine.Networking.NetworkManager.singleton.client.RegisterHandler(BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE, HandleBaggedObjectsPersistenceMessage);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[RegisterNetworkHandlers] Registered bagged objects persistence message handler");
                }
            }
        }
    }
}
