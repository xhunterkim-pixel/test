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
    private const string TradersHost = "traders.local";
    private const string BackgroundHost = "bg.local";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly Settings _settings = Settings.Load();
    private readonly ItemDatabase _db = new();
    private string? _modFolder;
    private int _unsaved;

    public HostForm()
    {
        Text = "Trader Editor";
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
                    "Open the download page now?", "Trader Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
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
        MapBackground();

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
        };
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return result;

        _modFolder = folder;
        _settings.ModFolder = folder;
        _settings.Save();
        result["modFolder"] = folder;

        var tradersFolder = Path.Combine(folder, "traders");
        Directory.CreateDirectory(tradersFolder);
        _web.CoreWebView2.ClearVirtualHostNameToFolderMapping(TradersHost);
        _web.CoreWebView2.SetVirtualHostNameToFolderMapping(TradersHost, tradersFolder, CoreWebView2HostResourceAccessKind.Allow);

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
        };
    }

    private static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";

    /// <summary>URL of a file in a trader folder; the ?v= changes with the file so the page never shows an old copy.</summary>
    private string ImageUrl(string folder, string file)
    {
        var path = Path.Combine(_modFolder!, "traders", folder, file);
        long v = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        return $"https://{TradersHost}/{Uri.EscapeDataString(folder)}/{Uri.EscapeDataString(file)}?v={v}";
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
                });
            }
            result["items"] = items;
            result["itemsStatus"] = $"{_db.Items.Count:N0} items";
        }
        catch (Exception e)
        {
            result["itemsStatus"] = "Item database failed to load: " + e.Message;
        }
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
        MapBackground();
        return BackgroundUrl();
    }

    private JsonNode ClearBackground()
    {
        _settings.BackgroundImage = null;
        _settings.Save();
        return true;
    }

    private void MapBackground()
    {
        var path = _settings.BackgroundImage;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        _web.CoreWebView2.ClearVirtualHostNameToFolderMapping(BackgroundHost);
        _web.CoreWebView2.SetVirtualHostNameToFolderMapping(BackgroundHost, Path.GetDirectoryName(path)!, CoreWebView2HostResourceAccessKind.Allow);
    }

    private string? BackgroundUrl()
    {
        var path = _settings.BackgroundImage;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return $"https://{BackgroundHost}/{Uri.EscapeDataString(Path.GetFileName(path))}?v={File.GetLastWriteTimeUtc(path).Ticks}";
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
        Text = count > 0 ? $"Trader Editor  •  {count} unsaved" : "Trader Editor";
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
