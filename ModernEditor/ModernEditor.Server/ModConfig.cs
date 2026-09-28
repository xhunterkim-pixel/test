using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModernEditor.Server;

/// <summary>
/// user\mods\ModernEditor\config.json — the editor's section switches (left panel):
///   { "traders": true, "itemStats": true, "levelGate": true }
/// traders   false → no custom trader is loaded (their files stay as they are)
/// itemStats false → item_stats.json isn't applied
/// levelGate false → quests that follow an item's Level Gate level keep the level saved in the editor
///                   (Level Gate itself is a separate mod and keeps working)
/// A missing file or key means on.
/// </summary>
public sealed class ModConfig
{
    public bool Traders { get; private init; } = true;
    public bool ItemStats { get; private init; } = true;
    public bool LevelGate { get; private init; } = true;

    public static ModConfig Load(string modFolder)
    {
        var file = Path.Combine(modFolder, "config.json");
        try
        {
            if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonObject o)
            {
                var c = new ModConfig
                {
                    Traders = (bool?)o["traders"] ?? true,
                    ItemStats = (bool?)o["itemStats"] ?? true,
                    LevelGate = (bool?)o["levelGate"] ?? true,
                };
                ModLog.Detail($"[ModernEditor] config.json: traders {(c.Traders ? "on" : "OFF")}, item stats {(c.ItemStats ? "on" : "OFF")}, Level Gate levels {(c.LevelGate ? "on" : "OFF")}");
                return c;
            }
        }
        catch (Exception e) { ModLog.Detail($"[ModernEditor] config.json couldn't be read ({e.Message}) — everything stays on."); }
        return new ModConfig();
    }
}
