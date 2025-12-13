using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Linq;

namespace DrifterBossGrabMod.Patches
{
    public static class InteractableCachingPatches
    {
        private static readonly HashSet<GameObject> cachedInteractables = new HashSet<GameObject>();
        private static bool isCacheInitialized = false;
        private static bool cacheNeedsRefresh = false;
        private static readonly object cacheLock = new object();

        public static HashSet<GameObject> CachedInteractables
        {
            get
            {
                lock (cacheLock)
                {
                    return new HashSet<GameObject>(cachedInteractables);
                }
            }
        }

        public static void MarkCacheForRefresh()
        {
            cacheNeedsRefresh = true;
        }

        public static bool CacheNeedsRefresh => cacheNeedsRefresh;

        public static void RefreshCache()
        {
            lock (cacheLock)
            {
                cachedInteractables.Clear();
                foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (mb is IInteractable)
                    {
                        cachedInteractables.Add(mb.gameObject);
                    }
                }
                isCacheInitialized = true;
                cacheNeedsRefresh = false;

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Refreshed interactable cache: {cachedInteractables.Count} objects cached");
                }
            }
        }

        public static void ClearCache()
        {
            lock (cacheLock)
            {
                cachedInteractables.Clear();
                isCacheInitialized = false;
                cacheNeedsRefresh = false;
            }
        }

        public static void AddToCache(GameObject gameObject)
        {
            if (gameObject && gameObject.GetComponent<IInteractable>() != null)
            {
                lock (cacheLock)
                {
                    cachedInteractables.Add(gameObject);
                }
            }
        }

        public static void RemoveFromCache(GameObject gameObject)
        {
            lock (cacheLock)
            {
                cachedInteractables.Remove(gameObject);
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Removed destroyed interactable {gameObject.name}");
                }
            }
        }

        public static bool IsCacheInitialized => isCacheInitialized;

        /// Adds SpecialObjectAttributes to interactable objects that don't have them,
        /// making them grabbable like chests using the native game mechanic.
        public static void AddSpecialObjectAttributesToInteractable(GameObject obj)
        {
            if (obj == null || obj.GetComponent<CharacterBody>() != null || obj.GetComponent<HurtBox>() != null)
                return;

            var interactable = obj.GetComponent<IInteractable>();
            if (interactable == null) return;

            // Check if already has SpecialObjectAttributes
            var existingSoa = obj.GetComponent<SpecialObjectAttributes>();
            if (existingSoa != null)
            {
                // If it already has SpecialObjectAttributes, ensure it's configured for grabbing
                if (!existingSoa.grabbable || string.IsNullOrEmpty(existingSoa.breakoutStateMachineName))
                {
                    existingSoa.grabbable = true;
                    existingSoa.breakoutStateMachineName = ""; // Required for BaggedObject to attach the object
                    existingSoa.orientToFloor = true; // Like chests

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Updated existing SpecialObjectAttributes on {obj.name} for grabbing");
                    }
                }
                return;
            }

            // Add SpecialObjectAttributes to make it grabbable like chests
            var soa = obj.AddComponent<SpecialObjectAttributes>();

            // Configure for grabbing (similar to chests)
            soa.grabbable = true;
            soa.massOverride = 100f; // Default mass for environment objects
            soa.maxDurability = 8; // Same as chests
            soa.durability = 8;
            soa.hullClassification = HullClassification.Human;
            soa.breakoutStateMachineName = ""; // Required for BaggedObject to attach the object
            soa.orientToFloor = true; // Like chests

            // Set up basic state management collections
            soa.renderersToDisable = new System.Collections.Generic.List<Renderer>();
            soa.behavioursToDisable = new System.Collections.Generic.List<MonoBehaviour>();
            soa.collisionToDisable = new System.Collections.Generic.List<GameObject>();
            soa.childObjectsToDisable = new System.Collections.Generic.List<GameObject>();
            soa.pickupDisplaysToDisable = new System.Collections.Generic.List<PickupDisplay>();
            soa.lightsToDisable = new System.Collections.Generic.List<Light>();
            soa.objectsToDetach = new System.Collections.Generic.List<GameObject>();
            soa.childSpecialObjectAttributes = new System.Collections.Generic.List<SpecialObjectAttributes>();
            soa.skillHighlightRenderers = new System.Collections.Generic.List<Renderer>();
            soa.soundEventsToStop = new System.Collections.Generic.List<AkEvent>();
            soa.soundEventsToPlay = new System.Collections.Generic.List<AkEvent>();

            // Find and configure components
            var renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                soa.renderersToDisable.Add(renderer);
            }

            var colliders = obj.GetComponentsInChildren<Collider>();
            foreach (var collider in colliders)
            {
                soa.collisionToDisable.Add(collider.gameObject);
            }

            // Disable common interactable behaviors during grab
            var behaviors = obj.GetComponents<MonoBehaviour>();
            foreach (var behavior in behaviors)
            {
                if (behavior is IInteractable || behavior is Highlight || behavior is PurchaseInteraction)
                {
                    soa.behavioursToDisable.Add(behavior);
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Added SpecialObjectAttributes to interactable: {obj.name}");
            }
        }

        /// Scans all current interactables and adds SpecialObjectAttributes if missing
        public static void EnsureAllInteractablesHaveSpecialObjectAttributes()
        {
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is IInteractable)
                {
                    AddSpecialObjectAttributesToInteractable(mb.gameObject);
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
                if (__result && __result.GetComponent<IInteractable>() != null)
                {
                    AddToCache(__result);
                    // Make sure it has SpecialObjectAttributes for grabbing
                    AddSpecialObjectAttributesToInteractable(__result);
                }
            }
        }

        [HarmonyPatch(typeof(Object), "Destroy", typeof(Object))]
        public class Object_Destroy_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Object obj)
            {
                try
                {
                    if (obj is GameObject go && go.GetComponent<IInteractable>() != null)
                    {
                        RemoveFromCache(go);
                    }
                }
                catch (System.Exception)
                {
                    // Ignore exceptions during object destruction
                }
            }
        }

        #endregion
    }
}