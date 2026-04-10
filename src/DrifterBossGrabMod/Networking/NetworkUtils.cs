#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;

namespace DrifterBossGrabMod.Networking
{
    // Helper utilities for safe network operations with retry logic and comprehensive logging.
    public static class NetworkUtils
    {
        // Configuration
        private const int DefaultMaxRetries = 3;
        private const float DefaultRetryDelay = 0.1f;
        private const float MaxRetryDelay = 0.5f;

        // Cache for objects that have been verified as "ready"
        private static readonly Dictionary<uint, float> _readyObjectCache = new Dictionary<uint, float>();
        private static readonly object _readyObjectCacheLock = new object();
        private const float CacheValidityDuration = 5f;

        // Safely finds a local object by NetworkInstanceId with retry logic.
        public static GameObject? FindLocalObjectSafe(NetworkInstanceId netId, int maxRetries = DefaultMaxRetries, float retryDelay = DefaultRetryDelay, string? context = null)
        {
            if (netId == NetworkInstanceId.Invalid)
            {
                Log.Warning($"[NetworkUtils.FindLocalObjectSafe] Invalid NetworkInstanceId - Context: {context ?? "unknown"}");
                return null;
            }

            string objIdentifier = $"netId={netId.Value}";
            if (context != null)
            {
                objIdentifier = $"{objIdentifier} (context: {context})";
            }

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                var obj = NetworkServer.FindLocalObject(netId);

                if (obj != null)
                {
                    // Object found - validate it's ready
                    if (ValidateObjectReady(obj))
                    {
                        Log.Info($"[NetworkUtils.FindLocalObjectSafe] Found {obj.name} on attempt {attempt + 1}/{maxRetries} - {objIdentifier}");
                        return obj;
                    }
                    else
                    {
                        Log.Warning($"[NetworkUtils.FindLocalObjectSafe] Found {obj.name} but it's not ready (attempt {attempt + 1}/{maxRetries}) - {objIdentifier}");
                    }
                }
                else
                {
                    Log.Warning($"[NetworkUtils.FindLocalObjectSafe] Object not found on attempt {attempt + 1}/{maxRetries} - {objIdentifier}");
                }

                // Don't wait on the last attempt
                if (attempt < maxRetries - 1)
                {
                    // Exponential backoff with cap
                    float delay = Math.Min(retryDelay * (float)Math.Pow(1.5, attempt), MaxRetryDelay);
                    Log.Warning($"[NetworkUtils.FindLocalObjectSafe] Retrying in {delay:F3}s... - {objIdentifier}");
                }
            }

            Log.Error($"[NetworkUtils.FindLocalObjectSafe] Failed to find object after {maxRetries} attempts - {objIdentifier}");
            return null;
        }

        // Finds a local object by NetworkInstanceId with detailed logging on failure.
        public static GameObject? FindLocalObjectWithLogging(NetworkInstanceId netId, string operation, bool isServer = true)
        {
            var obj = NetworkServer.FindLocalObject(netId);

            if (obj != null)
            {
                Log.Info($"[NetworkUtils.{operation}] Successfully found {obj.name} (netId={netId.Value}) on {(isServer ? "server" : "client")}");
                return obj;
            }

            // Log detailed failure information
            Log.Error($"[NetworkUtils.{operation}] Failed to find object (netId={netId.Value}) on {(isServer ? "server" : "client")}");

            // Try to provide more context about what might be wrong
            if (netId != NetworkInstanceId.Invalid)
            {
                // Check if the ID is in the server's lookup
                bool isInServerLookup = false;
                try
                {
                    // This is a bit hacky but helps with debugging
                    var serverLookupField = typeof(NetworkServer).GetField("s_Spawned", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (serverLookupField != null)
                    {
                        var lookupDict = serverLookupField.GetValue(null) as Dictionary<NetworkInstanceId, NetworkIdentity>;
                        isInServerLookup = lookupDict != null && lookupDict.ContainsKey(netId);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[NetworkUtils.{operation}] Could not check server lookup: {ex.Message}");
                }

                if (isInServerLookup)
                {
                    Log.Error($"[NetworkUtils.{operation}] NetworkInstanceId exists in server lookup but FindLocalObject returned null - object may be destroyed/inactive");
                }
                else
                {
                    Log.Error($"[NetworkUtils.{operation}] NetworkInstanceId not found in server lookup - object may not be spawned yet or was destroyed");
                }
            }

            return null;
        }

        // Validates that an object is ready for network operations.
        public static bool ValidateObjectReady(GameObject? obj)
        {
            if (obj == null)
            {
                Log.Warning("[NetworkUtils.ValidateObjectReady] GameObject is null");
                return false;
            }

            if (!obj.activeInHierarchy)
            {
                Log.Warning($"[NetworkUtils.ValidateObjectReady] {obj.name} is not active in hierarchy");
                return false;
            }

            var netId = obj.GetComponent<NetworkIdentity>();
            if (netId == null)
            {
                Log.Warning($"[NetworkUtils.ValidateObjectReady] {obj.name} does not have NetworkIdentity component");
                return false;
            }

            lock (_readyObjectCacheLock)
            {
                if (_readyObjectCache.TryGetValue(netId.netId.Value, out float cacheTime))
                {
                    if (Time.time - cacheTime < CacheValidityDuration)
                    {
                        // Object was validated recently and is still valid
                        return true;
                    }
                }
            }

            if (!netId.isActiveAndEnabled)
            {
                Log.Warning($"[NetworkUtils.ValidateObjectReady] {obj.name} NetworkIdentity is not active/enabled");
                return false;
            }

            if (obj == null)
            {
                Log.Warning($"[NetworkUtils.ValidateObjectReady] Object is being destroyed");
                return false;
            }

            lock (_readyObjectCacheLock)
            {
                _readyObjectCache[netId.netId.Value] = Time.time;
            }

            return true;
        }

        // Clears the ready object cache for a specific object or all objects.
        public static void InvalidateReadyCache(GameObject? obj)
        {
            if (obj == null) return;

            var netId = obj.GetComponent<NetworkIdentity>();
            if (netId != null)
            {
                lock (_readyObjectCacheLock)
                {
                    _readyObjectCache.Remove(netId.netId.Value);
                }
            }
        }

        // Clears the entire ready object cache.
        public static void ClearReadyCache()
        {
            lock (_readyObjectCacheLock)
            {
                _readyObjectCache.Clear();
            }
            Log.Info("[NetworkUtils.ClearReadyCache] Cleared all object ready cache entries");
        }

        // Gets a NetworkIdentity component safely with null checking.
        public static NetworkIdentity? GetNetworkIdentitySafe(GameObject? obj)
        {
            if (obj == null)
            {
                Log.Warning("[NetworkUtils.GetNetworkIdentitySafe] GameObject is null");
                return null;
            }

            var netId = obj.GetComponent<NetworkIdentity>();
            if (netId == null)
            {
                Log.Warning($"[NetworkUtils.GetNetworkIdentitySafe] {obj.name} does not have NetworkIdentity component");
            }

            return netId;
        }

        // Logs detailed information about a GameObject for debugging.
        public static void LogObjectDetails(GameObject? obj, string context)
        {
            if (obj == null)
            {
                Log.Warning($"[NetworkUtils.LogObjectDetails] {context} - GameObject is null");
                return;
            }

            var netId = obj.GetComponent<NetworkIdentity>();
            Log.Info($"[NetworkUtils.LogObjectDetails] {context}:");
            Log.Info($"  Name: {obj.name}");
            Log.Info($"  activeInHierarchy: {obj.activeInHierarchy}");
            Log.Info($"  NetworkIdentity: {(netId != null ? $"netId={netId.netId.Value}" : "null")}");
            Log.Info($"  NetworkIdentity.isActiveAndEnabled: {(netId != null && netId.isActiveAndEnabled)}");
            Log.Info($"  InstanceID: {obj.GetInstanceID()}");
            Log.Info($"  Transform.position: {obj.transform.position}");
            Log.Info($"  Parent: {(obj.transform.parent != null ? obj.transform.parent.name : "null")}");
        }

        // Checks if a GameObject reference is valid
        public static bool IsValidGameObjectReference(GameObject? obj)
        {
            if (obj == null) return false;

            try
            {
                // This will throw if the object is destroyed
                if (!obj) return false;

                // Check if it has NetworkIdentity (required for network sync)
                var netId = obj.GetComponent<NetworkIdentity>();
                return netId != null;
            }
            catch
            {
                return false;
            }
        }

        // Gets a safe object name for logging (handles null/destroyed objects).
        public static string GetSafeObjectName(GameObject? obj)
        {
            if (obj == null) return "null";
            try
            {
                return !obj ? "destroyed" : obj.name;
            }
            catch
            {
                return "error";
            }
        }

        // Logs a network operation with comprehensive context.
        public static void LogNetworkOperation(string operation, GameObject? obj, bool isServer, Dictionary<string, object>? additionalContext = null)
        {
            var logBuilder = new System.Text.StringBuilder();
            logBuilder.Append($"[NetworkUtils.{operation}] {(isServer ? "SERVER" : "CLIENT")}");

            if (obj != null)
            {
                var netId = obj.GetComponent<NetworkIdentity>();
                logBuilder.Append($" | Object: {GetSafeObjectName(obj)}");
                logBuilder.Append($" | netId: {(netId != null ? netId.netId.Value.ToString() : "none")}");
                logBuilder.Append($" | activeInHierarchy: {obj.activeInHierarchy}");
            }

            if (additionalContext != null)
            {
                foreach (var kvp in additionalContext)
                {
                    logBuilder.Append($" | {kvp.Key}: {kvp.Value}");
                }
            }

            Log.Info(logBuilder.ToString());
        }

        // Cleans up stale cache entries
        public static void CleanupStaleCacheEntries()
        {
            lock (_readyObjectCacheLock)
            {
                var keysToRemove = new List<uint>();

                foreach (var kvp in _readyObjectCache)
                {
                    if (Time.time - kvp.Value > CacheValidityDuration)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _readyObjectCache.Remove(key);
                }

                if (keysToRemove.Count > 0)
                {
                    Log.Info($"[NetworkUtils.CleanupStaleCacheEntries] Cleaned up {keysToRemove.Count} stale cache entries");
                }
            }
        }
    }
}
