#nullable enable
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace DrifterBossGrabMod.Core
{
    public static class EnvironmentCarryState
    {
        public static void Prepare(GameObject target)
        {
            if (target == null) return;
            var gong = target.GetComponent<TrialGongInteraction>();
            var teleporter = target.GetComponent<TeleporterInteraction>();
            var accessNode = target.GetComponent<AccessCodesNodeController>();
            if (gong == null && teleporter == null && accessNode == null) return;

            if (gong == null && teleporter != null)
            {
                foreach (var locator in target.GetComponentsInChildren<ModelLocator>(true))
                {
                    if (locator.modelTransform != null && !locator.modelTransform.IsChildOf(target.transform))
                    {
                        var modelParent = locator.modelBaseTransform != null && locator.modelBaseTransform.IsChildOf(target.transform)
                            ? locator.modelBaseTransform
                            : locator.transform;
                        locator.modelTransform.SetParent(modelParent, true);
                        locator.dontDetatchFromParent = true;
                        locator.autoUpdateModelTransform = false;
                    }
                }
            }

            var attributes = target.GetComponent<SpecialObjectAttributes>();
            if (gong != null)
            {
                var placement = target.GetComponent<GongLandingPlacement>() ?? target.AddComponent<GongLandingPlacement>();
                if (attributes == null)
                {
                    attributes = target.AddComponent<SpecialObjectAttributes>();
                    attributes.maxDurability = 8;
                    attributes.durability = attributes.maxDurability;
                    attributes.collisionToDisable = new List<GameObject>();
                    attributes.behavioursToDisable = new List<MonoBehaviour>();
                    attributes.childObjectsToDisable = new List<GameObject>();
                    attributes.pickupDisplaysToDisable = new List<PickupDisplay>();
                    attributes.objectsToDetach = new List<GameObject>();
                    attributes.childSpecialObjectAttributes = new List<SpecialObjectAttributes>();
                    attributes.soundEventsToStop = new List<AkEvent>();
                    attributes.soundEventsToPlay = new List<AkEvent>();
                    attributes.breakoutStateMachineName = "";
                    placement.AddedVisibilityAttributes = true;
                }
                attributes.massOverride = Constants.Limits.RitualGongMass;
                placement.Capture(gong);
            }

            if (attributes == null) return;
            attributes.renderersToDisable ??= new List<Renderer>();
            attributes.lightsToDisable ??= new List<Light>();
            var collidersToDisable = gong != null
                ? ReflectionCache.SpecialObjectAttributes.CollidersToDisable?.GetValue(attributes) as List<Collider>
                : null;
            var gongPlacement = gong != null ? target.GetComponent<GongLandingPlacement>() : null;
            var models = new HashSet<Transform>();
            var pendingModels = new Queue<Transform>();
            pendingModels.Enqueue(target.transform);
            while (pendingModels.Count > 0)
            {
                var model = pendingModels.Dequeue();
                if (!models.Add(model)) continue;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    if (!attributes.renderersToDisable.Contains(renderer)) attributes.renderersToDisable.Add(renderer);
                foreach (var light in model.GetComponentsInChildren<Light>(true))
                    if (!attributes.lightsToDisable.Contains(light)) attributes.lightsToDisable.Add(light);
                if (collidersToDisable != null)
                {
                    foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider.isTrigger || collider.GetComponent<HurtBox>() != null) continue;
                        gongPlacement?.RememberCollider(collider);
                        if (!collidersToDisable.Contains(collider)) collidersToDisable.Add(collider);
                    }
                }
                foreach (var locator in model.GetComponentsInChildren<ModelLocator>(true))
                {
                    if (locator.modelTransform != null) pendingModels.Enqueue(locator.modelTransform);
                }
            }
        }
    }
}
