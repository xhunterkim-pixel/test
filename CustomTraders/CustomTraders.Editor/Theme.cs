using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CustomTraders.Editor;

/// <summary>
/// Dark, rounded look for the whole editor: colors, fonts, pill buttons,
/// badges, and <see cref="Apply"/> which restyles a control tree.
/// </summary>
public static class Theme
{
    // Spotify-like: black window, #121212 panels, green accent, white text.
    public static readonly Color Bg = Color.FromArgb(0, 0, 0);
    public static readonly Color Surface = Color.FromArgb(18, 18, 18);
    public static readonly Color Card = Color.FromArgb(31, 31, 31);
    public static readonly Color CardHover = Color.FromArgb(42, 42, 42);
    public static readonly Color CardSelected = Color.FromArgb(56, 56, 56);
    public static readonly Color Input = Color.FromArgb(42, 42, 42);
    public static readonly Color Border = Color.FromArgb(72, 72, 72);
    public static readonly Color Text = Color.FromArgb(255, 255, 255);
    public static readonly Color Muted = Color.FromArgb(179, 179, 179);

    public static readonly Color DefaultAccent = Color.FromArgb(30, 215, 96); // Spotify green

    /// <summary>Main button / highlight color — the user can pick any color (Appearance menu).</summary>
    public static Color Accent { get; set; } = DefaultAccent;

    /// <summary>Text color that reads on top of the accent (black on light colors, white on dark ones).</summary>
    public static Color OnAccent => IsLight(Accent) ? Color.Black : Color.White;
    public static readonly Color Violet = Color.FromArgb(160, 130, 255);
    public static readonly Color Pink = Color.FromArgb(255, 122, 182);
    public static readonly Color Green = Color.FromArgb(30, 215, 96);
    public static readonly Color Orange = Color.FromArgb(255, 164, 43);
    public static readonly Color Red = Color.FromArgb(241, 94, 108);
    public static readonly Color Blue = Color.FromArgb(80, 155, 245);
    public static readonly Color Yellow = Color.FromArgb(245, 205, 70);

    // Bold, chunky type like Spotify's (Segoe UI weights ship with Windows).
    public static readonly Font Body = new("Segoe UI Semibold", 10f);
    public static readonly Font BodyBold = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font RowTitle = new("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font Big = new("Segoe UI Semibold", 12f);
    public static readonly Font Small = new("Segoe UI", 8.5f, FontStyle.Bold);
    public static readonly Font Caption = new("Segoe UI Semibold", 9f);
    public static readonly Font Heading = new("Segoe UI Black", 13f);
    public static readonly Font Title = new("Segoe UI Black", 32f);
    public static readonly Font PanelTitle = new("Segoe UI Black", 16f);

    /// <summary>Color per objective type, so Kill / Hand over / Extract... never look alike.</summary>
    public static Color ForConditionType(string type) => type switch
    {
        Shared.ConditionTypes.HandoverItem => Blue,
        Shared.ConditionTypes.FindItem => Green,
        Shared.ConditionTypes.Kill => Red,
        Shared.ConditionTypes.Extract => Orange,
        Shared.ConditionTypes.UseItem => Pink,
        _ => Muted,
    };

    /// <summary>Color per quest option A-D.</summary>
    public static Color ForOption(int option) => option switch
    {
        1 => Violet,
        2 => Blue,
        3 => Yellow,
        _ => Pink,
    };

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>A small rounded "pill" label; returns its width.</summary>
    public static int DrawBadge(Graphics g, string text, Color color, int x, int y, Font? font = null)
    {
        font ??= Small;
        var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        var r = new Rectangle(x, y, size.Width + 14, size.Height + 6);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        FillRounded(g, Color.FromArgb(55, color), r, r.Height / 2f);
        using (var pen = new Pen(Color.FromArgb(170, color)))
        using (var path = Rounded(r, r.Height / 2f))
            g.DrawPath(pen, path);
        TextRenderer.DrawText(g, text, font, r, Lighten(color), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return r.Width;
    }

    public static bool IsLight(Color c) => c.R * 0.3 + c.G * 0.59 + c.B * 0.11 > 150;

    public static Color Lighten(Color c, float amount = 0.35f) =>
        Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

    public static Color Darken(Color c, float amount = 0.25f) =>
        Color.FromArgb(c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));

    // ------------------------------------------------------------------ apply

    /// <summary>Restyles a control and everything inside it.</summary>
    public static void Apply(Control root)
    {
        Style(root);
        foreach (Control child in root.Controls) Apply(child);
    }

    private static void Style(Control c)
    {
        switch (c)
        {
            case PillButton:
            case GlassPanel:
            case RowList:
            case Toggle:
                return; // draw themselves
            case TextBox t:
                t.ForeColor = Text;
                if (t.BorderStyle == BorderStyle.None) break; // e.g. the search pill
                t.BackColor = Input;
                t.BorderStyle = BorderStyle.None; // no grey frame; the card around it is the edge
                break;
            case NumericUpDown n:
                n.BackColor = Input;
                n.ForeColor = Text;
                n.BorderStyle = BorderStyle.None;
                break;
            case ComboBox cb:
                StyleCombo(cb);
                break;
            case CheckedListBox clb:
                clb.BackColor = Input;
                clb.ForeColor = Text;
                clb.BorderStyle = BorderStyle.None;
                break;
            case ListBox lb:
                lb.BackColor = Input;
                lb.ForeColor = Text;
                lb.BorderStyle = BorderStyle.None;
                break;
            case CheckBox cx:
                cx.ForeColor = Text;
                cx.BackColor = Color.Transparent;
                cx.FlatStyle = FlatStyle.Flat;
                cx.FlatAppearance.BorderColor = Accent;
                cx.FlatAppearance.CheckedBackColor = Accent;
                cx.Cursor = Cursors.Hand;
                break;
            case DataGridView grid:
                StyleGrid(grid);
                break;
            case Label l:
                if (l.ForeColor == SystemColors.ControlText || l.ForeColor == Color.Empty) l.ForeColor = Text;
                l.BackColor = Color.Transparent;
                break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Card;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Border;
                break;
            case PictureBox p:
                p.BackColor = Color.Transparent;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel:
                c.BackColor = Color.Transparent;
                c.ForeColor = Text;
                break;
        }
    }

    private static void StyleCombo(ComboBox cb)
    {
        cb.FlatStyle = FlatStyle.Flat;
        cb.BackColor = Input;
        cb.ForeColor = Text;
        if (cb.DrawMode != DrawMode.Normal) return;
        cb.DrawMode = DrawMode.OwnerDrawFixed;
        cb.ItemHeight = Math.Max(cb.ItemHeight, cb.Font.Height + 6);
        cb.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var back = new SolidBrush(selected ? CardSelected : Input)) e.Graphics.FillRectangle(back, e.Bounds);
            if (e.Index >= 0)
            {
                var r = e.Bounds; r.X += 4; r.Width -= 4;
                TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, r, Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        };
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Bg; // pitch black lines
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Card;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Card;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.RowHeadersDefaultCellStyle.BackColor = Card;
        grid.RowHeadersDefaultCellStyle.ForeColor = Text;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = CardSelected;
        grid.DefaultCellStyle.BackColor = Input;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = CardSelected;
        grid.DefaultCellStyle.SelectionForeColor = Text;
    }

    // ------------------------------------------------------------------ window chrome

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar on Windows 10 (20H1+) / 11.</summary>
    public static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }
        catch
        {
            // older Windows: normal title bar
        }
    }

    /// <summary>Standard dark dialog look (item picker, prompts).</summary>
    public static void Dialog(Form form)
    {
        form.BackColor = Color.FromArgb(24, 24, 24);
        form.ForeColor = Text;
        form.Font = Body;
        form.HandleCreated += (_, _) => DarkTitleBar(form);
        form.Load += (_, _) => Apply(form);
    }

    /// <summary>
    /// Copy of a picture scaled to cover <paramref name="size"/> (cropped, not
    /// stretched) and darkened so text on top stays readable.
    /// </summary>
    public static Bitmap CoverDarkened(Image source, Size size, int dimPercent)
    {
        var output = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(output);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float scale = Math.Max((float)output.Width / source.Width, (float)output.Height / source.Height);
        float w = source.Width * scale, h = source.Height * scale;
        g.DrawImage(source, (output.Width - w) / 2, (output.Height - h) / 2, w, h);
        using var dim = new SolidBrush(Color.FromArgb((int)(Math.Clamp(dimPercent, 0, 95) * 2.55), Bg));
        g.FillRectangle(dim, 0, 0, output.Width, output.Height);
        return output;
    }
}

public enum PillStyle
{
    /// <summary>Green filled with black text (Spotify's play button).</summary>
    Primary,
    /// <summary>Transparent with a light outline (Spotify's "Follow").</summary>
    Outline,
    /// <summary>Grey filled chip; white with black text when Selected ("By you" filter chip).</summary>
    Chip,
    /// <summary>Red outline, for delete/remove.</summary>
    Danger,
    /// <summary>Just text, lights up on hover (icon buttons).</summary>
    Ghost,
}

/// <summary>Rounded button in the Spotify styles above.</summary>
public class PillButton : Button
{
    private bool _hover, _down;
    private PillStyle _style = PillStyle.Outline;
    private bool _selected;
    private Color? _tint;

    public PillStyle Style { get => _style; set { _style = value; Invalidate(); } }

    /// <summary>Chips: drawn "on" (white) when true.</summary>
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    /// <summary>Optional color for a chip when selected (e.g. the objective type color).</summary>
    public Color? Tint { get => _tint; set { _tint = value; Invalidate(); } }

    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.BodyBold;
        Cursor = Cursors.Hand;
        Height = 34;
        Margin = new Padding(4);
    }

    public PillButton(string text, PillStyle style = PillStyle.Outline) : this()
    {
        Text = text;
        _style = style;
        Width = TextRenderer.MeasureText(text, Font).Width + 36;
    }

    /// <summary>A round button (the big green "play" circle), text is a symbol.</summary>
    public static PillButton Round(string symbol, int size, PillStyle style = PillStyle.Primary, string? tooltip = null)
    {
        var b = new PillButton(symbol, style) { Width = size, Height = size, Font = new Font("Segoe UI Symbol", size / 3.2f, FontStyle.Bold) };
        if (tooltip != null) new ToolTip().SetToolTip(b, tooltip);
        return b;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent != null) // what's behind (panel / picture) first
        {
            var state = g.Save();
            g.TranslateTransform(-Left, -Top);
            InvokePaintBackground(Parent, new PaintEventArgs(g, new Rectangle(Left, Top, Width, Height)));
            g.Restore(state);
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Height / 2f;
        Color? fill = null, outline = null;
        Color text = Theme.Text;

        switch (_style)
        {
            case PillStyle.Primary:
                fill = _down ? Theme.Darken(Theme.Accent, 0.15f) : _hover ? Theme.Lighten(Theme.Accent, 0.12f) : Theme.Accent;
                text = Theme.OnAccent;
                if (_hover && !_down) r.Inflate(0.8f, 0.8f); // Spotify's little grow on hover
                break;
            case PillStyle.Outline:
                outline = _hover ? Theme.Text : Theme.Border;
                if (_down) fill = Theme.CardHover;
                break;
            case PillStyle.Danger:
                outline = _hover ? Theme.Red : Theme.Darken(Theme.Red, 0.3f);
                text = _hover ? Theme.Red : Theme.Lighten(Theme.Red, 0.2f);
                break;
            case PillStyle.Chip:
                if (_selected)
                {
                    fill = _tint ?? Theme.Text;
                    text = Color.Black;
                }
                else
                {
                    fill = _hover ? Theme.CardSelected : Theme.CardHover;
                }
                break;
            case PillStyle.Ghost:
                text = _hover || _selected ? Theme.Text : Theme.Muted;
                if (_hover) fill = Theme.CardHover;
                break;
        }
        if (!Enabled) { text = Color.FromArgb(110, Theme.Muted); fill = fill == null ? null : Color.FromArgb(90, fill.Value); }

        if (fill is { } f) Theme.FillRounded(g, f, r, radius);
        if (outline is { } o)
        {
            using var pen = new Pen(o, 1.3f);
            using var path = Theme.Rounded(r, radius);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// A rounded, slightly see-through card that holds other controls; the
/// background picture shows faintly through it.
/// </summary>
public class GlassPanel : Panel
{
    public int Radius { get; set; } = 10;
    public Color Tint { get; set; } = Color.FromArgb(232, Theme.Surface);
    public Color? Outline { get; set; }

    public GlassPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Padding = new Padding(14);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e); // parent's background (picture) behind the card
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
        Theme.FillRounded(g, Tint, r, Radius);
        if (Outline is { } outline)
        {
            using var pen = new Pen(outline, 1.2f);
            using var path = Theme.Rounded(r, Radius);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>Spotify-style switch (green pill when on) used instead of tick boxes.</summary>
public sealed class Toggle : CheckBox
{
    private bool _hover;

    public Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        Height = 30;
        UseMnemonic = false;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(52 + text.Width, Math.Max(28, text.Height + 8));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent != null) // what's behind first
        {
            var state = g.Save();
            g.TranslateTransform(-Left, -Top);
            InvokePaintBackground(Parent, new PaintEventArgs(g, new Rectangle(Left, Top, Width, Height)));
            g.Restore(state);
        }

        var track = new RectangleF(1, (Height - 22) / 2f, 42, 22);
        Color fill = Checked ? (_hover ? Theme.Lighten(Theme.Accent, 0.12f) : Theme.Accent) : (_hover ? Theme.Border : Theme.CardSelected);
        if (!Enabled) fill = Color.FromArgb(90, fill);
        Theme.FillRounded(g, fill, track, 11);
        float knob = 16;
        float x = Checked ? track.Right - knob - 3 : track.X + 3;
        using (var brush = new SolidBrush(Checked ? Theme.OnAccent : Theme.Text))
            g.FillEllipse(brush, x, track.Y + 3, knob, knob);

        var textRect = new Rectangle(52, 0, Width - 52, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>One line of text, cut with "…" when it doesn't fit (never wraps).</summary>
public sealed class OneLineLabel : Label
{
    public OneLineLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        UseMnemonic = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
    }
}

/// <summary>A heading label with an optional colored dot in front.</summary>
public sealed class Heading : Label
{
    public Heading(string text, Color? dot = null)
    {
        Text = (dot != null ? "●  " : "") + text;
        Font = Theme.Heading;
        ForeColor = dot ?? Theme.Text;
        AutoSize = false;
        Height = 34;
        Dock = DockStyle.Top;
        TextAlign = ContentAlignment.MiddleLeft;
        BackColor = Color.Transparent;
    }
}

/// <summary>A control whose height depends on the width it gets (wrapping text).</summary>
public interface IHeightForWidth
{
    int HeightForWidth(int width);
}

/// <summary>Grey helper text that wraps.</summary>
public sealed class Hint : Label, IHeightForWidth
{
    public Hint(string text)
    {
        Text = text;
        ForeColor = Theme.Muted;
        AutoSize = false;
        BackColor = Color.Transparent;
        Padding = new Padding(2, 2, 2, 6);
        Height = 26;
        TextAlign = ContentAlignment.TopLeft;
        UseMnemonic = false;
    }

    public int HeightForWidth(int width)
    {
        if (Text.Length == 0) return 4;
        var size = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(20, width - Padding.Horizontal), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return size.Height + Padding.Vertical + 4;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        (Parent as StackPanel)?.Relayout();
    }
}
