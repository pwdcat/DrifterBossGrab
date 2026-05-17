#nullable enable
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

    // ========================================================================================
    // GRABBABLE OBJECT PATCHES
    // ========================================================================================
    public static class GrabbableObjectPatches
    {

        private static readonly System.Type SceneReductionType = typeof(SceneReduction);
        private static readonly System.Type EntityStateMachineType = typeof(EntityStateMachine);
        private static readonly System.Type NetworkIdentityType = typeof(NetworkIdentity);
        private static readonly System.Type SpecialObjectAttributesType = typeof(SpecialObjectAttributes);
        private static readonly System.Text.RegularExpressions.Regex NumericSuffixPattern = new System.Text.RegularExpressions.Regex(@"\s*\(\d+\)$", System.Text.RegularExpressions.RegexOptions.Compiled);

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

            if (obj.name.Contains("_LOD"))
            {
                Transform lodParent = obj.transform.parent;
                while (lodParent != null)
                {

                    if (lodParent.gameObject.TryGetComponent(out SceneReduction _))
                    {
                        return lodParent.gameObject;
                    }
                    lodParent = lodParent.parent;
                }

            }

            if (obj.TryGetComponent(out EntityStateMachine _))
            {
                return obj;
            }

            Transform current = obj.transform.parent;
            while (current != null)
            {

                if (current.gameObject.TryGetComponent(out EntityStateMachine _) && PluginConfig.IsGrabbable(current.gameObject))
                {
                    return current.gameObject;
                }

                current = current.parent;
            }

            return PluginConfig.IsGrabbable(obj.transform.root.gameObject) ? obj.transform.root.gameObject : obj;
        }

        // ========================================================================================
        // SETUP LOGIC
        // ========================================================================================
        public static void AddSpecialObjectAttributesToGrabbableObject(GameObject obj)
        {
            if (obj == null)
                return;

            string objName = obj.name;

            string lowerObjName = objName.ToLowerInvariant();

            if (!PluginConfig.IsGrabbable(obj))
                return;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[GrabbableObjectPatches] AddSpecialObjectAttributesToGrabbableObject called for {objName}");
            }

            if (lowerObjName.Contains("survivorpod"))
            {
                if (obj.TryGetComponent(out EntityStateMachine podEsm) && podEsm.state is EntityStates.SurvivorPod.Descent)
                {
                    DrifterBossGrabPlugin.Instance?.StartCoroutine(DelayedSurvivorPodSetup(obj));
                    return;
                }
            }

            if (string.IsNullOrEmpty(objName))
            {
                objName = obj.name = "GrabbableObject_" + obj.GetInstanceID();
                lowerObjName = objName.ToLowerInvariant();
            }

            var targetObj = FindEntityStateMachineTarget(obj);

            if (!targetObj.TryGetComponent(out EntityStateMachine esm))
            {
                esm = targetObj.AddComponent<EntityStateMachine>();
                esm.customName = "Body";
                esm.initialStateType = new SerializableEntityStateType(typeof(EntityStates.Uninitialized));
                esm.mainStateType = new SerializableEntityStateType(typeof(EntityStates.Uninitialized));
                esm.networkIndex = -1;
                esm.AllowStartWithoutNetworker = true;

                if (esm.state is EntityStates.Uninitialized)
                {
                    esm.SetState(EntityStateCatalog.InstantiateState(ref esm.initialStateType));
                }
            }

            if (!targetObj.TryGetComponent(out NetworkIdentity networkIdentity))
            {
                networkIdentity = targetObj.AddComponent<NetworkIdentity>();
                networkIdentity.serverOnly = false;
                networkIdentity.localPlayerAuthority = false;

                try
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.Spawn(targetObj);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[GrabPatch] Failed to spawn object {objName} on network: {ex.Message}");
                }
            }

            if (targetObj.TryGetComponent(out SpecialObjectAttributes existingSoa))
            {

                bool shouldBeGrabbable = PluginConfig.IsGrabbable(obj);
                if (shouldBeGrabbable)
                {

                    if (!existingSoa.grabbable || string.IsNullOrEmpty(existingSoa.breakoutStateMachineName))
                    {
                        existingSoa.grabbable = true;
                        existingSoa.breakoutStateMachineName = "";
                        existingSoa.orientToFloor = true;
                    }

                    if (!existingSoa.isVoid && lowerObjName.Contains("void"))
                    {
                        existingSoa.isVoid = true;
                    }

                    var existingLights = obj.GetComponentsInChildren<Light>(false);
                    foreach (var light in existingLights)
                    {
                        if (!existingSoa.lightsToDisable.Contains(light))
                        {
                            existingSoa.lightsToDisable.Add(light);
                        }
                    }

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

                    existingSoa.grabbable = false;
                }
                return;
            }

            var soa = targetObj.AddComponent<SpecialObjectAttributes>();

            var (scaledMass, scaledDurability) = CalculateScaledAttributes(obj, objName);

            soa.grabbable = true;
            soa.massOverride = scaledMass;
            soa.maxDurability = scaledDurability;
            soa.durability = scaledDurability;
            soa.hullClassification = HullClassification.Human;
            soa.breakoutStateMachineName = "";
            soa.orientToFloor = true;

            string displayName = objName.Replace("(Clone)", "");

            displayName = NumericSuffixPattern.Replace(displayName, "");
            soa.bestName = displayName;

            soa.isVoid = lowerObjName.Contains("void");

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

            var lights = ComponentPool.RentLightList();
            obj.GetComponentsInChildren(false, lights);
            foreach (var light in lights)
            {
                soa.lightsToDisable.Add(light);
            }

            ComponentPool.ReturnLightList(lights);

            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                soa.pickupDisplaysToDisable.Add(pickupDisplay);
            }

        }
        private static IEnumerator DelayedSurvivorPodSetup(GameObject survivorPod)
        {

            yield return new WaitForSeconds(5f);

            if (survivorPod != null && survivorPod.TryGetComponent(out EntityStateMachine esm))
            {

                if (esm.state is EntityStates.SurvivorPod.Landed ||
                    esm.state is EntityStates.SurvivorPod.PreRelease ||
                    esm.state is EntityStates.SurvivorPod.Release ||
                    esm.state is EntityStates.SurvivorPod.ReleaseFinished)
                {

                    AddSpecialObjectAttributesToGrabbableObject(survivorPod);
                }
            }
        }

        // ========================================================================================
        // PROJECTILE SETUP
        // ========================================================================================
        public static void AddSpecialObjectAttributesToProjectile(GameObject obj)
        {
            if (obj == null)
                return;

            string objName = obj.name;
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[GrabbableObjectPatches] AddSpecialObjectAttributesToProjectile called for {objName}");
            }

            string lowerObjName = objName.ToLowerInvariant();

            if (string.IsNullOrEmpty(objName))
            {
                objName = obj.name = "Projectile_" + obj.GetInstanceID();
                lowerObjName = objName.ToLowerInvariant();
            }

            var targetObj = obj;

            if (!targetObj.TryGetComponent(out NetworkIdentity networkIdentity))
            {
                networkIdentity = targetObj.AddComponent<NetworkIdentity>();
                networkIdentity.serverOnly = false;
                networkIdentity.localPlayerAuthority = false;

                try
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.Spawn(targetObj);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[GrabPatch] Failed to spawn projectile {objName} on network: {ex.Message}");
                }
            }

            if (!targetObj.TryGetComponent(out SpecialObjectAttributes soa))
            {

                soa = targetObj.AddComponent<SpecialObjectAttributes>();

                var (scaledMass, scaledDurability) = CalculateScaledAttributes(obj, objName);

                soa.grabbable = true;
                soa.massOverride = scaledMass;
                soa.maxDurability = scaledDurability;
                soa.durability = scaledDurability;
                soa.hullClassification = HullClassification.Human;
                soa.breakoutStateMachineName = "";
                soa.orientToFloor = true;

                string displayName = objName.Replace("(Clone)", "");

                displayName = NumericSuffixPattern.Replace(displayName, "");
                soa.bestName = displayName;

                soa.isVoid = lowerObjName.Contains("void");
            }
            else
            {

                if (!soa.grabbable)
                {
                    soa.grabbable = true;
                }

                if (string.IsNullOrEmpty(soa.breakoutStateMachineName))
                {
                    soa.breakoutStateMachineName = "";
                }

                soa.orientToFloor = true;
            }

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

            var lights = ComponentPool.RentLightList();
            obj.GetComponentsInChildren(false, lights);
            foreach (var light in lights)
            {
                soa.lightsToDisable.Add(light);
            }

            ComponentPool.ReturnLightList(lights);

            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                soa.pickupDisplaysToDisable.Add(pickupDisplay);
            }

            var stickOnImpactComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
            foreach (var stickComponent in stickOnImpactComponents)
            {
                soa.behavioursToDisable.Add(stickComponent);

            }

            var fuseComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileFuse>(true);
            foreach (var fuseComponent in fuseComponents)
            {
                soa.behavioursToDisable.Add(fuseComponent);

            }

        }

        // ========================================================================================
        // ENUMERATION HELPERS
        // ========================================================================================
        public static void EnsureAllGrabbableObjectsHaveSpecialObjectAttributes()
        {
            if (DrifterBossGrabPlugin.Instance)
            {
                DrifterBossGrabPlugin.Instance!.StartCoroutine(EnsureAllGrabbableObjectsHaveSpecialObjectAttributesAsync());
            }
            else
            {

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
            int batchSize = 100;

            foreach (GameObject go in allObjects)
            {
                if (go == null) continue;

                try
                {
                    AddSpecialObjectAttributesToGrabbableObject(go);
                }
                catch (Exception ex)
                {
                    Log.Error($"[GrabPatch] Failed to add attributes to {(!go ? "null" : go.name)}: {ex.Message}");
                }

                count++;
                if (count % batchSize == 0)
                {
                    yield return null;
                }
            }

        }
        #region Harmony Patches

        // ========================================================================================
        // HARMONY PATCHES
        // ========================================================================================
        [HarmonyPatch(typeof(DirectorCore), "TrySpawnObject")]
        public class DirectorCore_TrySpawnObject_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(GameObject __result)
            {
                if (__result)
                {

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
                    totalSize += size.x * size.y * size.z;
                }
                else if (collider is SphereCollider sphere)
                {
                    float radius = sphere.radius;
                    totalSize += (4f / 3f) * Mathf.PI * radius * radius * radius;
                }
                else if (collider is CapsuleCollider capsule)
                {
                    float radius = capsule.radius;
                    float height = capsule.height;

                    totalSize += Mathf.PI * radius * radius * height;
                }
                else if (collider is MeshCollider mesh)
                {

                    var bounds = mesh.bounds;
                    totalSize += bounds.size.x * bounds.size.y * bounds.size.z;
                }
            }

            totalSize = Mathf.Max(totalSize, 0.1f);
            return totalSize;
        }
        private static (float massOverride, int maxDurability) CalculateScaledAttributes(GameObject obj, string objName)
        {
            float sizeMetric = CalculateObjectSizeMetric(obj);
            const float referenceSize = 10f;
            const float baseMass = 100f;
            const int baseDurability = 8;

            float scaleFactor = Mathf.Clamp(sizeMetric / referenceSize, 0.5f, 5f);

            float scaledMass = baseMass * scaleFactor;
            int scaledDurability = Mathf.RoundToInt(baseDurability * scaleFactor);

            scaledMass = Mathf.Max(scaledMass, 25f);
            scaledDurability = Mathf.Max(scaledDurability, 3);

            return (scaledMass, scaledDurability);
        }
        #endregion
        #region SpecialObjectAttributes Patches

        // ========================================================================================
        // SPECIAL OBJECT ATTRIBUTES PATCHES
        // ========================================================================================
        [HarmonyPatch(typeof(SpecialObjectAttributes), "Start")]
        public class SpecialObjectAttributes_Start_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance)
            {

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

        // ========================================================================================
        // ICON HELPERS
        // ========================================================================================
        public static string GetIconPathForObject(string lowerCaseName)
        {

            if (lowerCaseName.Contains("lunar") || lowerCaseName.Contains("newt") || lowerCaseName.Contains("portalshop") || lowerCaseName.Contains("portalms"))
            {
                return "RoR2/Base/LunarIcon_1.png";
            }

            if (lowerCaseName.Contains("void"))
            {
                return "RoR2/Base/VoidIcon_2.png";
            }

            if (lowerCaseName.Contains("halcyonite") || lowerCaseName.Contains("colossus"))
            {
                return "RoR2/Base/texColossusExpansionIcon2White.png";
            }

            if (lowerCaseName.Contains("portalgoldshores"))
            {
                return "RoR2/Base/TitanGoldDuringTP/texGoldHeartIcon.png";
            }

            if (lowerCaseName.Contains("teleporter") || lowerCaseName.Contains("portal"))
            {
                return "RoR2/Base/Common/MiscIcons/texTeleporterIconOutlined.png";
            }

            if (lowerCaseName.Contains("shrine") || lowerCaseName.Contains("statue"))
            {
                return "RoR2/Base/ShrineIcon.png";
            }

            if (lowerCaseName.Contains("pillar"))
            {
                return "RoR2/Base/PillarIcon.png";
            }

            if (lowerCaseName.Contains("vending"))
            {
                return "RoR2/DLC1/VendingMachine/texVendingMachineBody.png";
            }

            if (lowerCaseName.Contains("pot"))
            {
                return "RoR2/Base/ExplosivePotDestructible/texExplosivePotDestructibleBody.png";
            }

            if (lowerCaseName.Contains("ship") || lowerCaseName.Contains("survivor"))
            {
                return "RoR2/Base/Common/MiscIcons/texRescueshipIcon.png";
            }

            if (lowerCaseName.Contains("rock") || lowerCaseName.Contains("chunk") || lowerCaseName.Contains("boulder"))
            {
                return "RoR2/Base/skymeadow/texSMMaulingRock.png";
            }

            return "RoR2/Base/Common/MiscIcons/texMysteryIcon.png";
        }
        [HarmonyPatch(typeof(EntityStates.CaptainSupplyDrop.BaseCaptainSupplyDropState), "OnEnter")]
        public class BaseCaptainSupplyDropState_OnEnter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(EntityStates.CaptainSupplyDrop.BaseCaptainSupplyDropState __instance)
            {

                AddSpecialObjectAttributesToGrabbableObject(__instance.outer.gameObject);
            }
        }
        #endregion
    }
}
