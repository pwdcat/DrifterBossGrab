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
        // Thread-safe tracking of objects currently in bags
        private static readonly HashSet<GameObject> _currentlyBaggedObjects = new HashSet<GameObject>();
        private static readonly object _lock = new object();
        // Maximum cache size to prevent memory bloat
        private const int MAX_TRACKED_OBJECTS = 50;
        // Track object when added to bag
        public static void TrackBaggedObject(GameObject obj)
        {
            if (obj == null) return;
            lock (_lock)
            {
                if (_currentlyBaggedObjects.Count >= MAX_TRACKED_OBJECTS)
                {
                    // Remove oldest object if at limit
                    var oldest = _currentlyBaggedObjects.FirstOrDefault();
                    if (oldest != null)
                    {
                        _currentlyBaggedObjects.Remove(oldest);
                    }
                }
                _currentlyBaggedObjects.Add(obj);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Debug($" Tracking bagged object: {obj.name} (total tracked: {_currentlyBaggedObjects.Count})");
                }
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    var health = obj.GetComponent<RoR2.HealthComponent>();
                    Log.Info($"[DEBUG] [TrackBaggedObject] {obj.name}: alive={health?.alive}");
                }
            }
        }
        // Stop tracking object when removed from bag or thrown
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
                        Log.Info($"[DEBUG] [UntrackBaggedObject] {obj.name}: alive={health?.alive}, isDestroying={isDestroying}");
                    }
                    // Remove from persistence when thrown
                    PersistenceManager.RemovePersistedObject(obj, isDestroying);
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Debug($" Untracked bagged object: {obj.name} (total tracked: {_currentlyBaggedObjects.Count})");
                    }
                }
            }
        }
        // Get all currently bagged objects
        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            lock (_lock)
            {
                // Filter out null or destroyed objects
                _currentlyBaggedObjects.RemoveWhere(obj => obj == null);
                return _currentlyBaggedObjects.ToList();
            }
        }
        // Check if object is currently bagged
        public static bool IsObjectCurrentlyBagged(GameObject obj)
        {
            if (obj == null) return false;
            lock (_lock)
            {
                return _currentlyBaggedObjects.Contains(obj);
            }
        }
        // Clear all tracked objects (on run end, etc.)
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
