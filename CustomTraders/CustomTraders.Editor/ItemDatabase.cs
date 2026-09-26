using System.Text.Json;

namespace CustomTraders.Editor;

/// <summary>What kind of item it is, for the item picker filters and the checks.</summary>
public enum ItemCategory { Other, Weapon, Grenade, Ammo, Gear, Food, Meds, Money, Melee, WeaponPart, AmmoBox, Key, Barter, Container, Special, QuestItem }

/// <summary>One game item, as shown in the item picker.</summary>
public sealed record GameItem(string Id, string Name, string ShortName, string ParentId, ItemCategory Category, string Caliber)
{
    /// <summary>Offer category (ItemGroups key) and, for guns, the weapon class key.</summary>
    public string Group { get; init; } = "Other";
    public string WeaponClass { get; init; } = "";

    /// <summary>Not something a player really holds (dev/AI-only "DO NOT USE" items, pockets, stash templates...).</summary>
    public bool Hidden { get; init; }

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
        ("5447e1d04bdc2dff2f8b4567", ItemCategory.Melee),     // Knife
        ("543be5cb4bdc2deb348b4568", ItemCategory.AmmoBox),
        ("5448fe124bdc2da5018b4567", ItemCategory.WeaponPart), // Mod: magazines, scopes, barrels, plates...
        ("543be5e94bdc2df1348b4568", ItemCategory.Key),
        ("5448eb774bdc2d0a728b4567", ItemCategory.Barter),
        ("5448ecbe4bdc2d60728b4568", ItemCategory.Barter),    // Info (intelligence, diaries...)
        ("616eb7aea207f41933308f46", ItemCategory.Barter),    // repair kits
        ("5795f317245977243854e041", ItemCategory.Container),  // SimpleContainer
        ("5671435f4bdc2d96058b4569", ItemCategory.Container),  // LockableContainer
        ("5447e0e74bdc2d3c308b4567", ItemCategory.Special),    // SpecItem (markers, jammers...)
        ("567849dd4bdc2d150f8b456e", ItemCategory.Special),    // Map
    };

    public Dictionary<string, GameItem> Items { get; } = new();

    /// <summary>Handbook value in roubles (what traders base their prices on).</summary>
    public Dictionary<string, double> Handbook { get; } = new();

    /// <summary>Flea market reference price in roubles (templates/prices.json).</summary>
    public Dictionary<string, double> Flea { get; } = new();

    /// <summary>Whole default preset of a weapon (the gun with all its parts): handbook / flea value in roubles.</summary>
    public Dictionary<string, (double Handbook, double Flea)> Presets { get; } = new();

    /// <summary>
    /// Best share of the handbook value any trader pays when a player sells to them
    /// (100 - LL1 buy_price_coef, same as the server's GetHighestSellToTraderPrice).
    /// </summary>
    public double BestTraderRate { get; private set; } = 0.6;

    /// <summary>What the best-paying trader gives a player for the item, in roubles (0 = unknown).</summary>
    public double TraderSellPrice(string tpl) => Handbook.TryGetValue(tpl, out var h) ? Math.Round(h * BestTraderRate) : 0;

    /// <summary>English texts of the game (item, quest and trader names...).</summary>
    public Dictionary<string, string> Names { get; private set; } = new();
    public string? SourceFolder { get; private set; }

    public bool IsLoaded => Items.Count > 0;

    private readonly Dictionary<string, string> _parents = new();

    /// <summary>Offer category (ItemGroups key: Weapons, Ammo, Armor, Headwear, Rigs...) — nearest matching base class.</summary>
    public string GroupOf(string id, bool questItem = false)
    {
        if (questItem) return "QuestItems";
        for (int i = 0; i < 20 && _parents.TryGetValue(id, out var parent) && !string.IsNullOrEmpty(parent); i++)
        {
            foreach (var g in Shared.ItemGroups.All)
                if (g.Classes.Contains(parent)) return g.Key;
            id = parent;
        }
        return "Other";
    }

    /// <summary>Weapon class key (ItemGroups.WeaponClasses: AssaultRifle, Smg, Pistol...) or "".</summary>
    public string WeaponClassOf(string id)
    {
        for (int i = 0; i < 20 && _parents.TryGetValue(id, out var parent) && !string.IsNullOrEmpty(parent); i++)
        {
            foreach (var w in Shared.ItemGroups.WeaponClasses)
                if (w.Class == parent) return w.Key;
            id = parent;
        }
        return "";
    }

    /// <summary>Group of a modded item: the item it was cloned from, else its parent class.</summary>
    public string GroupFor(string? clonedFrom, string? parent)
    {
        if (clonedFrom != null && Items.ContainsKey(clonedFrom)) return GroupOf(clonedFrom);
        if (string.IsNullOrEmpty(parent)) return "Other";
        foreach (var g in Shared.ItemGroups.All)
            if (g.Classes.Contains(parent)) return g.Key;
        return GroupOf(parent);
    }

    /// <summary>Category of a modded item: the item it was cloned from, else its parent class.</summary>
    public ItemCategory CategoryFor(string? clonedFrom, string? parent)
    {
        if (clonedFrom != null && Items.TryGetValue(clonedFrom, out var source)) return source.Category;
        if (string.IsNullOrEmpty(parent)) return ItemCategory.Other;
        foreach (var (rootId, category) in Roots)
            if (parent == rootId) return category;
        return CategoryOf(parent, _parents);
    }

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
        var parents = _parents;
        parents.Clear();
        var raw = new List<(string Id, string Parent, string Name, string Caliber, bool Quest)>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var item = p.Value;
            string parent = item.TryGetProperty("_parent", out var par) ? par.GetString() ?? "" : "";
            parents[p.Name] = parent;
            if (item.TryGetProperty("_type", out var type) && type.GetString() != "Item") continue;
            string internalName = item.TryGetProperty("_name", out var n) ? n.GetString() ?? p.Name : p.Name;
            // Ammo: "Caliber"; weapons: "ammoCaliber" (what they shoot).
            string caliber = "";
            bool quest = false;
            if (item.TryGetProperty("_props", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                quest = props.TryGetProperty("QuestItem", out var q) && q.ValueKind == JsonValueKind.True;
                foreach (var key in new[] { "Caliber", "ammoCaliber" })
                    if (props.TryGetProperty(key, out var cal) && cal.ValueKind == JsonValueKind.String) { caliber = cal.GetString() ?? ""; break; }
            }
            raw.Add((p.Name, parent, internalName, caliber, quest));
        }

        LoadPrices(databaseFolder);
        foreach (var (id, parent, internalName, caliber, quest) in raw)
        {
            string name = names.TryGetValue($"{id} Name", out var nm) && !string.IsNullOrWhiteSpace(nm) ? nm : internalName;
            string shortName = names.TryGetValue($"{id} ShortName", out var sn) ? sn : "";
            var category = quest ? ItemCategory.QuestItem : CategoryOf(id, parents);
            // What players can hold is what the handbook lists (plus quest items); the rest are templates and dev items.
            bool hidden = name.Contains("DO_NOT_USE", StringComparison.OrdinalIgnoreCase) || name.Contains("DO NOT USE", StringComparison.OrdinalIgnoreCase) ||
                          (Handbook.Count > 0 && !quest && !Handbook.ContainsKey(id));
            Items[id] = new GameItem(id, name, shortName, parent, category, caliber)
            {
                Hidden = hidden, Group = GroupOf(id, quest), WeaponClass = category == ItemCategory.Weapon ? WeaponClassOf(id) : "",
            };
        }
    }

    private void LoadPrices(string databaseFolder)
    {
        Handbook.Clear();
        Flea.Clear();
        Presets.Clear();
        try
        {
            var handbook = Path.Combine(databaseFolder, "templates", "handbook.json");
            if (File.Exists(handbook))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(handbook));
                if (doc.RootElement.TryGetProperty("Items", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var e in list.EnumerateArray())
                        if (e.TryGetProperty("Id", out var id) && e.TryGetProperty("Price", out var price) && price.ValueKind == JsonValueKind.Number)
                            Handbook[id.GetString() ?? ""] = price.GetDouble();
            }
            var prices = Path.Combine(databaseFolder, "templates", "prices.json");
            if (File.Exists(prices))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(prices));
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number) Flea[p.Name] = p.Value.GetDouble();
            }
            double best = 0;
            var traders = Path.Combine(databaseFolder, "traders");
            if (Directory.Exists(traders))
                foreach (var dir in Directory.GetDirectories(traders))
                {
                    var file = Path.Combine(dir, "base.json");
                    if (!File.Exists(file) || Path.GetFileName(dir).Equals("ragfair", StringComparison.OrdinalIgnoreCase)) continue; // the flea market isn't a trader
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
                    if (!doc.RootElement.TryGetProperty("loyaltyLevels", out var levels) || levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() == 0) continue;
                    if (!levels[0].TryGetProperty("buy_price_coef", out var coef) || coef.ValueKind != JsonValueKind.Number) continue;
                    // traders that buy nothing (empty items_buy) or "pay 100%" placeholders don't count
                    bool buys = doc.RootElement.TryGetProperty("items_buy", out var ib) && ib.TryGetProperty("category", out var cats) && cats.ValueKind == JsonValueKind.Array && cats.GetArrayLength() > 0;
                    if (!buys || coef.GetDouble() <= 0) continue;
                    best = Math.Max(best, (100 - coef.GetDouble()) / 100);
                }
            if (best > 0) BestTraderRate = Math.Min(1, best);

            // default presets (what an offer with "sell the assembled gun" really sells)
            var globals = Path.Combine(databaseFolder, "globals.json");
            if (File.Exists(globals) && Handbook.Count > 0)
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(globals));
                if (doc.RootElement.TryGetProperty("ItemPresets", out var presets) && presets.ValueKind == JsonValueKind.Object)
                    foreach (var p in presets.EnumerateObject())
                    {
                        if (!p.Value.TryGetProperty("_encyclopedia", out var enc) || enc.ValueKind != JsonValueKind.String) continue;
                        if (!p.Value.TryGetProperty("_items", out var parts) || parts.ValueKind != JsonValueKind.Array) continue;
                        double hb = 0, flea = 0;
                        foreach (var part in parts.EnumerateArray())
                        {
                            string tpl = part.TryGetProperty("_tpl", out var t) ? t.GetString() ?? "" : "";
                            double h = Handbook.TryGetValue(tpl, out var hv) ? hv : 0;
                            hb += h;
                            flea += Flea.TryGetValue(tpl, out var fv) ? fv : h;
                        }
                        if (hb > 0) Presets[enc.GetString()!] = (hb, flea);
                    }
            }
        }
        catch
        {
            // Prices are only a convenience for the editor.
        }
    }

    /// <summary>The game's own quests (id, English name, trader nickname), for "unlocked by a game quest".</summary>
    public List<(string Id, string Name, string Trader, string TraderId)> LoadQuests()
    {
        var result = new List<(string, string, string, string)>();
        if (SourceFolder == null) return result;
        var path = Path.Combine(SourceFolder, "templates", "quests.json");
        if (!File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var q in doc.RootElement.EnumerateObject())
        {
            string trader = q.Value.TryGetProperty("traderId", out var t) ? t.GetString() ?? "" : "";
            string name = Names.TryGetValue($"{q.Name} name", out var n) ? n : q.Value.TryGetProperty("QuestName", out var qn) ? qn.GetString() ?? q.Name : q.Name;
            string traderName = Names.TryGetValue($"{trader} Nickname", out var tn) ? tn : "";
            result.Add((q.Name, name, traderName, trader));
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
