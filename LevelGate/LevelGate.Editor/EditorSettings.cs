using System.Text.Json;
using System.Text.Json.Nodes;
using CustomTraders.Editor;

namespace LevelGate.Editor;

/// <summary>Editor preferences, kept in %AppData%\LevelGateEditor\settings.json.</summary>
public sealed class EditorSettings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LevelGateEditor");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    /// <summary>...\BepInEx\plugins\LevelGate\config\level_requirements.json</summary>
    public string? ConfigFile { get; set; }

    /// <summary>What the page remembers (panel widths, sort, filters...). The page owns it; C# only stores it.</summary>
    public JsonObject? Ui { get; set; }

    /// <summary>Mods whose items are listed. Enabled = their items show.</summary>
    public List<ModImport> Mods { get; set; } = new();

    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath)) ?? new EditorSettings();
        }
        catch
        {
            // broken settings file: start fresh
        }
        return new EditorSettings();
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
