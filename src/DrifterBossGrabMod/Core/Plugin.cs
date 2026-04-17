#nullable enable
using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod
{
    [BepInPlugin(Constants.PluginGuid, Constants.PluginName, Constants.PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    public partial class DrifterBossGrabPlugin : BaseUnityPlugin, IConfigObserver
    {
        public static class Timing
        {
            public const float BatchInitializationDelay = 0.2f;
            public const float GrabbableComponentTypesUpdateDelay = 0.5f;
            public const float HudSubTabVisibilityUpdateDelay = 0.5f;
            public const float BalanceSubTabVisibilityUpdateDelay = 0.5f;
        }

        public static class BatchProcessing
        {
            public const int BatchSize = 50;
        }

        public static class UI
        {
            public const int IconTextureSize = 256;
            public const float IconRectX = 0f;
            public const float IconRectY = 0f;
            public const float IconPivotX = 0.5f;
            public const float IconPivotY = 0.5f;
        }

        public static DrifterBossGrabPlugin? Instance { get; private set; }
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);
        private static UnityEngine.Coroutine? _grabbableComponentTypesUpdateCoroutine;
        public static volatile bool _isSwappingPassengers = false;
        public static float LastCycleClientTime = 0f;
        public static bool IsSwappingPassengers => _isSwappingPassengers;
        public static bool IsDrifterPresent { get; set; } = false;

        private DrifterGrabFeature? _drifterGrabFeature;
        private BottomlessBagFeature? _bottomlessBagFeature;
        private PersistenceFeature? _persistenceFeature;
        private BalanceFeature? _balanceFeature;
        private RecoveryFeature? _recoveryFeature;

        private Harmony? _drifterGrabHarmony;
        private Harmony? _bottomlessBagHarmony;
        private Harmony? _persistenceHarmony;
        private Harmony? _balanceHarmony;
        private Harmony? _recoveryHarmony;
        
        private bool _wasBottomlessBagEnabled;
        private bool _wasPersistenceEnabled;
        private bool _wasBalanceEnabled;
        private bool _wasDrifterGrabEnabled;
        private bool _wasRecoveryEnabled;




        private void InitializeInstance()
        {
            Instance = this;
        }

        private void InitializeCoreSystems()
        {
            Log.Init(Logger);
            PluginConfig.Init(Config);
            Log.EnableDebugLogs = PluginConfig.Instance.EnableDebugLogs.Value;
        }

        private void InitializeFeatures()
        {
            _drifterGrabHarmony = new Harmony(Constants.PluginGuid + ".driftergrab");
            _bottomlessBagHarmony = new Harmony(Constants.PluginGuid + ".bottomlessbag");
            _persistenceHarmony = new Harmony(Constants.PluginGuid + ".persistence");
            _balanceHarmony = new Harmony(Constants.PluginGuid + ".balance");

            _drifterGrabFeature = new DrifterGrabFeature();
            _drifterGrabFeature.Initialize(_drifterGrabHarmony);

            _bottomlessBagFeature = new BottomlessBagFeature();
            _bottomlessBagFeature.Initialize(_bottomlessBagHarmony);

            _persistenceFeature = new PersistenceFeature();
            _persistenceFeature.Initialize(_persistenceHarmony);

            _balanceFeature = new BalanceFeature();
            _balanceFeature.Initialize(_balanceHarmony);

            _recoveryHarmony = new Harmony(Constants.PluginGuid + ".recovery");
            _recoveryFeature = new RecoveryFeature();
            _recoveryFeature.Initialize(_recoveryHarmony);

            PersistenceManager.Initialize();
        }

        public void Awake()
        {
            InitializeInstance();
            InitializeCoreSystems();
            InitializeFeatures();
            ConfigChangeNotifier.Init();
            ConfigChangeNotifier.AddObserver(this);
            SetupConfigurationEventHandlers();
            SetupFeatureToggleHandlers();
            SetupClientPreferenceHandlers();
            InitializeFormulaVariables();
            
            Config.SettingChanged += OnConfigSettingChangedEvent;
            
            PresetManager.CheckAndApplyPresetOnStartup();

            SyncFeatureTrackingState();

            RegisterGameEvents();

            Networking.BagStateSync.Init(new Harmony(Constants.PluginGuid + ".networking"));
            Networking.NetworkMessageRegistry.Initialize();

            Input.InputSetup.Init();

            ProperSave.ProperSaveIntegration.Initialize();
            ProperSave.Spawning.ObjectSpawner.Initialize();

            // Might make future teleporter feature
            new Harmony(Constants.PluginGuid + ".teleportersafety").PatchAll(typeof(Patches.TeleporterSafetyPatches));

            new Harmony(Constants.PluginGuid + ".combatdirector").PatchAll(typeof(Patches.CombatDirectorPatches));
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
                IsDrifterPresent = true;
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                Patches.UIPatches.InitializeMassCapacityUI(body);
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
            DrifterBossGrabPlugin.IsDrifterPresent = false;
            Patches.ZoneDetectionPatches.ResetZoneInversionDetection();
            Patches.SceneExitPatches.ResetCaptureFlag();
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
            if (!DrifterBossGrabPlugin.IsDrifterPresent) yield break;
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(UnityEngine.FindObjectsSortMode.None);
            const int batchSize = BatchProcessing.BatchSize;
            for (int i = 0; i < allObjects.Length; i += batchSize)
            {
                int endIndex = UnityEngine.Mathf.Min(i + batchSize, allObjects.Length);
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
        }

        private static System.Collections.IEnumerator DelayedUpdateDrifterPresence()
        {
            yield return null;
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
            yield return new UnityEngine.WaitForSeconds(Timing.HudSubTabVisibilityUpdateDelay);
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.UpdateHudSubTabVisibility();
            }
        }

        private static System.Collections.IEnumerator DelayedUpdateBalanceSubTabVisibility()
        {
            yield return new UnityEngine.WaitForSeconds(Timing.BalanceSubTabVisibilityUpdateDelay);
            if (DrifterBossGrabPlugin.Instance != null)
            {
                DrifterBossGrabPlugin.Instance.UpdateBalanceSubTabVisibility();
            }
        }

        public void OnDestroy()
        {
            Run.onPlayerFirstCreatedServer -= OnPlayerFirstCreated;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneChanged;
            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            ConfigChangeNotifier.RemoveObserver(this);
            ConfigChangeNotifier.Cleanup();
            RemoveConfigurationEventHandlers();
            RemovePersistenceEventHandlers();
            RemoveFeatureToggleHandlers();
            RemoveClientPreferenceHandlers();
            CleanupFeatures();
            StopCoroutines();
            Patches.UIPatches.CleanupMassCapacityUI();
            Networking.BagStateSync.Cleanup();
            Networking.NetworkMessageRegistry.Cleanup();
            ProperSave.ProperSaveIntegration.Cleanup();
        }

        private void CleanupFeatures()
        {
            PersistenceManager.Cleanup();
            _drifterGrabFeature?.Cleanup(_drifterGrabHarmony!);
            _bottomlessBagFeature?.Cleanup(_bottomlessBagHarmony!);
            _persistenceFeature?.Cleanup(_persistenceHarmony!);
            _balanceFeature?.Cleanup(_balanceHarmony!);
            _recoveryFeature?.Cleanup(_recoveryHarmony!);
        }

        private void StopCoroutines()
        {
            if (_grabbableComponentTypesUpdateCoroutine != null)
            {
                StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
                _grabbableComponentTypesUpdateCoroutine = null;
            }
        }

        public void OnConfigChanged(string key, object value)
        {
        }

        public void Start()
        {
            SetupRiskOfOptions();
        }

        public void Update()
        {
            if (PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                Patches.BottomlessBagPatches.HandleInput();
            }
        }
    }
}
