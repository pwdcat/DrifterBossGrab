using System;
using System.Collections.Generic;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod
{
    public static class StateManagement
    {
        // Cached config values for performance
        internal static bool cachedDebugLogsEnabled;

        public static void Initialize(bool debugLogsEnabled)
        {
            cachedDebugLogsEnabled = debugLogsEnabled;
        }

        public static void UpdateDebugLogging(bool debugLogsEnabled)
        {
            cachedDebugLogsEnabled = debugLogsEnabled;
        }

        public static void DisableMovementColliders(GameObject obj, Dictionary<GameObject, bool> originalStates)
        {
            var modelLocator = obj.GetComponent<ModelLocator>();
            if (modelLocator && modelLocator.modelTransform)
            {
                foreach (Transform child in modelLocator.modelTransform.GetComponentsInChildren<Transform>(true))
                {
                    var collider = child.GetComponent<Collider>();
                    int layer = child.gameObject.layer;
                    string layerName = LayerMask.LayerToName(layer);
                    if (cachedDebugLogsEnabled)
                    {
                        Log.Info($"{Constants.LogPrefix} Checking {child.name}, layer: {layer} ({layerName}), hasCollider: {collider != null}");
                    }
                    if (collider != null)
                    {
                        if (!originalStates.ContainsKey(child.gameObject))
                        {
                            originalStates[child.gameObject] = child.gameObject.activeSelf;
                        }
                        child.gameObject.SetActive(false);
                        if (cachedDebugLogsEnabled)
                        {
                            Log.Info($"{Constants.LogPrefix} Disabled {child.name} due to collider on layer {layerName}");
                        }
                    }
                }
            }
        }
        public static void RestoreMovementColliders(Dictionary<GameObject, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetActive(kvp.Value);
                }
            }
            originalStates.Clear();
        }
    }
}