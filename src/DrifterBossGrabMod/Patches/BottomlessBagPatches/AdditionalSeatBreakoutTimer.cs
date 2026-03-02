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
        private static System.Collections.Generic.Dictionary<GameObject, int> _wiggleLoopsActive = new System.Collections.Generic.Dictionary<GameObject, int>();

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
            if (!_hasPlayedRustle && controller != null && NetworkServer.active)
            {
                _hasPlayedRustle = true;
                PlayBagAnimation("Bag, Rumble", "Rustle", "Rumble.playbackRate", 1f, 0.1f);
                AddWiggleLoop(controller.gameObject);
            }

            if (controller == null)
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
                var body = gameObject.GetComponent<CharacterBody>();
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    Destroy(this);
                    return;
                }

                // If not in additional seat anymore, stop timer
                if (BagHelpers.GetAdditionalSeat(controller, gameObject) == null)
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
                    Patches.BagPassengerManager.RemoveBaggedObject(controller, gameObject, true);
                    return;
                }
                
                PlayBagAnimation("Bag, Rumble", "BagBurst", "Rumble.playbackRate", 0.5f, 0.1f);
            }
        }

        private void OnDestroy()
        {
            if (_hasPlayedRustle && controller != null && controller.gameObject != null)
            {
                RemoveWiggleLoop(controller.gameObject);
                PlayBagAnimation("Bag, Rumble", "Empty", "Rumble.playbackRate", 1f, 0.1f);
            }
        }

        private static void AddWiggleLoop(GameObject drifterObject)
        {
            if (drifterObject == null) return;
            if (!_wiggleLoopsActive.ContainsKey(drifterObject))
            {
                _wiggleLoopsActive[drifterObject] = 0;
            }
            if (_wiggleLoopsActive[drifterObject] == 0)
            {
                Util.PlaySound("Play_drifter_repossess_bagWiggle_Loop", drifterObject);
            }
            _wiggleLoopsActive[drifterObject]++;
        }

        private static void RemoveWiggleLoop(GameObject drifterObject)
        {
            if (drifterObject == null) return;
            if (_wiggleLoopsActive.ContainsKey(drifterObject))
            {
                _wiggleLoopsActive[drifterObject]--;
                if (_wiggleLoopsActive[drifterObject] <= 0)
                {
                    Util.PlaySound("Stop_drifter_repossess_bagWiggle_Loop", drifterObject);
                    _wiggleLoopsActive[drifterObject] = 0;
                }
            }
        }

        private void PlayBagAnimation(string layerName, string animationStateName, string playbackRateParam, float duration, float crossfadeDuration)
        {
            try
            {
                if (controller == null) return;
                var esm = EntityStateMachine.FindByCustomName(controller.gameObject, "Bag");
                if (esm != null && esm.state != null)
                {
                    var playCrossfadeMethod = typeof(EntityStates.EntityState).GetMethod(
                        "PlayCrossfade",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        null,
                        new Type[] { typeof(string), typeof(string), typeof(string), typeof(float), typeof(float) },
                        null
                    );
                    
                    if (playCrossfadeMethod != null)
                    {
                        playCrossfadeMethod.Invoke(esm.state, new object[] { layerName, animationStateName, playbackRateParam, duration, crossfadeDuration });
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
            var body = gameObject.GetComponent<CharacterBody>();
            if (body != null && body.healthComponent != null && !body.healthComponent.alive)
            {
                return;
            }

            if (controller == null) return;
            CharacterBody? controllerBody = controller.GetComponent<CharacterBody>();
            if (controllerBody == null) return;

            Vector3 forward = Vector3.up;
            if (controllerBody.characterDirection != null)
            {
                forward = Quaternion.AngleAxis((UnityEngine.Random.value < 0.5f) ? 45f : -45f, -controllerBody.characterDirection.forward) * Vector3.up;
            }
            
            float mass = controller.CalculateBaggedObjectMass(gameObject);
            float speed = Mathf.Max(10f, 30f * mass / DrifterBagController.maxMass);
            
            if (_cachedProjectilePrefab == null)
            {
                _cachedProjectilePrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/ThrownObjectProjectileNoStun.prefab").WaitForCompletion();
            }

            FireProjectileInfo fireProjectileInfo = new FireProjectileInfo
            {
                projectilePrefab = _cachedProjectilePrefab,
                position = controller.transform.position,
                rotation = Util.QuaternionSafeLookRotation(forward),
                owner = controller.gameObject,
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
