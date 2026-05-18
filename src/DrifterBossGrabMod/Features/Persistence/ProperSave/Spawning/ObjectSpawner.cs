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
                Log.Debug($"[ObjectSpawn] Detected CharacterMaster {objData.PrefabName} (SaveType: {objData.SaveType}), spawning master...");

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
                        Log.Debug($"[ObjectSpawn] Successfully spawned master {spawnedMaster.name} via DirectorCore");
                        return spawnedMaster;
                    }
                }

                spawnedMasters?.Add(objData.ObjectInstanceId);
                return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
            }

            if (IsEnemyBody(objData.PrefabName))
            {
                var masterName = objData.MasterName ?? objData.PrefabName.Replace("Body", "Master");
                Log.Debug($"[ObjectSpawn] Enemy body detected, spawning via CharacterMaster '{masterName}'");

                if (spawnedMasters != null && spawnedMasters.Contains(objData.ObjectInstanceId))
                {
                    Log.Debug($"[ObjectSpawn] Skipping duplicate spawn for {objData.PrefabName} - instance ID {objData.ObjectInstanceId} already spawned");
                    return null;
                }

                var masterSpawnCard = SpawnCardRegistry.FindSpawnCardByExactName(masterName);
                if (masterSpawnCard != null)
                {

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

                        var characterMaster = spawnedMaster.GetComponent<CharacterMaster>();
                        if (characterMaster != null)
                        {
                            var savedTeamIndex = GetSavedTeamIndex(objData);
                            characterMaster.teamIndex = savedTeamIndex ?? TeamIndex.Monster;
                            Log.Debug($"[ObjectSpawn] Assigned team {characterMaster.teamIndex} to {spawnedMaster.name}");
                        }

                        if (spawnedMaster.transform.parent != null && spawnedMaster.transform.parent.name == "DBG_PersistenceContainer")
                        {
                            spawnedMaster.transform.SetParent(null, true);
                            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawnedMaster, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                        }

                        var spawnedBody = characterMaster?.SpawnBody(spawnedMaster.transform.position, spawnedMaster.transform.rotation);
                        if (spawnedBody != null)
                        {
                            Log.Debug($"[ObjectSpawn] Successfully spawned body {spawnedBody.name} via master {masterName}");
                            return spawnedBody.gameObject;
                        }
                    }
                    else
                    {
                        spawnedMasters?.Add(objData.ObjectInstanceId);
                        Log.Warning($"[ObjectSpawn] Failed to spawn via DirectorCore, falling back to PrefabSpawner");
                    }
                }

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

            var spawnCard = FindSpawnCardExact(objData);

            if (spawnCard == null)
            {

                if (IsEnemyBody(objData.PrefabName))
                {
                    var masterName = objData.MasterName ?? objData.PrefabName.Replace("Body", "Master");
                    Log.Debug($"[ObjectSpawn] Trying to find master spawn card '{masterName}' for enemy body '{objData.PrefabName}'");

                    var masterSpawnCard = SpawnCardRegistry.FindSpawnCardByExactName(masterName);
                    if (masterSpawnCard != null)
                    {
                        spawnCard = masterSpawnCard;
                        Log.Debug($"[ObjectSpawn] Found master spawn card for {masterName}");
                    }
                }

                if (spawnCard == null)
                {
                    Log.Warning($"[ObjectSpawn] Could not find SpawnCard for {objData.PrefabName}, trying PrefabSpawner fallback");

                    return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
                }
            }

            if (spawnCard.prefab == null)
            {
                Log.Error($"[ObjectSpawn] Spawn card '{spawnCard.name}' has no prefab!");
                return null;
            }

            var placementRule = CreatePlacementRuleForRestoration(objData, ownerPlayerId);

            var spawnRequest = new DirectorSpawnRequest(
                spawnCard,
                placementRule,
                RoR2Application.rng
            );

            var spawnedObject = DirectorCore.instance.TrySpawnObject(spawnRequest);

            if (spawnedObject != null)
            {

                var characterMaster = spawnedObject.GetComponent<CharacterMaster>();
                if (characterMaster != null)
                {

                    var savedTeamIndex = GetSavedTeamIndex(objData);
                    characterMaster.teamIndex = savedTeamIndex ?? TeamIndex.Monster;

                    Log.Debug($"[ObjectSpawn] Assigned team {characterMaster.teamIndex} to {spawnedObject.name}");

                    var spawnedBody = characterMaster.SpawnBody(spawnedObject.transform.position, spawnedObject.transform.rotation);

                    if (spawnedBody != null && objData.PrefabName.EndsWith("Body"))
                    {
                        Log.Debug($"[ObjectSpawn] Using spawned body {spawnedBody.name} instead of master {spawnedObject.name}");
                        spawnedObject = spawnedBody.gameObject;
                    }
                }

                if (spawnedObject.transform.parent != null && spawnedObject.transform.parent.name == "DBG_PersistenceContainer")
                {
                    spawnedObject.transform.SetParent(null, true);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawnedObject, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var components = spawnedObject.GetComponents<Component>();
                    Log.Debug($"[ObjectSpawn] Spawned object has {components.Length} components:");
                    foreach (var comp in components.Take(15))
                    {
                        Log.Debug($"  - {comp.GetType().Name}");
                    }

                    var soa = spawnedObject.GetComponent<SpecialObjectAttributes>();
                    var shrine = spawnedObject.GetComponent<HalcyoniteShrineInteractable>();
                    var charBody = spawnedObject.GetComponent<CharacterBody>();

                    if (soa == null) Log.Warning($"  - SpecialObjectAttributes: not FOUND");
                    else Log.Debug($"  - SpecialObjectAttributes: FOUND (durability={soa.durability}, locked={soa.locked})");

                    if (shrine == null) Log.Warning($"  - HalcyoniteShrineInteractable: not FOUND");
                    else Log.Debug($"  - HalcyoniteShrineInteractable: FOUND (interactions={shrine.interactions})");

                    if (charBody == null) Log.Warning($"  - CharacterBody: not FOUND");
                    else Log.Debug($"  - CharacterBody: FOUND");
                }

                Log.Debug($"[ObjectSpawn] Successfully spawned {spawnedObject.name}");
            }
            else
            {
                Log.Warning($"[ObjectSpawn] Failed to spawn {objData.PrefabName}, trying PrefabSpawner fallback");

                return PrefabSpawner.SpawnObjectFromPrefab(objData, ownerPlayerId);
            }

            return spawnedObject;
        }

        // ========================================================================================
        // PLACEMENT HELPERS
        // ========================================================================================
        private static DirectorPlacementRule CreatePlacementRuleForRestoration(BaggedObjectSaveData objData, string? ownerPlayerId)
        {

            var targetBody = FindOwnerBody(ownerPlayerId);

            if (targetBody != null)
            {

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

                var hostUser = RoR2.NetworkUser.readOnlyInstancesList.FirstOrDefault(nu => nu.isServer);
                if (hostUser != null && hostUser.master != null)
                {
                    return hostUser.master.GetBody();
                }
                return null;
            }

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

            if (assetId.HasValue && assetId.Value != Guid.Empty)
            {
                var card = SpawnCardRegistry.FindSpawnCardByAssetIdExact(assetId.Value);
                if (card != null) return card;
            }

            if (!prefabHash.Equals(default))
            {
                var card = SpawnCardRegistry.FindSpawnCardByPrefabHashExact(prefabHash);
                if (card != null) return card;
            }

            if (!string.IsNullOrEmpty(objData.PrefabName))
            {
                var card = SpawnCardRegistry.FindSpawnCardByExactName(objData.PrefabName);
                if (card != null) return card;
            }

            return null;
        }

    }
}
