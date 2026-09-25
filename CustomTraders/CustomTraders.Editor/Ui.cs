namespace CustomTraders.Editor;

/// <summary>
/// A two-column "label: control" panel whose controls read from / write to
/// whatever object is currently selected, through get/set lambdas. Call
/// <see cref="RefreshValues"/> after the selection changes.
/// </summary>
public sealed class FieldPanel : TableLayoutPanel
{
    private readonly List<Action> _refreshers = new();
    private bool _loading;

    /// <summary>Raised after the user changed any value.</summary>
    public event Action? Changed;

    public FieldPanel()
    {
        ColumnCount = 2;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Padding = new Padding(4);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    public void RefreshValues()
    {
        _loading = true;
        try { foreach (var r in _refreshers) r(); }
        finally { _loading = false; }
    }

    private void Set(Action apply)
    {
        if (_loading) return;
        apply();
        Changed?.Invoke();
    }

    public T AddRow<T>(string label, T control) where T : Control
    {
        int row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        control.Dock = DockStyle.Fill;
        Controls.Add(control, 1, row);
        return control;
    }

    public TextBox AddText(string label, Func<string?> get, Action<string> set, bool multiline = false, int height = 70)
    {
        var box = new TextBox { Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
        if (multiline) box.Height = height;
        AddRow(label, box);
        box.TextChanged += (_, _) => Set(() => set(box.Text));
        _refreshers.Add(() => box.Text = get() ?? "");
        return box;
    }

    public NumericUpDown AddNumber(string label, Func<decimal> get, Action<decimal> set, decimal min, decimal max, int decimals = 0, decimal increment = 1)
    {
        var num = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = increment, ThousandsSeparator = true, Width = 160 };
        AddRow(label, num);
        num.Dock = DockStyle.Left;
        num.ValueChanged += (_, _) => Set(() => set(num.Value));
        _refreshers.Add(() => num.Value = Math.Clamp(get(), min, max));
        return num;
    }

    public CheckBox AddCheck(string label, Func<bool> get, Action<bool> set, string? text = null)
    {
        var check = new CheckBox { Text = text ?? "", AutoSize = true };
        AddRow(label, check);
        check.Dock = DockStyle.Left;
        check.CheckedChanged += (_, _) => Set(() => set(check.Checked));
        _refreshers.Add(() => check.Checked = get());
        return check;
    }

    /// <summary>Drop-down of (value, label) pairs; the options are re-read on every refresh.</summary>
    public ComboBox AddCombo(string label, Func<IEnumerable<(string Value, string Label)>> options, Func<string?> get, Action<string> set)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label", ValueMember = "Value" };
        AddRow(label, combo);
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboOption o) Set(() => set(o.Value));
        };
        _refreshers.Add(() =>
        {
            combo.Items.Clear();
            foreach (var (value, text) in options()) combo.Items.Add(new ComboOption(value, text));
            string? current = get();
            combo.SelectedItem = combo.Items.Cast<ComboOption>().FirstOrDefault(o => o.Value == current);
        });
        return combo;
    }

    public ComboBox AddCombo(string label, string[] values, Func<string?> get, Action<string> set) =>
        AddCombo(label, () => values.Select(v => (v, v)), get, set);

    /// <summary>Item id + its name + a "Pick..." button opening the item search.</summary>
    public void AddItem(string label, ItemDatabase db, Func<string?> get, Action<string> set, bool weaponsOnly = false)
    {
        var name = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        var pick = new Button { Text = "Pick...", Width = 80, Dock = DockStyle.Right };
        var row = new Panel { Height = 28 };
        row.Controls.Add(name);
        row.Controls.Add(pick);
        AddRow(label, row);

        pick.Click += (_, _) =>
        {
            using var dialog = new ItemPickerDialog(db, weaponsOnly);
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedTpl != null)
            {
                Set(() => set(dialog.SelectedTpl));
                RefreshValues();
            }
        };
        _refreshers.Add(() =>
        {
            string? tpl = get();
            name.Text = string.IsNullOrWhiteSpace(tpl) ? "(none — click Pick...)" : $"{db.NameOf(tpl)}   ({tpl})";
        });
    }

    private sealed record ComboOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}

/// <summary>
/// A list box of the items in a list, with Add / Remove / Up / Down buttons.
/// The list itself comes from <c>getList</c>, so pointing it at another
/// trader/quest is just <see cref="RefreshList"/>.
/// </summary>
public sealed class ListEditor<T> : Panel where T : class
{
    private readonly Func<List<T>?> _getList;
    private readonly Func<T?> _create;
    private readonly Func<T, string> _display;
    private readonly ListBox _box = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.LeftToRight };

    public event Action? SelectionChanged;
    public event Action? ListChanged;

    public T? Selected => _box.SelectedItem is Entry e ? e.Value : null;

    public ListEditor(string title, Func<List<T>?> getList, Func<T?> create, Func<T, string> display, string addText = "Add")
    {
        _getList = getList;
        _create = create;
        _display = display;

        var header = new Label { Text = title, Dock = DockStyle.Top, Height = 22, Font = new Font(DefaultFont, FontStyle.Bold) };
        var add = new Button { Text = addText, AutoSize = true };
        var remove = new Button { Text = "Remove", AutoSize = true };
        var up = new Button { Text = "▲", Width = 32 };
        var down = new Button { Text = "▼", Width = 32 };
        _buttons.Controls.AddRange(new Control[] { add, remove, up, down });

        Controls.Add(_box);
        Controls.Add(header);
        Controls.Add(_buttons);

        _box.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke();
        add.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null) return;
            var item = _create();
            if (item == null) return;
            list.Add(item);
            RefreshList(item);
            ListChanged?.Invoke();
        };
        remove.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null || Selected == null) return;
            int index = _box.SelectedIndex;
            list.Remove(Selected);
            RefreshList(list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)]);
            ListChanged?.Invoke();
        };
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(+1);
    }

    private void MoveSelected(int delta)
    {
        var list = _getList();
        var item = Selected;
        if (list == null || item == null) return;
        int i = list.IndexOf(item), j = i + delta;
        if (j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        RefreshList(item);
        ListChanged?.Invoke();
    }

    /// <summary>Rebuilds the list box (keeping or setting the selection).</summary>
    public void RefreshList(T? select = null)
    {
        select ??= Selected;
        var list = _getList();
        _box.BeginUpdate();
        _box.Items.Clear();
        if (list != null)
            foreach (var item in list) _box.Items.Add(new Entry(item, _display));
        _box.EndUpdate();

        var match = _box.Items.Cast<Entry>().FirstOrDefault(e => ReferenceEquals(e.Value, select));
        if (match != null) _box.SelectedItem = match;
        else if (_box.Items.Count > 0) _box.SelectedIndex = 0;
        else SelectionChanged?.Invoke();
        Enabled = list != null;
    }

    /// <summary>Updates the visible text of the rows (after an edit) without touching the selection.</summary>
    public void RefreshTexts()
    {
        _box.BeginUpdate();
        for (int i = 0; i < _box.Items.Count; i++) _box.Items[i] = _box.Items[i];
        _box.EndUpdate();
    }

    private sealed class Entry(T value, Func<T, string> display)
    {
        public T Value { get; } = value;
        public override string ToString() => display(Value);
    }
}

public static class Prompt
{
    public static string? Ask(IWin32Window owner, string title, string question, string initial = "")
    {
        using var form = new Form { Text = title, Width = 420, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var label = new Label { Text = question, Left = 10, Top = 10, Width = 380 };
        var box = new TextBox { Text = initial, Left = 10, Top = 34, Width = 380 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 234, Top = 68, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 315, Top = 68, Width = 75 };
        form.Controls.AddRange(new Control[] { label, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}
