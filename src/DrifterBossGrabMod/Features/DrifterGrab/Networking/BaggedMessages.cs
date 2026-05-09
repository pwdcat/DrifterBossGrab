#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.Networking
{
    // ========================================================================================
    // PERSISTENCE MESSAGES
    // ========================================================================================

    public class BaggedObjectsPersistenceMessage : MessageBase
    {
        public List<NetworkInstanceId> baggedObjectNetIds = new List<NetworkInstanceId>();
        public List<string> ownerPlayerIds = new List<string>();
        public List<bool> collidersDisabled = new List<bool>();

        public override void Serialize(NetworkWriter writer)
        {
            int count = Math.Min(baggedObjectNetIds.Count, 1000);
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(baggedObjectNetIds[i]);
            }

            count = Math.Min(ownerPlayerIds.Count, 1000);
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(ownerPlayerIds[i] ?? string.Empty);
            }

            count = Math.Min(collidersDisabled.Count, 1000);
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(collidersDisabled[i]);
            }

        }

        public override void Deserialize(NetworkReader reader)
        {
            int count = Math.Min(reader.ReadInt32(), 1000);
            baggedObjectNetIds.Clear();
            for (int i = 0; i < count; i++)
            {
                baggedObjectNetIds.Add(reader.ReadNetworkId());
            }

            count = Math.Min(reader.ReadInt32(), 1000);
            ownerPlayerIds.Clear();
            for (int i = 0; i < count; i++)
            {
                ownerPlayerIds.Add(reader.ReadString());
            }

            count = Math.Min(reader.ReadInt32(), 1000);
            collidersDisabled.Clear();
            for (int i = 0; i < count; i++)
            {
                collidersDisabled.Add(reader.ReadBoolean());
            }

        }
    }

    // ========================================================================================
    // BAG STATE MESSAGES
    // ========================================================================================

    public class UpdateBagStateMessage : MessageBase
    {
        public NetworkInstanceId controllerNetId;
        public int selectedIndex;
        public uint[] baggedIds = System.Array.Empty<uint>();
        public uint[] seatIds = System.Array.Empty<uint>();
        public int scrollDirection;
        public bool[] collidersDisabled = System.Array.Empty<bool>();
        public float[] breakoutTimes = System.Array.Empty<float>();
        public float[] elapsedBreakoutTimes = System.Array.Empty<float>();

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(controllerNetId);
            writer.Write(selectedIndex);
            writer.Write(scrollDirection);

            int baggedCount = Math.Min(baggedIds.Length, 500);
            writer.Write(baggedCount);
            for (int i = 0; i < baggedCount; i++) writer.Write(baggedIds[i]);

            int seatCount = Math.Min(seatIds.Length, 500);
            writer.Write(seatCount);
            for (int i = 0; i < seatCount; i++) writer.Write(seatIds[i]);

            int colliderCount = Math.Min(collidersDisabled.Length, 500);
            writer.Write(colliderCount);
            for (int i = 0; i < colliderCount; i++) writer.Write(collidersDisabled[i]);

            int timerCount = Math.Min(breakoutTimes.Length, 500);
            writer.Write(timerCount);
            for (int i = 0; i < timerCount; i++) writer.Write(breakoutTimes[i]);

            int elapsedCount = Math.Min(elapsedBreakoutTimes.Length, 500);
            writer.Write(elapsedCount);
            for (int i = 0; i < elapsedCount; i++) writer.Write(elapsedBreakoutTimes[i]);
        }

        public override void Deserialize(NetworkReader reader)
        {
            controllerNetId = reader.ReadNetworkId();
            selectedIndex = reader.ReadInt32();
            scrollDirection = reader.ReadInt32();

            int count = Math.Min(reader.ReadInt32(), 500);
            baggedIds = new uint[count];
            for (int i = 0; i < count; i++) baggedIds[i] = reader.ReadUInt32();

            int count2 = Math.Min(reader.ReadInt32(), 500);
            seatIds = new uint[count2];
            for (int i = 0; i < count2; i++) seatIds[i] = reader.ReadUInt32();

            int count3 = Math.Min(reader.ReadInt32(), 500);
            collidersDisabled = new bool[count3];
            for (int i = 0; i < count3; i++) collidersDisabled[i] = reader.ReadBoolean();

            int count4 = Math.Min(reader.ReadInt32(), 500);
            breakoutTimes = new float[count4];
            for (int i = 0; i < count4; i++) breakoutTimes[i] = reader.ReadSingle();

            int count5 = Math.Min(reader.ReadInt32(), 500);
            elapsedBreakoutTimes = new float[count5];
            for (int i = 0; i < count5; i++) elapsedBreakoutTimes[i] = reader.ReadSingle();
        }
    }
    // ========================================================================================
    // ACTION MESSAGES
    // ========================================================================================

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

            int baggedCount = Math.Min(baggedIds.Length, 500);
            writer.Write(baggedCount);
            for (int i = 0; i < baggedCount; i++) writer.Write(baggedIds[i]);

            int seatCount = Math.Min(seatIds.Length, 500);
            writer.Write(seatCount);
            for (int i = 0; i < seatCount; i++) writer.Write(seatIds[i]);
        }

        public override void Deserialize(NetworkReader reader)
        {
            controllerNetId = reader.ReadNetworkId();
            selectedIndex = reader.ReadInt32();

            int count = Math.Min(reader.ReadInt32(), 500);
            baggedIds = new uint[count];
            for (int i = 0; i < count; i++) baggedIds[i] = reader.ReadUInt32();

            int count2 = Math.Min(reader.ReadInt32(), 500);
            seatIds = new uint[count2];
            for (int i = 0; i < count2; i++) seatIds[i] = reader.ReadUInt32();
        }
    }

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

    // ========================================================================================
    // SYNC MESSAGES
    // ========================================================================================

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
        public bool EnableStockRefreshClamping;
        public bool EnableSuccessiveGrabStockRefresh;
        public float CycleCooldown;

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
            writer.Write(EnableStockRefreshClamping);
            writer.Write(EnableSuccessiveGrabStockRefresh);
            writer.Write(CycleCooldown);

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
            EnableStockRefreshClamping = reader.ReadBoolean();
            EnableSuccessiveGrabStockRefresh = reader.ReadBoolean();
            CycleCooldown = reader.ReadSingle();

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

    // ========================================================================================
    // CLIENT PREFERENCE MESSAGES
    // ========================================================================================

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

    // ========================================================================================
    // NOTIFICATION MESSAGES
    // ========================================================================================

    public class BagStateUpdatedMessage : MessageBase
    {
        public NetworkInstanceId controllerNetId;
        public int selectedIndex;
        public NetworkInstanceId removedObjectNetId;
        public uint[] baggedIds = System.Array.Empty<uint>();
        public uint[] seatIds = System.Array.Empty<uint>();
        public int scrollDirection;
        public bool isThrowOperation;
        public bool[] collidersDisabled = System.Array.Empty<bool>();
        public float[] breakoutTimes = System.Array.Empty<float>();
        public float[] elapsedBreakoutTimes = System.Array.Empty<float>();

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(controllerNetId);
            writer.Write(selectedIndex);
            writer.Write(removedObjectNetId);
            writer.Write(scrollDirection);
            writer.Write(isThrowOperation);

            int baggedCount = Math.Min(baggedIds.Length, 500);
            writer.Write(baggedCount);
            for (int i = 0; i < baggedCount; i++) writer.Write(baggedIds[i]);

            int seatCount = Math.Min(seatIds.Length, 500);
            writer.Write(seatCount);
            for (int i = 0; i < seatCount; i++) writer.Write(seatIds[i]);

            int colliderCount = Math.Min(collidersDisabled.Length, 500);
            writer.Write(colliderCount);
            for (int i = 0; i < colliderCount; i++) writer.Write(collidersDisabled[i]);

            int timerCount = Math.Min(breakoutTimes.Length, 500);
            writer.Write(timerCount);
            for (int i = 0; i < timerCount; i++) writer.Write(breakoutTimes[i]);

            int elapsedCount = Math.Min(elapsedBreakoutTimes.Length, 500);
            writer.Write(elapsedCount);
            for (int i = 0; i < elapsedCount; i++) writer.Write(elapsedBreakoutTimes[i]);
        }

        public override void Deserialize(NetworkReader reader)
        {
            controllerNetId = reader.ReadNetworkId();
            selectedIndex = reader.ReadInt32();
            removedObjectNetId = reader.ReadNetworkId();
            scrollDirection = reader.ReadInt32();
            isThrowOperation = reader.ReadBoolean();

            int count = Math.Min(reader.ReadInt32(), 500);
            baggedIds = new uint[count];
            for (int i = 0; i < count; i++) baggedIds[i] = reader.ReadUInt32();

            int count2 = Math.Min(reader.ReadInt32(), 500);
            seatIds = new uint[count2];
            for (int i = 0; i < count2; i++) seatIds[i] = reader.ReadUInt32();

            int count3 = Math.Min(reader.ReadInt32(), 500);
            collidersDisabled = new bool[count3];
            for (int i = 0; i < count3; i++) collidersDisabled[i] = reader.ReadBoolean();

            int count4 = Math.Min(reader.ReadInt32(), 500);
            breakoutTimes = new float[count4];
            for (int i = 0; i < count4; i++) breakoutTimes[i] = reader.ReadSingle();

            int count5 = Math.Min(reader.ReadInt32(), 500);
            elapsedBreakoutTimes = new float[count5];
            for (int i = 0; i < count5; i++) elapsedBreakoutTimes[i] = reader.ReadSingle();
        }
    }
}
