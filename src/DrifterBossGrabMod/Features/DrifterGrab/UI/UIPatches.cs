#nullable enable
using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.UI;

namespace DrifterBossGrabMod.Patches
{

    public static class UIPatches
    {
        private static GameObject? _massCapacityUIControllerObject;

        public static void InitializeMassCapacityUI(CharacterBody drifterBody)
        {
            if (drifterBody == null || drifterBody.bodyIndex != BodyCatalog.FindBodyIndex("DrifterBody"))
            {
                return;
            }

            Log.Debug($"[UIPatches] InitializeMassCapacityUI() called for {drifterBody.name}");

            if (drifterBody.GetComponent<BaggedObjectInfoUIController>() == null)
            {
                drifterBody.gameObject.AddComponent<BaggedObjectInfoUIController>();
                Log.Debug($"[UIPatches] Added BaggedObjectInfoUIController to {drifterBody.name}");
            }

            if (!drifterBody.hasAuthority)
            {
                return;
            }

            var existingController = drifterBody.GetComponent<MassCapacityUIController>();
            if (existingController == null)
            {
                drifterBody.gameObject.AddComponent<MassCapacityUIController>();
                _massCapacityUIControllerObject = drifterBody.gameObject;
                Log.Debug($"[UIPatches] Added MassCapacityUIController to {drifterBody.name}");
            }
            else
            {
                _massCapacityUIControllerObject = drifterBody.gameObject;
                Log.Debug($"[UIPatches] MassCapacityUIController already exists on {drifterBody.name}");
            }
        }

        public static void CleanupMassCapacityUI()
        {

            Log.Debug("[UIPatches] MassCapacityUIController cleanup not needed (component on DrifterBody)");
        }

        public static void UpdateMassCapacityUIOnCapacityChange(DrifterBagController controller)
        {
            if (_massCapacityUIControllerObject == null) return;

            var massCapacityUIController = _massCapacityUIControllerObject.GetComponent<MassCapacityUIController>();
            if (massCapacityUIController != null)
            {
                massCapacityUIController.UpdateCapacityUI();
            }
        }

        [HarmonyPatch]
        public static class BaggedCardCompatibilityPatches
        {
            [HarmonyPatch(typeof(RoR2.UI.AllyCardController), nameof(RoR2.UI.AllyCardController.Awake))]
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void StripIncompatibleAllyComponents(RoR2.UI.AllyCardController __instance)
            {
                if (__instance is RoR2.UI.BaggedCardController)
                {
                    foreach (var mb in __instance.GetComponents<MonoBehaviour>())
                    {
                        if (mb != null && mb.GetType().Name == "AllyCardData")
                        {
                            UnityEngine.Object.DestroyImmediate(mb);
                        }
                    }

                    var extraSlot = __instance.transform.Find("WhatchaGotThere EquipmentSlot");
                    if (extraSlot != null)
                    {
                        UnityEngine.Object.DestroyImmediate(extraSlot.gameObject);
                    }
                }
            }
        }
    }
}
