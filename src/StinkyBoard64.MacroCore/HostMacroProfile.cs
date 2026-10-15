using System.Text.Json;

namespace StinkyFootboard.Core;

/// <summary>
/// Host-side, never stored on the board. Each slot corresponds to a unique
/// Windows F5..F12 trigger key. An empty binding leaves that key unmodified.
/// An active hook intercepts trigger keys from ANY keyboard, not a particular
/// HID device: unique assignments avoid per-device source ambiguity.
/// </summary>
public sealed class HostMacroProfile
{
    public int Version { get; set; } = 2;
    // Existing version 2 profiles without this property keep the old
    // 40 ms key-down + 40 ms gap behavior by default.
    public int StepDelayMilliseconds { get; set; } = 40;
    public List<HostMacroBinding> Bindings { get; set; } =
        Enumerable.Range(0, 8).Select(slot => new HostMacroBinding
        {
            Slot = slot
        }).ToList();

    public static HostMacroProfile LoadJson(string filename)
    {
        var profile = JsonSerializer.Deserialize<HostMacroProfile>(File.ReadAllText(filename),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The host macro JSON file was empty.");
        profile.Validate();
        return profile;
    }

    public void SaveJson(string filename)
    {
        Validate();
        File.WriteAllText(filename, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Validate()
    {
        // Reject old v1 JSON; its slots meant invalid F13–F20, not F5–F12.
        if (Version != 2 || Bindings is null || Bindings.Any(b => b is null) ||
            Bindings.Any(b => b.Slot < 0) ||
            Bindings.Select(b => b.Slot).Distinct().Count() != Bindings.Count)
            throw new InvalidDataException("Profile version, binding IDs or duplicated IDs are invalid.");

        HostMacroTiming.ValidateDelay(StepDelayMilliseconds);

        foreach (var binding in Bindings)
        {
            binding.Validate();
        }
        var active = Bindings.Where(b => b.IsConfigured).ToArray();
        for (int i = 0; i < active.Length; i++)
        for (int j = i + 1; j < active.Length; j++)
        {
            var left = active[i].EffectiveTrigger;
            var right = active[j].EffectiveTrigger;
            if (left.Overlaps(right) &&
                (left.IsSubsetOf(right) == right.IsSubsetOf(left)))
                throw new InvalidDataException(
                    $"Conflicting or ambiguous hotkeys: {left} and {right}. " +
                    "Use non-overlapping combos or make one strictly more specific.");
        }
    }

    public HostMacroProfile Copy() => new()
    {
        Version = Version,
        StepDelayMilliseconds = StepDelayMilliseconds,
        Bindings = Bindings.Select(b => new HostMacroBinding
        {
            Slot = b.Slot, Text = b.Text, Mode = b.Mode,
            Trigger = b.Trigger?.Copy(),
            Steps = b.Steps.Select(step => step.Copy()).ToList()
        }).ToList()
    };
}

/// <summary>
/// Global default hold duration for step macros. Unicode text has no inserted delays.
/// Higher slider values correspond to faster playback. Both bounds have
/// ten-millisecond resolution to avoid a misleading sub-OS-tick setting.
/// </summary>
public static class HostMacroTiming
{
    public const int MinimumDelayMilliseconds = 10;
    public const int MaximumDelayMilliseconds = 200;
    public const int DefaultDelayMilliseconds = 40;
    public const int MinimumSpeed = 1;
    public const int MaximumSpeed = 20;
    public const int DefaultSpeed = 17;

    public static int DelayFromSpeed(int speed)
    {
        if (speed is < MinimumSpeed or > MaximumSpeed)
            throw new ArgumentOutOfRangeException(nameof(speed));
        return (MaximumSpeed + 1 - speed) * 10;
    }

    public static int SpeedFromDelay(int delayMilliseconds)
    {
        ValidateDelay(delayMilliseconds);
        return MaximumSpeed + 1 - delayMilliseconds / 10;
    }

    public static void ValidateDelay(int delayMilliseconds)
    {
        if (delayMilliseconds is < MinimumDelayMilliseconds or
            > MaximumDelayMilliseconds || delayMilliseconds % 10 != 0)
            throw new InvalidDataException(
                "Macro key duration must be 10–200 ms in steps of 10 ms.");
    }
}

public sealed class HostMacroBinding
{
    /// <summary>Zero-based marker slot: 0=F5, 7=F12.</summary>
    public int Slot { get; set; }
    /// <summary>
    /// Older version-2 JSON defaults to Text. Its former 32-character limit
    /// is a firmware-era restriction and does not apply to host Unicode text.
    /// </summary>
    public HostMacroMode Mode { get; set; } = HostMacroMode.Text;
    public string Text { get; set; } = "";
    public List<HostMacroStep> Steps { get; set; } = [];
    /// <summary>Null means the legacy F5–F12 trigger inferred from Slot 0..7.</summary>
    public HostMacroTrigger? Trigger { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public HostMacroTrigger EffectiveTrigger => Trigger ??
        new HostMacroTrigger { Key = 0x74 + Slot };

    public bool IsConfigured => Mode switch
    {
        HostMacroMode.Text => !string.IsNullOrEmpty(Text),
        HostMacroMode.Steps => Steps is { Count: > 0 },
        _ => false
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
            throw new InvalidDataException($"Binding {Slot} has an unknown macro mode.");
        if (Text is null || Steps is null || Steps.Any(s => s is null))
            throw new InvalidDataException($"Binding {Slot} macro data is incomplete.");
        if (Trigger is null && Slot > 7)
            throw new InvalidDataException("New macros require an explicit trigger.");
        EffectiveTrigger.Validate();
        if (Mode == HostMacroMode.Steps)
            foreach (var step in Steps)
                step.Validate();
    }

    public HostMacroBinding Copy() => new()
    {
        Slot = Slot, Mode = Mode, Text = Text,
        Trigger = Trigger?.Copy(), Steps = Steps.Select(s => s.Copy()).ToList()
    };
}

