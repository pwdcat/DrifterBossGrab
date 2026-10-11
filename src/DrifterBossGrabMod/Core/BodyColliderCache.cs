#nullable enable
using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod
{

    public class BodyColliderCache : MonoBehaviour
    {
        private Collider[]? _colliders;
        private bool _isInitialized = false;
        private readonly Dictionary<Renderer, bool> _renderingStates = new Dictionary<Renderer, bool>();

        public void SetModelVisibility(bool isVisible)
        {
            if (isVisible)
            {
                foreach (var state in _renderingStates)
                    if (state.Key != null) state.Key.forceRenderingOff = state.Value;
                _renderingStates.Clear();
                return;
            }

            var renderers = new HashSet<Renderer>(GetComponentsInChildren<Renderer>(true));
            var model = GetComponent<ModelLocator>()?.modelTransform;
            if (model != null)
                renderers.UnionWith(model.GetComponentsInChildren<Renderer>(true));
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (!_renderingStates.ContainsKey(renderer))
                    _renderingStates.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
        }

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
            Log.Debug($"[BodyColliderCache] Populating collider cache for {gameObject.name}");

            var modelLocator = GetComponent<ModelLocator>();
            if (modelLocator != null && modelLocator.modelTransform != null)
            {

                _colliders = modelLocator.modelTransform.GetComponentsInChildren<Collider>(true);
                if (GetComponent<WakeDonationController>() != null)
                {
                    var colliders = new HashSet<Collider>(_colliders);
                    colliders.UnionWith(GetComponentsInChildren<Collider>(true));
                    _colliders = new Collider[colliders.Count];
                    colliders.CopyTo(_colliders);
                }
            }
            else
            {

                _colliders = GetComponentsInChildren<Collider>(true);
            }

            Log.Debug($"[BodyColliderCache] Found {_colliders?.Length ?? 0} colliders for {gameObject.name}");

            _isInitialized = true;
        }

        public void RefreshCache()
        {
            _isInitialized = false;
        }

        public Dictionary<Collider, bool> OriginalStates { get; } = new Dictionary<Collider, bool>();

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

        public static bool ProtectDeadPassenger(GameObject obj)
        {
            if (obj == null) return false;
            var body = obj.GetComponent<CharacterBody>();
            if (body == null || body.healthComponent == null || body.healthComponent.alive) return false;

            obj.layer = LayerIndex.fakeActor.intVal;
            if (body.characterMotor != null) body.characterMotor.Motor.RebuildCollidableLayers();
            return true;
        }

        public static void RestoreMovementColliders(System.Collections.Generic.Dictionary<Collider, bool> originalStates)
        {
            foreach (var kvp in originalStates)
            {
                if (kvp.Key != null)
                {
                    var cache = kvp.Key.GetComponentInParent<BodyColliderCache>();
                    if (cache == null || !ProtectDeadPassenger(cache.gameObject))
                        kvp.Key.enabled = kvp.Value;

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
