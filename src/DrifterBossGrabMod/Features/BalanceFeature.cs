using HarmonyLib;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod
{

    public class BalanceFeature : FeatureToggleBase
    {
        public override string FeatureName => "Balance";
        public override bool IsEnabled => PluginConfig.Instance.EnableBalance.Value;

        protected override void ApplyPatches(Harmony harmony)
        {
            Log.Info($"[{FeatureName}] Applying balance patches...");

            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.CharacterBody_RecalculateStats_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.CharacterBody_OnDestroy_Patch)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.EmptyBag_ModifyProjectile_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.EmptyBag_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.EmptyBag_FireProjectile_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.ProjectileManager_FireProjectile_Patch)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.StateCalculationPatches.SuffocateSlam_AuthorityModifyOverlapAttack_ApplyCustomDamage)).Patch();
            harmony.CreateClassProcessor(typeof(CmdDamageBaggedObject_AoE)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.StateCalculationPatches.SuffocateSlam_OnEnter_UseDynamicCapacity)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.StateCalculationPatches.BluntForceHit3_OnEnter_UseFormula)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.BaggedObject_UpdateBaggedObjectMass)).Patch();

            Log.Info($"[{FeatureName}] Balance patches applied successfully.");
        }
    }
}
