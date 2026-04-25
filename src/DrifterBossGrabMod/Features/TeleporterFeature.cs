using HarmonyLib;

namespace DrifterBossGrabMod
{
    public class TeleporterFeature : FeatureToggleBase
    {
        public override string FeatureName => "Teleporter";
        public override bool IsEnabled => PluginConfig.Instance.EnableObjectPersistence.Value && !PluginConfig.IsPersistenceBlacklisted("Teleporter");

        protected override void ApplyPatches(Harmony harmony)
        {
            harmony.CreateClassProcessor(typeof(Patches.TeleporterPatches)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BossGroupPatches)).Patch();
        }
    }
}