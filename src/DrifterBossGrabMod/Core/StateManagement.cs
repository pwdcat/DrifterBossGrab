using System;
using System.Collections.Generic;
using UnityEngine;
using RoR2;
namespace DrifterBossGrabMod
{
    public static class StateManagement
    {
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
                    if (collider != null)
                    {
                        if (!originalStates.ContainsKey(child.gameObject))
                        {
                            originalStates[child.gameObject] = child.gameObject.activeSelf;
                        }
                        child.gameObject.SetActive(false);
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
