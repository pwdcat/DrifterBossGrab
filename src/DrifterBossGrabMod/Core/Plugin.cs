using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod
{
    [BepInPlugin(Constants.PluginGuid, Constants.PluginName, Constants.PluginVersion)]
    public class DrifterBossGrabPlugin : BaseUnityPlugin, IConfigObserver
    {
        // Constants for timing delays in coroutines.
        public static class Timing
        {
            // Delay in seconds before batch processing SpecialObjectAttributes initialization.
            // Allows the scene to stabilize before scanning objects.
            public const float BatchInitializationDelay = 0.2f;

            // Delay in seconds before updating grabbable component types cache.
            // Ensures configuration changes are processed before updating.
            public const float GrabbableComponentTypesUpdateDelay = 0.5f;

            // Delay in seconds before updating HUD sub-tab visibility.
            // Allows RiskOfOptions UI to initialize properly.
            public const float HudSubTabVisibilityUpdateDelay = 0.5f;

            // Delay in seconds before updating Balance sub-tab visibility.
            // Allows RiskOfOptions UI to initialize properly.
            public const float BalanceSubTabVisibilityUpdateDelay = 0.5f;
        }

        // Constants for batch processing operations.
        public static class BatchProcessing
        {
            // Number of objects to process per batch during SpecialObjectAttributes initialization.
            // Balances performance and responsiveness during scene loading.
            public const int BatchSize = 50;
        }

        // Constants for UI texture and icon dimensions.
        public static class UI
        {
            // Width and height of the mod icon texture in pixels.
            // Standard size for RiskOfOptions mod icons.
            public const int IconTextureSize = 256;

            // X-coordinate of the mod icon texture rect (always 0 for full texture).
            public const float IconRectX = 0f;

            // Y-coordinate of the mod icon texture rect (always 0 for full texture).
            public const float IconRectY = 0f;

            // X-coordinate of the mod icon sprite pivot point (center of texture).
            public const float IconPivotX = 0.5f;

            // Y-coordinate of the mod icon sprite pivot point (center of texture).
            public const float IconPivotY = 0.5f;
        }

        public static DrifterBossGrabPlugin? Instance { get; private set; }
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);
        private static UnityEngine.Coroutine? _grabbableComponentTypesUpdateCoroutine;
        public static bool _isSwappingPassengers = false;
        public static bool IsSwappingPassengers => _isSwappingPassengers;
        public static bool IsDrifterPresent { get; set; } = false;

        // Feature instances
        private DrifterGrabFeature? _drifterGrabFeature;
        private BottomlessBagFeature? _bottomlessBagFeature;
        private PersistenceFeature? _persistenceFeature;
        private BalanceFeature? _balanceFeature;

        // Harmony instances
        private Harmony? _drifterGrabHarmony;
        private Harmony? _bottomlessBagHarmony;
        private Harmony? _persistenceHarmony;
        private Harmony? _balanceHarmony;

        // Track current feature states
        private bool _wasBottomlessBagEnabled;
        private bool _wasPersistenceEnabled;
        private bool _wasBalanceEnabled;
        private bool _wasDrifterGrabEnabled;

        private ConfigurationComposite? _configurationComposite;

        // Event handlers
        private EventHandler? debugLogsHandler;
        private EventHandler? blacklistHandler;
        private EventHandler? recoveryBlacklistHandler;
        private EventHandler? grabbableComponentTypesHandler;
        private EventHandler? grabbableKeywordBlacklistHandler;
        private EventHandler? bossGrabbingHandler;
        private EventHandler? npcGrabbingHandler;
        private EventHandler? environmentGrabbingHandler;
        private EventHandler? lockedObjectGrabbingHandler;
        private EventHandler? projectileGrabbingModeHandler;
        private EventHandler? persistenceHandler;
        private EventHandler? autoGrabHandler;
        private EventHandler? bottomlessBagToggleHandler;
        private EventHandler? persistenceToggleHandler;
        private EventHandler? balanceToggleHandler;

        private void InitializeInstance()
        {
            Instance = this;
        }

        private void InitializeCoreSystems()
        {
            Log.Init(Logger);
            PluginConfig.Init(Config);
            StateManagement.Initialize(PluginConfig.Instance.EnableDebugLogs.Value);
            Log.EnableDebugLogs = PluginConfig.Instance.EnableDebugLogs.Value;
        }

        private void InitializeConfigurationComposite()
        {
            _configurationComposite = new ConfigurationComposite();

            // Register patches that need initialization/cleanup

            // Create and store Harmony instances
            _drifterGrabHarmony = new Harmony(Constants.PluginGuid + ".driftergrab");
            _bottomlessBagHarmony = new Harmony(Constants.PluginGuid + ".bottomlessbag");
            _persistenceHarmony = new Harmony(Constants.PluginGuid + ".persistence");
            _balanceHarmony = new Harmony(Constants.PluginGuid + ".balance");

            // Create and store feature instances
            _drifterGrabFeature = new DrifterGrabFeature();
            _drifterGrabFeature.Initialize(_drifterGrabHarmony);

            _bottomlessBagFeature = new BottomlessBagFeature();
            _bottomlessBagFeature.Initialize(_bottomlessBagHarmony);

            _persistenceFeature = new PersistenceFeature();
            _persistenceFeature.Initialize(_persistenceHarmony);

            _balanceFeature = new BalanceFeature();
            _balanceFeature.Initialize(_balanceHarmony);

            _wasBottomlessBagEnabled = PluginConfig.Instance.BottomlessBagEnabled.Value;

            _wasPersistenceEnabled = PluginConfig.Instance.EnableObjectPersistence.Value;

            _wasBalanceEnabled = PluginConfig.Instance.EnableBalance.Value;

            _wasDrifterGrabEnabled = PluginConfig.Instance.SelectedPreset.Value != PresetType.Vanilla;

            // Add components to composite
            _configurationComposite.AddComponent((IConfigurable)PatchFactory.Instance);
            _configurationComposite.AddComponent(new PersistenceManagerWrapper());

            // Initialize all components
            _configurationComposite.Initialize();
        }

        public void Awake()
        {
            InitializeInstance();
            InitializeCoreSystems();
            InitializeConfigurationComposite();
            ConfigChangeNotifier.Init();
            ConfigChangeNotifier.AddObserver(this);
            SetupConfigurationEventHandlers();

            // Apply preset if manually modified in config file before UI loads
            PresetManager.CheckAndApplyPresetOnStartup();

            SetupFeatureToggleHandlers();
            RegisterGameEvents();

            // Initialize networking
            Networking.BagStateSync.Init(new Harmony(Constants.PluginGuid + ".networking"));
            Networking.ConfigSyncHandler.Init();

            // Initialize Rewired input actions for controller support
            Input.InputSetup.Init();
        }

        private void RemoveConfigurationEventHandlers()
        {
            PluginConfig.RemoveEventHandlers(
                debugLogsHandler ?? ((sender, args) => { }),
                blacklistHandler ?? ((sender, args) => { }),
                recoveryBlacklistHandler ?? ((sender, args) => { }),
                grabbableComponentTypesHandler ?? ((sender, args) => { }),
                grabbableKeywordBlacklistHandler ?? ((sender, args) => { }),
                bossGrabbingHandler ?? ((sender, args) => { }),
                npcGrabbingHandler ?? ((sender, args) => { }),
                environmentGrabbingHandler ?? ((sender, args) => { }),
                lockedObjectGrabbingHandler ?? ((sender, args) => { }),
                projectileGrabbingModeHandler ?? ((sender, args) => { })
            );
        }

        private void RemovePersistenceEventHandlers()
        {
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged -= persistenceHandler;
            PluginConfig.Instance.EnableAutoGrab.SettingChanged -= autoGrabHandler;
        }

        private void RemoveFeatureToggleHandlers()
        {
            PluginConfig.Instance.BottomlessBagEnabled.SettingChanged -= bottomlessBagToggleHandler;
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged -= persistenceToggleHandler;
            PluginConfig.Instance.EnableBalance.SettingChanged -= balanceToggleHandler;
        }

        private void CleanupConfigurationComposite()
        {
            _configurationComposite?.Cleanup();

            // Cleanup using stored feature instances
            _drifterGrabFeature?.Cleanup(_drifterGrabHarmony!);
            _bottomlessBagFeature?.Cleanup(_bottomlessBagHarmony!);
            _persistenceFeature?.Cleanup(_persistenceHarmony!);
            _balanceFeature?.Cleanup(_balanceHarmony!);
        }

        private void StopCoroutines()
        {
            if (_grabbableComponentTypesUpdateCoroutine != null)
            {
                StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
                _grabbableComponentTypesUpdateCoroutine = null;
            }
        }

        private void SetupFeatureToggleHandlers()
        {
            // Bottomless Bag toggle handler
            bottomlessBagToggleHandler = (sender, args) =>
            {
                bool isEnabled = PluginConfig.Instance.BottomlessBagEnabled.Value;
                if (isEnabled != _wasBottomlessBagEnabled)
                {
                    _bottomlessBagFeature?.Toggle(_bottomlessBagHarmony!, isEnabled);
                    _wasBottomlessBagEnabled = isEnabled;
                     if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[FeatureToggle] BottomlessBag feature {(isEnabled ? "enabled" : "disabled")} at runtime");
                    }
                }
            };
            PluginConfig.Instance.BottomlessBagEnabled.SettingChanged += bottomlessBagToggleHandler;

            // Persistence toggle handler
            persistenceToggleHandler = (sender, args) =>
            {
                bool isEnabled = PluginConfig.Instance.EnableObjectPersistence.Value;
                if (isEnabled != _wasPersistenceEnabled)
                {
                    _persistenceFeature?.Toggle(_persistenceHarmony!, isEnabled);
                    _wasPersistenceEnabled = isEnabled;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[FeatureToggle] Persistence feature {(isEnabled ? "enabled" : "disabled")} at runtime");
                    }
                }
            };
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged += persistenceToggleHandler;

            // Balance toggle handler
            balanceToggleHandler = (sender, args) =>
            {
                bool isEnabled = PluginConfig.Instance.EnableBalance.Value;
                if (isEnabled != _wasBalanceEnabled)
                {
                    _balanceFeature?.Toggle(_balanceHarmony!, isEnabled);
                    _wasBalanceEnabled = isEnabled;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[FeatureToggle] Balance feature {(isEnabled ? "enabled" : "disabled")} at runtime");
                    }
                }
            };
            PluginConfig.Instance.EnableBalance.SettingChanged += balanceToggleHandler;
        }

        public void OnDestroy()
        {
            RemoveConfigurationEventHandlers();
            RemovePersistenceEventHandlers();
            RemoveFeatureToggleHandlers();
            CleanupConfigurationComposite();
            StopCoroutines();
            Patches.UIPatches.CleanupMassCapacityUI();
        }

        public void OnConfigChanged(string key, object value)
        {
            // Handle config changes if needed
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[OnConfigChanged] Config changed: {key} = {value}");
            }
        }

        public void Start()
        {
            SetupRiskOfOptions();
        }

        public void Update()
        {
            // Only handle bottomless bag input when feature is enabled
            if (FeatureState.IsCyclingEnabled)
            {
                Patches.BottomlessBagPatches.HandleInput();
            }
        }
        private void RecalculateAllBaggedMasses()
        {
            // Trigger mass recalculation for all bag controllers
            foreach (var bagController in UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None))
            {
                Patches.BagPassengerManager.ForceRecalculateMass(bagController);
            }
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
                StateManagement.UpdateDebugLogging(PluginConfig.Instance.EnableDebugLogs.Value);
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
            // When dropdown changes, update string field to show current formula for that flag
            PluginConfig.Instance.SelectedFlag.SettingChanged += (sender, args) =>
            {
                var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;

                var flagConfig = PluginConfig.GetFlagMultiplierConfig(selectedFlag);
                PluginConfig.Instance.SelectedFlagMultiplier.Value = flagConfig.Value.ToString();
                RefreshStringInputFieldUI(PluginConfig.Instance.SelectedFlagMultiplier);
            };

            // When formula field changes, validate and trigger recalculation
            PluginConfig.Instance.SelectedFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;
                var formulaString = PluginConfig.Instance.SelectedFlagMultiplier.Value;

                // Validate the formula
                var error = Balance.FormulaParser.Validate(formulaString);
                if (error != null)
                {
                    Log.Warning($"[PluginConfig] Invalid FlagMultiplier formula for {selectedFlag}: {error}");
                    return;
                }

                // Trigger mass recalculation for all bag controllers
                RecalculateAllBaggedMasses();
            };

            PluginConfig.Instance.AllFlagMultiplier.SettingChanged += (sender, args) =>
            {
                RecalculateAllBaggedMasses();
            };
        }

        private void SetupHudSubTabHandlers()
        {
            // When HUD sub-tab changes, update visibility of settings
            PluginConfig.Instance.SelectedHudElement.SettingChanged += (sender, args) =>
            {
                var selectedSubTab = PluginConfig.Instance.SelectedHudElement.Value;
                Log.Info($"[SelectedHudSubTab.SettingChanged] HUD sub-tab changed to: {selectedSubTab}");
                UpdateHudSubTabVisibility();
            };
        }

        private void SetupBalanceSubTabHandlers()
        {
            // When Balance sub-tab changes, update visibility of settings
            PluginConfig.Instance.SelectedBalanceSubTab.SettingChanged += (sender, args) =>
            {
                var selectedSubTab = PluginConfig.Instance.SelectedBalanceSubTab.Value;
                Log.Info($"[SelectedBalanceSubTab.SettingChanged] Balance sub-tab changed to: {selectedSubTab}");
                UpdateBalanceSubTabVisibility();
            };
        }

        private void SetupPresetHandlers()
        {
            // When preset changes, apply the preset to all config entries
            PluginConfig.Instance.SelectedPreset.SettingChanged += (sender, args) =>
            {
                var selectedPreset = PluginConfig.Instance.SelectedPreset.Value;
                Log.Info($"[SelectedPreset.SettingChanged] Preset changed to: {selectedPreset}");
                PluginConfig.Instance.LastSelectedPreset.Value = selectedPreset; // Sync hidden tracker
                PresetManager.ApplyPreset(selectedPreset);

                // Toggle DrifterGrabFeature based on preset (enabled when not Vanilla)
                bool isNowEnabled = selectedPreset != PresetType.Vanilla;
                if (isNowEnabled != _wasDrifterGrabEnabled)
                {
                    _drifterGrabFeature?.Toggle(_drifterGrabHarmony!, isNowEnabled);
                    _wasDrifterGrabEnabled = isNowEnabled;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[FeatureToggle] DrifterGrab feature {(isNowEnabled ? "enabled" : "disabled")} at runtime due to preset change");
                    }
                }
            };
        }

        private void SetupAutoSwitchToCustomHandlers()
        {
            // Add event handlers to all settings to detect modifications and auto-switch to Custom
            // General settings
            PluginConfig.Instance.EnableBossGrabbing.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };
            PluginConfig.Instance.EnableNPCGrabbing.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };
            PluginConfig.Instance.EnableEnvironmentGrabbing.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };
            PluginConfig.Instance.EnableLockedObjectGrabbing.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };
            PluginConfig.Instance.ProjectileGrabbingMode.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };
            PluginConfig.Instance.EnableDebugLogs.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };


            PluginConfig.Instance.EnableConfigSync.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };

            PluginConfig.Instance.BreakoutTimeMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MaxSmacks.SettingChanged += (sender, args) => PresetManager.OnSettingModified();

            // Persistence settings
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableAutoGrab.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.PersistBaggedBosses.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.PersistBaggedNPCs.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.PersistBaggedEnvironmentObjects.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.AutoGrabDelay.SettingChanged += (sender, args) => PresetManager.OnSettingModified();

            // Bottomless Bag settings
            PluginConfig.Instance.BottomlessBagEnabled.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.AddedCapacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableStockRefreshClamping.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CycleCooldown.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.PlayAnimationOnCycle.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableMouseWheelScrolling.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.InverseMouseWheelScrolling.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.AutoPromoteMainSeat.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.PrioritizeMainSeat.SettingChanged += (sender, args) => PresetManager.OnSettingModified();

            // HUD settings
            PluginConfig.Instance.EnableCarouselHUD.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselSpacing.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselAnimationDuration.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            // Per-slot backing configs
            PluginConfig.Instance.CenterSlotX.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotY.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotScale.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotOpacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotShowIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotShowWeightIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotShowName.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotShowHealthBar.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CenterSlotShowSlotNumber.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotX.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotY.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotScale.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotOpacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotShowIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotShowWeightIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotShowName.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotShowHealthBar.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SideSlotShowSlotNumber.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableDamagePreview.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.DamagePreviewColor.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.UseNewWeightIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.WeightDisplayMode.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.ScaleWeightColor.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.ShowTotalMassOnWeightIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableMassCapacityUI.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCapacityUIPositionX.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCapacityUIPositionY.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCapacityUIScale.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableSeparators.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.GradientIntensity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityGradientColorStart.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityGradientColorMid.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityGradientColorEnd.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceGradientColorStart.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceGradientColorMid.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceGradientColorEnd.SettingChanged += (sender, args) => PresetManager.OnSettingModified();


            // Balance settings
            PluginConfig.Instance.EnableBalance.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.AoEDamageDistribution.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.SlotScalingFormula.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCapacityFormula.SettingChanged += (sender, args) => PresetManager.OnSettingModified();

            PluginConfig.Instance.EliteFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.BossFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.ChampionFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.PlayerFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MinionFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.DroneFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MechanicalFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.VoidFlagMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceMax.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.StateCalculationMode.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MovespeedPenaltyFormula.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.BagScaleCap.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCap.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
        }

        // Refreshes the FloatField UI to display the current ConfigEntry value.
        private void RefreshFloatFieldUI(ConfigEntry<float> configEntry)
        {
            if (!RooInstalled) return;

            // Build the setting token from the config entry's definition
            string expectedToken = $"{Constants.PluginGuid}.{configEntry.Definition.Section}.{configEntry.Definition.Key}.FLOAT_FIELD".Replace(" ", "_").ToUpper();

            Log.Info($"[RefreshFloatFieldUI] Attempting to refresh FloatField UI");
            Log.Info($"[RefreshFloatFieldUI] Expected token: {expectedToken}");

            // Find all ModSettingsFloatField components in the scene
            var floatFields = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSettingsFloatField>(UnityEngine.FindObjectsSortMode.None);
            Log.Info($"[RefreshFloatFieldUI] Found {floatFields.Length} FloatField components");

            bool found = false;
            foreach (var floatField in floatFields)
            {
                Log.Info($"[RefreshFloatFieldUI] Checking FloatField with token: {floatField.settingToken}");
                // Check if this floatField matches our setting token
                if (floatField.settingToken == expectedToken)
                {
                    Log.Info($"[RefreshFloatFieldUI] Found matching FloatField!");
                    // Directly update the text field to display the new value
                    // Get the current value from the config entry
                    var newValue = configEntry.Value;
                    Log.Info($"[RefreshFloatFieldUI] Updating text field to: {newValue}");

                    // Access the valueText field (it's a public field, not a property)
                    var valueTextField = typeof(RiskOfOptions.Components.Options.ModSettingsNumericField<float>)
                        .GetField("valueText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (valueTextField != null)
                    {
                        // Get the TMP_InputField object
                        var inputField = valueTextField.GetValue(floatField) as TMPro.TMP_InputField;

                        if (inputField != null)
                        {
                            // Format the value using the formatString from the config
                            var formatStringField = typeof(RiskOfOptions.Components.Options.ModSettingsNumericField<float>)
                                .GetField("formatString", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                            string formatString = formatStringField?.GetValue(floatField) as string ?? "F2";

                            // Get the culture info for decimal separator
                            var separatorProperty = typeof(RiskOfOptions.Components.Options.ModSetting)
                                .GetProperty("Separator", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                            System.Globalization.CultureInfo cultureInfo;
                            if (separatorProperty != null)
                            {
                                var separator = (RiskOfOptions.Options.DecimalSeparator)separatorProperty.GetValue(null);
                                cultureInfo = separator.GetCultureInfo();
                            }
                            else
                            {
                                cultureInfo = System.Globalization.CultureInfo.InvariantCulture;
                            }

                            var formattedValue = string.Format(cultureInfo, formatString, newValue);
                            inputField.text = formattedValue;
                            Log.Info($"[RefreshFloatFieldUI] Text field updated successfully to: {formattedValue}");
                        }
                        else
                        {
                            Log.Warning($"[RefreshFloatFieldUI] valueText field is null!");
                        }
                    }
                    else
                    {
                        Log.Warning($"[RefreshFloatFieldUI] Could not find valueText field!");
                    }

                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Log.Warning($"[RefreshFloatFieldUI] Could not find FloatField with token: {expectedToken}");
            }
        }

        // Refreshes the StringInputField UI to display the current ConfigEntry value.
        private void RefreshStringInputFieldUI(ConfigEntry<string> configEntry)
        {
            if (!RooInstalled) return;

            // Build the setting token from the config entry's definition
            string expectedToken = $"{Constants.PluginGuid}.{configEntry.Definition.Section}.{configEntry.Definition.Key}.STRING_INPUT_FIELD".Replace(" ", "_").ToUpper();

            Log.Info($"[RefreshStringInputFieldUI] Attempting to refresh StringInputField UI");
            Log.Info($"[RefreshStringInputFieldUI] Expected token: {expectedToken}");

            // Find all ModSetting components in the scene
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);
            Log.Info($"[RefreshStringInputFieldUI] Found {allSettings.Length} ModSetting components");

            bool found = false;
            foreach (var setting in allSettings)
            {
                Log.Info($"[RefreshStringInputFieldUI] Checking ModSetting with token: {setting.settingToken}");
                // Check if this setting matches our setting token
                if (setting.settingToken == expectedToken)
                {
                    Log.Info($"[RefreshStringInputFieldUI] Found matching StringInputField! Forcing re-render.");
                    var go = setting.gameObject;
                    if (go != null && go.activeSelf)
                    {
                        go.SetActive(false);
                        go.SetActive(true);
                    }

                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Log.Warning($"[RefreshStringInputFieldUI] Could not find StringInputField with token: {expectedToken}");
            }
        }

        private void RefreshCheckBoxUI(ConfigEntry<bool> configEntry)
        {
            if (!RooInstalled) return;

            // Build the setting token from the config entry's definition
            string expectedToken = $"{Constants.PluginGuid}.{configEntry.Definition.Section}.{configEntry.Definition.Key}.CHECKBOX".Replace(" ", "_").ToUpper();

            // Find all ModSetting components in the scene and match by token
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);

            foreach (var setting in allSettings)
            {
                if (setting.settingToken == expectedToken)
                {
                    // Force re-render by toggling the GameObject
                    var go = setting.gameObject;
                    if (go != null && go.activeSelf)
                    {
                        go.SetActive(false);
                        go.SetActive(true);
                    }
                    return;
                }
            }
        }

        public void UpdateHudSubTabVisibility()
        {
            if (!RooInstalled) return;

            var selectedSubTab = PluginConfig.Instance.SelectedHudElement.Value;
            Log.Info($"[UpdateHudSubTabVisibility] Updating visibility for sub-tab: {selectedSubTab}");

            // Get all ModSetting components in the scene
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);
            Log.Info($"[UpdateHudSubTabVisibility] Found {allSettings.Length} total settings");

            int shownCount = 0;
            int hiddenCount = 0;
            int notFoundCount = 0;

            foreach (var setting in allSettings)
            {
                // Check if this setting belongs to HUD category
                if (!string.IsNullOrEmpty(setting.settingToken) && PluginConfig.HudSettingToSubTab.TryGetValue(setting.settingToken, out var subTabs))
                {
                    bool shouldShow = selectedSubTab == HudElementType.All || System.Array.IndexOf(subTabs, selectedSubTab) >= 0;

                    // Use CanvasGroup and LayoutElement to properly show/hide without destroying
                    var canvasGroup = setting.GetComponent<UnityEngine.CanvasGroup>();
                    if (canvasGroup == null)
                    {
                        canvasGroup = setting.gameObject.AddComponent<UnityEngine.CanvasGroup>();
                    }

                    var layoutElement = setting.GetComponent<UnityEngine.UI.LayoutElement>();
                    if (layoutElement == null)
                    {
                        layoutElement = setting.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    }

                    if (shouldShow)
                    {
                        canvasGroup.alpha = 1f;
                        canvasGroup.blocksRaycasts = true;
                        layoutElement.ignoreLayout = false;
                        shownCount++;
                        Log.Info($"[UpdateHudSubTabVisibility] SHOWING: {setting.settingToken}");
                    }
                    else
                    {
                        canvasGroup.alpha = 0f;
                        canvasGroup.blocksRaycasts = false;
                        layoutElement.ignoreLayout = true;
                        hiddenCount++;
                        Log.Info($"[UpdateHudSubTabVisibility] HIDING: {setting.settingToken}");
                    }
                }
                else
                {
                    notFoundCount++;
                }
            }

            Log.Info($"[UpdateHudSubTabVisibility] === SUMMARY === Show: {shownCount}, Hide: {hiddenCount}, Not in mapping: {notFoundCount}");
        }

        // Updates the visibility of Balance settings based on the selected sub-tab.
        public void UpdateBalanceSubTabVisibility()
        {
            if (!RooInstalled) return;

            var selectedSubTab = PluginConfig.Instance.SelectedBalanceSubTab.Value;
            Log.Info($"[UpdateBalanceSubTabVisibility] Updating visibility for sub-tab: {selectedSubTab}");

            // Get all ModSetting components in the scene
            var allSettings = UnityEngine.Object.FindObjectsByType<RiskOfOptions.Components.Options.ModSetting>(UnityEngine.FindObjectsSortMode.None);
            Log.Info($"[UpdateBalanceSubTabVisibility] Found {allSettings.Length} total settings");

            int shownCount = 0;
            int hiddenCount = 0;
            int notFoundCount = 0;

            foreach (var setting in allSettings)
            {
                // Check if this setting belongs to Balance category
                if (!string.IsNullOrEmpty(setting.settingToken) && PluginConfig.BalanceSettingToSubTab.TryGetValue(setting.settingToken, out var subTab))
                {
                    bool shouldShow = subTab == selectedSubTab || selectedSubTab == BalanceSubTabType.All;

                    // Use CanvasGroup and LayoutElement to properly show/hide without destroying
                    var canvasGroup = setting.GetComponent<UnityEngine.CanvasGroup>();
                    if (canvasGroup == null)
                    {
                        canvasGroup = setting.gameObject.AddComponent<UnityEngine.CanvasGroup>();
                    }

                    var layoutElement = setting.GetComponent<UnityEngine.UI.LayoutElement>();
                    if (layoutElement == null)
                    {
                        layoutElement = setting.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    }

                    if (shouldShow)
                    {
                        canvasGroup.alpha = 1f;
                        canvasGroup.blocksRaycasts = true;
                        layoutElement.ignoreLayout = false;
                        shownCount++;
                        Log.Info($"[UpdateBalanceSubTabVisibility] SHOWING: {setting.settingToken} (sub-tab: {subTab})");
                    }
                    else
                    {
                        canvasGroup.alpha = 0f;
                        canvasGroup.blocksRaycasts = false;
                        layoutElement.ignoreLayout = true;
                        hiddenCount++;
                        Log.Info($"[UpdateBalanceSubTabVisibility] HIDING: {setting.settingToken} (sub-tab: {subTab})");
                    }
                }
                else
                {
                    notFoundCount++;
                }
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[UpdateBalanceSubTabVisibility] === SUMMARY === Show: {shownCount}, Hide: {hiddenCount}, Not in mapping: {notFoundCount}");
        }
        private void RegisterGameEvents()
        {
            Run.onPlayerFirstCreatedServer += OnPlayerFirstCreated;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
            CharacterBody.onBodyStartGlobal += OnBodyStart;
        }

        private void OnBodyStart(CharacterBody body)
        {
            if (body && body.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody"))
            {
                // Check if it's Drifter Survivor
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[Plugin] Drifter body spawned: {body.name}. Triggering object scan.");
                }

                IsDrifterPresent = true;
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();

                // Initialize Mass Capacity UI
                Patches.UIPatches.InitializeMassCapacityUI();
            }
        }

        private static void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController pcm)
        {
            if (pcm != null && pcm.networkUser != null && pcm.networkUser.connectionToClient != null)
            {
                Networking.ConfigSyncHandler.SendConfigToClient(pcm.networkUser.connectionToClient);
            }
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            DrifterBossGrabPlugin.IsDrifterPresent = false; // Reset flag on scene change
            Patches.OtherPatches.ResetZoneInversionDetection();
            PersistenceSceneHandler.Instance.OnSceneChanged(oldScene, newScene);
            if (Instance != null)
            {
                Instance.StartCoroutine(DelayedUpdateDrifterPresence());
                Instance.StartCoroutine(DelayedEnsureSpecialObjectAttributes());
                Instance.StartCoroutine(DelayedBatchSpecialObjectAttributesInitialization());
            }

        }

        private static System.Collections.IEnumerator DelayedEnsureSpecialObjectAttributes()
        {
            yield return null;
            if (DrifterBossGrabPlugin.IsDrifterPresent)
            {
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            }
        }

        private static System.Collections.IEnumerator DelayedBatchSpecialObjectAttributesInitialization()
        {
            yield return new UnityEngine.WaitForSeconds(Timing.BatchInitializationDelay);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[DelayedBatchSpecialObjectAttributesInitialization] IsDrifterPresent: {DrifterBossGrabPlugin.IsDrifterPresent}");
            }
            if (!DrifterBossGrabPlugin.IsDrifterPresent) yield break;
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            const int batchSize = BatchProcessing.BatchSize;
            for (int i = 0; i < allObjects.Length; i += batchSize)
            {
                int endIndex = Mathf.Min(i + batchSize, allObjects.Length);
                for (int j = i; j < endIndex; j++)
                {
                    var obj = allObjects[j];
                    if (obj != null && PluginConfig.IsGrabbable(obj))
                    {
                        Patches.GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);
                    }
                }
                if (endIndex < allObjects.Length)
                {
                    yield return null;
                }
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[DelayedBatchSpecialObjectAttributesInitialization] Completed batched SpecialObjectAttributes initialization for {allObjects.Length} objects");
            }
        }

        private static System.Collections.IEnumerator DelayedUpdateDrifterPresence()
        {
            yield return null; // Wait one frame for objects to spawn
            DrifterBossGrabPlugin.IsDrifterPresent = UnityEngine.Object.FindAnyObjectByType<RoR2.DrifterBagController>() != null;
        }

        private static System.Collections.IEnumerator DelayedGrabbableComponentTypesUpdate()
        {
            yield return new UnityEngine.WaitForSeconds(Timing.GrabbableComponentTypesUpdateDelay);
            PluginConfig.ClearGrabbableComponentTypesCache();
            if (DrifterBossGrabPlugin.IsDrifterPresent)
            {
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            }
            _grabbableComponentTypesUpdateCoroutine = null;
        }

        private static System.Collections.IEnumerator DelayedUpdateHudSubTabVisibility()
        {
            yield return new UnityEngine.WaitForSeconds(Timing.HudSubTabVisibilityUpdateDelay); // Wait for RiskOfOptions UI to initialize
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.UpdateHudSubTabVisibility();
            }
        }

        private static System.Collections.IEnumerator DelayedUpdateBalanceSubTabVisibility()
        {
            yield return new UnityEngine.WaitForSeconds(Timing.BalanceSubTabVisibilityUpdateDelay); // Wait for RiskOfOptions UI to initialize
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.UpdateBalanceSubTabVisibility();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        private void SetupRiskOfOptionsEvents()
        {
            if (!RooInstalled) return;
            try
            {
                var harmony = new Harmony(Constants.PluginGuid + ".roo_ui");
                var targetMethod = AccessTools.Method(typeof(RiskOfOptions.Components.Panel.ModOptionPanelController), "LoadOptionListFromCategory");
                if (targetMethod != null)
                {
                    var postfixMethod = AccessTools.Method(typeof(DrifterBossGrabPlugin), nameof(OnRooCategoryLoaded));
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfixMethod));
                    Log.Info("[SetupRiskOfOptionsEvents] Successfully patched RiskOfOptions category loaded event.");
                }
                else
                {
                    Log.Warning("[SetupRiskOfOptionsEvents] Failed to find LoadOptionListFromCategory method in RiskOfOptions.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[SetupRiskOfOptionsEvents] Exception while patching RiskOfOptions: {ex}");
            }
        }

        private static void OnRooCategoryLoaded(string modGuid)
        {
            if (modGuid == Constants.PluginGuid && Instance != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[OnRooCategoryLoaded] Risk of options category loaded for our mod. Triggering delayed visibility update.");
                }
                Instance.StartCoroutine(DelayedUpdateRooVisibility());
            }
        }

        private static System.Collections.IEnumerator DelayedUpdateRooVisibility()
        {
            yield return new UnityEngine.WaitForEndOfFrame();
            if (Instance != null)
            {
                Instance.UpdateHudSubTabVisibility();
                Instance.UpdateBalanceSubTabVisibility();
            }
        }

        private void SetupRiskOfOptions()
        {
            if (!RooInstalled) return;
            ModSettingsManager.SetModDescription("Allows Drifter to grab bosses, NPCs, and environment objects.", Constants.PluginGuid, Constants.PluginName);
            try
            {
                byte[] array = File.ReadAllBytes(System.IO.Path.Combine(DirectoryName, "icon.png"));
                UnityEngine.Texture2D val = new UnityEngine.Texture2D(UI.IconTextureSize, UI.IconTextureSize);
                UnityEngine.ImageConversion.LoadImage(val, array);
                ModSettingsManager.SetModIcon(UnityEngine.Sprite.Create(val, new UnityEngine.Rect(UI.IconRectX, UI.IconRectY, UI.IconTextureSize, UI.IconTextureSize), new UnityEngine.Vector2(UI.IconPivotX, UI.IconPivotY)));
            }
            catch (Exception ex)
            {
                Log.Error($"[SetupRiskOfOptions] Failed to load mod icon: {ex.Message}\n{ex.StackTrace}");
            }
            AddConfigurationOptions();

            // Initialize HUD sub-tab visibility after options are added
            StartCoroutine(DelayedUpdateHudSubTabVisibility());
            // Initialize Balance sub-tab visibility after options are added
            StartCoroutine(DelayedUpdateBalanceSubTabVisibility());

            SetupRiskOfOptionsEvents();
        }

        private void AddConfigurationOptions()
        {
            if (!RooInstalled) return;
            // Preset selection dropdown
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedPreset));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBossGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableNPCGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableEnvironmentGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableLockedObjectGrabbing));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.ProjectileGrabbingMode));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableObjectPersistence));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableAutoGrab));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedBosses));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedNPCs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedEnvironmentObjects));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.PersistenceBlacklist));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.AutoGrabDelay, new RiskOfOptions.OptionConfigs.StepSliderConfig { min = 0f, max = 10f, increment = 0.1f }));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.BodyBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.GrabbableComponentTypes));
            ModSettingsManager.AddOption(new DrifterBossGrabMod.Config.UI.ComponentChooserOption(PluginConfig.Instance.ComponentChooserDummyEntry, "Component Chooser", "Click to load and toggle components in the GrabbableComponentTypes list."));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.ComponentChooserSortModeEntry));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.RecoveryObjectBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.GrabbableKeywordBlacklist));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDebugLogs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableConfigSync));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BottomlessBagEnabled));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.AddedCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableStockRefreshClamping));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.CycleCooldown, new RiskOfOptions.OptionConfigs.StepSliderConfig { min = 0f, max = 1f, increment = 0.01f }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PlayAnimationOnCycle));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMouseWheelScrolling));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.InverseMouseWheelScrolling));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.AutoPromoteMainSeat));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PrioritizeMainSeat));

            // Balance configuration options
            // Balance sub-tab selection dropdown (at the top of Balance category)
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedBalanceSubTab));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBalance));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SlotScalingFormula));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MassCapacityFormula));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MovespeedPenaltyFormula));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.StateCalculationMode));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.AoEDamageDistribution));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.OverencumbranceMax));

            // Character flag multiplier UI options
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedFlag));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.SelectedFlagMultiplier));

            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.BreakoutTimeMultiplier));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.Instance.MaxSmacks));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.BagScaleCap));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MassCap));

            // HUD element selector and configs
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedHudElement));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableCarouselHUD));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSpacing));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselAnimationDuration));

            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotX));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotY));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotScale));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CenterSlotOpacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowIcon));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowWeightIcon));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowName));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowHealthBar));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.CenterSlotShowSlotNumber));

            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotX));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotY));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotScale));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SideSlotOpacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowIcon));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowWeightIcon));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowName));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowHealthBar));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.SideSlotShowSlotNumber));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDamagePreview));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.DamagePreviewColor));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UseNewWeightIcon));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.WeightDisplayMode));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ScaleWeightColor));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ShowTotalMassOnWeightIcon));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMassCapacityUI));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIPositionX));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIPositionY));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIScale));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableSeparators));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.GradientIntensity, new RiskOfOptions.OptionConfigs.StepSliderConfig { min = 0f, max = 1f, increment = 0.05f }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorStart));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorMid));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorEnd));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorStart));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorMid));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorEnd));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBaggedObjectInfo));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.BaggedObjectInfoX));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.BaggedObjectInfoY));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.BaggedObjectInfoScale));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.BaggedObjectInfoColor));
        }
    }
}
