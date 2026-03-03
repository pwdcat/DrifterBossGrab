using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace DrifterBossGrabMod
{
    public interface IPluginConfig
    {
        ConfigEntry<float> BreakoutTimeMultiplier { get; }
        ConfigEntry<bool> EnableBossGrabbing { get; }
        ConfigEntry<bool> EnableNPCGrabbing { get; }
        ConfigEntry<bool> EnableEnvironmentGrabbing { get; }
        ConfigEntry<bool> EnableLockedObjectGrabbing { get; }
        ConfigEntry<ProjectileGrabbingMode> ProjectileGrabbingMode { get; }
        ConfigEntry<int> MaxSmacks { get; }
        ConfigEntry<bool> EnableDebugLogs { get; }
        ConfigEntry<string> BodyBlacklist { get; }
        ConfigEntry<string> RecoveryObjectBlacklist { get; }
        ConfigEntry<string> GrabbableComponentTypes { get; }
        ConfigEntry<string> GrabbableKeywordBlacklist { get; }
        ConfigEntry<bool> EnableComponentAnalysisLogs { get; }
        ConfigEntry<bool> EnableObjectPersistence { get; }
        ConfigEntry<bool> EnableAutoGrab { get; }
        ConfigEntry<bool> PersistBaggedBosses { get; }
        ConfigEntry<bool> PersistBaggedNPCs { get; }
        ConfigEntry<bool> PersistBaggedEnvironmentObjects { get; }
        ConfigEntry<string> PersistenceBlacklist { get; }
        ConfigEntry<bool> BottomlessBagEnabled { get; }
        ConfigEntry<string> AddedCapacity { get; }
        ConfigEntry<bool> EnableMouseWheelScrolling { get; }
        ConfigEntry<KeyboardShortcut> ScrollUpKeybind { get; }
        ConfigEntry<KeyboardShortcut> ScrollDownKeybind { get; }

        bool IsBlacklisted(string? name);
        bool IsRecoveryBlacklisted(string? name);
        bool IsKeywordBlacklisted(string? name);
        bool IsGrabbable(GameObject? obj);
        void Init(ConfigFile cfg);
        void RemoveEventHandlers(
            EventHandler debugLogsHandler,
            EventHandler blacklistHandler,
            EventHandler recoveryBlacklistHandler,
            EventHandler grabbableComponentTypesHandler,
            EventHandler grabbableKeywordBlacklistHandler,
            EventHandler bossGrabbingHandler,
            EventHandler npcGrabbingHandler,
            EventHandler environmentGrabbingHandler,
            EventHandler lockedObjectGrabbingHandler);
        void ClearBlacklistCache();
        void ClearRecoveryBlacklistCache();
        void ClearGrabbableComponentTypesCache();
        void ClearGrabbableKeywordBlacklistCache();
    }
}
