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
            private const int MaxPoolSize = 25;
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
                if (list != null && _rendererLists.Count < MaxPoolSize)
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
                if (list != null && _colliderLists.Count < MaxPoolSize)
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
                if (list != null && _lightLists.Count < MaxPoolSize)
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
                if (list != null && _behaviorLists.Count < MaxPoolSize)
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
                    // Check if this parent has SceneReduction component using cached type and TryGetComponent
                    if (lodParent.gameObject.TryGetComponent(out SceneReduction _))
                    {
                        return lodParent.gameObject;
                    }
                    lodParent = lodParent.parent;
                }

            }
            // First check if the object itself has an EntityStateMachine using cached type and TryGetComponent
            if (obj.TryGetComponent(out EntityStateMachine _))
            {
                return obj;
            }
            // If not, traverse up the hierarchy, but only through objects with GrabbableComponentTypes
            Transform current = obj.transform.parent;
            while (current != null)
            {
                // Check if this parent object has an EntityStateMachine and is grabbable using cached type and TryGetComponent
                if (current.gameObject.TryGetComponent(out EntityStateMachine _) && PluginConfig.IsGrabbable(current.gameObject))
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
                if (obj.TryGetComponent(out EntityStateMachine podEsm) && podEsm.state is EntityStates.SurvivorPod.Descent)
                {
                    DrifterBossGrabPlugin.Instance?.StartCoroutine(DelayedSurvivorPodSetup(obj));
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

            // Ensure the target object has an EntityStateMachine for state management during grabbing
            if (!targetObj.TryGetComponent(out EntityStateMachine esm))
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
            }
            // Ensure the target object has NetworkIdentity for networking synchronization
            if (!targetObj.TryGetComponent(out NetworkIdentity networkIdentity))
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
                    }
                }
                catch (Exception)
                {
                    // Silently handle spawn errors
                }
            }
            // Check if already has SpecialObjectAttributes using cached type and TryGetComponent
            if (targetObj.TryGetComponent(out SpecialObjectAttributes existingSoa))
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
                    }
                    // Ensure isVoid is set if not already - use pre-cached lowercase name
                    if (!existingSoa.isVoid && lowerObjName.Contains("void"))
                    {
                        existingSoa.isVoid = true;
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

                    // Add any missing PickupDisplays to pickupDisplaysToDisable
                    var existingPickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
                    foreach (var pickupDisplay in existingPickupDisplays)
                    {
                        if (!existingSoa.pickupDisplaysToDisable.Contains(pickupDisplay))
                        {
                            existingSoa.pickupDisplaysToDisable.Add(pickupDisplay);
                        }
                    }

                }
                else
                {
                    // Object should not be grabbable, disable it
                    existingSoa.grabbable = false;
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

            ComponentPool.ReturnLightList(lights);
            // Find and disable PickupDisplays
            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                soa.pickupDisplaysToDisable.Add(pickupDisplay);
            }

        }
        private static IEnumerator DelayedSurvivorPodSetup(GameObject survivorPod)
        {
            // Wait a few seconds for the pod to potentially land
            yield return new WaitForSeconds(5f);
            // Check if the pod still exists and has landed
            if (survivorPod != null && survivorPod.TryGetComponent(out EntityStateMachine esm))
            {
                // Check if it's now in Landed state or later
                if (esm.state is EntityStates.SurvivorPod.Landed ||
                    esm.state is EntityStates.SurvivorPod.PreRelease ||
                    esm.state is EntityStates.SurvivorPod.Release ||
                    esm.state is EntityStates.SurvivorPod.ReleaseFinished)
                {
                    // Now set it up for grabbing
                    AddSpecialObjectAttributesToGrabbableObject(survivorPod);
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

            // Ensure the target object has NetworkIdentity for networking synchronization
            if (!targetObj.TryGetComponent(out NetworkIdentity networkIdentity))
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
                    }
                }
                catch (Exception)
                {
                    // Silently handle spawn errors
                }
            }
            // Check if already has SpecialObjectAttributes using cached type and TryGetComponent
            if (!targetObj.TryGetComponent(out SpecialObjectAttributes soa))
            {
                // Add SpecialObjectAttributes to make the projectile grabbable
                soa = targetObj.AddComponent<SpecialObjectAttributes>();
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
            }
            else
            {
                // Existing SpecialObjectAttributes found - preserve its configuration
                // Only ensure grabbable is set to true if it wasn't already
                if (!soa.grabbable)
                {
                    soa.grabbable = true;
                }
                // Ensure breakoutStateMachineName is set for grabbing to work
                if (string.IsNullOrEmpty(soa.breakoutStateMachineName))
                {
                    soa.breakoutStateMachineName = "";
                }
                // Ensure orientToFloor is set
                soa.orientToFloor = true;
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

            ComponentPool.ReturnLightList(lights);
            // Find and disable PickupDisplays
            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                soa.pickupDisplaysToDisable.Add(pickupDisplay);
            }

            // Find and disable ProjectileStickOnImpact components to prevent position reset on throw
            var stickOnImpactComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
            foreach (var stickComponent in stickOnImpactComponents)
            {
                soa.behavioursToDisable.Add(stickComponent);

            }
            // Find and disable ProjectileFuse components to prevent premature detonation
            var fuseComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileFuse>(true);
            foreach (var fuseComponent in fuseComponents)
            {
                soa.behavioursToDisable.Add(fuseComponent);

            }

        }
        public static void EnsureAllGrabbableObjectsHaveSpecialObjectAttributes()
        {
            if (DrifterBossGrabPlugin.Instance)
            {
                DrifterBossGrabPlugin.Instance.StartCoroutine(EnsureAllGrabbableObjectsHaveSpecialObjectAttributesAsync());
            }
            else
            {
                // Fallback if plugin instance isn't available (unlikely)
                foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                {
                    AddSpecialObjectAttributesToGrabbableObject(go);
                }
            }
        }

        public static IEnumerator EnsureAllGrabbableObjectsHaveSpecialObjectAttributesAsync()
        {

            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            int count = 0;
            int total = allObjects.Length;
            int batchSize = 100; // Process 100 objects per frame

            foreach (GameObject go in allObjects)
            {
                if (go == null) continue;

                try
                {
                    AddSpecialObjectAttributesToGrabbableObject(go);
                }
                catch (Exception)
                {

                }

                count++;
                if (count % batchSize == 0)
                {
                    yield return null; // Wait for next frame
                }
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
