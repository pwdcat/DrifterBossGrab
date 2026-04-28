#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;

namespace DrifterBossGrabMod.Networking
{
    // Provides reliable object lookups across the network to mitigate the risk of NullReferenceExceptions during high-latency syncs.
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

        // Retry logic accounts for the non-deterministic timing of Unity's object spawning and registration.


        // Detailed logging is essential for diagnosing synchronization failures that only occur in multi-player environments.
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

        // Validation ensures that we don't attempt operations on objects that are partially initialized or already marked for destruction.
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

        // Cache invalidation prevents "ghost" references when an object ID is recycled by the engine.
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

        // Comprehensive state dumps are the primary tool for debugging complex race conditions in the vehicle system.
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

        // Reference validation is required because Unity's implicit null checks don't always detect destroyed C# objects.


        // Safe naming prevents diagnostic logs from crashing if the target object has already been garbage collected.
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

        // Contextual logging allows us to trace the flow of network messages between the server and specific client instances.
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

        // Periodic cleanup prevents memory pressure and lookup slowdowns in extremely long runs.

        // Stable string IDs are required because raw numeric IDs can shift when players transition between offline and online states.
        public static string GetPlayerIdString(NetworkUserId id)
        {
            // Prefer the string value if it exists (usually for specialized platforms)
            if (id.strValue != null) return id.strValue;

            // Fallback to value_subId format which is stable and unique for Steam/Local users
            return $"{id.value}_{id.subId}";
        }
    }
}
