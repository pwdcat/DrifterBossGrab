#nullable enable
using System;
namespace DrifterBossGrabMod
{
    internal static class Constants
    {
        public const string CloneSuffix = "(Clone)";
        public const string PluginGuid = "com.pwdcat.DrifterBossGrab";
        public const string PluginName = "DrifterBossGrab";
        public const string PluginVersion = "1.8.0";

        // Timeouts prevent infinite hangs on network operations or object initialization failures
        public static class Timeouts
        {
            public const float SyncStateTimeout = 2.0f;
            public const float AutoGrabDelay = 0.5f;
            public const float OverencumbranceDebuffRemovalDelay = 1.5f;
            public const int MaxWaitFramesForPlayerBody = 120;
            public const float SyncWaitIncrement = 0.1f;
        }

        // Safety limits prevent integer overflow and performance issues with extreme values
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

        // Multiplier values for scaling calculations
        public static class Multipliers
        {
            public const float DefaultMassMultiplier = 1.0f;
            public const float DefaultVelocityMultiplier = 1.0f;
            public const float ExponentialScalingBase = 0.5f;
            public const float WalkSpeedPenaltyMax = 0.5f;
            public const float PercentageDivisor = 100.0f;
            public const float CapacityRatioThreshold = 1f;
            public const float ScalingMultiplierBase = 1f;

            // Base damage coefficient for suffocate slam. Multiplied by mass ratio for scaling.
            public const float SlamBaseDamageCoef = 2.8f;
            public const float SlamMassScaling = 5.0f;

            // Per-item damage bonuses applied during slam calculation. Used when target lacks CharacterBody.
            public const float DelicateWatchDamageBonus = 0.2f;
            public const float NearbyDamageBonus = 0.2f;
        }

        // Network message IDs for client-server sync. Must match between client and server.
        public static class Network
        {
            public const short BaggedObjectsPersistenceMessageType = 201;
            public const short CycleRequestMessageType = 205;
            public const short UpdateBagStateMessageType = 206;
            public const short ClientUpdateBagStateMessageType = 207;
            public const short GrabObjectMessageType = 208;
            public const short ClientPreferencesMessageType = 209;
            public const short SyncConfigMessageType = 210;
            public const short BagStateUpdatedMessageType = 211;
        }

        // Helper methods for parsing config values
        public static int ParseCapacityString(string? value, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            string upperValue = value.Trim().ToUpper();
            if (upperValue == "INF" || upperValue == "INFINITY")
                return int.MaxValue;

            if (int.TryParse(value, out int parsedValue))
                return parsedValue;

            return defaultValue;
        }

    }
}
