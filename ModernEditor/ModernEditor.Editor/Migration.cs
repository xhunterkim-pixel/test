using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModernEditor.Editor;

/// <summary>
/// Moving the files of the two old server mods into Modern Editor's one mod folder:
///   user\mods\CustomTraders\traders\*          → user\mods\ModernEditor\traders\*          (one trader.json per trader, as before)
///   user\mods\CustomTraders\deleted_traders\*  → user\mods\ModernEditor\deleted_traders\*
///   user\mods\ItemStatEditor\item_stats.json   → user\mods\ModernEditor\item_stats.json    (its own file, as before)
/// Everything is copied first; only then are the old mod folders moved out of user\mods (into
/// user\ModernEditor_old_mods\, their DLLs renamed .dll.old) so the server doesn't load the same traders twice.
/// Level Gate's level_requirements.json is not part of it: it stays where Level Gate reads it, untouched.
/// </summary>
public static class Migration
{
    public const string ModName = "ModernEditor";

    /// <summary>user\mods above / around a folder: a mods folder itself, a mod folder inside it, or an SPT folder.</summary>
    public static string? ModsRootOf(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var dir = new DirectoryInfo(folder);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            if (dir.Name.Equals("mods", StringComparison.OrdinalIgnoreCase) && dir.Parent?.Name.Equals("user", StringComparison.OrdinalIgnoreCase) == true) return dir.FullName;
            foreach (var c in new[] { Path.Combine(dir.FullName, "SPT_Runtime", "user", "mods"), Path.Combine(dir.FullName, "user", "mods") })
                if (Directory.Exists(c)) return c;
        }
        return null;
    }

    /// <summary>The SPT folder (the one holding BepInEx) above a folder or file.</summary>
    public static string? SptRootOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var dir = File.Exists(path) ? new FileInfo(path).Directory : new DirectoryInfo(path);
        for (int i = 0; i < 9 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "BepInEx"))) return dir.FullName;
        return null;
    }

    /// <summary>What there is to move (null = nothing).</summary>
    public static JsonObject? Pending(string modsRoot)
    {
        var target = Path.Combine(modsRoot, ModName);
        var found = new JsonArray();
        var ct = Path.Combine(modsRoot, "CustomTraders");
        if (Directory.Exists(Path.Combine(ct, "traders")))
        {
            var traders = Directory.GetDirectories(Path.Combine(ct, "traders")).Where(d => File.Exists(Path.Combine(d, "trader.json"))).Select(Path.GetFileName).ToList();
            if (traders.Count > 0 || File.Exists(Path.Combine(ct, "CustomTraders.dll")))
                found.Add(new JsonObject { ["mod"] = "CustomTraders", ["folder"] = ct, ["what"] = $"{traders.Count} trader(s): {string.Join(", ", traders.Take(8))}{(traders.Count > 8 ? "…" : "")}" });
        }
        else if (File.Exists(Path.Combine(ct, "CustomTraders.dll")))
            found.Add(new JsonObject { ["mod"] = "CustomTraders", ["folder"] = ct, ["what"] = "the old server mod (no traders)" });
        foreach (var (name, dll) in new[] { ("ItemStatEditor", "ItemStatEditor.Server.dll") })
        {
            var folder = Path.Combine(modsRoot, name);
            if (!Directory.Exists(folder)) continue;
            int n = CountStats(Path.Combine(folder, "item_stats.json"));
            if (n > 0 || File.Exists(Path.Combine(folder, dll)))
                found.Add(new JsonObject { ["mod"] = name, ["folder"] = folder, ["what"] = n > 0 ? $"item_stats.json ({n} item edit(s))" : "the old server mod (no edits)" });
        }
        if (found.Count == 0) return null;
        return new JsonObject { ["from"] = found, ["to"] = target, ["serverMod"] = File.Exists(Path.Combine(target, "ModernEditor.dll")) };
    }

    private static int CountStats(string file)
    {
        try { return File.Exists(file) && JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonObject o ? o.Count : 0; }
        catch { return 0; }
    }

    /// <summary>Copies everything into ModernEditor, then moves the old mods out of user\mods. Returns what was done, line by line.</summary>
    public static JsonArray Run(string modsRoot)
    {
        var log = new JsonArray();
        var target = Path.Combine(modsRoot, ModName);
        Directory.CreateDirectory(Path.Combine(target, "traders"));
        var ct = Path.Combine(modsRoot, "CustomTraders");
        foreach (var sub in new[] { "traders", "deleted_traders" })
        {
            var from = Path.Combine(ct, sub);
            if (!Directory.Exists(from)) continue;
            foreach (var dir in Directory.GetDirectories(from))
            {
                var to = Path.Combine(target, sub, Path.GetFileName(dir));
                if (Directory.Exists(to)) { log.Add($"{sub}\\{Path.GetFileName(dir)}: already in ModernEditor — kept that one"); continue; }
                CopyDir(dir, to);
                log.Add($"{sub}\\{Path.GetFileName(dir)} → ModernEditor\\{sub}");
            }
        }
        // item stats: the old file, and the one the first Level & Item Editor kept in LevelGate's server folder
        foreach (var old in new[] { Path.Combine(modsRoot, "ItemStatEditor", "item_stats.json"), Path.Combine(modsRoot, "LevelGate", "item_stats.json") })
        {
            if (!File.Exists(old)) continue;
            var to = Path.Combine(target, "item_stats.json");
            if (!File.Exists(to)) { File.Copy(old, to); log.Add($"{Path.GetFileName(Path.GetDirectoryName(old))}\\item_stats.json → ModernEditor\\item_stats.json"); continue; }
            // both exist: ModernEditor's edits win, the old file only adds items it doesn't have yet
            var mine = JsonNode.Parse(File.ReadAllText(to)) as JsonObject ?? new JsonObject();
            var theirs = JsonNode.Parse(File.ReadAllText(old)) as JsonObject ?? new JsonObject();
            int added = 0;
            foreach (var (id, e) in theirs) if (!mine.ContainsKey(id) && e != null) { mine[id] = e.DeepClone(); added++; }
            File.WriteAllText(to, mine.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            log.Add($"{Path.GetFileName(Path.GetDirectoryName(old))}\\item_stats.json: {added} item(s) added to ModernEditor\\item_stats.json");
        }
        // the old mods out of user\mods (the server loads every folder there), DLLs renamed so nothing can load them
        var parked = Path.Combine(Directory.GetParent(modsRoot)!.FullName, "ModernEditor_old_mods");
        foreach (var name in new[] { "CustomTraders", "ItemStatEditor" })
        {
            var folder = Path.Combine(modsRoot, name);
            if (!Directory.Exists(folder)) continue;
            Directory.CreateDirectory(parked);
            var to = Path.Combine(parked, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}");
            try
            {
                Directory.Move(folder, to);
                foreach (var dll in Directory.GetFiles(to, "*.dll", SearchOption.AllDirectories)) File.Move(dll, dll + ".old");
                log.Add($"user\\mods\\{name} moved to user\\ModernEditor_old_mods (kept as a backup)");
            }
            catch (Exception e)
            {
                log.Add($"✖ user\\mods\\{name} couldn't be moved ({e.Message}) — close the SPT server and delete that folder yourself; your files are already copied.");
            }
        }
        return log;
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from)) CopyDir(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
