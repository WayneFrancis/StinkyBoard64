using StinkyFootboard.Core;
using System.Drawing;

namespace StinkyFootboard.MacroHost;

internal sealed class HostTriggerEditor : Form
{
    private readonly NumericUpDown _key = new()
    {
        Minimum = 1, Maximum = 254, Value = 0x74, Width = 110
    };
    private readonly TextBox _capture = new()
    {
        ReadOnly = true, Width = 270,
        Text = "Focus here, then press your trigger key"
    };
    private readonly ComboBox _ctrl = Selector();
    private readonly ComboBox _alt = Selector();
    private readonly ComboBox _shift = Selector();
    private readonly ComboBox _win = Selector();
    private readonly Label _preview = new() { AutoSize = true };
    public HostMacroTrigger Result { get; private set; }

    private static ComboBox Selector()
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        c.Items.AddRange(["None", "Either", "Left", "Right", "Both"]);
        c.SelectedIndex = 0;
        return c;
    }

    public HostTriggerEditor(HostMacroTrigger existing)
    {
        Result = existing.Copy();
        Text = "Configure keyboard trigger";
        ClientSize = new Size(550, 435);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(15),
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);
        void Line(string caption, Control control)
        {
            int row = root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.Controls.Add(new Label
            {
                Text = caption, AutoSize = true, Margin = new Padding(2, 12, 0, 0)
            }, 0, row);
            root.Controls.Add(control, 1, row);
        }
        Line("Trigger key (Windows VK)", _key);
        Line("Capture key", _capture);
        Line("Ctrl requirement", _ctrl);
        Line("Alt requirement", _alt);
        Line("Shift requirement", _shift);
        Line("Windows-key requirement", _win);
        Line("Preview", _preview);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save trigger", Width = 140, Height = 34 };
        var cancel = new Button
        {
            Text = "Cancel", Width = 100, Height = 34,
            DialogResult = DialogResult.Cancel
        };
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        Line("", footer);
        AcceptButton = save;
        CancelButton = cancel;

        _key.Value = Math.Clamp(existing.Key, 1, 254);
        _ctrl.SelectedIndex = (int)existing.Ctrl;
        _alt.SelectedIndex = (int)existing.Alt;
        _shift.SelectedIndex = (int)existing.Shift;
        _win.SelectedIndex = (int)existing.Win;
        _key.ValueChanged += (_, _) => RefreshPreview();
        foreach (var combo in new[] { _ctrl, _alt, _shift, _win })
            combo.SelectedIndexChanged += (_, _) => RefreshPreview();

        // Modifier sides are selected in the four controls, rather than
        // inferred from standard WinForms KeyEventArgs (which merges sides).
        _capture.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        _capture.KeyDown += (_, e) =>
        {
            int vk = (int)e.KeyCode & 0xFF;
            if (vk is >= 1 and <= 254) _key.Value = vk;
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        save.Click += (_, _) =>
        {
            var trigger = Read();
            try
            {
                trigger.Validate();
                Result = trigger;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid trigger",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        RefreshPreview();
    }

    private HostMacroTrigger Read() => new()
    {
        Key = (int)_key.Value,
        Ctrl = (HostModifierRequirement)_ctrl.SelectedIndex,
        Alt = (HostModifierRequirement)_alt.SelectedIndex,
        Shift = (HostModifierRequirement)_shift.SelectedIndex,
        Win = (HostModifierRequirement)_win.SelectedIndex
    };

    private void RefreshPreview() => _preview.Text = Read().ToString();
}
