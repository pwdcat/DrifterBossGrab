#nullable enable
using System;
namespace DrifterBossGrabMod
{
    internal static class Constants
    {
        public const string CloneSuffix = "(Clone)";
        public const string PluginGuid = "com.pwdcat.DrifterBossGrab";
        public const string PluginName = "DrifterBossGrab";
        public const string PluginVersion = "1.8.1";

        public static class Timeouts
        {
            public const float SyncStateTimeout = 2.0f;
            public const float AutoGrabDelay = 0.5f;
            public const float OverencumbranceDebuffRemovalDelay = 1.5f;
            public const int MaxWaitFramesForPlayerBody = 120;
            public const float SyncWaitIncrement = 0.1f;
        }

        public static class Limits
        {
            public const float MaxMass = 700f;
            public const int MaxCapacity = 100;
            public const float MinimumMassPercentage = 0.1f;
            public const float MinimumMass = 1f;
            public const float PositionOffset = 0.5f;
            public const float CameraForwardOffset = 2f;
            public const float OriginYOffset = 1f;
            public const int SingleCapacity = 1;
            public const int DefaultJunkQuantity = 4;
            public const int MinDurabilityThreshold = 1;
            public const float DefaultMassPerStock = 700f;
        }

        public static class Multipliers
        {
            public const float DefaultMassMultiplier = 1.0f;
            public const float DefaultVelocityMultiplier = 1.0f;
            public const float ExponentialScalingBase = 0.5f;
            public const float WalkSpeedPenaltyMax = 0.5f;
            public const float PercentageDivisor = 100.0f;
            public const float CapacityRatioThreshold = 1f;
            public const float ScalingMultiplierBase = 1f;

            public const float SlamBaseDamageCoef = 2.8f;
            public const float SlamMassScaling = 5.0f;

            public const float DelicateWatchDamageBonus = 0.2f;
            public const float NearbyDamageBonus = 0.2f;
        }

        public static class Network
        {
            public const short MultiplexerMessageType = 16259;
            public const uint MSG_SIGNATURE = 0x444247; // DBG

            // Sub-message types
            public const byte BaggedObjectsPersistenceSubMessageType = 1;
            public const byte UpdateBagStateSubMessageType = 2;
            public const byte CycleRequestSubMessageType = 3;
            public const byte ClientUpdateBagStateSubMessageType = 4;
            public const byte GrabObjectSubMessageType = 5;
            public const byte SyncConfigSubMessageType = 6;
            public const byte ClientPreferencesSubMessageType = 7;
            public const byte BagStateUpdatedSubMessageType = 8;
            public const byte RequestConfigSubMessageType = 9;
        }

    }
}
