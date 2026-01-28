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
using EntityStates;
using EntityStates.Drifter.Bag;
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
        private int _cachedInstanceId;

        private void Start()
        {
            if (obj != null) _cachedInstanceId = obj.GetInstanceID();
        }

        private void OnDestroy()
        {
            if (isRemovingManual) return;
            
            // Log destruction for debugging
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BaggedObjectTracker] OnDestroy called for InstanceID: {_cachedInstanceId}");
            }

            // Always attempt to untrack from persistence first
            if (!ReferenceEquals(obj, null))
            {
                PersistenceObjectsTracker.UntrackBaggedObject(obj, true);
            }


            if (!ReferenceEquals(controller, null) && !ReferenceEquals(obj, null))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectTracker] Object {(obj ? obj.name : "Destroyed")} is being destroyed. Removing from bag of {(controller ? controller.name : "DestroyedController")}");
                }
                BagPatches.RemoveBaggedObject(controller, obj, true);
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
        public static int GetBaggedObjectCount(DrifterBagController controller)
        {
            if (controller == null) return 0;
            if (baggedObjectsDict.TryGetValue(controller, out var list))
            {
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
            return 0;
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
                if (modelLocator != null)
                {
                    // Add ModelStatePreserver to store original state before modifying/stashing
                    if (passengerObject.GetComponent<ModelStatePreserver>() == null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($" [AssignPassenger] Adding ModelStatePreserver to {passengerObject.name} before stashing");
                        passengerObject.AddComponent<ModelStatePreserver>();
                    }

                    if (!modelLocator.autoUpdateModelTransform)
                    {
                        modelLocator.autoUpdateModelTransform = true;
                    }
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
                // Get objects count that are actually in the bag (not in projectile state)
                int objectsInBag = GetBaggedObjectCount(__instance);
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




                // If capacity is > 1, try to stash (use Additional Seat) first
                // This keeps the Main Seat empty, allowing the "Grab" skill to persist for chaining
                if (!isAlreadyTrackedByThisController && effectiveCapacity > 1)
                {
                    // Check if we have an empty additional seat
                    if (!additionalSeatsDict.TryGetValue(__instance, out var seatDict))
                    {
                        seatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>();
                        additionalSeatsDict[__instance] = seatDict;
                    }

                    var newSeat = BottomlessBagPatches.FindOrCreateEmptySeat(__instance, ref seatDict);
                    
                    // If no empty seat found, but we are below total capacity, check if Main Seat is empty
                    // If Main Seat is empty, we should fill it last according to prioritize rule
                    // But if Additional Seats are full, we use Main Seat
                    
                    if (newSeat != null)
                    {

                        
                        _usingAdditionalSeat = true;
                        AddTracker(__instance, passengerObject);
                        
                        // Ensure it's not tracked as main seat
                        if (GetMainSeatObject(__instance) == passengerObject)
                        {
                            SetMainSeatObject(__instance, null);
                        }

                        if (NetworkServer.active)
                        {
                            newSeat.AssignPassenger(passengerObject);
                        }
                        seatDict[passengerObject] = newSeat;
                        
                        if (!list.Any(o => o != null && o.GetInstanceID() == passengerInstanceId))
                        {
                            list.Add(passengerObject);
                        }

                        if (NetworkServer.active)
                        {
                            PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                        }
                        UpdateCarousel(__instance);
                        if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                        {
                            UpdateNetworkBagState(__instance, 0);
                        }
                        ForceRecalculateMass(__instance);
                        return false; // Skip original (keeps Main Seat empty)
                    }


                        _usingAdditionalSeat = true;
                        AddTracker(__instance, passengerObject);
                        
                        // Parent to bag so it moves with us
                        passengerObject.transform.SetParent(__instance.transform);
                        passengerObject.transform.localPosition = Vector3.zero;
                        
                        // Disable colliders to prevent "jank" collisions
                        // We use a discard dictionary because we don't need to restore locally 
                        // (Server sync + Seat logic will handle state eventually)
                        var discardDict = new Dictionary<GameObject, bool>();
                        StateManagement.DisableMovementColliders(passengerObject, discardDict);
                        
                        // Also set rb isKinematic if present
                        var rb = passengerObject.GetComponent<Rigidbody>();
                        if (rb) rb.isKinematic = true;

                        // Add to local list so it shows up in UI immediately
                        if (!list.Any(o => o != null && o.GetInstanceID() == passengerInstanceId))
                        {
                            list.Add(passengerObject);
                        }
                        
                        UpdateCarousel(__instance);
                        ForceRecalculateMass(__instance);
                        
                        return false; 
                    }
                    else
                    {

                    }
                }
                
                // Fallback to Main Seat (original method) if additional seats are full or capacity is 1.
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
                    if (!DrifterBossGrabPlugin.IsSwappingPassengers)
                    {
                        UpdateNetworkBagState(__instance, 0);
                    }
                    ForceRecalculateMass(__instance);
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
                tracker.obj = obj;
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AddTracker] Added BaggedObjectTracker to {obj.name} for {controller.name}");
                }
            }
            
            // ALWAYS update the controller reference, as it might have changed (e.g., scene transition)
            // If the old controller is null/destroyed, we definitely need the new one.
            if (tracker.controller != controller)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AddTracker] Updating controller reference for {obj.name} from {(tracker.controller ? tracker.controller.name : "null/destroyed")} to {controller.name}");
                }
                tracker.controller = controller;
            }
        }
        public static void UpdateCarousel(DrifterBagController controller, int direction = 0)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                string controllerName = "null";
                try 
                {
                     // Safe check for Unity Object destruction
                     if ((object)controller != null && controller != null) 
                        controllerName = controller.name;
                     else 
                        controllerName = "Destroyed/Null";
                }
                catch { controllerName = "ErrorGettingName"; }

                Log.Info($"[UpdateCarousel] Called for controller: {controllerName} with direction {direction}");
            }
            var carousels = UnityEngine.Object.FindObjectsByType<UI.BaggedObjectCarousel>(FindObjectsSortMode.None);
            foreach (var carousel in carousels)
            {
                carousel.PopulateCarousel(direction);
            }
            

        }

        public static void UpdateNetworkBagState(DrifterBagController? controller, int direction = 0)
        {
            // Use ReferenceEquals to check for null instance, but also check if Unity object is destroyed
            if (ReferenceEquals(controller, null) || (controller is UnityEngine.Object uController && !uController)) return;

            if (!NetworkServer.active && !controller.hasAuthority) return;

            var netController = controller.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController != null)
            {
                if (!baggedObjectsDict.TryGetValue(controller, out var baggedObjects))
                {
                    baggedObjects = new List<GameObject>();
                }
                
                // Filter out null/destroyed objects before sending to clients
                // Use robust null check
                baggedObjects.RemoveAll(obj => ReferenceEquals(obj, null) || (obj is UnityEngine.Object uo && !uo));

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
                
                netController.SetBagState(selectedIndex, baggedObjects, additionalSeats, direction);
            }
        }

        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false)
        {
            // Use ReferenceEquals to allow processing of destroyed Unity objects
            if (ReferenceEquals(obj, null)) return;
            
            // Safe InstanceID retrieval
            int targetInstanceId;
            try 
            {
                targetInstanceId = obj.GetInstanceID();
            }
            catch 
            {
                // If we can't get InstanceID (object completely gone), relying on reference equality in RemoveAll
                targetInstanceId = -1;
            }


            if (DrifterBossGrabPlugin.IsSwappingPassengers)
            {
                return;
            }

            // Check if the object being removed is the currently selected one
            GameObject? mainPassengerBefore = GetMainSeatObject(controller);
            bool wasMainPassenger = (mainPassengerBefore != null && mainPassengerBefore == obj);

            // Force cleanup from mainSeatDict if it matches, regardless of wasMainPassenger check
            if (mainPassengerBefore != null && mainPassengerBefore.GetInstanceID() == obj.GetInstanceID())
            {
               SetMainSeatObject(controller, null);
               wasMainPassenger = true;
            }


            if (additionalSeatsDict.TryGetValue(controller, out var seatDict))
            {
                 // Remove by GameObject key
                 if (seatDict.ContainsKey(obj))
                 {
                     System.Collections.Generic.CollectionExtensions.Remove(seatDict, obj, out _);
                 }
                 // Double check values (seats) just in case
                 var toRemove = new List<GameObject>();
                 foreach(var kvp in seatDict) 
                 {
                     if(kvp.Value != null && kvp.Value.NetworkpassengerBodyObject == obj)
                     {
                         toRemove.Add(kvp.Key);
                     }
                 }
                 foreach(var key in toRemove)
                 {
                     System.Collections.Generic.CollectionExtensions.Remove(seatDict, key, out _);
                 }
            }

            // Check if object is being thrown (in projectile state)
            bool isThrowing = OtherPatches.IsInProjectileState(obj);

            // Remove from baggedObjectsDict - remove ALL entries matching this instance ID
            if (baggedObjectsDict.TryGetValue(controller, out List<GameObject> list))
            {

                try {
                    var tracker = obj.GetComponent<BaggedObjectTracker>();
                    if (tracker != null)
                    {
                        tracker.isRemovingManual = true;
                        UnityEngine.Object.Destroy(tracker);
                    }
                } catch { /* Ignore if object is too far gone */ }

                // Robust removal: remove by instance ID or if null/destroyed using ReferenceEquals
                list.RemoveAll(x => ReferenceEquals(x, null) || (x is UnityEngine.Object uo && !uo) || (targetInstanceId != -1 && x.GetInstanceID() == targetInstanceId));

                // If the object being removed was tracked as the main passenger, clear that tracking.
                if (wasMainPassenger)
                {
                    // Force eject the object from the vehicle seat if it's still there
                    if (NetworkServer.active && controller.vehicleSeat != null && controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RemoveBaggedObject] Force ejecting {(obj ? obj.name : "null")} from Main Seat to clear it (isThrowing: {isThrowing}, isDestroying: {isDestroying})");
                        }
                        
                        if (isDestroying)
                        {
                            // Should catch NRE in VehicleSeat.OnPassengerExit because the object is partially destroyed
                            try
                            {
                                controller.vehicleSeat.EjectPassenger(obj);
                            }
                            catch (Exception ex)
                            {
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($"[RemoveBaggedObject] Suppressed expected exception during ejection of destroying object: {ex.GetType().Name} - {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            controller.vehicleSeat.EjectPassenger(obj);
                        }
                    }

                    SetMainSeatObject(controller, null);


                    if (PluginConfig.Instance.AutoPromoteMainSeat.Value && list.Count > 0 && (NetworkServer.active || (controller && controller.hasAuthority)))
                    {
                        var newMain = list[0];
                        if (newMain != null && !OtherPatches.IsInProjectileState(newMain))
                        {
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[RemoveBaggedObject] Auto-promoting {(newMain ? newMain.name : "null")} to Main Seat after removal of previous main.");
                            }
                            
                            // If it's in an additional seat, we should eject it first so it can move to main
                            if (additionalSeatsDict.TryGetValue(controller, out var promotionSeatDict))
                            {
                                if (promotionSeatDict.ContainsKey(newMain))
                                {
                                    // Find the seat using value check to be sure
                                    foreach (var kvp in promotionSeatDict)
                                    {
                                        if (kvp.Key == newMain && kvp.Value != null)
                                        {
                                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                                Log.Info($"[RemoveBaggedObject] Ejecting {(newMain ? newMain.name : "null")} from additional seat before promoting.");
                                            
                                            if (NetworkServer.active)
                                            {
                                                kvp.Value.EjectPassenger(newMain);
                                            }
                                            promotionSeatDict.TryRemove(newMain, out _);
                                            break;
                                        }
                                    }
                                }
                            }
                            
                            // Assign to Main Seat
                            if (NetworkServer.active)
                            {
                                controller.AssignPassenger(newMain);
                                
                                // Verify assignment
                                if (controller.vehicleSeat != null)
                                {
                                    if (controller.vehicleSeat.NetworkpassengerBodyObject != newMain)
                                    {
                                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"[RemoveBaggedObject] WARNING: AssignPassenger failed to seat {(newMain ? newMain.name : "null")}. Seat occupant: {(controller.vehicleSeat.NetworkpassengerBodyObject ? controller.vehicleSeat.NetworkpassengerBodyObject.name : "null")}");
                                            Log.Info($"[RemoveBaggedObject] Forcing direct VehicleSeat.AssignPassenger...");
                                        }
                                        controller.vehicleSeat.AssignPassenger(newMain);
                                        
                                        if (controller.vehicleSeat.NetworkpassengerBodyObject != newMain && PluginConfig.Instance.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"[RemoveBaggedObject] Forced assignment also failed!");
                                        }
                                    }
                                    else if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    {
                                        Log.Info($"[RemoveBaggedObject] Successfully seated {(newMain ? newMain.name : "null")} in Main Seat.");
                                    }
                                }
                            }
                            else if (controller.hasAuthority)
                            {
                                // Client-side prediction for auto-promotion
                                SetMainSeatObject(controller, newMain);
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                {
                                    Log.Info($"[RemoveBaggedObject] Client-side auto-promotion of {(newMain ? newMain.name : "null")} complete.");
                                }
                            }
                        }
                    }
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
            int direction = wasMainPassenger ? 1 : 0;
            UpdateCarousel(controller, direction);
            
            // Sync network state after removal
            UpdateNetworkBagState(controller, direction);

            // This ensures BaggedObject.OnExit runs and clears overrides
            if (controller != null)
            {
                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag")
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RemoveBaggedObject] Updating Bag state machine for {(controller ? controller.name : "null")}");
                        }
                        
                        // Check if we have a new main passenger that needs a state
                        var currentMain = GetMainSeatObject(controller);
                        if (currentMain != null)
                        {
                             if (PluginConfig.Instance.EnableDebugLogs.Value)
                             {
                                 Log.Info($"[RemoveBaggedObject] Transitioning Bag state machine to BaggedObject for {(currentMain ? currentMain.name : "null")}");
                             }
                             var newState = new BaggedObject();
                             newState.targetObject = currentMain;
                             esm.SetNextState(newState);
                        }
                        else
                        {
                             if (PluginConfig.Instance.EnableDebugLogs.Value)
                             {
                                 Log.Info($"[RemoveBaggedObject] Resetting Bag state machine to Main (Idle)");
                             }
                             // This will trigger SetNextStateToMain (which we patched to allow reset if untracked)
                             esm.SetNextStateToMain();
                        }
                        break;
                    }
                }
            }
            ForceRecalculateMass(controller);
        }
        
        public static void ForceRecalculateMass(DrifterBagController controller)
        {
            if (controller == null) return;
            
            float totalMass = 0f;
            if (baggedObjectsDict.TryGetValue(controller, out var list))
            {
                foreach (var obj in list)
                {
                    if (obj != null && !OtherPatches.IsInProjectileState(obj))
                    {
                        totalMass += controller.CalculateBaggedObjectMass(obj);
                    }
                }
            }
            
            // Multiplier application removed because CalculateBaggedObjectMass already applies it.

            // Clamp like original, unless uncapping is enabled
            if (!PluginConfig.Instance.UncapBagScale.Value)
            {
                totalMass = Mathf.Clamp(totalMass, 0f, 700f); // 700f is default maxMass
            }
            else
            {
                totalMass = Mathf.Max(totalMass, 0f);
            }
            
            // Set private field 'baggedMass'
            var field = AccessTools.Field(typeof(DrifterBagController), "baggedMass");
            if (field != null)
            {
                field.SetValue(controller, totalMass);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ForceRecalculateMass] Set total baggedMass to {totalMass} for {controller.name} (Objects: {(list?.Count ?? 0)})");
                }
                
                // Trigger stat recalculation to reflect mass changes if possible
                controller.GetComponent<CharacterBody>()?.RecalculateStats();

                // Update visual bag scale if we are in BaggedObject state
                var stateMachines = controller.GetComponents<EntityStateMachine>();
                foreach (var esm in stateMachines)
                {
                    if (esm.customName == "Bag" && esm.state is BaggedObject baggedObject)
                    {
                        BaggedObjectPatches.UpdateBagScale(baggedObject, totalMass);
                        break;
                    }
                }
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
            
            // Periodically clean up destroyed entries to prevent pollution
            if (mainSeatDict.Count > 10)
            {
                var keysToRemove = mainSeatDict.Keys.Where(k => k == null || (k is UnityEngine.Object uo && !uo)).ToList();
                foreach (var k in keysToRemove) mainSeatDict.TryRemove(k, out _);
            }

            if (obj != null)
            {
                mainSeatDict[controller] = obj;
            }
            else
            {
                mainSeatDict.TryRemove(controller, out _);
            }
        }

        public static GameObject? GetMainSeatObject(DrifterBagController controller)
        {
            if (controller == null) return null;
            if (mainSeatDict.TryGetValue(controller, out var obj))
            {
                if (obj == null || (obj is UnityEngine.Object uo && !uo))
                {
                    mainSeatDict.TryRemove(controller, out _);
                    return null;
                }
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
