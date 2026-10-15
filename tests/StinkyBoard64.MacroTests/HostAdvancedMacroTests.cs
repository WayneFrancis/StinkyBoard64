using Microsoft.VisualStudio.TestTools.UnitTesting;
using StinkyFootboard.Core;

namespace StinkyFootboard.Tests;

[TestClass]
public sealed class HostAdvancedMacroTests
{
    [TestMethod]
    public void TextModeAcceptsLongUnicodePunctuationAndNewlines()
    {
        var p = new HostMacroProfile();
        string text = new string('x', 1000) + " — Hello, world!\n雪 😀";
        p.Bindings[0].Mode = HostMacroMode.Text;
        p.Bindings[0].Text = text;
        p.Validate();
        Assert.IsTrue(p.Bindings[0].IsConfigured);

        string filename = Path.GetTempFileName();
        try
        {
            p.SaveJson(filename);
            var loaded = HostMacroProfile.LoadJson(filename);
            Assert.AreEqual(text, loaded.Bindings[0].Text);
            Assert.AreEqual(HostMacroMode.Text, loaded.Bindings[0].Mode);
        }
        finally { File.Delete(filename); }
    }

    [TestMethod]
    public void RoundTripsKeyboardMouseAndWheelStepsWithoutLengthLimit()
    {
        var p = new HostMacroProfile();
        var b = p.Bindings[0];
        b.Mode = HostMacroMode.Steps;
        foreach (HostMacroAction kind in Enum.GetValues<HostMacroAction>())
            b.Steps.Add(new HostMacroStep
            {
                Action = kind, Key = 0x7B, Milliseconds = 25, WheelNotches = 3
            });
        for (int i = 0; i < 150; i++)
            b.Steps.Add(new HostMacroStep { Action = HostMacroAction.KeyPress, Key = 0x70 });
        Assert.AreEqual(169, b.Steps.Count);
        p.Validate();

        string filename = Path.GetTempFileName();
        try
        {
            p.SaveJson(filename);
            var loaded = HostMacroProfile.LoadJson(filename);
            var sequence = loaded.Bindings[0];
            Assert.AreEqual(HostMacroMode.Steps, sequence.Mode);
            Assert.AreEqual(169, sequence.Steps.Count);
            Assert.AreEqual(HostMacroAction.WheelDown, sequence.Steps[17].Action);
            Assert.AreEqual(3, sequence.Steps[17].WheelNotches);
            Assert.AreEqual(0x7B, sequence.Steps[1].Key);
            Assert.IsTrue(sequence.IsConfigured);
        }
        finally { File.Delete(filename); }
    }

    [TestMethod]
    public void DeepCopyDoesNotShareSequenceSteps()
    {
        var p = new HostMacroProfile();
        p.Bindings[4].Mode = HostMacroMode.Steps;
        p.Bindings[4].Steps.Add(new HostMacroStep
            { Action = HostMacroAction.WheelUp, WheelNotches = 2 });
        var copy = p.Copy();
        copy.Bindings[4].Steps[0].WheelNotches = 9;
        Assert.AreEqual(2, p.Bindings[4].Steps[0].WheelNotches);
    }

    [TestMethod]
    public void RejectsUnsupportedOrUnboundedStepValues()
    {
        var p = new HostMacroProfile();
        var b = p.Bindings[0];
        b.Mode = HostMacroMode.Steps;
        var step = new HostMacroStep { Action = HostMacroAction.Delay, Milliseconds = -1 };
        b.Steps.Add(step);
        Assert.ThrowsExactly<InvalidDataException>(() => p.Validate());
        step.Milliseconds = 3_600_001;
        Assert.ThrowsExactly<InvalidDataException>(() => p.Validate());
        step.Action = HostMacroAction.KeyPress;
        step.Key = 0;
        step.Milliseconds = 40;
        Assert.ThrowsExactly<InvalidDataException>(() => p.Validate());
        step.Key = 0xA2;
        step.Milliseconds = 60_001;
        Assert.ThrowsExactly<InvalidDataException>(() => p.Validate());
        step.Action = HostMacroAction.WheelDown;
        step.WheelNotches = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => p.Validate());
        step.WheelNotches = 1;
        step.Milliseconds = 0;
        p.Validate();
    }

    [TestMethod]
    public void OldVersionTwoTextBindingsStillWorkWithoutModeField()
    {
        const string json = """
        {"Version":2,"StepDelayMilliseconds":40,"Bindings":[
        {"Slot":0,"Text":"jr"},{"Slot":1,"Text":""},
        {"Slot":2,"Text":""},{"Slot":3,"Text":""},
        {"Slot":4,"Text":""},{"Slot":5,"Text":""},
        {"Slot":6,"Text":""},{"Slot":7,"Text":""}]}
        """;
        string filename = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filename, json);
            var loaded = HostMacroProfile.LoadJson(filename);
            Assert.AreEqual("jr", loaded.Bindings[0].Text);
            Assert.AreEqual(HostMacroMode.Text, loaded.Bindings[0].Mode);
            Assert.IsTrue(loaded.Bindings[0].IsConfigured);
        }
        finally { File.Delete(filename); }
    }
}
