#nullable enable
using System;
using DrifterBossGrabMod;
using DrifterBossGrabMod.ProperSave.Core;
using System.Collections.Generic;
using DrifterBossGrabMod.ProperSave.Data;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod.ProperSave.Matching;

namespace DrifterBossGrabMod.ProperSave.Spawning
{
    public static class ObjectSpawner
    {
        public static void Initialize()
        {
            SpawnCardRegistry.Initialize();
        }

        public static GameObject? SpawnObjectFromSaveData(BaggedObjectSaveData objData, string? ownerPlayerId = null)
        {
            if (DirectorCore.instance == null)
            {
                Log.Error("[ObjectSpawn] DirectorCore instance not available");
                return null;
            }

            if (IsCharacterMaster(objData.PrefabName))
            {
                Log.Info($"[ObjectSpawn] Detected CharacterMaster {objData.PrefabName}, using PrefabSpawner");
                return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
            }

            // Find SpawnCard by exact AssetId, PrefabHash, or exact name
            var spawnCard = FindSpawnCardExact(objData);

            if (spawnCard == null)
            {
                // If we're trying to spawn an enemy body, try to find the master spawn card instead
                if (IsEnemyBody(objData.PrefabName))
                {
                    var masterName = objData.PrefabName.Replace("Body", "Master");
                    Log.Info($"[ObjectSpawn] Trying to find master spawn card '{masterName}' for enemy body '{objData.PrefabName}'");

                    var masterSpawnCard = SpawnCardRegistry.FindSpawnCardByExactName(masterName);
                    if (masterSpawnCard != null)
                    {
                        spawnCard = masterSpawnCard;
                        Log.Info($"[ObjectSpawn] Found master spawn card for {masterName}");
                    }
                }

                if (spawnCard == null)
                {
                    Log.Warning($"[ObjectSpawn] Could not find SpawnCard for {objData.PrefabName}, trying PrefabSpawner fallback");

                    // Fallback to direct prefab instantiation
                    return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
                }
            }

            if (spawnCard.prefab == null)
            {
                Log.Error($"[ObjectSpawn] Spawn card '{spawnCard.name}' has no prefab!");
                return null;
            }

            // Create placement rule that positions objects bunched up near the player
            var placementRule = CreatePlacementRuleForRestoration(objData, ownerPlayerId);

            var spawnRequest = new DirectorSpawnRequest(
                spawnCard,
                placementRule,
                RoR2Application.rng
            );

            var spawnedObject = DirectorCore.instance.TrySpawnObject(spawnRequest);

            if (spawnedObject != null)
            {
                // Handle CharacterMaster team assignment
                var characterMaster = spawnedObject.GetComponent<CharacterMaster>();
                if (characterMaster != null)
                {
                    // Get saved team index from save data, default to Monster team
                    var savedTeamIndex = GetSavedTeamIndex(objData);
                    characterMaster.teamIndex = savedTeamIndex ?? TeamIndex.Monster;

                    Log.Info($"[ObjectSpawn] Assigned team {characterMaster.teamIndex} to {spawnedObject.name}");

                    // Spawn the body for the master at the spawned object's position
                    var spawnedBody = characterMaster.SpawnBody(spawnedObject.transform.position, spawnedObject.transform.rotation);

                    // If we're expecting a body and successfully spawned one, use the body instead of master
                    if (spawnedBody != null && objData.PrefabName.EndsWith("Body"))
                    {
                        Log.Info($"[ObjectSpawn] Using spawned body {spawnedBody.name} instead of master {spawnedObject.name}");
                        spawnedObject = spawnedBody.gameObject;
                    }
                }

                // Reparent object from persistence container before processing
                if (spawnedObject.transform.parent != null && spawnedObject.transform.parent.name == "DBG_PersistenceContainer")
                {
                    spawnedObject.transform.SetParent(null, true);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawnedObject, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }

                // Log components on spawned object for debugging
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var components = spawnedObject.GetComponents<Component>();
                    Log.Info($"[ObjectSpawn] Spawned object has {components.Length} components:");
                    foreach (var comp in components.Take(15))
                    {
                        Log.Info($"  - {comp.GetType().Name}");
                    }

                    // Check for specific components we're trying to serialize
                    var soa = spawnedObject.GetComponent<SpecialObjectAttributes>();
                    var shrine = spawnedObject.GetComponent<HalcyoniteShrineInteractable>();
                    var charBody = spawnedObject.GetComponent<CharacterBody>();
                    var inventory = spawnedObject.GetComponent<Inventory>();

                if (soa == null) Log.Warning($"  - SpecialObjectAttributes: NOT FOUND");
                else Log.Info($"  - SpecialObjectAttributes: FOUND (durability={soa.durability}, locked={soa.locked})");

                if (shrine == null) Log.Warning($"  - HalcyoniteShrineInteractable: NOT FOUND");
                else Log.Info($"  - HalcyoniteShrineInteractable: FOUND (interactions={shrine.interactions})");

                if (charBody == null) Log.Warning($"  - CharacterBody: NOT FOUND");
                else Log.Info($"  - CharacterBody: FOUND");

                if (inventory == null) Log.Warning($"  - Inventory: NOT FOUND");
                else Log.Info($"  - Inventory: FOUND (items={inventory.itemAcquisitionOrder.Count})");
                }

                // Validate the spawned object (relaxed validation for restoration)
                var validation = ObjectValidator.ValidateMatch(spawnedObject, objData);

                // For restoration, we only care that the object type is correct, not position
                if (validation.ConfidenceScore < 0.5f)
                {
                    Log.Error($"[ObjectSpawn] Spawned object failed basic validation: {string.Join(", ", validation.ValidationErrors)}");
                    UnityEngine.Object.Destroy(spawnedObject);
                    return null;
                }

                Log.Info($"[ObjectSpawn] Successfully spawned {spawnedObject.name} (confidence: {validation.ConfidenceScore:F2})");
            }
            else
            {
                Log.Warning($"[ObjectSpawn] Failed to spawn {objData.PrefabName}, trying PrefabSpawner fallback");

                // Fallback to direct prefab instantiation
                return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
            }

            return spawnedObject;
        }

        private static DirectorPlacementRule CreatePlacementRuleForRestoration(BaggedObjectSaveData objData, string? ownerPlayerId)
        {
            // Position objects bunched up near the player (like persistence system does)
            var targetBody = FindOwnerBody(ownerPlayerId);

            if (targetBody != null)
            {
                // Position very close to player (0.5 units in front and up, bunched up)
                var playerPos = targetBody.transform.position;
                var playerForward = targetBody.transform.forward;
                var targetPos = playerPos + playerForward * Constants.Limits.PositionOffset + Vector3.up * Constants.Limits.PositionOffset;

                return new DirectorPlacementRule
                {
                    placementMode = DirectorPlacementRule.PlacementMode.Direct,
                    position = targetPos,
                    spawnOnTarget = null
                };
            }
            else
            {
                // Fallback: position at scene center or camera position
                var camera = Camera.main;
                if (camera != null)
                {
                    var cameraPos = camera.transform.position;
                    var cameraForward = camera.transform.forward;
                    var fallbackPos = cameraPos + cameraForward * Constants.Limits.CameraForwardOffset;

                    return new DirectorPlacementRule
                    {
                        placementMode = DirectorPlacementRule.PlacementMode.Direct,
                        position = fallbackPos,
                        spawnOnTarget = null
                    };
                }
                else
                {
                    // Last resort: position at origin with offset
                    return new DirectorPlacementRule
                    {
                        placementMode = DirectorPlacementRule.PlacementMode.Direct,
                        position = new Vector3(0, Constants.Limits.OriginYOffset, 0),
                        spawnOnTarget = null
                    };
                }
            }
        }

        private static CharacterBody? FindOwnerBody(string? ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                // Fallback to host's body (any body)
                var hostUser = RoR2.NetworkUser.readOnlyInstancesList.FirstOrDefault(nu => nu.isServer);
                if (hostUser != null && hostUser.master != null)
                {
                    return hostUser.master.GetBody();
                }
                return null;
            }

            // Find the NetworkUser associated with this player id
            RoR2.NetworkUser? matchedUser = null;
            foreach (var nu in RoR2.NetworkUser.readOnlyInstancesList)
            {
                var id = nu.id;
                var idString = id.strValue != null ? id.strValue : $"{id.value}_{id.subId}";
                if (idString == ownerId)
                {
                    matchedUser = nu;
                    break;
                }
            }

            if (matchedUser != null)
            {
                return matchedUser.master?.GetBody();
            }

            return null;
        }

        private static DirectorPlacementRule CreatePlacementRule(BaggedObjectSaveData objData)
        {
            var savedPosition = ParseVector3(objData.Position);

            // First try: Direct placement at saved position if not zero
            if (savedPosition != Vector3.zero)
            {
                return new DirectorPlacementRule
                {
                    placementMode = DirectorPlacementRule.PlacementMode.Direct,
                    position = savedPosition,
                    spawnOnTarget = null
                };
            }

            // Second try: Direct placement at owner player's position + forward*3f + up*0.5f
            var ownerPlayer = FindOwnerPlayer(objData.OwnerPlayerId);
            if (ownerPlayer != null)
            {
                return new DirectorPlacementRule
                {
                    placementMode = DirectorPlacementRule.PlacementMode.Direct,
                    position = ownerPlayer.transform.position + ownerPlayer.transform.forward * ProperSaveConstants.Spawning.ForwardPlacementDistance + Vector3.up * ProperSaveConstants.Spawning.UpwardPlacementOffset,
                    spawnOnTarget = null
                };
            }

            // Last resort: Random placement
            return new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Random,
                position = Vector3.zero,
                spawnOnTarget = null
            };
        }

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

        private static NetworkHash128 ParsePrefabHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return default;
            try
            {
                return NetworkHash128.Parse(s);
            }
            catch
            {
                return default;
            }
        }

        private static Guid? ParseGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (Guid.TryParse(s, out var guid))
                return guid;
            return null;
        }

        private static TeamIndex? GetSavedTeamIndex(BaggedObjectSaveData objData)
        {
            if (objData == null || objData.ComponentStates == null)
                return null;

            foreach (var entry in objData.ComponentStates)
            {
                foreach (var value in entry.Values)
                {
                    if (value.Key == "teamIndex" && value.Type == "System.Byte")
                    {
                        if (byte.TryParse(value.Value, out var teamByte))
                        {
                            return (TeamIndex)teamByte;
                        }
                    }
                }
            }
            return null;
        }

        private static bool IsCharacterMaster(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;

            return prefabName.EndsWith("Master");
        }

        private static bool IsEnemyBody(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;

            var bodyIndex = BodyCatalog.FindBodyIndex(prefabName);
            if (bodyIndex == BodyIndex.None) return false;

            var survivorIndex = SurvivorCatalog.GetSurvivorIndexFromBodyIndex(bodyIndex);
            return survivorIndex == SurvivorIndex.None;
        }

        private static SpawnCard? FindSpawnCardExact(BaggedObjectSaveData objData)
        {
            var assetId = ParseGuid(objData.AssetId);
            var prefabHash = ParsePrefabHash(objData.PrefabHash);

            // Priority 1: Exact AssetId match
            if (assetId.HasValue && assetId.Value != Guid.Empty)
            {
                var card = SpawnCardRegistry.FindSpawnCardByAssetIdExact(assetId.Value);
                if (card != null) return card;
            }

            // Priority 2: Exact PrefabHash match
            if (!prefabHash.Equals(default))
            {
                var card = SpawnCardRegistry.FindSpawnCardByPrefabHashExact(prefabHash);
                if (card != null) return card;
            }

            // Priority 3: Exact name match
            if (!string.IsNullOrEmpty(objData.PrefabName))
            {
                var card = SpawnCardRegistry.FindSpawnCardByExactName(objData.PrefabName);
                if (card != null) return card;
            }

            return null;
        }

        private static CharacterBody? FindOwnerPlayer(string? ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                return null;
            }

            var playerCharacterBodies = PlayerCharacterMasterController.instances
                .Where(pcm => pcm?.master?.GetBody()?.netId.ToString() == ownerId)
                .Select(pcm => pcm.master.GetBody())
                .FirstOrDefault();

            return playerCharacterBodies;
        }

        private static void RestoreObjectState(GameObject obj, BaggedObjectSaveData objData)
        {
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"[ObjectSpawner] Restoring state for {obj.name}, {objData.ComponentStates?.Count ?? 0} component entries");
                Log.Info($"  - Object components: {string.Join(", ", obj.GetComponents<Component>().Take(10).Select(c => c.GetType().Name))}");
            }

            var purchaseInteraction = obj.GetComponent<PurchaseInteraction>();
            if (purchaseInteraction != null)
            {
                var costState = GetCostFromState(objData);
                if (costState.HasValue)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"  - Restoring PurchaseInteraction cost: {costState.Value}");
                    }
                    purchaseInteraction.Networkcost = costState.Value;
                }
            }

            var allPlugins = ProperSaveIntegration.GetSerializerPlugins();
            if (PluginConfig.Instance.EnableDebugLogs.Value)
            {
                Log.Info($"  - Available plugins: {string.Join(", ", allPlugins.Select(p => p.GetType().Name))}");
            }

            if (objData.ComponentStates == null)
            {
                return;
            }

            foreach (var entry in objData.ComponentStates)
            {
                var plugin = allPlugins
                    .FirstOrDefault(p => p.PluginName == entry.PluginName);

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($"  - Looking for plugin '{entry.PluginName}' for entry with {entry.Values.Count} values");
                    if (plugin != null)
                    {
                        Log.Info($"    - Found plugin: {plugin.PluginName}, Priority: {plugin.Priority}");
                        Log.Info($"    - CanHandle({obj.name}): {plugin.CanHandle(obj)}");
                    }
                    else
                    {
                        Log.Warning($"    - Plugin '{entry.PluginName}' not found!");
                    }
                }

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

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"    - Calling RestoreState with {state.Count} values");
                    }

                    plugin.RestoreState(obj, state);

                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"    - RestoreState completed for {plugin.GetType().Name}");
                    }
                }
                else if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Warning($"  - Skipping plugin '{entry.PluginName}': {(plugin == null ? "not found" : "CanHandle returned false")}");
                }
            }
        }

        private static int? GetCostFromState(BaggedObjectSaveData objData)
        {
            foreach (var entry in objData.ComponentStates)
            {
                foreach (var value in entry.Values)
                {
                    if (value.Key == "purchaseCost" && value.Type == "System.Int32")
                    {
                        if (int.TryParse(value.Value, out var costInt))
                        {
                            return costInt;
                        }
                    }
                }
            }
            return null;
        }

        private static object? DeserializeValue(string value, string typeStr)
        {
            if (string.IsNullOrEmpty(value)) return null;

            switch (typeStr)
            {
                case "System.Boolean":
                case "bool":
                    return bool.Parse(value);

                case "System.Int32":
                case "int":
                    return int.Parse(value);

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

            return value;
        }
    }
}
