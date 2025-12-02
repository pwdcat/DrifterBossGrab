using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using System.Linq;

namespace DrifterBossGrabMod.Patches
{
    public static class BagPatches
    {
        private static bool cachedEnableEnvironmentInvisibility;
        private static bool cachedEnableEnvironmentInteractionDisable;

        public static void UpdateEnvironmentSettings(bool enableInvisibility, bool disableInteraction)
        {
            cachedEnableEnvironmentInvisibility = enableInvisibility;
            cachedEnableEnvironmentInteractionDisable = disableInteraction;
        }

        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger_PreventBlacklisted
        {
            [HarmonyPrefix]
            public static bool Prefix(GameObject passengerObject)
            {
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject.name))
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger
        {
            [HarmonyPrefix]
            public static void Prefix(GameObject passengerObject)
            {
                if (!passengerObject) return;

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
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Assigning IInteractable: {passengerObject.name}");
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
                    Log.Info($"{Constants.LogPrefix} Searching for interactables: origin={__instance.searchOrigin}, minDist={__instance.minDistanceFilter}, maxDist={__instance.maxDistanceFilter}");
                }

                // Initialize or refresh cache when needed
                if (!InteractableCachingPatches.IsCacheInitialized || InteractableCachingPatches.CacheNeedsRefresh)
                {
                    InteractableCachingPatches.RefreshCache();
                }

                var existingResults = new HashSet<GameObject>(__result);
                var results = new List<GameObject>(__result);

                foreach (var go in InteractableCachingPatches.CachedInteractables)
                {
                    if (go == null || go.GetComponent<CharacterBody>() != null || go.GetComponent<HurtBox>() != null || existingResults.Contains(go))
                        continue;

                    // Check blacklist
                    if (PluginConfig.IsBlacklisted(go.name))
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Skipped blacklisted interactable {go.name}");
                        }
                        continue;
                    }

                    var position = go.transform.position;
                    var vector = position - __instance.searchOrigin;
                    var sqrMagnitude = vector.sqrMagnitude;

                    if (sqrMagnitude >= __instance.minDistanceFilter * __instance.minDistanceFilter && 
                        sqrMagnitude <= __instance.maxDistanceFilter * __instance.maxDistanceFilter)
                    {
                        // Simple distance check
                        results.Add(go);
                        existingResults.Add(go);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Added interactable {go.name} at distance {Mathf.Sqrt(sqrMagnitude)}");
                        }
                    }
                }
                __result = results;
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