namespace LevelGate.Editor;

internal static class Program
{
    private static readonly string CrashLog = Path.Combine(AppContext.BaseDirectory, "LevelGate.Editor.crash.txt");

    [STAThread]
    private static void Main()
    {
        // Never close silently: any error is shown and written next to the exe.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, fatal: true);

        // One editor at a time (two would overwrite each other's saves).
        using var single = new Mutex(true, @"Local\LevelGateEditor.SingleInstance", out bool first);
        if (!first)
        {
            BringOpenEditorToFront();
            return;
        }

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new HostForm());
        }
        catch (Exception e)
        {
            Report(e, fatal: true);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    private static void BringOpenEditorToFront()
    {
        try
        {
            var me = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
            {
                if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }
        catch
        {
            // couldn't reach it: the open editor stays as it is
        }
        MessageBox.Show("LevelGate Editor is already open.", HostForm.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void Report(Exception? e, bool fatal)
    {
        string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  LevelGate Editor {(fatal ? "crashed" : "error")}\n{e}\n\n";
        try { File.AppendAllText(CrashLog, text); }
        catch { /* read-only folder: the message box still shows it */ }
        MessageBox.Show(
            $"{(fatal ? "The editor crashed" : "Something went wrong")}:\n\n{e?.Message}\n\n" +
            $"Details were saved to:\n{CrashLog}\n\nSend that file (or a screenshot of this) to get it fixed.",
            HostForm.AppTitle, MessageBoxButtons.OK, fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
    }
}
