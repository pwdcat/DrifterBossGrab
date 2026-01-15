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
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($" Tracking bagged object: {obj.name} (total tracked: {_currentlyBaggedObjects.Count})");
                }
            }
        }
        // Stop tracking object when removed from bag or thrown
        public static void UntrackBaggedObject(GameObject obj)
        {
            if (obj == null) return;
            lock (_lock)
            {
                if (_currentlyBaggedObjects.Remove(obj))
                {
                    // Remove from persistence when thrown
                    PersistenceManager.RemovePersistedObject(obj);
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($" Untracked bagged object: {obj.name} (total tracked: {_currentlyBaggedObjects.Count})");
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
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($" Cleared all tracked bagged objects");
                }
            }
        }
        // Cleanup null references periodically
        public static void CleanupNullReferences()
        {
            lock (_lock)
            {
                int beforeCount = _currentlyBaggedObjects.Count;
                _currentlyBaggedObjects.RemoveWhere(obj => obj == null);
                int removed = beforeCount - _currentlyBaggedObjects.Count;
                if (removed > 0 && PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($" Cleaned up {removed} null references from bagged objects tracker");
                }
            }
        }
        // Get count of currently tracked objects
        public static int GetTrackedObjectsCount()
        {
            lock (_lock)
            {
                return _currentlyBaggedObjects.Count;
            }
        }
    }
}