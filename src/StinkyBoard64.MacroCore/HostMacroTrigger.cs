using System.Text.Json.Serialization;

namespace StinkyFootboard.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostModifierRequirement { None, Either, Left, Right, Both }

/// <summary>Physical modifier state. The two sides of each modifier remain distinct.</summary>
[Flags]
public enum HostModifierState
{
    None = 0,
    LeftCtrl = 1, RightCtrl = 2,
    LeftAlt = 4, RightAlt = 8,
    LeftShift = 16, RightShift = 32,
    LeftWin = 64, RightWin = 128
}

/// <summary>
/// Keyboard hotkey. Each modifier selector is exact: None means neither side,
/// Either means at least one side, Left/Right means only that side, Both means both.
/// </summary>
public sealed class HostMacroTrigger
{
    public int Key { get; set; } = 0x74; // F5 by default
    public HostModifierRequirement Ctrl { get; set; }
    public HostModifierRequirement Alt { get; set; }
    public HostModifierRequirement Shift { get; set; }
    public HostModifierRequirement Win { get; set; }

    public void Validate()
    {
        if (Key is < 1 or > 254 || Key is 0x10 or 0x11 or 0x12
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5
            or 0x5B or 0x5C)
            throw new InvalidDataException("Choose a non-modifier keyboard trigger key (VK 1-254).");
        foreach (var kind in new[] { Ctrl, Alt, Shift, Win })
            if (!Enum.IsDefined(kind))
                throw new InvalidDataException("Unrecognized modifier requirement.");
    }

    private static bool Match(HostModifierRequirement requirement, bool left, bool right) =>
        requirement switch
        {
            HostModifierRequirement.None => !left && !right,
            HostModifierRequirement.Either => left || right,
            HostModifierRequirement.Left => left && !right,
            HostModifierRequirement.Right => !left && right,
            HostModifierRequirement.Both => left && right,
            _ => false
        };

    public bool Matches(HostModifierState state) =>
        Match(Ctrl, state.HasFlag(HostModifierState.LeftCtrl), state.HasFlag(HostModifierState.RightCtrl)) &&
        Match(Alt, state.HasFlag(HostModifierState.LeftAlt), state.HasFlag(HostModifierState.RightAlt)) &&
        Match(Shift, state.HasFlag(HostModifierState.LeftShift), state.HasFlag(HostModifierState.RightShift)) &&
        Match(Win, state.HasFlag(HostModifierState.LeftWin), state.HasFlag(HostModifierState.RightWin));

    // The exhaustive 4^4 test is small, easy to audit, and avoids subtle
    // special-case bugs when two bindings partially overlap.
    public bool Overlaps(HostMacroTrigger other) =>
        Key == other.Key && Enumerable.Range(0, 256).Any(bits =>
            Matches((HostModifierState)bits) && other.Matches((HostModifierState)bits));

    public bool IsSubsetOf(HostMacroTrigger other) =>
        Key == other.Key && Enumerable.Range(0, 256).All(bits =>
            !Matches((HostModifierState)bits) || other.Matches((HostModifierState)bits));

    public HostMacroTrigger Copy() => new()
    {
        Key = Key, Ctrl = Ctrl, Alt = Alt, Shift = Shift, Win = Win
    };

    private static string Part(string name, HostModifierRequirement requirement) =>
        requirement switch
        {
            HostModifierRequirement.None => "",
            HostModifierRequirement.Either => name + "+",
            HostModifierRequirement.Left => "L" + name + "+",
            HostModifierRequirement.Right => "R" + name + "+",
            HostModifierRequirement.Both => "L" + name + "+R" + name + "+",
            _ => ""
        };

    public override string ToString() =>
        Part("Ctrl", Ctrl) + Part("Alt", Alt) + Part("Shift", Shift) +
        Part("Win", Win) + $"{(Key is >= 0x70 and <= 0x87 ? $"F{Key - 0x6F}" : $"VK 0x{Key:X2}")}";
}

/// <summary>
/// Only considers physical modifier events (not injected). Low-level Windows
/// callbacks often report generic VK_CONTROL/SHIFT/MENU, so use scan+extended.
/// </summary>
public sealed class HostPhysicalModifiers
{
    public HostModifierState State { get; private set; }

    /// <summary>
    /// The Windows keyboard driver emits synthetic Shift transitions for
    /// Shift+Numpad navigation translation (scan 0x22A / 0x236).
    /// They are NOT physical releases/presses and must not modify the
    /// held-modifier state used for exact left/right hotkey matching.
    /// Ordinary Shift scan codes 0x2A and 0x36 still update normally.
    /// </summary>
    public static bool IsSyntheticShiftEvent(uint virtualKey, uint scanCode) =>
        virtualKey is 0x10 or 0xA0 or 0xA1 &&
        scanCode is 0x22A or 0x236;

    public bool Update(uint virtualKey, uint scanCode, uint flags, bool down)
    {
        // Windows generates artificial Shift up/down events before/after
        // numeric-keypad navigation. A VK_HOME (scan 0x47) is therefore
        // allowed to match a physically held Right Shift even if Windows'
        // logical Shift state was briefly released to translate the key.
        if (IsSyntheticShiftEvent(virtualKey, scanCode))
            return true;

        const uint extended = 0x01;
        HostModifierState bit = virtualKey switch
        {
            0xA0 => HostModifierState.LeftShift,
            0xA1 => HostModifierState.RightShift,
            0x10 => (scanCode & 0xFF) == 0x36 ? HostModifierState.RightShift : HostModifierState.LeftShift,
            0xA2 => HostModifierState.LeftCtrl,
            0xA3 => HostModifierState.RightCtrl,
            0x11 => (flags & extended) != 0 ? HostModifierState.RightCtrl : HostModifierState.LeftCtrl,
            0xA4 => HostModifierState.LeftAlt,
            0xA5 => HostModifierState.RightAlt,
            0x12 => (flags & extended) != 0 ? HostModifierState.RightAlt : HostModifierState.LeftAlt,
            0x5B => HostModifierState.LeftWin,
            0x5C => HostModifierState.RightWin,
            _ => HostModifierState.None
        };
        if (bit == HostModifierState.None) return false;
        State = down ? State | bit : State & ~bit;
        return true;
    }
}
