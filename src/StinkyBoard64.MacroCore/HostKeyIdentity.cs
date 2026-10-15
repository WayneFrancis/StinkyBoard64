namespace StinkyFootboard.Core;

/// <summary>
/// Physical-key normalization for keypad keys that Windows may translate
/// to navigation virtual keys depending on NumLock and Shift state.
/// KBDLLHOOKSTRUCT's low bit in Flags is LLKHF_EXTENDED (E0-prefixed
/// keys); that bit distinguishes the dedicated navigation cluster
/// from the non-extended numeric keypad. Other keys are left untouched.
/// </summary>
public static class HostKeyIdentity
{
    public static int NormalizeVirtualKey(uint virtualKey, uint scanCode, uint hookFlags)
    {
        const uint llkhfExtended = 0x01;
        if ((hookFlags & llkhfExtended) != 0)
            return (int)virtualKey;

        // Set-1 scan codes for the physical number pad. Only translate
        // the actual corresponding VK pair; don't rewrite unrelated
        // non-keypad keys reported by unusual layouts or drivers.
        return (scanCode, virtualKey) switch
        {
            (0x47, 0x24 or 0x67) => 0x67, // Home / Numpad 7
            (0x48, 0x26 or 0x68) => 0x68, // Up / Numpad 8
            (0x49, 0x21 or 0x69) => 0x69, // Prior / Numpad 9
            (0x4B, 0x25 or 0x64) => 0x64, // Left / Numpad 4
            (0x4C, 0x0C or 0x65) => 0x65, // Clear / Numpad 5
            (0x4D, 0x27 or 0x66) => 0x66, // Right / Numpad 6
            (0x4F, 0x23 or 0x61) => 0x61, // End / Numpad 1
            (0x50, 0x28 or 0x62) => 0x62, // Down / Numpad 2
            (0x51, 0x22 or 0x63) => 0x63, // Next / Numpad 3
            (0x52, 0x2D or 0x60) => 0x60, // Insert / Numpad 0
            (0x53, 0x2E or 0x6E) => 0x6E, // Delete / Numpad decimal
            _ => (int)virtualKey
        };
    }
}
