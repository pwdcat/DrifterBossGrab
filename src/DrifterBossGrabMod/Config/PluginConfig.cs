using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using RoR2;
using DrifterBossGrabMod.Balance;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod
{
    public enum ProjectileGrabbingMode
    {
        None = 0,
        SurvivorOnly = 1,
        AllProjectiles = 2
    }

    public enum WeightDisplayMode
    {
        None = 0,
        Multiplier = 1,
        Pounds = 2,
        KiloGrams = 3
    }

    public enum StateCalculationMode
    {
        Current = 0,
        All = 1
    }

    public enum AoEDamageMode
    {
        Full = 0,
        Split = 1
    }

    public enum CharacterFlagType
    {
        Elite,
        Boss,
        Champion,
        Player,
        Minion,
        Drone,
        Mechanical,
        Void
    }

    public enum HudSubTabType
    {
        All,
        Carousel,
        CapacityUI,
        DamagePreview
    }

    public enum BalanceSubTabType
    {
        All,
        Capacity,
        MassMultipliers,
        Overencumbrance,
        StateCalculation,
        MovespeedPenalty,
        Other
    }

    public enum PresetType
    {
        Vanilla,       // All features disabled, vanilla behavior
        Intended,      // Boss grab only
        Default,       // All features in DrifterGrabFeature + bottomless bag and persistence
        Balance,       // Default + balance features
        Custom         // User has modified settings
    }

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
        public ConfigEntry<ProjectileGrabbingMode> ProjectileGrabbingMode { get; private set; } = null!;
        public ConfigEntry<int> MaxSmacks { get; private set; } = null!;
        public ConfigEntry<string> MassMultiplier { get; private set; } = null!;
        public ConfigEntry<bool> EnableDebugLogs { get; private set; } = null!;
        public ConfigEntry<string> BodyBlacklist { get; private set; } = null!;
        public ConfigEntry<string> RecoveryObjectBlacklist { get; private set; } = null!;
        public ConfigEntry<string> GrabbableComponentTypes { get; private set; } = null!;
        public ConfigEntry<string> GrabbableKeywordBlacklist { get; private set; } = null!;
        public ConfigEntry<bool> EnableConfigSync { get; private set; } = null!;
        public ConfigEntry<bool> EnableComponentAnalysisLogs { get; private set; } = null!;
        public ConfigEntry<bool> EnableObjectPersistence { get; private set; } = null!;
        public ConfigEntry<bool> EnableAutoGrab { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedBosses { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedNPCs { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedEnvironmentObjects { get; private set; } = null!;
        public ConfigEntry<string> PersistenceBlacklist { get; private set; } = null!;
        public ConfigEntry<float> AutoGrabDelay { get; private set; } = null!;
        public ConfigEntry<bool> BottomlessBagEnabled { get; private set; } = null!;
        public ConfigEntry<int> BottomlessBagBaseCapacity { get; private set; } = null!;
        public ConfigEntry<bool> EnableStockRefreshClamping { get; private set; } = null!;
        public ConfigEntry<float> CycleCooldown { get; private set; } = null!;
        public ConfigEntry<bool> EnableMouseWheelScrolling { get; private set; } = null!;
        public ConfigEntry<bool> InverseMouseWheelScrolling { get; private set; } = null!;
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
        public ConfigEntry<bool> BagUIShowIcon { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowWeight { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowName { get; private set; } = null!;
        public ConfigEntry<bool> BagUIShowHealthBar { get; private set; } = null!;
        public ConfigEntry<bool> EnableDamagePreview { get; private set; } = null!;
        public ConfigEntry<Color> DamagePreviewColor { get; private set; } = null!;
        public ConfigEntry<bool> UseNewWeightIcon { get; private set; } = null!;
        public ConfigEntry<WeightDisplayMode> WeightDisplayMode { get; private set; } = null!;
        public ConfigEntry<bool> ScaleWeightColor { get; private set; } = null!;
        public ConfigEntry<bool> AutoPromoteMainSeat { get; private set; } = null!;
        public ConfigEntry<bool> UncapBagScale { get; private set; } = null!;
        public ConfigEntry<bool> UncapMass { get; private set; } = null!;
        public ConfigEntry<bool> EnableCarouselHUD { get; private set; } = null!;
        public ConfigEntry<bool> EnableMassCapacityUI { get; private set; } = null!;
        public ConfigEntry<float> MassCapacityUIPositionX { get; private set; } = null!;
        public ConfigEntry<float> MassCapacityUIPositionY { get; private set; } = null!;
        public ConfigEntry<float> MassCapacityUIScale { get; private set; } = null!;
        // Balance configuration
        public ConfigEntry<bool> EnableBalance { get; private set; } = null!;
        public ConfigEntry<bool> EnableAoESlamDamage { get; private set; } = null!;
        public ConfigEntry<AoEDamageMode> AoEDamageDistribution { get; private set; } = null!;
        public ConfigEntry<DrifterBossGrabMod.Balance.CapacityScalingMode> CapacityScalingMode { get; private set; } = null!;
        public ConfigEntry<DrifterBossGrabMod.Balance.ScalingType> CapacityScalingType { get; private set; } = null!;
        public ConfigEntry<float> CapacityScalingBonusPerCapacity { get; private set; } = null!;
        public ConfigEntry<float> EliteMassBonusPercent { get; private set; } = null!;
        public ConfigEntry<bool> EnableOverencumbrance { get; private set; } = null!;
        public ConfigEntry<float> OverencumbranceMaxPercent { get; private set; } = null!;
        public ConfigEntry<bool> UncapCapacity { get; private set; } = null!;
        public ConfigEntry<bool> ToggleMassCapacity { get; private set; } = null!;
        public ConfigEntry<bool> StateCalculationModeEnabled { get; private set; } = null!;
        public ConfigEntry<StateCalculationMode> StateCalculationMode { get; private set; } = null!;
        public ConfigEntry<float> AllModeMassMultiplier { get; private set; } = null!;

        // Character flag mass multiplier configurations
        public ConfigEntry<float> BossMassBonusPercent { get; private set; } = null!;
        public ConfigEntry<float> ChampionMassBonusPercent { get; private set; } = null!;
        public ConfigEntry<float> PlayerMassBonusPercent { get; private set; } = null!;
        public ConfigEntry<float> MinionMassBonusPercent { get; private set; } = null!;
        public ConfigEntry<float> DroneMassBonusPercent { get; private set; } = null!;
        public ConfigEntry<float> MechanicalMassBonusPercent { get; private set; } = null!;
        public ConfigEntry<float> VoidMassBonusPercent { get; private set; } = null!;

        // Risk of Options UI controls for flag multiplier configuration
        public ConfigEntry<CharacterFlagType> SelectedFlag { get; private set; } = null!;
        public ConfigEntry<float> SelectedFlagMultiplier { get; private set; } = null!;

        // Risk of Options UI controls for HUD sub-tab system
        public ConfigEntry<HudSubTabType> SelectedHudSubTab { get; private set; } = null!;

        // Risk of Options UI controls for Balance sub-tab system
        public ConfigEntry<BalanceSubTabType> SelectedBalanceSubTab { get; private set; } = null!;

        // Risk of Options UI controls for Preset system
        public ConfigEntry<PresetType> SelectedPreset { get; private set; } = null!;

        public ConfigEntry<float> MinMovespeedPenalty { get; private set; } = null!;
        public ConfigEntry<float> MaxMovespeedPenalty { get; private set; } = null!;
        public ConfigEntry<float> FinalMovespeedPenaltyLimit { get; private set; } = null!;

        // Mapping of setting tokens to HUD sub-tabs
        public static readonly Dictionary<string, HudSubTabType> HudSettingToSubTab = new()
        {
            // Carousel settings (includes all carousel + bag UI elements)
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLECAROUSELHUD.CHECKBOX"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELSPACING.FLOAT_FIELD"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELCENTEROFFSETX.FLOAT_FIELD"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELCENTEROFFSETY.FLOAT_FIELD"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELSIDEOFFSETX.FLOAT_FIELD"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELSIDEOFFSETY.FLOAT_FIELD"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELSIDESCALE.FLOAT_FIELD"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELSIDEOPACITY.FLOAT_FIELD"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELANIMATIONDURATION.FLOAT_FIELD"] = HudSubTabType.Carousel,
            // Bag UI settings (icons, names, healthbar, weight)
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGUISHOWICON.CHECKBOX"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGUISHOWWEIGHT.CHECKBOX"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGUISHOWNAME.CHECKBOX"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGUISHOWHEALTHBAR.CHECKBOX"] = HudSubTabType.Carousel,
            // Weight icon and display settings
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.USENEWWEIGHTICON.CHECKBOX"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.WEIGHTDISPLAYMODE.CHOICE"] = HudSubTabType.Carousel,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SCALEWEIGHTCOLOR.CHECKBOX"] = HudSubTabType.Carousel,

            // Capacity UI settings
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLEMASSCAPACITYUI.CHECKBOX"] = HudSubTabType.CapacityUI,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.MASSCAPACITYUIPOSITIONX.FLOAT_FIELD"] = HudSubTabType.CapacityUI,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.MASSCAPACITYUIPOSITIONY.FLOAT_FIELD"] = HudSubTabType.CapacityUI,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.MASSCAPACITYUISCALE.FLOAT_FIELD"] = HudSubTabType.CapacityUI,

            // Damage Preview settings (only these two)
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLEDAMAGEPREVIEW.CHECKBOX"] = HudSubTabType.DamagePreview,
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.DAMAGEPREVIEWCOLOR.COLOR"] = HudSubTabType.DamagePreview
        };

        // Mapping of setting tokens to Balance sub-tabs
        public static readonly Dictionary<string, BalanceSubTabType> BalanceSettingToSubTab = new()
        {
            // Capacity settings
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.ENABLEBALANCE.CHECKBOX"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.UNCAPCAPACITY.CHECKBOX"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.TOGGLEMASSCAPACITY.CHECKBOX"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.CAPACITYSCALINGMODE.CHOICE"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.CAPACITYSCALINGTYPE.CHOICE"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.CAPACITYSCALINGBONUSPERCAPACITY.FLOAT_FIELD"] = BalanceSubTabType.Capacity,

            // Mass Multipliers settings (only UI controls, not individual multipliers)
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.SELECTEDFLAG.CHOICE"] = BalanceSubTabType.MassMultipliers,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.FLAGMULTIPLIER.FLOAT_FIELD"] = BalanceSubTabType.MassMultipliers,

            // Overencumbrance settings
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.ENABLEOVERENCUMBRANCE.CHECKBOX"] = BalanceSubTabType.Overencumbrance,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.OVERENCUMBRANCEMAXPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Overencumbrance,

            // State Calculation settings
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.STATECALCULATIONMODEENABLED.CHECKBOX"] = BalanceSubTabType.StateCalculation,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.STATECALCULATIONMODE.CHOICE"] = BalanceSubTabType.StateCalculation,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.ALLMODEMASSMULTIPLIER.FLOAT_FIELD"] = BalanceSubTabType.StateCalculation,

            // Movespeed Penalty settings
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MINMOVESPEEDPENALTY.FLOAT_FIELD"] = BalanceSubTabType.MovespeedPenalty,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAXMOVESPEEDPENALTY.FLOAT_FIELD"] = BalanceSubTabType.MovespeedPenalty,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.FINALMOVESPEEDPENALTYLIMIT.FLOAT_FIELD"] = BalanceSubTabType.MovespeedPenalty,

            // Other settings (includes individual mass multipliers and other settings)
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.ELITEMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.BOSSMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.CHAMPIONMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.PLAYERMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MINIONMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.DRONEMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MECHANICALMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.VOIDMASSBONUSPERCENT.FLOAT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.UNCAPBAGSCALE.CHECKBOX"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.UNCAPMASS.CHECKBOX"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.ENABLEAOESLAMDAMAGE.CHECKBOX"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.AOEDAMAGEDISTRIBUTION.CHOICE"] = BalanceSubTabType.Other
        };

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
            Instance.SearchRangeMultiplier = cfg.Bind("Skill", "SearchRangeMultiplier", 1.0f, "Multiplier for Drifter's repossess search range.\nFormula: FinalRange = BaseRange × SearchRangeMultiplier");
            Instance.ForwardVelocityMultiplier = cfg.Bind("Skill", "ForwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess forward velocity.\nFormula: FinalForwardVelocity = BaseForwardVelocity × ForwardVelocityMultiplier");
            Instance.UpwardVelocityMultiplier = cfg.Bind("Skill", "UpwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess upward velocity.\nFormula: FinalUpwardVelocity = BaseUpwardVelocity × UpwardVelocityMultiplier");
            Instance.BreakoutTimeMultiplier = cfg.Bind("Skill", "BreakoutTimeMultiplier", 1.0f, "Multiplier for how long bagged enemies take to break out.\nFormula: FinalBreakoutTime = BaseBreakoutTime × BreakoutTimeMultiplier");
            Instance.MaxSmacks = cfg.Bind("Skill", "MaxSmacks", 3, new ConfigDescription("Maximum number of hits before bagged enemies break out.\nBagged enemies will break out after receiving this many hits.", new AcceptableValueRange<int>(1, 100)));
            Instance.MassMultiplier = cfg.Bind("Skill", "MassMultiplier", "1", "Multiplier for mass of bagged objects.\nFormula: FinalMass = BaseMass × MassMultiplier\nExample: 1.5 = 50% more mass, 0.5 = 50% less mass");
            Instance.EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Enable grabbing of boss enemies.\nWhen disabled, boss enemies cannot be repossessed.");
            Instance.EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Enable grabbing of NPCs with ungrabbable flag.\nWhen enabled, allows grabbing NPCs that are normally marked as ungrabbable.");
            Instance.EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Enable grabbing of environment objects like teleporters, chests, shrines.\nWhen enabled, allows repossessing interactable world objects.");
            Instance.EnableLockedObjectGrabbing = cfg.Bind("General", "EnableLockedObjectGrabbing", false, "Enable grabbing of locked objects.\nWhen enabled, allows grabbing objects that are currently locked (e.g., locked chests).");
            Instance.ProjectileGrabbingMode = cfg.Bind("General", "ProjectileGrabbingMode", DrifterBossGrabMod.ProjectileGrabbingMode.None, "Mode for projectile grabbing:\n- None: Cannot grab projectiles\n- SurvivorOnly: Can only grab survivor projectiles\n- AllProjectiles: Can grab all projectiles");
            Instance.EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Enable debug logging.\nWhen enabled, logs detailed information about grabbing mechanics for debugging.");
            Instance.BodyBlacklist = cfg.Bind("General", "Blacklist", "HeaterPodBodyNoRespawn,ThrownObjectProjectile,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                "Comma-separated list of body and projectile names to never grab.\n" +
                "Example: SolusWingBody,Teleporter1,ShrineHalcyonite,PortalShop,RailgunnerPistolProjectile,SyringeProjectile\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see body/projectile names, case-insensitive matching");
            Instance.RecoveryObjectBlacklist = cfg.Bind("General", "RecoveryObjectBlacklist", "",
                "Comma-separated list of object names to never recover from the abyss.\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
            Instance.GrabbableComponentTypes = cfg.Bind("General", "GrabbableComponentTypes", "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction",
                "Comma-separated list of component type names that make objects grabbable.\n" +
                "Objects must have at least one of these components to be grabbable.\n" +
                "Example: SurfaceDefProvider,EntityStateMachine,JumpVolume\n" +
                "Use exact component type names (case-sensitive).");
            Instance.GrabbableKeywordBlacklist = cfg.Bind("General", "GrabbableKeywordBlacklist", "Master,Controller",
                "Comma-separated list of keywords that make objects NOT grabbable if found in their name.\n" +
                "Objects with these keywords in their name will be excluded from grabbing.\n" +
                "Example: 'Master' prevents grabbing enemy masters\n" +
                "Case-insensitive matching, partial matches allowed.");
            Instance.EnableConfigSync = cfg.Bind("General", "EnableConfigSync", true,
                "Enable synchronization of configuration settings from host to new clients.\n" +
                "When enabled, clients joining a game will receive the host's configuration settings.");
            Instance.EnableComponentAnalysisLogs = cfg.Bind("General", "EnableComponentAnalysisLogs", false,
                "Enable scanning of all objects in the current scene to log component types.\n" +
                "This can be performance-intensive and should only be enabled for debugging.\n" +
                "Shows all unique component types found in the scene for potential grabbable objects.");
            Instance.EnableObjectPersistence = cfg.Bind("Persistence", "EnableObjectPersistence",
                false,
                "Enable persistence of grabbed objects across stage transitions.\n" +
                "When enabled, bagged objects are saved and restored when changing stages.");
            Instance.EnableAutoGrab = cfg.Bind("Persistence", "EnableAutoGrab",
                false,
                "Automatically re-grab persisted objects on Drifter respawn.\n" +
                "When enabled, persisted objects are automatically repossessed after Drifter respawns.");
            Instance.PersistBaggedBosses = cfg.Bind("Persistence", "PersistBaggedBosses",
                true,
                "Allow persistence of bagged boss enemies.\n" +
                "When disabled, boss enemies will not persist across stages.");
            Instance.PersistBaggedNPCs = cfg.Bind("Persistence", "PersistBaggedNPCs",
                true,
                "Allow persistence of bagged NPCs.\n" +
                "When disabled, NPCs will not persist across stages.");
            Instance.PersistBaggedEnvironmentObjects = cfg.Bind("Persistence", "PersistBaggedEnvironmentObjects",
                true,
                "Allow persistence of bagged environment objects.\n" +
                "When disabled, environment objects will not persist across stages.");
            Instance.PersistenceBlacklist = cfg.Bind("Persistence", "PersistenceBlacklist", "",
                "Comma-separated list of object names to never persist.\n" +
                "Example: Teleporter1,Chest1,ShrineChance\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see object names, case-insensitive matching");
            Instance.AutoGrabDelay = cfg.Bind("Persistence", "AutoGrabDelay", 1.0f, "Delay before auto-grabbing persisted objects on stage start (seconds).\n" +
                "Determines how long to wait after stage start before auto-grabbing persisted objects.");
            Instance.BottomlessBagEnabled = cfg.Bind("Bottomless Bag", "EnableBottomlessBag",
                false,
                "Allows the scroll wheel to cycle through stored passengers.\n" +
                "When enabled, bag capacity scales with the number of repossesses.\n" +
                "Formula: TotalCapacity = UtilityMaxStocks + BaseCapacity + (UtilityMaxStocks × CapacityScalingBonus)");
            Instance.BottomlessBagBaseCapacity = cfg.Bind("Bottomless Bag", "BaseCapacity", 0, "Base capacity for bottomless bag, added to utility max stocks.\n" +
                "Formula: TotalCapacity = UtilityMaxStocks + BaseCapacity + CapacityScalingBonus");
            Instance.EnableStockRefreshClamping = cfg.Bind("Bottomless Bag", "EnableStockRefreshClamping", false, "When enabled, Repossess stock refresh is clamped to max stocks minus number of bagged items.\n" +
                "Formula: RefreshedStocks = MaxStocks - BaggedItemCount\n" +
                "Prevents refreshing more stocks than available slots.");
            Instance.CycleCooldown = cfg.Bind("Bottomless Bag", "CycleCooldown", 0.2f, "Cooldown between passenger cycles (seconds).\n" +
                "Minimum time between scroll wheel cycles to prevent rapid switching.");
            Instance.EnableMouseWheelScrolling = cfg.Bind("Bottomless Bag", "EnableMouseWheelScrolling", true, "Enable mouse wheel scrolling for cycling passengers.\n" +
                "When enabled, mouse wheel can be used to cycle through bagged objects.");
            Instance.InverseMouseWheelScrolling = cfg.Bind("Bottomless Bag", "InverseMouseWheelScrolling", false, "Invert the mouse wheel scrolling direction.\n" +
                "When enabled, scrolling up goes to previous object, down goes to next.");
            Instance.ScrollUpKeybind = cfg.Bind("Bottomless Bag", "ScrollUpKeybind", new KeyboardShortcut(KeyCode.None), "Keybind to scroll up through passengers.\n" +
                "Alternative to mouse wheel for cycling to previous object.");
            Instance.ScrollDownKeybind = cfg.Bind("Bottomless Bag", "ScrollDownKeybind", new KeyboardShortcut(KeyCode.None), "Keybind to scroll down through passengers.\n" +
                "Alternative to mouse wheel for cycling to next object.");
            Instance.EnableCarouselHUD = cfg.Bind("Hud", "EnableCarouselHUD", false, "Enable the custom Carousel HUD for Drifter's bag.\n" +
                "When disabled, reverts to vanilla UI behavior.\n" +
                "Note: Automatically enabled when BottomlessBag is enabled.");
            Instance.CarouselSpacing = cfg.Bind("Hud", "CarouselSpacing", 45.0f, "Vertical spacing for carousel items.\n" +
                "Distance between adjacent items in the carousel.");
            Instance.CarouselCenterOffsetX = cfg.Bind("Hud", "CarouselCenterOffsetX", 25.0f, "Horizontal offset for center carousel item.\n" +
                "X position offset for the currently selected item.");
            Instance.CarouselCenterOffsetY = cfg.Bind("Hud", "CarouselCenterOffsetY", 50.0f, "Vertical offset for center carousel item.\n" +
                "Y position offset for the currently selected item.");
            Instance.CarouselSideOffsetX = cfg.Bind("Hud", "CarouselSideOffsetX", 20.0f, "Horizontal offset for side carousel items.\n" +
                "X position offset for adjacent items.");
            Instance.CarouselSideOffsetY = cfg.Bind("Hud", "CarouselSideOffsetY", 5.0f, "Vertical offset for side carousel items.\n" +
                "Y position offset for adjacent items.");
            Instance.CarouselSideScale = cfg.Bind("Hud", "CarouselSideScale", 0.8f, "Scale for side carousel items.\n" +
                "Size multiplier for adjacent items (0.0 to 1.0).");
            Instance.CarouselSideOpacity = cfg.Bind("Hud", "CarouselSideOpacity", 0.3f, "Opacity for side carousel items.\n" +
                "Transparency for adjacent items (0.0 to 1.0).");
            Instance.CarouselAnimationDuration = cfg.Bind("Hud", "CarouselAnimationDuration", 0.4f, "Duration of carousel animation in seconds.\n" +
                "Time for carousel items to animate into position when cycling.");
            Instance.BagUIShowIcon = cfg.Bind("Hud", "BagUIShowIcon", true, "Show icon in additional Bag UI elements.\n" +
                "When enabled, displays the object's icon in the UI.");
            Instance.BagUIShowWeight = cfg.Bind("Hud", "BagUIShowWeight", true, "Show weight indicator in additional Bag UI elements.\n" +
                "When enabled, displays the object's weight/mass in the UI.");
            Instance.BagUIShowName = cfg.Bind("Hud", "BagUIShowName", true, "Show name in additional Bag UI elements.\n" +
                "When enabled, displays the object's name in the UI.");
            Instance.BagUIShowHealthBar = cfg.Bind("Hud", "BagUIShowHealthBar", true, "Show health bar in additional Bag UI elements.\n" +
                "When enabled, displays a health bar for the object in the UI.");
            Instance.EnableDamagePreview = cfg.Bind("Hud", "EnableDamagePreview", false, "Show a damage preview overlay on bagged object health bars.\n" +
                "Indicates predicted slam damage to the object.");
            Instance.DamagePreviewColor = cfg.Bind("Hud", "DamagePreviewColor", new Color(1f, 0.15f, 0.15f, 0.8f), "Color for the damage preview overlay.\n" +
                "RGBA color for the damage preview indicator.");
            Instance.UseNewWeightIcon = cfg.Bind("Hud", "UseNewWeightIcon", false, "Use the new custom weight icon instead of the original.\n" +
                "When enabled, uses a custom weight icon design.");
            Instance.WeightDisplayMode = cfg.Bind("Hud", "WeightDisplayMode", DrifterBossGrabMod.WeightDisplayMode.Multiplier, "Mode for displaying weight:\n" +
                "- None: No weight display\n" +
                "- Multiplier: Show as mass multiplier (e.g., 2.5x)\n" +
                "- Pounds: Show in pounds (lb)\n" +
                "- KiloGrams: Show in kilograms (kg)");
            Instance.ScaleWeightColor = cfg.Bind("Hud", "ScaleWeightColor", true, "Scale the weight icon color based on mass.\n" +
                "When enabled, weight icon color changes from green (light) to red (heavy) based on mass.");
            Instance.AutoPromoteMainSeat = cfg.Bind("Bottomless Bag", "AutoPromoteMainSeat", true, "Automatically promote the next object in the bag to the main seat when the current main object is removed.\n" +
                "When enabled, cycling through the bag automatically updates the main seat.");
            Instance.UncapBagScale = cfg.Bind("Balance", "UncapBagScale", false, "When enabled, the bag visual size will not be capped and will continue to grow based on the mass of the stored object(s).\n" +
                "Formula: BagScale = 0.5 + 0.5 × ((Mass - 1) / (MaxMass - 1))\n" +
                "When disabled, bag scale is clamped between 1 and MaxMass.");
            Instance.UncapMass = cfg.Bind("Balance", "UncapMass", false, "When enabled, the mass cap of 700 is removed.\n" +
                "When disabled, mass is clamped to a maximum of 700.");
            Instance.EnableMassCapacityUI = cfg.Bind("Hud", "EnableMassCapacityUI", false, "Enable the Mass Capacity UI for displaying bag capacity status.\n" +
                "Shows current mass vs capacity as a progress bar.");
            Instance.MassCapacityUIPositionX = cfg.Bind("Hud", "MassCapacityUIPositionX", -20.0f, "Horizontal position offset for the Mass Capacity UI.\n" +
                "X position offset from the default location.");
            Instance.MassCapacityUIPositionY = cfg.Bind("Hud", "MassCapacityUIPositionY", 0.0f, "Vertical position offset for the Mass Capacity UI.\n" +
                "Y position offset from the default location.");
            Instance.MassCapacityUIScale = cfg.Bind("Hud", "MassCapacityUIScale", 0.8f, "Scale multiplier for the Mass Capacity UI.\n" +
                "Size multiplier for the UI element.");

            // Balance configuration bindings
            Instance.EnableBalance = cfg.Bind("Balance", "EnableBalance", false, "Enable balance features (capacity scaling, elite mass bonus, overencumbrance).\n" +
                "When disabled, all balance features are bypassed and vanilla behavior is used.");
            Instance.EnableAoESlamDamage = cfg.Bind("Balance", "EnableAoESlamDamage", false, "When enabled, slam/bluntforce actions damage every object in the bag.\n" +
                "Only active in 'All' calculation mode.\n" +
                "AoEDamageDistribution determines how damage is split among objects.");
            Instance.AoEDamageDistribution = cfg.Bind("Balance", "AoEDamageDistribution", AoEDamageMode.Full, "Mode for AoE damage distribution:\n" +
                "- Full: Each object takes full damage (total damage × object count)\n" +
                "- Split: Damage is divided among objects (total damage ÷ object count)");
            Instance.CapacityScalingMode = cfg.Bind("Balance", "CapacityScalingMode", DrifterBossGrabMod.Balance.CapacityScalingMode.IncreaseCapacity, "Mode for capacity scaling:\n" +
                "- IncreaseCapacity: Increases mass capacity based on utility stocks\n" +
                "- HalveMass: Reduces mass of objects based on utility stocks");
            Instance.CapacityScalingType = cfg.Bind("Balance", "CapacityScalingType", ScalingType.Exponential, "Type of capacity scaling:\n" +
                "- Linear: Capacity increases linearly with utility stocks\n" +
                "- Exponential: Capacity increases exponentially with utility stocks");
            Instance.CapacityScalingBonusPerCapacity = cfg.Bind("Balance", "CapacityScalingBonusPerCapacity", 100.0f, "Bonus mass capacity per utility stock.\n" +
                "Formula: CapacityBonus = UtilityStocks × CapacityScalingBonusPerCapacity\n" +
                "Example: With 2 stocks and 100 bonus, capacity increases by 200");
            Instance.EliteMassBonusPercent = cfg.Bind("Balance", "EliteMassBonusPercent", 0.0f, "Percentage mass bonus for elites.\n" +
                "Formula: EliteMass = BaseMass × (1 + EliteMassBonusPercent / 100)\n" +
                "Example: With 10%, a 100 mass elite becomes 110 mass\n" +
                "0 = disabled");
            Instance.BossMassBonusPercent = cfg.Bind("Balance", "BossMassBonusPercent", 0.0f, "Percentage mass bonus for bosses.\n" +
                "Formula: BossMass = BaseMass × (1 + BossMassBonusPercent / 100)\n" +
                "Example: With 100%, a 100 mass boss becomes 200 mass\n" +
                "0 = disabled");
            Instance.ChampionMassBonusPercent = cfg.Bind("Balance", "ChampionMassBonusPercent", 0.0f, "Percentage mass bonus for champions.\n" +
                "Formula: ChampionMass = BaseMass × (1 + ChampionMassBonusPercent / 100)\n" +
                "Example: With 75%, a 100 mass champion becomes 175 mass\n" +
                "0 = disabled");
            Instance.PlayerMassBonusPercent = cfg.Bind("Balance", "PlayerMassBonusPercent", 0.0f, "Percentage mass bonus for player-controlled entities.\n" +
                "Formula: PlayerMass = BaseMass × (1 + PlayerMassBonusPercent / 100)\n" +
                "Example: With 50%, a 100 mass player becomes 150 mass\n" +
                "0 = disabled");
            Instance.MinionMassBonusPercent = cfg.Bind("Balance", "MinionMassBonusPercent", 0.0f, "Percentage mass bonus for minions.\n" +
                "Formula: MinionMass = BaseMass × (1 + MinionMassBonusPercent / 100)\n" +
                "Example: With 50%, a 100 mass minion becomes 150 mass\n" +
                "0 = disabled");
            Instance.DroneMassBonusPercent = cfg.Bind("Balance", "DroneMassBonusPercent", 0.0f, "Percentage mass bonus for drones.\n" +
                "Formula: DroneMass = BaseMass × (1 + DroneMassBonusPercent / 100)\n" +
                "Example: With -50%, a 100 mass drone becomes 50 mass\n" +
                "Negative values reduce mass, 0 = disabled");
            Instance.MechanicalMassBonusPercent = cfg.Bind("Balance", "MechanicalMassBonusPercent", 0.0f, "Percentage mass bonus for mechanical entities.\n" +
                "Formula: MechanicalMass = BaseMass × (1 + MechanicalMassBonusPercent / 100)\n" +
                "Example: With 50%, a 100 mass mechanical becomes 150 mass\n" +
                "0 = disabled");
            Instance.VoidMassBonusPercent = cfg.Bind("Balance", "VoidMassBonusPercent", 0.0f, "Percentage mass bonus for void entities.\n" +
                "Formula: VoidMass = BaseMass × (1 + VoidMassBonusPercent / 100)\n" +
                "Example: With 50%, a 100 mass void becomes 150 mass\n" +
                "0 = disabled");

            // Risk of Options UI controls (not saved to config file, used for UI only)
            Instance.SelectedFlag = cfg.Bind("Balance", "SelectedFlag", CharacterFlagType.Elite,
                "Select which flag to modify (UI only)");
            Instance.SelectedFlag.Value = CharacterFlagType.Elite; // Set default
            Instance.SelectedFlagMultiplier = cfg.Bind("Balance", "FlagMultiplier", 10.0f,
                "Multiplier for selected flag (UI only)");
            Instance.SelectedFlagMultiplier.Value = Instance.EliteMassBonusPercent.Value; // Initialize with elite value

            // HUD sub-tab selection
            Instance.SelectedHudSubTab = cfg.Bind("Hud", "SelectedHudSubTab", HudSubTabType.All,
                "Select which HUD settings group to view (UI only)");
            Instance.SelectedHudSubTab.Value = HudSubTabType.All; // Set default

            // Balance sub-tab selection
            Instance.SelectedBalanceSubTab = cfg.Bind("Balance", "SelectedBalanceSubTab", BalanceSubTabType.All,
                "Select which Balance settings group to view (UI only)");
            Instance.SelectedBalanceSubTab.Value = BalanceSubTabType.All; // Set default

            // Preset selection
            Instance.SelectedPreset = cfg.Bind("General", "SelectedPreset", PresetType.Intended,
                "Select a preset to apply all settings at once.\n" +
                "- Vanilla: All features disabled, vanilla behavior\n" +
                "- Intended: Boss grab only\n" +
                "- Default: All features in DrifterGrabFeature + bottomless bag and persistence\n" +
                "- Balance: Default + balance features\n" +
                "- Custom: User has modified settings (auto-switched)");

            Instance.EnableOverencumbrance = cfg.Bind("Balance", "EnableOverencumbrance", true, "Enable overencumbrance penalties.\n" +
                "When enabled, exceeding mass capacity incurs additional penalties.");
            Instance.OverencumbranceMaxPercent = cfg.Bind("Balance", "OverencumbranceMaxPercent", 100.0f, "Maximum overencumbrance percentage.\n" +
                "100% = double capacity, 50% = 1.5x capacity.\n" +
                "Formula: MaxMass = Capacity × (1 + OverencumbranceMaxPercent / 100)");
            Instance.UncapCapacity = cfg.Bind("Balance", "UncapCapacity", false, "When enabled, storage is practically infinite (slot count ignored).\n" +
                "Slot count is ignored, only mass matters.\n" +
                "When disabled, both slot and mass capacity are enforced.");
            Instance.ToggleMassCapacity = cfg.Bind("Balance", "ToggleMassCapacity", true, "When disabled, mass capacity is ignored and only slot capacity is used.\n" +
                "When enabled, both slot and mass capacity are enforced.");
            Instance.StateCalculationModeEnabled = cfg.Bind("Balance", "StateCalculationModeEnabled", false, "Enable state calculation mode (Current vs All) in Balance tab.\n" +
                "When enabled, allows choosing between Current and All calculation modes.");
            Instance.StateCalculationMode = cfg.Bind("Balance", "StateCalculationMode", DrifterBossGrabMod.StateCalculationMode.Current, "Mode for calculating bagged object state:\n" +
                "- Current: Only the currently selected object affects Drifter's stats\n" +
                "- All: All bagged objects are aggregated for stat calculation");
            Instance.AllModeMassMultiplier = cfg.Bind("Balance", "AllModeMassMultiplier", 1.0f, "Multiplier for mass calculation in All mode.\n" +
                "Formula: AllModeMass = SumOfAllMasses × AllModeMassMultiplier\n" +
                "Examples:\n" +
                "  1.0 = Full sum of all masses\n" +
                "  0.5 = Half of all masses\n" +
                "  2.0 = Double the sum of all masses");

            Instance.MinMovespeedPenalty = cfg.Bind("Balance", "MinMovespeedPenalty", 0.0f, "Minimum movement speed penalty (as a percentage of base movement speed).\n" +
                "This is the penalty when mass ratio is 0% (empty bag).\n" +
                "Formula: BasePenalty = Lerp(MinMovespeedPenalty, MaxMovespeedPenalty, MassRatio)\n" +
                "Example: 0.0 = no minimum penalty, 0.1 = minimum 10% penalty");
            Instance.MaxMovespeedPenalty = cfg.Bind("Balance", "MaxMovespeedPenalty", 0.5f, "Maximum movement speed penalty (as a percentage of base movement speed).\n" +
                "This is the penalty when mass ratio is 100% (at capacity).\n" +
                "Formula: BasePenalty = Lerp(MinMovespeedPenalty, MaxMovespeedPenalty, MassRatio)\n" +
                "Example: 0.5 = maximum 50% penalty at full capacity");
            Instance.FinalMovespeedPenaltyLimit = cfg.Bind("Balance", "FinalMovespeedPenaltyLimit", 0.8f, "Final limit for movement speed penalty (as a percentage of base movement speed).\n" +
                "This is a hard cap applied AFTER AllModePenaltyMultiplier is applied.\n" +
                "Formula: FinalPenalty = Clamp(BasePenalty × AllModePenaltyMultiplier, Min, FinalMovespeedPenaltyLimit)\n" +
                "Example: With MaxMovespeedPenalty=0.5, AllModePenaltyMultiplier=2.0:\n" +
                "  BasePenalty = 0.5, After Multiplier = 1.0, Clamped to FinalLimit = 0.8");

            // Force EnableCarouselHUD to true if BottomlessBagEnabled is true
            if (Instance.BottomlessBagEnabled.Value && !Instance.EnableCarouselHUD.Value)
            {
                Instance.EnableCarouselHUD.Value = true;
            }

            // Add event handlers for live updates
            Instance.BottomlessBagEnabled.SettingChanged += (sender, args) =>
            {
                // Force EnableCarouselHUD to true when BottomlessBagEnabled is true
                if (Instance.BottomlessBagEnabled.Value && !Instance.EnableCarouselHUD.Value)
                {
                    Instance.EnableCarouselHUD.Value = true;
                }
            };
            Instance.EnableCarouselHUD.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowWeight.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowName.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.BagUIShowHealthBar.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.UseNewWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.WeightDisplayMode.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ScaleWeightColor.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.DamagePreviewColor.SettingChanged += (sender, args) => UpdateDamagePreviewColors();
            Instance.EnableMassCapacityUI.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIPositionX.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIPositionY.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIScale.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();

            // Balance config change handlers
            Instance.CapacityScalingMode.SettingChanged += (sender, args) =>
            {
                // Trigger capacity recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.CapacityScalingType.SettingChanged += (sender, args) =>
            {
                // Trigger capacity recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.CapacityScalingBonusPerCapacity.SettingChanged += (sender, args) =>
            {
                // Trigger capacity recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.BottomlessBagBaseCapacity.SettingChanged += (sender, args) =>
            {
                // Trigger capacity recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.UncapCapacity.SettingChanged += (sender, args) =>
            {
                // Trigger capacity recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.ToggleMassCapacity.SettingChanged += (sender, args) =>
            {
                // Trigger capacity recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            // State calculation mode config change handlers
            Instance.StateCalculationModeEnabled.SettingChanged += (sender, args) =>
            {
                // Trigger state recalculation for all bag controllers when mode is enabled/disabled
                if (PluginConfig.Instance.EnableBalance.Value)
                {
                    foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                    {
                        CapacityScalingSystem.RecalculateMass(bagController);
                        CapacityScalingSystem.RecalculateState(bagController);
                    }
                }
            };

            Instance.StateCalculationMode.SettingChanged += (sender, args) =>
            {
                // Trigger state recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateState(bagController);
                }
            };

            Instance.AllModeMassMultiplier.SettingChanged += (sender, args) =>
            {
                // Trigger mass recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateMass(bagController);
                }
            };



            Instance.MinMovespeedPenalty.SettingChanged += (sender, args) =>
            {
                // Trigger penalty recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculatePenalty(bagController);
                }
            };

            Instance.MaxMovespeedPenalty.SettingChanged += (sender, args) =>
            {
                // Trigger penalty recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculatePenalty(bagController);
                }
            };



            Instance.UncapBagScale.SettingChanged += (sender, args) =>
            {
                // Trigger bag scale recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.UncapMass.SettingChanged += (sender, args) =>
            {
                // Trigger mass recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

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
            EventHandler lockedObjectGrabbingHandler,
            EventHandler projectileGrabbingModeHandler)
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
            Instance.ProjectileGrabbingMode.SettingChanged -= projectileGrabbingModeHandler;
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

        public static ConfigEntry<float> GetFlagMultiplierConfig(CharacterFlagType flag)
        {
            switch (flag)
            {
                case CharacterFlagType.Elite: return Instance.EliteMassBonusPercent;
                case CharacterFlagType.Boss: return Instance.BossMassBonusPercent;
                case CharacterFlagType.Champion: return Instance.ChampionMassBonusPercent;
                case CharacterFlagType.Player: return Instance.PlayerMassBonusPercent;
                case CharacterFlagType.Minion: return Instance.MinionMassBonusPercent;
                case CharacterFlagType.Drone: return Instance.DroneMassBonusPercent;
                case CharacterFlagType.Mechanical: return Instance.MechanicalMassBonusPercent;
                case CharacterFlagType.Void: return Instance.VoidMassBonusPercent;
                default: return Instance.EliteMassBonusPercent;
            }
        }

        public static string GetFlagDisplayName(CharacterFlagType flag)
        {
            switch (flag)
            {
                case CharacterFlagType.Elite: return "Elite";
                case CharacterFlagType.Boss: return "Boss";
                case CharacterFlagType.Champion: return "Champion";
                case CharacterFlagType.Player: return "Player";
                case CharacterFlagType.Minion: return "Minion";
                case CharacterFlagType.Drone: return "Drone";
                case CharacterFlagType.Mechanical: return "Mechanical";
                case CharacterFlagType.Void: return "Void";
                default: return "Elite";
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

        private static void UpdateMassCapacityUIToggles()
        {
            var massCapacityUIControllers = UnityEngine.Object.FindObjectsByType<UI.MassCapacityUIController>(FindObjectsSortMode.None);
            foreach (var massCapacityUI in massCapacityUIControllers)
            {
                massCapacityUI.UpdateConfig();
            }
        }

        private static void UpdateDamagePreviewColors()
        {
            var overlays = UnityEngine.Object.FindObjectsByType<UI.DamagePreviewOverlay>(FindObjectsSortMode.None);
            foreach (var overlay in overlays)
            {
                overlay.UpdateColor();
            }
        }

        private static UnityEngine.Transform? FindDeepChild(UnityEngine.Transform parent, string name)
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
