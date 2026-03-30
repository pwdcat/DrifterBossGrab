#nullable enable
using System;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using DrifterBossGrabMod.Core;

namespace DrifterBossGrabMod.Patches
{
    public class AdditionalSeatBreakoutTimer : MonoBehaviour
    {
        public DrifterBagController? controller;
        public float breakoutTime;
        public float breakoutAttempts;
        
        private bool _hasPlayedRustle = false;
        private CharacterBody? _cachedBody;
        private static System.Collections.Generic.Dictionary<GameObject, int> _wiggleLoopsActive = new System.Collections.Generic.Dictionary<GameObject, int>();
        private static readonly System.Reflection.MethodInfo _cachedPlayCrossfadeMethod = typeof(EntityStates.EntityState).GetMethod(
            "PlayCrossfade",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            new Type[] { typeof(string), typeof(string), typeof(string), typeof(float), typeof(float) },
            null
        );

        // Check if an object is eligible for breakout.
        // requires CharacterBody, not player-controlled, and has a CharacterMaster.
        public static bool CanBreakout(GameObject obj)
        {
            if (obj == null) return false;
            var body = obj.GetComponent<CharacterBody>();
            if (body == null) return false;
            if (body.isPlayerControlled) return false;
            if (body.master == null) return false;
            if (body.healthComponent == null || !body.healthComponent.alive) return false;
            return true;
        }
        
        private float _breakoutTimer;
        private int _baseBreakoutChance1inX = 3;

        private void Start()
        {
            _cachedBody = gameObject.GetComponent<CharacterBody>();
        }

        public float GetElapsedBreakoutTime()
        {
            return _breakoutTimer;
        }

        public void SetElapsedBreakoutTime(float time)
        {
            _breakoutTimer = time;
        }
        private static GameObject? _cachedProjectilePrefab;

        private void FixedUpdate()
        {
            var currentController = controller;
            if (!_hasPlayedRustle && currentController != null && NetworkServer.active)
            {
                _hasPlayedRustle = true;
                PlayBagAnimation("Bag, Rumble", "Rustle", "Rumble.playbackRate", 1f, 0.1f);
                AddWiggleLoop(currentController.gameObject);
            }

            if (currentController == null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[DEBUG] [AdditionalSeatBreakoutTimer] Destroying timer on {gameObject.name}: controller is null");
                Destroy(this);
                return;
            }

            // Only run on server/authority
            if (!NetworkServer.active) return;

            // Stop timer if dead or invalid
            try
            {
                var body = _cachedBody ?? gameObject.GetComponent<CharacterBody>();
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    Destroy(this);
                    return;
                }

                // If not in additional seat anymore, stop timer
                if (BagHelpers.GetAdditionalSeat(currentController, gameObject) == null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[DEBUG] [AdditionalSeatBreakoutTimer] Destroying timer on {gameObject.name}: no longer in an additional seat");
                    Destroy(this);
                    return;
                }
            }
            catch (Exception ex)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Warning($"[AdditionalSeatBreakoutTimer] Error validating object state: {ex}");
                Destroy(this);
                return;
            }

            // Core timer logic reproduced from BaggedObject
            _breakoutTimer += Time.fixedDeltaTime;

            if (_breakoutTimer >= breakoutTime * 0.5f)
            {
                // Force breakout attributes
                SpecialObjectAttributes.ForceBreakout(gameObject);
            }

            if (_breakoutTimer >= breakoutTime)
            {
                _breakoutTimer -= breakoutTime;
                breakoutTime *= 0.65f;
                breakoutAttempts += 1;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[DEBUG] [AdditionalSeatBreakoutTimer] {gameObject.name} breakout attempt #{breakoutAttempts}. Breakout time adjusted to {breakoutTime:F2}"); // Changed from _breakoutAttempts to breakoutAttempts

                // Play sound
                var sfxLocator = gameObject.GetComponent<SfxLocator>();
                if (sfxLocator)
                {
                    Util.PlaySound(sfxLocator.barkSound, gameObject);
                }

                if (!DrifterBagController.bagDisableBreakout && UnityEngine.Random.Range(0, _baseBreakoutChance1inX) == 0)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[DEBUG] [AdditionalSeatBreakoutTimer] {gameObject.name} successfully broke out from the additional seat!");
                    Breakout();
                    Patches.BagPassengerManager.RemoveBaggedObject(currentController, gameObject, true);
                    return;
                }
                
                PlayBagAnimation("Bag, Rumble", "BagBurst", "Rumble.playbackRate", 0.5f, 0.1f);
            }
        }

        private void OnDestroy()
        {
            var currentController = controller;
            if (_hasPlayedRustle && currentController != null && currentController.gameObject != null)
            {
                RemoveWiggleLoop(currentController.gameObject);
                PlayBagAnimation("Bag, Rumble", "Empty", "Rumble.playbackRate", 1f, 0.1f);
            }
        }

        private static void AddWiggleLoop(GameObject drifterObject)
        {
            if (drifterObject == null) return;
            if (!_wiggleLoopsActive.TryGetValue(drifterObject, out int count))
            {
                count = 0;
            }
            if (count == 0)
            {
                Util.PlaySound("Play_drifter_repossess_bagWiggle_Loop", drifterObject);
            }
            _wiggleLoopsActive[drifterObject] = count + 1;
        }

        private static void RemoveWiggleLoop(GameObject drifterObject)
        {
            if (drifterObject == null) return;
            if (_wiggleLoopsActive.TryGetValue(drifterObject, out int count))
            {
                count--;
                if (count <= 0)
                {
                    Util.PlaySound("Stop_drifter_repossess_bagWiggle_Loop", drifterObject);
                    count = 0;
                }
                _wiggleLoopsActive[drifterObject] = count;
            }
        }

        private void PlayBagAnimation(string layerName, string animationStateName, string playbackRateParam, float duration, float crossfadeDuration)
        {
            try
            {
                var currentController = controller;
                if (currentController == null) return;
                var esm = EntityStateMachine.FindByCustomName(currentController.gameObject, "Bag");
                if (esm != null && esm.state != null)
                {
                    if (_cachedPlayCrossfadeMethod != null)
                    {
                        _cachedPlayCrossfadeMethod.Invoke(esm.state, new object[] { layerName, animationStateName, playbackRateParam, duration, crossfadeDuration });
                    }
                }
            }
            catch (Exception ex)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Warning($"[AdditionalSeatBreakoutTimer] Failed to play animation {animationStateName}: {ex}");
                }
            }
        }

        private void Breakout()
        {
            if (gameObject == null) return;
            var body = _cachedBody ?? gameObject.GetComponent<CharacterBody>();
            if (body != null && body.healthComponent != null && !body.healthComponent.alive)
            {
                return;
            }

            var currentController = controller;
            if (currentController == null) return;

            // Use the enemy's own character direction, not the controller's
            Vector3 forward = Vector3.up;
            if (body != null && body.characterDirection != null)
            {
                forward = Quaternion.AngleAxis((UnityEngine.Random.value < 0.5f) ? 45f : -45f, -body.characterDirection.forward) * Vector3.up;
            }
            
            float mass = currentController.CalculateBaggedObjectMass(gameObject);
            float speed = Mathf.Max(10f, 30f * mass / DrifterBossGrabMod.Balance.CapacityScalingSystem.CalculateMassCapacity(currentController));
            
            // Apply max launch speed cap if configured
            if (!PluginConfig.Instance.IsMaxLaunchSpeedInfinite)
            {
                speed = Mathf.Min(speed, PluginConfig.Instance.ParsedMaxLaunchSpeed);
            }
            
            if (_cachedProjectilePrefab == null)
            {
                _cachedProjectilePrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/ThrownObjectProjectileNoStun.prefab").WaitForCompletion();
            }

            // Try to find the exit transform on the enemy's model
            Transform? exitTransform = null;
            if (body != null && body.modelLocator != null && body.modelLocator.modelTransform != null)
            {
                var modelAnimator = body.modelLocator.modelTransform.GetComponent<Animator>();
                if (modelAnimator != null)
                {
                    // Look for an exit transform on the enemy's model
                    // This matches the vanilla BaggedObject behavior
                    var childTransforms = body.modelLocator.modelTransform.GetComponentsInChildren<Transform>();
                    foreach (var child in childTransforms)
                    {
                        if (child.name.Contains("Exit") || child.name.Contains("Muzzle") || child.name.Contains("Throw"))
                        {
                            exitTransform = child;
                            break;
                        }
                    }
                }
            }

            FireProjectileInfo fireProjectileInfo = new FireProjectileInfo
            {
                projectilePrefab = _cachedProjectilePrefab,
                position = (exitTransform != null) ? exitTransform.position : ((body != null) ? body.transform.position : currentController.transform.position),
                rotation = Util.QuaternionSafeLookRotation(forward),
                owner = currentController.gameObject,
                damage = 0f,
                speedOverride = speed,
                force = 20f,
                crit = false,
                damageColorIndex = DamageColorIndex.Default,
                target = null
            };
            
            // Spawn projectile immediately to hook up the passenger
            GameObject spawnedProjectile = ProjectileManager.instance.FireProjectileImmediateServer(fireProjectileInfo, null, 0, 0.0);
            if (spawnedProjectile != null)
            {
                var thrownController = spawnedProjectile.GetComponent<ThrownObjectProjectileController>();
                if (thrownController != null)
                {
                    thrownController.SetPassengerServer(gameObject);
                }
            }
        }
    }
}
