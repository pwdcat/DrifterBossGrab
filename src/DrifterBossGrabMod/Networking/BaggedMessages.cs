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
        public int scrollDirection;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(controllerNetId);
            writer.Write(selectedIndex);
            writer.Write(scrollDirection);

            writer.Write(baggedIds.Length);
            foreach (var id in baggedIds) writer.Write(id);

            writer.Write(seatIds.Length);
            foreach (var id in seatIds) writer.Write(id);
        }

        public override void Deserialize(NetworkReader reader)
        {
            controllerNetId = reader.ReadNetworkId();
            selectedIndex = reader.ReadInt32();
            scrollDirection = reader.ReadInt32();

            int count = reader.ReadInt32();
            baggedIds = new uint[count];
            for (int i = 0; i < count; i++) baggedIds[i] = reader.ReadUInt32();

            int count2 = reader.ReadInt32();
            seatIds = new uint[count2];
            for (int i = 0; i < count2; i++) seatIds[i] = reader.ReadUInt32();
        }
    }
    // Network message for requesting a cycle (Client -> Server)
    public class CyclePassengersMessage : MessageBase
    {
        public NetworkInstanceId bagControllerNetId = NetworkInstanceId.Invalid;
        public int amount;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(bagControllerNetId);
            writer.Write(amount);
        }

        public override void Deserialize(NetworkReader reader)
        {
            bagControllerNetId = reader.ReadNetworkId();
            amount = reader.ReadInt32();
        }
    }

    // Network message for client to send bag state to server (Client -> Server)
    public class ClientUpdateBagStateMessage : MessageBase
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

            int count2 = reader.ReadInt32();
            seatIds = new uint[count2];
            for (int i = 0; i < count2; i++) seatIds[i] = reader.ReadUInt32();
        }
    }

    // Network message for grabbing an object (Client -> Server)
    // This message is sent when a client grabs an object via RepossessExit
    public class GrabObjectMessage : MessageBase
    {
        public NetworkInstanceId bagControllerNetId = NetworkInstanceId.Invalid;
        public NetworkInstanceId targetObjectNetId = NetworkInstanceId.Invalid;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(bagControllerNetId);
            writer.Write(targetObjectNetId);
        }

        public override void Deserialize(NetworkReader reader)
        {
            bagControllerNetId = reader.ReadNetworkId();
            targetObjectNetId = reader.ReadNetworkId();
        }
    }

    // Network message for syncing config from Host to Client
    public class SyncConfigMessage : MessageBase
    {
        // General Grabbing
        public bool EnableBossGrabbing;
        public bool EnableNPCGrabbing;
        public bool EnableEnvironmentGrabbing;
        public bool EnableLockedObjectGrabbing;
        public ProjectileGrabbingMode ProjectileGrabbingMode;

        // Skill Scalars
        public float SearchRangeMultiplier;
        public float BreakoutTimeMultiplier;
        public float ForwardVelocityMultiplier;
        public float UpwardVelocityMultiplier;
        public int MaxSmacks;
        public string MassMultiplier = string.Empty;

        // Blacklists & Component Types
        public string BodyBlacklist = string.Empty;
        public string RecoveryObjectBlacklist = string.Empty;
        public string GrabbableComponentTypes = string.Empty;
        public string GrabbableKeywordBlacklist = string.Empty;

        // Persistence
        public bool EnableObjectPersistence; // Important sync
        public bool EnableAutoGrab;
        public bool PersistBaggedBosses;
        public bool PersistBaggedNPCs;
        public bool PersistBaggedEnvironmentObjects;
        public float AutoGrabDelay;

        // Bottomless Bag
        public bool BottomlessBagEnabled;
        public int BottomlessBagBaseCapacity;
        public bool UncapBagScale;
        public bool UncapMass;

        // Balance - All toggle fields
        public bool EnableBalance;
        public bool EnableAoESlamDamage;
        public bool EliteMassBonusEnabled;
        public bool EnableOverencumbrance;
        public bool UncapCapacity;
        public bool ToggleMassCapacity;
        public bool StateCalculationModeEnabled;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(EnableBossGrabbing);
            writer.Write(EnableNPCGrabbing);
            writer.Write(EnableEnvironmentGrabbing);
            writer.Write(EnableLockedObjectGrabbing);
            writer.Write((int)ProjectileGrabbingMode);

            writer.Write(SearchRangeMultiplier);
            writer.Write(BreakoutTimeMultiplier);
            writer.Write(ForwardVelocityMultiplier);
            writer.Write(UpwardVelocityMultiplier);
            writer.Write(MaxSmacks);
            writer.Write(MassMultiplier);

            writer.Write(BodyBlacklist);
            writer.Write(RecoveryObjectBlacklist);
            writer.Write(GrabbableComponentTypes);
            writer.Write(GrabbableKeywordBlacklist);

            writer.Write(EnableObjectPersistence);
            writer.Write(EnableAutoGrab);
            writer.Write(PersistBaggedBosses);
            writer.Write(PersistBaggedNPCs);
            writer.Write(PersistBaggedEnvironmentObjects);
            writer.Write(AutoGrabDelay);

            writer.Write(BottomlessBagEnabled);
            writer.Write(BottomlessBagBaseCapacity);

            // Balance
            writer.Write(EnableBalance);
            writer.Write(EnableAoESlamDamage);
            writer.Write(EliteMassBonusEnabled);
            writer.Write(EnableOverencumbrance);
            writer.Write(UncapCapacity);
            writer.Write(ToggleMassCapacity);
            writer.Write(StateCalculationModeEnabled);
            writer.Write(UncapBagScale);
            writer.Write(UncapMass);
        }

        public override void Deserialize(NetworkReader reader)
        {
            EnableBossGrabbing = reader.ReadBoolean();
            EnableNPCGrabbing = reader.ReadBoolean();
            EnableEnvironmentGrabbing = reader.ReadBoolean();
            EnableLockedObjectGrabbing = reader.ReadBoolean();
            ProjectileGrabbingMode = (ProjectileGrabbingMode)reader.ReadInt32();

            SearchRangeMultiplier = reader.ReadSingle();
            BreakoutTimeMultiplier = reader.ReadSingle();
            ForwardVelocityMultiplier = reader.ReadSingle();
            UpwardVelocityMultiplier = reader.ReadSingle();
            MaxSmacks = reader.ReadInt32();
            MassMultiplier = reader.ReadString();

            BodyBlacklist = reader.ReadString();
            RecoveryObjectBlacklist = reader.ReadString();
            GrabbableComponentTypes = reader.ReadString();
            GrabbableKeywordBlacklist = reader.ReadString();

            EnableObjectPersistence = reader.ReadBoolean();
            EnableAutoGrab = reader.ReadBoolean();
            PersistBaggedBosses = reader.ReadBoolean();
            PersistBaggedNPCs = reader.ReadBoolean();
            PersistBaggedEnvironmentObjects = reader.ReadBoolean();
            AutoGrabDelay = reader.ReadSingle();

            BottomlessBagEnabled = reader.ReadBoolean();
            BottomlessBagBaseCapacity = reader.ReadInt32();

            // Balance
            EnableBalance = reader.ReadBoolean();
            EnableAoESlamDamage = reader.ReadBoolean();
            EliteMassBonusEnabled = reader.ReadBoolean();
            EnableOverencumbrance = reader.ReadBoolean();
            UncapCapacity = reader.ReadBoolean();
            ToggleMassCapacity = reader.ReadBoolean();
            StateCalculationModeEnabled = reader.ReadBoolean();
            UncapBagScale = reader.ReadBoolean();
            UncapMass = reader.ReadBoolean();
        }
    }
}
