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
        None = 0,
        Full = 1,
        Split = 2
    }

    public enum CharacterFlagType
    {
        All,
        Elite,
        Boss,
        Champion,
        Player,
        Minion,
        Drone,
        Mechanical,
        Void
    }

    public enum HudElementType
    {
        All,
        SelectedSlot,
        AdjacentSlot,
        DamagePreview,
        CapacityUI,
        BaggedObjectInfo
    }

    public enum BalanceSubTabType
    {
        All,
        Capacity,
        TagScaling,
        Penalty,
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
    // Dummy enum for RiskOfOptions ChoiceOption
    public enum ComponentChooserDummy { SelectToToggle }
    public enum ComponentChooserSortMode { ByFrequency, ByProximity }

    public class PluginConfig
    {
        private static PluginConfig _instance = null!;
        public static PluginConfig Instance => _instance ??= new PluginConfig();

        // General
        public ConfigEntry<bool> EnableBossGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableNPCGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableEnvironmentGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableLockedObjectGrabbing { get; private set; } = null!;
        public ConfigEntry<ProjectileGrabbingMode> ProjectileGrabbingMode { get; private set; } = null!;
        public ConfigEntry<string> BodyBlacklist { get; private set; } = null!;
        public ConfigEntry<string> RecoveryObjectBlacklist { get; private set; } = null!;
        public ConfigEntry<string> GrabbableComponentTypes { get; private set; } = null!;
        public ConfigEntry<string> GrabbableKeywordBlacklist { get; private set; } = null!;
        public ConfigEntry<bool> EnableDebugLogs { get; private set; } = null!;
        public ConfigEntry<ComponentChooserSortMode> ComponentChooserSortModeEntry { get; private set; } = null!;
        public ConfigEntry<ComponentChooserDummy> ComponentChooserDummyEntry { get; private set; } = null!;
        public ConfigEntry<bool> EnableConfigSync { get; private set; } = null!;
        public ConfigEntry<PresetType> SelectedPreset { get; private set; } = null!;
        public ConfigEntry<PresetType> LastSelectedPreset { get; private set; } = null!;

        // Bottomless Bag
        public ConfigEntry<bool> BottomlessBagEnabled { get; private set; } = null!;
        public ConfigEntry<string> AddedCapacity { get; private set; } = null!;
        public ConfigEntry<bool> EnableStockRefreshClamping { get; private set; } = null!;
        public ConfigEntry<float> CycleCooldown { get; private set; } = null!;
        public ConfigEntry<bool> PlayAnimationOnCycle { get; private set; } = null!;
        public ConfigEntry<bool> EnableMouseWheelScrolling { get; private set; } = null!;
        public ConfigEntry<bool> InverseMouseWheelScrolling { get; private set; } = null!;
        public ConfigEntry<bool> AutoPromoteMainSeat { get; private set; } = null!;
        public ConfigEntry<bool> PrioritizeMainSeat { get; private set; } = null!;

        // Persistence
        public ConfigEntry<bool> EnableObjectPersistence { get; private set; } = null!;
        public ConfigEntry<bool> EnableAutoGrab { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedBosses { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedNPCs { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedEnvironmentObjects { get; private set; } = null!;
        public ConfigEntry<string> PersistenceBlacklist { get; private set; } = null!;
        public ConfigEntry<float> AutoGrabDelay { get; private set; } = null!;

        // HUD
        public ConfigEntry<bool> EnableCarouselHUD { get; private set; } = null!;
        public ConfigEntry<float> CarouselSpacing { get; private set; } = null!;
        public ConfigEntry<float> CarouselAnimationDuration { get; private set; } = null!;

        // HUD - Slot UI Controls
        public ConfigEntry<HudElementType> SelectedHudElement { get; private set; } = null!;
        public ConfigEntry<float> CenterSlotX { get; private set; } = null!;
        public ConfigEntry<float> CenterSlotY { get; private set; } = null!;
        public ConfigEntry<float> CenterSlotScale { get; private set; } = null!;
        public ConfigEntry<float> CenterSlotOpacity { get; private set; } = null!;
        public ConfigEntry<bool> CenterSlotShowIcon { get; private set; } = null!;
        public ConfigEntry<bool> CenterSlotShowWeightIcon { get; private set; } = null!;
        public ConfigEntry<bool> CenterSlotShowName { get; private set; } = null!;
        public ConfigEntry<bool> CenterSlotShowHealthBar { get; private set; } = null!;
        public ConfigEntry<bool> CenterSlotShowSlotNumber { get; private set; } = null!;
        public ConfigEntry<float> SideSlotX { get; private set; } = null!;
        public ConfigEntry<float> SideSlotY { get; private set; } = null!;
        public ConfigEntry<float> SideSlotScale { get; private set; } = null!;
        public ConfigEntry<float> SideSlotOpacity { get; private set; } = null!;
        public ConfigEntry<bool> SideSlotShowIcon { get; private set; } = null!;
        public ConfigEntry<bool> SideSlotShowWeightIcon { get; private set; } = null!;
        public ConfigEntry<bool> SideSlotShowName { get; private set; } = null!;
        public ConfigEntry<bool> SideSlotShowHealthBar { get; private set; } = null!;
        public ConfigEntry<bool> SideSlotShowSlotNumber { get; private set; } = null!;

        // HUD - Bag Info UI
        public ConfigEntry<bool> EnableBaggedObjectInfo { get; private set; } = null!;
        public ConfigEntry<float> BaggedObjectInfoX { get; private set; } = null!;
        public ConfigEntry<float> BaggedObjectInfoY { get; private set; } = null!;
        public ConfigEntry<float> BaggedObjectInfoScale { get; private set; } = null!;
        public ConfigEntry<Color> BaggedObjectInfoColor { get; private set; } = null!;

        // HUD - Visual Styling
        public ConfigEntry<bool> UseNewWeightIcon { get; private set; } = null!;
        public ConfigEntry<WeightDisplayMode> WeightDisplayMode { get; private set; } = null!;
        public ConfigEntry<bool> ScaleWeightColor { get; private set; } = null!;
        public ConfigEntry<bool> ShowTotalMassOnWeightIcon { get; private set; } = null!;
        public ConfigEntry<bool> EnableDamagePreview { get; private set; } = null!;
        public ConfigEntry<Color> DamagePreviewColor { get; private set; } = null!;
        public ConfigEntry<bool> EnableMassCapacityUI { get; private set; } = null!;
        public ConfigEntry<float> MassCapacityUIPositionX { get; private set; } = null!;
        public ConfigEntry<float> MassCapacityUIPositionY { get; private set; } = null!;
        public ConfigEntry<float> MassCapacityUIScale { get; private set; } = null!;
        public ConfigEntry<bool> EnableSeparators { get; private set; } = null!;
        public ConfigEntry<float> GradientIntensity { get; private set; } = null!;
        public ConfigEntry<Color> CapacityGradientColorStart { get; private set; } = null!;
        public ConfigEntry<Color> CapacityGradientColorMid { get; private set; } = null!;
        public ConfigEntry<Color> CapacityGradientColorEnd { get; private set; } = null!;
        public ConfigEntry<Color> OverencumbranceGradientColorStart { get; private set; } = null!;
        public ConfigEntry<Color> OverencumbranceGradientColorMid { get; private set; } = null!;
        public ConfigEntry<Color> OverencumbranceGradientColorEnd { get; private set; } = null!;

        // Balance
        public ConfigEntry<bool> EnableBalance { get; private set; } = null!;
        public ConfigEntry<AoEDamageMode> AoEDamageDistribution { get; private set; } = null!;
        public ConfigEntry<float> BreakoutTimeMultiplier { get; private set; } = null!;
        public ConfigEntry<int> MaxSmacks { get; private set; } = null!;
        public ConfigEntry<string> BagScaleCap { get; private set; } = null!;
        public ConfigEntry<string> MassCap { get; private set; } = null!;

        // Balance - Mathematics & State Tracking
        public ConfigEntry<StateCalculationMode> StateCalculationMode { get; private set; } = null!;
        public ConfigEntry<float> OverencumbranceMax { get; private set; } = null!;
        public ConfigEntry<string> SlotScalingFormula { get; private set; } = null!;
        public ConfigEntry<string> MassCapacityFormula { get; private set; } = null!;
        public ConfigEntry<string> MovespeedPenaltyFormula { get; private set; } = null!;

        // Balance - Flag Multipliers
        public ConfigEntry<string> EliteFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> BossFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> ChampionFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> PlayerFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> MinionFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> DroneFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> MechanicalFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> VoidFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<string> AllFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<CharacterFlagType> SelectedFlag { get; private set; } = null!;
        public ConfigEntry<string> SelectedFlagMultiplier { get; private set; } = null!;
        public ConfigEntry<BalanceSubTabType> SelectedBalanceSubTab { get; private set; } = null!;
        // Mapping of setting tokens to HUD sub-tabs
        public static readonly Dictionary<string, HudElementType[]> HudSettingToSubTab = new()
        {
            // Carousel settings (shared)
            // SelectedHudElement dropdown should always be visible regardless of selected sub-tab
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SELECTEDHUDELEMENT.CHOICE"] = new[] { 
                HudElementType.All, 
                HudElementType.SelectedSlot, 
                HudElementType.AdjacentSlot, 
                HudElementType.DamagePreview, 
                HudElementType.CapacityUI, 
                HudElementType.BaggedObjectInfo 
            },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLECAROUSELHUD.CHECKBOX"] = new[] { HudElementType.SelectedSlot, HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELSPACING.FLOAT_FIELD"] = new[] { HudElementType.SelectedSlot, HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAROUSELANIMATIONDURATION.FLOAT_FIELD"] = new[] { HudElementType.SelectedSlot, HudElementType.AdjacentSlot },

            // CenterSlot settings (Selected)
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTX.FLOAT_FIELD"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTY.FLOAT_FIELD"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTSCALE.FLOAT_FIELD"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTOPACITY.FLOAT_FIELD"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTSHOWICON.CHECKBOX"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTSHOWWEIGHTICON.CHECKBOX"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTSHOWNAME.CHECKBOX"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTSHOWHEALTHBAR.CHECKBOX"] = new[] { HudElementType.SelectedSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CENTERSLOTSHOWSLOTNUMBER.CHECKBOX"] = new[] { HudElementType.SelectedSlot },

            // SideSlot settings (Adjacent)
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTX.FLOAT_FIELD"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTY.FLOAT_FIELD"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTSCALE.FLOAT_FIELD"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTOPACITY.FLOAT_FIELD"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTSHOWICON.CHECKBOX"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTSHOWWEIGHTICON.CHECKBOX"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTSHOWNAME.CHECKBOX"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTSHOWHEALTHBAR.CHECKBOX"] = new[] { HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SIDESLOTSHOWSLOTNUMBER.CHECKBOX"] = new[] { HudElementType.AdjacentSlot },

            // Weight icon and display settings
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.USENEWWEIGHTICON.CHECKBOX"] = new[] { HudElementType.SelectedSlot, HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.WEIGHTDISPLAYMODE.CHOICE"] = new[] { HudElementType.SelectedSlot, HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SCALEWEIGHTCOLOR.CHECKBOX"] = new[] { HudElementType.SelectedSlot, HudElementType.AdjacentSlot },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.SHOWTOTALMASSONWEIGHTICON.CHECKBOX"] = new[] { HudElementType.SelectedSlot },

            // Capacity UI settings
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLEMASSCAPACITYUI.CHECKBOX"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.MASSCAPACITYUIPOSITIONX.FLOAT_FIELD"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.MASSCAPACITYUIPOSITIONY.FLOAT_FIELD"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.MASSCAPACITYUISCALE.FLOAT_FIELD"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLESEPARATORS.CHECKBOX"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.GRADIENTINTENSITY.STEP_SLIDER"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAPACITYGRADIENTCOLORSTART.COLOR"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAPACITYGRADIENTCOLORMID.COLOR"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.CAPACITYGRADIENTCOLOREND.COLOR"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.OVERENCUMBRANCEGRADIENTCOLORSTART.COLOR"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.OVERENCUMBRANCEGRADIENTCOLORMID.COLOR"] = new[] { HudElementType.CapacityUI },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.OVERENCUMBRANCEGRADIENTCOLOREND.COLOR"] = new[] { HudElementType.CapacityUI },

            // Damage Preview settings (only these two)
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLEDAMAGEPREVIEW.CHECKBOX"] = new[] { HudElementType.DamagePreview },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.DAMAGEPREVIEWCOLOR.COLOR"] = new[] { HudElementType.DamagePreview },

            // Bagged Object Info settings
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLEBAGGEDOBJECTINFO.CHECKBOX"] = new[] { HudElementType.BaggedObjectInfo },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGGEDOBJECTINFOX.FLOAT_FIELD"] = new[] { HudElementType.BaggedObjectInfo },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGGEDOBJECTINFOY.FLOAT_FIELD"] = new[] { HudElementType.BaggedObjectInfo },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGGEDOBJECTINFOSCALE.FLOAT_FIELD"] = new[] { HudElementType.BaggedObjectInfo },
            ["PWDCAT.DRIFTERBOSSGRAB.HUD.BAGGEDOBJECTINFOCOLOR.COLOR"] = new[] { HudElementType.BaggedObjectInfo }
        };

        // Mapping of setting tokens to Balance sub-tabs
        public static readonly Dictionary<string, BalanceSubTabType> BalanceSettingToSubTab = new()
        {
            // Capacity settings
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.ENABLEBALANCE.CHECKBOX"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.UNCAPCAPACITY.CHECKBOX"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.SLOTSCALINGFORMULA.STRING_INPUT_FIELD"] = BalanceSubTabType.Capacity,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MASSCAPACITYFORMULA.STRING_INPUT_FIELD"] = BalanceSubTabType.Capacity,

            // Tag Scaling settings (only UI controls, not individual multipliers)
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.SELECTEDFLAG.CHOICE"] = BalanceSubTabType.TagScaling,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.FLAGMULTIPLIER.STRING_INPUT_FIELD"] = BalanceSubTabType.TagScaling,
            ["PWDCAT.DRIFTERBOSSGRAB.CHARACTER_FLAGS.ALL_FLAG_MULTIPLIER.STRING_INPUT_FIELD"] = BalanceSubTabType.TagScaling,

            // Penalty settings
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.OVERENCUMBRANCEMAX.FLOAT_FIELD"] = BalanceSubTabType.Penalty,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.STATECALCULATIONMODE.CHOICE"] = BalanceSubTabType.Penalty,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MASSMULTIPLIERFORMULA.STRING_INPUT_FIELD"] = BalanceSubTabType.Penalty,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MOVESPEEDPENALTYFORMULA.STRING_INPUT_FIELD"] = BalanceSubTabType.Penalty,

            // Other settings (includes individual mass multipliers and other settings)

            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.BAGSCALECAP.STRING_INPUT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MASSCAP.STRING_INPUT_FIELD"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.AOEDAMAGEDISTRIBUTION.CHOICE"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.BREAKOUTTIMEMULTIPLIER.STEP_SLIDER"] = BalanceSubTabType.Other,
            ["PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAXSMACKS.INT_SLIDER"] = BalanceSubTabType.Other
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
            Instance.BreakoutTimeMultiplier = cfg.Bind("Balance", "BreakoutTimeMultiplier", 1.0f, "Multiplier for how long bagged enemies take to break out.\nFormula: FinalBreakoutTime = BaseBreakoutTime × BreakoutTimeMultiplier");
            Instance.MaxSmacks = cfg.Bind("Balance", "MaxSmacks", 3, new ConfigDescription("Maximum number of hits before bagged enemies break out.\nBagged enemies will break out after receiving this many hits.", new AcceptableValueRange<int>(1, 100)));
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
            Instance.ComponentChooserSortModeEntry = cfg.Bind("General", "ComponentChooserSortMode", ComponentChooserSortMode.ByFrequency,
                "How to sort the components when clicking the Component Chooser.\n" +
                "ByFrequency: Sorts by how many times the component appears in the scene.\n" +
                "ByProximity: Sorts by how close the component's GameObject is to the player's camera.");
            Instance.ComponentChooserDummyEntry = cfg.Bind("General", "ComponentChooserDummy", ComponentChooserDummy.SelectToToggle,
                "Dummy setting for the Component Chooser UI.");
            Instance.EnableConfigSync = cfg.Bind("General", "EnableConfigSync", true,
                "Enable synchronization of configuration settings from host to new clients.\n" +
                "When enabled, clients joining a game will receive the host's configuration settings.");
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
                "Formula: TotalCapacity = UtilityMaxStocks + AddedCapacity + (UtilityMaxStocks × CapacityScalingBonus)");
            Instance.AddedCapacity = cfg.Bind("Bottomless Bag", "AddedCapacity", "0", "Added capacity for bottomless bag, added to utility max stocks. Use 'INF' for infinity.\n" +
                "Formula: TotalCapacity = UtilityMaxStocks + AddedCapacity + CapacityScalingBonus");
            Instance.EnableStockRefreshClamping = cfg.Bind("Bottomless Bag", "EnableStockRefreshClamping", false, "When enabled, Repossess stock refresh is clamped to max stocks minus number of bagged items.\n" +
                "Formula: RefreshedStocks = MaxStocks - BaggedItemCount\n" +
                "Prevents refreshing more stocks than available slots.");
            Instance.CycleCooldown = cfg.Bind("Bottomless Bag", "CycleCooldown", 0.2f, "Cooldown between passenger cycles (seconds).\n" +
                "Minimum time between scroll wheel cycles to prevent rapid switching.");
            Instance.PlayAnimationOnCycle = cfg.Bind("Bottomless Bag", "PlayAnimationOnCycle", false, "When enabled, plays the bag grab animation when cycling to a new passenger.\n" +
                "Disabled by default to reduce visual noise when cycling.");
            Instance.EnableMouseWheelScrolling = cfg.Bind("Bottomless Bag", "EnableMouseWheelScrolling", true, "Enable mouse wheel scrolling for cycling passengers.\n" +
                "When enabled, mouse wheel can be used to cycle through bagged objects.");
            Instance.InverseMouseWheelScrolling = cfg.Bind("Bottomless Bag", "InverseMouseWheelScrolling", false, "Invert the mouse wheel scrolling direction.\n" +
                "When enabled, scrolling up goes to previous object, down goes to next.");

            Instance.EnableCarouselHUD = cfg.Bind("Hud", "EnableCarouselHUD", false, "Enable the custom Carousel HUD for Drifter's bag.\n" +
                "When disabled, reverts to vanilla UI behavior.\n" +
                "Note: Automatically enabled when BottomlessBag is enabled.");
            Instance.CarouselSpacing = cfg.Bind("Hud", "CarouselSpacing", 45.0f, "Vertical spacing for carousel items.\n" +
                "Distance between adjacent items in the carousel.");
            Instance.CarouselAnimationDuration = cfg.Bind("Hud", "CarouselAnimationDuration", 0.4f, "Duration of carousel animation in seconds.\n" +
                "Time for carousel items to animate into position when cycling.");

            // Per-slot backing configs (CenterSlot)
            Instance.CenterSlotX = cfg.Bind("Hud", "CenterSlotX", 25.0f, "X position offset for center slot.");
            Instance.CenterSlotY = cfg.Bind("Hud", "CenterSlotY", 50.0f, "Y position offset for center slot.");
            Instance.CenterSlotScale = cfg.Bind("Hud", "CenterSlotScale", 1.0f, "Scale for center slot (0.0 to 2.0).");
            Instance.CenterSlotOpacity = cfg.Bind("Hud", "CenterSlotOpacity", 1.0f, "Opacity for center slot (0.0 to 1.0).");
            Instance.CenterSlotShowIcon = cfg.Bind("Hud", "CenterSlotShowIcon", true, "Show icon in center slot.");
            Instance.CenterSlotShowWeightIcon = cfg.Bind("Hud", "CenterSlotShowWeightIcon", true, "Show weight icon in center slot.");
            Instance.CenterSlotShowName = cfg.Bind("Hud", "CenterSlotShowName", true, "Show name in center slot.");
            Instance.CenterSlotShowHealthBar = cfg.Bind("Hud", "CenterSlotShowHealthBar", true, "Show health bar in center slot.");
            Instance.CenterSlotShowSlotNumber = cfg.Bind("Hud", "CenterSlotShowSlotNumber", true, "Show slot number in center slot.");

            // Per-slot backing configs (SideSlot)
            Instance.SideSlotX = cfg.Bind("Hud", "SideSlotX", 20.0f, "X position offset for side slots.");
            Instance.SideSlotY = cfg.Bind("Hud", "SideSlotY", 5.0f, "Y position offset for side slots.");
            Instance.SideSlotScale = cfg.Bind("Hud", "SideSlotScale", 0.8f, "Scale for side slots (0.0 to 2.0).");
            Instance.SideSlotOpacity = cfg.Bind("Hud", "SideSlotOpacity", 0.3f, "Opacity for side slots (0.0 to 1.0).");
            Instance.SideSlotShowIcon = cfg.Bind("Hud", "SideSlotShowIcon", true, "Show icon in side slots.");
            Instance.SideSlotShowWeightIcon = cfg.Bind("Hud", "SideSlotShowWeightIcon", true, "Show weight icon in side slots.");
            Instance.SideSlotShowName = cfg.Bind("Hud", "SideSlotShowName", true, "Show name in side slots.");
            Instance.SideSlotShowHealthBar = cfg.Bind("Hud", "SideSlotShowHealthBar", true, "Show health bar in side slots.");
            Instance.SideSlotShowSlotNumber = cfg.Bind("Hud", "SideSlotShowSlotNumber", true, "Show slot number in side slots.");

            // HUD element selector and configs
            Instance.SelectedHudElement = cfg.Bind("Hud", "SelectedHudElement", HudElementType.All,
                "Select which HUD element group to configure (UI only).\n" +
                "- SelectedSlot: Center slot settings\n" +
                "- AdjacentSlot: Side slot settings");
            Instance.SelectedHudElement.Value = HudElementType.All;

            Instance.EnableBaggedObjectInfo = cfg.Bind("Hud", "EnableBaggedObjectInfo", true, "Enable the Bagged Object Info stats panel on the Info Screen (Tab).");
            Instance.BaggedObjectInfoX = cfg.Bind("Hud", "BaggedObjectInfoX", 20.0f, "X position offset for the Bagged Object Info panel.");
            Instance.BaggedObjectInfoY = cfg.Bind("Hud", "BaggedObjectInfoY", -200.0f, "Y position offset for the Bagged Object Info panel.");
            Instance.BaggedObjectInfoScale = cfg.Bind("Hud", "BaggedObjectInfoScale", 1.0f, "Scale value for the Bagged Object Info panel.");
            Instance.BaggedObjectInfoColor = cfg.Bind("Hud", "BaggedObjectInfoColor", new Color(1f, 1f, 1f, 0.9f), "Main text color for the Bagged Object Info panel.");
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
            Instance.ScaleWeightColor = cfg.Bind("Hud", "ScaleWeightColor", true, "Scale the weight icon color based on capacity usage.\n" +
                "When enabled, weight icon color uses the capacity gradient (green to red) based on mass/capacity ratio.");
            Instance.ShowTotalMassOnWeightIcon = cfg.Bind("Hud", "ShowTotalMassOnWeightIcon", false, "Show total bag mass on the center slot weight icon.\n" +
                "When enabled, the center slot displays total bag mass instead of the selected object's mass.\n" +
                "The icon color uses the overencumbrance gradient based on capacity percentage.");
            Instance.AutoPromoteMainSeat = cfg.Bind("Bottomless Bag", "AutoPromoteMainSeat", true, "Automatically promote the next object in the bag to the main seat when the current main object is removed.\n" +
                "When enabled, cycling through the bag automatically updates the main seat.");
            Instance.PrioritizeMainSeat = cfg.Bind("Bottomless Bag", "PrioritizeMainSeat", false, "When enabled, newly grabbed objects are placed in the main seat first instead of additional seats.\n" +
                "When disabled (default), new objects go directly to additional seats.");
            Instance.BagScaleCap = cfg.Bind("Balance", "BagScaleCap", "1", "Bag visual size cap. Set to 'INF' or 'Infinity' to uncap completely, continuing to grow based on mass.\n" +
                "Formula: BagScale = 0.5 + 0.5 × ((Mass - 1) / (MaxMass - 1))");
            Instance.MassCap = cfg.Bind("Balance", "MassCap", "700", "Mass cap for caught entities. Set to 'INF' or 'Infinity' to remove the mass cap.");
            Instance.EnableMassCapacityUI = cfg.Bind("Hud", "EnableMassCapacityUI", false, "Enable the Mass Capacity UI for displaying bag capacity status.\n" +
                "Shows current mass vs capacity as a progress bar.");
            Instance.MassCapacityUIPositionX = cfg.Bind("Hud", "MassCapacityUIPositionX", -20.0f, "Horizontal position offset for the Mass Capacity UI.\n" +
                "X position offset from the default location.");
            Instance.MassCapacityUIPositionY = cfg.Bind("Hud", "MassCapacityUIPositionY", 0.0f, "Vertical position offset for the Mass Capacity UI.\n" +
                "Y position offset from the default location.");
            Instance.MassCapacityUIScale = cfg.Bind("Hud", "MassCapacityUIScale", 0.8f, "Scale multiplier for the Mass Capacity UI.\n" +
                "Size multiplier for the UI element.");
            Instance.EnableSeparators = cfg.Bind("Hud", "EnableSeparators", true, "Enable dynamic separators (threshold pips) on the Mass Capacity UI.\n" +
                "Shows boundaries for each slot, or dynamically based on mass.");
            Instance.GradientIntensity = cfg.Bind("Hud", "GradientIntensity", 1.0f, "Intensity of the gradient color on the Mass Capacity UI.\n" +
                "0.0 is no gradient (solid mid color), 1.0 is full intensity.");

            Instance.CapacityGradientColorStart = cfg.Bind("Hud", "CapacityGradientColorStart", new Color(0.0f, 1.0f, 0.0f, 1.0f), "Start color (low mass) for standard capacity gradient.");
            Instance.CapacityGradientColorMid = cfg.Bind("Hud", "CapacityGradientColorMid", new Color(1.0f, 1.0f, 0.0f, 1.0f), "Mid color (medium mass) for standard capacity gradient.");
            Instance.CapacityGradientColorEnd = cfg.Bind("Hud", "CapacityGradientColorEnd", new Color(1.0f, 0.0f, 0.0f, 1.0f), "End color (high mass) for standard capacity gradient.");

            Instance.OverencumbranceGradientColorStart = cfg.Bind("Hud", "OverencumbranceGradientColorStart", new Color(0f, 1.0f, 1.0f, 1.0f), "Start color (low encumbrance) for overencumbrance gradient.");
            Instance.OverencumbranceGradientColorMid = cfg.Bind("Hud", "OverencumbranceGradientColorMid", new Color(0.0f, 0.0f, 1.0f, 1.0f), "Mid color (medium encumbrance) for overencumbrance gradient.");
            Instance.OverencumbranceGradientColorEnd = cfg.Bind("Hud", "OverencumbranceGradientColorEnd", new Color(0.0f, 0.0f, 1.0f, 1.0f), "End color (high encumbrance) for overencumbrance gradient.");

            // Balance configuration bindings
            Instance.EnableBalance = cfg.Bind("Balance", "EnableBalance", false, "Enable balance features (capacity scaling, elite mass bonus, overencumbrance).\n" +
                "When disabled, all balance features are bypassed and vanilla behavior is used.");
            Instance.AoEDamageDistribution = cfg.Bind("Balance", "AoEDamageDistribution", AoEDamageMode.Full, "Mode for AoE damage distribution:\n" +
                "- None: AoE slam damage disabled\n" +
                "- Full: Each object takes full damage (total damage × object count)\n" +
                "- Split: Damage is divided among objects (total damage ÷ object count)");
            Instance.SlotScalingFormula = cfg.Bind("Balance", "SlotScalingFormula", "0",
                "Formula for extra bag slots. Result is auto-floored to an integer.\n" +
                "Variables: H = max health, L = level, C = utility stocks, MC = mass cap, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: 'H/100 + L' = 1 slot per 100 HP + 1 per level, 'floor(H/200) + floor(L/3)' = discrete steps\n" +
                "Set to '0' to disable extra slot scaling.");

            Instance.MassCapacityFormula = cfg.Bind("Balance", "MassCapacityFormula", "C * MC",
                "Formula for mass capacity limit.\n" +
                "Variables: H = max health, L = level, C = utility stocks, MC = mass cap, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: 'C * MC' = linear 100 per stock, 'MC * 1.5^(C-1)' = exponential\n" +
                "Use 'INF' for unlimited mass capacity.");



            // Formula-based flag multiplier configurations (one per flag type)
            Instance.EliteFlagMultiplier = cfg.Bind("Balance", "EliteFlagMultiplier", "1",
                "Formula-based mass multiplier for Elite entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.EliteFlagMultiplier.Value = "1"; // Initialize with default

            Instance.BossFlagMultiplier = cfg.Bind("Balance", "BossFlagMultiplier", "1",
                "Formula-based mass multiplier for Boss entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.BossFlagMultiplier.Value = "1"; // Initialize with default

            Instance.ChampionFlagMultiplier = cfg.Bind("Balance", "ChampionFlagMultiplier", "1",
                "Formula-based mass multiplier for Champion entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.ChampionFlagMultiplier.Value = "1"; // Initialize with default

            Instance.PlayerFlagMultiplier = cfg.Bind("Balance", "PlayerFlagMultiplier", "1",
                "Formula-based mass multiplier for Player-controlled entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.PlayerFlagMultiplier.Value = "1"; // Initialize with default

            Instance.MinionFlagMultiplier = cfg.Bind("Balance", "MinionFlagMultiplier", "1",
                "Formula-based mass multiplier for Minion entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.MinionFlagMultiplier.Value = "1"; // Initialize with default

            Instance.DroneFlagMultiplier = cfg.Bind("Balance", "DroneFlagMultiplier", "1",
                "Formula-based mass multiplier for Drone entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.DroneFlagMultiplier.Value = "1"; // Initialize with default

            Instance.MechanicalFlagMultiplier = cfg.Bind("Balance", "MechanicalFlagMultiplier", "1",
                "Formula-based mass multiplier for Mechanical entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.MechanicalFlagMultiplier.Value = "1"; // Initialize with default

            Instance.VoidFlagMultiplier = cfg.Bind("Balance", "VoidFlagMultiplier", "1",
                "Formula-based mass multiplier for Void entities.\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.VoidFlagMultiplier.Value = "1"; // Initialize with default

            Instance.AllFlagMultiplier = cfg.Bind(
                new ConfigDefinition("Character Flags", "All Flag Multiplier"),
                "1",
                new ConfigDescription(
                    "Universal multiplier that applies to ALL enemies. Stacks with specific flags.\n" +
                    "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                    "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                    "Constants: pi, e, INF\n" +
                    "Examples: '1' = no change, 'BH/max(B,1)' = base health becomes mass, 'H/max(B,1)' = scaled health becomes mass"
                )
            );

            // Risk of Options UI controls (not saved to config file, used for UI only)
            Instance.SelectedFlag = cfg.Bind("Balance", "SelectedFlag", CharacterFlagType.Elite,
                "Select which flag to modify (UI only)");
            Instance.SelectedFlag.Value = CharacterFlagType.Elite; // Set default
            Instance.SelectedFlagMultiplier = cfg.Bind("Balance", "FlagMultiplier", "1",
                "Mass Multiplier for selected flag (UI only).\n" +
                                "Variables: H = max health (scaled), BH = base max health (unscaled), L = level, B = base mass, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '1.5' = 1.5x mass, 'H/1000' = 0.1% of max health, 'H/max(B,1)' = Health becomes mass");
            Instance.SelectedFlagMultiplier.Value = "1"; // Initialize with default



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

            Instance.LastSelectedPreset = cfg.Bind("Hidden", "LastSelectedPreset", PresetType.Intended,
                "Internal tracker of the last applied preset");

            Instance.OverencumbranceMax = cfg.Bind("Balance", "OverencumbranceMax", 100.0f, "Maximum overencumbrance percentage.\n" +
                "0 = no overencumbrance allowed, 100% = double capacity, 50% = 1.5x capacity.\n" +
                "Formula: MaxMass = Capacity × (1 + OverencumbranceMax / 100)");
            Instance.StateCalculationMode = cfg.Bind("Balance", "StateCalculationMode", DrifterBossGrabMod.StateCalculationMode.Current, "Mode for calculating bagged object state:\n" +
                "- Current: Only the currently selected object affects Drifter's stats\n" +
                "- All: All bagged objects are aggregated for stat calculation");

            Instance.MovespeedPenaltyFormula = cfg.Bind("Balance", "MovespeedPenaltyFormula", "0",
                "Formula for movement speed penalty.\n" +
                "Variables: T = Total mass, M = Mass capacity, C = Bag capacity (slots), H = Max health, L = Level, MC = mass cap, S = current stage\n" +
                "Functions: floor, ceil, round, min, max, abs, sqrt, log, ln, clamp, sin, cos, pow\n" +
                "Constants: pi, e, INF\n" +
                "Examples: '0' = no penalty, 'clamp((T/M) * 0.5, 0, 0.8)' = 50% at full capacity, capped at 80%\n" +
                "Set to '0' for no penalty.");

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
            // Per-slot backing config change handlers (trigger UI updates)
            Instance.CenterSlotShowIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowName.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowHealthBar.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.CenterSlotShowSlotNumber.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowName.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowHealthBar.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.SideSlotShowSlotNumber.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.UseNewWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.WeightDisplayMode.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ScaleWeightColor.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.ShowTotalMassOnWeightIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
            Instance.DamagePreviewColor.SettingChanged += (sender, args) => UpdateDamagePreviewColors();
            Instance.EnableMassCapacityUI.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIPositionX.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIPositionY.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.MassCapacityUIScale.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.EnableSeparators.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.GradientIntensity.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.CapacityGradientColorStart.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.CapacityGradientColorMid.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.CapacityGradientColorEnd.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.OverencumbranceGradientColorStart.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.OverencumbranceGradientColorMid.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();
            Instance.OverencumbranceGradientColorEnd.SettingChanged += (sender, args) => UpdateMassCapacityUIToggles();

            // Balance config change handlers
            Instance.SlotScalingFormula.SettingChanged += (sender, args) =>
            {
                // Validate and trigger capacity recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.SlotScalingFormula.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid SlotScalingFormula: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                    CapacityScalingSystem.RecalculateState(bagController);
                }
            };

            Instance.MassCapacityFormula.SettingChanged += (sender, args) =>
            {
                // Validate and trigger capacity recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.MassCapacityFormula.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MassCapacityFormula: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.AddedCapacity.SettingChanged += (sender, args) =>
            {
                // Trigger capacity recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
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


            Instance.MovespeedPenaltyFormula.SettingChanged += (sender, args) =>
            {
                // Validate and trigger penalty recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.MovespeedPenaltyFormula.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MovespeedPenaltyFormula: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculatePenalty(bagController);
                }
            };

            Instance.BagScaleCap.SettingChanged += (sender, args) =>
            {
                // Trigger bag scale recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.MassCap.SettingChanged += (sender, args) =>
            {
                // Trigger mass recalculation for all bag controllers
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            // Flag multiplier formula change handlers
            Instance.EliteFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.EliteFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid EliteFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.BossFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.BossFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid BossFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.ChampionFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.ChampionFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid ChampionFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.PlayerFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.PlayerFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid PlayerFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.MinionFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.MinionFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MinionFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.DroneFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.DroneFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid DroneFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.MechanicalFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.MechanicalFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid MechanicalFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.VoidFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate and trigger mass recalculation for all bag controllers
                var error = FormulaParser.Validate(Instance.VoidFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid VoidFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            // SelectedFlagMultiplier change handler - syncs to the appropriate flag's formula config
            Instance.SelectedFlagMultiplier.SettingChanged += (sender, args) =>
            {
                // Validate the formula
                var error = FormulaParser.Validate(Instance.SelectedFlagMultiplier.Value);
                if (error != null)
                {
                    Log.Warning($"[PluginConfig] Invalid FlagMultiplier formula: {error}");
                    return;
                }

                // Update the appropriate flag's formula config based on the selected flag
                var selectedFlag = Instance.SelectedFlag.Value;
                string newFormula = Instance.SelectedFlagMultiplier.Value;

                switch (selectedFlag)
                {
                    case CharacterFlagType.Elite:
                        Instance.EliteFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Boss:
                        Instance.BossFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Champion:
                        Instance.ChampionFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Player:
                        Instance.PlayerFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Minion:
                        Instance.MinionFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Drone:
                        Instance.DroneFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Mechanical:
                        Instance.MechanicalFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.Void:
                        Instance.VoidFlagMultiplier.Value = newFormula;
                        break;
                    case CharacterFlagType.All:
                        Instance.AllFlagMultiplier.Value = newFormula;
                        break;
                }
            };

            // SelectedFlag change handler - updates SelectedFlagMultiplier to show the current flag's formula
            Instance.SelectedFlag.SettingChanged += (sender, args) =>
            {
                var selectedFlag = Instance.SelectedFlag.Value;
                string currentFormula = "0";

                switch (selectedFlag)
                {
                    case CharacterFlagType.Elite:
                        currentFormula = Instance.EliteFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Boss:
                        currentFormula = Instance.BossFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Champion:
                        currentFormula = Instance.ChampionFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Player:
                        currentFormula = Instance.PlayerFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Minion:
                        currentFormula = Instance.MinionFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Drone:
                        currentFormula = Instance.DroneFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Mechanical:
                        currentFormula = Instance.MechanicalFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.Void:
                        currentFormula = Instance.VoidFlagMultiplier.Value;
                        break;
                    case CharacterFlagType.All:
                        currentFormula = Instance.AllFlagMultiplier.Value;
                        break;
                }

                // Update SelectedFlagMultiplier to show the current flag's formula
                Instance.SelectedFlagMultiplier.Value = currentFormula;
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

        public static ConfigEntry<string> GetFlagMultiplierConfig(CharacterFlagType flag)
        {
            switch (flag)
            {
                case CharacterFlagType.Elite: return Instance.EliteFlagMultiplier;
                case CharacterFlagType.Boss: return Instance.BossFlagMultiplier;
                case CharacterFlagType.Champion: return Instance.ChampionFlagMultiplier;
                case CharacterFlagType.Player: return Instance.PlayerFlagMultiplier;
                case CharacterFlagType.Minion: return Instance.MinionFlagMultiplier;
                case CharacterFlagType.Drone: return Instance.DroneFlagMultiplier;
                case CharacterFlagType.Mechanical: return Instance.MechanicalFlagMultiplier;
                case CharacterFlagType.Void: return Instance.VoidFlagMultiplier;
                case CharacterFlagType.All: return Instance.AllFlagMultiplier;
                default: return Instance.AllFlagMultiplier;
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
                case CharacterFlagType.All: return "All";
                default: return "All";
            }
        }

        // Carousel slot backing config getters (follow SelectedFlag pattern)
        public static ConfigEntry<float> GetSlotXConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotX : Instance.SideSlotX;
        public static ConfigEntry<float> GetSlotYConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotY : Instance.SideSlotY;
        public static ConfigEntry<float> GetSlotScaleConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotScale : Instance.SideSlotScale;
        public static ConfigEntry<float> GetSlotOpacityConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotOpacity : Instance.SideSlotOpacity;
        public static ConfigEntry<bool> GetSlotShowIconConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotShowIcon : Instance.SideSlotShowIcon;
        public static ConfigEntry<bool> GetSlotShowWeightIconConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotShowWeightIcon : Instance.SideSlotShowWeightIcon;
        public static ConfigEntry<bool> GetSlotShowNameConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotShowName : Instance.SideSlotShowName;
        public static ConfigEntry<bool> GetSlotShowHealthBarConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotShowHealthBar : Instance.SideSlotShowHealthBar;
        public static ConfigEntry<bool> GetSlotShowSlotNumberConfig(HudElementType slot) =>
            slot == HudElementType.SelectedSlot ? Instance.CenterSlotShowSlotNumber : Instance.SideSlotShowSlotNumber;

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
