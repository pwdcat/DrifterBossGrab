#nullable enable
using System;
using System.Collections.Generic;
using DrifterBossGrabMod.ProperSave.Data;
using System.Linq;
using RoR2;

namespace DrifterBossGrabMod.ProperSave.Spawning.Resolvers
{
    public class ComponentTypeResolver : ISpawnCardResolver
    {
        public int Priority => 50;

        public bool CanResolve(BaggedObjectSaveData saveData)
        {
            return !string.IsNullOrEmpty(saveData.ObjectType);
        }

        public SpawnCard? Resolve(BaggedObjectSaveData saveData)
        {
            if (string.IsNullOrEmpty(saveData.ObjectType)) return null;

            var allSpawnCards = GetAllSpawnCards();

            SpawnCard? bestMatch = null;
            int bestMatchScore = -1;

            foreach (var card in allSpawnCards)
            {
                if (card == null || card.prefab == null) continue;

                int matchScore = CalculateMatchScore(card, saveData.ObjectType);
                if (matchScore > bestMatchScore)
                {
                    bestMatch = card;
                    bestMatchScore = matchScore;
                }
            }

            return bestMatch;
        }

        private int CalculateMatchScore(SpawnCard card, string objectType)
        {
            int score = 0;

            if (card.prefab.GetComponent<ChestBehavior>() != null)
            {
                if (objectType.Equals("Chest", StringComparison.OrdinalIgnoreCase)) score += 3;
            }

            if (card.prefab.GetComponent<ShopTerminalBehavior>() != null)
            {
                if (objectType.Equals("Duplicator", StringComparison.OrdinalIgnoreCase)) score += 3;
            }

            if (card.prefab.GetComponent<ShrineBehavior>() != null)
            {
                if (objectType.Equals("Shrine", StringComparison.OrdinalIgnoreCase)) score += 3;
            }

            if (card.prefab.GetComponent<PurchaseInteraction>() != null)
            {
                if (objectType.Equals("PurchaseInteraction", StringComparison.OrdinalIgnoreCase)) score += 2;
            }

            var prefabName = card.prefab.name.ToLower();
            var objectName = objectType.ToLower();

            if (prefabName.Contains(objectName))
            {
                score += 1;
            }

            return score;
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
