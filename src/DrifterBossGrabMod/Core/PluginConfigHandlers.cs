#nullable enable
using System;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod
{
    public partial class DrifterBossGrabPlugin
    {
        private void SyncFeatureTrackingState()
        {
            _wasBottomlessBagEnabled = PluginConfig.Instance.BottomlessBagEnabled.Value;
            _wasPersistenceEnabled = PluginConfig.Instance.EnableObjectPersistence.Value;
            _wasTeleporterEnabled = PluginConfig.Instance.EnableObjectPersistence.Value && !PluginConfig.IsPersistenceBlacklisted("Teleporter");
            _wasBalanceEnabled = PluginConfig.Instance.EnableBalance.Value;
            _wasDrifterGrabEnabled = PluginConfig.Instance.SelectedPreset.Value != PresetType.Vanilla;
        }

        private void InitializeFormulaVariables()
        {
            Balance.FormulaRegistry.RegisterVariable("H",
                (body) => body?.maxHealth ?? 0f,
                "Character's max health");

            Balance.FormulaRegistry.RegisterVariable("L",
                (body) => body?.level ?? 1f,
                "Character's level");

            Balance.FormulaRegistry.RegisterVariable("C",
                (body) => body != null && body.skillLocator != null && body.skillLocator.utility != null
                    ? body.skillLocator.utility.maxStock : 1f,
                "Utility stock count");

            Balance.FormulaRegistry.RegisterVariable("S",
                (body) => Run.instance ? Run.instance.stageClearCount + 1 : 1,
                "Current stage number");

            Balance.FormulaRegistry.RegisterVariable("MC",
                (body) =>
                {
                    string massCapStr = PluginConfig.Instance.MassCap.Value;
                    if (string.Equals(massCapStr, "INF", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(massCapStr, "Infinity", StringComparison.OrdinalIgnoreCase))
                    {
                        return float.MaxValue;
                    }
                    return float.TryParse(massCapStr, out float massCap) ? massCap : 700f;
                },
                "Mass capacity limit (from config)");

            Balance.FormulaRegistry.RegisterVariable("BH",
                (body) => body?.baseMaxHealth ?? 0f,
                "Character's base max health");

            Balance.FormulaRegistry.RegisterVariable("B",
                (body) => 0f,
                "Base mass (for flag multipliers)");

            Log.Info("[FormulaInit] Default variables initialized");
        }

        private void SetupConfigurationEventHandlers()
        {
            SetupDebugLogsHandler();
            SetupBlacklistHandlers();
            SetupVelocityHandlers();
            SetupGrabbableHandlers();
            SetupGrabbingHandlers();
            SetupPersistenceHandlers();
            SetupCharacterFlagMultiplierHandlers();
            SetupHudSubTabHandlers();
            SetupBalanceSubTabHandlers();
            SetupPresetHandlers();
            SetupAutoSwitchToCustomHandlers();
            PersistenceManager.UpdateCachedConfig();
        }

        private void SetupDebugLogsHandler()
        {
            debugLogsHandler = (sender, args) =>
            {
                Log.EnableDebugLogs = PluginConfig.Instance.EnableDebugLogs.Value;
            };
            PluginConfig.Instance.EnableDebugLogs.SettingChanged += debugLogsHandler;
        }

        private void SetupBlacklistHandlers()
        {
            blacklistHandler = (sender, args) =>
            {
                PluginConfig.ClearBlacklistCache();
            };
            PluginConfig.Instance.BodyBlacklist.SettingChanged += blacklistHandler;

            recoveryBlacklistHandler = (sender, args) =>
            {
                PluginConfig.ClearRecoveryBlacklistCache();
            };
            PluginConfig.Instance.RecoveryObjectBlacklist.SettingChanged += recoveryBlacklistHandler;

            persistenceBlacklistHandler = (sender, args) =>
            {
                PluginConfig.ClearPersistenceBlacklistCache();
            };
            PluginConfig.Instance.PersistenceBlacklist.SettingChanged += persistenceBlacklistHandler;
        }

        private void SetupVelocityHandlers()
        {
        }

        private void SetupGrabbableHandlers()
        {
            grabbableComponentTypesHandler = (sender, args) =>
            {
                if (_grabbableComponentTypesUpdateCoroutine != null)
                {
                    StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
                }
                _grabbableComponentTypesUpdateCoroutine = StartCoroutine(DelayedGrabbableComponentTypesUpdate());
            };
            PluginConfig.Instance.GrabbableComponentTypes.SettingChanged += grabbableComponentTypesHandler;

            grabbableKeywordBlacklistHandler = (sender, args) =>
            {
                PluginConfig.ClearGrabbableKeywordBlacklistCache();
            };
            PluginConfig.Instance.GrabbableKeywordBlacklist.SettingChanged += grabbableKeywordBlacklistHandler;
        }

        private void SetupGrabbingHandlers()
        {
            bossGrabbingHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.Instance.EnableBossGrabbing.SettingChanged += bossGrabbingHandler;

            npcGrabbingHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.Instance.EnableNPCGrabbing.SettingChanged += npcGrabbingHandler;

            environmentGrabbingHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.Instance.EnableEnvironmentGrabbing.SettingChanged += environmentGrabbingHandler;

            lockedObjectGrabbingHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.Instance.EnableLockedObjectGrabbing.SettingChanged += lockedObjectGrabbingHandler;

            projectileGrabbingModeHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.Instance.ProjectileGrabbingMode.SettingChanged += projectileGrabbingModeHandler;
        }

        private void SetupPersistenceHandlers()
        {
            persistenceHandler = (sender, args) =>
            {
                PersistenceManager.UpdateCachedConfig();
            };
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged += persistenceHandler;

            autoGrabHandler = (sender, args) =>
            {
                PersistenceManager.UpdateCachedConfig();
            };
            PluginConfig.Instance.EnableAutoGrab.SettingChanged += autoGrabHandler;
        }

        private void SetupCharacterFlagMultiplierHandlers()
        {
            PluginConfig.Instance.SelectedFlag.SettingChanged += (sender, args) =>
            {
                var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;

                var flagConfig = PluginConfig.GetFlagMultiplierConfig(selectedFlag);
                PluginConfig.Instance.SelectedFlagMultiplier.Value = flagConfig.Value.ToString();
                RefreshStringInputFieldUI(PluginConfig.Instance.SelectedFlagMultiplier);
            };

            PluginConfig.Instance.SelectedFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;
                var formulaString = PluginConfig.Instance.SelectedFlagMultiplier.Value;

                var error = Balance.FormulaParser.Validate(formulaString);
                if (error != null)
                {
                    Log.Warning($"[ConfigValidation] Invalid FlagMultiplier formula for {selectedFlag}: {error}");
                    return;
                }

                var flagConfig = PluginConfig.GetFlagMultiplierConfig(selectedFlag);
                if (flagConfig != null && flagConfig.Value != formulaString)
                {
                    flagConfig.Value = formulaString;
                }

                RecalculateAllBaggedMasses();
            };

            PluginConfig.Instance.AllFlagMultiplier.SettingChanged += (sender, args) =>
            {
                RecalculateAllBaggedMasses();
            };
        }

        private void SetupHudSubTabHandlers()
        {
            PluginConfig.Instance.SelectedHudElement.SettingChanged += (sender, args) =>
            {
                UpdateHudSubTabVisibility();
            };
        }

        private void SetupBalanceSubTabHandlers()
        {
            PluginConfig.Instance.SelectedBalanceSubTab.SettingChanged += (sender, args) =>
            {
                UpdateBalanceSubTabVisibility();
            };
        }

        private void SetupPresetHandlers()
        {
            PluginConfig.Instance.SelectedPreset.SettingChanged += (sender, args) =>
            {
                var selectedPreset = PluginConfig.Instance.SelectedPreset.Value;
                PluginConfig.Instance.LastSelectedPreset.Value = selectedPreset;
                PresetManager.ApplyPreset(selectedPreset);

                bool isNowEnabled = selectedPreset != PresetType.Vanilla;
                if (isNowEnabled != _wasDrifterGrabEnabled)
                {
                    _drifterGrabFeature?.Toggle(_drifterGrabHarmony!, isNowEnabled);
                    _wasDrifterGrabEnabled = isNowEnabled;
                }
            };
        }

        private void SetupAutoSwitchToCustomHandlers()
        {
            RegisterAutoSwitchHandlers(
                PluginConfig.Instance.EnableBossGrabbing,
                PluginConfig.Instance.EnableNPCGrabbing,
                PluginConfig.Instance.EnableEnvironmentGrabbing,
                PluginConfig.Instance.EnableLockedObjectGrabbing,
                PluginConfig.Instance.ProjectileGrabbingMode,
                PluginConfig.Instance.EnableDebugLogs,
                PluginConfig.Instance.EnableConfigSync
            );

            RegisterPresetOnlyHandlers(
                PluginConfig.Instance.BreakoutTimeMultiplier,
                PluginConfig.Instance.MaxSmacks,
                PluginConfig.Instance.EnableObjectPersistence,
                PluginConfig.Instance.EnableAutoGrab,
                PluginConfig.Instance.PersistBaggedBosses,
                PluginConfig.Instance.PersistBaggedNPCs,
                PluginConfig.Instance.PersistBaggedEnvironmentObjects,
                PluginConfig.Instance.AutoGrabDelay,
                PluginConfig.Instance.BottomlessBagEnabled,
                PluginConfig.Instance.AddedCapacity,
                PluginConfig.Instance.EnableStockRefreshClamping,
                PluginConfig.Instance.CycleCooldown,
                PluginConfig.Instance.PlayAnimationOnCycle,
                PluginConfig.Instance.EnableMouseWheelScrolling,
                PluginConfig.Instance.InverseMouseWheelScrolling,
                PluginConfig.Instance.AutoPromoteMainSeat,
                PluginConfig.Instance.PrioritizeMainSeat,
                PluginConfig.Instance.EnableCarouselHUD,
                PluginConfig.Instance.CarouselSpacing,
                PluginConfig.Instance.CarouselAnimationDuration,
                PluginConfig.Instance.CenterSlotX,
                PluginConfig.Instance.CenterSlotY,
                PluginConfig.Instance.CenterSlotScale,
                PluginConfig.Instance.CenterSlotOpacity,
                PluginConfig.Instance.CenterSlotShowIcon,
                PluginConfig.Instance.CenterSlotShowWeightIcon,
                PluginConfig.Instance.CenterSlotShowName,
                PluginConfig.Instance.CenterSlotShowHealthBar,
                PluginConfig.Instance.CenterSlotShowSlotNumber,
                PluginConfig.Instance.SideSlotX,
                PluginConfig.Instance.SideSlotY,
                PluginConfig.Instance.SideSlotScale,
                PluginConfig.Instance.SideSlotOpacity,
                PluginConfig.Instance.SideSlotShowIcon,
                PluginConfig.Instance.SideSlotShowWeightIcon,
                PluginConfig.Instance.SideSlotShowName,
                PluginConfig.Instance.SideSlotShowHealthBar,
                PluginConfig.Instance.SideSlotShowSlotNumber,
                PluginConfig.Instance.EnableDamagePreview,
                PluginConfig.Instance.DamagePreviewColor,
                PluginConfig.Instance.UseNewWeightIcon,
                PluginConfig.Instance.WeightDisplayMode,
                PluginConfig.Instance.ScaleWeightColor,
                PluginConfig.Instance.ShowTotalMassOnWeightIcon,
                PluginConfig.Instance.EnableMassCapacityUI,
                PluginConfig.Instance.MassCapacityUIPositionX,
                PluginConfig.Instance.MassCapacityUIPositionY,
                PluginConfig.Instance.MassCapacityUIScale,
                PluginConfig.Instance.EnableSeparators,
                PluginConfig.Instance.GradientIntensity,
                PluginConfig.Instance.CapacityGradientColorStart,
                PluginConfig.Instance.CapacityGradientColorMid,
                PluginConfig.Instance.CapacityGradientColorEnd,
                PluginConfig.Instance.OverencumbranceGradientColorStart,
                PluginConfig.Instance.OverencumbranceGradientColorMid,
                PluginConfig.Instance.OverencumbranceGradientColorEnd,
                PluginConfig.Instance.EnableBalance,
                PluginConfig.Instance.AoEDamageDistribution,
                PluginConfig.Instance.SlotScalingFormula,
                PluginConfig.Instance.MassCapacityFormula,
                PluginConfig.Instance.SlamDamageFormula,
                PluginConfig.Instance.EliteFlagMultiplier,
                PluginConfig.Instance.BossFlagMultiplier,
                PluginConfig.Instance.ChampionFlagMultiplier,
                PluginConfig.Instance.PlayerFlagMultiplier,
                PluginConfig.Instance.MinionFlagMultiplier,
                PluginConfig.Instance.DroneFlagMultiplier,
                PluginConfig.Instance.MechanicalFlagMultiplier,
                PluginConfig.Instance.VoidFlagMultiplier,
                PluginConfig.Instance.OverencumbranceMax,
                PluginConfig.Instance.StateCalculationMode,
                PluginConfig.Instance.MovespeedPenaltyFormula,
                PluginConfig.Instance.BagScaleCap,
                PluginConfig.Instance.SearchRadiusMultiplier,
                PluginConfig.Instance.MassCap
            );
        }

        private void RecalculateAllBaggedMasses()
        {
            foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(UnityEngine.FindObjectsSortMode.None))
            {
                Patches.BagPassengerManager.ForceRecalculateMass(bagController);
            }
        }
    }
}
