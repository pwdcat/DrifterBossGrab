using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;

namespace DrifterBossGrabMod
{
    [BepInPlugin(Constants.PluginGuid, Constants.PluginName, Constants.PluginVersion)]
    public class DrifterBossGrabPlugin : BaseUnityPlugin
    {
        private const short BAGGED_OBJECTS_PERSISTENCE_MSG_TYPE = 85;
        // Plugin instance
        public static DrifterBossGrabPlugin Instance { get; private set; }

        // Gets whether Risk of Options is installed
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        // Gets the directory name where the plugin is located
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);

        // Event handler references for cleanup
        private EventHandler debugLogsHandler;
        private EventHandler blacklistHandler;
        private EventHandler forwardVelHandler;
        private EventHandler upwardVelHandler;
        private EventHandler recoveryBlacklistHandler;
        private EventHandler grabbableComponentTypesHandler;
        private EventHandler grabbableKeywordBlacklistHandler;
        private EventHandler bossGrabbingHandler;
        private EventHandler npcGrabbingHandler;
        private EventHandler environmentGrabbingHandler;
        private EventHandler lockedObjectGrabbingHandler;
        private EventHandler persistenceHandler;
        private EventHandler autoGrabHandler;

        // Debounce coroutine for grabbable component types updates
        private static UnityEngine.Coroutine? _grabbableComponentTypesUpdateCoroutine;

        // Flag to prevent RemoveBaggedObject from removing objects during swap operations
        private static bool _isSwappingPassengers = false;

        public static bool IsSwappingPassengers => _isSwappingPassengers;

        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            
            // Initialize configuration
            PluginConfig.Init(Config);

            // Initialize state management with debug logging setting
            StateManagement.Initialize(PluginConfig.EnableDebugLogs.Value);
            Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;

            // Initialize persistence system
            PersistenceManager.Initialize();

            // Initialize patch systems
            Patches.RepossessPatches.Initialize();

            // Setup configuration event handlers
            SetupConfigurationEventHandlers();

            // Apply all Harmony patches
            ApplyHarmonyPatches();

            // Initialize run lifecycle event handlers
            Patches.RunLifecyclePatches.Initialize();

            // Initialize teleporter event handlers
            Patches.TeleporterPatches.Initialize();

            // Register for game events
            RegisterGameEvents();
        }

        public void OnDestroy()
        {
            // Remove configuration event handlers to prevent memory leaks
            PluginConfig.RemoveEventHandlers(
                debugLogsHandler,
                blacklistHandler,
                forwardVelHandler,
                upwardVelHandler,
                recoveryBlacklistHandler,
                grabbableComponentTypesHandler,
                grabbableKeywordBlacklistHandler,
                bossGrabbingHandler,
                npcGrabbingHandler,
                environmentGrabbingHandler,
                lockedObjectGrabbingHandler
            );

            // Remove persistence event handlers
            PluginConfig.EnableObjectPersistence.SettingChanged -= persistenceHandler;
            PluginConfig.EnableAutoGrab.SettingChanged -= autoGrabHandler;

            // Cleanup run lifecycle event handlers
            Patches.RunLifecyclePatches.Cleanup();

            // Cleanup teleporter event handlers
            Patches.TeleporterPatches.Cleanup();

            // Cleanup persistence system
            PersistenceManager.Cleanup();

        }

        public void Start()
        {
            SetupRiskOfOptions();
        }

        public void Update()
        {
            // Check for scroll wheel input to cycle through passengers
            if (PluginConfig.BottomlessBagEnabled.Value)
            {
                float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
                if (scrollDelta != 0f)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Scroll wheel detected: scrollDelta={scrollDelta}");
                    }
                    CyclePassengers(scrollDelta > 0f);
                }
            }
        }

        private void CyclePassengers(bool scrollUp)
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] CyclePassengers called - scrollUp={scrollUp}");
            }
            
            // Get all DrifterBagControllers
            var bagControllers = UnityEngine.Object.FindObjectsOfType<DrifterBagController>();
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] Found {bagControllers.Length} bag controllers");
            }
            
            foreach (var bagController in bagControllers)
            {
                // Check if this bag controller has local authority
                if (!bagController.isAuthority)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Skipping bag controller - no local authority (isAuthority={bagController.isAuthority})");
                    }
                    continue;
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Processing bag controller with local authority");
                }

                // Check if vehicleSeat is valid
                if (bagController.vehicleSeat == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] ERROR: vehicleSeat is null!");
                    }
                    continue;
                }

                // Get the bagged objects list
                if (!Patches.BagPatches.baggedObjectsDict.TryGetValue(bagController, out var baggedObjects))
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] No bagged objects found in dictionary for this bag");
                    }
                    continue;
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] baggedObjects count: {baggedObjects.Count}");
                }

                // Filter out null objects and objects in projectile state
                // Also deduplicate by instance ID to prevent the same object from appearing multiple times
                var seenInstanceIds = new HashSet<int>();
                var validObjects = new List<GameObject>();
                foreach (var obj in baggedObjects)
                {
                    if (obj != null && !Patches.OtherPatches.IsInProjectileState(obj))
                    {
                        // Use GetInstanceID to detect duplicates
                        int instanceId = obj.GetInstanceID();
                        if (!seenInstanceIds.Contains(instanceId))
                        {
                            seenInstanceIds.Add(instanceId);
                            validObjects.Add(obj);
                        }
                        else if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] Skipping duplicate instance {obj.name} (inst={instanceId})");
                        }
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] validObjects count: {validObjects.Count}");
                    for (int i = 0; i < validObjects.Count; i++)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] validObjects[{i}] = {validObjects[i]?.name ?? "null"}");
                    }
                }

                if (validObjects.Count == 0)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] No valid objects to cycle");
                    }
                    continue;
                }

                // Call the new cycling logic
                CycleToNextObject(bagController, validObjects, scrollUp);
                break; // Only handle one bag controller (local player)
            }
        }

        private void CycleToNextObject(DrifterBagController bagController, List<GameObject> validObjects, bool scrollUp)
        {
            var vehicleSeat = bagController.vehicleSeat;
            
            // Use our tracked main seat object instead of vehicleSeat.currentPassengerBody
            // because currentPassengerBody may not be updated immediately after AssignPassenger
            GameObject mainPassenger = Patches.BagPatches.GetMainSeatObject(bagController);
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] CycleToNextObject: scrollUp={scrollUp}, trackedMainPassenger={mainPassenger?.name ?? "null"}, validObjects={validObjects.Count}");
            }
            
            // Filter out null and find the actual main passenger (by instance ID matching)
            GameObject actualMainPassenger = null;
            int mainPassengerInstanceId = mainPassenger?.GetInstanceID() ?? 0;
            
            foreach (var obj in validObjects)
            {
                if (obj != null && obj.GetInstanceID() == mainPassengerInstanceId && mainPassengerInstanceId != 0)
                {
                    actualMainPassenger = obj;
                    break;
                }
            }
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] actualMainPassenger={actualMainPassenger?.name ?? "null"}");
            }
            
            // Count empty/additional seats to determine if null state is available
            // Use hasPassenger instead of currentPassengerBody to correctly detect occupancy for all passenger types
            int emptySeatsCount = 0;
            if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                foreach (var kvp in seatDict)
                {
                    if (kvp.Value != null && !kvp.Value.hasPassenger)
                    {
                        emptySeatsCount++;
                    }
                }
            }
            
            // Also check if there are any orphaned child seats that are empty
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == bagController.vehicleSeat) continue;
                
                bool isTracked = false;
                if (seatDict != null)
                {
                    foreach (var kvp in seatDict)
                    {
                        if (kvp.Value == childSeat)
                        {
                            isTracked = true;
                            break;
                        }
                    }
                }
                
                if (!isTracked && !childSeat.hasPassenger)
                {
                    emptySeatsCount++;
                }
            }
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] emptySeatsCount={emptySeatsCount}");
            }
            
            // Determine if we're currently in null state
            bool isInNullState = actualMainPassenger == null && emptySeatsCount > 0;
            
            // Calculate total positions: validObjects count + (emptySeatsCount > 0 ? 1 : 0)
            // Null state bundles all empty seats into one position
            int totalPositions = validObjects.Count + (emptySeatsCount > 0 ? 1 : 0);
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] isInNullState={isInNullState}, totalPositions={totalPositions}");
            }
            
            // Case 1: No main passenger - put first valid object in main seat, or prepare for cycling
            if (actualMainPassenger == null)
            {
                if (validObjects.Count > 0 && emptySeatsCount == 0)
                {
                    // No empty seats, must put an object in main seat
                    var objToMove = validObjects[0];
                    
                    // Check if this object is currently in an additional seat
                    var sourceAdditionalSeat = GetAdditionalSeatForObject(bagController, objToMove);
                    
                    if (sourceAdditionalSeat != null)
                    {
                        // Eject from additional seat first
                        sourceAdditionalSeat.EjectPassenger(objToMove);
                        
                        // Remove from additionalSeatsDict
                        if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var dictForRemoval))
                        {
                            dictForRemoval.Remove(objToMove);
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} [BottomlessBag] Removed {objToMove.name} from additionalSeatsDict when moving to main");
                            }
                        }
                    }
                    
                    // Assign to main seat
                    vehicleSeat.AssignPassenger(objToMove);
                    
                    // Track this object as being in the main seat
                    Patches.BagPatches.SetMainSeatObject(bagController, objToMove);
                    
                    // Refresh UI overlay
                    Patches.BaggedObjectPatches.RefreshUIOverlayForMainSeat(objToMove);
                    
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Moved {objToMove.name} to main seat (was {(sourceAdditionalSeat != null ? "in additional seat" : "unseated")})");
                    }
                    return;
                }
                // If we have empty seats, continue to cycling logic to handle null state
                // Don't return here - let the cycling logic handle it
            }
            
            // Case 2: Main passenger exists - find current index and cycle
            int currentIndex = -1;
            bool currentIsNull = false;
            
            if (isInNullState)
            {
                // We're currently in null state (main seat is null, but we have empty seats)
                currentIndex = validObjects.Count; // Null state is after all valid objects
                currentIsNull = true;
            }
            else
            {
                for (int i = 0; i < validObjects.Count; i++)
                {
                    if (validObjects[i] != null && validObjects[i].GetInstanceID() == actualMainPassenger.GetInstanceID())
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] currentIndex={currentIndex}, currentIsNull={currentIsNull}");
            }
            
            if (currentIndex < 0 && !currentIsNull)
            {
                // Main passenger not found in validObjects and not in null state, treat as null
                currentIndex = validObjects.Count;
                currentIsNull = true;
            }
            
            // Calculate next index
            int direction = scrollUp ? 1 : -1;
            int nextIndex = currentIndex + direction;
            
            // Wrap around including null state
            if (nextIndex >= totalPositions) nextIndex = 0;
            if (nextIndex < 0) nextIndex = totalPositions - 1;
            
            bool nextIsNull = nextIndex == validObjects.Count;
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycling from index {currentIndex} ({(currentIsNull ? "null" : validObjects[currentIndex]?.name)}) to {nextIndex} ({(nextIsNull ? "null" : validObjects[nextIndex]?.name)})");
            }
            
            // Handle the cycling action
            if (nextIsNull)
            {
                // Cycling to null state - eject current passenger and leave main seat empty
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycling to null state - ejecting current passenger");
                }
                
                if (!currentIsNull && actualMainPassenger != null)
                {
                    _isSwappingPassengers = true;
                    
                    // Find or create an empty seat for the displaced passenger
                    var seatForCurrent = FindOrCreateEmptySeat(bagController);
                    
                    // Eject current from main
                    vehicleSeat.EjectPassenger(actualMainPassenger);
                    
                    // Remove UI overlay for object leaving main
                    Patches.BaggedObjectPatches.RemoveUIOverlay(actualMainPassenger);
                    
                    // Clear main seat tracking
                    Patches.BagPatches.SetMainSeatObject(bagController, null);
                    
                    // Assign to the empty seat
                    if (seatForCurrent != null)
                    {
                        seatForCurrent.AssignPassenger(actualMainPassenger);
                        
                        // Track that actualMainPassenger is now in an additional seat
                        if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var dictForTracking))
                        {
                            dictForTracking[actualMainPassenger] = seatForCurrent;
                        }
                        
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] Placed {actualMainPassenger.name} in empty seat");
                        }
                    }
                    
                    _isSwappingPassengers = false;
                    
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycled to null state - main seat is now empty");
                    }
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Already in null state - cycling wraps to first object");
                    }
                    // We're already in null state and cycling to null again - wrap to first object
                    nextIsNull = false;
                    nextIndex = 0;
                    
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Wrapping from null to {validObjects[0].name}");
                    }
                }
            }
            
            // If we wrapped from null to first object, handle it
            else if (!nextIsNull && currentIsNull)
            {
                // Cycling from null to a valid object
                var targetObject = validObjects[nextIndex];
                
                if (targetObject == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Target object is null, skipping");
                    }
                    return;
                }
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycling from null to {targetObject.name}");
                }
                
                _isSwappingPassengers = true;
                
                // Check if this object is currently in an additional seat
                var sourceAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject);
                
                if (sourceAdditionalSeat != null)
                {
                    // Eject from additional seat first
                    sourceAdditionalSeat.EjectPassenger(targetObject);
                    
                    // Remove from additionalSeatsDict
                    if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDictLocal))
                    {
                        seatDictLocal.Remove(targetObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] Removed {targetObject.name} from additionalSeatsDict when moving to main");
                        }
                    }
                }
                
                // Assign target to main seat
                vehicleSeat.AssignPassenger(targetObject);
                
                // Track target object as being in the main seat
                Patches.BagPatches.SetMainSeatObject(bagController, targetObject);
                
                // Refresh UI overlay for object now in main
                Patches.BaggedObjectPatches.RefreshUIOverlayForMainSeat(targetObject);
                
                _isSwappingPassengers = false;
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycled from null to {targetObject.name} in main seat");
                }
            }
            else
            {
                // Normal cycling between objects (neither current nor next is null)
                var currentObject = validObjects[currentIndex];
                var targetObject = validObjects[nextIndex];
                
                if (targetObject == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Target object is null, skipping");
                    }
                    return;
                }
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycling from {currentObject.name} to {targetObject.name}");
                }
                
                // Swap: eject current from main, put target in main, move current to target's seat (or find empty seat)
                _isSwappingPassengers = true;
                
                // Find target's additional seat
                var targetAdditionalSeat = GetAdditionalSeatForObject(bagController, targetObject);
                
                // Eject current from main
                vehicleSeat.EjectPassenger(currentObject);
                
                // Remove UI overlay for object leaving main
                Patches.BaggedObjectPatches.RemoveUIOverlay(currentObject);
                
                if (targetAdditionalSeat != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [BottomlessBag] Swap path: targetAdditionalSeat={targetAdditionalSeat.name}");
                    }
                    
                    // Validate seat is not null or destroyed
                    if (targetAdditionalSeat == null)
                    {
                        Log.Warning($"{Constants.LogPrefix} [BottomlessBag] ERROR: targetAdditionalSeat is null! Falling back to empty seat.");
                        targetAdditionalSeat = null;
                    }
                    else
                    {
                        // Target is in an additional seat - swap with current
                        targetAdditionalSeat.EjectPassenger(targetObject);
                        
                        // Remove both from additionalSeatsDict temporarily
                        if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDictLocal))
                        {
                            seatDictLocal.Remove(currentObject);
                            seatDictLocal.Remove(targetObject);
                        }
                        
                        // Assign current to target's former seat
                        targetAdditionalSeat.AssignPassenger(currentObject);
                        
                        // Verify the assignment
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            var currentPassenger = targetAdditionalSeat.currentPassengerBody;
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] After AssignPassenger: seat={targetAdditionalSeat.name}, currentPassenger={currentPassenger?.name ?? "null"}, hasPassenger={targetAdditionalSeat.hasPassenger}");
                        }
                        
                        // Track current in target's seat
                        if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var dictForCurrent))
                        {
                            dictForCurrent[currentObject] = targetAdditionalSeat;
                        }
                        
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] Swapped: {currentObject.name} in target's additional seat (was {targetObject.name}'s seat)");
                            // Verify the tracking
                            if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var verifyDict))
                            {
                                Log.Info($"{Constants.LogPrefix} [BottomlessBag] additionalSeatsDict count after swap: {verifyDict.Count}");
                                foreach (var kvp in verifyDict)
                                {
                                    Log.Info($"{Constants.LogPrefix} [BottomlessBag]   tracked: {kvp.Key.name} -> {kvp.Value?.name ?? "NULL"}");
                                }
                            }
                        }
                    }
                }
                
                // If targetAdditionalSeat was null or invalid, use the empty seat path
                if (targetAdditionalSeat == null)
                {
                    // Target is not in a seat - find or create an empty seat for current
                    var seatForCurrent = FindOrCreateEmptySeat(bagController);
                    if (seatForCurrent != null)
                    {
                        seatForCurrent.AssignPassenger(currentObject);
                        
                        // Verify the assignment
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            var currentPassenger = seatForCurrent.currentPassengerBody;
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] After AssignPassenger to empty seat: seat={seatForCurrent.name}, currentPassenger={currentPassenger?.name ?? "null"}, hasPassenger={seatForCurrent.hasPassenger}");
                        }
                        
                        if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var dictForNew))
                        {
                            dictForNew[currentObject] = seatForCurrent;
                        }
                        
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] Placed {currentObject.name} in empty seat (found or created)");
                            // Verify the tracking
                            if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var verifyDict))
                            {
                                Log.Info($"{Constants.LogPrefix} [BottomlessBag] additionalSeatsDict count after placement: {verifyDict.Count}");
                                foreach (var kvp in verifyDict)
                                {
                                    Log.Info($"{Constants.LogPrefix} [BottomlessBag]   tracked: {kvp.Key.name} -> {kvp.Value?.name ?? "null"}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Log.Warning($"{Constants.LogPrefix} [BottomlessBag] WARNING: No seat available for {currentObject.name}!");
                    }
                }
                
                // Assign target to main seat
                vehicleSeat.AssignPassenger(targetObject);
                
                // If target was in an additional seat, clean up the tracking
                if (targetAdditionalSeat != null)
                {
                    // Remove target from additionalSeatsDict since it's now in main seat
                    if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var cleanupDict))
                    {
                        cleanupDict.Remove(targetObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [BottomlessBag] Removed {targetObject.name} from additionalSeatsDict (moved to main seat)");
                        }
                    }
                }
                
                // Track target object as being in the main seat
                Patches.BagPatches.SetMainSeatObject(bagController, targetObject);
                
                // Refresh UI overlay for object now in main
                Patches.BaggedObjectPatches.RefreshUIOverlayForMainSeat(targetObject);
                
                _isSwappingPassengers = false;
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycled to {targetObject.name} in main seat");
                }
            }
        }

        private RoR2.VehicleSeat FindOrCreateEmptySeat(DrifterBagController bagController)
        {
            var vehicleSeat = bagController.vehicleSeat;
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Searching for empty seat...");
            }
            
            // First, check tracked seats in additionalSeatsDict for empty ones
            if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Checking {seatDict.Count} tracked seats");
                }
                
                foreach (var kvp in seatDict)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Checking seat for {kvp.Key?.name ?? "null"}: seat={(kvp.Value != null ? kvp.Value.name : "NULL")}, hasPassenger={kvp.Value?.hasPassenger}");
                    }
                    
                    if (kvp.Value != null && !kvp.Value.hasPassenger)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Found empty tracked seat: {kvp.Value.name}");
                        }
                        return kvp.Value;
                    }
                }
            }
            
            // If no empty tracked seat found, search all child objects for existing VehicleSeats that aren't tracked
            var childSeats = bagController.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Checking {childSeats.Length} child seats");
            }
            
            foreach (var childSeat in childSeats)
            {
                if (childSeat == vehicleSeat) continue;
                
                bool isTracked = false;
                if (seatDict != null)
                {
                    foreach (var kvp in seatDict)
                    {
                        if (kvp.Value == childSeat)
                        {
                            isTracked = true;
                            break;
                        }
                    }
                }
                
                if (!isTracked && !childSeat.hasPassenger)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Found untracked empty child seat: {childSeat.name}");
                    }
                    return childSeat;
                }
            }
            
            // No empty seat found - create a new one
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Creating new seat");
            }
            
            var seatObject = new GameObject($"AdditionalSeat_Empty_{DateTime.Now.Ticks}");
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            
            // Copy settings from main seat
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
            
            if (seatDict == null)
            {
                seatDict = new Dictionary<GameObject, RoR2.VehicleSeat>();
                Patches.BagPatches.additionalSeatsDict[bagController] = seatDict;
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Created new additionalSeatsDict entry");
                }
            }
            
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [FindOrCreateEmptySeat] Created new seat: {newSeat.name}");
            }
            
            return newSeat;
        }

        private RoR2.VehicleSeat GetAdditionalSeatForObject(DrifterBagController bagController, GameObject obj)
        {
            if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                if (seatDict.TryGetValue(obj, out var seat))
                {
                    return seat;
                }
            }
            return null;
        }

        private RoR2.VehicleSeat GetOrCreateAdditionalSeat(DrifterBagController bagController, int seatIndex)
        {
            if (Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict))
            {
                // Find the seat at this index
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
            
            // If we need to create a new seat
            // This is a fallback - we may need to create a new seat object
            var seatObject = new GameObject($"AdditionalSeat_Index_{seatIndex}_{DateTime.Now.Ticks}");
            seatObject.transform.SetParent(bagController.transform);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            
            // Copy settings from main seat
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

        private void PerformPassengerCycle(DrifterBagController bagController, GameObject currentMain, GameObject newMain)
        {
            var vehicleSeat = bagController.vehicleSeat;
            var additionalSeats = Patches.BagPatches.additionalSeatsDict.TryGetValue(bagController, out var seatDict) ? seatDict : null;

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] PerformPassengerCycle - currentMain={currentMain?.name ?? "null"}, newMain={newMain?.name ?? "null"}");
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] vehicleSeat = {vehicleSeat?.name ?? "null"}");
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] additionalSeats exists = {additionalSeats != null}, count = {additionalSeats?.Count ?? 0}");
            }

            // Check if current main is in an additional seat
            RoR2.VehicleSeat? currentAdditionalSeat = null;
            if (additionalSeats != null && additionalSeats.TryGetValue(currentMain, out var seat))
            {
                currentAdditionalSeat = seat;
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] currentMain found in additionalSeatsDict, seat={seat?.name ?? "null"}");
                }
            }
            else
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] currentMain NOT found in additionalSeatsDict");
                }
            }

            // Eject current passenger from main seat
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] Ejecting {currentMain?.name ?? "null"} from main seat");
            }
            vehicleSeat.EjectPassenger(currentMain);

            // Assign new passenger to main seat
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] Assigning {newMain?.name ?? "null"} to main seat");
            }
            vehicleSeat.AssignPassenger(newMain);

            // If current passenger was in an additional seat, we need to move it back to an additional seat
            // Or if it wasn't in an additional seat, we need to create one for it
            if (currentAdditionalSeat != null)
            {
                // Move current passenger back to its additional seat
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Moving currentMain back to its additional seat");
                }
                currentAdditionalSeat.AssignPassenger(currentMain);
            }
            else if (additionalSeats != null)
            {
                // Create a new additional seat for the displaced passenger
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Creating new additional seat for displaced passenger");
                }
                var seatObject = new GameObject($"AdditionalSeat_Cycled_{DateTime.Now.Ticks}");
                seatObject.transform.SetParent(bagController.transform);
                seatObject.transform.localPosition = Vector3.zero;
                seatObject.transform.localRotation = Quaternion.identity;
                var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
                
                // Copy settings from main seat
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
                
                // Assign the displaced passenger to the new seat
                newSeat.AssignPassenger(currentMain);
                additionalSeats[currentMain] = newSeat;
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] Created and assigned to new additional seat");
                }
            }
            else
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [BottomlessBag] No additionalSeats dict exists, passenger will remain ejected");
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [BottomlessBag] Cycle complete: {newMain.name} in main seat, {currentMain.name} displaced");
            }
        }

        #region Configuration Management

        private void SetupConfigurationEventHandlers()
        {
            debugLogsHandler = (sender, args) =>
            {
                Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;
                StateManagement.UpdateDebugLogging(PluginConfig.EnableDebugLogs.Value);
            };
            PluginConfig.EnableDebugLogs.SettingChanged += debugLogsHandler;


            blacklistHandler = (sender, args) =>
            {
                // Clear blacklist cache so it rebuilds with new value
                PluginConfig.ClearBlacklistCache();
            };
            PluginConfig.BodyBlacklist.SettingChanged += blacklistHandler;

            // Runtime updates for velocity multipliers
            forwardVelHandler = Patches.RepossessPatches.OnForwardVelocityChanged;
            PluginConfig.ForwardVelocityMultiplier.SettingChanged += forwardVelHandler;

            upwardVelHandler = Patches.RepossessPatches.OnUpwardVelocityChanged;
            PluginConfig.UpwardVelocityMultiplier.SettingChanged += upwardVelHandler;

            recoveryBlacklistHandler = (sender, args) =>
            {
                // Clear recovery blacklist cache so it rebuilds with new value
                PluginConfig.ClearRecoveryBlacklistCache();
            };
            PluginConfig.RecoveryObjectBlacklist.SettingChanged += recoveryBlacklistHandler;

            grabbableComponentTypesHandler = (sender, args) =>
            {
                // Debounce the update to avoid excessive processing while typing
                if (_grabbableComponentTypesUpdateCoroutine != null)
                {
                    Instance.StopCoroutine(_grabbableComponentTypesUpdateCoroutine);
                }
                _grabbableComponentTypesUpdateCoroutine = Instance.StartCoroutine(DelayedGrabbableComponentTypesUpdate());
            };
            PluginConfig.GrabbableComponentTypes.SettingChanged += grabbableComponentTypesHandler;

            grabbableKeywordBlacklistHandler = (sender, args) =>
            {
                // Clear grabbable keyword blacklist cache so it rebuilds with new value
                PluginConfig.ClearGrabbableKeywordBlacklistCache();
            };
            PluginConfig.GrabbableKeywordBlacklist.SettingChanged += grabbableKeywordBlacklistHandler;

            bossGrabbingHandler = (sender, args) =>
            {
                // Update existing SpecialObjectAttributes based on new boss grabbing setting
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            };
            PluginConfig.EnableBossGrabbing.SettingChanged += bossGrabbingHandler;

            npcGrabbingHandler = (sender, args) =>
            {
                // Update existing SpecialObjectAttributes based on new NPC grabbing setting
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            };
            PluginConfig.EnableNPCGrabbing.SettingChanged += npcGrabbingHandler;

            environmentGrabbingHandler = (sender, args) =>
            {
                // Update existing SpecialObjectAttributes based on new environment grabbing setting
                Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
            };
            PluginConfig.EnableEnvironmentGrabbing.SettingChanged += environmentGrabbingHandler;

            lockedObjectGrabbingHandler = (sender, args) =>
            {
                // it's checked at runtime
            };
            PluginConfig.EnableLockedObjectGrabbing.SettingChanged += lockedObjectGrabbingHandler;

            // Persistence event handlers
            persistenceHandler = (sender, args) =>
            {
                PersistenceManager.UpdateCachedConfig();
            };
            PluginConfig.EnableObjectPersistence.SettingChanged += persistenceHandler;

            autoGrabHandler = (sender, args) =>
            {
                PersistenceManager.UpdateCachedConfig();
            };
            PluginConfig.EnableAutoGrab.SettingChanged += autoGrabHandler;


            // Initialize caches
            PersistenceManager.UpdateCachedConfig();
        }

        #endregion

        #region Harmony Patching

        private void ApplyHarmonyPatches()
        {
            Harmony harmony = new Harmony("pwdcat.DrifterBossGrab");
            harmony.PatchAll();
        }

        #endregion

        #region Game Event Management

        private void RegisterGameEvents()
        {
            // Player spawn event to refresh cache
            Run.onPlayerFirstCreatedServer += OnPlayerFirstCreated;

            // Scene changes to refresh cache and handle persistence
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private static void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController pcm)
        {
            // Caching system removed - no cache to refresh
            // SpecialObjectAttributes system handles object discovery natively

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Player spawned - SpecialObjectAttributes system active");
            }
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            // Caching system removed - no cache to refresh
            // SpecialObjectAttributes system handles object discovery natively

            // Reset zone inversion detection for new stage
            Patches.OtherPatches.ResetZoneInversionDetection();

            // Handle persistence restoration
            PersistenceManager.OnSceneChanged(oldScene, newScene);

            // Ensure all grabbable objects have SpecialObjectAttributes for grabbing (delayed to allow objects to spawn)
            Instance.StartCoroutine(DelayedEnsureSpecialObjectAttributes());

            // Batch initialize SpecialObjectAttributes for better performance
            Instance.StartCoroutine(DelayedBatchSpecialObjectAttributesInitialization());

            // Scan all scene components if component analysis is enabled
            Patches.BagPatches.ScanAllSceneComponents();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Scene changed from {oldScene.name} to {newScene.name} - SpecialObjectAttributes system active");
            }
        }

        private static System.Collections.IEnumerator DelayedEnsureSpecialObjectAttributes()
        {
            // Wait one frame to allow objects to spawn
            yield return null;
            Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();
        }

        private static System.Collections.IEnumerator DelayedBatchSpecialObjectAttributesInitialization()
        {
            // Wait slightly longer than the regular ensure to allow all objects to spawn
            yield return new UnityEngine.WaitForSeconds(0.2f);

            // Batch process objects in smaller chunks to avoid frame drops
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            const int batchSize = 50; // Process 50 objects per frame

            for (int i = 0; i < allObjects.Length; i += batchSize)
            {
                int endIndex = Mathf.Min(i + batchSize, allObjects.Length);

                // Process this batch
                for (int j = i; j < endIndex; j++)
                {
                    var obj = allObjects[j];
                    if (obj != null && PluginConfig.IsGrabbable(obj))
                    {
                        Patches.GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);
                    }
                }

                // Yield to next frame if we have more batches to process
                if (endIndex < allObjects.Length)
                {
                    yield return null;
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Completed batched SpecialObjectAttributes initialization for {allObjects.Length} objects");
            }
        }

        private static System.Collections.IEnumerator DelayedGrabbableComponentTypesUpdate()
        {
            // Wait 0.5 seconds to debounce updates while typing
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Clear grabbable component types cache so it rebuilds with new value
            PluginConfig.ClearGrabbableComponentTypesCache();

            // Update existing SpecialObjectAttributes based on new setting
            Patches.GrabbableObjectPatches.EnsureAllGrabbableObjectsHaveSpecialObjectAttributes();

            _grabbableComponentTypesUpdateCoroutine = null;
        }

        #endregion

        #region Risk of Options Integration

        private void SetupRiskOfOptions()
        {
            if (!RooInstalled) return;

            ModSettingsManager.SetModDescription("Allows Drifter to grab bosses, NPCs, and environment objects.", Constants.PluginGuid, Constants.PluginName);
            
            try
            {
                byte[] array = File.ReadAllBytes(System.IO.Path.Combine(DirectoryName, "icon.png"));
                UnityEngine.Texture2D val = new UnityEngine.Texture2D(256, 256);
                UnityEngine.ImageConversion.LoadImage(val, array);
                ModSettingsManager.SetModIcon(UnityEngine.Sprite.Create(val, new UnityEngine.Rect(0f, 0f, 256f, 256f), new UnityEngine.Vector2(0.5f, 0.5f)));
            }
            catch (Exception)
            {
                // Icon loading failed - continue without icon
            }

            // Add configuration options to the Risk of Options interface
            AddConfigurationOptions();
        }

        private void AddConfigurationOptions()
        {
            if (!RooInstalled) return;

            // Grabbing Toggles
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableBossGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableNPCGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableEnvironmentGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableLockedObjectGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableProjectileGrabbing));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.ProjectileGrabbingSurvivorOnly));

            // Persistence Settings
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableObjectPersistence));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableAutoGrab));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedBosses));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedNPCs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.PersistBaggedEnvironmentObjects));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.PersistenceBlacklist));

            // Skill Settings
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.SearchRangeMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.ForwardVelocityMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.UpwardVelocityMultiplier));
            ModSettingsManager.AddOption(new StepSliderOption(PluginConfig.BreakoutTimeMultiplier));
            ModSettingsManager.AddOption(new IntSliderOption(PluginConfig.MaxSmacks));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.MassMultiplier));

            // Safety & Filtering
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.BodyBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.GrabbableComponentTypes));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.GrabbableKeywordBlacklist));
            ModSettingsManager.AddOption(new StringInputFieldOption(PluginConfig.RecoveryObjectBlacklist));

            // Debug & Development
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableDebugLogs));
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableComponentAnalysisLogs));

            // Bottomless Bag Settings (scroll wheel cycling)
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.BottomlessBagEnabled));
            ModSettingsManager.AddOption(new ChoiceOption(PluginConfig.BottomlessBagCycleOrder));
        }

        #endregion
    }
}
