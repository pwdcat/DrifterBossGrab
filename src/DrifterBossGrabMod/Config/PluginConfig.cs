using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace DrifterBossGrabMod
{
    public interface ICachedValue<T>
    {
        T Value { get; }
        void Invalidate();
    }

    public class LazyCachedValue<T> : ICachedValue<T>
    {
        private readonly Func<T> _factory;
        private T? _value;
        private bool _isValid;
        private readonly object _lock = new object();

        public T Value
        {
            get
            {
                lock (_lock)
                {
                    if (!_isValid)
                    {
                        _value = _factory();
                        _isValid = true;
                    }
                    return _value!;
                }
            }
        }

        public void Invalidate()
        {
            lock (_lock)
            {
                _isValid = false;
                _value = default;
            }
        }

        public LazyCachedValue(Func<T> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }
    }
    public class PluginConfig
    {
        private static PluginConfig _instance = null!;
        public static PluginConfig Instance => _instance ??= new PluginConfig();

        public ConfigEntry<float> SearchRangeMultiplier { get; private set; } = null!;
        public ConfigEntry<float> BreakoutTimeMultiplier { get; private set; } = null!;
        public ConfigEntry<float> ForwardVelocityMultiplier { get; private set; } = null!;
        public ConfigEntry<float> UpwardVelocityMultiplier { get; private set; } = null!;
        public ConfigEntry<bool> EnableBossGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableNPCGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableEnvironmentGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableLockedObjectGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableProjectileGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> ProjectileGrabbingSurvivorOnly { get; private set; } = null!;
        public ConfigEntry<int> MaxSmacks { get; private set; } = null!;
        public ConfigEntry<string> MassMultiplier { get; private set; } = null!;
        public ConfigEntry<bool> EnableDebugLogs { get; private set; } = null!;
        public ConfigEntry<string> BodyBlacklist { get; private set; } = null!;
        public ConfigEntry<string> RecoveryObjectBlacklist { get; private set; } = null!;
        public ConfigEntry<string> GrabbableComponentTypes { get; private set; } = null!;
        public ConfigEntry<string> GrabbableKeywordBlacklist { get; private set; } = null!;
        public ConfigEntry<bool> EnableComponentAnalysisLogs { get; private set; } = null!;
        public ConfigEntry<bool> EnableObjectPersistence { get; private set; } = null!;
        public ConfigEntry<bool> EnableAutoGrab { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedBosses { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedNPCs { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedEnvironmentObjects { get; private set; } = null!;
        public ConfigEntry<string> PersistenceBlacklist { get; private set; } = null!;
        public ConfigEntry<bool> BottomlessBagEnabled { get; private set; } = null!;
        public ConfigEntry<int> BottomlessBagBaseCapacity { get; private set; } = null!;
        public ConfigEntry<bool> EnableStockRefreshClamping { get; private set; } = null!;
        public ConfigEntry<bool> EnableMouseWheelScrolling { get; private set; } = null!;
        public ConfigEntry<KeyboardShortcut> ScrollUpKeybind { get; private set; } = null!;
        public ConfigEntry<KeyboardShortcut> ScrollDownKeybind { get; private set; } = null!;
        public ConfigEntry<float> CarouselSpacing { get; private set; } = null!;
        public ConfigEntry<float> CarouselCenterOffsetX { get; private set; } = null!;
        public ConfigEntry<float> CarouselCenterOffsetY { get; private set; } = null!;
        public ConfigEntry<float> CarouselSideOffsetX { get; private set; } = null!;
        public ConfigEntry<float> CarouselSideOffsetY { get; private set; } = null!;
        public ConfigEntry<float> CarouselSideScale { get; private set; } = null!;
        public ConfigEntry<float> CarouselSideOpacity { get; private set; } = null!;
        public ConfigEntry<float> CarouselAnimationDuration { get; private set; } = null!;
        public ConfigEntry<float> BagUIScale { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowPortrait { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowIcon { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowWeight { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowName { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowHealthBar { get; private set; } = null!;
        public ConfigEntry<bool> UseNewWeightIcon { get; private set; } = null!;
        public ConfigEntry<bool> ShowWeightText { get; private set; } = null!;
        public ConfigEntry<bool> ScaleWeightColor { get; private set; } = null!;
        internal ICachedValue<HashSet<string>> _blacklistCache = null!;
        internal ICachedValue<HashSet<string>> _blacklistCacheWithClones = null!;
        internal ICachedValue<HashSet<string>> _recoveryBlacklistCache = null!;
        internal ICachedValue<HashSet<string>> _recoveryBlacklistCacheWithClones = null!;
        internal ICachedValue<HashSet<string>> _grabbableComponentTypesCache = null!;
        internal ICachedValue<HashSet<string>> _grabbableKeywordBlacklistCache = null!;
        private readonly List<IGrabbingStrategy> _grabbingStrategies = new List<IGrabbingStrategy>
        {
            new BossGrabbingStrategy(),
            new NPCGrabbingStrategy(),
            new EnvironmentGrabbingStrategy()
        };
        public static bool IsBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Instance._blacklistCacheWithClones.Value.Contains(name);
        }
        public static bool IsRecoveryBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Instance._recoveryBlacklistCacheWithClones.Value.Contains(name);
        }
        public static bool IsKeywordBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var keyword in Instance._grabbableKeywordBlacklistCache.Value)
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
            bool hasRequiredComponent = false;
            foreach (var componentType in Instance._grabbableComponentTypesCache.Value)
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
            foreach (var strategy in Instance._grabbingStrategies)
            {
                if (strategy.CanGrab(obj))
                {
                    return true;
                }
            }
            return false;
        }
        public static void Init(ConfigFile cfg)
        {
            Instance.SearchRangeMultiplier = cfg.Bind("Skill", "SearchRangeMultiplier", 1.0f, "Multiplier for Drifter's repossess search range");
            Instance.ForwardVelocityMultiplier = cfg.Bind("Skill", "ForwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess forward velocity");
            Instance.UpwardVelocityMultiplier = cfg.Bind("Skill", "UpwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess upward velocity");
            Instance.BreakoutTimeMultiplier = cfg.Bind("Skill", "BreakoutTimeMultiplier", 1.0f, "Multiplier for how long bagged enemies take to break out");
            Instance.MaxSmacks = cfg.Bind("Skill", "MaxSmacks", 3, new ConfigDescription("Maximum number of hits before bagged enemies break out", new AcceptableValueRange<int>(1, 100)));
            Instance.MassMultiplier = cfg.Bind("Skill", "MassMultiplier", "1", "Multiplier for the mass of bagged objects");
            Instance.EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Enable grabbing of boss enemies");
            Instance.EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Enable grabbing of NPCs with ungrabbable flag");
            Instance.EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Enable grabbing of environment objects like teleporters, chests, shrines");
            Instance.EnableLockedObjectGrabbing = cfg.Bind("General", "EnableLockedObjectGrabbing", false, "Enable grabbing of locked objects");
            Instance.EnableProjectileGrabbing = cfg.Bind("General", "EnableProjectileGrabbing", false, "Enable grabbing of projectiles");
            Instance.ProjectileGrabbingSurvivorOnly = cfg.Bind("General", "ProjectileGrabbingSurvivorOnly", true, "Restrict projectile grabbing to only those fired by survivor players");
            Instance.EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Enable debug logging");
            Instance.BodyBlacklist = cfg.Bind("General", "Blacklist", "HeaterPodBodyNoRespawn,ThrownObjectProjectile,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                "Comma-separated list of body and projectile names to never grab.\n" +
                "Example: SolusWingBody,Teleporter1,ShrineHalcyonite,PortalShop,RailgunnerPistolProjectile,SyringeProjectile\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see body/projectile names, case-insensitive matching");
            Instance.RecoveryObjectBlacklist = cfg.Bind("General", "RecoveryObjectBlacklist", "",
                "Comma-separated list of object names to never recover from the abyss\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
            Instance.GrabbableComponentTypes = cfg.Bind("General", "GrabbableComponentTypes", "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction",
                "Comma-separated list of component type names that make objects grabbable.\n" +
                "Example: SurfaceDefProvider,EntityStateMachine,JumpVolume\n" +
                "Objects must have at least one of these components to be grabbable.\n" +
                "Use exact component type names (case-sensitive).");
            Instance.GrabbableKeywordBlacklist = cfg.Bind("General", "GrabbableKeywordBlacklist", "Master,Controller",
                "Comma-separated list of keywords that make objects NOT grabbable if found in their name.\n" +
                "Example: Master\n" +
                "Objects with these keywords in their name will be excluded from grabbing.\n" +
                "Case-insensitive matching, partial matches allowed.\n" +
                "'Master' prevents grabbing enemy masters");
            Instance.EnableComponentAnalysisLogs = cfg.Bind("General", "EnableComponentAnalysisLogs", false,
                "Enable scanning of all objects in the current scene to log component types.\n" +
                "This can be performance-intensive and should only be enabled for debugging.\n" +
                "Shows all unique component types found in the scene for potential grabbable objects.");
            Instance.EnableObjectPersistence = cfg.Bind("Persistence", "EnableObjectPersistence",
                false,
                "Enable persistence of grabbed objects across stage transitions");
            Instance.EnableAutoGrab = cfg.Bind("Persistence", "EnableAutoGrab",
                false,
                "Automatically re-grab persisted objects on Drifter respawn");
            Instance.PersistBaggedBosses = cfg.Bind("Persistence", "PersistBaggedBosses",
                true,
                "Allow persistence of bagged boss enemies");
            Instance.PersistBaggedNPCs = cfg.Bind("Persistence", "PersistBaggedNPCs",
                true,
                "Allow persistence of bagged NPCs");
            Instance.PersistBaggedEnvironmentObjects = cfg.Bind("Persistence", "PersistBaggedEnvironmentObjects",
                true,
                "Allow persistence of bagged environment objects");
            Instance.PersistenceBlacklist = cfg.Bind("Persistence", "PersistenceBlacklist", "",
                "Comma-separated list of object names to never persist.\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
            Instance.BottomlessBagEnabled = cfg.Bind("Bottomless Bag", "EnableBottomlessBag",
                false,
                "Allows the scroll wheel to cycle through stored passengers. Bag capacity scales with the number of repossesses.");
            Instance.BottomlessBagBaseCapacity = cfg.Bind("Bottomless Bag", "BaseCapacity", 0, "Base capacity for bottomless bag, added to utility max stocks");
            Instance.EnableStockRefreshClamping = cfg.Bind("Bottomless Bag", "EnableStockRefreshClamping", false, "When enabled, Repossess stock refresh is clamped to max stocks minus number of bagged items");
            Instance.EnableMouseWheelScrolling = cfg.Bind("Bottomless Bag", "EnableMouseWheelScrolling", true, "Enable mouse wheel scrolling for cycling passengers");
            Instance.ScrollUpKeybind = cfg.Bind("Bottomless Bag", "ScrollUpKeybind", new KeyboardShortcut(KeyCode.None), "Keybind to scroll up through passengers");
            Instance.ScrollDownKeybind = cfg.Bind("Bottomless Bag", "ScrollDownKeybind", new KeyboardShortcut(KeyCode.None), "Keybind to scroll down through passengers");
            Instance.CarouselSpacing = cfg.Bind("Hud", "CarouselSpacing", 120.0f, "Vertical spacing for carousel items");
            Instance.CarouselCenterOffsetX = cfg.Bind("Hud", "CarouselCenterOffsetX", 0.0f, "Horizontal offset for the center carousel item");
            Instance.CarouselCenterOffsetY = cfg.Bind("Hud", "CarouselCenterOffsetY", 0.0f, "Vertical offset for the center carousel item");
            Instance.CarouselSideOffsetX = cfg.Bind("Hud", "CarouselSideOffsetX", 0.0f, "Horizontal offset for the side carousel items");
            Instance.CarouselSideOffsetY = cfg.Bind("Hud", "CarouselSideOffsetY", 0.0f, "Vertical offset for the side carousel items");
            Instance.CarouselSideScale = cfg.Bind("Hud", "CarouselSideScale", 0.8f, "Scale for side carousel items");
            Instance.CarouselSideOpacity = cfg.Bind("Hud", "CarouselSideOpacity", 0.6f, "Opacity for side carousel items");
            Instance.CarouselAnimationDuration = cfg.Bind("Hud", "CarouselAnimationDuration", 0.5f, "Duration of carousel animation in seconds");
            Instance.BagUIScale = cfg.Bind("Hud", "BagUIScale", 0.8f, "Overall scale for carousel slots");
            Instance.BagUIShowPortrait = cfg.Bind("Hud", "BagUIShowPortrait", true, "Show portrait in additional Bag UI elements");
            Instance.BagUIShowIcon = cfg.Bind("Hud", "BagUIShowIcon", true, "Show icon in additional Bag UI elements");
            Instance.BagUIShowWeight = cfg.Bind("Hud", "BagUIShowWeight", true, "Show weight indicator in additional Bag UI elements");
            Instance.BagUIShowName = cfg.Bind("Hud", "BagUIShowName", true, "Show name in additional Bag UI elements");
            Instance.BagUIShowHealthBar = cfg.Bind("Hud", "BagUIShowHealthBar", true, "Show health bar in additional Bag UI elements");
            Instance.UseNewWeightIcon = cfg.Bind("Hud", "UseNewWeightIcon", false, "Use the new custom weight icon instead of the original");
            Instance.ShowWeightText = cfg.Bind("Hud", "ShowWeightText", false, "Show weight multiplier text on the weight icon");
            Instance.ScaleWeightColor = cfg.Bind("Hud", "ScaleWeightColor", true, "Scale the weight icon color based on mass");

            // Add event handlers for live updates
            Instance.BagUIShowPortrait.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowWeight.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowName.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowHealthBar.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.UseNewWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ShowWeightText.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ScaleWeightColor.SettingChanged += (sender, args) => UpdateBagUIToggles();

            // Initialize lazy caches
            Instance._blacklistCache = new LazyCachedValue<HashSet<string>>(() =>
                string.IsNullOrEmpty(Instance.BodyBlacklist.Value)
                    ? new HashSet<string>()
                    : Instance.BodyBlacklist.Value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase));

            Instance._blacklistCacheWithClones = new LazyCachedValue<HashSet<string>>(() =>
            {
                var baseSet = Instance._blacklistCache.Value;
                var withClones = new HashSet<string>(baseSet, StringComparer.OrdinalIgnoreCase);
                foreach (var item in baseSet)
                {
                    withClones.Add(item + Constants.CloneSuffix);
                }
                return withClones;
            });

            Instance._recoveryBlacklistCache = new LazyCachedValue<HashSet<string>>(() =>
                string.IsNullOrEmpty(Instance.RecoveryObjectBlacklist.Value)
                    ? new HashSet<string>()
                    : Instance.RecoveryObjectBlacklist.Value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase));

            Instance._recoveryBlacklistCacheWithClones = new LazyCachedValue<HashSet<string>>(() =>
            {
                var baseSet = Instance._recoveryBlacklistCache.Value;
                var withClones = new HashSet<string>(baseSet, StringComparer.OrdinalIgnoreCase);
                foreach (var item in baseSet)
                {
                    withClones.Add(item + Constants.CloneSuffix);
                }
                return withClones;
            });

            Instance._grabbableComponentTypesCache = new LazyCachedValue<HashSet<string>>(() =>
                string.IsNullOrEmpty(Instance.GrabbableComponentTypes.Value)
                    ? new HashSet<string>()
                    : Instance.GrabbableComponentTypes.Value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.Ordinal));

            Instance._grabbableKeywordBlacklistCache = new LazyCachedValue<HashSet<string>>(() =>
                string.IsNullOrEmpty(Instance.GrabbableKeywordBlacklist.Value)
                    ? new HashSet<string>()
                    : Instance.GrabbableKeywordBlacklist.Value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase));

            // Wire invalidation on config changes
            Instance.BodyBlacklist.SettingChanged += (sender, args) => { Instance._blacklistCache.Invalidate(); Instance._blacklistCacheWithClones.Invalidate(); };
            Instance.RecoveryObjectBlacklist.SettingChanged += (sender, args) => { Instance._recoveryBlacklistCache.Invalidate(); Instance._recoveryBlacklistCacheWithClones.Invalidate(); };
            Instance.GrabbableComponentTypes.SettingChanged += (sender, args) => Instance._grabbableComponentTypesCache.Invalidate();
            Instance.GrabbableKeywordBlacklist.SettingChanged += (sender, args) => Instance._grabbableKeywordBlacklistCache.Invalidate();
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
            Instance.EnableDebugLogs.SettingChanged -= debugLogsHandler;
            Instance.BodyBlacklist.SettingChanged -= blacklistHandler;
            Instance.ForwardVelocityMultiplier.SettingChanged -= forwardVelHandler;
            Instance.UpwardVelocityMultiplier.SettingChanged -= upwardVelHandler;
            Instance.RecoveryObjectBlacklist.SettingChanged -= recoveryBlacklistHandler;
            Instance.GrabbableComponentTypes.SettingChanged -= grabbableComponentTypesHandler;
            Instance.GrabbableKeywordBlacklist.SettingChanged -= grabbableKeywordBlacklistHandler;
            Instance.EnableBossGrabbing.SettingChanged -= bossGrabbingHandler;
            Instance.EnableNPCGrabbing.SettingChanged -= npcGrabbingHandler;
            Instance.EnableEnvironmentGrabbing.SettingChanged -= environmentGrabbingHandler;
            Instance.EnableLockedObjectGrabbing.SettingChanged -= lockedObjectGrabbingHandler;
        }
        public static void ClearBlacklistCache()
        {
            Instance._blacklistCache.Invalidate();
            Instance._blacklistCacheWithClones.Invalidate();
        }
        public static void ClearRecoveryBlacklistCache()
        {
            Instance._recoveryBlacklistCache.Invalidate();
            Instance._recoveryBlacklistCacheWithClones.Invalidate();
        }
        public static void ClearGrabbableComponentTypesCache()
        {
            Instance._grabbableComponentTypesCache.Invalidate();
        }
        public static void ClearGrabbableKeywordBlacklistCache()
        {
            Instance._grabbableKeywordBlacklistCache.Invalidate();
        }

        private static void UpdateBagUIScale()
        {
            var carousels = UnityEngine.Object.FindObjectsByType<UI.BaggedObjectCarousel>(FindObjectsSortMode.None);
            foreach (var carousel in carousels)
            {
                carousel.UpdateScales();
            }
        }


        private static void UpdateBagUIToggles()
        {
            var carousels = UnityEngine.Object.FindObjectsByType<UI.BaggedObjectCarousel>(FindObjectsSortMode.None);
            foreach (var carousel in carousels)
            {
                carousel.UpdateToggles();
            }
        }


        private static UnityEngine.Transform FindDeepChild(UnityEngine.Transform parent, string name)
        {
            foreach (UnityEngine.Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                var result = FindDeepChild(child, name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}