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
                        var networkIdentity = g.GetComponent<NetworkIdentity>();
                        if (networkIdentity == null) return false;

                        var prefabAssetId = new Guid(networkIdentity.assetId.ToString());
                        return prefabAssetId == assetId.Value;
                    });

                if (prefab != null)
                {
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

            RestoreObjectState(spawnedObject, objData);

            // Spawn on network
            if (NetworkServer.active)
            {
                NetworkServer.Spawn(spawnedObject);
            }
            else
            {
                Log.Warning("[PrefabSpawner] NetworkServer is not active, skipping NetworkServer.Spawn");
            }

            // Move to active scene if needed
            if (spawnedObject.scene.name != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spawnedObject, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }

            Log.Info($"[PrefabSpawner] Successfully spawned prefab: {spawnedObject.name}");
            return spawnedObject;
        }

        private static void RestoreObjectState(GameObject spawnedObject, BaggedObjectSaveData objData)
        {
            if (objData == null || objData.ComponentStates == null) return;

            var allPlugins = ProperSaveIntegration.GetSerializerPlugins();

            foreach (var entry in objData.ComponentStates)
            {
                var plugin = allPlugins.FirstOrDefault(p => p.PluginName == entry.PluginName);

                if (plugin != null && plugin.CanHandle(spawnedObject))
                {
                    var state = new System.Collections.Generic.Dictionary<string, object>();

                    foreach (var value in entry.Values)
                    {
                        var deserializedValue = DeserializeValue(value.Value, value.Type);
                        if (deserializedValue != null)
                        {
                            state[value.Key] = deserializedValue;
                        }
                    }

                    plugin.RestoreState(spawnedObject, state);
                }
            }
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
                Log.Warning($"[PrefabSpawner] Failed to deserialize value '{value}' of type '{typeStr}': {ex.Message}");
            }

            return value;
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
