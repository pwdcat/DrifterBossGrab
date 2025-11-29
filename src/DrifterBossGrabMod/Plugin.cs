using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;
using HarmonyLib;
using UnityEngine;
using EntityStates;
using EntityStates.Drifter;

namespace DrifterBossGrabMod
{ 
    // Constants
    internal static class Constants
    {
        public const string LogPrefix = "[DrifterBossGrab]";
        public const string RepossessSuccessSound = "Play_drifter_repossess_success";
        public const string FullBodyOverride = "FullBody, Override";
        public const string SuffocateHit = "SuffocateHit";
        public const string SuffocatePlaybackRate = "Suffocate.playbackRate";
        public const string CloneSuffix = "(Clone)";
    }

    // Configuration class for the Drifter Boss Grab mod settings
    internal static class PluginConfig
    {
        public static ConfigEntry<float> SearchRangeMultiplier { get; private set; }
        public static ConfigEntry<float> BreakoutTimeMultiplier { get; private set; }
        public static ConfigEntry<float> ForwardVelocityMultiplier { get; private set; }
        public static ConfigEntry<float> UpwardVelocityMultiplier { get; private set; }
        public static ConfigEntry<bool> EnableBossGrabbing { get; private set; }
        public static ConfigEntry<bool> EnableNPCGrabbing { get; private set; }
        public static ConfigEntry<bool> EnableEnvironmentGrabbing { get; private set; }
        public static ConfigEntry<int> MaxSmacks { get; private set; }
        public static ConfigEntry<string> MassMultiplier { get; private set; }
        public static ConfigEntry<bool> EnableDebugLogs { get; private set; }
        public static ConfigEntry<string> BodyBlacklist { get; private set; }
        public static ConfigEntry<bool> EnableEnvironmentInvisibility { get; private set; }
        public static ConfigEntry<bool> EnableEnvironmentInteractionDisable { get; private set; }

        internal static HashSet<string>? _blacklistCache;
        internal static HashSet<string>? _blacklistCacheWithClones;
        private static string? _lastBlacklistValue;

        public static bool IsBlacklisted(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string currentValue = BodyBlacklist.Value;
            if (_blacklistCache == null || _lastBlacklistValue != currentValue)
            {
                _lastBlacklistValue = currentValue;
                _blacklistCache = string.IsNullOrEmpty(currentValue)
                    ? new HashSet<string>()
                    : currentValue.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _blacklistCacheWithClones = new HashSet<string>(_blacklistCache, StringComparer.OrdinalIgnoreCase);
                foreach (var item in _blacklistCache)
                {
                    _blacklistCacheWithClones.Add(item + Constants.CloneSuffix);
                }
            }
            return _blacklistCacheWithClones.Contains(name);
        }

        public static void Init(ConfigFile cfg)
        {
            SearchRangeMultiplier = cfg.Bind("Repossess", "SearchRangeMultiplier", 1.0f, "Multiplier for Drifter's repossess search range");
            BreakoutTimeMultiplier = cfg.Bind("Bag", "BreakoutTimeMultiplier", 1.0f, "Multiplier for how long bagged enemies take to break out");
            ForwardVelocityMultiplier = cfg.Bind("Repossess", "ForwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess forward velocity");
            UpwardVelocityMultiplier = cfg.Bind("Repossess", "UpwardVelocityMultiplier", 1.0f, "Multiplier for Drifter's repossess upward velocity");
            EnableBossGrabbing = cfg.Bind("General", "EnableBossGrabbing", true, "Enable grabbing of boss enemies");
            EnableNPCGrabbing = cfg.Bind("General", "EnableNPCGrabbing", false, "Enable grabbing of NPCs with ungrabbable flag");
            EnableEnvironmentGrabbing = cfg.Bind("General", "EnableEnvironmentGrabbing", false, "Enable grabbing of environment objects like teleporters, chests, shrines");
            MaxSmacks = cfg.Bind("Bag", "MaxSmacks", 3, new ConfigDescription("Maximum number of hits before bagged enemies break out", new AcceptableValueRange<int>(1, 100)));
            MassMultiplier = cfg.Bind("Bag", "MassMultiplier", "1", "Multiplier for the mass of bagged objects");
            EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false, "Enable debug logging");
            BodyBlacklist = cfg.Bind("General", "BodyBlacklist", "HeaterPodBodyNoRespawn,GenericPickup",
                "Comma-separated list of body names to never grab.\n" +
                "Example: SolusWingBody,Teleporter1,ShrineHalcyonite,PortalShop\n" +
                "Automatically handles (Clone) - just enter the base name.\n" +
                "Use debug logs to see body names, case-insensitive matching");
            EnableEnvironmentInvisibility = cfg.Bind("General", "EnableEnvironmentInvisibility", true, "Make grabbed environment objects invisible while in the bag");
            EnableEnvironmentInteractionDisable = cfg.Bind("General", "EnableEnvironmentInteractionDisable", true, "Disable interactions for grabbed environment objects while in the bag");
        }
    }

    // Main plugin class 
    [BepInPlugin("com.DrifterBossGrab.DrifterBossGrab", "DrifterBossGrab", "1.2.0")]
    public class DrifterBossGrabPlugin : BaseUnityPlugin
    {
        // Gets whether Risk of Options is installed
        internal static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");
        internal static DrifterBossGrabPlugin Instance { get; private set; }

        // Gets the directory name where the plugin is located
        internal string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);

        // Cache for IInteractable objects
        private static HashSet<GameObject> cachedInteractables = new HashSet<GameObject>();
        private static bool isCacheInitialized = false;
        private const int MAX_CACHE_SIZE = 1000;
        private static readonly object cacheLock = new object();

        // Cached config values
        private static bool cachedDebugLogsEnabled;
        private static bool cachedEnableEnvironmentInvisibility;
        private static bool cachedEnableEnvironmentInteractionDisable;
        private static bool cacheNeedsRefresh = false;
        private static FieldInfo? forwardVelocityField;
        private static FieldInfo? upwardVelocityField;
        private static float? originalForwardVelocity;
        private static float? originalUpwardVelocity;

        // Event handler references for cleanup
        private EventHandler debugLogsHandler;
        private EventHandler envInvisHandler;
        private EventHandler envInteractHandler;
        private EventHandler blacklistHandler;
        private EventHandler forwardVelHandler;
        private EventHandler upwardVelHandler;

        // Disables renderers on the given GameObject for invisibility
        private static void DisableRenderersForInvisibility(GameObject obj, Dictionary<Renderer, bool> originalStates)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (!originalStates.ContainsKey(renderer))
                {
                    originalStates[renderer] = renderer.enabled;
                }
                renderer.enabled = false;
            }
        }

        // Disables non-trigger colliders on the given GameObject
        private static void DisableColliders(GameObject obj, Dictionary<Collider, bool> originalStates)
        {
            Collider[] colliders = obj.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                if (!originalStates.ContainsKey(col))
                {
                    originalStates[col] = col.enabled;
                }
                if (!col.isTrigger)
                {
                    col.enabled = false;
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Disabled non-trigger collider {col.name} on {obj.name}");
                    }
                }
                else if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} Kept trigger collider {col.name} enabled on {obj.name}");
                }
            }
        }

        // Disables the IInteractable component
        private static void DisableInteractable(IInteractable interactable, Dictionary<MonoBehaviour, bool> originalStates)
        {
            var interactableMB = interactable as MonoBehaviour;
            if (interactableMB != null)
            {
                if (!originalStates.ContainsKey(interactableMB))
                {
                    originalStates[interactableMB] = interactableMB.enabled;
                }
                interactableMB.enabled = false;
            }
        }

        // Disables all colliders on enemies to prevent collision issues
        private static void DisableMovementColliders(GameObject obj, Dictionary<GameObject, bool> originalStates)
        {
            var modelLocator = obj.GetComponent<ModelLocator>();
            if (modelLocator && modelLocator.modelTransform)
            {
                foreach (Transform child in modelLocator.modelTransform.GetComponentsInChildren<Transform>(true))
                {
                    var collider = child.GetComponent<Collider>();
                    int layer = child.gameObject.layer;
                    string layerName = LayerMask.LayerToName(layer);
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Checking {child.name}, layer: {layer} ({layerName}), hasCollider: {collider != null}");
                    }
                    if (collider != null)
                    {
                        if (!originalStates.ContainsKey(child.gameObject))
                        {
                            originalStates[child.gameObject] = child.gameObject.activeSelf;
                        }
                        child.gameObject.SetActive(false);
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Disabled {child.name} due to collider on layer {layerName}");
                        }
                    }
                }
            }
        }

        // Restores renderer states
        private static void RestoreRenderers(Dictionary<Renderer, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalStates.Clear();
        }

        // Restores all collider states
        private static void RestoreColliders(Dictionary<Collider, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Restored collider {kvp.Key.name} (trigger: {kvp.Key.isTrigger}) to enabled={kvp.Value}");
                    }
                }
            }
            originalStates.Clear();
        }

        // Restores IInteractable states
        private static void RestoreInteractables(Dictionary<MonoBehaviour, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalStates.Clear();
        }

        // Restores collider isTrigger states
        private static void RestoreIsTrigger(Dictionary<Collider, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.isTrigger = kvp.Value;
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Restored collider {kvp.Key.name} isTrigger to {kvp.Value}");
                    }
                }
            }
            originalStates.Clear();
        }

        // Restores collider states
        private static void RestoreMovementColliders(Dictionary<GameObject, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetActive(kvp.Value);
                }
            }
            originalStates.Clear();
        }

        // Component that stores the original states on landing
        private class GrabbedObjectState : MonoBehaviour
        {
            public Dictionary<Collider, bool> originalColliderStates = new Dictionary<Collider, bool>();
            public Dictionary<Collider, bool> originalIsTrigger = new Dictionary<Collider, bool>();
            public Dictionary<MonoBehaviour, bool> originalInteractableStates = new Dictionary<MonoBehaviour, bool>();
            public Dictionary<GameObject, bool> originalMovementStates = new Dictionary<GameObject, bool>();
            public Dictionary<Renderer, bool> originalRendererStates = new Dictionary<Renderer, bool>();
            public Dictionary<Highlight, bool> originalHighlightStates = new Dictionary<Highlight, bool>();

            public void RestoreAllStates()
            {
                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} Restoring all states for {gameObject.name} on landing");
                }

                // Restore all states
                RestoreColliders(originalColliderStates);
                RestoreIsTrigger(originalIsTrigger);
                RestoreInteractables(originalInteractableStates);
                RestoreMovementColliders(originalMovementStates);
                RestoreRenderers(originalRendererStates);

                // Restore highlight states
                foreach (var kvp in originalHighlightStates)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = kvp.Value;
                    }
                }
                originalHighlightStates.Clear();

                // Re-enable Rigidbody
                Rigidbody rb = gameObject.GetComponent<Rigidbody>();
                if (rb)
                {
                    rb.isKinematic = false;
                    rb.detectCollisions = true;
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Restored Rigidbody for {gameObject.name}");
                    }
                }

                // Remove this component since restoration is complete
                Destroy(this);
            }
        }
 
        // Initialize
        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            PluginConfig.Init(Config);

            // Initialize cached config values
            cachedDebugLogsEnabled = PluginConfig.EnableDebugLogs.Value;
            cachedEnableEnvironmentInvisibility = PluginConfig.EnableEnvironmentInvisibility.Value;
            cachedEnableEnvironmentInteractionDisable = PluginConfig.EnableEnvironmentInteractionDisable.Value;
            Log.EnableDebugLogs = cachedDebugLogsEnabled;

            // Cache reflection fields
            forwardVelocityField = AccessTools.Field(typeof(EntityStates.Drifter.Repossess), "forwardVelocity");
            upwardVelocityField = AccessTools.Field(typeof(EntityStates.Drifter.Repossess), "upwardVelocity");

            // Subscribe to config changes for real-time updates
            debugLogsHandler = (sender, args) =>
            {
                cachedDebugLogsEnabled = PluginConfig.EnableDebugLogs.Value;
                Log.EnableDebugLogs = cachedDebugLogsEnabled;
            };
            PluginConfig.EnableDebugLogs.SettingChanged += debugLogsHandler;

            envInvisHandler = (sender, args) =>
            {
                cachedEnableEnvironmentInvisibility = PluginConfig.EnableEnvironmentInvisibility.Value;
            };
            PluginConfig.EnableEnvironmentInvisibility.SettingChanged += envInvisHandler;

            envInteractHandler = (sender, args) =>
            {
                cachedEnableEnvironmentInteractionDisable = PluginConfig.EnableEnvironmentInteractionDisable.Value;
            };
            PluginConfig.EnableEnvironmentInteractionDisable.SettingChanged += envInteractHandler;

            blacklistHandler = (sender, args) =>
            {
                // Clear cache so it rebuilds with new value
                PluginConfig._blacklistCache = null;
                PluginConfig._blacklistCacheWithClones = null;
            };
            PluginConfig.BodyBlacklist.SettingChanged += blacklistHandler;

            // Runtime updates for multipliers
            forwardVelHandler = (sender, args) =>
            {
                if (originalForwardVelocity.HasValue)
                {
                    forwardVelocityField.SetValue(null, originalForwardVelocity.Value * PluginConfig.ForwardVelocityMultiplier.Value);
                }
            };
            PluginConfig.ForwardVelocityMultiplier.SettingChanged += forwardVelHandler;

            upwardVelHandler = (sender, args) =>
            {
                if (originalUpwardVelocity.HasValue)
                {
                    upwardVelocityField.SetValue(null, originalUpwardVelocity.Value * PluginConfig.UpwardVelocityMultiplier.Value);
                }
            };
            PluginConfig.UpwardVelocityMultiplier.SettingChanged += upwardVelHandler;

            Harmony harmony = new Harmony("com.DrifterBossGrab");
            harmony.PatchAll();

            // Player spawn event to refresh cache
            Run.onPlayerFirstCreatedServer += OnPlayerFirstCreated;

            // Scene changes to refresh cache
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        // Cleanup event handlers to prevent memory leaks
        public void OnDestroy()
        {
            PluginConfig.EnableDebugLogs.SettingChanged -= debugLogsHandler;
            PluginConfig.EnableEnvironmentInvisibility.SettingChanged -= envInvisHandler;
            PluginConfig.EnableEnvironmentInteractionDisable.SettingChanged -= envInteractHandler;
            PluginConfig.BodyBlacklist.SettingChanged -= blacklistHandler;
            PluginConfig.ForwardVelocityMultiplier.SettingChanged -= forwardVelHandler;
            PluginConfig.UpwardVelocityMultiplier.SettingChanged -= upwardVelHandler;
        }

        private static void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController pcm)
        {
            // Mark cache as needing refresh when a player spawns
            cacheNeedsRefresh = true;

            if (cachedDebugLogsEnabled)
            {
                Log.Info($"{Constants.LogPrefix} Marked cache for refresh on player spawn");
            }
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            // Mark cache as needing refresh when scene changes
            cacheNeedsRefresh = true;

            if (cachedDebugLogsEnabled)
            {
                Log.Info($"{Constants.LogPrefix} Marked cache for refresh on scene change from {oldScene.name} to {newScene.name}");
            }
        }

        // Sets up Risk of Options settings if available
        public void Start()
        {
            if (Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions"))
            {
                ModSettingsManager.SetModDescription("Allows Drifter to grab bosses, NPCs, and environment objects.", "com.DrifterBossGrab.DrifterBossGrab", "DrifterBossGrab");
                try
                {
                    byte[] array = File.ReadAllBytes(System.IO.Path.Combine(DrifterBossGrabPlugin.Instance.DirectoryName, "icon.png"));
                    Texture2D val = new Texture2D(256, 256);
                    ImageConversion.LoadImage(val, array);
                    ModSettingsManager.SetModIcon(Sprite.Create(val, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f)));
                }
                catch (Exception ex)
                {
                    // Icon loading failed
                }

                ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.SearchRangeMultiplier));
                ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.BreakoutTimeMultiplier));
                ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.ForwardVelocityMultiplier));
                ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.UpwardVelocityMultiplier));
                ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableBossGrabbing));
                ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableNPCGrabbing));
                ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentGrabbing));
                ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.MaxSmacks));
                ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.MassMultiplier));
                ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableDebugLogs));
                ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.BodyBlacklist));
                ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentInvisibility));
                ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentInteractionDisable));
            }
        }

        #region Repossess Patches

        // Increase Drifter bag search range and apply velocity multipliers
        [HarmonyPatch(typeof(EntityStates.Drifter.Repossess), MethodType.Constructor)]
        public class Repossess_Constructor_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Repossess __instance)
            {
                __instance.searchRange *= PluginConfig.SearchRangeMultiplier.Value;

                // Cache original values on first use
                if (originalForwardVelocity == null)
                {
                    originalForwardVelocity = (float)forwardVelocityField.GetValue(null);
                    originalUpwardVelocity = (float)upwardVelocityField.GetValue(null);
                }

                forwardVelocityField.SetValue(null, originalForwardVelocity.Value * PluginConfig.ForwardVelocityMultiplier.Value);
                upwardVelocityField.SetValue(null, originalUpwardVelocity.Value * PluginConfig.UpwardVelocityMultiplier.Value);
            }
        }

        // Set max smacks for bagged enemies
        [HarmonyPatch(typeof(DrifterBagController), "Awake")]
        public class DrifterBagController_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance)
            {
                __instance.maxSmacks = PluginConfig.MaxSmacks.Value;
            }
        }

        // Apply mass multiplier to bagged objects
        [HarmonyPatch(typeof(DrifterBagController), "CalculateBaggedObjectMass")]
        public class DrifterBagController_CalculateBaggedObjectMass_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                float multiplier = 1.0f;
                if (float.TryParse(PluginConfig.MassMultiplier.Value, out float parsed))
                {
                    multiplier = parsed;
                }
                __result *= multiplier;
                __result = Mathf.Clamp(__result, 0f, DrifterBagController.maxMass);
            }
        }

        // Extend breakout time for all bagged objects
        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter_ExtendBreakoutTime
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                // Cache traverse object to avoid repeated creation
                var traverse = Traverse.Create(__instance);
                var targetObject = traverse.Field("targetObject").GetValue<GameObject>();
                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} BaggedObject.OnEnter: targetObject = {targetObject}");
                    if (targetObject)
                    {
                        var body = targetObject.GetComponent<CharacterBody>();
                        if (body)
                        {
                            Log.Info($"{Constants.LogPrefix} Bagging {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, currentVehicle: {body.currentVehicle != null}");
                        }
                    }
                }
                var currentBreakoutTime = traverse.Field("breakoutTime").GetValue<float>();
                traverse.Field("breakoutTime").SetValue(currentBreakoutTime * PluginConfig.BreakoutTimeMultiplier.Value);
            }
        }

        // Force isTargetable to true
        [HarmonyPatch(typeof(SpecialObjectAttributes), "get_isTargetable")]
        public class SpecialObjectAttributes_get_isTargetable
        {
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance, ref bool __result)
            {
                var body = __instance.gameObject.GetComponent<CharacterBody>();
                if (body)
                {
                    bool isBoss = body.isBoss;
                    bool isUngrabbable = body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                    bool canOverride = ((isBoss && PluginConfig.EnableBossGrabbing.Value) ||
                                        (isUngrabbable && PluginConfig.EnableNPCGrabbing.Value)) &&
                                       !PluginConfig.IsBlacklisted(__instance.gameObject.name);
                    if (canOverride)
                    {
                        __result = true;
                    }
                }
            }
        }

        // Allow targeting ungrabbable enemies
        [HarmonyPatch(typeof(RepossessBullseyeSearch), "HurtBoxPassesRequirements")]
        public class RepossessBullseyeSearch_HurtBoxPassesRequirements
        {
            [HarmonyPostfix]
            public static void Postfix(ref bool __result, HurtBox hurtBox)
            {
                __result = false;
                if (hurtBox && hurtBox.healthComponent)
                {
                    var body = hurtBox.healthComponent.body;
                    bool allowTargeting = false;
                    if (body)
                    {
                        if ((PluginConfig.EnableBossGrabbing.Value && body.isBoss) ||
                            (PluginConfig.EnableNPCGrabbing.Value && body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)))
                        {
                            allowTargeting = true;
                        }
                        else if (!body.currentVehicle)
                        {
                            allowTargeting = true;
                        }
                    }
                    if (allowTargeting && !PluginConfig.IsBlacklisted(body.name))
                    {
                        __result = true;
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Allowing targeting of boss/elite/NPC: {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, ungrabbable: {body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)}, currentVehicle: {body.currentVehicle != null}");
                        }
                    }
                }
            }
        }

        // Prevent bosses and NPCs from dodging
        [HarmonyPatch(typeof(SpecialObjectAttributes), "AvoidCapture")]
        public class SpecialObjectAttributes_AvoidCapture
        {
            [HarmonyPrefix]
            public static bool Prefix(SpecialObjectAttributes __instance)
            {
                if (PluginConfig.IsBlacklisted(__instance.gameObject.name))
                {
                    return true; // Allow original behavior for blacklisted
                }
                var body = __instance.gameObject.GetComponent<CharacterBody>();
                if (body)
                {
                    bool isBoss = body.isBoss;
                    bool isUngrabbable = body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                    bool shouldPrevent = (isBoss && PluginConfig.EnableBossGrabbing.Value) ||
                                         (isUngrabbable && PluginConfig.EnableNPCGrabbing.Value);
                    return !shouldPrevent;
                }
                return true; // Allow if no body
            }
        }

        #endregion

        #region Interactable Caching Patches

        [HarmonyPatch(typeof(DirectorCore), "TrySpawnObject")]
        public class DirectorCore_TrySpawnObject_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(GameObject __result)
            {
                if (__result && __result.GetComponent<IInteractable>() != null)
                {
                    cachedInteractables.Add(__result);
                }
            }
        }

        [HarmonyPatch(typeof(UnityEngine.Object), "Destroy", typeof(UnityEngine.Object))]
        public class Object_Destroy_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(UnityEngine.Object obj)
            {
                if (obj is GameObject go && go.GetComponent<IInteractable>() != null)
                {
                    cachedInteractables.Remove(go);
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Removed destroyed interactable {go.name}");
                    }
                }
            }
        }

        #endregion

        #region Bag Patches

        // Prevent assigning blacklisted passengers
        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger_PreventBlacklisted
        {
            [HarmonyPrefix]
            public static bool Prefix(GameObject passengerObject)
            {
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject.name))
                {
                    return false;
                }
                return true;
            }
        }

        // Modify AssignPassenger to eject bosses from vehicles
        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger
        {
            [HarmonyPrefix]
            public static void Prefix(GameObject passengerObject)
            {
                if (!passengerObject) return;

                CharacterBody body = null;
                var interactable = passengerObject.GetComponent<IInteractable>();
                var rb = passengerObject.GetComponent<Rigidbody>();
                var highlight = passengerObject.GetComponent<Highlight>();

                // Create local state dictionaries
                var localColliderStates = new Dictionary<Collider, bool>();
                var localRendererStates = new Dictionary<Renderer, bool>();
                var localInteractableEnabled = new Dictionary<MonoBehaviour, bool>();
                var localHighlightStates = new Dictionary<Highlight, bool>();
                var localDisabledStates = new Dictionary<GameObject, bool>();

                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} AssignPassenger called for {passengerObject}");
                }

                // Clean SpecialObjectAttributes lists to remove null entries
                var soa = passengerObject.GetComponent<SpecialObjectAttributes>();
                if (soa)
                {
                    soa.childSpecialObjectAttributes.RemoveAll(s => s == null);
                    soa.renderersToDisable.RemoveAll(r => r == null);
                    soa.behavioursToDisable.RemoveAll(b => b == null);
                    soa.childObjectsToDisable.RemoveAll(c => c == null);
                    soa.pickupDisplaysToDisable.RemoveAll(p => p == null);
                    soa.lightsToDisable.RemoveAll(l => l == null);
                    soa.objectsToDetach.RemoveAll(o => o == null);
                    soa.skillHighlightRenderers.RemoveAll(r => r == null);
                    // For MinePodBody, disable all colliders and hurtboxes to prevent collision issues
                    if (passengerObject.name.Contains("MinePodBody"))
                    {
                        Traverse.Create(soa).Field("disableAllCollidersAndHurtboxes").SetValue(true);
                    }
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Cleaned null entries from SpecialObjectAttributes on {passengerObject.name}");
                    }
                }

                // Cache component lookups
                body = passengerObject.GetComponent<CharacterBody>();

                if (body)
                {
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Assigning {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, currentVehicle: {body.currentVehicle != null}");
                    }
                    // Eject ungrabbable enemies from vehicles before assigning
                    if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && body.currentVehicle != null)
                    {
                        body.currentVehicle.EjectPassenger(passengerObject);
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Ejected {body.name} from vehicle");
                        }
                    }
                    // TODO, need a revisit
                    // If has Rigidbody, make kinematic to prevent physics issues
                    if (rb)
                    {
                        rb.isKinematic = true;
                        rb.detectCollisions = false;
                    }
                }

                if (interactable != null)
                {
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Assigning IInteractable: {passengerObject.name}");
                    }
                    if (cachedEnableEnvironmentInvisibility)
                    {
                        DisableRenderersForInvisibility(passengerObject, localRendererStates);
                    }
                    if (cachedEnableEnvironmentInteractionDisable)
                    {
                        DisableInteractable(interactable, localInteractableEnabled);
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Disabling IInteractable on {passengerObject.name}");
                        }
                    }
                    DisableColliders(passengerObject, localColliderStates);
                    // TODO, need a revisit
                    // If has Rigidbody, make kinematic
                    if (rb)
                    {
                        rb.isKinematic = true;
                        rb.detectCollisions = false;
                    }
                    // TODO, inconsistent
                    // Disable highlight to prevent persistent glow effect
                    if (highlight != null)
                    {
                        localHighlightStates[highlight] = highlight.enabled;
                        highlight.enabled = false;
                    }
                }

                // Disable all colliders on enemies to prevent movement bugs for flying bosses
                if (body != null)
                {
                    if (cachedDebugLogsEnabled)
                    {
                        var modelLocator = passengerObject.GetComponent<ModelLocator>();
                        Log.Info($"{Constants.LogPrefix} ModelLocator for {passengerObject.name}: {modelLocator != null}");
                    }
                    DisableMovementColliders(passengerObject, localDisabledStates);
                }

                // Remove any existing GrabbedObjectState to prevent duplicates
                var existingState = passengerObject.GetComponent<GrabbedObjectState>();
                if (existingState != null)
                {
                    // Restore the object's original states before destroying the component
                    existingState.RestoreAllStates();
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Restored and destroyed existing GrabbedObjectState on {passengerObject.name}");
                    }
                }

                // Add state storage component to all passengers
                var grabbedState = passengerObject.AddComponent<GrabbedObjectState>();
                grabbedState.originalColliderStates = localColliderStates;
                grabbedState.originalRendererStates = localRendererStates;
                grabbedState.originalInteractableStates = localInteractableEnabled;
                grabbedState.originalMovementStates = localDisabledStates;
                grabbedState.originalHighlightStates = localHighlightStates;
                grabbedState.originalIsTrigger = new Dictionary<Collider, bool>();

                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} Added GrabbedObjectState to {passengerObject.name}");
                }
            }
        }

        // Patch RepossessBullseyeSearch.GetResults to include IInteractable objects
        [HarmonyPatch(typeof(RepossessBullseyeSearch), "GetResults")]
        public class RepossessBullseyeSearch_GetResults_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(RepossessBullseyeSearch __instance, ref IEnumerable<GameObject> __result)
            {
                if (!PluginConfig.EnableEnvironmentGrabbing.Value)
                    return;

                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} Searching for interactables: origin={__instance.searchOrigin}, minDist={__instance.minDistanceFilter}, maxDist={__instance.maxDistanceFilter}");
                }

                // Initialize or refresh cache when needed
                if (!isCacheInitialized || cacheNeedsRefresh)
                {
                    lock (cacheLock)
                    {
                        cachedInteractables.Clear();
                        foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                        {
                            if (mb is IInteractable)
                            {
                                cachedInteractables.Add(mb.gameObject);
                                if (cachedInteractables.Count >= MAX_CACHE_SIZE)
                                {
                                    break; // Prevent excessive memory usage
                                }
                            }
                        }
                        isCacheInitialized = true;
                        cacheNeedsRefresh = false;

                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Refreshed interactable cache: {cachedInteractables.Count} objects cached");
                        }
                    }
                }

                HashSet<GameObject> existingResults = new HashSet<GameObject>(__result);
                List<GameObject> results = new List<GameObject>(__result);

                foreach (GameObject go in cachedInteractables)
                {
                    if (go == null || go.GetComponent<CharacterBody>() != null || go.GetComponent<HurtBox>() != null || existingResults.Contains(go))
                        continue;

                    // Check blacklist
                    if (PluginConfig.IsBlacklisted(go.name))
                    {
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Skipped blacklisted interactable {go.name}");
                        }
                        continue;
                    }

                    Vector3 position = go.transform.position;
                    Vector3 vector = position - __instance.searchOrigin;
                    float sqrMagnitude = vector.sqrMagnitude;

                    if (sqrMagnitude >= __instance.minDistanceFilter * __instance.minDistanceFilter && sqrMagnitude <= __instance.maxDistanceFilter * __instance.maxDistanceFilter)
                    {
                        // Simple distance check
                        results.Add(go);
                        existingResults.Add(go);
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Added interactable {go.name} at distance {Mathf.Sqrt(sqrMagnitude)}");
                        }
                    }
                }
                __result = results;
            }
        }

        #endregion

        #region RepossessExit Patches

        // Patch BaggedObject.OnExit to re-enable colliders for bagged objects
        [HarmonyPatch(typeof(EntityStates.Drifter.Bag.BaggedObject), "OnExit")]
        public class BaggedObject_OnExit_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.Bag.BaggedObject __instance)
            {
                try
                {
                    var traverse = Traverse.Create(__instance);
                    GameObject targetObject = traverse.Field("targetObject").GetValue<GameObject>();
                    if (targetObject == null) return;

                    // TODO, need a revisit
                    // Re-enable Rigidbody for released objects
                    Rigidbody rb = targetObject.GetComponent<Rigidbody>();
                    if (rb)
                    {
                        rb.isKinematic = false;
                        rb.detectCollisions = true;
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Restored Rigidbody on {targetObject.name} (bag exit)");
                        }
                    }

                    // Check if this object has GrabbedObjectState (it's being thrown as projectile)
                    var grabbedState = targetObject.GetComponent<GrabbedObjectState>();
                    if (grabbedState != null)
                    {
                        // For environment objects, restore renderers and highlights immediately on throw
                        // Colliders and other states will be restored on projectile impact
                        RestoreRenderers(grabbedState.originalRendererStates);
                        foreach (var kvp in grabbedState.originalHighlightStates)
                        {
                            if (kvp.Key != null)
                            {
                                kvp.Key.enabled = kvp.Value;
                            }
                        }
                        grabbedState.originalRendererStates.Clear();
                        grabbedState.originalHighlightStates.Clear();

                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Restored renderers and highlights immediately for thrown {targetObject.name} - other states will restore on impact");
                        }
                    }
                    else
                    {
                        // Fallback for objects without GrabbedObjectState (should not happen in normal flow)
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} No GrabbedObjectState found for {targetObject.name} - skipping state restoration");
                        }
                    }

                }
                catch (Exception ex)
                {
                    Log.Error($"[DrifterBossGrab] Error in BaggedObject.OnExit restoration: {ex.Message}");
                }
            }
        }

        // Patch RepossessExit.OnEnter to allow grabbing bosses despite Ungrabbable flag
        [HarmonyPatch(typeof(EntityStates.Drifter.RepossessExit), "OnEnter")]
        public class RepossessExit_OnEnter_Patch
        {
            private static GameObject? originalChosenTarget;

            [HarmonyPrefix]
            public static void Prefix(EntityStates.Drifter.RepossessExit __instance)
            {
                var traverse = Traverse.Create(__instance);
                originalChosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} RepossessExit Prefix: originalChosenTarget = {originalChosenTarget}");
                }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityStates.Drifter.RepossessExit __instance)
            {
                // Only apply grabbing logic if any grabbing type is enabled
                if (!PluginConfig.EnableBossGrabbing.Value && !PluginConfig.EnableNPCGrabbing.Value)
                    return;

                var traverse = Traverse.Create(__instance);
                var chosenTarget = traverse.Field("chosenTarget").GetValue<GameObject>();
                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} RepossessExit Postfix: chosenTarget = {chosenTarget}, originalChosenTarget = {originalChosenTarget}");
                }
                if (chosenTarget && chosenTarget.GetComponent<IInteractable>() != null)
                {
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Allowing grab for IInteractable: {chosenTarget.name}");
                    }
                    var colliders = chosenTarget.GetComponentsInChildren<Collider>();
                    var grabbedState = chosenTarget.GetComponent<GrabbedObjectState>();
                    foreach (var col in colliders)
                    {
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Found collider {col.name} on {chosenTarget.name}, isTrigger: {col.isTrigger}, enabled: {col.enabled}");
                        }
                        if (grabbedState != null && !grabbedState.originalIsTrigger.ContainsKey(col))
                        {
                            grabbedState.originalIsTrigger[col] = col.isTrigger;
                        }
                        if (!col.isTrigger)
                        {
                            col.isTrigger = true;
                            if (cachedDebugLogsEnabled)
                            {
                                Log.Info($"{Constants.LogPrefix} Set collider {col.name} on {chosenTarget.name} to trigger to prevent collision during grab");
                            }
                        }
                    }
                }
                // If chosenTarget was rejected but it's grabbable, allow it
                if (chosenTarget == null && originalChosenTarget != null)
                {
                    var component2 = originalChosenTarget.GetComponent<CharacterBody>();
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Checking body: {component2}, ungrabbable: {component2 && component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable)}");
                    }
                    if (component2)
                    {
                        bool isBoss = component2.master && component2.master.isBoss;
                        bool isElite = component2.isElite;
                        bool isUngrabbable = component2.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable);
                        bool canGrab = (PluginConfig.EnableBossGrabbing.Value && isBoss) ||
                                        (PluginConfig.EnableNPCGrabbing.Value && isUngrabbable);
                        bool isBlacklisted = PluginConfig.IsBlacklisted(component2.name);
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Body {component2.name}: isBoss={isBoss}, isElite={isElite}, ungrabbable={isUngrabbable}, canGrab={canGrab}, isBlacklisted={isBlacklisted}");
                        }
                        if (canGrab && !isBlacklisted)
                        {
                            if (cachedDebugLogsEnabled)
                            {
                                Log.Info($"{Constants.LogPrefix} Allowing grab for {component2.name}");
                            }
                            traverse.Field("chosenTarget").SetValue(originalChosenTarget);
                            traverse.Field("activatedHitpause").SetValue(true);
                            try
                            {
                                Util.PlaySound(Constants.RepossessSuccessSound, Traverse.Create(__instance).Field("gameObject").GetValue<GameObject>());
                                Traverse.Create(__instance).Method("PlayCrossfade", new object[] { Constants.FullBodyOverride, Constants.SuffocateHit, Constants.SuffocatePlaybackRate, traverse.Field("duration").GetValue<float>() * 2.5f, traverse.Field("duration").GetValue<float>() });
                                var animator = Traverse.Create(__instance).Method("GetModelAnimator").GetValue<object>();
                                if (animator != null) Traverse.Create(animator).Field("speed").SetValue(0f);

                                // TODO, this might be useless now, old code
                                var characterMotor = Traverse.Create(__instance).Field("characterMotor").GetValue<CharacterMotor>();
                                if (characterMotor != null)
                                {
                                    // Set stored velocity to zero to prevent being pushed around after grabbing
                                    traverse.Field("storedHitPauseVelocity").SetValue(Vector3.zero);
                                }
                                var hitPauseTimer = traverse.Field("hitPauseTimer").GetValue<float>() + RepossessExit.hitPauseDuration;
                                traverse.Field("hitPauseTimer").SetValue(hitPauseTimer);
                                var component3 = originalChosenTarget.GetComponent<SetStateOnHurt>();
                                if (component3 != null && component3.canBeStunned)
                                {
                                    component3.SetStun(RepossessExit.hitPauseDuration);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"[DrifterBossGrab] Error in grabbing logic: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Other Patches

        // Patch HackingMainState.FixedUpdate to update sphere search origin when beacon moves, bum code but it works
        [HarmonyPatch(typeof(EntityStates.CaptainSupplyDrop.HackingMainState), "FixedUpdate")]
        public class HackingMainState_FixedUpdate_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(EntityStates.CaptainSupplyDrop.HackingMainState __instance)
            {
                // Update the search origin to follow the beacon's current position
                var traverse = Traverse.Create(__instance);
                var field = __instance.GetType().GetField("sphereSearch", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var sphereSearch = (SphereSearch)field.GetValue(__instance);
                    var transform = traverse.Property("transform").GetValue<Transform>();
                    if (sphereSearch != null && transform != null && sphereSearch.origin != transform.position)
                    {
                        sphereSearch.origin = transform.position;
                    }
                }
            }
        }

        // Patch ThrownObjectProjectileController.ImpactBehavior to restore grabbed object states on landing
        [HarmonyPatch(typeof(RoR2.Projectile.ThrownObjectProjectileController), "ImpactBehavior")]
        public class ThrownObjectProjectileController_ImpactBehavior_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(RoR2.Projectile.ThrownObjectProjectileController __instance)
            {
                if (cachedDebugLogsEnabled)
                {
                    Log.Info($"{Constants.LogPrefix} ThrownObjectProjectileController.ImpactBehavior called");
                }

                // Get the passenger from the projectile
                GameObject passenger = __instance.Networkpassenger;
                if (passenger != null)
                {
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile passenger: {passenger.name}");
                    }

                    // Check if the passenger has our state storage component
                    var grabbedState = passenger.GetComponent<GrabbedObjectState>();
                    if (grabbedState != null)
                    {
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Projectile impacted - restoring states for {passenger.name}");
                        }
                        // Restore all the stored states
                        grabbedState.RestoreAllStates();
                    }
                    else
                    {
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} No GrabbedObjectState found on passenger {passenger.name}");
                        }
                    }
                }
                else
                {
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Projectile has no passenger");
                    }
                }
            }
        }

        #endregion
    }
}