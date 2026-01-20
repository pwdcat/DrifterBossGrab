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
        private const short BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE = 85;
        public static DrifterBossGrabPlugin? Instance { get; private set; }
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);
        private static UnityEngine.Coroutine? _grabbableComponentTypesUpdateCoroutine;
        public static bool _isSwappingPassengers = false;
        public static bool IsSwappingPassengers => _isSwappingPassengers;
        public static bool IsDrifterPresent { get; set; } = false;

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
        private EventHandler? persistenceHandler;
        private EventHandler? autoGrabHandler;
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
            PatchFactory.Instance.RegisterPatch(typeof(Patches.RepossessPatches));
            PatchFactory.Instance.RegisterPatch(typeof(Patches.RunLifecyclePatches));
            PatchFactory.Instance.RegisterPatch(typeof(Patches.TeleporterPatches));
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
            RegisterGameEvents();
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
                lockedObjectGrabbingHandler ?? ((sender, args) => { })
            );
        }

        private void RemovePersistenceEventHandlers()
        {
            PluginConfig.Instance.EnableObjectPersistence.SettingChanged -= persistenceHandler;
            PluginConfig.Instance.EnableAutoGrab.SettingChanged -= autoGrabHandler;
        }

        private void CleanupConfigurationComposite()
        {
            _configurationComposite?.Cleanup();
        }

        private void StopCoroutines()
        {
            if (Instance != null)
            {
                Instance.StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
            }
        }

        public void OnDestroy()
        {
            RemoveConfigurationEventHandlers();
            RemovePersistenceEventHandlers();
            CleanupConfigurationComposite();
            StopCoroutines();
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
            Patches.BottomlessBagPatches.HandleInput();
        }
        #region Configuration Management
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
                    Instance!.StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
                }
                _grabbableComponentTypesUpdateCoroutine = Instance!.StartCoroutine(DelayedGrabbableComponentTypesUpdate());
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
            };
            PluginConfig.Instance.EnableLockedObjectGrabbing.SettingChanged += lockedObjectGrabbingHandler;
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

        #endregion
        #region Game Event Management
        private void RegisterGameEvents()
        {
            Run.onPlayerFirstCreatedServer += OnPlayerFirstCreated;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }
        private static void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController pcm)
        {
        }
        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            DrifterBossGrabPlugin.IsDrifterPresent = false; // Reset flag on scene change
            Patches.OtherPatches.ResetZoneInversionDetection();
            PersistenceSceneHandler.Instance.OnSceneChanged(oldScene, newScene);
            Instance!.StartCoroutine(DelayedUpdateDrifterPresence());
            Instance!.StartCoroutine(DelayedEnsureSpecialObjectAttributes());
            Instance!.StartCoroutine(DelayedBatchSpecialObjectAttributesInitialization());
            Patches.BagPatches.ScanAllSceneComponents();
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
        #endregion
        #region Risk of Options Integration
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
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableProjectileGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.ProjectileGrabbingSurvivorOnly));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableObjectPersistence));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableAutoGrab));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedBosses));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedNPCs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.PersistBaggedEnvironmentObjects));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.Instance.PersistenceBlacklist));
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
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.BottomlessBagEnabled));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.Instance.BottomlessBagBaseCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableStockRefreshClamping));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.Instance.EnableMouseWheelScrolling));
            ModSettingsManager.AddOption(new KeyBindOption(PluginConfig.Instance.ScrollUpKeybind));
            ModSettingsManager.AddOption(new KeyBindOption(PluginConfig.Instance.ScrollDownKeybind));
        }
        #endregion
    }
}

