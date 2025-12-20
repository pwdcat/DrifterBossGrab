using System;
using System.IO;
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
        // Plugin instance
        public static DrifterBossGrabPlugin Instance { get; private set; }

        // Gets whether Risk of Options is installed
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        // Gets the directory name where the plugin is located
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);

        // Event handler references for cleanup
        private EventHandler debugLogsHandler;
        private EventHandler blacklistHandler;
        private EventHandler forwardVelHandler;
        private EventHandler upwardVelHandler;
        private EventHandler recoveryBlacklistHandler;
        private EventHandler grabbableComponentTypesHandler;
        private EventHandler grabbableKeywordBlacklistHandler;
        private EventHandler bossGrabbingHandler;
        private EventHandler npcGrabbingHandler;
        private EventHandler environmentGrabbingHandler;
        private EventHandler persistenceHandler;
        private EventHandler autoGrabHandler;
        private EventHandler maxPersistHandler;

        // Debounce coroutine for grabbable component types updates
        private static UnityEngine.Coroutine? _grabbableComponentTypesUpdateCoroutine;

        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            
            // Initialize configuration
            PluginConfig.Init(Config);

            // Initialize state management with debug logging setting
            StateManagement.Initialize(PluginConfig.EnableDebugLogs.Value);
            Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;

            // Initialize persistence system
            PersistenceManager.Initialize();

            // Initialize patch systems
            Patches.RepossessPatches.Initialize();

            // Setup configuration event handlers
            SetupConfigurationEventHandlers();

            // Apply all Harmony patches
            ApplyHarmonyPatches();

            // Initialize run lifecycle event handlers
            Patches.RunLifecyclePatches.Initialize();

            // Initialize teleporter event handlers
            Patches.TeleporterPatches.Initialize();

            // Register for game events
            RegisterGameEvents();
        }

        public void OnDestroy()
        {
            // Remove configuration event handlers to prevent memory leaks
            PluginConfig.RemoveEventHandlers(
                debugLogsHandler,
                blacklistHandler,
                forwardVelHandler,
                upwardVelHandler,
                recoveryBlacklistHandler,
                grabbableComponentTypesHandler,
                grabbableKeywordBlacklistHandler,
                bossGrabbingHandler,
                npcGrabbingHandler,
                environmentGrabbingHandler
            );

            // Remove persistence event handlers
            PluginConfig.EnableObjectPersistence.SettingChanged -= persistenceHandler;
            PluginConfig.EnableAutoGrab.SettingChanged -= autoGrabHandler;
            PluginConfig.MaxPersistedObjects.SettingChanged -= maxPersistHandler;

            // Cleanup run lifecycle event handlers
            Patches.RunLifecyclePatches.Cleanup();

            // Cleanup teleporter event handlers
            Patches.TeleporterPatches.Cleanup();

            // Cleanup persistence system
            PersistenceManager.Cleanup();

        }

        public void Start()
        {
            SetupRiskOfOptions();
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
                // Clear blacklist cache so it rebuilds with new value
                PluginConfig.ClearBlacklistCache();
            };
            PluginConfig.BodyBlacklist.SettingChanged += blacklistHandler;

            // Runtime updates for velocity multipliers
            forwardVelHandler = Patches.RepossessPatches.OnForwardVelocityChanged;
            PluginConfig.ForwardVelocityMultiplier.SettingChanged += forwardVelHandler;

            upwardVelHandler = Patches.RepossessPatches.OnUpwardVelocityChanged;
            PluginConfig.UpwardVelocityMultiplier.SettingChanged += upwardVelHandler;

            recoveryBlacklistHandler = (sender, args) =>
            {
                // Clear recovery blacklist cache so it rebuilds with new value
                PluginConfig.ClearRecoveryBlacklistCache();
            };
            PluginConfig.RecoveryObjectBlacklist.SettingChanged += recoveryBlacklistHandler;

            grabbableComponentTypesHandler = (sender, args) =>
            {
                // Debounce the update to avoid excessive processing while typing
                if (_grabbableComponentTypesUpdateCoroutine != null)
                {
                    Instance.StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
                }
                _grabbableComponentTypesUpdateCoroutine = Instance.StartCoroutine(DelayedGrabbableComponentTypesUpdate());
            };
            PluginConfig.GrabbableComponentTypes.SettingChanged += grabbableComponentTypesHandler;

            grabbableKeywordBlacklistHandler = (sender, args) =>
            {
                // Clear grabbable keyword blacklist cache so it rebuilds with new value
                PluginConfig.ClearGrabbableKeywordBlacklistCache();
            };
            PluginConfig.GrabbableKeywordBlacklist.SettingChanged += grabbableKeywordBlacklistHandler;

            bossGrabbingHandler = (sender, args) =>
            {
                // Update existing SpecialObjectAttributes based on new boss grabbing setting
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            };
            PluginConfig.EnableBossGrabbing.SettingChanged += bossGrabbingHandler;

            npcGrabbingHandler = (sender, args) =>
            {
                // Update existing SpecialObjectAttributes based on new NPC grabbing setting
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            };
            PluginConfig.EnableNPCGrabbing.SettingChanged += npcGrabbingHandler;

            environmentGrabbingHandler = (sender, args) =>
            {
                // Update existing SpecialObjectAttributes based on new environment grabbing setting
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            };
            PluginConfig.EnableEnvironmentGrabbing.SettingChanged += environmentGrabbingHandler;

            // Persistence event handlers
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

            maxPersistHandler = (sender, args) =>
            {
                PersistenceManager.UpdateCachedConfig();
            };
            PluginConfig.MaxPersistedObjects.SettingChanged += maxPersistHandler;

            // Initialize caches
            PersistenceManager.UpdateCachedConfig();
        }

        #endregion

        #region Harmony Patching

        private void ApplyHarmonyPatches()
        {
            Harmony harmony = new Harmony("com.DrifterBossGrab");
            harmony.PatchAll();
        }

        #endregion

        #region Game Event Management

        private void RegisterGameEvents()
        {
            // Player spawn event to refresh cache
            Run.onPlayerFirstCreatedServer += OnPlayerFirstCreated;

            // Scene changes to refresh cache and handle persistence
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private static void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController pcm)
        {
            // Caching system removed - no cache to refresh
            // SpecialObjectAttributes system handles object discovery natively

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Player spawned - SpecialObjectAttributes system active");
            }
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            // Caching system removed - no cache to refresh
            // SpecialObjectAttributes system handles object discovery natively

            // Reset zone inversion detection for new stage
            Patches.OtherPatches.ResetZoneInversionDetection();

            // Handle persistence restoration
            PersistenceManager.OnSceneChanged(oldScene, newScene);

            // Ensure all grabbable objects have SpecialObjectAttributes for grabbing (delayed to allow objects to spawn)
            Instance.StartCoroutine(DelayedEnsureSpecialObjectAttributes());

            // Batch initialize SpecialObjectAttributes for better performance
            Instance.StartCoroutine(DelayedBatchSpecialObjectAttributesInitialization());

            // Scan all scene components if component analysis is enabled
            Patches.BagPatches.ScanAllSceneComponents();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Scene changed from {oldScene.name} to {newScene.name} - SpecialObjectAttributes system active");
            }
        }

        private static System.Collections.IEnumerator DelayedEnsureSpecialObjectAttributes()
        {
            // Wait one frame to allow objects to spawn
            yield return null;
            Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
        }

        private static System.Collections.IEnumerator DelayedBatchSpecialObjectAttributesInitialization()
        {
            // Wait slightly longer than the regular ensure to allow all objects to spawn
            yield return new UnityEngine.WaitForSeconds(0.2f);

            // Batch process objects in smaller chunks to avoid frame drops
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            const int batchSize = 50; // Process 50 objects per frame

            for (int i = 0; i < allObjects.Length; i += batchSize)
            {
                int endIndex = Mathf.Min(i + batchSize, allObjects.Length);

                // Process this batch
                for (int j = i; j < endIndex; j++)
                {
                    var obj = allObjects[j];
                    if (obj != null && PluginConfig.IsGrabbable(obj))
                    {
                        Patches.GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);
                    }
                }

                // Yield to next frame if we have more batches to process
                if (endIndex < allObjects.Length)
                {
                    yield return null;
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Completed batched SpecialObjectAttributes initialization for {allObjects.Length} objects");
            }
        }

        private static System.Collections.IEnumerator DelayedGrabbableComponentTypesUpdate()
        {
            // Wait 0.5 seconds to debounce updates while typing
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Clear grabbable component types cache so it rebuilds with new value
            PluginConfig.ClearGrabbableComponentTypesCache();

            // Update existing SpecialObjectAttributes based on new setting
            Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();

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
                // Icon loading failed - continue without icon
            }

            // Add configuration options to the Risk of Options interface
            AddConfigurationOptions();
        }

        private void AddConfigurationOptions()
        {
            if (!RooInstalled) return;

            // Repossess options
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.SearchRangeMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.BreakoutTimeMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.ForwardVelocityMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.UpwardVelocityMultiplier));

            // General grabbing options
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableBossGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableNPCGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentGrabbing));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.GrabbableComponentTypes));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.GrabbableKeywordBlacklist));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableComponentAnalysisLogs));

            // Bag options
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.MaxSmacks));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.MassMultiplier));

            // Debug and blacklist
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableDebugLogs));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.BodyBlacklist));


            // Recovery options
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.RecoveryObjectBlacklist));

            // Persistence options
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableObjectPersistence));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableAutoGrab));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.MaxPersistedObjects));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedBosses));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedNPCs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedEnvironmentObjects));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.PersistenceBlacklist));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.OnlyPersistCurrentlyBagged));
        }

        #endregion
    }
}