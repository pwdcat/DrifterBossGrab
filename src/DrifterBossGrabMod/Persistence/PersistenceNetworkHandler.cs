using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2.Networking;

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
            foreach (var netId in message.baggedObjectNetIds)
            {
                GameObject obj = ClientScene.FindLocalObject(netId);
                if (obj != null && PersistenceObjectManager.IsValidForPersistence(obj))
                {
                    PersistenceObjectManager.AddPersistedObject(obj);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[HandleBaggedObjectsPersistenceMessage] Added object {obj.name} (netId: {netId}) to persistence from network message");
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[HandleBaggedObjectsPersistenceMessage] Object with netId {netId} not found or invalid for persistence");
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
                    NetworkIdentity? identity = obj.GetComponent<NetworkIdentity>();
                    if (identity != null)
                    {
                        message.baggedObjectNetIds.Add(identity.netId);
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
