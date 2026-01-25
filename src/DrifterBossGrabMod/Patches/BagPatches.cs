using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using System.Reflection;
using DrifterBossGrabMod;
namespace DrifterBossGrabMod.Patches
{
    public interface ISeatBuilder
    {
        ISeatBuilder SetName(string name);
        ISeatBuilder SetParent(Transform parent);
        ISeatBuilder CopyFrom(VehicleSeat source);
        VehicleSeat Build();
    }

    public class AdditionalSeatBuilder : ISeatBuilder
    {
        private string _name = null!;
        private Transform _parent = null!;
        private VehicleSeat _sourceSeat = null!;

        public ISeatBuilder SetName(string name)
        {
            _name = name;
            return this;
        }

        public ISeatBuilder SetParent(Transform parent)
        {
            _parent = parent;
            return this;
        }

        public ISeatBuilder CopyFrom(VehicleSeat source)
        {
            _sourceSeat = source;
            return this;
        }

        public VehicleSeat Build()
        {
            GameObject seatObject;
            if (Networking.BagStateSync.AdditionalSeatPrefab != null)
            {
                seatObject = UnityEngine.Object.Instantiate(Networking.BagStateSync.AdditionalSeatPrefab);
                seatObject.SetActive(true);
            }
            else
            {
                seatObject = new GameObject(_name);
            }
            
            seatObject.name = _name;
            seatObject.transform.SetParent(_parent);
            seatObject.transform.localPosition = Vector3.zero;
            seatObject.transform.localRotation = Quaternion.identity;
            
            var newSeat = seatObject.GetComponent<RoR2.VehicleSeat>();
            if (newSeat == null) newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
            
            if (_sourceSeat != null)
            {
                newSeat.seatPosition = _sourceSeat.seatPosition;
                newSeat.exitPosition = _sourceSeat.exitPosition;
                newSeat.ejectOnCollision = _sourceSeat.ejectOnCollision;
                newSeat.hidePassenger = _sourceSeat.hidePassenger;
                newSeat.exitVelocityFraction = _sourceSeat.exitVelocityFraction;
                newSeat.disablePassengerMotor = _sourceSeat.disablePassengerMotor;
                newSeat.isEquipmentActivationAllowed = _sourceSeat.isEquipmentActivationAllowed;
                newSeat.shouldProximityHighlight = _sourceSeat.shouldProximityHighlight;
                newSeat.disableInteraction = _sourceSeat.disableInteraction;
                newSeat.shouldSetIdle = _sourceSeat.shouldSetIdle;
                newSeat.additionalExitVelocity = _sourceSeat.additionalExitVelocity;
                newSeat.disableAllCollidersAndHurtboxes = _sourceSeat.disableAllCollidersAndHurtboxes;
                newSeat.disableColliders = _sourceSeat.disableColliders;
                newSeat.disableCharacterNetworkTransform = _sourceSeat.disableCharacterNetworkTransform;
                newSeat.ejectFromSeatOnMapEvent = _sourceSeat.ejectFromSeatOnMapEvent;
                newSeat.inheritRotation = _sourceSeat.inheritRotation;
                newSeat.holdPassengerAfterDeath = _sourceSeat.holdPassengerAfterDeath;
                newSeat.ejectPassengerToGround = _sourceSeat.ejectPassengerToGround;
                newSeat.ejectRayDistance = _sourceSeat.ejectRayDistance;
                newSeat.handleExitTeleport = _sourceSeat.handleExitTeleport;
                newSeat.setCharacterMotorPositionToCurrentPosition = _sourceSeat.setCharacterMotorPositionToCurrentPosition;
                newSeat.passengerState = _sourceSeat.passengerState;
            }
            return newSeat;
        }
    }

    public class SeatValidationContext
    {
        public DrifterBagController? BagController { get; set; }
        public GameObject? TargetObject { get; set; }
        public GameObject? CurrentObject { get; set; }
        public List<GameObject>? ValidObjects { get; set; }
        public bool IsInNullState { get; set; }
        public SeatValidationType ValidationType { get; set; }
    }
    public enum SeatValidationType
    {
        Basic,
        Swap,
        Configuration,
        NullStateTransition
    }
    public class BaggedObjectTracker : MonoBehaviour
    {
        public DrifterBagController? controller;
        public GameObject? obj;
        public bool isRemovingManual = false;

        private void OnDestroy()
        {
            if (isRemovingManual) return;
            if (controller != null && obj != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectTracker] Object {obj.name} is being destroyed. Removing from bag of {controller.name}");
                }
                BagPatches.RemoveBaggedObject(controller, obj);
            }
        }
    }

    public static class BagPatches
    {
        private static readonly ConcurrentDictionary<DrifterBagController, List<GameObject>> _baggedObjectsDict = new ConcurrentDictionary<DrifterBagController, List<GameObject>>();
        public static ConcurrentDictionary<DrifterBagController, List<GameObject>> baggedObjectsDict => _baggedObjectsDict;

        private static readonly ConcurrentDictionary<DrifterBagController, ConcurrentDictionary<GameObject, RoR2.VehicleSeat>> _additionalSeatsDict = new ConcurrentDictionary<DrifterBagController, ConcurrentDictionary<GameObject, RoR2.VehicleSeat>>();
        public static ConcurrentDictionary<DrifterBagController, ConcurrentDictionary<GameObject, RoR2.VehicleSeat>> additionalSeatsDict => _additionalSeatsDict;

        private static readonly ConcurrentDictionary<DrifterBagController, GameObject> _mainSeatDict = new ConcurrentDictionary<DrifterBagController, GameObject>();
        public static ConcurrentDictionary<DrifterBagController, GameObject> mainSeatDict => _mainSeatDict;
        public static void ScanAllSceneComponents()
        {
            if (!PluginConfig.Instance.EnableComponentAnalysisLogs.Value) return;
            Log.Info($"[ScanAllSceneComponents] === SCANNING ALL COMPONENTS IN CURRENT SCENE ===");
            // Get all root GameObjects in the scene
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            // Collect all unique component types
            var componentTypes = new HashSet<string>();
            foreach (var rootObj in rootObjects)
            {
                // Recursively scan all objects
                ScanObjectComponents(rootObj, componentTypes);
            }
            Log.Info($"[ScanAllSceneComponents] === UNIQUE COMPONENT TYPES FOUND ({componentTypes.Count}) ===");
            foreach (var type in componentTypes.OrderBy(t => t))
            {
                Log.Info($"[ScanAllSceneComponents] {type}");
            }
            Log.Info($"[ScanAllSceneComponents] === END SCENE COMPONENT SCAN ===");
        }
        public static void ClearCaches()
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[BagPatches] Clearing all bag caches");
            }
            baggedObjectsDict.Clear();
            additionalSeatsDict.Clear();
            mainSeatDict.Clear();
        }

        [HarmonyPatch(typeof(Run), "Start")]
        public class Run_Start_Patch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                ClearCaches();
            }
        }
        private static void ScanObjectComponents(GameObject obj, HashSet<string> componentTypes)
        {
            if (obj == null) return;
            // Get all components on this object
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component != null)
                {
                    componentTypes.Add(component.GetType().Name);
                }
            }
            // Recursively scan children
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                ScanObjectComponents(obj.transform.GetChild(i).gameObject, componentTypes);
            }
        }
        public static int GetUtilityMaxStock(DrifterBagController drifterBagController)
        {
            if (!PluginConfig.Instance.BottomlessBagEnabled.Value)
            {
                return 1;
            }
            var body = drifterBagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                int maxStock = body.skillLocator.utility.maxStock;
                return maxStock + PluginConfig.Instance.BottomlessBagBaseCapacity.Value;
            }
            // Default to base capacity if skill not found
            return PluginConfig.Instance.BottomlessBagBaseCapacity.Value;
        }
        private static bool HasActiveTeleporterInScene(GameObject excludeTeleporter)
        {
            var allTeleporters = UnityEngine.Object.FindObjectsByType<RoR2.TeleporterInteraction>(FindObjectsSortMode.None);
            foreach (var teleporter in allTeleporters)
            {
                if (teleporter.gameObject != excludeTeleporter && teleporter.enabled && !PersistenceManager.ShouldDisableTeleporter(teleporter.gameObject))
                {
                    return true;
                }
            }
            return false;
        }
        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger
        {
            // Static field to track if we're using an additional seat for the current call
            // This is used to communicate between prefix and postfix
            private static bool _usingAdditionalSeat = false;
            [HarmonyPrefix]
            public static bool Prefix(DrifterBagController __instance, GameObject passengerObject)
            {
                // Reset tracking flag for this call
                _usingAdditionalSeat = false;
                // Check blacklist first - return false to prevent grabbing blacklisted objects
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject.name))
                {
                    return false;
                }
                if (passengerObject == null) return true;
                CharacterBody? body = null;
                var localDisabledStates = new Dictionary<GameObject, bool>();
                // Ensure autoUpdateModelTransform is true so model follows GameObject if ModelLocator exists
                var modelLocator = passengerObject.GetComponent<ModelLocator>();
                if (modelLocator != null && !modelLocator.autoUpdateModelTransform)
                {
                    // Add ModelStatePreserver to store original state before modifying
                    var statePreserver = passengerObject.AddComponent<ModelStatePreserver>();
                    modelLocator.autoUpdateModelTransform = true;
                }
                // Cache component lookups
                body = passengerObject.GetComponent<CharacterBody>();
                if (body)
                {
                    // Validate CharacterBody state to prevent crashes with corrupted objects
                    if (body.baseMaxHealth <= 0 || body.levelMaxHealth < 0 ||
                        body.teamComponent == null || body.teamComponent.teamIndex < 0)
                    {
                        return false; // Prevent grabbing
                    }
                    // Eject ungrabbable enemies from vehicles before assigning
                    if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && body.currentVehicle != null)
                    {
                        body.currentVehicle.EjectPassenger(passengerObject);
                    }
                }
                // Disable all colliders on enemies to prevent movement bugs for flying bosses
                if (body != null && body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable))
                {
                    StateManagement.DisableMovementColliders(passengerObject, localDisabledStates);
                }
                // Special handling for teleporters - disable if there's another active teleporter
                var teleporterInteraction = passengerObject.GetComponent<RoR2.TeleporterInteraction>();
                if (teleporterInteraction != null)
                {
                    // Check if there's another teleporter in the scene that is not disabled
                    bool hasActiveTeleporter = HasActiveTeleporterInScene(passengerObject);
                    if (hasActiveTeleporter)
                    {
                        teleporterInteraction.enabled = false;
                        PersistenceManager.MarkTeleporterForDisabling(passengerObject);
                    }
                }
                // Track the bagged object for persistence
                PersistenceObjectsTracker.TrackBaggedObject(passengerObject);
                // Get effective capacity
                int effectiveCapacity = GetUtilityMaxStock(__instance);
                // Initialize list if needed
                if (!baggedObjectsDict.TryGetValue(__instance, out var list))
                {
                    list = new List<GameObject>();
                    baggedObjectsDict[__instance] = list;
                }
                // Count objects that are actually in the bag (not in projectile state)
                // Use instance ID to avoid counting duplicates
                var countedInstanceIds = new HashSet<int>();
                int objectsInBag = 0;
                foreach (var obj in list)
                {
                    if (obj != null && !OtherPatches.IsInProjectileState(obj))
                    {
                        int instanceId = obj.GetInstanceID();
                        if (!countedInstanceIds.Contains(instanceId))
                        {
                            countedInstanceIds.Add(instanceId);
                            objectsInBag++;
                        }
                    }
                }
                int passengerInstanceId = passengerObject.GetInstanceID();
                bool isAlreadyTrackedByThisController = false;
                foreach (var trackedObj in list)
                {
                    if (trackedObj != null && trackedObj.GetInstanceID() == passengerInstanceId)
                    {
                        isAlreadyTrackedByThisController = true;
                        break;
                    }
                }
                // Check if bag is full (only count objects not in projectile state)
                // Skip this check if object is already tracked (reassignment/cycling)
                if (!isAlreadyTrackedByThisController && objectsInBag >= effectiveCapacity)
                {
                    return false;
                }


                // Use main seat for single capacity, additional seats for capacity > 1
                // Use main seat for single capacity, additional seats for capacity > 1
                // Skip this check if object is already tracked (reassignment/cycling)
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AssignPassenger] Config Valid: {PluginConfig.Instance != null}");
                    if (PluginConfig.Instance != null)
                    {
                        Log.Info($"[AssignPassenger] BottomlessBagEnabled: {PluginConfig.Instance.BottomlessBagEnabled.Value}");
                    }
                    Log.Info($"[AssignPassenger] isAlreadyTracked: {isAlreadyTrackedByThisController}, effectiveCapacity: {effectiveCapacity}, objectsInBag: {objectsInBag}");
                    if (__instance.vehicleSeat != null)
                    {
                        Log.Info($"[AssignPassenger] MainSeat hasPassenger: {__instance.vehicleSeat.hasPassenger}");
                        if (__instance.vehicleSeat.hasPassenger)
                        {
                            Log.Info($"[AssignPassenger] MainSeat Occupant: {__instance.vehicleSeat.NetworkpassengerBodyObject?.name ?? "null"}");
                        }
                    }
                }

                if (!isAlreadyTrackedByThisController && effectiveCapacity > 1)
                {
                    // Count how many objects are currently in additional seats
                    int additionalSeatsCount = 0;
                    if (additionalSeatsDict.TryGetValue(__instance, out var existingSeatDict))
                    {
                        additionalSeatsCount = existingSeatDict.Count;
                    }
                    
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                         Log.Info($"[AssignPassenger] additionalSeatsCount: {additionalSeatsCount}, effectiveCapacity - 1: {effectiveCapacity - 1}");
                    }

                    // If we have room in additional seats (total capacity - 1 for main seat), use additional seat
                    if (additionalSeatsCount < effectiveCapacity - 1)
                    {
                        // Mark that we're using an additional seat so the postfix knows not to set main seat
                        _usingAdditionalSeat = true;
                        // Create additional seat if needed
                        if (!additionalSeatsDict.TryGetValue(__instance, out var seatDict))
                        {
                            seatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>();
                            additionalSeatsDict[__instance] = seatDict;
                        }

                        var newSeat = BottomlessBagPatches.FindOrCreateEmptySeat(__instance, ref seatDict);
                        if (newSeat != null)
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[AssignPassenger] Found/Created additional seat. Assigning {passengerObject.name}");
                            }
                            
                            // Add tracker
                            AddTracker(__instance, passengerObject);

                            // Ensure it's not tracked as main seat if we're putting it in an additional seat.
                            // This prevents stale state from causing UpdateNetworkBagState to report it's the selected item.
                            if (GetMainSeatObject(__instance) == passengerObject)
                            {
                                SetMainSeatObject(__instance, null);
                            }

                            newSeat.AssignPassenger(passengerObject);
                            seatDict[passengerObject] = newSeat;
                            
                            // Add to list if not already present (check by instance ID)
                            bool alreadyInList = false;
                            foreach (var existingObj in list)
                            {
                                if (existingObj != null && existingObj.GetInstanceID() == passengerInstanceId)
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }
                            if (!alreadyInList)
                            {
                                list.Add(passengerObject);
                            }
                            // Sync network state and send persistence message since postfix won't run
                            if (UnityEngine.Networking.NetworkServer.active)
                            {
                                PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                            }
                            UpdateCarousel(__instance);
                            UpdateNetworkBagState(__instance);
                            return false; // Prevent original method (which puts it in main seat)
                        }
                        else
                        {
                             if (PluginConfig.Instance.EnableDebugLogs.Value)
                             {
                                 Log.Warning($"[AssignPassenger] FindOrCreateEmptySeat returned NULL! Fallback to Main Seat.");
                             }
                        }
                    }
                    else
                    {
                         if (PluginConfig.Instance.EnableDebugLogs.Value)
                         {
                             Log.Info($"[AssignPassenger] No room in additional seats. Fallback to Main Seat (Overwrite).");
                         }
                    }
                }
                else
                {
                     if (PluginConfig.Instance.EnableDebugLogs.Value)
                     {
                         Log.Info($"[AssignPassenger] Capacity Check Failed or Already Tracked. Fallback to Main Seat.");
                     }
                }
                // If no additional seats available or capacity is 1, use main seat
                // Otherwise, allow original method to assign to main seat
                return true;
            }
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (passengerObject != null)
                {
                    // Don't add to tracking if object is already in projectile state
                    // (this can happen if the object was thrown and then somehow grabbed again)
                    if (OtherPatches.IsInProjectileState(passengerObject))
                    {
                        return;
                    }

                    // Add tracker
                    AddTracker(__instance, passengerObject);

                    if (!_usingAdditionalSeat && __instance.vehicleSeat != null && NetworkServer.active)
                    {
                        __instance.vehicleSeat.AssignPassenger(passengerObject);
                    }
                    // Only track this object as being in the main seat if we didn't use an additional seat
                    // If we used an additional seat, the main seat remains empty or with its previous occupant
                    if (!_usingAdditionalSeat)
                    {
                        SetMainSeatObject(__instance, passengerObject);
                        // Remove from additional seats if it was there (e.g., when cycling to main)
                        if (additionalSeatsDict.TryGetValue(__instance, out var seatDict))
                        {
                            System.Collections.Generic.CollectionExtensions.Remove(seatDict, passengerObject, out _);
                        }
                    }

                    // Add to list if not already added (for main seat assignment)
                    if (!baggedObjectsDict.TryGetValue(__instance, out var list))
                    {
                        list = new List<GameObject>();
                        baggedObjectsDict[__instance] = list;
                    }
                    // Check by instance ID to prevent duplicates
                    bool alreadyTracked = false;
                    int passengerInstanceId = passengerObject.GetInstanceID();
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
                        list.Add(passengerObject);
                    }
                    // Send persistence message for all bagged objects
                    if (UnityEngine.Networking.NetworkServer.active)
                    {
                        PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                    }
                    // Update carousel
                    UpdateCarousel(__instance);
                    // Sync network state after grab
                    UpdateNetworkBagState(__instance);
                }
            }
        }

        public static void AddTracker(DrifterBagController controller, GameObject obj)
        {
            if (obj == null || controller == null) return;
            var tracker = obj.GetComponent<BaggedObjectTracker>();
            if (tracker == null)
            {
                tracker = obj.AddComponent<BaggedObjectTracker>();
                tracker.controller = controller;
                tracker.obj = obj;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AddTracker] Added BaggedObjectTracker to {obj.name} for {controller.name}");
                }
            }
        }
        public static void UpdateCarousel(DrifterBagController controller, int direction = 0)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[UpdateCarousel] Called for controller: {controller?.name ?? "null"} with direction {direction}");
            }
            var carousels = UnityEngine.Object.FindObjectsByType<UI.BaggedObjectCarousel>(FindObjectsSortMode.None);
            foreach (var carousel in carousels)
            {
                carousel.PopulateCarousel(direction);
            }
            
            // NOTE: Do NOT call UpdateNetworkBagState here! It causes an infinite loop.
            // Network state is synced explicitly in AssignPassenger, CycleToNextObject, and RemoveBaggedObject.
        }

        public static void UpdateNetworkBagState(DrifterBagController? controller)
        {
            if (controller == null || (!NetworkServer.active && !controller.hasAuthority)) return;

            var netController = controller.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController != null)
            {
                if (!baggedObjectsDict.TryGetValue(controller, out var baggedObjects))
                {
                    baggedObjects = new List<GameObject>();
                }
                
                var additionalSeats = new List<GameObject>();
                if (additionalSeatsDict.TryGetValue(controller, out var seatDict))
                {
                    // Send the VehicleSeat GameObjects (values), not the grabbed objects (keys)
                    foreach (var seat in seatDict.Values)
                    {
                        if (seat != null) additionalSeats.Add(seat.gameObject);
                    }
                }

                // Calculate selected index
                int selectedIndex = -1;
                var mainPassenger = GetMainSeatObject(controller);
                
                if (mainPassenger != null)
                {
                    for (int i = 0; i < baggedObjects.Count; i++)
                    {
                        if (baggedObjects[i] != null && baggedObjects[i].GetInstanceID() == mainPassenger.GetInstanceID())
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }
                
                netController.SetBagState(selectedIndex, baggedObjects, additionalSeats);
            }
        }

        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj)
        {
            if (obj == null) return;
            // Check if we're in the middle of a swap operation - if so, skip removal
            // The swap logic in Plugin.cs handles seat reassignment properly
            if (DrifterBossGrabPlugin.IsSwappingPassengers)
            {
                return;
            }

            // Check if the object being removed is the currently selected one
            GameObject? mainPassengerBefore = GetMainSeatObject(controller);
            bool wasMainPassenger = (mainPassengerBefore != null && mainPassengerBefore == obj);

            // Check if object is being thrown (in projectile state)
            bool isThrowing = OtherPatches.IsInProjectileState(obj);

            // Remove from baggedObjectsDict - remove ALL entries matching this instance ID
            if (baggedObjectsDict.TryGetValue(controller, out var list))
            {
                // Prevent recursion if this was called by the tracker or if we're doing it manually
                var tracker = obj.GetComponent<BaggedObjectTracker>();
                if (tracker != null)
                {
                    tracker.isRemovingManual = true;
                    UnityEngine.Object.Destroy(tracker);
                }

                int targetInstanceId = obj.GetInstanceID();
                var objectsToRemove = new List<GameObject>();
                // First pass: find all objects with matching instance ID
                foreach (var trackedObj in list)
                {
                    if (trackedObj != null && trackedObj.GetInstanceID() == targetInstanceId)
                    {
                        objectsToRemove.Add(trackedObj);
                    }
                }
                // Second pass: remove all found objects
                foreach (var objToRemove in objectsToRemove)
                {
                    list.Remove(objToRemove);
                }

                // If the object being removed was tracked as the main passenger, clear that tracking.
                if (wasMainPassenger)
                {
                    SetMainSeatObject(controller, null);
                }
            }
            // Cleanup empty additional seats only when throwing
            if (isThrowing)
            {
                CleanupEmptyAdditionalSeats(controller);
            }
            // Send updated persistence message after removal
            if (UnityEngine.Networking.NetworkServer.active)
            {
                PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, controller);
            }
            
            // Update carousel
            // If we threw the selected item, animate to the next item (or empty slot)
            UpdateCarousel(controller, wasMainPassenger ? 1 : 0);
            
            // Sync network state after removal
            UpdateNetworkBagState(controller);
        }
        public static bool IsBaggedObject(DrifterBagController controller, GameObject obj)
        {
            if (obj == null || controller == null) return false;
            if (baggedObjectsDict.TryGetValue(controller, out var list))
            {
                int targetInstanceId = obj.GetInstanceID();
                foreach (var trackedObj in list)
                {
                    if (trackedObj != null && trackedObj.GetInstanceID() == targetInstanceId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        public static RoR2.VehicleSeat? GetAdditionalSeat(DrifterBagController controller, GameObject obj)
        {
            if (obj == null || controller == null) return null;
            if (additionalSeatsDict.TryGetValue(controller, out var seatDict))
            {
                if (seatDict.TryGetValue(obj, out var seat))
                {
                    return seat;
                }
            }
            return null;
        }
        public static void SetMainSeatObject(DrifterBagController controller, GameObject? obj)
        {
            if (controller == null) return;
            if (obj != null)
            {
                mainSeatDict[controller] = obj;
            }
            else
            {
                if (mainSeatDict.ContainsKey(controller))
                {
                    System.Collections.Generic.CollectionExtensions.Remove(mainSeatDict, controller, out _);
                }
            }
        }
        public static GameObject? GetMainSeatObject(DrifterBagController controller)
        {
            if (controller == null) return null;
            if (mainSeatDict.TryGetValue(controller, out var obj))
            {
                return obj;
            }
            // Fallback: Check vehicle seat directly (useful for clients when sync happened but dict not updated)
            if (controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
            {
                return controller.vehicleSeat.NetworkpassengerBodyObject;
            }
            return null;
        }
        public static int GetCurrentBaggedCount(DrifterBagController controller)
        {
            if (controller == null) return 0;
            if (!baggedObjectsDict.TryGetValue(controller, out var list))
            {
                return 0;
            }
            var countedInstanceIds = new HashSet<int>();
            int objectsInBag = 0;
            foreach (var obj in list)
            {
                if (obj != null && !OtherPatches.IsInProjectileState(obj))
                {
                    int instanceId = obj.GetInstanceID();
                    if (!countedInstanceIds.Contains(instanceId))
                    {
                        countedInstanceIds.Add(instanceId);
                        objectsInBag++;
                    }
                }
            }
            return objectsInBag;
        }
        public static bool HasRoomForGrab(DrifterBagController controller)
        {
            if (controller == null) return false;
            int effectiveCapacity = GetUtilityMaxStock(controller);
            int currentCount = GetCurrentBaggedCount(controller);
            return currentCount < effectiveCapacity;
        }
        public static void CleanupEmptyAdditionalSeats(DrifterBagController controller)
        {
            if (controller == null)
            {
                return;
            }
            var seatDict = additionalSeatsDict.TryGetValue(controller, out var tempSeatDict) ? tempSeatDict : null;
            var seatsToRemove = new List<GameObject>();
            if (seatDict != null)
            {
                foreach (var kvp in seatDict)
                {
                    var seat = kvp.Value;
                    var obj = kvp.Key;
                    if (seat != null)
                    {
                        bool hasPassenger = seat.hasPassenger;
                        GameObject passenger = seat.NetworkpassengerBodyObject;
                        if (!hasPassenger)
                        {
                            // Seat is empty or passenger is thrown, destroy it
                            if (NetworkServer.active)
                            {
                                NetworkServer.UnSpawn(seat.gameObject);
                            }
                            if (seat.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(seat.gameObject);
                            }
                            seatsToRemove.Add(kvp.Key);
                        }
                    }
                    else
                    {
                        seatsToRemove.Add(kvp.Key);
                    }
                }
                // Remove from dictionary
                foreach (var obj in seatsToRemove)
                {
                    seatDict.TryRemove(obj, out _);
                }
                // If the seatDict is now empty, remove it from additionalSeatsDict
                if (seatDict.Count == 0)
                {
                    System.Collections.Generic.CollectionExtensions.Remove(additionalSeatsDict, controller, out _);
                }
            }
            // Also destroy untracked empty seats
            var childSeats = controller.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == controller.vehicleSeat) continue;
                bool isTracked = seatDict != null && seatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.UnSpawn(childSeat.gameObject);
                    }
                    UnityEngine.Object.Destroy(childSeat.gameObject);
                }
            }
        }
    }
    // Postfix for VehicleSeat.AssignPassenger to handle additional seat assignments
    [HarmonyPatch(typeof(RoR2.VehicleSeat), nameof(RoR2.VehicleSeat.AssignPassenger))]
    public static class VehicleSeat_AssignPassenger_Postfix
    {
        [HarmonyPostfix]
        public static void Postfix(RoR2.VehicleSeat __instance, GameObject bodyObject)
        {
            if (bodyObject == null || !NetworkServer.active) return;
            // Check if this is an additional seat (not the main seat)
            var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
            if (drifterBagController == null) return;
            // Check if this is the main seat
            if (__instance == drifterBagController.vehicleSeat)
            {
                // This is the main seat - let the main logic handle it
                return;
            }
            // This is an additional seat
            // Track this object in the additional seat
            if (!BagPatches.additionalSeatsDict.TryGetValue(drifterBagController, out var seatDict))
            {
                seatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>();
                BagPatches.additionalSeatsDict[drifterBagController] = seatDict;
            }
            // Remove from any existing tracking first
            foreach (var kvp in seatDict.ToList())
            {
                if (kvp.Value == __instance)
                {
                    System.Collections.Generic.CollectionExtensions.Remove(seatDict, kvp.Key, out _);
                }
            }
            seatDict[bodyObject] = __instance;
        }
    }

    [HarmonyPatch(typeof(GlobalEventManager), nameof(GlobalEventManager.OnCharacterDeath))]
    public static class GlobalEventManager_OnCharacterDeath
    {
        [HarmonyPostfix]
        public static void Postfix(DamageReport damageReport)
        {
            if (damageReport == null || damageReport.victimBody == null) return;
            GameObject victim = damageReport.victimBody.gameObject;

            // Search all bags for this victim
            foreach (var kvp in BagPatches.baggedObjectsDict)
            {
                var controller = kvp.Key;
                var list = kvp.Value;

                if (list != null && list.Contains(victim))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[GlobalEventManager_OnCharacterDeath] Bagged object {victim.name} died. Removing from bag of {controller.name}");
                    }
                    BagPatches.RemoveBaggedObject(controller, victim);
                }
            }
        }
    }
}
