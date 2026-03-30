#nullable enable
using System.Collections.Generic;
using UnityEngine;
using DrifterBossGrabMod.ProperSave.Data;
using RoR2;
using DrifterBossGrabMod.ProperSave.Spawning.Resolvers;

namespace DrifterBossGrabMod.ProperSave.Spawning
{
    public interface ISpawnCardResolver
    {
        int Priority { get; }
        bool CanResolve(BaggedObjectSaveData saveData);
        SpawnCard? Resolve(BaggedObjectSaveData saveData);
    }

    public static class SpawnCardResolverRegistry
    {
        private static readonly List<ISpawnCardResolver> _resolvers = new();
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;

            _resolvers.Add(new AssetIdResolver());
            _resolvers.Add(new PrefabResolver());
            _resolvers.Add(new ComponentTypeResolver());

            _resolvers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _initialized = true;

            Log.Info($"[SpawnCardResolve] SpawnCardResolverRegistry initialized with {_resolvers.Count} resolvers");
        }

        public static SpawnCard? FindSpawnCard(BaggedObjectSaveData saveData)
        {
            if (!_initialized)
            {
                Initialize();
            }

            foreach (var resolver in _resolvers)
            {
                if (resolver.CanResolve(saveData))
                {
                    var spawnCard = resolver.Resolve(saveData);
                    if (spawnCard != null)
                    {
                        return spawnCard;
                    }
                }
            }

            return null;
        }

        public static void RegisterResolver(ISpawnCardResolver resolver)
        {
            if (resolver == null) return;

            _resolvers.Add(resolver);
            _resolvers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            Log.Info($"[SpawnCardResolve] Registered spawn card resolver: {resolver.GetType().Name}");
        }
    }
}
