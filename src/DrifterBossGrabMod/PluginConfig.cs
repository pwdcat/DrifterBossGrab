using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace DrifterBossGrabMod
{
    /// Configuration management for the DrifterBossGrabMod
    /// Handles all user-configurable settings and real-time updates
    public static class PluginConfig
    {
        // Configuration entries
        public static ConfigEntry<float> SearchRangeMultiplier { get; private set; }
        public static ConfigEntry<float> BreakoutTimeMultiplier { get; private set; }
        public static ConfigEntry<float> ForwardVelocityMultiplier { get; private set; }
        public static ConfigEntry<float> UpwardVelocityMultiplier { get; private set; }
        public static ConfigEntry<bool> EnableBossGrabbing { get; private set; }
        public static ConfigEntry<bool> EnableNPCGrabbing { get; private set; }
        public static ConfigEntry<bool> EnableEnvironmentGrabbing { get; private set; }
        public static ConfigEntry<bool> EnableLockedObjectGrabbing { get; private set; }
        public static ConfigEntry<int> MaxSmacks { get; private set; }
        public static ConfigEntry<string> MassMultiplier { get; private set; }
        public static ConfigEntry<bool> EnableDebugLogs { get; private set; }
        public static ConfigEntry<string> BodyBlacklist { get; private set; }
        public static ConfigEntry<string> RecoveryObjectBlacklist { get; private set; }
        public static ConfigEntry<string> GrabbableComponentTypes { get; private set; } = null!;
        public static ConfigEntry<string> GrabbableKeywordBlacklist { get; private set; }
        public static ConfigEntry<bool> EnableComponentAnalysisLogs { get; private set; }

        // Persistence settings
        public static ConfigEntry<bool> EnableObjectPersistence { get; private set; }
        public static ConfigEntry<bool> EnableAutoGrab { get; private set; }
        public static ConfigEntry<int> MaxPersistedObjects { get; private set; }
        public static ConfigEntry<bool> PersistBaggedBosses { get; private set; }
        public static ConfigEntry<bool> PersistBaggedNPCs { get; private set; }
        public static ConfigEntry<bool> PersistBaggedEnvironmentObjects { get; private set; }
        public static ConfigEntry<string> PersistenceBlacklist { get; private set; }
        public static ConfigEntry<bool> OnlyPersistCurrentlyBagged { get; private set; }

        // Internal cache for blacklist to avoid parsing every time
        internal static HashSet<string>? _blacklistCache;
        internal static HashSet<string>? _blacklistCacheWithClones;
        private static string? _lastBlacklistValue;

        // Recovery blacklist cache
        internal static HashSet<string>? _recoveryBlacklistCache;
        internal static HashSet<string>? _recoveryBlacklistCacheWithClones;
        private static string? _lastRecoveryBlacklistValue;

        // Grabbable component types cache
        internal static HashSet<string>? _grabbableComponentTypesCache;
        private static string? _lastGrabbableComponentTypesValue;

        // Grabbable keyword blacklist cache
        internal static HashSet<string>? _grabbableKeywordBlacklistCache;
        private static string? _lastGrabbableKeywordBlacklistValue;

        // Checks if a body name is blacklisted from being grabbed
        // name: The body name to check
        // Returns: True if the body is blacklisted
        public static bool IsBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string currentValue = BodyBlacklist.Value;
            if (_blacklistCache == null || _lastBlacklistValue != currentValue)
            {
                _lastBlacklistValue = currentValue;
                _blacklistCache = string.IsNullOrEmpty(currentValue)
                    ? new HashSet<string>()
                    : currentValue.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _blacklistCacheWithClones = new HashSet<string>(_blacklistCache, StringComparer.OrdinalIgnoreCase);
                foreach (var item in _blacklistCache)
                {
                    _blacklistCacheWithClones.Add(item + Constants.CloneSuffix);
                }
            }
            return _blacklistCacheWithClones.Contains(name);
        }

        // Checks if an object name is blacklisted from abyss recovery
        // name: The object name to check
        // Returns: True if the object is blacklisted from recovery
        public static bool IsRecoveryBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string currentValue = RecoveryObjectBlacklist.Value;
            if (_recoveryBlacklistCache == null || _lastRecoveryBlacklistValue != currentValue)
            {
                _lastRecoveryBlacklistValue = currentValue;
                _recoveryBlacklistCache = string.IsNullOrEmpty(currentValue)
                    ? new HashSet<string>()
                    : currentValue.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _recoveryBlacklistCacheWithClones = new HashSet<string>(_recoveryBlacklistCache, StringComparer.OrdinalIgnoreCase);
                foreach (var item in _recoveryBlacklistCache)
                {
                    _recoveryBlacklistCacheWithClones.Add(item + Constants.CloneSuffix);
                }
            }
            return _recoveryBlacklistCacheWithClones.Contains(name);
        }

        // Checks if an object name contains blacklisted keywords
        // name: The object name to check
        // Returns: True if the object name contains any blacklisted keywords
        public static bool IsKeywordBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string currentValue = GrabbableKeywordBlacklist.Value;
            if (_grabbableKeywordBlacklistCache == null || _lastGrabbableKeywordBlacklistValue != currentValue)
            {
                _lastGrabbableKeywordBlacklistValue = currentValue;
                _grabbableKeywordBlacklistCache = string.IsNullOrEmpty(currentValue)
                    ? new HashSet<string>()
                    : currentValue.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var keyword in _grabbableKeywordBlacklistCache)
            {
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Checks if an object is grabbable based on its components and configuration toggles
        // obj: The GameObject to check
        // Returns: True if the object has any of the grabbable component types and is allowed by toggles
        public static bool IsGrabbable(GameObject? obj)
        {
            if (obj == null) return false;

            // Check for keyword blacklist first
            if (IsKeywordBlacklisted(obj.name))
            {
                return false;
            }

            // Check for body blacklist
            if (IsBlacklisted(obj.name))
            {
                return false;
            }

            // Check if object has required components
            string currentValue = GrabbableComponentTypes.Value;
            if (_grabbableComponentTypesCache == null || _lastGrabbableComponentTypesValue != currentValue)
            {
                _lastGrabbableComponentTypesValue = currentValue;
                _grabbableComponentTypesCache = string.IsNullOrEmpty(currentValue)
                    ? new HashSet<string>()
                    : currentValue.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.Ordinal);
            }

            bool hasRequiredComponent = false;
            foreach (var componentType in _grabbableComponentTypesCache)
            {
                var component = obj.GetComponent(componentType);
                if (component != null)
                {
                    hasRequiredComponent = true;
                    break;
                }
            }

            if (!hasRequiredComponent)
            {
                return false;
            }

            // Now check the appropriate toggle based on object type
            var characterBody = obj.GetComponent<RoR2.CharacterBody>();
            if (characterBody != null)
            {
                // It's an enemy/NPC
                if (characterBody.isBoss || characterBody.isChampion)
                {
                    return EnableBossGrabbing.Value;
                }
                else
                {
                    return EnableNPCGrabbing.Value;
                }
            }
            else
            {
                // It's an environment object (has IInteractable or other components but no CharacterBody)
                return EnableEnvironmentGrabbing.Value;
            }
        }

        // Initializes all configuration entries
        // cfg: The BepInEx configuration file
        public static void Init(ConfigFile cfg)
        {
            // Repossess settings
            SearchRangeMultiplier = cfg.Bind("Repossess", "SearchRangeMultiplier", 1.0f, "Multiplier for Drifter's repossess search range");
            ForwardVelocityMultiplier = cfg.Bind("Repossess", "ForwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess forward velocity");
            UpwardVelocityMultiplier = cfg.Bind("Repossess", "UpwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess upward velocity");

            // Bag settings
            BreakoutTimeMultiplier = cfg.Bind("Bag", "BreakoutTimeMultiplier", 1.0f, "Multiplier for how long bagged enemies take to break out");
            MaxSmacks = cfg.Bind("Bag", "MaxSmacks", 3, new ConfigDescription("Maximum number of hits before bagged enemies break out", new AcceptableValueRange<int>(1, 100)));
            MassMultiplier = cfg.Bind("Bag", "MassMultiplier", "1", "Multiplier for the mass of bagged objects");

            // General grabbing settings
            EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Enable grabbing of boss enemies");
            EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Enable grabbing of NPCs with ungrabbable flag");
            EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Enable grabbing of environment objects like teleporters, chests, shrines");
            EnableLockedObjectGrabbing = cfg.Bind("General", "EnableLockedObjectGrabbing", true, "Enable grabbing of locked objects");

            // Debug and blacklist
            EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Enable debug logging");
            BodyBlacklist = cfg.Bind("General", "BodyBlacklist", "HeaterPodBodyNoRespawn,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal",
                "Comma-separated list of body names to never grab.\n" +
                "Example: SolusWingBody,Teleporter1,ShrineHalcyonite,PortalShop\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see body names, case-insensitive matching");


            // Abyss recovery settings
            RecoveryObjectBlacklist = cfg.Bind("General", "RecoveryObjectBlacklist", "",
                "Comma-separated list of object names to never recover from abyss falls.\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");

            // Grabbable component types
            GrabbableComponentTypes = cfg.Bind("General", "GrabbableComponentTypes", "PurchaseInteraction,TeleporterInteraction,GenericInteraction",
                "Comma-separated list of component type names that make objects grabbable.\n" +
                "Example: SurfaceDefProvider,EntityStateMachine,JumpVolume\n" +
                "Objects must have at least one of these components to be grabbable.\n" +
                "Use exact component type names (case-sensitive).");

            // Grabbable keyword blacklist
            GrabbableKeywordBlacklist = cfg.Bind("General", "GrabbableKeywordBlacklist", "Master,Controller",
                "Comma-separated list of keywords that make objects NOT grabbable if found in their name.\n" +
                "Example: Master\n" +
                "Objects with these keywords in their name will be excluded from grabbing.\n" +
                "Case-insensitive matching, partial matches allowed.\n" +
                "'Master' prevents grabbing enemy masters");

            // Component analysis debug logs
            EnableComponentAnalysisLogs = cfg.Bind("General", "EnableComponentAnalysisLogs", false,
                "Enable scanning of all objects in the current scene to log component types.\n" +
                "This can be performance-intensive and should only be enabled for debugging.\n" +
                "Shows all unique component types found in the scene for potential grabbable objects.");

            // Persistence settings
            EnableObjectPersistence = cfg.Bind("Persistence", "EnableObjectPersistence", false, "Enable persistence of grabbed objects across stage transitions");
            EnableAutoGrab = cfg.Bind("Persistence", "EnableAutoGrab", false, "Automatically re-grab persisted objects on Drifter respawn");
            MaxPersistedObjects = cfg.Bind("Persistence", "MaxPersistedObjects", 10, new ConfigDescription("Maximum number of objects that can be persisted at once", new AcceptableValueRange<int>(1, 50)));
            PersistBaggedBosses = cfg.Bind("Persistence", "PersistBaggedBosses", true, "Allow persistence of bagged boss enemies");
            PersistBaggedNPCs = cfg.Bind("Persistence", "PersistBaggedNPCs", true, "Allow persistence of bagged NPCs");
            PersistBaggedEnvironmentObjects = cfg.Bind("Persistence", "PersistBaggedEnvironmentObjects", true, "Allow persistence of bagged environment objects");
            PersistenceBlacklist = cfg.Bind("Persistence", "PersistenceBlacklist", "",
                "Comma-separated list of object names to never persist.\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
            OnlyPersistCurrentlyBagged = cfg.Bind("Persistence", "OnlyPersistCurrentlyBagged", true, "Only persist objects that are currently in the bag (excludes thrown objects)");
        }


        // Removes all event handlers to prevent memory leaks
        // debugLogsHandler: Handler for debug log settings changes
        // blacklistHandler: Handler for blacklist changes
        // forwardVelHandler: Handler for forward velocity multiplier changes
        // upwardVelHandler: Handler for upward velocity multiplier changes
        // recoveryBlacklistHandler: Handler for recovery blacklist changes
        // grabbableComponentTypesHandler: Handler for grabbable component types changes
        // grabbableKeywordBlacklistHandler: Handler for grabbable keyword blacklist changes
        // bossGrabbingHandler: Handler for boss grabbing toggle changes
        // npcGrabbingHandler: Handler for NPC grabbing toggle changes
        // environmentGrabbingHandler: Handler for environment grabbing toggle changes
        // lockedObjectGrabbingHandler: Handler for locked object grabbing toggle changes
        public static void RemoveEventHandlers(
            EventHandler debugLogsHandler,
            EventHandler blacklistHandler,
            EventHandler forwardVelHandler,
            EventHandler upwardVelHandler,
            EventHandler recoveryBlacklistHandler,
            EventHandler grabbableComponentTypesHandler,
            EventHandler grabbableKeywordBlacklistHandler,
            EventHandler bossGrabbingHandler,
            EventHandler npcGrabbingHandler,
            EventHandler environmentGrabbingHandler,
            EventHandler lockedObjectGrabbingHandler)
        {
            EnableDebugLogs.SettingChanged -= debugLogsHandler;
            BodyBlacklist.SettingChanged -= blacklistHandler;
            ForwardVelocityMultiplier.SettingChanged -= forwardVelHandler;
            UpwardVelocityMultiplier.SettingChanged -= upwardVelHandler;
            RecoveryObjectBlacklist.SettingChanged -= recoveryBlacklistHandler;
            GrabbableComponentTypes.SettingChanged -= grabbableComponentTypesHandler;
            GrabbableKeywordBlacklist.SettingChanged -= grabbableKeywordBlacklistHandler;
            EnableBossGrabbing.SettingChanged -= bossGrabbingHandler;
            EnableNPCGrabbing.SettingChanged -= npcGrabbingHandler;
            EnableEnvironmentGrabbing.SettingChanged -= environmentGrabbingHandler;
            EnableLockedObjectGrabbing.SettingChanged -= lockedObjectGrabbingHandler;
        }

        // Clears the blacklist cache to force a rebuild
        public static void ClearBlacklistCache()
        {
            _blacklistCache = null;
            _blacklistCacheWithClones = null;
        }

        // Clears the recovery blacklist cache to force a rebuild
        public static void ClearRecoveryBlacklistCache()
        {
            _recoveryBlacklistCache = null;
            _recoveryBlacklistCacheWithClones = null;
        }

        // Clears the grabbable component types cache to force a rebuild
        public static void ClearGrabbableComponentTypesCache()
        {
            _grabbableComponentTypesCache = null;
            _lastGrabbableComponentTypesValue = null;
        }

        // Clears the grabbable keyword blacklist cache to force a rebuild
        public static void ClearGrabbableKeywordBlacklistCache()
        {
            _grabbableKeywordBlacklistCache = null;
            _lastGrabbableKeywordBlacklistValue = null;
        }
    }
}