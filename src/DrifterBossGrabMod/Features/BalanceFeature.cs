using HarmonyLib;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod
{
    // Feature that applies all balance-related patches
    // Includes capacity scaling, elite mass bonus, overencumbrance, and weight display
    public class BalanceFeature : FeatureToggleBase
    {
        public override string FeatureName => "Balance";
        public override bool IsEnabled => PluginConfig.Instance.EnableBalance.Value;

        protected override void ApplyPatches(Harmony harmony)
        {
            Log.Info($"[{FeatureName}] Applying balance patches...");

            // Overencumbrance Patches
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.CharacterBody_RecalculateStats_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.CharacterBody_OnDestroy_Patch)).Patch();

            // Launch Speed Cap Patches
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.EmptyBag_ModifyProjectile_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.ProjectileManager_FireProjectile_Patch)).Patch();

            // State Calculation Patches
            harmony.CreateClassProcessor(typeof(Patches.StateCalculationPatches.SuffocateSlam_AuthorityModifyOverlapAttack_ApplyCustomDamage)).Patch();
            harmony.CreateClassProcessor(typeof(CmdDamageBaggedObject_AoE)).Patch();

            // Patch SuffocateSlam.OnEnter
            harmony.CreateClassProcessor(typeof(Patches.StateCalculationPatches.SuffocateSlam_OnEnter_UseDynamicCapacity)).Patch();

            // Patch BluntForceHit3.OnEnter for bludgeon damage formula
            harmony.CreateClassProcessor(typeof(Patches.StateCalculationPatches.BluntForceHit3_OnEnter_UseFormula)).Patch();

            // Movement Penalty Fix Patch
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.BaggedObject_UpdateBaggedObjectMass)).Patch();

            Log.Info($"[{FeatureName}] Balance patches applied successfully.");
        }
    }
}
