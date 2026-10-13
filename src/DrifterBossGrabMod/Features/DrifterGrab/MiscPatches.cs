#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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

        [HarmonyPatch(typeof(EntityStates.SolusHeart.PhasePreFight.MissionPreFight), "DetectDamage")]
        public static class MissionPreFight_DetectDamage_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(DamageInfo info)
            {
                return info != null && info.inflictedHurtbox != null;
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

        [HarmonyPatch(typeof(ThrownObjectProjectileController), "CalculatePassengerFinalPosition")]
        public static class ThrownObjectProjectileController_CalculatePassengerFinalPosition_Gong_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(ThrownObjectProjectileController __instance, Vector3 position, ref Quaternion rotation, Quaternion ___initialRotation)
            {
                if (!UnityEngine.Networking.NetworkServer.active || __instance.Networkpassenger == null ||
                    __instance.Networkpassenger.GetComponent<TrialGongInteraction>() == null) return;
                var owner = __instance.GetComponent<ProjectileController>()?.owner;
                var facing = owner != null ? Vector3.ProjectOnPlane(owner.transform.position - position, Vector3.up) : Vector3.zero;
                var initialRotation = ___initialRotation;
                rotation = facing.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(facing, Vector3.up)
                    : Quaternion.Euler(0f, initialRotation.eulerAngles.y, 0f);
            }
        }

        [HarmonyPatch(typeof(ThrownObjectProjectileController), nameof(ThrownObjectProjectileController.ImpactBehavior))]
        public static class ThrownObjectProjectileController_ImpactBehavior_JunkReward_Patch
        {
            private static readonly ConditionalWeakTable<ThrownObjectProjectileController, object> _rewardedProjectiles = new();

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var generateJunk = AccessTools.Method(typeof(JunkController), nameof(JunkController.CallCmdGenerateJunkQuantity));
                var generateImpactJunk = AccessTools.Method(typeof(ThrownObjectProjectileController_ImpactBehavior_JunkReward_Patch), nameof(GenerateImpactJunk));
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
                if (!patched) Log.Warning("[ImpactJunk] Could not find the thrown-object junk reward call.");
            }

            private static void GenerateImpactJunk(JunkController controller, Vector3 position, int quantity, ThrownObjectProjectileController projectile)
            {
                var passenger = projectile.Networkpassenger;
                if (!ImpactJunkPolicy.AllowsReward(passenger)) return;
                if (_rewardedProjectiles.TryGetValue(projectile, out _))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[ImpactJunk] Skipped duplicate reward: projectile={projectile.GetInstanceID()}, passenger={(passenger != null ? passenger.name : "null")}");
                    return;
                }
                _rewardedProjectiles.Add(projectile, new object());
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Debug($"[ImpactJunk] Reward: projectile={projectile.GetInstanceID()}, passenger={(passenger != null ? passenger.name : "null")}, quantity={quantity}");
                controller.CallCmdGenerateJunkQuantity(position, quantity);
            }
        }

        [HarmonyPatch(typeof(WakeDonationController), nameof(WakeDonationController.GetInteractability))]
        public static class WakeDonationController_GetInteractability_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref Interactability __result)
            {
                if (WakeController.instance != null) return true;
                __result = Interactability.Disabled;
                return false;
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), nameof(VehicleSeat.RpcEjectPassenger))]
        public static class VehicleSeat_RpcEjectPassenger_Gong_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(VehicleSeat __instance, out GongLandingPlacement? __state)
            {
                __state = null;
                if (__instance == null) return false;
                if (__instance.GetComponent<ThrownObjectProjectileController>() != null)
                    __state = __instance.currentPassengerTransform?.GetComponent<GongLandingPlacement>();
                return true;
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
