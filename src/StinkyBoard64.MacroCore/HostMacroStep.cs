using System.Text.Json.Serialization;

namespace StinkyFootboard.Core;

/// <summary>
/// Host-only macro actions. The footboard continues to send its existing
/// F5-F12 triggers; none of these actions is stored in board EEPROM.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostMacroMode { Text, Steps }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostMacroAction
{
    Delay,
    KeyDown, KeyUp, KeyPress,
    LeftMouseDown, LeftMouseUp, LeftMouseClick, LeftMouseDoubleClick,
    RightMouseDown, RightMouseUp, RightMouseClick, RightMouseDoubleClick,
    MiddleMouseDown, MiddleMouseUp, MiddleMouseClick, MiddleMouseDoubleClick,
    WheelUp, WheelDown,
    Beep
}

public sealed class HostMacroStep
{
    public HostMacroAction Action { get; set; } = HostMacroAction.KeyPress;

    /// <summary>Windows virtual-key value 1..254; applies to keyboard actions.</summary>
    public int Key { get; set; } = 0x41;

    /// <summary>
    /// Delay step: explicit wait (0..3,600,000 ms).
    /// Press/click: hold time (0 means use the live host speed setting).
    /// Other actions: ignored.
    /// </summary>
    public int Milliseconds { get; set; }

    /// <summary>Signed direction is represented by Action; this is a positive count.</summary>
    public int WheelNotches { get; set; } = 1;

    /// <summary>Console.Beep frequency. Windows supports 37–32767 Hz.</summary>
    public int FrequencyHz { get; set; } = 880;
    public bool UsesBeep => Action == HostMacroAction.Beep;

    public bool UsesKey => Action is HostMacroAction.KeyDown or HostMacroAction.KeyUp
        or HostMacroAction.KeyPress;

    public bool UsesDuration => Action is HostMacroAction.Delay
        or HostMacroAction.KeyPress
        or HostMacroAction.LeftMouseClick or HostMacroAction.LeftMouseDoubleClick
        or HostMacroAction.RightMouseClick or HostMacroAction.RightMouseDoubleClick
        or HostMacroAction.MiddleMouseClick or HostMacroAction.MiddleMouseDoubleClick;

    public bool UsesWheel => Action is HostMacroAction.WheelUp or HostMacroAction.WheelDown;

    public void Validate()
    {
        if (!Enum.IsDefined(Action))
            throw new InvalidDataException("Unsupported macro action.");
        if (UsesKey && Key is < 1 or > 254)
            throw new InvalidDataException("Keyboard keys must have a Windows VK value from 1 to 254.");
        if (Action == HostMacroAction.Delay && Milliseconds is < 0 or > 3_600_000)
            throw new InvalidDataException("Delay must be 0 to 3,600,000 ms.");
        if (UsesDuration && Action != HostMacroAction.Delay &&
            Milliseconds is < 0 or > 60_000)
            throw new InvalidDataException("Press/click duration must be 0 (default) or 1 to 60,000 ms.");
        if (UsesBeep && FrequencyHz is < 37 or > 32767)
            throw new InvalidDataException("Beep frequency must be 37–32767 Hz.");
        if (UsesBeep && Milliseconds is < 0 or > 5000)
            throw new InvalidDataException("Beep duration must be 0 (120 ms default) or 1–5000 ms.");
        if (UsesWheel && WheelNotches is < 1 or > 100)
            throw new InvalidDataException("Wheel steps must be 1 to 100 notches.");
    }

    public HostMacroStep Copy() => new()
    {
        Action = Action, Key = Key, Milliseconds = Milliseconds,
        WheelNotches = WheelNotches, FrequencyHz = FrequencyHz
    };
}
