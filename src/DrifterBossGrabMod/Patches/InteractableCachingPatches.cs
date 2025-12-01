using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrifterBossGrabMod.Patches
{
    public static class InteractableCachingPatches
    {
        private static readonly HashSet<GameObject> cachedInteractables = new HashSet<GameObject>();
        private static bool isCacheInitialized = false;
        private static bool cacheNeedsRefresh = false;
        private static readonly object cacheLock = new object();

        public static HashSet<GameObject> CachedInteractables => cachedInteractables;

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
                        if (cachedInteractables.Count >= Constants.MAX_CACHE_SIZE)
                        {
                            break; // Prevent excessive memory usage
                        }
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