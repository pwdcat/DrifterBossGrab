#nullable enable
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

    // ========================================================================================
    // PLUGIN CONFIGURATION
    // ========================================================================================
    public enum EnemyRecoveryMode
    {
        Kill = 0,
        Recover = 1
    }

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
        MainSlot,
        SideSlots,
        WeightIcon,
        DamagePreview,
        CapacityUI,
        StatsPanel
    }

    public enum BalanceSubTabType
    {
        All,
        Formulas,
        Multipliers,
        Limits
    }

    public enum PresetType
    {
        Vanilla,
        Intended,
        Minimal,
        Default,
        Balance,
        Hardcore,
        Caveman,
        Custom
    }

    // ========================================================================================
    // CACHING UTILITIES
    // ========================================================================================
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

    public enum ComponentChooserDummy { SelectToToggle }
    public enum ComponentChooserSortMode { ByFrequency, ByProximity, ByRaycast }

    // ========================================================================================
    // CORE CONFIGURATION
    // ========================================================================================
    public partial class PluginConfig
    {
        private static PluginConfig _instance = null!;
        public static PluginConfig Instance => _instance ??= new PluginConfig();

        public ConfigEntry<bool> EnableBossGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableNPCGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableEnvironmentGrabbing { get; private set; } = null!;
        public ConfigEntry<bool> EnableLockedObjectGrabbing { get; private set; } = null!;
        public ConfigEntry<ProjectileGrabbingMode> ProjectileGrabbingMode { get; private set; } = null!;
        public ConfigEntry<string> BodyBlacklist { get; private set; } = null!;
        public ConfigEntry<string> RecoveryObjectBlacklist { get; private set; } = null!;
        public ConfigEntry<string> GrabbableComponentTypes { get; private set; } = null!;
        public ConfigEntry<string> GrabbableKeywordBlacklist { get; private set; } = null!;
        public ConfigEntry<float> SearchRadiusMultiplier { get; private set; } = null!;
        public ConfigEntry<bool> EnableDebugLogs { get; private set; } = null!;
        public ConfigEntry<bool> EnableCombatDirectorPatches { get; private set; } = null!;
        public ConfigEntry<ComponentChooserSortMode> ComponentChooserSortModeEntry { get; private set; } = null!;
        public ConfigEntry<ComponentChooserDummy> ComponentChooserDummyEntry { get; private set; } = null!;
        public ConfigEntry<bool> EnableConfigSync { get; private set; } = null!;
        public ConfigEntry<PresetType> SelectedPreset { get; private set; } = null!;
        public ConfigEntry<PresetType> LastSelectedPreset { get; private set; } = null!;

        public ConfigEntry<bool> EnableRecoveryFeature { get; private set; } = null!;
        public ConfigEntry<EnemyRecoveryMode> EnemyRecoveryMode { get; private set; } = null!;
        public ConfigEntry<bool> RecoverBaggedBosses { get; private set; } = null!;
        public ConfigEntry<bool> RecoverBaggedNPCs { get; private set; } = null!;
        public ConfigEntry<bool> RecoverBaggedEnvironmentObjects { get; private set; } = null!;

        public ConfigEntry<bool> BottomlessBagEnabled { get; private set; } = null!;
        public ConfigEntry<bool> EnableStockRefreshClamping { get; private set; } = null!;
        public ConfigEntry<bool> EnableSuccessiveGrabStockRefresh { get; private set; } = null!;
        public ConfigEntry<float> CycleCooldown { get; private set; } = null!;
        public ConfigEntry<bool> PlayAnimationOnCycle { get; private set; } = null!;
        public ConfigEntry<bool> EnableMouseWheelScrolling { get; private set; } = null!;
        public ConfigEntry<bool> InverseMouseWheelScrolling { get; private set; } = null!;
        public ConfigEntry<bool> AutoPromoteMainSeat { get; private set; } = null!;
        public ConfigEntry<bool> PrioritizeMainSeat { get; private set; } = null!;

        public ConfigEntry<bool> EnableObjectPersistence { get; private set; } = null!;
        public ConfigEntry<bool> EnableAutoGrab { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedBosses { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedNPCs { get; private set; } = null!;
        public ConfigEntry<bool> PersistBaggedEnvironmentObjects { get; private set; } = null!;
        public ConfigEntry<string> PersistenceBlacklist { get; private set; } = null!;
        public ConfigEntry<float> AutoGrabDelay { get; private set; } = null!;

        public ConfigEntry<bool> EnableCarouselHUD { get; private set; } = null!;
        public ConfigEntry<float> CarouselSpacing { get; private set; } = null!;
        public ConfigEntry<float> CarouselAnimationDuration { get; private set; } = null!;

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

        public ConfigEntry<bool> EnableBaggedObjectInfo { get; private set; } = null!;
        public ConfigEntry<float> BaggedObjectInfoX { get; private set; } = null!;
        public ConfigEntry<float> BaggedObjectInfoY { get; private set; } = null!;
        public ConfigEntry<float> BaggedObjectInfoScale { get; private set; } = null!;
        public ConfigEntry<Color> BaggedObjectInfoColor { get; private set; } = null!;

        public ConfigEntry<bool> UseNewWeightIcon { get; private set; } = null!;
        public ConfigEntry<WeightDisplayMode> WeightDisplayMode { get; private set; } = null!;
        public ConfigEntry<bool> ScaleWeightColor { get; private set; } = null!;
        public ConfigEntry<bool> ShowTotalMassOnWeightIcon { get; private set; } = null!;
        public ConfigEntry<bool> ShowOverencumberIcon { get; private set; } = null!;
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
        public ConfigEntry<bool> IsHudEditorEnabled { get; private set; } = null!;

        public ConfigEntry<bool> EnableBalance { get; private set; } = null!;
        public ConfigEntry<AoEDamageMode> AoEDamageDistribution { get; private set; } = null!;
        public ConfigEntry<float> BreakoutTimeMultiplier { get; private set; } = null!;
        public ConfigEntry<int> MaxSmacks { get; private set; } = null!;
        public ConfigEntry<string> BagScaleCap { get; private set; } = null!;
        public ConfigEntry<string> MassCap { get; private set; } = null!;
        public ConfigEntry<string> MaxLaunchSpeed { get; private set; } = null!;

        public ConfigEntry<StateCalculationMode> StateCalculationMode { get; private set; } = null!;
        public ConfigEntry<float> OverencumbranceMax { get; private set; } = null!;
        public ConfigEntry<string> SlotScalingFormula { get; private set; } = null!;
        public ConfigEntry<string> MassCapacityFormula { get; private set; } = null!;
        public ConfigEntry<string> MovespeedPenaltyFormula { get; private set; } = null!;
        public ConfigEntry<string> SlamDamageFormula { get; private set; } = null!;

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
        public static readonly Dictionary<string, HudElementType[]> HudSettingToSubTab = new()
        {
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.HUD_FILTER.CHOICE"] = new[] {
                HudElementType.All,
                HudElementType.MainSlot,
                HudElementType.SideSlots,
                HudElementType.WeightIcon,
                HudElementType.DamagePreview,
                HudElementType.CapacityUI,
                HudElementType.StatsPanel
            },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLE_HUD_EDITOR.CHECKBOX"] = new[] {
                HudElementType.All,
                HudElementType.MainSlot,
                HudElementType.SideSlots,
                HudElementType.WeightIcon,
                HudElementType.DamagePreview,
                HudElementType.CapacityUI,
                HudElementType.StatsPanel
            },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLE_CAROUSEL_HUD.CHECKBOX"] = new[] { HudElementType.MainSlot, HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.VERTICAL_SPACING.FLOAT_FIELD"] = new[] { HudElementType.MainSlot, HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.ANIMATION_DURATION.FLOAT_FIELD"] = new[] { HudElementType.MainSlot, HudElementType.SideSlots },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.MAIN_SLOT_X_OFFSET.FLOAT_FIELD"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.MAIN_SLOT_Y_OFFSET.FLOAT_FIELD"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.MAIN_SLOT_SCALE.FLOAT_FIELD"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.MAIN_SLOT_OPACITY.FLOAT_FIELD"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_ICON_(MAIN).CHECKBOX"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_WEIGHT_ICON_(MAIN).CHECKBOX"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_NAME_(MAIN).CHECKBOX"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_HEALTH_(MAIN).CHECKBOX"] = new[] { HudElementType.MainSlot },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_SLOT_#_(MAIN).CHECKBOX"] = new[] { HudElementType.MainSlot },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SIDE_SLOT_X_OFFSET.FLOAT_FIELD"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SIDE_SLOT_Y_OFFSET.FLOAT_FIELD"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SIDE_SLOT_SCALE.FLOAT_FIELD"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SIDE_SLOT_OPACITY.FLOAT_FIELD"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_ICON_(SIDE).CHECKBOX"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_WEIGHT_ICON_(SIDE).CHECKBOX"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_NAME_(SIDE).CHECKBOX"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_HEALTH_(SIDE).CHECKBOX"] = new[] { HudElementType.SideSlots },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_SLOT_#_(SIDE).CHECKBOX"] = new[] { HudElementType.SideSlots },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.USE_NEW_WEIGHT_ICON.CHECKBOX"] = new[] { HudElementType.WeightIcon },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.WEIGHT_DISPLAY_MODE.CHOICE"] = new[] { HudElementType.WeightIcon },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SCALE_WEIGHT_COLOR.CHECKBOX"] = new[] { HudElementType.WeightIcon },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_TOTAL_MASS.CHECKBOX"] = new[] { HudElementType.WeightIcon },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.SHOW_OVERENCUMBERED_ICON.CHECKBOX"] = new[] { HudElementType.WeightIcon },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLE_DAMAGE_PREVIEW.CHECKBOX"] = new[] { HudElementType.DamagePreview },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.DAMAGE_PREVIEW_COLOR.COLOR"] = new[] { HudElementType.DamagePreview },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLE_CAPACITY_UI.CHECKBOX"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.CAPACITY_UI_X_POS.FLOAT_FIELD"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.CAPACITY_UI_Y_POS.FLOAT_FIELD"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.CAPACITY_UI_SCALE.FLOAT_FIELD"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLE_SEPARATORS.CHECKBOX"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.GRADIENT_INTENSITY.STEP_SLIDER"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.GRADIENT_COLOR_START.COLOR"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.GRADIENT_COLOR_MID.COLOR"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.GRADIENT_COLOR_END.COLOR"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.OVERENCUMBRANCE_START.COLOR"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.OVERENCUMBRANCE_MID.COLOR"] = new[] { HudElementType.CapacityUI },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.OVERENCUMBRANCE_END.COLOR"] = new[] { HudElementType.CapacityUI },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.ENABLE_STATS_PANEL.CHECKBOX"] = new[] { HudElementType.StatsPanel },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.STATS_PANEL_X_POS.FLOAT_FIELD"] = new[] { HudElementType.StatsPanel },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.STATS_PANEL_Y_POS.FLOAT_FIELD"] = new[] { HudElementType.StatsPanel },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.STATS_PANEL_SCALE.FLOAT_FIELD"] = new[] { HudElementType.StatsPanel },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.HUD.STATS_PANEL_COLOR.COLOR"] = new[] { HudElementType.StatsPanel }
        };

        public static readonly Dictionary<string, BalanceSubTabType[]> BalanceSettingToSubTab = new()
        {
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MASS_CAPACITY_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Formulas },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.FLAG.CHOICE"] = new[] { BalanceSubTabType.Multipliers },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MULTIPLIER.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Multipliers },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.CHARACTER_FLAGS.ALL_FLAG_MULTIPLIER.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Multipliers },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAX_OVERENCUMBRANCE_(%).FLOAT_FIELD"] = new[] { BalanceSubTabType.Limits },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.STATE_CALCULATION.CHOICE"] = new[] { BalanceSubTabType.Formulas },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MASS_MULTIPLIER_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Formulas },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.SPEED_PENALTY_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Formulas },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.BAG_VISUAL_SIZE_CAP.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Limits },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.BAGGED_ENTITY_MASS_CAP.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Limits },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.AOE_DAMAGE.CHOICE"] = new[] { BalanceSubTabType.Limits },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.GRAB_RANGE_MULTIPLIER.STEP_SLIDER"] = new[] { BalanceSubTabType.Multipliers },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.BREAKOUT_TIME_MULTIPLIER.STEP_SLIDER"] = new[] { BalanceSubTabType.Multipliers },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAX_HITS_BEFORE_BREAKOUT.INT_SLIDER"] = new[] { BalanceSubTabType.Limits },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAX_LAUNCH_SPEED.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Limits },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.SLAM_DAMAGE_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Formulas }
        };

        internal ICachedValue<HashSet<string>> _blacklistCache = null!;
        internal ICachedValue<HashSet<string>> _blacklistCacheWithClones = null!;
        internal ICachedValue<HashSet<string>> _recoveryBlacklistCache = null!;
        internal ICachedValue<HashSet<string>> _recoveryBlacklistCacheWithClones = null!;
        internal ICachedValue<HashSet<string>> _persistenceBlacklistCache = null!;
        internal ICachedValue<HashSet<string>> _persistenceBlacklistCacheWithClones = null!;
        internal ICachedValue<HashSet<string>> _grabbableComponentTypesCache = null!;
        internal ICachedValue<HashSet<string>> _grabbableKeywordBlacklistCache = null!;
        private readonly List<IGrabbingStrategy> _grabbingStrategies = new List<IGrabbingStrategy>
        {
            new BossGrabbingStrategy(),
            new NPCGrabbingStrategy(),
            new EnvironmentGrabbingStrategy()
        };

        // ========================================================================================
        // BLACKLIST HELPERS
        // ========================================================================================
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
        public static bool IsPersistenceBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Instance._persistenceBlacklistCacheWithClones.Value.Contains(name);
        }
        public static bool IsPersistenceBlacklisted(GameObject? obj)
        {
            if (obj == null) return false;

            if (IsPersistenceBlacklisted(obj.name)) return true;

            if (Instance._persistenceBlacklistCache.Value.Contains("Teleporter"))
            {
                if (obj.GetComponent<RoR2.TeleporterInteraction>() != null)
                {
                    return true;
                }
            }

            return false;
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

        // ========================================================================================
        // GRABBABILITY LOGIC
        // ========================================================================================
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

        // ========================================================================================
        // PARSING UTILITIES
        // ========================================================================================
        private bool _isBagScaleCapInfinite;
        private bool _isMassCapInfinite;
        private bool _isAddedCapacityInfinite;
        private bool _isMaxLaunchSpeedInfinite;
        private float _parsedMassCap = 700f;
        private float _parsedBagScaleCap = 1f;
        private float _parsedMaxLaunchSpeed = 30f;

        public bool IsBagScaleCapInfinite => _isBagScaleCapInfinite;
        public bool IsMassCapInfinite => _isMassCapInfinite;
        public bool IsAddedCapacityInfinite => _isAddedCapacityInfinite;
        public bool IsMaxLaunchSpeedInfinite => _isMaxLaunchSpeedInfinite;
        public float ParsedMassCap => _parsedMassCap;
        public float ParsedBagScaleCap => _parsedBagScaleCap;
        public float ParsedMaxLaunchSpeed => _parsedMaxLaunchSpeed;

        public void RefreshCachedConfigStrings()
        {
            _isBagScaleCapInfinite = string.Equals(BagScaleCap.Value, "INF", StringComparison.OrdinalIgnoreCase) || string.Equals(BagScaleCap.Value, "INFINITY", StringComparison.OrdinalIgnoreCase);
            _parsedBagScaleCap = _isBagScaleCapInfinite ? float.MaxValue : (float.TryParse(BagScaleCap.Value, out var bsc) ? bsc : 1f);

            _isMassCapInfinite = string.Equals(MassCap.Value, "INF", StringComparison.OrdinalIgnoreCase) || string.Equals(MassCap.Value, "INFINITY", StringComparison.OrdinalIgnoreCase);
            _parsedMassCap = _isMassCapInfinite ? float.MaxValue : (float.TryParse(MassCap.Value, out var mc) ? mc : 700f);

            _isAddedCapacityInfinite = string.Equals(SlotScalingFormula.Value, "INF", StringComparison.OrdinalIgnoreCase) || string.Equals(SlotScalingFormula.Value, "INFINITY", StringComparison.OrdinalIgnoreCase);

            _isMaxLaunchSpeedInfinite = string.Equals(MaxLaunchSpeed.Value, "INF", StringComparison.OrdinalIgnoreCase) || string.Equals(MaxLaunchSpeed.Value, "INFINITY", StringComparison.OrdinalIgnoreCase);
            _parsedMaxLaunchSpeed = _isMaxLaunchSpeedInfinite ? float.MaxValue : (float.TryParse(MaxLaunchSpeed.Value, out var mls) ? mls : 30f);
        }

        // ========================================================================================
        // INITIALIZATION
        // ========================================================================================
        public static void Init(ConfigFile cfg)
        {
            InitGeneralConfig(cfg);
            InitRecoveryConfig(cfg);
            InitPersistenceConfig(cfg);
            InitHudConfig(cfg);
            InitBottomlessBagConfig(cfg);
            InitBalanceConfig(cfg);
            InitCharacterFlagsConfig(cfg);
            InitBlacklistCaches();
            Instance.RefreshCachedConfigStrings();
        }

        private static void InitBlacklistCaches()
        {
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

            Instance._persistenceBlacklistCache = new LazyCachedValue<HashSet<string>>(() =>
                string.IsNullOrEmpty(Instance.PersistenceBlacklist.Value)
                    ? new HashSet<string>()
                    : Instance.PersistenceBlacklist.Value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase));

            Instance._persistenceBlacklistCacheWithClones = new LazyCachedValue<HashSet<string>>(() =>
            {
                var baseSet = Instance._persistenceBlacklistCache.Value;
                var withClones = new HashSet<string>(baseSet, StringComparer.OrdinalIgnoreCase);
                foreach (var item in baseSet)
                {
                    withClones.Add(item + Constants.CloneSuffix);
                }
                return withClones;
            });

            Instance.BodyBlacklist.SettingChanged += (sender, args) => { Instance._blacklistCache.Invalidate(); Instance._blacklistCacheWithClones.Invalidate(); };
            Instance.RecoveryObjectBlacklist.SettingChanged += (sender, args) => { Instance._recoveryBlacklistCache.Invalidate(); Instance._recoveryBlacklistCacheWithClones.Invalidate(); };
            Instance.PersistenceBlacklist.SettingChanged += (sender, args) => { Instance._persistenceBlacklistCache.Invalidate(); Instance._persistenceBlacklistCacheWithClones.Invalidate(); };
            Instance.GrabbableComponentTypes.SettingChanged += (sender, args) => Instance._grabbableComponentTypesCache.Invalidate();
            Instance.GrabbableKeywordBlacklist.SettingChanged += (sender, args) => Instance._grabbableKeywordBlacklistCache.Invalidate();
        }
        public static void RemoveEventHandlers(
            EventHandler debugLogsHandler,
            EventHandler blacklistHandler,
            EventHandler recoveryBlacklistHandler,
            EventHandler persistenceBlacklistHandler,
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
            Instance.PersistenceBlacklist.SettingChanged -= persistenceBlacklistHandler;
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
        public static void ClearPersistenceBlacklistCache()
        {
            Instance._persistenceBlacklistCache.Invalidate();
            Instance._persistenceBlacklistCacheWithClones.Invalidate();
        }
        public static void ClearGrabbableComponentTypesCache()
        {
            Instance._grabbableComponentTypesCache.Invalidate();
        }
        public static void ClearGrabbableKeywordBlacklistCache()
        {
            Instance._grabbableKeywordBlacklistCache.Invalidate();
        }
        public static void InvalidateAllCaches()
        {
            Instance._blacklistCache.Invalidate();
            Instance._blacklistCacheWithClones.Invalidate();
            Instance._recoveryBlacklistCache.Invalidate();
            Instance._recoveryBlacklistCacheWithClones.Invalidate();
            Instance._persistenceBlacklistCache.Invalidate();
            Instance._persistenceBlacklistCacheWithClones.Invalidate();
            Instance._grabbableComponentTypesCache.Invalidate();
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

        public static ConfigEntry<float> GetSlotXConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotX : Instance.SideSlotX;
        public static ConfigEntry<float> GetSlotYConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotY : Instance.SideSlotY;
        public static ConfigEntry<float> GetSlotScaleConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotScale : Instance.SideSlotScale;
        public static ConfigEntry<float> GetSlotOpacityConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotOpacity : Instance.SideSlotOpacity;
        public static ConfigEntry<bool> GetSlotShowIconConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotShowIcon : Instance.SideSlotShowIcon;
        public static ConfigEntry<bool> GetSlotShowWeightIconConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotShowWeightIcon : Instance.SideSlotShowWeightIcon;
        public static ConfigEntry<bool> GetSlotShowNameConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotShowName : Instance.SideSlotShowName;
        public static ConfigEntry<bool> GetSlotShowHealthBarConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotShowHealthBar : Instance.SideSlotShowHealthBar;
        public static ConfigEntry<bool> GetSlotShowSlotNumberConfig(HudElementType slot) =>
            slot == HudElementType.MainSlot ? Instance.CenterSlotShowSlotNumber : Instance.SideSlotShowSlotNumber;

        // ========================================================================================
        // UI REFRESH HANDLERS
        // ========================================================================================
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
