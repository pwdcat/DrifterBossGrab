#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2.SolusWeb;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod
{

    public class BodyColliderCache : MonoBehaviour
    {
        private Collider[]? _colliders;
        private bool _isInitialized = false;
        private readonly Dictionary<Renderer, bool> _renderingStates = new Dictionary<Renderer, bool>();

        private static readonly ConditionalWeakTable<ChipAnimator, BodyColliderCache> _heartOwners = new();
        private Transform? _heartCore;
        private CharacterBody? _heartBody;
        private int _heartCoreIndex = -1;

        private Transform? GetHeartCore()
        {
            if (_heartCore != null) return _heartCore;
            var processor = GetComponent<SolusHeartDamageProcessor>();
            var animator = processor != null ? processor.ChipAnimator : null;
            var body = GetComponent<CharacterBody>();
            var model = body != null && body.modelLocator != null ? body.modelLocator.modelTransform : null;
            var group = model != null ? model.GetComponent<HurtBoxGroup>() : null;
            var hurtBox = group != null ? group.mainHurtBox : null;
            if (animator == null || animator.chips == null || hurtBox == null || body == null) return null;
            for (int i = 0; i < animator.chips.Length; i++)
            {
                var chip = animator.chips[i];
                if (chip == null || (hurtBox.transform != chip && !hurtBox.transform.IsChildOf(chip))) continue;
                _heartCore = chip;
                _heartCoreIndex = i;
                _heartBody = body;
                _heartOwners.Remove(animator);
                _heartOwners.Add(animator, this);
                return chip;
            }
            return null;
        }

        [HarmonyPatch(typeof(ChipAnimator), "LateUpdate")]
        public static class ChipAnimator_LateUpdate_CarryCore
        {
            [HarmonyPostfix]
            public static void Postfix(ChipAnimator __instance)
            {
                if (!_heartOwners.TryGetValue(__instance, out var cache) || cache == null) return;
                var body = cache._heartBody;
                var core = cache._heartCore;
                var model = body != null && body.modelLocator != null ? body.modelLocator.modelTransform : null;
                if (body == null || core == null || model == null) return;
                var seat = body.currentVehicle;
                bool carried = body.healthComponent != null && body.healthComponent.alive &&
                    ((seat != null && (seat.GetComponentInParent<DrifterBagController>() != null ||
                        seat.GetComponent<RoR2.Projectile.ThrownObjectProjectileController>() != null)) ||
                    Patches.ProjectileRecoveryPatches.IsInProjectileState(body.gameObject));
                if (!carried) return;
                core.SetPositionAndRotation(model.position, model.rotation);
                core.localScale = model.lossyScale;
                int index = cache._heartCoreIndex;
                if (__instance.velocities != null && index < __instance.velocities.Length)
                    __instance.velocities[index] = Vector4.zero;
                if (__instance.chipTransforms != null && index < __instance.chipTransforms.Length)
                    __instance.chipTransforms[index] = core.localToWorldMatrix;
            }
        }

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
            var core = GetHeartCore();
            if (core != null) renderers.UnionWith(core.GetComponentsInChildren<Renderer>(true));
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

            var core = GetHeartCore();
            if (core != null)
            {
                var colliders = new HashSet<Collider>(_colliders ?? Array.Empty<Collider>());
                colliders.UnionWith(core.GetComponentsInChildren<Collider>(true));
                _colliders = new Collider[colliders.Count];
                colliders.CopyTo(_colliders);
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
