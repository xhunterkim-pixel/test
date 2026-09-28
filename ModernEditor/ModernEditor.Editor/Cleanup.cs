using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualBasic.FileIO;

namespace ModernEditor.Editor;

/// <summary>
/// Old files Modern Editor made unnecessary, offered on the start page ("Clean Up"):
///   · user\ModernEditor_old_mods\*                   backups of CustomTraders / ItemStatEditor made by "Move My Files"
///   · %AppData%\CustomTradersEditor, LevelGateEditor  the old editors' settings and icon caches (settings already imported)
///   · the old editor programs (CustomTraders.Editor.exe, LevelAndItemEditor.exe, LevelGate.Editor.exe) near the SPT folder
///   · user\mods\LevelGate\item_stats.json            where the first Level &amp; Item Editor kept item stats (merged first)
/// Only what a scan found can be removed, and it goes to the Recycle Bin (not deleted for good).
/// Level Gate itself (its plugin, its server mod, level_requirements.json) is never listed.
/// The old mods still inside user\mods are left to "Move My Files", so nothing is lost.
/// </summary>
public static class Cleanup
{
    private static readonly string[] OldExes = { "CustomTraders.Editor", "LevelAndItemEditor", "LevelGate.Editor" };
    /// <summary>What the old editor folders held besides the program itself.</summary>
    private static readonly string[] EditorExtras =
    {
        "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "Microsoft.Web.WebView2.Wpf.dll", "WebView2Loader.dll",
    };

    public static JsonArray Scan(string? modFolder, string? configFile)
    {
        var found = new List<(string path, string kind, string what)>();
        var modsRoot = Migration.ModsRootOf(modFolder);
        var spt = Migration.SptRootOf(configFile) ?? Migration.SptRootOf(modsRoot);

        // backups made by "Move My Files"
        if (modsRoot != null)
        {
            var parked = Path.Combine(Directory.GetParent(modsRoot)!.FullName, "ModernEditor_old_mods");
            if (Directory.Exists(parked))
                foreach (var dir in Directory.GetDirectories(parked))
                    found.Add((dir, "backup", $"Backup of the old {Path.GetFileName(dir).Split('_')[0]} mod (its files are already in ModernEditor)"));
            var lgStats = Path.Combine(modsRoot, "LevelGate", "item_stats.json");
            if (File.Exists(lgStats) && File.Exists(Path.Combine(modsRoot, Migration.ModName, "item_stats.json")))
                found.Add((lgStats, "stats", "Item stats of the first Level & Item Editor (merged into ModernEditor\\item_stats.json before removing)"));
        }

        // the old editors' settings and icon caches — only once Modern Editor has its own settings
        if (File.Exists(Path.Combine(Settings.Folder, "settings.json")))
            foreach (var old in Settings.OldFolders)
                if (Directory.Exists(old))
                    found.Add((old, "settings", $"{(old.EndsWith("CustomTradersEditor") ? "Custom Trader Creator" : "Level & Item Editor")} settings and icon cache (already imported)"));

        // the old editor programs
        var here = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in SearchRoots(spt, here))
            foreach (var dir in Folders(root, root.EndsWith("plugins", StringComparison.OrdinalIgnoreCase) ? 3 : 2))
                foreach (var exe in OldExes)
                {
                    var path = Path.Combine(dir, exe + ".exe");
                    if (!File.Exists(path) || !seen.Add(path)) continue;
                    if (string.Equals(dir.TrimEnd('\\', '/'), here, StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add((path, "program", $"Old editor program {exe}.exe — Modern Editor replaces it"));
                }

        return new JsonArray(found.Select(f => (JsonNode)new JsonObject
        {
            ["path"] = f.path,
            ["kind"] = f.kind,
            ["what"] = f.what,
            ["size"] = SizeOf(f.path, f.kind),
        }).ToArray());
    }

    /// <summary>Folders the old editors were usually put in: the SPT folder, the one above it, BepInEx\plugins, and around this program.</summary>
    private static IEnumerable<string> SearchRoots(string? spt, string here)
    {
        var roots = new List<string>();
        if (spt != null)
        {
            roots.Add(spt);
            if (Directory.GetParent(spt) is { } up) roots.Add(up.FullName);
            roots.Add(Path.Combine(spt, "BepInEx", "plugins"));
        }
        if (Directory.GetParent(here) is { } parent) roots.Add(parent.FullName);
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Folders(string root, int depth)
    {
        yield return root;
        if (depth == 0) yield break;
        string[] subs;
        try { subs = Directory.GetDirectories(root); } catch { yield break; }
        foreach (var sub in subs)
        {
            var name = Path.GetFileName(sub);
            // big game / server folders never hold an editor
            if (name.StartsWith('.') || name is "EscapeFromTarkov_Data" or "SPT_Data" or "user" or "Logs" or "cache" or "Windows" or "Program Files" or "Program Files (x86)") continue;
            foreach (var d in Folders(sub, depth - 1)) yield return d;
        }
    }

    private static long SizeOf(string path, string kind)
    {
        try
        {
            if (kind == "program") return ProgramFiles(path).Sum(f => new FileInfo(f).Length);
            if (File.Exists(path)) return new FileInfo(path).Length;
            return Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    /// <summary>An old editor's files: the program and what came with it (WebView2, ui\), not anything else in that folder.</summary>
    private static List<string> ProgramFiles(string exe)
    {
        var dir = Path.GetDirectoryName(exe)!;
        var name = Path.GetFileNameWithoutExtension(exe);
        var files = new List<string>();
        foreach (var ext in new[] { ".exe", ".dll", ".runtimeconfig.json", ".deps.json", ".pdb", ".crash.txt" })
            if (File.Exists(Path.Combine(dir, name + ext))) files.Add(Path.Combine(dir, name + ext));
        // the WebView2 files only when no other program in that folder needs them
        bool alone = !Directory.GetFiles(dir, "*.exe").Any(f => !OldExes.Contains(Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase));
        if (alone)
            foreach (var extra in EditorExtras)
                if (File.Exists(Path.Combine(dir, extra))) files.Add(Path.Combine(dir, extra));
        if (alone && Directory.Exists(Path.Combine(dir, "ui"))) files.AddRange(Directory.GetFiles(Path.Combine(dir, "ui"), "*", System.IO.SearchOption.AllDirectories));
        return files;
    }

    /// <summary>Sends what was picked to the Recycle Bin — only paths a fresh scan still finds. Returns what was done, line by line.</summary>
    public static JsonArray Run(IEnumerable<string> picked, string? modFolder, string? configFile)
    {
        var log = new JsonArray();
        var scan = Scan(modFolder, configFile).OfType<JsonObject>().ToDictionary(x => (string)x["path"]!, x => (string)x["kind"]!, StringComparer.OrdinalIgnoreCase);
        foreach (var path in picked.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!scan.TryGetValue(path, out var kind)) { log.Add($"✖ {path}: not in the list any more — skipped"); continue; }
            try
            {
                switch (kind)
                {
                    case "settings":
                        KeepIcons(path);
                        Recycle(path);
                        break;
                    case "stats":
                        MergeStats(path, Path.Combine(Migration.ModsRootOf(modFolder)!, Migration.ModName, "item_stats.json"));
                        Recycle(path);
                        break;
                    case "program":
                        var dir = Path.GetDirectoryName(path)!;
                        foreach (var f in ProgramFiles(path)) FileSystem.DeleteFile(f, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        RemoveEmpty(Path.Combine(dir, "ui"));
                        RemoveEmpty(dir);
                        break;
                    default:
                        Recycle(path);
                        break;
                }
                log.Add($"{path} → Recycle Bin");
            }
            catch (Exception e) { log.Add($"✖ {path}: {e.Message}"); }
        }
        return log;
    }

    private static void Recycle(string path)
    {
        if (Directory.Exists(path)) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else if (File.Exists(path)) FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static void RemoveEmpty(string dir)
    {
        try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
    }

    /// <summary>The old icon cache's pictures move into Modern Editor's, so nothing has to be downloaded again.</summary>
    private static void KeepIcons(string oldFolder)
    {
        var from = Path.Combine(oldFolder, "icons");
        if (!Directory.Exists(from)) return;
        var to = Path.Combine(Settings.Folder, "icons");
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from, "*.webp"))
        {
            var dest = Path.Combine(to, Path.GetFileName(f));
            if (!File.Exists(dest)) File.Copy(f, dest);
        }
    }

    /// <summary>Items only the old file has are added to ModernEditor's item_stats.json (its own edits win).</summary>
    private static void MergeStats(string old, string mine)
    {
        var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var target = JsonNode.Parse(File.ReadAllText(mine), documentOptions: opts) as JsonObject ?? new JsonObject();
        var theirs = JsonNode.Parse(File.ReadAllText(old), documentOptions: opts) as JsonObject ?? new JsonObject();
        int added = 0;
        foreach (var (id, e) in theirs) if (!target.ContainsKey(id) && e != null) { target[id] = e.DeepClone(); added++; }
        if (added > 0) File.WriteAllText(mine, target.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
