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
        private bool _clientBagged;
        private readonly Dictionary<Collider, bool> _clientBagColliderStates = new();
        private float _restoreProtectionUntil;
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
            if (!_hasWingMotorSnapshot || !_wingWasCarried || _clientBagged || _projectile != null ||
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
            if (bag == null || bag.characterBody == null || passenger == bag.gameObject) return;
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
            if (owner == null || owner.gameObject == gameObject) return;
            if (_owner == null && _snapshotOwner == owner) _owner = owner;
            else Capture(owner);
            if (projectile != null) _projectile = projectile;
            if (!_protecting) _active.Add(this);
            _protecting = true;
            Apply();
        }

        internal void BeginRestoration(CharacterBody owner, float duration)
        {
            Begin(owner);
            _restoreProtectionUntil = Time.unscaledTime + duration;
        }

        internal void FinishRestoration()
        {
            _restoreProtectionUntil = 0f;
            StartSeparationWatch();
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
            StartSeparationWatch();
        }

        internal void SetClientBagged(CharacterBody? owner, bool bagged)
        {
            if (UnityEngine.Networking.NetworkServer.active) return;
            if (bagged && (owner == null || owner.gameObject == gameObject)) return;
            if (bagged) Begin(owner!);
            _clientBagged = bagged;
            if (!bagged)
            {
                RestoreClientBagColliders();
                StartSeparationWatch();
                return;
            }

            var body = GetComponent<CharacterBody>();
            var seat = body != null ? body.currentVehicle : null;
            foreach (var collider in GetSolidColliders(gameObject))
            {
                if (!_clientBagColliderStates.ContainsKey(collider))
                {
                    bool original = collider.enabled;
                    if (seat != null && VehicleSeat_SetPassengerCollisions_Patch.TryGetOriginalState(seat, collider, out bool seatOriginal))
                        original = seatOriginal;
                    _clientBagColliderStates.Add(collider, original);
                }
                collider.enabled = false;
            }
        }

        private void RestoreClientBagColliders(bool force = false)
        {
            if (_clientBagColliderStates.Count == 0) return;
            if (!force)
            {
                var seat = VehicleSeat.FindVehicleSeatWithPassenger(gameObject);
                if (seat != null && (seat.GetComponentInParent<DrifterBagController>() != null ||
                    seat.GetComponent<RoR2.Projectile.ThrownObjectProjectileController>() != null)) return;
                if (Patches.ProjectileRecoveryPatches.IsInProjectileState(gameObject)) return;
            }
            bool dead = BodyColliderCache.ProtectDeadPassenger(gameObject);
            foreach (var pair in _clientBagColliderStates)
                if (pair.Key != null && !dead) pair.Key.enabled = pair.Value;
            _clientBagColliderStates.Clear();
        }

        private Coroutine? _separationRoutine;

        private void LateUpdate()
        {
            if (UnityEngine.Networking.NetworkServer.active) return;
            if (_clientBagged)
            {
                foreach (var collider in _clientBagColliderStates.Keys)
                    if (collider != null && collider.enabled) collider.enabled = false;
            }
            else RestoreClientBagColliders();
        }

        internal void StartSeparationWatch()
        {
            if (!gameObject.activeInHierarchy || !_protecting || _clientBagged) return;
            if (_separationRoutine != null)
            {
                StopCoroutine(_separationRoutine);
            }
            _separationRoutine = StartCoroutine(SeparationRoutine());
        }

        private System.Collections.IEnumerator SeparationRoutine()
        {
            float timeout = Time.unscaledTime + 1.0f;
            var wait = new WaitForFixedUpdate();

            while (true)
            {
                yield return wait;

                if (_clientBagged)
                {
                    _separationRoutine = null;
                    yield break;
                }

                if (!_protecting || _owner == null)
                {
                    Clear();
                    yield break;
                }

                if (VehicleSeat.FindVehicleSeatWithPassenger(gameObject) != null ||
                    _projectile != null || Patches.ProjectileRecoveryPatches.IsInProjectileState(gameObject) ||
                    Time.unscaledTime < _restoreProtectionUntil)
                {
                    timeout = Time.unscaledTime + 1.0f;
                    continue;
                }

                RestoreClientBagColliders();
                if (_clientBagColliderStates.Count != 0) continue;

                if (!IsOverlappingOwner() || Time.unscaledTime >= timeout)
                {
                    break;
                }
            }

            RestoreEncounterMotor();
            Clear();
            _separationRoutine = null;
        }

        private bool IsOverlappingOwner()
        {
            if (_owner == null) return false;
            foreach (var passenger in _passengerColliders)
            {
                if (passenger == null || !passenger.enabled || !passenger.gameObject.activeInHierarchy) continue;
                foreach (var carrier in _ownerColliders)
                {
                    if (carrier == null || !carrier.enabled || !carrier.gameObject.activeInHierarchy) continue;
                    if (Physics.ComputePenetration(carrier, carrier.transform.position, carrier.transform.rotation,
                        passenger, passenger.transform.position, passenger.transform.rotation, out _, out _))
                        return true;
                }
            }
            return false;
        }

        private void OnDisable() => Clear();

        private void OnDestroy() => Clear();

        private void Clear()
        {
            if (_separationRoutine != null)
            {
                StopCoroutine(_separationRoutine);
                _separationRoutine = null;
            }
            _clientBagged = false;
            RestoreClientBagColliders(true);
            _active.Remove(this);
            _owner = null;
            _projectile = null;
            _protecting = false;
            _restoreProtectionUntil = 0f;
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

            [HarmonyPostfix]
            public static void Postfix(VehicleSeat __instance, GameObject passenger)
            {
                if (passenger == null) return;
                var protection = passenger.GetComponent<PassengerReleaseProtection>();
                protection?.StartSeparationWatch();
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

        [HarmonyPatch(typeof(VehicleSeat), "SetPassengerCollisions")]
        public static class VehicleSeat_SetPassengerCollisions_Patch
        {
            private static readonly System.Reflection.FieldInfo PassengerInfoField = AccessTools.Field(typeof(VehicleSeat), "passengerInfo");
            private static readonly System.Reflection.FieldInfo CollidersField = AccessTools.Field(PassengerInfoField.FieldType, "allColliders");
            private static readonly System.Reflection.FieldInfo StatesField = AccessTools.Field(PassengerInfoField.FieldType, "originalCollidersState");

            internal static bool TryGetOriginalState(VehicleSeat seat, Collider collider, out bool enabled)
            {
                var info = PassengerInfoField.GetValue(seat);
                var colliders = (Collider[]?)CollidersField.GetValue(info);
                var states = (bool[]?)StatesField.GetValue(info);
                int index = colliders != null ? System.Array.IndexOf(colliders, collider) : -1;
                enabled = index >= 0 && states != null && index < states.Length && states[index];
                return index >= 0 && states != null && index < states.Length;
            }

            [HarmonyPrefix]
            public static void Prefix(VehicleSeat __instance, bool enabled)
            {
                if (enabled) return;
                var bag = __instance.GetComponentInParent<DrifterBagController>();
                if (bag == null)
                {
                    var projectile = __instance.GetComponent<RoR2.Projectile.ThrownObjectProjectileController>();
                    var owner = projectile != null ? __instance.GetComponent<RoR2.Projectile.ProjectileController>()?.owner : null;
                    if (owner == null || owner.GetComponent<DrifterBagController>() == null) return;
                }
                var passenger = __instance.NetworkpassengerBodyObject;
                if (passenger == null || passenger.GetComponent<CharacterBody>() == null) return;
                var info = PassengerInfoField.GetValue(__instance);
                var colliders = (Collider[])CollidersField.GetValue(info);
                var states = (bool[])StatesField.GetValue(info);
                ExtendCollisionSnapshot(colliders, states, GetSolidColliders(passenger), out var extendedColliders, out var extendedStates);
                if (extendedColliders.Length == colliders.Length) return;
                CollidersField.SetValue(info, extendedColliders);
                StatesField.SetValue(info, extendedStates);
                PassengerInfoField.SetValue(__instance, info);
            }
        }

        internal static void ExtendCollisionSnapshot(Collider[] colliders, bool[] states, IEnumerable<Collider> candidates,
            out Collider[] extendedColliders, out bool[] extendedStates)
        {
            var seen = new HashSet<Collider>(colliders);
            var result = new List<Collider>(colliders);
            var originalStates = new List<bool>(states);
            foreach (var collider in candidates)
            {
                if (collider == null || !seen.Add(collider)) continue;
                result.Add(collider);
                originalStates.Add(collider.enabled);
            }
            extendedColliders = result.ToArray();
            extendedStates = originalStates.ToArray();
        }
    }
}
