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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[UIPatches] InitializeMassCapacityUI() called for {drifterBody.name}");
            }

            if (drifterBody.GetComponent<BaggedObjectInfoUIController>() == null)
            {
                drifterBody.gameObject.AddComponent<BaggedObjectInfoUIController>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UIPatches] Added BaggedObjectInfoUIController to {drifterBody.name}");
                }
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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UIPatches] Added MassCapacityUIController to {drifterBody.name}");
                }
            }
            else
            {
                _massCapacityUIControllerObject = drifterBody.gameObject;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UIPatches] MassCapacityUIController already exists on {drifterBody.name}");
                }
            }
        }

        public static void CleanupMassCapacityUI()
        {

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[UIPatches] MassCapacityUIController cleanup not needed (component on DrifterBody)");
            }
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

    }
}
