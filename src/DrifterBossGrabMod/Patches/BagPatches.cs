using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using System.Reflection;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    public static class BagPatches
    {
        public static void ScanAllSceneComponents()
        {
            if (!PluginConfig.EnableComponentAnalysisLogs.Value) return;

            Log.Info($"{Constants.LogPrefix} === SCANNING ALL COMPONENTS IN CURRENT SCENE ===");

            // Get all root GameObjects in the scene
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            // Collect all unique component types
            var componentTypes = new HashSet<string>();

            foreach (var rootObj in rootObjects)
            {
                // Recursively scan all objects
                ScanObjectComponents(rootObj, componentTypes);
            }

            Log.Info($"{Constants.LogPrefix} === UNIQUE COMPONENT TYPES FOUND ({componentTypes.Count}) ===");
            foreach (var type in componentTypes.OrderBy(t => t))
            {
                Log.Info($"{Constants.LogPrefix} {type}");
            }
            Log.Info($"{Constants.LogPrefix} === END SCENE COMPONENT SCAN ===");
        }

        private static void ScanObjectComponents(GameObject obj, HashSet<string> componentTypes)
        {
            if (obj == null) return;

            // Get all components on this object
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component != null)
                {
                    componentTypes.Add(component.GetType().Name);
                }
            }

            // Recursively scan children
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                ScanObjectComponents(obj.transform.GetChild(i).gameObject, componentTypes);
            }
        }

        // Check if there's another active teleporter in the scene
        private static bool HasActiveTeleporterInScene(GameObject excludeTeleporter)
        {
            var allTeleporters = UnityEngine.Object.FindObjectsOfType<RoR2.TeleporterInteraction>(false);
            foreach (var teleporter in allTeleporters)
            {
                if (teleporter.gameObject != excludeTeleporter && teleporter.enabled && !PersistenceManager.ShouldDisableTeleporter(teleporter.gameObject))
                {
                    return true;
                }
            }
            return false;
        }


        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger
        {
            [HarmonyPrefix]
            public static bool Prefix(GameObject passengerObject)
            {
                // Check blacklist first - return false to prevent grabbing blacklisted objects
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject.name))
                {
                    return false;
                }

                if (passengerObject == null) return true;

                CharacterBody body = null;
                var localDisabledStates = new Dictionary<GameObject, bool>();

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} AssignPassenger called for {passengerObject}");
                }

                // Ensure autoUpdateModelTransform is true so model follows GameObject if ModelLocator exists
                var modelLocator = passengerObject.GetComponent<ModelLocator>();
                if (modelLocator != null && !modelLocator.autoUpdateModelTransform)
                {
                    // Add ModelStatePreserver to store original state before modifying
                    var statePreserver = passengerObject.AddComponent<ModelStatePreserver>();

                    modelLocator.autoUpdateModelTransform = true;
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Set autoUpdateModelTransform=true for object with ModelLocator {passengerObject.name}");
                    }
                }

                // Cache component lookups
                body = passengerObject.GetComponent<CharacterBody>();

                if (body)
                {
                    // Validate CharacterBody state to prevent crashes with corrupted objects
                    if (body.baseMaxHealth <= 0 || body.levelMaxHealth < 0 ||
                        body.teamComponent == null || body.teamComponent.teamIndex < 0)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Skipping bag assignment for {body.name} due to invalid CharacterBody state: health={body.baseMaxHealth}/{body.levelMaxHealth}, team={(int)(body.teamComponent?.teamIndex ?? (TeamIndex)(-1))}");
                        }
                        return false; // Prevent grabbing
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Assigning {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, currentVehicle: {body.currentVehicle != null}");
                    }

                    // Eject ungrabbable enemies from vehicles before assigning
                    if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && body.currentVehicle != null)
                    {
                        body.currentVehicle.EjectPassenger(passengerObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Ejected {body.name} from vehicle");
                        }
                    }
                }
                
                // Disable all colliders on enemies to prevent movement bugs for flying bosses
                if (body != null && body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable))
                {
                    StateManagement.DisableMovementColliders(passengerObject, localDisabledStates);
                }

                // Special handling for teleporters - disable if there's another active teleporter
                var teleporterInteraction = passengerObject.GetComponent<RoR2.TeleporterInteraction>();
                if (teleporterInteraction != null)
                {
                    // Check if there's another teleporter in the scene that is not disabled
                    bool hasActiveTeleporter = HasActiveTeleporterInScene(passengerObject);
                    if (hasActiveTeleporter)
                    {
                        teleporterInteraction.enabled = false;
                        PersistenceManager.MarkTeleporterForDisabling(passengerObject);

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Disabled TeleporterInteraction on grabbed teleporter {passengerObject.name} - active teleporter found");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Left TeleporterInteraction enabled on grabbed teleporter {passengerObject.name} - no active teleporter found");
                        }
                    }
                }

                // Track the bagged object for persistence
                PersistenceObjectsTracker.TrackBaggedObject(passengerObject);

                return true;
            }
        }
    }
}