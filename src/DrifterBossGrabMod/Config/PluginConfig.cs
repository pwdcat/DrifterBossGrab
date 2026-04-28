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
        Capacity,
        Multipliers,
        Penalty,
        Misc
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
    public enum ComponentChooserSortMode { ByFrequency, ByProximity, ByRaycast }

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
        public ConfigEntry<string> AddedCapacity { get; private set; } = null!;
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

        // Balance
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
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.ENABLE_BALANCE.CHECKBOX"] = new[] { BalanceSubTabType.Capacity },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.UNCAP_CAPACITY.CHECKBOX"] = new[] { BalanceSubTabType.Capacity },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.SLOT_SCALING_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Capacity },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MASS_CAPACITY_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Capacity },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.FLAG.CHOICE"] = new[] { BalanceSubTabType.Multipliers },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MULTIPLIER.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Multipliers },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.CHARACTER_FLAGS.ALL_FLAG_MULTIPLIER.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Multipliers },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAX_OVERENCUMBRANCE_(%).FLOAT_FIELD"] = new[] { BalanceSubTabType.Penalty },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.STATE_CALCULATION.CHOICE"] = new[] { BalanceSubTabType.Penalty },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MASS_MULTIPLIER_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Penalty },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.SPEED_PENALTY_FORMULA.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Penalty },

            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.BAG_VISUAL_SIZE_CAP.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Misc },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.BAGGED_ENTITY_MASS_CAP.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Misc },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.AOE_DAMAGE.CHOICE"] = new[] { BalanceSubTabType.Misc },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.GRAB_RANGE_MULTIPLIER.STEP_SLIDER"] = new[] { BalanceSubTabType.Misc },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.BREAKOUT_TIME_MULTIPLIER.STEP_SLIDER"] = new[] { BalanceSubTabType.Misc },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAX_HITS_BEFORE_BREAKOUT.INT_SLIDER"] = new[] { BalanceSubTabType.Misc },
            ["COM.PWDCAT.DRIFTERBOSSGRAB.BALANCE.MAX_LAUNCH_SPEED.STRING_INPUT_FIELD"] = new[] { BalanceSubTabType.Misc }
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

            // Check by name first (handles clones automatically via cache)
            if (IsPersistenceBlacklisted(obj.name)) return true;

            // Special case for Teleporters: if "Teleporter" is in the blacklist, 
            // any object with a TeleporterInteraction component is blocked.
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

        // Cached config string parsing - avoids Trim().ToUpper() allocations on hot paths
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

            _isAddedCapacityInfinite = string.Equals(AddedCapacity.Value, "INF", StringComparison.OrdinalIgnoreCase) || string.Equals(AddedCapacity.Value, "INFINITY", StringComparison.OrdinalIgnoreCase);

            _isMaxLaunchSpeedInfinite = string.Equals(MaxLaunchSpeed.Value, "INF", StringComparison.OrdinalIgnoreCase) || string.Equals(MaxLaunchSpeed.Value, "INFINITY", StringComparison.OrdinalIgnoreCase);
            _parsedMaxLaunchSpeed = _isMaxLaunchSpeedInfinite ? float.MaxValue : (float.TryParse(MaxLaunchSpeed.Value, out var mls) ? mls : 30f);
        }

        public static void Init(ConfigFile cfg)
        {
            Instance.SelectedPreset = cfg.Bind("General", "SelectedPreset", PresetType.Intended,
                "Preset to load. Changes are auto-applied.");

            Instance.LastSelectedPreset = cfg.Bind("Hidden", "LastSelectedPreset", PresetType.Intended,
                "Internal tracker of the last applied preset.");

            Instance.EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Allow grabbing bosses.");
            Instance.EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Allow grabbing normally-ungrabbable NPCs.");
            Instance.EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Allow grabbing environment objects.");
            Instance.EnableLockedObjectGrabbing = cfg.Bind("General", "EnableLockedObjectGrabbing", false, "Allow grabbing locked objects.");
            Instance.ProjectileGrabbingMode = cfg.Bind("General", "ProjectileGrabbingMode", DrifterBossGrabMod.ProjectileGrabbingMode.None, "Projectile grab mode.");
            Instance.EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Log grab mechanics for debugging.");
            Instance.BodyBlacklist = cfg.Bind("General", "Blacklist", "HeaterPodBodyNoRespawn,ThrownObjectProjectile,GenericPickup,MultiShopTerminal,MultiShopLargeTerminal,MultiShopEquipmentTerminal,RailgunnerPistolProjectile,FMJRamping,SyringeProjectile,EngiGrenadeProjectile,CrocoSpit,CaptainTazer,LunarSpike,LunarNeedleProjectile,StickyBomb,RocketProjectile,StunAndPierceBoomerang",
                "Bodies and projectiles to never grab. Comma-separated.");
            Instance.RecoveryObjectBlacklist = cfg.Bind("General", "RecoveryObjectBlacklist", "",
                "Objects to never recover from the abyss. Comma-separated.");

            Instance.EnableRecoveryFeature = cfg.Bind("Recovery", "EnableRecoveryFeature", true, "Return bagged items that fall off the map.");
            Instance.EnemyRecoveryMode = cfg.Bind("Recovery", "EnemyRecoveryMode", DrifterBossGrabMod.EnemyRecoveryMode.Recover, "Behavior for bagged enemies falling off the map.");
            Instance.RecoverBaggedBosses = cfg.Bind("Recovery", "RecoverBaggedBosses", true, "Recover bagged bosses from the abyss.");
            Instance.RecoverBaggedNPCs = cfg.Bind("Recovery", "RecoverBaggedNPCs", true, "Recover bagged NPCs from the abyss.");
            Instance.RecoverBaggedEnvironmentObjects = cfg.Bind("Recovery", "RecoverBaggedEnvironmentObjects", true, "Recover bagged environment objects from the abyss.");

            Instance.GrabbableComponentTypes = cfg.Bind("General", "GrabbableComponentTypes", "PurchaseInteraction,TeleporterInteraction,GenericInteraction,ProxyInteraction,DummyPingableInteraction,MealPrepController",
                "Component type names that make objects grabbable. Comma-separated.");
            Instance.GrabbableKeywordBlacklist = cfg.Bind("General", "GrabbableKeywordBlacklist", "Master,Controller",
                "Keywords that prevent grabbing if found in name. Comma-separated.");
            Instance.ComponentChooserSortModeEntry = cfg.Bind("Hidden", "ComponentChooserSortMode", ComponentChooserSortMode.ByFrequency,
                "How to sort components in the UI.");
            Instance.ComponentChooserDummyEntry = cfg.Bind("Hidden", "ComponentChooserDummy", ComponentChooserDummy.SelectToToggle,
                "Dummy setting for UI.");
            Instance.EnableConfigSync = cfg.Bind("General", "EnableConfigSync", true,
                "Sync configuration from host to clients.");
            Instance.EnableObjectPersistence = cfg.Bind("Persistence", "EnableObjectPersistence",
                false,
                "Save and restore bagged objects across stages.");
            Instance.EnableAutoGrab = cfg.Bind("Persistence", "EnableAutoGrab",
                false,
                "Auto-grab persisted objects on stage start.");
            Instance.PersistBaggedBosses = cfg.Bind("Persistence", "PersistBaggedBosses",
                true,
                "Allow bosses to persist across stages.");
            Instance.PersistBaggedNPCs = cfg.Bind("Persistence", "PersistBaggedNPCs",
                true,
                "Allow NPCs to persist across stages.");
            Instance.PersistBaggedEnvironmentObjects = cfg.Bind("Persistence", "PersistBaggedEnvironmentObjects",
                true,
                "Allow environment objects to persist across stages.");
            Instance.PersistenceBlacklist = cfg.Bind("Persistence", "PersistenceBlacklist", "",
                "Objects to never persist. Comma-separated.");
            Instance.AutoGrabDelay = cfg.Bind("Persistence", "AutoGrabDelay", 1.0f, "Delay before auto-grabbing persisted objects (seconds).");
            Instance.BottomlessBagEnabled = cfg.Bind("Bottomless Bag", "EnableBottomlessBag",
                false,
                "Store multiple objects and cycle through them.");
            Instance.AddedCapacity = cfg.Bind("Bottomless Bag", "AddedCapacity", "0", "Flat extra bag capacity.");
            Instance.EnableStockRefreshClamping = cfg.Bind("Bottomless Bag", "EnableStockRefreshClamping", false, "Clamp stock refresh to empty slots.");
            Instance.EnableSuccessiveGrabStockRefresh = cfg.Bind("Bottomless Bag", "EnableSuccessiveGrabStockRefresh", false, "Refresh stock only after a successful grab at 0.");
            Instance.CycleCooldown = cfg.Bind("Bottomless Bag", "CycleCooldown", 0.2f, "Cooldown between passenger cycles.");
            Instance.PlayAnimationOnCycle = cfg.Bind("Bottomless Bag", "PlayAnimationOnCycle", false, "Play grab animation when cycling.");
            Instance.EnableMouseWheelScrolling = cfg.Bind("Bottomless Bag", "EnableMouseWheelScrolling", true, "Cycle passengers via mouse wheel.");
            Instance.InverseMouseWheelScrolling = cfg.Bind("Bottomless Bag", "InverseMouseWheelScrolling", false, "Invert mouse wheel cycle direction.");

            Instance.EnableCarouselHUD = cfg.Bind("Hud", "EnableCarouselHUD", false, "Enable the custom Carousel HUD.");
            Instance.CarouselSpacing = cfg.Bind("Hud", "CarouselSpacing", 45.0f, "Vertical spacing for carousel items.");
            Instance.CarouselAnimationDuration = cfg.Bind("Hud", "CarouselAnimationDuration", 0.4f, "Duration of carousel animation.");

            Instance.CenterSlotX = cfg.Bind("Hud", "CenterSlotX", 25.0f, "X position offset for center slot.");
            Instance.CenterSlotY = cfg.Bind("Hud", "CenterSlotY", 50.0f, "Y position offset for center slot.");
            Instance.CenterSlotScale = cfg.Bind("Hud", "CenterSlotScale", 1.0f, "Scale for center slot.");
            Instance.CenterSlotOpacity = cfg.Bind("Hud", "CenterSlotOpacity", 1.0f, "Opacity for center slot.");
            Instance.CenterSlotShowIcon = cfg.Bind("Hud", "CenterSlotShowIcon", true, "Show icon in center slot.");
            Instance.CenterSlotShowWeightIcon = cfg.Bind("Hud", "CenterSlotShowWeightIcon", true, "Show weight icon in center slot.");
            Instance.CenterSlotShowName = cfg.Bind("Hud", "CenterSlotShowName", true, "Show name in center slot.");
            Instance.CenterSlotShowHealthBar = cfg.Bind("Hud", "CenterSlotShowHealthBar", true, "Show health bar in center slot.");
            Instance.CenterSlotShowSlotNumber = cfg.Bind("Hud", "CenterSlotShowSlotNumber", true, "Show slot number in center slot.");

            Instance.SideSlotX = cfg.Bind("Hud", "SideSlotX", 20.0f, "X position offset for side slots.");
            Instance.SideSlotY = cfg.Bind("Hud", "SideSlotY", 5.0f, "Y position offset for side slots.");
            Instance.SideSlotScale = cfg.Bind("Hud", "SideSlotScale", 0.8f, "Scale for side slots.");
            Instance.SideSlotOpacity = cfg.Bind("Hud", "SideSlotOpacity", 0.3f, "Opacity for side slots.");
            Instance.SideSlotShowIcon = cfg.Bind("Hud", "SideSlotShowIcon", true, "Show icon in side slots.");
            Instance.SideSlotShowWeightIcon = cfg.Bind("Hud", "SideSlotShowWeightIcon", true, "Show weight icon in side slots.");
            Instance.SideSlotShowName = cfg.Bind("Hud", "SideSlotShowName", true, "Show name in side slots.");
            Instance.SideSlotShowHealthBar = cfg.Bind("Hud", "SideSlotShowHealthBar", true, "Show health bar in side slots.");
            Instance.SideSlotShowSlotNumber = cfg.Bind("Hud", "SideSlotShowSlotNumber", true, "Show slot number in side slots.");

            Instance.SelectedHudElement = cfg.Bind("Hidden", "SelectedHudElement", HudElementType.All,
                "Select which HUD element group to configure.");
            Instance.SelectedHudElement.Value = HudElementType.All;

            Instance.EnableBaggedObjectInfo = cfg.Bind("Hud", "EnableBaggedObjectInfo", false, "Enable the Bagged Object Info stats panel.");
            Instance.BaggedObjectInfoX = cfg.Bind("Hud", "BaggedObjectInfoX", 20.0f, "X position offset for stats panel.");
            Instance.BaggedObjectInfoY = cfg.Bind("Hud", "BaggedObjectInfoY", 0.0f, "Y position offset for stats panel.");
            Instance.BaggedObjectInfoScale = cfg.Bind("Hud", "BaggedObjectInfoScale", 1.0f, "Scale for stats panel.");
            Instance.BaggedObjectInfoColor = cfg.Bind("Hud", "BaggedObjectInfoColor", new Color(1f, 1f, 1f, 0.9f), "Text color for stats panel.");
            Instance.EnableDamagePreview = cfg.Bind("Hud", "EnableDamagePreview", false, "Show damage preview overlay.");
            Instance.DamagePreviewColor = cfg.Bind("Hud", "DamagePreviewColor", new Color(1f, 0.15f, 0.15f, 0.8f), "Color for damage preview.");
            Instance.UseNewWeightIcon = cfg.Bind("Hud", "UseNewWeightIcon", false, "Use the custom weight icon.");
            Instance.WeightDisplayMode = cfg.Bind("Hud", "WeightDisplayMode", DrifterBossGrabMod.WeightDisplayMode.Multiplier, "Mode for weight display.");
            Instance.ScaleWeightColor = cfg.Bind("Hud", "ScaleWeightColor", true, "Scale weight icon color by capacity.");
            Instance.ShowTotalMassOnWeightIcon = cfg.Bind("Hud", "ShowTotalMassOnWeightIcon", false, "Show total bag mass on center slot.");
            Instance.ShowOverencumberIcon = cfg.Bind("Hud", "ShowOverencumberIcon", false, "Show overencumbrance icon.");
            Instance.AutoPromoteMainSeat = cfg.Bind("Bottomless Bag", "AutoPromoteMainSeat", false, "Auto-promote next object when main is removed.");
            Instance.PrioritizeMainSeat = cfg.Bind("Bottomless Bag", "PrioritizeMainSeat", false, "New objects go to main seat first.");
            Instance.EnableMassCapacityUI = cfg.Bind("Hud", "EnableMassCapacityUI", false, "Enable the Mass Capacity UI bar.");
            Instance.MassCapacityUIPositionX = cfg.Bind("Hud", "MassCapacityUIPositionX", -20.0f, "X offset for Mass Capacity UI.");
            Instance.MassCapacityUIPositionY = cfg.Bind("Hud", "MassCapacityUIPositionY", 0.0f, "Y offset for Mass Capacity UI.");
            Instance.MassCapacityUIScale = cfg.Bind("Hud", "MassCapacityUIScale", 0.8f, "Scale for Mass Capacity UI.");
            Instance.EnableSeparators = cfg.Bind("Hud", "EnableSeparators", true, "Show threshold pips on Mass Capacity UI.");
            Instance.GradientIntensity = cfg.Bind("Hud", "GradientIntensity", 1.0f, "Intensity of the gradient color.");

            Instance.CapacityGradientColorStart = cfg.Bind("Hud", "CapacityGradientColorStart", new Color(0.0f, 1.0f, 0.0f, 1.0f), "Start color for standard capacity gradient.");
            Instance.CapacityGradientColorMid = cfg.Bind("Hud", "CapacityGradientColorMid", new Color(1.0f, 1.0f, 0.0f, 1.0f), "Mid color for standard capacity gradient.");
            Instance.CapacityGradientColorEnd = cfg.Bind("Hud", "CapacityGradientColorEnd", new Color(1.0f, 0.0f, 0.0f, 1.0f), "End color for standard capacity gradient.");

            Instance.OverencumbranceGradientColorStart = cfg.Bind("Hud", "OverencumbranceGradientColorStart", new Color(0f, 1.0f, 1.0f, 1.0f), "Start color for overencumbrance gradient.");
            Instance.OverencumbranceGradientColorMid = cfg.Bind("Hud", "OverencumbranceGradientColorMid", new Color(0.0f, 0.0f, 0.5f, 1.0f), "Mid color for overencumbrance gradient.");
            Instance.OverencumbranceGradientColorEnd = cfg.Bind("Hud", "OverencumbranceGradientColorEnd", new Color(0.0f, 0.0f, 1.0f, 1.0f), "End color for overencumbrance gradient.");

            Instance.EnableBalance = cfg.Bind("Balance", "EnableBalance", false, "Enable mass and penalty systems.");
            Instance.SlotScalingFormula = cfg.Bind("Balance", "SlotScalingFormula", "0", "Formula for extra bag slots. Supported: H (Max HP), L (Level), C (Stocks), MC (Mass Cap), S (Stage).");
            Instance.MassCapacityFormula = cfg.Bind("Balance", "MassCapacityFormula", "C * MC", "Formula for mass capacity limit. Supported: H (Max HP), L (Level), C (Stocks), MC (Mass Cap), S (Stage).");
            Instance.MovespeedPenaltyFormula = cfg.Bind("Balance", "MovespeedPenaltyFormula", "0", "Formula for movement speed penalty. Supported: T (Total Mass), M (Mass Cap limit), C (Total Cap), H (Max HP), L (Level), MC (Mass Cap config), S (Stage).");

            Instance.SlamDamageFormula = cfg.Bind("Balance", "SlamDamageFormula",
                "BASE_COEF + (MASS_SCALING * BM / MC)",
                "Formula for slam damage coefficient. Supported: BASE_COEF, MASS_SCALING, BM (Bagged Mass), MC (Mass Cap).");
            Instance.StateCalculationMode = cfg.Bind("Balance", "StateCalculationMode", DrifterBossGrabMod.StateCalculationMode.Current, "State calculation mode for stats.");
            Instance.AoEDamageDistribution = cfg.Bind("Balance", "AoEDamageDistribution", AoEDamageMode.Full, "Mode for AoE damage distribution.");
            Instance.OverencumbranceMax = cfg.Bind("Balance", "OverencumbranceMax", 100.0f, "Maximum overencumbrance percentage.");

            Instance.SearchRadiusMultiplier = cfg.Bind("Balance", "SearchRadiusMultiplier", 1.0f, "Multiplier for grab reach distance.");
            Instance.BreakoutTimeMultiplier = cfg.Bind("Balance", "BreakoutTimeMultiplier", 1.0f, "Multiplier for breakout time.");
            Instance.MaxSmacks = cfg.Bind("Balance", "MaxSmacks", 3, new ConfigDescription("Hits before breakout.", new AcceptableValueRange<int>(1, 100)));
            Instance.MaxLaunchSpeed = cfg.Bind("Balance", "MaxLaunchSpeed", "30", "Maximum launch speed for breakout.");
            Instance.BagScaleCap = cfg.Bind("Balance", "BagScaleCap", "1", "Bag visual size cap.");
            Instance.MassCap = cfg.Bind("Balance", "MassCap", "700", "Mass cap for caught entities.");

            Instance.EliteFlagMultiplier = cfg.Bind("Character Flags", "EliteFlagMultiplier", "1", "Mass multiplier for Elite entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.EliteFlagMultiplier.Value = "1";

            Instance.BossFlagMultiplier = cfg.Bind("Character Flags", "BossFlagMultiplier", "1", "Mass multiplier for Boss entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.BossFlagMultiplier.Value = "1";

            Instance.ChampionFlagMultiplier = cfg.Bind("Character Flags", "ChampionFlagMultiplier", "1", "Mass multiplier for Champion entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.ChampionFlagMultiplier.Value = "1";

            Instance.PlayerFlagMultiplier = cfg.Bind("Character Flags", "PlayerFlagMultiplier", "1", "Mass multiplier for Player entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.PlayerFlagMultiplier.Value = "1";

            Instance.MinionFlagMultiplier = cfg.Bind("Character Flags", "MinionFlagMultiplier", "1", "Mass multiplier for Minion entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.MinionFlagMultiplier.Value = "1";

            Instance.DroneFlagMultiplier = cfg.Bind("Character Flags", "DroneFlagMultiplier", "1", "Mass multiplier for Drone entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.DroneFlagMultiplier.Value = "1";

            Instance.MechanicalFlagMultiplier = cfg.Bind("Character Flags", "MechanicalFlagMultiplier", "1", "Mass multiplier for Mechanical entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.MechanicalFlagMultiplier.Value = "1";

            Instance.VoidFlagMultiplier = cfg.Bind("Character Flags", "VoidFlagMultiplier", "1", "Mass multiplier for Void entities. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).");
            Instance.VoidFlagMultiplier.Value = "1";

            Instance.AllFlagMultiplier = cfg.Bind(
                new ConfigDefinition("Character Flags", "all Flag Multiplier"),
                "1",
                new ConfigDescription("Universal multiplier for all enemies. Supported: B (Base Mass), H (Max HP), BH (Base Max HP), L (Level), S (Stage).")
            );

            Instance.SelectedFlag = cfg.Bind("Hidden", "SelectedFlag", CharacterFlagType.All,
                "Select which flag to modify.");
            Instance.SelectedFlag.Value = CharacterFlagType.All;
            Instance.SelectedFlagMultiplier = cfg.Bind("Hidden", "FlagMultiplier", "1",
                "Mass multiplier for selected flag.");
            Instance.SelectedFlagMultiplier.Value = "1";

            Instance.SelectedBalanceSubTab = cfg.Bind("Hidden", "SelectedBalanceSubTab", BalanceSubTabType.All,
                "Select which Balance settings group to view.");
            Instance.SelectedBalanceSubTab.Value = BalanceSubTabType.All;



            // Force EnableCarouselHUD to true if BottomlessBagEnabled is true
            if (Instance.BottomlessBagEnabled.Value && !Instance.EnableCarouselHUD.Value)
            {
                Instance.EnableCarouselHUD.Value = true;
            }

            Instance.BottomlessBagEnabled.SettingChanged += (sender, args) =>
            {
                // Force EnableCarouselHUD to true when BottomlessBagEnabled is true
                if (Instance.BottomlessBagEnabled.Value && !Instance.EnableCarouselHUD.Value)
                {
                    Instance.EnableCarouselHUD.Value = true;
                }
            };
            Instance.EnableCarouselHUD.SettingChanged += (sender, args) => UpdateBagUIToggles();
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
            Instance.ShowOverencumberIcon.SettingChanged += (sender, args) => UpdateBagUIToggles();
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

            Instance.SlotScalingFormula.SettingChanged += (sender, args) =>
            {
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
                Instance.RefreshCachedConfigStrings();
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateCapacity(bagController);
                }
            };

            Instance.StateCalculationMode.SettingChanged += (sender, args) =>
            {
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    CapacityScalingSystem.RecalculateState(bagController);
                }
            };

            Instance.MovespeedPenaltyFormula.SettingChanged += (sender, args) =>
            {
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
                Instance.RefreshCachedConfigStrings();
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.MassCap.SettingChanged += (sender, args) =>
            {
                Instance.RefreshCachedConfigStrings();
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.EliteFlagMultiplier.SettingChanged += (sender, args) =>
            {
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
                var error = FormulaParser.Validate(Instance.VoidFlagMultiplier.Value);
                if (error != null)
                    Log.Warning($"[PluginConfig] Invalid VoidFlagMultiplier: {error}");
                foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
                {
                    DrifterBossGrabMod.Patches.BagPassengerManager.ForceRecalculateMass(bagController);
                }
            };

            Instance.SlamDamageFormula.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.SlamDamageFormula.Value);
                if (error != null)
                {
                    Log.Warning($"[PluginConfig] Invalid SlamDamageFormula: {error}");
                }

                var overlays = UnityEngine.Object.FindObjectsByType<UI.DamagePreviewOverlay>(FindObjectsSortMode.None);
                foreach (var overlay in overlays)
                {
                    overlay.InvalidateCache();
                }
            };

            Instance.SelectedFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var error = FormulaParser.Validate(Instance.SelectedFlagMultiplier.Value);
                if (error != null)
                {
                    Log.Warning($"[PluginConfig] Invalid FlagMultiplier formula: {error}");
                    return;
                }

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

                Instance.SelectedFlagMultiplier.Value = currentFormula;
            };

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

            Instance._grabbableComponentTypesCache = new LazyCachedValue<HashSet<string>>(() =>
                string.IsNullOrEmpty(Instance.GrabbableComponentTypes.Value)
                    ? new HashSet<string>()
                    : Instance.GrabbableComponentTypes.Value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.Ordinal));

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

            // Wire invalidation on config changes
            Instance.BodyBlacklist.SettingChanged += (sender, args) => { Instance._blacklistCache.Invalidate(); Instance._blacklistCacheWithClones.Invalidate(); };
            Instance.RecoveryObjectBlacklist.SettingChanged += (sender, args) => { Instance._recoveryBlacklistCache.Invalidate(); Instance._recoveryBlacklistCacheWithClones.Invalidate(); };
            Instance.PersistenceBlacklist.SettingChanged += (sender, args) => { Instance._persistenceBlacklistCache.Invalidate(); Instance._persistenceBlacklistCacheWithClones.Invalidate(); };
            Instance.GrabbableComponentTypes.SettingChanged += (sender, args) => Instance._grabbableComponentTypesCache.Invalidate();
            Instance.GrabbableKeywordBlacklist.SettingChanged += (sender, args) => Instance._grabbableKeywordBlacklistCache.Invalidate();

            // Initial refresh of cached config string values
            Instance.RefreshCachedConfigStrings();
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

        // Carousel slot backing config getters (follow SelectedFlag pattern)
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
