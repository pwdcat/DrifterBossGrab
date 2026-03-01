using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using RoR2;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    // Handles seat transitions during passenger cycling.
    public static class SeatTransitionHandler
    {
        // Handles transition from an object to null state.
        internal static void HandleNullStateTransition(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject actualMainPassenger, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict, int validObjectCount)
        {
            if (!SeatValidator.HasSpaceForNullStateTransition(bagController, validObjectCount, localSeatDict))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[HandleNullStateTransition] No space for null transition, aborting.");
                return;
            }

            if (!SeatValidator.ValidateNullStateTransition(bagController, actualMainPassenger, localSeatDict))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[HandleNullStateTransition] ValidateNullStateTransition failed, aborting.");
                return;
            }
            var seatForCurrent = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict);
            
            // Save main seat timer state before ejecting from main seat
            if (actualMainPassenger != null)
            {
                var currentState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, actualMainPassenger);
                if (currentState != null)
                {
                    var stateData = new Core.BaggedObjectStateData();
                    stateData.CaptureFromBaggedObject(currentState);
                    BaggedObjectPatches.SaveObjectState(bagController, actualMainPassenger, stateData);
                    
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[DEBUG] [HandleNullStateTransition] Saved state for {actualMainPassenger.name} before cycling away to null (server-side)");
                }
            }

            vehicleSeat.EjectPassenger(actualMainPassenger);
            if (actualMainPassenger != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[DEBUG] [HandleNullStateTransition] Ejected passenger {actualMainPassenger.name} from main seat");
                BagPatches.SetMainSeatObject(bagController, null);
                BaggedObjectPatches.RemoveUIOverlay(actualMainPassenger, bagController);
            }
            BagPatches.SetMainSeatObject(bagController, null);
            if (seatForCurrent != null && actualMainPassenger != null)
            {
                seatForCurrent.AssignPassenger(actualMainPassenger);
                localSeatDict[actualMainPassenger] = seatForCurrent;
                
                // Create AdditionalSeatBreakoutTimer when moving object back to additional seat
                if (UnityEngine.Networking.NetworkServer.active && !actualMainPassenger.GetComponent<AdditionalSeatBreakoutTimer>())
                {
                    var timer = actualMainPassenger.AddComponent<AdditionalSeatBreakoutTimer>();
                    timer.controller = bagController;
                    
                    // Calculate breakout time like vanilla
                    float mass = bagController.CalculateBaggedObjectMass(actualMainPassenger);
                    float baseBreakoutTime = 10f;
                    float breakoutMultiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                    float finalTime = Mathf.Max(baseBreakoutTime - 0.005f * mass, 1f);
                    var hc = actualMainPassenger.GetComponent<CharacterBody>();
                    if (hc && hc.isElite) finalTime *= 0.8f;
                    timer.breakoutTime = finalTime * breakoutMultiplier;
                    
                    // Restore previous timer state if available
                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, actualMainPassenger);
                    if (storedState != null)
                    {
                        if (storedState.breakoutTime > 0f) timer.breakoutTime = storedState.breakoutTime;
                        timer.SetElapsedBreakoutTime(storedState.elapsedBreakoutTime);
                        timer.breakoutAttempts = storedState.breakoutAttempts;
                        
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[DEBUG] [HandleNullStateTransition] Created & restored AdditionalSeatBreakoutTimer for {actualMainPassenger.name}: age={storedState.elapsedBreakoutTime}, attempts={storedState.breakoutAttempts}, breakoutTime={timer.breakoutTime}");
                    }
                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[DEBUG] [HandleNullStateTransition] Created fresh AdditionalSeatBreakoutTimer for {actualMainPassenger.name}: breakoutTime={timer.breakoutTime}");
                    }
                }
            }
            if (actualMainPassenger != null)
            {
                BagPatches.SetMainSeatObject(bagController, null);
            }
            BaggedObjectPatches.RemoveUIOverlayForNullState(bagController);
        }

        // Handles transition from null state to an object.
        internal static void HandleNullToObjectTransition(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict)
        {
             if (targetObject == null) return;

            var sourceAdditionalSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, targetObject, localSeatDict);
            if (sourceAdditionalSeat != null)
            {
                // Capture timer state before ejecting from additional seat
                var timer = targetObject.GetComponent<AdditionalSeatBreakoutTimer>();
                if (timer != null)
                {
                    var timerState = BaggedObjectPatches.LoadObjectState(bagController, targetObject) ?? new Core.BaggedObjectStateData();
                    if (timerState.targetObject == null) // Was a blank generic state
                    {
                        timerState.CalculateFromObject(targetObject, bagController);
                    }
                    timerState.CaptureFromAdditionalTimer(timer);
                    BaggedObjectPatches.SaveObjectState(bagController, targetObject, timerState);
                }

                sourceAdditionalSeat.EjectPassenger(targetObject);
                System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
            }

            // Save state before assigning passenger
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[HandleNullToObjectTransition] Saving state before AssignPassenger (null -> {targetObject.name})");

            bagController.AssignPassenger(targetObject);
            BagPatches.SetMainSeatObject(bagController, targetObject);

            // Restore target object's state after cycling to it
            if (targetObject != null)
            {
                var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                if (storedState != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[DEBUG] [HandleNullToObjectTransition] Restoring stored state for {targetObject.name}");
                    var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, targetObject);
                    if (baggedState != null)
                    {
                        storedState.ApplyToBaggedObject(baggedState);
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[DEBUG] [HandleNullToObjectTransition] No stored state found for {targetObject.name}, using fresh state");
                }
            }
        }

        // Handles swapping between two objects.
        internal static void HandleObjectSwap(DrifterBagController bagController, RoR2.VehicleSeat vehicleSeat, GameObject currentObject, GameObject targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict, int direction)
        {
             if (targetObject == null) return;

            if (!SeatValidator.ValidateSeatStateForSwap(bagController, currentObject, targetObject, localSeatDict))
            {
                return;
            }

            // Check if current is physically in seat (server-side).
            bool currentIsPhysicallyInSeat = vehicleSeat.hasPassenger &&
                vehicleSeat.NetworkpassengerBodyObject != null &&
                vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == currentObject.GetInstanceID();

            var targetAdditionalSeat = AdditionalSeatManager.GetAdditionalSeatForObject(bagController, targetObject);

            if (currentIsPhysicallyInSeat)
            {
                // Save current main seat state before ejecting.
                if (currentObject != null)
                {
                    var currentState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, currentObject);
                    if (currentState != null)
                    {
                        var stateData = new Core.BaggedObjectStateData();
                        stateData.CaptureFromBaggedObject(currentState);
                        BaggedObjectPatches.SaveObjectState(bagController, currentObject, stateData);
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[DEBUG] [HandleObjectSwap] Saved state for {currentObject.name} before cycling away (server-side)");
                    }
                }

                // Server-side swap.
                vehicleSeat.EjectPassenger(currentObject);
                if (currentObject != null)
                {
                    BagPatches.SetMainSeatObject(bagController, null);
                    BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                }
                if (targetAdditionalSeat != null)
                {
                    // Capture timer state before ejecting from additional seat
                    var timer = targetObject.GetComponent<AdditionalSeatBreakoutTimer>();
                    if (timer != null)
                    {
                        var timerState = BaggedObjectPatches.LoadObjectState(bagController, targetObject) ?? new Core.BaggedObjectStateData();
                        if (timerState.targetObject == null) // Was a blank generic state
                        {
                            timerState.CalculateFromObject(targetObject, bagController);
                        }
                        timerState.CaptureFromAdditionalTimer(timer);
                        BaggedObjectPatches.SaveObjectState(bagController, targetObject, timerState);
                    }

                    targetAdditionalSeat.EjectPassenger(targetObject);
                    System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
                    targetAdditionalSeat.AssignPassenger(currentObject);
                    BagPatches.SetMainSeatObject(bagController, null);
                    if (currentObject != null)
                    {
                        BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                    }
                    if (currentObject != null) localSeatDict[currentObject] = targetAdditionalSeat;
                    
                    // Create AdditionalSeatBreakoutTimer when moving current object to additional seat
                    if (currentObject != null && UnityEngine.Networking.NetworkServer.active && !currentObject.GetComponent<AdditionalSeatBreakoutTimer>())
                    {
                        var swapTimer = currentObject.AddComponent<AdditionalSeatBreakoutTimer>();
                        swapTimer.controller = bagController;
                        float mass = bagController.CalculateBaggedObjectMass(currentObject);
                        float baseTime = 10f;
                        float multiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                        float ft = Mathf.Max(baseTime - 0.005f * mass, 1f);
                        var cb = currentObject.GetComponent<CharacterBody>();
                        if (cb && cb.isElite) ft *= 0.8f;
                        swapTimer.breakoutTime = ft * multiplier;
                        var ss = BaggedObjectPatches.LoadObjectState(bagController, currentObject);
                        if (ss != null)
                        {
                            if (ss.breakoutTime > 0f) swapTimer.breakoutTime = ss.breakoutTime;
                            swapTimer.SetElapsedBreakoutTime(ss.elapsedBreakoutTime);
                            swapTimer.breakoutAttempts = ss.breakoutAttempts;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Info($"[DEBUG] [HandleObjectSwap] Created & restored timer for {currentObject.name}: age={ss.elapsedBreakoutTime}, breakoutTime={swapTimer.breakoutTime}");
                        }
                    }
                }
                if (targetAdditionalSeat == null)
                {
                    var newSeat = AdditionalSeatManager.FindOrCreateEmptySeat(bagController, ref localSeatDict);
                    if (newSeat != null && currentObject != null)
                    {
                        newSeat.AssignPassenger(currentObject);
                        localSeatDict[currentObject] = newSeat;
                    }
                }

                vehicleSeat.AssignPassenger(targetObject);
                BagPatches.SetMainSeatObject(bagController, targetObject);

                // Restore target state.
                if (targetObject != null)
                {
                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                    if (storedState != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[DEBUG] [HandleObjectSwap] Restoring stored state for {targetObject.name} (server-side)");
                        var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, targetObject);
                        if (baggedState != null)
                        {
                            storedState.ApplyToBaggedObject(baggedState);
                        }
                    }

                }

                BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                BagCarouselUpdater.UpdateCarousel(bagController, direction);
            }
            else
            {
                // Client/Message swap.
                if (targetAdditionalSeat != null)
                {
                    // Capture timer state BEFORE ejecting from additional seat
                    var timer = targetObject.GetComponent<AdditionalSeatBreakoutTimer>();
                    if (timer != null)
                    {
                        var timerState = BaggedObjectPatches.LoadObjectState(bagController, targetObject) ?? new Core.BaggedObjectStateData();
                        if (timerState.targetObject == null) // Was a blank generic state
                        {
                            timerState.CalculateFromObject(targetObject, bagController);
                        }
                        timerState.CaptureFromAdditionalTimer(timer);
                        BaggedObjectPatches.SaveObjectState(bagController, targetObject, timerState);
                    }

                    targetAdditionalSeat.EjectPassenger(targetObject);
                    System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
                    if (currentObject != null)
                    {
                        targetAdditionalSeat.AssignPassenger(currentObject);
                        localSeatDict[currentObject] = targetAdditionalSeat;
                        
                        // Create AdditionalSeatBreakoutTimer for client-side swap
                        if (UnityEngine.Networking.NetworkServer.active && !currentObject.GetComponent<AdditionalSeatBreakoutTimer>())
                        {
                            var swapTimer = currentObject.AddComponent<AdditionalSeatBreakoutTimer>();
                            swapTimer.controller = bagController;
                            float mass = bagController.CalculateBaggedObjectMass(currentObject);
                            float baseTime = 10f;
                            float multiplier = PluginConfig.Instance.BreakoutTimeMultiplier.Value;
                            float ft = Mathf.Max(baseTime - 0.005f * mass, 1f);
                            var cb = currentObject.GetComponent<CharacterBody>();
                            if (cb && cb.isElite) ft *= 0.8f;
                            swapTimer.breakoutTime = ft * multiplier;
                            var ss = BaggedObjectPatches.LoadObjectState(bagController, currentObject);
                            if (ss != null)
                            {
                                if (ss.breakoutTime > 0f) swapTimer.breakoutTime = ss.breakoutTime;
                                swapTimer.SetElapsedBreakoutTime(ss.elapsedBreakoutTime);
                                swapTimer.breakoutAttempts = ss.breakoutAttempts;
                            }
                        }
                    }
                }

                // Save current object's state before cycling away
                if (currentObject != null)
                {
                    var currentState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, currentObject);
                    if (currentState != null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[DEBUG] [HandleObjectSwap] Saved state for {currentObject.name} before cycling away (client-side)");
                        var stateData = new Core.BaggedObjectStateData();
                        stateData.CaptureFromBaggedObject(currentState);
                        BaggedObjectPatches.SaveObjectState(bagController, currentObject, stateData);

                    }
                }

                BagPatches.SetMainSeatObject(bagController, targetObject);
                if (targetObject != null)
                {
                    // Ensure object is removed from additional seats dict immediately.
                    var realDict = BagPatches.GetState(bagController).AdditionalSeats;
                    if (realDict != null)
                    {
                        realDict.TryRemove(targetObject, out _);
                    }

                    vehicleSeat.AssignPassenger(targetObject);

                    // Restore target state.
                    var storedState = BaggedObjectPatches.LoadObjectState(bagController, targetObject);
                    if (storedState != null)
                    {

                        var baggedState = BaggedObjectPatches.FindOrCreateBaggedObjectState(bagController, targetObject);
                        if (baggedState != null)
                        {
                            storedState.ApplyToBaggedObject(baggedState);
                        }
                    }

                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                    BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                }
                BagCarouselUpdater.UpdateCarousel(bagController, direction);
            }
        }
    }
}
