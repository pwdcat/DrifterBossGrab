using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;

namespace DrifterBossGrabMod.Patches
{
    public static class BaggedObjectsOnlyDetection
    {
        // Cache Drifter body index for performance
        private static BodyIndex _drifterBodyIndex = BodyIndex.None;

        // Get all objects currently in Drifter bags
        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} GetCurrentlyBaggedObjects called - inspecting Drifter bags directly");
            }

            var currentlyBaggedObjects = new List<GameObject>();

            // Cache Drifter body index
            if (_drifterBodyIndex == BodyIndex.None)
            {
                _drifterBodyIndex = BodyCatalog.FindBodyIndex("DrifterBody");
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} DrifterBody index: {_drifterBodyIndex}");
                }
            }

            // Find all Drifter players and check their bags directly
            var drifterPlayers = PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master.GetBody()?.bodyIndex == _drifterBodyIndex).ToList();

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Found {drifterPlayers.Count} Drifter players");
                Log.Info($"{Constants.LogPrefix} Total PlayerCharacterMasterController instances: {PlayerCharacterMasterController.instances.Count}");
            }

            foreach (var drifter in drifterPlayers)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Inspecting Drifter: {drifter.master.name}, body: {drifter.master.GetBody()?.name ?? "null"}");
                }

                // Try to find bag controller on the master first
                var bagController = drifter.GetComponent<DrifterBagController>();

                // If not found on master, try to find it on the body
                if (bagController == null)
                {
                    var body = drifter.master.GetBody();
                    if (body != null)
                    {
                        bagController = body.GetComponent<DrifterBagController>();
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Found bag controller on body: {body.name}");
                        }
                    }
                }
                else
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Found bag controller on master");
                    }
                }

                if (bagController == null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} No bag controller found for this Drifter");
                    }
                    continue;
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Bag controller found, inspecting contents...");
                }

                try
                {
                    // First, let's log all available properties and fields to find the correct member name
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        var allProperties = bagController.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        var allFields = bagController.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                        Log.Info($"{Constants.LogPrefix} Available properties on {bagController.GetType().Name}:");
                        foreach (var prop in allProperties)
                        {
                            if (prop.PropertyType == typeof(GameObject) || prop.PropertyType == typeof(NetworkIdentity) || prop.Name.ToLower().Contains("passenger") || prop.Name.ToLower().Contains("network"))
                            {
                                try
                                {
                                    var value = prop.GetValue(bagController);
                                    Log.Info($"{Constants.LogPrefix}   Property: {prop.Name} ({prop.PropertyType.Name}) = {value?.ToString() ?? "null"}");
                                }
                                catch
                                {
                                    Log.Info($"{Constants.LogPrefix}   Property: {prop.Name} ({prop.PropertyType.Name}) = <access error>");
                                }
                            }
                        }

                        Log.Info($"{Constants.LogPrefix} Available fields on {bagController.GetType().Name}:");
                        foreach (var field in allFields)
                        {
                            if (field.FieldType == typeof(GameObject) || field.FieldType == typeof(NetworkIdentity) || field.Name.ToLower().Contains("passenger") || field.Name.ToLower().Contains("network"))
                            {
                                try
                                {
                                    var value = field.GetValue(bagController);
                                    Log.Info($"{Constants.LogPrefix}   Field: {field.Name} ({field.FieldType.Name}) = {value?.ToString() ?? "null"}");
                                }
                                catch
                                {
                                    Log.Info($"{Constants.LogPrefix}   Field: {field.Name} ({field.FieldType.Name}) = <access error>");
                                }
                            }
                        }
                    }

                    // Try the correct property/field names based on debug inspection
                    GameObject baggedObject = null;
                    string foundVia = "";

                    // Try NetworkbaggedObject property (capital B - this is the correct one!)
                    var networkBaggedObjectProperty = bagController.GetType().GetProperty("NetworkbaggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (networkBaggedObjectProperty != null)
                    {
                        baggedObject = (GameObject)networkBaggedObjectProperty.GetValue(bagController);
                        foundVia = "NetworkbaggedObject property";
                    }

                    // Try baggedObject field (lowercase b - backup)
                    if (baggedObject == null)
                    {
                        var baggedObjectField = bagController.GetType().GetField("baggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (baggedObjectField != null)
                        {
                            baggedObject = (GameObject)baggedObjectField.GetValue(bagController);
                            foundVia = "baggedObject field";
                        }
                    }

                    // Fallback: Try old property names for compatibility
                    if (baggedObject == null)
                    {
                        var networkPassengerProperty = bagController.GetType().GetProperty("Networkpassenger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (networkPassengerProperty != null)
                        {
                            baggedObject = (GameObject)networkPassengerProperty.GetValue(bagController);
                            foundVia = "Networkpassenger property (fallback)";
                        }
                    }

                    if (baggedObject == null)
                    {
                        var passengerProperty = bagController.GetType().GetProperty("passenger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (passengerProperty != null)
                        {
                            baggedObject = (GameObject)passengerProperty.GetValue(bagController);
                            foundVia = "passenger property (fallback)";
                        }
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        if (baggedObject != null)
                        {
                            Log.Info($"{Constants.LogPrefix} Found bagged object via {foundVia}: {baggedObject.name}");
                        }
                        else
                        {
                            Log.Info($"{Constants.LogPrefix} No bagged object found in bag controller");
                        }
                    }

                    if (baggedObject != null && IsValidForPersistence(baggedObject))
                    {
                        currentlyBaggedObjects.Add(baggedObject);
                    }
                }
                catch (System.Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Error checking bagged objects: {ex.Message}");
                    }
                }
            }

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Found {currentlyBaggedObjects.Count} currently bagged objects via direct bag inspection");
                for (int i = 0; i < currentlyBaggedObjects.Count; i++)
                {
                    Log.Info($"{Constants.LogPrefix} Bagged object {i}: {currentlyBaggedObjects[i]?.name ?? "null"}");
                }
            }

            return currentlyBaggedObjects;
        }

        // Check if object is valid for persistence (not thrown, not blacklisted, etc.)
        private static bool IsValidForPersistence(GameObject obj)
        {
            if (obj == null) return false;

            // Check if object is still actively bagged (not in projectile state)
            // TODO: Check for thrown state if applicable
            // var projectileController = obj.GetComponent<ThrownObjectProjectileController>();
            // if (projectileController != null) return false;

            // Check blacklist
            if (PluginConfig.IsBlacklisted(obj.name))
            {
                return false;
            }

            return true;
        }

        // Check if specific object is currently bagged
        public static bool IsObjectCurrentlyBagged(GameObject obj)
        {
            if (obj == null) return false;

            // Cache Drifter body index
            if (_drifterBodyIndex == BodyIndex.None)
            {
                _drifterBodyIndex = BodyCatalog.FindBodyIndex("DrifterBody");
            }

            // Check all Drifter players
            return PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master.GetBody()?.bodyIndex == _drifterBodyIndex)
                .Any(drifter =>
                {
                    // Try to find bag controller on the master first
                    var bagController = drifter.GetComponent<DrifterBagController>();

                    // If not found on master, try to find it on the body
                    if (bagController == null)
                    {
                        var body = drifter.master.GetBody();
                        if (body != null)
                        {
                            bagController = body.GetComponent<DrifterBagController>();
                        }
                    }

                    if (bagController == null) return false;

                    try
                    {
                        // Use the correct property/field names based on debug inspection
                        var networkBaggedObjectProperty = bagController.GetType().GetProperty("NetworkbaggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (networkBaggedObjectProperty != null)
                        {
                            var baggedObject = (GameObject)networkBaggedObjectProperty.GetValue(bagController);
                            return baggedObject == obj;
                        }

                        // Try baggedObject field
                        var baggedObjectField = bagController.GetType().GetField("baggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (baggedObjectField != null)
                        {
                            var baggedObject = (GameObject)baggedObjectField.GetValue(bagController);
                            return baggedObject == obj;
                        }

                        // Fallback to old names for compatibility
                        var networkPassengerProperty = bagController.GetType().GetProperty("Networkpassenger");
                        if (networkPassengerProperty != null)
                        {
                            var baggedObject = (GameObject)networkPassengerProperty.GetValue(bagController);
                            return baggedObject == obj;
                        }

                        var passengerProperty = bagController.GetType().GetProperty("passenger");
                        if (passengerProperty != null)
                        {
                            var baggedObject = (GameObject)passengerProperty.GetValue(bagController);
                            return baggedObject == obj;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Error checking if object is bagged: {ex.Message}");
                        }
                    }
                    return false;
                });
        }
    }
}