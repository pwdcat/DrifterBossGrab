using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using System.Linq;
using System.Reflection.Emit;
using EntityStates;
using RoR2.ContentManagement;

namespace DrifterBossGrabMod.Patches
{
    public static class GrabbableObjectPatches
    {
        // Cache frequently used component types to reduce reflection overhead
        private static readonly System.Type SceneReductionType = typeof(SceneReduction);
        private static readonly System.Type EntityStateMachineType = typeof(EntityStateMachine);
        private static readonly System.Type NetworkIdentityType = typeof(NetworkIdentity);
        private static readonly System.Type SpecialObjectAttributesType = typeof(SpecialObjectAttributes);


        // Object pooling for temporary collections to reduce GC pressure
        private static class ComponentPool
        {
            private static readonly Stack<List<Renderer>> _rendererLists = new Stack<List<Renderer>>();
            private static readonly Stack<List<Collider>> _colliderLists = new Stack<List<Collider>>();
            private static readonly Stack<List<Light>> _lightLists = new Stack<List<Light>>();
            private static readonly Stack<List<MonoBehaviour>> _behaviorLists = new Stack<List<MonoBehaviour>>();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static List<Renderer> RentRendererList(int capacity = 16)
            {
                if (_rendererLists.Count > 0)
                {
                    var list = _rendererLists.Pop();
                    list.Clear();
                    if (list.Capacity < capacity) list.Capacity = capacity;
                    return list;
                }
                return new List<Renderer>(capacity);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ReturnRendererList(List<Renderer> list)
            {
                if (list != null && _rendererLists.Count < 10) // Limit pool size
                {
                    _rendererLists.Push(list);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static List<Collider> RentColliderList(int capacity = 16)
            {
                if (_colliderLists.Count > 0)
                {
                    var list = _colliderLists.Pop();
                    list.Clear();
                    if (list.Capacity < capacity) list.Capacity = capacity;
                    return list;
                }
                return new List<Collider>(capacity);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ReturnColliderList(List<Collider> list)
            {
                if (list != null && _colliderLists.Count < 10)
                {
                    _colliderLists.Push(list);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static List<Light> RentLightList(int capacity = 8)
            {
                if (_lightLists.Count > 0)
                {
                    var list = _lightLists.Pop();
                    list.Clear();
                    if (list.Capacity < capacity) list.Capacity = capacity;
                    return list;
                }
                return new List<Light>(capacity);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ReturnLightList(List<Light> list)
            {
                if (list != null && _lightLists.Count < 10)
                {
                    _lightLists.Push(list);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static List<MonoBehaviour> RentBehaviorList(int capacity = 8)
            {
                if (_behaviorLists.Count > 0)
                {
                    var list = _behaviorLists.Pop();
                    list.Clear();
                    if (list.Capacity < capacity) list.Capacity = capacity;
                    return list;
                }
                return new List<MonoBehaviour>(capacity);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ReturnBehaviorList(List<MonoBehaviour> list)
            {
                if (list != null && _behaviorLists.Count < 10)
                {
                    _behaviorLists.Push(list);
                }
            }
        }

        private static GameObject FindEntityStateMachineTarget(GameObject obj)
        {
            // Special handling for LOD objects - prefer parent with SceneReduction
            if (obj.name.Contains("_LOD"))
            {
                Transform lodParent = obj.transform.parent;
                while (lodParent != null)
                {
                    // Check if this parent has SceneReduction component using cached type
                    if (lodParent.gameObject.GetComponent(SceneReductionType) != null)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Found SceneReduction parent {lodParent.name} for LOD object {obj.name}");
                        }
                        return lodParent.gameObject;
                    }
                    lodParent = lodParent.parent;
                }
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} No SceneReduction parent found for LOD object {obj.name}, using object itself");
                }
            }

            // First check if the object itself has an EntityStateMachine using cached type
            if (obj.GetComponent(EntityStateMachineType) != null)
            {
                return obj;
            }

            // If not, traverse up the hierarchy, but only through objects with GrabbableComponentTypes
            Transform current = obj.transform.parent;

            while (current != null)
            {
                // Check if this parent object has an EntityStateMachine and is grabbable using cached type
                if (current.gameObject.GetComponent(EntityStateMachineType) != null && PluginConfig.IsGrabbable(current.gameObject))
                {
                    return current.gameObject;
                }

                // Move to next parent
                current = current.parent;
            }

            // No EntityStateMachine found, use the root if it's grabbable, otherwise use obj
            return PluginConfig.IsGrabbable(obj.transform.root.gameObject) ? obj.transform.root.gameObject : obj;
        }

        public static void AddSpecialObjectAttributesToGrabbableObject(GameObject obj)
        {
            if (obj == null)
                return;

            // Cache the object name to avoid repeated property access
            string objName = obj.name;

            // Pre-cache lowercase name for multiple string operations
            string lowerObjName = objName.ToLowerInvariant();

            // Only process objects that have the required GrabbableComponentTypes
            if (!PluginConfig.IsGrabbable(obj))
                return;

            // Special handling for SurvivorPod - wait until it lands
            if (lowerObjName.Contains("survivorpod"))
            {
                var podEsm = obj.GetComponent(EntityStateMachineType) as EntityStateMachine;
                if (podEsm != null && podEsm.state is EntityStates.SurvivorPod.Descent)
                {
                    // Pod is still landing, set up a callback to handle it when it lands
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} SurvivorPod {objName} is still landing, delaying grab setup until landed");
                    }

                    // Use a simple approach: check again in a few seconds
                    // This is simpler than hooking into state changes
                    DrifterBossGrabPlugin.Instance.StartCoroutine(DelayedSurvivorPodSetup(obj));
                    return;
                }
            }

            // Ensure the object has a name for identification and blacklisting
            if (string.IsNullOrEmpty(objName))
            {
                objName = obj.name = "GrabbableObject_" + obj.GetInstanceID();
                lowerObjName = objName.ToLowerInvariant(); // Update cached lowercase
            }

            // Find the appropriate target object for EntityStateMachine management
            var targetObj = FindEntityStateMachineTarget(obj);

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Using target object {targetObj.name} for EntityStateMachine management (original: {objName})");
            }

            // Ensure the target object has an EntityStateMachine for state management during grabbing
            var esm = targetObj.GetComponent(EntityStateMachineType) as EntityStateMachine;
            if (esm == null)
            {
                esm = targetObj.AddComponent<EntityStateMachine>();
                esm.customName = "Body"; // Standard name for state machines
                esm.initialStateType = new SerializableEntityStateType(typeof(EntityStates.Uninitialized));
                esm.mainStateType = new SerializableEntityStateType(typeof(EntityStates.Uninitialized));
                esm.networkIndex = -1;
                esm.AllowStartWithoutNetworker = true;

                // Initialize the state machine with the Idle state
                if (esm.state is EntityStates.Uninitialized)
                {
                    esm.SetState(EntityStateCatalog.InstantiateState(ref esm.initialStateType));
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Added EntityStateMachine to target {targetObj.name} for grabbing {objName}, state now: {esm.state}");
                }
            }
            else
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Target {targetObj.name} already has EntityStateMachine for grabbing {objName}: state = {esm.state}");
                }
            }

            // Ensure the target object has NetworkIdentity for networking synchronization
            var networkIdentity = targetObj.GetComponent(NetworkIdentityType) as NetworkIdentity;
            if (networkIdentity != null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Target {targetObj.name} already has NetworkIdentity for grabbing {objName}: netId = {networkIdentity.netId}");
                }
            }
            else
            {
                // Object doesn't have NetworkIdentity - add it and try to spawn it on the network
                networkIdentity = targetObj.AddComponent<NetworkIdentity>();
                networkIdentity.serverOnly = false;
                networkIdentity.localPlayerAuthority = false;

                // Try to spawn the object on the network
                try
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.Spawn(targetObj);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Successfully spawned {targetObj.name} on network with netId = {networkIdentity.netId}");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} NetworkServer not active, cannot spawn {targetObj.name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Failed to spawn {targetObj.name} on network: {ex.Message}");
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Added NetworkIdentity to target {targetObj.name} for grabbing {objName}: netId = {networkIdentity.netId}");
                }
            }

            // Check if already has SpecialObjectAttributes using cached type
            var existingSoa = targetObj.GetComponent(SpecialObjectAttributesType) as SpecialObjectAttributes;
            if (existingSoa != null)
            {
                // Check if this object should be grabbable
                bool shouldBeGrabbable = PluginConfig.IsGrabbable(obj);

                if (shouldBeGrabbable)
                {
                    // If it already has SpecialObjectAttributes, ensure it's configured for grabbing
                    if (!existingSoa.grabbable || string.IsNullOrEmpty(existingSoa.breakoutStateMachineName))
                    {
                        existingSoa.grabbable = true;
                        existingSoa.breakoutStateMachineName = ""; // Required for BaggedObject to attach the object
                        existingSoa.orientToFloor = true; // Like chests

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Updated existing SpecialObjectAttributes on {targetObj.name} for grabbing {objName}");
                        }
                    }

                    // Ensure isVoid is set if not already - cache the lowercase check
                    if (!existingSoa.isVoid && objName.ToLower().Contains("void"))
                    {
                        existingSoa.isVoid = true;
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Marked existing SpecialObjectAttributes on {targetObj.name} for {objName} as void object");
                        }
                    }

                    // Add any missing lights to lightsToDisable
                    var existingLights = obj.GetComponentsInChildren<Light>(false);
                    foreach (var light in existingLights)
                    {
                        if (!existingSoa.lightsToDisable.Contains(light))
                        {
                            existingSoa.lightsToDisable.Add(light);
                        }
                    }

                    if (PluginConfig.EnableDebugLogs.Value && existingLights.Length > 0)
                    {
                        Log.Info($"{Constants.LogPrefix} Added {existingLights.Length} lights to existing SpecialObjectAttributes for {targetObj.name} from {objName}");
                    }

                    // Add any missing PickupDisplays to pickupDisplaysToDisable
                    var existingPickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
                    foreach (var pickupDisplay in existingPickupDisplays)
                    {
                        if (!existingSoa.pickupDisplaysToDisable.Contains(pickupDisplay))
                        {
                            existingSoa.pickupDisplaysToDisable.Add(pickupDisplay);
                        }
                    }

                    if (PluginConfig.EnableDebugLogs.Value && existingPickupDisplays.Length > 0)
                    {
                        Log.Info($"{Constants.LogPrefix} Added {existingPickupDisplays.Length} PickupDisplays to existing SpecialObjectAttributes for {targetObj.name} from {objName}");
                    }

                }
                else
                {
                    // Object should not be grabbable, disable it
                    existingSoa.grabbable = false;

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Disabled SpecialObjectAttributes on {targetObj.name} - {objName} not allowed by GrabbableComponentTypes");
                    }
                }
                return;
            }

            // Add SpecialObjectAttributes to make it grabbable like chests
            var soa = targetObj.AddComponent<SpecialObjectAttributes>();

            // Calculate scaled attributes based on object size
            var (scaledMass, scaledDurability) = CalculateScaledAttributes(obj, objName);

            // Configure for grabbing (similar to chests)
            soa.grabbable = true;
            soa.massOverride = scaledMass; // Scaled mass based on object size
            soa.maxDurability = scaledDurability; // Scaled durability based on object size
            soa.durability = scaledDurability; // Set current durability to max
            soa.hullClassification = HullClassification.Human;
            soa.breakoutStateMachineName = ""; // Required for BaggedObject to attach the object
            soa.orientToFloor = true; // Like chests

            // Use pre-cached lowercase name for display name and void check
            string displayName = objName.Replace("(Clone)", "");

            // Remove numeric suffixes like (1), (0), etc.
            var numericSuffixPattern = new System.Text.RegularExpressions.Regex(@"\s*\(\d+\)$");
            displayName = numericSuffixPattern.Replace(displayName, "");

            soa.bestName = displayName;

            // Use pre-cached lowercase name for void check
            soa.isVoid = lowerObjName.Contains("void");

            if (PluginConfig.EnableDebugLogs.Value && soa.isVoid)
            {
                Log.Info($"{Constants.LogPrefix} Marked {objName} as void object");
            }


            // Set up basic state management collections
            soa.renderersToDisable = new System.Collections.Generic.List<Renderer>(16);
            soa.behavioursToDisable = new System.Collections.Generic.List<MonoBehaviour>(8);
            soa.collisionToDisable = new System.Collections.Generic.List<GameObject>(16);
            soa.childObjectsToDisable = new System.Collections.Generic.List<GameObject>(4);
            soa.pickupDisplaysToDisable = new System.Collections.Generic.List<PickupDisplay>(2);
            soa.lightsToDisable = new System.Collections.Generic.List<Light>(4);
            soa.objectsToDetach = new System.Collections.Generic.List<GameObject>(2);
            soa.childSpecialObjectAttributes = new System.Collections.Generic.List<SpecialObjectAttributes>(2);
            soa.skillHighlightRenderers = new System.Collections.Generic.List<Renderer>(4);
            soa.soundEventsToStop = new System.Collections.Generic.List<AkEvent>(2);
            soa.soundEventsToPlay = new System.Collections.Generic.List<AkEvent>(2);

            // Find and configure components using pooled collections to reduce GC pressure
            var renderers = ComponentPool.RentRendererList();
            obj.GetComponentsInChildren(false, renderers);
            foreach (var renderer in renderers)
            {
                soa.renderersToDisable.Add(renderer);
            }
            ComponentPool.ReturnRendererList(renderers);

            var colliders = ComponentPool.RentColliderList();
            obj.GetComponentsInChildren(false, colliders);
            foreach (var collider in colliders)
            {
                soa.collisionToDisable.Add(collider.gameObject);
            }
            ComponentPool.ReturnColliderList(colliders);

            // Find and disable lights using pooled collection
            var lights = ComponentPool.RentLightList();
            obj.GetComponentsInChildren(false, lights);
            foreach (var light in lights)
            {
                soa.lightsToDisable.Add(light);
            }

            if (PluginConfig.EnableDebugLogs.Value && lights.Count > 0)
            {
                Log.Info($"{Constants.LogPrefix} Added {lights.Count} lights to disable for {objName}");
            }
            ComponentPool.ReturnLightList(lights);

            // Find and disable PickupDisplays
            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                soa.pickupDisplaysToDisable.Add(pickupDisplay);
            }

            if (PluginConfig.EnableDebugLogs.Value && pickupDisplays.Length > 0)
            {
                Log.Info($"{Constants.LogPrefix} Added {pickupDisplays.Length} PickupDisplays to disable for {objName}");
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Added SpecialObjectAttributes to target object: {targetObj.name} for grabbable object: {objName}");
            }
        }

        private static IEnumerator DelayedSurvivorPodSetup(GameObject survivorPod)
        {
            // Wait a few seconds for the pod to potentially land
            yield return new WaitForSeconds(5f);

            // Check if the pod still exists and has landed
            if (survivorPod != null)
            {
                var esm = survivorPod.GetComponent<EntityStateMachine>();
                if (esm != null)
                {
                    // Check if it's now in Landed state or later
                    if (esm.state is EntityStates.SurvivorPod.Landed ||
                        esm.state is EntityStates.SurvivorPod.PreRelease ||
                        esm.state is EntityStates.SurvivorPod.Release ||
                        esm.state is EntityStates.SurvivorPod.ReleaseFinished)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} SurvivorPod {survivorPod.name} has landed (state: {esm.state}), setting up for grabbing");
                        }

                        // Now set it up for grabbing
                        AddSpecialObjectAttributesToGrabbableObject(survivorPod);
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} SurvivorPod {survivorPod.name} still not landed (state: {esm.state}), skipping grab setup");
                        }
                    }
                }
            }
        }

        public static void AddSpecialObjectAttributesToProjectile(GameObject obj)
        {
            if (obj == null)
                return;

            // Cache the object name to avoid repeated property access
            string objName = obj.name;

            // Pre-cache lowercase name for multiple string operations
            string lowerObjName = objName.ToLowerInvariant();

            // Ensure the object has a name for identification and blacklisting
            if (string.IsNullOrEmpty(objName))
            {
                objName = obj.name = "Projectile_" + obj.GetInstanceID();
                lowerObjName = objName.ToLowerInvariant(); // Update cached lowercase
            }

            // For projectiles, use the object itself as the target (projectiles are usually simple objects)
            var targetObj = obj;

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Using projectile object {targetObj.name} for SpecialObjectAttributes (original: {objName})");
            }

            // Ensure the target object has NetworkIdentity for networking synchronization
            var networkIdentity = targetObj.GetComponent(NetworkIdentityType) as NetworkIdentity;
            if (networkIdentity != null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Target {targetObj.name} already has NetworkIdentity: netId = {networkIdentity.netId}");
                }
            }
            else
            {
                // Object doesn't have NetworkIdentity - add it and try to spawn it on the network
                networkIdentity = targetObj.AddComponent<NetworkIdentity>();
                networkIdentity.serverOnly = false;
                networkIdentity.localPlayerAuthority = false;

                // Try to spawn the object on the network
                try
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.Spawn(targetObj);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Successfully spawned projectile {targetObj.name} on network with netId = {networkIdentity.netId}");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} NetworkServer not active, cannot spawn projectile {targetObj.name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Failed to spawn projectile {targetObj.name} on network: {ex.Message}");
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Added NetworkIdentity to projectile {targetObj.name}: netId = {networkIdentity.netId}");
                }
            }

            // Check if already has SpecialObjectAttributes using cached type
            var existingSoa = targetObj.GetComponent(SpecialObjectAttributesType) as SpecialObjectAttributes;
            if (existingSoa != null)
            {
                // Ensure it's configured for grabbing
                if (!existingSoa.grabbable || string.IsNullOrEmpty(existingSoa.breakoutStateMachineName))
                {
                    existingSoa.grabbable = true;
                    existingSoa.breakoutStateMachineName = ""; // Required for BaggedObject to attach the object
                    existingSoa.orientToFloor = true; // Like chests

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Updated existing SpecialObjectAttributes on projectile {targetObj.name}");
                    }
                }
                return;
            }

            // Add SpecialObjectAttributes to make the projectile grabbable
            var soa = targetObj.AddComponent<SpecialObjectAttributes>();

            // Calculate scaled attributes based on object size
            var (scaledMass, scaledDurability) = CalculateScaledAttributes(obj, objName);

            // Configure for grabbing (similar to chests)
            soa.grabbable = true;
            soa.massOverride = scaledMass; // Scaled mass based on object size
            soa.maxDurability = scaledDurability; // Scaled durability based on object size
            soa.durability = scaledDurability; // Set current durability to max
            soa.hullClassification = HullClassification.Human;
            soa.breakoutStateMachineName = ""; // Required for BaggedObject to attach the object
            soa.orientToFloor = true; // Like chests

            // Use pre-cached lowercase name for display name and void check
            string displayName = objName.Replace("(Clone)", "");

            // Remove numeric suffixes like (1), (0), etc.
            var numericSuffixPattern = new System.Text.RegularExpressions.Regex(@"\s*\(\d+\)$");
            displayName = numericSuffixPattern.Replace(displayName, "");

            soa.bestName = displayName;

            // Use pre-cached lowercase name for void check
            soa.isVoid = lowerObjName.Contains("void");

            if (PluginConfig.EnableDebugLogs.Value && soa.isVoid)
            {
                Log.Info($"{Constants.LogPrefix} Marked projectile {objName} as void object");
            }

            // Set up basic state management collections
            soa.renderersToDisable = new System.Collections.Generic.List<Renderer>(16);
            soa.behavioursToDisable = new System.Collections.Generic.List<MonoBehaviour>(8);
            soa.collisionToDisable = new System.Collections.Generic.List<GameObject>(16);
            soa.childObjectsToDisable = new System.Collections.Generic.List<GameObject>(4);
            soa.pickupDisplaysToDisable = new System.Collections.Generic.List<PickupDisplay>(2);
            soa.lightsToDisable = new System.Collections.Generic.List<Light>(4);
            soa.objectsToDetach = new System.Collections.Generic.List<GameObject>(2);
            soa.childSpecialObjectAttributes = new System.Collections.Generic.List<SpecialObjectAttributes>(2);
            soa.skillHighlightRenderers = new System.Collections.Generic.List<Renderer>(4);
            soa.soundEventsToStop = new System.Collections.Generic.List<AkEvent>(2);
            soa.soundEventsToPlay = new System.Collections.Generic.List<AkEvent>(2);

            // Find and configure components using pooled collections to reduce GC pressure
            var renderers = ComponentPool.RentRendererList();
            obj.GetComponentsInChildren(false, renderers);
            foreach (var renderer in renderers)
            {
                soa.renderersToDisable.Add(renderer);
            }
            ComponentPool.ReturnRendererList(renderers);

            var colliders = ComponentPool.RentColliderList();
            obj.GetComponentsInChildren(false, colliders);
            foreach (var collider in colliders)
            {
                soa.collisionToDisable.Add(collider.gameObject);
            }
            ComponentPool.ReturnColliderList(colliders);

            // Find and disable lights using pooled collection
            var lights = ComponentPool.RentLightList();
            obj.GetComponentsInChildren(false, lights);
            foreach (var light in lights)
            {
                soa.lightsToDisable.Add(light);
            }

            if (PluginConfig.EnableDebugLogs.Value && lights.Count > 0)
            {
                Log.Info($"{Constants.LogPrefix} Added {lights.Count} lights to disable for projectile {objName}");
            }
            ComponentPool.ReturnLightList(lights);

            // Find and disable PickupDisplays
            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                soa.pickupDisplaysToDisable.Add(pickupDisplay);
            }

            if (PluginConfig.EnableDebugLogs.Value && pickupDisplays.Length > 0)
            {
                Log.Info($"{Constants.LogPrefix} Added {pickupDisplays.Length} PickupDisplays to disable for projectile {objName}");
            }

            // Find and disable ProjectileStickOnImpact components to prevent position reset on throw
            var stickOnImpactComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
            foreach (var stickComponent in stickOnImpactComponents)
            {
                soa.behavioursToDisable.Add(stickComponent);
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Added ProjectileStickOnImpact to disable for projectile {objName}");
                }
            }

            // Find and disable ProjectileFuse components to prevent premature detonation
            var fuseComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileFuse>(true);
            foreach (var fuseComponent in fuseComponents)
            {
                soa.behavioursToDisable.Add(fuseComponent);
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Added ProjectileFuse to disable for projectile {objName}");
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Added SpecialObjectAttributes to projectile: {targetObj.name}");
            }
        }

        public static void EnsureAllGrabbableObjectsHaveSpecialObjectAttributes()
        {
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                AddSpecialObjectAttributesToGrabbableObject(go);
            }
        }


        #region Harmony Patches

        [HarmonyPatch(typeof(DirectorCore), "TrySpawnObject")]
        public class DirectorCore_TrySpawnObject_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(GameObject __result)
            {
                if (__result)
                {
                    // Make sure it has SpecialObjectAttributes for grabbing if it's grabbable
                    AddSpecialObjectAttributesToGrabbableObject(__result);
                }
            }
        }

        /// Calculates a size metric for an object based on its colliders
        private static float CalculateObjectSizeMetric(GameObject obj)
        {
            if (obj == null) return 1f;

            float totalSize = 0f;
            var colliders = obj.GetComponentsInChildren<Collider>(false);

            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;

                if (collider is BoxCollider box)
                {
                    var size = box.size;
                    totalSize += size.x * size.y * size.z; // Volume
                }
                else if (collider is SphereCollider sphere)
                {
                    float radius = sphere.radius;
                    totalSize += (4f/3f) * Mathf.PI * radius * radius * radius; // Volume
                }
                else if (collider is CapsuleCollider capsule)
                {
                    float radius = capsule.radius;
                    float height = capsule.height;
                    // Approximate volume for capsule
                    totalSize += Mathf.PI * radius * radius * height;
                }
                else if (collider is MeshCollider mesh)
                {
                    // For mesh colliders, use bounds volume as approximation
                    var bounds = mesh.bounds;
                    totalSize += bounds.size.x * bounds.size.y * bounds.size.z;
                }
            }

            // Ensure minimum size
            totalSize = Mathf.Max(totalSize, 0.1f);

            return totalSize;
        }

        /// Calculates scaled mass and durability based on object size
        private static (float massOverride, int maxDurability) CalculateScaledAttributes(GameObject obj, string objName)
        {
            float sizeMetric = CalculateObjectSizeMetric(obj);

            const float referenceSize = 10f;
            const float baseMass = 100f;
            const int baseDurability = 8;

            // Calculate scale factor (clamp to reasonable range)
            float scaleFactor = Mathf.Clamp(sizeMetric / referenceSize, 0.5f, 5f);

            // Scale mass and durability
            float scaledMass = baseMass * scaleFactor;
            int scaledDurability = Mathf.RoundToInt(baseDurability * scaleFactor);

            // Ensure minimum values
            scaledMass = Mathf.Max(scaledMass, 25f);
            scaledDurability = Mathf.Max(scaledDurability, 3);

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Size scaling for {objName}: sizeMetric={sizeMetric:F2}, scaleFactor={scaleFactor:F2}, mass={scaledMass:F0}, durability={scaledDurability}");
            }

            return (scaledMass, scaledDurability);
        }


        #endregion

        #region SpecialObjectAttributes Patches

        [HarmonyPatch(typeof(SpecialObjectAttributes), "Start")]
        public class SpecialObjectAttributes_Start_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance)
            {
                // After the original Start() logic, if portraitIcon is still null, set default icons
                if (__instance.portraitIcon == null)
                {
                    string lowerCaseName = __instance.gameObject.name.ToLowerInvariant();
                    string iconPath = GetIconPathForObject(lowerCaseName);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        __instance.portraitIcon = Addressables.LoadAssetAsync<Texture>(iconPath).WaitForCompletion();
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Set default icon {iconPath} for {__instance.gameObject.name}");
                        }
                    }
                }
            }
        }

        private static string GetIconPathForObject(string lowerCaseName)
        {
            // Lunar objects (lunar, newt)
            if (lowerCaseName.Contains("lunar") || lowerCaseName.Contains("newt") || lowerCaseName.Contains("portalshop") || lowerCaseName.Contains("portalms"))
            {
                return "RoR2/Base/LunarIcon_1.png";
            }

            // Void objects
            if (lowerCaseName.Contains("void"))
            {
                return "RoR2/Base/VoidIcon_2.png";
            }

            // Halyconite, DLC2
            if (lowerCaseName.Contains("halcyonite") || lowerCaseName.Contains("colossus"))
            {
                return "RoR2/Base/texColossusExpansionIcon2White.png";
            }
            
            // Golden Portal
            if (lowerCaseName.Contains("portalgoldshores"))
            {
                return "RoR2/Base/TitanGoldDuringTP/texGoldHeartIcon.png";
            }

            // Teleporters and portals
            if (lowerCaseName.Contains("teleporter") || lowerCaseName.Contains("portal"))
            {
                return "RoR2/Base/Common/MiscIcons/texTeleporterIconOutlined.png";
            }

            // Shrines
            if (lowerCaseName.Contains("shrine") || lowerCaseName.Contains("statue"))
            {
                return "RoR2/Base/ShrineIcon.png";
            }

            // Pillars
            if (lowerCaseName.Contains("pillar"))
            {
                return "RoR2/Base/PillarIcon.png";
            }

            // Vending Machines
            if (lowerCaseName.Contains("vending"))
            {
                return "RoR2/DLC1/VendingMachine/texVendingMachineBody.png";
            }

            // Pots
            if (lowerCaseName.Contains("pot"))
            {
                return "RoR2/Base/ExplosivePotDestructible/texExplosivePotDestructibleBody.png";
            }

            // SurvivorPod and Ships
            if (lowerCaseName.Contains("ship") || lowerCaseName.Contains("survivor"))
            {
                return "RoR2/Base/Common/MiscIcons/texRescueshipIcon.png";
            }

            // Rocks
            if (lowerCaseName.Contains("rock") || lowerCaseName.Contains("chunk") || lowerCaseName.Contains("boulder"))
            {
                return "RoR2/Base/skymeadow/texSMMaulingRock.png";
            }

            // Default fallback
            return "RoR2/Base/Common/MiscIcons/texMysteryIcon.png";
        }

        [HarmonyPatch(typeof(EntityStates.CaptainSupplyDrop.BaseCaptainSupplyDropState), "OnEnter")]
        public class BaseCaptainSupplyDropState_OnEnter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.CaptainSupplyDrop.BaseCaptainSupplyDropState __instance)
            {
                // Add SpecialObjectAttributes to the supply drop when its state starts
                AddSpecialObjectAttributesToGrabbableObject(__instance.outer.gameObject);
            }
        }

        #endregion
    }
}