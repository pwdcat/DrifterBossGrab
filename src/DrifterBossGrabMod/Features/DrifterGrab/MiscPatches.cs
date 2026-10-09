#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using EntityStates.CaptainSupplyDrop;
using UnityEngine;
using DrifterBossGrabMod;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{
    public static class MiscPatches
    {

        private static readonly FieldInfo _sphereSearchField = ReflectionCache.HackingMainState.SphereSearch;

        [HarmonyPatch(typeof(EntityStates.ChampRushReaper.ReaperSecondPhaseSpawnTransition), "ToggleShriekParameters")]
        public static class ReaperSecondPhaseSpawnTransition_ToggleShriekParameters_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(bool setActive, EntityStateMachine[]? ___entityStateMachines)
            {
                return setActive || ___entityStateMachines != null;
            }
        }

        [HarmonyPatch(typeof(HackingMainState), "ScanForTarget")]
        public class HackingMainState_ScanForTarget_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(HackingMainState __instance)
            {

                if (_sphereSearchField != null)
                {
                    var sphereSearch = (SphereSearch)_sphereSearchField.GetValue(__instance);
                    if (sphereSearch != null && __instance.transform != null)
                    {
                        sphereSearch.origin = __instance.transform.position;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), "OnPassengerEnter")]
        public static class VehicleSeat_OnPassengerEnter_Gong_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(VehicleSeat __instance, GameObject passenger)
            {
                if (__instance.GetComponent<ThrownObjectProjectileController>() == null) return;
                passenger?.GetComponent<GongLandingPlacement>()?.BeginFlight(__instance);
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "OnSyncPassenger")]
        public static class ThrownObjectProjectileController_OnSyncPassenger_Gong_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(GameObject passengerObject, out Quaternion? __state)
            {
                __state = passengerObject != null && passengerObject.GetComponent<GongLandingPlacement>()?.AddedVisibilityAttributes == true
                    ? passengerObject.transform.rotation : null;
            }

            [HarmonyPostfix]
            public static void Postfix(ref Quaternion ___initialRotation, Quaternion? __state)
            {
                var rotation = __state;
                if (rotation.HasValue) ___initialRotation = rotation.Value;
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), nameof(ThrownObjectProjectileController.ImpactBehavior))]
        public static class ThrownObjectProjectileController_ImpactBehavior_Gong_Patch
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var generateJunk = AccessTools.Method(typeof(JunkController), nameof(JunkController.CallCmdGenerateJunkQuantity));
                var generateImpactJunk = AccessTools.Method(typeof(ThrownObjectProjectileController_ImpactBehavior_Gong_Patch), nameof(GenerateImpactJunk));
                bool patched = false;
                foreach (var instruction in instructions)
                {
                    if (instruction.Calls(generateJunk))
                    {
                        var loadProjectile = new CodeInstruction(OpCodes.Ldarg_0);
                        loadProjectile.labels.AddRange(instruction.labels);
                        instruction.labels.Clear();
                        yield return loadProjectile;
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = generateImpactJunk;
                        patched = true;
                    }
                    yield return instruction;
                }
                if (!patched) Log.Warning("[GongImpact] Could not find the thrown-object junk reward call.");
            }

            private static void GenerateImpactJunk(JunkController controller, Vector3 position, int quantity, ThrownObjectProjectileController projectile)
            {
                var passenger = projectile.Networkpassenger;
                if (passenger != null && passenger.GetComponent<TrialGongInteraction>() != null) return;
                controller.CallCmdGenerateJunkQuantity(position, quantity);
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), nameof(VehicleSeat.RpcEjectPassenger))]
        public static class VehicleSeat_RpcEjectPassenger_Gong_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(VehicleSeat __instance, out GongLandingPlacement? __state)
            {
                __state = __instance.GetComponent<ThrownObjectProjectileController>() != null
                    ? __instance.currentPassengerTransform?.GetComponent<GongLandingPlacement>() : null;
            }

            [HarmonyPostfix]
            public static void Postfix(GongLandingPlacement? __state)
            {
                if (__state != null) __state.CompleteLanding();
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "EjectPassengerToFinalPosition")]
        public class ThrownObjectProjectileController_EjectPassengerToFinalPosition_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ThrownObjectProjectileController __instance)
            {
                Log.Debug($"[EjectPassenger] CALLED for {__instance.name} | Passenger: {(__instance.Networkpassenger != null ? __instance.Networkpassenger.name : "null")} | Server: {UnityEngine.Networking.NetworkServer.active}");

                return true;
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "CheckForDeadPassenger")]
        public class ThrownObjectProjectileController_CheckForDeadPassenger_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ThrownObjectProjectileController __instance)
            {
                try
                {

                    var passenger = __instance.Networkpassenger;
                    if (passenger == null)
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[PassengerPatch] Failed to check passenger: {ex.Message}");
                    return false;
                }
                return true;
            }
        }
    }
}
