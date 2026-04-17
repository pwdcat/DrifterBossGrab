using HarmonyLib;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod
{
    public class RecoveryFeature : FeatureToggleBase
    {
        public override string FeatureName => "Recovery";
        public override bool IsEnabled => PluginConfig.Instance.EnableRecoveryFeature.Value;

        protected override void ApplyPatches(Harmony harmony)
        {
            // Register the new MapZone hooks
            harmony.CreateClassProcessor(typeof(Patches.ProjectileRecoveryPatches.MapZone_TryZoneStart_Patch)).Patch();
        }
    }
}
