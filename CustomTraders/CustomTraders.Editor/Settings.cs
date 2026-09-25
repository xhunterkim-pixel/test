using System.Text.Json;

namespace CustomTraders.Editor;

/// <summary>Editor preferences, kept in %AppData%\CustomTradersEditor\settings.json.</summary>
public sealed class Settings
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomTradersEditor");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public string? ModFolder { get; set; }

    /// <summary>Picture shown behind the editor (any png/jpg on the PC), or null.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>How much the picture is darkened, 0-90 %, so text stays readable.</summary>
    public int BackgroundDim { get; set; } = 55;

    /// <summary>Button / highlight color as #RRGGBB, or null for the default green.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Column widths the user dragged, per list ("offers", "quests", "checks").</summary>
    public Dictionary<string, List<int>> ColumnWidths { get; set; } = new();

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
