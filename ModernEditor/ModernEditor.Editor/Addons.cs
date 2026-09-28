using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModernEditor.Editor;

/// <summary>
/// Add-ons: other mods of ours the editor can read. Level Gate's DLL is only read; its level_requirements.json is
/// edited by the Level Limits page (same layout, only the "items" entries).
/// Level Gate: BepInEx\plugins\LevelGate\LevelGate.dll + config\level_requirements.json
/// ({ "items": { "&lt;item id&gt;": level } }), found next to the SPT install the traders live in.
/// </summary>
public static class Addons
{
    private const string LevelGateGuid = "com.yourname.levelgate";

    public static JsonObject Scan(string? modFolder, string? pickedPath)
    {
        return new JsonObject { ["levelgate"] = ScanLevelGate(modFolder, pickedPath) };
    }

    private static JsonObject ScanLevelGate(string? modFolder, string? pickedPath)
    {
        var result = new JsonObject { ["found"] = false };
        try
        {
            // Every Level Gate on the PC we can see (an SPT folder can hold more than one BepInEx,
            // e.g. C:\SPT\BepInEx and C:\SPT\SPT_Runtime\BepInEx): the one with a config wins.
            var found = new List<(string? Dll, string? Config)>();
            if (!string.IsNullOrEmpty(pickedPath) && File.Exists(pickedPath))
                found.Add(pickedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? (NearDll(pickedPath), pickedPath)
                    : (pickedPath, ConfigNear(pickedPath)));
            foreach (var root in SptRoots(modFolder, pickedPath))
            {
                var plugins = Path.Combine(root, "BepInEx", "plugins");
                var dll = FindDll(plugins);
                var cfg = dll != null ? ConfigNear(dll) : null;
                cfg ??= new[] { Path.Combine(plugins, "LevelGate", "config", "level_requirements.json") }.FirstOrDefault(File.Exists);
                if (dll != null || cfg != null) found.Add((dll ?? (cfg != null ? NearDll(cfg) : null), cfg));
            }
            found = found.DistinctBy(x => (x.Dll ?? "") + "|" + (x.Config ?? ""), StringComparer.OrdinalIgnoreCase).ToList();
            if (found.Count == 0) return result;

            var best = found
                .OrderByDescending(x => x.Config != null)
                .ThenByDescending(x => x.Config != null ? File.GetLastWriteTimeUtc(x.Config) : DateTime.MinValue)
                .First();
            var (bestDll, config) = best;

            result["found"] = true;
            result["dll"] = bestDll;
            result["config"] = config;
            result["version"] = bestDll != null ? VersionOf(bestDll) : null;
            var others = new JsonArray();
            foreach (var x in found.Where(x => x != best)) others.Add(x.Dll ?? x.Config);
            if (others.Count > 0) result["others"] = others;
            if (config == null) { result["error"] = "Level Gate is installed, but its config\\level_requirements.json wasn't found (it's made the first time the game starts with Level Gate). Use Locate Level Gate… if it's somewhere else."; return result; }
            result["modified"] = File.GetLastWriteTimeUtc(config).Ticks;
            var items = new JsonObject();
            using (var doc = JsonDocument.Parse(File.ReadAllText(config), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
            {
                if (doc.RootElement.TryGetProperty("items", out var map) && map.ValueKind == JsonValueKind.Object)
                    foreach (var p in map.EnumerateObject())
                    {
                        int level = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n) ? n
                            : p.Value.ValueKind == JsonValueKind.String && int.TryParse(p.Value.GetString(), out var m) ? m : 0;
                        if (level > 0) items[p.Name] = level;
                    }
            }
            result["items"] = items;
        }
        catch (Exception e)
        {
            result["error"] = "Couldn't read Level Gate: " + e.Message;
        }
        return result;
    }

    /// <summary>Version of the LevelGate.dll next to a level_requirements.json (…\LevelGate\config\ → …\LevelGate\LevelGate.dll).</summary>
    public static string? VersionOfConfig(string? config) =>
        string.IsNullOrEmpty(config) || NearDll(config) is not { } dll ? null : VersionOf(dll);

    /// <summary>config\level_requirements.json next to a LevelGate.dll.</summary>
    private static string? ConfigNear(string dll)
    {
        var dir = Path.GetDirectoryName(dll)!;
        return new[] { Path.Combine(dir, "config", "level_requirements.json"), Path.Combine(dir, "LevelGate", "config", "level_requirements.json") }.FirstOrDefault(File.Exists);
    }

    /// <summary>LevelGate.dll one folder above its config folder.</summary>
    private static string? NearDll(string config)
    {
        var dir = Path.GetDirectoryName(Path.GetDirectoryName(config) ?? "") ?? "";
        var dll = Path.Combine(dir, "LevelGate.dll");
        return File.Exists(dll) ? dll : null;
    }

    /// <summary>SPT folders (the ones holding BepInEx): above the traders' mod folder or a picked file, then C:\SPT and the like.</summary>
    private static IEnumerable<string> SptRoots(string? modFolder, string? near)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { modFolder, near, AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "BepInEx")) && seen.Add(dir.FullName)) yield return dir.FullName;
        }
        foreach (var drive in new[] { "C", "D", "E", "F", "G" })
            foreach (var name in new[] { "SPT", "SPTarkov", "SPT-AKI", "Games\\SPT", "Games\\SPTarkov" })
            {
                var root = $"{drive}:\\{name}";
                if (Directory.Exists(Path.Combine(root, "BepInEx")) && seen.Add(root)) yield return root;
            }
    }

    private static string? FindDll(string plugins)
    {
        if (!Directory.Exists(plugins)) return null;
        var direct = new[] { Path.Combine(plugins, "LevelGate", "LevelGate.dll"), Path.Combine(plugins, "LevelGate.dll") }.FirstOrDefault(File.Exists);
        if (direct != null) return direct;
        foreach (var dir in Directory.GetDirectories(plugins))
        {
            var dll = Path.Combine(dir, "LevelGate.dll");
            if (File.Exists(dll)) return dll;
        }
        return null;
    }

    /// <summary>The version in [BepInPlugin("com.yourname.levelgate", "LevelGate", "1.6.1")], read straight from the DLL's bytes.</summary>
    public static string? VersionOf(string dll)
    {
        try
        {
            var bytes = File.ReadAllBytes(dll);
            var guid = Encoding.UTF8.GetBytes(LevelGateGuid);
            for (int at = IndexOf(bytes, guid, 0); at > 0; at = IndexOf(bytes, guid, at + 1))
            {
                if (bytes[at - 1] != guid.Length) continue; // attribute strings are length-prefixed
                int p = at + guid.Length;
                string? name = ReadSerString(bytes, ref p), version = ReadSerString(bytes, ref p);
                if (name != null && version != null && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+(\.\d+){1,3}$")) return version;
            }
            // any plugin id: the name "LevelGate" followed by a version
            var nameBytes = Encoding.UTF8.GetBytes("LevelGate");
            for (int at = IndexOf(bytes, nameBytes, 0); at > 0; at = IndexOf(bytes, nameBytes, at + 1))
            {
                if (bytes[at - 1] != nameBytes.Length) continue;
                int p = at + nameBytes.Length;
                var version = ReadSerString(bytes, ref p);
                if (version != null && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+(\.\d+){1,3}$")) return version;
            }
            var info = FileVersionInfo.GetVersionInfo(dll);
            return string.IsNullOrEmpty(info.FileVersion) || info.FileVersion == "1.0.0.0" ? null : info.FileVersion;
        }
        catch { return null; }
    }

    private static string? ReadSerString(byte[] b, ref int p)
    {
        if (p >= b.Length) return null;
        int len = b[p];
        if (len == 0xFF || len >= 0x80 || p + 1 + len > b.Length) return null;
        var s = Encoding.UTF8.GetString(b, p + 1, len);
        p += 1 + len;
        return s;
    }

    private static int IndexOf(byte[] hay, byte[] needle, int from)
    {
        for (int i = from; i <= hay.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
