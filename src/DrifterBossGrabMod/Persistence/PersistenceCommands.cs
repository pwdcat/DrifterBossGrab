using System;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod
{
    // Command interface for persistence operations
    public interface IPersistenceCommand
    {
        void Execute();
        void Undo();
    }

    // Concrete command for adding a persisted object
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

    // Concrete command for removing a persisted object
    public class RemovePersistedObjectCommand : IPersistenceCommand
    {
        private readonly GameObject _obj;

        public RemovePersistedObjectCommand(GameObject obj)
        {
            _obj = obj;
        }

        public void Execute()
        {
            PersistenceObjectManager.RemovePersistedObjectInternal(_obj);
        }

        public void Undo()
        {
            PersistenceObjectManager.AddPersistedObjectInternal(_obj);
        }
    }

    // Concrete command for clearing all persisted objects
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

    // Command invoker for managing persistence commands
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