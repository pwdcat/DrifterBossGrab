using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    // Harmony patches for state calculation integration
    public static class StateCalculationPatches
    {
        // Patch to ensure state calculation is applied when a new passenger is assigned to the bag
        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger_StateCalculation
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject passengerObject)
            {
                // DIAGNOSTIC LOG: Track when AssignPassenger triggers state calculation
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AssignPassenger_StateCalculation] Called with passengerObject={passengerObject?.name ?? "null"}, EnableBalance={PluginConfig.Instance.EnableBalance.Value}, NetworkServer.active={NetworkServer.active}");
                }

                if (passengerObject == null || !PluginConfig.Instance.EnableBalance.Value) return;

                // Trigger state recalculation with current mode
                BaggedObjectPatches.SynchronizeBaggedObjectState(__instance, passengerObject);
            }
        }

        // Patch to ensure state calculation is applied when cycling to a new object
        [HarmonyPatch(typeof(BottomlessBagPatches), "CycleToNextObject")]
        public class CycleToNextObject_StateCalculation
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject newObject)
            {
                if (newObject == null || !PluginConfig.Instance.EnableBalance.Value) return;

                // Trigger state recalculation with current mode
                BaggedObjectPatches.SynchronizeBaggedObjectState(__instance, newObject);
            }
        }
    }
}
