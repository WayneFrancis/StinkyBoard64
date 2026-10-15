using StinkyFootboard.Core;
using System.Drawing;

namespace StinkyFootboard.MacroHost;

/// <summary>
/// Separate opt-in host macro runner. It neither enumerates the footboards
/// nor opens HID write handles. Users explicitly map marker keys using
/// the existing hardware editor before arming this Windows-only process.
/// </summary>
internal sealed class MacroHostForm : Form
{
    private HostMacroProfile _profile = new();
    private KeyboardMacroHook? _hook;
    private readonly ListView _macroList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false
    };
    private readonly Button _addTrigger = new() { Text = "Add trigger", AutoSize = true };
    private readonly Button _editTrigger = new() { Text = "Edit trigger", AutoSize = true };
    private readonly Button _editMacro = new() { Text = "Edit macro...", AutoSize = true };
    private readonly Button _removeTrigger = new() { Text = "Remove", AutoSize = true };
    private readonly TrackBar _speed = new()
    {
        Minimum = HostMacroTiming.MinimumSpeed,
        Maximum = HostMacroTiming.MaximumSpeed,
        Value = HostMacroTiming.DefaultSpeed,
        TickFrequency = 2,
        Width = 270,
        Height = 45,
        AutoSize = false
    };
    private readonly Label _speedDescription = new()
    {
        AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(10, 13, 0, 0)
    };
    private readonly Button _start = new()
    {
        Text = "Start intercepting", Width = 145, Height = 36
    };
    private readonly Button _stop = new()
    {
        Text = "Stop interception", Width = 135, Height = 36
    };
    private readonly Button _test = new()
    {
        Text = "Test output (3s)", Width = 145, Height = 36
    };
    private readonly Button _load = new() { Text = "Load JSON", Width = 85, Height = 36 };
    private readonly Button _save = new() { Text = "Save JSON", Width = 85, Height = 36 };
    private readonly Button _identify = new()
    {
        Text = "Identify next key", Width = 135, Height = 36
    };
    private readonly Button _hideToTray = new()
    {
        Text = "Hide to tray...", Width = 135, Height = 36
    };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Multiline = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Vertical,
        TabStop = false
    };
    private readonly CancellationTokenSource _closing = new();
    private bool _exiting;
    private bool _shownTrayHint;
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayRestore = new("Show MacroHost");
    private readonly ToolStripMenuItem _trayStop = new("Stop interception");
    private readonly ToolStripMenuItem _trayExit = new("Exit MacroHost");
    private readonly Label _status = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.DarkBlue,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public MacroHostForm()
    {
        Text = "Stinky Footboard — Windows Macro Host (experimental)";
        ClientSize = new Size(850, 790);
        MinimumSize = new Size(810, 710);
        StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "EXPERIMENTAL — NO FIRMWARE OR EEPROM WRITES\n" +
                "The physical footboard can still emit its assigned F5–F12 keys. " +
                "The Windows host can intercept any configured keyboard key + " +
                "exact left/right modifier combination, globally on ALL keyboards. " +
                "The final key is suppressed when matched; modifiers remain physical.\n" +
                "Macros can send Unicode text, keys, mouse actions, wheel or Beep. " +
                "Protected/raw-input applications may ignore generated inputs.",
            ForeColor = Color.DarkRed, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _macroList.Columns.Add("Trigger", 310);
        _macroList.Columns.Add("Macro body", 440);
        _macroList.Columns.Add("Binding ID", 100);
        var macroPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1
        };
        macroPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        macroPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        macroPanel.Controls.Add(_macroList, 0, 0);
        var bindingButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        bindingButtons.Controls.AddRange(
            [_addTrigger, _editTrigger, _editMacro, _removeTrigger]);
        macroPanel.Controls.Add(bindingButtons, 0, 1);
        root.Controls.Add(macroPanel, 0, 1);
        _addTrigger.Click += (_, _) => AddBinding();
        _editTrigger.Click += (_, _) => EditTrigger();
        _editMacro.Click += (_, _) => EditBinding();
        _removeTrigger.Click += (_, _) => RemoveBinding();
        _macroList.DoubleClick += (_, _) => EditBinding();

        var speedRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = false
        };
        speedRow.Controls.Add(new Label
        {
            Text = "Default press/click speed (slow → fast):",
            AutoSize = true, Margin = new Padding(0, 15, 3, 0)
        });
        speedRow.Controls.Add(_speed);
        speedRow.Controls.Add(_speedDescription);
        root.Controls.Add(speedRow, 0, 2);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.DarkSlateGray,
            Text = "Text macros: Unicode keystrokes without deliberate delays. " +
                "Step macros: keyboard key down/up/press, delays, all three mouse " +
                "buttons, scrolling and tone-adjustable Beep. Select a row to test. " +
                "Save/Load JSON edits the entire profile; a macro editor can export/import one macro.",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoScroll = false
        };
        actions.Controls.AddRange(
            [_load, _save, _start, _stop, _test, _identify, _hideToTray]);
        root.Controls.Add(actions, 0, 4);

        var diagnostics = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Hotkey diagnostics (only matched triggers and explicitly identified keys logged)"
        };
        diagnostics.Controls.Add(_log);
        root.Controls.Add(diagnostics, 0, 5);
        root.Controls.Add(_status, 0, 6);

        _load.Click += (_, _) => LoadProfile();
        _save.Click += (_, _) => SaveProfile();
        _start.Click += (_, _) => Start();
        _stop.Click += (_, _) => Stop();
        _test.Click += async (_, _) => await TestInjectionAsync();
        _identify.Click += (_, _) => IdentifyNextKey();
        _hideToTray.Click += (_, _) => ConfirmHideToTray();
        _speed.ValueChanged += (_, _) => UpdateSpeed();

        // The tray icon is only offered after an explicit Start. Minimize
        // keeps the macro hook alive and hides the window; Close/Exit always
        // stops it. This must never silently activate interception on startup.
        _tray.Icon = SystemIcons.Information;
        _tray.Text = "Stinky MacroHost — stopped";
        _tray.BalloonTipTitle = "Stinky MacroHost is running";
        _tray.BalloonTipIcon = ToolTipIcon.Info;
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.AddRange([_trayRestore, _trayStop, new ToolStripSeparator(), _trayExit]);
        _tray.ContextMenuStrip = trayMenu;
        _tray.Visible = false;
        _trayRestore.Click += (_, _) => RestoreFromTray();
        _trayStop.Click += (_, _) => Stop();
        _trayExit.Click += (_, _) => Close();
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        Resize += (_, _) =>
        {
            if (!_exiting && WindowState == FormWindowState.Minimized &&
                _hook is not null)
                ShowTrayHint();
        };
        FormClosing += (_, _) =>
        {
            _exiting = true;
            _closing.Cancel();
            Stop();
            _tray.Visible = false;
            _tray.Dispose();
        };

        ShowProfile();
        _stop.Enabled = false;
        _identify.Enabled = false;
        _hideToTray.Enabled = false;
        Status("Stopped. No keys intercepted. Test output first to isolate SendInput.");
    }

    private int? SelectedId => _macroList.SelectedItems.Count == 0
        ? null : (int)_macroList.SelectedItems[0].Tag!;

    private HostMacroBinding? SelectedBinding() => SelectedId is int id
        ? _profile.Bindings.FirstOrDefault(b => b.Slot == id) : null;

    private void RefreshBindings(int? selectedId = null)
    {
        selectedId ??= SelectedId;
        _macroList.BeginUpdate();
        _macroList.Items.Clear();
        foreach (var binding in _profile.Bindings.OrderBy(b => b.Slot))
        {
            string summary = !binding.IsConfigured ? "(pass through)" :
                binding.Mode == HostMacroMode.Text
                    ? $"Text ({binding.Text.Length} chars): " +
                        binding.Text.Replace("\r", "\\r").Replace("\n", "\\n")
                    : $"Sequence ({binding.Steps.Count} steps)";
            var item = new ListViewItem(binding.EffectiveTrigger.ToString())
            {
                Tag = binding.Slot
            };
            item.SubItems.Add(summary);
            item.SubItems.Add(binding.Slot.ToString());
            _macroList.Items.Add(item);
            if (binding.Slot == selectedId)
            {
                item.Selected = true;
                item.Focused = true;
            }
        }
        _macroList.EndUpdate();
    }

    private void AddBinding()
    {
        if (_hook is not null) return;
        using var editor = new HostTriggerEditor(new HostMacroTrigger { Key = 0x70 });
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        int newId = _profile.Bindings.Count == 0 ? 8 :
            Math.Max(8, _profile.Bindings.Max(b => b.Slot) + 1);
        var binding = new HostMacroBinding
        {
            Slot = newId, Trigger = editor.Result
        };
        _profile.Bindings.Add(binding);
        RefreshBindings(newId);
        EditBinding();
    }

    private void EditTrigger()
    {
        if (_hook is not null || SelectedBinding() is not { } binding) return;
        using var editor = new HostTriggerEditor(binding.EffectiveTrigger);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        var candidate = _profile.Copy();
        candidate.Bindings.Single(b => b.Slot == binding.Slot).Trigger = editor.Result;
        try { candidate.Validate(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Conflicting triggers",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        binding.Trigger = editor.Result;
        RefreshBindings(binding.Slot);
    }

    private void EditBinding()
    {
        if (_hook is not null || SelectedBinding() is not { } binding) return;
        using var editor = new MacroBindingEditor(binding,
            HostMacroTiming.DelayFromSpeed(_speed.Value));
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        var candidate = _profile.Copy();
        int index = candidate.Bindings.FindIndex(b => b.Slot == binding.Slot);
        candidate.Bindings[index] = editor.Result;
        try { candidate.Validate(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Conflicting triggers",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _profile.Bindings[_profile.Bindings.FindIndex(b => b.Slot == binding.Slot)] =
            editor.Result;
        RefreshBindings(binding.Slot);
    }

    private void RemoveBinding()
    {
        if (_hook is not null || SelectedBinding() is not { } binding) return;
        _profile.Bindings.Remove(binding);
        RefreshBindings();
    }

    private HostMacroProfile ReadFromUI()
    {
        var profile = _profile.Copy();
        profile.StepDelayMilliseconds = HostMacroTiming.DelayFromSpeed(_speed.Value);
        profile.Validate();
        return profile;
    }

    private void ShowProfile()
    {
        RefreshBindings();
        _speed.Value = HostMacroTiming.SpeedFromDelay(
            _profile.StepDelayMilliseconds);
        UpdateSpeed();
    }

    private void UpdateSpeed()
    {
        int delay = HostMacroTiming.DelayFromSpeed(_speed.Value);
        _speedDescription.Text = $"{delay} ms default hold; no text delay";
        // Updated speed takes effect on the next macro. Never tear down
        // or reinstall an active hook simply to adjust its timing.
        _hook?.SetStepDelay(delay);
    }

    private void Start()
    {
        if (_hook is not null) return;
        HostMacroProfile profile;
        try { profile = ReadFromUI(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid macro", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (profile.Bindings.All(b => !b.IsConfigured))
        {
            Status("Define at least one macro before starting.");
            return;
        }

        if (MessageBox.Show(this,
            "Enable system-wide interception of all configured keyboard shortcuts?\n\n" +
            "Shortcuts from any physical keyboard can trigger, not only " +
            "Stinky Footboards. Only the final trigger key is suppressed; " +
            "modifier keys are still passed through. Injected output can be " +
            "missed by protected or elevated applications. No footboard " +
            "firmware will be changed.\n\n" +
            "Click STOP to release all triggers and end interception.",
            "Enable Windows macro interception?", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        KeyboardMacroHook? installed = null;
        try
        {
            installed = new KeyboardMacroHook(profile);
            installed.StatusChanged += HookStatus;
            installed.Start();
            _hook = installed;
            _profile = profile;
            _addTrigger.Enabled = _editTrigger.Enabled =
                _editMacro.Enabled = _removeTrigger.Enabled = false;
            _load.Enabled = false;
            // Saving the existing text + current live timing is safe while
            // interception runs; loading/changing trigger strings is not.
            _save.Enabled = true;
            _start.Enabled = false;
            _stop.Enabled = true;
            _identify.Enabled = true;
            _hideToTray.Enabled = true;
            _shownTrayHint = false;
            UpdateTrayState();
            Log("Hook installed. Configured physical keyboard combos are monitored globally.");
            Status("ACTIVE: waiting for configured hotkeys. Test in Notepad first.");
        }
        catch (Exception ex)
        {
            installed?.Dispose();
            MessageBox.Show(this, ex.Message, "Cannot enable hook",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Stop();
        }
    }

    private void ShowTrayHint()
    {
        if (_hook is null || _shownTrayHint || _exiting) return;
        _shownTrayHint = true;
        _tray.Visible = true;
        _tray.BalloonTipText =
            "Macros remain active. Windows might put the blue information icon " +
            "under ^ (Show hidden icons) beside the clock. " +
            "MacroHost remains on the taskbar so you can restore it safely.";
        _tray.ShowBalloonTip(5000);
        Log("Minimized. Interception remains active. The notification icon " +
            "may be under Windows' ^ hidden-icons menu beside the clock. " +
            "The taskbar entry remains available to restore MacroHost.");
    }

    private void ConfirmHideToTray()
    {
        if (_hook is null) return;
        if (MessageBox.Show(this, "Windows 11 may put the MacroHost notification icon under " +
            "'^' (Show hidden icons) beside the clock.\n\n" +
            "Have you confirmed that you can find the blue information icon " +
            "and its Show MacroHost / Stop interception context menu?\n\n" +
            "Choosing Yes will remove the window from the taskbar. " +
            "If the tray icon isn't accessible, use Task Manager to exit " +
            "StinkyFootboard.MacroHost (which stops interception).\n\n" +
            "Hide the window completely?",
            "Hide MacroHost from taskbar?", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
            != DialogResult.Yes)
            return;

        _tray.Visible = true;
        ShowInTaskbar = false;
        Hide();
        Log("MacroHost is hidden in the Windows notification area. " +
            "Use the blue information icon under ^ to restore or stop.");
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        bool active = _hook is not null;
        _trayStop.Enabled = active;
        _tray.Text = active ? "Stinky MacroHost — intercepting shortcuts" :
            "Stinky MacroHost — stopped";
        // An active intercept must always have an available Stop/Exit
        // command, even if the main window has been hidden.
        _tray.Visible = active;
    }

    private void IdentifyNextKey()
    {
        if (_hook is null) return;
        try
        {
            _hook.IdentifyNextPhysicalKey();
            Log("IDENTIFY ARMED: press the footboard once. " +
                "Only the NEXT physical key-down will be reported; " +
                "no text or later keystrokes are recorded.");
        }
        catch (Exception ex) { Log("IDENTIFY ERROR: " + ex.Message); }
    }

    private void HookStatus(string status)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() => Log(status)));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void Log(string message)
    {
        if (_log.TextLength > 12000)
            _log.Clear();
        _log.AppendText($"{DateTime.Now:HH:mm:ss.fff} {message}" +
            Environment.NewLine);
        Status(message);
    }

    private void Status(string status) => _status.Text = status;

    private async Task TestInjectionAsync()
    {
        HostMacroProfile profile;
        try { profile = ReadFromUI(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid macro", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        HostMacroBinding? first = SelectedId is int selected
            ? profile.Bindings.FirstOrDefault(b => b.Slot == selected && b.IsConfigured)
            : null;
        first ??= profile.Bindings.FirstOrDefault(b => b.IsConfigured);
        if (first is null)
        {
            Status("Configure a text or step macro before testing.");
            return;
        }

        string summary = first.Mode == HostMacroMode.Text
            ? $"Unicode text ({first.Text.Length} characters)"
            : $"sequence ({first.Steps.Count} steps, possibly including mouse clicks)";
        if (MessageBox.Show(this,
            $"Test {first.EffectiveTrigger} {summary} without pressing the board?\n\n" +
            "The host will begin sending actions to the foreground window " +
            "THREE seconds after you click Yes. Open Notepad or another safe " +
            "test window. A step sequence may click or scroll under the mouse. " +
            "Nothing is written to board EEPROM.",
            "Isolated macro test", MessageBoxButtons.YesNo,
            MessageBoxIcon.Information, MessageBoxDefaultButton.Button2)
            != DialogResult.Yes)
            return;

        _test.Enabled = false;
        try
        {
            Log("TEST: " + KeyboardMacroHook.InputLayoutDescription);
            Log($"TEST: 3-second countdown to execute {first.EffectiveTrigger} {summary}...");
            await Task.Delay(TimeSpan.FromSeconds(3), _closing.Token);
            int delay = HostMacroTiming.DelayFromSpeed(_speed.Value);
            await HostMacroPlayback.PlayAsync(first, delay, _closing.Token);
            Log($"TEST: {summary} accepted by SendInput. Check the target application.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("TEST FAILED: " + ex.Message); }
        finally
        {
            if (!IsDisposed) _test.Enabled = true;
        }
    }

    private void Stop()
    {
        if (_hook is not null)
        {
            _hook.StatusChanged -= HookStatus;
            _hook.Dispose();
            _hook = null;
        }
        _addTrigger.Enabled = _editTrigger.Enabled =
            _editMacro.Enabled = _removeTrigger.Enabled = true;
        _load.Enabled = true;
        _save.Enabled = true;
        _start.Enabled = true;
        _stop.Enabled = false;
        _identify.Enabled = false;
        _hideToTray.Enabled = false;
        // Stopping from the tray restores the normal window so the user
        // knows interception is disabled and can edit the profile again.
        if (!Visible && !_exiting)
            RestoreFromTray();
        if (!_exiting)
            UpdateTrayState();
        Log("Stopped. All configured hotkeys now pass through normally.");
    }

    private void LoadProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Host macro profiles (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _profile = HostMacroProfile.LoadJson(dialog.FileName);
            ShowProfile();
            Status($"Loaded {Path.GetFileName(dialog.FileName)}. Not enabled.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProfile()
    {
        try
        {
            var profile = ReadFromUI();
            using var dialog = new SaveFileDialog
            {
                Filter = "Host macro profiles (*.json)|*.json",
                DefaultExt = "json", AddExtension = true, FileName = "StinkyHostMacros.json"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            profile.SaveJson(dialog.FileName);
            Status($"Saved {Path.GetFileName(dialog.FileName)}. No firmware was changed.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
