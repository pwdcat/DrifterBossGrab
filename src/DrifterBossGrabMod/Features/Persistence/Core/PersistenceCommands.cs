#nullable enable
using System;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod
{

    public interface IPersistenceCommand
    {
        void Execute();
    }

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

    public class ClearPersistedObjectsCommand : IPersistenceCommand
    {
        private GameObject[] _clearedObjects = null!;

        public void Execute()
        {
            _clearedObjects = PersistenceObjectManager.GetPersistedObjects();
            PersistenceObjectManager.ClearPersistedObjectsInternal();
        }
    }

    public class PersistenceCommandInvoker
    {
        public void ExecuteCommand(IPersistenceCommand command)
        {
            command.Execute();
        }
    }
}
