#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace DrifterBossGrabMod
{

    public static class PersistenceManager
    {

        private static readonly HashSet<GameObject> _teleportersCurrentlyBagged = new HashSet<GameObject>();
        private static readonly object _teleporterLock = new object();

        public static void Initialize()
        {
            PersistenceObjectManager.Initialize();
        }

        public static void Cleanup()
        {
            PersistenceObjectManager.Cleanup();
        }

        public static void UpdateCachedConfig()
        {
            PersistenceObjectManager.UpdateCachedConfig();
        }

        public static void RemovePersistedObject(GameObject obj, bool isDestroying = false)
        {
            PersistenceObjectManager.RemovePersistedObject(obj, isDestroying);
        }

        public static void ClearPersistedObjects()
        {
            PersistenceObjectManager.ClearPersistedObjects();
        }

        public static void CaptureCurrentlyBaggedObjects()
        {
            PersistenceObjectManager.CaptureCurrentlyBaggedObjects();
        }

        public static List<GameObject> GetCurrentlyBaggedObjects()
        {
            return PersistenceObjectManager.GetCurrentlyBaggedObjects();
        }

        public static void MoveObjectsToPersistenceContainer()
        {
            PersistenceObjectManager.MoveObjectsToPersistenceContainer();
        }

        public static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            PersistenceSceneHandler.Instance.OnSceneChanged(oldScene, newScene);
        }

        public static void ScheduleAutoGrab(RoR2.CharacterMaster master)
        {
            PersistenceSceneHandler.Instance.ScheduleAutoGrab(master);
        }

        public static bool IsTeleporterCurrentlyBagged(GameObject obj)
        {
            lock (_teleporterLock)
            {
                return _teleportersCurrentlyBagged.Contains(obj);
            }
        }

        public static void MarkTeleporterAsBagged(GameObject obj)
        {
            if (obj == null) return;
            lock (_teleporterLock)
            {
                _teleportersCurrentlyBagged.Add(obj);
                if (PluginConfig.Instance.EnableDebugLogs.Value)
                {
                    Log.Info($" Marked {obj.name} as bagged, total bagged: {_teleportersCurrentlyBagged.Count}");
                }
            }
        }

        public static void UnmarkTeleporterAsBagged(GameObject obj)
        {
            if (obj == null) return;
            lock (_teleporterLock)
            {
                if (_teleportersCurrentlyBagged.Remove(obj))
                {
                    if (PluginConfig.Instance.EnableDebugLogs.Value)
                    {
                        Log.Info($" Unmarked {obj.name} as bagged, total remaining: {_teleportersCurrentlyBagged.Count}");
                    }
                }
            }
        }

    }
}
