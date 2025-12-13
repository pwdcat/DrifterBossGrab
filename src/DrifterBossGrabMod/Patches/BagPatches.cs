using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using System.Reflection;

namespace DrifterBossGrabMod.Patches
{
    public static class BagPatches
    {
        private static bool cachedEnableEnvironmentInvisibility;
        private static bool cachedEnableEnvironmentInteractionDisable;

        /// Debug utility to dump all components and their field values for a GameObject, with special focus on SpecialObjectAttributes
        private static void DumpObjectComponents(GameObject obj, string context = "")
        {
            if (!PluginConfig.EnableComponentAnalysisLogs.Value) return;

            Log.Info($"{Constants.LogPrefix} === DUMPING COMPONENTS FOR {obj.name} ({context}) ===");

            // Get all components
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;

                Log.Info($"{Constants.LogPrefix} Component: {component.GetType().Name}");

                // Special handling for SpecialObjectAttributes
                if (component is SpecialObjectAttributes soa)
                {
                    Log.Info($"{Constants.LogPrefix}   --- SpecialObjectAttributes Details ---");

                    // Use reflection to get all fields
                    var fields = typeof(SpecialObjectAttributes).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        try
                        {
                            var value = field.GetValue(soa);
                            string valueStr;

                            if (value is System.Collections.ICollection collection)
                            {
                                valueStr = $"Collection with {collection.Count} items";
                                if (collection.Count > 0)
                                {
                                    var items = new System.Collections.Generic.List<object>();
                                    foreach (var item in collection)
                                    {
                                        items.Add(item?.ToString() ?? "null");
                                    }
                                    valueStr += $" [{string.Join(", ", items)}]";
                                }
                            }
                            else
                            {
                                valueStr = value?.ToString() ?? "null";
                            }

                            Log.Info($"{Constants.LogPrefix}     {field.Name}: {valueStr}");
                        }
                        catch (Exception ex)
                        {
                            Log.Info($"{Constants.LogPrefix}     {field.Name}: Error getting value - {ex.Message}");
                        }
                    }
                }
                else
                {
                    // For other components, just log basic info
                    Log.Info($"{Constants.LogPrefix}   Type: {component.GetType().FullName}");
                    Log.Info($"{Constants.LogPrefix}   Enabled: {(component as Behaviour)?.enabled ?? true}");
                }
            }

            Log.Info($"{Constants.LogPrefix} === END DUMP FOR {obj.name} ===");
        }

        /// Scans all GameObjects in the current scene and logs their components for analysis
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

        /// Recursively scans a GameObject and its children for components
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

        public static void UpdateEnvironmentSettings(bool enableInvisibility, bool disableInteraction)
        {
            cachedEnableEnvironmentInvisibility = enableInvisibility;
            cachedEnableEnvironmentInteractionDisable = disableInteraction;
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
                var interactable = passengerObject.GetComponent<IInteractable>();
                var rb = passengerObject.GetComponent<Rigidbody>();
                var highlight = passengerObject.GetComponent<Highlight>();

                // Create local state dictionaries
                var localColliderStates = new Dictionary<Collider, bool>();
                var localRendererStates = new Dictionary<Renderer, bool>();
                var localInteractableEnabled = new Dictionary<MonoBehaviour, bool>();
                var localHighlightStates = new Dictionary<Highlight, bool>();
                var localDisabledStates = new Dictionary<GameObject, bool>();

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} AssignPassenger called for {passengerObject}");
                }


                // Clean SpecialObjectAttributes lists to remove null entries
                var soa = passengerObject.GetComponent<SpecialObjectAttributes>();
                if (soa)
                {
                    soa.childSpecialObjectAttributes.RemoveAll(s => s == null);
                    soa.renderersToDisable.RemoveAll(r => r == null);
                    soa.behavioursToDisable.RemoveAll(b => b == null);
                    soa.childObjectsToDisable.RemoveAll(c => c == null);
                    soa.pickupDisplaysToDisable.RemoveAll(p => p == null);
                    soa.lightsToDisable.RemoveAll(l => l == null);
                    soa.objectsToDetach.RemoveAll(o => o == null);
                    soa.skillHighlightRenderers.RemoveAll(r => r == null);
                    
                    // For MinePodBody, disable all colliders and hurtboxes to prevent collision issues
                    if (passengerObject.name.Contains("MinePodBody"))
                    {
                        Traverse.Create(soa).Field("disableAllCollidersAndHurtboxes").SetValue(true);
                    }
                    
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Cleaned null entries from SpecialObjectAttributes on {passengerObject.name}");
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

                    // If has Rigidbody, make kinematic to prevent physics issues
                    if (rb)
                    {
                        rb.isKinematic = true;
                        rb.detectCollisions = false;
                    }
                }

                if (interactable != null)
                {
                    // Special handling for complex objects that cause VehicleSeat issues
                    // MUST happen BEFORE debug logging so the dump shows the correct state
                    if (passengerObject.name.Contains("Teleporter") || passengerObject.name.Contains("Shrine"))
                    {
                        // Disable problematic components that can cause VehicleSeat crashes
                        var combatSquad = passengerObject.GetComponent<CombatSquad>();
                        if (combatSquad != null)
                        {
                            localInteractableEnabled[combatSquad] = combatSquad.enabled;
                            combatSquad.enabled = false;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Disabled CombatSquad on {passengerObject.name}");
                            }
                        }

                        var bossGroup = passengerObject.GetComponent<BossGroup>();
                        if (bossGroup != null)
                        {
                            localInteractableEnabled[bossGroup] = bossGroup.enabled;
                            bossGroup.enabled = false;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Disabled BossGroup on {passengerObject.name}");
                            }
                        }

                        var sceneExitController = passengerObject.GetComponent<SceneExitController>();
                        if (sceneExitController != null)
                        {
                            localInteractableEnabled[sceneExitController] = sceneExitController.enabled;
                            sceneExitController.enabled = false;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Disabled SceneExitController on {passengerObject.name}");
                            }
                        }

                        // Disable PortalSpawners
                        var portalSpawners = passengerObject.GetComponents<PortalSpawner>();
                        foreach (var spawner in portalSpawners)
                        {
                            if (spawner != null)
                            {
                                localInteractableEnabled[spawner] = spawner.enabled;
                                spawner.enabled = false;
                                if (PluginConfig.EnableDebugLogs.Value)
                                {
                                    Log.Info($"{Constants.LogPrefix} Disabled PortalSpawner on {passengerObject.name}");
                                }
                            }
                        }

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Complex object {passengerObject.name} detected - disabled problematic components");
                        }
                    }


                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Assigning IInteractable: {passengerObject.name}");

                        // Debug: Dump components for all grabbed objects
                        DumpObjectComponents(passengerObject, "Object Being Grabbed");
                    }

                    if (cachedEnableEnvironmentInvisibility)
                    {
                        StateManagement.DisableRenderersForInvisibility(passengerObject, localRendererStates);
                    }

                    if (cachedEnableEnvironmentInteractionDisable)
                    {
                        StateManagement.DisableInteractable(interactable, localInteractableEnabled);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Disabling IInteractable on {passengerObject.name}");
                        }
                    }

                    StateManagement.DisableColliders(passengerObject, localColliderStates);

                    // If has Rigidbody, make kinematic
                    if (rb)
                    {
                        rb.isKinematic = true;
                        rb.detectCollisions = false;
                    }

                    // Disable highlight to prevent persistent glow effect
                    if (highlight != null)
                    {
                        localHighlightStates[highlight] = highlight.enabled;
                        highlight.enabled = false;
                    }
                }

                // Disable all colliders on enemies to prevent movement bugs for flying bosses
                if (body != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        var modelLocator = passengerObject.GetComponent<ModelLocator>();
                        Log.Info($"{Constants.LogPrefix} ModelLocator for {passengerObject.name}: {modelLocator != null}");
                    }
                    StateManagement.DisableMovementColliders(passengerObject, localDisabledStates);
                }

                // Check if GrabbedObjectState already exists (created in RepossessExit for trigger state storage)
                var grabbedState = passengerObject.GetComponent<GrabbedObjectState>();
                if (grabbedState == null)
                {
                    // Create new GrabbedObjectState if it doesn't exist
                    grabbedState = passengerObject.AddComponent<GrabbedObjectState>();
                    grabbedState.originalIsTrigger = new Dictionary<Collider, bool>();
                }
                
                // Set the other state dictionaries
                grabbedState.originalColliderStates = localColliderStates;
                grabbedState.originalRendererStates = localRendererStates;
                grabbedState.originalInteractableStates = localInteractableEnabled;
                grabbedState.originalMovementStates = localDisabledStates;
                grabbedState.originalHighlightStates = localHighlightStates;

                // Track the bagged object for persistence
                PersistenceObjectsTracker.TrackBaggedObject(passengerObject);

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Added GrabbedObjectState to {passengerObject.name}");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(RepossessBullseyeSearch), "GetResults")]
        public class RepossessBullseyeSearch_GetResults_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(RepossessBullseyeSearch __instance, ref System.Collections.Generic.IEnumerable<GameObject> __result)
            {
                if (!PluginConfig.EnableEnvironmentGrabbing.Value)
                    return;

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Searching for SpecialObjectAttributes objects: origin={__instance.searchOrigin}, minDist={__instance.minDistanceFilter}, maxDist={__instance.maxDistanceFilter}");
                }

                var existingResults = new HashSet<GameObject>(__result);
                var results = new List<GameObject>(__result);

                // Find all SpecialObjectAttributes in the scene (mimics how chests work)
                foreach (SpecialObjectAttributes soa in UnityEngine.Object.FindObjectsByType<SpecialObjectAttributes>(FindObjectsSortMode.None))
                {
                    var go = soa.gameObject;
                    if (go == null || go.GetComponent<CharacterBody>() != null || go.GetComponent<HurtBox>() != null || existingResults.Contains(go))
                        continue;

                    // Only include if targetable
                    if (!soa.isTargetable) continue;

                    // Check blacklist
                    if (PluginConfig.IsBlacklisted(go.name))
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Skipped blacklisted SpecialObjectAttributes object {go.name}");
                        }
                        continue;
                    }

                    var position = go.transform.position;
                    var vector = position - __instance.searchOrigin;
                    var sqrMagnitude = vector.sqrMagnitude;

                    if (sqrMagnitude >= __instance.minDistanceFilter * __instance.minDistanceFilter &&
                        sqrMagnitude <= __instance.maxDistanceFilter * __instance.maxDistanceFilter)
                    {
                        results.Add(go);
                        existingResults.Add(go);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Added SpecialObjectAttributes object {go.name} at distance {Mathf.Sqrt(sqrMagnitude)}");

                            // Debug: Dump components for all SpecialObjectAttributes objects found in search
                            DumpObjectComponents(go, "SpecialObjectAttributes Object in Search Results");
                        }
                    }
                }
                __result = results;
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), "OnPassengerEnter")]
        public class VehicleSeat_OnPassengerEnter
        {
            [HarmonyPrefix]
            public static bool Prefix(VehicleSeat __instance, GameObject passenger)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} VehicleSeat.OnPassengerEnter called for passenger: {passenger?.name ?? "null"}");

                    if (passenger != null)
                    {
                        var hasCharacterBody = passenger.GetComponent<CharacterBody>() != null;
                        var hasInteractable = passenger.GetComponent<IInteractable>() != null;
                        var hasSpecialObjectAttributes = passenger.GetComponent<SpecialObjectAttributes>() != null;
                        var hasEntityStateMachine = passenger.GetComponent<EntityStateMachine>() != null;
                        var hasNetworkIdentity = passenger.GetComponent<NetworkIdentity>() != null;

                        Log.Info($"{Constants.LogPrefix} Passenger {passenger.name}: hasCharacterBody={hasCharacterBody}, hasInteractable={hasInteractable}, hasSpecialObjectAttributes={hasSpecialObjectAttributes}, hasEntityStateMachine={hasEntityStateMachine}, hasNetworkIdentity={hasNetworkIdentity}");

                        // Log VehicleSeat details
                        Log.Info($"{Constants.LogPrefix} VehicleSeat instance: {__instance?.name ?? "null"}, gameObject: {__instance?.gameObject?.name ?? "null"}");
                        if (__instance != null)
                        {
                            // Log available properties using reflection
                            var properties = __instance.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            foreach (var prop in properties)
                            {
                                try
                                {
                                    var value = prop.GetValue(__instance);
                                    Log.Info($"{Constants.LogPrefix} VehicleSeat {prop.Name}: {value?.ToString() ?? "null"}");
                                }
                                catch
                                {
                                    Log.Info($"{Constants.LogPrefix} VehicleSeat {prop.Name}: <access error>");
                                }
                            }
                        }

                        // Check for problematic components on teleporters
                        if (passenger.name.Contains("Teleporter"))
                        {
                            var combatSquad = passenger.GetComponent<CombatSquad>();
                            var bossGroup = passenger.GetComponent<BossGroup>();
                            var holdoutController = passenger.GetComponent<HoldoutZoneController>();
                            var sceneExitController = passenger.GetComponent<SceneExitController>();

                            Log.Info($"{Constants.LogPrefix} Teleporter components: CombatSquad={combatSquad != null}, BossGroup={bossGroup != null}, HoldoutZoneController={holdoutController != null}, SceneExitController={sceneExitController != null}");

                            if (combatSquad != null) Log.Info($"{Constants.LogPrefix} CombatSquad enabled: {combatSquad.enabled}");
                            if (bossGroup != null) Log.Info($"{Constants.LogPrefix} BossGroup enabled: {bossGroup.enabled}");
                            if (holdoutController != null) Log.Info($"{Constants.LogPrefix} HoldoutZoneController enabled: {holdoutController.enabled}");
                            if (sceneExitController != null) Log.Info($"{Constants.LogPrefix} SceneExitController enabled: {sceneExitController.enabled}");
                        }
                    }
                }

                // Allow all passengers to go through VehicleSeat.OnPassengerEnter
                // Previously skipped environment objects to prevent crashes, but this prevents proper attachment
                // Environment objects need to be attached to the bag for proper grabbing behavior
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(VehicleSeat __instance, GameObject passenger)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} VehicleSeat.OnPassengerEnter completed successfully for passenger: {passenger?.name ?? "null"}");
                }
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), "OnSyncBaggedObject")]
        public class DrifterBagController_OnSyncBaggedObject
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject targetObject)
            {
                if (targetObject == null) return;

                var interactable = targetObject.GetComponent<IInteractable>();
                if (interactable == null) return; // Only handle environment objects

                var grabbedState = targetObject.GetComponent<GrabbedObjectState>();
                if (grabbedState != null) return; // Already handled by AssignPassenger on grabber client

                // Add GrabbedObjectState for other clients
                grabbedState = targetObject.AddComponent<GrabbedObjectState>();
                grabbedState.originalIsTrigger = new Dictionary<Collider, bool>();

                // Apply local client settings
                var localColliderStates = new Dictionary<Collider, bool>();
                var localRendererStates = new Dictionary<Renderer, bool>();
                var localInteractableEnabled = new Dictionary<MonoBehaviour, bool>();
                var localHighlightStates = new Dictionary<Highlight, bool>();
                var localDisabledStates = new Dictionary<GameObject, bool>();

                var rb = targetObject.GetComponent<Rigidbody>();
                var highlight = targetObject.GetComponent<Highlight>();
                var body = targetObject.GetComponent<CharacterBody>();

                if (cachedEnableEnvironmentInvisibility)
                {
                    StateManagement.DisableRenderersForInvisibility(targetObject, localRendererStates);
                }

                if (cachedEnableEnvironmentInteractionDisable)
                {
                    StateManagement.DisableInteractable(interactable, localInteractableEnabled);
                }

                StateManagement.DisableColliders(targetObject, localColliderStates);

                // Make kinematic if has Rigidbody
                if (rb)
                {
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }

                // Disable highlight
                if (highlight != null)
                {
                    localHighlightStates[highlight] = highlight.enabled;
                    highlight.enabled = false;
                }

                // Disable movement colliders for enemies
                if (body != null)
                {
                    StateManagement.DisableMovementColliders(targetObject, localDisabledStates);
                }

                // Set the states on GrabbedObjectState
                grabbedState.originalColliderStates = localColliderStates;
                grabbedState.originalRendererStates = localRendererStates;
                grabbedState.originalInteractableStates = localInteractableEnabled;
                grabbedState.originalMovementStates = localDisabledStates;
                grabbedState.originalHighlightStates = localHighlightStates;

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Applied local settings to grabbed object {targetObject.name} via sync");
                }
            }
        }
    }
}