#nullable enable
using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using DrifterBossGrabMod.UI;
using RoR2.UI;

namespace DrifterBossGrabMod.Patches
{
    // ========================================================================================
    // UI PATCHES
    // ========================================================================================
    public static class UIPatches
    {
        private static GameObject? _massCapacityUIControllerObject;

        public static void InitializeMassCapacityUI(CharacterBody drifterBody)
        {
            if (drifterBody == null || drifterBody.bodyIndex != BodyCatalog.FindBodyIndex("DrifterBody"))
            {
                return;
            }

            if (drifterBody.GetComponent<BaggedObjectInfoUIController>() == null)
            {
                drifterBody.gameObject.AddComponent<BaggedObjectInfoUIController>();
            }

            if (!drifterBody.hasAuthority)
            {
                return;
            }

            var existingController = drifterBody.GetComponent<MassCapacityUIController>();
            if (existingController == null)
            {
                drifterBody.gameObject.AddComponent<MassCapacityUIController>();
            }
            _massCapacityUIControllerObject = drifterBody.gameObject;
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
        public static class PassengerHealthBarPatches
        {
            [HarmonyPatch(typeof(RoR2.UI.HealthBar), "UpdateBarInfos")]
            [HarmonyPostfix]
            public static void ApplyEliteColor(RoR2.UI.HealthBar __instance)
            {
                try
                {
                    var card = __instance.GetComponentInParent<RoR2.UI.BaggedCardController>();
                    if (!card) return;
                    if (!__instance.source) return;

                    var values = __instance.source.GetHealthBarValues();
                    if (values.isVoid || (__instance.healthCritical && __instance.style.flashOnHealthCritical)) return;
                    if (!PassengerHealthBarColorResolver.TryGetEliteColor(__instance.source.body, out var color)) return;

                    __instance.barInfoCollection.instantHealthbarInfo.color = PassengerHealthBarColorResolver.BlendEliteColor(
                        __instance.barInfoCollection.instantHealthbarInfo.color, PassengerHealthBarColorResolver.Shade(color, 0.72f));
                    __instance.barInfoCollection.trailingOverHealthbarInfo.color = PassengerHealthBarColorResolver.BlendEliteColor(
                        __instance.barInfoCollection.trailingOverHealthbarInfo.color, color);
                    __instance.barInfoCollection.regainableHealthBarInfo.color =
                        PassengerHealthBarColorResolver.BlendEliteColor(__instance.barInfoCollection.regainableHealthBarInfo.color,
                            __instance.style.regainableHealthStyle.baseColor * color);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[PassengerHealthBarColor] Could not update passenger color: {ex.Message}");
                }
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

namespace DrifterBossGrabMod.UI
{
    internal static class PassengerHealthBarColorResolver
    {
        internal static bool TryGetEliteColor(CharacterBody? body, out Color color)
        {
            color = default;
            if (PluginConfig.Instance.EliteHealthBarColorIntensity.Value <= 0f) return false;
            if (!body || BuffCatalog.eliteBuffIndices == null) return false;

            foreach (var elite in EliteIdentity.Enumerate(body))
            {

                var locator = body!.GetComponent<ModelLocator>();
                var model = locator && locator.modelTransform
                    ? locator.modelTransform.GetComponent<CharacterModel>() : null;
                if (model && model!.myEliteIndex == elite!.eliteIndex && model.lightColorOverride.HasValue)
                {
                    color = model.lightColorOverride.Value;
                    color.a = 1f;
                    return true;
                }
                color = elite!.color;
                return true;
            }
            return false;
        }

        internal static Color Shade(Color color, float brightness)
        {
            return new Color(color.r * brightness, color.g * brightness, color.b * brightness, 1f);
        }

        internal static Color BlendEliteColor(Color normalColor, Color eliteColor)
        {
            float intensity = PluginConfig.Instance.EliteHealthBarColorIntensity.Value;
            if (float.IsNaN(intensity) || float.IsInfinity(intensity)) intensity = 1f;
            var color = Color.LerpUnclamped(normalColor, eliteColor, Mathf.Clamp(intensity, 0f, 2f));
            return new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), normalColor.a);
        }
    }
}
