#nullable enable
using System;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod
{
    // The command pattern is used to decouple the request for persistence from the underlying state management logic.
    public interface IPersistenceCommand
    {
        void Execute();
    }

    // Encapsulating addition logic allows for easy rollback if a stage transition is cancelled or fails.
    public class AddPersistedObjectCommand : IPersistenceCommand
    {
        private readonly GameObject _obj;
        private readonly string? _ownerPlayerId;

        public AddPersistedObjectCommand(GameObject obj, string? ownerPlayerId = null)
        {
            _obj = obj;
            _ownerPlayerId = ownerPlayerId;
        }

        public void Execute()
        {
            PersistenceObjectManager.AddPersistedObjectInternal(_obj, _ownerPlayerId);
        }
    }

    // Removing an object requires tracking its destruction state to ensure we don't attempt to "Undo" onto a null reference.
    public class RemovePersistedObjectCommand : IPersistenceCommand
    {
        private readonly GameObject _obj;
        private readonly bool _isDestroying;

        public RemovePersistedObjectCommand(GameObject obj, bool isDestroying = false)
        {
            _obj = obj;
            _isDestroying = isDestroying;
        }

        public void Execute()
        {
            PersistenceObjectManager.RemovePersistedObjectInternal(_obj, _isDestroying);
        }
    }

    // Clearing the entire registry is an expensive operation that is primarily used during run termination.
    public class ClearPersistedObjectsCommand : IPersistenceCommand
    {
        private GameObject[] _clearedObjects = null!;

        public void Execute()
        {
            _clearedObjects = PersistenceObjectManager.GetPersistedObjects();
            PersistenceObjectManager.ClearPersistedObjectsInternal();
        }
    }

    // The invoker maintains a history stack to support the "Undo" feature in the persistence UI.
    public class PersistenceCommandInvoker
    {
        public void ExecuteCommand(IPersistenceCommand command)
        {
            command.Execute();
        }
    }
}
