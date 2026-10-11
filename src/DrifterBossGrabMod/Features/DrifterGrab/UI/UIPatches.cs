#nullable enable
using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.UI;

namespace DrifterBossGrabMod.Patches
{

    public static class UIPatches
    {
        private static GameObject? _massCapacityUIControllerObject;

        public static void InitializeMassCapacityUI(CharacterBody drifterBody)
        {
            if (drifterBody == null || drifterBody.bodyIndex != BodyCatalog.FindBodyIndex("DrifterBody"))
            {
                return;
            }

            Log.Debug($"[UIPatches] InitializeMassCapacityUI() called for {drifterBody.name}");

            if (drifterBody.GetComponent<BaggedObjectInfoUIController>() == null)
            {
                drifterBody.gameObject.AddComponent<BaggedObjectInfoUIController>();
                Log.Debug($"[UIPatches] Added BaggedObjectInfoUIController to {drifterBody.name}");
            }

            if (!drifterBody.hasAuthority)
            {
                return;
            }

            var existingController = drifterBody.GetComponent<MassCapacityUIController>();
            if (existingController == null)
            {
                drifterBody.gameObject.AddComponent<MassCapacityUIController>();
                _massCapacityUIControllerObject = drifterBody.gameObject;
                Log.Debug($"[UIPatches] Added MassCapacityUIController to {drifterBody.name}");
            }
            else
            {
                _massCapacityUIControllerObject = drifterBody.gameObject;
                Log.Debug($"[UIPatches] MassCapacityUIController already exists on {drifterBody.name}");
            }
        }

        public static void CleanupMassCapacityUI()
        {

            Log.Debug("[UIPatches] MassCapacityUIController cleanup not needed (component on DrifterBody)");
        }

        public static void UpdateMassCapacityUIOnCapacityChange(DrifterBagController controller)
        {
            if (_massCapacityUIControllerObject == null) return;

            var massCapacityUIController = _massCapacityUIControllerObject.GetComponent<MassCapacityUIController>();
            if (massCapacityUIController != null)
            {
                massCapacityUIController.UpdateCapacityUI();
            }
        }

        [HarmonyPatch]
        public static class BaggedCardCompatibilityPatches
        {
            [HarmonyPatch(typeof(RoR2.UI.BaggedCardController), nameof(RoR2.UI.BaggedCardController.InfoOverride))]
            [HarmonyPostfix]
            public static void UpdateObjectPresentation(RoR2.UI.BaggedCardController __instance)
            {
                if (__instance.GetComponentInParent<BaggedObjectCarousel>() == null) return;

                var attributes = __instance.sourcePassengerAttributes;
                var target = attributes != null ? attributes.gameObject : null;
                if (__instance.portraitIconImage != null)
                {
                    var icon = __instance.portraitIconImage;
                    bool isTeleporterIcon = __instance.sourceBody == null && target != null &&
                        target.GetComponent<TeleporterInteraction>() != null && icon.texture != null &&
                        icon.texture.name == "texTeleporterIconOutlined";
                    icon.uvRect = isTeleporterIcon ? new Rect(0.075f, 0.075f, 0.85f, 0.85f) : new Rect(0f, 0f, 1f, 1f);
                }

                if (__instance.sourceBody != null || target == null || __instance.nameLabel == null) return;

                var rawName = target.name.Replace("(Clone)", "").Trim();
                var rawNameWithoutNumber = System.Text.RegularExpressions.Regex.Replace(rawName, @"\s*\(\d+\)$", "");
                if (attributes != null && !string.IsNullOrWhiteSpace(attributes.bestName) &&
                    attributes.bestName != rawName && attributes.bestName != rawNameWithoutNumber)
                    return;

                var displayNameProvider = target.GetComponent<GenericDisplayNameProvider>();
                if (displayNameProvider != null && !string.IsNullOrWhiteSpace(displayNameProvider.displayToken) &&
                    !Language.IsTokenInvalid(displayNameProvider.displayToken))
                {
                    __instance.nameLabel.text = Language.GetString(displayNameProvider.displayToken);
                    return;
                }

                var inspectInfo = target.GetComponent<GenericInspectInfoProvider>()?.GetInfo();
                if (inspectInfo != null && !string.IsNullOrWhiteSpace(inspectInfo.TitleToken) &&
                    !Language.IsTokenInvalid(inspectInfo.TitleToken))
                {
                    __instance.nameLabel.text = Language.GetString(inspectInfo.TitleToken);
                }
            }

            [HarmonyPatch(typeof(RoR2.UI.AllyCardController), nameof(RoR2.UI.AllyCardController.Awake))]
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void StripIncompatibleAllyComponents(RoR2.UI.AllyCardController __instance)
            {
                if (__instance is RoR2.UI.BaggedCardController)
                {
                    foreach (var mb in __instance.GetComponents<MonoBehaviour>())
                    {
                        if (mb != null && mb.GetType().Name == "AllyCardData")
                        {
                            UnityEngine.Object.DestroyImmediate(mb);
                        }
                    }

                    var extraSlot = __instance.transform.Find("WhatchaGotThere EquipmentSlot");
                    if (extraSlot != null)
                    {
                        UnityEngine.Object.DestroyImmediate(extraSlot.gameObject);
                    }
                }
            }
        }
    }
}
