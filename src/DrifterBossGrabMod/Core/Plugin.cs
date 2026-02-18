using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;

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
        private void SetupConfigurationEventHandlers()
        {
            SetupDebugLogsHandler();
            SetupBlacklistHandlers();
            SetupVelocityHandlers();
            SetupGrabbableHandlers();
            SetupGrabbingHandlers();
            SetupPersistenceHandlers();
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
        }

        private void AddConfigurationOptions()
        {
            if (!RooInstalled) return;
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

            // Balance configuration options
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableBalance));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UncapCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UncapBagScale));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.UncapMass));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ToggleMassCapacity));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.CapacityScalingMode));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.Instance.CapacityScalingType));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.CapacityScalingBonusPerCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EliteMassBonusEnabled));
            ModSettingsManager.AddOption(new FloatFieldOption(PluginConfig.Instance.EliteMassBonusPercent));
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
        }
    }
}
