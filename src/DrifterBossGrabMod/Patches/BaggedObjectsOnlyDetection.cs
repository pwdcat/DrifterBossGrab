using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
namespace DrifterBossGrabMod.Patches
{
    public static class BaggedObjectsOnlyDetection
    {
        private static BodyIndex _drifterBodyIndex = BodyIndex.None;

        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            var currentlyBaggedObjects = new List<GameObject>();
            if (_drifterBodyIndex == BodyIndex.None)
            {
                _drifterBodyIndex = BodyCatalog.FindBodyIndex("DrifterBody");
            }
            var drifterPlayers = PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master.GetBody()?.bodyIndex == _drifterBodyIndex && pcm.isLocalPlayer).ToList();
            foreach (var drifter in drifterPlayers)
            {
                var bagController = drifter.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    var body = drifter.master.GetBody();
                    if (body != null)
                    {
                        bagController = body.GetComponent<DrifterBagController>();
                    }
                }
                if (bagController == null)
                {
                    continue;
                }
                try
                {
                    GameObject? baggedObject = null;
                    var networkBaggedObjectProperty = bagController.GetType().GetProperty("NetworkbaggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (networkBaggedObjectProperty != null)
                    {
                        baggedObject = (GameObject)networkBaggedObjectProperty.GetValue(bagController);
                    }
                    if (baggedObject == null)
                    {
                        var baggedObjectField = bagController.GetType().GetField("baggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (baggedObjectField != null)
                        {
                            baggedObject = (GameObject)baggedObjectField.GetValue(bagController);
                        }
                    }
                    if (baggedObject == null)
                    {
                        var networkPassengerProperty = bagController.GetType().GetProperty("Networkpassenger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (networkPassengerProperty != null)
                        {
                            baggedObject = (GameObject)networkPassengerProperty.GetValue(bagController);
                        }
                    }
                    if (baggedObject == null)
                    {
                        var passengerProperty = bagController.GetType().GetProperty("passenger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (passengerProperty != null)
                        {
                            baggedObject = (GameObject)passengerProperty.GetValue(bagController);
                        }
                    }
                    if (baggedObject != null && IsValidForPersistence(baggedObject))
                    {
                        currentlyBaggedObjects.Add(baggedObject);
                    }
                }
                catch (System.Exception ex)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Error checking bagged objects: {ex.Message}");
                    }
                }
            }
        return currentlyBaggedObjects;
        }

        private static bool IsValidForPersistence(GameObject obj)
        {
            if (obj == null) return false;
            if (PluginConfig.IsBlacklisted(obj.name))
            {
                return false;
            }
            return true;
        }

        public static bool IsObjectCurrentlyBagged(GameObject obj)
        {
            if (obj == null) return false;
            if (_drifterBodyIndex == BodyIndex.None)
            {
                _drifterBodyIndex = BodyCatalog.FindBodyIndex("DrifterBody");
            }
            return PlayerCharacterMasterController.instances
                .Where(pcm => pcm.master.GetBody()?.bodyIndex == _drifterBodyIndex)
                .Any(drifter =>
                {
                    var bagController = drifter.GetComponent<DrifterBagController>();
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
                        var networkBaggedObjectProperty = bagController.GetType().GetProperty("NetworkbaggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (networkBaggedObjectProperty != null)
                        {
                            var baggedObject = (GameObject)networkBaggedObjectProperty.GetValue(bagController);
                            return baggedObject == obj;
                        }
                        var baggedObjectField = bagController.GetType().GetField("baggedObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (baggedObjectField != null)
                        {
                            var baggedObject = (GameObject)baggedObjectField.GetValue(bagController);
                            return baggedObject == obj;
                        }
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
                        if (PluginConfig.Instance.EnableDebugLogs.Value)
                        {
                            Log.Info($" Error checking if object is bagged: {ex.Message}");
                        }
                    }
                    return false;
                });
        }
    }
}
