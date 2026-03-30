using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Rewired;
using Rewired.Data;
using Rewired.Data.Mapping;
using RoR2;
using RoR2.UI;
using UnityEngine;
using BagCraftingMod.Config;

namespace BagCraftingMod.Input
{
    internal static class InputSetup
    {
        private static bool _initialized = false;
        private static MethodInfo? _actionElementMapApplyMethod;
        private static MonoMod.RuntimeDetour.Hook? _userDataHook;

        private delegate void UserDataInitDelegate(UserData self);

        internal static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            Log.Info("[BagCrafting] Initializing Rewired input...");

            // Register actions in RoR2's InputCatalog
            AddActionsToInputCatalog();

            var harmony = new Harmony("pwdcat.BagCraftingMod.input");

            // Hook UserData initialization
            MethodInfo? userDataInit = FindUserDataInitMethod();
            if (userDataInit != null)
            {
                try
                {
                    _userDataHook = new MonoMod.RuntimeDetour.Hook(
                        userDataInit,
                        typeof(InputSetup).GetMethod(nameof(AddCustomActions), BindingFlags.NonPublic | BindingFlags.Static)!
                    );
                }
                catch (Exception e)
                {
                    Log.Warning($"[InputSetup] Failed to hook UserData init: {e}");
                }
            }

            _actionElementMapApplyMethod = FindActionElementMapApplyMethod();

            // Profile patches
            var loadDefaultProfile = AccessTools.Method(typeof(UserProfile), nameof(UserProfile.LoadDefaultProfile));
            if (loadDefaultProfile != null)
                harmony.Patch(loadDefaultProfile, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnLoadDefaultProfile)));

            var fillDefaultJoystick = AccessTools.Method(typeof(UserProfile), "FillDefaultJoystickMaps");
            if (fillDefaultJoystick != null)
                harmony.Patch(fillDefaultJoystick, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnFillDefaultJoystickMaps)));

            var loadUserProfiles = AccessTools.Method(typeof(SaveSystem), "LoadUserProfiles");
            if (loadUserProfiles != null)
                harmony.Patch(loadUserProfiles, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnLoadUserProfiles)));

            // Settings UI patch
            var settingsStart = AccessTools.Method(typeof(SettingsPanelController), "Start");
            if (settingsStart != null)
                harmony.Patch(settingsStart, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnSettingsPanelStart)));

            // Language patch
            var getStringMethod = AccessTools.Method(typeof(Language), nameof(Language.GetString), new[] { typeof(string) });
            if (getStringMethod != null)
                harmony.Patch(getStringMethod, prefix: new HarmonyMethod(typeof(InputSetup), nameof(OnLanguageGetString)));

            Log.Info("[BagCrafting] Input registration complete");
        }

        private static bool OnLanguageGetString(string token, ref string __result)
        {
            if (token == RewiredActions.ToggleCrafting.DisplayToken)
            {
                __result = "Toggle Bag Crafting Menu";
                return false;
            }
            return true;
        }

        private static MethodInfo? FindUserDataInitMethod()
        {
            var actionsField = AccessTools.Field(typeof(UserData), "actions");
            if (actionsField == null) return null;

            MethodInfo? bestCandidate = null;
            int bestLocalCount = 0;

            foreach (var method in typeof(UserData).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
                    continue;

                try
                {
                    var instructions = PatchProcessor.GetCurrentInstructions(method);
                    bool referencesActions = instructions.Any(instr =>
                        (instr.opcode == System.Reflection.Emit.OpCodes.Ldfld || instr.opcode == System.Reflection.Emit.OpCodes.Stfld) &&
                        instr.operand is FieldInfo fi && fi == actionsField);

                    if (referencesActions)
                    {
                        var body = method.GetMethodBody();
                        int localCount = body?.LocalVariables.Count ?? 0;
                        if (localCount > bestLocalCount)
                        {
                            bestCandidate = method;
                            bestLocalCount = localCount;
                        }
                    }
                }
                catch { }
            }
            return bestCandidate;
        }

        private static MethodInfo? FindActionElementMapApplyMethod()
        {
            foreach (var method in typeof(ActionElementMap).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method.ReturnType != typeof(void)) continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 1 && typeof(ControllerMap).IsAssignableFrom(parameters[0].ParameterType))
                    return method;
            }
            return null;
        }

        private static void ApplyElementMapToControllerMap(ActionElementMap elementMap, ControllerMap controllerMap)
        {
            if (_actionElementMapApplyMethod != null)
                _actionElementMapApplyMethod.Invoke(elementMap, new object[] { controllerMap });
        }

        private static void AddActionsToInputCatalog()
        {
            InputCatalog.actionToToken[RewiredActions.ToggleCrafting] = RewiredActions.ToggleCrafting.DisplayToken;
        }

        private static void AddCustomActions(UserDataInitDelegate orig, UserData self)
        {
            if (self.actions != null)
            {
                var action = RewiredActions.ToggleCrafting;
                if (!self.actions.Exists(a => a.name == action.Name))
                {
                    while (self.actions.Exists(a => a.id == action.ActionId))
                        action.ActionId++;

                    self.actions.Add(action);
                    if (BagCraftingMod.Config.PluginConfig.Instance.EnableDebugLogs.Value) Log.Info($"[BagCrafting] Added action '{action.Name}'");
                }

                if (self.keyboardMaps != null && self.joystickMaps != null)
                {
                    FillActionMaps(action, self.keyboardMaps, self.joystickMaps);
                }
            }
            orig(self);
        }

        private static void OnLoadUserProfiles(SaveSystem __instance)
        {
            foreach (var up in __instance.loadedUserProfiles.Values)
            {
                AddActionMaps(RewiredActions.ToggleCrafting, up);
                up.RequestEventualSave();
            }
        }

        private static void OnLoadDefaultProfile() => AddActionMaps(RewiredActions.ToggleCrafting, UserProfile.defaultProfile);
        private static void OnFillDefaultJoystickMaps(UserProfile __instance) => AddActionMaps(RewiredActions.ToggleCrafting, __instance);

        private static void OnSettingsPanelStart(SettingsPanelController __instance)
        {
            if (__instance.name.Contains("Controls (M&KB)") || __instance.name.Contains("Controls (Gamepad)"))
            {
                var jumpBinding = __instance.transform.Find("Scroll View/Viewport/VerticalLayout/SettingsEntryButton, Binding (Jump)");
                if (jumpBinding != null)
                {
                    var newBinding = UnityEngine.Object.Instantiate(jumpBinding, jumpBinding.parent);
                    var control = newBinding.GetComponent<InputBindingControl>();
                    control.actionName = RewiredActions.ToggleCrafting.Name;
                    control.Awake();
                }
            }
        }

        private static void FillActionMaps(RewiredActions action, List<ControllerMap_Editor> keyboardMaps, List<ControllerMap_Editor> joystickMaps)
        {
            foreach (var jm in joystickMaps.Where(m => m.categoryId == 0 && m.actionElementMaps.All(map => map.actionId != action.ActionId)))
                jm.actionElementMaps.Add(action.DefaultJoystickMap);

            foreach (var km in keyboardMaps.Where(m => m.categoryId == 0 && m.actionElementMaps.All(map => map.actionId != action.ActionId)))
                km.actionElementMaps.Add(action.DefaultKeyboardMap);
        }

        private static void AddActionMaps(RewiredActions action, UserProfile userProfile)
        {
            foreach (var (_, map) in userProfile.HardwareJoystickMaps2)
            {
                if (map.AllMaps.All(m => m.actionId != action.ActionId))
                {
                    map.CreateElementMap(action.DefaultJoystickMap.actionId, action.DefaultJoystickMap.axisContribution, action.DefaultJoystickMap.elementIdentifierId, action.DefaultJoystickMap.elementType, action.DefaultJoystickMap.axisRange, action.DefaultJoystickMap.invert);
                    ApplyElementMapToControllerMap(action.DefaultJoystickMap, map);
                }
            }

            if (userProfile.keyboardMap.AllMaps.All(m => m.actionId != action.ActionId))
            {
                userProfile.keyboardMap.CreateElementMap(action.DefaultKeyboardMap.actionId, action.DefaultKeyboardMap.axisContribution, action.DefaultKeyboardMap.elementIdentifierId, action.DefaultJoystickMap.elementType, action.DefaultJoystickMap.axisRange, action.DefaultJoystickMap.invert, out var resultMap);
                resultMap._keyboardKeyCode = action.DefaultKeyboardKey;
                ApplyElementMapToControllerMap(action.DefaultKeyboardMap, userProfile.keyboardMap);
            }
        }
    }
}
