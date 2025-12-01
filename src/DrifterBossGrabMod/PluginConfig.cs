using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace DrifterBossGrabMod
{
// Configuration management for the DrifterBossGrabMod
// Handles all user-configurable settings and real-time updates
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
        public static ConfigEntry<int> MaxSmacks { get; private set; }
        public static ConfigEntry<string> MassMultiplier { get; private set; }
        public static ConfigEntry<bool> EnableDebugLogs { get; private set; }
        public static ConfigEntry<string> BodyBlacklist { get; private set; }
        public static ConfigEntry<bool> EnableEnvironmentInvisibility { get; private set; }
        public static ConfigEntry<bool> EnableEnvironmentInteractionDisable { get; private set; }
        public static ConfigEntry<bool> EnableUprightRecovery { get; private set; }
        public static ConfigEntry<string> RecoveryObjectBlacklist { get; private set; }

        // Internal cache for blacklist to avoid parsing every time
        internal static HashSet<string>? _blacklistCache;
        internal static HashSet<string>? _blacklistCacheWithClones;
        private static string? _lastBlacklistValue;

        // Recovery blacklist cache
        internal static HashSet<string>? _recoveryBlacklistCache;
        internal static HashSet<string>? _recoveryBlacklistCacheWithClones;
        private static string? _lastRecoveryBlacklistValue;

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

            // Debug and blacklist
            EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Enable debug logging");
            BodyBlacklist = cfg.Bind("General", "BodyBlacklist", "HeaterPodBodyNoRespawn,GenericPickup",
                "Comma-separated list of body names to never grab.\n" +
                "Example: SolusWingBody,Teleporter1,ShrineHalcyonite,PortalShop\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see body names, case-insensitive matching");

            // Environment object settings
            EnableEnvironmentInvisibility = cfg.Bind("General", "EnableEnvironmentInvisibility", true, "Make grabbed environment objects invisible while in the bag");
            EnableEnvironmentInteractionDisable = cfg.Bind("General", "EnableEnvironmentInteractionDisable", true, "Disable interactions for grabbed environment objects while in the bag");
            EnableUprightRecovery = cfg.Bind("General", "EnableUprightRecovery", false, "Reset rotation of recovered thrown objects to upright position");

            // Abyss recovery settings
            RecoveryObjectBlacklist = cfg.Bind("Recovery", "RecoveryObjectBlacklist", "",
                "Comma-separated list of object names to never recover from abyss falls.\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
        }

        // Sets up event handlers for real-time configuration updates
        // debugLogsHandler: Handler for debug log settings changes
        // envInvisHandler: Handler for environment invisibility changes
        // envInteractHandler: Handler for environment interaction disable changes
        // blacklistHandler: Handler for blacklist changes
        // forwardVelHandler: Handler for forward velocity multiplier changes
        // upwardVelHandler: Handler for upward velocity multiplier changes
        // recoveryBlacklistHandler: Handler for recovery blacklist changes
        public static void SetupEventHandlers(
            EventHandler debugLogsHandler,
            EventHandler envInvisHandler,
            EventHandler envInteractHandler,
            EventHandler blacklistHandler,
            EventHandler forwardVelHandler,
            EventHandler upwardVelHandler,
            EventHandler recoveryBlacklistHandler)
        {
            EnableDebugLogs.SettingChanged += debugLogsHandler;
            EnableEnvironmentInvisibility.SettingChanged += envInvisHandler;
            EnableEnvironmentInteractionDisable.SettingChanged += envInteractHandler;
            BodyBlacklist.SettingChanged += blacklistHandler;
            ForwardVelocityMultiplier.SettingChanged += forwardVelHandler;
            UpwardVelocityMultiplier.SettingChanged += upwardVelHandler;
            RecoveryObjectBlacklist.SettingChanged += recoveryBlacklistHandler;
        }

        // Removes all event handlers to prevent memory leaks
        // debugLogsHandler: Handler for debug log settings changes
        // envInvisHandler: Handler for environment invisibility changes
        // envInteractHandler: Handler for environment interaction disable changes
        // blacklistHandler: Handler for blacklist changes
        // forwardVelHandler: Handler for forward velocity multiplier changes
        // upwardVelHandler: Handler for upward velocity multiplier changes
        // recoveryBlacklistHandler: Handler for recovery blacklist changes
        public static void RemoveEventHandlers(
            EventHandler debugLogsHandler,
            EventHandler envInvisHandler,
            EventHandler envInteractHandler,
            EventHandler blacklistHandler,
            EventHandler forwardVelHandler,
            EventHandler upwardVelHandler,
            EventHandler recoveryBlacklistHandler)
        {
            EnableDebugLogs.SettingChanged -= debugLogsHandler;
            EnableEnvironmentInvisibility.SettingChanged -= envInvisHandler;
            EnableEnvironmentInteractionDisable.SettingChanged -= envInteractHandler;
            BodyBlacklist.SettingChanged -= blacklistHandler;
            ForwardVelocityMultiplier.SettingChanged -= forwardVelHandler;
            UpwardVelocityMultiplier.SettingChanged -= upwardVelHandler;
            RecoveryObjectBlacklist.SettingChanged -= recoveryBlacklistHandler;
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
    }
}