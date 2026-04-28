using HarmonyLib;
using UnityEngine;
using DrifterBossGrabMod.Config;

namespace DrifterBossGrabMod
{
    public class DrifterGrabFeature : FeatureToggleBase
    {
        public override string FeatureName => "DrifterGrab";
        public override bool IsEnabled => PluginConfig.Instance.SelectedPreset.Value != PresetType.Vanilla; // Disabled when Vanilla preset is selected

        protected override void ApplyPatches(Harmony harmony)
        {
            harmony.CreateClassProcessor(typeof(Patches.BagPatches.Run_Start_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BagPatches.DrifterBagController_AssignPassenger)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.HackingMainState_FixedUpdate_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ProjectileFuse_FixedUpdate_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ThrownObjectProjectileController_EjectPassengerToFinalPosition_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ThrownObjectProjectileController_CheckForDeadPassenger_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.ProjectileRecoveryPatches.ThrownObjectProjectileController_OnSyncPassenger_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.ProjectileRecoveryPatches.ThrownObjectProjectileController_ImpactBehavior_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.ProjectileRecoveryPatches.ThrownObjectProjectileController_OnDestroy_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.SpecialObjectAttributesPatches.SpecialObjectAttributes_OnEnable_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.SpecialObjectAttributesPatches.SpecialObjectAttributes_OnDisable_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.SpecialObjectAttributesPatches.SpecialObjectAttributes_Start_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.SpecialObjectAttributesPatches.BaggedObject_OnEnter_Patch)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnSerialize_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnDeserialize_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.BaggedObject_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.BaggedObject_OnExit_Patch)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_CalculateBaggedObjectMass_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_RecalculateBaggedObjectMass_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_OnSyncBaggedObject_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_Awake_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.BaggedObject_OnEnter_ExtendBreakoutTime)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.SpecialObjectAttributes_get_isTargetable)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.RepossessBullseyeSearch_HurtBoxPassesRequirements)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.SpecialObjectAttributes_AvoidCapture)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.AimRepossess_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.Repossess_OnEnter_Patch)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.CharacterSpawnPatches.CharacterMaster_OnBodyStart)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.GrabbableObjectPatches.DirectorCore_TrySpawnObject_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.GrabbableObjectPatches.SpecialObjectAttributes_Start_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.GrabbableObjectPatches.BaseCaptainSupplyDropState_OnEnter_Patch)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.EntityStateMachine_SetState)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ThrownObjectProjectileController_CheckForDeadPassenger_Patch)).Patch();

            harmony.CreateClassProcessor(typeof(Patches.ProjectilePatches.ProjectileController_Start_Patch)).Patch();
        }
    }
}
