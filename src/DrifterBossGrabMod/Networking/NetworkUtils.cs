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

        // logging
        public static GameObject? FindLocalObjectWithLogging(NetworkInstanceId netId, string operation, bool isServer = true)
        {
            var obj = NetworkServer.FindLocalObject(netId);

            if (obj != null)
            {
                Log.DebugIfEnabled($"[NetworkUtils.{operation}] Successfully found {obj.name} netId {netId.Value} on {(isServer ? "server" : "client")}");
                return obj;
            }

            Log.Error($"[NetworkUtils.{operation}] Failed to find object netId {netId.Value} on {(isServer ? "server" : "client")}");

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
                    Log.DebugIfEnabled($"[NetworkUtils.{operation}] Could not check server lookup: {ex.Message}");
                }

                if (isInServerLookup)
                {
                    Log.Error($"[NetworkUtils.{operation}] NetworkInstanceId exists in server lookup but FindLocalObject returned null");
                }
                else
                {
                    Log.Error($"[NetworkUtils.{operation}] NetworkInstanceId not found in server lookup");
                }
            }

            return null;
        }

        public static bool ValidateObjectReady(GameObject? obj)
        {
            if (obj == null)
            {
                Log.DebugIfEnabled("[NetworkUtils.ValidateObjectReady] GameObject is null");
                return false;
            }

            if (!obj.activeInHierarchy)
            {
                Log.DebugIfEnabled($"[NetworkUtils.ValidateObjectReady] {obj.name} is not active in hierarchy");
                return false;
            }

            var netId = obj.GetComponent<NetworkIdentity>();
            if (netId == null)
            {
                Log.DebugIfEnabled($"[NetworkUtils.ValidateObjectReady] {obj.name} does not have NetworkIdentity component");
                return false;
            }

            lock (_readyObjectCacheLock)
            {
                if (_readyObjectCache.TryGetValue(netId.netId.Value, out float cacheTime))
                {
                    if (Time.time - cacheTime < CacheValidityDuration)
                    {
                        return true;
                    }
                }
            }

            if (!netId.isActiveAndEnabled)
            {
                Log.DebugIfEnabled($"[NetworkUtils.ValidateObjectReady] {obj.name} NetworkIdentity is not active/enabled");
                return false;
            }

            if (obj == null)
            {
                Log.DebugIfEnabled($"[NetworkUtils.ValidateObjectReady] Object is being destroyed");
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

        // Comprehensive state dumps for debugging
        public static void LogObjectDetails(GameObject? obj, string context)
        {
            if (obj == null)
            {
                Log.DebugIfEnabled($"[NetworkUtils.LogObjectDetails] {context} - GameObject is null");
                return;
            }

            var netId = obj.GetComponent<NetworkIdentity>();
            Log.DebugIfEnabled($"[NetworkUtils.LogObjectDetails] {context}:");
            Log.DebugIfEnabled($"  Name: {obj.name}");
            Log.DebugIfEnabled($"  activeInHierarchy: {obj.activeInHierarchy}");
            Log.DebugIfEnabled($"  NetworkIdentity: {(netId != null ? $"netId {netId.netId.Value}" : "null")}");
            Log.DebugIfEnabled($"  NetworkIdentity.isActiveAndEnabled: {(netId != null && netId.isActiveAndEnabled)}");
            Log.DebugIfEnabled($"  InstanceID: {obj.GetInstanceID()}");
            Log.DebugIfEnabled($"  Transform.position: {obj.transform.position}");
            Log.DebugIfEnabled($"  Parent: {(obj.transform.parent != null ? obj.transform.parent.name : "null")}");
        }

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

            Log.DebugIfEnabled(logBuilder.ToString());
        }

        // Stable string IDs are required because raw numeric IDs can shift when players transition between offline and online states.
        public static string GetPlayerIdString(NetworkUserId id)
        {
            if (id.strValue != null) return id.strValue;

            return $"{id.value}_{id.subId}";
        }
    }
}

