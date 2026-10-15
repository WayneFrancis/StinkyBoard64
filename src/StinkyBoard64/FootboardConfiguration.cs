using System.Text.Json;
using System.Text.Json.Serialization;

namespace StinkyFootboard.Core;

// Only firmware 1.2's proven single-key configuration is supported.
// Existing modes, modifier bytes and stored intervals are retained.
public enum StinkyButtonMode : byte { Normal, KeyDown, Pulse, Unbound }

public sealed class FootboardButton
{
  public int Number { get; set; }
  public StinkyButtonMode Mode { get; set; }
  public byte KeyUsage { get; set; }
  public byte ModifierMask { get; set; }
  public uint StoredIntervalMilliseconds { get; set; }
  public int PulseClicksPerSecond { get; set; } = 10;

  // Warning only. The full alternate bank is not readable or preserved.
  [JsonIgnore]
  public bool HasExtendedData { get; set; }

  public FootboardButton Copy() => (FootboardButton)MemberwiseClone();
}

public sealed class FootboardConfiguration
{
  public int FormatVersion { get; set; } = 1;
  public List<FootboardButton> Buttons { get; set; } =
      Enumerable.Range(1, 4).Select(i => new FootboardButton
      {
        Number = i,
        Mode = StinkyButtonMode.Normal,
        KeyUsage = (byte)(0x58 + i)
      }).ToList();

  [JsonIgnore]
  public bool HasExtendedData => Buttons.Any(b => b.HasExtendedData);

  public FootboardConfiguration Copy() => new()
  {
    FormatVersion = FormatVersion,
    Buttons = Buttons.Select(b => b.Copy()).ToList()
  };

  public void Validate()
  {
    if (FormatVersion != 1 || 
        Buttons is null || 
        Buttons.Count != 4 ||
        Buttons.Any(b => b is null) ||
        !Buttons.Select(b => b.Number).Order().SequenceEqual(new[] { 1, 2, 3, 4 }))
      throw new InvalidDataException("Expected format version 1 with four numbered buttons.");

    foreach (var b in Buttons)
    {
      if (!Enum.IsDefined(b.Mode) || 
          b.KeyUsage == 0 ||
          (b.ModifierMask & ~StinkyModifiers.SupportedMask) != 0 ||
          b.StoredIntervalMilliseconds > ushort.MaxValue ||
          b.PulseClicksPerSecond is < 1 or > 30)
        throw new InvalidDataException($"Button {b.Number}: unsupported profile value.");
    }
  }

  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
  };

  public static FootboardConfiguration LoadJson(string path)
  {
    string json = File.ReadAllText(path);
    using (var document = JsonDocument.Parse(json))
    {
      // Prevent loss of previously configured macros from profiles of
      // the full editor; only simple key profiles may be loaded here.
      if (document.RootElement.TryGetProperty("Buttons", out var buttons) &&
          buttons.ValueKind == JsonValueKind.Array)
        foreach (var button in buttons.EnumerateArray())
          if (button.TryGetProperty("MacroSteps", out var macro) &&
              macro.ValueKind == JsonValueKind.Array && 
              macro.GetArrayLength() != 0)
            throw new InvalidDataException("The JSON contains a multi-step macro. This app supports single keys only.");
    }

    var result = JsonSerializer.Deserialize<FootboardConfiguration>(json, Options) ?? throw new InvalidDataException("Empty JSON profile.");
    result.Validate();
    return result;
  }

  public void SaveJson(string path)
  {
    Validate();
    File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
  }
}

public sealed record HidKeyOption(string Name, byte Usage)
{
  public override string ToString() => Name;
}

public static class BasicHidKeys
{
  public static IReadOnlyList<HidKeyOption> Options { get; } = Create();

  public static string Describe(byte usage) => Options.FirstOrDefault(x => x.Usage == usage)?.Name ?? $"HID 0x{usage:X2}";

  private static IReadOnlyList<HidKeyOption> Create()
  {
    var values = new List<HidKeyOption>();
    for (int i = 0; i < 26; i++)
      values.Add(new HidKeyOption(((char)('A' + i)).ToString(), (byte)(0x04 + i)));

    for (int i = 1; i <= 9; i++)
      values.Add(new HidKeyOption(i.ToString(), (byte)(0x1D + i)));

    values.Add(new HidKeyOption("0", 0x27));
    values.AddRange([new("Enter", 0x28), 
                     new("Escape", 0x29), 
                     new("Backspace", 0x2A),
                     new("Tab", 0x2B), 
                     new("Space", 0x2C),
                     new("Minus", 0x2D), 
                     new("Equals", 0x2E),
                     new("Left bracket", 0x2F), 
                     new("Right bracket", 0x30),
                     new("Backslash", 0x31), 
                     new("Semicolon", 0x33),
                     new("Apostrophe", 0x34), 
                     new("Grave", 0x35),
                     new("Comma", 0x36), 
                     new("Period", 0x37), 
                     new("Slash", 0x38),
                     new("Insert", 0x49),  
                     new("Home", 0x4A), 
                     new("Page Up", 0x4B),
                     new("Delete", 0x4C), 
                     new("End", 0x4D), 
                     new("Page Down", 0x4E),
                     new("Right Arrow", 0x4F), 
                     new("Left Arrow", 0x50),
                     new("Down Arrow", 0x51),
                     new("Up Arrow", 0x52),
                     new("Numpad /", 0x54),
                     new("Numpad *", 0x55),
                     new("Numpad -", 0x56),
                     new("Numpad +", 0x57),
                     new("Numpad Enter", 0x58),
                     new("Numpad .", 0x63),
                     new("Applications / Menu", 0x65)
    ]);
    for (int i = 1; i <= 12; i++)
      values.Add(new HidKeyOption($"F{i}", (byte)(0x39 + i)));
    for (int i = 13; i <= 24; i++)
      values.Add(new HidKeyOption($"F{i}", (byte)(0x68 + i - 13)));
    for (int i = 1; i <= 9; i++)
      values.Add(new HidKeyOption($"Numpad {i}", (byte)(0x58 + i)));
    values.Add(new HidKeyOption("Numpad 0", 0x62));
    return values;
  }
}

public static class StinkyModifiers
{
  public const byte SupportedMask = 0x77; // L/R Ctrl, Shift and Alt
  public static string Describe(byte mask)
  {
    if (mask == 0) return "None";
    string[] names = ["LCtrl", "LShift", "LAlt", "LWin", "RCtrl", "RShift", "RAlt", "RWin"];
    return string.Join(" + ", Enumerable.Range(0, 8).Where(bit => (mask & (1 << bit)) != 0).Select(bit => names[bit]));
  }
}

public static class StinkyOutputReportBuilder
{
  public static IReadOnlyList<byte[]> Build(FootboardConfiguration profile)
  {
    profile.Validate();
    var result = new List<byte[]>(12);
    foreach (var button in profile.Buttons.OrderBy(x => x.Number))
    {
      byte number = checked((byte)button.Number);
      byte[] mode = NewReport(0x04, number);
      mode[3] = (byte)button.Mode;
      ushort interval = checked((ushort)button.StoredIntervalMilliseconds);
      mode[5] = (byte)interval;
      mode[6] = (byte)(interval >> 8);
      result.Add(mode);

      byte[] key = NewReport(0x11, number);
      key[3] = button.KeyUsage;
      key[4] = button.ModifierMask;
      result.Add(key);

      byte[] persist = NewReport(0x0B, number);
      persist[3] = button.KeyUsage;
      result.Add(persist);
    }
    return result;
  }

  private static byte[] NewReport(byte opcode, byte number)
  {
    var report = new byte[65]; // Zero report ID at index zero
    report[1] = opcode;
    report[2] = number;
    return report;
  }
}
