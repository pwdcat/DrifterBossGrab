using System;
using HarmonyLib;
using UnityEngine;
using RoR2;
using DrifterBossGrabMod.Patches;

namespace DrifterBossGrabMod
{
    public class BottomlessBagFeature : FeatureToggleBase
    {
        public override string FeatureName => "BottomlessBag";
        public override bool IsEnabled => PluginConfig.Instance.BottomlessBagEnabled.Value;

        protected override void ApplyPatches(Harmony harmony)
        {
            Log.Info($"[{FeatureName}] Applying patches...");

            // Only apply bottomless bag patches when enabled
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectPatches.BaggedObject_TryOverrideUtility)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectPatches.BaggedObject_TryOverridePrimary)).Patch();

            // Missing lifecycle and UI patches
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.BaggedObject_OnEnter)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.BaggedObject_OnExit)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.BaggedObject_FixedUpdate)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectPatches.VehicleSeat_OnPassengerExit)).Patch();
            
            // Animation Patches
            harmony.CreateClassProcessor(typeof(Patches.AnimationPatches)).Patch();

            // Newly identified missing patches
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.EntityStateMachine_SetNextStateToMain)).Patch();

            // Bag transition/cleanup patches
            harmony.CreateClassProcessor(typeof(Patches.VehicleSeat_AssignPassenger_Postfix)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.GlobalEventManager_OnCharacterDeath)).Patch();
            
            harmony.CreateClassProcessor(typeof(Patches.SkillPatches.GenericSkill_RunRecharge)).Patch();

            // Successive grab stock refresh patch
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnExit_Patch)).Patch();

            Log.Info($"[{FeatureName}] Patches applied successfully.");
        }
    }
}
