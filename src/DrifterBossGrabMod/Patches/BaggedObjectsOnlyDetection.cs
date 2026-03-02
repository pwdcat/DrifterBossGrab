using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;
namespace DrifterBossGrabMod.Patches
{
    public static class BaggedObjectsOnlyDetection
    {
        private static BodyIndex _drifterBodyIndex = BodyIndex.None;
        
        // Reflection Cache
        private static readonly PropertyInfo _networkbaggedObjectProperty = typeof(DrifterBagController).GetProperty("NetworkbaggedObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo _baggedObjectField = typeof(DrifterBagController).GetField("baggedObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo _networkpassengerProperty = typeof(DrifterBagController).GetProperty("Networkpassenger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo _passengerProperty = typeof(DrifterBagController).GetProperty("passenger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
                    if (_networkbaggedObjectProperty != null)
                    {
                        baggedObject = (GameObject)_networkbaggedObjectProperty.GetValue(bagController);
                    }
                    if (baggedObject == null && _baggedObjectField != null)
                    {
                        baggedObject = (GameObject)_baggedObjectField.GetValue(bagController);
                    }
                    if (baggedObject == null && _networkpassengerProperty != null)
                    {
                        baggedObject = (GameObject)_networkpassengerProperty.GetValue(bagController);
                    }
                    if (baggedObject == null && _passengerProperty != null)
                    {
                        baggedObject = (GameObject)_passengerProperty.GetValue(bagController);
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
                        if (_networkbaggedObjectProperty != null)
                        {
                            var baggedObject = (GameObject)_networkbaggedObjectProperty.GetValue(bagController);
                            return baggedObject == obj;
                        }
                        if (_baggedObjectField != null)
                        {
                            var baggedObject = (GameObject)_baggedObjectField.GetValue(bagController);
                            return baggedObject == obj;
                        }
                        if (_networkpassengerProperty != null)
                        {
                            var baggedObject = (GameObject)_networkpassengerProperty.GetValue(bagController);
                            return baggedObject == obj;
                        }
                        if (_passengerProperty != null)
                        {
                            var baggedObject = (GameObject)_passengerProperty.GetValue(bagController);
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
