using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomTraders.Editor;

/// <summary>A mod folder whose items the editor knows about.</summary>
public sealed class ModImport
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>Editor preferences, kept in %AppData%\CustomTradersEditor\settings.json.</summary>
public sealed class Settings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomTradersEditor");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public string? ModFolder { get; set; }

    /// <summary>Picture shown behind the editor (any png/jpg on the PC), or null.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>
    /// Everything the page itself remembers (button color, background darkness,
    /// column widths, panel width...). The page owns this; C# only stores it.
    /// </summary>
    public JsonObject? Ui { get; set; }

    /// <summary>Mods whose items were imported (Mods page). Enabled = their items can be picked.</summary>
    public List<ModImport> Mods { get; set; } = new();

    /// <summary>Every mod item id ever seen: id -> [mod name, item name]. Kept when an import is removed, so the checks can still say which mod an id came from.</summary>
    public Dictionary<string, string[]> ModIdMemory { get; set; } = new();

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();

            // Older editor versions only remembered the mod folder.
            var old = Path.Combine(Folder, "modfolder.txt");
            if (File.Exists(old)) return new Settings { ModFolder = File.ReadAllText(old).Trim() };
        }
        catch
        {
            // broken settings file: start fresh
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // remembering settings is a convenience only
        }
    }
}
