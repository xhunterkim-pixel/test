using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomTraders.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CustomTraders.Editor;

/// <summary>
/// The editor window. The whole interface is a web page (ui\index.html)
/// drawn by the Edge engine (WebView2) on the graphics card, so scrolling and
/// page changes are smooth. This class only does what a page can't: read and
/// write trader.json files, open file/folder pickers, crop icons, and load
/// the game's item list. The page calls these through small JSON messages:
///   page → { id, method, args }      host → { id, ok, result | error }
/// </summary>
public sealed class HostForm : Form
{
    private const string AppHost = "app.local";
    // Pictures (trader icons, quest images, background) are served by this
    // window itself from disk, so a changed picture always shows right away.
    private const string FilesHost = "files.local";

    /// <summary>Shown in the window title, the page's top-left corner and the start menu.</summary>
    public const string Version = "1.0.0";
    private const string AppTitle = "Custom Trader Creator";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly Settings _settings = Settings.Load();
    private readonly ItemDatabase _db = new();
    private string? _modFolder;
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
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.Folder, "WebView2"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (MessageBox.Show(this,
                    "The editor needs the Microsoft Edge WebView2 Runtime (normally already part of Windows 10/11).\n\n" +
                    "Open the download page now?", "Custom Trader Creator", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                OpenUrl("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("TRADER_EDITOR_DEVTOOLS") == "1";
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // no F5 reload / Ctrl+P etc. (would lose unsaved edits)
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        core.SetVirtualHostNameToFolderMapping(AppHost, uiFolder, CoreWebView2HostResourceAccessKind.Allow);
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
        try
        {
            var message = JsonNode.Parse(json)!.AsObject();
            id = message["id"]?.DeepClone();
            var method = (string?)message["method"] ?? "";
            var args = message["args"] as JsonObject ?? new JsonObject();
            var result = Call(method, args);
            Reply(new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result });
        }
        catch (Exception e)
        {
            Reply(new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = e.Message });
        }
    }

    private void Reply(JsonObject reply) => _web.CoreWebView2?.PostWebMessageAsJson(reply.ToJsonString());

    private JsonNode? Call(string method, JsonObject a) => method switch
    {
        "init" => Snapshot(_settings.ModFolder),
        "browseModFolder" => BrowseModFolder(),
        "reload" => Snapshot(_modFolder),
        "saveTrader" => SaveTrader(Str(a, "folder"), Str(a, "text")),
        "newTrader" => NewTrader(Str(a, "name")),
        "deleteTrader" => DeleteTrader(Str(a, "folder")),
        "duplicateTrader" => DuplicateTrader(Str(a, "folder"), Str(a, "name"), Str(a, "text")),
        "listDeleted" => ListDeleted(),
        "modsScanAll" => ModsScanAll(),
        "modAddFolder" => ModAddFolder(),
        "modSet" => ModSet(Str(a, "name"), (bool?)a["enabled"] ?? true),
        "modRemove" => ModRemove(Str(a, "name")),
        "modRescan" => ItemsPayload(rescan: true),
        "modForget" => ModForget(Str(a, "name")),
        "restoreDeleted" => RestoreDeleted(Str(a, "name")),
        "purgeDeleted" => PurgeDeleted(Str(a, "name")),
        "chooseAvatar" => ChooseAvatar(Str(a, "folder")),
        "chooseQuestImage" => ChooseQuestImage(Str(a, "folder"), Str(a, "questId")),
        "chooseBackground" => ChooseBackground(),
        "clearBackground" => ClearBackground(),
        "saveUi" => SaveUi(a["ui"] as JsonObject),
        "setUnsaved" => SetUnsaved((int?)a["count"] ?? 0),
        "openFolder" => OpenFolder(Str(a, "folder")),
        _ => throw new InvalidOperationException($"Unknown request '{method}'."),
    };

    private static string Str(JsonObject a, string key) => (string?)a[key] ?? "";

    // ------------------------------------------------------------------ mod folder / traders

    /// <summary>Everything the page needs to show: settings, traders, item list.</summary>
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
        };
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return result;

        _modFolder = folder;
        _settings.ModFolder = folder;
        _settings.Save();
        result["modFolder"] = folder;

        var tradersFolder = Path.Combine(folder, "traders");
        Directory.CreateDirectory(tradersFolder);

        var traders = (JsonArray)result["traders"]!;
        foreach (var dir in Directory.GetDirectories(tradersFolder).OrderBy(d => d))
        {
            var file = Path.Combine(dir, "trader.json");
            if (!File.Exists(file)) continue;
            try
            {
                traders.Add(TraderPayload(dir, JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                })!));
            }
            catch (Exception e)
            {
                ((JsonArray)result["problems"]!).Add($"{Path.GetFileName(dir)}: could not read trader.json — {e.Message}");
            }
        }

        LoadItems(folder, result);
        result["deletedCount"] = DeletedDirs().Length;
        return result;
    }

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
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";

    /// <summary>URL of a file in a trader folder; the ?v= changes with the file so the page never shows an old copy.</summary>
    private string ImageUrl(string folder, string file)
    {
        var path = Path.Combine(_modFolder!, "traders", folder, file);
        long v = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        return $"https://{FilesHost}/traders/{Uri.EscapeDataString(folder)}/{Uri.EscapeDataString(file)}?v={v}";
    }

    private void LoadItems(string modFolder, JsonObject result)
    {
        try
        {
            var dbFolder = ItemDatabase.FindDatabaseFolder(modFolder);
            if (dbFolder == null)
            {
                result["itemsStatus"] = "Item database not found (SPT_Data\\database above the mod folder) — item names unavailable.";
                return;
            }
            if (_db.SourceFolder != dbFolder || !_db.IsLoaded) _db.Load(dbFolder);
            var items = new JsonArray();
            foreach (var item in _db.Items.Values)
            {
                items.Add(new JsonObject
                {
                    ["i"] = item.Id,
                    ["n"] = item.Name,
                    ["s"] = item.ShortName,
                    ["c"] = item.Category.ToString(),
                    ["k"] = item.Caliber.Length > 0 ? item.Caliber : null,
                    ["h"] = _db.Handbook.TryGetValue(item.Id, out var h) ? Math.Round(h) : null,
                    ["p"] = _db.TraderSellPrice(item.Id) is var p && p > 0 ? p : null,
                    ["f"] = _db.Flea.TryGetValue(item.Id, out var f) ? Math.Round(f) : null,
                    ["ph"] = _db.Presets.TryGetValue(item.Id, out var preset) ? Math.Round(preset.Handbook) : null,
                    ["pf"] = _db.Presets.TryGetValue(item.Id, out var preset2) ? Math.Round(preset2.Flea) : null,
                    ["x"] = item.Hidden ? 1 : null,
                    ["g"] = item.Group,
                    ["wc"] = item.WeaponClass.Length > 0 ? item.WeaponClass : null,
                });
            }
            int modded = AddModItems(items, result);
            result["items"] = items;
            result["itemsStatus"] = $"{_db.Items.Count:N0} items" + (modded > 0 ? $" + {modded:N0} modded" : "");
            var quests = new JsonArray();
            foreach (var (id, name, trader) in _db.LoadQuests())
                quests.Add(new JsonObject { ["i"] = id, ["n"] = name, ["t"] = trader });
            result["gameQuests"] = quests;
            result["traderRate"] = Math.Round(_db.BestTraderRate * 100);
            result["gameQuestImages"] = GameQuestImages(dbFolder, result);
        }
        catch (Exception e)
        {
            result["itemsStatus"] = "Item database failed to load: " + e.Message;
        }
    }

    // ------------------------------------------------------------------ items from other mods (Mods page)

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

    /// <summary>Adds the items of switched-on imports to the item list; returns how many.</summary>
    private int AddModItems(JsonArray items, JsonObject result, bool rescan = false)
    {
        ScanMods(rescan);
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
                    ["k"] = it.Caliber.Length > 0 ? it.Caliber : null,
                    ["h"] = it.Handbook > 0 ? Math.Round(it.Handbook) : null, ["p"] = sell > 0 ? sell : null,
                    ["m"] = mod.Name,
                    ["g"] = it.Group,
                    ["wc"] = it.WeaponClass.Length > 0 ? it.WeaponClass : null,
                };
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

    /// <summary>Items + mods only (the traders on the page stay as they are).</summary>
    private JsonObject ItemsPayload(bool rescan = false)
    {
        var result = new JsonObject();
        if (_modFolder == null) return result;
        if (rescan) _modScan.Clear();
        LoadItems(_modFolder, result);
        return result;
    }

    /// <summary>...\user\mods (the folder that holds CustomTraders and the other server mods).</summary>
    private string? ModsRoot() => _modFolder == null ? null : Directory.GetParent(_modFolder)?.FullName;

    /// <summary>Name of a mod from any folder inside it: the folder right under user\mods.</summary>
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
        var root = ModsRoot() ?? throw new InvalidOperationException("Pick the mod folder first (Browse...).");
        var added = new JsonArray();
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(_modFolder!).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
            if (_settings.Mods.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            List<ModItem> found;
            try { found = ModScanner.Scan(dir, _db); } catch { continue; }
            if (found.Count == 0) continue;
            _settings.Mods.Add(new ModImport { Name = name, Folder = dir, Enabled = true });
            _modScan[name] = found;
            added.Add(name);
        }
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

    /// <summary>Forgets every remembered id of a mod that's no longer imported.</summary>
    private JsonNode ModForget(string name)
    {
        foreach (var id in _settings.ModIdMemory.Where(kv => kv.Value.Length > 0 && kv.Value[0] == name).Select(kv => kv.Key).ToList())
            _settings.ModIdMemory.Remove(id);
        _settings.Save();
        return ItemsPayload();
    }

    private JsonNode? BrowseModFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the CustomTraders mod folder (…\\SPT_Runtime\\user\\mods\\CustomTraders)",
            UseDescriptionForTitle = true,
        };
        if (_modFolder != null) dialog.InitialDirectory = _modFolder;
        return dialog.ShowDialog(this) == DialogResult.OK ? Snapshot(dialog.SelectedPath) : null;
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
        if (_modFolder == null) throw new InvalidOperationException("Pick the mod folder first (Browse...).");
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

    /// <summary>Copies a trader folder (icons, quest images) and writes the copy's trader.json (with new ids, made by the page).</summary>
    private JsonNode DuplicateTrader(string folder, string name, string text)
    {
        JsonNode.Parse(text); // never write something that isn't JSON
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

    // ------------------------------------------------------------------ the game's quest pictures

    private string? _gameQuestImages;

    /// <summary>The game's quest pictures (…/images/quests near the database), usable as quest images without copying.</summary>
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
            list.Add(new JsonObject
            {
                ["n"] = Path.GetFileNameWithoutExtension(file),
                ["u"] = $"https://{FilesHost}/game/quests/{Uri.EscapeDataString(file)}",
            });
        }
        return list;
    }

    /// <summary>
    /// Looks for an "images\quests" (or "images\quest") folder with pictures in and around
    /// SPT_Data — installs differ (SPT_Data\images, SPT_Data\Server\images, SPT_Runtime\SPT_Data\images...).
    /// </summary>
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
            if (best != null) break; // nearest match wins
        }
        return best;

        static IEnumerable<string> Search(string folder, int depth)
        {
            IEnumerable<string> subs;
            try { subs = Directory.GetDirectories(folder); } catch { yield break; }
            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub).ToLowerInvariant();
                if (name is "user" or "node_modules" or "bepinex" or "logs" or "cache" or "database") continue; // big and never it
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

    /// <summary>Moves a deleted trader back into traders/ (with a new folder name if taken).</summary>
    private JsonNode RestoreDeleted(string name)
    {
        var source = TrashDir(name);
        string baseName = System.Text.RegularExpressions.Regex.Replace(name, @"_\d{8}_\d{6}$", "");
        string target = Path.Combine(_modFolder!, "traders", baseName);
        for (int n = 2; Directory.Exists(target); n++) target = Path.Combine(_modFolder!, "traders", $"{baseName} {n}");
        Directory.Move(source, target);
        return Snapshot(_modFolder);
    }

    /// <summary>Erases one deleted trader for good, or all of them (name = "*").</summary>
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
        using var dialog = new OpenFileDialog { Title = title, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp" };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private JsonNode? ChooseAvatar(string folder)
    {
        var dir = TraderDir(folder);
        var source = PickImage("Choose trader icon");
        if (source == null) return null;
        var target = Path.Combine(dir, "avatar.png");
        SaveSquareImage(source, target, 256); // square 256x256, cropped to fill
        return new JsonObject
        {
            ["file"] = "avatar.png",
            ["url"] = ImageUrl(folder, "avatar.png"),
            ["avatarColor"] = AverageColor(target),
        };
    }

    private JsonNode? ChooseQuestImage(string folder, string questId)
    {
        var dir = TraderDir(folder);
        if (!Ids.IsValid(questId)) throw new InvalidOperationException("Invalid quest id.");
        var source = PickImage("Choose quest image");
        if (source == null) return null;
        string name = $"quest_{questId}.png";
        using (var input = LoadBitmap(source))
            input.Save(Path.Combine(dir, name), ImageFormat.Png);
        return new JsonObject { ["file"] = name, ["url"] = ImageUrl(folder, name) };
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
    /// Answers https://files.local/traders/&lt;folder&gt;/&lt;file&gt; and
    /// https://files.local/background with the file from disk (never cached).
    /// </summary>
    private void ServeFile(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web.CoreWebView2.Environment;
        try
        {
            var uri = new Uri(e.Request.Uri);
            var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
            if (parts is ["icon", var iconName]) { ServeIcon(e, iconName); return; }
            string? path = null;
            if (parts is ["background"]) path = _settings.BackgroundImage;
            else if (parts is ["game", "quests", var gameFile] && _gameQuestImages != null &&
                     !gameFile.Contains("..") && gameFile.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                path = Path.Combine(_gameQuestImages, gameFile);
            else if (parts is ["traders", var folder, var file] && _modFolder != null &&
                     !folder.Contains("..") && !file.Contains("..") &&
                     folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                path = Path.Combine(_modFolder, "traders", folder, file);

            if (path == null || !File.Exists(path))
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not found", "Cache-Control: no-store");
                return;
            }
            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream",
            };
            var stream = new MemoryStream(File.ReadAllBytes(path));
            // the game's own pictures never change: let the browser keep them (the gallery shows hundreds)
            var cache = parts[0] == "game" ? "Cache-Control: max-age=86400" : "Cache-Control: no-store";
            e.Response = env.CreateWebResourceResponse(stream, 200, "OK",
                $"Content-Type: {type}\r\n{cache}\r\nAccess-Control-Allow-Origin: *");
        }
        catch
        {
            e.Response = env.CreateWebResourceResponse(null, 500, "Error", "Cache-Control: no-store");
        }
    }

    /// <summary>https://files.local/icon/&lt;id&gt;-icon.webp — downloaded / cached in the background (ItemIcons).</summary>
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

    private static Bitmap LoadBitmap(string path)
    {
        // Load a copy so the file isn't locked.
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static void SaveSquareImage(string source, string target, int size)
    {
        using var input = LoadBitmap(source);
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

    /// <summary>The icon's average color as #rrggbb, for the header gradient (like Spotify).</summary>
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

    private JsonNode SetUnsaved(int count)
    {
        _unsaved = count;
        Text = count > 0 ? $"{AppTitle}  •  {count} unsaved" : AppTitle;
        return true;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_unsaved == 0) return;
        if (MessageBox.Show(this, $"{_unsaved} trader(s) have unsaved changes. Close and lose them?", "Unsaved changes",
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
