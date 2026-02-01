using HarmonyLib;
using UnityEngine;

namespace DrifterBossGrabMod
{
    public class PersistenceFeature : FeatureToggleBase
    {
        public override string FeatureName => "Persistence";
        public override bool IsEnabled => PluginConfig.Instance.EnableObjectPersistence.Value;

        protected override void ApplyPatches(Harmony harmony)
        {
            Log.Info($"[{FeatureName}] Applying patches...");
            // Only apply persistence patches when enabled

            harmony.CreateClassProcessor(typeof(Patches.SceneExitPatches.SceneExitController_OnEnable)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.SceneExitPatches.SceneExitController_OnDisable)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.TeleporterPatches)).Patch();

            // Initialize manual patches
            Log.Info($"[{FeatureName}] Initializing manual patches (RunLifecyclePatches, TeleporterPatches)...");
            Patches.RunLifecyclePatches.Initialize();
            Patches.TeleporterPatches.Initialize();
            Log.Info($"[{FeatureName}] Patches applied successfully.");
        }

        public override void Cleanup(Harmony harmony)
        {
            base.Cleanup(harmony);
            // Cleanup manual patches
            Log.Info($"[{FeatureName}] Cleaning up manual patches...");
            Patches.RunLifecyclePatches.Cleanup();
            Patches.TeleporterPatches.Cleanup();
            Log.Info($"[{FeatureName}] Cleanup complete.");
        }
    }
}