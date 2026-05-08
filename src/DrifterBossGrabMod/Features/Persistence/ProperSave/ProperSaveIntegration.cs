#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using RoR2;
using DrifterBossGrabMod.API;
using DrifterBossGrabMod.ProperSave.Core;
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave.Spawning;
using DrifterBossGrabMod.ProperSave.Serializers;

namespace DrifterBossGrabMod.ProperSave
{
    // ========================================================================================
    // PROPER SAVE CONSTANTS
    // ========================================================================================

    public static class ProperSaveConstants
    {
        public static class Timing
        {
            public const int MaxDirectorCoreWaitAttempts = 300;
            public const float DirectorCoreWaitIncrement = 0.1f;
            public const float PostDirectorCoreWait = 0.5f;
            public const float PostRegistryRebuildWait = 0.3f;
        }
    }

    // ========================================================================================
    // PROPER SAVE INTEGRATION
    // ========================================================================================

    public static class ProperSaveIntegration
    {
        private const string SAVE_KEY = "DrifterBossGrabMod_BagData";
        private const string PROPER_SAVE_GUID = "com.KingEnderBrine.ProperSave";

        private static readonly List<IObjectSerializerPlugin> _serializerPlugins = new();
        private static bool _initialized = false;
        private static bool _properSaveAvailable = false;
        private static DrifterBagSaveData? _pendingSaveData;

        public static void Initialize()
        {
            if (_initialized) return;

            _properSaveAvailable = IsProperSaveAvailable();
            if (!_properSaveAvailable)
            {
                return;
            }

            RegisterBuiltInPlugins();

            Run.onRunStartGlobal += OnRunStart;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            var loadingAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ProperSave");

            if (loadingAssembly == null)
            {
                Log.Error("[ProperSave] Could not find ProperSave assembly");
                return;
            }

            var saveFileType = loadingAssembly.GetType("ProperSave.SaveFile");
            var loadingType = loadingAssembly.GetType("ProperSave.Loading");

            if (saveFileType == null || loadingType == null)
            {
                Log.Error("[ProperSave] Could not find ProperSave types");
                return;
            }

            var onGatherSaveDataEvent = saveFileType.GetEvent("OnGatherSaveData");
            var onLoadingStartedEvent = loadingType.GetEvent("OnLoadingStarted");

            if (onGatherSaveDataEvent == null || onLoadingStartedEvent == null)
            {
                Log.Error("[ProperSave] Could not find ProperSave events");
                return;
            }

            var onGatherSaveDataMethod = typeof(ProperSaveIntegration).GetMethod("OnGatherSaveData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var onLoadingStartedMethod = typeof(ProperSaveIntegration).GetMethod("OnLoadingStarted",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (onGatherSaveDataMethod == null || onLoadingStartedMethod == null)
            {
                Log.Error("[ProperSave] Could not find handler methods");
                return;
            }

            onGatherSaveDataEvent.AddEventHandler(null,
                Delegate.CreateDelegate(onGatherSaveDataEvent.EventHandlerType!, onGatherSaveDataMethod));
            onLoadingStartedEvent.AddEventHandler(null,
                Delegate.CreateDelegate(onLoadingStartedEvent.EventHandlerType!, onLoadingStartedMethod));

            _initialized = true;
            Log.DebugIfEnabled("[ProperSave] Integration initialized with {0} plugins", _serializerPlugins.Count);
        }

        public static void Cleanup()
        {
            if (!_initialized) return;

            Run.onRunStartGlobal -= OnRunStart;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            SpawnCardRegistry.Cleanup();

            _serializerPlugins.Clear();
            _pendingSaveData = null;
            _initialized = false;
        }

        private static void OnRunStart(Run run)
        {
            Log.DebugIfEnabled("[ProperSave] OnRunStart called, _pendingSaveData is {0}", (_pendingSaveData == null ? "null" : $"not null ({_pendingSaveData.BaggedObjects?.Count ?? 0} objects)"));
        }

        private static void OnActiveSceneChanged(Scene prevScene, Scene nextScene)
        {
            Log.DebugIfEnabled("[ProperSave] OnActiveSceneChanged called - prevScene: {0}, nextScene: {1}", prevScene.name, nextScene.name);

            if (_pendingSaveData == null) return;

            // Only restore when loading a save (Single mode scene load to game scene)
            Run.instance.StartCoroutine(DelayedSceneLoadRestoration(nextScene));
        }

        // ========================================================================================
        // RESTORATION ENGINE
        // ========================================================================================

        private static System.Collections.IEnumerator DelayedSceneLoadRestoration(Scene scene)
        {
            yield return null;

            if (_pendingSaveData == null)
            {
                yield break;
            }

            Log.DebugIfEnabled("[ProperSave] Starting WaitForDirectorCoreAndRestore coroutine for {0} objects", _pendingSaveData.BaggedObjects?.Count ?? 0);
            Run.instance.StartCoroutine(WaitForDirectorCoreAndRestore());
        }

        private static System.Collections.IEnumerator WaitForDirectorCoreAndRestore()
        {
            int attempts = 0;
            while (DirectorCore.instance == null && attempts < ProperSaveConstants.Timing.MaxDirectorCoreWaitAttempts)
            {
                yield return new WaitForSeconds(ProperSaveConstants.Timing.DirectorCoreWaitIncrement);
                attempts++;
            }

            if (DirectorCore.instance == null)
            {
                Log.Error("[ProperSave] DirectorCore not available after 30 seconds, aborting restoration");
                _pendingSaveData = null;
                yield break;
            }

            if (_pendingSaveData == null)
            {
                yield break;
            }

            if (_pendingSaveData.BaggedObjects == null)
            {
                Log.Error("[ProperSave] BaggedObjects list is null in save data, aborting restoration");
                _pendingSaveData = null;
                yield break;
            }

            yield return new WaitForSeconds(ProperSaveConstants.Timing.PostDirectorCoreWait);

            try
            {
                SpawnCardRegistry.RebuildRegistry();
            }
            catch (Exception ex)
            {
                Log.Error($"[ProperSave] Failed to rebuild spawn card registry: {ex.Message}");
                _pendingSaveData = null;
                yield break;
            }
            yield return new WaitForSeconds(ProperSaveConstants.Timing.PostRegistryRebuildWait);

            Log.DebugIfEnabled("[ProperSave] Restoring {0} bagged objects", _pendingSaveData.BaggedObjects.Count);

            RestoreBagState(_pendingSaveData!);
            _pendingSaveData = null;
        }

        // ========================================================================================
        // BUILT-IN PLUGINS
        // ========================================================================================

        private static void RegisterBuiltInPlugins()
        {
            Log.DebugIfEnabled("[ProperSaveIntegration] Registering built-in serializer plugins...");

            // Enemy serializers (highest priority, 1:1 restoration)
            var characterMasterSerializer = BuiltInSerializersAPI.ForCharacterMaster();
            _serializerPlugins.Add(characterMasterSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", characterMasterSerializer.GetType().Name, characterMasterSerializer.Priority);

            var characterBodySerializer = BuiltInSerializersAPI.ForCharacterBody();
            _serializerPlugins.Add(characterBodySerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", characterBodySerializer.GetType().Name, characterBodySerializer.Priority);

            // Interactable serializers (API-based)
            var chestSerializer = BuiltInSerializersAPI.ForChest();
            _serializerPlugins.Add(chestSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", chestSerializer.GetType().Name, chestSerializer.Priority);

            var duplicatorSerializer = BuiltInSerializersAPI.ForDuplicator();
            _serializerPlugins.Add(duplicatorSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", duplicatorSerializer.GetType().Name, duplicatorSerializer.Priority);

            var shrineSerializer = BuiltInSerializersAPI.ForShrine();
            _serializerPlugins.Add(shrineSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", shrineSerializer.GetType().Name, shrineSerializer.Priority);

            var soaSerializer = BuiltInSerializersAPI.ForSpecialObjectAttributes();
            _serializerPlugins.Add(soaSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", soaSerializer.GetType().Name, soaSerializer.Priority);

            var junkCubeSerializer = BuiltInSerializersAPI.ForJunkCubeController();
            _serializerPlugins.Add(junkCubeSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", junkCubeSerializer.GetType().Name, junkCubeSerializer.Priority);

            var halcyoniteShrineSerializer = BuiltInSerializersAPI.ForHalcyoniteShrineInteractable();
            _serializerPlugins.Add(halcyoniteShrineSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", halcyoniteShrineSerializer.GetType().Name, halcyoniteShrineSerializer.Priority);

            var tinkerableSerializer = BuiltInSerializersAPI.ForTinkerableObjectAttributes();
            _serializerPlugins.Add(tinkerableSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", tinkerableSerializer.GetType().Name, tinkerableSerializer.Priority);

            var teleporterSerializer = BuiltInSerializersAPI.ForTeleporter();
            _serializerPlugins.Add(teleporterSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", teleporterSerializer.GetType().Name, teleporterSerializer.Priority);

            var purchaseSerializer = BuiltInSerializersAPI.ForPurchaseInteraction();
            _serializerPlugins.Add(purchaseSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", purchaseSerializer.GetType().Name, purchaseSerializer.Priority);

            // Reflection-based fallbacks and integrations
            var genericSerializer = BuiltInSerializersAPI.ForGenericComponentSerializer();
            _serializerPlugins.Add(genericSerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", genericSerializer.GetType().Name, genericSerializer.Priority);

            var qualitySerializer = BuiltInSerializersAPI.ForQualityIntegration();
            _serializerPlugins.Add(qualitySerializer);
            Log.DebugIfEnabled("  - Added: {0} (Priority: {1})", qualitySerializer.GetType().Name, qualitySerializer.Priority);

            // Sort by priority
            _serializerPlugins.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            Log.DebugIfEnabled("[ProperSaveIntegration] Total registered plugins: {0}", _serializerPlugins.Count);
        }

        public static void RegisterPlugin(IObjectSerializerPlugin plugin)
        {
            if (plugin == null) return;
            _serializerPlugins.Add(plugin);
            _serializerPlugins.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            Log.DebugIfEnabled("[Serializer] Registered plugin: {0} (Priority: {1})", plugin.GetType().Name, plugin.Priority);
        }

        public static List<IObjectSerializerPlugin> GetSerializerPlugins()
        {
            return new List<IObjectSerializerPlugin>(_serializerPlugins);
        }

        // ========================================================================================
        // SAVE/LOAD HANDLERS
        // ========================================================================================

        private static void OnGatherSaveData(Dictionary<string, object> gatheredData)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value)
            {
                return;
            }

            var saveData = CreateSaveData();
            gatheredData[SAVE_KEY] = saveData;

            Log.DebugIfEnabled("[ProperSave] Saved {0} bagged objects", saveData.BaggedObjects.Count);
        }

        private static void OnLoadingStarted(object saveFile)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value)
            {
                return;
            }

            try
            {
                var saveFileType = saveFile.GetType();
                var getModdedDataMethod = saveFileType.GetMethod("GetModdedData");

                if (getModdedDataMethod == null)
                {
                    Log.Error("[ProperSave] Could not find GetModdedData method");
                    return;
                }

                var genericMethod = getModdedDataMethod.MakeGenericMethod(typeof(DrifterBagSaveData));
                var saveData = (DrifterBagSaveData?)genericMethod.Invoke(saveFile, new object[] { SAVE_KEY });

                if (saveData == null)
                {
                    return;
                }

                _pendingSaveData = saveData;
                Log.DebugIfEnabled("[ProperSave] Queued {0} bagged objects for restoration", saveData.BaggedObjects.Count);
            }
            catch (Exception ex)
            {
                Log.Error($"[ProperSave] Failed to queue bag data: {ex.Message}");
            }
        }

        // ========================================================================================
        // DATA CAPTURE
        // ========================================================================================

        private static DrifterBagSaveData CreateSaveData()
        {
            var saveData = new DrifterBagSaveData
            {
                SaveSceneName = SceneManager.GetActiveScene().name,
                StageClearCount = Run.instance?.stageClearCount ?? 0
            };

            SpawnCardRegistry.Initialize();

            var persistedObjects = PersistenceObjectManager.GetPersistedObjects();
            var capturedInstanceIds = new HashSet<int>();

            // Build bag slot index map before iterating persisted objects
            var bagSlotIndexMap = new Dictionary<int, int>(); // instanceId -> bagSlotIndex
            var controllers = UnityEngine.Object.FindObjectsByType<RoR2.DrifterBagController>(UnityEngine.FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                var baggedObjects = API.DrifterBagAPI.GetBaggedObjects(controller);
                for (int i = 0; i < baggedObjects.Count; i++)
                {
                    var bagObj = baggedObjects[i];
                    if (bagObj != null)
                    {
                        bagSlotIndexMap[bagObj.GetInstanceID()] = i;
                    }
                }
            }

            foreach (var obj in persistedObjects)
            {
                if (obj == null) continue;

                // Skip objects with TeleporterInteraction if Teleporter is blacklisted
                var teleporterInteraction = obj.GetComponent<RoR2.TeleporterInteraction>();
                if (teleporterInteraction != null && PluginConfig.IsPersistenceBlacklisted("Teleporter"))
                {
                    Log.DebugIfEnabled("[ProperSaveIntegration] Skipping teleporter object {0} (Teleporter is blacklisted)", obj.name);
                    continue;
                }

                var objData = CaptureObjectData(obj, capturedInstanceIds);
                if (objData != null)
                {
                    if (bagSlotIndexMap.TryGetValue(obj.GetInstanceID(), out var index))
                    {
                        objData.BagSlotIndex = index;
                    }
                    saveData.BaggedObjects.Add(objData);
                }
            }

            return saveData;
        }

        private static BaggedObjectSaveData? CaptureObjectData(GameObject obj, HashSet<int>? capturedInstanceIds = null)
        {
            if (obj == null) return null;

            var networkIdentity = obj.GetComponent<NetworkIdentity>();
            if (networkIdentity == null)
            {
                return null;
            }

            var characterBody = obj.GetComponent<CharacterBody>();
            var master = characterBody != null ? characterBody.master : obj.GetComponent<CharacterMaster>();

            bool isSavingMaster = master != null;
            GameObject objectToCapture = (isSavingMaster && master != null) ? master.gameObject : obj;

            // Deduplicate to ensure we only capture this specific master or body once
            if (capturedInstanceIds != null)
            {
                int instanceId = objectToCapture.GetInstanceID();
                if (capturedInstanceIds.Contains(instanceId))
                {
                    Log.DebugIfEnabled("[CaptureObjectData] Skipping duplicate capture for {0} (from source {1})", objectToCapture.name, obj.name);
                    return null;
                }
                capturedInstanceIds.Add(instanceId);
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                if (isSavingMaster && master != null)
                    Log.DebugIfEnabled($"[CaptureObjectData] Capturing master {master.name} for object {obj.name}");
                else
                    Log.DebugIfEnabled($"[CaptureObjectData] Capturing body/object {obj.name}");
            }

            string? masterName = master?.name;

            // Check if object is currently in a seat
            bool? isMainSeatObject = null;
            int? additionalSeatIndex = null;
            CheckObjectInSeats(obj, out isMainSeatObject, out additionalSeatIndex);

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.DebugIfEnabled("[CaptureObjectData] Capturing data for {0}", obj.name);
                var components = obj.GetComponents<Component>();
                Log.DebugIfEnabled("[CaptureObjectData] Components on object ({0} total): {1}", components.Length, string.Join(", ", components.Take(10).Select(c => c.GetType().Name)));

                var masterComponent = obj.GetComponent<RoR2.CharacterMaster>();
                if (masterComponent != null)
                {
                    Log.DebugIfEnabled("[CaptureObjectData] Object has CharacterMaster component: {0}", masterComponent.name);
                }
                else
                {
                    Log.DebugIfEnabled("[CaptureObjectData] Object does not have CharacterMaster component");
                    if (characterBody != null && characterBody.master != null)
                    {
                        Log.DebugIfEnabled("[CaptureObjectData] CharacterBody's master is: {0} (saved as: {1})", characterBody.master.name, masterName);
                    }
                }
            }

            string prefabName = System.Text.RegularExpressions.Regex.Replace(objectToCapture.name, @"\(Clone\)(\(\d+\))?$", "");

            // For masters, we need to use the actual network identity of the master for AssetID etc.
            var captureIdentity = objectToCapture.GetComponent<NetworkIdentity>();
            if (captureIdentity == null) captureIdentity = networkIdentity;

            var objData = new BaggedObjectSaveData
            {
                ObjectName = obj.name,
                SaveType = isSavingMaster ? "CharacterMaster" : "Body",
                PrefabName = prefabName,
                ObjectInstanceId = objectToCapture.GetInstanceID(),
                SceneName = SceneManager.GetActiveScene().name,
                OwnerPlayerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj) ?? string.Empty,

                Position = SerializationHelpers.SerializeVector3(obj.transform.position),
                Rotation = SerializationHelpers.SerializeQuaternion(obj.transform.rotation),

                AssetId = SerializationHelpers.SerializeGuid(new Guid(captureIdentity.assetId.ToString())),
                PrefabHash = captureIdentity.assetId.ToString(),

                ComponentType = GetPrimaryComponentType(objectToCapture),

                MasterName = masterName,

                IsMainSeatObject = isMainSeatObject,
                AdditionalSeatIndex = additionalSeatIndex

            };

            objData.SpawnCardPath = GetSpawnCardPath(objectToCapture);

            foreach (var plugin in _serializerPlugins)
            {
                if (plugin.CanHandle(objectToCapture))
                {
                    var state = plugin.CaptureState(objectToCapture);
                    if (state != null)
                    {
                        Log.DebugIfEnabled("[ProperSaveIntegration] Plugin '{0}' handled {1}, captured {2} values", plugin.PluginName, obj.name, state.Count);

                        var entry = new ComponentStateEntry
                        {
                            PluginName = plugin.PluginName
                        };

                        foreach (var kvp in state)
                        {
                            var value = kvp.Value;
                            var typeStr = value?.GetType().FullName ?? "System.String";
                            var valueStr = SerializationHelpers.SerializeValue(value);

                            entry.Values.Add(new StateValue
                            {
                                Key = kvp.Key,
                                Type = typeStr,
                                Value = valueStr
                            });

                            if (kvp.Key == "ObjectType" && string.IsNullOrEmpty(objData.ObjectType))
                            {
                                objData.ObjectType = value?.ToString() ?? string.Empty;
                            }
                        }

                        objData.ComponentStates.Add(entry);
                    }
                }
            }

            if (objData.ComponentStates.Count > 0)
            {
                Log.DebugIfEnabled("[ProperSaveIntegration] Total serializers handling {0}: {1}", obj.name, objData.ComponentStates.Count);
            }
            Log.DebugIfEnabled("[ProperSaveIntegration] No serializers handled {0}!", obj.name);

            return objData;
        }

        private static void CheckObjectInSeats(GameObject obj, out bool? isMainSeatObject, out int? additionalSeatIndex)
        {
            isMainSeatObject = null;
            additionalSeatIndex = null;

            var controllers = UnityEngine.Object.FindObjectsByType<RoR2.DrifterBagController>(UnityEngine.FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (controller == null) continue;

                // Check main seat
                var mainSeat = API.DrifterBagAPI.GetMainPassenger(controller);
                if (mainSeat != null && mainSeat.GetInstanceID() == obj.GetInstanceID())
                {
                    isMainSeatObject = true;
                    return;
                }

                // Check additional seats
                var additionalSeats = API.DrifterBagAPI.GetAdditionalSeats(controller);
                int seatIndex = 0;
                foreach (var kvp in additionalSeats)
                {
                    if (kvp.Key != null && kvp.Key.GetInstanceID() == obj.GetInstanceID())
                    {
                        additionalSeatIndex = seatIndex;
                        return;
                    }
                    seatIndex++;
                }
            }
        }

        private static string GetPrimaryComponentType(GameObject obj)
        {
            foreach (var comp in obj.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                if (type.Namespace?.StartsWith("UnityEngine") == true) continue;
                if (type.Namespace?.StartsWith("System") == true) continue;
                return type.AssemblyQualifiedName;
            }
            return string.Empty;
        }

        // ========================================================================================
        // STATE RESTORATION
        // ========================================================================================

        private static System.Collections.IEnumerator DelayedStateRestoration(GameObject obj, BaggedObjectSaveData objData, System.Action onComplete)
        {
            // Wait a few frames to ensure NetworkBehaviour components are fully initialized (needs a revisit)
            yield return null;  // Wait 1 frame for object initialization
            yield return null;  // Wait 2nd frame for NetworkBehaviour sync

            // Now restore the object state
            RestoreObjectState(obj, objData);

            // Call the completion callback
            onComplete?.Invoke();
        }

        private static void EnsureSOAFromSaveData(GameObject obj, BaggedObjectSaveData objData)
        {
            if (obj == null || objData == null || objData.ComponentStates == null)
                return;

            bool hasSOAInSaveData = objData.ComponentStates.Any(entry =>
                entry.PluginName.Contains("SpecialObjectAttributes", StringComparison.OrdinalIgnoreCase));

            if (!hasSOAInSaveData)
                return;

            bool hasSOA = obj.GetComponent<RoR2.SpecialObjectAttributes>() != null;
            if (hasSOA)
                return;

            Patches.GrabbableObjectPatches.AddSpecialObjectAttributesToGrabbableObject(obj);
        }

        private static void RestoreBagState(DrifterBagSaveData saveData)
        {
            if (saveData == null || saveData.BaggedObjects == null)
            {
                Log.Error("[ProperSave] Invalid save data");
                return;
            }

            var objectsToRestore = new List<(GameObject obj, BaggedObjectSaveData data)>();
            var spawnedMasters = new HashSet<int>();
            var sortedObjects = saveData.BaggedObjects
                .OrderBy(o => o.BagSlotIndex >= 0 ? o.BagSlotIndex : int.MaxValue)
                .ToList();

            foreach (var objData in sortedObjects)
            {
                try
                {
                    if (objData == null) continue;
                    var obj = ObjectSpawner.SpawnObjectFromSaveData(objData, objData.OwnerPlayerId, spawnedMasters);

                    if (obj != null)
                    {
                        objectsToRestore.Add((obj, objData));
                    }
                    else
                    {
                        Log.DebugIfEnabled($"[ProperSave] Failed to spawn object: {objData.PrefabName}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[ProperSave] Exception spawning {objData?.PrefabName}: {ex.Message}");
                }
            }

            // Now restore all objects after they're spawned
            Log.DebugIfEnabled("[ProperSave] Spawning complete, restoring {0} objects...", objectsToRestore.Count);
            Run.instance.StartCoroutine(RestoreAllObjects(objectsToRestore));
        }

        private static System.Collections.IEnumerator RestoreAllObjects(List<(GameObject obj, BaggedObjectSaveData data)> objectsToRestore)
        {
            int successCount = 0;
            int failureCount = 0;

            foreach (var (obj, objData) in objectsToRestore)
            {
                yield return null;
                yield return null;

                GameObject? objectToAutoGrab = obj;
                bool isCharacterMaster = obj.GetComponent<CharacterMaster>() != null;

                // For CharacterMaster objects, wait for body to spawn before processing
                if (isCharacterMaster)
                {
                    var master = obj.GetComponent<CharacterMaster>();
                    if (master != null)
                    {
                        int waitFrame = 0;
                        int maxWaitFrames = 30;
                        while (master.GetBody() == null && waitFrame < maxWaitFrames)
                        {
                            yield return null;
                            waitFrame++;
                        }

                        var body = master.GetBody();
                        if (body != null)
                        {
                            objectToAutoGrab = body.gameObject;
                            isCharacterMaster = false;
                            Log.DebugIfEnabled("[RestoreAllObjects] Auto-grabbing body {0} from master {1}", body.name, master.name);
                        }
                    }
                }

                try
                {
                    Log.DebugIfEnabled("[RestoreAllObjects] Restoring {0} (frame {1})", obj.name, Time.frameCount);

                    // Refresh BodyColliderCache to ensure it has valid collider references
                    var colliderCache = obj.GetComponent<BodyColliderCache>();
                    if (colliderCache != null)
                    {
                        colliderCache.RefreshCache();
                        Log.DebugIfEnabled("[RestoreAllObjects] Refreshed BodyColliderCache for {0}", obj.name);
                    }

                    // Check health before restoration
                    var healthBefore = obj.GetComponent<RoR2.HealthComponent>();
                    if (healthBefore != null)
                    {
                        Log.DebugIfEnabled("[RestoreAllObjects] Health BEFORE restoration: health={0}, fullHealth={1}", healthBefore.health, healthBefore.fullHealth);
                    }

                    // Now restore the object state
                    RestoreObjectState(obj, objData);

                    // Check health after restoration
                    var healthAfter = obj.GetComponent<RoR2.HealthComponent>();
                    if (healthAfter != null)
                    {
                        Log.DebugIfEnabled("[RestoreAllObjects] Health AFTER restoration: health={0}, fullHealth={1}, healthFraction={2}", healthAfter.health, healthAfter.fullHealth, healthAfter.healthFraction);
                    }

                    if (!isCharacterMaster)
                    {
                        bool wasInSeat = objData.IsMainSeatObject == true || objData.AdditionalSeatIndex.HasValue;
                        if (wasInSeat || PersistenceObjectManager.GetCachedEnableAutoGrab())
                        {
                            DrifterBagAPI.ScheduleAutoGrab(objectToAutoGrab, objData.OwnerPlayerId);
                        }
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    Log.Error($"[ProperSave] Exception restoring {objData?.PrefabName}: {ex.Message}");
                }
            }

            Log.DebugIfEnabled("[ProperSave] Restoration complete: {0} success, {1} failed", successCount, failureCount);
        }

        private static void RestoreObjectState(GameObject obj, BaggedObjectSaveData objData)
        {
            if (obj == null)
            {
                Log.Error("[ProperSave] RestoreObjectState: obj is null");
                return;
            }

            if (objData == null)
            {
                Log.Error("[ProperSave] RestoreObjectState: objData is null");
                return;
            }

            if (objData.ComponentStates == null)
            {
                Log.DebugIfEnabled($"[ProperSave] RestoreObjectState: ComponentStates is null for {objData.ObjectName}");
                return;
            }

            EnsureSOAFromSaveData(obj, objData);

            foreach (var entry in objData.ComponentStates)
            {
                try
                {
                    var plugin = _serializerPlugins.FirstOrDefault(p => p.PluginName == entry.PluginName);

                    if (plugin != null)
                    {
                        GameObject targetObj = obj;

                        if (!plugin.CanHandle(targetObj))
                        {
                            // If dealing with a CharacterBody but the plugin wants a master
                            var body = targetObj.GetComponent<RoR2.CharacterBody>();
                            if (body != null && body.master != null && plugin.CanHandle(body.master.gameObject))
                            {
                                targetObj = body.master.gameObject;
                                Log.DebugIfEnabled("[ProperSave] Cross-resolved CharacterBody to CharacterMaster {0} for plugin '{1}'", body.master.name, entry.PluginName);
                            }
                            else
                            {
                                // If dealing with a CharacterMaster but the plugin wants a body
                                var master = targetObj.GetComponent<RoR2.CharacterMaster>();
                                if (master != null && master.GetBody() != null && plugin.CanHandle(master.GetBody().gameObject))
                                {
                                    targetObj = master.GetBody().gameObject;
                                    Log.DebugIfEnabled("[ProperSave] Cross-resolved CharacterMaster to CharacterBody {0} for plugin '{1}'", master.GetBody().name, entry.PluginName);
                                }
                            }
                        }

                        if (plugin.CanHandle(targetObj))
                        {
                            var state = new Dictionary<string, object>();

                            foreach (var value in entry.Values)
                            {
                                var deserializedValue = SerializationHelpers.DeserializeValue(value.Value, value.Type);
                                if (deserializedValue != null)
                                {
                                    state[value.Key] = deserializedValue;
                                }
                            }

                            plugin.RestoreState(targetObj, state);

                            Log.DebugIfEnabled("[ProperSave] Plugin '{0}' handled {1}, restored {2} values", entry.PluginName, targetObj.name, state.Count);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[ProperSave] Failed to restore state for plugin '{entry.PluginName}' on {obj.name}: {ex.Message}");
                }
            }
        }

        private static bool IsProperSaveAvailable()
        {
            return Chainloader.PluginInfos.ContainsKey(PROPER_SAVE_GUID);
        }

        private static string GetSpawnCardPath(GameObject obj)
        {
            var networkIdentity = obj.GetComponent<NetworkIdentity>();
            if (networkIdentity == null) return string.Empty;

            SpawnCard? spawnCard = null;

            if (!networkIdentity.assetId.Equals(default))
            {
                var assetId = new Guid(networkIdentity.assetId.ToString());
                spawnCard = SpawnCardRegistry.FindSpawnCardByAssetIdExact(assetId);
            }

            if (spawnCard == null)
            {
                spawnCard = SpawnCardRegistry.FindSpawnCardByPrefabHashExact(networkIdentity.assetId);
            }

            if (spawnCard == null && !string.IsNullOrEmpty(obj.name))
            {
                spawnCard = SpawnCardRegistry.FindSpawnCardByExactName(obj.name);
            }

            if (spawnCard != null)
            {
                return spawnCard.name;
            }

            return string.Empty;
        }
    }
}

