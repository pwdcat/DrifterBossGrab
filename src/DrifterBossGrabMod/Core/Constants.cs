using System;
namespace DrifterBossGrabMod
{
    // Shared constants used throughout the DrifterBossGrabMod
    internal static class Constants
    {
        public const string CloneSuffix = "(Clone)";
        // Version info
        public const string PluginGuid = "pwdcat.DrifterBossGrab";
        public const string PluginName = "DrifterBossGrab";
        public const string PluginVersion = "1.7.0";

        // Timeout values for various operations
        public static class Timeouts
        {
            public const float SyncStateTimeout = 2.0f;
            public const float AutoGrabDelay = 0.5f;
            public const float OverencumbranceDebuffRemovalDelay = 1.5f;
            public const int MaxWaitFramesForPlayerBody = 120;
            public const float SyncWaitIncrement = 0.1f;
        }

        // Limit values for capacity and mass
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
        }

        // Network message types
        public static class Network
        {
            public const short UpdateBagStateMessageType = 206;
            public const short GrabObjectMessageType = 208;
        }
    }
}
