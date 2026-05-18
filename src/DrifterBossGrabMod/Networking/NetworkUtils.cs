#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;

namespace DrifterBossGrabMod.Networking
{

    public static class NetworkUtils
    {

        private const int DefaultMaxRetries = 3;
        private const float DefaultRetryDelay = 0.1f;
        private const float MaxRetryDelay = 0.5f;

        private static readonly Dictionary<uint, float> _readyObjectCache = new Dictionary<uint, float>();
        private static readonly object _readyObjectCacheLock = new object();
        private const float CacheValidityDuration = 5f;

        public static GameObject? FindLocalObjectWithLogging(NetworkInstanceId netId, string operation, bool isServer = true)
        {
            var obj = NetworkServer.FindLocalObject(netId);

            if (obj != null)
            {
                Log.Debug($"[NetworkUtils.{operation}] Successfully found {obj.name} (netId={netId.Value}) on {(isServer ? "server" : "client")}");
                return obj;
            }

            Log.Error($"[NetworkUtils.{operation}] Failed to find object (netId={netId.Value}) on {(isServer ? "server" : "client")}");

            if (netId != NetworkInstanceId.Invalid)
            {

                bool isInServerLookup = false;
                try
                {

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

        public static bool TryEnsureNetworkIdentityActive(GameObject obj)
        {
            if (obj == null) return false;

            var netId = obj.GetComponent<NetworkIdentity>();
            if (netId == null) return false;

            if (!obj.activeInHierarchy)
            {
                obj.SetActive(true);
                if (!obj.activeInHierarchy) return false;
            }

            if (netId.isActiveAndEnabled) return true;

            try
            {
                netId.enabled = true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[NetworkUtils.TryEnsureNetworkIdentityActive] Failed to set enabled on {obj.name}: {ex.Message}");
                return false;
            }

            if (!netId.isActiveAndEnabled)
            {
                Log.Warning($"[NetworkUtils.TryEnsureNetworkIdentityActive] NetworkIdentity still not active after enable for {obj.name}");
                return false;
            }

            InvalidateReadyCache(obj);
            Log.Debug($"[NetworkUtils.TryEnsureNetworkIdentityActive] Successfully re-enabled NetworkIdentity for {obj.name}");
            return true;
        }

        public static bool ValidateObjectReadyWithRecovery(GameObject? obj)
        {
            if (ValidateObjectReady(obj)) return true;

            if (obj == null) return false;

            var netId = obj.GetComponent<NetworkIdentity>();
            if (netId != null && !netId.isActiveAndEnabled)
            {
                if (TryEnsureNetworkIdentityActive(obj))
                {
                    return ValidateObjectReady(obj);
                }
            }

            return false;
        }

        public static bool IsNetworkIdentityInactive(GameObject obj)
        {
            if (obj == null) return false;
            var netId = obj.GetComponent<NetworkIdentity>();
            return netId != null && !netId.isActiveAndEnabled;
        }

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

        public static void LogObjectDetails(GameObject? obj, string context)
        {
            if (obj == null)
            {
                Log.Warning($"[NetworkUtils.LogObjectDetails] {context} - GameObject is null");
                return;
            }

            var netId = obj.GetComponent<NetworkIdentity>();
            Log.Debug($"[NetworkUtils.LogObjectDetails] {context}:");
            Log.Debug($"  Name: {obj.name}");
            Log.Debug($"  activeInHierarchy: {obj.activeInHierarchy}");
            Log.Debug($"  NetworkIdentity: {(netId != null ? $"netId={netId.netId.Value}" : "null")}");
            Log.Debug($"  NetworkIdentity.isActiveAndEnabled: {(netId != null && netId.isActiveAndEnabled)}");
            Log.Debug($"  InstanceID: {obj.GetInstanceID()}");
            Log.Debug($"  Transform.position: {obj.transform.position}");
            Log.Debug($"  Parent: {(obj.transform.parent != null ? obj.transform.parent.name : "null")}");
        }

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

            Log.Debug(logBuilder.ToString());
        }

        public static string GetPlayerIdString(NetworkUserId id)
        {

            if (id.strValue != null) return id.strValue;

            return $"{id.value}_{id.subId}";
        }
    }
}
