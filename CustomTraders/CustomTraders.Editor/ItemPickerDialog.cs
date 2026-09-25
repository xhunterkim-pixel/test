namespace CustomTraders.Editor;

public enum ItemFilter { All, WeaponsAndGrenades, Weapons, Grenades, Ammo, Gear, FoodAndDrink, Medical, Money }

/// <summary>Search the game's items by name/short name/id and pick one.</summary>
public sealed class ItemPickerDialog : Form
{
    private static readonly (ItemFilter Filter, string Label)[] Filters =
    {
        (ItemFilter.All, "All items"),
        (ItemFilter.WeaponsAndGrenades, "Weapons + grenades"),
        (ItemFilter.Weapons, "Weapons"),
        (ItemFilter.Grenades, "Grenades"),
        (ItemFilter.Ammo, "Ammo"),
        (ItemFilter.Gear, "Gear (armor, helmets, rigs, bags, eyewear, masks)"),
        (ItemFilter.FoodAndDrink, "Food & drink"),
        (ItemFilter.Medical, "Medical"),
        (ItemFilter.Money, "Money"),
    };

    private readonly ItemDatabase _db;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "What are you looking for? (name, short name or id)", Font = Theme.Big };
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Dock = DockStyle.Fill };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.None };
    private readonly TextBox _manual = new() { Width = 240, PlaceholderText = "or paste an item id" };

    public string? SelectedTpl { get; private set; }

    public ItemPickerDialog(ItemDatabase db, ItemFilter filter = ItemFilter.All)
    {
        _db = db;
        Text = "Pick an item";
        Width = 860;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        Theme.Dialog(this);
        Padding = new Padding(12);

        foreach (var (_, label) in Filters) _filter.Items.Add(label);
        _filter.SelectedIndex = Array.FindIndex(Filters, f => f.Filter == filter);

        _list.BackColor = Theme.Surface;
        _list.ForeColor = Theme.Text;
        _list.Font = Theme.Body;
        _list.Columns.Add("Name", 420);
        _list.Columns.Add("Short", 130);
        _list.Columns.Add("Type", 110);
        _list.Columns.Add("Id", 180);
        _list.OwnerDraw = true;
        _list.DrawColumnHeader += (_, e) =>
        {
            using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, Theme.Caption, e.Bounds, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        _list.DrawItem += (_, e) => e.DrawDefault = false;
        _list.DrawSubItem += (_, e) =>
        {
            bool selected = e.Item!.Selected;
            using (var b = new SolidBrush(selected ? Theme.CardSelected : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            var color = e.ColumnIndex == 0 ? (selected ? Theme.Accent : Theme.Text) : Theme.Muted;
            TextRenderer.DrawText(e.Graphics, e.SubItem!.Text, e.ColumnIndex == 0 ? Theme.BodyBold : Theme.Body, e.Bounds, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };

        var searchCard = new GlassPanel { Dock = DockStyle.Top, Height = 52, Radius = 26, Tint = Theme.CardHover, Padding = new Padding(20, 12, 12, 8) };
        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = Theme.CardHover;
        _search.ForeColor = Theme.Text;
        searchCard.Controls.Add(_search);

        var filterRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 2, Padding = new Padding(0, 8, 0, 4) };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        filterRow.Controls.Add(new Label { Text = "Show:", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(0, 6, 8, 0) }, 0, 0);
        filterRow.Controls.Add(_filter, 1, 0);

        var ok = new PillButton("Select", PillStyle.Primary) { Width = 110 };
        var cancel = new PillButton("Cancel", PillStyle.Outline) { Width = 100, DialogResult = DialogResult.Cancel };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        _manual.Margin = new Padding(4, 10, 12, 4);
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(ok);
        bottom.Controls.Add(_manual);

        var listCard = new GlassPanel { Dock = DockStyle.Fill, Tint = Theme.Surface, Padding = new Padding(8) };
        listCard.Controls.Add(_list);

        Controls.Add(listCard);
        Controls.Add(filterRow);
        Controls.Add(searchCard);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;

        _search.TextChanged += (_, _) => Populate();
        _filter.SelectedIndexChanged += (_, _) => Populate();
        _list.DoubleClick += (_, _) => Accept();
        ok.Click += (_, _) => Accept();
        Shown += (_, _) => _search.Focus();

        if (!_db.IsLoaded)
            _list.Items.Add(new ListViewItem(new[] { "Item database not loaded — paste an id below", "", "", "" }));
        Populate();
    }

    private static bool Matches(GameItem item, ItemFilter filter) => filter switch
    {
        ItemFilter.WeaponsAndGrenades => item.Category is ItemCategory.Weapon or ItemCategory.Grenade,
        ItemFilter.Weapons => item.Category == ItemCategory.Weapon,
        ItemFilter.Grenades => item.Category == ItemCategory.Grenade,
        ItemFilter.Ammo => item.Category == ItemCategory.Ammo,
        ItemFilter.Gear => item.Category == ItemCategory.Gear,
        ItemFilter.FoodAndDrink => item.Category == ItemCategory.Food,
        ItemFilter.Medical => item.Category == ItemCategory.Meds,
        ItemFilter.Money => item.Category == ItemCategory.Money,
        _ => true,
    };

    private void Populate()
    {
        if (!_db.IsLoaded) return;
        var filter = Filters[Math.Max(0, _filter.SelectedIndex)].Filter;
        string q = _search.Text.Trim();
        var matches = _db.Items.Values
            .Where(i => Matches(i, filter))
            .Where(i => q.Length == 0
                        || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.Id.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name)
            .Take(500);

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in matches)
        {
            string type = item.Category == ItemCategory.Ammo && item.Caliber.Length > 0 ? ItemDatabase.CaliberName(item.Caliber) : item.Category.ToString();
            _list.Items.Add(new ListViewItem(new[] { item.Name, item.ShortName, type, item.Id }) { Tag = item.Id });
        }
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

    /// <summary>Opens the picker and returns the chosen item id (or null).</summary>
    public static string? Pick(IWin32Window owner, ItemDatabase db, ItemFilter filter)
    {
        using var dialog = new ItemPickerDialog(db, filter);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedTpl : null;
    }
}
