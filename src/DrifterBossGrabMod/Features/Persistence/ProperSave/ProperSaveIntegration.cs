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
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[ProperSave] Integration initialized with {_serializerPlugins.Count} plugins");
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
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ProperSave] OnRunStart called, _pendingSaveData is {(_pendingSaveData == null ? "null" : $"not null ({_pendingSaveData.BaggedObjects?.Count ?? 0} objects)")}");
            }
        }

        private static void OnActiveSceneChanged(Scene prevScene, Scene nextScene)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ProperSave] OnActiveSceneChanged called - prevScene: {prevScene.name}, nextScene: {nextScene.name}");
            }

            if (_pendingSaveData == null) return;

            // Only restore when loading a save (Single mode scene load to game scene)
            Run.instance.StartCoroutine(DelayedSceneLoadRestoration(nextScene));
        }

        private static System.Collections.IEnumerator DelayedSceneLoadRestoration(Scene scene)
        {
            yield return null;

            if (_pendingSaveData == null)
            {
                yield break;
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ProperSave] Starting WaitForDirectorCoreAndRestore coroutine for {_pendingSaveData.BaggedObjects?.Count ?? 0} objects");
            }
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ProperSave] Restoring {_pendingSaveData.BaggedObjects.Count} bagged objects");
            }

            RestoreBagState(_pendingSaveData!);
            _pendingSaveData = null;
        }

        private static void RegisterBuiltInPlugins()
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info("[ProperSaveIntegration] Registering built-in serializer plugins...");
            }

            // Enemy serializers (highest priority, 1:1 restoration)
            var characterMasterSerializer = BuiltInSerializersAPI.ForCharacterMaster();
            _serializerPlugins.Add(characterMasterSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {characterMasterSerializer.GetType().Name} (Priority: {characterMasterSerializer.Priority})");
            }

            var characterBodySerializer = BuiltInSerializersAPI.ForCharacterBody();
            _serializerPlugins.Add(characterBodySerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {characterBodySerializer.GetType().Name} (Priority: {characterBodySerializer.Priority})");
            }

            // Interactable serializers (API-based)
            var chestSerializer = BuiltInSerializersAPI.ForChest();
            _serializerPlugins.Add(chestSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {chestSerializer.GetType().Name} (Priority: {chestSerializer.Priority})");
            }

            var duplicatorSerializer = BuiltInSerializersAPI.ForDuplicator();
            _serializerPlugins.Add(duplicatorSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {duplicatorSerializer.GetType().Name} (Priority: {duplicatorSerializer.Priority})");
            }

            var shrineSerializer = BuiltInSerializersAPI.ForShrine();
            _serializerPlugins.Add(shrineSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {shrineSerializer.GetType().Name} (Priority: {shrineSerializer.Priority})");
            }

            var soaSerializer = BuiltInSerializersAPI.ForSpecialObjectAttributes();
            _serializerPlugins.Add(soaSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {soaSerializer.GetType().Name} (Priority: {soaSerializer.Priority})");
            }

            var junkCubeSerializer = BuiltInSerializersAPI.ForJunkCubeController();
            _serializerPlugins.Add(junkCubeSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {junkCubeSerializer.GetType().Name} (Priority: {junkCubeSerializer.Priority})");
            }

            var halcyoniteShrineSerializer = BuiltInSerializersAPI.ForHalcyoniteShrineInteractable();
            _serializerPlugins.Add(halcyoniteShrineSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {halcyoniteShrineSerializer.GetType().Name} (Priority: {halcyoniteShrineSerializer.Priority})");
            }

            var tinkerableSerializer = BuiltInSerializersAPI.ForTinkerableObjectAttributes();
            _serializerPlugins.Add(tinkerableSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {tinkerableSerializer.GetType().Name} (Priority: {tinkerableSerializer.Priority})");
            }

            var purchaseSerializer = BuiltInSerializersAPI.ForPurchaseInteraction();
            _serializerPlugins.Add(purchaseSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {purchaseSerializer.GetType().Name} (Priority: {purchaseSerializer.Priority})");
            }

            // Reflection-based fallbacks and integrations
            var genericSerializer = BuiltInSerializersAPI.ForGenericComponentSerializer();
            _serializerPlugins.Add(genericSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {genericSerializer.GetType().Name} (Priority: {genericSerializer.Priority})");
            }

            var qualitySerializer = BuiltInSerializersAPI.ForQualityIntegration();
            _serializerPlugins.Add(qualitySerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {qualitySerializer.GetType().Name} (Priority: {qualitySerializer.Priority})");
            }

            // Sort by priority
            _serializerPlugins.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ProperSaveIntegration] Total registered plugins: {_serializerPlugins.Count}");
            }
        }

        public static void RegisterPlugin(IObjectSerializerPlugin plugin)
        {
            if (plugin == null) return;
            _serializerPlugins.Add(plugin);
            _serializerPlugins.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[Serializer] Registered plugin: {plugin.GetType().Name} (Priority: {plugin.Priority})");
        }

        public static List<IObjectSerializerPlugin> GetSerializerPlugins()
        {
            return new List<IObjectSerializerPlugin>(_serializerPlugins);
        }

        private static void OnGatherSaveData(Dictionary<string, object> gatheredData)
        {
            if (!PluginConfig.Instance.EnableObjectPersistence.Value)
            {
                return;
            }

            var saveData = CreateSaveData();
            gatheredData[SAVE_KEY] = saveData;

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ProperSave] Saved {saveData.BaggedObjects.Count} bagged objects");
            }
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
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"[ProperSave] Queued {saveData.BaggedObjects.Count} bagged objects for restoration");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ProperSave] Failed to queue bag data: {ex.Message}");
            }
        }

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
                var state = Patches.BagPatches.GetState(controller);
                if (state?.BaggedObjects == null) continue;

                lock (state.BagLock)
                {
                    for (int i = 0; i < state.BaggedObjects.Count; i++)
                    {
                        var bagObj = state.BaggedObjects[i];
                        if (bagObj != null)
                        {
                            bagSlotIndexMap[bagObj.GetInstanceID()] = i;
                        }
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[ProperSaveIntegration] Skipping teleporter object {obj.name} (Teleporter is blacklisted)");
                    }
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
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[CaptureObjectData] Skipping duplicate capture for {objectToCapture.name} (from source {obj.name})");
                    }
                    return null;
                }
                capturedInstanceIds.Add(instanceId);
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                if (isSavingMaster && master != null)
                    Log.Info($"[CaptureObjectData] Capturing master {master.name} for object {obj.name}");
                else
                    Log.Info($"[CaptureObjectData] Capturing body/object {obj.name}");
            }

            string? masterName = master?.name;

            // Check if object is currently in a seat
            bool? isMainSeatObject = null;
            int? additionalSeatIndex = null;
            CheckObjectInSeats(obj, out isMainSeatObject, out additionalSeatIndex);

            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[CaptureObjectData] Capturing data for {obj.name}");
                var components = obj.GetComponents<Component>();
                Log.Info($"[CaptureObjectData] Components on object ({components.Length} total): {string.Join(", ", components.Take(10).Select(c => c.GetType().Name))}");

                var masterComponent = obj.GetComponent<RoR2.CharacterMaster>();
                if (masterComponent != null)
                {
                    Log.Info($"[CaptureObjectData] Object HAS CharacterMaster component: {masterComponent.name}");
                }
                else
                {
                    Log.Info($"[CaptureObjectData] Object does not have CharacterMaster component");
                    if (characterBody != null && characterBody.master != null)
                    {
                        Log.Info($"[CaptureObjectData] CharacterBody's master is: {characterBody.master.name} (saved as: {masterName})");
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[ProperSaveIntegration] Plugin '{plugin.PluginName}' handled {obj.name}, captured {state.Count} values");
                        }

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

            if (PluginConfig.Instance.EnableDebugLogs.Value && objData.ComponentStates.Count > 0)
            {
                Log.Info($"[ProperSaveIntegration] Total serializers handling {obj.name}: {objData.ComponentStates.Count}");
            }
            else if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Warning($"[ProperSaveIntegration] No serializers handled {obj.name}!");
            }

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

                var state = Patches.BagPatches.GetState(controller);
                if (state == null) continue;

                // Check main seat
                if (state.MainSeatObject != null && state.MainSeatObject.GetInstanceID() == obj.GetInstanceID())
                {
                    isMainSeatObject = true;
                    return;
                }

                // Check additional seats
                int seatIndex = 0;
                foreach (var kvp in state.AdditionalSeats)
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
                        Log.Warning($"[ProperSave] Failed to spawn object: {objData.PrefabName}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[ProperSave] Exception spawning {objData?.PrefabName}: {ex.Message}");
                }
            }

            // Now restore all objects after they're spawned
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[ProperSave] Spawning complete, restoring {objectsToRestore.Count} objects...");
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
                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                                Log.Debug($"[RestoreAllObjects] Auto-grabbing body {body.name} from master {master.name}");
                        }
                    }
                }

                try
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Debug($"[RestoreAllObjects] Restoring {obj.name} (frame {Time.frameCount})");

                    // Refresh BodyColliderCache to ensure it has valid collider references
                    var colliderCache = obj.GetComponent<BodyColliderCache>();
                    if (colliderCache != null)
                    {
                        colliderCache.RefreshCache();
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($"[RestoreAllObjects] Refreshed BodyColliderCache for {obj.name}");
                        }
                    }

                    // Check health before restoration
                    var healthBefore = obj.GetComponent<RoR2.HealthComponent>();
                    if (healthBefore != null && PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[RestoreAllObjects] Health BEFORE restoration: health={healthBefore.health}, fullHealth={healthBefore.fullHealth}");
                    }

                    // Now restore the object state
                    RestoreObjectState(obj, objData);

                    // Check health after restoration
                    var healthAfter = obj.GetComponent<RoR2.HealthComponent>();
                    if (healthAfter != null && PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($"[RestoreAllObjects] Health AFTER restoration: health={healthAfter.health}, fullHealth={healthAfter.fullHealth}, healthFraction={healthAfter.healthFraction}");
                    }

                    if (!isCharacterMaster)
                    {
                        bool wasInSeat = objData.IsMainSeatObject == true || objData.AdditionalSeatIndex.HasValue;
                        if (wasInSeat || PersistenceObjectManager.GetCachedEnableAutoGrab())
                        {
                            PersistenceSceneHandler.ScheduleAutoGrabForObject(objectToAutoGrab, objData.OwnerPlayerId);
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

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Debug($"[ProperSave] Restoration complete: {successCount} success, {failureCount} failed");
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
                Log.Warning($"[ProperSave] RestoreObjectState: ComponentStates is null for {objData.ObjectName}");
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
                                if (PluginConfig.Instance.EnableDebugLogs.Value)
                                    Log.Info($"[ProperSave] Cross-resolved CharacterBody to CharacterMaster {body.master.name} for plugin '{entry.PluginName}'");
                            }
                            else
                            {
                                // If dealing with a CharacterMaster but the plugin wants a body
                                var master = targetObj.GetComponent<RoR2.CharacterMaster>();
                                if (master != null && master.GetBody() != null && plugin.CanHandle(master.GetBody().gameObject))
                                {
                                    targetObj = master.GetBody().gameObject;
                                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                                        Log.Info($"[ProperSave] Cross-resolved CharacterMaster to CharacterBody {master.GetBody().name} for plugin '{entry.PluginName}'");
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

                            if (PluginConfig.Instance.EnableDebugLogs.Value)
                            {
                                Log.Info($"[ProperSave] Plugin '{entry.PluginName}' handled {targetObj.name}, restored {state.Count} values");
                            }
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

