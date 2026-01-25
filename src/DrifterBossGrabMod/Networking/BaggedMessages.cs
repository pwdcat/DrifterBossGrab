using System.Collections.Generic;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.Networking
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

    // Network message for syncing bag state
    public class UpdateBagStateMessage : MessageBase
    {
        public NetworkInstanceId controllerNetId;
        public int selectedIndex;
        public uint[] baggedIds = System.Array.Empty<uint>();
        public uint[] seatIds = System.Array.Empty<uint>();

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(controllerNetId);
            writer.Write(selectedIndex);
            
            writer.Write(baggedIds.Length);
            foreach (var id in baggedIds) writer.Write(id);
            
            writer.Write(seatIds.Length);
            foreach (var id in seatIds) writer.Write(id);
        }

        public override void Deserialize(NetworkReader reader)
        {
            controllerNetId = reader.ReadNetworkId();
            selectedIndex = reader.ReadInt32();
            
            int count = reader.ReadInt32();
            baggedIds = new uint[count];
            for (int i = 0; i < count; i++) baggedIds[i] = reader.ReadUInt32();
            
            count = reader.ReadInt32();
            seatIds = new uint[count];
            for (int i = 0; i < count; i++) seatIds[i] = reader.ReadUInt32();
        }
    }
    // Network message for requesting a cycle (Client -> Server)
    public class CyclePassengersMessage : MessageBase
    {
        public NetworkInstanceId bagControllerNetId = NetworkInstanceId.Invalid;
        public bool scrollUp;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(bagControllerNetId);
            writer.Write(scrollUp);
        }

        public override void Deserialize(NetworkReader reader)
        {
            bagControllerNetId = reader.ReadNetworkId();
            scrollUp = reader.ReadBoolean();
        }
    }
}
