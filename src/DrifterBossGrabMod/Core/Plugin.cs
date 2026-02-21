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
        private EventHandler? forwardVelHandler;
        private EventHandler? upwardVelHandler;
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
            SetupFeatureToggleHandlers();
            RegisterGameEvents();

            // Initialize networking
            Networking.BagStateSync.Init(new Harmony(Constants.PluginGuid + ".networking"));
            Networking.ConfigSyncHandler.Init();
        }

        private void RemoveConfigurationEventHandlers()
        {
            PluginConfig.RemoveEventHandlers(
                debugLogsHandler ?? ((sender, args) => { }),
                blacklistHandler ?? ((sender, args) => { }),
                forwardVelHandler ?? ((sender, args) => { }),
                upwardVelHandler ?? ((sender, args) => { }),
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
            forwardVelHandler = Patches.RepossessPatches.OnForwardVelocityChanged;
            PluginConfig.Instance.ForwardVelocityMultiplier.SettingChanged += forwardVelHandler;

            upwardVelHandler = Patches.RepossessPatches.OnUpwardVelocityChanged;
            PluginConfig.Instance.UpwardVelocityMultiplier.SettingChanged += upwardVelHandler;
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
            // When dropdown changes, update float field to show current value for that flag
            PluginConfig.Instance.SelectedFlag.SettingChanged += (sender, args) =>
            {
                var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;
                var flagConfig = PluginConfig.GetFlagMultiplierConfig(selectedFlag);
                var newValue = flagConfig.Value;

                Log.Info($"[SelectedFlag.SettingChanged] Selected flag changed to: {selectedFlag}");
                Log.Info($"[SelectedFlag.SettingChanged] Flag multiplier value: {newValue}");
                Log.Info($"[SelectedFlag.SettingChanged] Setting SelectedFlagMultiplier.Value to: {newValue}");

                PluginConfig.Instance.SelectedFlagMultiplier.Value = newValue;

                // Refresh the FloatField UI to show the updated value
                RefreshFloatFieldUI(PluginConfig.Instance.SelectedFlagMultiplier);
            };

            // When float field changes, update the multiplier for the currently selected flag
            PluginConfig.Instance.SelectedFlagMultiplier.SettingChanged += (sender, args) =>
            {
                var selectedFlag = PluginConfig.Instance.SelectedFlag.Value;
                var flagConfig = PluginConfig.GetFlagMultiplierConfig(selectedFlag);
                flagConfig.Value = PluginConfig.Instance.SelectedFlagMultiplier.Value;
                Log.Info($"[SelectedFlagMultiplier.SettingChanged] Flag {selectedFlag} multiplier updated to: {PluginConfig.Instance.SelectedFlagMultiplier.Value}");
                // Trigger mass recalculation for all bag controllers
                RecalculateAllBaggedMasses();
            };
        }

        private void SetupHudSubTabHandlers()
        {
            // When HUD sub-tab changes, update visibility of settings
            PluginConfig.Instance.SelectedHudSubTab.SettingChanged += (sender, args) =>
            {
                var selectedSubTab = PluginConfig.Instance.SelectedHudSubTab.Value;
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
            PluginConfig.Instance.EnableComponentAnalysisLogs.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };
            PluginConfig.Instance.EnableConfigSync.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };
            PluginConfig.Instance.MassMultiplier.SettingChanged += (sender, args) =>
            {
                PresetManager.OnSettingModified();
                PresetManager.RefreshPresetDropdownUI();
            };

            // Skill settings
            PluginConfig.Instance.SearchRangeMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.ForwardVelocityMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.UpwardVelocityMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
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
            PluginConfig.Instance.BottomlessBagBaseCapacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableStockRefreshClamping.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CycleCooldown.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableMouseWheelScrolling.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.InverseMouseWheelScrolling.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.AutoPromoteMainSeat.SettingChanged += (sender, args) => PresetManager.OnSettingModified();

            // HUD settings
            PluginConfig.Instance.EnableCarouselHUD.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselSpacing.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselCenterOffsetX.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselCenterOffsetY.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselSideOffsetX.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselSideOffsetY.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselSideScale.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselSideOpacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CarouselAnimationDuration.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.BagUIShowIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.BagUIShowWeight.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.BagUIShowName.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.BagUIShowHealthBar.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableDamagePreview.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.DamagePreviewColor.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.UseNewWeightIcon.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.WeightDisplayMode.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.ScaleWeightColor.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableMassCapacityUI.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCapacityUIPositionX.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCapacityUIPositionY.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MassCapacityUIScale.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableSeparators.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableGradient.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.GradientIntensity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityGradientColorStart.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityGradientColorMid.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityGradientColorEnd.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceGradientColorStart.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceGradientColorMid.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceGradientColorEnd.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.GradientIntensity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();

            // Balance settings
            PluginConfig.Instance.EnableBalance.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableAoESlamDamage.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.AoEDamageDistribution.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityScalingMode.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityScalingType.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.CapacityScalingBonusPerCapacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EliteMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.BossMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.ChampionMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.PlayerMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MinionMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.DroneMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MechanicalMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.VoidMassBonusPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.EnableOverencumbrance.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.OverencumbranceMaxPercent.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.UncapCapacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.ToggleMassCapacity.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.StateCalculationModeEnabled.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.StateCalculationMode.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.AllModeMassMultiplier.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MinMovespeedPenalty.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.MaxMovespeedPenalty.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.FinalMovespeedPenaltyLimit.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.UncapBagScale.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
            PluginConfig.Instance.UncapMass.SettingChanged += (sender, args) => PresetManager.OnSettingModified();
        }

        // Refreshes the FloatField UI to display the current ConfigEntry value.
        private void RefreshFloatFieldUI(ConfigEntry<float> configEntry)
        {
            if (!RooInstalled) return;

            // Build the setting token that RiskOfOptions uses to identify UI components
            // Format: {ModGuid}.{Category}.{Name}.{OptionTypeName} (no RISK_OF_OPTIONS prefix)
            string expectedToken = $"{Constants.PluginGuid}.Balance.FlagMultiplier.FLOAT_FIELD".Replace(" ", "_").ToUpper();

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

        // Updates the visibility of HUD settings based on the selected sub-tab.
        public void UpdateHudSubTabVisibility()
        {
            if (!RooInstalled) return;

            var selectedSubTab = PluginConfig.Instance.SelectedHudSubTab.Value;
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
                if (PluginConfig.HudSettingToSubTab.TryGetValue(setting.settingToken, out var subTab))
                {
                    bool shouldShow = subTab == selectedSubTab || selectedSubTab == HudSubTabType.All;

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
                        Log.Info($"[UpdateHudSubTabVisibility] SHOWING: {setting.settingToken} (sub-tab: {subTab})");
                    }
                    else
                    {
                        canvasGroup.alpha = 0f;
                        canvasGroup.blocksRaycasts = false;
                        layoutElement.ignoreLayout = true;
                        hiddenCount++;
                        Log.Info($"[UpdateHudSubTabVisibility] HIDING: {setting.settingToken} (sub-tab: {subTab})");
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
                if (PluginConfig.BalanceSettingToSubTab.TryGetValue(setting.settingToken, out var subTab))
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

            Log.Info($"[UpdateBalanceSubTabVisibility] === SUMMARY === Show: {shownCount}, Hide: {hiddenCount}, Not in mapping: {notFoundCount}");
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
            yield return new UnityEngine.WaitForSeconds(0.2f);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[DelayedBatchSpecialObjectAttributesInitialization] IsDrifterPresent: {DrifterBossGrabPlugin.IsDrifterPresent}");
            }
            if (!DrifterBossGrabPlugin.IsDrifterPresent) yield break;
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            const int batchSize = 50;
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
            yield return new UnityEngine.WaitForSeconds(0.5f);
            PluginConfig.ClearGrabbableComponentTypesCache();
            if (DrifterBossGrabPlugin.IsDrifterPresent)
            {
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            }
            _grabbableComponentTypesUpdateCoroutine = null;
        }

        private static System.Collections.IEnumerator DelayedUpdateHudSubTabVisibility()
        {
            yield return new UnityEngine.WaitForSeconds(0.5f); // Wait for RiskOfOptions UI to initialize
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.UpdateHudSubTabVisibility();
            }
        }

        private static System.Collections.IEnumerator DelayedUpdateBalanceSubTabVisibility()
        {
            yield return new UnityEngine.WaitForSeconds(0.5f); // Wait for RiskOfOptions UI to initialize
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.UpdateBalanceSubTabVisibility();
            }
        }
        private void SetupRiskOfOptions()
        {
            if (!RooInstalled) return;
            ModSettingsManager.SetModDescription("Allows Drifter to grab bosses, NPCs, and environment objects.", Constants.PluginGuid, Constants.PluginName);
            try
            {
                byte[] array = File.ReadAllBytes(System.IO.Path.Combine(DirectoryName, "icon.png"));
                UnityEngine.Texture2D val = new UnityEngine.Texture2D(256, 256);
                UnityEngine.ImageConversion.LoadImage(val, array);
                ModSettingsManager.SetModIcon(UnityEngine.Sprite.Create(val, new UnityEngine.Rect(0f, 0f, 256f, 256f), new UnityEngine.Vector2(0.5f, 0.5f)));
            }
            catch (Exception)
            {
            }
            AddConfigurationOptions();

            // Initialize HUD sub-tab visibility after options are added
            StartCoroutine(DelayedUpdateHudSubTabVisibility());
            // Initialize Balance sub-tab visibility after options are added
            StartCoroutine(DelayedUpdateBalanceSubTabVisibility());
        }

        private void AddConfigurationOptions()
        {
            if (!RooInstalled) return;
            // Preset selection dropdown (at the top of General category)
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
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.SearchRangeMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.ForwardVelocityMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.UpwardVelocityMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.BreakoutTimeMultiplier));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.Instance.MaxSmacks));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.MassMultiplier));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.BodyBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.GrabbableComponentTypes));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.GrabbableKeywordBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.RecoveryObjectBlacklist));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDebugLogs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableComponentAnalysisLogs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableConfigSync));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BottomlessBagEnabled));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.Instance.BottomlessBagBaseCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableStockRefreshClamping));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.CycleCooldown, new RiskOfOptions.OptionConfigs.StepSliderConfig { min = 0f, max = 1f, increment = 0.01f }));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMouseWheelScrolling));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.InverseMouseWheelScrolling));
            ModSettingsManager.AddOption(new KeyBindOption(PluginConfig.Instance.ScrollUpKeybind));
            ModSettingsManager.AddOption(new KeyBindOption(PluginConfig.Instance.ScrollDownKeybind));

            // HUD sub-tab selection dropdown (at the top of HUD category)
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedHudSubTab));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableCarouselHUD));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSpacing));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselCenterOffsetX));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselCenterOffsetY));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSideOffsetX));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSideOffsetY));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSideScale));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselSideOpacity));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CarouselAnimationDuration));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BagUIShowIcon));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BagUIShowWeight));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BagUIShowName));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BagUIShowHealthBar));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableDamagePreview));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.DamagePreviewColor));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UseNewWeightIcon));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.WeightDisplayMode));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ScaleWeightColor));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.AutoPromoteMainSeat));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMassCapacityUI));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIPositionX));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIPositionY));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MassCapacityUIScale));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableSeparators));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableGradient));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.Instance.GradientIntensity, new RiskOfOptions.OptionConfigs.StepSliderConfig { min = 0f, max = 1f, increment = 0.05f }));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorStart));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorMid));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.CapacityGradientColorEnd));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorStart));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorMid));
            ModSettingsManager.AddOption(new ColorOption(PluginConfig.Instance.OverencumbranceGradientColorEnd));

            // Balance configuration options
            // Balance sub-tab selection dropdown (at the top of Balance category)
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedBalanceSubTab));

            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBalance));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UncapCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UncapBagScale));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UncapMass));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ToggleMassCapacity));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.CapacityScalingMode));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.CapacityScalingType));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CapacityScalingBonusPerCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableOverencumbrance));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.OverencumbranceMaxPercent));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.StateCalculationModeEnabled));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.StateCalculationMode));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableAoESlamDamage));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.AoEDamageDistribution));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.AllModeMassMultiplier));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MinMovespeedPenalty));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.MaxMovespeedPenalty));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.FinalMovespeedPenaltyLimit));

            // Character flag multiplier UI options
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.SelectedFlag));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.SelectedFlagMultiplier));
        }
    }
}
