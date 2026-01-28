using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
using EntityStates.Drifter.Bag;
namespace DrifterBossGrabMod.Patches
{
    public static class BottomlessBagPatches
    {
        private static float _lastCycleTime = 0f;
        private static float _scrollAccumulator = 0f;
        private const float SCROLL_THRESHOLD = 0.1f; // Cumulative delta required to trigger one scroll event
        public static void HandleInput()
        {
            if (PluginConfig.Instance.BottomlessBagEnabled.Value)
            {

                var localUser = LocalUserManager.GetFirstLocalUser();
                if (localUser != null && localUser.cachedBody != null)
                {
                    bool isBlockingState = false;
                    var stateMachines = localUser.cachedBody.GetComponents<EntityStateMachine>();
                    foreach (var stateMachine in stateMachines)
                    {
                        if (stateMachine != null)
                        {
                            if (stateMachine.state is EntityStates.Drifter.Repossess || stateMachine.state is EntityStates.Drifter.RepossessExit)
                            {
                                isBlockingState = true;
                            }
                        }
                    }

                    if (isBlockingState) return;
                }
                int cycleAmount = 0;
                
                // 1. Handle Mouse Wheel with Thresholding
                if (PluginConfig.Instance.EnableMouseWheelScrolling.Value)
                {
                    float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
                    if (scrollDelta != 0f)
                    {
                        if (_scrollAccumulator != 0f && Mathf.Sign(scrollDelta) != Mathf.Sign(_scrollAccumulator))
                        {
                            _scrollAccumulator = 0f;
                        }
                        _scrollAccumulator += scrollDelta;
                    }
                    else
                    {
                        _scrollAccumulator = Mathf.MoveTowards(_scrollAccumulator, 0f, Time.deltaTime * 0.5f);
                    }

                    if (Mathf.Abs(_scrollAccumulator) >= SCROLL_THRESHOLD && Time.time >= _lastCycleTime + PluginConfig.Instance.CycleCooldown.Value)
                    {
                        // Trigger only one cycle per cooldown period to prevent skipping items
                        bool isMovingForward = _scrollAccumulator > 0f;
                        bool up;
                        if (isMovingForward)
                        {
                            if (PluginConfig.Instance.InverseMouseWheelScrolling.Value) up = true; // scrollUp (inverted from new default)
                            else up = false; // scrollDown (new default)
                        }
                        else
                        {
                            if (PluginConfig.Instance.InverseMouseWheelScrolling.Value) up = false; // scrollDown (inverted from new default)
                            else up = true; // scrollUp (new default)
                        }
                        
                        cycleAmount = up ? 1 : -1;
                        _scrollAccumulator -= Mathf.Sign(_scrollAccumulator) * SCROLL_THRESHOLD;
                        _lastCycleTime = Time.time;
                    }
                }


                if (Time.time >= _lastCycleTime + PluginConfig.Instance.CycleCooldown.Value)
                {
                    if (PluginConfig.Instance.ScrollUpKeybind.Value.MainKey != KeyCode.None && Input.GetKeyDown(PluginConfig.Instance.ScrollUpKeybind.Value.MainKey))
                    {
                        bool modifiersPressed = true;
                        foreach (var modifier in PluginConfig.Instance.ScrollUpKeybind.Value.Modifiers)
                        {
                            if (!Input.GetKey(modifier)) { modifiersPressed = false; break; }
                        }
                        if (modifiersPressed) { cycleAmount++; _lastCycleTime = Time.time; }
                    }
                    if (PluginConfig.Instance.ScrollDownKeybind.Value.MainKey != KeyCode.None && Input.GetKeyDown(PluginConfig.Instance.ScrollDownKeybind.Value.MainKey))
                    {
                        bool modifiersPressed = true;
                        foreach (var modifier in PluginConfig.Instance.ScrollDownKeybind.Value.Modifiers)
                        {
                            if (!Input.GetKey(modifier)) { modifiersPressed = false; break; }
                        }
                        if (modifiersPressed) { cycleAmount--; _lastCycleTime = Time.time; }
                    }
                }

                // 3. Execute Cycle
                if (cycleAmount != 0)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value) 
                        Log.Info($"[HandleInput] Triggering CyclePassengers. amount: {cycleAmount}");
                    
                    CyclePassengers(cycleAmount);
                }
            }
        }
        public static void CyclePassengers(DrifterBagController bagController, int amount)
        {
            if (bagController == null || amount == 0) return;

            // Prevent scrolling if capacity is 1 or less
            if (BagPatches.GetUtilityMaxStock(bagController) <= 1) return;
    
            // If we are on client and have authority, send a message to server
            if (!NetworkServer.active && bagController.hasAuthority)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) 
                    Log.Info($"[CyclePassengers] Client has authority, sending cycle request via network message. amount: {amount}");
                
                Networking.CycleNetworkHandler.SendCycleRequest(bagController, amount);
                return;
            }
    
            // If we are the server, perform the cycle directly
            if (NetworkServer.active)
            {
                ServerCyclePassengers(bagController, amount);
            }
        }


        public static void ServerCyclePassengers(DrifterBagController bagController, int amount)
        {
            if (!NetworkServer.active || amount == 0) return; // Safety guard
            
            if (bagController.vehicleSeat == null)
            {
                Log.Info($" [BottomlessBag] ERROR: vehicleSeat is null!");
                return;
            }
            
            List<GameObject> baggedObjects;
            if (!BagPatches.baggedObjectsDict.TryGetValue(bagController, out baggedObjects) || baggedObjects.Count == 0)
            {
                // Try network controller as fallback for client grabs
                var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                if (netController != null)
                {
                    baggedObjects = netController.GetBaggedObjects();
                }
            }

            if (baggedObjects == null || baggedObjects.Count == 0)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[ServerCyclePassengers] No bagged objects found for {bagController.name}. Correcting client state (Empty).");
                

                var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
                if (netController != null)
                {
                    // Clear any lingering NetIDs in the controller to ensure consistency
                    netController.SetBagState(-1, new List<GameObject>(), new List<GameObject>());
                }
                return;
            }
            
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[ServerCyclePassengers] Found {baggedObjects.Count} bagged objects for {bagController.name}");
            
            var seenInstanceIds = new HashSet<int>();
            var validObjects = new List<GameObject>();
            var allObjectsInScene = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var potentialRegrabObjects = new List<GameObject>();
            foreach (var sceneObj in allObjectsInScene)
            {
                if (sceneObj != null && PluginConfig.IsGrabbable(sceneObj))
                {
                    bool wasPreviouslyTracked = false;
                    foreach (var trackedObj in baggedObjects)
                    {
                        if (trackedObj != null && trackedObj.GetInstanceID() == sceneObj.GetInstanceID())
                        {
                            wasPreviouslyTracked = true;
                            break;
                        }
                    }
                    if (wasPreviouslyTracked && !OtherPatches.IsInProjectileState(sceneObj))
                    {
                        potentialRegrabObjects.Add(sceneObj);
                    }
                }
            }
            foreach (var obj in baggedObjects)
            {
                if (obj == null)
                {
                    continue;
                }
                bool isInProjectileState = OtherPatches.IsInProjectileState(obj);
                if (isInProjectileState)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[ServerCyclePassengers] Excluding {obj.name} (Projectile State)");
                    continue;
                }
                int instanceId = obj.GetInstanceID();
                if (!seenInstanceIds.Contains(instanceId))
                {
                    seenInstanceIds.Add(instanceId);
                    validObjects.Add(obj);
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                     Log.Info($"[ServerCyclePassengers] Excluding {obj.name} (Duplicate InstanceID)");
                }
            }
            foreach (var regrabObj in potentialRegrabObjects)
            {
                int instanceId = regrabObj.GetInstanceID();
                if (!seenInstanceIds.Contains(instanceId))
                {
                    seenInstanceIds.Add(instanceId);
                    validObjects.Add(regrabObj);
                }
            }
            if (validObjects.Count == 0)
            {
                 if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[ServerCyclePassengers] validObjects count is 0 after filtering!");
                return;
            }
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ServerCyclePassengers] Valid objects count: {validObjects.Count}. Total bagged: {baggedObjects.Count}");
                foreach(var obj in validObjects)
                {
                     Log.Info($"  - Valid: {obj.name} (ID: {obj.GetInstanceID()})");
                }
            }
            CycleToNextObject(bagController, validObjects, amount);
        }

        private static void CyclePassengers(int amount)
        {
            if (amount == 0) return;
            var bagControllers = UnityEngine.Object.FindObjectsByType<DrifterBagController>(FindObjectsSortMode.None);
            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CyclePassengers] Found {bagControllers.Length} bag controllers in scene.");
            foreach (var bagController in bagControllers)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[CyclePassengers] Checking controller: {bagController.name}, isAuthority: {bagController.isAuthority}, hasAuthority: {bagController.hasAuthority}");
                }
                if (!bagController.isAuthority)
                {
                    continue;
                }
                
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CyclePassengers] Authority found for {bagController.name}, calling overload.");
                CyclePassengers(bagController, amount);
                break;
            }
        }
        private static void CycleToNextObject(DrifterBagController bagController, List<GameObject> validObjects, int amount)
        {
            // Use a local copy of the seatDict for atomic updates
            ConcurrentDictionary<GameObject, RoR2.VehicleSeat> localSeatDict;
            if (!BagPatches.additionalSeatsDict.TryGetValue(bagController, out var existingSeatDict))
            {
                localSeatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>();
            }
            else
            {
                localSeatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>(existingSeatDict);
            }

            var vehicleSeat = bagController.vehicleSeat;
            GameObject? mainPassenger = BagPatches.GetMainSeatObject(bagController);
            if (mainPassenger == null && vehicleSeat.hasPassenger)
            {
                GameObject? seatPassenger = null;
                if (vehicleSeat.hasPassenger)
                {
                    seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                }
                if (seatPassenger != null)
                {
                    bool shouldTrack = false;
                    foreach (var obj in validObjects)
                    {
                        if (obj != null && obj.GetInstanceID() == seatPassenger.GetInstanceID())
                        {
                            shouldTrack = true;
                            break;
                        }
                    }
                    if (shouldTrack)
                    {
                        if (!BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list))
                        {
                            list = new List<GameObject>();
                            BagPatches.baggedObjectsDict[bagController] = list;
                        }
                        int passengerInstanceId = seatPassenger.GetInstanceID();
                        bool alreadyTracked = false;
                        foreach (var existingObj in list)
                        {
                            if (existingObj != null && existingObj.GetInstanceID() == passengerInstanceId)
                            {
                                alreadyTracked = true;
                                break;
                            }
                        }
                        if (!alreadyTracked)
                        {
                            list.Add(seatPassenger);
                        }
                        BagPatches.SetMainSeatObject(bagController, seatPassenger);
                        BagPatches.UpdateCarousel(bagController, 0);
                        mainPassenger = seatPassenger;
                    }
                }
            }
            if (mainPassenger == null && vehicleSeat.hasPassenger)
            {
                var seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                bool shouldTrack = false;
                foreach (var obj in validObjects)
                {
                    if (obj != null && obj.GetInstanceID() == seatPassenger.GetInstanceID())
                    {
                        shouldTrack = true;
                        break;
                    }
                }
                if (shouldTrack)
                {
                    BagPatches.SetMainSeatObject(bagController, seatPassenger);
                    BagPatches.UpdateCarousel(bagController, 0);
                    mainPassenger = seatPassenger;
                }
            }
            if (mainPassenger != null)
            {
                bool isActuallyInMainSeat = false;
                bool isActuallyInAdditionalSeat = false;
                if (vehicleSeat.hasPassenger)
                {
                    if (vehicleSeat.hasPassenger && vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == mainPassenger.GetInstanceID())
                    {
                        isActuallyInMainSeat = true;
                    }
                }
                if (localSeatDict.Count > 0)
                {
                    foreach (var kvp in localSeatDict)
                    {
                        if (kvp.Value != null && kvp.Value.hasPassenger)
                        {
                            if (kvp.Value.NetworkpassengerBodyObject.GetInstanceID() == mainPassenger.GetInstanceID())
                            {
                                isActuallyInAdditionalSeat = true;
                                break;
                            }
                        }
                    }
                }
                if (!isActuallyInMainSeat && isActuallyInAdditionalSeat)
                {
                    BagPatches.SetMainSeatObject(bagController, null);
                    mainPassenger = null;
                }
            }
            if (mainPassenger != null)
            {
                bool mainPassengerStillValid = false;
                int mainPassengerInstanceId = mainPassenger.GetInstanceID();
                foreach (var obj in validObjects)
                {
                    if (obj != null && obj.GetInstanceID() == mainPassengerInstanceId)
                    {
                        mainPassengerStillValid = true;
                        break;
                    }
                }
                if (!mainPassengerStillValid && OtherPatches.IsInProjectileState(mainPassenger))
                {
                    mainPassengerStillValid = false;
                }
                if (!mainPassengerStillValid)
                {
                    BagPatches.SetMainSeatObject(bagController, null);
                    BagPatches.UpdateCarousel(bagController, 0);
                    mainPassenger = null;
                }
            }
            GameObject? actualMainPassenger = null;
            int actualMainPassengerInstanceId = mainPassenger?.GetInstanceID() ?? 0;
            foreach (var obj in validObjects)
            {
                if (obj != null && obj.GetInstanceID() == actualMainPassengerInstanceId && actualMainPassengerInstanceId != 0)
                {
                    actualMainPassenger = obj;
                    break;
                }
            }
            if (actualMainPassenger == null && mainPassenger != null)
            {
                if (vehicleSeat.hasPassenger)
                {
                    GameObject? seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                    if (seatPassenger != null && seatPassenger.GetInstanceID() == actualMainPassengerInstanceId)
                    {
                        actualMainPassenger = mainPassenger;
                    }
                }

                if (actualMainPassenger == null)
                {
                    // Check if mainPassenger is in validObjects
                    bool isInValidObjects = false;
                    int mpInstanceId = mainPassenger.GetInstanceID();
                    foreach (var obj in validObjects)
                    {
                        if (obj != null && obj.GetInstanceID() == mpInstanceId)
                        {
                            isInValidObjects = true;
                            actualMainPassenger = obj;
                            break;
                        }
                    }
                    
                    if (!isInValidObjects)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[CycleToNextObject] mainPassenger {mainPassenger.name} not in validObjects and not in seat, returning early");
                        return;
                    }

                }
            }
            int emptySeatsCount = 0;
            foreach (var kvp in localSeatDict)
            {
                if (kvp.Value != null && !kvp.Value.hasPassenger)
                {
                    emptySeatsCount++;
                }
            }
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == bagController.vehicleSeat) continue;
                bool isTracked = localSeatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    emptySeatsCount++;
                }
            }

            var netController = bagController.GetComponent<Networking.BottomlessBagNetworkController>();
            bool isInNullState = false;
            
            if (netController != null)
            {
                if (netController.selectedIndex == -1)
                {
                    actualMainPassenger = null;
                    isInNullState = true;
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[CycleToNextObject] Logical state: Null (Empty Hands)");
                }
                else
                {
                    var logicalObjects = netController.GetBaggedObjects();
                    if (netController.selectedIndex >= 0 && netController.selectedIndex < logicalObjects.Count)
                    {
                        var logicalMain = logicalObjects[netController.selectedIndex];
                        if (logicalMain != null)
                        {
                            // Find this object in our current validObjects list to handle different instances/filtering
                            foreach (var obj in validObjects)
                            {
                                if (obj != null && obj.GetInstanceID() == logicalMain.GetInstanceID())
                                {
                                    if (actualMainPassenger != obj && PluginConfig.Instance.EnableDebugLogs.Value)
                                        Log.Info($"[CycleToNextObject] Overriding physical passenger ({actualMainPassenger?.name ?? "null"}) with logical selection ({obj.name})");
                                    
                                    actualMainPassenger = obj;
                                    isInNullState = false;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Fallback to physical state if no net controller
                isInNullState = actualMainPassenger == null && validObjects.Count > 0;
            }

            int totalPositions = validObjects.Count + 1;
            if (actualMainPassenger == null)
            {
                if (validObjects.Count == 0)
                {
                    return;
                }
            }
            

            if (actualMainPassenger == null && !isInNullState && vehicleSeat.hasPassenger)
            {
                GameObject? seatPassenger = vehicleSeat.NetworkpassengerBodyObject;
                if (seatPassenger != null)
                {
                    bool shouldTrack = false;
                    foreach (var obj in validObjects)
                    {
                        if (obj != null && obj.GetInstanceID() == seatPassenger.GetInstanceID())
                        {
                            shouldTrack = true;
                            break;
                        }
                    }
                    if (shouldTrack)
                    {
                        if (!BagPatches.baggedObjectsDict.TryGetValue(bagController, out var list))
                        {
                            list = new List<GameObject>();
                            BagPatches.baggedObjectsDict[bagController] = list;
                        }
                        int passengerInstanceId = seatPassenger.GetInstanceID();
                        bool alreadyTracked = false;
                        foreach (var existingObj in list)
                        {
                            if (existingObj != null && existingObj.GetInstanceID() == passengerInstanceId)
                            {
                                alreadyTracked = true;
                                break;
                            }
                        }
                        if (!alreadyTracked)
                        {
                            list.Add(seatPassenger);
                        }
                        BagPatches.SetMainSeatObject(bagController, seatPassenger);
                        BagPatches.UpdateCarousel(bagController, 0);
                        actualMainPassenger = seatPassenger;
                    }
                }
            }
            int currentIndex = -1;
            bool currentIsNull = false;
            if (isInNullState)
            {
                currentIndex = validObjects.Count;
                currentIsNull = true;
            }
            else
            {
                for (int i = 0; i < validObjects.Count; i++)
                {
                    if (validObjects[i] != null && actualMainPassenger != null && validObjects[i].GetInstanceID() == actualMainPassenger.GetInstanceID())
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }
            if (currentIndex < 0 && !currentIsNull)
            {
                currentIndex = validObjects.Count;
                currentIsNull = true;
            }
            int nextIndex = (currentIndex + amount) % totalPositions;
            if (nextIndex < 0) nextIndex += totalPositions;
            bool nextIsNull = (nextIndex == validObjects.Count);
            
            int direction = Math.Sign(amount);

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CycleToNextObject] Index Calc: Current={currentIndex} (IsNull={currentIsNull}), Amount={amount}, Next={nextIndex} (IsNull={nextIsNull}), TotalPos={totalPositions}");
            }

            bool hasValidSeatConfiguration = ValidateSeatConfiguration(bagController, validObjects, actualMainPassenger, isInNullState, localSeatDict);
            if (!hasValidSeatConfiguration)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleToNextObject] Invalid Seat Conf, Aborting.");
                return;
            }
            DrifterBossGrabPlugin._isSwappingPassengers = true;
            try
            {
                if (nextIsNull)
                {
                    if (!currentIsNull && actualMainPassenger != null)
                    {
                        if (!HasSpaceForNullStateTransition(bagController, validObjects.Count, localSeatDict))
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleToNextObject] No space for null transition, aborting.");
                            return;
                        }
                        
                        if (!ValidateNullStateTransition(bagController, actualMainPassenger, localSeatDict))
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[CycleToNextObject] ValidateNullStateTransition failed, aborting.");
                            return;
                        }
                        var seatForCurrent = FindOrCreateEmptySeat(bagController, ref localSeatDict);
                        vehicleSeat.EjectPassenger(actualMainPassenger);
                        if (actualMainPassenger != null)
                        {
                            System.Collections.Generic.CollectionExtensions.Remove(BagPatches.mainSeatDict, bagController, out _);
                            BaggedObjectPatches.RemoveUIOverlay(actualMainPassenger);
                        }
                        BagPatches.SetMainSeatObject(bagController, null);
                        if (seatForCurrent != null && actualMainPassenger != null)
                        {
                            seatForCurrent.AssignPassenger(actualMainPassenger);
                            localSeatDict[actualMainPassenger] = seatForCurrent;
                        }
                        if (actualMainPassenger != null)
                        {
                            System.Collections.Generic.CollectionExtensions.Remove(BagPatches.mainSeatDict, bagController, out _);
                        }
                        BaggedObjectPatches.RemoveUIOverlayForNullState(bagController);
                    }
                    else
                    {
                        nextIsNull = false;
                        nextIndex = 0;
                    }
                }
                else if (!nextIsNull && currentIsNull)
                {
                    var targetObject = validObjects[nextIndex];
                    if (targetObject == null)
                    {
                        return;
                    }
                    
                    var sourceAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject, localSeatDict);
                    if (sourceAdditionalSeat != null)
                    {
                        sourceAdditionalSeat.EjectPassenger(targetObject);
                        System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
                    }
                    bagController.AssignPassenger(targetObject);
                    BagPatches.SetMainSeatObject(bagController, targetObject);
                }
                else
                {
                    var currentObject = validObjects[currentIndex];
                    var targetObject = validObjects[nextIndex];
                    if (targetObject == null)
                    {
                        return;
                    }
    
                    if (!ValidateSeatStateForSwap(bagController, currentObject, targetObject, localSeatDict))
                    {
                        return;
                    }
                    

                    bool currentIsPhysicallyInSeat = vehicleSeat.hasPassenger && 
                        vehicleSeat.NetworkpassengerBodyObject != null &&
                        vehicleSeat.NetworkpassengerBodyObject.GetInstanceID() == currentObject.GetInstanceID();
                    
                    var targetAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject);
                    
                    if (currentIsPhysicallyInSeat)
                    {
                        // Regular server-side swap - manipulate physical seats
                        vehicleSeat.EjectPassenger(currentObject);
                        if (currentObject != null)
                        {
                            System.Collections.Generic.CollectionExtensions.Remove(BagPatches.mainSeatDict, bagController, out _);
                            BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                        }
                        if (targetAdditionalSeat != null)
                        {
                            targetAdditionalSeat.EjectPassenger(targetObject);
                            System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
                            targetAdditionalSeat.AssignPassenger(currentObject);
                            BagPatches.SetMainSeatObject(bagController, null);
                            BaggedObjectPatches.RemoveUIOverlay(currentObject, bagController);
                            localSeatDict[currentObject] = targetAdditionalSeat;
                        }
                        if (targetAdditionalSeat == null)
                        {
                            int totalSeatsCount = 0;
                            if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDictForCheck))
                            {
                                totalSeatsCount = seatDictForCheck.Count;
                            }
                            // Create a new seat if we have room
                            if (totalSeatsCount + 1 <= BagPatches.GetUtilityMaxStock(bagController))
                            {
                                var newSeat = FindOrCreateEmptySeat(bagController, ref localSeatDict);
                                if (newSeat != null)
                                {
                                    newSeat.AssignPassenger(currentObject);
                                    localSeatDict[currentObject] = newSeat;
                                }
                            }
                            else
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[CycleToNextObject] No room for new additional seat when swapping. Capacity: {BagPatches.GetUtilityMaxStock(bagController)}, Current: {totalSeatsCount}");
                                return; // Cannot create a new seat, so abort swap
                            }
                        }
                        vehicleSeat.AssignPassenger(targetObject);
                        BagPatches.SetMainSeatObject(bagController, targetObject);
                        BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                        BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                        BagPatches.UpdateCarousel(bagController, direction);
                    }
                    else
                    {
                        // Client-authority swap or handled by messages
                        if (targetAdditionalSeat != null)
                        {
                            targetAdditionalSeat.EjectPassenger(targetObject);
                            System.Collections.Generic.CollectionExtensions.Remove(localSeatDict, targetObject, out _);
                            if (currentObject != null)
                            {
                                targetAdditionalSeat.AssignPassenger(currentObject);
                                localSeatDict[currentObject] = targetAdditionalSeat;
                            }
                        }
                        BagPatches.SetMainSeatObject(bagController, targetObject);
                        if (targetObject != null)
                        {
                            vehicleSeat.AssignPassenger(targetObject);
                            BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
                            BaggedObjectPatches.SynchronizeBaggedObjectState(bagController, targetObject);
                        }
                        BagPatches.UpdateCarousel(bagController, direction);
                    }
                }
    
                if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var finalDict))
                {
                    foreach (var kvp in localSeatDict)
                    {
                        finalDict[kvp.Key] = kvp.Value;
                    }
                    var keysToRemove = finalDict.Keys.Where(k => !localSeatDict.ContainsKey(k)).ToList();
                    foreach (var k in keysToRemove) finalDict.TryRemove(k, out _);
                }
                else
                {
                    BagPatches.additionalSeatsDict[bagController] = localSeatDict;
                }
                
                BagPatches.UpdateCarousel(bagController, direction);
                BagPatches.UpdateNetworkBagState(bagController, direction);
            }
            finally
            {
                DrifterBossGrabPlugin._isSwappingPassengers = false;
            }
            if (!nextIsNull)
            {
                var targetObject = nextIndex < validObjects.Count ? validObjects[nextIndex] : null;
                if (targetObject != null)
                    BaggedObjectPatches.RefreshUIOverlayForMainSeat(bagController, targetObject);
            }
            
            // Sync network state after cycle operation
            BagPatches.UpdateNetworkBagState(bagController, direction);
            BagPatches.ForceRecalculateMass(bagController);
            
            // Debugging: Inspect BaggedObject state and active skills
             if (PluginConfig.Instance.EnableDebugLogs.Value && (UnityEngine.Networking.NetworkServer.active || (bagController && bagController.hasAuthority)))
            {
                var stateMachines = bagController.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag" && esm.state is BaggedObject bo)
                    {
                        Log.Info($" [CycleToNextObject] Inspecting BaggedObject state for {bagController.name}...");
                        
                        var fields = typeof(BaggedObject).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        foreach (var field in fields)
                        {
                            var val = field.GetValue(bo);
                            var valStr = val != null ? val.ToString() : "null";
                            if (val is UnityEngine.Object obj && obj) valStr = $"{obj.name} ({obj.GetType().Name})";
                            Log.Info($"    Field {field.Name}: {valStr}");
                        }

                        // Inspect Active Skills
                        var skillLocator = bagController.GetComponent<SkillLocator>();
                        if (skillLocator)
                        {
                            if (skillLocator.utility && skillLocator.utility.skillDef)
                            {
                                Log.Info($"    Utility SkillDef: {skillLocator.utility.skillDef.skillName} ({skillLocator.utility.skillDef.GetType().Name})");
                                // If the SkillDef has a 'target' field (common in some implementations), log it
                                var defFields = skillLocator.utility.skillDef.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                foreach (var df in defFields)
                                {
                                    if (df.FieldType == typeof(GameObject) || df.FieldType == typeof(NetworkInstanceId))
                                    {
                                         var dVal = df.GetValue(skillLocator.utility.skillDef);
                                         Log.Info($"       DefField {df.Name}: {dVal}");
                                    }
                                }
                            }
                            if (skillLocator.primary && skillLocator.primary.skillDef)
                            {
                                Log.Info($"    Primary SkillDef: {skillLocator.primary.skillDef.skillName} ({skillLocator.primary.skillDef.GetType().Name})");
                            }
                        }
                    }
                }
            }
        }
        private static bool ValidateSeatConfiguration(DrifterBagController bagController, List<GameObject> validObjects, GameObject? actualMainPassenger, bool isInNullState, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (!isInNullState && actualMainPassenger == null)
            {
                return false;
            }
            if (isInNullState && validObjects.Count == 0)
            {
                return false;
            }
            if (seatDict.Count > 0)
            {
                foreach (var kvp in seatDict)
                {
                    var trackedObject = kvp.Key;
                    var trackedSeat = kvp.Value;
                    if (trackedSeat == null)
                    {
                        return false;
                    }
                    if (trackedSeat.hasPassenger)
                    {
                        var actualPassenger = trackedSeat.NetworkpassengerBodyObject;
                        if (actualPassenger.GetInstanceID() != trackedObject.GetInstanceID())
                        {
                            return false;
                        }
                    }
                    else if (!trackedSeat.hasPassenger)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        private static bool ValidateSeatStateForSwap(DrifterBagController bagController, GameObject? currentObject, GameObject? targetObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (targetObject == null) return false;
            var mainSeat = bagController.vehicleSeat;
            if (mainSeat == null)
            {
                return false;
            }
            bool isActuallyInMainSeat = false;
            if (mainSeat.hasPassenger)
            {
                var actualMainPassenger = mainSeat.NetworkpassengerBodyObject;
                isActuallyInMainSeat = actualMainPassenger != null && actualMainPassenger.GetInstanceID() == currentObject!.GetInstanceID();
            }
            
            // For client-grabbed objects, the server's vehicleSeat may not be populated
            // Trust the mainSeatDict tracking as authoritative for these cases
            if (!isActuallyInMainSeat && currentObject != null)
            {
                var trackedMain = BagPatches.GetMainSeatObject(bagController);
                if (trackedMain != null && trackedMain.GetInstanceID() == currentObject.GetInstanceID())
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[ValidateSeatStateForSwap] Trusting mainSeatDict tracking for {currentObject.name} (not physically in vehicleSeat)");
                    isActuallyInMainSeat = true;
                }
            }
            
            if (!isActuallyInMainSeat)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[ValidateSeatStateForSwap] Failed: currentObject {currentObject?.name ?? "null"} not in main seat");
                return false;
            }
            var targetAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject, seatDict);
            if (targetAdditionalSeat != null)
            {
                if (targetAdditionalSeat.hasPassenger)
                {
                    var actualTargetPassenger = targetAdditionalSeat.NetworkpassengerBodyObject;
                    if (actualTargetPassenger != null && actualTargetPassenger.GetInstanceID() == targetObject.GetInstanceID())
                    {
                    }
                }
            }
            else
            {
                 if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[ValidateSeatStateForSwap] targetAdditionalSeat is null for {targetObject?.name}. Allowing swap via fallback (virtual/create new).");
                return true; // Use fallback logic (e.g. FindOrCreateEmptySeat) in CycleToNextObject
            }
            if (seatDict.Count > 0)
            {
                foreach (var kvp in seatDict)
                {
                    var seat = kvp.Value;
                    if (seat != null && seat.hasPassenger)
                    {
                        var seatPassenger = seat.NetworkpassengerBodyObject;
                        if (seatPassenger != null && seatPassenger.GetInstanceID() == currentObject.GetInstanceID() && seat != mainSeat)
                        {
                            return false;
                        }
                        if (seatPassenger != null && seatPassenger.GetInstanceID() == targetObject.GetInstanceID() && seat == mainSeat)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        public static RoR2.VehicleSeat FindOrCreateEmptySeat(DrifterBagController bagController, ref ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            var vehicleSeat = bagController.vehicleSeat;
            foreach (var kvp in seatDict)
            {
                if (kvp.Value != null && !kvp.Value.hasPassenger)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[FindOrCreateEmptySeat] Found existing empty tracked seat for {bagController.name}");
                    return kvp.Value;
                }
            }
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == vehicleSeat) continue;
                bool isTracked = seatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[FindOrCreateEmptySeat] Found existing empty untracked seat for {bagController.name}");
                    return childSeat;
                }
            }
            
            int currentCapacity = BagPatches.GetUtilityMaxStock(bagController);
            int totalAdditionalSeats = seatDict.Count;

            if (totalAdditionalSeats >= currentCapacity - 1)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[FindOrCreateEmptySeat] Cannot create additional seat. Capacity reached ({totalAdditionalSeats + 1}/{currentCapacity})");
                return null!;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[FindOrCreateEmptySeat] Creating new additional seat (Current additional: {totalAdditionalSeats}, Capacity: {currentCapacity})");
            
            // Disable local seat creation on client to prevent conflicts
            if (!NetworkServer.active) return null!;

            var seatObject = (Networking.BagStateSync.AdditionalSeatPrefab != null) 
                ? UnityEngine.Object.Instantiate(Networking.BagStateSync.AdditionalSeatPrefab)
                : new GameObject($"AdditionalSeat_Empty_{DateTime.Now.Ticks}");
            
            seatObject.SetActive(true);
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            
            var newSeat = seatObject.GetComponent<RoR2.VehicleSeat>();
            if (newSeat == null) newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            
            NetworkServer.Spawn(seatObject);

            newSeat.seatPosition = vehicleSeat.seatPosition;
            newSeat.exitPosition = vehicleSeat.exitPosition;
            newSeat.ejectOnCollision = vehicleSeat.ejectOnCollision;
            newSeat.hidePassenger = vehicleSeat.hidePassenger;
            newSeat.exitVelocityFraction = vehicleSeat.exitVelocityFraction;
            newSeat.disablePassengerMotor = vehicleSeat.disablePassengerMotor;
            newSeat.isEquipmentActivationAllowed = vehicleSeat.isEquipmentActivationAllowed;
            newSeat.shouldProximityHighlight = vehicleSeat.shouldProximityHighlight;
            newSeat.disableInteraction = vehicleSeat.disableInteraction;
            newSeat.shouldSetIdle = vehicleSeat.shouldSetIdle;
            newSeat.additionalExitVelocity = vehicleSeat.additionalExitVelocity;
            newSeat.disableAllCollidersAndHurtboxes = vehicleSeat.disableAllCollidersAndHurtboxes;
            newSeat.disableColliders = vehicleSeat.disableColliders;
            newSeat.disableCharacterNetworkTransform = vehicleSeat.disableCharacterNetworkTransform;
            newSeat.ejectFromSeatOnMapEvent = vehicleSeat.ejectFromSeatOnMapEvent;
            newSeat.inheritRotation = vehicleSeat.inheritRotation;
            newSeat.holdPassengerAfterDeath = vehicleSeat.holdPassengerAfterDeath;
            newSeat.ejectPassengerToGround = vehicleSeat.ejectPassengerToGround;
            newSeat.ejectRayDistance = vehicleSeat.ejectRayDistance;
            newSeat.handleExitTeleport = vehicleSeat.handleExitTeleport;
            newSeat.setCharacterMotorPositionToCurrentPosition = vehicleSeat.setCharacterMotorPositionToCurrentPosition;
            newSeat.passengerState = vehicleSeat.passengerState;
            return newSeat;
        }
        public static RoR2.VehicleSeat FindOrCreateEmptySeat(DrifterBagController bagController)
        {
            var vehicleSeat = bagController.vehicleSeat;
            if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && !kvp.Value.hasPassenger)
                    {
                        return kvp.Value;
                    }
                }
            }
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == vehicleSeat) continue;
                bool isTracked = seatDict != null && seatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    return childSeat;
                }
            }
            
            // Disable local seat creation on client to prevent conflicts
            if (!NetworkServer.active) return null!;

            var seatObject = (Networking.BagStateSync.AdditionalSeatPrefab != null) 
                ? UnityEngine.Object.Instantiate(Networking.BagStateSync.AdditionalSeatPrefab)
                : new GameObject($"AdditionalSeat_Empty_{DateTime.Now.Ticks}");
            
            seatObject.SetActive(true);
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            
            var newSeat = seatObject.GetComponent<RoR2.VehicleSeat>();
            if (newSeat == null) newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            
            NetworkServer.Spawn(seatObject);

            newSeat.seatPosition = vehicleSeat.seatPosition;
            newSeat.exitPosition = vehicleSeat.exitPosition;
            newSeat.ejectOnCollision = vehicleSeat.ejectOnCollision;
            newSeat.hidePassenger = vehicleSeat.hidePassenger;
            newSeat.exitVelocityFraction = vehicleSeat.exitVelocityFraction;
            newSeat.disablePassengerMotor = vehicleSeat.disablePassengerMotor;
            newSeat.isEquipmentActivationAllowed = vehicleSeat.isEquipmentActivationAllowed;
            newSeat.shouldProximityHighlight = vehicleSeat.shouldProximityHighlight;
            newSeat.disableInteraction = vehicleSeat.disableInteraction;
            newSeat.shouldSetIdle = vehicleSeat.shouldSetIdle;
            newSeat.additionalExitVelocity = vehicleSeat.additionalExitVelocity;
            newSeat.disableAllCollidersAndHurtboxes = vehicleSeat.disableAllCollidersAndHurtboxes;
            newSeat.disableColliders = vehicleSeat.disableColliders;
            newSeat.disableCharacterNetworkTransform = vehicleSeat.disableCharacterNetworkTransform;
            newSeat.ejectFromSeatOnMapEvent = vehicleSeat.ejectFromSeatOnMapEvent;
            newSeat.inheritRotation = vehicleSeat.inheritRotation;
            newSeat.holdPassengerAfterDeath = vehicleSeat.holdPassengerAfterDeath;
            newSeat.ejectPassengerToGround = vehicleSeat.ejectPassengerToGround;
            newSeat.ejectRayDistance = vehicleSeat.ejectRayDistance;
            newSeat.handleExitTeleport = vehicleSeat.handleExitTeleport;
            newSeat.setCharacterMotorPositionToCurrentPosition = vehicleSeat.setCharacterMotorPositionToCurrentPosition;
            newSeat.passengerState = vehicleSeat.passengerState;
            return newSeat;
        }
        private static RoR2.VehicleSeat? GetAdditionalSeatForObject(DrifterBagController bagController, GameObject? obj, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (obj == null) return null!;
            if (seatDict.TryGetValue(obj, out var seat))
            {
                return seat;
            }
            return null!;
        }
        private static RoR2.VehicleSeat? GetAdditionalSeatForObject(DrifterBagController bagController, GameObject? obj)
        {
            if (obj == null) return null!;
            if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                if (seatDict.TryGetValue(obj, out var seat))
                {
                    return seat;
                }
            }
            return null!;
        }
        private static RoR2.VehicleSeat GetOrCreateAdditionalSeat(DrifterBagController bagController, int seatIndex)
        {
            if (BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                int currentIndex = 0;
                foreach (var kvp in seatDict)
                {
                    if (currentIndex == seatIndex)
                    {
                        return kvp.Value;
                    }
                    currentIndex++;
                }
            }
            if (!NetworkServer.active) return null!;

            var seatObject = (Networking.BagStateSync.AdditionalSeatPrefab != null) 
                ? UnityEngine.Object.Instantiate(Networking.BagStateSync.AdditionalSeatPrefab)
                : new GameObject($"AdditionalSeat_Index_{seatIndex}_{DateTime.Now.Ticks}");
            
            seatObject.SetActive(true);
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            
            var newSeat = seatObject.GetComponent<RoR2.VehicleSeat>();
            if (newSeat == null) newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            
            NetworkServer.Spawn(seatObject);

            var mainSeat = bagController.vehicleSeat;
            newSeat.seatPosition = mainSeat.seatPosition;
            newSeat.exitPosition = mainSeat.exitPosition;
            newSeat.ejectOnCollision = mainSeat.ejectOnCollision;
            newSeat.hidePassenger = mainSeat.hidePassenger;
            newSeat.exitVelocityFraction = mainSeat.exitVelocityFraction;
            newSeat.disablePassengerMotor = mainSeat.disablePassengerMotor;
            newSeat.isEquipmentActivationAllowed = mainSeat.isEquipmentActivationAllowed;
            newSeat.shouldProximityHighlight = mainSeat.shouldProximityHighlight;
            newSeat.disableInteraction = mainSeat.disableInteraction;
            newSeat.shouldSetIdle = mainSeat.shouldSetIdle;
            newSeat.additionalExitVelocity = mainSeat.additionalExitVelocity;
            newSeat.disableAllCollidersAndHurtboxes = mainSeat.disableAllCollidersAndHurtboxes;
            newSeat.disableColliders = mainSeat.disableColliders;
            newSeat.disableCharacterNetworkTransform = mainSeat.disableCharacterNetworkTransform;
            newSeat.ejectFromSeatOnMapEvent = mainSeat.ejectFromSeatOnMapEvent;
            newSeat.inheritRotation = mainSeat.inheritRotation;
            newSeat.holdPassengerAfterDeath = mainSeat.holdPassengerAfterDeath;
            newSeat.ejectPassengerToGround = mainSeat.ejectPassengerToGround;
            newSeat.ejectRayDistance = mainSeat.ejectRayDistance;
            newSeat.handleExitTeleport = mainSeat.handleExitTeleport;
            newSeat.setCharacterMotorPositionToCurrentPosition = mainSeat.setCharacterMotorPositionToCurrentPosition;
            newSeat.passengerState = mainSeat.passengerState;
            return newSeat;
        }
        private static void PerformPassengerCycle(DrifterBagController bagController, GameObject currentMain, GameObject newMain)
        {
            DrifterBossGrabPlugin._isSwappingPassengers = true;
            try
            {
                var vehicleSeat = bagController.vehicleSeat;
                var additionalSeats = BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict) ? seatDict : null;
                RoR2.VehicleSeat? currentAdditionalSeat = null;
                if (additionalSeats != null && additionalSeats.TryGetValue(currentMain, out var seat))
                {
                    currentAdditionalSeat = seat;
                }
                vehicleSeat.EjectPassenger(currentMain);
                vehicleSeat.AssignPassenger(newMain);
                if (currentAdditionalSeat != null)
                {
                    currentAdditionalSeat.AssignPassenger(currentMain);
                }
                else if (additionalSeats != null)
                {
                    var seatObject = new GameObject($"AdditionalSeat_Cycled_{DateTime.Now.Ticks}");
                    seatObject.transform.SetParent(bagController.transform);
                    seatObject.transform.localPosition = Vector3.zero;
                    seatObject.transform.localRotation = Quaternion.identity;
                    var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
                    newSeat.seatPosition = vehicleSeat.seatPosition;
                    newSeat.exitPosition = vehicleSeat.exitPosition;
                    newSeat.ejectOnCollision = vehicleSeat.ejectOnCollision;
                    newSeat.hidePassenger = vehicleSeat.hidePassenger;
                    newSeat.exitVelocityFraction = vehicleSeat.exitVelocityFraction;
                    newSeat.disablePassengerMotor = vehicleSeat.disablePassengerMotor;
                    newSeat.isEquipmentActivationAllowed = vehicleSeat.isEquipmentActivationAllowed;
                    newSeat.shouldProximityHighlight = vehicleSeat.shouldProximityHighlight;
                    newSeat.disableInteraction = vehicleSeat.disableInteraction;
                    newSeat.shouldSetIdle = vehicleSeat.shouldSetIdle;
                    newSeat.additionalExitVelocity = vehicleSeat.additionalExitVelocity;
                    newSeat.disableAllCollidersAndHurtboxes = vehicleSeat.disableAllCollidersAndHurtboxes;
                    newSeat.disableColliders = vehicleSeat.disableColliders;
                    newSeat.disableCharacterNetworkTransform = vehicleSeat.disableCharacterNetworkTransform;
                    newSeat.ejectFromSeatOnMapEvent = vehicleSeat.ejectFromSeatOnMapEvent;
                    newSeat.inheritRotation = vehicleSeat.inheritRotation;
                    newSeat.holdPassengerAfterDeath = vehicleSeat.holdPassengerAfterDeath;
                    newSeat.ejectPassengerToGround = vehicleSeat.ejectPassengerToGround;
                    newSeat.ejectRayDistance = vehicleSeat.ejectRayDistance;
                    newSeat.handleExitTeleport = vehicleSeat.handleExitTeleport;
                    newSeat.setCharacterMotorPositionToCurrentPosition = vehicleSeat.setCharacterMotorPositionToCurrentPosition;
                    newSeat.passengerState = vehicleSeat.passengerState;
                    newSeat.AssignPassenger(currentMain);
                    additionalSeats[currentMain] = newSeat;
                }
            }
            finally
            {
                DrifterBossGrabPlugin._isSwappingPassengers = false;
            }
        }
        private static bool ValidateNullStateTransition(DrifterBagController bagController, GameObject? currentObject, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            if (currentObject == null) return false;
            var mainSeat = bagController.vehicleSeat;
            if (mainSeat == null)
            {
                return false;
            }
            bool isActuallyInMainSeat = false;
            if (mainSeat.hasPassenger)
            {
                var actualMainPassenger = mainSeat.NetworkpassengerBodyObject;
                isActuallyInMainSeat = actualMainPassenger != null && actualMainPassenger.GetInstanceID() == currentObject.GetInstanceID();
            }
            if (!isActuallyInMainSeat)
            {
                return false;
            }
            var availableSeat = FindOrCreateEmptySeat(bagController, ref seatDict);
            if (availableSeat == null)
            {
                return false;
            }
            if (seatDict.Count > 0)
            {
                foreach (var kvp in seatDict)
                {
                    var seat = kvp.Value;
                    if (seat != null && seat.hasPassenger)
                    {
                        var seatPassenger = seat.NetworkpassengerBodyObject;
                        if (seatPassenger.GetInstanceID() == currentObject.GetInstanceID() && seat != mainSeat)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        private static bool HasSpaceForNullStateTransition(DrifterBagController bagController, int currentObjectCount, ConcurrentDictionary<GameObject, RoR2.VehicleSeat> seatDict)
        {
            int effectiveCapacity = BagPatches.GetUtilityMaxStock(bagController);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[HasSpaceForNullStateTransition] currentObjectCount: {currentObjectCount}, effectiveCapacity: {effectiveCapacity}");
            }

            if (currentObjectCount >= effectiveCapacity)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[HasSpaceForNullStateTransition] Capacity reached ({currentObjectCount}/{effectiveCapacity}), blocking null transition to prevent over-filling");
                return false;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[HasSpaceForNullStateTransition] Space available for null transition");
            
            return true;
        }
    }
}
