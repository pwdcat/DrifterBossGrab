#nullable enable
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using RoR2.HudOverlay;
using EntityStates.Drifter.Bag;
using UnityEngine;
using UnityEngine.Networking;
using DrifterBossGrabMod;

namespace DrifterBossGrabMod.Patches
{
    public static class SpecialObjectAttributesPatches
    {

        public static readonly HashSet<GameObject> RegisteredObjects = new HashSet<GameObject>();

        private static readonly FieldInfo _targetObjectField = ReflectionCache.BaggedObject.TargetObject;
        private static readonly FieldInfo _collidersToDisableField = ReflectionCache.SpecialObjectAttributes.CollidersToDisable;
        private static readonly FieldInfo _uiOverlayControllerField = ReflectionCache.BaggedObject.UIOverlayController;

        private static bool IsEssentialCollider(Collider collider, GameObject root)
        {
            if (collider.gameObject == root)
                return true;
            if (collider.GetComponent<HurtBox>() != null)
                return true;
            if (collider.GetComponentInParent<HurtBoxGroup>(true) != null)
                return true;
            if (collider.GetComponent<CharacterMotor>() != null)
                return true;
            return false;
        }

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
                var targetObject = _targetObjectField?.GetValue(__instance) as GameObject;
                if (targetObject == null) return;

                var specialAttrs = targetObject.GetComponent<SpecialObjectAttributes>();
                if (specialAttrs == null) return;

                var colliders = targetObject.GetComponentsInChildren<Collider>(true);
                var collidersToDisable = _collidersToDisableField?.GetValue(specialAttrs) as List<Collider>;
                if (collidersToDisable != null)
                {
                    foreach (var collider in colliders)
                    {
                        if (IsEssentialCollider(collider, targetObject))
                            continue;
                        if (!collidersToDisable.Contains(collider))
                        {
                            collidersToDisable.Add(collider);
                        }
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix(BaggedObject __instance)
            {

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
