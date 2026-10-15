using StinkyFootboard.Core;
using System.Drawing;

namespace StinkyFootboard.MacroHost;

/// <summary>
/// Simple ordered step editor. Keyboard keys can be captured directly or
/// entered by Windows virtual-key value; no firmware HID usage restrictions.
/// </summary>
internal sealed class MacroBindingEditor : Form
{
    private readonly HostMacroBinding _binding;
    private readonly List<HostMacroStep> _steps;
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _text = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both,
        AcceptsReturn = true, AcceptsTab = true, WordWrap = false
    };
    private readonly Panel _stepsPanel = new() { Dock = DockStyle.Fill };
    private readonly ListBox _stepList = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly ComboBox _action = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 265 };
    private readonly NumericUpDown _virtualKey = new() { Minimum = 1, Maximum = 254, Width = 100, Value = 0x41 };
    private readonly TextBox _capture = new()
    {
        Width = 255, ReadOnly = true, Text = "Click here, then press any keyboard key"
    };
    private readonly NumericUpDown _duration = new()
    {
        Minimum = 0, Maximum = 3_600_000, Width = 125, ThousandsSeparator = true
    };
    private readonly NumericUpDown _notches = new() { Minimum = 1, Maximum = 100, Width = 100, Value = 1 };
    private readonly Label _keyLabel = new() { AutoSize = true, Text = "Keyboard key (VK)" };
    private readonly Label _durationLabel = new() { AutoSize = true };
    private readonly Label _wheelLabel = new() { AutoSize = true, Text = "Wheel notches" };
    private readonly Label _toneLabel = new() { AutoSize = true, Text = "Beep tone (Hz)" };
    private readonly NumericUpDown _tone = new()
    {
        Minimum = 37, Maximum = 32767, Value = 880, Width = 140
    };
    private readonly Label _keyName = new() { AutoSize = true };
    private readonly Button _add = new() { Text = "Add step", AutoSize = true };
    private readonly Button _apply = new() { Text = "Apply change", AutoSize = true };
    private readonly Button _remove = new() { Text = "Delete", AutoSize = true };
    private readonly Button _up = new() { Text = "Move up", AutoSize = true };
    private readonly Button _down = new() { Text = "Move down", AutoSize = true };

    public HostMacroBinding Result => _binding;

    public MacroBindingEditor(HostMacroBinding original, int defaultHold)
    {
        _binding = original.Copy();
        _steps = _binding.Steps.Select(s => s.Copy()).ToList();
        Text = $"Edit {_binding.EffectiveTrigger} macro";
        ClientSize = new Size(970, 650);
        MinimumSize = new Size(820, 510);
        StartPosition = FormStartPosition.CenterParent;
        _mode.Items.AddRange(["Text — no inserted delays", "Steps — keyboard / mouse / wheel"]);
        _mode.SelectedIndex = (int)_binding.Mode;
        _action.Items.AddRange(Enum.GetNames<HostMacroAction>());
        _action.SelectedIndex = 0;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        header.Controls.Add(new Label
        {
            Text = $"F{_binding.Slot + 5} macro type:", AutoSize = true,
            Margin = new Padding(0, 10, 10, 0)
        });
        header.Controls.Add(_mode);
        header.Controls.Add(new Label
        {
            Text = $"    Global slider currently supplies {defaultHold} ms " +
                "for press/click steps with duration 0. Text has no added delays.",
            AutoSize = true, Margin = new Padding(0, 10, 0, 0)
        });
        root.Controls.Add(header, 0, 0);
        _text.Text = _binding.Text;
        root.Controls.Add(_text, 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            Size = new Size(900, 460), SplitterDistance = 520, Panel1MinSize = 220, Panel2MinSize = 240
        };
        _stepsPanel.Controls.Add(split);
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        split.Panel1.Controls.Add(left);
        left.Controls.Add(_stepList, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([_add, _apply, _remove, _up, _down]);
        left.Controls.Add(buttons, 0, 1);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12)
        };
        split.Panel2.Controls.Add(right);
        right.Controls.Add(new Label { Text = "Action", AutoSize = true });
        right.Controls.Add(_action);
        right.Controls.Add(_keyLabel);
        right.Controls.Add(_virtualKey);
        right.Controls.Add(_keyName);
        right.Controls.Add(_capture);
        right.Controls.Add(_durationLabel);
        right.Controls.Add(_duration);
        right.Controls.Add(_wheelLabel);
        right.Controls.Add(_notches);
        right.Controls.Add(_toneLabel);
        right.Controls.Add(_tone);
        right.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(350, 0),
            Text = "Use KeyDown / KeyUp steps for held modifiers " +
                "(Ctrl, Shift, Alt) or other keys. Any outstanding down " +
                "events are safely released at the end of a macro."
        });

        root.Controls.Add(_stepsPanel, 0, 1);
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft
        };
        var ok = new Button { Text = "Save macro", Width = 125, Height = 34 };
        var cancel = new Button
        {
            Text = "Cancel", Width = 105, Height = 34,
            DialogResult = DialogResult.Cancel
        };
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);
        var export = new Button { Text = "Save macro JSON...", Width = 147, Height = 34 };
        var import = new Button { Text = "Load macro JSON...", Width = 147, Height = 34 };
        footer.Controls.Add(export);
        footer.Controls.Add(import);
        export.Click += (_, _) => SaveMacroFile();
        import.Click += (_, _) => LoadMacroFile();
        root.Controls.Add(footer, 0, 2);
        AcceptButton = ok;
        CancelButton = cancel;

        _mode.SelectedIndexChanged += (_, _) => UpdateMode();
        _action.SelectedIndexChanged += (_, _) => UpdateAction();
        _virtualKey.ValueChanged += (_, _) => UpdateKeyName();
        // Make Tab, arrows and other dialog keys capturable by this field.
        _capture.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        _capture.KeyDown += (_, e) =>
        {
            int key = (int)e.KeyCode & 0xff;
            if (key is >= 1 and <= 254) _virtualKey.Value = key;
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        _stepList.SelectedIndexChanged += (_, _) => SelectStep();
        _add.Click += (_, _) => AddStep();
        _apply.Click += (_, _) => ApplyStep();
        _remove.Click += (_, _) => RemoveStep();
        _up.Click += (_, _) => MoveStep(-1);
        _down.Click += (_, _) => MoveStep(1);
        ok.Click += (_, _) =>
        {
            try
            {
                if (_mode.SelectedIndex == (int)HostMacroMode.Steps &&
                    _stepList.SelectedIndex >= 0)
                    ApplyStep();
                _binding.Mode = (HostMacroMode)_mode.SelectedIndex;
                _binding.Text = _text.Text;
                _binding.Steps = _steps.Select(s => s.Copy()).ToList();
                _binding.Validate();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid macro",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        UpdateMode();
        UpdateAction();
        UpdateKeyName();
        RefreshSteps(0);
    }

    private void UpdateMode()
    {
        bool isSteps = _mode.SelectedIndex == (int)HostMacroMode.Steps;
        _text.Visible = !isSteps;
        _stepsPanel.Visible = isSteps;
    }

    private HostMacroAction CurrentAction =>
        Enum.Parse<HostMacroAction>((string)_action.SelectedItem!);

    private void UpdateAction()
    {
        if (_action.SelectedItem is null) return;
        var sample = new HostMacroStep { Action = CurrentAction };
        _keyLabel.Visible = sample.UsesKey;
        _virtualKey.Visible = sample.UsesKey;
        _keyName.Visible = sample.UsesKey;
        _capture.Visible = sample.UsesKey;
        _durationLabel.Visible = sample.UsesDuration;
        _duration.Visible = sample.UsesDuration;
        _wheelLabel.Visible = sample.UsesWheel;
        _notches.Visible = sample.UsesWheel;
        _toneLabel.Visible = sample.UsesBeep;
        _tone.Visible = sample.UsesBeep;
        _durationLabel.Visible = sample.UsesDuration || sample.UsesBeep;
        _duration.Visible = sample.UsesDuration || sample.UsesBeep;
        _duration.Maximum = CurrentAction == HostMacroAction.Delay
            ? 3_600_000 : CurrentAction == HostMacroAction.Beep ? 5_000 : 60_000;
        if (_duration.Value > _duration.Maximum) _duration.Value = _duration.Maximum;
        _durationLabel.Text = CurrentAction == HostMacroAction.Delay
            ? "Delay before next step (ms)"
            : CurrentAction == HostMacroAction.Beep
                ? "Beep length (ms; 0 = 120)"
                : "Hold duration (ms; 0 = live speed slider)";
    }

    private void UpdateKeyName()
    {
        int vk = (int)_virtualKey.Value;
        _keyName.Text = $"Key: {(Keys)vk} (VK 0x{vk:X2})";
    }

    private HostMacroStep ReadStep()
    {
        var step = new HostMacroStep
        {
            Action = CurrentAction,
            Key = (int)_virtualKey.Value,
            Milliseconds = (int)_duration.Value,
            WheelNotches = (int)_notches.Value,
            FrequencyHz = (int)_tone.Value
        };
        step.Validate();
        return step;
    }

    private void RefreshSteps(int selected)
    {
        _stepList.BeginUpdate();
        _stepList.Items.Clear();
        for (int i = 0; i < _steps.Count; i++)
            _stepList.Items.Add($"{i + 1,3}. {Describe(_steps[i])}");
        _stepList.EndUpdate();
        if (_steps.Count > 0)
            _stepList.SelectedIndex = Math.Clamp(selected, 0, _steps.Count - 1);
        _apply.Enabled = _remove.Enabled = _up.Enabled = _down.Enabled =
            _stepList.SelectedIndex >= 0;
    }

    private static string Describe(HostMacroStep s)
    {
        string name = s.Action.ToString();
        if (s.UsesKey) name += $" {(Keys)s.Key} [VK 0x{s.Key:X2}]";
        if (s.UsesDuration)
            name += s.Action == HostMacroAction.Delay
                ? $" — {s.Milliseconds} ms"
                : $" — {(s.Milliseconds == 0 ? "slider default" :
                    s.Milliseconds + " ms")}";
        if (s.UsesWheel) name += $" — {s.WheelNotches} notch(es)";
        if (s.UsesBeep) name += $" — {s.FrequencyHz} Hz, {(s.Milliseconds == 0 ? 120 : s.Milliseconds)} ms";
        return name;
    }

    private void SelectStep()
    {
        int index = _stepList.SelectedIndex;
        bool hasStep = index >= 0;
        _apply.Enabled = _remove.Enabled = hasStep;
        _up.Enabled = hasStep && index > 0;
        _down.Enabled = hasStep && index < _steps.Count - 1;
        if (!hasStep) return;
        var step = _steps[index];
        _action.SelectedItem = step.Action.ToString();
        _virtualKey.Value = Math.Clamp(step.Key, 1, 254);
        _duration.Value = Math.Min(_duration.Maximum,
            Math.Max(_duration.Minimum, step.Milliseconds));
        _notches.Value = Math.Clamp(step.WheelNotches, 1, 100);
        _tone.Value = Math.Clamp(step.FrequencyHz, 37, 32767);
    }

    private void AddStep()
    {
        try
        {
            var step = ReadStep();
            int index = _stepList.SelectedIndex + 1;
            _steps.Insert(index, step);
            RefreshSteps(index);
        }
        catch (Exception ex) { ShowStepError(ex); }
    }

    private void ApplyStep()
    {
        int index = _stepList.SelectedIndex;
        if (index < 0) return;
        _steps[index] = ReadStep();
        RefreshSteps(index);
    }

    private void RemoveStep()
    {
        int index = _stepList.SelectedIndex;
        if (index < 0) return;
        _steps.RemoveAt(index);
        RefreshSteps(Math.Max(0, index - 1));
    }

    private void MoveStep(int delta)
    {
        int index = _stepList.SelectedIndex;
        int next = index + delta;
        if (index < 0 || next < 0 || next >= _steps.Count) return;
        (_steps[index], _steps[next]) = (_steps[next], _steps[index]);
        RefreshSteps(next);
    }

    private HostMacroBinding SnapshotMacro()
    {
        if (_mode.SelectedIndex == (int)HostMacroMode.Steps &&
            _stepList.SelectedIndex >= 0)
            ApplyStep();
        var b = _binding.Copy();
        b.Mode = (HostMacroMode)_mode.SelectedIndex;
        b.Text = _text.Text;
        b.Steps = _steps.Select(s => s.Copy()).ToList();
        b.Validate();
        return b;
    }

    private void SaveMacroFile()
    {
        try
        {
            var document = HostMacroDocument.FromBinding(SnapshotMacro());
            using var dialog = new SaveFileDialog
            {
                Filter = "Standalone macro JSON (*.json)|*.json",
                DefaultExt = "json", AddExtension = true,
                FileName = "StinkyMacro.json"
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                document.SaveJson(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot save macro",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadMacroFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Standalone macro JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var document = HostMacroDocument.LoadJson(dialog.FileName);
            _steps.Clear();
            _steps.AddRange(document.Steps.Select(s => s.Copy()));
            _text.Text = document.Text;
            _mode.SelectedIndex = (int)document.Mode;
            RefreshSteps(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot load macro",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowStepError(Exception ex) =>
        MessageBox.Show(this, ex.Message, "Invalid step", MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
}
