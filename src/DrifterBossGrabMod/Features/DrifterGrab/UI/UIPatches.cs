#nullable enable
using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.UI;

namespace DrifterBossGrabMod.Patches
{
    // Patches for UI-related functionality, including Capacity UI initialization.
    public static class UIPatches
    {
        private static GameObject? _massCapacityUIControllerObject;

        // Initializes the Capacity UI Controller for the local player.
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

            // Always add BaggedObjectInfoUIController to all Drifters so spectating works, 
            // but the controller itself should handle visibility based on whether it's the HUD's target.
            if (drifterBody.GetComponent<BaggedObjectInfoUIController>() == null)
            {
                drifterBody.gameObject.AddComponent<BaggedObjectInfoUIController>();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UIPatches] Added BaggedObjectInfoUIController to {drifterBody.name}");
                }
            }

            // Only add MassCapacityUIController if it's the local player's body
            if (!drifterBody.hasAuthority)
            {
                return;
            }

            if (!PluginConfig.Instance.EnableMassCapacityUI.Value)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[UIPatches] MassCapacityUI is disabled in config, skipping capacity bar initialization");
                }
                return;
            }

            // Add MassCapacityUIController directly to DrifterBody (like BaggedObjectUIController)
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

        // Cleans up the Capacity UI Controller.
        public static void CleanupMassCapacityUI()
        {
            // No cleanup needed since component is on DrifterBody and will be destroyed with it
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[UIPatches] MassCapacityUIController cleanup not needed (component on DrifterBody)");
            }
        }

        // Updates the Capacity UI when capacity changes.
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
