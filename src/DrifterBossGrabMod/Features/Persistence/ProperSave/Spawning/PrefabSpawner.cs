#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DrifterBossGrabMod;
using DrifterBossGrabMod.ProperSave.Data;
using DrifterBossGrabMod.ProperSave.Core;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.ProperSave.Spawning
{
    public static class PrefabSpawner
    {

        public static GameObject? SpawnObjectFromPrefab(BaggedObjectSaveData objData, string? ownerPlayerId = null)
        {
            if (objData == null) return null;

            var assetId = SerializationHelpers.ParseGuid(objData.AssetId);
            if (!assetId.HasValue || assetId == Guid.Empty)
            {
                Log.Warning("[PrefabSpawner] No AssetId provided, cannot spawn from prefab");
                return null;
            }

            // Look up of prefab from NetworkManager.spawnPrefabs using saved AssetId
            GameObject? prefab = null;
            if (NetworkManager.singleton != null && NetworkManager.singleton.spawnPrefabs != null)
            {
                prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(p =>
                {
                    if (p == null) return false;
                    var networkIdentity = p.GetComponent<NetworkIdentity>();
                    if (networkIdentity == null) return false;

                    var prefabAssetId = new Guid(networkIdentity.assetId.ToString());
                    return prefabAssetId == assetId.Value;
                });
            }

            // Fallback: Try to find prefab by name in all loaded GameObjects
            if (prefab == null && !string.IsNullOrEmpty(objData.PrefabName))
            {
                prefab = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g =>
                    {
                        if (g == null) return false;

                        if (!RoR2.Util.IsPrefab(g)) return false;

                        var networkIdentity = g.GetComponent<NetworkIdentity>();
                        if (networkIdentity == null) return false;

                        var prefabAssetId = new Guid(networkIdentity.assetId.ToString());
                        return prefabAssetId == assetId.Value;
                    });

                if (prefab != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                        Log.Info($"[PrefabSpawner] Found prefab by AssetId in Resources: {prefab.name}");
                }
            }

            if (prefab == null)
            {
                Log.Warning($"[PrefabSpawner] Could not find prefab with AssetId {assetId}");
                return null;
            }

            // Instantiate directly with Object.Instantiate + NetworkServer.Spawn
            GameObject spawnedObject;
            try
            {
                spawnedObject = UnityEngine.Object.Instantiate(prefab);
                if (spawnedObject == null)
                {
                    Log.Error("[PrefabSpawner] Object.Instantiate returned null");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[PrefabSpawner] Failed to instantiate prefab: {ex.Message}");
                return null;
            }

            // Position objects bunched up near the player (like persistence system does)
            PositionObjectNearPlayer(spawnedObject, ownerPlayerId);

            // Reset rotation to identity for bunched up spawning
            spawnedObject.transform.rotation = Quaternion.identity;

            // Handle CharacterMaster - spawn master on network FIRST, then spawn body
            var characterMaster = spawnedObject.GetComponent<CharacterMaster>();
            CharacterBody? spawnedBody = null;

            if (characterMaster != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[PrefabSpawner] Found CharacterMaster {spawnedObject.name}");

                // Get saved team index from save data, default to Monster team
                var savedTeamIndex = GetSavedTeamIndex(objData);
                characterMaster.teamIndex = savedTeamIndex ?? TeamIndex.Monster;

                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[PrefabSpawner] Assigned team {characterMaster.teamIndex} to {spawnedObject.name}");

                // Find the body prefab for this master
                var bodyPrefab = BodyCatalog.FindBodyPrefab(objData.PrefabName);
                if (bodyPrefab != null)
                {
                    characterMaster.bodyPrefab = bodyPrefab;
                }
            }

            // Spawn the master on network BEFORE spawning body
            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[PrefabSpawner] Spawning master on network...");
            if (NetworkServer.active)
            {
                NetworkServer.Spawn(spawnedObject);
            }
            else
            {
                Log.Warning("[PrefabSpawner] NetworkServer is not active, skipping NetworkServer.Spawn");
            }

            // Now spawn the body (after master is network-spawned)
            if (characterMaster != null)
            {
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                    Log.Info($"[PrefabSpawner] Spawning body from master...");
                spawnedBody = characterMaster.SpawnBody(spawnedObject.transform.position, spawnedObject.transform.rotation);

                if (spawnedBody != null)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($"[PrefabSpawner] Spawned body {spawnedBody.name} from master {spawnedObject.name}");
                        Log.Info($"[PrefabSpawner] Body {spawnedBody.name} master: {spawnedBody.master?.name ?? "null"}");
                        Log.Info($"[PrefabSpawner] Master {spawnedObject.name} body: {characterMaster.GetBody()?.name ?? "null"}");
                    }

                    spawnedObject = spawnedBody.gameObject;
                }
                else
                {
                    Log.Error($"[PrefabSpawner] Failed to spawn body from master {spawnedObject.name}");
                }
            }

            // Move to active scene if needed
            if (spawnedObject.scene.name != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawnedObject, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }

            if (PluginConfig.Instance.EnableDebugLogs.Value)
                Log.Info($"[PrefabSpawner] Successfully spawned prefab: {spawnedObject.name}");
            return spawnedObject;
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



        private static void PositionObjectNearPlayer(GameObject obj, string? ownerPlayerId)
        {
            var targetBody = FindOwnerBody(ownerPlayerId);

            if (targetBody != null)
            {
                // Position very close to player (0.5 units in front and up, bunched up)
                var playerPos = targetBody.transform.position;
                var playerForward = targetBody.transform.forward;
                var targetPos = playerPos + playerForward * Constants.Limits.PositionOffset + Vector3.up * Constants.Limits.PositionOffset;
                obj.transform.position = targetPos;
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
                    obj.transform.position = fallbackPos;
                }
                else
                {
                    // Last resort: position at origin with offset
                    obj.transform.position = new Vector3(0, Constants.Limits.OriginYOffset, 0);
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
    }
}
