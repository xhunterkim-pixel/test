namespace CustomTraders.Editor;

internal static class Program
{
    private static readonly string CrashLog = Path.Combine(AppContext.BaseDirectory, "CustomTraders.Editor.crash.txt");

    [STAThread]
    private static void Main()
    {
        // Never close silently: any error is shown and written next to the exe.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, fatal: true);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#pragma warning disable WFO5001 // dark scrollbars / common controls (Windows 10+)
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
            Application.Run(new MainForm());
        }
        catch (Exception e)
        {
            Report(e, fatal: true);
        }
    }

    private static void Report(Exception? e, bool fatal)
    {
        string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  CustomTraders Editor {(fatal ? "crashed" : "error")}\n{e}\n\n";
        try { File.AppendAllText(CrashLog, text); }
        catch { /* read-only folder: the message box still shows it */ }
        MessageBox.Show(
            $"{(fatal ? "The editor crashed" : "Something went wrong")}:\n\n{e?.Message}\n\n" +
            $"Details were saved to:\n{CrashLog}\n\nSend that file (or a screenshot of this) to get it fixed.",
            "CustomTraders Editor", MessageBoxButtons.OK, fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
    }
}
