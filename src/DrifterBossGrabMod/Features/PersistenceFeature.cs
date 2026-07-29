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
            harmony.CreateClassProcessor(typeof(Patches.SceneExitPatches)).Patch();
            Patches.RunLifecyclePatches.Initialize();
        }

        public override void Cleanup()
        {
            base.Cleanup();
            Patches.RunLifecyclePatches.Cleanup();
        }
    }
}
