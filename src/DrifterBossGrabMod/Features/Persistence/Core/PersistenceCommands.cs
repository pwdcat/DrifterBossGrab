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
        void Undo();
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

        public void Undo()
        {
            PersistenceObjectManager.RemovePersistedObjectInternal(_obj);
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

        public void Undo()
        {
            PersistenceObjectManager.AddPersistedObjectInternal(_obj);
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

        public void Undo()
        {
            foreach (var obj in _clearedObjects)
            {
                if (obj != null)
                {
                    PersistenceObjectManager.AddPersistedObjectInternal(obj);
                }
            }
        }
    }

    // The invoker maintains a history stack to support the "Undo" feature in the persistence UI.
    public class PersistenceCommandInvoker
    {
        private readonly System.Collections.Generic.Stack<IPersistenceCommand> _commandHistory = new System.Collections.Generic.Stack<IPersistenceCommand>();

        public void ExecuteCommand(IPersistenceCommand command)
        {
            command.Execute();
            _commandHistory.Push(command);
        }

        public void UndoLastCommand()
        {
            if (_commandHistory.Count > 0)
            {
                var command = _commandHistory.Pop();
                command.Undo();
            }
        }

        public void ClearHistory()
        {
            _commandHistory.Clear();
        }

        public int GetHistoryCount()
        {
            return _commandHistory.Count;
        }
    }
}
