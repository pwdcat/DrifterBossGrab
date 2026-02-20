using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using RiskOfOptions.Components.Options;
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
                // Don't apply "Custom" - it's just a state indicator
                return;
            }

            if (!PresetDefinitions.Presets.ContainsKey(presetType))
            {
                Log.Warning($"[PresetManager] Preset {presetType} not found in definitions.");
                return;
            }

            _isApplyingPreset = true;
            var presetValues = PresetDefinitions.Presets[presetType];

            try
            {
                Log.Info($"[PresetManager] Applying preset: {presetType}");
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
                                // Handle enum values
                                var configEntryType = configEntry.GetType().GetGenericArguments().FirstOrDefault();
                                if (configEntryType != null && configEntryType.IsEnum && setting.Value.GetType() == configEntryType)
                                {
                                    // Use reflection to set enum value
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
                            Log.Warning($"[PresetManager] Failed to apply setting {setting.Key}: {ex.Message}");
                        }
                    }
                }

                Log.Info($"[PresetManager] Applied {appliedCount} settings for preset {presetType}");

                // Update the preset dropdown
                PluginConfig.Instance.SelectedPreset.Value = presetType;

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

        // Refresh all RiskOfOptions UI components to show updated config values.
        // Forces re-rendering by deactivating and reactivating all settings.
        private static void RefreshAllRiskOfOptionsUI()
        {
            if (!DrifterBossGrabPlugin.RooInstalled) return;

            // Find all ModSetting components in the scene
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

        // Called when any setting is modified to auto-switch to Custom preset.
        public static void OnSettingModified()
        {
            // Don't auto-switch if we're currently applying a preset
            if (_isApplyingPreset)
            {
                return;
            }

            // Auto-switch to Custom if not already
            if (PluginConfig.Instance.SelectedPreset.Value != PresetType.Custom)
            {
                Log.Info($"[PresetManager] Setting modified, switching to Custom preset");
                PluginConfig.Instance.SelectedPreset.Value = PresetType.Custom;
            }
        }

        // Refreshes the preset dropdown UI to show the current preset value
        public static void RefreshPresetDropdownUI()
        {
            if (!DrifterBossGrabPlugin.RooInstalled) return;

            // Find all ModSetting components in scene
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

        // Get a ConfigEntry by its category and key.
        // param settingKey: Format: "Category.SettingName"
        // returns: The ConfigEntry if found, null otherwise.
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
                "Skill" => GetSkillConfigEntry(key),
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
                "EnableDebugLogs" => instance.EnableDebugLogs,
                "EnableComponentAnalysisLogs" => instance.EnableComponentAnalysisLogs,
                "EnableConfigSync" => instance.EnableConfigSync,
                "MassMultiplier" => instance.MassMultiplier,
                _ => null
            };
        }

        private static ConfigEntryBase? GetSkillConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "SearchRangeMultiplier" => instance.SearchRangeMultiplier,
                "ForwardVelocityMultiplier" => instance.ForwardVelocityMultiplier,
                "UpwardVelocityMultiplier" => instance.UpwardVelocityMultiplier,
                "BreakoutTimeMultiplier" => instance.BreakoutTimeMultiplier,
                "MaxSmacks" => instance.MaxSmacks,
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
                "BaseCapacity" => instance.BottomlessBagBaseCapacity,
                "EnableStockRefreshClamping" => instance.EnableStockRefreshClamping,
                "CycleCooldown" => instance.CycleCooldown,
                "EnableMouseWheelScrolling" => instance.EnableMouseWheelScrolling,
                "InverseMouseWheelScrolling" => instance.InverseMouseWheelScrolling,
                "AutoPromoteMainSeat" => instance.AutoPromoteMainSeat,
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
                "CarouselCenterOffsetX" => instance.CarouselCenterOffsetX,
                "CarouselCenterOffsetY" => instance.CarouselCenterOffsetY,
                "CarouselSideOffsetX" => instance.CarouselSideOffsetX,
                "CarouselSideOffsetY" => instance.CarouselSideOffsetY,
                "CarouselSideScale" => instance.CarouselSideScale,
                "CarouselSideOpacity" => instance.CarouselSideOpacity,
                "CarouselAnimationDuration" => instance.CarouselAnimationDuration,
                "BagUIShowIcon" => instance.BagUIShowIcon,
                "BagUIShowWeight" => instance.BagUIShowWeight,
                "BagUIShowName" => instance.BagUIShowName,
                "BagUIShowHealthBar" => instance.BagUIShowHealthBar,
                "EnableDamagePreview" => instance.EnableDamagePreview,
                "DamagePreviewColor" => instance.DamagePreviewColor,
                "UseNewWeightIcon" => instance.UseNewWeightIcon,
                "WeightDisplayMode" => instance.WeightDisplayMode,
                "ScaleWeightColor" => instance.ScaleWeightColor,
                "EnableMassCapacityUI" => instance.EnableMassCapacityUI,
                "MassCapacityUIPositionX" => instance.MassCapacityUIPositionX,
                "MassCapacityUIPositionY" => instance.MassCapacityUIPositionY,
                "MassCapacityUIScale" => instance.MassCapacityUIScale,
                _ => null
            };
        }

        private static ConfigEntryBase? GetBalanceConfigEntry(string key)
        {
            var instance = PluginConfig.Instance;
            return key switch
            {
                "EnableBalance" => instance.EnableBalance,
                "EnableAoESlamDamage" => instance.EnableAoESlamDamage,
                "AoEDamageDistribution" => instance.AoEDamageDistribution,
                "CapacityScalingMode" => instance.CapacityScalingMode,
                "CapacityScalingType" => instance.CapacityScalingType,
                "CapacityScalingBonusPerCapacity" => instance.CapacityScalingBonusPerCapacity,
                "EliteMassBonusPercent" => instance.EliteMassBonusPercent,
                "BossMassBonusPercent" => instance.BossMassBonusPercent,
                "ChampionMassBonusPercent" => instance.ChampionMassBonusPercent,
                "PlayerMassBonusPercent" => instance.PlayerMassBonusPercent,
                "MinionMassBonusPercent" => instance.MinionMassBonusPercent,
                "DroneMassBonusPercent" => instance.DroneMassBonusPercent,
                "MechanicalMassBonusPercent" => instance.MechanicalMassBonusPercent,
                "VoidMassBonusPercent" => instance.VoidMassBonusPercent,
                "EnableOverencumbrance" => instance.EnableOverencumbrance,
                "OverencumbranceMaxPercent" => instance.OverencumbranceMaxPercent,
                "UncapCapacity" => instance.UncapCapacity,
                "ToggleMassCapacity" => instance.ToggleMassCapacity,
                "StateCalculationModeEnabled" => instance.StateCalculationModeEnabled,
                "StateCalculationMode" => instance.StateCalculationMode,
                "AllModeMassMultiplier" => instance.AllModeMassMultiplier,
                "MinMovespeedPenalty" => instance.MinMovespeedPenalty,
                "MaxMovespeedPenalty" => instance.MaxMovespeedPenalty,
                "FinalMovespeedPenaltyLimit" => instance.FinalMovespeedPenaltyLimit,
                "UncapBagScale" => instance.UncapBagScale,
                "UncapMass" => instance.UncapMass,
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
