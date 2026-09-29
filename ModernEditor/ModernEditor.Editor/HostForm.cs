using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModernEditor.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ModernEditor.Editor;

/// <summary>
/// The Modern Editor window: the Custom Trader Creator, the Level &amp; Item Editor and the Progression view in one.
/// The interface is a web page (ui\, built into the exe) drawn by the Edge engine (WebView2); this class only does
/// what a page can't — files, pickers, pictures, the game's item list:
///   page → { id, method, args }      host → { id, ok, result | error }
///   host → { event: "diskChanged", … } when Level Gate's file is changed by someone else (the game's F9 window)
/// Trader methods keep their old names; the item pages' methods start with "lg.".
/// Files:  user\mods\ModernEditor\traders\&lt;trader&gt;\trader.json · user\mods\ModernEditor\item_stats.json ·
///         user\mods\ModernEditor\disabled_levels.json · BepInEx\plugins\LevelGate\config\level_requirements.json (Level Gate's
///         own file: only its "items" entries are changed, in the same layout, everything else in it is kept).
/// </summary>
public sealed class HostForm : Form
{
    private const string AppHost = "app.local";
    private const string FilesHost = "files.local";

    // MAJOR.MINOR.PATCH — 2.0.0: the Custom Trader Creator (1.0.0) and the Level & Item Editor (1.1.0) merged.
    public const string Version = "2.0.8";
    public const string AppTitle = "Modern Editor";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly Settings _settings = Settings.Load();
    private readonly ItemDatabase _db = new();
    private string? _modFolder;
    private int _unsaved;

    public HostForm()
    {
        Text = AppTitle;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { /* keep the default */ }
        Width = 1680;
        Height = 1020;
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
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.Folder, "WebView2"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            EditorLog.Error("start", "WebView2 Runtime not found");
            if (MessageBox.Show(this,
                    "Modern Editor needs the Microsoft Edge WebView2 Runtime (normally already part of Windows 10/11).\n\n" +
                    "Open the download page now?", AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                OpenUrl("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        EditorLog.Info("start", $"WebView2 {core.Environment.BrowserVersionString}");
        core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("MODERN_EDITOR_DEVTOOLS") == "1";
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // no F5 reload / Ctrl+P etc. (would lose unsaved edits)
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        // The page is built into the exe; a "ui" folder next to it (for changing the page without rebuilding) wins.
        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        if (File.Exists(Path.Combine(uiFolder, "index.html")))
            core.SetVirtualHostNameToFolderMapping(AppHost, uiFolder, CoreWebView2HostResourceAccessKind.Allow);
        else
            core.AddWebResourceRequestedFilter($"https://{AppHost}/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter($"https://{FilesHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ServeFile;

        core.WebMessageReceived += (_, e) => HandleMessage(e.WebMessageAsJson);
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenUrl(e.Uri); }; // links open in the normal browser
        core.Navigate($"https://{AppHost}/index.html");
    }

    // ------------------------------------------------------------------ messages

    private void HandleMessage(string json)
    {
        JsonNode? id = null;
        string method0 = "?";
        try
        {
            var message = JsonNode.Parse(json)!.AsObject();
            id = message["id"]?.DeepClone();
            var method = (string?)message["method"] ?? "";
            var args = message["args"] as JsonObject ?? new JsonObject();
            method0 = method;
            // settings saves and background checks happen all the time: one short line, no data
            bool quiet = method is "log" or "saveUi" or "lg.saveUi" or "setUnsaved" or "addonsScan" or "setZoom";
            if (!quiet) EditorLog.Info("call", $"{method} {BriefArgs(args)}");
            else if (method != "log") EditorLog.Info("call", method + (method == "setUnsaved" ? " " + args["count"] : ""));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = Call(method, args);
            if (!quiet)
            {
                EditorLog.Info("call", $"{method} done in {watch.ElapsedMilliseconds} ms → {BriefResult(result)}");
                if (result is JsonObject r)
                    foreach (var key in new[] { "problems", "statsError", "migrated", "cleaned" })
                        if (r[key] is JsonNode n && n.ToJsonString() is var text && text is not ("[]" or "null" or "\"\"")) EditorLog.Warn(method, $"{key}: {text}");
            }
            Reply(new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result });
        }
        catch (Exception e)
        {
            EditorLog.Error("call " + method0, e);
            Reply(new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = e.Message });
        }
    }

    /// <summary>The request's arguments for the log: big texts (a whole trader.json) only by size.</summary>
    private static string BriefArgs(JsonObject a)
    {
        if (a.Count == 0) return "";
        return string.Join(", ", a.Select(kv => kv.Key + "=" + (kv.Value is JsonValue v && v.TryGetValue(out string? str) ? EditorLog.Brief(str, 120)
            : kv.Value is JsonObject o ? $"{{{o.Count} keys}} {EditorLog.Brief(o.ToJsonString(), 300)}"
            : kv.Value is JsonArray arr ? $"[{arr.Count}] {EditorLog.Brief(arr.ToJsonString(), 300)}"
            : kv.Value?.ToJsonString() ?? "null")));
    }

    /// <summary>The answer for the log: lists by their size, objects by their keys, short values as they are.</summary>
    private static string BriefResult(JsonNode? r)
    {
        if (r is not JsonObject o) return EditorLog.Brief(r?.ToJsonString(), 200);
        return string.Join(", ", o.Select(kv => kv.Key + "=" + (kv.Value switch
        {
            JsonArray arr => $"[{arr.Count}]",
            JsonObject obj => $"{{{obj.Count}}}",
            null => "null",
            var v => EditorLog.Brief(v.ToJsonString(), 160),
        })));
    }

    private void Reply(JsonObject reply) => _web.CoreWebView2?.PostWebMessageAsJson(reply.ToJsonString());

    private JsonNode? Call(string method, JsonObject a) => method switch
    {
        // ---- traders (Custom Trader Creator)
        "init" => Snapshot(StartModFolder()),
        "browseModFolder" => BrowseModFolder(),
        "reload" => Snapshot(_modFolder),
        "saveTrader" => SaveTrader(Str(a, "folder"), Str(a, "text")),
        "newTrader" => NewTrader(Str(a, "name")),
        "importTrader" => ImportTrader(),
        "deleteTrader" => DeleteTrader(Str(a, "folder")),
        "duplicateTrader" => DuplicateTrader(Str(a, "folder"), Str(a, "name"), Str(a, "text")),
        "listDeleted" => ListDeleted(),
        "restoreDeleted" => RestoreDeleted(Str(a, "name")),
        "purgeDeleted" => PurgeDeleted(Str(a, "name")),
        "chooseAvatar" => ChooseAvatar(Str(a, "folder")),
        "chooseQuestImage" => ChooseQuestImage(Str(a, "folder"), Str(a, "questId")),
        "saveConvertedImage" => SaveConvertedImage(Str(a, "kind"), Str(a, "folder"), Str(a, "questId"), Str(a, "png")),
        "saveQuestAutoImage" => SaveQuestAutoImage(Str(a, "folder"), Str(a, "questId"), Str(a, "png")),
        "chooseBackground" => ChooseBackground(),
        "clearBackground" => ClearBackground(),
        "openFolder" => OpenFolder(Str(a, "folder")),
        "addonsScan" => Addons.Scan(_modFolder, _configFile ?? _settings.LevelGatePath),
        "addonLocate" => AddonLocate(),
        "migrate" => Migrate(),
        "migrateLater" => MigrateLater(),
        // ---- mods (shared by every page)
        "modsScanAll" => ModsScanAll(),
        "modAddFolder" => ModAddFolder(),
        "modSet" => ModSet(Str(a, "name"), (bool?)a["enabled"] ?? true),
        "modRemove" => ModRemove(Str(a, "name")),
        "modRescan" => ItemsPayload(rescan: true),
        "modForget" => ModForget(Str(a, "name")),
        // ---- level limits, item stats, progression (Level & Item Editor)
        "lg.init" => LevelSnapshot(StartConfigFile()),
        "lg.reload" => LevelSnapshot(_configFile),
        "lg.browse" => BrowseConfig(),
        "lg.save" => SaveLevels(a["set"] as JsonObject, a["remove"] as JsonArray),
        "lg.saveDisabled" => SaveDisabled(a["disabled"] as JsonObject),
        "lg.saveStats" => SaveStats(a["edits"] as JsonObject),
        "lg.saveUi" => SaveLgUi(a["ui"] as JsonObject),
        "lg.openConfigFolder" => OpenConfigFolder(),
        "lg.openStatsFolder" => OpenStatsFolder(),
        // ---- window
        "saveUi" => SaveUi(a["ui"] as JsonObject),
        "setUnsaved" => SetUnsaved((int?)a["count"] ?? 0),
        // ---- logs, old files
        "log" => PageLog(Str(a, "level"), Str(a, "text")),
        "openLogs" => OpenLogs(),
        "setSections" => SaveSections(a),
        "setZoom" => SetZoom((double?)a["factor"] ?? 1),
        "cleanupScan" => Cleanup.Scan(_modFolder, _configFile),
        "cleanup" => new JsonObject { ["cleaned"] = Cleanup.Run((a["paths"] as JsonArray ?? new JsonArray()).Select(x => (string?)x ?? ""), _modFolder, _configFile), ["left"] = Cleanup.Scan(_modFolder, _configFile) },
        _ => throw new InvalidOperationException($"Unknown request '{method}'."),
    };

    private static string Str(JsonObject a, string key) => (string?)a[key] ?? "";

    private JsonNode? AddonLocate()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Find Level Gate — pick LevelGate.dll or its config\\level_requirements.json",
            Filter = "Level Gate|LevelGate.dll;level_requirements.json|All files|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        _settings.LevelGatePath = dialog.FileName;
        _settings.Save();
        return Addons.Scan(_modFolder, _settings.LevelGatePath);
    }

    // ------------------------------------------------------------------ the mod folder (user\mods\ModernEditor)

    /// <summary>
    /// The ModernEditor mod folder: the remembered one; an old CustomTraders folder (from the Custom Trader Creator's
    /// settings) becomes its ModernEditor sibling; else found next to Level Gate's config or in a usual SPT folder.
    /// </summary>
    private string? StartModFolder()
    {
        var saved = _settings.ModFolder;
        if (saved != null && Path.GetFileName(saved.TrimEnd('\\', '/')).Equals(Migration.ModName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.GetDirectoryName(saved.TrimEnd('\\', '/'))))
            return saved;
        foreach (var from in new[] { saved, _settings.ConfigFile, AppContext.BaseDirectory }.Concat(SptRootGuesses()))
            if (Migration.ModsRootOf(from) is { } mods) return Path.Combine(mods, Migration.ModName);
        return saved;
    }

    /// <summary>Everything the trader pages need: settings, traders, item list, and what the old mods still hold.</summary>
    private JsonObject Snapshot(string? folder)
    {
        var result = new JsonObject
        {
            ["modFolder"] = null,
            ["ui"] = _settings.Ui?.DeepClone(),
            ["background"] = BackgroundUrl(),
            ["traders"] = new JsonArray(),
            ["items"] = null,
            ["itemsStatus"] = "",
            ["problems"] = new JsonArray(),
            ["deletedCount"] = 0,
            ["version"] = Version,
            ["imported"] = new JsonArray(_settings.Imported.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
        };
        if (string.IsNullOrWhiteSpace(folder) || Migration.ModsRootOf(folder) == null && !Directory.Exists(folder)) return result;

        Directory.CreateDirectory(folder);
        _modFolder = folder;
        _settings.ModFolder = folder;
        _settings.Save();
        result["modFolder"] = folder;
        result["serverMod"] = File.Exists(Path.Combine(folder, "ModernEditor.dll"));
        result["sections"] = ReadSections();
        if (!_settings.MigrationAsked && Migration.ModsRootOf(folder) is { } mods && Migration.Pending(mods) is { } pending)
            result["migration"] = pending;

        var tradersFolder = Path.Combine(folder, "traders");
        Directory.CreateDirectory(tradersFolder);
        var traders = (JsonArray)result["traders"]!;
        foreach (var dir in Directory.GetDirectories(tradersFolder).OrderBy(d => d))
        {
            var file = Path.Combine(dir, "trader.json");
            if (!File.Exists(file)) continue;
            try
            {
                traders.Add(TraderPayload(dir, JsonNode.Parse(File.ReadAllText(file), documentOptions: Lenient)!));
            }
            catch (Exception e)
            {
                ((JsonArray)result["problems"]!).Add($"{Path.GetFileName(dir)}: could not read trader.json — {e.Message}");
            }
        }

        LoadItems(result);
        result["deletedCount"] = DeletedDirs().Length;
        return result;
    }

    private JsonNode? Migrate()
    {
        var mods = Migration.ModsRootOf(_modFolder) ?? throw new InvalidOperationException("The SPT user\\mods folder wasn't found — pick it with Browse… first.");
        var log = Migration.Run(mods);
        _settings.MigrationAsked = true;
        _settings.Save();
        var snap = Snapshot(Path.Combine(mods, Migration.ModName));
        snap["migrated"] = log;
        return snap;
    }

    // ------------------------------------------------------------------ section switches (user\mods\ModernEditor\config.json)

    private static readonly string[] SectionKeys = { "traders", "itemStats", "levelGate" };

    /// <summary>The left panel's switches — also read by the server mod (off = that part isn't applied in game).</summary>
    private JsonObject ReadSections()
    {
        var result = new JsonObject();
        JsonObject? file = null;
        try
        {
            var path = _modFolder != null ? Path.Combine(_modFolder, "config.json") : null;
            if (path != null && File.Exists(path)) file = JsonNode.Parse(File.ReadAllText(path), documentOptions: Lenient) as JsonObject;
        }
        catch (Exception e) { EditorLog.Warn("config", "config.json couldn't be read: " + e.Message); }
        foreach (var key in SectionKeys) result[key] = (bool?)file?[key] ?? true;
        return result;
    }

    private JsonNode SaveSections(JsonObject a)
    {
        if (_modFolder == null) throw new InvalidOperationException("Pick your SPT folder first (Browse… at the top).");
        var path = Path.Combine(_modFolder, "config.json");
        JsonObject file;
        try { file = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path), documentOptions: Lenient) as JsonObject ?? new JsonObject() : new JsonObject(); }
        catch { file = new JsonObject(); }
        foreach (var key in SectionKeys)
            if (a[key] is JsonValue v && v.TryGetValue(out bool on)) file[key] = on;
        Directory.CreateDirectory(_modFolder);
        File.WriteAllText(path, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        EditorLog.Info("config", "config.json: " + file.ToJsonString());
        return ReadSections();
    }

    /// <summary>Interface size (Appearance → Size): the whole page scales like a browser zoom, so clicks and drags stay right.</summary>
    private JsonNode SetZoom(double factor)
    {
        factor = Math.Clamp(factor, 0.5, 3);
        if (Math.Abs(_web.ZoomFactor - factor) > 0.001) { _web.ZoomFactor = factor; EditorLog.Info("zoom", $"interface size {factor:P0}"); }
        return factor;
    }

    private static JsonNode PageLog(string level, string text) { EditorLog.Page(level, text); return true; }

    private static JsonNode OpenLogs()
    {
        if (Directory.Exists(EditorLog.Folder)) OpenUrl(EditorLog.Folder);
        return EditorLog.Folder;
    }

    private JsonNode MigrateLater() { _settings.MigrationAsked = true; _settings.Save(); return true; }

    private JsonObject TraderPayload(string dir, JsonNode file)
    {
        var name = Path.GetFileName(dir);
        var images = new JsonObject();
        foreach (var path in Directory.EnumerateFiles(dir).Where(IsImage))
            images[Path.GetFileName(path)] = ImageUrl(name, Path.GetFileName(path));

        string avatar = (string?)file["avatar"] ?? "avatar.png";
        var avatarPath = Path.Combine(dir, avatar);
        return new JsonObject
        {
            ["folder"] = name,
            ["file"] = file,
            ["images"] = images,
            ["avatarColor"] = File.Exists(avatarPath) ? AverageColor(avatarPath) : null,
            ["modified"] = File.Exists(Path.Combine(dir, "trader.json")) ? new DateTimeOffset(File.GetLastWriteTimeUtc(Path.Combine(dir, "trader.json"))).ToUnixTimeMilliseconds() : 0,
        };
    }

    private static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".avif" or ".svg" or ".ico";

    /// <summary>URL of a file in a trader folder; the ?v= changes with the file so the page never shows an old copy.</summary>
    private string ImageUrl(string folder, string file)
    {
        var path = Path.Combine(_modFolder!, "traders", folder, file);
        long v = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        return $"https://{FilesHost}/traders/{Uri.EscapeDataString(folder)}/{Uri.EscapeDataString(file)}?v={v}";
    }

    private JsonNode? BrowseModFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your SPT folder, or …\\SPT_Runtime\\user\\mods (Modern Editor keeps its files in user\\mods\\ModernEditor)",
            UseDescriptionForTitle = true,
        };
        if (_modFolder != null) dialog.InitialDirectory = Directory.GetParent(_modFolder)?.FullName ?? _modFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        var picked = dialog.SelectedPath;
        var mods = Migration.ModsRootOf(picked);
        return Snapshot(mods != null ? Path.Combine(mods, Migration.ModName) : picked);
    }

    private string TraderDir(string folder)
    {
        if (_modFolder == null) throw new InvalidOperationException("No mod folder selected.");
        if (string.IsNullOrWhiteSpace(folder) || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder is "." or "..")
            throw new InvalidOperationException("Invalid trader folder.");
        return Path.Combine(_modFolder, "traders", folder);
    }

    private JsonNode SaveTrader(string folder, string text)
    {
        JsonNode.Parse(text); // never write something that isn't JSON
        var dir = TraderDir(folder);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "trader.json");
        var temp = target + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, target, overwrite: true);
        return true;
    }

    private JsonNode NewTrader(string name)
    {
        if (_modFolder == null) throw new InvalidOperationException("Pick the SPT folder first (Browse...).");
        name = name.Trim();
        string safe = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ')).Trim();
        if (safe.Length == 0) safe = "Trader";
        string dir = Path.Combine(_modFolder, "traders", safe);
        for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(_modFolder, "traders", $"{safe} {n}");
        Directory.CreateDirectory(dir);

        var file = new TraderFile { Id = Ids.New(), Name = name, Nickname = name, Avatar = "avatar.png" };
        SavePlaceholderAvatar(Path.Combine(dir, "avatar.png"), name);
        file.Save(Path.Combine(dir, "trader.json"));
        return TraderPayload(dir, JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "trader.json")))!);
    }

    /// <summary>2.0.8: another trader (a game trader or another mod's) as one of yours: pick its base.json (assort.json next
    /// to it); base info, offers and picture come over (TraderImport), quests don't.</summary>
    private JsonNode? ImportTrader()
    {
        if (_modFolder == null) throw new InvalidOperationException("Pick the SPT folder first (Browse...).");
        using var dialog = new OpenFileDialog
        {
            Title = "Import a trader — pick its base.json (assort.json must be next to it)",
            Filter = "Trader base.json|base.json|JSON files|*.json|All files|*.*",
        };
        if (DatabaseFolder() is { } dbf && Directory.Exists(Path.Combine(dbf, "traders"))) dialog.InitialDirectory = Path.Combine(dbf, "traders");
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        var r = TraderImport.Load(dialog.FileName,
            tpl => _db.Items.TryGetValue(tpl, out var it) && it.Category == ItemCategory.Weapon,
            tpl => _db.Items.ContainsKey(tpl) || Currencies.IsCurrency(tpl) || tpl is Currencies.GpCoin or Currencies.LegaMedal);
        string name = r.File.Nickname.Length > 0 ? r.File.Nickname : "Imported Trader";
        string safe = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ')).Trim();
        if (safe.Length == 0) safe = "Trader";
        string dir = Path.Combine(_modFolder, "traders", safe);
        for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(_modFolder, "traders", $"{safe} {n}");
        Directory.CreateDirectory(dir);
        bool pic = false;
        if (r.AvatarPath != null && TryLoadBitmap(File.ReadAllBytes(r.AvatarPath)) is { } bmp)
            using (bmp) { SaveSquareImage(bmp, Path.Combine(dir, "avatar.png"), 256); pic = true; }
        if (!pic)
        {
            SavePlaceholderAvatar(Path.Combine(dir, "avatar.png"), name);
            if (r.AvatarPath != null) r.Notes.Add($"Its picture ({Path.GetFileName(r.AvatarPath)}) couldn't be read: use Choose Icon From PC… on the Trader page.");
        }
        r.File.Save(Path.Combine(dir, "trader.json"));
        EditorLog.Info("import", $"{dialog.FileName} → {dir}: " + string.Join(" | ", r.Notes));
        var payload = TraderPayload(dir, JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "trader.json")))!);
        return new JsonObject { ["trader"] = payload, ["notes"] = new JsonArray(r.Notes.Select(x => (JsonNode?)x).ToArray()) };
    }

    /// <summary>Copies a trader folder (icons, quest images) and writes the copy's trader.json (with new ids, made by the page).</summary>
    private JsonNode DuplicateTrader(string folder, string name, string text)
    {
        JsonNode.Parse(text);
        var source = TraderDir(folder);
        string safe = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ')).Trim();
        if (safe.Length == 0) safe = "Trader";
        string dir = Path.Combine(_modFolder!, "traders", safe);
        for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(_modFolder!, "traders", $"{safe} {n}");
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (Path.GetFileName(file).Equals("trader.json", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        }
        File.WriteAllText(Path.Combine(dir, "trader.json"), text);
        return TraderPayload(dir, JsonNode.Parse(text)!);
    }

    // ------------------------------------------------------------------ Level Gate's level_requirements.json (read / written in its own layout)

    private static readonly string ConfigTail = Path.Combine("BepInEx", "plugins", "LevelGate", "config", "level_requirements.json");
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private string? _configFile;
    private FileSystemWatcher? _watcher;
    private DateTime _ourWrite;
    private System.Windows.Forms.Timer? _diskTimer;

    private static IEnumerable<string> SptRootGuesses()
    {
        foreach (var drive in new[] { "C", "D", "E", "F", "G" })
            foreach (var root in new[] { "SPT", "SPTarkov", "SPT-AKI", "Games\\SPT", "Games\\SPTarkov" })
                yield return Path.Combine($"{drive}:\\", root);
    }

    /// <summary>The file the game reads: …\BepInEx\plugins\LevelGate\config\level_requirements.json of the SPT install the
    /// traders live in (or above this exe, or C:\SPT…). A copy elsewhere (an example in a zip) is never picked.</summary>
    private string? FindConfigFile()
    {
        var tries = new List<string>();
        foreach (var start in new[] { _modFolder, _settings.ModFolder, AppContext.BaseDirectory })
            if (Migration.SptRootOf(start) is { } root) tries.Add(Path.Combine(root, ConfigTail));
        foreach (var root in SptRootGuesses()) tries.Add(Path.Combine(root, ConfigTail));
        return tries.FirstOrDefault(File.Exists);
    }

    private string? StartConfigFile()
    {
        var saved = _settings.ConfigFile;
        if (saved != null && File.Exists(saved) && Migration.SptRootOf(saved) != null) return saved;
        return FindConfigFile() ?? (saved != null && File.Exists(saved) ? saved : null);
    }

    private string? SptRoot() => Migration.SptRootOf(_configFile) ?? Migration.SptRootOf(_modFolder);

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

    /// <summary>Everything the item pages need: level limits, disabled limits, item stats (the items come with the trader snapshot).</summary>
    private JsonObject LevelSnapshot(string? configFile)
    {
        var result = new JsonObject
        {
            ["configFile"] = null,
            ["levels"] = new JsonObject(),
            ["ui"] = _settings.LgUi?.DeepClone(),
            ["version"] = Version,
            ["levelGateVersion"] = Addons.VersionOfConfig(configFile),
            ["disabled"] = ReadDisabled(),
            ["disabledFile"] = DisabledFile(),
        };
        StatsPayload(result);
        if (string.IsNullOrWhiteSpace(configFile) || !Directory.Exists(Path.GetDirectoryName(configFile))) return result;
        _configFile = configFile;
        _settings.ConfigFile = configFile;
        _settings.Save();
        Watch();
        result["configFile"] = configFile;
        result["sptRoot"] = SptRoot();
        result["notInGame"] = Migration.SptRootOf(configFile) == null; // a copy the game never reads
        result["levels"] = Levels();
        result["modified"] = Modified();
        return result;
    }

    private JsonNode? BrowseConfig()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Pick Level Gate's level_requirements.json (…\\BepInEx\\plugins\\LevelGate\\config)",
            Filter = "Level Gate config|level_requirements.json|JSON files|*.json",
            FileName = "level_requirements.json",
            CheckFileExists = false,
        };
        if (_configFile != null) dialog.InitialDirectory = Path.GetDirectoryName(_configFile);
        return dialog.ShowDialog(this) == DialogResult.OK ? LevelSnapshot(dialog.FileName) : null;
    }

    /// <summary>
    /// Applies the page's changes to what's on disk right now (so anything the game's F9 window added meanwhile stays):
    /// existing entries keep their place, new ones go at the end; every other part of Level Gate's file is kept as it is.
    /// </summary>
    private JsonNode SaveLevels(JsonObject? set, JsonArray? remove)
    {
        if (_configFile == null) throw new InvalidOperationException("Pick Level Gate's level_requirements.json first (Browse… on the Level Limits page).");
        var root = ReadConfig();
        var items = (JsonObject)root["items"]!;
        foreach (var id in remove ?? new JsonArray())
            if ((string?)id is { } key) items.Remove(key);
        foreach (var (id, v) in set ?? new JsonObject())
            items[id] = Math.Clamp((int?)v ?? 1, 1, 99);
        Directory.CreateDirectory(Path.GetDirectoryName(_configFile)!);
        var temp = _configFile + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _configFile, overwrite: true);
        _ourWrite = File.GetLastWriteTimeUtc(_configFile);
        return new JsonObject { ["levels"] = Levels(), ["modified"] = Modified() };
    }

    /// <summary>Limits switched off in Level Limits: kept here (not in Level Gate's file), put back when switched on.</summary>
    private string? DisabledFile() => _modFolder != null ? Path.Combine(_modFolder, "disabled_levels.json") : null;

    private JsonObject ReadDisabled()
    {
        var result = new JsonObject();
        try
        {
            if (DisabledFile() is { } f && File.Exists(f) && JsonNode.Parse(File.ReadAllText(f), documentOptions: Lenient) is JsonObject o)
                foreach (var (id, v) in o)
                    if (v is JsonValue value && value.TryGetValue(out int level)) result[id] = level;
        }
        catch { /* unreadable: nothing disabled */ }
        return result;
    }

    private JsonNode SaveDisabled(JsonObject? disabled)
    {
        var file = DisabledFile() ?? throw new InvalidOperationException("The ModernEditor mod folder isn't known yet — pick your SPT folder with Browse… first.");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var clean = new JsonObject();
        foreach (var (id, v) in disabled ?? new JsonObject()) if (v is JsonValue value && value.TryGetValue(out int l)) clean[id] = Math.Clamp(l, 1, 99);
        var temp = file + ".tmp";
        File.WriteAllText(temp, clean.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file, overwrite: true);
        return ReadDisabled();
    }

    /// <summary>Tells the page when Level Gate's file changes outside the editor (the game's F9 window, a text editor).</summary>
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

    private JsonNode OpenStatsFolder()
    {
        if (StatsFile() is { } f && Path.GetDirectoryName(f) is { } dir && Directory.Exists(dir)) OpenUrl(dir);
        return true;
    }

    private JsonNode OpenConfigFolder()
    {
        if (_configFile != null && Path.GetDirectoryName(_configFile) is { } dir && Directory.Exists(dir)) OpenUrl(dir);
        return true;
    }

    // ------------------------------------------------------------------ item stat edits (user\mods\ModernEditor\item_stats.json)

    private readonly ItemStats _stats = new();
    private string? _statsLoaded;

    private string? StatsFile() => _modFolder != null ? Path.Combine(_modFolder, "item_stats.json") : null;

    /// <summary>Where the old ItemStatEditor mod kept the edits (read until they're moved over).</summary>
    private string? OldStatsFile() => ModsRoot() is { } mods ? new[] { Path.Combine(mods, "ItemStatEditor", "item_stats.json"), Path.Combine(mods, "LevelGate", "item_stats.json") }.FirstOrDefault(File.Exists) : null;

    private void StatsPayload(JsonObject result)
    {
        var file = StatsFile();
        result["statsFile"] = file;
        result["serverMod"] = file != null && File.Exists(Path.Combine(Path.GetDirectoryName(file)!, "ModernEditor.dll"));
        if (file == null || !File.Exists(file)) file = OldStatsFile() ?? file; // edits not moved over yet
        result["statsReadFrom"] = file;
        var edits = new JsonObject();
        try
        {
            if (file != null && File.Exists(file) && JsonNode.Parse(File.ReadAllText(file), documentOptions: Lenient) is JsonObject all)
                foreach (var (id, e) in all)
                    if (e is JsonObject) edits[id] = e.DeepClone();
        }
        catch (Exception e) { result["statsError"] = "item_stats.json couldn't be read: " + e.Message; }
        result["statEdits"] = edits;
    }

    private JsonNode SaveStats(JsonObject? edits)
    {
        var file = StatsFile() ?? throw new InvalidOperationException("The SPT user\\mods folder wasn't found — pick your SPT folder with Browse… first.");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var clean = new JsonObject();
        foreach (var (id, e) in edits ?? new JsonObject())
            if (e is JsonObject o && o.Count > 0) clean[id] = o.DeepClone();
        var temp = file + ".tmp";
        File.WriteAllText(temp, clean.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file, overwrite: true);
        var result = new JsonObject();
        StatsPayload(result);
        return result;
    }

    // ------------------------------------------------------------------ the game's item list (+ modded items)

    /// <summary>The database: next to the mod folder, else next to Level Gate's config, else a usual SPT folder.</summary>
    private string? DatabaseFolder()
    {
        var starts = new List<string?> { _modFolder, SptRoot() };
        starts.AddRange(SptRootGuesses().Where(Directory.Exists));
        return starts.Where(s => !string.IsNullOrEmpty(s)).Select(s => ItemDatabase.FindDatabaseFolder(s!)).FirstOrDefault(d => d != null);
    }

    /// <summary>
    /// One item list for every page. Each item: i id, n name, s short, c kind, g trader category (Shared.ItemGroups),
    /// lg item-page category when it differs (Ammo Packs), k caliber, wc weapon class, h handbook, p what traders pay,
    /// f flea, ph / pf whole preset, x hidden, m mod, st stats, mk med kind, ob / ot sold by the game's traders (2 barter,
    /// 1 money only / their names).
    /// </summary>
    private void LoadItems(JsonObject result)
    {
        try
        {
            var dbFolder = DatabaseFolder();
            if (dbFolder == null)
            {
                result["itemsStatus"] = "Item database not found (SPT_Data\\database) — pick your SPT folder with Browse…; item names unavailable.";
                return;
            }
            if (_db.SourceFolder != dbFolder || !_db.IsLoaded) { _db.Load(dbFolder); _statsLoaded = null; }
            if (_statsLoaded != dbFolder)
            {
                try { _stats.Load(dbFolder, _db, ModsRoot()); } catch (Exception e) { result["statsError"] = e.Message; }
                _statsLoaded = dbFolder;
            }
            var items = new JsonArray();
            foreach (var item in _db.Items.Values)
            {
                items.Add(new JsonObject
                {
                    ["i"] = item.Id,
                    ["n"] = item.Name,
                    ["s"] = item.ShortName,
                    ["c"] = item.Category.ToString(),
                    ["g"] = TraderGroup(item.Group),
                    ["lg"] = item.Group != TraderGroup(item.Group) ? item.Group : null,
                    ["k"] = item.Caliber.Length > 0 ? item.Caliber : null,
                    ["wc"] = item.WeaponClass.Length > 0 ? item.WeaponClass : null,
                    ["h"] = _db.Handbook.TryGetValue(item.Id, out var h) ? Math.Round(h) : null,
                    ["p"] = _db.TraderSellPrice(item.Id) is var p && p > 0 ? p : null,
                    ["f"] = _db.Flea.TryGetValue(item.Id, out var f) ? Math.Round(f) : null,
                    ["ph"] = _db.Presets.TryGetValue(item.Id, out var preset) ? Math.Round(preset.Handbook) : null,
                    ["pf"] = _db.Presets.TryGetValue(item.Id, out var preset2) ? Math.Round(preset2.Flea) : null,
                    ["x"] = item.Hidden ? 1 : null,
                    ["st"] = _stats.Show.TryGetValue(item.Id, out var st) ? st.DeepClone() : null,
                    ["mk"] = _stats.Meds.TryGetValue(item.Id, out var med) ? (string?)med["kind"] : null,
                    // 2.0.8: sold by the game's own traders: ob = 2 barter (maybe money too), 1 money only; ot = who
                    ["ob"] = _db.GameOffers.TryGetValue(item.Id, out var go) ? (go.Barter ? 2 : 1) : null,
                    ["ot"] = _db.GameOffers.TryGetValue(item.Id, out var go2) ? string.Join(", ", go2.Traders) : null,
                    // each game offer: trader, loyalty level, ways to pay [[tpl, count]…]
                    ["os"] = _db.GameOfferList.TryGetValue(item.Id, out var gl) ? new JsonArray(gl.Select(o => (JsonNode?)new JsonObject
                    {
                        ["t"] = o.Trader, ["ll"] = o.Loyalty,
                        ["p"] = new JsonArray(o.Prices.Select(pr => (JsonNode?)new JsonArray(pr.Select(c => (JsonNode?)new JsonArray(c.Tpl, c.Count)).ToArray())).ToArray()),
                    }).ToArray()) : null,
                });
            }
            int modded = AddModItems(items, result);
            result["items"] = items;
            result["itemsStatus"] = $"{_db.Items.Count:N0} items" + (modded > 0 ? $" + {modded:N0} modded" : "");
            var meds = new JsonObject();
            foreach (var (id, m) in _stats.Meds) meds[id] = m.DeepClone();
            result["meds"] = meds;
            var defaults = new JsonObject();
            foreach (var (id, m) in _stats.Defaults) defaults[id] = m.DeepClone();
            result["medsDefault"] = defaults;
            result["statSources"] = new JsonArray(_stats.Sources.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            var quests = new JsonArray();
            var gameQuests = _db.LoadQuests();
            foreach (var (id, name, trader, traderId) in gameQuests)
                quests.Add(new JsonObject { ["i"] = id, ["n"] = name, ["t"] = trader, ["ti"] = traderId });
            result["gameQuests"] = quests;
            result["traderRate"] = Math.Round(_db.BestTraderRate * 100);
            result["gameQuestImages"] = GameQuestImages(dbFolder, result);
            result["gameTraders"] = GameTraders(dbFolder, gameQuests.Select(q => (q.TraderId, q.Trader)));
        }
        catch (Exception e)
        {
            result["itemsStatus"] = "Item database failed to load: " + e.Message;
        }
    }

    /// <summary>The trader pages' categories (what the server knows): ammo packs are Ammo there.</summary>
    private static string TraderGroup(string group) => group switch
    {
        "AmmoPacks" => "Ammo",
        "Headsets" or "FaceCovers" or "Eyewear" or "Armbands" => "Gear", // 2.0.8: split only on the item pages
        _ => group,
    };

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
            try
            {
                var found = ModScanner.Scan(mod.Folder, _db);
                _modScan[mod.Name] = found;
                foreach (var it in found) _settings.ModIdMemory[it.Id] = new[] { mod.Name, it.Name };
            }
            catch (Exception e)
            {
                _modScan[mod.Name] = new List<ModItem>();
                _modErrors[mod.Name] = e.Message;
            }
        }
        _settings.Save();
    }

    /// <summary>Adds the items of switched-on imports to the item list (stats too); returns how many.</summary>
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
                double sell = Math.Round(it.Handbook * _db.BestTraderRate);
                var node = new JsonObject
                {
                    ["i"] = it.Id, ["n"] = it.Name, ["s"] = it.ShortName, ["c"] = it.Category.ToString(),
                    ["g"] = TraderGroup(it.Group), ["lg"] = it.Group != TraderGroup(it.Group) ? it.Group : null,
                    ["k"] = it.Caliber.Length > 0 ? it.Caliber : null,
                    ["wc"] = it.WeaponClass.Length > 0 ? it.WeaponClass : null,
                    ["h"] = it.Handbook > 0 ? Math.Round(it.Handbook) : null, ["p"] = sell > 0 ? sell : null,
                    ["m"] = mod.Name,
                };
                // stats of the modded item (meds / food / ammo / armor) — never allowed to break the item list
                try
                {
                    _stats.AddModItems(new[] { it });
                    if (_stats.Show.TryGetValue(it.Id, out var mst)) node["st"] = mst.DeepClone();
                    if (_stats.Meds.TryGetValue(it.Id, out var mmed)) node["mk"] = (string?)mmed["kind"];
                }
                catch { /* shown without stats */ }
                if (mod.Enabled) { items.Add(node); count++; }
                else off.Add(node);
            }
        }
        result["mods"] = mods;
        result["modItemsOff"] = off;
        var memory = new JsonObject();
        foreach (var (id, v) in _settings.ModIdMemory) memory[id] = new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        result["modMemory"] = memory;
        return count;
    }

    /// <summary>Items + mods only (the traders and levels on the page stay as they are).</summary>
    private JsonObject ItemsPayload(bool rescan = false)
    {
        var result = new JsonObject();
        if (rescan) { _modScan.Clear(); _statsLoaded = null; }
        LoadItems(result);
        return result;
    }

    /// <summary>...\user\mods (the folder that holds ModernEditor and the other server mods).</summary>
    private string? ModsRoot() => Migration.ModsRootOf(_modFolder) ?? (SptRoot() is { } root ? Migration.ModsRootOf(root) : null);

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
        var root = ModsRoot() ?? throw new InvalidOperationException("The SPT user\\mods folder wasn't found — pick your SPT folder with Browse… first.");
        if (!_db.IsLoaded) throw new InvalidOperationException("The game's item list isn't loaded yet.");
        var added = new JsonArray();
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals(Migration.ModName, StringComparison.OrdinalIgnoreCase) || name is "CustomTraders" or "ItemStatEditor") continue; // our own
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
        if (ModsRoot() is { } root && Directory.Exists(root)) dialog.InitialDirectory = root;
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

    /// <summary>Removes an import. The ids stay remembered, so the checks can still name the mod.</summary>
    private JsonNode ModRemove(string name)
    {
        _settings.Mods.RemoveAll(m => m.Name == name);
        _modScan.Remove(name);
        _settings.Save();
        return ItemsPayload();
    }

    private JsonNode ModForget(string name)
    {
        foreach (var id in _settings.ModIdMemory.Where(kv => kv.Value.Length > 0 && kv.Value[0] == name).Select(kv => kv.Key).ToList())
            _settings.ModIdMemory.Remove(id);
        _settings.Save();
        return ItemsPayload();
    }

    // ------------------------------------------------------------------ the game's quest / trader pictures

    private string? _gameQuestImages, _gameTraderImages;

    private JsonArray GameQuestImages(string databaseFolder, JsonObject result)
    {
        var list = new JsonArray();
        _gameQuestImages = FindQuestImagesFolder(databaseFolder, out var looked);
        result["gameQuestImagesFolder"] = _gameQuestImages;
        result["gameQuestImagesLooked"] = looked;
        if (_gameQuestImages == null) return list;
        foreach (var path in Directory.EnumerateFiles(_gameQuestImages).Where(IsImage).OrderBy(p => p))
        {
            var file = Path.GetFileName(path);
            list.Add(new JsonObject { ["n"] = Path.GetFileNameWithoutExtension(file), ["u"] = $"https://{FilesHost}/game/quests/{Uri.EscapeDataString(file)}" });
        }
        return list;
    }

    private JsonArray GameTraders(string databaseFolder, IEnumerable<(string Id, string Name)> traders)
    {
        var list = new JsonArray();
        _gameTraderImages = null;
        if (_gameQuestImages != null && Directory.GetParent(_gameQuestImages) is { } images)
        {
            var folder = Path.Combine(images.FullName, "traders");
            if (Directory.Exists(folder)) _gameTraderImages = folder;
        }
        foreach (var (id, name) in traders.Where(x => x.Id.Length > 0).DistinctBy(x => x.Id))
        {
            var names = new List<string>();
            try
            {
                var basePath = Path.Combine(databaseFolder, "traders", id, "base.json");
                if (File.Exists(basePath) && JsonNode.Parse(File.ReadAllText(basePath))?["avatar"]?.GetValue<string>() is { Length: > 0 } avatar)
                    names.Add(Path.GetFileName(avatar));
            }
            catch { /* unreadable base.json: fall back to the id */ }
            names.AddRange(new[] { ".png", ".jpg", ".jpeg", ".webp" }.Select(ext => id + ext));
            string? file = _gameTraderImages == null ? null :
                names.FirstOrDefault(f => f.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(Path.Combine(_gameTraderImages, f)));
            list.Add(new JsonObject
            {
                ["i"] = id,
                ["n"] = name.Length > 0 ? name : id,
                ["u"] = file == null ? null : $"https://{FilesHost}/game/traders/{Uri.EscapeDataString(file)}",
            });
        }
        return list;
    }

    private static string? FindQuestImagesFolder(string databaseFolder, out string looked)
    {
        var roots = new List<string>();
        var dir = Directory.GetParent(databaseFolder);
        for (int i = 0; i < 3 && dir != null; i++, dir = dir.Parent) roots.Add(dir.FullName);
        looked = string.Join("; ", roots);
        string? best = null;
        int bestCount = 0;
        foreach (var root in roots)
        {
            foreach (var candidate in Search(root, 0))
            {
                int count = Directory.EnumerateFiles(candidate).Count(IsImage);
                if (count > bestCount) { best = candidate; bestCount = count; }
            }
            if (best != null) break;
        }
        return best;

        static IEnumerable<string> Search(string folder, int depth)
        {
            IEnumerable<string> subs;
            try { subs = Directory.GetDirectories(folder); } catch { yield break; }
            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub).ToLowerInvariant();
                if (name is "user" or "node_modules" or "bepinex" or "logs" or "cache" or "database") continue;
                if (name is "quests" or "quest" && Path.GetFileName(folder).Equals("images", StringComparison.OrdinalIgnoreCase)) yield return sub;
                else if (depth < 3) foreach (var x in Search(sub, depth + 1)) yield return x;
            }
        }
    }

    // ------------------------------------------------------------------ deleted traders (trash)

    private string[] DeletedDirs()
    {
        if (_modFolder == null) return Array.Empty<string>();
        var trash = Path.Combine(_modFolder, "deleted_traders");
        return Directory.Exists(trash) ? Directory.GetDirectories(trash).OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray() : Array.Empty<string>();
    }

    private string TrashDir(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Invalid folder.");
        return Path.Combine(_modFolder!, "deleted_traders", name);
    }

    private JsonNode ListDeleted()
    {
        var list = new JsonArray();
        foreach (var dir in DeletedDirs())
        {
            string name = Path.GetFileName(dir), trader = name;
            try
            {
                var file = Path.Combine(dir, "trader.json");
                if (File.Exists(file)) trader = (string?)JsonNode.Parse(File.ReadAllText(file))?["name"] ?? name;
            }
            catch { /* show the folder name */ }
            long size = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            list.Add(new JsonObject { ["folder"] = name, ["name"] = trader, ["when"] = Directory.GetLastWriteTime(dir).ToString("yyyy-MM-dd HH:mm"), ["kb"] = size / 1024 });
        }
        return list;
    }

    private JsonNode RestoreDeleted(string name)
    {
        var source = TrashDir(name);
        string baseName = System.Text.RegularExpressions.Regex.Replace(name, @"_\d{8}_\d{6}$", "");
        string target = Path.Combine(_modFolder!, "traders", baseName);
        for (int n = 2; Directory.Exists(target); n++) target = Path.Combine(_modFolder!, "traders", $"{baseName} {n}");
        Directory.Move(source, target);
        return Snapshot(_modFolder);
    }

    private JsonNode PurgeDeleted(string name)
    {
        var dirs = name == "*" ? DeletedDirs() : new[] { TrashDir(name) };
        foreach (var dir in dirs) if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        return ListDeleted();
    }

    private JsonNode DeleteTrader(string folder)
    {
        var dir = TraderDir(folder);
        string trash = Path.Combine(_modFolder!, "deleted_traders");
        Directory.CreateDirectory(trash);
        string target = Path.Combine(trash, $"{folder}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.Move(dir, target);
        return target;
    }

    private JsonNode OpenFolder(string folder)
    {
        var dir = string.IsNullOrEmpty(folder) ? _modFolder : TraderDir(folder);
        if (dir != null && Directory.Exists(dir)) OpenUrl(dir);
        return true;
    }

    // ------------------------------------------------------------------ pictures

    private string? PickImage(string title)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.avif;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.svg|All files|*.*" };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private JsonNode? ChooseAvatar(string folder)
    {
        var dir = TraderDir(folder);
        var source = PickImage("Choose trader icon");
        if (source == null) return null;
        var bytes = File.ReadAllBytes(source);
        return TryLoadBitmap(bytes) is { } input ? SaveAvatar(folder, input) : AskPageToConvert(source, bytes, "avatar", folder, "");
    }

    private JsonObject SaveAvatar(string folder, Bitmap input)
    {
        using (input)
        {
            var target = Path.Combine(TraderDir(folder), "avatar.png");
            SaveSquareImage(input, target, 256);
            return new JsonObject { ["file"] = "avatar.png", ["url"] = ImageUrl(folder, "avatar.png"), ["avatarColor"] = AverageColor(target) };
        }
    }

    /// <summary>
    /// Pictures Windows can't open itself (webp, avif, svg…): the page decodes them (the Edge engine reads them all) and sends
    /// them back as PNG through saveConvertedImage — the game gets a normal PNG either way.
    /// </summary>
    private static JsonObject AskPageToConvert(string source, byte[] bytes, string kind, string folder, string questId)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant().TrimStart('.');
        var mime = ext switch { "jpg" or "jpeg" => "image/jpeg", "svg" => "image/svg+xml", "ico" => "image/x-icon", "tif" or "tiff" => "image/tiff", "" => "image/png", _ => "image/" + ext };
        EditorLog.Info("image", $"{Path.GetFileName(source)} is converted to PNG by the page ({bytes.Length:N0} bytes, {mime})");
        return new JsonObject { ["convert"] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}", ["kind"] = kind, ["folder"] = folder, ["questId"] = questId };
    }

    private JsonNode SaveConvertedImage(string kind, string folder, string questId, string png)
    {
        var comma = png.IndexOf(',');
        var bytes = Convert.FromBase64String(comma >= 0 ? png[(comma + 1)..] : png);
        var input = TryLoadBitmap(bytes) ?? throw new InvalidOperationException("That picture couldn't be read.");
        return kind == "avatar" ? SaveAvatar(folder, input) : SaveQuestImage(folder, questId, input);
    }

    private JsonNode? ChooseQuestImage(string folder, string questId)
    {
        var dir = TraderDir(folder);
        if (!Ids.IsValid(questId)) throw new InvalidOperationException("Invalid quest id.");
        var source = PickImage("Choose quest image");
        if (source == null) return null;
        var bytes = File.ReadAllBytes(source);
        return TryLoadBitmap(bytes) is { } input ? SaveQuestImage(folder, questId, input) : AskPageToConvert(source, bytes, "quest", folder, questId);
    }

    private JsonObject SaveQuestImage(string folder, string questId, Bitmap input)
    {
        if (!Ids.IsValid(questId)) throw new InvalidOperationException("Invalid quest id.");
        string name = $"quest_{questId}.png";
        using (input) input.Save(Path.Combine(TraderDir(folder), name), ImageFormat.Png);
        return new JsonObject { ["file"] = name, ["url"] = ImageUrl(folder, name) };
    }

    /// <summary>A quest picture the page drew from an item's picture (PNG, base64) — saved as quest_&lt;id&gt;_item.png in the trader's folder.</summary>
    private JsonNode SaveQuestAutoImage(string folder, string questId, string png)
    {
        var dir = TraderDir(folder);
        if (!Ids.IsValid(questId)) throw new InvalidOperationException("Invalid quest id.");
        var comma = png.IndexOf(',');
        var bytes = Convert.FromBase64String(comma >= 0 ? png[(comma + 1)..] : png);
        if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != (byte)'P') throw new InvalidOperationException("Not a PNG picture.");
        string name = $"quest_{questId}_item.png";
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        return new JsonObject { ["file"] = name, ["url"] = ImageUrl(folder, name) + "?v=" + DateTime.UtcNow.Ticks };
    }

    private JsonNode? ChooseBackground()
    {
        var source = PickImage("Choose a background picture");
        if (source == null) return null;
        _settings.BackgroundImage = source;
        _settings.Save();
        return BackgroundUrl();
    }

    private JsonNode ClearBackground()
    {
        _settings.BackgroundImage = null;
        _settings.Save();
        return true;
    }

    private string? BackgroundUrl()
    {
        var path = _settings.BackgroundImage;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return $"https://{FilesHost}/background?v={File.GetLastWriteTimeUtc(path).Ticks}";
    }

    /// <summary>
    /// https://app.local/… — the page, built into the exe; https://files.local/traders/&lt;folder&gt;/&lt;file&gt;, /background,
    /// /game/quests|traders/&lt;file&gt; — files from disk; /icon/&lt;id&gt;-icon.webp — item pictures (ItemIcons).
    /// </summary>
    private void ServeFile(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web.CoreWebView2.Environment;
        try
        {
            var uri = new Uri(e.Request.Uri);
            if (uri.Host.Equals(AppHost, StringComparison.OrdinalIgnoreCase)) { ServePage(e, uri); return; }
            var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
            if (parts is ["icon", var iconName]) { ServeIcon(e, iconName); return; }
            string? path = null;
            if (parts is ["background"]) path = _settings.BackgroundImage;
            else if (parts is ["game", "quests", var gameFile] && _gameQuestImages != null && Safe(gameFile)) path = Path.Combine(_gameQuestImages, gameFile);
            else if (parts is ["game", "traders", var traderFile] && _gameTraderImages != null && Safe(traderFile)) path = Path.Combine(_gameTraderImages, traderFile);
            else if (parts is ["traders", var folder, var file] && _modFolder != null && Safe(folder) && Safe(file)) path = Path.Combine(_modFolder, "traders", folder, file);

            if (path == null || !File.Exists(path))
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not found", "Cache-Control: no-store");
                return;
            }
            var stream = new MemoryStream(File.ReadAllBytes(path));
            var cache = parts[0] == "game" ? "Cache-Control: max-age=86400" : "Cache-Control: no-store";
            e.Response = env.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {ContentType(path)}\r\n{cache}\r\nAccess-Control-Allow-Origin: *");
        }
        catch
        {
            e.Response = env.CreateWebResourceResponse(null, 500, "Error", "Cache-Control: no-store");
        }
    }

    private static bool Safe(string name) => !name.Contains("..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".avif" => "image/avif",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        _ => "application/octet-stream",
    };

    /// <summary>The page's own files, from the exe (resources named ui/…).</summary>
    private void ServePage(CoreWebView2WebResourceRequestedEventArgs e, Uri uri)
    {
        var env = _web.CoreWebView2.Environment;
        var name = "ui/" + Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var asm = Assembly.GetExecutingAssembly();
        // RecursiveDir gives "ui/items\app.js" on Windows builds, "ui/items/app.js" elsewhere
        var stream = asm.GetManifestResourceStream(name) ?? asm.GetManifestResourceStream(name.Replace('/', '\\').Replace("ui\\", "ui/"));
        if (stream == null) { e.Response = env.CreateWebResourceResponse(null, 404, "Not found", "Cache-Control: no-store"); return; }
        e.Response = env.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {ContentType(name)}\r\nCache-Control: no-store");
    }

    private void ServeIcon(CoreWebView2WebResourceRequestedEventArgs e, string name)
    {
        var deferral = e.GetDeferral();
        _ = Task.Run(async () =>
        {
            byte[]? bytes = null;
            try { bytes = await ItemIcons.GetAsync(name); } catch { /* shown as "no picture" */ }
            BeginInvoke(() =>
            {
                try
                {
                    var env = _web.CoreWebView2.Environment;
                    e.Response = bytes == null
                        ? env.CreateWebResourceResponse(null, 404, "Not found", "Cache-Control: no-store")
                        : env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", "Content-Type: image/webp\r\nCache-Control: max-age=604800\r\nAccess-Control-Allow-Origin: *");
                }
                catch { /* window closing */ }
                finally { deferral.Complete(); }
            });
        });
    }

    private static Bitmap LoadBitmap(string path) => TryLoadBitmap(File.ReadAllBytes(path)) ?? throw new InvalidOperationException($"{Path.GetFileName(path)} couldn't be read as a picture.");

    /// <summary>The picture, or null when Windows can't read that format (webp, avif, svg…).</summary>
    private static Bitmap? TryLoadBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception) { return null; }
    }

    private static void SaveSquareImage(Bitmap input, string target, int size)
    {
        using var output = new Bitmap(size, size);
        using (var g = Graphics.FromImage(output))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Black);
            float scale = Math.Max((float)size / input.Width, (float)size / input.Height);
            float w = input.Width * scale, h = input.Height * scale;
            g.DrawImage(input, (size - w) / 2, (size - h) / 2, w, h);
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
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 256, 256), Color.FromArgb(40, 110, 70), Color.FromArgb(18, 18, 18), 45f))
                g.FillRectangle(brush, 0, 0, 256, 256);
            using var font = new Font("Segoe UI Black", 80f);
            var text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.White, (256 - size.Width) / 2, (256 - size.Height) / 2);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    private static string? AverageColor(string path)
    {
        try
        {
            using var image = LoadBitmap(path);
            using var small = new Bitmap(image, new Size(8, 8));
            long r = 0, g = 0, b = 0;
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                {
                    var c = small.GetPixel(x, y);
                    r += c.R; g += c.G; b += c.B;
                }
            return $"#{r / 64:x2}{g / 64:x2}{b / 64:x2}";
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ misc

    private JsonNode SaveUi(JsonObject? ui)
    {
        _settings.Ui = (JsonObject?)ui?.DeepClone();
        _settings.Save();
        return true;
    }

    private JsonNode SaveLgUi(JsonObject? ui)
    {
        _settings.LgUi = (JsonObject?)ui?.DeepClone();
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
        EditorLog.Info("close", $"closing with {_unsaved} unsaved change(s)");
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
