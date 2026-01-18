using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using System.Reflection;
using DrifterBossGrabMod;
namespace DrifterBossGrabMod.Patches
{
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
    public static class BagPatches
    {
        internal static readonly Dictionary<DrifterBagController, List<GameObject>> baggedObjectsDict = new Dictionary<DrifterBagController, List<GameObject>>();
        internal static readonly Dictionary<DrifterBagController, Dictionary<GameObject, RoR2.VehicleSeat>> additionalSeatsDict = new Dictionary<DrifterBagController, Dictionary<GameObject, RoR2.VehicleSeat>>();
        internal static readonly Dictionary<DrifterBagController, GameObject> mainSeatDict = new Dictionary<DrifterBagController, GameObject>();
        public static void ScanAllSceneComponents()
        {
            if (!PluginConfig.EnableComponentAnalysisLogs.Value) return;
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
            if (!PluginConfig.BottomlessBagEnabled.Value)
            {
                return 1;
            }
            var body = drifterBagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                int maxStock = body.skillLocator.utility.maxStock;
                return maxStock + PluginConfig.BottomlessBagBaseCapacity.Value;
            }
            // Default to base capacity if skill not found
            return PluginConfig.BottomlessBagBaseCapacity.Value;
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
        [HarmonyPatch(typeof(DrifterBagController), "bagFull", MethodType.Getter)]
        public class DrifterBagController_get_bagFull
        {
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, ref bool __result)
            {
                // Simplified: bag is full if there's an object in the main seat (using mod's tracking).
                // This works because TryOverrideUtility controls what can be in the seat.
                // When bagFull is true and you try to grab something new:
                // 1. The old object's TryOverrideUtility is skipped (already in seat)
                // 2. The new object's TryOverrideUtility is called (replaces old)
                __result = GetMainSeatObject(__instance) != null;
            }
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
                // Skip this check if object is already tracked (reassignment/cycling)
                if (!isAlreadyTrackedByThisController && effectiveCapacity > 1)
                {
                    // Count how many objects are currently in additional seats
                    int additionalSeatsCount = 0;
                    if (additionalSeatsDict.TryGetValue(__instance, out var existingSeatDict))
                    {
                        additionalSeatsCount = existingSeatDict.Count;
                    }
                    // If we have room in additional seats (total capacity - 1 for main seat), use additional seat
                    if (additionalSeatsCount < effectiveCapacity - 1)
                    {
                        // Mark that we're using an additional seat so the postfix knows not to set main seat
                        _usingAdditionalSeat = true;
                        // Create additional seat if needed
                        if (!additionalSeatsDict.TryGetValue(__instance, out var seatDict))
                        {
                            seatDict = new Dictionary<GameObject, RoR2.VehicleSeat>();
                            additionalSeatsDict[__instance] = seatDict;
                        }
                        var seatObject = new GameObject($"AdditionalSeat_{additionalSeatsCount}");
                        seatObject.transform.SetParent(__instance.transform);
                        seatObject.transform.localPosition = Vector3.zero;
                        seatObject.transform.localRotation = Quaternion.identity;
                        var newSeat = seatObject.AddComponent<RoR2.VehicleSeat>();
                        newSeat.seatPosition = __instance.vehicleSeat.seatPosition;
                        newSeat.exitPosition = __instance.vehicleSeat.exitPosition;
                        newSeat.ejectOnCollision = __instance.vehicleSeat.ejectOnCollision;
                        newSeat.hidePassenger = __instance.vehicleSeat.hidePassenger;
                        newSeat.exitVelocityFraction = __instance.vehicleSeat.exitVelocityFraction;
                        newSeat.disablePassengerMotor = __instance.vehicleSeat.disablePassengerMotor;
                        newSeat.isEquipmentActivationAllowed = __instance.vehicleSeat.isEquipmentActivationAllowed;
                        newSeat.shouldProximityHighlight = __instance.vehicleSeat.shouldProximityHighlight;
                        newSeat.disableInteraction = __instance.vehicleSeat.disableInteraction;
                        newSeat.shouldSetIdle = __instance.vehicleSeat.shouldSetIdle;
                        newSeat.additionalExitVelocity = __instance.vehicleSeat.additionalExitVelocity;
                        newSeat.disableAllCollidersAndHurtboxes = __instance.vehicleSeat.disableAllCollidersAndHurtboxes;
                        newSeat.disableColliders = __instance.vehicleSeat.disableColliders;
                        newSeat.disableCharacterNetworkTransform = __instance.vehicleSeat.disableCharacterNetworkTransform;
                        newSeat.ejectFromSeatOnMapEvent = __instance.vehicleSeat.ejectFromSeatOnMapEvent;
                        newSeat.inheritRotation = __instance.vehicleSeat.inheritRotation;
                        newSeat.holdPassengerAfterDeath = __instance.vehicleSeat.holdPassengerAfterDeath;
                        newSeat.ejectPassengerToGround = __instance.vehicleSeat.ejectPassengerToGround;
                        newSeat.ejectRayDistance = __instance.vehicleSeat.ejectRayDistance;
                        newSeat.handleExitTeleport = __instance.vehicleSeat.handleExitTeleport;
                        newSeat.setCharacterMotorPositionToCurrentPosition = __instance.vehicleSeat.setCharacterMotorPositionToCurrentPosition;
                        newSeat.passengerState = __instance.vehicleSeat.passengerState;
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
                        return false; // Prevent original method
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
                    if (!_usingAdditionalSeat && __instance.vehicleSeat != null)
                    {
                        __instance.vehicleSeat.AssignPassenger(passengerObject);
                    }
                    // Only track this object as being in the main seat if we didn't use an additional seat
                    // If we used an additional seat, the main seat remains empty or with its previous occupant
                    if (!_usingAdditionalSeat)
                    {
                        SetMainSeatObject(__instance, passengerObject);
                    }
                    else
                    {
                        SetMainSeatObject(__instance, null);
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
                        PersistenceManager.SendBaggedObjectsPersistenceMessage(list);
                    }
                }
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
            // Check if object is being thrown (in projectile state)
            bool isThrowing = OtherPatches.IsInProjectileState(obj);
            // Remove from projectile state tracking if present
            if (isThrowing)
            {
                OtherPatches.RemoveFromProjectileState(obj);
            }
            // Remove from baggedObjectsDict - remove ALL entries matching this instance ID
            if (baggedObjectsDict.TryGetValue(controller, out var list))
            {
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
            }
            // Cleanup empty additional seats only when throwing
            if (isThrowing)
            {
                CleanupEmptyAdditionalSeats(controller);
            }
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
                    mainSeatDict.Remove(controller);
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
                Debug.Log("[CleanupEmptyAdditionalSeats] Controller is null, skipping");
                return;
            }
            var seatDict = additionalSeatsDict.TryGetValue(controller, out var tempSeatDict) ? tempSeatDict : null;
            if (seatDict == null)
            {
                Debug.Log($"[CleanupEmptyAdditionalSeats] No additional seats found for controller {controller.gameObject.name}");
            }
            else
            {
                Debug.Log($"[CleanupEmptyAdditionalSeats] Checking {seatDict.Count} additional seats for controller {controller.gameObject.name}");
            }
            var seatsToRemove = new List<GameObject>();
            if (seatDict != null)
            {
                foreach (var kvp in seatDict)
                {
                    var seat = kvp.Value;
                    var obj = kvp.Key;
                    string objName = obj != null ? obj.name : "null";
                    if (seat != null)
                    {
                        bool hasPassenger = seat.hasPassenger;
                        GameObject passenger = seat.NetworkpassengerBodyObject;
                        string passengerName = passenger != null ? passenger.name : "null";
                        Debug.Log($"[CleanupEmptyAdditionalSeats] Seat for {objName}: hasPassenger={hasPassenger}, passenger={passengerName}");
                        if (!hasPassenger)
                        {
                            // Seat is empty or passenger is thrown, destroy it
                            Debug.Log($"[CleanupEmptyAdditionalSeats] Destroying empty/thrown seat for {objName}");
                            if (seat.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(seat.gameObject);
                            }
                            seatsToRemove.Add(kvp.Key);
                        }
                        else
                        {
                            Debug.Log($"[CleanupEmptyAdditionalSeats] Seat for {objName} is occupied, keeping it");
                        }
                    }
                    else
                    {
                        Debug.Log($"[CleanupEmptyAdditionalSeats] Seat is null for {objName}, removing from dict");
                        seatsToRemove.Add(kvp.Key);
                    }
                }
                // Remove from dictionary
                Debug.Log($"[CleanupEmptyAdditionalSeats] Removing {seatsToRemove.Count} seats from dictionary");
                foreach (var obj in seatsToRemove)
                {
                    string objName = obj != null ? obj.name : "null";
                    Debug.Log($"[CleanupEmptyAdditionalSeats] Removing {objName} from seatDict");
                    seatDict.Remove(obj);
                }
                // If the seatDict is now empty, remove it from additionalSeatsDict
                if (seatDict.Count == 0)
                {
                    Debug.Log($"[CleanupEmptyAdditionalSeats] seatDict is empty, removing controller {controller.gameObject.name} from additionalSeatsDict");
                    additionalSeatsDict.Remove(controller);
                }
                else
                {
                    Debug.Log($"[CleanupEmptyAdditionalSeats] {seatDict.Count} seats remaining for controller {controller.gameObject.name}");
                }
            }
            // Also destroy untracked empty seats
            var childSeats = controller.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == controller.vehicleSeat) continue;
                bool isTracked = seatDict != null && seatDict.ContainsValue(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    Debug.Log($"[CleanupEmptyAdditionalSeats] Destroying untracked empty seat for {controller.gameObject.name}");
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
            if (bodyObject == null) return;
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
                seatDict = new Dictionary<GameObject, RoR2.VehicleSeat>();
                BagPatches.additionalSeatsDict[drifterBagController] = seatDict;
            }
            // Remove from any existing tracking first
            foreach (var kvp in seatDict.ToList())
            {
                if (kvp.Value == __instance)
                {
                    seatDict.Remove(kvp.Key);
                }
            }
            seatDict[bodyObject] = __instance;
            BagPatches.SetMainSeatObject(drifterBagController, null);
        }
    }
}