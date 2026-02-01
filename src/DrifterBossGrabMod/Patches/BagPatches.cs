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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[BaggedObjectTracker] OnDestroy called for InstanceID: {_cachedInstanceId}");
            }

            if (!ReferenceEquals(obj, null))
            {
                PersistenceObjectsTracker.UntrackBaggedObject(obj, true);
            }

            if (!ReferenceEquals(controller, null) && !ReferenceEquals(obj, null))
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BaggedObjectTracker] Object {(obj != null ? obj.name : "Destroyed")} is being destroyed. Removing from bag of {(controller != null ? controller.name : "DestroyedController")}");
                }
                if (controller != null && obj != null)
                {
                    BagPatches.RemoveBaggedObject(controller, obj, true);
                }
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
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var componentTypes = new HashSet<string>();
            foreach (var rootObj in rootObjects)
            {
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
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component != null)
                {
                    componentTypes.Add(component.GetType().Name);
                }
            }
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                ScanObjectComponents(obj.transform.GetChild(i).gameObject, componentTypes);
            }
        }
        public static int GetUtilityMaxStock(DrifterBagController drifterBagController)
        {
            if (!FeatureState.IsCyclingEnabled)
            {
                return 1;
            }
            var body = drifterBagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                int maxStock = body.skillLocator.utility.maxStock;
                return maxStock + PluginConfig.Instance.BottomlessBagBaseCapacity.Value;
            }
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
            private static bool _usingAdditionalSeat = false;
            [HarmonyPrefix]
            public static bool Prefix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[AssignPassenger] Prefix called for {__instance?.name ?? "null"} with passenger {passengerObject?.name ?? "null"}");
                }
                _usingAdditionalSeat = false;
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject.name))
                {
                    return false;
                }
                if (passengerObject == null) return true;
                CharacterBody? body = null;
                var localDisabledStates = new Dictionary<GameObject, bool>();
                var modelLocator = passengerObject.GetComponent<ModelLocator>();
                if (modelLocator != null)
                {
                    bool isCycling = DrifterBossGrabPlugin.IsSwappingPassengers;
                    var existingPreserver = passengerObject.GetComponent<ModelStatePreserver>();
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BagPatches.AssignPassenger] === ASSIGN PASSENGER CHECK ===");
                        Log.Info($"[BagPatches.AssignPassenger] Object: {passengerObject.name}");
                        Log.Info($"[BagPatches.AssignPassenger] EnableObjectPersistence: {PluginConfig.Instance.EnableObjectPersistence.Value}");
                        Log.Info($"[BagPatches.AssignPassenger] ModelLocator exists: {modelLocator != null}");
                        Log.Info($"[BagPatches.AssignPassenger] ModelTransform exists: {modelLocator.modelTransform != null}");
                        Log.Info($"[BagPatches.AssignPassenger] ModelStatePreserver already exists: {existingPreserver != null}");
                        Log.Info($"[BagPatches.AssignPassenger] IsSwappingPassengers (isCycling): {isCycling}");
                        Log.Info($"[BagPatches.AssignPassenger] NetworkServer.active: {NetworkServer.active}");
                        Log.Info($"[BagPatches.AssignPassenger] Will add ModelStatePreserver: {!isCycling && PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator.modelTransform != null && existingPreserver == null}");
                        Log.Info($"[BagPatches.AssignPassenger] ================================");
                    }
                    if (!isCycling && PluginConfig.Instance.EnableObjectPersistence.Value && modelLocator.modelTransform != null && existingPreserver == null)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                            Log.Info($"[BagPatches.AssignPassenger] >>> ADDING ModelStatePreserver to {passengerObject.name} (Persistence enabled)");
                        passengerObject.AddComponent<ModelStatePreserver>();
                    }
                    else if (!isCycling && !PluginConfig.Instance.EnableObjectPersistence.Value && PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BagPatches.AssignPassenger] >>> SKIPPING ModelStatePreserver for {passengerObject.name} - Persistence is DISABLED");
                    }
                    else if (isCycling && PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BagPatches.AssignPassenger] >>> SKIPPING ModelStatePreserver for {passengerObject.name} - Cycling operation in progress (IsSwappingPassengers=true)");
                    }

                    if (!isCycling && PluginConfig.Instance.EnableObjectPersistence.Value && !modelLocator.autoUpdateModelTransform)
                    {
                        modelLocator.autoUpdateModelTransform = true;
                    }
                }
                body = passengerObject.GetComponent<CharacterBody>();
                if (body)
                {
                    if (body.baseMaxHealth <= 0 || body.levelMaxHealth < 0 ||
                        body.teamComponent == null || body.teamComponent.teamIndex < 0)
                    {
                        return false;
                    }
                    if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && body.currentVehicle != null)
                    {
                        body.currentVehicle.EjectPassenger(passengerObject);
                    }
                }
                if (body != null && body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable))
                {
                    StateManagement.DisableMovementColliders(passengerObject, localDisabledStates);
                }
                var teleporterInteraction = passengerObject.GetComponent<RoR2.TeleporterInteraction>();
                if (teleporterInteraction != null)
                {
                    bool hasActiveTeleporter = HasActiveTeleporterInScene(passengerObject);
                    if (hasActiveTeleporter)
                    {
                        teleporterInteraction.enabled = false;
                        PersistenceManager.MarkTeleporterForDisabling(passengerObject);
                    }
                }
                PersistenceObjectsTracker.TrackBaggedObject(passengerObject);
                int effectiveCapacity = __instance != null ? GetUtilityMaxStock(__instance) : 1;
                if (!baggedObjectsDict.TryGetValue(__instance, out var list))
                {
                    list = new List<GameObject>();
                    baggedObjectsDict[__instance] = list;
                }
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
                if (!isAlreadyTrackedByThisController && objectsInBag >= effectiveCapacity)
                {
                    return false;
                }

                if (effectiveCapacity == 1 && isAlreadyTrackedByThisController)
                {
                    bool isAlreadyInMainSeat = __instance.vehicleSeat != null &&
                        __instance.vehicleSeat.hasPassenger &&
                        ReferenceEquals(__instance.vehicleSeat.NetworkpassengerBodyObject, passengerObject);

                    if (isAlreadyInMainSeat)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[AssignPassenger] SKIP: Object {passengerObject.name} is already tracked and in main seat (capacity=1). Preventing state reset.");
                        }
                        return false;
                    }
                }

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
                        if (__instance.vehicleSeat.hasPassenger && __instance.vehicleSeat.NetworkpassengerBodyObject != null)
                        {
                            Log.Info($"[AssignPassenger] MainSeat Occupant: {__instance.vehicleSeat.NetworkpassengerBodyObject.name}");
                        }
                    }
                }

                if (!isAlreadyTrackedByThisController && effectiveCapacity > 1)
                {
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
                            Log.Info($"[AssignPassenger] Found empty additional seat. Stashing {passengerObject.name}");
                        }

                        _usingAdditionalSeat = true;
                        AddTracker(__instance, passengerObject);

                        if (GetMainSeatObject(__instance) == passengerObject)
                        {
                            SetMainSeatObject(__instance, null);
                        }

                        seatDict[passengerObject] = newSeat;

                        if (NetworkServer.active)
                        {
                            newSeat.AssignPassenger(passengerObject);
                        }

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
                        return false;
                    }
                    else if (!UnityEngine.Networking.NetworkServer.active)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[AssignPassenger] Client virtual stash for {passengerObject.name}");
                        }

                        _usingAdditionalSeat = true;
                        AddTracker(__instance, passengerObject);

                        passengerObject.transform.SetParent(__instance.transform);
                        passengerObject.transform.localPosition = Vector3.zero;

                        var discardDict = new Dictionary<GameObject, bool>();
                        StateManagement.DisableMovementColliders(passengerObject, discardDict);

                        var rb = passengerObject.GetComponent<Rigidbody>();
                        if (rb) rb.isKinematic = true;

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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[AssignPassenger] Additional seats full ({seatDict.Count}). Falling back to Main Seat.");
                        }
                    }
                }

                return true;
            }
            [HarmonyPostfix]
            public static void Postfix(DrifterBagController __instance, GameObject passengerObject)
            {
                if (passengerObject != null)
                {
                    if (OtherPatches.IsInProjectileState(passengerObject))
                    {
                        return;
                    }

                    AddTracker(__instance, passengerObject);

                    if (!_usingAdditionalSeat && __instance.vehicleSeat != null && NetworkServer.active)
                    {
                        if (__instance.vehicleSeat.NetworkpassengerBodyObject != passengerObject)
                        {
                            __instance.vehicleSeat.AssignPassenger(passengerObject);
                        }
                    }
                    if (!_usingAdditionalSeat)
                    {
                        SetMainSeatObject(__instance, passengerObject);
                        if (additionalSeatsDict.TryGetValue(__instance, out var seatDict))
                        {
                            System.Collections.Generic.CollectionExtensions.Remove(seatDict, passengerObject, out _);
                        }
                    }

                    if (!baggedObjectsDict.TryGetValue(__instance, out var list))
                    {
                        list = new List<GameObject>();
                        baggedObjectsDict[__instance] = list;
                    }
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
                    if (UnityEngine.Networking.NetworkServer.active)
                    {
                        PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, __instance);
                    }
                    UpdateCarousel(__instance);
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

            if (tracker != null && tracker.controller != controller)
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
            if (ReferenceEquals(controller, null) || (controller is UnityEngine.Object uController && !uController)) return;

            if (!NetworkServer.active && !controller.hasAuthority) return;

            var netController = controller.GetComponent<Networking.BottomlessBagNetworkController>();
            if (netController != null)
            {
                if (!baggedObjectsDict.TryGetValue(controller, out var baggedObjects))
                {
                    baggedObjects = new List<GameObject>();
                }

                baggedObjects.RemoveAll(obj => ReferenceEquals(obj, null) || (obj is UnityEngine.Object uo && !uo));

                var additionalSeats = new List<GameObject>();
                if (additionalSeatsDict.TryGetValue(controller, out var seatDict))
                {
                    foreach (var seat in seatDict.Values)
                    {
                        if (seat != null) additionalSeats.Add(seat.gameObject);
                    }
                }

                int selectedIndex = -1;
                var mainPassenger = GetMainSeatObject(controller);

                bool isActuallyInMainSeat = false;
                if (mainPassenger != null && controller.vehicleSeat != null && controller.vehicleSeat.hasPassenger)
                {
                    if (ReferenceEquals(controller.vehicleSeat.NetworkpassengerBodyObject, mainPassenger))
                    {
                        isActuallyInMainSeat = true;
                    }
                }

                if (isActuallyInMainSeat)
                {
                    for (int i = 0; i < baggedObjects.Count; i++)
                    {
                        if (baggedObjects[i] != null && baggedObjects[i].GetInstanceID() == mainPassenger.GetInstanceID())
                        {
                            selectedIndex = i;
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[UpdateNetworkBagState] Setting selectedIndex to {i} for {baggedObjects[i].name} (physically in main seat)");
                            }
                            break;
                        }
                    }
                }
                else if (mainPassenger != null && PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[UpdateNetworkBagState] Skipping selectedIndex calculation - {mainPassenger.name} is tracked as main but not physically in main seat (likely in additional seat)");
                }

                netController.SetBagState(selectedIndex, baggedObjects, additionalSeats, direction);
            }
        }

        public static void RemoveBaggedObject(DrifterBagController controller, GameObject obj, bool isDestroying = false)
        {
            if (ReferenceEquals(obj, null)) return;

            int targetInstanceId;
            try
            {
                targetInstanceId = obj.GetInstanceID();
            }
            catch
            {
                targetInstanceId = -1;
            }

            if (DrifterBossGrabPlugin.IsSwappingPassengers)
            {
                return;
            }

            GameObject? mainPassengerBefore = GetMainSeatObject(controller);
            bool wasMainPassenger = (mainPassengerBefore != null && mainPassengerBefore == obj);

            if (mainPassengerBefore != null && mainPassengerBefore.GetInstanceID() == obj.GetInstanceID())
            {
               SetMainSeatObject(controller, null);
               wasMainPassenger = true;
            }

            if (additionalSeatsDict.TryGetValue(controller, out var seatDict))
            {
                 if (seatDict.ContainsKey(obj))
                 {
                     System.Collections.Generic.CollectionExtensions.Remove(seatDict, obj, out _);
                 }
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

            bool isThrowing = OtherPatches.IsInProjectileState(obj);

            if (baggedObjectsDict.TryGetValue(controller, out List<GameObject> list))
            {
                try {
                    var tracker = obj.GetComponent<BaggedObjectTracker>();
                    if (tracker != null)
                    {
                        tracker.isRemovingManual = true;
                        UnityEngine.Object.Destroy(tracker);
                    }
                } catch { }

                list.RemoveAll(x => ReferenceEquals(x, null) || (x is UnityEngine.Object uo && !uo) || (targetInstanceId != -1 && x.GetInstanceID() == targetInstanceId));

                if (wasMainPassenger)
                {
                    if (NetworkServer.active && controller.vehicleSeat != null && controller.vehicleSeat.NetworkpassengerBodyObject == obj)
                    {
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RemoveBaggedObject] Force ejecting {(obj ? obj.name : "null")} from Main Seat to clear it (isThrowing: {isThrowing}, isDestroying: {isDestroying})");
                        }

                        if (isDestroying)
                        {
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

                             if (additionalSeatsDict.TryGetValue(controller, out var promotionSeatDict))
                             {
                                 if (newMain != null && promotionSeatDict.ContainsKey(newMain))
                                 {
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
                                             if (newMain != null)
                                             {
                                                 promotionSeatDict.TryRemove(newMain, out _);
                                             }
                                             break;
                                         }
                                     }
                                 }
                             }

                             if (NetworkServer.active)
                             {
                                 controller.AssignPassenger(newMain);

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
                                             Log.Info($"[RemoveBaggedObject] CRITICAL: Forced assignment also failed!");
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

            if (isThrowing)
            {
                CleanupEmptyAdditionalSeats(controller);
            }

            if (UnityEngine.Networking.NetworkServer.active)
            {
                PersistenceNetworkHandler.SendBaggedObjectsPersistenceMessage(list, controller);
            }

            int direction = wasMainPassenger ? 1 : 0;
            if (controller != null)
            {
                UpdateCarousel(controller, direction);
            }

            if (controller != null)
            {
                UpdateNetworkBagState(controller, direction);
            }

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

                         var currentMain = controller != null ? GetMainSeatObject(controller) : null;
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
                              esm.SetNextStateToMain();
                         }
                        break;
                    }
                }
            }
            ForceRecalculateMass(controller);

            if (obj != null && !isDestroying && !isThrowing)
            {
                var preserver = obj.GetComponent<ModelStatePreserver>();
                if (preserver != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[BagPatches.RemoveBaggedObject] === REMOVING BAGGED OBJECT ===");
                        Log.Info($"[BagPatches.RemoveBaggedObject] Object: {obj.name}");
                        Log.Info($"[BagPatches.RemoveBaggedObject] Found ModelStatePreserver on {obj.name}");
                        Log.Info($"[BagPatches.RemoveBaggedObject] Restoring original model state for {obj.name}");
                        Log.Info($"[BagPatches.RemoveBaggedObject] isDestroying: {isDestroying}, isThrowing: {isThrowing}");
                        Log.Info($"[BagPatches.RemoveBaggedObject] IsSwappingPassengers: {DrifterBossGrabPlugin.IsSwappingPassengers}");
                        Log.Info($"[BagPatches.RemoveBaggedObject] ================================");
                    }

                    preserver.RestoreOriginalState(false);
                    UnityEngine.Object.Destroy(preserver);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[BagPatches.RemoveBaggedObject] >>> DESTROYED ModelStatePreserver on {obj.name}");
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[BagPatches.RemoveBaggedObject] >>> NO ModelStatePreserver found on {obj.name}");
                }
            }
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

            if (!PluginConfig.Instance.UncapBagScale.Value)
            {
                totalMass = Mathf.Clamp(totalMass, 0f, 700f);
            }
            else
            {
                totalMass = Mathf.Max(totalMass, 0f);
            }

            var field = AccessTools.Field(typeof(DrifterBagController), "baggedMass");
            if (field != null)
            {
                field.SetValue(controller, totalMass);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ForceRecalculateMass] Set total baggedMass to {totalMass} for {controller.name} (Objects: {(list?.Count ?? 0)})");
                }

                controller.GetComponent<CharacterBody>()?.RecalculateStats();

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
        public static bool IsBaggedObject(DrifterBagController controller, GameObject? obj)
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
        public static void CleanupEmptyAdditionalSeats(DrifterBagController? controller)
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
                            if (NetworkServer.active)
                            {
                                NetworkServer.Destroy(seat.gameObject);
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
                foreach (var obj in seatsToRemove)
                {
                    seatDict.TryRemove(obj, out _);
                }
                if (seatDict.Count == 0)
                {
                    System.Collections.Generic.CollectionExtensions.Remove(additionalSeatsDict, controller, out _);
                }
            }
            var childSeats = controller.GetComponentsInChildren<RoR2.VehicleSeat>(true);
            foreach (var childSeat in childSeats)
            {
                if (childSeat == controller.vehicleSeat) continue;
                bool isTracked = seatDict != null && seatDict.Values.Contains(childSeat);
                if (!isTracked && !childSeat.hasPassenger)
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.Destroy(childSeat.gameObject);
                    }
                    UnityEngine.Object.Destroy(childSeat.gameObject);
                }
            }
        }
    }
    [HarmonyPatch(typeof(RoR2.VehicleSeat), nameof(RoR2.VehicleSeat.AssignPassenger))]
    public static class VehicleSeat_AssignPassenger_Postfix
    {
        [HarmonyPostfix]
        public static void Postfix(RoR2.VehicleSeat __instance, GameObject bodyObject)
        {
            if (bodyObject == null || !NetworkServer.active) return;
            var drifterBagController = __instance.GetComponentInParent<DrifterBagController>();
            if (drifterBagController == null) return;
            if (__instance == drifterBagController.vehicleSeat)
            {
                return;
            }

            if (!BagPatches.additionalSeatsDict.TryGetValue(drifterBagController, out var seatDict))
            {
                seatDict = new ConcurrentDictionary<GameObject, RoR2.VehicleSeat>();
                BagPatches.additionalSeatsDict[drifterBagController] = seatDict;
            }
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
                    if (controller != null && victim != null)
                    {
                        BagPatches.RemoveBaggedObject(controller, victim);
                    }
                }
            }
        }
    }
}
