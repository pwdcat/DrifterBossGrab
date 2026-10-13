#nullable enable
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Core
{
    public class PassengerReleaseProtection : MonoBehaviour
    {
        private static readonly List<PassengerReleaseProtection> _active = new();
        private readonly List<(Collider passenger, Collider owner, bool ignored)> _pairs = new();
        private CharacterBody? _owner;
        private CharacterBody? _snapshotOwner;
        private Collider[] _passengerColliders = System.Array.Empty<Collider>();
        private Collider[] _ownerColliders = System.Array.Empty<Collider>();
        private GameObject? _projectile;
        private VehicleSeat? _lastSeat;

        private bool _protecting;
        private bool _hasWingMotorSnapshot;
        private bool _wingWasCarried;
        private bool _wingMotorEnabled;
        private bool _wingInnerMotorEnabled;

        internal static void CaptureEncounterMotor(GameObject passenger)
        {
            var body = passenger.GetComponent<CharacterBody>();
            if (body == null || body.bodyIndex != BodyCatalog.FindBodyIndex("SolusWingBody")) return;
            var protection = passenger.GetComponent<PassengerReleaseProtection>() ?? passenger.AddComponent<PassengerReleaseProtection>();
            if (protection._hasWingMotorSnapshot) return;
            var motor = body.characterMotor;
            if (motor == null || motor.Motor == null) return;
            protection._wingMotorEnabled = motor.enabled;
            protection._wingInnerMotorEnabled = motor.Motor.enabled;
            protection._hasWingMotorSnapshot = true;
        }

        private void RestoreEncounterMotor()
        {
            if (!_hasWingMotorSnapshot || !_wingWasCarried || _projectile != null ||
                Patches.ProjectileRecoveryPatches.IsInProjectileState(gameObject) ||
                VehicleSeat.FindVehicleSeatWithPassenger(gameObject) != null) return;
            var body = GetComponent<CharacterBody>();
            var motor = body != null ? body.characterMotor : null;
            _hasWingMotorSnapshot = false;
            _wingWasCarried = false;
            if (body == null || body.healthComponent == null || !body.healthComponent.alive || motor == null || motor.Motor == null) return;
            motor.enabled = _wingMotorEnabled;
            motor.Motor.enabled = _wingInnerMotorEnabled && motor.hasEffectiveAuthority;
        }

        public static void CapturePassenger(VehicleSeat seat, GameObject? passenger)
        {
            if (passenger == null || passenger.GetComponent<CharacterBody>() == null) return;
            var bag = seat.GetComponentInParent<DrifterBagController>();
            if (bag == null || bag.characterBody == null) return;
            var protection = passenger.GetComponent<PassengerReleaseProtection>() ?? passenger.AddComponent<PassengerReleaseProtection>();
            CaptureEncounterMotor(passenger);
            protection._wingWasCarried = protection._hasWingMotorSnapshot;
            protection.Capture(bag.characterBody);
        }

        public void Capture(CharacterBody owner)
        {
            if (_owner == owner) return;
            Clear();
            _pairs.Clear();
            _owner = owner;
            _snapshotOwner = owner;
            _passengerColliders = GetSolidColliders(gameObject);
            _ownerColliders = System.Array.FindAll(GetSolidColliders(owner.gameObject),
                collider => System.Array.IndexOf(_passengerColliders, collider) < 0);
            foreach (var passenger in _passengerColliders)
                foreach (var carrier in _ownerColliders)
                    _pairs.Add((passenger, carrier, Physics.GetIgnoreCollision(passenger, carrier)));
        }

        public void Begin(CharacterBody owner, GameObject? projectile = null)
        {
            if (_owner == null && _snapshotOwner == owner) _owner = owner;
            else Capture(owner);
            if (projectile != null) _projectile = projectile;
            if (!_protecting) _active.Add(this);
            _protecting = true;
            Apply();
        }

        private static Collider[] GetSolidColliders(GameObject obj)
        {
            var colliders = new HashSet<Collider>(obj.GetComponentsInChildren<Collider>(true));
            var model = obj.GetComponent<ModelLocator>()?.modelTransform;
            if (model != null) colliders.UnionWith(model.GetComponentsInChildren<Collider>(true));
            var body = obj.GetComponent<CharacterBody>();
            if (obj.GetComponent<SolusHeartDamageProcessor>() != null)
            {
                var cache = obj.GetComponent<BodyColliderCache>() ?? obj.AddComponent<BodyColliderCache>();
                colliders.UnionWith(cache.GetColliders());
            }
            colliders.RemoveWhere(collider => collider == null || collider.isTrigger || collider.GetComponent<HurtBox>() != null ||
                (collider.GetComponentInParent<CharacterBody>() != null && collider.GetComponentInParent<CharacterBody>() != body));
            var result = new Collider[colliders.Count];
            colliders.CopyTo(result);
            return result;
        }

        public void Apply()
        {
            if (!_protecting) return;
            foreach (var pair in _pairs)
                if (pair.passenger != null && pair.owner != null && pair.passenger.enabled && pair.owner.enabled &&
                    pair.passenger.gameObject.activeInHierarchy && pair.owner.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(pair.passenger, pair.owner, true);
        }

        public void FinishFlight()
        {
            _projectile = null;
            Apply();
            RestoreEncounterMotor();
        }

        private void FixedUpdate()
        {
            RestoreEncounterMotor();
            if (!_protecting) return;
            if (_owner == null) { Clear(); return; }
            Apply();
            if (_projectile != null || VehicleSeat.FindVehicleSeatWithPassenger(gameObject) != null) return;
            foreach (var passenger in _passengerColliders)
            {
                if (passenger == null || !passenger.enabled || !passenger.gameObject.activeInHierarchy) continue;
                foreach (var carrier in _ownerColliders)
                {
                    if (carrier == null || !carrier.enabled || !carrier.gameObject.activeInHierarchy) continue;
                    if (Physics.ComputePenetration(carrier, carrier.transform.position, carrier.transform.rotation,
                        passenger, passenger.transform.position, passenger.transform.rotation, out _, out _)) return;
                }
            }
            Clear();
        }

        private void OnDisable() => Clear();

        private void OnDestroy() => Clear();

        private void Clear()
        {
            _active.Remove(this);
            _owner = null;
            _projectile = null;
            _protecting = false;
            foreach (var pair in _pairs)
                if (pair.passenger != null && pair.owner != null)
                    Physics.IgnoreCollision(pair.passenger, pair.owner, pair.ignored);
        }

        [HarmonyPatch(typeof(VehicleSeat), "OnPassengerEnter")]
        public static class VehicleSeat_OnPassengerEnter_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(VehicleSeat __instance, GameObject passenger, out HurtBoxGroup? __state)
            {
                __state = null;
                if (passenger != null && __instance.GetComponentInParent<DrifterBagController>() != null)
                    Patches.SpecialObjectAttributesPatches.PreparePassengerModelColliders(passenger);
                CapturePassenger(__instance, passenger);
                if (passenger != null && passenger.GetComponent<PassengerReleaseProtection>() != null && __instance.disableAllCollidersAndHurtboxes)
                {
                    var previousSeat = VehicleSeat.FindVehicleSeatWithPassenger(passenger, __instance);
                    var body = passenger.GetComponent<CharacterBody>();
                    var model = body != null && body.modelLocator != null ? body.modelLocator.modelTransform : null;
                    var hurtBoxes = model != null ? model.GetComponent<HurtBoxGroup>() : null;
                    if (previousSeat != null && previousSeat.disableAllCollidersAndHurtboxes && hurtBoxes != null &&
                        hurtBoxes.hurtBoxesDeactivatorCounter > 0) __state = hurtBoxes;
                }
            }

            [HarmonyPostfix]
            public static void Postfix(VehicleSeat __instance, GameObject passenger, HurtBoxGroup? __state)
            {
                var body = passenger != null ? passenger.GetComponent<CharacterBody>() : null;
                if (__state != null && passenger != null && body != null && body.currentVehicle == __instance)
                {
                    __state.hurtBoxesDeactivatorCounter--;
                }
                var protection = passenger != null ? passenger.GetComponent<PassengerReleaseProtection>() : null;
                if (protection != null)
                {
                    protection._lastSeat = __instance;
                    var bag = __instance.GetComponentInParent<DrifterBagController>();
                    if (bag != null && bag.characterBody != null) protection.Begin(bag.characterBody);
                }
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), nameof(VehicleSeat.FindVehicleSeatWithPassenger))]
        public static class VehicleSeat_FindVehicleSeatWithPassenger_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(GameObject gameObject, VehicleSeat callingSeat, ref VehicleSeat? __result)
            {
                if (__result != null || UnityEngine.Networking.NetworkServer.active || gameObject == null || callingSeat == null ||
                    callingSeat.GetComponent<RoR2.Projectile.ThrownObjectProjectileController>() == null) return;
                var protection = gameObject.GetComponent<PassengerReleaseProtection>();
                var previousSeat = protection != null ? protection._lastSeat : null;
                if (previousSeat == null || previousSeat == callingSeat || previousSeat.NetworkpassengerBodyObject != gameObject ||
                    previousSeat.GetComponentInParent<DrifterBagController>() == null) return;
                __result = previousSeat;
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), "OnPassengerExit")]
        public static class VehicleSeat_OnPassengerExit_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(VehicleSeat __instance, GameObject passenger)
            {
                if (passenger != null && __instance.GetComponentInParent<DrifterBagController>() == null &&
                    __instance.GetComponent<RoR2.Projectile.ThrownObjectProjectileController>() == null)
                    CaptureEncounterMotor(passenger);
            }
        }

        [HarmonyPatch(typeof(CharacterMotor), nameof(CharacterMotor.IsColliderValidForCollisions))]
        public static class CharacterMotor_IsColliderValidForCollisions_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CharacterMotor __instance, Collider coll, ref bool __result)
            {
                if (!__result || __instance == null || coll == null) return;
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var protection = _active[i];
                    if (protection == null)
                    {
                        _active.RemoveAt(i);
                        continue;
                    }
                    if (protection._owner == null)
                    {
                        protection.Clear();
                        continue;
                    }
                    bool ownerMotor = __instance.gameObject == protection._owner.gameObject;
                    bool passengerMotor = __instance.gameObject == protection.gameObject;
                    if ((ownerMotor && System.Array.IndexOf(protection._passengerColliders, coll) >= 0) ||
                        (passengerMotor && System.Array.IndexOf(protection._ownerColliders, coll) >= 0))
                    {
                        __result = false;
                        return;
                    }
                }
            }
        }
    }
}
