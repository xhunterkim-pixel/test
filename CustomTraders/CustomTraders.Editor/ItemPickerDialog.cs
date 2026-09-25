namespace CustomTraders.Editor;

/// <summary>Search the game's items by name/short name/id and pick one.</summary>
public sealed class ItemPickerDialog : Form
{
    private readonly ItemDatabase _db;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Type to search (name, short name or id)..." };
    private readonly CheckBox _weaponsOnly = new() { Text = "Weapons only", AutoSize = true };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly TextBox _manual = new() { Width = 220, PlaceholderText = "or paste an item id" };

    public string? SelectedTpl { get; private set; }

    public ItemPickerDialog(ItemDatabase db, bool weaponsOnly = false)
    {
        _db = db;
        Text = "Pick an item";
        Width = 760;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        _weaponsOnly.Checked = weaponsOnly;
        _list.Columns.Add("Name", 380);
        _list.Columns.Add("Short", 120);
        _list.Columns.Add("Id", 200);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 32, ColumnCount = 2, Padding = new Padding(4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(_search, 0, 0);
        top.Controls.Add(_weaponsOnly, 1, 0);

        var ok = new Button { Text = "Select", DialogResult = DialogResult.None, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(ok);
        bottom.Controls.Add(_manual);

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;

        _search.TextChanged += (_, _) => Populate();
        _weaponsOnly.CheckedChanged += (_, _) => Populate();
        _list.DoubleClick += (_, _) => Accept();
        ok.Click += (_, _) => Accept();

        if (!_db.IsLoaded)
        {
            _list.Items.Add(new ListViewItem(new[] { "Item database not loaded — paste an id below", "", "" }));
        }
        Populate();
    }

    private void Populate()
    {
        if (!_db.IsLoaded) return;
        string q = _search.Text.Trim();
        var matches = _db.Items.Values
            .Where(i => !_weaponsOnly.Checked || i.IsWeapon)
            .Where(i => q.Length == 0
                        || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.Id.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name)
            .Take(500);

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in matches)
            _list.Items.Add(new ListViewItem(new[] { item.Name, item.ShortName, item.Id }) { Tag = item.Id });
        _list.EndUpdate();
    }

    private void Accept()
    {
        string manual = _manual.Text.Trim();
        if (manual.Length > 0)
        {
            if (!CustomTraders.Shared.Ids.IsValid(manual))
            {
                MessageBox.Show(this, "An item id is 24 characters of 0-9 / a-f.", "Invalid id");
                return;
            }
            SelectedTpl = manual.ToLowerInvariant();
        }
        else if (_list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is string tpl)
        {
            SelectedTpl = tpl;
        }
        else
        {
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
