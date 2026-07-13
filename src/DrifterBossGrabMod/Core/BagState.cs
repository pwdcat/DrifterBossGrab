#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using DrifterBossGrabMod.Features;
using RoR2;

namespace DrifterBossGrabMod.Core
{

    public class BagState
    {

        public object BagLock { get; } = new object();

        public List<GameObject> BaggedObjects { get; set; } = new List<GameObject>();

        private readonly HashSet<int> _baggedObjectIds = new HashSet<int>();

        public ConcurrentDictionary<GameObject, VehicleSeat> AdditionalSeats { get; set; } = new ConcurrentDictionary<GameObject, VehicleSeat>();

        public GameObject? MainSeatObject { get; set; }

        public GameObject? IncomingObject { get; set; }

        public UncappedBagScaleComponent? UncappedBagScale { get; set; }

        public ConcurrentDictionary<GameObject, Dictionary<Collider, bool>> DisabledCollidersByObject { get; } = new ConcurrentDictionary<GameObject, Dictionary<Collider, bool>>();

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
