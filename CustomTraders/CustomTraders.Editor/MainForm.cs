using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CustomTraders.Shared;

namespace CustomTraders.Editor;

/// <summary>One trader loaded from traders/&lt;folder&gt;/trader.json.</summary>
public sealed class TraderEntry(string folder, TraderFile file)
{
    public string Folder { get; } = folder;
    public TraderFile File { get; } = file;
    public bool Dirty { get; set; }
    public override string ToString() => (Dirty ? "* " : "") + File.Name;
}

public sealed class MainForm : Form
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomTradersEditor", "modfolder.txt");

    private readonly ItemDatabase _db = new();
    private readonly List<TraderEntry> _traders = new();
    private string? _modFolder;

    private readonly TextBox _modFolderBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Label _dbStatus = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
    private readonly ListBox _traderList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    // Trader tab
    private readonly FieldPanel _traderFields = new();
    private readonly PictureBox _avatar = new() { Width = 128, Height = 128, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly DataGridView _loyalty = new() { Height = 150, Dock = DockStyle.Top, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersWidth = 60, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

    // Offers tab
    private readonly ListEditor<OfferDef> _offers;
    private readonly FieldPanel _offerFields = new();
    private readonly ListEditor<CostDef> _costs;
    private readonly FieldPanel _costFields = new();

    // Quests tab
    private readonly ListEditor<QuestDef> _quests;
    private readonly FieldPanel _questFields = new();
    private readonly CheckedListBox _prereqs = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly ListEditor<ConditionDef> _conditions;
    private readonly FieldPanel _conditionFields = new();
    private readonly ListBox _conditionItems = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListEditor<RewardDef> _rewards;
    private readonly FieldPanel _rewardFields = new();
    private bool _loadingPrereqs;

    private TraderEntry? Current => _traderList.SelectedItem as TraderEntry;
    private TraderFile? Trader => Current?.File;
    private OfferDef? Offer => _offers.Selected;
    private CostDef? Cost => _costs.Selected;
    private QuestDef? Quest => _quests.Selected;
    private ConditionDef? Condition => _conditions.Selected;
    private RewardDef? Reward => _rewards.Selected;

    public MainForm()
    {
        Text = "CustomTraders Editor";
        Width = 1280;
        Height = 860;
        StartPosition = FormStartPosition.CenterScreen;

        _offers = new ListEditor<OfferDef>("Offers", () => Trader?.Offers, NewOffer, DescribeOffer, "Add offer");
        _costs = new ListEditor<CostDef>("Cost (all required)", () => Offer?.Cost,
            () => new CostDef { ItemTpl = Currencies.Roubles, Count = 10000 },
            c => $"{c.Count:N0} x {_db.NameOf(c.ItemTpl)}", "Add cost");
        _quests = new ListEditor<QuestDef>("Quests", () => Trader?.Quests,
            () => new QuestDef { Id = Ids.New(), Name = "New quest" }, q => $"Lvl {q.MinLevel}  {q.Name}", "Add quest");
        _conditions = new ListEditor<ConditionDef>("Objectives", () => Quest?.Conditions,
            () => new ConditionDef { Id = Ids.New() }, DescribeCondition, "Add objective");
        _rewards = new ListEditor<RewardDef>("Rewards", () => Quest?.Rewards,
            () => new RewardDef { Id = Ids.New(), Type = RewardTypes.Experience, Value = 1000 }, DescribeReward, "Add reward");

        BuildLayout();
        BuildTraderTab();
        BuildOffersTab();
        BuildQuestsTab();

        _traderList.SelectedIndexChanged += (_, _) => ShowTrader();
        FormClosing += OnClosing;
        Shown += (_, _) => LoadModFolder(ReadSavedFolder());
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 5, Padding = new Padding(6, 4, 6, 0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Browse...", AutoSize = true };
        var reload = new Button { Text = "Reload", AutoSize = true };
        top.Controls.Add(new Label { Text = "Mod folder:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        top.Controls.Add(_modFolderBox, 1, 0);
        top.Controls.Add(browse, 2, 0);
        top.Controls.Add(reload, 3, 0);
        top.Controls.Add(_dbStatus, 4, 0);
        browse.Click += (_, _) => BrowseModFolder();
        reload.Click += (_, _) => { if (ConfirmDiscard()) LoadModFolder(_modFolder); };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6) };
        var save = new Button { Text = "Save all", Width = 110, Height = 28 };
        save.Click += (_, _) => SaveAll();
        bottom.Controls.Add(save);
        bottom.Controls.Add(_status);

        var left = new Panel { Dock = DockStyle.Fill };
        var traderButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
        var addTrader = new Button { Text = "New trader", AutoSize = true };
        var removeTrader = new Button { Text = "Delete", AutoSize = true };
        traderButtons.Controls.AddRange(new Control[] { addTrader, removeTrader });
        addTrader.Click += (_, _) => NewTrader();
        removeTrader.Click += (_, _) => DeleteTrader();
        left.Controls.Add(_traderList);
        left.Controls.Add(new Label { Text = "Traders", Dock = DockStyle.Top, Height = 22, Font = new Font(DefaultFont, FontStyle.Bold) });
        left.Controls.Add(traderButtons);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 220, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_tabs);

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private static Panel Stack(params Control[] topToBottom)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        foreach (var c in topToBottom.Reverse()) { c.Dock = DockStyle.Top; panel.Controls.Add(c); }
        return panel;
    }

    private void BuildTraderTab()
    {
        var f = _traderFields;
        f.AddText("Name", () => Trader?.Name, v => Trader!.Name = v);
        f.AddText("Nickname", () => Trader?.Nickname, v => Trader!.Nickname = v);
        f.AddText("Surname", () => Trader?.Surname, v => Trader!.Surname = v);
        f.AddText("Location", () => Trader?.Location, v => Trader!.Location = v);
        f.AddText("Description", () => Trader?.Description, v => Trader!.Description = v, multiline: true);
        f.AddCombo("Currency", new[] { "RUB", "USD", "EUR" }, () => Trader?.Currency, v => Trader!.Currency = v);
        f.AddCheck("Unlocked", () => Trader?.UnlockedByDefault ?? true, v => Trader!.UnlockedByDefault = v, "Available from the start");
        f.AddCheck("Flea market", () => Trader?.ListOnFlea ?? true, v => Trader!.ListOnFlea = v, "List this trader's offers on the flea market");
        f.AddNumber("Restock min (minutes)", () => Trader?.RefreshMinutesMin ?? 60, v => Trader!.RefreshMinutesMin = (int)v, 1, 10080);
        f.AddNumber("Restock max (minutes)", () => Trader?.RefreshMinutesMax ?? 120, v => Trader!.RefreshMinutesMax = (int)v, 1, 10080);
        f.Changed += MarkDirty;

        var pick = new Button { Text = "Choose icon from PC...", AutoSize = true };
        pick.Click += (_, _) => ChooseAvatar();
        var avatarRow = new FlowLayoutPanel { Height = 140, FlowDirection = FlowDirection.LeftToRight };
        avatarRow.Controls.Add(_avatar);
        avatarRow.Controls.Add(pick);

        _loyalty.CellValueChanged += (_, _) => MarkDirty();
        _loyalty.DataError += (_, e) => { e.ThrowException = false; };

        _tabs.TabPages.Add(Page("Trader", Stack(
            new Label { Text = "Icon (shown in the trader screen)", Height = 20 },
            avatarRow,
            f,
            new Label { Text = "Loyalty levels (LL1 to LL4)", Height = 22, Font = new Font(DefaultFont, FontStyle.Bold) },
            _loyalty)));
    }

    private void BuildOffersTab()
    {
        var o = _offerFields;
        o.AddItem("Item", _db, () => Offer?.ItemTpl, v => Offer!.ItemTpl = v, weaponsOnly: true);
        o.AddCheck("Weapon preset", () => Offer?.UseDefaultPreset ?? true, v => Offer!.UseDefaultPreset = v, "Sell the default assembled gun (not a bare receiver)");
        o.AddNumber("Loyalty level", () => Offer?.LoyaltyLevel ?? 1, v => Offer!.LoyaltyLevel = (int)v, 1, 4);
        o.AddCheck("Unlimited stock", () => Offer?.Unlimited ?? true, v => Offer!.Unlimited = v);
        o.AddNumber("Stock per restock", () => Offer?.Stock ?? 1, v => Offer!.Stock = (int)v, 1, 100000);
        o.AddNumber("Buy limit per restock", () => Offer?.BuyLimit ?? 0, v => Offer!.BuyLimit = (int)v, 0, 100000);
        o.AddRow("Locked by quest", new Label { AutoSize = true, Text = "Set by a quest's \"UnlockOffer\" reward (Quests tab)." });
        o.Changed += () => { MarkDirty(); _offers.RefreshTexts(); };

        var c = _costFields;
        c.AddItem("Cost item", _db, () => Cost?.ItemTpl, v => Cost!.ItemTpl = v);
        c.AddNumber("Amount", () => (decimal)(Cost?.Count ?? 1), v => Cost!.Count = (double)v, 1, 100000000);
        c.Changed += () => { MarkDirty(); _costs.RefreshTexts(); _offers.RefreshTexts(); };

        var quickMoney = new FlowLayoutPanel { Height = 34 };
        foreach (var (label, tpl) in new[] { ("+ Roubles", Currencies.Roubles), ("+ Dollars", Currencies.Dollars), ("+ Euros", Currencies.Euros) })
        {
            var b = new Button { Text = label, AutoSize = true };
            b.Click += (_, _) =>
            {
                if (Offer == null) return;
                var cost = new CostDef { ItemTpl = tpl, Count = tpl == Currencies.Roubles ? 50000 : 500 };
                Offer.Cost.Add(cost);
                _costs.RefreshList(cost);
                MarkDirty();
                _offers.RefreshTexts();
            };
            quickMoney.Controls.Add(b);
        }

        _offers.SelectionChanged += ShowOffer;
        _offers.ListChanged += MarkDirty;
        _costs.SelectionChanged += () => { _costFields.Enabled = Cost != null; _costFields.RefreshValues(); };
        _costs.ListChanged += () => { MarkDirty(); _offers.RefreshTexts(); };

        _costs.Height = 170;
        var right = Stack(o, new Label { Height = 10 }, _costs, quickMoney, c);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 420 };
        _offers.Dock = DockStyle.Fill;
        split.Panel1.Controls.Add(_offers);
        split.Panel2.Controls.Add(right);
        _tabs.TabPages.Add(Page("Offers / barters", split));
    }

    private void BuildQuestsTab()
    {
        var q = _questFields;
        q.AddText("Name", () => Quest?.Name, v => Quest!.Name = v);
        q.AddText("Description", () => Quest?.Description, v => Quest!.Description = v, multiline: true, height: 90);
        q.AddText("Completion message", () => Quest?.SuccessMessage, v => Quest!.SuccessMessage = v, multiline: true, height: 50);
        q.AddNumber("Unlocks at level", () => Quest?.MinLevel ?? 1, v => Quest!.MinLevel = (int)v, 1, 100);
        var image = new Button { Text = "Choose quest image...", AutoSize = true };
        image.Click += (_, _) => ChooseQuestImage();
        q.AddRow("Image", image);
        q.Changed += () => { MarkDirty(); _quests.RefreshTexts(); };

        _prereqs.ItemCheck += (_, e) => BeginInvoke(() => OnPrereqChecked());
        var prereqPanel = new Panel { Height = 120 };
        prereqPanel.Controls.Add(_prereqs);
        prereqPanel.Controls.Add(new Label { Text = "Requires these quests completed first", Dock = DockStyle.Top, Height = 20 });

        var cf = _conditionFields;
        cf.AddCombo("Type", ConditionTypes.All, () => Condition?.Type, v => Condition!.Type = v);
        cf.AddNumber("Count", () => Condition?.Count ?? 1, v => Condition!.Count = (int)v, 1, 100000);
        cf.AddCheck("Found in raid", () => Condition?.FoundInRaid ?? true, v => Condition!.FoundInRaid = v, "Items must be found in raid");
        cf.AddCombo("Kill target", new[] { "Any", "Savage", "AnyPmc", "Usec", "Bear" }, () => Condition?.KillTarget, v => Condition!.KillTarget = v);
        cf.AddText("Kill maps (comma list)", () => string.Join(", ", Condition?.Locations ?? new()),
            v => Condition!.Locations = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());
        cf.AddText("Objective text", () => Condition?.Text, v => Condition!.Text = v);
        cf.Changed += () => { MarkDirty(); _conditions.RefreshTexts(); };

        var itemButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
        var addItem = new Button { Text = "Add item...", AutoSize = true };
        var removeItem = new Button { Text = "Remove item", AutoSize = true };
        itemButtons.Controls.AddRange(new Control[] { addItem, removeItem });
        addItem.Click += (_, _) =>
        {
            if (Condition == null) return;
            using var dialog = new ItemPickerDialog(_db);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedTpl != null)
            {
                Condition.ItemTpls.Add(dialog.SelectedTpl);
                ShowConditionItems();
                MarkDirty();
                _conditions.RefreshTexts();
            }
        };
        removeItem.Click += (_, _) =>
        {
            if (Condition == null || _conditionItems.SelectedIndex < 0) return;
            Condition.ItemTpls.RemoveAt(_conditionItems.SelectedIndex);
            ShowConditionItems();
            MarkDirty();
            _conditions.RefreshTexts();
        };
        var itemsPanel = new Panel { Height = 130 };
        itemsPanel.Controls.Add(_conditionItems);
        itemsPanel.Controls.Add(new Label { Text = "Items (hand-in / find: any of these counts)", Dock = DockStyle.Top, Height = 20 });
        itemsPanel.Controls.Add(itemButtons);

        var rf = _rewardFields;
        rf.AddCombo("Type", RewardTypes.All, () => Reward?.Type, v => Reward!.Type = v);
        rf.AddNumber("XP / standing", () => (decimal)(Reward?.Value ?? 0), v => Reward!.Value = (double)v, -1000000, 100000000, 2, 0.01m);
        rf.AddItem("Item", _db, () => Reward?.ItemTpl, v => Reward!.ItemTpl = v);
        rf.AddNumber("Item count", () => Reward?.Count ?? 1, v => Reward!.Count = (int)v, 1, 100000);
        rf.AddCheck("Item found in raid", () => Reward?.FoundInRaid ?? true, v => Reward!.FoundInRaid = v);
        rf.AddCombo("Offer to unlock", () => (Trader?.Offers ?? new()).Select(o => (o.Id, DescribeOffer(o))), () => Reward?.OfferId, v => Reward!.OfferId = v);
        rf.AddNumber("Unlocked quantity", () => Reward?.Quantity ?? 0, v => Reward!.Quantity = (int)v, 0, 100000);
        rf.AddRow("", new Label { AutoSize = true, Text = "UnlockOffer: quantity = stock per restock of the unlocked offer (0 = the offer's own setting)." });
        rf.Changed += () => { MarkDirty(); _rewards.RefreshTexts(); _offers.RefreshTexts(); };

        _quests.SelectionChanged += ShowQuest;
        _quests.ListChanged += () => { MarkDirty(); ShowQuest(); };
        _conditions.SelectionChanged += () => { _conditionFields.Enabled = Condition != null; _conditionFields.RefreshValues(); ShowConditionItems(); };
        _conditions.ListChanged += MarkDirty;
        _rewards.SelectionChanged += () => { _rewardFields.Enabled = Reward != null; _rewardFields.RefreshValues(); };
        _rewards.ListChanged += () => { MarkDirty(); _offers.RefreshTexts(); };

        _conditions.Height = 150;
        _rewards.Height = 150;
        var right = Stack(q, prereqPanel,
            new Label { Height = 8 }, _conditions, cf, itemsPanel,
            new Label { Height = 8 }, _rewards, rf);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        _quests.Dock = DockStyle.Fill;
        split.Panel1.Controls.Add(_quests);
        split.Panel2.Controls.Add(right);
        _tabs.TabPages.Add(Page("Quests", split));
    }

    // ------------------------------------------------------------------ show

    private void ShowTrader()
    {
        _traderFields.Enabled = Trader != null;
        _traderFields.RefreshValues();
        _loyalty.DataSource = Trader == null ? null : new BindingList<LoyaltyLevelDef>(Trader.LoyaltyLevels);
        ShowAvatar();
        _offers.RefreshList();
        _quests.RefreshList();
    }

    private void ShowOffer()
    {
        _offerFields.Enabled = Offer != null;
        _offerFields.RefreshValues();
        _costs.RefreshList();
    }

    private void ShowQuest()
    {
        _questFields.Enabled = Quest != null;
        _questFields.RefreshValues();
        _conditions.RefreshList();
        _rewards.RefreshList();
        ShowPrereqs();
    }

    private void ShowConditionItems()
    {
        _conditionItems.Items.Clear();
        if (Condition == null) return;
        foreach (var tpl in Condition.ItemTpls) _conditionItems.Items.Add($"{_db.NameOf(tpl)}   ({tpl})");
    }

    /// <summary>Every quest of every loaded trader (except the current one) as a possible prerequisite.</summary>
    private void ShowPrereqs()
    {
        _loadingPrereqs = true;
        _prereqs.Items.Clear();
        if (Quest != null)
        {
            foreach (var entry in _traders)
            foreach (var other in entry.File.Quests.Where(x => !ReferenceEquals(x, Quest)))
            {
                int i = _prereqs.Items.Add(new PrereqItem(other.Id, $"{entry.File.Name}: {other.Name}"));
                _prereqs.SetItemChecked(i, Quest.PrerequisiteQuestIds.Contains(other.Id));
            }
        }
        _prereqs.Enabled = Quest != null;
        _loadingPrereqs = false;
    }

    private void OnPrereqChecked()
    {
        if (_loadingPrereqs || Quest == null) return;
        Quest.PrerequisiteQuestIds = _prereqs.CheckedItems.Cast<PrereqItem>().Select(p => p.Id).ToList();
        MarkDirty();
    }

    private sealed record PrereqItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private void ShowAvatar()
    {
        var old = _avatar.Image;
        _avatar.Image = null;
        old?.Dispose();
        if (Current == null) return;
        var path = Path.Combine(Current.Folder, Trader!.Avatar ?? "");
        if (!File.Exists(path)) return;
        // Load a copy so the file isn't locked (it can then be replaced).
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var loaded = Image.FromStream(stream);
        _avatar.Image = new Bitmap(loaded);
    }

    // ------------------------------------------------------------------ text

    private string DescribeOffer(OfferDef o)
    {
        string cost = o.Cost.Count == 0 ? "no cost!" : string.Join(" + ", o.Cost.Select(c =>
            Currencies.IsCurrency(c.ItemTpl) ? $"{c.Count:N0} {CurrencySymbol(c.ItemTpl)}" : $"{c.Count:N0}x {_db.NameOf(c.ItemTpl)}"));
        string stock = o.Unlimited ? "" : $"  [{o.Stock}]";
        bool locked = Trader?.Quests.Any(q => q.Rewards.Any(r => r.Type == RewardTypes.UnlockOffer && r.OfferId == o.Id)) == true;
        return $"LL{o.LoyaltyLevel}  {_db.NameOf(o.ItemTpl)}  —  {cost}{stock}{(locked ? "  (quest)" : "")}";
    }

    private static string CurrencySymbol(string tpl) => tpl == Currencies.Dollars ? "$" : tpl == Currencies.Euros ? "€" : "₽";

    private string DescribeCondition(ConditionDef c) => c.Type switch
    {
        ConditionTypes.Kill => $"Kill {c.Count} {c.KillTarget}{(c.Locations.Count > 0 ? " on " + string.Join(",", c.Locations) : "")}",
        _ => $"{c.Type} {c.Count}x {(c.ItemTpls.Count == 0 ? "(no items)" : string.Join(" / ", c.ItemTpls.Select(_db.NameOf)))}",
    };

    private string DescribeReward(RewardDef r) => r.Type switch
    {
        RewardTypes.Experience => $"{r.Value:N0} XP",
        RewardTypes.TraderStanding => $"+{r.Value:0.##} standing",
        RewardTypes.Item => $"{r.Count}x {_db.NameOf(r.ItemTpl)}",
        RewardTypes.UnlockOffer => "Unlock: " + (Trader?.Offers.FirstOrDefault(o => o.Id == r.OfferId) is { } o ? DescribeOffer(o) : "(pick an offer)")
                                   + (r.Quantity > 0 ? $"  x{r.Quantity}" : ""),
        _ => r.Type,
    };

    // ------------------------------------------------------------------ actions

    private OfferDef? NewOffer()
    {
        using var dialog = new ItemPickerDialog(_db, weaponsOnly: true);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedTpl == null) return null;
        return new OfferDef
        {
            Id = Ids.New(),
            ItemTpl = dialog.SelectedTpl,
            Cost = { new CostDef { ItemTpl = Currencies.Roubles, Count = 50000 } },
        };
    }

    private void NewTrader()
    {
        if (_modFolder == null) { BrowseModFolder(); if (_modFolder == null) return; }
        string? name = Prompt.Ask(this, "New trader", "Trader name:");
        if (name == null) return;

        string safe = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ')).Trim();
        if (safe.Length == 0) safe = "Trader";
        string folder = Path.Combine(_modFolder, "traders", safe);
        for (int n = 2; Directory.Exists(folder); n++) folder = Path.Combine(_modFolder, "traders", $"{safe} {n}");
        Directory.CreateDirectory(folder);

        var file = new TraderFile { Id = Ids.New(), Name = name, Nickname = name, Avatar = "avatar.png" };
        SavePlaceholderAvatar(Path.Combine(folder, "avatar.png"), name);
        file.Save(Path.Combine(folder, "trader.json"));

        var entry = new TraderEntry(folder, file);
        _traders.Add(entry);
        RefreshTraderList(entry);
        SetStatus($"Created {name} in {folder}");
    }

    private void DeleteTrader()
    {
        if (Current == null || _modFolder == null) return;
        if (MessageBox.Show(this,
                $"Remove trader \"{Trader!.Name}\"?\n\nIts folder is moved to \"deleted_traders\" (not erased), so you can put it back later.\n" +
                "Players who used this trader keep their items, but its offers and quests disappear.",
                "Remove trader", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        string trash = Path.Combine(_modFolder, "deleted_traders");
        Directory.CreateDirectory(trash);
        string target = Path.Combine(trash, $"{Path.GetFileName(Current.Folder)}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.Move(Current.Folder, target);
        _traders.Remove(Current);
        RefreshTraderList();
        SetStatus($"Moved to {target}");
    }

    private void ChooseAvatar()
    {
        if (Current == null) return;
        using var dialog = new OpenFileDialog { Title = "Choose trader icon", Filter = "Images|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // Stored as a square 256x256 image in the trader's folder.
        string target = Path.Combine(Current.Folder, "avatar.png");
        SaveSquareImage(dialog.FileName, target, 256);
        Trader!.Avatar = "avatar.png";
        MarkDirty();
        ShowAvatar();
    }

    private void ChooseQuestImage()
    {
        if (Current == null || Quest == null) return;
        using var dialog = new OpenFileDialog { Title = "Choose quest image", Filter = "Images|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string name = $"quest_{Quest.Id}.png";
        using (var source = Image.FromFile(dialog.FileName))
        using (var copy = new Bitmap(source))
            copy.Save(Path.Combine(Current.Folder, name), ImageFormat.Png);
        Quest.Image = name;
        MarkDirty();
        SetStatus($"Quest image saved as {name}");
    }

    private static void SaveSquareImage(string source, string target, int size)
    {
        using var input = Image.FromFile(source);
        using var output = new Bitmap(size, size);
        using (var g = Graphics.FromImage(output))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Black);
            float scale = Math.Max((float)size / input.Width, (float)size / input.Height);
            float w = input.Width * scale, h = input.Height * scale;
            g.DrawImage(input, (size - w) / 2, (size - h) / 2, w, h); // crop to fill
        }
        string temp = target + ".tmp";
        output.Save(temp, ImageFormat.Png);
        File.Move(temp, target, overwrite: true);
    }

    private static void SavePlaceholderAvatar(string path, string name)
    {
        using var bmp = new Bitmap(256, 256);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(45, 48, 52));
            using var pen = new Pen(Color.FromArgb(150, 150, 150), 8);
            g.DrawRectangle(pen, 4, 4, 247, 247);
            using var font = new Font(FontFamily.GenericSansSerif, 34, FontStyle.Bold);
            var text = name.Length > 8 ? name[..8] : name;
            var sizeF = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.Gainsboro, (256 - sizeF.Width) / 2, (256 - sizeF.Height) / 2);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    // ------------------------------------------------------------------ load / save

    private void BrowseModFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the CustomTraders mod folder (…\\SPT_Runtime\\user\\mods\\CustomTraders)",
            UseDescriptionForTitle = true,
        };
        if (_modFolder != null) dialog.InitialDirectory = _modFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!ConfirmDiscard()) return;
        LoadModFolder(dialog.SelectedPath);
    }

    private void LoadModFolder(string? folder)
    {
        _traders.Clear();
        _modFolder = null;
        _modFolderBox.Text = "";
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            RefreshTraderList();
            SetStatus("Click Browse... and select your …\\SPT_Runtime\\user\\mods\\CustomTraders folder.");
            return;
        }

        _modFolder = folder;
        _modFolderBox.Text = folder;
        SaveFolderSetting(folder);

        string tradersFolder = Path.Combine(folder, "traders");
        Directory.CreateDirectory(tradersFolder);
        foreach (var dir in Directory.GetDirectories(tradersFolder).OrderBy(d => d))
        {
            string file = Path.Combine(dir, "trader.json");
            if (!File.Exists(file)) continue;
            try { _traders.Add(new TraderEntry(dir, TraderFile.Load(file))); }
            catch (Exception e) { MessageBox.Show(this, $"Could not read {file}:\n{e.Message}", "Load error"); }
        }

        LoadItemDatabase(folder);
        RefreshTraderList();
        SetStatus($"Loaded {_traders.Count} trader(s). Restart the SPT server after saving to apply changes.");
    }

    private void LoadItemDatabase(string modFolder)
    {
        try
        {
            var dbFolder = ItemDatabase.FindDatabaseFolder(modFolder);
            if (dbFolder == null)
            {
                _dbStatus.Text = "Item database not found (item names unavailable)";
                return;
            }
            Cursor = Cursors.WaitCursor;
            _db.Load(dbFolder);
            _dbStatus.Text = $"{_db.Items.Count:N0} items";
        }
        catch (Exception e)
        {
            _dbStatus.Text = "Item database failed to load";
            MessageBox.Show(this, e.Message, "Item database");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SaveAll()
    {
        int saved = 0;
        foreach (var entry in _traders.Where(t => t.Dirty))
        {
            var problems = Validate(entry.File);
            if (problems.Count > 0 &&
                MessageBox.Show(this, $"{entry.File.Name} has problems:\n\n- {string.Join("\n- ", problems)}\n\nSave anyway?",
                    "Check before saving", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                continue;

            entry.File.Save(Path.Combine(entry.Folder, "trader.json"));
            entry.Dirty = false;
            saved++;
        }
        RefreshTraderList(Current);
        SetStatus(saved == 0 ? "Nothing to save." : $"Saved {saved} trader(s). Restart the SPT server to apply.");
    }

    private List<string> Validate(TraderFile file)
    {
        var problems = new List<string>();
        foreach (var o in file.Offers)
        {
            if (!Ids.IsValid(o.ItemTpl)) problems.Add($"An offer has no item.");
            if (o.Cost.Count == 0) problems.Add($"Offer {_db.NameOf(o.ItemTpl)} has no cost.");
        }
        foreach (var q in file.Quests)
        {
            if (q.Conditions.Count == 0) problems.Add($"Quest \"{q.Name}\" has no objectives (it completes instantly).");
            foreach (var c in q.Conditions.Where(c => c.Type != ConditionTypes.Kill && c.ItemTpls.Count == 0))
                problems.Add($"Quest \"{q.Name}\": a {c.Type} objective has no items.");
            foreach (var r in q.Rewards.Where(r => r.Type == RewardTypes.UnlockOffer && file.Offers.All(o => o.Id != r.OfferId)))
                problems.Add($"Quest \"{q.Name}\": an UnlockOffer reward has no offer picked.");
            foreach (var r in q.Rewards.Where(r => r.Type == RewardTypes.Item && !Ids.IsValid(r.ItemTpl)))
                problems.Add($"Quest \"{q.Name}\": an Item reward has no item picked.");
        }
        return problems;
    }

    // ------------------------------------------------------------------ misc

    private void MarkDirty()
    {
        if (Current == null || Current.Dirty) return;
        Current.Dirty = true;
        int index = _traderList.SelectedIndex;
        _traderList.Items[index] = _traderList.Items[index]; // redraw the "*"
    }

    private void RefreshTraderList(TraderEntry? select = null)
    {
        select ??= Current;
        _traderList.Items.Clear();
        foreach (var t in _traders) _traderList.Items.Add(t);
        if (select != null && _traders.Contains(select)) _traderList.SelectedItem = select;
        else if (_traders.Count > 0) _traderList.SelectedIndex = 0;
        else ShowTrader();
    }

    private bool ConfirmDiscard()
    {
        if (!_traders.Any(t => t.Dirty)) return true;
        return MessageBox.Show(this, "You have unsaved changes. Discard them?", "Unsaved changes",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_traders.Any(t => t.Dirty)) return;
        var answer = MessageBox.Show(this, "Save changes before closing?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) e.Cancel = true;
        else if (answer == DialogResult.Yes) SaveAll();
    }

    private void SetStatus(string text) => _status.Text = text;

    private static string? ReadSavedFolder()
    {
        try { return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null; }
        catch { return null; }
    }

    private static void SaveFolderSetting(string folder)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, folder);
        }
        catch
        {
            // remembering the folder is a convenience only
        }
    }
}
