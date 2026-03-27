#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Rewired;
using Rewired.Data;
using Rewired.Data.Mapping;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace DrifterBossGrabMod.Input
{
    // Handles initialization of custom Rewired actions for bag cycling
    // Hooks into RoR2's input system to register ScrollBagUp/Down actions
    // with both keyboard and controller support, and adds UI entries to Controls settings
    internal static class InputSetup
    {
        private static bool _initialized = false;

        // Cached reflection results for obfuscated methods
        private static MethodInfo? _actionElementMapApplyMethod;

        // Store the Hook so it doesn't get GC'd
        private static MonoMod.RuntimeDetour.Hook? _userDataHook;

        // Trampoline delegate type for the UserData init method
        private delegate void UserDataInitDelegate(UserData self);

        internal static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            Log.Info("[InputSetup] Initializing Rewired input actions...");

            // Register actions in RoR2's InputCatalog for display names
            AddActionsToInputCatalog();

            var harmony = new Harmony(Constants.PluginGuid + ".input");

            // Hook UserData initialization to inject our custom actions
            MethodInfo? userDataInit = FindUserDataInitMethod();
            if (userDataInit != null)
            {
                Log.Info($"[InputSetup] Found UserData init method: {userDataInit.Name} (DeclaringType: {userDataInit.DeclaringType?.FullName})");

                try
                {
                    _userDataHook = new MonoMod.RuntimeDetour.Hook(
                        userDataInit,
                        typeof(InputSetup).GetMethod(nameof(AddCustomActions), BindingFlags.NonPublic | BindingFlags.Static)!
                    );
                    Log.Info($"[InputSetup] Successfully hooked UserData init method via MonoMod.RuntimeDetour.Hook");
                }
                catch (Exception e)
                {
                    Log.Warning($"[InputSetup] Failed to hook UserData init via MonoMod.RuntimeDetour.Hook: {e}");
                }
            }
            else
            {
                Log.Warning("[InputSetup] Could not find UserData initialization method!");
                Log.Warning("[InputSetup] Dumping all non-public void instance methods on UserData:");
                foreach (var m in typeof(UserData).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
                    {
                        Log.Info($"[InputSetup]   Candidate: {m.Name}");
                    }
                }
            }

            // Discover the ActionElementMap apply method for use in AddActionMaps
            _actionElementMapApplyMethod = FindActionElementMapApplyMethod();
            if (_actionElementMapApplyMethod != null)
            {
                Log.Info($"[InputSetup] Found ActionElementMap apply method: {_actionElementMapApplyMethod.Name}");
            }
            else
            {
                Log.Warning("[InputSetup] Could not find ActionElementMap apply method. Profile bindings may not persist.");
            }

            // Hook UserProfile methods for default bindings
            var loadDefaultProfile = AccessTools.Method(typeof(UserProfile), nameof(UserProfile.LoadDefaultProfile));
            if (loadDefaultProfile != null)
                harmony.Patch(loadDefaultProfile, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnLoadDefaultProfile)));

            var fillDefaultJoystick = AccessTools.Method(typeof(UserProfile), "FillDefaultJoystickMaps");
            if (fillDefaultJoystick != null)
                harmony.Patch(fillDefaultJoystick, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnFillDefaultJoystickMaps)));

            var loadUserProfiles = AccessTools.Method(typeof(SaveSystem), "LoadUserProfiles");
            if (loadUserProfiles != null)
                harmony.Patch(loadUserProfiles, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnLoadUserProfiles)));

            // Hook SettingsPanelController.Start to add our keybind entries to the Controls UI
            var settingsStart = AccessTools.Method(typeof(SettingsPanelController), "Start");
            if (settingsStart != null)
                harmony.Patch(settingsStart, postfix: new HarmonyMethod(typeof(InputSetup), nameof(OnSettingsPanelStart)));

            // Hook Language.GetString to display our action names cleanly without needing a language file
            var getStringMethod = AccessTools.Method(typeof(Language), nameof(Language.GetString), new[] { typeof(string) });
            if (getStringMethod != null)
                harmony.Patch(getStringMethod, prefix: new HarmonyMethod(typeof(InputSetup), nameof(OnLanguageGetString)));

            Log.Info("[InputSetup] Rewired input actions registered successfully.");
        }

        private static bool OnLanguageGetString(string token, ref string __result)
        {
            if (token == RewiredActions.ScrollBagUp.DisplayToken)
            {
                __result = "Scroll Bag Up";
                return false; // Skip original method
            }
            if (token == RewiredActions.ScrollBagDown.DisplayToken)
            {
                __result = "Scroll Bag Down";
                return false; // Skip original method
            }
            return true; // Run original method
        }

        #region Obfuscated Method Discovery

        // Finds the UserData initialization method by IL analysis
        // Looks for a non-public instance void method that references the 'actions' field
        private static MethodInfo? FindUserDataInitMethod()
        {
            var actionsField = AccessTools.Field(typeof(UserData), "actions");
            if (actionsField == null)
            {
                Log.Warning("[InputSetup] Could not find UserData.actions field for method discovery.");
                return null;
            }

            Log.Info($"[InputSetup] Looking for methods referencing field: {actionsField.Name} ({actionsField.FieldType})");

            MethodInfo? bestCandidate = null;
            int bestLocalCount = 0;

            foreach (var method in typeof(UserData).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
                    continue;

                try
                {
                    // Check if this method's IL body references the 'actions' field
                    var instructions = PatchProcessor.GetCurrentInstructions(method);
                    bool referencesActions = instructions.Any(instr =>
                        (instr.opcode == System.Reflection.Emit.OpCodes.Ldfld || instr.opcode == System.Reflection.Emit.OpCodes.Stfld) &&
                        instr.operand is FieldInfo fi && fi == actionsField);

                    if (referencesActions)
                    {
                        var body = method.GetMethodBody();
                        int localCount = body?.LocalVariables.Count ?? 0;
                        Log.Info($"[InputSetup] Found candidate method: {method.Name} (locals={localCount}, IL instructions={instructions.Count})");
                        
                        // Pick the candidate with the most local variables (the init method is the biggest)
                        if (localCount > bestLocalCount)
                        {
                            bestCandidate = method;
                            bestLocalCount = localCount;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Info($"[InputSetup] Could not analyze method {method.Name}: {e.GetType().Name}");
                }
            }

            if (bestCandidate != null)
            {
                Log.Info($"[InputSetup] Selected best candidate: {bestCandidate.Name} (locals={bestLocalCount})");
            }

            return bestCandidate;
        }

        // Finds the ActionElementMap method that applies/copies the element map to a ControllerMap
        // Looks for a non-public instance method on ActionElementMap that takes a single ControllerMap parameter
        private static MethodInfo? FindActionElementMapApplyMethod()
        {
            foreach (var method in typeof(ActionElementMap).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method.ReturnType != typeof(void))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 1 && typeof(ControllerMap).IsAssignableFrom(parameters[0].ParameterType))
                {
                    return method;
                }
            }

            return null;
        }

        // Invokes the discovered ActionElementMap apply method, falling back gracefully if not found
        private static void ApplyElementMapToControllerMap(ActionElementMap elementMap, ControllerMap controllerMap)
        {
            if (_actionElementMapApplyMethod != null)
            {
                _actionElementMapApplyMethod.Invoke(elementMap, new object[] { controllerMap });
            }
        }

        #endregion

        #region InputCatalog & Action Registration

        private static void AddActionsToInputCatalog()
        {
            InputCatalog.actionToToken[RewiredActions.ScrollBagUp] = RewiredActions.ScrollBagUp.DisplayToken;
            InputCatalog.actionToToken[RewiredActions.ScrollBagDown] = RewiredActions.ScrollBagDown.DisplayToken;
        }

        // MonoMod.RuntimeDetour.Hook target. This wraps the original UserData init method.
        // Adds our custom actions to UserData.actions before calling the original
        private static void AddCustomActions(UserDataInitDelegate orig, UserData self)
        {
            Log.Info("[InputSetup] AddCustomActions hook fired!");

            if (self.actions != null)
            {
                // Register each action, auto-resolving ActionId collisions
                foreach (var action in new[] { RewiredActions.ScrollBagUp, RewiredActions.ScrollBagDown })
                {
                    // Check if already registered by name
                    var existingByName = self.actions.Find(a => a.name == action.Name);
                    if (existingByName != null)
                    {
                        Log.Info($"[InputSetup] Action '{action.Name}' already registered (id={existingByName.id}). Skipping.");
                        continue;
                    }

                    // Auto-resolve ActionId collisions by incrementing
                    int originalId = action.ActionId;
                    int attempts = 0;
                    while (self.actions.Exists(a => a.id == action.ActionId) && attempts < 50)
                    {
                        action.ActionId++;
                        attempts++;
                    }

                    if (attempts >= 50)
                    {
                        Log.Warning($"[InputSetup] Could not find free ActionId for '{action.Name}' after 50 attempts starting from {originalId}. Skipping.");
                        continue;
                    }

                    if (action.ActionId != originalId)
                    {
                        Log.Warning($"[InputSetup] ActionId collision for '{action.Name}': {originalId} was taken, resolved to {action.ActionId}.");
                    }

                    self.actions.Add(action);
                    Log.Info($"[InputSetup] Added action '{action.Name}' (id={action.ActionId}) to UserData.actions");
                }

                if (self.keyboardMaps != null && self.joystickMaps != null)
                {
                    FillActionMaps(RewiredActions.ScrollBagUp, self.keyboardMaps, self.joystickMaps);
                    FillActionMaps(RewiredActions.ScrollBagDown, self.keyboardMaps, self.joystickMaps);
                }
            }
            else
            {
                Log.Warning("[InputSetup] UserData.actions is null!");
            }

            // Call the original method - this processes the actions list and registers them with the Rewired engine
            orig(self);
            Log.Info("[InputSetup] Original UserData init completed.");
        }

        #endregion

        #region Profile Binding Hooks

        private static void OnLoadUserProfiles(SaveSystem __instance)
        {
            foreach (var (name, userProfile) in __instance.loadedUserProfiles)
            {
                try
                {
                    AddMissingBindings(userProfile);
                    userProfile.RequestEventualSave();
                }
                catch (Exception e)
                {
                    Log.Warning($"[InputSetup] Failed to add default bindings to '{name}' profile: {e}");
                }
            }
        }

        private static void OnLoadDefaultProfile()
        {
            try
            {
                AddMissingBindings(UserProfile.defaultProfile);
            }
            catch (Exception e)
            {
                Log.Warning($"[InputSetup] Failed to add default bindings to default profile: {e}");
            }
        }

        private static void OnFillDefaultJoystickMaps(UserProfile __instance)
        {
            AddMissingBindings(__instance);
        }

        #endregion

        #region Settings UI

        // Adds our keybind entries to the Controls settings panel (both M&KB and Gamepad)
        // Clones an existing binding button (Jump) and sets the action name
        private static void OnSettingsPanelStart(SettingsPanelController __instance)
        {
            if (__instance.name == "SettingsSubPanel, Controls (M&KB)" || __instance.name == "SettingsSubPanel, Controls (Gamepad)")
            {
                var jumpBindingTransform = __instance.transform.Find("Scroll View/Viewport/VerticalLayout/SettingsEntryButton, Binding (Jump)");
                if (jumpBindingTransform != null)
                {
                    AddActionBindingToSettings(RewiredActions.ScrollBagUp.Name, jumpBindingTransform);
                    AddActionBindingToSettings(RewiredActions.ScrollBagDown.Name, jumpBindingTransform);
                    Log.Info($"[InputSetup] Added keybind entries to {__instance.name}");
                }
                else
                {
                    Log.Warning($"[InputSetup] Could not find Jump binding transform in {__instance.name}");
                }
            }
        }

        private static void AddActionBindingToSettings(string actionName, Transform buttonToCopy)
        {
            var inputBindingObject = UnityEngine.Object.Instantiate(buttonToCopy, buttonToCopy.parent);
            var inputBindingControl = inputBindingObject.GetComponent<InputBindingControl>();
            inputBindingControl.actionName = actionName;
            // Re-run Awake to apply the new actionName
            inputBindingControl.Awake();
        }

        #endregion

        #region Action Map Helpers

        private static void AddMissingBindings(UserProfile userProfile)
        {
            AddActionMaps(RewiredActions.ScrollBagUp, userProfile);
            AddActionMaps(RewiredActions.ScrollBagDown, userProfile);
        }

        private static void FillActionMaps(RewiredActions action, List<ControllerMap_Editor> keyboardMaps, List<ControllerMap_Editor> joystickMaps)
        {
            foreach (var joystickMap in joystickMaps)
            {
                if (joystickMap.categoryId == 0 && joystickMap.actionElementMaps.All(map => map.actionId != action.ActionId))
                {
                    joystickMap.actionElementMaps.Add(action.DefaultJoystickMap);
                }
            }

            foreach (var keyboardMap in keyboardMaps)
            {
                if (keyboardMap.categoryId == 0 && keyboardMap.actionElementMaps.All(map => map.actionId != action.ActionId))
                {
                    if (action.DefaultKeyboardKey != KeyboardKeyCode.None)
                    {
                        keyboardMap.actionElementMaps.Add(action.DefaultKeyboardMap);
                    }
                }
            }
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

            if (action.DefaultKeyboardKey != KeyboardKeyCode.None)
            {
                if (userProfile.keyboardMap.AllMaps.All(m => m.actionId != action.ActionId))
                {
                    userProfile.keyboardMap.CreateElementMap(action.DefaultKeyboardMap.actionId, action.DefaultKeyboardMap.axisContribution, action.DefaultKeyboardMap.elementIdentifierId, action.DefaultJoystickMap.elementType, action.DefaultJoystickMap.axisRange, action.DefaultJoystickMap.invert, out var resultMap);
                    resultMap._keyboardKeyCode = action.DefaultKeyboardKey;
                    ApplyElementMapToControllerMap(action.DefaultKeyboardMap, userProfile.keyboardMap);
                }
            }
        }

        #endregion
    }
}
