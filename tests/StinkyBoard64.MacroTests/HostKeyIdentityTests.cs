using Microsoft.VisualStudio.TestTools.UnitTesting;
using StinkyFootboard.Core;

namespace StinkyFootboard.Tests;

[TestClass]
public sealed class HostKeyIdentityTests
{
    [TestMethod]
    public void ShiftedKeypadSevenHomeMapsToPhysicalNumpadSeven()
    {
        Assert.AreEqual(0x67, HostKeyIdentity.NormalizeVirtualKey(0x24, 0x47, 0));
        Assert.AreEqual(0x67, HostKeyIdentity.NormalizeVirtualKey(0x67, 0x47, 0));
        // The dedicated Home key uses E0 47; it must NOT be Numpad7.
        Assert.AreEqual(0x24, HostKeyIdentity.NormalizeVirtualKey(0x24, 0x47, 1));
    }

    [TestMethod]
    public void AllKeypadNavigationAliasesFollowPhysicalScanCode()
    {
        var cases = new (uint scan, uint navigationVk, int keypadVk)[]
        {
            (0x47, 0x24, 0x67), // Home
            (0x48, 0x26, 0x68), // Up
            (0x49, 0x21, 0x69), // Page Up
            (0x4B, 0x25, 0x64), // Left
            (0x4C, 0x0C, 0x65), // Clear
            (0x4D, 0x27, 0x66), // Right
            (0x4F, 0x23, 0x61), // End
            (0x50, 0x28, 0x62), // Down
            (0x51, 0x22, 0x63), // Page Down
            (0x52, 0x2D, 0x60), // Insert
            (0x53, 0x2E, 0x6E)  // Delete
        };
        foreach (var (scan, navigationVk, keypadVk) in cases)
        {
            Assert.AreEqual(keypadVk,
                HostKeyIdentity.NormalizeVirtualKey(navigationVk, scan, 0));
            Assert.AreEqual(keypadVk,
                HostKeyIdentity.NormalizeVirtualKey((uint)keypadVk, scan, 0));
            Assert.AreEqual((int)navigationVk,
                HostKeyIdentity.NormalizeVirtualKey(navigationVk, scan, 1));
        }
    }

    [TestMethod]
    public void KeypadNormalizationDoesNotChangeOtherKeysOrOtherScans()
    {
        Assert.AreEqual(0x41, HostKeyIdentity.NormalizeVirtualKey(0x41, 0x1E, 0));
        Assert.AreEqual(0x24, HostKeyIdentity.NormalizeVirtualKey(0x24, 0x48, 0));
        Assert.AreEqual(0x79, HostKeyIdentity.NormalizeVirtualKey(0x79, 0x44, 0));
    }

    [TestMethod]
    public void SpecificShiftAltCtrlHotkeyMatchesAfterKeypadNormalization()
    {
        var trigger = new HostMacroTrigger
        {
            Key = 0x67,
            Ctrl = HostModifierRequirement.Left,
            Shift = HostModifierRequirement.Right,
            Alt = HostModifierRequirement.Right
        };
        trigger.Validate();
        var physical = new HostPhysicalModifiers();
        physical.Update(0x11, 0x1D, 0, true); // Left Ctrl
        physical.Update(0x10, 0x36, 0, true); // Right Shift
        physical.Update(0x12, 0x38, 0x01, true); // Right Alt (extended)
        int delivered = HostKeyIdentity.NormalizeVirtualKey(0x24, 0x47, 0);
        Assert.AreEqual(trigger.Key, delivered);
        Assert.IsTrue(trigger.Matches(physical.State));

        physical.Update(0x12, 0x38, 0x01, false);
        Assert.IsFalse(trigger.Matches(physical.State));
    }

    [TestMethod]
    public void WindowsFakeRightShiftUpDoesNotLosePhysicallyHeldShift()
    {
        var trigger = new HostMacroTrigger
        {
            Key = 0x67, Ctrl = HostModifierRequirement.Left,
            Alt = HostModifierRequirement.Right,
            Shift = HostModifierRequirement.Right
        };
        var state = new HostPhysicalModifiers();
        state.Update(0x11, 0x1D, 0, true); // LCtrl physical down
        state.Update(0x12, 0x38, 1, true); // RAlt physical down
        state.Update(0x10, 0x36, 0, true); // RShift physical down

        // Windows emits an artificial RShift UP before the keypad key when
        // interpreting Shift+Numpad7 as Home. It must be ignored.
        Assert.IsTrue(HostPhysicalModifiers.IsSyntheticShiftEvent(0x10, 0x236));
        Assert.IsTrue(state.Update(0x10, 0x236, 0, false));
        Assert.AreEqual(
            HostModifierState.LeftCtrl | HostModifierState.RightAlt |
            HostModifierState.RightShift, state.State);
        int key = HostKeyIdentity.NormalizeVirtualKey(0x24, 0x47, 0);
        Assert.AreEqual(trigger.Key, key);
        Assert.IsTrue(trigger.Matches(state.State));

        // The driver's later synthetic Shift DOWN is also ignored.
        state.Update(0xA1, 0x236, 0, true);
        Assert.IsTrue(trigger.Matches(state.State));

        // A REAL right Shift release has scan 0x36 and MUST clear it.
        Assert.IsFalse(HostPhysicalModifiers.IsSyntheticShiftEvent(0xA1, 0x36));
        state.Update(0xA1, 0x36, 0, false);
        Assert.IsFalse(state.State.HasFlag(HostModifierState.RightShift));
        Assert.IsFalse(trigger.Matches(state.State));
    }

    [TestMethod]
    public void FakeShiftEventsCannotInventShiftAndDoNotChangeOtherModifiers()
    {
        var state = new HostPhysicalModifiers();
        state.Update(0x11, 0x1D, 0, true);
        state.Update(0x10, 0x236, 0, true);
        state.Update(0x10, 0x22A, 0, false);
        Assert.AreEqual(HostModifierState.LeftCtrl, state.State);

        state.Update(0x10, 0x2A, 0, true); // real LShift
        state.Update(0x10, 0x22A, 0, false); // synthetic UP
        Assert.IsTrue(state.State.HasFlag(HostModifierState.LeftShift));
        state.Update(0x10, 0x2A, 0, false); // real UP
        Assert.IsFalse(state.State.HasFlag(HostModifierState.LeftShift));

        // Scan 0x236 on a non-shift key is not a Shift event.
        Assert.IsFalse(HostPhysicalModifiers.IsSyntheticShiftEvent(0x41, 0x236));
    }

}
