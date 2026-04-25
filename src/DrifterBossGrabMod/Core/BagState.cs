#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using DrifterBossGrabMod.Features;
using RoR2;

namespace DrifterBossGrabMod.Core
{
    // Consolidated state for a single DrifterBagController - replaces scattered static dictionaries in BagPatches
    public class BagState
    {
        // Lock object for thread-safe access to BaggedObjects
        public object BagLock { get; } = new object();

        // Bagged objects list - must synchronize access via BagLock
        public List<GameObject> BaggedObjects { get; set; } = new List<GameObject>();

        // Fast InstanceID lookup for BaggedObjects (O(1) Contains)
        private readonly HashSet<int> _baggedObjectIds = new HashSet<int>();

        // Additional seats mapping (Object -> Seat)
        public ConcurrentDictionary<GameObject, VehicleSeat> AdditionalSeats { get; set; } = new ConcurrentDictionary<GameObject, VehicleSeat>();

        // Main seat object - must synchronize access via BagLock
        public GameObject? MainSeatObject { get; set; }

        // Incoming object for predictive capacity
        public GameObject? IncomingObject { get; set; }

        // Tracks user scroll intent for upcoming passenger assignments (-1 = no intent)
        public int IntendedSelectedIndex { get; set; } = -1;

        // Uncapped bag scale component
        public UncappedBagScaleComponent? UncappedBagScale { get; set; }

        // Tracks disabled collider states for each bagged object (for Ungrabbable enemies)
        public ConcurrentDictionary<GameObject, Dictionary<Collider, bool>> DisabledCollidersByObject { get; } = new ConcurrentDictionary<GameObject, Dictionary<Collider, bool>>();

        // Dirty flag to prevent redundant mass recalculations
        private bool _massDirty = true;

        // Gets whether the mass needs to be recalculated
        public bool IsMassDirty => _massDirty;

        // Marks mass as dirty, requiring recalculation
        public void MarkMassDirty()
        {
            _massDirty = true;
        }

        internal void ClearMassDirty()
        {
            _massDirty = false;
        }

        // O(1) check if an object's InstanceID is in the bagged set
        public bool ContainsInstanceId(int instanceId)
        {
            return _baggedObjectIds.Contains(instanceId);
        }

        public void AddInstanceId(int instanceId)
        {
            _baggedObjectIds.Add(instanceId);
        }

        public void RemoveInstanceId(int instanceId)
        {
            _baggedObjectIds.Remove(instanceId);
        }
    }
}
