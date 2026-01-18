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
    public class DrifterBossGrabPlugin : BaseUnityPlugin
    {
        private const short BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE = 85;
        public static DrifterBossGrabPlugin? Instance { get; private set; }
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);
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
        private static UnityEngine.Coroutine? _grabbableComponentTypesUpdateCoroutine;
        public static bool _isSwappingPassengers = false;
        public static bool IsSwappingPassengers => _isSwappingPassengers;
        public static bool IsDrifterPresent { get; set; } = false;
        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            PluginConfig.Init(Config);
            StateManagement.Initialize(PluginConfig.EnableDebugLogs.Value);
            Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;
            PersistenceManager.Initialize();
            Patches.RepossessPatches.Initialize();
            SetupConfigurationEventHandlers();
            ApplyHarmonyPatches();
            Patches.RunLifecyclePatches.Initialize();
            Patches.TeleporterPatches.Initialize();
            RegisterGameEvents();
        }
        public void OnDestroy()
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
            PluginConfig.EnableObjectPersistence.SettingChanged -= persistenceHandler;
            PluginConfig.EnableAutoGrab.SettingChanged -= autoGrabHandler;
            Patches.RunLifecyclePatches.Cleanup();
            Patches.TeleporterPatches.Cleanup();
            PersistenceManager.Cleanup();
            if (Instance != null)
            {
                Instance.StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
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
            debugLogsHandler = (sender, args) =>
            {
                Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;
                StateManagement.UpdateDebugLogging(PluginConfig.EnableDebugLogs.Value);
            };
            PluginConfig.EnableDebugLogs.SettingChanged += debugLogsHandler;
            blacklistHandler = (sender, args) =>
            {
                PluginConfig.ClearBlacklistCache();
            };
            PluginConfig.BodyBlacklist.SettingChanged += blacklistHandler;
            forwardVelHandler = Patches.RepossessPatches.OnForwardVelocityChanged;
            PluginConfig.ForwardVelocityMultiplier.SettingChanged += forwardVelHandler;
            upwardVelHandler = Patches.RepossessPatches.OnUpwardVelocityChanged;
            PluginConfig.UpwardVelocityMultiplier.SettingChanged += upwardVelHandler;
            recoveryBlacklistHandler = (sender, args) =>
            {
                PluginConfig.ClearRecoveryBlacklistCache();
            };
            PluginConfig.RecoveryObjectBlacklist.SettingChanged += recoveryBlacklistHandler;
            grabbableComponentTypesHandler = (sender, args) =>
            {
                if (_grabbableComponentTypesUpdateCoroutine != null)
                {
                    Instance!.StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
                }
                _grabbableComponentTypesUpdateCoroutine = Instance!.StartCoroutine(DelayedGrabbableComponentTypesUpdate());
            };
            PluginConfig.GrabbableComponentTypes.SettingChanged += grabbableComponentTypesHandler;
            grabbableKeywordBlacklistHandler = (sender, args) =>
            {
                PluginConfig.ClearGrabbableKeywordBlacklistCache();
            };
            PluginConfig.GrabbableKeywordBlacklist.SettingChanged += grabbableKeywordBlacklistHandler;
            bossGrabbingHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.EnableBossGrabbing.SettingChanged += bossGrabbingHandler;
            npcGrabbingHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.EnableNPCGrabbing.SettingChanged += npcGrabbingHandler;
            environmentGrabbingHandler = (sender, args) =>
            {
                if (DrifterBossGrabPlugin.IsDrifterPresent)
                {
                    Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
                }
            };
            PluginConfig.EnableEnvironmentGrabbing.SettingChanged += environmentGrabbingHandler;
            lockedObjectGrabbingHandler = (sender, args) =>
            {
            };
            PluginConfig.EnableLockedObjectGrabbing.SettingChanged += lockedObjectGrabbingHandler;
            persistenceHandler = (sender, args) =>
            {
                PersistenceManager.UpdateCachedConfig();
            };
            PluginConfig.EnableObjectPersistence.SettingChanged += persistenceHandler;
            autoGrabHandler = (sender, args) =>
            {
                PersistenceManager.UpdateCachedConfig();
            };
            PluginConfig.EnableAutoGrab.SettingChanged += autoGrabHandler;
            PersistenceManager.UpdateCachedConfig();
        }
        #endregion
        #region Harmony Patching
        private void ApplyHarmonyPatches()
        {
            Harmony harmony = new Harmony("pwdcat.DrifterBossGrab");
            harmony.PatchAll();
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
            PersistenceManager.OnSceneChanged(oldScene, newScene);
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
            if (PluginConfig.EnableDebugLogs.Value)
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
            if (PluginConfig.EnableDebugLogs.Value)
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
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableBossGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableNPCGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableLockedObjectGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableProjectileGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.ProjectileGrabbingSurvivorOnly));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableObjectPersistence));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableAutoGrab));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedBosses));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedNPCs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedEnvironmentObjects));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.PersistenceBlacklist));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.SearchRangeMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.ForwardVelocityMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.UpwardVelocityMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.BreakoutTimeMultiplier));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.MaxSmacks));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.MassMultiplier));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.BodyBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.GrabbableComponentTypes));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.GrabbableKeywordBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.RecoveryObjectBlacklist));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableDebugLogs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableComponentAnalysisLogs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.BottomlessBagEnabled));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.BottomlessBagBaseCapacity));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableMouseWheelScrolling));
            ModSettingsManager.AddOption(new KeyBindOption(PluginConfig.ScrollUpKeybind));
            ModSettingsManager.AddOption(new KeyBindOption(PluginConfig.ScrollDownKeybind));
        }
        #endregion
    }
}