using System.Runtime.InteropServices;

namespace CustomTraders.Editor;

// -----------------------------------------------------------------------------
// Smooth motion: eased wheel scrolling that follows the mouse, and cross-fades
// when a page / trader / quest changes, so nothing ever flashes half-drawn.
// -----------------------------------------------------------------------------

/// <summary>
/// Sends the mouse wheel to the list or panel under the mouse (Windows would
/// send it to whichever control has focus), and scrolls it smoothly. Number
/// boxes and drop-downs under the mouse are scrolled past instead of having
/// their value changed by accident.
/// </summary>
public sealed class WheelRouter : IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        int delta = (short)((long)m.WParam >> 16);
        var hwnd = WindowFromPoint(Cursor.Position);
        var control = Control.FromChildHandle(hwnd);
        if (control == null) return false; // e.g. an open drop-down list: let Windows handle it

        for (var c = control; c != null; c = c.Parent)
        {
            // A focused multi-line text box scrolls its own text.
            if (c is TextBox { Multiline: true, Focused: true }) return false;
            if (c is ListView) return false; // item picker: native scrolling

            if (SmoothScroll.CanScroll(c))
            {
                SmoothScroll.By(c, -delta * 110 / 120);
                return true;
            }
        }
        return true; // nothing scrollable under the mouse: swallow (don't change a focused number box)
    }
}

/// <summary>Eased scrolling for <see cref="RowList"/> and auto-scrolling panels.</summary>
public static class SmoothScroll
{
    private sealed class State
    {
        public float Current;
        public float Target;
    }

    private static readonly Dictionary<Control, State> Moving = new();
    private static readonly System.Windows.Forms.Timer Timer = new() { Interval = 10 };

    static SmoothScroll()
    {
        Timer.Tick += (_, _) => Step();
    }

    public static bool CanScroll(Control c) => c switch
    {
        RowList list => list.CanScroll,
        ScrollableControl s => s.AutoScroll && s.VerticalScroll.Visible && Max(c) > 0,
        _ => false,
    };

    private static int Get(Control c) => c switch
    {
        RowList list => list.ScrollOffset,
        ScrollableControl s => -s.AutoScrollPosition.Y,
        _ => 0,
    };

    private static int Max(Control c) => c switch
    {
        RowList list => list.MaxScroll,
        ScrollableControl s => Math.Max(0, s.DisplayRectangle.Height - s.ClientSize.Height),
        _ => 0,
    };

    private static void Set(Control c, int value)
    {
        switch (c)
        {
            case RowList list:
                list.ScrollOffset = value;
                break;
            case ScrollableControl s:
                s.AutoScrollPosition = new Point(0, value);
                s.Update(); // paint this frame now, not later in one jump
                break;
        }
    }

    /// <summary>Scrolls by <paramref name="pixels"/> (positive = down), animated.</summary>
    public static void By(Control c, int pixels)
    {
        if (!Moving.TryGetValue(c, out var state))
        {
            state = new State { Current = Get(c) };
            state.Target = state.Current;
            Moving[c] = state;
        }
        state.Target = Math.Clamp(state.Target + pixels, 0, Max(c));
        Timer.Start();
    }

    private static void Step()
    {
        foreach (var (control, state) in Moving.ToList())
        {
            if (control.IsDisposed || !control.Visible) { Moving.Remove(control); continue; }
            // Ease out: cover a third of the remaining distance each frame.
            state.Current += (state.Target - state.Current) * 0.32f;
            if (Math.Abs(state.Target - state.Current) < 0.6f) state.Current = state.Target;
            Set(control, (int)Math.Round(state.Current));
            if (state.Current == state.Target) Moving.Remove(control);
        }
        if (Moving.Count == 0) Timer.Stop();
    }
}

/// <summary>
/// A click-through picture of the old screen laid over part of the window
/// that fades out while the new content is already drawn underneath.
/// </summary>
internal sealed class FadeOverlay : Form
{
    private readonly Bitmap _shot;

    public FadeOverlay(Bitmap shot, Rectangle screenBounds)
    {
        _shot = shot;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = screenBounds;
        BackgroundImage = shot;
        BackgroundImageLayout = ImageLayout.None;
        Opacity = 1;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // tool window (no taskbar/alt-tab), never takes focus, clicks go through, layered (fades)
            cp.ExStyle |= 0x00000080 | 0x08000000 | 0x00000020 | 0x00080000;
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _shot.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Cross-fades a region of a form while its content changes.</summary>
public sealed class Crossfader(Form form)
{
    private const int DurationMs = 170;
    private bool _busy;

    /// <summary>
    /// Photographs <paramref name="area"/>, runs <paramref name="change"/>
    /// under the photo, then fades the photo away. Falls back to a plain
    /// change when the window isn't on screen.
    /// </summary>
    public void Run(Control area, Action change)
    {
        if (_busy || !form.Visible || form.WindowState == FormWindowState.Minimized || Form.ActiveForm != form ||
            !area.IsHandleCreated || !area.Visible || area.Width < 4 || area.Height < 4)
        {
            change();
            return;
        }

        _busy = true;
        FadeOverlay? overlay = null;
        bool changed = false;
        try
        {
            var rect = area.RectangleToScreen(area.ClientRectangle);
            var shot = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(shot))
                g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
            overlay = new FadeOverlay(shot, rect);
            overlay.Show(form);
            overlay.Update();

            changed = true;
            change();
            form.Update(); // the new content is fully drawn under the photo
        }
        catch
        {
            // screen capture not possible (locked screen, remote desktop...): no fade
            overlay?.Dispose();
            _busy = false;
            if (!changed) change();
            return;
        }

        var fading = overlay;
        var started = Environment.TickCount64;
        var timer = new System.Windows.Forms.Timer { Interval = 10 };
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - started) / (double)DurationMs);
            double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            if (t >= 1 || fading.IsDisposed)
            {
                timer.Stop();
                timer.Dispose();
                fading.Close();
                fading.Dispose();
                _busy = false;
                return;
            }
            fading.Opacity = 1 - eased;
        };
        timer.Start();
    }
}
