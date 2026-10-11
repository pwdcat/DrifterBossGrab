#nullable enable
using System.Collections.Generic;
using System.Reflection;
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

        public static void CapturePassenger(VehicleSeat seat, GameObject? passenger)
        {
            if (passenger == null || passenger.GetComponent<CharacterBody>() == null) return;
            var bag = seat.GetComponentInParent<DrifterBagController>();
            if (bag == null || bag.characterBody == null) return;
            var protection = passenger.GetComponent<PassengerReleaseProtection>() ?? passenger.AddComponent<PassengerReleaseProtection>();
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
            LogCollisionState("capture");
        }

        public void Begin(CharacterBody owner, GameObject? projectile = null)
        {
            if (_owner == null && _snapshotOwner == owner) _owner = owner;
            else Capture(owner);
            if (projectile != null) _projectile = projectile;
            if (!_protecting) _active.Add(this);
            _protecting = true;
            Apply();
            LogHeldCollisionState();
        }

        private void LogHeldCollisionState()
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value || _owner == null) return;
            var ownerMotor = _owner.GetComponent<CharacterMotor>();
            var attached = ownerMotor != null && ownerMotor.Motor != null ? ownerMotor.Motor.AttachedRigidbody : null;
            int enabledSolids = 0;
            int acceptedSolids = 0;
            bool attachedToPassenger = false;
            foreach (var collider in _passengerColliders)
            {
                if (collider == null) continue;
                if (attached != null && collider.attachedRigidbody == attached) attachedToPassenger = true;
                if (!collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                enabledSolids++;
                if (ownerMotor != null && ownerMotor.IsColliderValidForCollisions(collider)) acceptedSolids++;
            }
            LogCollisionState("held");
            Log.Debug($"[HeldCollisionState] peer={(UnityEngine.Networking.NetworkServer.active ? "server" : "client")}, object={name}, solids={_passengerColliders.Length}, enabledSolids={enabledSolids}, acceptedByOwnerMotor={acceptedSolids}, ownerAttachedBody={attached?.name}, attachedToPassenger={attachedToPassenger}");
        }

        private static Collider[] GetSolidColliders(GameObject obj)
        {
            var colliders = new HashSet<Collider>(obj.GetComponentsInChildren<Collider>(true));
            var model = obj.GetComponent<ModelLocator>()?.modelTransform;
            if (model != null) colliders.UnionWith(model.GetComponentsInChildren<Collider>(true));
            var body = obj.GetComponent<CharacterBody>();
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
            LogCollisionState("seat-exit");
        }

        private void FixedUpdate()
        {
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
            bool wasProtecting = _protecting;
            var owner = _owner;
            _active.Remove(this);
            _owner = null;
            _projectile = null;
            _protecting = false;
            if (wasProtecting && this != null)
                Log.Debug($"[PassengerReleaseProtection] Clearing protection for {name}: pairs={_pairs.Count}, owner={(owner != null ? owner.name : "destroyed")}");
            foreach (var pair in _pairs)
                if (pair.passenger != null && pair.owner != null)
                    Physics.IgnoreCollision(pair.passenger, pair.owner, pair.ignored);
            if (wasProtecting && this != null && _snapshotOwner != null) LogCollisionState("separated");
        }

        private void LogCollisionState(string phase)
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value) return;
            var body = GetComponent<CharacterBody>();
            var motor = GetComponent<CharacterMotor>();
            var rb = GetComponent<Rigidbody>();
            var model = body != null && body.modelLocator != null ? body.modelLocator.modelTransform : null;
            var hurtBoxes = model != null ? model.GetComponent<HurtBoxGroup>() : null;
            var seat = VehicleSeat.FindVehicleSeatWithPassenger(gameObject);
            var currentVehicle = body != null ? body.currentVehicle : null;
            int ignoredPairs = 0;
            int originallyIgnoredPairs = 0;
            foreach (var pair in _pairs)
            {
                if (pair.ignored) originallyIgnoredPairs++;
                if (pair.passenger != null && pair.owner != null && Physics.GetIgnoreCollision(pair.passenger, pair.owner)) ignoredPairs++;
            }
            Log.Debug($"[ReleaseCollisionState] phase={phase}, peer={(UnityEngine.Networking.NetworkServer.active ? "server" : "client")}, object={name}, netId={GetComponent<UnityEngine.Networking.NetworkIdentity>()?.netId}, layer={gameObject.layer}, seat={seat?.name}, seatDisableAll={seat?.disableAllCollidersAndHurtboxes}, seatDisableColliders={seat?.disableColliders}, currentVehicle={(currentVehicle != null ? currentVehicle.name : null)}, motorEnabled={motor?.enabled}, innerMotorEnabled={motor?.Motor?.enabled}, motorLayers={motor?.Motor?.CollidableLayers.value}, isKinematic={rb?.isKinematic}, detectCollisions={rb?.detectCollisions}, hurtBoxCounter={hurtBoxes?.hurtBoxesDeactivatorCounter}, ignoredPairs={ignoredPairs}, originallyIgnoredPairs={originallyIgnoredPairs}");
        }

        private static readonly FieldInfo? _seatPassengerInfo = AccessTools.Field(typeof(VehicleSeat), "passengerInfo");
        private static readonly FieldInfo? _seatPassengerBody = _seatPassengerInfo != null ? AccessTools.Field(_seatPassengerInfo.FieldType, "body") : null;
        private static readonly FieldInfo? _seatColliders = _seatPassengerInfo != null ? AccessTools.Field(_seatPassengerInfo.FieldType, "allColliders") : null;
        private static readonly FieldInfo? _seatColliderStates = _seatPassengerInfo != null ? AccessTools.Field(_seatPassengerInfo.FieldType, "originalCollidersState") : null;
        private static readonly FieldInfo? _seatColliderOverride = _seatPassengerInfo != null ? AccessTools.Field(_seatPassengerInfo.FieldType, "collidersOverride") : null;

        private static void LogSeatTransition(VehicleSeat seat, GameObject? passenger, string phase)
        {
            if (!PluginConfig.Instance.EnableDebugLogs.Value || seat == null) return;
            var info = _seatPassengerInfo?.GetValue(seat);
            var infoBody = info != null ? _seatPassengerBody?.GetValue(info) as CharacterBody : null;
            if (passenger == null && infoBody != null) passenger = infoBody.gameObject;
            if (passenger == null || passenger.GetComponent<PassengerReleaseProtection>() == null) return;
            var body = passenger.GetComponent<CharacterBody>();
            var otherSeat = VehicleSeat.FindVehicleSeatWithPassenger(passenger, seat);
            var colliders = info != null ? _seatColliders?.GetValue(info) as Collider[] : null;
            var states = info != null ? _seatColliderStates?.GetValue(info) as bool[] : null;
            var colliderOverride = info != null ? _seatColliderOverride?.GetValue(info) : null;
            var rb = passenger.GetComponent<Rigidbody>();
            var rigidbodyMotor = passenger.GetComponent<RigidbodyMotor>();
            Log.Debug($"[SeatCollisionTransition] frame={Time.frameCount}, phase={phase}, peer={(UnityEngine.Networking.NetworkServer.active ? "server" : "client")}, object={passenger.name}, netId={passenger.GetComponent<UnityEngine.Networking.NetworkIdentity>()?.netId}, alive={body?.healthComponent?.alive}, seat={seat.name}#{seat.GetInstanceID()}, otherSeat={otherSeat?.name}, infoBody={infoBody?.name}, disableAll={seat.disableAllCollidersAndHurtboxes}, disableColliders={seat.disableColliders}, disableMotor={seat.disablePassengerMotor}, collidersOverride={colliderOverride}, colliderCount={colliders?.Length}, savedCount={states?.Length}, parent={passenger.transform.parent?.name}, isKinematic={rb?.isKinematic}, rigidbodyMotorEnabled={rigidbodyMotor?.enabled}, rigidbodyMotorAuthority={rigidbodyMotor?.hasEffectiveAuthority}");
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
                LogSeatTransition(__instance, passenger, "enter-before");
            }

            [HarmonyPostfix]
            public static void Postfix(VehicleSeat __instance, GameObject passenger, HurtBoxGroup? __state)
            {
                var body = passenger != null ? passenger.GetComponent<CharacterBody>() : null;
                if (__state != null && passenger != null && body != null && body.currentVehicle == __instance)
                {
                    __state.hurtBoxesDeactivatorCounter--;
                    Log.Debug($"[SeatCollisionTransition] Balanced inherited hurtbox disable for {passenger.name}: counter={__state.hurtBoxesDeactivatorCounter}");
                }
                var protection = passenger != null ? passenger.GetComponent<PassengerReleaseProtection>() : null;
                if (protection != null)
                {
                    protection._lastSeat = __instance;
                    var bag = __instance.GetComponentInParent<DrifterBagController>();
                    if (bag != null && bag.characterBody != null) protection.Begin(bag.characterBody);
                }
                LogSeatTransition(__instance, passenger, "enter-after");
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
                Log.Debug($"[SeatCollisionTransition] Recovering client handoff from {previousSeat.name} (active={previousSeat.isActiveAndEnabled}) to {callingSeat.name} for {gameObject.name}");
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), "OnPassengerExit")]
        public static class VehicleSeat_OnPassengerExit_Diagnostics
        {
            [HarmonyPrefix]
            public static void Prefix(VehicleSeat __instance, GameObject passenger)
            {
                LogSeatTransition(__instance, passenger, "exit-before");
            }

            [HarmonyPostfix]
            public static void Postfix(VehicleSeat __instance, GameObject passenger)
            {
                LogSeatTransition(__instance, passenger, "exit-after");
            }
        }

        [HarmonyPatch(typeof(VehicleSeat), "SetPassengerCollisions")]
        public static class VehicleSeat_SetPassengerCollisions_Diagnostics
        {
            [HarmonyPrefix]
            public static void Prefix(VehicleSeat __instance, bool enabled)
            {
                LogSeatTransition(__instance, null, enabled ? "restore-before" : "disable-before");
            }

            [HarmonyPostfix]
            public static void Postfix(VehicleSeat __instance, bool enabled)
            {
                LogSeatTransition(__instance, null, enabled ? "restore-after" : "disable-after");
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
