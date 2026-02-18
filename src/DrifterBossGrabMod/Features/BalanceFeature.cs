using HarmonyLib;
using UnityEngine;
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

            // Capacity Scaling Patches
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.DrifterBagController_CalculateBaggedObjectMass_Patch)).Patch();

            // Overencumbrance Patches
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.CharacterBody_RecalculateStats_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BalancePatches.CharacterBody_OnDestroy_Patch)).Patch();

            // State Calculation Patches
            harmony.CreateClassProcessor(typeof(StateCalculationPatches)).Patch();
            harmony.CreateClassProcessor(typeof(CmdDamageBaggedObject_AoE)).Patch();

            // Movement Penalty Fix Patch
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.BaggedObject_UpdateBaggedObjectMass)).Patch();

            Log.Info($"[{FeatureName}] Balance patches applied successfully.");
        }
    }
}
