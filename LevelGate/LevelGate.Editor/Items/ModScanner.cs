using System.Text.Json;
using System.Text.RegularExpressions;

namespace LevelGate.Editor;

/// <summary>An item added by another mod (found in its json files).</summary>
public sealed record ModItem(string Id, string Name, string ShortName, ItemCategory Category, string Caliber, double Handbook)
{
    public string Group { get; init; } = "Other";
    public string WeaponClass { get; init; } = "";
}

/// <summary>
/// Finds the items a mod adds by reading its json files: item definitions
/// (WTT / CustomItemService style with "itemTplToClone" + "locales", or
/// items.json style with "_parent" + "_props") and locale files (en.json with
/// "&lt;id&gt; Name" / "&lt;id&gt; ShortName"). Nothing is changed in the mod.
/// </summary>
public static class ModScanner
{
    private static readonly Regex HexId = new("^[0-9a-fA-F]{24}$", RegexOptions.Compiled);
    private static readonly Regex LocaleKey = new("^([0-9a-fA-F]{24}) (Name|ShortName)$", RegexOptions.Compiled);
    private static readonly Regex OtherLanguage = new("^(?!en$)[a-z]{2}(-[a-z]{2})?$", RegexOptions.Compiled); // ru, fr, es-mx... (English wins)
    private const int MaxFiles = 6000;
    private const long MaxFileBytes = 40L * 1024 * 1024;

    private sealed class Raw
    {
        public string? Name, ShortName, Internal, Parent, Clone, Caliber;
        public double? Handbook;
    }

    public static List<ModItem> Scan(string folder, ItemDatabase db)
    {
        var raws = new Dictionary<string, Raw>(StringComparer.OrdinalIgnoreCase);
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        int files = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            if (++files > MaxFiles) break;
            if (path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")) continue;
            try
            {
                if (new FileInfo(path).Length > MaxFileBytes) continue;
                if (OtherLanguage.IsMatch(Path.GetFileNameWithoutExtension(path).ToLowerInvariant())) continue;
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path), options);
                Visit(doc.RootElement, raws, 0, Path.GetFileNameWithoutExtension(path).Equals("en", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // not json we understand (config, package.json with comments...) — skip
            }
        }

        var result = new List<ModItem>();
        foreach (var (id, r) in raws)
        {
            var key = id.ToLowerInvariant();
            if (db.Items.ContainsKey(key)) continue; // a vanilla item the mod only changes
            if (r.Name == null && r.Internal == null && r.Parent == null && r.Clone == null) continue;
            var clone = r.Clone != null ? db.Get(r.Clone) : null;
            string name = r.Name ?? r.Internal ?? (clone != null ? clone.Name + " (modded)" : key);
            double hb = r.Handbook ?? (r.Clone != null && db.Handbook.TryGetValue(r.Clone, out var h) ? h : 0);
            var category = db.CategoryFor(r.Clone, r.Parent);
            result.Add(new ModItem(key, name, r.ShortName ?? "", category, r.Caliber ?? clone?.Caliber ?? "", hb)
            {
                Group = db.GroupFor(r.Clone, r.Parent),
                WeaponClass = category != ItemCategory.Weapon ? "" : clone?.WeaponClass ?? ItemGroups.WeaponClasses.FirstOrDefault(w => w.Class == r.Parent).Key ?? "",
            });
        }
        return result.OrderBy(i => i.Name).ToList();
    }

    private static Raw Get(Dictionary<string, Raw> raws, string id) => raws.TryGetValue(id, out var r) ? r : raws[id] = new Raw();

    private static string? Str(JsonElement e, params string[] keys)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            foreach (var k in keys)
                if (p.Name.Equals(k, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
        return null;
    }

    private static JsonElement? Obj(JsonElement e, string key)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object) return p.Value;
        return null;
    }

    private static double? Num(JsonElement e, string key)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number) return p.Value.GetDouble();
        return null;
    }

    /// <summary>WTT / CustomItemService item: clone + overrides + locales.</summary>
    private static void AddCloneStyle(Dictionary<string, Raw> raws, string id, JsonElement def)
    {
        var r = Get(raws, id);
        r.Clone ??= Str(def, "itemTplToClone");
        r.Parent ??= Str(def, "parentId");
        r.Handbook ??= Num(def, "handbookPriceRoubles");
        if (Obj(def, "overrideProperties") is { } over) r.Caliber ??= Str(over, "Caliber", "ammoCaliber");
        if (Obj(def, "locales") is { } locales)
        {
            var en = Obj(locales, "en") ?? (locales.EnumerateObject().FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Object).Value is { ValueKind: JsonValueKind.Object } any ? any : null);
            if (en is { } l)
            {
                r.Name ??= Str(l, "name");
                r.ShortName ??= Str(l, "shortName");
            }
        }
    }

    private static void Visit(JsonElement e, Dictionary<string, Raw> raws, int depth, bool localeFile)
    {
        if (depth > 6) return;
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) if (x.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Visit(x, raws, depth + 1, localeFile);
            return;
        }
        if (e.ValueKind != JsonValueKind.Object) return;

        // a single item per file: { "newId": "...", "itemTplToClone": ..., "locales": ... }
        if (Str(e, "newId") is { } newId && HexId.IsMatch(newId)) { AddCloneStyle(raws, newId, e); return; }

        foreach (var p in e.EnumerateObject())
        {
            var m = LocaleKey.Match(p.Name);
            if (m.Success && p.Value.ValueKind == JsonValueKind.String)
            {
                var r = Get(raws, m.Groups[1].Value);
                if (m.Groups[2].Value == "Name") r.Name ??= p.Value.GetString(); else r.ShortName ??= p.Value.GetString();
                continue;
            }
            if (p.Value.ValueKind != JsonValueKind.Object) { if (p.Value.ValueKind == JsonValueKind.Array) Visit(p.Value, raws, depth + 1, localeFile); continue; }
            var v = p.Value;
            if (HexId.IsMatch(p.Name))
            {
                if (Obj(v, "_props") is { } props || Str(v, "_parent") != null)
                {
                    var r = Get(raws, p.Name);
                    r.Parent ??= Str(v, "_parent");
                    r.Internal ??= Str(v, "_name");
                    if (Obj(v, "_props") is { } pr) r.Caliber ??= Str(pr, "Caliber", "ammoCaliber");
                    continue;
                }
                if (Str(v, "itemTplToClone") != null || Obj(v, "overrideProperties") != null || Obj(v, "locales") != null)
                {
                    AddCloneStyle(raws, p.Name, v);
                    continue;
                }
                if (Str(v, "Name") is { } name && (localeFile || Str(v, "ShortName") != null))
                {
                    var r = Get(raws, p.Name);
                    r.Name ??= name;
                    r.ShortName ??= Str(v, "ShortName");
                    continue;
                }
            }
            // locale files grouped by language ({ "en": { "<id> Name": ... } }) or other nesting
            if (p.Name.Equals("en", StringComparison.OrdinalIgnoreCase)) Visit(v, raws, depth + 1, true);
            else if (OtherLanguage.IsMatch(p.Name)) continue;
            else if (depth < 3) Visit(v, raws, depth + 1, localeFile);
        }
    }
}
