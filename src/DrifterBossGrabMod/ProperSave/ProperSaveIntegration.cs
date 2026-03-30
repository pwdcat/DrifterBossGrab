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
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave.Matching;
using DrifterBossGrabMod.ProperSave.Serializers;
using DrifterBossGrabMod.ProperSave.Spawning;

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

        public static class Spawning
        {
            public const float ForwardPlacementDistance = 3f;
            public const float UpwardPlacementOffset = 0.5f;
        }

        public static class Validation
        {
            public const float PositionMatchWeight = 0.3f;
            public const float ComponentTypeMatchWeight = 0.2f;
            public const float ExactPrefabHashConfidence = 0.95f;
            public const float ExactAssetIdConfidence = 1.0f;
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
            Log.Info($"[ProperSave] Integration initialized with {_serializerPlugins.Count} plugins");
        }

        public static void Cleanup()
        {
            if (!_initialized) return;

            Run.onRunStartGlobal -= OnRunStart;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

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
            // Use a delay to ensure scene is fully loaded
            Run.instance.StartCoroutine(DelayedSceneLoadRestoration(nextScene));
        }

        private static System.Collections.IEnumerator DelayedSceneLoadRestoration(Scene scene)
        {
            // Wait a frame to ensure scene is fully loaded
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
            var characterBodySerializer = BuiltInSerializers.ForCharacterBody();
            _serializerPlugins.Add(characterBodySerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {characterBodySerializer.GetType().Name} (Priority: {characterBodySerializer.Priority})");
            }

            var enemyInventorySerializer = new Serializers.Plugins.EnemyInventorySerializerPlugin();
            _serializerPlugins.Add(enemyInventorySerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {enemyInventorySerializer.GetType().Name} (Priority: {enemyInventorySerializer.Priority})");
            }

            // Interactable serializers (declarative)
            var chestSerializer = BuiltInSerializers.ForChest();
            _serializerPlugins.Add(chestSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {chestSerializer.GetType().Name} (Priority: {chestSerializer.Priority})");
            }

            var duplicatorSerializer = BuiltInSerializers.ForDuplicator();
            _serializerPlugins.Add(duplicatorSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {duplicatorSerializer.GetType().Name} (Priority: {duplicatorSerializer.Priority})");
            }

            var shrineSerializer = BuiltInSerializers.ForShrine();
            _serializerPlugins.Add(shrineSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {shrineSerializer.GetType().Name} (Priority: {shrineSerializer.Priority})");
            }

            var soaSerializer = BuiltInSerializers.ForSpecialObjectAttributes();
            _serializerPlugins.Add(soaSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {soaSerializer.GetType().Name} (Priority: {soaSerializer.Priority})");
            }

            var junkCubeSerializer = BuiltInSerializers.ForJunkCubeController();
            _serializerPlugins.Add(junkCubeSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {junkCubeSerializer.GetType().Name} (Priority: {junkCubeSerializer.Priority})");
            }

            var halcyoniteShrineSerializer = BuiltInSerializers.ForHalcyoniteShrineInteractable();
            _serializerPlugins.Add(halcyoniteShrineSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {halcyoniteShrineSerializer.GetType().Name} (Priority: {halcyoniteShrineSerializer.Priority})");
            }

            var tinkerableSerializer = BuiltInSerializers.ForTinkerableObjectAttributes();
            _serializerPlugins.Add(tinkerableSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {tinkerableSerializer.GetType().Name} (Priority: {tinkerableSerializer.Priority})");
            }

            var purchaseSerializer = BuiltInSerializers.ForPurchaseInteraction();
            _serializerPlugins.Add(purchaseSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {purchaseSerializer.GetType().Name} (Priority: {purchaseSerializer.Priority})");
            }

            // Reflection-based fallbacks and integrations
            var genericSerializer = new Serializers.Plugins.GenericComponentSerializerPlugin();
            _serializerPlugins.Add(genericSerializer);
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Added: {genericSerializer.GetType().Name} (Priority: {genericSerializer.Priority})");
            }

            var qualitySerializer = new Serializers.Plugins.QualityIntegrationPlugin();
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
            Log.Info($"[Serializer] Registered plugin: {plugin.GetType().Name} (Priority: {plugin.Priority})");
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

            foreach (var obj in persistedObjects)
            {
                if (obj == null) continue;

                var objData = CaptureObjectData(obj);
                if (objData != null)
                {
                    saveData.BaggedObjects.Add(objData);
                }
            }

            return saveData;
        }

        private static string SerializeVector3(Vector3 v) => $"{v.x}|{v.y}|{v.z}";
        private static string SerializeQuaternion(Quaternion q) => $"{q.x}|{q.y}|{q.z}|{q.w}";
        private static string SerializeDateTime(DateTime dt) => dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        private static string SerializeGuid(Guid? guid) => guid?.ToString() ?? string.Empty;

        private static Vector3 ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector3.zero;
            var parts = s.Split('|');
            if (parts.Length != 3) return Vector3.zero;
            float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x);
            float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z);
            return new Vector3(x, y, z);
        }

        private static Quaternion ParseQuaternion(string s)
        {
            if (string.IsNullOrEmpty(s)) return Quaternion.identity;
            var parts = s.Split('|');
            if (parts.Length != 4) return Quaternion.identity;
            float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x);
            float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var z);
            float.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w);
            return new Quaternion(x, y, z, w);
        }

        private static DateTime ParseDateTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.Now;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                return dt;
            return DateTime.Now;
        }

        private static Guid? ParseGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (Guid.TryParse(s, out var guid))
                return guid;
            return null;
        }

        private static BaggedObjectSaveData? CaptureObjectData(GameObject obj)
        {
            if (obj == null) return null;

            var networkIdentity = obj.GetComponent<NetworkIdentity>();
            if (networkIdentity == null)
            {
                return null;
            }

            var objData = new BaggedObjectSaveData
            {
                ObjectName = obj.name,
                PrefabName = obj.name,
                ObjectInstanceId = obj.GetInstanceID(),
                SceneName = SceneManager.GetActiveScene().name,
                OwnerPlayerId = PersistenceObjectManager.GetPersistedObjectOwnerPlayerId(obj) ?? string.Empty,

                Position = SerializeVector3(obj.transform.position),
                Rotation = SerializeQuaternion(obj.transform.rotation),

                AssetId = SerializeGuid(new Guid(networkIdentity.assetId.ToString())),
                PrefabHash = networkIdentity.assetId.ToString(),

                ComponentType = GetPrimaryComponentType(obj),

                ValidationInfo = new ObjectValidationInfo
                {
                    SaveTime = SerializeDateTime(DateTime.Now),
                    StageName = SceneManager.GetActiveScene().name,
                    StageClearCount = Run.instance?.stageClearCount ?? 0
                }
            };

            objData.SpawnCardPath = GetSpawnCardPath(obj);

            foreach (var plugin in _serializerPlugins)
            {
                if (plugin.CanHandle(obj))
                {
                    var state = plugin.CaptureState(obj);
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
                            var valueStr = SerializeValue(value);

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

        private static string GetPrimaryComponentType(GameObject obj)
        {
            // Scan for ANY component that inherits from NetworkBehaviour or MonoBehaviour
            // Prioritizing RoR2-specific types but falling back to the first meaningful component
            foreach (var comp in obj.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                // Skip Unity/System internals
                if (type.Namespace?.StartsWith("UnityEngine") == true) continue;
                if (type.Namespace?.StartsWith("System") == true) continue;
                return type.AssemblyQualifiedName;
            }
            return string.Empty;
        }

        private static System.Collections.IEnumerator DelayedStateRestoration(GameObject obj, BaggedObjectSaveData objData, System.Action onComplete)
        {
            // Wait a few frames to ensure NetworkBehaviour components are fully initialized
            yield return null;  // Wait 1 frame for object initialization
            yield return null;  // Wait 2nd frame for NetworkBehaviour sync

            // Now restore the object state
            RestoreObjectState(obj, objData);

            // Call the completion callback
            onComplete?.Invoke();
        }

        private static void RestoreBagState(DrifterBagSaveData saveData)
        {
            if (saveData == null || saveData.BaggedObjects == null)
            {
                Log.Error("[ProperSave] Invalid save data");
                return;
            }

            // We use successCount/failureCount differently now because restoration is async
            // These are just for tracking, actual success/failure is handled in the coroutine
            var objectsToRestore = new List<(GameObject obj, BaggedObjectSaveData data)>();

            foreach (var objData in saveData.BaggedObjects)
            {
                try
                {
                    if (objData == null) continue;

                    // Always spawn new objects from save data (like persistence system does)
                    // Don't try to match to scene objects since we're loading from a save file
                    var obj = ObjectSpawner.SpawnObjectFromSaveData(objData, objData.OwnerPlayerId);

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
                    Log.Error($"[ProperSave] Exception spawning {objData?.PrefabName}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            // Now restore all objects after they're spawned
            Log.Info($"[ProperSave] Spawning complete, restoring {objectsToRestore.Count} objects...");
            Run.instance.StartCoroutine(RestoreAllObjects(objectsToRestore));
        }

        private static System.Collections.IEnumerator RestoreAllObjects(List<(GameObject obj, BaggedObjectSaveData data)> objectsToRestore)
        {
            int successCount = 0;
            int failureCount = 0;

            foreach (var (obj, objData) in objectsToRestore)
            {
                yield return null;  // Wait 1 frame for object initialization
                yield return null;  // Wait 2nd frame for NetworkBehaviour sync

                try
                {
                    // Now restore the object state
                    RestoreObjectState(obj, objData);

                    // Only auto-grab if object wasn't already in a seat when saved
                    // Skip CharacterMaster objects - they're AI controllers, not grabbable objects
                    bool wasInSeat = objData.IsMainSeatObject == true || objData.AdditionalSeatIndex.HasValue;
                    bool isCharacterMaster = obj.GetComponent<CharacterMaster>() != null;
                    if (!wasInSeat && !isCharacterMaster && PersistenceObjectManager.GetCachedEnableAutoGrab())
                    {
                        ScheduleAutoGrab(obj);
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    Log.Error($"[ProperSave] Exception restoring {objData?.PrefabName}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            Log.Info($"[ProperSave] Restoration complete: {successCount} success, {failureCount} failed");
        }

        private static void ScheduleAutoGrab(GameObject obj)
        {
            if (!NetworkServer.active) return;

            var drifterBody = PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master?.bodyPrefab?.name?.Contains("Drifter") == true)
                .Select(pcm => pcm.master.GetBody())
                .FirstOrDefault();

            if (drifterBody == null)
            {
                return;
            }

            var bagController = drifterBody.GetComponent<DrifterBagController>();
            if (bagController == null)
            {
                bagController = drifterBody.GetComponentInParent<DrifterBagController>();
            }

            if (bagController == null)
            {
                return;
            }

            DrifterBagAPI.ScheduleAutoGrab(bagController, obj, PluginConfig.Instance.AutoGrabDelay.Value);
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

            foreach (var entry in objData.ComponentStates)
            {
                try
                {
                    var plugin = _serializerPlugins.FirstOrDefault(p => p.PluginName == entry.PluginName);

                    if (plugin != null && plugin.CanHandle(obj))
                    {
                        var state = new Dictionary<string, object>();

                        foreach (var value in entry.Values)
                        {
                            var deserializedValue = DeserializeValue(value.Value, value.Type);
                            if (deserializedValue != null)
                            {
                                state[value.Key] = deserializedValue;
                            }
                        }

                        plugin.RestoreState(obj, state);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[ProperSave] Failed to restore state for plugin '{entry.PluginName}' on {obj.name}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        private static string SerializeValue(object? value)
        {
            if (value == null) return "";

            if (value is bool b) return b.ToString();
            if (value is int i) return i.ToString();
            if (value is uint u) return u.ToString();
            if (value is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is string s) return s;

            return value.ToString() ?? "";
        }

        private static object? DeserializeValue(string value, string typeStr)
        {
            if (string.IsNullOrEmpty(value)) return null;

            try
            {
                switch (typeStr)
                {
                    case "System.Boolean":
                    case "bool":
                        return bool.Parse(value);

                    case "System.Int32":
                    case "int":
                        return int.Parse(value);

                    case "System.UInt32":
                    case "uint":
                        return uint.Parse(value);

                    case "System.Single":
                    case "float":
                        return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

                    case "System.Double":
                    case "double":
                        return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

                    case "System.String":
                    case "string":
                        return value;
                }
            }
            catch (Exception ex)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Warning($"[ProperSave] Failed to deserialize value '{value}' of type '{typeStr}': {ex.Message}");
                }
            }

            return value;
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
