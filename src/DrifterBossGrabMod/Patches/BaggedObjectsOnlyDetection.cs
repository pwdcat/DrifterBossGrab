#nullable enable
using System.Collections.Generic;
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
            foreach (var pcm in PlayerCharacterMasterController.instances)
            {
                if (!pcm.isLocalPlayer) continue;
                var body = pcm.master.GetBody();
                if (body == null || body.bodyIndex != _drifterBodyIndex) continue;

                var bagController = pcm.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    bagController = body.GetComponent<DrifterBagController>();
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
            foreach (var pcm in PlayerCharacterMasterController.instances)
            {
                var pcmBody = pcm.master.GetBody();
                if (pcmBody == null || pcmBody.bodyIndex != _drifterBodyIndex) continue;

                var bagController = pcm.GetComponent<DrifterBagController>();
                if (bagController == null)
                {
                    bagController = pcmBody.GetComponent<DrifterBagController>();
                }
                if (bagController == null) continue;
                try
                {
                    if (_networkbaggedObjectProperty != null)
                    {
                        var baggedObject = (GameObject)_networkbaggedObjectProperty.GetValue(bagController);
                        if (baggedObject == obj) return true;
                    }
                    if (_baggedObjectField != null)
                    {
                        var baggedObject = (GameObject)_baggedObjectField.GetValue(bagController);
                        if (baggedObject == obj) return true;
                    }
                    if (_networkpassengerProperty != null)
                    {
                        var baggedObject = (GameObject)_networkpassengerProperty.GetValue(bagController);
                        if (baggedObject == obj) return true;
                    }
                    if (_passengerProperty != null)
                    {
                        var baggedObject = (GameObject)_passengerProperty.GetValue(bagController);
                        if (baggedObject == obj) return true;
                    }
                }
                catch (System.Exception ex)
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Error checking if object is bagged: {ex.Message}");
                    }
                }
            }
            return false;
        }
    }
}
