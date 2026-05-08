#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using EntityStates.Drifter.Bag;
using DrifterBossGrabMod.Core;
using DrifterBossGrabMod.Networking;
using DrifterBossGrabMod.UI;
using UnityEngine.AddressableAssets;
using RoR2.Projectile;
using EntityStateMachine = RoR2.EntityStateMachine;

namespace DrifterBossGrabMod.Patches
{
    // ========================================================================================
    // BOTTOMLESS BAG INTERACTION
    // ========================================================================================

    public static class BottomlessBagPatches
    {
        public static void HandleInput() => CyclingInputHandler.HandleInput();
        public static void CyclePassengers(DrifterBagController ctrl, int amount) => PassengerCycler.CyclePassengers(ctrl, amount);
        public static void ServerCyclePassengers(DrifterBagController ctrl, int amount) => PassengerCycler.ServerCyclePassengers(ctrl, amount);
    }

    // ========================================================================================
    // CYCLING INPUT HANDLER
    // ========================================================================================

    public static class CyclingInputHandler
    {
        private static float _lastCycleTime = 0f;
        private static float _scrollAccumulator = 0f;
        private const float SCROLL_THRESHOLD = 0.1f;
        private static DrifterBagController? _cachedLocalController;

        public static void HandleInput()
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value) return;

            int cycleAmount = 0;
            if (PluginConfig.Instance.EnableMouseWheelScrolling.Value)
            {
                float scrollDelta = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
                if (scrollDelta != 0f)
                {
                    if (_scrollAccumulator != 0f && Mathf.Sign(scrollDelta) != Mathf.Sign(_scrollAccumulator)) _scrollAccumulator = 0f;
                    _scrollAccumulator += scrollDelta;
                }
                else _scrollAccumulator = Mathf.MoveTowards(_scrollAccumulator, 0f, Time.deltaTime * 0.5f);

                if (Mathf.Abs(_scrollAccumulator) >= SCROLL_THRESHOLD && Time.time >= _lastCycleTime + PluginConfig.Instance.CycleCooldown.Value)
                {
                    bool up = (_scrollAccumulator > 0f) ? !PluginConfig.Instance.InverseMouseWheelScrolling.Value : PluginConfig.Instance.InverseMouseWheelScrolling.Value;
                    cycleAmount = up ? 1 : -1;
                    _scrollAccumulator -= Mathf.Sign(_scrollAccumulator) * SCROLL_THRESHOLD;
                    _lastCycleTime = Time.time;
                }
            }

            if (Time.time >= _lastCycleTime + PluginConfig.Instance.CycleCooldown.Value)
            {
                if (LocalUserManager.GetFirstLocalUser()?.inputPlayer is { } player)
                {
                    if (player.GetButtonDown(DrifterBossGrabMod.Input.RewiredActions.ScrollBagUp.ActionId)) { cycleAmount--; _lastCycleTime = Time.time; }
                    if (player.GetButtonDown(DrifterBossGrabMod.Input.RewiredActions.ScrollBagDown.ActionId)) { cycleAmount++; _lastCycleTime = Time.time; }
                }
            }

            if (cycleAmount != 0) CyclePassengers(cycleAmount);
        }

        private static void CyclePassengers(int amount)
        {
            if (amount == 0) return;
            if (_cachedLocalController && _cachedLocalController!.isAuthority) { PassengerCycler.CyclePassengers(_cachedLocalController!, amount); return; }
            if (LocalUserManager.GetFirstLocalUser()?.cachedBody?.GetComponent<DrifterBagController>() is { } ctrl && ctrl.isAuthority)
            {
                _cachedLocalController = ctrl;
                PassengerCycler.CyclePassengers(ctrl, amount);
            }
        }
    }

    // ========================================================================================
    // PASSENGER CYCLER
    // ========================================================================================

    public static class PassengerCycler
    {
        private static readonly HashSet<int> _seenBuffer = new();
        private static readonly List<GameObject> _validBuffer = new();
        private static readonly List<GameObject> _regrabBuffer = new();

        public static void CyclePassengers(DrifterBagController bagController, int amount)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value || !bagController || amount == 0) return;
            if (BagCapacityCalculator.GetUtilityMaxStock(bagController) <= 1) return;

            if (!NetworkServer.active && bagController.hasAuthority) { CycleNetworkHandler.SendCycleRequest(bagController, amount); return; }
            if (NetworkServer.active) ServerCyclePassengers(bagController, amount);
        }

        public static void ServerCyclePassengers(DrifterBagController bagController, int amount)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value || !NetworkServer.active || !bagController.vehicleSeat) return;

            var baggedObjects = API.DrifterBagAPI.GetBaggedObjects(bagController);
            if (baggedObjects == null || baggedObjects.Count == 0) return;

            _seenBuffer.Clear(); _validBuffer.Clear(); _regrabBuffer.Clear();
            foreach (var sceneObj in SpecialObjectAttributesPatches.RegisteredObjects)
            {
                if (sceneObj && PluginConfig.IsGrabbable(sceneObj) && baggedObjects.Any(o => o && o.GetInstanceID() == sceneObj.GetInstanceID()) && !ProjectileRecoveryPatches.IsInProjectileState(sceneObj))
                    _regrabBuffer.Add(sceneObj);
            }

            foreach (var obj in baggedObjects)
            {
                if (obj && !ProjectileRecoveryPatches.IsInProjectileState(obj) && !_seenBuffer.Contains(obj.GetInstanceID()))
                {
                    _seenBuffer.Add(obj.GetInstanceID());
                    _validBuffer.Add(obj);
                }
            }

            foreach (var regrab in _regrabBuffer)
            {
                if (!_seenBuffer.Contains(regrab.GetInstanceID())) { _seenBuffer.Add(regrab.GetInstanceID()); _validBuffer.Add(regrab); }
            }

            if (_validBuffer.Count > 0) CycleToNextObject(bagController, _validBuffer, amount);
        }

        private static void CycleToNextObject(DrifterBagController bagController, List<GameObject> validObjects, int amount)
        {
            var localSeatDict = new ConcurrentDictionary<GameObject, VehicleSeat>(API.DrifterBagAPI.GetAdditionalSeats(bagController));
            var vehicleSeat = bagController.vehicleSeat;
            GameObject? mainPassenger = API.DrifterBagAPI.GetMainPassenger(bagController);

            // Recovery logic for main seat if null
            if (mainPassenger == null && vehicleSeat.hasPassenger && vehicleSeat.NetworkpassengerBodyObject is { } seatPassenger)
            {
                if (validObjects.Any(o => o && o.GetInstanceID() == seatPassenger.GetInstanceID()))
                {
                    if (!API.DrifterBagAPI.ContainsInstanceId(bagController, seatPassenger.GetInstanceID())) { API.DrifterBagAPI.AddBaggedObject(bagController, seatPassenger); API.DrifterBagAPI.AddInstanceId(bagController, seatPassenger.GetInstanceID()); }
                    API.DrifterBagAPI.SetMainSeatObject(bagController, seatPassenger);
                    UI.BagCarouselUpdater.UpdateCarousel(bagController, 0);
                    mainPassenger = seatPassenger;
                }
            }

            // Sync check
            if (mainPassenger != null)
            {
                bool inMain = vehicleSeat.hasPassenger && vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == mainPassenger.GetInstanceID();
                bool inAdd = localSeatDict.Any(kvp => kvp.Value && kvp.Value.hasPassenger && kvp.Value.NetworkpassengerBodyObject.GetInstanceID() == mainPassenger.GetInstanceID());
                if (!inMain && inAdd) { API.DrifterBagAPI.SetMainSeatObject(bagController, null); mainPassenger = null; }
            }

            if (mainPassenger != null && (!validObjects.Any(o => o && o.GetInstanceID() == mainPassenger.GetInstanceID()) || ProjectileRecoveryPatches.IsInProjectileState(mainPassenger)))
            {
                API.DrifterBagAPI.SetMainSeatObject(bagController, null);
                UI.BagCarouselUpdater.UpdateCarousel(bagController, 0);
                mainPassenger = null;
            }

            int currentIndex = -1;
            bool isInNullState = mainPassenger == null && validObjects.Count > 0;
            if (isInNullState) currentIndex = validObjects.Count;
            else if (mainPassenger != null)
            {
                for (int i = 0; i < validObjects.Count; i++)
                    if (validObjects[i] && validObjects[i].GetInstanceID() == mainPassenger.GetInstanceID()) { currentIndex = i; break; }
            }

            if (currentIndex < 0) { currentIndex = validObjects.Count; isInNullState = true; }

            int totalPos = validObjects.Count + 1;
            int nextIndex = (currentIndex + amount) % totalPos;
            if (nextIndex < 0) nextIndex += totalPos;

            int direction = Math.Sign(amount);
            bool isFull = validObjects.Count >= BagCapacityCalculator.GetUtilityMaxStock(bagController);
            if (isFull && nextIndex == validObjects.Count) { nextIndex = (direction > 0) ? 0 : validObjects.Count - 1; }

            API.DrifterBagAPI.SetIntendedSelectedIndex(bagController, nextIndex);
            if (!SeatValidator.ValidateSeatConfiguration(bagController, validObjects, mainPassenger, isInNullState, localSeatDict)) return;

            DrifterBossGrabPlugin._isSwappingPassengers = true;
            try
            {
                bool nextIsNull = (nextIndex == validObjects.Count);
                if (nextIsNull) SeatTransitionHandler.HandleNullStateTransition(bagController, vehicleSeat, mainPassenger!, localSeatDict, validObjects.Count);
                else if (isInNullState) SeatTransitionHandler.HandleNullToObjectTransition(bagController, vehicleSeat, validObjects[nextIndex], localSeatDict);
                else SeatTransitionHandler.HandleObjectSwap(bagController, vehicleSeat, validObjects[currentIndex], validObjects[nextIndex], localSeatDict, direction);

                API.DrifterBagAPI.SetAdditionalSeats(bagController, localSeatDict);
                UI.BagCarouselUpdater.UpdateCarousel(bagController, direction);
                UI.BagCarouselUpdater.UpdateNetworkBagState(bagController, direction);
            }
            finally { DrifterBossGrabPlugin._isSwappingPassengers = false; }

            if (nextIndex < validObjects.Count && validObjects[nextIndex] is { } target) API.DrifterBagAPI.RefreshUIOverlayForMainSeat(bagController, target);

            if (bagController.GetComponent<CharacterBody>()?.skillLocator is { } sl)
            {
                foreach (var esm in bagController.GetComponents<EntityStateMachine>())
                    if (esm.customName == "Bag" && esm.state is BaggedObject bo)
                    {
                        if (sl.utility) bo.TryOverrideUtility(sl.utility);
                        if (sl.primary) bo.TryOverridePrimary(sl.primary);
                    }
            }
            BagPassengerManager.ForceRecalculateMass(bagController);
        }
    }

    // ========================================================================================
    // SEAT TRANSITION HANDLER
    // ========================================================================================

    public static class SeatTransitionHandler
    {
        internal static void HandleNullStateTransition(DrifterBagController bagController, VehicleSeat vehicleSeat, GameObject current, ConcurrentDictionary<GameObject, VehicleSeat> localSeatDict, int validCount)
        {
            if (!SeatValidator.HasSpaceForNullStateTransition(bagController, validCount, localSeatDict) || !SeatValidator.ValidateNullStateTransition(bagController, current, localSeatDict)) return;

            if (current != null)
            {
                if (API.DrifterBagAPI.FindOrCreateBaggedObjectState(bagController, current) is { } state)
                {
                    var data = API.DrifterBagAPI.LoadObjectState(bagController, current) ?? new BaggedObjectStateData { targetObject = current };
                    data.CaptureBreakoutStateFromBaggedObject(state);
                    API.DrifterBagAPI.SaveObjectState(bagController, current, data);
                }
                BaggedObjectStatePatches.BaggedObject_OnExit.MarkPreserveOverridesDuringCycling(current);
                vehicleSeat.EjectPassenger(current);
                API.DrifterBagAPI.RemoveUIOverlay(current, bagController);
            }

            API.DrifterBagAPI.SetMainSeatObject(bagController, null);
            if (AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict, true) is { } newSeat && current != null)
            {
                newSeat.AssignPassenger(current);
                localSeatDict[current] = newSeat;
                if (NetworkServer.active && AdditionalSeatBreakoutTimer.CanBreakout(current) && !current.GetComponent<AdditionalSeatBreakoutTimer>())
                {
                    var timer = current.AddComponent<AdditionalSeatBreakoutTimer>();
                    timer.controller = bagController;
                    float mass = bagController.CalculateBaggedObjectMass(current);
                    timer.breakoutTime = Mathf.Max(10f - 0.005f * mass, 1f) * (PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.BreakoutTimeMultiplier.Value : 1f);
                    if (API.DrifterBagAPI.LoadObjectState(bagController, current) is { } s)
                    {
                        if (s.breakoutTime > 0) timer.breakoutTime = s.breakoutTime;
                        timer.SetElapsedBreakoutTime(s.elapsedBreakoutTime);
                        timer.breakoutAttempts = s.breakoutAttempts;
                    }
                }
            }
            API.DrifterBagAPI.RemoveUIOverlayForNullState(bagController);
        }

        internal static void HandleNullToObjectTransition(DrifterBagController bagController, VehicleSeat vehicleSeat, GameObject target, ConcurrentDictionary<GameObject, VehicleSeat> localSeatDict)
        {
            if (!target) return;
            VehicleSeat? source = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, target, localSeatDict);
            if (source != null)
            {
                if (target.GetComponent<AdditionalSeatBreakoutTimer>() is { } timer)
                {
                    var data = API.DrifterBagAPI.LoadObjectState(bagController, target) ?? new BaggedObjectStateData { targetObject = target };
                    data.CaptureFromAdditionalTimer(timer);
                    API.DrifterBagAPI.SaveObjectState(bagController, target, data);
                }
                source.EjectPassenger(target);
                localSeatDict.TryRemove(target, out _);
            }

            if (BagCapacityCalculator.GetCurrentBaggedCount(bagController) >= BagCapacityCalculator.GetUtilityMaxStock(bagController) && NetworkServer.active && source == null)
            {
                if (AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict, true) is { } ts) { ts.AssignPassenger(target); localSeatDict[target] = ts; return; }
            }

            API.DrifterBagAPI.SetMainSeatObject(bagController, target);
            bagController.AssignPassenger(target);
            if (API.DrifterBagAPI.LoadObjectState(bagController, target) is { } stored && API.DrifterBagAPI.FindOrCreateBaggedObjectState(bagController, target) is { } bs) stored.ApplyToBaggedObject(bs);
        }

        internal static void HandleObjectSwap(DrifterBagController bagController, VehicleSeat vehicleSeat, GameObject current, GameObject target, ConcurrentDictionary<GameObject, VehicleSeat> localSeatDict, int direction)
        {
            if (!target || !SeatValidator.ValidateSeatStateForSwap(bagController, current, target, localSeatDict)) return;

            bool server = NetworkServer.active;
            bool currentInMain = vehicleSeat.hasPassenger && vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == current.GetInstanceID();
            var targetAddSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, target);

            if (currentInMain)
            {
                if (API.DrifterBagAPI.FindOrCreateBaggedObjectState(bagController, current) is { } cs)
                {
                    var data = API.DrifterBagAPI.LoadObjectState(bagController, current) ?? new BaggedObjectStateData { targetObject = current };
                    data.CaptureBreakoutStateFromBaggedObject(cs);
                    API.DrifterBagAPI.SaveObjectState(bagController, current, data);
                }
                BaggedObjectStatePatches.BaggedObject_OnExit.MarkPreserveOverridesDuringCycling(current);
                vehicleSeat.EjectPassenger(current);
                API.DrifterBagAPI.RemoveUIOverlay(current, bagController);

                if (targetAddSeat)
                {
                    if (target.GetComponent<AdditionalSeatBreakoutTimer>() is { } t)
                    {
                        var d = API.DrifterBagAPI.LoadObjectState(bagController, target) ?? new BaggedObjectStateData { targetObject = target };
                        d.CaptureFromAdditionalTimer(t);
                        API.DrifterBagAPI.SaveObjectState(bagController, target, d);
                    }
                    targetAddSeat!.EjectPassenger(target);
                    localSeatDict.TryRemove(target, out _);
                    targetAddSeat!.AssignPassenger(current);
                    localSeatDict[current] = targetAddSeat!;

                    if (server && AdditionalSeatBreakoutTimer.CanBreakout(current) && !current.GetComponent<AdditionalSeatBreakoutTimer>())
                    {
                        var st = current.AddComponent<AdditionalSeatBreakoutTimer>();
                        st.controller = bagController;
                        float mass = bagController.CalculateBaggedObjectMass(current);
                        st.breakoutTime = Mathf.Max(10f - 0.005f * mass, 1f) * (PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.BreakoutTimeMultiplier.Value : 1f);
                        if (API.DrifterBagAPI.LoadObjectState(bagController, current) is { } s)
                        {
                            if (s.breakoutTime > 0) st.breakoutTime = s.breakoutTime;
                            st.SetElapsedBreakoutTime(s.elapsedBreakoutTime);
                            st.breakoutAttempts = s.breakoutAttempts;
                        }
                    }
                }
                else if (AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict, true) is { } ns) { ns.AssignPassenger(current); localSeatDict[current] = ns; }

                API.DrifterBagAPI.SetMainSeatObject(bagController, target);
                vehicleSeat.AssignPassenger(target);
                if (API.DrifterBagAPI.LoadObjectState(bagController, target) is { } stored && API.DrifterBagAPI.FindOrCreateBaggedObjectState(bagController, target) is { } bs) stored.ApplyToBaggedObject(bs);

                API.DrifterBagAPI.RefreshUIOverlayForMainSeat(bagController, target);
                API.DrifterBagAPI.SynchronizeBaggedObjectState(bagController, target);
                ResetBagStateMachine(bagController);
                UI.BagCarouselUpdater.UpdateCarousel(bagController, direction);
            }
            else // Client or non-server swap
            {
                if (targetAddSeat)
                {
                    if (target.GetComponent<AdditionalSeatBreakoutTimer>() is { } t)
                    {
                        var d = API.DrifterBagAPI.LoadObjectState(bagController, target) ?? new BaggedObjectStateData { targetObject = target };
                        d.CaptureFromAdditionalTimer(t);
                        API.DrifterBagAPI.SaveObjectState(bagController, target, d);
                    }
                    targetAddSeat!.EjectPassenger(target);
                    localSeatDict.TryRemove(target, out _);
                    if (current != null) { targetAddSeat!.AssignPassenger(current); localSeatDict[current] = targetAddSeat!; }
                }

                if (current != null && API.DrifterBagAPI.FindOrCreateBaggedObjectState(bagController, current) is { } cs)
                {
                    var data = API.DrifterBagAPI.LoadObjectState(bagController, current) ?? new BaggedObjectStateData { targetObject = current };
                    data.CaptureBreakoutStateFromBaggedObject(cs);
                    API.DrifterBagAPI.SaveObjectState(bagController, current, data);
                }

                API.DrifterBagAPI.SetMainSeatObject(bagController, target);
                if (target != null)
                {
                    API.DrifterBagAPI.GetAdditionalSeats(bagController)?.TryRemove(target, out _);
                    vehicleSeat.AssignPassenger(target);
                    if (API.DrifterBagAPI.LoadObjectState(bagController, target) is { } stored && API.DrifterBagAPI.FindOrCreateBaggedObjectState(bagController, target) is { } bs) stored.ApplyToBaggedObject(bs);
                    API.DrifterBagAPI.RefreshUIOverlayForMainSeat(bagController, target);
                    API.DrifterBagAPI.SynchronizeBaggedObjectState(bagController, target);
                    ResetBagStateMachine(bagController);
                }
                UI.BagCarouselUpdater.UpdateCarousel(bagController, direction);
            }
        }

        private static void ResetBagStateMachine(DrifterBagController controller)
        {
            if (EntityStateMachine.FindByCustomName(controller.gameObject, "Bag") is { } esm)
            {
                if (API.DrifterBagAPI.GetMainPassenger(controller) is { } main) esm.SetNextState(new BaggedObject { targetObject = main });
                else esm.SetNextStateToMain();
            }
        }
    }

    // ========================================================================================
    // SEAT VALIDATOR
    // ========================================================================================

    public static class SeatValidator
    {
        internal static bool ValidateSeatConfiguration(DrifterBagController ctrl, List<GameObject> valid, GameObject? main, bool isNull, ConcurrentDictionary<GameObject, VehicleSeat> dict)
        {
            if (!isNull && main == null) return false;
            if (isNull && valid.Count == 0) return false;
            foreach (var kvp in dict)
            {
                if (!kvp.Value || !kvp.Value.hasPassenger || !kvp.Value.NetworkpassengerBodyObject || kvp.Value.NetworkpassengerBodyObject.GetInstanceID() != kvp.Key.GetInstanceID()) return false;
            }
            return true;
        }

        internal static bool ValidateSeatStateForSwap(DrifterBagController ctrl, GameObject? current, GameObject? target, ConcurrentDictionary<GameObject, VehicleSeat> dict)
        {
            if (!target || !ctrl.vehicleSeat) return false;
            bool inMain = ctrl.vehicleSeat.hasPassenger && ctrl.vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == current?.GetInstanceID();
            if (!inMain && current != null && API.DrifterBagAPI.GetMainPassenger(ctrl)?.GetInstanceID() == current.GetInstanceID()) inMain = true;
            if (!inMain) return false;

            foreach (var kvp in dict)
            {
                if (kvp.Value && kvp.Value.hasPassenger && kvp.Value.NetworkpassengerBodyObject is { } p)
                {
                    if (current != null && p.GetInstanceID() == current.GetInstanceID() && kvp.Value != ctrl.vehicleSeat) return false;
                    if (p.GetInstanceID() == target!.GetInstanceID() && kvp.Value == ctrl.vehicleSeat) return false;
                }
            }
            return true;
        }

        internal static bool ValidateNullStateTransition(DrifterBagController ctrl, GameObject? current, ConcurrentDictionary<GameObject, VehicleSeat> dict)
        {
            if (!current || !ctrl.vehicleSeat || !ctrl.vehicleSeat.hasPassenger || ctrl.vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() != current!.GetInstanceID()) return false;
            if (AdditionalSeatManager.FindOrCreateEmptySeat(ctrl, ref dict) == null) return false;
            foreach (var kvp in dict) if (kvp.Value && kvp.Value.hasPassenger && kvp.Value.NetworkpassengerBodyObject && kvp.Value.NetworkpassengerBodyObject.GetInstanceID() == current!.GetInstanceID() && kvp.Value != ctrl.vehicleSeat) return false;
            return true;
        }

        internal static bool HasSpaceForNullStateTransition(DrifterBagController ctrl, int count, ConcurrentDictionary<GameObject, VehicleSeat> dict) => count < BagCapacityCalculator.GetUtilityMaxStock(ctrl);
    }

    // ========================================================================================
    // ADDITIONAL SEAT MANAGER
    // ========================================================================================

    public static class AdditionalSeatManager
    {
        public static void CopySeatProperties(VehicleSeat src, VehicleSeat dst)
        {
            if (!src || !dst) return;
            dst.seatPosition = src.seatPosition; dst.exitPosition = src.exitPosition; dst.ejectOnCollision = src.ejectOnCollision;
            dst.hidePassenger = src.hidePassenger; dst.exitVelocityFraction = src.exitVelocityFraction; dst.disablePassengerMotor = src.disablePassengerMotor;
            dst.isEquipmentActivationAllowed = src.isEquipmentActivationAllowed; dst.shouldProximityHighlight = src.shouldProximityHighlight;
            dst.disableInteraction = src.disableInteraction; dst.shouldSetIdle = src.shouldSetIdle; dst.additionalExitVelocity = src.additionalExitVelocity;
            dst.disableAllCollidersAndHurtboxes = src.disableAllCollidersAndHurtboxes; dst.disableColliders = src.disableColliders;
            dst.disableCharacterNetworkTransform = src.disableCharacterNetworkTransform; dst.ejectFromSeatOnMapEvent = src.ejectFromSeatOnMapEvent;
            dst.inheritRotation = src.inheritRotation; dst.holdPassengerAfterDeath = src.holdPassengerAfterDeath; dst.ejectPassengerToGround = src.ejectPassengerToGround;
            dst.ejectRayDistance = src.ejectRayDistance; dst.handleExitTeleport = src.handleExitTeleport; dst.setCharacterMotorPositionToCurrentPosition = src.setCharacterMotorPositionToCurrentPosition;
            dst.passengerState = src.passengerState;
        }

        public static VehicleSeat FindOrCreateEmptySeat(DrifterBagController ctrl, ref ConcurrentDictionary<GameObject, VehicleSeat> dict, bool ignoreCapacity = false)
        {
            foreach (var s in dict.Values) if (s && !s.hasPassenger) return s;
            foreach (var s in ctrl.GetComponentsInChildren<VehicleSeat>(true)) if (s != ctrl.vehicleSeat && !dict.Values.Contains(s) && !s.hasPassenger) return s;

            if (!ignoreCapacity && dict.Count >= BagCapacityCalculator.GetUtilityMaxStock(ctrl) - 1) return null!;
            if (!NetworkServer.active) return null!;

            var obj = (BagStateSync.AdditionalSeatPrefab != null) ? UnityEngine.Object.Instantiate(BagStateSync.AdditionalSeatPrefab) : new GameObject("AdditionalSeat_Empty_" + DateTime.Now.Ticks);
            obj.SetActive(true); obj.transform.SetParent(ctrl.transform); obj.transform.localPosition = Vector3.zero; obj.transform.localRotation = Quaternion.identity;
            var newSeat = obj.GetComponent<VehicleSeat>() ?? obj.AddComponent<VehicleSeat>();
            NetworkServer.Spawn(obj);
            CopySeatProperties(ctrl.vehicleSeat, newSeat);
            return newSeat;
        }

        public static VehicleSeat FindOrCreateEmptySeat(DrifterBagController ctrl)
        {
            var dict = API.DrifterBagAPI.GetAdditionalSeats(ctrl);
            return FindOrCreateEmptySeat(ctrl, ref dict);
        }

        internal static VehicleSeat? GetAdditionalSeatForObject(DrifterBagController ctrl, GameObject? obj, ConcurrentDictionary<GameObject, VehicleSeat> dict) => (obj && dict.TryGetValue(obj!, out var s)) ? s : null;
        internal static VehicleSeat? GetAdditionalSeatForObject(DrifterBagController ctrl, GameObject? obj) => GetAdditionalSeatForObject(ctrl, obj, API.DrifterBagAPI.GetAdditionalSeats(ctrl));
    }

    // ========================================================================================
    // ADDITIONAL SEAT BREAKOUT TIMER
    // ========================================================================================

    public class AdditionalSeatBreakoutTimer : MonoBehaviour
    {
        public DrifterBagController? controller;
        public float breakoutTime;
        public float breakoutAttempts;
        private bool _hasPlayedRustle = false;
        private float _breakoutTimer;
        private CharacterBody? _body;
        private SfxLocator? _sfxLocator;
        private static readonly Dictionary<GameObject, int> _wiggleLoops = new();
        private static readonly System.Reflection.MethodInfo? _playCrossfade = typeof(EntityStates.EntityState).GetMethod("PlayCrossfade", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new Type[] { typeof(string), typeof(string), typeof(string), typeof(float), typeof(float) }, null);

        public static bool CanBreakout(GameObject obj) => obj && obj.GetComponent<CharacterBody>() is { } b && !b.isPlayerControlled && b.master && b.healthComponent && b.healthComponent.alive;

        public float GetElapsedBreakoutTime() => _breakoutTimer;
        public void SetElapsedBreakoutTime(float t) => _breakoutTimer = t;

        private void FixedUpdate()
        {
            if (controller == null || !gameObject || !NetworkServer.active) return;
            if (!_hasPlayedRustle) { _hasPlayedRustle = true; PlayBagAnimation("Bag, Rumble", "Rustle", "Rumble.playbackRate", 1f, 0.1f); AddWiggleLoop(controller!.gameObject); }

            _body ??= GetComponent<CharacterBody>();
            if (_body == null || (_body.healthComponent && !_body.healthComponent.alive) || BagHelpers.GetAdditionalSeat(controller, gameObject) == null) { Destroy(this); return; }

            _breakoutTimer += Time.fixedDeltaTime;
            if (breakoutTime <= 0)
            {
                breakoutTime = Mathf.Max(10f - 0.005f * controller!.CalculateBaggedObjectMass(gameObject), 1f) * (PluginConfig.Instance.EnableBalance.Value ? PluginConfig.Instance.BreakoutTimeMultiplier.Value : 1f);
                if (API.DrifterBagAPI.FindStateForObject(gameObject) is { } s) breakoutAttempts = s.breakoutAttempts;
            }

            if (_breakoutTimer >= breakoutTime * 0.5f && gameObject.TryGetComponent<SpecialObjectAttributes>(out var soa) && soa.breakoutState.stateType != null)
            {
                SpecialObjectAttributes.ForceBreakout(gameObject);
                BagPassengerManager.RemoveBaggedObject(controller, gameObject, false);
                Destroy(this); return;
            }

            if (_breakoutTimer >= breakoutTime)
            {
                _breakoutTimer -= breakoutTime; breakoutTime *= 0.65f; breakoutAttempts++;
                _sfxLocator ??= GetComponent<SfxLocator>();
                if (_sfxLocator?.barkSound != null) Util.PlaySound(_sfxLocator.barkSound!, gameObject);

                if (!DrifterBagController.bagDisableBreakout && UnityEngine.Random.Range(0, 3) == 0) { Breakout(); BagPassengerManager.RemoveBaggedObject(controller, gameObject, false); return; }
                PlayBagAnimation("Bag, Rumble", "BagBurst", "Rumble.playbackRate", 0.5f, 0.1f);
            }
        }

        private void OnDestroy() { if (_hasPlayedRustle && controller) { RemoveWiggleLoop(controller!.gameObject); PlayBagAnimation("Bag, Rumble", "Empty", "Rumble.playbackRate", 1f, 0.1f); } }

        private void AddWiggleLoop(GameObject drifter) { if (!drifter) return; int c = _wiggleLoops.GetValueOrDefault(drifter, 0); if (c == 0) Util.PlaySound("Play_drifter_repossess_bagWiggle_Loop", drifter); _wiggleLoops[drifter] = c + 1; }
        private void RemoveWiggleLoop(GameObject drifter) { if (drifter && _wiggleLoops.TryGetValue(drifter, out int c)) { c--; if (c <= 0) { Util.PlaySound("Stop_drifter_repossess_bagWiggle_Loop", drifter); c = 0; } _wiggleLoops[drifter] = c; } }

        private void PlayBagAnimation(string layer, string state, string rate, float dur, float xfade)
        {
            if (controller && EntityStateMachine.FindByCustomName(controller!.gameObject, "Bag") is { state: { } s } && _playCrossfade != null) _playCrossfade.Invoke(s, new object[] { layer, state, rate, dur, xfade });
        }

        private void Breakout()
        {
            if (!gameObject || !controller) return;
            var body = gameObject.GetComponent<CharacterBody>();
            if (!body || !body.healthComponent.alive) return;

            Vector3 fwd = Vector3.up;
            if (body.characterDirection) fwd = Quaternion.AngleAxis((UnityEngine.Random.value < 0.5f) ? 45f : -45f, -body.characterDirection.forward) * Vector3.up;
            float speed = Mathf.Max(10f, 30f * controller!.CalculateBaggedObjectMass(gameObject) / DrifterBossGrabMod.Balance.CapacityScalingSystem.CalculateMassCapacity(controller!));
            if (PluginConfig.Instance.EnableBalance.Value && !PluginConfig.Instance.IsMaxLaunchSpeedInfinite) speed = Mathf.Min(speed, PluginConfig.Instance.ParsedMaxLaunchSpeed);

            var prefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/ThrownObjectProjectileNoStun.prefab").WaitForCompletion();
            FireProjectileInfo info = new FireProjectileInfo { projectilePrefab = prefab, position = body.transform.position, rotation = Util.QuaternionSafeLookRotation(fwd), owner = controller!.gameObject, speedOverride = speed, force = 20f };
            if (ProjectileManager.instance.FireProjectileImmediateServer(info, null, 0, 0.0)?.GetComponent<ThrownObjectProjectileController>() is { } tc) tc.SetPassengerServer(gameObject);
        }
    }

    // ========================================================================================
    // ANIMATION PATCHES
    // ========================================================================================

    [HarmonyPatch]
    public static class AnimationPatches
    {
        [HarmonyPatch(typeof(EntityStates.EntityState), "PlayCrossfade", new Type[] { typeof(string), typeof(string), typeof(string), typeof(float), typeof(float) })]
        [HarmonyPrefix]
        public static bool PlayCrossfade_Prefix(EntityStates.EntityState __instance, string layerName, string animationStateName)
        {
            if (!PluginConfig.Instance.PlayAnimationOnCycle.Value && (__instance is BaggedObject) && (DrifterBossGrabPlugin.IsSwappingPassengers || (!NetworkServer.active && Time.time - DrifterBossGrabPlugin.LastCycleClientTime < 0.3f))) return false;
            return true;
        }

        [HarmonyPatch(typeof(EntityStates.EntityState), "PlayCrossfade", new Type[] { typeof(string), typeof(string), typeof(float) })]
        [HarmonyPrefix]
        public static bool PlayCrossfadeShort_Prefix(EntityStates.EntityState __instance, string layerName, string animationStateName)
        {
            if (!PluginConfig.Instance.PlayAnimationOnCycle.Value && (__instance is BaggedObject) && (DrifterBossGrabPlugin.IsSwappingPassengers || (!NetworkServer.active && Time.time - DrifterBossGrabPlugin.LastCycleClientTime < 0.3f))) return false;
            return true;
        }
    }
}
