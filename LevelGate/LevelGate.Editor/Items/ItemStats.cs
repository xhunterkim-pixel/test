using System.Text.Json;
using System.Text.Json.Nodes;

namespace LevelGate.Editor;

/// <summary>
/// Item stats for the editor, read from the SPT database (templates\items.json, globals.json):
///   Show[id]  — what the lists show: ammo pen / damage, armor class, grid slots...
///   Meds[id]  — meds, stims and food in the editable shape (the BalancedMeds config shape):
///               medUseTime, MaxHpResource, hpResourceRate, foodUseTime, MaxResource,
///               effects_health { Energy: { value } }, effects_damage { Pain: { duration, ... } },
///               buffName + effects_buffs [ { BuffType, Value, Duration, ... } ].
/// Other server mods that change these (Item Property Backport, BalancedMeds) are layered on top,
/// so what's shown is what the game gets before LevelGate's own edits.
/// </summary>
public sealed class ItemStats
{
    // base classes
    private const string Ammo = "5485a8684bdc2da71d8b4567", AmmoBox = "543be5cb4bdc2deb348b4568";
    private const string Medkit = "5448f39d4bdc2d0a728b4568", Medical = "5448f3ac4bdc2dce718b4569", Drugs = "5448f3a14bdc2d27728b4569", Stimulator = "5448f3a64bdc2d60728b456a";
    private const string Food = "5448e8d04bdc2ddf718b4569", Drink = "5448e8d64bdc2dce718b4568";

    public Dictionary<string, JsonObject> Show { get; } = new();
    public Dictionary<string, JsonObject> Meds { get; } = new();
    /// <summary>Meds / food exactly as the SPT database has them (Escape From Tarkov's values, before any mod).</summary>
    public Dictionary<string, JsonObject> Defaults { get; } = new();
    /// <summary>Which other mods' changes were found (shown in the editor).</summary>
    public List<string> Sources { get; } = new();

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string? MedKind(ItemDatabase db, string id) =>
        db.HasAncestor(id, Medkit) ? "medkit" : db.HasAncestor(id, Stimulator) ? "stim" : db.HasAncestor(id, Drugs) ? "drug" :
        db.HasAncestor(id, Medical) ? "medical" : db.HasAncestor(id, Food) || db.HasAncestor(id, Drink) ? "food" : null;

    public void Load(string databaseFolder, ItemDatabase db, string? modsRoot)
    {
        Show.Clear(); Meds.Clear(); Defaults.Clear(); Sources.Clear();
        var buffs = LoadBuffs(Path.Combine(databaseFolder, "globals.json"));
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(databaseFolder, "templates", "items.json")), documentOptions: Lenient) as JsonObject;
        if (root == null) return;
        var props = new Dictionary<string, JsonObject>();
        foreach (var (id, node) in root)
            if (node?["_props"] is JsonObject p) props[id] = p;

        foreach (var (id, p) in props)
        {
            if (!db.Items.ContainsKey(id)) continue;
            var show = ShowOf(p, db.HasAncestor(id, Ammo), db.HasAncestor(id, AmmoBox), props);
            var kind = MedKind(db, id);
            if (kind != null) { Meds[id] = MedBase(p, kind, buffs); Defaults[id] = (JsonObject)Meds[id].DeepClone(); }
            if (show.Count > 0) Show[id] = show;
        }
        _props = props; _buffs = buffs; _db = db;
        if (modsRoot != null && Directory.Exists(modsRoot))
        {
            ApplyItemPropertyBackport(modsRoot, db);
            ApplyBalancedMeds(modsRoot);
        }
    }

    // ------------------------------------------------------------------ vanilla values

    private Dictionary<string, JsonObject> _props = new();
    private Dictionary<string, JsonArray> _buffs = new();
    private ItemDatabase? _db;

    /// <summary>List stats of one item from its _props.</summary>
    private static JsonObject ShowOf(JsonObject p, bool ammo, bool ammoBox, Dictionary<string, JsonObject> props)
    {
        var show = new JsonObject();
        if (ammo)
        {
            Num(show, "pen", p["PenetrationPower"]); Num(show, "dmg", p["Damage"]); Num(show, "ad", p["ArmorDamage"]);
            Num(show, "v", p["InitialSpeed"]); Num(show, "frag", p["FragmentationChance"]);
            if (D(p["ProjectileCount"]) is > 1 and var pc) show["pc"] = pc;
        }
        // armor class: the item's own, or the best of its default plates / soft inserts
        int ac = I(p["armorClass"]);
        if (p["Slots"] is JsonArray slots)
            foreach (var slot in slots)
                if (slot?["_props"]?["filters"] is JsonArray filters)
                    foreach (var f in filters)
                        if ((string?)f?["Plate"] is { Length: > 0 } plate && props.TryGetValue(plate, out var pp)) ac = Math.Max(ac, I(pp["armorClass"]));
        if (ac > 0) show["ac"] = ac;
        if (D(p["MaxDurability"]) is > 0 and var dur && ac > 0) show["dur"] = dur;
        if (p["Grids"] is JsonArray grids && grids.Count > 0)
        {
            int cells = 0;
            foreach (var g in grids) cells += I(g?["_props"]?["cellsH"]) * I(g?["_props"]?["cellsV"]);
            if (cells > 0) show["slots"] = cells;
        }
        // ammo packs: what's inside ("20 × M855")
        if (ammoBox && p["StackSlots"] is JsonArray stack && stack.Count > 0)
        {
            var slot = stack[0];
            string? inside = (string?)slot?["_props"]?["filters"]?[0]?["Filter"]?[0];
            int count = I(slot?["_max_count"]);
            if (inside != null) { show["of"] = inside; if (count > 0) show["pack"] = count; }
        }
        return show;
    }

    /// <summary>
    /// Items added by other mods: their stats from what the mod says (its _props, or the cloned
    /// item with the mod's overrideProperties). The mod's values count as their "default".
    /// </summary>
    public void AddModItems(IEnumerable<ModItem> items)
    {
        if (_db == null) return;
        foreach (var it in items)
        {
            if (_db.Items.ContainsKey(it.Id)) continue;
            JsonObject p;
            if (it.CloneOf != null && _props.TryGetValue(it.CloneOf, out var baseProps))
            {
                p = (JsonObject)baseProps.DeepClone();
                if (it.Props != null) foreach (var (k, v) in it.Props) p[k] = v?.DeepClone();
            }
            else if (it.Props != null) p = (JsonObject)it.Props.DeepClone();
            else continue;
            // kind: from the cloned item, or the parent class
            string anchor = it.CloneOf ?? it.Parent ?? "";
            bool Is(string cls) => anchor == cls || _db.HasAncestor(anchor, cls) || it.Parent == cls || (it.Parent != null && _db.HasAncestor(it.Parent, cls));
            var show = ShowOf(p, Is(Ammo), Is(AmmoBox), _props);
            if (show.Count > 0) Show[it.Id] = show;
            string? kind = Is(Medkit) ? "medkit" : Is(Stimulator) ? "stim" : Is(Drugs) ? "drug" : Is(Medical) ? "medical" : Is(Food) || Is(Drink) ? "food" : null;
            if (kind == null) continue;
            Meds[it.Id] = MedBase(p, kind, _buffs);
            Defaults[it.Id] = (JsonObject)Meds[it.Id].DeepClone();
        }
    }

    private static Dictionary<string, JsonArray> LoadBuffs(string globalsPath)
    {
        var result = new Dictionary<string, JsonArray>();
        try
        {
            if (!File.Exists(globalsPath)) return result;
            var g = JsonNode.Parse(File.ReadAllText(globalsPath), documentOptions: Lenient);
            if (g?["config"]?["Health"]?["Effects"]?["Stimulator"]?["Buffs"] is JsonObject all)
                foreach (var (name, list) in all)
                    if (list is JsonArray arr) result[name] = (JsonArray)arr.DeepClone();
        }
        catch { /* no buff details */ }
        return result;
    }

    private static JsonObject MedBase(JsonObject p, string kind, Dictionary<string, JsonArray> buffs)
    {
        var m = new JsonObject { ["kind"] = kind };
        foreach (var key in new[] { "medUseTime", "MaxHpResource", "hpResourceRate", "foodUseTime", "MaxResource" })
            if (p[key] is JsonValue v && D(v) is double d) m[key] = d;
        m["effects_health"] = Dict(p["effects_health"]);
        m["effects_damage"] = Dict(p["effects_damage"]);
        string buffName = (string?)p["StimulatorBuffs"] ?? "";
        m["buffName"] = buffName;
        m["effects_buffs"] = buffName.Length > 0 && buffs.TryGetValue(buffName, out var list) ? list.DeepClone() : new JsonArray();
        return m;
    }

    /// <summary>{ Energy: { value: 10 } } — an empty list ([]) in the files means "none".</summary>
    private static JsonObject Dict(JsonNode? node) => node is JsonObject o ? (JsonObject)o.DeepClone() : new JsonObject();

    // ------------------------------------------------------------------ other mods

    /// <summary>Item Property Backport: items.json { tpl: { properties: {...}, special_properties: { EffectsHealth, EffectsDamage, Buffs } } }.</summary>
    private void ApplyItemPropertyBackport(string modsRoot, ItemDatabase db)
    {
        string? file = null;
        try
        {
            foreach (var dir in Directory.GetDirectories(modsRoot))
            {
                bool isIpb = Path.GetFileName(dir).Contains("PropertyBackport", StringComparison.OrdinalIgnoreCase) ||
                             Directory.EnumerateFiles(dir, "*PropertyBackport*.dll", SearchOption.TopDirectoryOnly).Any();
                if (!isIpb) continue;
                file = Directory.EnumerateFiles(dir, "items.json", SearchOption.AllDirectories).FirstOrDefault();
                if (file != null) break;
            }
            if (file == null || JsonNode.Parse(File.ReadAllText(file), documentOptions: Lenient) is not JsonObject all) return;
            int changed = 0;
            foreach (var (id, entry) in all)
            {
                if (entry is not JsonObject e || !db.Items.ContainsKey(id)) continue;
                if (e["properties"] is JsonObject pr)
                {
                    // only stats the item already shows (ammo pen, armor durability...), plus armor class
                    var shown = Show.TryGetValue(id, out var existing) ? existing : new JsonObject();
                    foreach (var (k, key) in new[] { ("PenetrationPower", "pen"), ("Damage", "dmg"), ("ArmorDamage", "ad"), ("InitialSpeed", "v"), ("FragmentationChance", "frag"), ("MaxDurability", "dur") })
                        if (shown.ContainsKey(key) && pr[k] is JsonValue v && D(v) is double d) shown[key] = Math.Round(d, 2);
                    if (pr["armorClass"] is JsonValue acv && I(acv) > 0) shown["ac"] = I(acv);
                    if (shown.Count > 0) Show[id] = shown;
                    if (Meds.TryGetValue(id, out var m))
                        foreach (var key in new[] { "MaxHpResource", "MaxResource", "medUseTime", "hpResourceRate", "foodUseTime" })
                            if (pr[key] is JsonValue v && D(v) is double d) m[key] = d;
                }
                if (e["special_properties"] is JsonObject sp && Meds.TryGetValue(id, out var med))
                {
                    Merge(med, "effects_health", sp["EffectsHealth"]);
                    Merge(med, "effects_damage", sp["EffectsDamage"]);
                    if (sp["Buffs"] is JsonObject ib && med["effects_buffs"] is JsonArray list)
                        foreach (var (index, buff) in ib)
                            if (int.TryParse(index, out int i) && buff is JsonObject b)
                            {
                                while (list.Count <= i) list.Add(new JsonObject());
                                list[i] = b.DeepClone();
                            }
                }
                changed++;
            }
            Sources.Add($"Item Property Backport ({changed} items)");
        }
        catch { /* unreadable: vanilla values are shown */ }
    }

    /// <summary>{ Hydration: null } removes, { Energy: {...} } sets.</summary>
    private static void Merge(JsonObject med, string key, JsonNode? changes)
    {
        if (changes is not JsonObject c) return;
        if (med[key] is not JsonObject target) { target = new JsonObject(); med[key] = target; }
        foreach (var (k, v) in c)
        {
            if (v == null) target.Remove(k);
            else target[k] = v.DeepClone();
        }
    }

    /// <summary>BalancedMeds: user\mods\BalancedMeds\config\{drugs,medicals,medkits,stimulators}.json, same shape as ours.</summary>
    private void ApplyBalancedMeds(string modsRoot)
    {
        var config = Path.Combine(modsRoot, "BalancedMeds", "config");
        if (!Directory.Exists(config)) return;
        int changed = 0;
        foreach (var name in new[] { "drugs", "medicals", "medkits", "stimulators" })
        {
            try
            {
                var file = Path.Combine(config, name + ".json");
                if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file), documentOptions: Lenient) is not JsonObject all) continue;
                foreach (var (id, entry) in all)
                {
                    if (entry is not JsonObject e || !Meds.TryGetValue(id, out var m)) continue;
                    ApplyOverride(m, e);
                    changed++;
                }
            }
            catch { /* skip that file */ }
        }
        if (changed > 0) Sources.Add($"BalancedMeds ({changed} items)");
    }

    /// <summary>Fields present in the override replace the item's (the effects lists as a whole).</summary>
    public static void ApplyOverride(JsonObject m, JsonObject e)
    {
        foreach (var key in new[] { "medUseTime", "MaxHpResource", "hpResourceRate", "foodUseTime", "MaxResource" })
            if (e[key] is JsonValue v && D(v) is double d) m[key] = d;
        if (e.ContainsKey("effects_health")) m["effects_health"] = Dict(e["effects_health"]);
        if (e.ContainsKey("effects_damage")) m["effects_damage"] = Dict(e["effects_damage"]);
        if ((string?)e["buffName"] is { Length: > 0 } buffName) m["buffName"] = buffName;
        if (e["effects_buffs"] is JsonArray list) m["effects_buffs"] = list.DeepClone();
    }

    // ------------------------------------------------------------------ helpers

    private static void Num(JsonObject o, string key, JsonNode? v) { if (D(v) is double d) o[key] = Math.Round(d, 2); }

    private static double? D(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue(out double d)) return d;
        if (v.TryGetValue(out string? s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    private static int I(JsonNode? n) => D(n) is double d ? (int)d : 0;
}
