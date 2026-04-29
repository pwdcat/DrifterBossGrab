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
    // Manages preset application and auto-switching to Custom preset.
    public static class PresetManager
    {
        // Flag to prevent infinite loops when applying presets
        private static bool _isApplyingPreset = false;

        // Apply a preset to all config entries.
        // param presetType: The preset type to apply.
        public static void ApplyPreset(PresetType presetType)
        {
            if (presetType == PresetType.Custom)
            {
                // "Custom" preset is auto-set on manual config changes; it's a state indicator, not a real preset
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
                            // Set value based on its type
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
                                // ConfigEntry.SetCurrentValue lacks public enum overload; reflection required for type safety
                                var configEntryType = configEntry.GetType().GetGenericArguments().FirstOrDefault();
                                if (configEntryType != null && configEntryType.IsEnum && setting.Value.GetType() == configEntryType)
                                {
                                    // Public API doesn't expose enum type handling; private reflection ensures correct enum assignment
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

                // Sync UI to show "Custom" after manual setting changes
                PluginConfig.Instance.SelectedPreset.Value = presetType;
                PluginConfig.Instance.LastSelectedPreset.Value = presetType;

                // Force refresh of all bag controllers to apply changes
                RefreshAllBagControllers();

                // Refresh all RiskOfOptions UI to show updated values
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

        // Forces RiskOfOptions UI refresh by toggling GameObject states (bypasses internal caches)
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
                // Force re-render by deactivating and reactivating the GameObject
                var gameObject = setting.gameObject;
                if (gameObject != null && gameObject.activeSelf)
                {
                    gameObject.SetActive(false);
                    gameObject.SetActive(true);
                }
            }
        }

        // Auto-switch to Custom preset on manual setting change (prevents preset override).
        public static void OnSettingModified()
        {
            if (_isApplyingPreset) return;
            if (PluginConfig.Instance.SelectedPreset.Value != PresetType.Custom)
            {
                PluginConfig.Instance.SelectedPreset.Value = PresetType.Custom;
            }
        }

        // Syncs RiskOfOptions UI to display current preset selection
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
                // Force re-render by deactivating and reactivating GameObject
                var gameObject = setting.gameObject;
                if (gameObject != null && gameObject.activeSelf)
                {
                    gameObject.SetActive(false);
                    gameObject.SetActive(true);
                }
            }
        }

        // Maps category.key strings to config entries for preset value assignment
        private static ConfigEntryBase? GetConfigEntry(string settingKey)
        {
            var parts = settingKey.Split('.');
            if (parts.Length != 2)
            {
                return null;
            }

            var category = parts[0];
            var key = parts[1];

            // Map category names to config entries
            var configEntry = category switch
            {
                "General" => GetGeneralConfigEntry(key),
                "Persistence" => GetPersistenceConfigEntry(key),
                "BottomlessBag" => GetBottomlessBagConfigEntry(key),
                "Hud" => GetHudConfigEntry(key),
                "Balance" => GetBalanceConfigEntry(key),
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
                "AddedCapacity" => instance.AddedCapacity,
                "EnableStockRefreshClamping" => instance.EnableStockRefreshClamping,
                "EnableSuccessiveGrabStockRefresh" => instance.EnableSuccessiveGrabStockRefresh,
                "CycleCooldown" => instance.CycleCooldown,
                "PlayAnimationOnCycle" => instance.PlayAnimationOnCycle,
                "EnableMouseWheelScrolling" => instance.EnableMouseWheelScrolling,
                "InverseMouseWheelScrolling" => instance.InverseMouseWheelScrolling,
                "AutoPromoteMainSeat" => instance.AutoPromoteMainSeat,
                "PrioritizeMainSeat" => instance.PrioritizeMainSeat,
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
                "SlotScalingFormula" => instance.SlotScalingFormula,
                "MassCapacityFormula" => instance.MassCapacityFormula,
                "EliteFlagMultiplier" => instance.EliteFlagMultiplier,
                "BossFlagMultiplier" => instance.BossFlagMultiplier,
                "ChampionFlagMultiplier" => instance.ChampionFlagMultiplier,
                "PlayerFlagMultiplier" => instance.PlayerFlagMultiplier,
                "MinionFlagMultiplier" => instance.MinionFlagMultiplier,
                "DroneFlagMultiplier" => instance.DroneFlagMultiplier,
                "MechanicalFlagMultiplier" => instance.MechanicalFlagMultiplier,
                "VoidFlagMultiplier" => instance.VoidFlagMultiplier,
                "AllFlagMultiplier" => instance.AllFlagMultiplier,
                "SlamDamageFormula" => instance.SlamDamageFormula,
                "SelectedFlag" => instance.SelectedFlag,
                "SelectedFlagMultiplier" => instance.SelectedFlagMultiplier,
                "SelectedBalanceSubTab" => instance.SelectedBalanceSubTab,
                "OverencumbranceMax" => instance.OverencumbranceMax,
                "StateCalculationMode" => instance.StateCalculationMode,
                "MovespeedPenaltyFormula" => instance.MovespeedPenaltyFormula,
                "BagScaleCap" => instance.BagScaleCap,
                "MassCap" => instance.MassCap,
                "MaxLaunchSpeed" => instance.MaxLaunchSpeed,
                _ => null
            };
        }

        // Force refresh of all bag controllers to apply config changes.
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
