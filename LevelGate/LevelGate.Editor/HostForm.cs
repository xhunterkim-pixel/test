using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomTraders.Editor;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LevelGate.Editor;

/// <summary>
/// The editor window. The interface is a web page (ui\index.html) drawn by the
/// Edge engine (WebView2), like the Custom Trader Creator. This class only does
/// what a page can't: read / write level_requirements.json, load the game's
/// item list (and modded items), open pickers and serve item pictures.
///   page → { id, method, args }      host → { id, ok, result | error }
///   host → { event: "diskChanged", ... } when the file is changed by someone else (the game's F9 window)
/// </summary>
public sealed class HostForm : Form
{
    private const string AppHost = "app.local";
    private const string FilesHost = "files.local";

    public const string Version = "1.0.0";
    public const string AppTitle = "LevelGate Editor";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly EditorSettings _settings = EditorSettings.Load();
    private readonly ItemDatabase _db = new();
    private string? _configFile;
    private FileSystemWatcher? _watcher;
    private DateTime _ourWrite;
    private System.Windows.Forms.Timer? _diskTimer;
    private int _unsaved;

    public HostForm()
    {
        Text = AppTitle;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { /* keep the default */ }
        Width = 1640;
        Height = 1000;
        MinimumSize = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Controls.Add(_web);
        HandleCreated += (_, _) => DarkTitleBar();
        Shown += async (_, _) => await StartAsync();
        FormClosing += OnClosing;
    }

    // ------------------------------------------------------------------ startup

    private async Task StartAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(EditorSettings.Folder, "WebView2"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (MessageBox.Show(this,
                    "The editor needs the Microsoft Edge WebView2 Runtime (normally already part of Windows 10/11).\n\n" +
                    "Open the download page now?", AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                OpenUrl("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("LEVELGATE_EDITOR_DEVTOOLS") == "1";
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // no F5 reload (would lose unsaved edits)
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Allow);
        core.AddWebResourceRequestedFilter($"https://{FilesHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ServeFile;
        core.WebMessageReceived += (_, e) => HandleMessage(e.WebMessageAsJson);
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenUrl(e.Uri); };
        core.Navigate($"https://{AppHost}/index.html");
    }

    // ------------------------------------------------------------------ messages

    private void HandleMessage(string json)
    {
        JsonNode? id = null;
        try
        {
            var message = JsonNode.Parse(json)!.AsObject();
            id = message["id"]?.DeepClone();
            var method = (string?)message["method"] ?? "";
            var args = message["args"] as JsonObject ?? new JsonObject();
            Reply(new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = Call(method, args) });
        }
        catch (Exception e)
        {
            Reply(new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = e.Message });
        }
    }

    private void Reply(JsonObject reply) => _web.CoreWebView2?.PostWebMessageAsJson(reply.ToJsonString());

    private JsonNode? Call(string method, JsonObject a) => method switch
    {
        "init" => Snapshot(_settings.ConfigFile ?? FindConfigFile()),
        "reload" => Snapshot(_configFile),
        "browse" => Browse(),
        "save" => Save(a["set"] as JsonObject, a["remove"] as JsonArray),
        "modsScanAll" => ModsScanAll(),
        "modAddFolder" => ModAddFolder(),
        "modSet" => ModSet((string?)a["name"] ?? "", (bool?)a["enabled"] ?? true),
        "modRemove" => ModRemove((string?)a["name"] ?? ""),
        "modRescan" => ItemsPayload(rescan: true),
        "saveUi" => SaveUi(a["ui"] as JsonObject),
        "setUnsaved" => SetUnsaved((int?)a["count"] ?? 0),
        "openConfigFolder" => OpenConfigFolder(),
        _ => throw new InvalidOperationException($"Unknown request '{method}'."),
    };

    // ------------------------------------------------------------------ the config file

    /// <summary>Where LevelGate's config usually is: next to this exe, above it, or C:\SPT, D:\SPT...</summary>
    private static string? FindConfigFile()
    {
        var tail = Path.Combine("BepInEx", "plugins", "LevelGate", "config", "level_requirements.json");
        var tries = new List<string>();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            tries.Add(Path.Combine(dir.FullName, "config", "level_requirements.json")); // exe inside the plugin folder
            tries.Add(Path.Combine(dir.FullName, tail));
        }
        foreach (var drive in new[] { "C", "D", "E", "F" })
            foreach (var root in new[] { "SPT", "SPTarkov", "Games\\SPT" })
                tries.Add(Path.Combine($"{drive}:\\", root, tail));
        return tries.FirstOrDefault(File.Exists);
    }

    /// <summary>The SPT folder (the one holding BepInEx) above the config file.</summary>
    private string? SptRoot()
    {
        if (_configFile == null) return null;
        var dir = new FileInfo(_configFile).Directory;
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "BepInEx"))) return dir.FullName;
        return null;
    }

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private JsonObject ReadConfig()
    {
        if (_configFile == null || !File.Exists(_configFile)) return new JsonObject { ["items"] = new JsonObject() };
        var root = JsonNode.Parse(File.ReadAllText(_configFile), documentOptions: Lenient) as JsonObject ?? new JsonObject();
        if (root["items"] is not JsonObject) root["items"] = new JsonObject();
        return root;
    }

    private JsonObject Levels()
    {
        var levels = new JsonObject();
        foreach (var (id, v) in (JsonObject)ReadConfig()["items"]!)
            if (v is JsonValue value && value.TryGetValue(out int level)) levels[id] = level;
        return levels;
    }

    private long Modified() => _configFile != null && File.Exists(_configFile)
        ? new DateTimeOffset(File.GetLastWriteTimeUtc(_configFile)).ToUnixTimeMilliseconds() : 0;

    /// <summary>Everything the page shows: the level limits, the item list, mods, settings.</summary>
    private JsonObject Snapshot(string? configFile)
    {
        var result = new JsonObject
        {
            ["configFile"] = null,
            ["levels"] = new JsonObject(),
            ["ui"] = _settings.Ui?.DeepClone(),
            ["items"] = null,
            ["itemsStatus"] = "",
            ["version"] = Version,
        };
        if (string.IsNullOrWhiteSpace(configFile) || !Directory.Exists(Path.GetDirectoryName(configFile))) return result;

        _configFile = configFile;
        _settings.ConfigFile = configFile;
        _settings.Save();
        Watch();
        result["configFile"] = configFile;
        result["sptRoot"] = SptRoot();
        result["levels"] = Levels();
        result["modified"] = Modified();
        LoadItems(result);
        return result;
    }

    private JsonNode? Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Pick LevelGate's level_requirements.json (…\\BepInEx\\plugins\\LevelGate\\config)",
            Filter = "LevelGate config|level_requirements.json|JSON files|*.json",
            FileName = "level_requirements.json",
            CheckFileExists = false,
        };
        if (_configFile != null) dialog.InitialDirectory = Path.GetDirectoryName(_configFile);
        return dialog.ShowDialog(this) == DialogResult.OK ? Snapshot(dialog.FileName) : null;
    }

    /// <summary>
    /// Applies the page's changes to what's on disk right now (so anything the game's F9 window
    /// added meanwhile stays): existing entries keep their place, new ones go at the end.
    /// </summary>
    private JsonNode Save(JsonObject? set, JsonArray? remove)
    {
        if (_configFile == null) throw new InvalidOperationException("Pick level_requirements.json first (Browse…).");
        var root = ReadConfig();
        var items = (JsonObject)root["items"]!;
        foreach (var id in remove ?? new JsonArray())
            if ((string?)id is { } key) items.Remove(key);
        foreach (var (id, v) in set ?? new JsonObject())
        {
            int level = Math.Clamp((int?)v ?? 1, 1, 99);
            items[id] = level;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_configFile)!);
        var temp = _configFile + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _configFile, overwrite: true);
        _ourWrite = File.GetLastWriteTimeUtc(_configFile);
        return new JsonObject { ["levels"] = Levels(), ["modified"] = Modified() };
    }

    /// <summary>Tells the page when the file changes outside the editor (the game's F9 window, a text editor).</summary>
    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_configFile == null || Path.GetDirectoryName(_configFile) is not { } dir || !Directory.Exists(dir)) return;
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_configFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler changed = (_, _) => BeginInvoke(() =>
        {
            _diskTimer?.Stop();
            _diskTimer ??= new System.Windows.Forms.Timer { Interval = 400 };
            _diskTimer.Tick -= OnDiskSettled;
            _diskTimer.Tick += OnDiskSettled;
            _diskTimer.Start();
        });
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (s, e) => changed(s, e);
    }

    private void OnDiskSettled(object? sender, EventArgs e)
    {
        _diskTimer?.Stop();
        try
        {
            if (_configFile == null || !File.Exists(_configFile)) return;
            if (File.GetLastWriteTimeUtc(_configFile) == _ourWrite) return; // our own save
            _web.CoreWebView2?.PostWebMessageAsJson(new JsonObject
            {
                ["event"] = "diskChanged",
                ["levels"] = Levels(),
                ["modified"] = Modified(),
            }.ToJsonString());
        }
        catch
        {
            // half-written file: the next change event tries again
        }
    }

    private JsonNode OpenConfigFolder()
    {
        if (_configFile != null && Path.GetDirectoryName(_configFile) is { } dir && Directory.Exists(dir)) OpenUrl(dir);
        return true;
    }

    // ------------------------------------------------------------------ items

    private void LoadItems(JsonObject result)
    {
        try
        {
            var root = SptRoot();
            var dbFolder = root == null ? null : ItemDatabase.FindDatabaseFolder(root) ?? ItemDatabase.FindDatabaseFolder(Path.Combine(root, "SPT_Runtime"));
            if (dbFolder == null)
            {
                result["itemsStatus"] = "Item database not found (SPT_Data\\database in the SPT folder) — only ids can be shown.";
                return;
            }
            if (_db.SourceFolder != dbFolder || !_db.IsLoaded) _db.Load(dbFolder);
            var items = new JsonArray();
            foreach (var item in _db.Items.Values)
            {
                if (item.Category == ItemCategory.Money) continue;
                items.Add(new JsonObject
                {
                    ["i"] = item.Id,
                    ["n"] = item.Name,
                    ["s"] = item.ShortName,
                    ["c"] = item.Category.ToString(),
                    ["g"] = item.Group,
                    ["k"] = item.Caliber.Length > 0 ? item.Caliber : null,
                    ["wc"] = item.WeaponClass.Length > 0 ? item.WeaponClass : null,
                    ["h"] = _db.Handbook.TryGetValue(item.Id, out var h) ? Math.Round(h) : null,
                    ["f"] = _db.Flea.TryGetValue(item.Id, out var f) ? Math.Round(f) : null,
                    ["x"] = item.Hidden ? 1 : null,
                });
            }
            int modded = AddModItems(items, result);
            result["items"] = items;
            result["itemsStatus"] = $"{_db.Items.Count:N0} items" + (modded > 0 ? $" + {modded:N0} modded" : "");
        }
        catch (Exception e)
        {
            result["itemsStatus"] = "Item database failed to load: " + e.Message;
        }
    }

    // ------------------------------------------------------------------ items from other mods

    private readonly Dictionary<string, List<ModItem>> _modScan = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _modErrors = new(StringComparer.OrdinalIgnoreCase);

    private void ScanMods(bool rescan)
    {
        foreach (var mod in _settings.Mods)
        {
            if (!rescan && _modScan.ContainsKey(mod.Name)) continue;
            _modErrors.Remove(mod.Name);
            if (!Directory.Exists(mod.Folder))
            {
                _modScan[mod.Name] = new List<ModItem>();
                _modErrors[mod.Name] = "Folder not found — the mod was moved or uninstalled.";
                continue;
            }
            try { _modScan[mod.Name] = ModScanner.Scan(mod.Folder, _db); }
            catch (Exception e)
            {
                _modScan[mod.Name] = new List<ModItem>();
                _modErrors[mod.Name] = e.Message;
            }
        }
    }

    private int AddModItems(JsonArray items, JsonObject result)
    {
        ScanMods(rescan: false);
        int count = 0;
        var mods = new JsonArray();
        var off = new JsonArray();
        var seen = new HashSet<string>(_db.Items.Keys);
        foreach (var mod in _settings.Mods)
        {
            var list = _modScan.GetValueOrDefault(mod.Name) ?? new List<ModItem>();
            mods.Add(new JsonObject
            {
                ["name"] = mod.Name, ["folder"] = mod.Folder, ["enabled"] = mod.Enabled, ["count"] = list.Count,
                ["error"] = _modErrors.GetValueOrDefault(mod.Name),
            });
            foreach (var it in list)
            {
                if (!seen.Add(it.Id)) continue; // two mods / vanilla with the same id: first wins
                var node = new JsonObject
                {
                    ["i"] = it.Id, ["n"] = it.Name, ["s"] = it.ShortName, ["c"] = it.Category.ToString(), ["g"] = it.Group,
                    ["k"] = it.Caliber.Length > 0 ? it.Caliber : null,
                    ["wc"] = it.WeaponClass.Length > 0 ? it.WeaponClass : null,
                    ["h"] = it.Handbook > 0 ? Math.Round(it.Handbook) : null,
                    ["m"] = mod.Name,
                };
                if (mod.Enabled) { items.Add(node); count++; }
                else off.Add(node);
            }
        }
        result["mods"] = mods;
        result["modItemsOff"] = off;
        return count;
    }

    private JsonObject ItemsPayload(bool rescan = false)
    {
        var result = new JsonObject();
        if (rescan) _modScan.Clear();
        LoadItems(result);
        return result;
    }

    /// <summary>The server mods folder: SPT\SPT_Runtime\user\mods or SPT\user\mods.</summary>
    private string? ModsRoot()
    {
        var root = SptRoot();
        if (root == null) return null;
        return new[] { Path.Combine(root, "SPT_Runtime", "user", "mods"), Path.Combine(root, "user", "mods") }.FirstOrDefault(Directory.Exists);
    }

    private string ModNameOf(string folder)
    {
        var root = ModsRoot();
        var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        if (root != null && full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar)[0];
        return Path.GetFileName(full);
    }

    private JsonNode ModsScanAll()
    {
        var root = ModsRoot() ?? throw new InvalidOperationException("The SPT user\\mods folder wasn't found next to the LevelGate config — use Import a Mod Folder… instead.");
        if (!_db.IsLoaded) throw new InvalidOperationException("The game's item list isn't loaded yet.");
        var added = new JsonArray();
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (_settings.Mods.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            List<ModItem> found;
            try { found = ModScanner.Scan(dir, _db); } catch { continue; }
            if (found.Count == 0) continue;
            _settings.Mods.Add(new ModImport { Name = name, Folder = dir, Enabled = true });
            _modScan[name] = found;
            added.Add(name);
        }
        _settings.Save();
        var result = ItemsPayload();
        result["added"] = added;
        return result;
    }

    private JsonNode? ModAddFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Pick a mod folder (e.g. ...\\user\\mods\\WTT-ContentBackport) — its item files and en.json are scanned",
            UseDescriptionForTitle = true,
        };
        if (ModsRoot() is { } root) dialog.InitialDirectory = root;
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        var name = ModNameOf(dialog.SelectedPath);
        var existing = _settings.Mods.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { existing.Folder = dialog.SelectedPath; existing.Enabled = true; }
        else _settings.Mods.Add(new ModImport { Name = name, Folder = dialog.SelectedPath, Enabled = true });
        _modScan.Remove(name);
        _settings.Save();
        var result = ItemsPayload();
        result["added"] = new JsonArray(name);
        return result;
    }

    private JsonNode ModSet(string name, bool enabled)
    {
        var mod = _settings.Mods.FirstOrDefault(m => m.Name == name) ?? throw new InvalidOperationException($"No imported mod '{name}'.");
        mod.Enabled = enabled;
        _settings.Save();
        return ItemsPayload();
    }

    private JsonNode ModRemove(string name)
    {
        _settings.Mods.RemoveAll(m => m.Name == name);
        _modScan.Remove(name);
        _settings.Save();
        return ItemsPayload();
    }

    // ------------------------------------------------------------------ item pictures

    /// <summary>https://files.local/icon/&lt;id&gt;-icon.webp — downloaded once and cached (shared with the trader editor).</summary>
    private void ServeFile(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web.CoreWebView2.Environment;
        var parts = new Uri(e.Request.Uri).AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (parts is not ["icon", var name])
        {
            e.Response = env.CreateWebResourceResponse(null, 404, "Not found", "Cache-Control: no-store");
            return;
        }
        var deferral = e.GetDeferral();
        _ = Task.Run(async () =>
        {
            byte[]? bytes = null;
            try { bytes = await ItemIcons.GetAsync(name); } catch { /* shown as "no picture" */ }
            BeginInvoke(() =>
            {
                try
                {
                    e.Response = bytes == null
                        ? env.CreateWebResourceResponse(null, 404, "Not found", "Cache-Control: no-store")
                        : env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", "Content-Type: image/webp\r\nCache-Control: max-age=604800\r\nAccess-Control-Allow-Origin: *");
                }
                catch { /* window closing */ }
                finally { deferral.Complete(); }
            });
        });
    }

    // ------------------------------------------------------------------ misc

    private JsonNode SaveUi(JsonObject? ui)
    {
        _settings.Ui = (JsonObject?)ui?.DeepClone();
        _settings.Save();
        return true;
    }

    private JsonNode SetUnsaved(int count)
    {
        _unsaved = count;
        Text = count > 0 ? $"{AppTitle}  •  {count} unsaved" : AppTitle;
        return true;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_unsaved == 0) return;
        if (MessageBox.Show(this, $"{_unsaved} unsaved change(s). Close and lose them?", "Unsaved changes",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            e.Cancel = true;
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* nothing to open it with */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void DarkTitleBar()
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
        }
        catch
        {
            // older Windows: normal title bar
        }
    }
}
