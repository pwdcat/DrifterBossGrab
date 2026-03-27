#nullable enable
using System.Collections.Generic;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.Networking
{
    // Network message for broadcasting bagged objects for persistence
    public class BaggedObjectsPersistenceMessage : MessageBase
    {
        public List<NetworkInstanceId> baggedObjectNetIds = new List<NetworkInstanceId>();
        public List<string> ownerPlayerIds = new List<string>();
        public List<bool> collidersDisabled = new List<bool>();

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
            writer.Write((int)collidersDisabled.Count);
            foreach (var disabled in collidersDisabled)
            {
                writer.Write(disabled);
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
            count = reader.ReadInt32();
            collidersDisabled.Clear();
            for (int i = 0; i < count; i++)
            {
                collidersDisabled.Add(reader.ReadBoolean());
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
        public bool[] collidersDisabled = System.Array.Empty<bool>();

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(controllerNetId);
            writer.Write(selectedIndex);
            writer.Write(scrollDirection);

            writer.Write(baggedIds.Length);
            foreach (var id in baggedIds) writer.Write(id);

            writer.Write(seatIds.Length);
            foreach (var id in seatIds) writer.Write(id);

            writer.Write(collidersDisabled.Length);
            foreach (var disabled in collidersDisabled) writer.Write(disabled);
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

            int count3 = reader.ReadInt32();
            collidersDisabled = new bool[count3];
            for (int i = 0; i < count3; i++) collidersDisabled[i] = reader.ReadBoolean();
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
        public float SearchRadiusMultiplier;
        public ComponentChooserSortMode ComponentChooserSortMode;

        // Skill Scalars
        public float BreakoutTimeMultiplier;
        public int MaxSmacks;
        public string MaxLaunchSpeed = "100";

        // Blacklists & Component Types
        public string BodyBlacklist = string.Empty;
        public string RecoveryObjectBlacklist = string.Empty;
        public string GrabbableComponentTypes = string.Empty;
        public string GrabbableKeywordBlacklist = string.Empty;

        // Persistence
        public bool EnableObjectPersistence;
        public bool EnableAutoGrab;
        public bool PersistBaggedBosses;
        public bool PersistBaggedNPCs;
        public bool PersistBaggedEnvironmentObjects;
        public string PersistenceBlacklist = string.Empty;
        public float AutoGrabDelay;

        // Bottomless Bag
        public bool BottomlessBagEnabled;
        public string AddedCapacity = "0";
        public bool EnableStockRefreshClamping;
        public bool EnableSuccessiveGrabStockRefresh;
        public float CycleCooldown;
        // Balance

        // Balance
        public bool EnableBalance;
        public AoEDamageMode AoEDamageDistribution;
        public string BagScaleCap = "1";
        public string MassCap = "700";
        public StateCalculationMode StateCalculationMode;
        public float OverencumbranceMax;
        public string SlotScalingFormula = string.Empty;
        public string MassCapacityFormula = string.Empty;
        public string MovespeedPenaltyFormula = string.Empty;

        // Balance - Flag Multipliers
        public string EliteFlagMultiplier = "1.0";
        public string BossFlagMultiplier = "1.0";
        public string ChampionFlagMultiplier = "1.0";
        public string PlayerFlagMultiplier = "1.0";
        public string MinionFlagMultiplier = "1.0";
        public string DroneFlagMultiplier = "1.0";
        public string MechanicalFlagMultiplier = "1.0";
        public string VoidFlagMultiplier = "1.0";
        public string AllFlagMultiplier = "1.0";

        public override void Serialize(NetworkWriter writer)
        {
            // General Grabbing
            writer.Write(EnableBossGrabbing);
            writer.Write(EnableNPCGrabbing);
            writer.Write(EnableEnvironmentGrabbing);
            writer.Write(EnableLockedObjectGrabbing);
            writer.Write((int)ProjectileGrabbingMode);
            writer.Write(SearchRadiusMultiplier);
            writer.Write((int)ComponentChooserSortMode);

            // Skill Scalars
            writer.Write(BreakoutTimeMultiplier);
            writer.Write(MaxSmacks);

            // Blacklists & Component Types
            writer.Write(BodyBlacklist);
            writer.Write(RecoveryObjectBlacklist);
            writer.Write(GrabbableComponentTypes);
            writer.Write(GrabbableKeywordBlacklist);

            // Persistence
            writer.Write(EnableObjectPersistence);
            writer.Write(EnableAutoGrab);
            writer.Write(PersistBaggedBosses);
            writer.Write(PersistBaggedNPCs);
            writer.Write(PersistBaggedEnvironmentObjects);
            writer.Write(PersistenceBlacklist);
            writer.Write(AutoGrabDelay);

            // Bottomless Bag
            writer.Write(BottomlessBagEnabled);
            writer.Write(AddedCapacity);
            writer.Write(EnableStockRefreshClamping);
            writer.Write(EnableSuccessiveGrabStockRefresh);
            writer.Write(CycleCooldown);
            // Balance

            // Balance
            writer.Write(EnableBalance);
            writer.Write((int)AoEDamageDistribution);
            writer.Write(BagScaleCap);
            writer.Write(MassCap);
            writer.Write((int)StateCalculationMode);
            writer.Write(OverencumbranceMax);
            writer.Write(SlotScalingFormula);
            writer.Write(MassCapacityFormula);
            writer.Write(MovespeedPenaltyFormula);

            // Balance - Flag Multipliers
            writer.Write(EliteFlagMultiplier);
            writer.Write(BossFlagMultiplier);
            writer.Write(ChampionFlagMultiplier);
            writer.Write(PlayerFlagMultiplier);
            writer.Write(MinionFlagMultiplier);
            writer.Write(DroneFlagMultiplier);
            writer.Write(MechanicalFlagMultiplier);
            writer.Write(VoidFlagMultiplier);
            writer.Write(AllFlagMultiplier);
        }

        public override void Deserialize(NetworkReader reader)
        {
            // General Grabbing
            EnableBossGrabbing = reader.ReadBoolean();
            EnableNPCGrabbing = reader.ReadBoolean();
            EnableEnvironmentGrabbing = reader.ReadBoolean();
            EnableLockedObjectGrabbing = reader.ReadBoolean();
            ProjectileGrabbingMode = (ProjectileGrabbingMode)reader.ReadInt32();
            SearchRadiusMultiplier = reader.ReadSingle();
            ComponentChooserSortMode = (ComponentChooserSortMode)reader.ReadInt32();

            // Skill Scalars
            BreakoutTimeMultiplier = reader.ReadSingle();
            MaxSmacks = reader.ReadInt32();

            // Blacklists & Component Types
            BodyBlacklist = reader.ReadString();
            RecoveryObjectBlacklist = reader.ReadString();
            GrabbableComponentTypes = reader.ReadString();
            GrabbableKeywordBlacklist = reader.ReadString();

            // Persistence
            EnableObjectPersistence = reader.ReadBoolean();
            EnableAutoGrab = reader.ReadBoolean();
            PersistBaggedBosses = reader.ReadBoolean();
            PersistBaggedNPCs = reader.ReadBoolean();
            PersistBaggedEnvironmentObjects = reader.ReadBoolean();
            PersistenceBlacklist = reader.ReadString();
            AutoGrabDelay = reader.ReadSingle();

            // Bottomless Bag
            BottomlessBagEnabled = reader.ReadBoolean();
            AddedCapacity = reader.ReadString();
            EnableStockRefreshClamping = reader.ReadBoolean();
            EnableSuccessiveGrabStockRefresh = reader.ReadBoolean();
            CycleCooldown = reader.ReadSingle();
            // Balance

            // Balance
            EnableBalance = reader.ReadBoolean();
            AoEDamageDistribution = (AoEDamageMode)reader.ReadInt32();
            BagScaleCap = reader.ReadString();
            MassCap = reader.ReadString();
            StateCalculationMode = (StateCalculationMode)reader.ReadInt32();
            OverencumbranceMax = reader.ReadSingle();
            SlotScalingFormula = reader.ReadString();
            MassCapacityFormula = reader.ReadString();
            MovespeedPenaltyFormula = reader.ReadString();

            // Balance - Flag Multipliers
            EliteFlagMultiplier = reader.ReadString();
            BossFlagMultiplier = reader.ReadString();
            ChampionFlagMultiplier = reader.ReadString();
            PlayerFlagMultiplier = reader.ReadString();
            MinionFlagMultiplier = reader.ReadString();
            DroneFlagMultiplier = reader.ReadString();
            MechanicalFlagMultiplier = reader.ReadString();
            VoidFlagMultiplier = reader.ReadString();
            AllFlagMultiplier = reader.ReadString();
        }
    }

    // Network message for client preference sync (AutoPromote, PrioritizeMainSeat)
    public class ClientPreferencesMessage : MessageBase
    {
        public NetworkInstanceId controllerNetId;
        public bool autoPromoteMainSeat;
        public bool prioritizeMainSeat;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(controllerNetId);
            writer.Write(autoPromoteMainSeat);
            writer.Write(prioritizeMainSeat);
        }

        public override void Deserialize(NetworkReader reader)
        {
            controllerNetId = reader.ReadNetworkId();
            autoPromoteMainSeat = reader.ReadBoolean();
            prioritizeMainSeat = reader.ReadBoolean();
        }
    }
}
