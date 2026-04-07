#nullable enable
using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod
{
    // cache colliders for characterbody to avoid expensive GetComponentsInChildren calls
    // added to objects when first grabbed by drifter
    public class BodyColliderCache : MonoBehaviour
    {
        private Collider[]? _colliders;
        private bool _isInitialized = false;

        public Collider[] GetColliders()
        {
            if (!_isInitialized)
            {
                PopulateCache();
            }
            return _colliders ?? Array.Empty<Collider>();
        }

        private void PopulateCache()
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BodyColliderCache] Populating collider cache for {gameObject.name}");
            }

            var modelLocator = GetComponent<ModelLocator>();
            if (modelLocator != null && modelLocator.modelTransform != null)
            {
                // Capture all colliders on the model, including inactive ones (e.g., higher LODs)
                _colliders = modelLocator.modelTransform.GetComponentsInChildren<Collider>(true);
            }
            else
            {
                // Fallback to object's own colliders if no model locator exists
                _colliders = GetComponentsInChildren<Collider>(true);
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BodyColliderCache] Found {_colliders?.Length ?? 0} colliders for {gameObject.name}");
            }

            _isInitialized = true;
        }

        // force refresh of cache if model transform changes
        public void RefreshCache()
        {
            _isInitialized = false;
        }

        public Dictionary<Collider, bool> OriginalStates { get; } = new Dictionary<Collider, bool>();

        // disable all movement colliders on an object and record their previous state
        public static void DisableMovementColliders(GameObject obj, System.Collections.Generic.Dictionary<Collider, bool> originalStates)
        {
            System.Collections.Generic.IEnumerable<Collider> colliders;

            var cache = obj.GetComponent<BodyColliderCache>();
            if (cache == null)
            {
                cache = obj.AddComponent<BodyColliderCache>();
            }
            colliders = cache.GetColliders();

            if (cache.OriginalStates.Count > 0 && originalStates.Count == 0)
            {
                foreach (var kvp in cache.OriginalStates)
                {
                    originalStates[kvp.Key] = kvp.Value;
                }
            }

            foreach (Collider collider in colliders)
            {
                if (collider != null && collider.enabled)
                {
                    if (!originalStates.ContainsKey(collider))
                        originalStates[collider] = collider.enabled;
                    
                    if (!cache.OriginalStates.ContainsKey(collider))
                        cache.OriginalStates[collider] = collider.enabled;

                    collider.enabled = false;
                }
            }
        }

        // restore colliders to their previously recorded states
        public static void RestoreMovementColliders(System.Collections.Generic.Dictionary<Collider, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                    
                    // Also clear from the cache if possible
                    var cache = kvp.Key.GetComponentInParent<BodyColliderCache>();
                    if (cache != null)
                    {
                        cache.OriginalStates.Remove(kvp.Key);
                    }
                }
            }
            originalStates.Clear();
        }
    }
}
