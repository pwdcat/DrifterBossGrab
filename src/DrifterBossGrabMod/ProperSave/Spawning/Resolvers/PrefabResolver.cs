#nullable enable
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.ProperSave.Data;
using System.Linq;
using UnityEngine;
using RoR2;

namespace DrifterBossGrabMod.ProperSave.Spawning.Resolvers
{
    public class PrefabResolver : ISpawnCardResolver
    {
        public int Priority => 80;

        private readonly Dictionary<GameObject, SpawnCard> _prefabToCardCache = new();
        private bool _cacheBuilt = false;

        public bool CanResolve(BaggedObjectSaveData saveData)
        {
            return !string.IsNullOrEmpty(saveData.SpawnCardPath) || !string.IsNullOrEmpty(saveData.ObjectName);
        }

        public SpawnCard? Resolve(BaggedObjectSaveData saveData)
        {
            if (!_cacheBuilt)
            {
                BuildCache();
            }

            var cleanObjectName = saveData.ObjectName.Replace("(Clone)", "").Trim();
            var spawnCardName = !string.IsNullOrEmpty(saveData.SpawnCardPath)
                ? saveData.SpawnCardPath
                : cleanObjectName;

            if (string.IsNullOrEmpty(spawnCardName))
            {
                return null;
            }

            if (_prefabToCardCache.Count > 0)
            {
                var foundByName = _prefabToCardCache.Values
                    .FirstOrDefault(x => x.name == spawnCardName);

                if (foundByName != null)
                {
                    return foundByName;
                }
            }

            var allSpawnCards = _prefabToCardCache.Values.ToList();

            foreach (var card in allSpawnCards)
            {
                if (card == null || card.prefab == null) continue;

                var prefabName = card.prefab.name.Replace("(Clone)", "").Trim();
                if (prefabName == spawnCardName || card.name == spawnCardName)
                {
                    return card;
                }

                if (prefabName.Contains(cleanObjectName, StringComparison.OrdinalIgnoreCase) ||
                    card.name.Contains(cleanObjectName, StringComparison.OrdinalIgnoreCase))
                {
                    return card;
                }
            }

            return null;
        }

        private void BuildCache()
        {
            _prefabToCardCache.Clear();

            try
            {
                var stageInfo = ClassicStageInfo.instance;
                var sceneDirector = UnityEngine.Object.FindFirstObjectByType<SceneDirector>();
                var cards = new List<SpawnCard>();

                if (stageInfo != null && stageInfo.interactableDccsPool != null)
                {
                    var stageCards = stageInfo.interactableDccsPool
                        .GenerateWeightedSelection().choices
                        .Where(x => x.value != null)
                        .Select(x => x.value)
                        .Where(x => x.categories != null)
                        .SelectMany(x => x.categories)
                        .Where(x => x.cards != null)
                        .SelectMany(x => x.cards)
                        .Select(x => x.spawnCard)
                        .Where(x => x != null);

                    cards.AddRange(stageCards);
                }

                if (sceneDirector != null)
                {
                    var sceneCards = sceneDirector.GenerateInteractableCardSelection()
                        .choices
                        .Where(x => x.value != null && x.value.spawnCard != null)
                        .Select(x => x.value.spawnCard)
                        .Where(x => x != null);

                    cards.AddRange(sceneCards);
                }

                foreach (var card in cards.Distinct())
                {
                    if (card != null && card.prefab != null)
                    {
                        _prefabToCardCache[card.prefab] = card;
                    }
                }

                _cacheBuilt = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Resolver] Failed to build cache: {ex.Message}");
            }
        }

        public void ClearCache()
        {
            _prefabToCardCache.Clear();
            _cacheBuilt = false;
        }
    }
}
