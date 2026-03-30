#nullable enable
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using RoR2.HudOverlay;
using EntityStates.Drifter.Bag;
using UnityEngine;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    public static class SpecialObjectAttributesPatches
    {
        // Static registry of all active SpecialObjectAttributes GameObjects
        // Replaces expensive FindObjectsByType<GameObject>() scene scans
        public static readonly HashSet<GameObject> RegisteredObjects = new HashSet<GameObject>();

        // Cached reflection fields
        private static readonly FieldInfo _collisionToDisableField = ReflectionCache.SpecialObjectAttributes.CollisionToDisable;
        private static readonly FieldInfo _targetObjectField = ReflectionCache.BaggedObject.TargetObject;
        private static readonly FieldInfo _collidersToDisableField = ReflectionCache.SpecialObjectAttributes.CollidersToDisable;
        private static readonly FieldInfo _behavioursToDisableField = ReflectionCache.SpecialObjectAttributes.BehavioursToDisable;
        private static readonly FieldInfo _uiOverlayControllerField = ReflectionCache.BaggedObject.UIOverlayController;

        [HarmonyPatch(typeof(SpecialObjectAttributes), "OnEnable")]
        public class SpecialObjectAttributes_OnEnable_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance)
            {
                RegisteredObjects.Add(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(SpecialObjectAttributes), "OnDisable")]
        public class SpecialObjectAttributes_OnDisable_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance)
            {
                RegisteredObjects.Remove(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(SpecialObjectAttributes), "Start")]
        public class SpecialObjectAttributes_Start_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(SpecialObjectAttributes __instance)
            {
            }
        }

        [HarmonyPatch(typeof(BaggedObject), "OnEnter")]
        public class BaggedObject_OnEnter_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(BaggedObject __instance)
            {
                // Store colliders in SpecialObjectAttributes before BaggedObject.OnEnter disables them
                var targetObject = _targetObjectField?.GetValue(__instance) as GameObject;
                if (targetObject != null)
                {
                    // Ensure ModelStatePreserver is attached before the object is stashed and hidden
                    if (PluginConfig.Instance.EnableObjectPersistence.Value && targetObject.GetComponent<ModelStatePreserver>() == null)
                    {
                        targetObject.AddComponent<ModelStatePreserver>();
                    }

                    var specialAttrs = targetObject.GetComponent<SpecialObjectAttributes>();
                    if (specialAttrs != null)
                    {
                        var colliders = targetObject.GetComponentsInChildren<Collider>(true);
                        // Use reflection to access collidersToDisable
                        var collidersToDisable = _collidersToDisableField?.GetValue(specialAttrs) as System.Collections.Generic.List<Collider>;
                        if (collidersToDisable != null)
                        {
                            foreach (var collider in colliders)
                            {
                                if (!collidersToDisable.Contains(collider))
                                {
                                    collidersToDisable.Add(collider);
                                }
                            }
                        }
                        // Also store behaviors
                        var behavioursToDisable = _behavioursToDisableField?.GetValue(specialAttrs) as System.Collections.Generic.List<MonoBehaviour>;
                        if (behavioursToDisable != null)
                        {
                            var behaviors = targetObject.GetComponentsInChildren<MonoBehaviour>(true);
                            foreach (var behavior in behaviors)
                            {
                                // Only store behaviors that should be disabled
                                if (!behavioursToDisable.Contains(behavior))
                                {
                                    behavioursToDisable.Add(behavior);
                                }
                            }
                        }
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {
                // Remove original bag UI (Carousel enabled)
                if (PluginConfig.Instance.EnableCarouselHUD.Value)
                {
                    var uiOverlayController = _uiOverlayControllerField?.GetValue(__instance) as OverlayController;
                    if (uiOverlayController != null)
                    {
                        HudOverlayManager.RemoveOverlay(uiOverlayController);
                        _uiOverlayControllerField?.SetValue(__instance, null);
                    }
                }
            }
        }
    }
}
