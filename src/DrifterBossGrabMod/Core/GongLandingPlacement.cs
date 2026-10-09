#nullable enable
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Core
{
    [DefaultExecutionOrder(100)]
    public class GongLandingPlacement : MonoBehaviour
    {
        private Rigidbody? _gongBody;
        private HingeJoint? _hinge;
        private Transform? _frame;
        private Vector3 _anchorInFrame;
        private Quaternion _rotationInFrame;
        private bool _pendingPlacement;
        private Rigidbody? _flightAnchor;
        private VehicleSeat? _flightSeat;
        private bool _autoConfigureConnectedAnchor;
        private readonly Dictionary<Collider, bool> _colliderStates = new Dictionary<Collider, bool>();

        public bool AddedVisibilityAttributes { get; set; }

        public void RememberCollider(Collider collider)
        {
            if (!_colliderStates.ContainsKey(collider)) _colliderStates.Add(collider, collider.enabled);
        }

        private void RestoreColliders()
        {
            foreach (var entry in _colliderStates)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            _colliderStates.Clear();
        }

        public void Capture(TrialGongInteraction gong)
        {
            if (_pendingPlacement || gong.gongRigidBody == null) return;
            var hinge = gong.gongRigidBody.GetComponent<HingeJoint>();
            var frame = gong.gongRigidBody.transform.parent;
            if (hinge == null || hinge.connectedBody != null || frame == null) return;
            _gongBody = gong.gongRigidBody;
            _hinge = hinge;
            _frame = frame;
            _anchorInFrame = frame.InverseTransformPoint(hinge.connectedAnchor);
            _rotationInFrame = Quaternion.Inverse(frame.rotation) * _gongBody.rotation;
            _pendingPlacement = true;
        }

        public void BeginFlight(VehicleSeat seat)
        {
            if (!_pendingPlacement || _flightAnchor != null || _gongBody == null || _hinge == null || _frame == null) return;
            if (_hinge.connectedBody != null) return;
            GetComponent<ModelLocator>()?.LateUpdate();
            PlaceDiscOnFrame();
            var anchorObject = new GameObject("DBG_GongFlightAnchor");
            anchorObject.transform.SetPositionAndRotation(_frame.position, _frame.rotation);
            _flightAnchor = anchorObject.AddComponent<Rigidbody>();
            _flightAnchor.isKinematic = true;
            _flightAnchor.useGravity = false;
            _flightAnchor.detectCollisions = false;
            _flightSeat = seat;
            _autoConfigureConnectedAnchor = _hinge.autoConfigureConnectedAnchor;
            _hinge.autoConfigureConnectedAnchor = false;
            _hinge.connectedBody = _flightAnchor;
            _hinge.connectedAnchor = _flightAnchor.transform.InverseTransformPoint(_frame.TransformPoint(_anchorInFrame));
            Log.Debug($"[GongLandingPlacement] Attached {gameObject.name} to moving flight anchor; discKinematic={_gongBody.isKinematic}");
        }

        private void FixedUpdate()
        {
            if (_flightAnchor == null) return;
            if (_flightSeat == null || _flightSeat.currentPassengerTransform != transform || _frame == null || _hinge == null)
            {
                EndFlight();
                if (VehicleSeat.FindVehicleSeatWithPassenger(gameObject) == null) RestoreColliders();
                return;
            }
            GetComponent<ModelLocator>()?.LateUpdate();
            _hinge.connectedAnchor = Quaternion.Inverse(_frame.rotation) * (_frame.TransformPoint(_anchorInFrame) - _frame.position);
            _flightAnchor.MovePosition(_frame.position);
            _flightAnchor.MoveRotation(_frame.rotation);
        }

        private void EndFlight()
        {
            if (_flightAnchor == null) return;
            if (_hinge != null && _hinge.connectedBody == _flightAnchor)
            {
                _hinge.connectedBody = null;
                if (_frame != null) _hinge.connectedAnchor = _frame.TransformPoint(_anchorInFrame);
                _hinge.autoConfigureConnectedAnchor = _autoConfigureConnectedAnchor;
            }
            Destroy(_flightAnchor.gameObject);
            _flightAnchor = null;
            _flightSeat = null;
        }

        private void OnDestroy()
        {
            EndFlight();
        }

        private void PlaceDiscOnFrame()
        {
            if (_gongBody == null || _hinge == null || _frame == null) return;
            var rotation = _frame.rotation * _rotationInFrame;
            var localPosition = _anchorInFrame - _rotationInFrame * Vector3.Scale(_gongBody.transform.localScale, _hinge.anchor);
            var position = _frame.TransformPoint(localPosition);
            _gongBody.transform.SetPositionAndRotation(position, rotation);
            _gongBody.position = position;
            _gongBody.rotation = rotation;
        }

        public void CompleteLanding()
        {
            if (!_pendingPlacement || _gongBody == null || _hinge == null || _frame == null) return;
            if (VehicleSeat.FindVehicleSeatWithPassenger(gameObject) != null) return;
            GetComponent<ModelLocator>()?.LateUpdate();
            EndFlight();
            var anchor = _frame.TransformPoint(_anchorInFrame);
            PlaceDiscOnFrame();
            var autoConfigure = _hinge.autoConfigureConnectedAnchor;
            _hinge.autoConfigureConnectedAnchor = false;
            _hinge.connectedAnchor = anchor;
            _hinge.autoConfigureConnectedAnchor = autoConfigure;
            RestoreColliders();
            _pendingPlacement = false;
            Log.Debug($"[GongLandingPlacement] Aligned {gameObject.name} after final placement; kinematic={_gongBody.isKinematic}");
        }
    }
}
