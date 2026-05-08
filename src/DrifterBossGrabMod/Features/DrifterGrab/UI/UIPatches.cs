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

            Log.DebugIfEnabled("[UIPatches] InitializeMassCapacityUI() called for {0}", drifterBody.name);

            if (drifterBody.GetComponent<BaggedObjectInfoUIController>() == null)
            {
                drifterBody.gameObject.AddComponent<BaggedObjectInfoUIController>();
                Log.DebugIfEnabled("[UIPatches] Added BaggedObjectInfoUIController to {0}", drifterBody.name);
            }

            // Only add MassCapacityUIController if it's the local player's body
            if (!drifterBody.hasAuthority)
            {
                return;
            }

            if (!PluginConfig.Instance.EnableMassCapacityUI.Value)
            {
                Log.DebugIfEnabled("[UIPatches] MassCapacityUI is disabled in config, skipping capacity bar initialization");
                return;
            }

            // Add MassCapacityUIController directly to DrifterBody (like BaggedObjectUIController)
            var existingController = drifterBody.GetComponent<MassCapacityUIController>();
            if (existingController == null)
            {
                drifterBody.gameObject.AddComponent<MassCapacityUIController>();
                _massCapacityUIControllerObject = drifterBody.gameObject;
                Log.DebugIfEnabled("[UIPatches] Added MassCapacityUIController to {0}", drifterBody.name);
            }
            else
            {
                _massCapacityUIControllerObject = drifterBody.gameObject;
                Log.DebugIfEnabled("[UIPatches] MassCapacityUIController already exists on {0}", drifterBody.name);
            }
        }

        // Cleans up the Capacity UI Controller.
        public static void CleanupMassCapacityUI()
        {
            // No cleanup needed since component is on DrifterBody and will be destroyed with it
            Log.DebugIfEnabled("[UIPatches] MassCapacityUIController cleanup not needed (component on DrifterBody)");
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
