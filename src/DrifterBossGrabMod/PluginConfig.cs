using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
namespace DrifterBossGrabMod
{
    public static class PluginConfig
    {
        public static ConfigEntry<float> SearchRangeMultiplier { get; private set; } = null!;
        public static ConfigEntry<float> BreakoutTimeMultiplier { get; private set; } = null!;
        public static ConfigEntry<float> ForwardVelocityMultiplier { get; private set; } = null!;
        public static ConfigEntry<float> UpwardVelocityMultiplier { get; private set; } = null!;
        public static ConfigEntry<bool> EnableBossGrabbing { get; private set; } = null!;
        public static ConfigEntry<bool> EnableNPCGrabbing { get; private set; } = null!;
        public static ConfigEntry<bool> EnableEnvironmentGrabbing { get; private set; } = null!;
        public static ConfigEntry<bool> EnableLockedObjectGrabbing { get; private set; } = null!;
        public static ConfigEntry<bool> EnableProjectileGrabbing { get; private set; } = null!;
        public static ConfigEntry<bool> ProjectileGrabbingSurvivorOnly { get; private set; } = null!;
        public static ConfigEntry<int> MaxSmacks { get; private set; } = null!;
        public static ConfigEntry<string> MassMultiplier { get; private set; } = null!;
        public static ConfigEntry<bool> EnableDebugLogs { get; private set; } = null!;
        public static ConfigEntry<string> BodyBlacklist { get; private set; } = null!;
        public static ConfigEntry<string> RecoveryObjectBlacklist { get; private set; } = null!;
        public static ConfigEntry<string> GrabbableComponentTypes { get; private set; } = null!;
        public static ConfigEntry<string> GrabbableKeywordBlacklist { get; private set; } = null!;
        public static ConfigEntry<bool> EnableComponentAnalysisLogs { get; private set; } = null!;
        public static ConfigEntry<bool> EnableObjectPersistence { get; private set; } = null!;
        public static ConfigEntry<bool> EnableAutoGrab { get; private set; } = null!;
        public static ConfigEntry<bool> PersistBaggedBosses { get; private set; } = null!;
        public static ConfigEntry<bool> PersistBaggedNPCs { get; private set; } = null!;
        public static ConfigEntry<bool> PersistBaggedEnvironmentObjects { get; private set; } = null!;
        public static ConfigEntry<string> PersistenceBlacklist { get; private set; } = null!;
        public static ConfigEntry<bool> BottomlessBagEnabled { get; private set; } = null!;
        internal static HashSet<string>? _blacklistCache;
        internal static HashSet<string>? _blacklistCacheWithClones;
        private static string? _lastBlacklistValue;
        internal static HashSet<string>? _recoveryBlacklistCache;
        internal static HashSet<string>? _recoveryBlacklistCacheWithClones;
        private static string? _lastRecoveryBlacklistValue;
        internal static HashSet<string>? _grabbableComponentTypesCache;
        private static string? _lastGrabbableComponentTypesValue;
        internal static HashSet<string>? _grabbableKeywordBlacklistCache;
        private static string? _lastGrabbableKeywordBlacklistValue;
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
            return _blacklistCacheWithClones!.Contains(name);
        }
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
            return _recoveryBlacklistCacheWithClones!.Contains(name);
        }
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
            foreach (var keyword in _grabbableKeywordBlacklistCache!)
            {
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        public static bool IsGrabbable(GameObject? obj)
        {
            if (obj == null) return false;
            if (IsKeywordBlacklisted(obj.name))
            {
                return false;
            }
            if (IsBlacklisted(obj.name))
            {
                return false;
            }
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
            foreach (var componentType in _grabbableComponentTypesCache!)
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
            var characterBody = obj.GetComponent<RoR2.CharacterBody>();
            if (characterBody != null)
            {
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
                return EnableEnvironmentGrabbing.Value;
            }
        }
        public static void Init(ConfigFile cfg)
        {
            SearchRangeMultiplier = cfg.Bind("Skill", "SearchRangeMultiplier", 1.0f, "Multiplier for Drifter's repossess search range");
            ForwardVelocityMultiplier = cfg.Bind("Skill", "ForwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess forward velocity");
            UpwardVelocityMultiplier = cfg.Bind("Skill", "UpwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess upward velocity");
            BreakoutTimeMultiplier = cfg.Bind("Skill", "BreakoutTimeMultiplier", 1.0f, "Multiplier for how long bagged enemies take to break out");
            MaxSmacks = cfg.Bind("Skill", "MaxSmacks", 3, new ConfigDescription("Maximum number of hits before bagged enemies break out", new AcceptableValueRange<int>(1, 100)));
            MassMultiplier = cfg.Bind("Skill", "MassMultiplier", "1", "Multiplier for the mass of bagged objects");
            EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Enable grabbing of boss enemies");
            EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Enable grabbing of NPCs with ungrabbable flag");
            EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Enable grabbing of environment objects like teleporters, chests, shrines");
            EnableLockedObjectGrabbing = cfg.Bind("General", "EnableLockedObjectGrabbing", false, "Enable grabbing of locked objects");
            EnableProjectileGrabbing = cfg.Bind("General", "EnableProjectileGrabbing", false, "Enable grabbing of projectiles");
            ProjectileGrabbingSurvivorOnly = cfg.Bind("General", "ProjectileGrabbingSurvivorOnly", true, "Restrict projectile grabbing to only those fired by survivor players");
            EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Enable debug logging");
            BodyBlacklist = cfg.Bind("General", "Blacklist", "HeaterPodBodyNoRespawn,ThrownObjectProjectile,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                "Comma-separated list of body and projectile names to never grab.\n" +
                "Example: SolusWingBody,Teleporter1,ShrineHalcyonite,PortalShop,RailgunnerPistolProjectile,SyringeProjectile\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see body/projectile names, case-insensitive matching");
            RecoveryObjectBlacklist = cfg.Bind("General", "RecoveryObjectBlacklist", "",
                "Comma-separated list of object names to never recover from the abyss\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
            GrabbableComponentTypes = cfg.Bind("General", "GrabbableComponentTypes", "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction",
                "Comma-separated list of component type names that make objects grabbable.\n" +
                "Example: SurfaceDefProvider,EntityStateMachine,JumpVolume\n" +
                "Objects must have at least one of these components to be grabbable.\n" +
                "Use exact component type names (case-sensitive).");
            GrabbableKeywordBlacklist = cfg.Bind("General", "GrabbableKeywordBlacklist", "Master,Controller",
                "Comma-separated list of keywords that make objects NOT grabbable if found in their name.\n" +
                "Example: Master\n" +
                "Objects with these keywords in their name will be excluded from grabbing.\n" +
                "Case-insensitive matching, partial matches allowed.\n" +
                "'Master' prevents grabbing enemy masters");
            EnableComponentAnalysisLogs = cfg.Bind("General", "EnableComponentAnalysisLogs", false,
                "Enable scanning of all objects in the current scene to log component types.\n" +
                "This can be performance-intensive and should only be enabled for debugging.\n" +
                "Shows all unique component types found in the scene for potential grabbable objects.");
            EnableObjectPersistence = cfg.Bind("Persistence", "EnableObjectPersistence",
                false,
                "Enable persistence of grabbed objects across stage transitions");
            EnableAutoGrab = cfg.Bind("Persistence", "EnableAutoGrab",
                false,
                "Automatically re-grab persisted objects on Drifter respawn");
            PersistBaggedBosses = cfg.Bind("Persistence", "PersistBaggedBosses",
                true,
                "Allow persistence of bagged boss enemies");
            PersistBaggedNPCs = cfg.Bind("Persistence", "PersistBaggedNPCs",
                true,
                "Allow persistence of bagged NPCs");
            PersistBaggedEnvironmentObjects = cfg.Bind("Persistence", "PersistBaggedEnvironmentObjects",
                true,
                "Allow persistence of bagged environment objects");
            PersistenceBlacklist = cfg.Bind("Persistence", "PersistenceBlacklist", "",
                "Comma-separated list of object names to never persist.\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
            BottomlessBagEnabled = cfg.Bind("Bottomless Bag", "EnableBottomlessBag",
                false,
                "Allows the scroll wheel to cycle through stored passengers. Bag capacity scales with the number of repossesses.");
        }
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
        public static void ClearBlacklistCache()
        {
            _blacklistCache = null;
            _blacklistCacheWithClones = null;
        }
        public static void ClearRecoveryBlacklistCache()
        {
            _recoveryBlacklistCache = null;
            _recoveryBlacklistCacheWithClones = null;
        }
        public static void ClearGrabbableComponentTypesCache()
        {
            _grabbableComponentTypesCache = null;
            _lastGrabbableComponentTypesValue = null;
        }
        public static void ClearGrabbableKeywordBlacklistCache()
        {
            _grabbableKeywordBlacklistCache = null;
            _lastGrabbableKeywordBlacklistValue = null;
        }
    }
}