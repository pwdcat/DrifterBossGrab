using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;

namespace DrifterBossGrabMod
{
    [BepInPlugin(Constants.PluginGuid, Constants.PluginName, Constants.PluginVersion)]
    public class DrifterBossGrabPlugin : BaseUnityPlugin
    {
        // Plugin instance
        public static DrifterBossGrabPlugin Instance { get; private set; }

        // Gets whether Risk of Options is installed
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        // Gets the directory name where the plugin is located
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);

        // Event handler references for cleanup
        private EventHandler debugLogsHandler;
        private EventHandler envInvisHandler;
        private EventHandler envInteractHandler;
        private EventHandler blacklistHandler;
        private EventHandler forwardVelHandler;
        private EventHandler upwardVelHandler;
        private EventHandler recoveryBlacklistHandler;

        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            
            // Initialize configuration
            PluginConfig.Init(Config);

            // Initialize state management with debug logging setting
            StateManagement.Initialize(PluginConfig.EnableDebugLogs.Value);
            Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;

            // Initialize patch systems
            Patches.RepossessPatches.Initialize();

            // Setup configuration event handlers
            SetupConfigurationEventHandlers();

            // Apply all Harmony patches
            ApplyHarmonyPatches();

            // Register for game events
            RegisterGameEvents();
        }

        public void OnDestroy()
        {
            // Remove configuration event handlers to prevent memory leaks
            PluginConfig.RemoveEventHandlers(
                debugLogsHandler,
                envInvisHandler,
                envInteractHandler,
                blacklistHandler,
                forwardVelHandler,
                upwardVelHandler,
                recoveryBlacklistHandler
            );

            // Clear caches
            Patches.InteractableCachingPatches.ClearCache();
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

            envInvisHandler = (sender, args) =>
            {
                // Update cached environment settings
                Patches.BagPatches.UpdateEnvironmentSettings(
                    PluginConfig.EnableEnvironmentInvisibility.Value,
                    PluginConfig.EnableEnvironmentInteractionDisable.Value
                );
            };
            PluginConfig.EnableEnvironmentInvisibility.SettingChanged += envInvisHandler;

            envInteractHandler = (sender, args) =>
            {
                // Update cached environment settings
                Patches.BagPatches.UpdateEnvironmentSettings(
                    PluginConfig.EnableEnvironmentInvisibility.Value,
                    PluginConfig.EnableEnvironmentInteractionDisable.Value
                );
            };
            PluginConfig.EnableEnvironmentInteractionDisable.SettingChanged += envInteractHandler;

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

            // Initialize environment settings cache
            Patches.BagPatches.UpdateEnvironmentSettings(
                PluginConfig.EnableEnvironmentInvisibility.Value,
                PluginConfig.EnableEnvironmentInteractionDisable.Value
            );
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

            // Scene changes to refresh cache
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private static void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController pcm)
        {
            // Mark cache as needing refresh when a player spawns
            Patches.InteractableCachingPatches.MarkCacheForRefresh();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Marked cache for refresh on player spawn");
            }
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            // Mark cache as needing refresh when scene changes
            Patches.InteractableCachingPatches.MarkCacheForRefresh();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Marked cache for refresh on scene change from {oldScene.name} to {newScene.name}");
            }
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

            // Bag options
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.MaxSmacks));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.MassMultiplier));

            // Debug and blacklist
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableDebugLogs));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.BodyBlacklist));

            // Environment options
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentInvisibility));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentInteractionDisable));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableUprightRecovery));

            // Recovery options
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.RecoveryObjectBlacklist));
        }

        #endregion
    }
}