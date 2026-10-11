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

        public int EmptySlotIndex { get; set; } = -1;

        public int GetEmptySlotIndex(int count) => EmptySlotIndex < 0 ? count : Mathf.Clamp(EmptySlotIndex, 0, count);

        public int InsertGrabbedObject(GameObject passenger)
        {
            int index = GetEmptySlotIndex(BaggedObjects.Count);
            BaggedObjects.Insert(index, passenger);
            if (EmptySlotIndex >= 0) EmptySlotIndex = index + 1;
            AddInstanceId(passenger.GetInstanceID());
            return index;
        }

        public int ToSequenceIndex(int objectIndex, int count)
            => objectIndex < GetEmptySlotIndex(count) ? objectIndex : objectIndex + 1;

        public int ToObjectIndex(int sequenceIndex, int count)
        {
            int empty = GetEmptySlotIndex(count);
            return sequenceIndex == empty ? -1 : sequenceIndex < empty ? sequenceIndex : sequenceIndex - 1;
        }

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
