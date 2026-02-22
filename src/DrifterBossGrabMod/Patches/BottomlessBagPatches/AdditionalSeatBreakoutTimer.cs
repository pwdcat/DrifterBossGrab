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
        
        private float _breakoutTimer;
        private float _breakoutAttempts;
        private int _baseBreakoutChance1inX = 3;
        private static GameObject? _cachedProjectilePrefab;

        private void FixedUpdate()
        {
            if (controller == null)
            {
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
                _breakoutAttempts += 1f;

                // Play sound
                var sfxLocator = gameObject.GetComponent<SfxLocator>();
                if (sfxLocator)
                {
                    Util.PlaySound(sfxLocator.barkSound, gameObject);
                }

                if (!DrifterBagController.bagDisableBreakout && UnityEngine.Random.Range(0, _baseBreakoutChance1inX) == 0)
                {
                    Breakout();
                    Patches.BagPassengerManager.RemoveBaggedObject(controller, gameObject, true);
                    return;
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
