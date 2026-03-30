#nullable enable
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.ProperSave.Data;
using System.Linq;
using RoR2;
using UnityEngine.Networking;

namespace DrifterBossGrabMod.ProperSave.Spawning.Resolvers
{
    public class AssetIdResolver : ISpawnCardResolver
    {
        public int Priority => 100;

        public bool CanResolve(BaggedObjectSaveData saveData)
        {
            var assetId = ParseGuid(saveData.AssetId);
            return assetId.HasValue && assetId.Value != Guid.Empty;
        }

        public SpawnCard? Resolve(BaggedObjectSaveData saveData)
        {
            var assetId = ParseGuid(saveData.AssetId);
            if (!assetId.HasValue || assetId.Value == Guid.Empty) return null;

            try
            {
                var allSpawnCards = GetAllSpawnCards();

                foreach (var card in allSpawnCards)
                {
                    if (card == null || card.prefab == null) continue;

                    var networkIdentity = card.prefab.GetComponent<NetworkIdentity>();
                    if (networkIdentity != null)
                    {
                        var cardAssetId = new Guid(networkIdentity.assetId.ToString());
                        if (cardAssetId == assetId.Value)
                        {
                            return card;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Resolver] Error resolving spawn card: {ex.Message}");
            }

            return null;
        }

        private static Guid? ParseGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (Guid.TryParse(s, out var guid))
                return guid;
            return null;
        }

        private static SpawnCard[] GetAllSpawnCards()
        {
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

                return cards.Distinct().ToArray();
            }
            catch (Exception ex)
            {
                Log.Error($"[Resolver] Failed to get spawn cards: {ex.Message}");
                return Array.Empty<SpawnCard>();
            }
        }
    }
}
