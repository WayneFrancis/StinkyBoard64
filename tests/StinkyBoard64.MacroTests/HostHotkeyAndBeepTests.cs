using Microsoft.VisualStudio.TestTools.UnitTesting;
using StinkyFootboard.Core;

namespace StinkyFootboard.Tests;

[TestClass]
public sealed class HostHotkeyAndBeepTests
{
    [TestMethod]
    public void PhysicallyDistinguishesLeftAndRightControlShiftAndAlt()
    {
        var m = new HostPhysicalModifiers();
        Assert.IsTrue(m.Update(0x11, 0x1D, 0, true));
        Assert.IsTrue(m.Update(0x11, 0x1D, 0x01, true));
        Assert.IsTrue(m.Update(0x10, 0x36, 0, true));
        Assert.IsTrue(m.Update(0x12, 0x38, 0x01, true));
        Assert.AreEqual(HostModifierState.LeftCtrl | HostModifierState.RightCtrl |
            HostModifierState.RightShift | HostModifierState.RightAlt, m.State);
        Assert.IsTrue(m.Update(0x11, 0x1D, 0, false));
        Assert.IsFalse(m.State.HasFlag(HostModifierState.LeftCtrl));
        Assert.IsTrue(m.State.HasFlag(HostModifierState.RightCtrl));
    }

    [TestMethod]
    public void ExactSidesAndEitherModifierMatchAsExpected()
    {
        var exact = new HostMacroTrigger
        {
            Key = 0x79, Alt = HostModifierRequirement.Left,
            Shift = HostModifierRequirement.Right,
            Ctrl = HostModifierRequirement.Right
        };
        exact.Validate();
        var state = HostModifierState.LeftAlt |
            HostModifierState.RightShift | HostModifierState.RightCtrl;
        Assert.IsTrue(exact.Matches(state));
        Assert.IsFalse(exact.Matches(state | HostModifierState.RightAlt));
        Assert.IsFalse(exact.Matches(HostModifierState.LeftAlt |
            HostModifierState.LeftShift | HostModifierState.RightCtrl));
        Assert.IsFalse(exact.Matches(state | HostModifierState.LeftCtrl));

        var either = new HostMacroTrigger
        {
            Key = 0x79,
            Alt = HostModifierRequirement.Either,
            Shift = HostModifierRequirement.Right,
            Ctrl = HostModifierRequirement.Right
        };
        Assert.IsTrue(either.Matches(state));
        Assert.IsTrue(exact.IsSubsetOf(either));
        Assert.IsFalse(either.IsSubsetOf(exact));
        Assert.IsTrue(either.Overlaps(exact));
    }

    [TestMethod]
    public void ArbitraryNumberOfBindingsAndSpecializationRoundTrip()
    {
        var p = new HostMacroProfile();
        p.Bindings.Clear();
        for (int i = 0; i < 32; i++)
            p.Bindings.Add(new HostMacroBinding
            {
                Slot = i + 8,
                Trigger = new HostMacroTrigger { Key = 0x70 + i },
                Text = "testing"
            });
        // The above keys stay in 1..254 and are usable non-modifier keys.
        p.Validate();
        Assert.AreEqual(32, p.Bindings.Count);

        string file = Path.GetTempFileName();
        try
        {
            p.SaveJson(file);
            var loaded = HostMacroProfile.LoadJson(file);
            Assert.AreEqual(32, loaded.Bindings.Count);
            Assert.AreEqual(0x70 + 31, loaded.Bindings[31].EffectiveTrigger.Key);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void DuplicateAndAmbiguousOverlappingTriggersAreRejected()
    {
        var p = new HostMacroProfile();
        p.Bindings.Clear();
        p.Bindings.Add(new HostMacroBinding
        {
            Slot = 1, Trigger = new HostMacroTrigger
                { Key = 0x79, Alt = HostModifierRequirement.Either },
            Text = "a"
        });
        p.Bindings.Add(new HostMacroBinding
        {
            Slot = 2, Trigger = new HostMacroTrigger
                { Key = 0x79, Alt = HostModifierRequirement.Left },
            Text = "b"
        });
        p.Validate(); // left-only more specific; allowed
        p.Bindings[1].Trigger!.Alt = HostModifierRequirement.Either;
        Assert.ThrowsExactly<InvalidDataException>(() => p.Validate());
        p.Bindings[1].Trigger!.Alt = HostModifierRequirement.Left;
        p.Bindings[1].Trigger!.Shift = HostModifierRequirement.Either;
        p.Bindings[0].Trigger!.Shift = HostModifierRequirement.Right;
        // Alt-Either + RightShift overlaps LeftAlt + Shift-Either,
        // but neither is strictly more specific: reject ambiguity.
        Assert.IsTrue(p.Bindings[0].EffectiveTrigger.Overlaps(p.Bindings[1].EffectiveTrigger));
        Assert.ThrowsExactly<InvalidDataException>(() => p.Validate());
    }

    [TestMethod]
    public void StandaloneJsonCanBeLoadedIntoAnotherTriggerWithoutChangingIt()
    {
        var first = new HostMacroBinding
        {
            Slot = 10,
            Trigger = new HostMacroTrigger { Key = 0x41 },
            Mode = HostMacroMode.Steps,
            Steps =
            [
                new HostMacroStep
                {
                    Action = HostMacroAction.Beep,
                    Milliseconds = 260,
                    FrequencyHz = 1275
                },
                new HostMacroStep { Action = HostMacroAction.Delay, Milliseconds = 200 }
            ]
        };
        string file = Path.GetTempFileName();
        try
        {
            HostMacroDocument.FromBinding(first).SaveJson(file);
            var loaded = HostMacroDocument.LoadJson(file);
            var second = new HostMacroBinding
            {
                Slot = 15, Trigger = new HostMacroTrigger
                    { Key = 0x79, Ctrl = HostModifierRequirement.Right }
            };
            loaded.ApplyTo(second);
            Assert.AreEqual(0x79, second.EffectiveTrigger.Key);
            Assert.AreEqual(HostModifierRequirement.Right, second.EffectiveTrigger.Ctrl);
            Assert.AreEqual(2, second.Steps.Count);
            Assert.AreEqual(1275, second.Steps[0].FrequencyHz);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void BeepFrequencyAndDurationAreValidated()
    {
        var step = new HostMacroStep { Action = HostMacroAction.Beep };
        step.Validate(); // 880 Hz and 120 ms default
        step.FrequencyHz = 20;
        Assert.ThrowsExactly<InvalidDataException>(() => step.Validate());
        step.FrequencyHz = 440;
        step.Milliseconds = 5_001;
        Assert.ThrowsExactly<InvalidDataException>(() => step.Validate());
        step.Milliseconds = 500;
        step.Validate();
    }
}
