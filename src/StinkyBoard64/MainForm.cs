using StinkyFootboard.Core;
using StinkyFootboard.Diagnostics;
using System.Diagnostics;
using System.Drawing;

namespace StinkyFootboard.Editor;

internal sealed class MainForm : Form
{
  private IReadOnlyList<HidDescriptor> _boards = [];
  private readonly Dictionary<string, FootboardConfiguration> _drafts = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, string> _origins = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _readInSession = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _edited = new(StringComparer.OrdinalIgnoreCase);
  private bool _busy;
  private bool _loading;

  private readonly ComboBox _device = new()
  {
    DropDownStyle = ComboBoxStyle.DropDownList,
    Width = 565
  };
  private readonly Button _refresh = new() { Text = "Refresh / read all", AutoSize = true };
  private readonly Button _read = new() { Text = "Read selected", AutoSize = true };
  private readonly Button _load = new() { Text = "Load JSON", AutoSize = true };
  private readonly Button _save = new() { Text = "Save JSON", AutoSize = true };
  private readonly Button _macroHost = new() { Text = "Open Macro Host", AutoSize = true };
  private readonly Button _write = new()
  {
    Text = "Write to selected board",
    AutoSize = true,
    BackColor = Color.LightGoldenrodYellow
  };
  private readonly Label _source = new()
  {
    Dock = DockStyle.Fill,
    TextAlign = ContentAlignment.MiddleLeft
  };
  private readonly Label _status = new()
  {
    Dock = DockStyle.Fill,
    ForeColor = Color.DarkBlue,
    TextAlign = ContentAlignment.MiddleLeft
  };
  private readonly List<ComboBox> _keys = [];
  private readonly List<Label> _modifiers = [];
  private readonly List<Label> _modes = [];
  private readonly List<Label> _intervals = [];

  private sealed record BoardChoice(StinkyBoardIdentity Identity, string Display)
  {
    public override string ToString() => Display;
  }

  private BoardChoice? Selected => _device.SelectedItem as BoardChoice;

  public MainForm()
  {
    Text = "StinkyBoard64 — Key Configurator";
    StartPosition = FormStartPosition.CenterScreen;
    ClientSize = new Size(940, 475);
    MinimumSize = new Size(830, 445);
    AutoScaleMode = AutoScaleMode.Font;

    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 6,
      Padding = new Padding(12)
    };
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
    Controls.Add(root);

    var selectRow = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    selectRow.Controls.Add(new Label
    {
      AutoSize = true,
      Text = "Connected board:",
      Margin = new Padding(3, 9, 5, 0)
    });
    selectRow.Controls.Add(_device);
    selectRow.Controls.Add(_refresh);
    root.Controls.Add(selectRow, 0, 0);

    var actionRow = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    actionRow.Controls.AddRange([_read, _load, _save, _write, _macroHost]);
    root.Controls.Add(actionRow, 0, 1);

    var grid = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 5,
      RowCount = 5,
      CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
    };
    foreach (var percentage in new[] { 12, 29, 25, 18, 16 })
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percentage));
    grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
    for (int i = 0; i < 4; i++)
      grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

    string[] columns =
    [
        "Direction", "Key", "Modifiers (retained)",
            "Mode (retained)", "Stored interval"
    ];
    for (int i = 0; i < columns.Length; i++)
      grid.Controls.Add(new Label
      {
        Dock = DockStyle.Fill,
        Text = columns[i],
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(7, 2, 0, 2),
        Font = new Font(Font, FontStyle.Bold)
      }, i, 0);

    for (int i = 0; i < 4; i++)
    {
      int number = i + 1;
      grid.Controls.Add(new Label
      {
        Text = $"Button {number}",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(7, 2, 0, 2)
      }, 0, number);
      var key = new ComboBox
      {
        Dock = DockStyle.Top,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(7, 6, 7, 3)
      };
      _keys.Add(key);
      key.SelectedIndexChanged += (_, _) => KeyChanged(number);
      grid.Controls.Add(key, 1, number);
      Label ReadOnlyLabel() => new()
      {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(7, 2, 0, 2)
      };
      var mods = ReadOnlyLabel();
      var mode = ReadOnlyLabel();
      var interval = ReadOnlyLabel();
      _modifiers.Add(mods);
      _modes.Add(mode);
      _intervals.Add(interval);
      grid.Controls.Add(mods, 2, number);
      grid.Controls.Add(mode, 3, number);
      grid.Controls.Add(interval, 4, number);
    }
    root.Controls.Add(grid, 0, 2);
    root.Controls.Add(_source, 0, 3);
    root.Controls.Add(new Label
    {
      Dock = DockStyle.Fill,
      TextAlign = ContentAlignment.MiddleLeft,
      ForeColor = Color.DarkRed,
      Text = "USE AT YOUR OWN RISK. A write replaces ALL FOUR EEPROM " +
               "key/sequence pages on the selected board, even if only one key " +
               "changed. Stored macros and alternate-bank data may be erased. " +
               "Close original Stinky.exe. Save each board to JSON first. " +
               "Write requires Yes; No is the default."
    }, 0, 4);
    root.Controls.Add(_status, 0, 5);

    _device.SelectedIndexChanged += (_, _) => ShowSelected();
    _refresh.Click += async (_, _) => await RefreshAllAsync();
    _read.Click += async (_, _) => await ReadSelectedAsync();
    _load.Click += (_, _) => LoadJson();
    _save.Click += (_, _) => SaveJson();
    _write.Click += async (_, _) => await WriteSelectedAsync();
    _macroHost.Click += (_, _) => OpenMacroHost();

    ShowSelected();
    Shown += async (_, _) => await RefreshAllAsync();
  }

  private void OpenMacroHost()
  {
    // The Macro Host is an independent, opt-in process; opening its UI
    // NEVER starts interception automatically or changes board EEPROM.
    // In a source checkout always build/run from source so a previous
    // bin/Debug executable cannot shadow the code just pulled.
    string sourceProject = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "StinkyBoard64.MacroHost", "StinkyBoard64.MacroHost.csproj"));

    if (File.Exists(sourceProject))
    {
      try
      {
        var info = new ProcessStartInfo("dotnet")
        {
          WorkingDirectory = Path.GetDirectoryName(sourceProject)!,
          UseShellExecute = true
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--project");
        info.ArgumentList.Add(sourceProject);
        Process.Start(info);
      }
      catch (Exception ex)
      {
        MessageBox.Show(this,
            "Could not start the Macro Host project:\n\n" + ex.Message +
            "\n\nFrom the repository directory try:\n" +
            "dotnet run --project src/StinkyBoard64.MacroHost",
            "Macro Host launch failed", MessageBoxButtons.OK,
            MessageBoxIcon.Error);
      }
      return;
    }

    // Portable release layout: Editor/StinkyBoard64.exe and
    // MacroHost/StinkyBoard64.MacroHost.exe, with no SDK required.
    string? hostExe = new[]
    {
            Path.Combine(AppContext.BaseDirectory, "StinkyBoard64.MacroHost.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "MacroHost", "StinkyBoard64.MacroHost.exe"))
        }.FirstOrDefault(File.Exists);

    if (hostExe is null)
    {
      MessageBox.Show(this,
          "Macro Host wasn't found beside the editor. Use the complete " +
          "portable package or run from the source checkout:\n\n" +
          "dotnet run --project src/StinkyBoard64.MacroHost",
          "Macro Host not installed", MessageBoxButtons.OK,
          MessageBoxIcon.Information);
      return;
    }

    try
    {
      Process.Start(new ProcessStartInfo(hostExe)
      {
        WorkingDirectory = Path.GetDirectoryName(hostExe)!,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Could not open Macro Host",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private static bool OriginalRunning() =>
      Process.GetProcessesByName("Stinky").Length > 0;

  private void Status(string value) => _status.Text = value;

  private void Busy(bool value)
  {
    _busy = value;
    _refresh.Enabled = !value;
    _device.Enabled = !value && _boards.Count > 0;
    RefreshButtons();
  }

  private void RefreshButtons()
  {
    var selected = Selected;
    bool profile = selected is not null && _drafts.ContainsKey(selected.Identity.Path);
    _read.Enabled = !_busy && selected is not null;
    _load.Enabled = !_busy && selected is not null;
    _save.Enabled = !_busy && profile;
    bool identitiesUnique = _boards.Count == 1 ||
        (_boards.All(x => x.ContainerId.HasValue) &&
         _boards.Select(x => x.ContainerId).Distinct().Count() == _boards.Count);
    _write.Enabled = !_busy && profile && identitiesUnique &&
        _boards.Count is >= 1 and <= 2 &&
        selected is not null && _readInSession.Contains(selected.Identity.Path);
    foreach (var combo in _keys) combo.Enabled = !_busy && profile;
  }

  private void ShowSelected()
  {
    var selected = Selected;
    _loading = true;
    try
    {
      if (selected is not null &&
          _drafts.TryGetValue(selected.Identity.Path, out var draft))
      {
        for (int i = 0; i < 4; i++)
        {
          var setting = draft.Buttons.Single(x => x.Number == i + 1);
          var choices = BasicHidKeys.Options.ToList();
          if (!choices.Any(x => x.Usage == setting.KeyUsage))
            choices.Add(new HidKeyOption(
                $"Unknown stored HID 0x{setting.KeyUsage:X2}", setting.KeyUsage));
          _keys[i].DataSource = choices;
          _keys[i].SelectedItem = choices.Single(x => x.Usage == setting.KeyUsage);
          _modifiers[i].Text = StinkyModifiers.Describe(setting.ModifierMask);
          _modes[i].Text = setting.Mode.ToString();
          _intervals[i].Text = $"{setting.StoredIntervalMilliseconds} ms";
        }
        _source.Text = selected.Display + " — " +
            _origins.GetValueOrDefault(selected.Identity.Path, "Draft") +
            (_edited.Contains(selected.Identity.Path) ? " (edited; not written)" : "") +
            (draft.HasExtendedData
                ? "   WARNING: existing sequence data may be erased on write."
                : "");
      }
      else
      {
        foreach (var combo in _keys) combo.DataSource = null;
        foreach (var label in _modifiers.Concat(_modes).Concat(_intervals))
          label.Text = "—";
        _source.Text = selected is null
            ? "No supported Stinky Footboard detected."
            : "No settings read for this board. Click Read selected.";
      }
    }
    finally { _loading = false; }
    RefreshButtons();
  }

  private void KeyChanged(int number)
  {
    if (_loading || _busy || Selected is not { } selected ||
        _keys[number - 1].SelectedItem is not HidKeyOption key ||
        !_drafts.TryGetValue(selected.Identity.Path, out var draft))
      return;
    var setting = draft.Buttons.Single(x => x.Number == number);
    if (setting.KeyUsage == key.Usage) return;
    setting.KeyUsage = key.Usage;
    _edited.Add(selected.Identity.Path);
    _origins[selected.Identity.Path] = "Local key edit";
    ShowSelected();
  }

  private async Task RefreshAllAsync()
  {
    if (_busy) return;
    if (_edited.Count != 0 &&
        MessageBox.Show(this, "Refreshing discards unsaved key edits. Continue?",
            "Discard local edits?", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
      return;
    if (OriginalRunning())
    {
      Status("Close original Stinky.exe before reading the footboards.");
      return;
    }

    Busy(true);
    try
    {
      var discovered = HidDiscovery.Enumerate()
          .Where(ExperimentalDeviceWriter.IsSupportedBoard)
          .OrderBy(x => x.ContainerId).ThenBy(x => x.Path).ToArray();
      if (discovered.Length > 2)
        throw new InvalidOperationException("This app supports up to two boards.");
      _boards = discovered;
      _drafts.Clear();
      _origins.Clear();
      _edited.Clear();
      _readInSession.Clear();
      var choices = discovered.Select((x, i) =>
          new BoardChoice(new StinkyBoardIdentity(x.Path, x.ContainerId),
              $"Board {i + 1} · Container {x.ContainerId?.ToString() ?? "unknown"}"))
          .ToList();
      _loading = true;
      _device.DataSource = choices;
      _loading = false;
      Status($"Found {discovered.Length} footboard(s). Reading...");
      int succeeded = 0;
      var errors = new List<string>();
      foreach (var choice in choices)
      {
        try
        {
          var config = await ExperimentalDeviceReader.ReadAsync(choice.Identity);
          _drafts[choice.Identity.Path] = config;
          _origins[choice.Identity.Path] = "Read from hardware";
          _readInSession.Add(choice.Identity.Path);
          succeeded++;
        }
        catch (Exception ex)
        {
          errors.Add($"{choice.Display}: {ex.Message}");
        }
      }
      Status($"Read {succeeded} of {discovered.Length} footboard(s)." +
          (errors.Count == 0 ? "" : " Some reads failed."));
      if (errors.Count > 0)
        MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, errors),
            "Board read incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    catch (Exception ex)
    {
      Status("Could not refresh connected boards.");
      MessageBox.Show(this, ex.Message, "Refresh failed",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      Busy(false);
      ShowSelected();
    }
  }

  private async Task ReadSelectedAsync()
  {
    if (_busy || Selected is not { } selected) return;
    if (OriginalRunning())
    {
      MessageBox.Show(this, "Close original Stinky.exe.", "Read unavailable",
          MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }
    if (_edited.Contains(selected.Identity.Path) &&
        MessageBox.Show(this, "Discard your local changes and read the board?",
            "Discard local edits?", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
      return;

    Busy(true);
    try
    {
      var config = await ExperimentalDeviceReader.ReadAsync(selected.Identity);
      _drafts[selected.Identity.Path] = config;
      _origins[selected.Identity.Path] = "Read from hardware";
      _readInSession.Add(selected.Identity.Path);
      _edited.Remove(selected.Identity.Path);
      Status("Selected board read successfully.");
    }
    catch (Exception ex)
    {
      _readInSession.Remove(selected.Identity.Path);
      Status("Read failed.");
      MessageBox.Show(this, ex.Message, "Read failed",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      Busy(false);
      ShowSelected();
    }
  }

  private void SaveJson()
  {
    if (_busy || Selected is not { } selected ||
        !_drafts.TryGetValue(selected.Identity.Path, out var config)) return;
    using var dialog = new SaveFileDialog
    {
      Filter = "JSON profiles (*.json)|*.json",
      DefaultExt = "json",
      AddExtension = true,
      FileName = $"StinkyBoard64-{selected.Display.Split(' ')[1]}.json"
    };
    if (dialog.ShowDialog(this) != DialogResult.OK) return;
    try
    {
      config.SaveJson(dialog.FileName);
      Status($"Saved JSON: {Path.GetFileName(dialog.FileName)}");
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Save JSON failed",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void LoadJson()
  {
    if (_busy || Selected is not { } selected) return;
    using var dialog = new OpenFileDialog
    {
      Filter = "JSON profiles (*.json)|*.json",
      CheckFileExists = true
    };
    if (dialog.ShowDialog(this) != DialogResult.OK) return;
    try
    {
      var config = FootboardConfiguration.LoadJson(dialog.FileName);
      // Loading never changes the device or establishes a trusted read.
      _drafts[selected.Identity.Path] = config;
      _origins[selected.Identity.Path] =
          $"Loaded JSON: {Path.GetFileName(dialog.FileName)}";
      _edited.Add(selected.Identity.Path);
      Status("JSON loaded into the selected local draft. Nothing written.");
      ShowSelected();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Invalid JSON profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private async Task WriteSelectedAsync()
  {
    if (_busy || 
        Selected is not { } selected ||
        !_readInSession.Contains(selected.Identity.Path) ||
        !_drafts.TryGetValue(selected.Identity.Path, out var draft)) 
      return;
    if (OriginalRunning())
    {
      MessageBox.Show(this, "Close original Stinky.exe before writing.", "Write unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    var desired = draft.Copy();
    IReadOnlyList<byte[]> reports;
    try
    {
      reports = StinkyOutputReportBuilder.Build(desired);
      ExperimentalDeviceWriter.ValidateReportSequence(reports);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Invalid configuration",
          MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    string extra = desired.HasExtendedData ? "\n\nExtra sequence data was detected and WILL be erased." : "";
    var result = MessageBox.Show(this,
        $"Write four key assignments to {selected.Display}?\n\n" +
        "This permanently replaces ALL FOUR EEPROM key/sequence pages, " +
        "not only the keys you edited. Existing macros and alternate-bank " +
        "settings will be lost. A failed write may leave the configuration " +
        "incomplete. You accept the risk of this operation." + extra +
        "\n\nProceed with the write?",
        "Confirm write — default NO", MessageBoxButtons.YesNo,
        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
    if (result != DialogResult.Yes)
    {
      Status("Write cancelled; no HID configuration reports sent.");
      return;
    }

    StinkyBoardIdentity target = selected.Identity;
    StinkyBoardIdentity[] topology = _boards
        .Select(b => new StinkyBoardIdentity(b.Path, b.ContainerId)).ToArray();
    Busy(true);
    try
    {
      Status("Writing the selected footboard...");
      await Task.Run(() => ExperimentalDeviceWriter.Apply(target, topology, reports));
      Status("Write accepted. Checking hardware readback...");
      try
      {
        StinkyBoardIdentityGuard.RequireUnchangedWriteTarget(target, 
                                                             topology, 
                                                             HidDiscovery.Enumerate().Where(ExperimentalDeviceWriter.IsSupportedBoard).Select(b => new StinkyBoardIdentity(b.Path, b.ContainerId)).ToArray());
        var actual = await ExperimentalDeviceReader.ReadAsync(target);
        string check = StinkyWriteVerification.Compare(reports, actual);
        if (check.StartsWith("VERIFIED:", StringComparison.Ordinal))
        {
          _drafts[target.Path] = actual;
          _edited.Remove(target.Path);
          _origins[target.Path] = "Written and read back from hardware";
          Status(check);
        }
        else
        {
          _readInSession.Remove(target.Path);
          _origins[target.Path] = "Write not verified — read again before writing";
          Status(check);
          MessageBox.Show(this, check, "Write readback mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
      }
      catch (Exception ex)
      {
        _readInSession.Remove(target.Path);
        _origins[target.Path] = "Write not verified — read again before writing";
        Status("Write accepted, but readback verification was unavailable.");
        MessageBox.Show(this, ex.Message + "\n\nNo automatic retry was attempted. Check the footboard.", "Readback unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }
    catch (Exception ex)
    {
      _readInSession.Remove(target.Path);
      _origins[target.Path] = "Write failed — read again before writing";
      Status("Write failed or incomplete; do not immediately retry.");
      MessageBox.Show(this, ex.Message + "\n\nIf necessary, disconnect/reconnect " +
          "the board once and use the original utility for recovery.",
          "Write failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      Busy(false);
      ShowSelected();
    }
  }
}
