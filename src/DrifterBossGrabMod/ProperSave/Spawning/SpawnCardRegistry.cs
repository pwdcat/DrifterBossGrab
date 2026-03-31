#nullable enable
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.ProperSave.Data;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;

namespace DrifterBossGrabMod.ProperSave.Spawning
{
    public static class SpawnCardRegistry
    {
        private static readonly Dictionary<Guid, SpawnCard> _spawnCardByAssetId = new();
        private static readonly Dictionary<NetworkHash128, SpawnCard> _spawnCardByPrefabHash = new();
        private static readonly Dictionary<string, SpawnCard> _spawnCardByExactName = new();
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;

            Run.onRunStartGlobal += OnRunStart;
            OnRunStart(null!);

            _initialized = true;
            Log.Info("[SpawnCardRegistry] Initialized");
        }

        private static void OnRunStart(Run run)
        {
            RebuildRegistry();
        }

        public static void RebuildRegistry()
        {
            _spawnCardByAssetId.Clear();
            _spawnCardByPrefabHash.Clear();
            _spawnCardByExactName.Clear();

            var allSpawnCards = GetAllSpawnCards();

            foreach (var card in allSpawnCards)
            {
                if (card == null || card.prefab == null) continue;

                _spawnCardByExactName[card.name] = card;

                var networkIdentity = card.prefab.GetComponent<NetworkIdentity>();
                if (networkIdentity != null)
                {
                    var assetId = new Guid(networkIdentity.assetId.ToString());
                    _spawnCardByAssetId[assetId] = card;
                    _spawnCardByPrefabHash[networkIdentity.assetId] = card;
                }
            }

            Log.Info($"[SpawnCardRegistry] Indexed {_spawnCardByAssetId.Count} spawn cards by AssetId");
        }

        private static SpawnCard[] GetAllSpawnCards()
        {
            var cards = new List<SpawnCard>();

            try
            {
                // Priority 1: Resources.FindObjectsOfTypeAll<SpawnCard>() - catches everything loaded in memory
                var resourceCards = Resources.FindObjectsOfTypeAll<SpawnCard>();
                if (resourceCards != null && resourceCards.Length > 0)
                {
                    cards.AddRange(resourceCards);
                    Log.Info($"[SpawnCardRegistry] Found {resourceCards.Length} spawn cards via Resources.FindObjectsOfTypeAll");
                }

                // Priority 2: DirectorCardCategorySelection pools for monsters/interactables
                var stageInfo = ClassicStageInfo.instance;
                if (stageInfo != null)
                {
                    if (stageInfo.monsterDccsPool != null)
                    {
                        var monsterCards = stageInfo.monsterDccsPool
                            .GenerateWeightedSelection().choices
                            .Where(x => x.value != null)
                            .Select(x => x.value)
                            .Where(x => x.categories != null)
                            .SelectMany(x => x.categories)
                            .Where(x => x.cards != null)
                            .SelectMany(x => x.cards)
                            .Select(x => x.spawnCard)
                            .Where(x => x != null);

                        cards.AddRange(monsterCards);
                    }

                    if (stageInfo.interactableDccsPool != null)
                    {
                        var interactableCards = stageInfo.interactableDccsPool
                            .GenerateWeightedSelection().choices
                            .Where(x => x.value != null)
                            .Select(x => x.value)
                            .Where(x => x.categories != null)
                            .SelectMany(x => x.categories)
                            .Where(x => x.cards != null)
                            .SelectMany(x => x.cards)
                            .Select(x => x.spawnCard)
                            .Where(x => x != null);

                        cards.AddRange(interactableCards);
                    }
                }

                // Priority 3: SceneDirector selections (existing fallback)
                var sceneDirector = UnityEngine.Object.FindFirstObjectByType<SceneDirector>();
                if (sceneDirector != null)
                {
                    var sceneCards = sceneDirector.GenerateInteractableCardSelection()
                        .choices
                        .Where(x => x.value != null && x.value.spawnCard != null)
                        .Select(x => x.value.spawnCard)
                        .Where(x => x != null);

                    cards.AddRange(sceneCards);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SpawnCardRegistry] Failed to get spawn cards: {ex.Message}");
            }

            return cards.Distinct().ToArray();
        }

        public static SpawnCard? FindSpawnCardByAssetIdExact(Guid assetId)
        {
            return _spawnCardByAssetId.TryGetValue(assetId, out var card) ? card : null;
        }

        public static SpawnCard? FindSpawnCardByPrefabHashExact(NetworkHash128 prefabHash)
        {
            return _spawnCardByPrefabHash.TryGetValue(prefabHash, out var card) ? card : null;
        }

        public static SpawnCard? FindSpawnCardByExactName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _spawnCardByExactName.TryGetValue(name, out var card) ? card : null;
        }

        public static void Cleanup()
        {
            Run.onRunStartGlobal -= OnRunStart;
            _spawnCardByAssetId.Clear();
            _spawnCardByPrefabHash.Clear();
            _spawnCardByExactName.Clear();
            _initialized = false;
        }
    }
}
