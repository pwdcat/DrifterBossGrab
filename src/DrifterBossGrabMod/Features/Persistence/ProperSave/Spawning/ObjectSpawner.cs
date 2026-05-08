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
namespace DrifterBossGrabMod.ProperSave.Spawning
{
    // ========================================================================================
    // OBJECT SPAWNER
    // ========================================================================================

    public static class ObjectSpawner
    {
        public static void Initialize()
        {
            SpawnCardRegistry.Initialize();
        }

        // ========================================================================================
        // SPAWN LOGIC
        // ========================================================================================

        public static GameObject? SpawnObjectFromSaveData(BaggedObjectSaveData objData, string? ownerPlayerId = null, HashSet<int>? spawnedMasters = null)
        {
            if (DirectorCore.instance == null)
            {
                Log.Error("[ObjectSpawn] DirectorCore instance not available");
                return null;
            }

            if (objData.SaveType == "CharacterMaster" || IsCharacterMaster(objData.PrefabName))
            {
                Log.DebugIfEnabled("[ObjectSpawn] Detected CharacterMaster {0} (SaveType: {1}), spawning master...", objData.PrefabName, objData.SaveType);

                // Try to find master spawn card
                var masterName = objData.PrefabName;
                var masterSpawnCard = SpawnCardRegistry.FindSpawnCardByExactName(masterName);

                if (masterSpawnCard != null && masterSpawnCard.prefab != null)
                {
                    var masterPlacementRule = CreatePlacementRuleForRestoration(objData, ownerPlayerId);
                    var masterSpawnRequest = new DirectorSpawnRequest(
                        masterSpawnCard,
                        masterPlacementRule,
                        RoR2Application.rng
                    );

                    var spawnedMaster = DirectorCore.instance.TrySpawnObject(masterSpawnRequest);
                    if (spawnedMaster != null)
                    {
                        spawnedMasters?.Add(objData.ObjectInstanceId);
                        Log.DebugIfEnabled("[ObjectSpawn] Successfully spawned master {0} via DirectorCore", spawnedMaster.name);
                        return spawnedMaster;
                    }
                }

                // Fallback: PrefabSpawner
                spawnedMasters?.Add(objData.ObjectInstanceId);
                return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
            }

            // Check if we need to spawn via CharacterMaster for enemy bodies
            if (IsEnemyBody(objData.PrefabName))
            {
                var masterName = objData.MasterName ?? objData.PrefabName.Replace("Body", "Master");
                Log.DebugIfEnabled("[ObjectSpawn] Enemy body detected, spawning via CharacterMaster '{0}'", masterName);

                // Check if we've already spawned this instance to avoid duplicate spawns
                if (spawnedMasters != null && spawnedMasters.Contains(objData.ObjectInstanceId))
                {
                    Log.DebugIfEnabled("[ObjectSpawn] Skipping duplicate spawn for {0} - instance ID {1} already spawned", objData.PrefabName, objData.ObjectInstanceId);
                    return null;
                }

                // Try to find master spawn card first
                var masterSpawnCard = SpawnCardRegistry.FindSpawnCardByExactName(masterName);
                if (masterSpawnCard != null)
                {
                    // Spawn via DirectorCore with spawn card
                    if (masterSpawnCard.prefab == null)
                    {
                        Log.Error($"[ObjectSpawn] Spawn card '{masterSpawnCard.name}' has no prefab!");
                        spawnedMasters?.Add(objData.ObjectInstanceId);
                        return null;
                    }

                    var masterPlacementRule = CreatePlacementRuleForRestoration(objData, ownerPlayerId);
                    var masterSpawnRequest = new DirectorSpawnRequest(
                        masterSpawnCard,
                        masterPlacementRule,
                        RoR2Application.rng
                    );

                    var spawnedMaster = DirectorCore.instance.TrySpawnObject(masterSpawnRequest);
                    if (spawnedMaster != null)
                    {
                        spawnedMasters?.Add(objData.ObjectInstanceId);

                        // Get saved team index from save data, default to Monster team
                        var characterMaster = spawnedMaster.GetComponent<CharacterMaster>();
                        if (characterMaster != null)
                        {
                            var savedTeamIndex = GetSavedTeamIndex(objData);
                            characterMaster.teamIndex = savedTeamIndex ?? TeamIndex.Monster;
                            Log.DebugIfEnabled("[ObjectSpawn] Assigned team {0} to {1}", characterMaster.teamIndex, spawnedMaster.name);
                        }

                        // Reparent object from persistence container before processing
                        if (spawnedMaster.transform.parent != null && spawnedMaster.transform.parent.name == "DBG_PersistenceContainer")
                        {
                            spawnedMaster.transform.SetParent(null, true);
                            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawnedMaster, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                        }

                        var spawnedBody = characterMaster?.SpawnBody(spawnedMaster.transform.position, spawnedMaster.transform.rotation);
                        if (spawnedBody != null)
                        {
                            Log.DebugIfEnabled("[ObjectSpawn] Successfully spawned body {0} via master {1}", spawnedBody.name, masterName);
                            return spawnedBody.gameObject;
                        }
                    }
                    else
                    {
                        spawnedMasters?.Add(objData.ObjectInstanceId);
                        Log.DebugIfEnabled($"[ObjectSpawn] Failed to spawn via DirectorCore, falling back to PrefabSpawner");
                    }
                }

                // Fallback: Spawn directly via PrefabSpawner
                var masterObjData = new BaggedObjectSaveData
                {
                    PrefabName = masterName,
                    AssetId = objData.AssetId,
                    PrefabHash = objData.PrefabHash,
                    OwnerPlayerId = objData.OwnerPlayerId,
                    ComponentStates = objData.ComponentStates
                };

                spawnedMasters?.Add(objData.ObjectInstanceId);
                return PrefabSpawner.SpawnObjectFromPrefab(masterObjData, ownerPlayerId);
            }

            // Find SpawnCard by exact AssetId, PrefabHash, or exact name
            var spawnCard = FindSpawnCardExact(objData);

            if (spawnCard == null)
            {
                // If we're trying to spawn an enemy body, try to find the master spawn card instead
                if (IsEnemyBody(objData.PrefabName))
                {
                    var masterName = objData.MasterName ?? objData.PrefabName.Replace("Body", "Master");
                    Log.DebugIfEnabled("[ObjectSpawn] Trying to find master spawn card '{0}' for enemy body '{1}'", masterName, objData.PrefabName);

                    var masterSpawnCard = SpawnCardRegistry.FindSpawnCardByExactName(masterName);
                    if (masterSpawnCard != null)
                    {
                        spawnCard = masterSpawnCard;
                        Log.DebugIfEnabled("[ObjectSpawn] Found master spawn card for {0}", masterName);
                    }
                }

                if (spawnCard == null)
                {
                    Log.DebugIfEnabled($"[ObjectSpawn] Could not find SpawnCard for {objData.PrefabName}, trying PrefabSpawner fallback");

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

                    Log.DebugIfEnabled("[ObjectSpawn] Assigned team {0} to {1}", characterMaster.teamIndex, spawnedObject.name);

                    // Spawn the body for the master at the spawned object's position
                    var spawnedBody = characterMaster.SpawnBody(spawnedObject.transform.position, spawnedObject.transform.rotation);

                    // If we're expecting a body and successfully spawned one, use the body instead of master
                    if (spawnedBody != null && objData.PrefabName.EndsWith("Body"))
                    {
                        Log.DebugIfEnabled("[ObjectSpawn] Using spawned body {0} instead of master {1}", spawnedBody.name, spawnedObject.name);
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
                    Log.DebugIfEnabled("[ObjectSpawn] Spawned object has {0} components:", components.Length);
                    foreach (var comp in components.Take(15))
                    {
                        Log.DebugIfEnabled("  - {0}", comp.GetType().Name);
                    }

                    var soa = spawnedObject.GetComponent<SpecialObjectAttributes>();
                    var shrine = spawnedObject.GetComponent<HalcyoniteShrineInteractable>();
                    var charBody = spawnedObject.GetComponent<CharacterBody>();

                    if (soa == null) Log.DebugIfEnabled($"  - SpecialObjectAttributes: not found");
                    else Log.DebugIfEnabled("  - SpecialObjectAttributes: found (durability={0}, locked={1})", soa.durability, soa.locked);

                    if (shrine == null) Log.DebugIfEnabled($"  - HalcyoniteShrineInteractable: not found");
                    else Log.DebugIfEnabled("  - HalcyoniteShrineInteractable: found (interactions={0})", shrine.interactions);

                    if (charBody == null) Log.DebugIfEnabled($"  - CharacterBody: not found");
                    else Log.DebugIfEnabled("  - CharacterBody: found");
                }

                Log.DebugIfEnabled("[ObjectSpawn] Successfully spawned {0}", spawnedObject.name);
            }
            else
            {
                Log.DebugIfEnabled($"[ObjectSpawn] Failed to spawn {objData.PrefabName}, trying PrefabSpawner fallback");

                // Fallback to direct prefab instantiation
                return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
            }

            return spawnedObject;
        }

        // ========================================================================================
        // PLACEMENT HELPERS
        // ========================================================================================

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

        // ========================================================================================
        // UTILITY METHODS
        // ========================================================================================

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

            return prefabName.EndsWith("Master") || prefabName.Contains("Master(Clone)");
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
            var assetId = SerializationHelpers.ParseGuid(objData.AssetId);
            var prefabHash = SerializationHelpers.ParsePrefabHash(objData.PrefabHash);

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

    }
}

