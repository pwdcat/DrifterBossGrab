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
        public static void InitializeMassCapacityUI()
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[UIPatches] InitializeMassCapacityUI() called");
            }

            if (!PluginConfig.Instance.EnableMassCapacityUI.Value)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[UIPatches] MassCapacityUI is disabled in config, skipping initialization");
                }
                return;
            }

            // Find the local player's Drifter body
            var drifterBody = UnityEngine.Object.FindFirstObjectByType<CharacterBody>();
            if (drifterBody != null && drifterBody.bodyIndex == BodyCatalog.FindBodyIndex("DrifterBody"))
            {
                // Add MassCapacityUIController directly to DrifterBody (like BaggedObjectUIController)
                var existingController = drifterBody.GetComponent<MassCapacityUIController>();
                if (existingController == null)
                {
                    drifterBody.gameObject.AddComponent<MassCapacityUIController>();
                    _massCapacityUIControllerObject = drifterBody.gameObject;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info("[UIPatches] Added MassCapacityUIController to DrifterBody");
                    }
                }
                else
                {
                    _massCapacityUIControllerObject = drifterBody.gameObject;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info("[UIPatches] MassCapacityUIController already exists on DrifterBody");
                    }
                }
            }
            else
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info("[UIPatches] DrifterBody not found, skipping MassCapacityUI initialization");
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

        // Gets the current capacity for the local player.
        public static float GetCurrentCapacity()
        {
            if (_massCapacityUIControllerObject == null) return 0f;

            var massCapacityUIController = _massCapacityUIControllerObject.GetComponent<MassCapacityUIController>();
            return massCapacityUIController?.CurrentCapacity ?? 0f;
        }

        // Gets the current used capacity for the local player.
        public static float GetCurrentUsedCapacity()
        {
            if (_massCapacityUIControllerObject == null) return 0f;

            var massCapacityUIController = _massCapacityUIControllerObject.GetComponent<MassCapacityUIController>();
            return massCapacityUIController?.CurrentUsedCapacity ?? 0f;
        }
    }
}
