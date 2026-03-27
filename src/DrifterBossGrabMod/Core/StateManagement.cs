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
        public static void DisableMovementColliders(GameObject obj, Dictionary<Collider, bool> originalStates)
        {
            IEnumerable<Collider> colliders;

            // Use BodyColliderCache for CharacterBody objects to avoid expensive lookups
            if (obj.GetComponent<CharacterBody>() != null)
            {
                var cache = obj.GetComponent<BodyColliderCache>();
                if (cache == null)
                {
                    cache = obj.AddComponent<BodyColliderCache>();
                }
                colliders = cache.GetColliders();
            }
            else
            {
                // Fallback for non-body objects (e.g. environment)
                var modelLocator = obj.GetComponent<ModelLocator>();
                if (modelLocator && modelLocator.modelTransform)
                {
                    colliders = modelLocator.modelTransform.GetComponentsInChildren<Collider>(true);
                }
                else
                {
                    colliders = obj.GetComponentsInChildren<Collider>(true);
                }
            }

            foreach (Collider collider in colliders)
            {
                if (collider != null && collider.enabled)
                {
                    if (!originalStates.ContainsKey(collider))
                    {
                        originalStates[collider] = collider.enabled;
                    }
                    collider.enabled = false;
                }
            }
        }
        public static void RestoreMovementColliders(Dictionary<Collider, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalStates.Clear();
        }
    }
}
