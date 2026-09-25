using System.Text.Json;

namespace CustomTraders.Editor;

/// <summary>What kind of item it is, for the item picker filters and the checks.</summary>
public enum ItemCategory { Other, Weapon, Grenade, Ammo, Gear, Food, Meds, Money }

/// <summary>One game item, as shown in the item picker.</summary>
public sealed record GameItem(string Id, string Name, string ShortName, string ParentId, ItemCategory Category, string Caliber)
{
    public bool IsWeapon => Category == ItemCategory.Weapon;
    public override string ToString() => $"{Name}  [{ShortName}]";
}

/// <summary>
/// The game's item list with English names, read straight from the SPT
/// server's database (SPT_Data/database/templates/items.json and
/// locales/global/en.json), so the editor can search items by name.
/// </summary>
public sealed class ItemDatabase
{
    // Base classes in items.json (the "_parent" chain of every item ends in one of these).
    private static readonly (string Id, ItemCategory Category)[] Roots =
    {
        ("5422acb9af1c889c16000029", ItemCategory.Weapon),
        ("543be6564bdc2df4348b4568", ItemCategory.Grenade),   // ThrowWeap
        ("5485a8684bdc2da71d8b4567", ItemCategory.Ammo),
        ("543be5f84bdc2dd4348b456a", ItemCategory.Gear),      // Equipment: armor, helmets, rigs, backpacks, eyewear, face cover...
        ("57bef4c42459772e8d35a53b", ItemCategory.Gear),      // ArmoredEquipment
        ("5448e8d04bdc2ddf718b4569", ItemCategory.Food),
        ("5448e8d64bdc2dce718b4568", ItemCategory.Food),      // Drink
        ("543be5664bdc2dd4348b4569", ItemCategory.Meds),
        ("543be5dd4bdc2deb348b4569", ItemCategory.Money),
    };

    public Dictionary<string, GameItem> Items { get; } = new();

    /// <summary>English texts of the game (item, quest and trader names...).</summary>
    public Dictionary<string, string> Names { get; private set; } = new();
    public string? SourceFolder { get; private set; }

    public bool IsLoaded => Items.Count > 0;

    public bool Exists(string? tpl) => tpl != null && Items.ContainsKey(tpl);

    public GameItem? Get(string? tpl) => tpl != null && Items.TryGetValue(tpl, out var item) ? item : null;

    public string NameOf(string? tpl)
    {
        if (string.IsNullOrWhiteSpace(tpl)) return "(none)";
        if (Items.TryGetValue(tpl, out var item)) return item.Name;
        return IsLoaded ? $"(unknown {tpl})" : tpl;
    }

    /// <summary>All ammo calibers in the game ("Caliber556x45NATO"...), sorted.</summary>
    public List<string> Calibers() =>
        Items.Values.Where(i => i.Category == ItemCategory.Ammo && i.Caliber.Length > 0).Select(i => i.Caliber).Distinct().OrderBy(c => c).ToList();

    /// <summary>"Caliber556x45NATO" -> "5.56x45 NATO" style readable name.</summary>
    public static string CaliberName(string caliber)
    {
        var s = caliber.StartsWith("Caliber") ? caliber[7..] : caliber;
        // 556x45NATO -> 5.56x45 NATO, 762x39 -> 7.62x39, 9x19PARA -> 9x19 PARA, 12g -> 12g
        int x = s.IndexOf('x');
        if (x == 2 && s[..2] is "46" or "57" or "68" or "86" or "93") s = s[0] + "." + s[1..];
        else if (x == 3 && char.IsDigit(s[0])) s = s[0] + "." + s[1..];
        else if (x == 4 && char.IsDigit(s[0])) s = s[..2] + "." + s[2..];
        int tail = s.Length;
        while (tail > 0 && char.IsLetter(s[tail - 1])) tail--;
        if (tail > 0 && tail < s.Length && x > 0 && tail > x) s = s[..tail] + " " + s[tail..];
        return s;
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
        Names = names;
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
        var raw = new List<(string Id, string Parent, string Name, string Caliber)>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var item = p.Value;
            string parent = item.TryGetProperty("_parent", out var par) ? par.GetString() ?? "" : "";
            parents[p.Name] = parent;
            if (item.TryGetProperty("_type", out var type) && type.GetString() != "Item") continue;
            string internalName = item.TryGetProperty("_name", out var n) ? n.GetString() ?? p.Name : p.Name;
            // Ammo: "Caliber"; weapons: "ammoCaliber" (what they shoot).
            string caliber = "";
            if (item.TryGetProperty("_props", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "Caliber", "ammoCaliber" })
                    if (props.TryGetProperty(key, out var cal) && cal.ValueKind == JsonValueKind.String) { caliber = cal.GetString() ?? ""; break; }
            }
            raw.Add((p.Name, parent, internalName, caliber));
        }

        foreach (var (id, parent, internalName, caliber) in raw)
        {
            string name = names.TryGetValue($"{id} Name", out var nm) && !string.IsNullOrWhiteSpace(nm) ? nm : internalName;
            string shortName = names.TryGetValue($"{id} ShortName", out var sn) ? sn : "";
            Items[id] = new GameItem(id, name, shortName, parent, CategoryOf(id, parents), caliber);
        }
    }

    /// <summary>The game's own quests (id, English name, trader nickname), for "unlocked by a game quest".</summary>
    public List<(string Id, string Name, string Trader)> LoadQuests()
    {
        var result = new List<(string, string, string)>();
        if (SourceFolder == null) return result;
        var path = Path.Combine(SourceFolder, "templates", "quests.json");
        if (!File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var q in doc.RootElement.EnumerateObject())
        {
            string trader = q.Value.TryGetProperty("traderId", out var t) ? t.GetString() ?? "" : "";
            string name = Names.TryGetValue($"{q.Name} name", out var n) ? n : q.Value.TryGetProperty("QuestName", out var qn) ? qn.GetString() ?? q.Name : q.Name;
            string traderName = Names.TryGetValue($"{trader} Nickname", out var tn) ? tn : "";
            result.Add((q.Name, name, traderName));
        }
        return result.OrderBy(x => x.Item3).ThenBy(x => x.Item2).ToList();
    }

    private static ItemCategory CategoryOf(string id, Dictionary<string, string> parents)
    {
        for (int i = 0; i < 20 && parents.TryGetValue(id, out var parent) && !string.IsNullOrEmpty(parent); i++)
        {
            foreach (var (rootId, category) in Roots)
                if (parent == rootId) return category;
            id = parent;
        }
        return ItemCategory.Other;
    }
}
