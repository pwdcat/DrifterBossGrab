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
        // Cache frequently used component types to reduce reflection overhead
        private static readonly System.Type SceneReductionType = typeof(SceneReduction);
        private static readonly System.Type EntityStateMachineType = typeof(EntityStateMachine);
        private static readonly System.Type NetworkIdentityType = typeof(NetworkIdentity);
        private static readonly System.Type SpecialObjectAttributesType = typeof(SpecialObjectAttributes);
        private static readonly System.Text.RegularExpressions.Regex NumericSuffixPattern = new System.Text.RegularExpressions.Regex(@"\s*\(\d+\)$", System.Text.RegularExpressions.RegexOptions.Compiled);

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

        // ========================================================================================
        // SETUP LOGIC
        // ========================================================================================

        public static void AddSpecialObjectAttributesToGrabbableObject(GameObject obj)
        {
            if (obj == null) return;

            string objName = obj.name;
            string lowerObjName = objName.ToLowerInvariant();

            if (!PluginConfig.IsGrabbable(obj)) return;

            Log.DebugIfEnabled("[GrabbableObjectPatches] AddSpecialObjectAttributesToGrabbableObject called for {0}", objName);

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
                lowerObjName = objName.ToLowerInvariant();
            }

            var targetObj = FindEntityStateMachineTarget(obj);

            // Ensure the target object has an EntityStateMachine
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

            // Ensure the target object has NetworkIdentity
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

            if (!targetObj.TryGetComponent(out SpecialObjectAttributes soa))
            {
                soa = targetObj.AddComponent<SpecialObjectAttributes>();
                ConfigureSpecialObjectAttributes(obj, soa, objName, lowerObjName);
            }
            else
            {
                // Update existing SOA
                soa.grabbable = true;
                soa.breakoutStateMachineName = "";
                soa.orientToFloor = true;
                if (!soa.isVoid && lowerObjName.Contains("void")) soa.isVoid = true;

                // Refresh collections
                PopulateSOACollections(obj, soa);
            }
        }

        private static void ConfigureSpecialObjectAttributes(GameObject obj, SpecialObjectAttributes soa, string objName, string lowerObjName)
        {
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

            InitializeSOACollections(soa);
            PopulateSOACollections(obj, soa);
        }

        private static void InitializeSOACollections(SpecialObjectAttributes soa)
        {
            soa.renderersToDisable = new List<Renderer>(16);
            soa.behavioursToDisable = new List<MonoBehaviour>(8);
            soa.collisionToDisable = new List<GameObject>(16);
            soa.childObjectsToDisable = new List<GameObject>(4);
            soa.pickupDisplaysToDisable = new List<PickupDisplay>(2);
            soa.lightsToDisable = new List<Light>(4);
            soa.objectsToDetach = new List<GameObject>(2);
            soa.childSpecialObjectAttributes = new List<SpecialObjectAttributes>(2);
            soa.skillHighlightRenderers = new List<Renderer>(4);
            soa.soundEventsToStop = new List<AkEvent>(2);
            soa.soundEventsToPlay = new List<AkEvent>(2);
        }

        private static void PopulateSOACollections(GameObject obj, SpecialObjectAttributes soa)
        {
            // Renderers
            var renderers = obj.GetComponentsInChildren<Renderer>(false);
            foreach (var renderer in renderers)
            {
                if (!soa.renderersToDisable.Contains(renderer))
                    soa.renderersToDisable.Add(renderer);
            }

            // Colliders
            var colliders = obj.GetComponentsInChildren<Collider>(false);
            foreach (var collider in colliders)
            {
                if (!soa.collisionToDisable.Contains(collider.gameObject))
                    soa.collisionToDisable.Add(collider.gameObject);
            }

            // Lights
            var lights = obj.GetComponentsInChildren<Light>(false);
            foreach (var light in lights)
            {
                if (!soa.lightsToDisable.Contains(light))
                    soa.lightsToDisable.Add(light);
            }

            // PickupDisplays
            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                if (!soa.pickupDisplaysToDisable.Contains(pickupDisplay))
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
        // ========================================================================================
        // PROJECTILE SETUP
        // ========================================================================================

        public static void AddSpecialObjectAttributesToProjectile(GameObject obj)
        {
            if (obj == null) return;

            string objName = obj.name;
            Log.DebugIfEnabled("[GrabbableObjectPatches] AddSpecialObjectAttributesToProjectile called for {0}", objName);

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
                ConfigureSpecialObjectAttributes(obj, soa, objName, lowerObjName);
            }
            else
            {
                soa.grabbable = true;
                soa.breakoutStateMachineName = "";
                soa.orientToFloor = true;
                if (!soa.isVoid && lowerObjName.Contains("void")) soa.isVoid = true;

                PopulateSOACollections(obj, soa);
            }

            // Projectile-specific behavior disabling
            var stickOnImpactComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileStickOnImpact>(true);
            foreach (var stickComponent in stickOnImpactComponents)
            {
                if (!soa.behavioursToDisable.Contains(stickComponent))
                    soa.behavioursToDisable.Add(stickComponent);
            }

            var fuseComponents = obj.GetComponentsInChildren<RoR2.Projectile.ProjectileFuse>(true);
            foreach (var fuseComponent in fuseComponents)
            {
                if (!soa.behavioursToDisable.Contains(fuseComponent))
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
                catch (Exception ex)
                {
                    Log.Error($"[GrabPatch] Failed to add attributes to {(!go ? "null" : go.name)}: {ex.Message}");
                }

                count++;
                if (count % batchSize == 0)
                {
                    yield return null; // Wait for next frame
                }
            }

        }
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
                    totalSize += (4f / 3f) * Mathf.PI * radius * radius * radius; // Volume
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
        // ========================================================================================
        // SPECIAL OBJECT ATTRIBUTES PATCHES
        // ========================================================================================
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
        // ========================================================================================
        // ICON HELPERS
        // ========================================================================================

        public static string GetIconPathForObject(string lowerCaseName)
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
    }
}
