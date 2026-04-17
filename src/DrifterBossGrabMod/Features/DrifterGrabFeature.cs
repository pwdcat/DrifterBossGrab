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
            // Core grabbing patches that should always be active
            // These handle the fundamental grabbing mechanics
            harmony.CreateClassProcessor(typeof(Patches.BagPatches.Run_Start_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BagPatches.DrifterBagController_AssignPassenger)).Patch();
            // Explicitly register all nested patches for MiscPatches
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.HackingMainState_FixedUpdate_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ProjectileFuse_FixedUpdate_Patch)).Patch();
            // Prevent clients from calling server-only eject functions
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ThrownObjectProjectileController_EjectPassengerToFinalPosition_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ThrownObjectProjectileController_CheckForDeadPassenger_Patch)).Patch();
            // Explicitly register all nested patches for ProjectileRecoveryPatches
            harmony.CreateClassProcessor(typeof(Patches.ProjectileRecoveryPatches.ThrownObjectProjectileController_OnSyncPassenger_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.ProjectileRecoveryPatches.ThrownObjectProjectileController_ImpactBehavior_Patch)).Patch();
            // Explicitly register all nested patches for SpecialObjectAttributesPatches
            harmony.CreateClassProcessor(typeof(Patches.SpecialObjectAttributesPatches.SpecialObjectAttributes_Start_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.SpecialObjectAttributesPatches.BaggedObject_OnEnter_Patch)).Patch();

            // Explicitly register all nested patches for RepossessExitPatches
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnSerialize_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnDeserialize_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.BaggedObject_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.BaggedObject_OnExit_Patch)).Patch();

            // Explicitly register all nested patches for RepossessPatches
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

            // CharacterMaster_OnBodyStart handles both AutoGrab (core) and UI (bottomless bag)
            harmony.CreateClassProcessor(typeof(Patches.CharacterSpawnPatches.CharacterMaster_OnBodyStart)).Patch();

            // Explicitly register all nested patches for GrabbableObjectPatches
            harmony.CreateClassProcessor(typeof(Patches.GrabbableObjectPatches.DirectorCore_TrySpawnObject_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.GrabbableObjectPatches.SpecialObjectAttributes_Start_Patch)).Patch();

            // Detect and clean up tracking when a bagged creature's ESM leaves VehicleSeated (Child teleport)
            harmony.CreateClassProcessor(typeof(Patches.BaggedObjectStatePatches.EntityStateMachine_SetState)).Patch();

            // Defensive null check for CheckForDeadPassenger when passenger has been destroyed/teleported
            harmony.CreateClassProcessor(typeof(Patches.MiscPatches.ThrownObjectProjectileController_CheckForDeadPassenger_Patch)).Patch();

            // Explicitly register all nested patches for ProjectilePatches
            harmony.CreateClassProcessor(typeof(Patches.ProjectilePatches.ProjectileController_Start_Patch)).Patch();
        }
    }
}
