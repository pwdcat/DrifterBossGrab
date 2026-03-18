using Rewired;
using RoR2;

namespace BagCraftingMod.Input
{
    public class RewiredActions
    {
        public static RewiredActions ToggleCrafting { get; }

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
            ToggleCrafting = new RewiredActions
            {
                ActionId = 808,
                Name = "ToggleBagCrafting",
                DisplayToken = "BAG_CRAFTING_TOGGLE",
                DefaultKeyboardKey = KeyboardKeyCode.C,
                DefaultJoystickKey = 17
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
