using System.Diagnostics;
using System.Text;

namespace ModernEditor.Editor;

/// <summary>
/// Logs\ModernEditor_&lt;date&gt;_&lt;time&gt;.log next to ModernEditor.exe (or %AppData%\ModernEditor\Logs when that folder is
/// read-only): one file per start, the last 30 kept. Everything goes in — startup facts, every request from the page and how
/// long it took, files read and written, the page's own errors and messages, crashes with their full stack — so the file
/// can simply be sent along when something goes wrong.
/// </summary>
public static class EditorLog
{
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static string Folder { get; private set; } = "";
    public static string File { get; private set; } = "";

    public static void Start(string version)
    {
        foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "Logs"), Path.Combine(Settings.Folder, "Logs") })
        {
            try
            {
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"ModernEditor_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                System.IO.File.AppendAllText(file, "");
                Folder = dir; File = file;
                break;
            }
            catch { /* try the next place */ }
        }
        try
        {
            foreach (var old in new DirectoryInfo(Folder).GetFiles("ModernEditor_*.log").OrderByDescending(f => f.Name).Skip(30)) old.Delete();
        }
        catch { }
        Info("start", $"Modern Editor {version}");
        Info("start", $"Windows {Environment.OSVersion.Version} · {(Environment.Is64BitProcess ? "64" : "32")}-bit · .NET {Environment.Version} · culture {System.Globalization.CultureInfo.CurrentCulture.Name}");
        Info("start", $"Program: {AppContext.BaseDirectory}");
        Info("start", $"Settings: {Settings.Folder}");
        Info("start", $"Log: {File}");
    }

    public static void Info(string where, string text) => Write("INFO ", where, text);
    public static void Warn(string where, string text) => Write("WARN ", where, text);
    public static void Error(string where, string text) => Write("ERROR", where, text);
    public static void Error(string where, Exception e) => Write("ERROR", where, e.ToString());

    /// <summary>A line from the page (its errors, warnings, what it showed the user).</summary>
    public static void Page(string level, string text) => Write(level.ToUpperInvariant() switch
    {
        "ERROR" => "ERROR",
        "WARN" or "WARNING" => "WARN ",
        _ => "INFO ",
    }, "page", text);

    private static void Write(string level, string where, string text)
    {
        if (File == "") return;
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(" +").Append((Clock.ElapsedMilliseconds / 1000.0).ToString("0.000")).Append("s  ")
            .Append(level).Append("  [").Append(where).Append("] ")
            .Append(text.Replace("\r\n", "\n").Replace("\n", "\n        "))
            .Append(Environment.NewLine).ToString();
        lock (Gate)
        {
            try { System.IO.File.AppendAllText(File, line); }
            catch { /* never let the log break the editor */ }
        }
    }

    /// <summary>A short look at a value for the log (long texts are cut, with their size).</summary>
    public static string Brief(string? s, int max = 300)
    {
        if (s == null) return "null";
        s = s.Replace("\r", "").Replace("\n", " ");
        return s.Length <= max ? s : s[..max] + $"… ({s.Length:N0} chars)";
    }
}
