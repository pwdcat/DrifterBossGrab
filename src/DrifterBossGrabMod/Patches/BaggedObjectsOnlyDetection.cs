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
                    if (ReflectionCache.DrifterBagController.NetworkbaggedObject != null)
                    {
                        baggedObject = (GameObject)ReflectionCache.DrifterBagController.NetworkbaggedObject.GetValue(bagController);
                    }
                    if (baggedObject == null && ReflectionCache.DrifterBagController.BaggedObject != null)
                    {
                        baggedObject = (GameObject)ReflectionCache.DrifterBagController.BaggedObject.GetValue(bagController);
                    }
                    if (baggedObject == null && ReflectionCache.DrifterBagController.Networkpassenger != null)
                    {
                        baggedObject = (GameObject)ReflectionCache.DrifterBagController.Networkpassenger.GetValue(bagController);
                    }
                    if (baggedObject == null && ReflectionCache.DrifterBagController.Passenger != null)
                    {
                        baggedObject = (GameObject)ReflectionCache.DrifterBagController.Passenger.GetValue(bagController);
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
                    if (ReflectionCache.DrifterBagController.NetworkbaggedObject != null)
                    {
                        var baggedObject = (GameObject)ReflectionCache.DrifterBagController.NetworkbaggedObject.GetValue(bagController);
                        if (baggedObject == obj) return true;
                    }
                    if (ReflectionCache.DrifterBagController.BaggedObject != null)
                    {
                        var baggedObject = (GameObject)ReflectionCache.DrifterBagController.BaggedObject.GetValue(bagController);
                        if (baggedObject == obj) return true;
                    }
                    if (ReflectionCache.DrifterBagController.Networkpassenger != null)
                    {
                        var baggedObject = (GameObject)ReflectionCache.DrifterBagController.Networkpassenger.GetValue(bagController);
                        if (baggedObject == obj) return true;
                    }
                    if (ReflectionCache.DrifterBagController.Passenger != null)
                    {
                        var baggedObject = (GameObject)ReflectionCache.DrifterBagController.Passenger.GetValue(bagController);
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
