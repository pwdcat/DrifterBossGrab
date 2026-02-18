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
        // Bagged objects list
        public List<GameObject> BaggedObjects { get; set; } = new List<GameObject>();

        // Additional seats mapping (Object -> Seat)
        public ConcurrentDictionary<GameObject, VehicleSeat> AdditionalSeats { get; set; } = new ConcurrentDictionary<GameObject, VehicleSeat>();

        // Main seat object
        public GameObject? MainSeatObject { get; set; }

        // Incoming object for predictive capacity
        public GameObject? IncomingObject { get; set; }

        // Uncapped bag scale component
        public UncappedBagScaleComponent? UncappedBagScale { get; set; }

        // Dirty flag to prevent redundant mass recalculations
        private bool _massDirty = true;

        // Gets whether the mass needs to be recalculated
        public bool IsMassDirty => _massDirty;

        // Marks mass as dirty, requiring recalculation
        public void MarkMassDirty()
        {
            _massDirty = true;
        }

        // Clears the mass dirty flag after recalculation
        internal void ClearMassDirty()
        {
            _massDirty = false;
        }
    }
}
