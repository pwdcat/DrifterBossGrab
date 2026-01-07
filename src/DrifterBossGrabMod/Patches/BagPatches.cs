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
    public static class BagPatches
    {
        // Dictionary to hold baggedObjects for each DrifterBagController instance
        internal static readonly Dictionary<DrifterBagController, List<GameObject>> baggedObjectsDict = new Dictionary<DrifterBagController, List<GameObject>>();
        // Dictionary to hold additional VehicleSeat components for extra passengers
        internal static readonly Dictionary<DrifterBagController, Dictionary<GameObject, RoR2.VehicleSeat>> additionalSeatsDict = new Dictionary<DrifterBagController, Dictionary<GameObject, RoR2.VehicleSeat>>();
        // Dictionary to track which object is currently in the main seat for each bag controller
        internal static readonly Dictionary<DrifterBagController, GameObject> mainSeatDict = new Dictionary<DrifterBagController, GameObject>();


        public static void ScanAllSceneComponents()
        {
            if (!PluginConfig.EnableComponentAnalysisLogs.Value) return;

            Log.Info($"{Constants.LogPrefix} === SCANNING ALL COMPONENTS IN CURRENT SCENE ===");

            // Get all root GameObjects in the scene
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            // Collect all unique component types
            var componentTypes = new HashSet<string>();

            foreach (var rootObj in rootObjects)
            {
                // Recursively scan all objects
                ScanObjectComponents(rootObj, componentTypes);
            }

            Log.Info($"{Constants.LogPrefix} === UNIQUE COMPONENT TYPES FOUND ({componentTypes.Count}) ===");
            foreach (var type in componentTypes.OrderBy(t => t))
            {
                Log.Info($"{Constants.LogPrefix} {type}");
            }
            Log.Info($"{Constants.LogPrefix} === END SCENE COMPONENT SCAN ===");
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

        // Get the max stock of the Drifter's utility skill
        public static int GetUtilityMaxStock(DrifterBagController drifterBagController)
        {
            var body = drifterBagController.GetComponent<CharacterBody>();
            if (body && body.skillLocator && body.skillLocator.utility)
            {
                int maxStock = body.skillLocator.utility.maxStock;
                
                // Respect BottomlessBagEnabled setting - if disabled, cap capacity at 1
                if (!PluginConfig.BottomlessBagEnabled.Value && maxStock > 1)
                {
                    maxStock = 1;
                }
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} GetUtilityMaxStock: drifterBagController={drifterBagController?.name ?? "null"}, body={body?.name ?? "null"}, skillLocator={body.skillLocator != null}, utility={body.skillLocator.utility != null}, maxStock={maxStock}, BottomlessBagEnabled={PluginConfig.BottomlessBagEnabled.Value}");
                }
                return maxStock;
            }
            
            // Default to 1 if BottomlessBagEnabled is disabled or skill not found
            int defaultStock = PluginConfig.BottomlessBagEnabled.Value ? 1 : 1;
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} GetUtilityMaxStock: drifterBagController={drifterBagController?.name ?? "null"}, body={body?.name ?? "null"}, skillLocator={body?.skillLocator != null}, utility={body?.skillLocator?.utility != null}, using fallback={defaultStock}, BottomlessBagEnabled={PluginConfig.BottomlessBagEnabled.Value}");
            }
            return defaultStock;
        }

        // Check if there's another active teleporter in the scene
        private static bool HasActiveTeleporterInScene(GameObject excludeTeleporter)
        {
            var allTeleporters = UnityEngine.Object.FindObjectsOfType<RoR2.TeleporterInteraction>(false);
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
                if (!baggedObjectsDict.TryGetValue(__instance, out var list))
                {
                    list = new List<GameObject>();
                    baggedObjectsDict[__instance] = list;
                }
                int effectiveCapacity = GetUtilityMaxStock(__instance);
                
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
                
                __result = objectsInBag >= effectiveCapacity;
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    int projectileCount = OtherPatches.projectileStateObjects.Count;
                    Log.Info($"{Constants.LogPrefix} Bag full check: effectiveCapacity={effectiveCapacity}, objectsInBag={objectsInBag}, totalTracked={list.Count}, inProjectileState={projectileCount}, isFull={{__result}}, BottomlessBagEnabled={PluginConfig.BottomlessBagEnabled.Value}");
                }
            }
        }

        [HarmonyPatch(typeof(DrifterBagController), "AssignPassenger")]
        public class DrifterBagController_AssignPassenger
        {
            [HarmonyPrefix]
            public static bool Prefix(DrifterBagController __instance, GameObject passengerObject)
            {
                // Check blacklist first - return false to prevent grabbing blacklisted objects
                if (passengerObject && PluginConfig.IsBlacklisted(passengerObject.name))
                {
                    return false;
                }

                if (passengerObject == null) return true;

                CharacterBody body = null;
                var localDisabledStates = new Dictionary<GameObject, bool>();

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} AssignPassenger called for {passengerObject}");
                }

                // Ensure autoUpdateModelTransform is true so model follows GameObject if ModelLocator exists
                var modelLocator = passengerObject.GetComponent<ModelLocator>();
                if (modelLocator != null && !modelLocator.autoUpdateModelTransform)
                {
                    // Add ModelStatePreserver to store original state before modifying
                    var statePreserver = passengerObject.AddComponent<ModelStatePreserver>();

                    modelLocator.autoUpdateModelTransform = true;
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Set autoUpdateModelTransform=true for object with ModelLocator {passengerObject.name}");
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
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Skipping bag assignment for {body.name} due to invalid CharacterBody state: health={body.baseMaxHealth}/{body.levelMaxHealth}, team={(int)(body.teamComponent?.teamIndex ?? (TeamIndex)(-1))}");
                        }
                        return false; // Prevent grabbing
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Assigning {body.name}, isBoss: {body.isBoss}, isElite: {body.isElite}, currentVehicle: {body.currentVehicle != null}");
                    }

                    // Eject ungrabbable enemies from vehicles before assigning
                    if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.Ungrabbable) && body.currentVehicle != null)
                    {
                        body.currentVehicle.EjectPassenger(passengerObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Ejected {body.name} from vehicle");
                        }
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

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Disabled TeleporterInteraction on grabbed teleporter {passengerObject.name} - active teleporter found");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Left TeleporterInteraction enabled on grabbed teleporter {passengerObject.name} - no active teleporter found");
                        }
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

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} AssignPassenger capacity check: effectiveCapacity={effectiveCapacity}, objectsInBag={objectsInBag}, totalTracked={list.Count}");
                }

                // Check if bag is full (only count objects not in projectile state)
                if (objectsInBag >= effectiveCapacity)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Bag is full ({list.Count}/{effectiveCapacity}), cannot grab {passengerObject.name}");
                    }
                    return false;
                }

                // If capacity > 1 and we have room in additional seats, assign to additional seat
                if (effectiveCapacity > 1 && objectsInBag < effectiveCapacity - 1)
                {
                    // Create additional seat if needed
                    if (!additionalSeatsDict.TryGetValue(__instance, out var seatDict))
                    {
                        seatDict = new Dictionary<GameObject, RoR2.VehicleSeat>();
                        additionalSeatsDict[__instance] = seatDict;
                    }

                    var seatObject = new GameObject($"AdditionalSeat_{list.Count}");
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
                    int passengerInstanceId = passengerObject.GetInstanceID();
                    foreach (var existingObj in list)
                    {
                        if (existingObj != null && existingObj.GetInstanceID() == passengerInstanceId)
                        {
                            alreadyInList = true;
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Skipping duplicate add to list for {passengerObject.name} (inst={passengerInstanceId})");
                            }
                            break;
                        }
                    }
                    
                    if (!alreadyInList)
                    {
                        list.Add(passengerObject);
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Assigned {passengerObject.name} to additional seat. Bag state: {list.Count}/{effectiveCapacity}");
                    }

                    return false; // Prevent original method
                }

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
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Skipping tracking for {passengerObject.name} - already in projectile state");
                        }
                        return;
                    }
                    
                    // Track this object as being in the main seat (since postfix is only called for main seat assignments)
                    SetMainSeatObject(__instance, passengerObject);
                    
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
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Skipping duplicate tracking for {passengerObject.name} (inst={passengerInstanceId})");
                            }
                            break;
                        }
                    }
                    
                    if (!alreadyTracked)
                    {
                        list.Add(passengerObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            int effectiveCapacity = GetUtilityMaxStock(__instance);
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
                            Log.Info($"{Constants.LogPrefix} Assigned {passengerObject.name} to main seat. Bag state: {objectsInBag}/{effectiveCapacity}");
                        }
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
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Skipping removal for {obj.name} - currently swapping passengers");
                }
                return;
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Removing {obj.name} from tracking");
            }

            // Remove from projectile state tracking if present
            if (OtherPatches.IsInProjectileState(obj))
            {
                OtherPatches.RemoveFromProjectileState(obj);
            }

            // Check if object is actually in a seat (main or additional)
            bool isActuallyInMainSeat = controller.vehicleSeat != null && controller.vehicleSeat.currentPassengerBody == obj;
            bool isActuallyInAdditionalSeat = false;
            
            if (additionalSeatsDict.TryGetValue(controller, out var additionalSeatsMap))
            {
                foreach (var kvp in additionalSeatsMap)
                {
                    if (kvp.Value != null && kvp.Value.currentPassengerBody == obj)
                    {
                        isActuallyInAdditionalSeat = true;
                        break;
                    }
                }
            }

            bool isStillInMainSeat = mainSeatDict.TryGetValue(controller, out var mainObj) && mainObj != null && mainObj.GetInstanceID() == obj.GetInstanceID();
            bool isStillInAdditionalSeat = GetAdditionalSeat(controller, obj) != null;

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Seat check: actualMain={isActuallyInMainSeat}, actualAdditional={isActuallyInAdditionalSeat}, trackedMain={isStillInMainSeat}, trackedAdditional={isStillInAdditionalSeat}");
            }

            // Only keep in tracking if object is actually still in a seat on this controller
            if (isActuallyInMainSeat || isActuallyInAdditionalSeat)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Object {obj.name} is actually in a seat (main={isActuallyInMainSeat}, additional={isActuallyInAdditionalSeat}), not removing from baggedObjectsDict");
                }
                // Object is still in a seat, don't remove from tracking list
                // Only remove from additionalSeatsDict if it's in an additional seat
                if (isActuallyInAdditionalSeat && additionalSeatsDict.TryGetValue(controller, out var additionalSeatsMap2))
                {
                    if (additionalSeatsMap2.TryGetValue(obj, out var seat))
                    {
                        additionalSeatsMap2.Remove(obj);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Removed from additionalSeatsDict only, remaining seats: {additionalSeatsMap2.Count}");
                        }
                        // Destroy the seat GameObject
                        if (seat != null && seat.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(seat.gameObject);
                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Destroyed seat GameObject");
                            }
                        }
                    }
                }
                return;
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
                
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Removed {objectsToRemove.Count} instance(s) of {obj.name} from baggedObjectsDict, remaining: {list.Count}");
                }
            }

            // Remove from additionalSeatsDict and destroy the seat
            if (additionalSeatsDict.TryGetValue(controller, out var seatsDict))
            {
                if (seatsDict.TryGetValue(obj, out var seat))
                {
                    seatsDict.Remove(obj);
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Removed from additionalSeatsDict, remaining seats: {seatsDict.Count}");
                    }

                    // Destroy the seat GameObject
                    if (seat != null && seat.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(seat.gameObject);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} [RemoveBaggedObject] Destroyed seat GameObject");
                        }
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

        public static RoR2.VehicleSeat GetAdditionalSeat(DrifterBagController controller, GameObject obj)
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

        public static void SetMainSeatObject(DrifterBagController controller, GameObject obj)
        {
            if (controller == null) return;
            
            if (obj != null)
            {
                mainSeatDict[controller] = obj;
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [SetMainSeatObject] Set main seat to {obj.name} for controller {controller?.name ?? "null"}");
                }
            }
            else
            {
                if (mainSeatDict.ContainsKey(controller))
                {
                    mainSeatDict.Remove(controller);
                }
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} [SetMainSeatObject] Cleared main seat for controller {controller?.name ?? "null"}");
                }
            }
        }

        public static GameObject GetMainSeatObject(DrifterBagController controller)
        {
            if (controller == null) return null;
            
            if (mainSeatDict.TryGetValue(controller, out var obj))
            {
                return obj;
            }
            return null;
        }
    }
}