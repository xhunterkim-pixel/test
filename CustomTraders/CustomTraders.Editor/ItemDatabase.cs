using System.Text.Json;

namespace CustomTraders.Editor;

/// <summary>One game item, as shown in the item picker.</summary>
public sealed record GameItem(string Id, string Name, string ShortName, string ParentId, bool IsWeapon)
{
    public override string ToString() => $"{Name}  [{ShortName}]";
}

/// <summary>
/// The game's item list with English names, read straight from the SPT
/// server's database (SPT_Data/database/templates/items.json and
/// locales/global/en.json), so the editor can search items by name.
/// </summary>
public sealed class ItemDatabase
{
    private const string WeaponRootId = "5422acb9af1c889c16000029";

    public Dictionary<string, GameItem> Items { get; } = new();
    public string? SourceFolder { get; private set; }

    public bool IsLoaded => Items.Count > 0;

    public string NameOf(string? tpl)
    {
        if (string.IsNullOrWhiteSpace(tpl)) return "(none)";
        return Items.TryGetValue(tpl, out var item) ? item.Name : $"(unknown {tpl})";
    }

    /// <summary>
    /// Finds the database from a mod folder like
    /// ...\SPT_Runtime\user\mods\CustomTraders (walks up looking for SPT_Data).
    /// </summary>
    public static string? FindDatabaseFolder(string modFolder)
    {
        var dir = new DirectoryInfo(modFolder);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir.FullName, "SPT_Data", "database"),
                         Path.Combine(dir.FullName, "SPT_Data", "Server", "database"),
                         Path.Combine(dir.FullName, "SPT_Runtime", "SPT_Data", "database"),
                     })
            {
                if (File.Exists(Path.Combine(candidate, "templates", "items.json"))) return candidate;
            }
        }
        return null;
    }

    public void Load(string databaseFolder)
    {
        Items.Clear();
        SourceFolder = databaseFolder;

        var names = new Dictionary<string, string>();
        var localePath = Path.Combine(databaseFolder, "locales", "global", "en.json");
        if (File.Exists(localePath))
        {
            using var localeDoc = JsonDocument.Parse(File.ReadAllBytes(localePath));
            foreach (var p in localeDoc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String) names[p.Name] = p.Value.GetString() ?? "";
            }
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(databaseFolder, "templates", "items.json")));
        var parents = new Dictionary<string, string>();
        var raw = new List<(string Id, string Parent, string Name)>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var item = p.Value;
            string parent = item.TryGetProperty("_parent", out var par) ? par.GetString() ?? "" : "";
            parents[p.Name] = parent;
            if (item.TryGetProperty("_type", out var type) && type.GetString() != "Item") continue;
            string internalName = item.TryGetProperty("_name", out var n) ? n.GetString() ?? p.Name : p.Name;
            raw.Add((p.Name, parent, internalName));
        }

        foreach (var (id, parent, internalName) in raw)
        {
            string name = names.TryGetValue($"{id} Name", out var nm) && !string.IsNullOrWhiteSpace(nm) ? nm : internalName;
            string shortName = names.TryGetValue($"{id} ShortName", out var sn) ? sn : "";
            Items[id] = new GameItem(id, name, shortName, parent, HasAncestor(id, WeaponRootId, parents));
        }
    }

    private static bool HasAncestor(string id, string ancestor, Dictionary<string, string> parents)
    {
        for (int i = 0; i < 20 && parents.TryGetValue(id, out var parent) && !string.IsNullOrEmpty(parent); i++)
        {
            if (parent == ancestor) return true;
            id = parent;
        }
        return false;
    }
}
