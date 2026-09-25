using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using CustomTraders.Shared;

namespace CustomTraders.Editor;

/// <summary>
/// The editor window, laid out like Spotify:
///   top bar     — mod folder "search pill", background picture, checks
///   left        — your traders (like playlists)
///   middle      — big header of the trader + page chips + a numbered list
///   right       — details of whatever is selected in the middle list
///   bottom bar  — status, check summary, Save all
/// </summary>
public sealed class MainForm : Form
{
    private enum Page { Trader, Offers, Quests, Checks }

    private readonly Settings _settings = Settings.Load();
    private readonly ItemDatabase _db = new();
    private readonly List<TraderEntry> _traders = new();
    private readonly Dictionary<TraderEntry, Image> _avatars = new();
    private readonly Dictionary<string, (DateTime Written, Image Image)> _questImages = new();
    private readonly PictureBox _questImagePreview = new() { Height = 150, SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
    private readonly PillButton _removeQuestImage = new("Remove image", PillStyle.Danger) { Height = 30, Visible = false };
    private Control _root = null!;
    private Control _rightPanel = null!;
    private readonly Crossfader _fade;
    private int _freeze;
    private readonly List<CheckEntry> _history = new();
    private List<CheckEntry> _checks = new();
    private string? _modFolder;
    private Page _page = Page.Trader;
    private Image? _backgroundSource;
    private readonly System.Windows.Forms.Timer _backgroundTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer _checkTimer = new() { Interval = 1200 };

    // chrome
    private readonly OneLineLabel _folderLabel = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.Body };
    private readonly Label _dbLabel = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Caption, Padding = new Padding(0, 10, 8, 0) };
    private readonly PillButton _checksButton = new("Checks", PillStyle.Outline);
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, AutoEllipsis = true };
    private readonly Label _checkSummary = new() { AutoSize = true, Font = Theme.BodyBold, Padding = new Padding(0, 12, 16, 0), Cursor = Cursors.Hand };
    private readonly Label _unsaved = new() { AutoSize = true, ForeColor = Theme.Pink, Font = Theme.BodyBold, Padding = new Padding(0, 12, 8, 0) };

    // left
    private readonly RowList _traderList;

    // middle
    private readonly HeaderPanel _center = new();
    private readonly PictureBox _headerAvatar = new() { Size = new Size(128, 128), Location = new Point(24, 22), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
    private readonly Label _headerKind = new() { Text = "TRADER", AutoSize = true, Font = Theme.Small, ForeColor = Theme.Text, Location = new Point(172, 40) };
    private readonly Label _headerTitle = new() { AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Location = new Point(164, 58) };
    private readonly Label _headerSub = new() { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, Location = new Point(170, 118) };
    private readonly Dictionary<Page, PillButton> _chips = new();
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(12, 0, 12, 8) };
    private readonly Dictionary<Page, Control> _pages = new();

    // right
    private readonly Label _detailsTitle = new() { Dock = DockStyle.Top, Height = 44, Font = Theme.PanelTitle, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(4, 0, 0, 0) };
    private readonly StackPanel _details = new() { Dock = DockStyle.Fill, AutoScroll = true, Gap = 10, Padding = new Padding(0, 4, 8, 12) };
    private readonly Dictionary<Page, StackPanel> _detailPages = new();

    // trader page
    private readonly FieldPanel _traderFields = new(200) { Font = Theme.Big };
    private readonly DataGridView _loyalty = new() { Height = 205, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersWidth = 62, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Font = Theme.Big, ColumnHeadersHeight = 36 };
    private readonly PictureBox _bigAvatar = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Card, Height = 300 };
    private readonly Hint _traderSummary = new("");

    // offers
    private readonly ListEditor<OfferDef> _offers;
    private readonly FieldPanel _offerFields = new();
    private readonly ListEditor<CostDef> _costs;
    private readonly FieldPanel _costFields = new();
    private readonly Section _offerStatus = new("");
    private readonly Hint _offerStatusText = new("");
    private readonly PillButton _offerGoToQuest = new("Go to quest", PillStyle.Outline) { Height = 30 };

    // quests
    private readonly ListEditor<QuestDef> _quests;
    private readonly FieldPanel _questFields = new();
    private readonly Section _requirements = new("Unlock requirements", Theme.Violet);
    private readonly Hint _requirementsText = new("");
    private readonly StackPanel _prereqs = new() { Gap = 2 };
    private readonly ListEditor<ConditionDef> _conditions;
    private readonly FieldPanel _conditionFields = new(140);
    private readonly Section _objectiveSection = new("Objective");
    private readonly ItemListPanel _condItems;
    private readonly CheckListPanel _condBosses;
    private readonly Toggle _needWeapon = new() { Text = "Must kill with a specific weapon or grenade" };
    private readonly ItemListPanel _condWeapons;
    private readonly Toggle _needCaliber = new() { Text = "Must use specific ammo (caliber)" };
    private readonly ItemListPanel _condCalibers;
    private readonly Toggle _needWearing = new() { Text = "Must be wearing something" };
    private readonly ItemListPanel _condWearing;
    private readonly Toggle _needMaps = new() { Text = "Only on specific maps" };
    private readonly CheckListPanel _condMaps;
    private readonly Hint _waysHint = new("");
    private readonly ListEditor<RewardDef> _rewards;
    private readonly FieldPanel _rewardFields = new(140);
    private readonly Dictionary<ConditionDef, HashSet<string>> _expanded = new(ReferenceEqualityComparer.Instance);
    private bool _loadingChecks;

    // checks
    private readonly RowList _checkList;
    private CheckLevel? _checkFilter;
    private readonly List<(CheckLevel? Level, PillButton Chip)> _checkFilterChips = new();
    private readonly Hint _checkDetail = new("Select a line to see it in full.");
    private readonly PillButton _checkGoTo = new("Go to it", PillStyle.Primary);

    private TraderEntry? Current => _traderList.SelectedItem as TraderEntry;
    private TraderFile? Trader => Current?.File;
    private OfferDef? Offer => _offers.Selected;
    private CostDef? Cost => _costs.Selected;
    private QuestDef? Quest => _quests.Selected;
    private ConditionDef? Condition => _conditions.Selected;
    private RewardDef? Reward => _rewards.Selected;

    public MainForm()
    {
        _fade = new Crossfader(this);
        if (_settings.AccentColor is { Length: 7 } hex && hex[0] == '#')
        {
            try { Theme.Accent = ColorTranslator.FromHtml(hex); }
            catch { /* bad value in settings: keep green */ }
        }
        Text = "Trader Editor";
        Width = 1640;
        Height = 1000;
        MinimumSize = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;
        KeyPreview = true;
        Padding = new Padding(8);

        _traderList = new RowList(o => DescribeTrader((TraderEntry)o), showIndex: false, header: false) { Dock = DockStyle.Fill };
        _traderList.EmptyText = "No traders yet — click +";

        _offers = new ListEditor<OfferDef>(() => Trader?.Offers, NewOffer, DescribeOffer, "+ Add offer", DuplicateOffer,
            new[] { new Column("Unlock", 210), new Column("LL", 60), new Column("Stock", 110) }) { Dock = DockStyle.Fill };
        _offers.EmptyText = "No offers yet — click + Add offer";
        _costs = new ListEditor<CostDef>(() => Offer?.Cost,
            () => new CostDef { ItemTpl = Currencies.Roubles, Count = 10000 }, DescribeCost, "+ Barter item",
            c => new CostDef { ItemTpl = c.ItemTpl, Count = c.Count }, compact: true, height: 200);
        _quests = new ListEditor<QuestDef>(() => Trader?.Quests,
            () => new QuestDef { Id = Ids.New(), Name = "New quest" }, DescribeQuest, "+ Add quest", DuplicateQuest,
            new[] { new Column("Level", 70), new Column("Ways", 60) }, lines: 3) { Dock = DockStyle.Fill };
        _quests.EmptyText = "No quests yet — click + Add quest";
        _conditions = new ListEditor<ConditionDef>(() => Quest?.Conditions, NewCondition, DescribeCondition, "+ Objective",
            DuplicateCondition, compact: true, height: 250);
        _rewards = new ListEditor<RewardDef>(() => Quest?.Rewards,
            () => new RewardDef { Id = Ids.New(), Type = RewardTypes.Experience, Value = 1000 }, DescribeReward, "+ Reward",
            r => Clone(r, x => x.Id = Ids.New()), compact: true, height: 200);

        _condItems = new ItemListPanel("Items", () => Condition?.ItemTpls,
            owner => ItemPickerDialog.Pick(owner, _db, Condition?.Type == ConditionTypes.UseItem ? ItemFilter.Medical : ItemFilter.All),
            DescribeItem, Theme.Blue);
        _condWeapons = new ItemListPanel("Weapons / grenades — any one of these counts", () => Condition?.WeaponTpls,
            owner => ItemPickerDialog.Pick(owner, _db, ItemFilter.WeaponsAndGrenades), DescribeItem, Theme.Red, 130);
        _condCalibers = new ItemListPanel("Ammo calibers — pick any bullet of the caliber", () => Condition?.Calibers,
            PickCaliber, cal => new Row { Title = ItemDatabase.CaliberName(cal), Subtitle = cal, ThumbText = "•", ThumbColor = Theme.Yellow }, Theme.Yellow, 110);
        _condWearing = new ItemListPanel("Wearing — any one of these counts", () => Condition?.WearingTpls,
            owner => ItemPickerDialog.Pick(owner, _db, ItemFilter.Gear), DescribeItem, Theme.Orange, 130);
        _condBosses = new CheckListPanel("Which bosses count (none ticked = any boss)", KillTargets.Bosses.Select(b => (b.Role, b.Name)).ToArray(),
            () => Condition?.BossRoles, Theme.Red, 150);
        _condMaps = new CheckListPanel("Maps (any of the ticked)", Maps.All, () => Condition?.Locations, Theme.Green, 150);

        _checkList = new RowList(o => DescribeCheck((CheckEntry)o), new[] { new Column("When", 90) }) { Dock = DockStyle.Fill };
        _checkList.EmptyText = "Nothing to show";

        BuildChrome();
        BuildTraderPage();
        BuildOffersPage();
        BuildQuestsPage();
        BuildChecksPage();
        RememberColumns("offers", _offers.Rows);
        RememberColumns("quests", _quests.Rows);
        RememberColumns("checks", _checkList);
        Theme.Apply(this);
        ShowPage(Page.Trader);

        _traderList.SelectionChanged += () => _fade.Run(_root, ShowTrader);
        _checkTimer.Tick += (_, _) => { _checkTimer.Stop(); RunChecks(); };
        _backgroundTimer.Tick += (_, _) => { _backgroundTimer.Stop(); RenderBackground(); };
        Resize += (_, _) => { if (_backgroundSource != null) _backgroundTimer.Start(); };
        FormClosing += OnClosing;
        HandleCreated += (_, _) => Theme.DarkTitleBar(this);
        Shown += (_, _) =>
        {
            using (Freeze())
            {
                CreateAllHandles();
                LoadBackground();
                LoadModFolder(_settings.ModFolder);
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { SaveAll(); e.SuppressKeyPress = true; }
        };
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private void BuildChrome()
    {
        // --- top bar ----------------------------------------------------------
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 66, ColumnCount = 5, RowCount = 1, Padding = new Padding(4, 4, 4, 10) };
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 640));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var logo = new Label { Text = "Trader Editor", Font = new Font("Segoe UI Black", 16f), ForeColor = Theme.Text, AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
        top.Controls.Add(logo, 0, 0);

        // Mod folder in a Spotify-search-style pill.
        var pill = new GlassPanel { Dock = DockStyle.Fill, Radius = 24, Tint = Color.FromArgb(240, 36, 36, 36), Padding = new Padding(22, 6, 8, 6), Margin = new Padding(0) };
        var browse = new PillButton("Browse...", PillStyle.Ghost) { Dock = DockStyle.Right, Height = 36 };
        var reload = new PillButton("Reload", PillStyle.Ghost) { Dock = DockStyle.Right, Height = 36 };
        pill.Controls.Add(_folderLabel);
        pill.Controls.Add(reload);
        pill.Controls.Add(browse);
        browse.Click += (_, _) => BrowseModFolder();
        reload.Click += (_, _) => { if (ConfirmDiscard()) LoadModFolder(_modFolder); };
        top.Controls.Add(pill, 2, 0);

        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var background = new PillButton("Appearance", PillStyle.Outline);
        background.Click += (_, _) => BackgroundMenu().Show(background, new Point(0, background.Height + 4));
        _checksButton.Click += (_, _) => { if (_page != Page.Checks) _fade.Run(_root, () => ShowPage(Page.Checks)); };
        right.Controls.AddRange(new Control[] { _dbLabel, background, _checksButton });
        top.Controls.Add(right, 4, 0);

        // --- bottom bar ---------------------------------------------------------
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 64, ColumnCount = 4, Padding = new Padding(8, 10, 8, 4) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var save = new PillButton("Save all", PillStyle.Primary) { Height = 42, Width = 140, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
        save.Click += (_, _) => SaveAll();
        new ToolTip().SetToolTip(save, "Save every changed trader (Ctrl+S). Restart the SPT server afterwards.");
        _checkSummary.Click += (_, _) => { if (_page != Page.Checks) _fade.Run(_root, () => ShowPage(Page.Checks)); };
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_checkSummary, 1, 0);
        bottom.Controls.Add(_unsaved, 2, 0);
        bottom.Controls.Add(save, 3, 0);

        // --- three panels ---------------------------------------------------------
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _root = grid;
        grid.Controls.Add(BuildLeft(), 0, 0);
        grid.Controls.Add(BuildCenter(), 1, 0);
        grid.Controls.Add(BuildRight(), 2, 0);

        Controls.Add(grid);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    private Control BuildLeft()
    {
        var panel = new GlassPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), Padding = new Padding(8, 8, 8, 8), Tint = Theme.Surface };
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, ColumnCount = 3 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label { Text = "Your traders", Font = Theme.Heading, ForeColor = Theme.Text, AutoSize = true, Padding = new Padding(6, 10, 0, 0) };
        var add = PillButton.Round("+", 36, PillStyle.Chip, "New trader");
        var delete = PillButton.Round("✕", 36, PillStyle.Ghost, "Remove the selected trader (moved to deleted_traders, not erased)");
        add.Click += (_, _) => NewTrader();
        delete.Click += (_, _) => DeleteTrader();
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(add, 1, 0);
        header.Controls.Add(delete, 2, 0);

        var chip = new PillButton("Traders", PillStyle.Chip) { Selected = true, Height = 30, Margin = new Padding(6, 4, 0, 8) };
        var chipRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
        chipRow.Controls.Add(chip);

        panel.Controls.Add(_traderList);
        panel.Controls.Add(chipRow);
        panel.Controls.Add(header);
        return panel;
    }

    private Control BuildCenter()
    {
        _center.Dock = DockStyle.Fill;
        _center.Margin = new Padding(4, 0, 4, 0);
        _center.Padding = new Padding(0, 0, 0, 0);
        _center.Tint = Theme.Surface;

        var header = new Panel { Dock = DockStyle.Top, Height = 172, BackColor = Color.Transparent };
        header.Controls.AddRange(new Control[] { _headerAvatar, _headerKind, _headerTitle, _headerSub });

        // Big green round button (Spotify's play) = Save; then the page chips.
        var chips = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(20, 6, 0, 6), WrapContents = false };
        var play = PillButton.Round("✓", 52, PillStyle.Primary, "Save all (Ctrl+S)");
        play.Margin = new Padding(0, 0, 18, 0);
        play.Click += (_, _) => SaveAll();
        chips.Controls.Add(play);
        foreach (var (page, text) in new[] { (Page.Trader, "Trader"), (Page.Offers, "Offers & barters"), (Page.Quests, "Quests"), (Page.Checks, "Checks & log") })
        {
            var chip = new PillButton(text, PillStyle.Chip) { Height = 34, Margin = new Padding(0, 9, 8, 0) };
            chip.Click += (_, _) => { if (_page != page) _fade.Run(_root, () => ShowPage(page)); };
            _chips[page] = chip;
            chips.Controls.Add(chip);
        }

        _center.Controls.Add(_pageHost);
        _center.Controls.Add(chips);
        _center.Controls.Add(header);
        return _center;
    }

    private Control BuildRight()
    {
        var panel = new GlassPanel { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), Padding = new Padding(14, 8, 6, 8), Tint = Theme.Surface };
        _rightPanel = panel;
        panel.Controls.Add(_details);
        panel.Controls.Add(_detailsTitle);
        return panel;
    }

    private void AddPage(Page page, Control center, StackPanel details)
    {
        center.Dock = DockStyle.Fill;
        center.Visible = false;
        _pageHost.Controls.Add(center);
        _pages[page] = center;
        details.Visible = false;
        _details.Controls.Add(details);
        _detailPages[page] = details;
    }

    private void ShowPage(Page page)
    {
        using var _ = Freeze();
        _page = page;
        foreach (var (p, c) in _pages) c.Visible = p == page;
        foreach (var (p, d) in _detailPages) d.Visible = p == page;
        foreach (var (p, chip) in _chips) chip.Selected = p == page;
        _details.AutoScrollPosition = Point.Empty;
        _details.Relayout();
        UpdateDetailsTitle();
    }

    private void UpdateDetailsTitle()
    {
        _detailsTitle.Text = _page switch
        {
            Page.Trader => Trader?.Name.ToUpperInvariant() ?? "",
            Page.Offers => Offer != null ? _db.NameOf(Offer.ItemTpl) : "No offer selected",
            Page.Quests => Quest?.Name ?? "No quest selected",
            _ => "Details",
        };
    }

    // ---------------------------------------------------------------- trader page

    private void BuildTraderPage()
    {
        var f = _traderFields;
        f.AddCheck("In the game", () => Trader?.Enabled ?? true, v => Trader!.Enabled = v, "Trader is ON (off = the server skips it; nothing is deleted)");
        f.AddText("Name", () => Trader?.Name, v => Trader!.Name = v);
        f.AddText("Nickname", () => Trader?.Nickname, v => Trader!.Nickname = v);
        f.AddText("Surname", () => Trader?.Surname, v => Trader!.Surname = v);
        f.AddText("Location", () => Trader?.Location, v => Trader!.Location = v);
        f.AddText("Description", () => Trader?.Description, v => Trader!.Description = v, multiline: true, height: 110);
        f.AddChips("Currency", new[] { ("RUB", "₽ Roubles", Theme.Text), ("USD", "$ Dollars", Theme.Text), ("EUR", "€ Euros", Theme.Text) },
            () => Trader?.Currency, v => Trader!.Currency = v);
        f.AddCheck("Unlocked", () => Trader?.UnlockedByDefault ?? true, v => Trader!.UnlockedByDefault = v, "Available from the start");
        f.AddCheck("Flea market", () => Trader?.ListOnFlea ?? true, v => Trader!.ListOnFlea = v, "List this trader's offers on the flea");
        f.AddNumber("Restock every (min)", () => Trader?.RefreshMinutesMin ?? 60, v => Trader!.RefreshMinutesMin = (int)v, 1, 10080);
        f.AddNumber("...up to (min)", () => Trader?.RefreshMinutesMax ?? 120, v => Trader!.RefreshMinutesMax = (int)v, 1, 10080);
        f.Changed += () => { MarkDirty(); UpdateHeader(); _traderList.Redraw(); };

        // Only real cell edits (not the "LL1".."LL4" row labels set below).
        _loyalty.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) MarkDirty(); };
        _loyalty.DataError += (_, e) => { e.ThrowException = false; };
        _loyalty.DataBindingComplete += (_, _) =>
        {
            var names = new Dictionary<string, string>
            {
                [nameof(LoyaltyLevelDef.MinLevel)] = "Player level",
                [nameof(LoyaltyLevelDef.MinSalesSum)] = "Spent (₽)",
                [nameof(LoyaltyLevelDef.MinStanding)] = "Standing",
                [nameof(LoyaltyLevelDef.BuyPriceCoef)] = "Buys at %",
            };
            foreach (DataGridViewColumn column in _loyalty.Columns)
                if (names.TryGetValue(column.DataPropertyName, out var n)) column.HeaderText = n;
            for (int i = 0; i < _loyalty.Rows.Count; i++) _loyalty.Rows[i].HeaderCell.Value = $"LL{i + 1}";
        };

        var page = new StackPanel { AutoScroll = true, Gap = 12, Padding = new Padding(8, 4, 12, 12) };
        page.AddRange(
            new Section("Trader").Add(f),
            new Section("Loyalty levels").Add(new Hint("What a player needs for each loyalty level (LL1 – LL4). Offers can require a loyalty level."), _loyalty));

        var choose = new PillButton("Choose icon from PC...", PillStyle.Primary) { Height = 40 };
        choose.Click += (_, _) => ChooseAvatar();
        var details = new StackPanel { Gap = 10 };
        details.AddRange(
            _bigAvatar,
            new Toolbar(choose),
            new Hint("Any png or jpg; it's cropped to a square. The game shows the new icon after a server restart."),
            new Section("Summary").Add(_traderSummary));
        _details.SizeChanged += (_, _) => _bigAvatar.Height = Math.Max(160, _details.ClientSize.Width - 20);

        AddPage(Page.Trader, page, details);
    }

    // ---------------------------------------------------------------- offers

    private void BuildOffersPage()
    {
        var o = _offerFields;
        o.AddItem("Item", _db, () => Offer?.ItemTpl, v => Offer!.ItemTpl = v, ItemFilter.Weapons);
        o.AddCheck("Weapon preset", () => Offer?.UseDefaultPreset ?? true, v => Offer!.UseDefaultPreset = v, "Sell the assembled gun");
        o.AddNumber("Loyalty level", () => Offer?.LoyaltyLevel ?? 1, v => Offer!.LoyaltyLevel = (int)v, 1, 4);
        o.AddCheck("Stock", () => Offer?.Unlimited ?? true, v => Offer!.Unlimited = v, "Unlimited");
        o.AddNumber("Stock per restock", () => Offer?.Stock ?? 1, v => Offer!.Stock = (int)v, 1, 100000);
        o.ShowWhen(() => Offer is { Unlimited: false });
        o.AddNumber("Buy limit / restock", () => Offer?.BuyLimit ?? 0, v => Offer!.BuyLimit = (int)v, 0, 100000);
        o.Changed += () => { MarkDirty(); _offers.RefreshTexts(); UpdateDetailsTitle(); _offerFields.RefreshVisibility(); };

        var c = _costFields;
        c.AddItem("Pay with", _db, () => Cost?.ItemTpl, v => Cost!.ItemTpl = v);
        c.AddNumber("Amount", () => (decimal)(Cost?.Count ?? 1), v => Cost!.Count = (double)v, 1, 100000000);
        c.Changed += () => { MarkDirty(); _costs.RefreshTexts(); _offers.RefreshTexts(); };

        var quick = new Toolbar();
        foreach (var (label, tpl, amount) in new[] { ("+ ₽ Roubles", Currencies.Roubles, 50000), ("+ $ Dollars", Currencies.Dollars, 500), ("+ € Euros", Currencies.Euros, 500) })
        {
            var b = new PillButton(label, PillStyle.Chip) { Height = 30 };
            b.Click += (_, _) =>
            {
                if (Offer == null) return;
                var existing = Offer.Cost.FirstOrDefault(x => x.ItemTpl == tpl);
                var cost = existing ?? new CostDef { ItemTpl = tpl, Count = amount };
                if (existing == null) Offer.Cost.Add(cost);
                _costs.RefreshList(cost);
                MarkDirty();
                _offers.RefreshTexts();
            };
            quick.Controls.Add(b);
        }

        _offerGoToQuest.Click += (_, _) =>
        {
            var quest = Trader?.Quests.FirstOrDefault(q => q.Rewards.Any(r => r.Type == RewardTypes.UnlockOffer && r.OfferId == Offer?.Id));
            if (quest != null) GoTo(Current, quest);
        };
        _offerStatus.AddAction(_offerGoToQuest);
        _offerStatus.Add(_offerStatusText);

        _offers.SelectionChanged += () => _fade.Run(_rightPanel, ShowOffer);
        _offers.ListChanged += () => { MarkDirty(); ShowOffer(); UpdateHeader(); };
        _costs.SelectionChanged += () => { _costFields.Enabled = Cost != null; _costFields.RefreshValues(); };
        _costs.ListChanged += () => { MarkDirty(); _offers.RefreshTexts(); };

        var details = new StackPanel { Gap = 10 };
        details.AddRange(
            _offerStatus,
            new Section("Item").Add(o),
            new Section("Price / barter").Add(
                new Hint("Everything listed is needed to buy it: money and/or barter items."),
                _costs, quick, c));

        AddPage(Page.Offers, _offers, details);
    }

    // ---------------------------------------------------------------- quests

    private void BuildQuestsPage()
    {
        var q = _questFields;
        q.AddText("Name", () => Quest?.Name, v => Quest!.Name = v);
        q.AddNumber("Unlocks at level", () => Quest?.MinLevel ?? 1, v => Quest!.MinLevel = (int)v, 1, 79);
        q.AddText("Description", () => Quest?.Description, v => Quest!.Description = v, multiline: true, height: 90);
        q.AddText("When completed", () => Quest?.SuccessMessage, v => Quest!.SuccessMessage = v, multiline: true, height: 50);
        var image = new PillButton("Choose quest image...", PillStyle.Outline) { Height = 32, Dock = DockStyle.Left };
        image.Click += (_, _) => ChooseQuestImage();
        q.AddRow("Image", image);
        _removeQuestImage.Click += (_, _) =>
        {
            if (Quest == null) return;
            Quest.Image = null;
            MarkDirty();
            ShowQuestImage();
            _quests.RefreshTexts();
        };
        q.Changed += () => { MarkDirty(); _quests.RefreshTexts(); UpdateDetailsTitle(); ShowRequirements(); };


        // --- objective editor: looks different per type -------------------------------
        var cf = _conditionFields;
        cf.AddChips("Type", ConditionTypes.All.Select(t => (t, ShortTypeLabel(t), Theme.ForConditionType(t))).ToArray(),
            () => Condition?.Type, v => Condition!.Type = v);
        cf.AddChips("Way (option)", Enumerable.Range(1, 4).Select(i => (i.ToString(), QuestDef.OptionLetter(i), Theme.ForOption(i))).ToArray(),
            () => Condition?.Option.ToString(), v => Condition!.Option = int.Parse(v));
        cf.AddCombo("Kill who", KillTargets.All, () => Condition?.KillTarget, v => Condition!.KillTarget = v);
        cf.ShowWhen(() => Condition?.Type == ConditionTypes.Kill);
        cf.AddNumber("Amount", () => Condition?.Count ?? 1, v => Condition!.Count = (int)v, 1, 100000000);
        cf.LabelFrom(() => Condition?.Type switch
        {
            ConditionTypes.Kill => "How many kills",
            ConditionTypes.Extract => "How many extracts",
            ConditionTypes.UseItem => "How many uses",
            ConditionTypes.FindItem => "How many to find",
            _ when Condition != null && IsMoneyList(Condition.ItemTpls) => "How much to pay",
            _ => "How many",
        });
        cf.AddCheck("Found in raid", () => Condition?.FoundInRaid ?? true, v => Condition!.FoundInRaid = v, "Items must be found in raid");
        cf.ShowWhen(() => Condition?.Type is ConditionTypes.HandoverItem or ConditionTypes.FindItem && !IsMoneyList(Condition.ItemTpls));
        cf.AddText("Custom text", () => Condition?.Text, v => Condition!.Text = v);
        cf.Changed += OnConditionChanged;

        foreach (var (label, tpl) in new[] { ("₽", Currencies.Roubles), ("$", Currencies.Dollars), ("€", Currencies.Euros), ("GP coin", Currencies.GpCoin), ("Lega medal", Currencies.LegaMedal) })
            _condItems.AddQuick("+ " + label, tpl, Theme.Yellow);
        foreach (var list in new[] { _condItems, _condWeapons, _condCalibers, _condWearing })
            list.Changed += OnConditionChanged;
        _condBosses.Changed += OnConditionChanged;
        _condMaps.Changed += OnConditionChanged;

        HookRequirement(_needWeapon, "weapon", () => Condition?.WeaponTpls);
        HookRequirement(_needCaliber, "caliber", () => Condition?.Calibers);
        HookRequirement(_needWearing, "wearing", () => Condition?.WearingTpls);
        HookRequirement(_needMaps, "maps", () => Condition?.Locations);

        _objectiveSection.Add(cf, _condItems, _condBosses,
            _needWeapon, _condWeapons, _needCaliber, _condCalibers,
            _needWearing, _condWearing, _needMaps, _condMaps,
            new Hint("A kill can require a weapon/grenade AND worn gear at the same time. Weapons listed together are \"any one of\"."));

        // --- rewards -------------------------------------------------------------------
        var rf = _rewardFields;
        rf.AddChips("Type", new[]
        {
            (RewardTypes.Experience, "XP", Theme.Violet),
            (RewardTypes.TraderStanding, "Standing", Theme.Blue),
            (RewardTypes.Item, "Item", Theme.Orange),
            (RewardTypes.UnlockOffer, "Unlock offer", Theme.Green),
        }, () => Reward?.Type, v => Reward!.Type = v);
        rf.AddNumber("XP", () => (decimal)(Reward?.Value ?? 0), v => Reward!.Value = (double)v, 0, 100000000);
        rf.ShowWhen(() => Reward?.Type == RewardTypes.Experience);
        rf.AddNumber("Standing", () => (decimal)(Reward?.Value ?? 0), v => Reward!.Value = (double)v, -1, 1, 2, 0.01m);
        rf.ShowWhen(() => Reward?.Type == RewardTypes.TraderStanding);
        rf.AddItem("Item", _db, () => Reward?.ItemTpl, v => Reward!.ItemTpl = v);
        rf.ShowWhen(() => Reward?.Type == RewardTypes.Item);
        rf.AddNumber("How many", () => Reward?.Count ?? 1, v => Reward!.Count = (int)v, 1, 100000);
        rf.ShowWhen(() => Reward?.Type == RewardTypes.Item);
        rf.AddCheck("Found in raid", () => Reward?.FoundInRaid ?? true, v => Reward!.FoundInRaid = v);
        rf.ShowWhen(() => Reward?.Type == RewardTypes.Item);
        rf.AddCombo("Offer", () => (Trader?.Offers ?? new()).Select(o => (o.Id, OfferText(o))), () => Reward?.OfferId, v => Reward!.OfferId = v);
        rf.ShowWhen(() => Reward?.Type == RewardTypes.UnlockOffer);
        rf.AddNumber("Stock after unlock", () => Reward?.Quantity ?? 0, v => Reward!.Quantity = (int)v, 0, 100000);
        rf.ShowWhen(() => Reward?.Type == RewardTypes.UnlockOffer);
        rf.Changed += () => { MarkDirty(); _rewards.RefreshTexts(); _offers.RefreshTexts(); _quests.RefreshTexts(); _rewardFields.RefreshVisibility(); };

        _quests.SelectionChanged += () => _fade.Run(_rightPanel, ShowQuest);
        _quests.ListChanged += () => { MarkDirty(); ShowQuest(); UpdateHeader(); };
        _conditions.SelectionChanged += ShowCondition;
        _conditions.ListChanged += () => { MarkDirty(); ShowCondition(); _quests.RefreshTexts(); UpdateWaysHint(); };
        _rewards.SelectionChanged += () => { _rewardFields.Enabled = Reward != null; _rewardFields.RefreshValues(); };
        _rewards.ListChanged += () => { MarkDirty(); _offers.RefreshTexts(); _quests.RefreshTexts(); };

        _requirements.Add(_requirementsText);
        var details = new StackPanel { Gap = 10 };
        details.AddRange(
            _requirements,
            new Section("Quest").Add(q, _questImagePreview, new Toolbar(_removeQuestImage)),
            new Section("Required quests").Add(new Hint(
                "Switch on every quest (any trader) that must be finished first. For a quest with several ways (A, B, C...) " +
                "ANY one finished way counts — the editor and the server handle all its ways for you."), _prereqs),
            new Section("Objectives", Theme.Text).Add(_waysHint, _conditions),
            _objectiveSection,
            new Section("Rewards").Add(new Hint("Every way (option) gives the same rewards."), _rewards, rf));

        AddPage(Page.Quests, _quests, details);
    }

    private static string ShortTypeLabel(string type) => type switch
    {
        ConditionTypes.HandoverItem => "Hand over",
        ConditionTypes.FindItem => "Find",
        ConditionTypes.Kill => "Kill",
        ConditionTypes.Extract => "Extract",
        ConditionTypes.UseItem => "Use item",
        _ => type,
    };

    /// <summary>A "must ..." tick box that shows its list; unticking clears the list.</summary>
    private void HookRequirement(CheckBox box, string key, Func<List<string>?> list)
    {
        box.CheckedChanged += (_, _) =>
        {
            if (_loadingChecks || Condition == null) return;
            if (!_expanded.TryGetValue(Condition, out var set)) _expanded[Condition] = set = new HashSet<string>();
            if (box.Checked) set.Add(key);
            else
            {
                set.Remove(key);
                if (list() is { Count: > 0 } values) { values.Clear(); MarkDirty(); _conditions.RefreshTexts(); }
            }
            ShowConditionPanels();
        };
    }

    // ---------------------------------------------------------------- checks

    private void BuildChecksPage()
    {
        var run = new PillButton("Run checks", PillStyle.Primary);
        run.Click += (_, _) => { RunChecks(); ShowChecks(); };
        var bar = new Toolbar(run) { Dock = DockStyle.Top, Padding = new Padding(0, 2, 0, 8) };
        foreach (var (level, text) in new (CheckLevel?, string)[] { (null, "All"), (CheckLevel.Error, "Errors"), (CheckLevel.Warning, "Warnings"), (CheckLevel.Info, "Info"), (CheckLevel.Ok, "OK") })
        {
            var chip = new PillButton(text, PillStyle.Chip) { Height = 32, Margin = new Padding(4, 5, 4, 4) };
            chip.Click += (_, _) => { _checkFilter = level; ShowChecks(); };
            _checkFilterChips.Add((level, chip));
            bar.Controls.Add(chip);
        }
        var page = new Panel { BackColor = Color.Transparent };
        page.Controls.Add(_checkList);
        page.Controls.Add(bar);

        _checkList.SelectionChanged += () =>
        {
            var entry = _checkList.SelectedItem as CheckEntry;
            _checkDetail.Text = entry == null ? "Select a line to see it in full." : $"{entry.Where}\n\n{entry.Message}";
            _checkGoTo.Visible = entry?.Trader != null;
            _details.Relayout();
        };
        _checkList.ItemActivated += () => GoTo(_checkList.SelectedItem as CheckEntry);
        _checkGoTo.Click += (_, _) => GoTo(_checkList.SelectedItem as CheckEntry);

        var details = new StackPanel { Gap = 10 };
        details.AddRange(
            new Section("Selected").Add(_checkDetail, new Toolbar(_checkGoTo)),
            new Section("What the colors mean").Add(new Hint(
                "✖ Error — won't work: the server skips it, or the quest can never be done/unlocked.\n" +
                "⚠ Warning — works, but probably not what you meant.\n" +
                "i Info — good to know.\n" +
                "✔ OK — checked and fine.\n\n" +
                "Checks run when you open the editor, a moment after every change, and before saving.")));

        AddPage(Page.Checks, page, details);
    }

    // =====================================================================
    // Showing things
    // =====================================================================

    private void ShowTrader()
    {
        using var _ = Freeze();
        _traderFields.Enabled = Trader != null;
        _traderFields.RefreshValues();
        _loyalty.DataSource = Trader == null ? null : new BindingList<LoyaltyLevelDef>(Trader.LoyaltyLevels);
        _bigAvatar.Image = Current != null ? AvatarOf(Current) : null;
        UpdateHeader();
        _offers.RefreshList();
        _quests.RefreshList();
        UpdateDetailsTitle();
    }

    private void UpdateHeader()
    {
        var t = Trader;
        _headerTitle.Text = t?.Name ?? "No trader";
        _headerAvatar.Image = Current != null ? AvatarOf(Current) : null;
        _headerKind.Text = t is { Enabled: false } ? "TRADER · SWITCHED OFF" : "TRADER";
        _headerKind.ForeColor = t is { Enabled: false } ? Theme.Orange : Theme.Text;
        _headerSub.Text = t == null ? "Click + in \"Your traders\" to make one." :
            $"{t.Offers.Count} offer(s) · {t.Quests.Count} quest(s) · restocks every {t.RefreshMinutesMin}–{t.RefreshMinutesMax} min · {(t.UnlockedByDefault ? "unlocked from the start" : "locked at start")}";
        _center.HeaderColor = Current != null ? AverageColor(AvatarOf(Current)) : Theme.Accent;
        _traderSummary.Text = t == null ? "" :
            $"Sells {t.Offers.Count} thing(s), {t.Offers.Count(o => IsQuestLocked(o))} of them unlocked by quests.\n" +
            $"{t.Quests.Count} quest(s), from level {(t.Quests.Count == 0 ? 0 : t.Quests.Min(q => q.MinLevel))}.\n" +
            $"Pays {t.LoyaltyLevels.FirstOrDefault()?.BuyPriceCoef ?? 0}% of an item's value when players sell to them.";
        UpdateDetailsTitle();
    }

    private void ShowOffer()
    {
        using var _ = Freeze();
        _offerFields.Enabled = Offer != null;
        _offerFields.RefreshValues();
        _costs.RefreshList();
        UpdateDetailsTitle();

        var unlockers = Offer == null ? new List<(QuestDef Quest, RewardDef Reward)>() : QuestsUnlocking(Offer);
        if (Offer == null)
        {
            _offerStatus.Title = "Nothing selected";
            _offerStatus.TitleColor = Theme.Muted;
            _offerStatusText.Text = "Pick an offer in the list, or click + Add offer.";
        }
        else if (unlockers.Count > 0)
        {
            _offerStatus.Title = "🔒  Locked — unlocked by a quest";
            _offerStatus.TitleColor = Theme.Orange;
            _offerStatusText.Text = string.Join("\n", unlockers.Select(u =>
                $"• {u.Quest.Name} (level {u.Quest.MinLevel}) → {(u.Reward.Quantity > 0 ? $"{u.Reward.Quantity} per restock" : Offer.Unlimited ? "unlimited" : $"{Offer.Stock} per restock")}"));
        }
        else
        {
            _offerStatus.Title = "✔  For sale from the start";
            _offerStatus.TitleColor = Theme.Green;
            _offerStatusText.Text = $"Anyone with LL{Offer.LoyaltyLevel} with {Trader?.Name} can buy it. To lock it behind a quest, add an \"Unlock offer\" reward to a quest.";
        }
        _offerGoToQuest.Visible = unlockers.Count > 0;
        _details.Relayout();
    }

    private void ShowQuest()
    {
        using var _ = Freeze();
        _questFields.Enabled = Quest != null;
        _questFields.RefreshValues();
        _conditions.RefreshList();
        _rewards.RefreshList();
        _rewardFields.Enabled = Reward != null;
        _rewardFields.RefreshValues();
        ShowPrereqs();
        ShowRequirements();
        UpdateWaysHint();
        ShowQuestImage();
        UpdateDetailsTitle();
    }

    private void ShowQuestImage()
    {
        var image = Current != null && Quest != null ? QuestImage(Current, Quest) : null;
        _questImagePreview.Image = image;
        _questImagePreview.Visible = image != null;
        _removeQuestImage.Visible = image != null;
    }

    /// <summary>The quest's picture from the trader folder (cached; reloaded when the file changes).</summary>
    private Image? QuestImage(TraderEntry trader, QuestDef quest)
    {
        if (string.IsNullOrWhiteSpace(quest.Image)) return null;
        var path = Path.Combine(trader.Folder, quest.Image);
        try
        {
            if (!File.Exists(path)) return null;
            var written = File.GetLastWriteTimeUtc(path);
            if (_questImages.TryGetValue(path, out var cached) && cached.Written == written) return cached.Image;
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var loaded = Image.FromStream(stream);
            var image = new Bitmap(loaded);
            _questImages[path] = (written, image);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateWaysHint()
    {
        if (Quest == null) { _waysHint.Text = ""; return; }
        var options = Quest.UsedOptions();
        _waysHint.Text = options.Count <= 1
            ? "All objectives must be done. Want the player to pick one of several ways (hand in / kill / pay)? Put objectives in Way B, C, D."
            : $"{options.Count} ways to complete: {string.Join(", ", options.Select(o => $"{QuestDef.OptionLetter(o)} ({Quest.Conditions.Count(c => c.Option == o)} objective(s))"))}. " +
              "The player finishes ANY ONE way; all objectives inside that way are needed. In game each way shows as its own quest and the others are cancelled.";
        _details.Relayout();
    }

    private void ShowRequirements()
    {
        if (Quest == null) { _requirementsText.Text = ""; return; }
        var req = Checks.Requirements(Quest, _traders);
        var lines = new List<string>
        {
            req.EffectiveLevel > req.OwnLevel
                ? $"Player level {req.EffectiveLevel}  (set to {req.OwnLevel}, but an earlier quest needs {req.EffectiveLevel})"
                : $"Player level {req.OwnLevel}",
        };
        if (req.Chain.Count == 0) lines.Add("No quests needed before it.");
        else
        {
            lines.Add($"Finish these {req.Chain.Count} quest(s) first:");
            foreach (var (trader, quest, depth) in req.Chain.OrderByDescending(c => c.Depth))
                lines.Add($"{new string(' ', 2 * depth)}• {trader.File.Name}: {quest.Name}  (lvl {quest.MinLevel}){(depth > 1 ? "  — needed by an earlier one" : "")}" +
                          (quest.UsedOptions().Count is > 1 and var n ? $"  — any of its {n} ways ({string.Join("/", quest.UsedOptions().Select(QuestDef.OptionLetter))})" : "") +
                          (trader.File.Enabled ? "" : $"   ✖ {trader.File.Name} is switched OFF"));
        }
        foreach (var id in req.MissingIds) lines.Add($"✖ Needs a quest that no longer exists ({id}) — untick it below.");
        if (req.HasCycle) lines.Add("✖ The required quests go in a circle — this quest can never unlock.");
        _requirementsText.Text = string.Join("\n", lines);
        _requirements.TitleColor = req.MissingIds.Count > 0 || req.HasCycle || req.Chain.Any(c => !c.Trader.File.Enabled) ? Theme.Red : Theme.Violet;
        _details.Relayout();
    }

    private void ShowCondition()
    {
        using var _ = Freeze();
        _objectiveSection.Enabled = Condition != null;
        _conditionFields.RefreshValues();
        ShowConditionPanels();
    }

    /// <summary>Shows the parts of the objective editor that fit its type (kill / hand over / extract...).</summary>
    private void ShowConditionPanels()
    {
        var c = Condition;

        string type = c?.Type ?? "";
        bool kill = type == ConditionTypes.Kill;
        bool items = type is ConditionTypes.HandoverItem or ConditionTypes.FindItem or ConditionTypes.UseItem;
        var open = c != null && _expanded.TryGetValue(c, out var set) ? set : new HashSet<string>();

        _objectiveSection.Title = c == null ? "Objective" : $"{QuestDef.OptionLetter(c.Option)} · {ConditionTypes.Label(type).ToUpperInvariant()}";
        _objectiveSection.TitleColor = Theme.ForConditionType(type);
        _objectiveSection.Outline = Theme.Bg; // pitch black edge; the title carries the type color

        _condItems.Visible = items;
        _condItems.Title = type switch
        {
            ConditionTypes.HandoverItem => "What to hand over — any one of these counts (items or money)",
            ConditionTypes.FindItem => "What to find — any one of these counts",
            ConditionTypes.UseItem => "What to use — any one of these counts (food, drinks, meds)",
            _ => "Items",
        };
        _condBosses.Visible = kill && c!.KillTarget == "Boss";

        _loadingChecks = true;
        _needWeapon.Visible = kill;
        _needCaliber.Visible = kill;
        _needWearing.Visible = kill || type == ConditionTypes.Extract;
        _needMaps.Visible = kill || type is ConditionTypes.Extract or ConditionTypes.UseItem;
        _needWeapon.Checked = c != null && (c.WeaponTpls.Count > 0 || open.Contains("weapon"));
        _needCaliber.Checked = c != null && (c.Calibers.Count > 0 || open.Contains("caliber"));
        _needWearing.Checked = c != null && (c.WearingTpls.Count > 0 || open.Contains("wearing"));
        _needMaps.Checked = c != null && (c.Locations.Count > 0 || open.Contains("maps"));
        _loadingChecks = false;

        _condWeapons.Visible = _needWeapon.Visible && _needWeapon.Checked;
        _condCalibers.Visible = _needCaliber.Visible && _needCaliber.Checked;
        _condWearing.Visible = _needWearing.Visible && _needWearing.Checked;
        _condMaps.Visible = _needMaps.Visible && _needMaps.Checked;

        _condItems.RefreshList();
        _condWeapons.RefreshList();
        _condCalibers.RefreshList();
        _condWearing.RefreshList();
        _condBosses.RefreshList();
        _condMaps.RefreshList();
        _objectiveSection.Invalidate();
        _details.Relayout();
    }

    private void OnConditionChanged()
    {
        MarkDirty();
        _conditions.RefreshTexts();
        _quests.RefreshTexts();
        UpdateWaysHint();
        _conditionFields.RefreshVisibility(); // not RefreshValues: the user may be typing
        ShowConditionPanels();
    }

    /// <summary>One switch per quest of every trader; multi-way quests say that any way counts.</summary>
    private void ShowPrereqs()
    {
        _prereqs.SuspendLayout();
        foreach (Control c in _prereqs.Controls.Cast<Control>().ToList()) { _prereqs.Controls.Remove(c); c.Dispose(); }
        if (Quest != null)
        {
            foreach (var entry in _traders)
            foreach (var other in entry.File.Quests.Where(x => !ReferenceEquals(x, Quest)))
            {
                int ways = other.UsedOptions().Count;
                string text = $"{other.Name} · {entry.File.Name} · lvl {other.MinLevel}" +
                              (ways > 1 ? $" · any of {ways} ways" : "") +
                              (entry.File.Enabled ? "" : "  · trader OFF");
                var toggle = new Toggle { Text = text, Checked = Quest.PrerequisiteQuestIds.Contains(other.Id), Tag = other.Id, Font = Theme.Body };
                toggle.ForeColor = entry.File.Enabled ? Theme.Text : Theme.Muted;
                toggle.CheckedChanged += (_, _) => OnPrereqChecked((string)toggle.Tag!, toggle.Checked);
                _prereqs.Controls.Add(toggle);
            }
            if (_prereqs.Controls.Count == 0)
                _prereqs.Controls.Add(new Hint("No other quests yet."));
        }
        _prereqs.ResumeLayout();
        _prereqs.Relayout();
    }

    private void OnPrereqChecked(string id, bool required)
    {
        if (Quest == null || Quest.PrerequisiteQuestIds.Contains(id) == required) return;
        if (required) Quest.PrerequisiteQuestIds.Add(id);
        else Quest.PrerequisiteQuestIds.Remove(id);
        MarkDirty();
        ShowRequirements();
        _quests.RefreshTexts();
    }

    private void ShowChecks()
    {
        foreach (var (level, chip) in _checkFilterChips) chip.Selected = level == _checkFilter;
        var entries = _history.Concat(_checks
                .OrderBy(e => e.Level)
                .ThenBy(e => e.Where))
            .Where(e => _checkFilter == null || e.Level == _checkFilter);
        _checkList.SetItems(entries.Cast<object>());
    }

    // =====================================================================
    // Row text
    // =====================================================================

    private Row DescribeTrader(TraderEntry t)
    {
        var badges = new List<(string, Color)>();
        if (!t.File.Enabled) badges.Add(("OFF", Theme.Muted));
        if (t.Dirty) badges.Add(("UNSAVED", Theme.Pink));
        int errors = _checks.Count(c => c.Trader == t && c.Level == CheckLevel.Error);
        if (errors > 0) badges.Add(($"{errors} ✖", Theme.Red));
        return new Row
        {
            Title = t.File.Name,
            TitleColor = t.File.Enabled ? Theme.Text : Theme.Muted,
            Subtitle = $"{t.File.Offers.Count} offers · {t.File.Quests.Count} quests",
            Thumb = AvatarOf(t),
            Badges = badges,
        };
    }

    private Row DescribeOffer(OfferDef o)
    {
        var unlockers = QuestsUnlocking(o);
        bool locked = unlockers.Count > 0;
        var item = _db.Get(o.ItemTpl);
        int? unlockedStock = unlockers.Select(u => u.Reward.Quantity).FirstOrDefault(q => q > 0);
        string stock = unlockedStock > 0 ? $"{unlockedStock} / restock" : o.Unlimited ? "Unlimited" : $"{o.Stock} / restock";
        return new Row
        {
            Title = _db.NameOf(o.ItemTpl),
            Subtitle = CostText(o),
            ThumbText = Initials(item?.ShortName ?? _db.NameOf(o.ItemTpl)),
            ThumbColor = locked ? Theme.Orange : Theme.CardSelected,
            Badges = locked ? new() { ("QUEST", Theme.Orange) } : new(),
            Columns = new[]
            {
                locked ? "🔒 " + string.Join(", ", unlockers.Select(u => u.Quest.Name)) : "From start",
                $"LL{o.LoyaltyLevel}",
                stock,
            },
            ColumnColors = new[] { locked ? Theme.Orange : Theme.Green, Theme.Muted, Theme.Muted },
        };
    }

    private Row DescribeCost(CostDef c) => new()
    {
        Title = IsMoney(c.ItemTpl) ? $"{c.Count:N0} {MoneyName(c.ItemTpl)}" : $"{c.Count:N0} × {_db.NameOf(c.ItemTpl)}",
        ThumbText = IsMoney(c.ItemTpl) ? Currencies.Symbol(c.ItemTpl) : Initials(_db.Get(c.ItemTpl)?.ShortName ?? "?"),
        ThumbColor = IsMoney(c.ItemTpl) ? Theme.Yellow : Theme.CardSelected,
    };

    private Row DescribeQuest(QuestDef q)
    {
        var options = q.UsedOptions();
        var badges = new List<(string, Color)>();
        if (options.Count > 1) badges.Add(($"{options.Count} WAYS", Theme.Violet));
        if (q.PrerequisiteQuestIds.Count > 0) badges.Add(($"AFTER {q.PrerequisiteQuestIds.Count} QUEST{(q.PrerequisiteQuestIds.Count > 1 ? "S" : "")}", Theme.Blue));
        if (_checks.Any(c => ReferenceEquals(c.Target, q) && c.Level == CheckLevel.Error)) badges.Add(("✖ PROBLEM", Theme.Red));

        string objectives = q.Conditions.Count == 0 ? "No objectives yet" :
            string.Join("  ·  ", options.Select(o =>
                (options.Count > 1 ? QuestDef.OptionLetter(o) + ": " : "") +
                string.Join(" + ", q.Conditions.Where(c => c.Option == o || options.Count == 1).Select(ShortCondition))));
        return new Row
        {
            Title = q.Name,
            Subtitle = objectives,
            ThumbText = q.MinLevel.ToString(),
            ThumbColor = Theme.Violet,
            Thumb = Current != null ? QuestImage(Current, q) : null,
            Badges = badges,
            Detail = "Rewards: " + RewardsText(q),
            Columns = new[] { $"Lvl {q.MinLevel}", options.Count.ToString() },
        };
    }

    /// <summary>"20,000 XP · +0.05 standing · 80× M855 · 🔓 M4A1 ×3" for the quest list.</summary>
    private string RewardsText(QuestDef q) => q.Rewards.Count == 0 ? "—" : string.Join("  ·  ", q.Rewards.Select(r => r.Type switch
    {
        RewardTypes.Experience => $"{r.Value:N0} XP",
        RewardTypes.TraderStanding => $"{(r.Value >= 0 ? "+" : "")}{r.Value:0.##} standing",
        RewardTypes.Item => $"{r.Count}× {ShortName(r.ItemTpl)}",
        RewardTypes.UnlockOffer => "🔓 " + (Trader?.Offers.FirstOrDefault(o => o.Id == r.OfferId) is { } o ? ShortName(o.ItemTpl) : "?") + (r.Quantity > 0 ? $" ×{r.Quantity}" : ""),
        _ => r.Type,
    }));

    private string ShortCondition(ConditionDef c) => c.Type switch
    {
        ConditionTypes.Kill => $"kill {c.Count} {KillWho(c)}",
        ConditionTypes.Extract => $"extract {c.Count}×",
        ConditionTypes.UseItem => $"use {c.Count}× {ItemsText(c.ItemTpls)}",
        ConditionTypes.FindItem => $"find {c.Count}× {ItemsText(c.ItemTpls)}",
        _ when IsMoneyList(c.ItemTpls) => $"pay {c.Count:N0} {MoneyName(c.ItemTpls[0])}",
        _ => $"hand in {c.Count}× {ItemsText(c.ItemTpls)}",
    };

    private Row DescribeCondition(ConditionDef c)
    {
        var details = new List<string>();
        if (c.Type == ConditionTypes.Kill)
        {
            if (c.WeaponTpls.Count > 0) details.Add("with " + string.Join(" or ", c.WeaponTpls.Select(ShortName)));
            if (c.Calibers.Count > 0) details.Add(string.Join("/", c.Calibers.Select(ItemDatabase.CaliberName)) + " ammo");
        }
        if (c.WearingTpls.Count > 0 && c.Type is ConditionTypes.Kill or ConditionTypes.Extract) details.Add("wearing " + string.Join(" or ", c.WearingTpls.Select(ShortName)));
        if (c.Locations.Count > 0) details.Add("on " + string.Join(", ", c.Locations.Select(Maps.Name)));
        if (c.Type is ConditionTypes.HandoverItem or ConditionTypes.FindItem && c.FoundInRaid && !IsMoneyList(c.ItemTpls)) details.Add("found in raid");
        if (details.Count == 0) details.Add(c.Type == ConditionTypes.Kill ? "any weapon, any map" : c.Type == ConditionTypes.Extract ? "any map" : "");

        string title = c.Type switch
        {
            ConditionTypes.Kill => $"Kill {c.Count} × {KillWho(c)}",
            ConditionTypes.Extract => $"Extract {c.Count} time(s)",
            ConditionTypes.UseItem => $"Use {c.Count} × {ItemsText(c.ItemTpls)}",
            ConditionTypes.FindItem => $"Find {c.Count} × {ItemsText(c.ItemTpls)}",
            _ when IsMoneyList(c.ItemTpls) => $"Pay {c.Count:N0} {MoneyName(c.ItemTpls[0])}",
            _ => $"Hand over {c.Count} × {ItemsText(c.ItemTpls)}",
        };
        return new Row
        {
            Title = title,
            Subtitle = string.Join(" · ", details.Where(d => d.Length > 0)),
            ThumbText = QuestDef.OptionLetter(c.Option),
            ThumbColor = Theme.ForOption(c.Option),
            Badges = new() { (ShortTypeLabel(c.Type).ToUpperInvariant(), Theme.ForConditionType(c.Type)) },
        };
    }

    private Row DescribeReward(RewardDef r) => r.Type switch
    {
        RewardTypes.Experience => new Row { Title = $"{r.Value:N0} XP", ThumbText = "XP", ThumbColor = Theme.Violet },
        RewardTypes.TraderStanding => new Row { Title = $"{(r.Value >= 0 ? "+" : "")}{r.Value:0.##} standing with {Trader?.Name}", ThumbText = "+", ThumbColor = Theme.Blue },
        RewardTypes.Item => new Row { Title = $"{r.Count} × {_db.NameOf(r.ItemTpl)}", Subtitle = r.FoundInRaid ? "found in raid" : "", ThumbText = Initials(_db.Get(r.ItemTpl)?.ShortName ?? "?"), ThumbColor = Theme.Orange },
        RewardTypes.UnlockOffer => new Row
        {
            Title = "Unlocks: " + (Trader?.Offers.FirstOrDefault(o => o.Id == r.OfferId) is { } o ? OfferText(o) : "(pick an offer)"),
            Subtitle = r.Quantity > 0 ? $"{r.Quantity} per restock" : "stock as set on the offer",
            ThumbText = "🔓", ThumbColor = Theme.Green,
        },
        _ => new Row { Title = r.Type },
    };

    private Row DescribeItem(string tpl)
    {
        var item = _db.Get(tpl);
        return new Row
        {
            Title = IsMoney(tpl) ? MoneyName(tpl) : _db.NameOf(tpl),
            Subtitle = item == null ? tpl : item.Category == ItemCategory.Other ? item.ShortName : $"{item.Category} · {item.ShortName}",
            ThumbText = IsMoney(tpl) ? Currencies.Symbol(tpl) : Initials(item?.ShortName ?? "?"),
            ThumbColor = IsMoney(tpl) ? Theme.Yellow : item == null && _db.IsLoaded ? Theme.Red : Theme.CardSelected,
        };
    }

    private Row DescribeCheck(CheckEntry e) => new()
    {
        Title = e.Message,
        Subtitle = e.Where,
        ThumbText = e.Level switch { CheckLevel.Error => "✖", CheckLevel.Warning => "!", CheckLevel.Info => "i", _ => "✔" },
        ThumbColor = e.Level switch { CheckLevel.Error => Theme.Red, CheckLevel.Warning => Theme.Orange, CheckLevel.Info => Theme.Blue, _ => Theme.Green },
        Columns = new[] { e.Time.ToString("HH:mm:ss") },
    };

    private string KillWho(ConditionDef c) => c.KillTarget switch
    {
        "Boss" => c.BossRoles.Count == 0 ? "any boss" : string.Join(" / ", c.BossRoles.Select(KillTargets.BossName)),
        "AnyPmc" => "PMCs",
        "Usec" => "USEC",
        "Bear" => "BEAR",
        "Savage" => "Scavs",
        _ => "anyone",
    };

    private string OfferText(OfferDef o) => $"{_db.NameOf(o.ItemTpl)} — {CostText(o)}";

    private string CostText(OfferDef o) => o.Cost.Count == 0 ? "no price set!" : string.Join(" + ", o.Cost.Select(c =>
        IsMoney(c.ItemTpl) ? $"{c.Count:N0} {Currencies.Symbol(c.ItemTpl)}" : $"{c.Count:N0} × {_db.NameOf(c.ItemTpl)}"));

    private string ItemsText(List<string> tpls) => tpls.Count == 0 ? "(pick items)" : string.Join(" / ", tpls.Select(ShortName));

    private string ShortName(string tpl) => _db.Get(tpl) is { } i && i.ShortName.Length > 0 && i.Name.Length > 24 ? i.ShortName : _db.NameOf(tpl);

    private static string Initials(string text)
    {
        var s = new string(text.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        return s.Length <= 3 ? s : s[..3];
    }

    private static bool IsMoney(string tpl) => Currencies.IsCurrency(tpl) || tpl == Currencies.GpCoin || tpl == Currencies.LegaMedal;
    private static bool IsMoneyList(List<string> tpls) => tpls.Count > 0 && tpls.All(IsMoney);

    private string MoneyName(string tpl) => tpl switch
    {
        Currencies.Roubles => "₽ roubles",
        Currencies.Dollars => "$ dollars",
        Currencies.Euros => "€ euros",
        Currencies.GpCoin => "GP coins",
        Currencies.LegaMedal => "Lega medals",
        _ => _db.NameOf(tpl),
    };

    private List<(QuestDef Quest, RewardDef Reward)> QuestsUnlocking(OfferDef o) =>
        (Trader?.Quests ?? new()).SelectMany(q => q.Rewards.Where(r => r.Type == RewardTypes.UnlockOffer && r.OfferId == o.Id).Select(r => (q, r))).ToList();

    private bool IsQuestLocked(OfferDef o) => QuestsUnlocking(o).Count > 0;

    // =====================================================================
    // Creating / duplicating
    // =====================================================================

    private static T Clone<T>(T value, Action<T>? change = null)
    {
        var copy = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, TraderFile.JsonOptions), TraderFile.JsonOptions)!;
        change?.Invoke(copy);
        return copy;
    }

    private OfferDef? NewOffer()
    {
        var tpl = ItemPickerDialog.Pick(this, _db, ItemFilter.Weapons);
        if (tpl == null) return null;
        return new OfferDef
        {
            Id = Ids.New(),
            ItemTpl = tpl,
            Cost = { new CostDef { ItemTpl = Currencies.Roubles, Count = 50000 } },
        };
    }

    private OfferDef DuplicateOffer(OfferDef o) => Clone(o, c =>
    {
        c.Id = Ids.New();
        c.UnlockedByQuestId = null;
    });

    private QuestDef DuplicateQuest(QuestDef q) => Clone(q, c =>
    {
        c.Id = Ids.New();
        c.Name = q.Name + " (copy)";
        c.Image = null;
        foreach (var x in c.Conditions) x.Id = Ids.New();
        foreach (var x in c.Rewards) x.Id = Ids.New();
    });

    private ConditionDef NewCondition() => new()
    {
        Id = Ids.New(),
        // New objectives go into the way of the selected one.
        Option = Condition?.Option ?? 1,
    };

    private ConditionDef DuplicateCondition(ConditionDef c) => Clone(c, x => x.Id = Ids.New());

    private string? PickCaliber(IWin32Window owner)
    {
        var tpl = ItemPickerDialog.Pick(owner, _db, ItemFilter.Ammo);
        if (tpl == null) return null;
        var caliber = _db.Get(tpl)?.Caliber;
        if (string.IsNullOrEmpty(caliber))
        {
            MessageBox.Show(this, "That item has no caliber — pick a bullet (Ammo).", "Not ammo");
            return null;
        }
        return caliber;
    }

    // =====================================================================
    // Traders
    // =====================================================================

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
        Log(CheckLevel.Ok, name, $"Created trader {name} in {folder}", entry);
        ShowPage(Page.Trader);
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
        var removed = Current;
        _avatars.Remove(removed);
        Directory.Move(removed.Folder, target);
        _traders.Remove(removed);
        RefreshTraderList();
        Log(CheckLevel.Info, removed.File.Name, $"Moved to {target}");
        RunChecks();
    }

    private Image AvatarOf(TraderEntry t)
    {
        if (_avatars.TryGetValue(t, out var image)) return image;
        var path = Path.Combine(t.Folder, t.File.Avatar ?? "");
        try
        {
            if (File.Exists(path))
            {
                // Load a copy so the file isn't locked (it can then be replaced).
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var loaded = Image.FromStream(stream);
                image = new Bitmap(loaded);
            }
        }
        catch
        {
            // unreadable picture: placeholder below
        }
        image ??= PlaceholderImage(t.File.Name, 128);
        _avatars[t] = image;
        return image;
    }

    private static Color AverageColor(Image image)
    {
        using var small = new Bitmap(image, new Size(8, 8));
        long r = 0, g = 0, b = 0;
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                var c = small.GetPixel(x, y);
                r += c.R; g += c.G; b += c.B;
            }
        var avg = Color.FromArgb((int)(r / 64), (int)(g / 64), (int)(b / 64));
        // Keep it colorful but not too bright, like Spotify's header gradient.
        return Theme.IsLight(avg) ? Theme.Darken(avg, 0.35f) : Theme.Lighten(avg, 0.1f);
    }

    private void ChooseAvatar()
    {
        if (Current == null) return;
        using var dialog = new OpenFileDialog { Title = "Choose trader icon", Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // Stored as a square 256x256 image in the trader's folder.
        string target = Path.Combine(Current.Folder, "avatar.png");
        try
        {
            SaveSquareImage(dialog.FileName, target, 256);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"Couldn't read that picture:\n{e.Message}", "Icon");
            return;
        }
        Trader!.Avatar = "avatar.png";
        _avatars.Remove(Current);
        MarkDirty();
        _bigAvatar.Image = AvatarOf(Current);
        UpdateHeader();
        _traderList.Redraw();
        Log(CheckLevel.Ok, Trader.Name, "New icon saved. Restart the SPT server to see it in game.", Current);
    }

    private void ChooseQuestImage()
    {
        if (Current == null || Quest == null) return;
        using var dialog = new OpenFileDialog { Title = "Choose quest image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string name = $"quest_{Quest.Id}.png";
        using (var source = Image.FromFile(dialog.FileName))
        using (var copy = new Bitmap(source))
            copy.Save(Path.Combine(Current.Folder, name), ImageFormat.Png);
        Quest.Image = name;
        MarkDirty();
        ShowQuestImage();
        _quests.RefreshTexts();
        Log(CheckLevel.Ok, $"{Trader!.Name} › {Quest.Name}", $"Quest image saved as {name}", Current, Quest);
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

    private static Bitmap PlaceholderImage(string name, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, size, size), Color.FromArgb(40, 90, 60), Color.FromArgb(18, 18, 18), 45f))
            g.FillRectangle(brush, 0, 0, size, size);
        using var font = new Font("Segoe UI Black", size / 3.2f);
        var text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        var sizeF = g.MeasureString(text, font);
        g.DrawString(text, font, Brushes.White, (size - sizeF.Width) / 2, (size - sizeF.Height) / 2);
        return bmp;
    }

    private static void SavePlaceholderAvatar(string path, string name)
    {
        using var bmp = PlaceholderImage(name, 256);
        bmp.Save(path, ImageFormat.Png);
    }

    // =====================================================================
    // Background picture
    // =====================================================================

    private ContextMenuStrip BackgroundMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body, ShowImageMargin = false };
        menu.Items.Add("Choose picture from PC...", null, (_, _) => ChooseBackground());
        menu.Items.Add(new ToolStripSeparator());
        foreach (var (label, dim) in new[] { ("Picture: bright", 25), ("Picture: medium", 55), ("Picture: dark", 75) })
        {
            var item = new ToolStripMenuItem(label, null, (_, _) => { _settings.BackgroundDim = dim; _settings.Save(); RenderBackground(); })
            {
                Checked = _settings.BackgroundDim == dim,
                Enabled = _backgroundSource != null,
                ForeColor = Theme.Text,
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var remove = new ToolStripMenuItem("Remove picture", null, (_, _) =>
        {
            _settings.BackgroundImage = null;
            _settings.Save();
            _backgroundSource?.Dispose();
            _backgroundSource = null;
            RenderBackground();
        }) { Enabled = _backgroundSource != null, ForeColor = Theme.Text };
        menu.Items.Add(remove);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Button color...", null, (_, _) => ChooseAccent());
        menu.Items.Add(new ToolStripMenuItem("Button color: default green", null, (_, _) => SetAccent(null)) { Enabled = _settings.AccentColor != null });
        foreach (ToolStripItem i in menu.Items) i.ForeColor = Theme.Text;
        return menu;
    }

    private void ChooseAccent()
    {
        using var dialog = new ColorDialog { Color = Theme.Accent, FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) SetAccent(dialog.Color);
    }

    /// <summary>Recolors every green button / highlight; null = back to the default green.</summary>
    private void SetAccent(Color? color)
    {
        Theme.Accent = color ?? Theme.DefaultAccent;
        _settings.AccentColor = color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
        _settings.Save();
        using (Freeze()) Invalidate(true);
    }

    private void ChooseBackground()
    {
        using var dialog = new OpenFileDialog { Title = "Choose a background picture", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _settings.BackgroundImage = dialog.FileName;
        _settings.Save();
        LoadBackground();
    }

    private void LoadBackground()
    {
        _backgroundSource?.Dispose();
        _backgroundSource = null;
        var path = _settings.BackgroundImage;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var loaded = Image.FromStream(stream);
                _backgroundSource = new Bitmap(loaded);
            }
            catch (Exception e)
            {
                Log(CheckLevel.Warning, "Background", $"Couldn't load the background picture: {e.Message}");
            }
        }
        RenderBackground();
    }

    private void RenderBackground()
    {
        using var _ = Freeze();
        var old = BackgroundImage;
        BackgroundImage = _backgroundSource == null || ClientSize.Width < 10 ? null : Theme.CoverDarkened(_backgroundSource, ClientSize, _settings.BackgroundDim);
        BackgroundImageLayout = ImageLayout.None;
        old?.Dispose();
        // Panels are slightly see-through when there is a picture.
        byte alpha = (byte)(_backgroundSource == null ? 255 : 215);
        foreach (var panel in AllControls(this).OfType<GlassPanel>().Where(p => p is not Section && p.Tint.R == Theme.Surface.R && p.Tint.G == Theme.Surface.G))
            panel.Tint = Color.FromArgb(alpha, Theme.Surface);
        Invalidate(true);
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var child in AllControls(c)) yield return child;
        }
    }

    // =====================================================================
    // Load / save / checks
    // =====================================================================

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
        _avatars.Clear();
        _modFolder = null;
        _folderLabel.Text = "Click Browse... and pick your SPT_Runtime\\user\\mods\\CustomTraders folder";
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            RefreshTraderList();
            SetStatus("Click Browse... and select your …\\SPT_Runtime\\user\\mods\\CustomTraders folder.");
            return;
        }

        _modFolder = folder;
        _folderLabel.Text = folder;
        _settings.ModFolder = folder;
        _settings.Save();

        string tradersFolder = Path.Combine(folder, "traders");
        Directory.CreateDirectory(tradersFolder);
        foreach (var dir in Directory.GetDirectories(tradersFolder).OrderBy(d => d))
        {
            string file = Path.Combine(dir, "trader.json");
            if (!File.Exists(file)) continue;
            try { _traders.Add(new TraderEntry(dir, TraderFile.Load(file))); }
            catch (Exception e) { Log(CheckLevel.Error, Path.GetFileName(dir), $"Could not read trader.json: {e.Message}"); }
        }

        LoadItemDatabase(folder);
        RefreshTraderList();
        RunChecks();
        SetStatus($"Loaded {_traders.Count} trader(s). Restart the SPT server after saving to apply changes.");
    }

    private void LoadItemDatabase(string modFolder)
    {
        try
        {
            var dbFolder = ItemDatabase.FindDatabaseFolder(modFolder);
            if (dbFolder == null)
            {
                _dbLabel.Text = "Item database not found";
                Log(CheckLevel.Warning, "Items", "SPT_Data\\database not found above the mod folder — item names and item checks are unavailable.");
                return;
            }
            Cursor = Cursors.WaitCursor;
            _db.Load(dbFolder);
            _dbLabel.Text = $"{_db.Items.Count:N0} items";
        }
        catch (Exception e)
        {
            _dbLabel.Text = "Item database failed";
            Log(CheckLevel.Error, "Items", $"Item database failed to load: {e.Message}");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RunChecks()
    {
        _checks = Checks.Run(_traders, _db);
        int errors = _checks.Count(c => c.Level == CheckLevel.Error);
        int warnings = _checks.Count(c => c.Level == CheckLevel.Warning);
        _checkSummary.Text = errors + warnings == 0 ? "✔ All checks pass" : $"✖ {errors} error(s)   ⚠ {warnings} warning(s)";
        _checkSummary.ForeColor = errors > 0 ? Theme.Red : warnings > 0 ? Theme.Orange : Theme.Green;
        _checksButton.Text = errors > 0 ? $"✖ {errors}" : warnings > 0 ? $"⚠ {warnings}" : "✔ Checks";
        _checksButton.Width = TextRenderer.MeasureText(_checksButton.Text, _checksButton.Font).Width + 36;
        _chips[Page.Checks].Text = errors + warnings > 0 ? $"Checks & log ({errors + warnings})" : "Checks & log";
        _chips[Page.Checks].Width = TextRenderer.MeasureText(_chips[Page.Checks].Text, _chips[Page.Checks].Font).Width + 36;
        ShowChecks();
        _traderList.Redraw();
        _quests.RefreshTexts();
    }

    private void SaveAll()
    {
        Validate(); // commit the field being typed in
        RunChecks();
        var dirty = _traders.Where(t => t.Dirty).ToList();
        if (dirty.Count == 0)
        {
            SetStatus("Nothing to save.");
            return;
        }

        var errors = _checks.Where(c => c.Level == CheckLevel.Error && c.Trader != null && dirty.Contains(c.Trader)).ToList();
        if (errors.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"{errors.Count} problem(s) will stop things from working:\n\n- {string.Join("\n- ", errors.Take(6).Select(e => $"{e.Where}: {e.Message}"))}" +
                (errors.Count > 6 ? $"\n… and {errors.Count - 6} more (see Checks & log)" : "") +
                "\n\nSave anyway?  (No = show me the problems)",
                "Check before saving", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                _checkFilter = CheckLevel.Error;
                ShowPage(Page.Checks);
                ShowChecks();
                return;
            }
        }

        int saved = 0;
        foreach (var entry in dirty)
        {
            try
            {
                entry.File.Save(Path.Combine(entry.Folder, "trader.json"));
                entry.Dirty = false;
                saved++;
                int e = _checks.Count(c => c.Trader == entry && c.Level == CheckLevel.Error);
                Log(e > 0 ? CheckLevel.Warning : CheckLevel.Ok, entry.File.Name,
                    e > 0 ? $"Saved with {e} error(s) — see the checks." : "Saved. Restart the SPT server to apply.", entry);
            }
            catch (Exception ex)
            {
                Log(CheckLevel.Error, entry.File.Name, $"Could not save: {ex.Message}", entry);
            }
        }
        _traderList.Redraw();
        UpdateUnsaved();
        SetStatus($"Saved {saved} trader(s) at {DateTime.Now:HH:mm}. Restart the SPT server to apply.");
    }

    private void Log(CheckLevel level, string where, string message, TraderEntry? trader = null, object? target = null)
    {
        _history.Insert(0, new CheckEntry(level, where, message, trader, target));
        if (_history.Count > 200) _history.RemoveAt(_history.Count - 1);
        if (IsHandleCreated) ShowChecks();
    }

    private void GoTo(CheckEntry? entry)
    {
        if (entry?.Trader == null) return;
        GoTo(entry.Trader, entry.Target);
    }

    private void GoTo(TraderEntry? trader, object? target)
    {
        if (trader == null || !_traders.Contains(trader)) return;
        if (!ReferenceEquals(Current, trader)) _traderList.SelectedItem = trader;
        switch (target)
        {
            case OfferDef offer:
                ShowPage(Page.Offers);
                _offers.Select(offer);
                break;
            case QuestDef quest:
                ShowPage(Page.Quests);
                _quests.Select(quest);
                break;
            default:
                ShowPage(Page.Trader);
                break;
        }
    }

    // =====================================================================
    // misc
    // =====================================================================

    /// <summary>Restores dragged column widths and saves new ones.</summary>
    private void RememberColumns(string key, RowList list)
    {
        if (_settings.ColumnWidths.TryGetValue(key, out var widths))
            for (int i = 0; i < list.Columns.Length && i < widths.Count; i++)
                list.Columns[i].Width = Math.Clamp(widths[i], 40, 700);
        list.ColumnsChanged += () =>
        {
            _settings.ColumnWidths[key] = list.Columns.Select(c => c.Width).ToList();
            _settings.Save();
        };
    }

    // ---- smooth drawing -----------------------------------------------------------

    /// <summary>
    /// WS_EX_COMPOSITED: Windows draws the whole window (all child controls)
    /// off-screen first and shows it in one go — no flicker, no black boxes
    /// while controls repaint.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return cp;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SETREDRAW = 0x000B;

    /// <summary>
    /// Stops drawing while many things change (switching page/trader/quest),
    /// then draws once. Use: <c>using (Freeze()) { ... }</c>.
    /// </summary>
    private IDisposable Freeze()
    {
        if (_freeze++ == 0 && _root != null && _root.IsHandleCreated)
            SendMessage(_root.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        return new FreezeScope(this);
    }

    private void Unfreeze()
    {
        if (--_freeze > 0 || _root == null || !_root.IsHandleCreated) return;
        SendMessage(_root.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
        _root.Invalidate(true);
        _root.Update();
    }

    private sealed class FreezeScope(MainForm form) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            form.Unfreeze();
        }
    }

    /// <summary>Creates every control's window up front, so a page shown for the first time doesn't flash.</summary>
    private void CreateAllHandles()
    {
        foreach (var c in AllControls(this))
            if (!c.IsHandleCreated) _ = c.Handle;
    }

    private void MarkDirty()
    {
        if (Current == null) return;
        if (!Current.Dirty)
        {
            Current.Dirty = true;
            _traderList.Redraw();
            UpdateUnsaved();
        }
        _checkTimer.Stop();
        _checkTimer.Start(); // re-check shortly after the last change
    }

    private void UpdateUnsaved()
    {
        int n = _traders.Count(t => t.Dirty);
        _unsaved.Text = n == 0 ? "" : $"{n} unsaved";
    }

    private void RefreshTraderList(TraderEntry? select = null)
    {
        _traderList.SetItems(_traders.Cast<object>(), select ?? Current);
        UpdateUnsaved();
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

    /// <summary>The middle panel: rounded, with Spotify's colored gradient behind the header.</summary>
    private sealed class HeaderPanel : GlassPanel
    {
        private Color _headerColor = Theme.Accent;

        public Color HeaderColor
        {
            get => _headerColor;
            set { _headerColor = value; Invalidate(); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            var g = e.Graphics;
            var rect = new Rectangle(1, 1, Width - 3, Math.Min(Height - 3, 236));
            if (rect.Height < 2 || rect.Width < 2) return;
            using var path = Theme.Rounded(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height + Radius), Radius);
            using var brush = new LinearGradientBrush(rect, Color.FromArgb(200, _headerColor), Color.FromArgb(0, _headerColor), LinearGradientMode.Vertical);
            var state = g.Save();
            g.SetClip(new Rectangle(0, 0, Width, rect.Bottom));
            g.FillPath(brush, path);
            g.Restore(state);
        }
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color MenuItemSelected => Theme.CardHover;
        public override Color MenuItemBorder => Theme.CardHover;
        public override Color MenuBorder => Theme.Border;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
        public override Color CheckBackground => Theme.Accent;
        public override Color CheckSelectedBackground => Theme.Accent;
        public override Color CheckPressedBackground => Theme.Accent;
        public override Color ToolStripBorder => Theme.Bg;
    }
}
