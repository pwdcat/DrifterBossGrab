#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RoR2;
namespace DrifterBossGrabMod
{
    public static class PersistenceObjectsTracker
    {

        private static readonly HashSet<GameObject> _currentlyBaggedObjects = new HashSet<GameObject>();
        private static readonly object _lock = new object();



        public static void SetBaggedObjectVisibility(GameObject obj, bool isVisible)
        {
            if (obj == null) return;
            try
            {
                var modelLocator = obj.GetComponent<RoR2.ModelLocator>();
                if (modelLocator != null && modelLocator.modelTransform != null)
                {
                    var charModel = modelLocator.modelTransform.GetComponent<RoR2.CharacterModel>();
                    if (charModel != null)
                    {
                        if (!isVisible)
                        {
                            if (charModel.invisibilityCount <= 0)
                            {
                                charModel.invisibilityCount++;
                            }
                        }
                        else
                        {
                            if (charModel.invisibilityCount > 0)
                            {
                                charModel.invisibilityCount--;
                            }
                        }
                    }
                }

                var specialAttrs = obj.GetComponent<RoR2.SpecialObjectAttributes>();
                if (specialAttrs != null)
                {
                    if (specialAttrs.renderersToDisable != null)
                    {
                        foreach (var renderer in specialAttrs.renderersToDisable)
                        {
                            if (renderer != null)
                            {
                                renderer.forceRenderingOff = !isVisible;
                            }
                        }
                    }

                    if (specialAttrs.lightsToDisable != null)
                    {
                        foreach (var light in specialAttrs.lightsToDisable)
                        {
                            if (light != null) light.enabled = isVisible;
                        }
                    }

                    if (specialAttrs.pickupDisplaysToDisable != null)
                    {
                        foreach (var display in specialAttrs.pickupDisplaysToDisable)
                        {
                            if (display != null) display.SetRenderersEnabled(isVisible);
                        }
                    }

                    if (specialAttrs.childObjectsToDisable != null)
                    {
                        foreach (var childObj in specialAttrs.childObjectsToDisable)
                        {
                            if (childObj != null) childObj.SetActive(isVisible);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SetBaggedObjectVisibility] Exception during visibility update for {obj.name}: {ex}");
            }
        }

        public static void TrackBaggedObject(GameObject obj)
        {
            if (obj == null) return;
            lock (_lock)
            {
                _currentlyBaggedObjects.Add(obj);
                SetBaggedObjectVisibility(obj, false);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($" Tracking bagged object: {obj.name} (total tracked: {_currentlyBaggedObjects.Count})");
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var health = obj.GetComponent<RoR2.HealthComponent>();
                    Log.Debug($"[DEBUG] [TrackBaggedObject] {obj.name}: alive={health?.alive}");
                }
            }
        }

        public static void UntrackBaggedObject(GameObject obj, bool isDestroying = false)
        {
            if (ReferenceEquals(obj, null)) return;
            lock (_lock)
            {
                if (_currentlyBaggedObjects.Remove(obj))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        var health = obj.GetComponent<RoR2.HealthComponent>();
                        Log.Debug($"[DEBUG] [UntrackBaggedObject] {obj.name}: alive={health?.alive}, isDestroying={isDestroying}");
                    }

                    PersistenceManager.RemovePersistedObject(obj, isDestroying);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($" Untracked bagged object: {obj.name} (total tracked: {_currentlyBaggedObjects.Count})");
                    }

                    if (!isDestroying)
                    {
                        SetBaggedObjectVisibility(obj, true);
                    }

                }
            }
        }

        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            lock (_lock)
            {

                _currentlyBaggedObjects.RemoveWhere(obj => obj == null);
                return _currentlyBaggedObjects.ToList();
            }
        }

        public static bool IsObjectCurrentlyBagged(GameObject obj)
        {
            if (obj == null) return false;
            lock (_lock)
            {
                return _currentlyBaggedObjects.Contains(obj);
            }
        }

        public static void ClearTrackedObjects()
        {
            lock (_lock)
            {
                _currentlyBaggedObjects.Clear();
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($" Cleared all tracked bagged objects");
                }
            }
        }
    }
}
