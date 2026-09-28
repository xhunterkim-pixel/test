using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;

namespace ModernEditor.Server;

/// <summary>
/// Everything Modern Editor's server mod says goes to the SPT console as before and also to
/// user\mods\ModernEditor\logs\server_&lt;date&gt;_&lt;time&gt;.log (one file per server start, the last 20 kept),
/// with a few details the console doesn't show — so the file can be sent along when something is off.
/// </summary>
public static class ModLog
{
    public const string ModVersion = "2.0.5";
    private static readonly object Gate = new();
    private static string? _file;

    private static string? File
    {
        get
        {
            if (_file != null) return _file == "" ? null : _file;
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(typeof(ModLog).Assembly.Location)!, "logs");
                Directory.CreateDirectory(dir);
                foreach (var old in new DirectoryInfo(dir).GetFiles("server_*.log").OrderByDescending(f => f.Name).Skip(19)) old.Delete();
                _file = Path.Combine(dir, $"server_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                System.IO.File.WriteAllText(_file, $"Modern Editor server mod {ModVersion} · .NET {Environment.Version} · {Environment.OSVersion}{Environment.NewLine}");
            }
            catch { _file = ""; }
            return _file == "" ? null : _file;
        }
    }

    /// <summary>A line for the log file only (details too long for the console).</summary>
    public static void Detail(string text) => Write("INFO ", text);

    public static void MeInfo<T>(this ISptLogger<T> logger, string text) { Write("INFO ", text); logger.Info(text); }
    public static void MeWarning<T>(this ISptLogger<T> logger, string text) { Write("WARN ", text); logger.Warning(text); }
    public static void MeError<T>(this ISptLogger<T> logger, string text, Exception? e = null) { Write("ERROR", e == null ? text : text + Environment.NewLine + e); logger.Error(text, e); }
    public static void MeBlue<T>(this ISptLogger<T> logger, string text) { Write("INFO ", text); logger.LogWithColor(text, Spectre.Console.Color.DodgerBlue1, null, null); }

    private static void Write(string level, string text)
    {
        lock (Gate)
        {
            try
            {
                if (File is { } f) System.IO.File.AppendAllText(f, $"{DateTime.Now:HH:mm:ss.fff}  {level}  {text.Replace("\n", "\n        ")}{Environment.NewLine}");
            }
            catch { /* the log never stops the server */ }
        }
    }
}
