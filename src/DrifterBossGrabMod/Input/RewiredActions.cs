#nullable enable
using Rewired;
using RoR2;

namespace DrifterBossGrabMod.Input
{
    // Defines custom Rewired actions for bag cycling (ScrollBagUp / ScrollBagDown)
    // These actions work with both keyboard and controller through RoR2's native input system
    // Requires publicized assemblies for access to internal Rewired types
    public class RewiredActions
    {
        public static RewiredActions ScrollBagUp { get; }
        public static RewiredActions ScrollBagDown { get; }

        public int ActionId { get; internal set; }
        public string Name { get; private set; } = "";
        public string DisplayToken { get; private set; } = "";
        public KeyboardKeyCode DefaultKeyboardKey { get; private set; }
        public int DefaultJoystickKey { get; private set; }


        private ActionElementMap? _defaultJoystickMap;
        public ActionElementMap DefaultJoystickMap => _defaultJoystickMap ??= new ActionElementMap(ActionId, ControllerElementType.Button, DefaultJoystickKey, Pole.Positive, AxisRange.Positive);

        private ActionElementMap? _defaultKeyboardMap;
        public ActionElementMap DefaultKeyboardMap => _defaultKeyboardMap ??= new ActionElementMap(ActionId, ControllerElementType.Button, (int)DefaultKeyboardKey - 21) { _keyboardKeyCode = DefaultKeyboardKey };

        static RewiredActions()
        {
            ScrollBagUp = new RewiredActions
            {
                ActionId = 420,
                Name = "ScrollBagUp",
                DisplayToken = "DRIFTERBOSSGRAB_SCROLL_BAG_UP",
                DefaultKeyboardKey = KeyboardKeyCode.None,
                DefaultJoystickKey = 16  // D-Pad Up (Gamepad Template element 16)
            };

            ScrollBagDown = new RewiredActions
            {
                ActionId = 690,
                Name = "ScrollBagDown",
                DisplayToken = "DRIFTERBOSSGRAB_SCROLL_BAG_DOWN",
                DefaultKeyboardKey = KeyboardKeyCode.None,
                DefaultJoystickKey = 18  // D-Pad Down (Gamepad Template element 18)
            };
        }

        private InputAction? _inputAction;

        public static implicit operator InputCatalog.ActionAxisPair(RewiredActions action)
        {
            return new InputCatalog.ActionAxisPair(action.Name, AxisRange.Full);
        }

        public static implicit operator InputAction(RewiredActions action)
        {
            return action._inputAction ??= new InputAction
            {
                id = action.ActionId,
                name = action.Name,
                type = InputActionType.Button,
                descriptiveName = action.Name,
                behaviorId = 0,
                userAssignable = true,
                categoryId = 0
            };
        }
    }
}
