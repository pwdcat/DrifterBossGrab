using HarmonyLib;
using UnityEngine;

namespace DrifterBossGrabMod
{
    public class DrifterGrabFeature : FeatureToggleBase
    {
        public override string FeatureName => "DrifterGrab";
        public override bool IsEnabled => true; // Always enabled - core functionality
        
        protected override void ApplyPatches(Harmony harmony)
        {
            Log.Info($"[{FeatureName}] Applying patches...");
            // Core grabbing patches that should always be active
            // These handle the fundamental grabbing mechanics
            harmony.CreateClassProcessor(typeof(Patches.BagPatches.Run_Start_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BagPatches.DrifterBagController_AssignPassenger)).Patch();
            // Explicitly register all nested patches for OtherPatches
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.HackingMainState_FixedUpdate_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.ThrownObjectProjectileController_OnSyncPassenger_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.ThrownObjectProjectileController_CheckForDeadPassenger_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.ThrownObjectProjectileController_ImpactBehavior_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.MapZoneChecker_FixedUpdate_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.ProjectileFuse_FixedUpdate_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.SpecialObjectAttributes_Start_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.BaggedObject_OnEnter_Patch)).Patch();
            // Prevent clients from calling server-only eject functions (fixes desync when host grabs mid-air)
            harmony.CreateClassProcessor(typeof(Patches.OtherPatches.ThrownObjectProjectileController_EjectPassengerToFinalPosition_Patch)).Patch();

            // Explicitly register all nested patches for RepossessExitPatches
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.RepossessExit_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.BaggedObject_OnEnter_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessExitPatches.BaggedObject_OnExit_Patch)).Patch();

            // Explicitly register all nested patches for RepossessPatches
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.Repossess_Constructor_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_CalculateBaggedObjectMass_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_RecalculateBaggedObjectMass_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_OnSyncBaggedObject_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.DrifterBagController_Awake_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.BaggedObject_OnEnter_ExtendBreakoutTime)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.SpecialObjectAttributes_get_isTargetable)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.RepossessBullseyeSearch_HurtBoxPassesRequirements)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.SpecialObjectAttributes_AvoidCapture)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.RepossessPatches.Repossess_OnExit_Patch)).Patch();

            // CharacterMaster_OnBodyStart handles both AutoGrab (core) and UI (bottomless bag)
            harmony.CreateClassProcessor(typeof(Patches.CharacterSpawnPatches.CharacterMaster_OnBodyStart)).Patch();

            // Explicitly register UI patches to handle overlay suppression based on config
            harmony.CreateClassProcessor(typeof(Patches.UIPatches.BaggedObject_OnUIOverlayInstanceAdded)).Patch();

            // Explicitly register all nested patches for GrabbableObjectPatches
            harmony.CreateClassProcessor(typeof(Patches.GrabbableObjectPatches.DirectorCore_TrySpawnObject_Patch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.GrabbableObjectPatches.SpecialObjectAttributes_Start_Patch)).Patch();

            // Explicitly register all nested patches for ProjectilePatches
            harmony.CreateClassProcessor(typeof(Patches.ProjectilePatches.ProjectileController_Start_Patch)).Patch();
            Log.Info($"[{FeatureName}] Patches applied successfully.");
        }
    }
}