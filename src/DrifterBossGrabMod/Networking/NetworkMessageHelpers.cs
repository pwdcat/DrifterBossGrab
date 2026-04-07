#nullable enable
using System;
using UnityEngine;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.Networking
{
    public static class NetworkMessageHelpers
    {
        private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(2.0);

        // Cache for DDOL objects to avoid repeated scans
        private static readonly System.Collections.Generic.Dictionary<NetworkInstanceId, GameObject> _ddolObjectCache = new();
        private static readonly System.Collections.Generic.Dictionary<NetworkInstanceId, float> _ddolCacheTimestamps = new();
        private const float DDOL_CACHE_EXPIRY = 10f;

        public static GameObject? FindObjectByNetIdWithFallback(NetworkInstanceId netId, bool onServer = false)
        {
            if (netId == NetworkInstanceId.Invalid) return null;

            // Try standard lookups first
            GameObject? foundObj = ClientScene.FindLocalObject(netId);

            if (foundObj == null && onServer)
            {
                foundObj = NetworkServer.FindLocalObject(netId);
            }

            // Try DDOL cache
            if (foundObj == null)
            {
                if (_ddolObjectCache.TryGetValue(netId, out var cachedObj) && cachedObj != null)
                {
                    // Check if cache entry is still valid
                    if (_ddolCacheTimestamps.TryGetValue(netId, out var timestamp))
                    {
                        if (Time.time - timestamp < DDOL_CACHE_EXPIRY)
                        {
                            return cachedObj;
                        }
                        // Cache expired, remove it
                        _ddolObjectCache.Remove(netId);
                        _ddolCacheTimestamps.Remove(netId);
                    }
                }

                // Cache miss or expired - scan DDOL
                var dontDestroyOnLoadScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("DontDestroyOnLoad");
                if (dontDestroyOnLoadScene.IsValid() && dontDestroyOnLoadScene.isLoaded)
                {
                    foreach (var rootObj in dontDestroyOnLoadScene.GetRootGameObjects())
                    {
                        if (rootObj != null)
                        {
                            var identity = rootObj.GetComponent<NetworkIdentity>();
                            if (identity != null && identity.netId == netId)
                            {
                                // Cache the found object
                                _ddolObjectCache[netId] = rootObj;
                                _ddolCacheTimestamps[netId] = Time.time;
                                return rootObj;
                            }
                        }
                    }
                }
            }

            return foundObj;
        }

        // Periodically cleanup expired cache entries
        public static void CleanupDdolCache()
        {
            float currentTime = Time.time;
            var expiredIds = new System.Collections.Generic.List<NetworkInstanceId>();

            foreach (var kvp in _ddolCacheTimestamps)
            {
                if (currentTime - kvp.Value >= DDOL_CACHE_EXPIRY)
                {
                    expiredIds.Add(kvp.Key);
                }
            }

            foreach (var id in expiredIds)
            {
                _ddolObjectCache.Remove(id);
                _ddolCacheTimestamps.Remove(id);
            }
        }

        public static bool ValidateArrayBounds<T>(T[] array, int index)
        {
            return array != null && index >= 0 && index < array.Length;
        }

        public static bool ValidateArrayBounds<T>(T[] array, int startIndex, int count)
        {
            return array != null && startIndex >= 0 && count >= 0 && startIndex + count <= array.Length;
        }

        public static void ClearAndEnsureCapacity<T>(ref T[] array, int capacity)
        {
            if (array == null || array.Length < capacity)
            {
                array = new T[capacity];
            }
            else
            {
                Array.Clear(array, 0, capacity);
            }
        }
    }
}
