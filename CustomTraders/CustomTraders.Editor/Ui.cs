using System.Drawing.Drawing2D;

namespace CustomTraders.Editor;

// -----------------------------------------------------------------------------
// Layout
// -----------------------------------------------------------------------------

/// <summary>
/// Puts its visible children under each other at full width (like a web
/// page). Children that are AutoSize (field tables, other stacks, sections)
/// get the height they want; others keep their own Height. Hiding a child
/// closes the gap. With AutoScroll it scrolls when too tall.
/// </summary>
public class StackPanel : Panel
{
    private bool _inLayout, _pending;

    // Top-to-bottom order as added (WinForms' own child order can change, e.g. on focus).
    private readonly List<Control> _order = new();

    public int Gap { get; set; } = 8;

    public StackPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (!_order.Contains(e.Control!)) _order.Add(e.Control!);
        e.Control!.VisibleChanged += (_, _) => Relayout();
        e.Control.SizeChanged += (_, _) => { if (!_inLayout && !e.Control.AutoSize && e.Control is not IHeightForWidth) Relayout(); };
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        _order.Remove(e.Control!);
    }

    /// <summary>Lays out this stack and every stack it sits in (after a show/hide).</summary>
    public void Relayout()
    {
        if (_inLayout)
        {
            if (_pending || !IsHandleCreated) return;
            _pending = true;
            BeginInvoke(() => { _pending = false; Relayout(); });
            return;
        }
        for (Control? c = this; c != null; c = c.Parent)
            if (c is StackPanel s) s.PerformLayout();
    }

    /// <summary>Adds controls in top-to-bottom order.</summary>
    public void AddRange(params Control[] controls)
    {
        foreach (var c in controls) Controls.Add(c);
    }

    private static int HeightFor(Control c, int width) =>
        c is IHeightForWidth h ? h.HeightForWidth(width) :
        c.AutoSize || c is StackPanel ? c.GetPreferredSize(new Size(width, 0)).Height : c.Height;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int inner = width - Padding.Horizontal;
        int h = Padding.Vertical, n = 0;
        foreach (var c in _order)
        {
            if (!c.Visible) continue;
            h += HeightFor(c, inner - c.Margin.Horizontal) + c.Margin.Vertical;
            n++;
        }
        return new Size(width, h + Math.Max(0, n - 1) * Gap);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_inLayout || !Visible) return; // laid out again when shown
        _inLayout = true;
        try
        {
            int width = ClientSize.Width - Padding.Horizontal;
            int y = Padding.Top + (AutoScroll ? AutoScrollPosition.Y : 0);
            int start = y;
            bool first = true;
            foreach (var c in _order)
            {
                if (!c.Visible) continue;
                if (!first) y += Gap;
                first = false;
                int w = Math.Max(10, width - c.Margin.Horizontal);
                int h = HeightFor(c, w);
                c.SetBounds(Padding.Left + c.Margin.Left, y + c.Margin.Top, w, h);
                y += h + c.Margin.Vertical;
            }
            if (AutoScroll)
            {
                AutoScrollMinSize = new Size(0, y - start + Padding.Vertical);
                AdjustFormScrollbars(true); // normally done by the base layout, which is replaced here
            }
        }
        finally
        {
            _inLayout = false;
        }
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        PerformLayout();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) PerformLayout();
    }
}

/// <summary>A rounded card (like Spotify's "Credits" box) with a title row and stacked content.</summary>
public class Section : GlassPanel
{
    public StackPanel Body { get; } = new() { Gap = 6 };
    private readonly Label _title;
    private readonly FlowLayoutPanel _actions = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.Transparent };

    public Section(string title, Color? accent = null)
    {
        Tint = Theme.Card;
        Radius = 10;
        Padding = new Padding(16, 12, 16, 14);
        AutoSize = true;
        _title = new Label
        {
            Text = title, Font = Theme.Heading, ForeColor = accent ?? Theme.Text, AutoSize = true,
            BackColor = Color.Transparent, Location = new Point(Padding.Left, Padding.Top),
        };
        Controls.Add(_title);
        Controls.Add(_actions);
        Controls.Add(Body);
    }

    public string Title { get => _title.Text; set => _title.Text = value; }
    public Color TitleColor { get => _title.ForeColor; set => _title.ForeColor = value; }

    /// <summary>Small buttons shown on the right of the title ("Show all" in Spotify).</summary>
    public void AddAction(Control c) => _actions.Controls.Add(c);

    public Section Add(params Control[] controls)
    {
        Body.AddRange(controls);
        return this;
    }

    // The base constructor (GlassPanel) already triggers layout, before _title exists.
    private int HeaderHeight => _title == null ? 0 : Math.Max(_title.PreferredHeight, _actions.Controls.Count > 0 ? _actions.PreferredSize.Height : 0) + 8;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int body = Body.GetPreferredSize(new Size(width - Padding.Horizontal, 0)).Height;
        return new Size(width, Padding.Vertical + HeaderHeight + body);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_title == null) return; // still being constructed
        _title.Location = new Point(Padding.Left, Padding.Top);
        var actions = _actions.PreferredSize;
        _actions.SetBounds(Width - Padding.Right - actions.Width, Padding.Top - 4, actions.Width, actions.Height);
        int top = Padding.Top + HeaderHeight;
        int w = Width - Padding.Horizontal;
        Body.SetBounds(Padding.Left, top, w, Body.GetPreferredSize(new Size(w, 0)).Height);
    }
}

/// <summary>A horizontal row of controls (buttons, chips).</summary>
public sealed class Toolbar : FlowLayoutPanel
{
    public Toolbar(params Control[] controls)
    {
        AutoSize = true;
        WrapContents = true;
        BackColor = Color.Transparent;
        Margin = new Padding(0);
        Padding = new Padding(0);
        Controls.AddRange(controls);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // Wrap to the width we are given by the stack.
        if (proposedSize.Width > 0) return base.GetPreferredSize(new Size(proposedSize.Width, 0)) with { Width = proposedSize.Width };
        return base.GetPreferredSize(proposedSize);
    }
}

// -----------------------------------------------------------------------------
// Fields
// -----------------------------------------------------------------------------

/// <summary>
/// "label: control" rows whose controls read from / write to whatever object
/// is currently selected, through get/set lambdas. Rows can be shown only
/// when a condition holds (<see cref="ShowWhen"/>). Call
/// <see cref="RefreshValues"/> after the selection changes.
/// </summary>
public sealed class FieldPanel : TableLayoutPanel
{
    private readonly List<Action> _refreshers = new();
    private readonly List<(Control Label, Control Field, Func<bool> Visible)> _conditional = new();
    private readonly List<(Label Label, Func<string> Text)> _dynamicLabels = new();
    private bool _loading;

    /// <summary>Raised after the user changed any value.</summary>
    public event Action? Changed;

    public FieldPanel(int labelWidth = 150)
    {
        ColumnCount = 2;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        Padding = new Padding(0);
        Margin = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    public bool Loading => _loading;

    /// <summary>Re-reads every value (after the selection changed).</summary>
    public void RefreshValues()
    {
        _loading = true;
        try
        {
            foreach (var r in _refreshers) r();
        }
        finally { _loading = false; }
        RefreshVisibility();
    }

    /// <summary>
    /// Only re-evaluates which rows are shown and the dynamic labels — safe to
    /// call while the user is typing (doesn't touch the text boxes).
    /// </summary>
    public void RefreshVisibility()
    {
        foreach (var (label, text) in _dynamicLabels) label.Text = text();
        foreach (var (label, field, visible) in _conditional)
        {
            bool v = visible();
            label.Visible = v;
            field.Visible = v;
        }
        (Parent as StackPanel)?.Relayout();
    }

    private void Set(Action apply)
    {
        if (_loading) return;
        apply();
        Changed?.Invoke();
    }

    private Control _lastLabel = null!, _lastField = null!;

    public T AddRow<T>(string label, T control) where T : Control
    {
        int row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var l = new Label
        {
            Text = label, AutoSize = true, ForeColor = Theme.Muted, BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 7, 0, 0), MaximumSize = new Size(ColumnStyles[0].Width > 0 ? (int)ColumnStyles[0].Width - 6 : 140, 0),
        };
        Controls.Add(l, 0, row);
        if (control.Dock == DockStyle.None) control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 4, 3, 4);
        Controls.Add(control, 1, row);
        _lastLabel = l;
        _lastField = control;
        return control;
    }

    /// <summary>Only show the row added last while <paramref name="visible"/> is true.</summary>
    public FieldPanel ShowWhen(Func<bool> visible)
    {
        _conditional.Add((_lastLabel, _lastField, visible));
        return this;
    }

    /// <summary>Label text of the row added last is recomputed on refresh (e.g. "How many kills").</summary>
    public FieldPanel LabelFrom(Func<string> text)
    {
        _dynamicLabels.Add(((Label)_lastLabel, text));
        return this;
    }

    public TextBox AddText(string label, Func<string?> get, Action<string> set, bool multiline = false, int height = 70)
    {
        var box = new TextBox { Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
        if (multiline) { box.Height = height; box.Dock = DockStyle.Top; }
        AddRow(label, box);
        box.TextChanged += (_, _) => Set(() => set(box.Text));
        _refreshers.Add(() => box.Text = get() ?? "");
        return box;
    }

    public NumericUpDown AddNumber(string label, Func<decimal> get, Action<decimal> set, decimal min, decimal max, int decimals = 0, decimal increment = 1)
    {
        var num = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = increment, ThousandsSeparator = true, Width = 170, Dock = DockStyle.Left };
        AddRow(label, num);
        num.ValueChanged += (_, _) => Set(() => set(num.Value));
        _refreshers.Add(() => num.Value = Math.Clamp(get(), min, max));
        return num;
    }

    public CheckBox AddCheck(string label, Func<bool> get, Action<bool> set, string? text = null)
    {
        var check = new Toggle { Text = text ?? "", Dock = DockStyle.Left };
        AddRow(label, check);
        check.CheckedChanged += (_, _) => Set(() => set(check.Checked));
        _refreshers.Add(() => check.Checked = get());
        return check;
    }

    /// <summary>Drop-down of (value, label) pairs; the options are re-read on every refresh.</summary>
    public ComboBox AddCombo(string label, Func<IEnumerable<(string Value, string Label)>> options, Func<string?> get, Action<string> set)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
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

    public ComboBox AddCombo(string label, (string Value, string Label)[] values, Func<string?> get, Action<string> set) =>
        AddCombo(label, () => values, get, set);

    /// <summary>
    /// A row of chips, one of which is selected (objective type, option A-D...).
    /// Label and chips each take the full width, so long chip rows fit.
    /// </summary>
    public Toolbar AddChips(string label, (string Value, string Text, Color Color)[] values, Func<string?> get, Action<string> set)
    {
        var chips = values.Select(v => new PillButton(v.Text, PillStyle.Chip) { Tag = v.Value, Tint = v.Color, Height = 32, Margin = new Padding(0, 2, 8, 2) }).ToArray();
        var bar = new Toolbar(chips) { Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 0, 0, 6) };

        int row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var l = new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 2) };
        Controls.Add(l, 0, row);
        SetColumnSpan(l, 2);
        row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(bar, 0, row);
        SetColumnSpan(bar, 2);
        _lastLabel = l;
        _lastField = bar;
        foreach (var chip in chips)
        {
            chip.Click += (_, _) =>
            {
                Set(() => set((string)chip.Tag!));
                RefreshValues();
            };
        }
        _refreshers.Add(() =>
        {
            string? current = get();
            foreach (var chip in chips) chip.Selected = Equals(chip.Tag, current);
        });
        return bar;
    }

    /// <summary>Item name + a "Pick..." button opening the item search.</summary>
    public void AddItem(string label, ItemDatabase db, Func<string?> get, Action<string> set, ItemFilter filter = ItemFilter.All)
    {
        var name = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
        var pick = new PillButton("Pick...", PillStyle.Outline) { Dock = DockStyle.Right, Height = 32 };
        var row = new Panel { Height = 36, BackColor = Color.Transparent };
        row.Controls.Add(name);
        row.Controls.Add(pick);
        AddRow(label, row);

        pick.Click += (_, _) =>
        {
            using var dialog = new ItemPickerDialog(db, filter);
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedTpl != null)
            {
                Set(() => set(dialog.SelectedTpl));
                RefreshValues();
            }
        };
        _refreshers.Add(() =>
        {
            string? tpl = get();
            name.Text = string.IsNullOrWhiteSpace(tpl) ? "(nothing picked yet)" : db.NameOf(tpl);
            name.ForeColor = string.IsNullOrWhiteSpace(tpl) || !db.Exists(tpl) && db.IsLoaded ? Theme.Orange : Theme.Text;
        });
    }

    /// <summary>Any control that refreshes itself along with the fields.</summary>
    public void AddRefresher(Action refresh) => _refreshers.Add(refresh);

    private sealed record ComboOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}

// -----------------------------------------------------------------------------
// Row lists (Spotify track-list look)
// -----------------------------------------------------------------------------

/// <summary>What a row shows: a thumbnail, title, subtitle, badges and right-hand columns.</summary>
public sealed class Row
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";

    /// <summary>Optional third line (lists created with lines: 3), e.g. a quest's rewards.</summary>
    public string Detail { get; init; } = "";
    public Color DetailColor { get; init; } = Theme.Green;
    public Color TitleColor { get; init; } = Theme.Text;
    public Color ThumbColor { get; init; } = Theme.CardSelected;
    public string ThumbText { get; init; } = "";
    public Image? Thumb { get; init; }
    public List<(string Text, Color Color)> Badges { get; init; } = new();
    public string[] Columns { get; init; } = Array.Empty<string>();
    public Color[]? ColumnColors { get; init; }
}

/// <summary>A column of a <see cref="RowList"/>; the width can be dragged in the header.</summary>
public sealed class Column(string title, int width)
{
    public string Title { get; } = title;
    public int Width { get; set; } = width;
}

/// <summary>
/// A list drawn like a Spotify playlist: # | thumbnail + title/subtitle |
/// columns. Drawn entirely by hand into one double-buffered surface (no
/// native list box), so selecting, hovering and scrolling never flicker.
/// Column widths can be changed by dragging the edges in the header.
/// </summary>
public sealed class RowList : Control
{
    private const int HeaderHeight = 34;
    private readonly List<object> _items = new();
    private readonly VScrollBar _scroll = new() { Dock = DockStyle.Right, Visible = false, SmallChange = 24 };
    private readonly Func<object, Row> _describe;
    private readonly int _rowHeight;
    private readonly bool _compact, _header;
    private int _selected = -1, _hover = -1, _offset;
    private int _dragColumn = -1, _dragStartX, _dragStartWidth;

    public Column[] Columns { get; }
    public bool ShowIndex { get; }
    public string EmptyText { get; set; } = "";

    public event Action? SelectionChanged;
    public event Action? ItemActivated;

    /// <summary>Raised after the user dragged a column to a new width.</summary>
    public event Action? ColumnsChanged;

    private readonly bool _threeLines;

    public RowList(Func<object, Row> describe, Column[]? columns = null, bool showIndex = true, bool compact = false, bool header = true, int lines = 2)
    {
        _threeLines = lines >= 3 && !compact;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _describe = describe;
        Columns = columns ?? Array.Empty<Column>();
        ShowIndex = showIndex;
        _compact = compact;
        _header = header && !compact;
        _rowHeight = compact ? 44 : _threeLines ? 80 : 60;
        BackColor = compact ? Theme.Card : Theme.Surface;
        TabStop = true;
        Controls.Add(_scroll);
        _scroll.ValueChanged += (_, _) => { _offset = _scroll.Value; Invalidate(); };
    }

    public object? SelectedItem
    {
        get => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;
        set
        {
            int i = value == null ? -1 : IndexOf(value);
            if (i >= 0) Select(i);
        }
    }

    public int SelectedIndex => _selected;
    public int Count => _items.Count;

    private int IndexOf(object value)
    {
        for (int i = 0; i < _items.Count; i++)
            if (ReferenceEquals(_items[i], value) || Equals(_items[i], value)) return i;
        return -1;
    }

    /// <summary>Replaces the rows, keeping (or setting) the selection.</summary>
    public void SetItems(IEnumerable<object> items, object? select = null)
    {
        select ??= SelectedItem;
        _items.Clear();
        _items.AddRange(items);
        int index = select == null ? -1 : IndexOf(select);
        _selected = index >= 0 ? index : _items.Count > 0 ? 0 : -1;
        _hover = -1;
        UpdateScroll();
        EnsureVisible(_selected);
        Invalidate();
        SelectionChanged?.Invoke();
    }

    /// <summary>Redraws the rows (after an edit changed their text).</summary>
    public void Redraw() => Invalidate();

    private void Select(int index)
    {
        if (index < 0 || index >= _items.Count || index == _selected) return;
        _selected = index;
        EnsureVisible(index);
        Invalidate();
        SelectionChanged?.Invoke();
    }

    // ------------------------------------------------------------ scrolling

    private int ListTop => _header ? HeaderHeight : 0;
    private int ViewHeight => Math.Max(0, Height - ListTop);
    private int ContentWidth => Width - (_scroll.Visible ? _scroll.Width : 0);

    private void UpdateScroll()
    {
        int total = _items.Count * _rowHeight;
        if (total > ViewHeight && ViewHeight > 0)
        {
            _scroll.Top = ListTop;
            _scroll.LargeChange = ViewHeight;
            _scroll.Maximum = total - 1;
            _offset = Math.Clamp(_offset, 0, total - ViewHeight);
            _scroll.Value = _offset;
            _scroll.Visible = true;
        }
        else
        {
            _scroll.Visible = false;
            _offset = 0;
        }
    }

    private void ScrollTo(int offset)
    {
        int max = Math.Max(0, _items.Count * _rowHeight - ViewHeight);
        offset = Math.Clamp(offset, 0, max);
        if (offset == _offset) return;
        _offset = offset;
        if (_scroll.Visible) _scroll.Value = offset;
        Invalidate();
    }

    private void EnsureVisible(int index)
    {
        if (index < 0) return;
        int top = index * _rowHeight;
        if (top < _offset) ScrollTo(top);
        else if (top + _rowHeight > _offset + ViewHeight) ScrollTo(top + _rowHeight - ViewHeight);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll();
    }

    // Mouse wheel: handled by WheelRouter / SmoothScroll through these.
    public bool CanScroll => _scroll.Visible;
    public int MaxScroll => Math.Max(0, _items.Count * _rowHeight - ViewHeight);

    public int ScrollOffset
    {
        get => _offset;
        set
        {
            ScrollTo(value);
            Update(); // draw this animation frame now
        }
    }

    // ------------------------------------------------------------ mouse / keys

    private int IndexAt(int y)
    {
        if (y < ListTop) return -1;
        int i = (y - ListTop + _offset) / _rowHeight;
        return i >= 0 && i < _items.Count ? i : -1;
    }

    /// <summary>Column whose left edge is under x (in the header), for resizing.</summary>
    private int ColumnEdgeAt(int x)
    {
        var rects = ColumnRects(new Rectangle(0, 0, ContentWidth, HeaderHeight), out _, out _);
        for (int i = 0; i < rects.Length; i++)
            if (Math.Abs(x - rects[i].Left) <= 5) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragColumn >= 0)
        {
            // Dragging a column's left edge to the left makes it wider.
            int width = Math.Clamp(_dragStartWidth + (_dragStartX - e.X), 40, 700);
            int others = Columns.Where((_, i) => i != _dragColumn).Sum(c => c.Width);
            width = Math.Min(width, Math.Max(40, ContentWidth - others - (ShowIndex ? 48 : 0) - 180)); // keep room for the title
            if (width != Columns[_dragColumn].Width) { Columns[_dragColumn].Width = width; Invalidate(); }
            return;
        }
        if (_header && e.Y < HeaderHeight)
        {
            Cursor = ColumnEdgeAt(e.X) >= 0 ? Cursors.VSplit : Cursors.Default;
            if (_hover != -1) { _hover = -1; Invalidate(); }
            return;
        }
        Cursor = Cursors.Default;
        int hover = IndexAt(e.Y);
        if (hover != _hover) { _hover = hover; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        if (_header && e.Y < HeaderHeight)
        {
            _dragColumn = ColumnEdgeAt(e.X);
            if (_dragColumn >= 0) { _dragStartX = e.X; _dragStartWidth = Columns[_dragColumn].Width; Capture = true; }
            return;
        }
        int i = IndexAt(e.Y);
        if (i >= 0) Select(i);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragColumn < 0) return;
        _dragColumn = -1;
        Capture = false;
        ColumnsChanged?.Invoke();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (IndexAt(e.Y) >= 0) ItemActivated?.Invoke();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragColumn < 0) Cursor = Cursors.Default;
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int page = Math.Max(1, ViewHeight / _rowHeight);
        int target = e.KeyCode switch
        {
            Keys.Up => _selected - 1,
            Keys.Down => _selected + 1,
            Keys.PageUp => _selected - page,
            Keys.PageDown => _selected + page,
            Keys.Home => 0,
            Keys.End => _items.Count - 1,
            _ => int.MinValue,
        };
        if (target != int.MinValue && _items.Count > 0) Select(Math.Clamp(target, 0, _items.Count - 1));
        if (e.KeyCode == Keys.Enter) ItemActivated?.Invoke();
    }

    // ------------------------------------------------------------ drawing

    private Rectangle[] ColumnRects(Rectangle bounds, out Rectangle index, out Rectangle main)
    {
        int x = bounds.Right - 12;
        var rects = new Rectangle[Columns.Length];
        for (int i = Columns.Length - 1; i >= 0; i--)
        {
            x -= Columns[i].Width;
            rects[i] = new Rectangle(x, bounds.Top, Columns[i].Width, bounds.Height);
        }
        int left = bounds.Left + 8;
        index = ShowIndex ? new Rectangle(left, bounds.Top, 34, bounds.Height) : Rectangle.Empty;
        if (ShowIndex) left += 40;
        main = new Rectangle(left, bounds.Top, Math.Max(40, x - left - 8), bounds.Height);
        return rects;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int width = ContentWidth;

        if (_header)
        {
            var header = new Rectangle(0, 0, width, HeaderHeight);
            var cols = ColumnRects(header, out var index, out var main);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (ShowIndex) TextRenderer.DrawText(g, "#", Theme.Caption, index, Theme.Muted, flags | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "Title", Theme.Caption, main, Theme.Muted, flags);
            using var grip = new Pen(Theme.CardSelected);
            for (int i = 0; i < cols.Length; i++)
            {
                TextRenderer.DrawText(g, Columns[i].Title, Theme.Caption, cols[i] with { X = cols[i].X + 6, Width = cols[i].Width - 6 }, Theme.Muted, flags);
                g.DrawLine(grip, cols[i].Left, 9, cols[i].Left, HeaderHeight - 9); // drag handle
            }
            using var line = new Pen(Theme.CardSelected);
            g.DrawLine(line, 8, HeaderHeight - 1, width - 8, HeaderHeight - 1);
        }

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, Theme.Body, new Rectangle(0, ListTop, width, Math.Max(_rowHeight, ViewHeight)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        g.SetClip(new Rectangle(0, ListTop, width, ViewHeight));
        int first = _offset / _rowHeight;
        for (int i = first; i < _items.Count; i++)
        {
            int y = ListTop + i * _rowHeight - _offset;
            if (y > Height) break;
            DrawRow(g, i, new Rectangle(0, y, width, _rowHeight));
        }
        g.ResetClip();
    }

    private void DrawRow(Graphics g, int index, Rectangle bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool selected = index == _selected;
        var row = _describe(_items[index]);
        var card = new RectangleF(bounds.X + 4, bounds.Y + 2, bounds.Width - 8, bounds.Height - 4);
        if (selected) Theme.FillRounded(g, Theme.CardSelected, card, 6);
        else if (index == _hover) Theme.FillRounded(g, Theme.CardHover, card, 6);

        var cols = ColumnRects(bounds, out var indexRect, out var main);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        if (ShowIndex)
            TextRenderer.DrawText(g, (index + 1).ToString(), Theme.Big, indexRect, selected ? Theme.Accent : Theme.Muted, flags | TextFormatFlags.HorizontalCenter);

        // Thumbnail: picture or a colored rounded square with a symbol/letter.
        int thumb = bounds.Height - 16;
        var thumbRect = new Rectangle(main.X, bounds.Y + 8, thumb, thumb);
        if (row.Thumb != null)
        {
            using var path = Theme.Rounded(thumbRect, 5);
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(row.Thumb, thumbRect);
            g.Restore(state);
        }
        else
        {
            Theme.FillRounded(g, row.ThumbColor, thumbRect, 5);
            TextRenderer.DrawText(g, row.ThumbText, Theme.Small, thumbRect,
                Theme.IsLight(row.ThumbColor) ? Color.Black : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        int textX = thumbRect.Right + 12;
        int textW = main.Right - textX;
        var titleColor = selected ? Theme.Accent : row.TitleColor;
        int titleH = Theme.RowTitle.Height;

        if (_compact)
        {
            // One line: the title always in the same place, extra info to the side (never shifts the title).
            int sideW = row.Subtitle.Length == 0 ? 0 : Math.Min(TextRenderer.MeasureText(row.Subtitle, Theme.Caption).Width + 6, textW * 2 / 5);
            int badgesW = DrawBadges(g, row, 0, 0, measureOnly: true);
            int titleW = Math.Max(40, textW - sideW - badgesW - 10);
            int measured = TextRenderer.MeasureText(g, row.Title, Theme.RowTitle, Size.Empty, TextFormatFlags.NoPadding).Width + 4;
            var titleRect = new Rectangle(textX, bounds.Y, Math.Min(titleW, measured), bounds.Height);
            TextRenderer.DrawText(g, row.Title, Theme.RowTitle, titleRect, titleColor, flags | TextFormatFlags.NoPadding);
            DrawBadges(g, row, titleRect.Right + 8, bounds.Y + (bounds.Height - Theme.Small.Height - 6) / 2, measureOnly: false, limit: main.Right - sideW);
            if (sideW > 0)
                TextRenderer.DrawText(g, row.Subtitle, Theme.Caption, new Rectangle(main.Right - sideW, bounds.Y, sideW, bounds.Height),
                    Theme.Muted, flags | TextFormatFlags.Right);
        }
        else
        {
            // Lines at fixed positions: the title never moves, with or without the lines under it.
            // The name always gets the room first; badges only use what's left after it.
            int lineH = Theme.Caption.Height + 3;
            int blockH = titleH + 2 + lineH * (_threeLines ? 2 : 1);
            int titleY = bounds.Y + (bounds.Height - blockH) / 2;
            int measured = TextRenderer.MeasureText(g, row.Title, Theme.RowTitle, Size.Empty, TextFormatFlags.NoPadding).Width + 4;
            var titleRect = new Rectangle(textX, titleY, Math.Min(measured, textW), titleH + 2);
            TextRenderer.DrawText(g, row.Title, Theme.RowTitle, titleRect, titleColor, flags | TextFormatFlags.NoPadding);
            DrawBadges(g, row, titleRect.Right + 8, titleY + (titleH - Theme.Small.Height - 6) / 2 + 1, measureOnly: false, limit: main.Right);
            int y = titleY + titleH + 3;
            if (row.Subtitle.Length > 0)
                TextRenderer.DrawText(g, row.Subtitle, Theme.Caption, new Rectangle(textX, y, textW, lineH), Theme.Muted, flags | TextFormatFlags.NoPadding);
            if (_threeLines && row.Detail.Length > 0)
                TextRenderer.DrawText(g, row.Detail, Theme.Caption, new Rectangle(textX, y + lineH, textW, lineH), row.DetailColor, flags | TextFormatFlags.NoPadding);
        }

        for (int i = 0; i < cols.Length && i < row.Columns.Length; i++)
        {
            var color = row.ColumnColors != null && i < row.ColumnColors.Length ? row.ColumnColors[i] : Theme.Muted;
            TextRenderer.DrawText(g, row.Columns[i], Theme.Body, cols[i] with { X = cols[i].X + 6, Width = cols[i].Width - 10 }, color, flags);
        }
    }

    /// <summary>Draws the badges from x; returns the width they take.</summary>
    private static int DrawBadges(Graphics g, Row row, int x, int y, bool measureOnly, int limit = int.MaxValue)
    {
        int start = x;
        foreach (var (text, color) in row.Badges)
        {
            int w = TextRenderer.MeasureText(text, Theme.Small).Width + 14;
            if (!measureOnly)
            {
                if (x + w > limit) break;
                Theme.DrawBadge(g, text, color, x, y);
            }
            x += w + 6;
        }
        return x - start;
    }
}

/// <summary>
/// A <see cref="RowList"/> over a List&lt;T&gt; with Add / Duplicate / Remove /
/// Up / Down buttons. The list comes from <c>getList</c>, so pointing it at
/// another trader/quest is just <see cref="RefreshList"/>.
/// </summary>
public sealed class ListEditor<T> : Panel where T : class
{
    private readonly Func<List<T>?> _getList;
    private readonly Func<T?> _create;
    private readonly Func<T, T>? _duplicate;
    private readonly RowList _rows;
    private readonly Toolbar _buttons;

    public event Action? SelectionChanged;
    public event Action? ListChanged;

    public T? Selected => _rows.SelectedItem as T;
    public RowList Rows => _rows;
    public Toolbar Buttons => _buttons;

    public ListEditor(Func<List<T>?> getList, Func<T?> create, Func<T, Row> describe, string addText = "Add",
        Func<T, T>? duplicate = null, Column[]? columns = null, bool compact = false, int height = 0, int lines = 2)
    {
        _getList = getList;
        _create = create;
        _duplicate = duplicate;
        BackColor = Color.Transparent;
        if (height > 0) Height = height;

        _rows = new RowList(o => describe((T)o), columns, showIndex: !compact, compact: compact, header: !compact, lines: lines) { Dock = DockStyle.Fill };
        var add = new PillButton(addText, PillStyle.Primary);
        var dup = new PillButton("Duplicate", PillStyle.Outline) { Visible = duplicate != null };
        var remove = new PillButton("Remove", PillStyle.Danger);
        var up = new PillButton("▲", PillStyle.Ghost) { Width = 36 };
        var down = new PillButton("▼", PillStyle.Ghost) { Width = 36 };
        new ToolTip().SetToolTip(up, "Move up");
        new ToolTip().SetToolTip(down, "Move down");
        _buttons = new Toolbar(add, dup, remove, up, down) { Dock = compact ? DockStyle.Bottom : DockStyle.Top, WrapContents = false, Padding = new Padding(0, 2, 0, 6) };

        Controls.Add(_rows);
        Controls.Add(_buttons);

        _rows.SelectionChanged += () => SelectionChanged?.Invoke();
        add.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null) return;
            var item = _create();
            if (item == null) return;
            int at = Selected == null ? list.Count : list.IndexOf(Selected) + 1;
            list.Insert(Math.Clamp(at, 0, list.Count), item);
            RefreshList(item);
            ListChanged?.Invoke();
        };
        dup.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null || Selected == null || _duplicate == null) return;
            var copy = _duplicate(Selected);
            list.Insert(list.IndexOf(Selected) + 1, copy);
            RefreshList(copy);
            ListChanged?.Invoke();
        };
        remove.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null || Selected == null) return;
            int index = list.IndexOf(Selected);
            list.Remove(Selected);
            RefreshList(list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)]);
            ListChanged?.Invoke();
        };
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(+1);
    }

    public string EmptyText { get => _rows.EmptyText; set => _rows.EmptyText = value; }

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

    /// <summary>Rebuilds the rows (keeping or setting the selection).</summary>
    public void RefreshList(T? select = null)
    {
        var list = _getList();
        _rows.SetItems(list?.Cast<object>() ?? Enumerable.Empty<object>(), select);
        Enabled = list != null;
    }

    public void Select(T item) => _rows.SelectedItem = item;

    /// <summary>Redraws the rows after an edit, without touching the selection.</summary>
    public void RefreshTexts() => _rows.Redraw();
}

// -----------------------------------------------------------------------------
// Item / id lists
// -----------------------------------------------------------------------------

/// <summary>
/// A small list of item ids (objective items, weapons, worn gear...) with
/// Add / Remove and optional quick buttons.
/// </summary>
public sealed class ItemListPanel : StackPanel
{
    private readonly Func<List<string>?> _getList;
    private readonly Func<string, Row> _describe;
    private readonly RowList _rows;
    private readonly Label _title;
    private readonly Toolbar _buttons;

    public event Action? Changed;

    /// <param name="pick">Returns the value to add (e.g. an item id from the picker), or null.</param>
    public ItemListPanel(string title, Func<List<string>?> getList, Func<IWin32Window, string?> pick, Func<string, Row> describe, Color accent, int height = 150)
    {
        _getList = getList;
        _describe = describe;
        Gap = 4;
        _title = new Label { Text = title, AutoSize = false, Height = 24, ForeColor = accent, Font = Theme.BodyBold, BackColor = Color.Transparent };
        _rows = new RowList(o => _describe((string)o), showIndex: false, compact: true, header: false) { Height = height };
        var add = new PillButton("+ Add...", PillStyle.Outline) { Height = 30 };
        var remove = new PillButton("Remove", PillStyle.Danger) { Height = 30 };
        _buttons = new Toolbar(add, remove);
        AddRange(_title, _rows, _buttons);

        add.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null) return;
            var value = pick(FindForm()!);
            if (value == null) return;
            if (!list.Contains(value)) list.Add(value);
            RefreshList();
            Changed?.Invoke();
        };
        remove.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null || _rows.SelectedItem is not string value) return;
            list.Remove(value);
            RefreshList();
            Changed?.Invoke();
        };
    }

    public string Title { get => _title.Text; set => _title.Text = value; }

    /// <summary>Extra one-click button (e.g. "+ Roubles") that adds a fixed value.</summary>
    public void AddQuick(string text, string value, Color? color = null)
    {
        var b = new PillButton(text, PillStyle.Chip) { Height = 30, Tint = color };
        b.Click += (_, _) =>
        {
            var list = _getList();
            if (list == null || list.Contains(value)) return;
            list.Add(value);
            RefreshList();
            Changed?.Invoke();
        };
        _buttons.Controls.Add(b);
    }

    public void RefreshList()
    {
        _rows.EmptyText = "Nothing added yet";
        _rows.SetItems(_getList()?.Cast<object>() ?? Enumerable.Empty<object>());
    }
}

/// <summary>Tick boxes over a list of ids (maps, bosses).</summary>
public sealed class CheckListPanel : StackPanel
{
    private readonly Func<List<string>?> _getList;
    private readonly List<(string Id, PillButton Chip)> _chips = new();

    public event Action? Changed;

    /// <summary>Clickable chips (lit = picked) over a list of ids, e.g. maps or bosses.</summary>
    public CheckListPanel(string title, (string Id, string Name)[] options, Func<List<string>?> getList, Color accent, int height = 0)
    {
        _getList = getList;
        Gap = 4;
        var bar = new Toolbar();
        foreach (var (id, name) in options)
        {
            var chip = new PillButton(name, PillStyle.Chip) { Height = 30, Tint = accent, Margin = new Padding(0, 2, 6, 2) };
            chip.Click += (_, _) => Toggle(id, chip);
            _chips.Add((id, chip));
            bar.Controls.Add(chip);
        }
        AddRange(new Label { Text = title, AutoSize = false, Height = 24, ForeColor = accent, Font = Theme.BodyBold, BackColor = Color.Transparent }, bar);
    }

    private void Toggle(string id, PillButton chip)
    {
        var list = _getList();
        if (list == null) return;
        if (list.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(id);
        chip.Selected = list.Contains(id, StringComparer.OrdinalIgnoreCase);
        Changed?.Invoke();
    }

    public void RefreshList()
    {
        var list = _getList() ?? new List<string>();
        foreach (var (id, chip) in _chips) chip.Selected = list.Contains(id, StringComparer.OrdinalIgnoreCase);
    }
}

public static class Prompt
{
    public static string? Ask(IWin32Window owner, string title, string question, string initial = "")
    {
        using var form = new Form { Text = title, Width = 460, Height = 190, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        Theme.Dialog(form);
        var label = new Label { Text = question, Left = 16, Top = 14, Width = 410, Height = 24 };
        var box = new TextBox { Text = initial, Left = 16, Top = 42, Width = 410 };
        var ok = new PillButton("OK", PillStyle.Primary) { Left = 236, Top = 90, Width = 90, DialogResult = DialogResult.OK };
        var cancel = new PillButton("Cancel", PillStyle.Outline) { Left = 336, Top = 90, Width = 90, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { label, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}
