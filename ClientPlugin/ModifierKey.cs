using VRage.Input;

namespace ClientPlugin
{
    // Keys to hold down for replacing the paint. Ctrl and Shift select the mode, so they can't be one.
    public enum ModifierKey
    {
        Alt,
        LeftAlt,
        RightAlt,
        CapsLock,
        Tab,
        Tilde,
        Quote,
        Backslash,
    }

    public static class ModifierKeyExtensions
    {
        public static bool IsPressed(this ModifierKey key)
        {
            switch (key)
            {
                case ModifierKey.Alt:
                    return MyInput.Static.IsAnyAltKeyPressed();
                case ModifierKey.LeftAlt:
                    return MyInput.Static.IsKeyPress(MyKeys.LeftAlt);
                case ModifierKey.RightAlt:
                    return MyInput.Static.IsKeyPress(MyKeys.RightAlt);
                case ModifierKey.CapsLock:
                    return MyInput.Static.IsKeyPress(MyKeys.CapsLock);
                case ModifierKey.Tab:
                    return MyInput.Static.IsKeyPress(MyKeys.Tab);
                case ModifierKey.Tilde:
                    return MyInput.Static.IsKeyPress(MyKeys.OemTilde);
                case ModifierKey.Quote:
                    return MyInput.Static.IsKeyPress(MyKeys.OemQuotes);
                case ModifierKey.Backslash:
                    return MyInput.Static.IsKeyPress(MyKeys.OemPipe);
                default:
                    return false;
            }
        }

        public static string GetName(this ModifierKey key)
        {
            switch (key)
            {
                case ModifierKey.LeftAlt:
                    return "LeftAlt";
                case ModifierKey.RightAlt:
                    return "RightAlt";
                case ModifierKey.CapsLock:
                    return "CapsLock";
                case ModifierKey.Tilde:
                    return "~";
                case ModifierKey.Quote:
                    return "'";
                case ModifierKey.Backslash:
                    return "\\";
                default:
                    return key.ToString();
            }
        }
    }
}
