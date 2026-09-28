using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModernEditor.Editor;

/// <summary>A mod folder whose items the editor knows about.</summary>
public sealed class ModImport
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Editor preferences, kept in %AppData%\ModernEditor\settings.json. The first start takes everything over from the
/// Custom Trader Creator (%AppData%\CustomTradersEditor) and the Level &amp; Item Editor (%AppData%\LevelGateEditor):
/// folders, imported mods, background, and all that their pages remembered (tags, notes, your own orders and
/// categories, colours, column widths, folded cards…). The old files are only read, never changed.
/// </summary>
public sealed class Settings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModernEditor");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    /// <summary>%AppData% folders of the two editors Modern Editor replaces (their icon caches are reused).</summary>
    public static readonly string[] OldFolders =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomTradersEditor"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LevelGateEditor"),
    };

    /// <summary>...\SPT_Runtime\user\mods\ModernEditor (traders\, item_stats.json).</summary>
    public string? ModFolder { get; set; }

    /// <summary>...\BepInEx\plugins\LevelGate\config\level_requirements.json (Level Gate's file; its layout is never changed).</summary>
    public string? ConfigFile { get; set; }

    /// <summary>Picture shown behind the editor (any png/jpg on the PC), or null.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>What the trader pages remember (button color, column widths, panel widths, folded cards…). The page owns it.</summary>
    public JsonObject? Ui { get; set; }

    /// <summary>What the item pages (Level Limits, Item Stats, Progression) remember: tags, notes, orders, category moves…</summary>
    public JsonObject? LgUi { get; set; }

    /// <summary>Mods whose items were imported (Mods page). Enabled = their items can be picked and are listed.</summary>
    public List<ModImport> Mods { get; set; } = new();

    /// <summary>Every mod item id ever seen: id -> [mod name, item name] (the checks can still name a removed mod).</summary>
    public Dictionary<string, string[]> ModIdMemory { get; set; } = new();

    /// <summary>Add-ons page: a Level Gate file picked by hand (its DLL or level_requirements.json).</summary>
    public string? LevelGatePath { get; set; }

    /// <summary>What was taken over from the old editors (shown once on the start page).</summary>
    public List<string> Imported { get; set; } = new();

    /// <summary>Set once the old CustomTraders / ItemStatEditor mod files were moved into ModernEditor (or that was declined).</summary>
    public bool MigrationAsked { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // broken settings file: start fresh (and take the old editors' settings again)
        }
        var s = new Settings();
        s.ImportOld();
        s.Save();
        return s;
    }

    /// <summary>First start: everything the Custom Trader Creator and the Level &amp; Item Editor remembered.</summary>
    private void ImportOld()
    {
        var trader = ReadOld(Path.Combine(OldFolders[0], "settings.json"));
        if (trader != null)
        {
            ModFolder = (string?)trader["ModFolder"];
            BackgroundImage = (string?)trader["BackgroundImage"];
            LevelGatePath = (string?)trader["LevelGatePath"];
            Ui = trader["Ui"]?.DeepClone() as JsonObject;
            if (trader["ModIdMemory"] is JsonObject memory)
                foreach (var (id, v) in memory)
                    if (v is JsonArray a) ModIdMemory[id] = a.Select(x => (string?)x ?? "").ToArray();
            AddMods(trader["Mods"] as JsonArray);
            Imported.Add("Custom Trader Creator");
        }
        else if (File.Exists(Path.Combine(OldFolders[0], "modfolder.txt")))
        {
            try { ModFolder = File.ReadAllText(Path.Combine(OldFolders[0], "modfolder.txt")).Trim(); Imported.Add("Custom Trader Creator"); } catch { }
        }
        var items = ReadOld(Path.Combine(OldFolders[1], "settings.json"));
        if (items != null)
        {
            ConfigFile = (string?)items["ConfigFile"];
            LgUi = items["Ui"]?.DeepClone() as JsonObject;
            AddMods(items["Mods"] as JsonArray);
            Imported.Add("Level & Item Editor");
        }
    }

    private void AddMods(JsonArray? list)
    {
        foreach (var m in list ?? new JsonArray())
        {
            var name = (string?)m?["Name"];
            if (string.IsNullOrEmpty(name) || Mods.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            Mods.Add(new ModImport { Name = name, Folder = (string?)m?["Folder"] ?? "", Enabled = (bool?)m?["Enabled"] ?? true });
        }
    }

    private static JsonObject? ReadOld(string path)
    {
        try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.DeepClone() as JsonObject : null; }
        catch { return null; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // remembering settings is a convenience only
        }
    }
}
