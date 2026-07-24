namespace CoControl.Input;

/// <summary>
/// USB HID Usage IDs, Keyboard/Keypad page (0x07).
/// Used both for the remap table and for macro events.
/// </summary>
public enum KeyCode : byte
{
    None = 0x00,

    A = 0x04, B = 0x05, C = 0x06, D = 0x07, E = 0x08, F = 0x09, G = 0x0A,
    H = 0x0B, I = 0x0C, J = 0x0D, K = 0x0E, L = 0x0F, M = 0x10, N = 0x11,
    O = 0x12, P = 0x13, Q = 0x14, R = 0x15, S = 0x16, T = 0x17, U = 0x18,
    V = 0x19, W = 0x1A, X = 0x1B, Y = 0x1C, Z = 0x1D,

    D1 = 0x1E, D2 = 0x1F, D3 = 0x20, D4 = 0x21, D5 = 0x22,
    D6 = 0x23, D7 = 0x24, D8 = 0x25, D9 = 0x26, D0 = 0x27,

    Enter = 0x28, Escape = 0x29, Backspace = 0x2A, Tab = 0x2B, Space = 0x2C,
    Minus = 0x2D, Equals = 0x2E, LeftBracket = 0x2F, RightBracket = 0x30,
    Backslash = 0x31, NonUsHash = 0x32, Semicolon = 0x33, Apostrophe = 0x34,
    Grave = 0x35, Comma = 0x36, Period = 0x37, Slash = 0x38, CapsLock = 0x39,

    F1 = 0x3A, F2 = 0x3B, F3 = 0x3C, F4 = 0x3D, F5 = 0x3E, F6 = 0x3F,
    F7 = 0x40, F8 = 0x41, F9 = 0x42, F10 = 0x43, F11 = 0x44, F12 = 0x45,

    PrintScreen = 0x46, ScrollLock = 0x47, Pause = 0x48,
    Insert = 0x49, Home = 0x4A, PageUp = 0x4B,
    Delete = 0x4C, End = 0x4D, PageDown = 0x4E,
    Right = 0x4F, Left = 0x50, Down = 0x51, Up = 0x52,

    NonUsBackslash = 0x64, Application = 0x65,

    LeftCtrl = 0xE0, LeftShift = 0xE1, LeftAlt = 0xE2, LeftGui = 0xE3,
    RightCtrl = 0xE4, RightShift = 0xE5, RightAlt = 0xE6, RightGui = 0xE7,
}

/// <summary>
/// What a physical key is remapped to.
/// </summary>
public enum RemapType : byte
{
    /// <summary>Default firmware behavior (entry ignored).</summary>
    Default = 0x00,
    /// <summary>Plain key: param1 = KeyCode.</summary>
    Key = 0x01,
    /// <summary>Modifier combo: param1 = modifier bitmask (HID), param2 = KeyCode.</summary>
    Combo = 0x02,
    /// <summary>Consumer control (media): param1..2 = usage LE16.</summary>
    Media = 0x03,
    /// <summary>Macro trigger: param1 = macro slot index.</summary>
    Macro = 0x04,
    /// <summary>Key disabled.</summary>
    Disabled = 0xFF,
}

/// <summary>HID modifier bitmask used by <see cref="RemapType.Combo"/>.</summary>
[System.Flags]
public enum Modifiers : byte
{
    None = 0x00,
    LeftCtrl = 0x01, LeftShift = 0x02, LeftAlt = 0x04, LeftGui = 0x08,
    RightCtrl = 0x10, RightShift = 0x20, RightAlt = 0x40, RightGui = 0x80,
}
