#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using UnityEngine;
using DrifterBossGrabMod.Balance;
using RoR2;

namespace DrifterBossGrabMod.Config
{

    // ========================================================================================
    // PRESET MANAGER
    // ========================================================================================
    public static class PresetManager
    {

        private static bool _isApplyingPreset = false;

        // ========================================================================================
        // PRESET APPLICATION
        // ========================================================================================
        public static void ApplyPreset(PresetType presetType)
        {
            if (presetType == PresetType.Custom)
            {

                return;
            }

            if (!PresetDefinitions.Presets.ContainsKey(presetType))
            {
                Log.Warning($"[ConfigPreset] Preset {presetType} not found in definitions.");
                return;
            }

            _isApplyingPreset = true;
            var presetValues = PresetDefinitions.Presets[presetType];

            try
            {
                int appliedCount = 0;

                foreach (var setting in presetValues)
                {
                    var configEntry = GetConfigEntry(setting.Key);
                    if (configEntry != null)
                    {
                        try
                        {

                            if (setting.Value is bool boolValue)
                            {
                                var boolEntry = configEntry as ConfigEntry<bool>;
                                if (boolEntry != null)
                                {
                                    boolEntry.Value = boolValue;
                                    appliedCount++;
                                }
                            }
                            else if (setting.Value is float floatValue)
                            {
                                var floatEntry = configEntry as ConfigEntry<float>;
                                if (floatEntry != null)
                                {
                                    floatEntry.Value = floatValue;
                                    appliedCount++;
                                }
                            }
                            else if (setting.Value is int intValue)
                            {
                                var intEntry = configEntry as ConfigEntry<int>;
                                if (intEntry != null)
                                {
                                    intEntry.Value = intValue;
                                    appliedCount++;
                                }
                            }
                            else if (setting.Value is string stringValue)
                            {
                                var stringEntry = configEntry as ConfigEntry<string>;
                                if (stringEntry != null)
                                {
                                    stringEntry.Value = stringValue;
                                    appliedCount++;
                                }
                            }
                            else if (setting.Value is Color colorValue)
                            {
                                var colorEntry = configEntry as ConfigEntry<Color>;
                                if (colorEntry != null)
                                {
                                    colorEntry.Value = colorValue;
                                    appliedCount++;
                                }
                            }
                            else
                            {

                                var configEntryType = configEntry.GetType().GetGenericArguments().FirstOrDefault();
                                if (configEntryType != null && configEntryType.IsEnum && setting.Value.GetType() == configEntryType)
                                {

                                    var valueProperty = configEntry.GetType().GetProperty("Value");
                                    if (valueProperty != null)
                                    {
                                        valueProperty.SetValue(configEntry, setting.Value);
                                        appliedCount++;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[ConfigPreset] Failed to apply setting {setting.Key}: {ex.Message}");
                        }
                    }
                }

                PluginConfig.Instance.SelectedPreset.Value = presetType;
                PluginConfig.Instance.LastSelectedPreset.Value = presetType;
                var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;
                var flagConfig = PluginConfig.GetFlagMultiplierConfig(selectedFlag);
                if (flagConfig != null)
                {
                    PluginConfig.Instance.SelectedFlagMultiplier.Value = flagConfig.Value;
                }

                RefreshAllBagControllers();

                RefreshAllRiskOfOptionsUI();
            }
            finally
            {
                _isApplyingPreset = false;
            }
        }

        public static void CheckAndApplyPresetOnStartup()
        {
            var selected = PluginConfig.Instance.SelectedPreset.Value;
            var lastSelected = PluginConfig.Instance.LastSelectedPreset.Value;

            if (selected != lastSelected)
            {
                ApplyPreset(selected);
            }
        }

        // ========================================================================================
        // UI REFRESH HELPERS
        // ========================================================================================
        private static void RefreshAllRiskOfOptionsUI()
        {
            if (!DrifterBossGrabPlugin.RooInstalled) return;
            RefreshAllRiskOfOptionsUIInternal();
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void RefreshAllRiskOfOptionsUIInternal()
        {
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);

            foreach (var setting in allSettings)
            {

                var gameObject = setting.gameObject;
                if (gameObject != null && gameObject.activeSelf)
                {
                    gameObject.SetActive(false);
                    gameObject.SetActive(true);
                }
            }
        }

        // ========================================================================================
        // EVENT HANDLERS
        // ========================================================================================
        public static void OnSettingModified()
        {
            if (_isApplyingPreset) return;
            if (PluginConfig.Instance.SelectedPreset.Value != PresetType.Custom)
            {
                PluginConfig.Instance.SelectedPreset.Value = PresetType.Custom;
            }
        }

        public static void RefreshPresetDropdownUI()
        {
            if (!DrifterBossGrabPlugin.RooInstalled) return;
            RefreshPresetDropdownUIInternal();
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void RefreshPresetDropdownUIInternal()
        {
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);

            foreach (var setting in allSettings)
            {

                var gameObject = setting.gameObject;
                if (gameObject != null && gameObject.activeSelf)
                {
                    gameObject.SetActive(false);
                    gameObject.SetActive(true);
                }
            }
        }

        // ========================================================================================
        // CONFIG MAPPING
        // ========================================================================================
        private static ConfigEntryBase? GetConfigEntry(string settingKey)
        {
            var parts = settingKey.Split('.');
            if (parts.Length != 2)
            {
                return null;
            }

            var category = parts[0];
            var key = parts[1];

            var configEntry = category switch
            {
                "General" => GetGeneralConfigEntry(key),
                "Persistence" => GetPersistenceConfigEntry(key),
                "BottomlessBag" => GetBottomlessBagConfigEntry(key),
                "Hud" => GetHudConfigEntry(key),
                "Balance" => GetBalanceConfigEntry(key),
                "Character Flags" => GetCharacterFlagsConfigEntry(key),
                _ => null
            };

            return configEntry;
        }

        private static ConfigEntryBase? GetGeneralConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "EnableBossGrabbing" => instance.EnableBossGrabbing,
                "EnableNPCGrabbing" => instance.EnableNPCGrabbing,
                "EnableEnvironmentGrabbing" => instance.EnableEnvironmentGrabbing,
                "EnableLockedObjectGrabbing" => instance.EnableLockedObjectGrabbing,
                "ProjectileGrabbingMode" => instance.ProjectileGrabbingMode,
                "SearchRadiusMultiplier" => instance.SearchRadiusMultiplier,
                "BodyBlacklist" => instance.BodyBlacklist,
                "RecoveryObjectBlacklist" => instance.RecoveryObjectBlacklist,
                "GrabbableComponentTypes" => instance.GrabbableComponentTypes,
                "GrabbableKeywordBlacklist" => instance.GrabbableKeywordBlacklist,
                "ComponentChooserSortMode" => instance.ComponentChooserSortModeEntry,
                "ComponentChooserDummy" => instance.ComponentChooserDummyEntry,
                "EnableDebugLogs" => instance.EnableDebugLogs,
                "EnableConfigSync" => instance.EnableConfigSync,
                _ => null
            };
        }

        private static ConfigEntryBase? GetPersistenceConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "EnableObjectPersistence" => instance.EnableObjectPersistence,
                "EnableAutoGrab" => instance.EnableAutoGrab,
                "PersistBaggedBosses" => instance.PersistBaggedBosses,
                "PersistBaggedNPCs" => instance.PersistBaggedNPCs,
                "PersistBaggedEnvironmentObjects" => instance.PersistBaggedEnvironmentObjects,
                "PersistenceBlacklist" => instance.PersistenceBlacklist,
                "AutoGrabDelay" => instance.AutoGrabDelay,
                _ => null
            };
        }

        private static ConfigEntryBase? GetBottomlessBagConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "EnableBottomlessBag" => instance.BottomlessBagEnabled,
                "EnableStockRefreshClamping" => instance.EnableStockRefreshClamping,
                "EnableSuccessiveGrabStockRefresh" => instance.EnableSuccessiveGrabStockRefresh,
                "CycleCooldown" => instance.CycleCooldown,
                "PlayAnimationOnCycle" => instance.PlayAnimationOnCycle,
                "EnableMouseWheelScrolling" => instance.EnableMouseWheelScrolling,
                "InverseMouseWheelScrolling" => instance.InverseMouseWheelScrolling,
                "AutoPromoteMainSeat" => instance.AutoPromoteMainSeat,
                "PrioritizeMainSeat" => instance.PrioritizeMainSeat,
                "SlotScalingFormula" => instance.SlotScalingFormula,
                _ => null
            };
        }

        private static ConfigEntryBase? GetHudConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "EnableCarouselHUD" => instance.EnableCarouselHUD,
                "CarouselSpacing" => instance.CarouselSpacing,
                "CarouselAnimationDuration" => instance.CarouselAnimationDuration,
                "SelectedHudElement" => instance.SelectedHudElement,
                "CenterSlotX" => instance.CenterSlotX,
                "CenterSlotY" => instance.CenterSlotY,
                "CenterSlotScale" => instance.CenterSlotScale,
                "CenterSlotOpacity" => instance.CenterSlotOpacity,
                "CenterSlotShowIcon" => instance.CenterSlotShowIcon,
                "CenterSlotShowWeightIcon" => instance.CenterSlotShowWeightIcon,
                "CenterSlotShowName" => instance.CenterSlotShowName,
                "CenterSlotShowHealthBar" => instance.CenterSlotShowHealthBar,
                "CenterSlotShowSlotNumber" => instance.CenterSlotShowSlotNumber,
                "SideSlotX" => instance.SideSlotX,
                "SideSlotY" => instance.SideSlotY,
                "SideSlotScale" => instance.SideSlotScale,
                "SideSlotOpacity" => instance.SideSlotOpacity,
                "SideSlotShowIcon" => instance.SideSlotShowIcon,
                "SideSlotShowWeightIcon" => instance.SideSlotShowWeightIcon,
                "SideSlotShowName" => instance.SideSlotShowName,
                "SideSlotShowHealthBar" => instance.SideSlotShowHealthBar,
                "SideSlotShowSlotNumber" => instance.SideSlotShowSlotNumber,
                "EnableDamagePreview" => instance.EnableDamagePreview,
                "DamagePreviewColor" => instance.DamagePreviewColor,
                "UseNewWeightIcon" => instance.UseNewWeightIcon,
                "WeightDisplayMode" => instance.WeightDisplayMode,
                "ScaleWeightColor" => instance.ScaleWeightColor,
                "ShowTotalMassOnWeightIcon" => instance.ShowTotalMassOnWeightIcon,
                "ShowOverencumberIcon" => instance.ShowOverencumberIcon,
                "EnableMassCapacityUI" => instance.EnableMassCapacityUI,
                "MassCapacityUIPositionX" => instance.MassCapacityUIPositionX,
                "MassCapacityUIPositionY" => instance.MassCapacityUIPositionY,
                "MassCapacityUIScale" => instance.MassCapacityUIScale,
                "EnableSeparators" => instance.EnableSeparators,
                "GradientIntensity" => instance.GradientIntensity,
                "CapacityGradientColorStart" => instance.CapacityGradientColorStart,
                "CapacityGradientColorMid" => instance.CapacityGradientColorMid,
                "CapacityGradientColorEnd" => instance.CapacityGradientColorEnd,
                "OverencumbranceGradientColorStart" => instance.OverencumbranceGradientColorStart,
                "OverencumbranceGradientColorMid" => instance.OverencumbranceGradientColorMid,
                "OverencumbranceGradientColorEnd" => instance.OverencumbranceGradientColorEnd,
                "EnableBaggedObjectInfo" => instance.EnableBaggedObjectInfo,
                "BaggedObjectInfoX" => instance.BaggedObjectInfoX,
                "BaggedObjectInfoY" => instance.BaggedObjectInfoY,
                "BaggedObjectInfoScale" => instance.BaggedObjectInfoScale,
                "BaggedObjectInfoColor" => instance.BaggedObjectInfoColor,
                _ => null
            };
        }

        private static ConfigEntryBase? GetBalanceConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "EnableBalance" => instance.EnableBalance,
                "BreakoutTimeMultiplier" => instance.BreakoutTimeMultiplier,
                "MaxSmacks" => instance.MaxSmacks,
                "AoEDamageDistribution" => instance.AoEDamageDistribution,
                "MassCapacityFormula" => instance.MassCapacityFormula,
                "SlamDamageFormula" => instance.SlamDamageFormula,
                "OverencumbranceMax" => instance.OverencumbranceMax,
                "StateCalculationMode" => instance.StateCalculationMode,
                "MovespeedPenaltyFormula" => instance.MovespeedPenaltyFormula,
                "BagScaleCap" => instance.BagScaleCap,
                "MassCap" => instance.MassCap,
                "MaxLaunchSpeed" => instance.MaxLaunchSpeed,
                _ => null
            };
        }

        private static ConfigEntryBase? GetCharacterFlagsConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "EliteFlagMultiplier" => instance.EliteFlagMultiplier,
                "BossFlagMultiplier" => instance.BossFlagMultiplier,
                "ChampionFlagMultiplier" => instance.ChampionFlagMultiplier,
                "PlayerFlagMultiplier" => instance.PlayerFlagMultiplier,
                "MinionFlagMultiplier" => instance.MinionFlagMultiplier,
                "DroneFlagMultiplier" => instance.DroneFlagMultiplier,
                "MechanicalFlagMultiplier" => instance.MechanicalFlagMultiplier,
                "VoidFlagMultiplier" => instance.VoidFlagMultiplier,
                "AllFlagMultiplier" => instance.AllFlagMultiplier,
                _ => null
            };
        }

        // ========================================================================================
        // STATE REFRESH
        // ========================================================================================
        private static void RefreshAllBagControllers()
        {
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            foreach (var bagController in bagControllers)
            {
                CapacityScalingSystem.RecalculateCapacity(bagController);
                CapacityScalingSystem.RecalculateMass(bagController);
                CapacityScalingSystem.RecalculateState(bagController);
                CapacityScalingSystem.RecalculatePenalty(bagController);
                Patches.BagPassengerManager.ForceRecalculateMass(bagController);
            }
        }
    }
}
