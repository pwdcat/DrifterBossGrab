using System;

namespace DrifterBossGrabMod
{
// Shared constants used throughout the DrifterBossGrabMod
    internal static class Constants
    {
        public const string LogPrefix = "[DrifterBossGrab]";
        public const string RepossessSuccessSound = "Play_drifter_repossess_success";
        public const string FullBodyOverride = "FullBody, Override";
        public const string SuffocateHit = "SuffocateHit";
        public const string SuffocatePlaybackRate = "Suffocate.playbackRate";
        public const string CloneSuffix = "(Clone)";
        
        // Cache settings
        public const int MAX_CACHE_SIZE = 1000;
        
        // Version info
        public const string PluginGuid = "com.DrifterBossGrab.DrifterBossGrab";
        public const string PluginName = "DrifterBossGrab";
        public const string PluginVersion = "1.2.2";
    }
}