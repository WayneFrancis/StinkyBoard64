using System.Text.Json;

namespace StinkyFootboard.Core;

/// <summary>
/// Portable macro *body*, independent of a key binding; a saved macro can be
/// imported into any trigger without silently overwriting that trigger.
/// </summary>
public sealed class HostMacroDocument
{
    public string Format { get; set; } = "StinkyFootboard.HostMacro";
    public int Version { get; set; } = 1;
    public HostMacroMode Mode { get; set; }
    public string Text { get; set; } = "";
    public List<HostMacroStep> Steps { get; set; } = [];

    public static HostMacroDocument FromBinding(HostMacroBinding binding)
    {
        var doc = new HostMacroDocument
        {
            Mode = binding.Mode, Text = binding.Text,
            Steps = binding.Steps.Select(s => s.Copy()).ToList()
        };
        doc.Validate();
        return doc;
    }

    public void ApplyTo(HostMacroBinding binding)
    {
        Validate();
        binding.Mode = Mode;
        binding.Text = Text;
        binding.Steps = Steps.Select(s => s.Copy()).ToList();
    }

    public void Validate()
    {
        if (Format != "StinkyFootboard.HostMacro" || Version != 1)
            throw new InvalidDataException("Not a supported standalone host macro JSON file.");
        new HostMacroBinding { Mode = Mode, Text = Text, Steps = Steps }.Validate();
    }

    public void SaveJson(string path)
    {
        Validate();
        File.WriteAllText(path, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public static HostMacroDocument LoadJson(string path)
    {
        var result = JsonSerializer.Deserialize<HostMacroDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Standalone macro file is empty.");
        result.Validate();
        return result;
    }
}
