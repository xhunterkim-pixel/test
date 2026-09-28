using System.Text.Json;
using ModernEditor.Shared;
using SPTarkov.Common.Models.Logging;

namespace ModernEditor.Server;

/// <summary>
/// Quests whose level follows an item's Level Gate unlock level (QuestDef.LevelFromItem): at server start their
/// MinLevel becomes that item's level + LevelOffset, read from Level Gate's own config
/// (&lt;SPT&gt;\BepInEx\plugins\LevelGate\config\level_requirements.json — only read, never written). An item
/// without a limit counts as level 1. Without Level Gate the level saved by the editor is kept.
/// </summary>
public static class DynamicLevels
{
    public static void Apply<T>(IEnumerable<TraderFile> traders, string modFolder, ISptLogger<T> logger)
    {
        var quests = traders.SelectMany(t => t.Quests.Select(q => (Trader: t, Quest: q)))
            .Where(x => Ids.IsValid(x.Quest.LevelFromItem)).ToList();
        if (quests.Count == 0) return;
        var config = FindLevelGateConfig(modFolder);
        if (config == null)
        {
            logger.Warning($"[ModernEditor] {quests.Count} quest(s) follow an item's Level Gate level, but Level Gate's level_requirements.json " +
                           "wasn't found — they keep the level saved in the editor.");
            return;
        }
        Dictionary<string, int> levels;
        try { levels = ReadLevels(config); }
        catch (Exception e)
        {
            logger.Warning($"[ModernEditor] Level Gate's levels couldn't be read ({e.Message}) — dynamic quests keep the level saved in the editor.");
            return;
        }
        int moved = 0;
        foreach (var (trader, quest) in quests)
        {
            int itemLevel = levels.TryGetValue(quest.LevelFromItem!, out var l) ? l : 1;
            int level = Math.Clamp(itemLevel + quest.LevelOffset, 1, 79);
            if (level != quest.MinLevel)
            {
                logger.Info($"[ModernEditor]   {trader.Name} › {quest.Name}: level {quest.MinLevel} → {level} (follows its item, Level Gate level {itemLevel}" +
                            (quest.LevelOffset != 0 ? $" {quest.LevelOffset:+#;-#}" : "") + ")");
                moved++;
            }
            quest.MinLevel = level;
        }
        logger.Info($"[ModernEditor] Dynamic levels: {quests.Count} quest(s) follow their item's Level Gate level ({moved} moved since the last save).");
    }

    /// <summary>{ "items": { "&lt;tpl&gt;": level } } — numbers or numeric strings.</summary>
    public static Dictionary<string, int> ReadLevels(string path)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in items.EnumerateObject())
        {
            int level = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n) ? n
                : p.Value.ValueKind == JsonValueKind.String && int.TryParse(p.Value.GetString(), out var m) ? m : 0;
            if (level > 0) result[p.Name] = level;
        }
        return result;
    }

    /// <summary>Level Gate's config above the mod folder / the server's folder (C:\SPT\SPT_Runtime\user\mods\ModernEditor → C:\SPT\BepInEx\…).</summary>
    public static string? FindLevelGateConfig(string modFolder)
    {
        var relative = Path.Combine("BepInEx", "plugins", "LevelGate", "config", "level_requirements.json");
        foreach (var start in new[] { modFolder, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;
            var dir = new DirectoryInfo(start);
            for (int i = 0; i < 7 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

/// <summary>The old separate server mods Modern Editor replaces: loading both would add every trader twice.</summary>
public static class LegacyMods
{
    public static void Warn<T>(string modFolder, ISptLogger<T> logger)
    {
        var mods = Directory.GetParent(modFolder)?.FullName;
        if (mods == null) return;
        foreach (var (folder, dll) in new[] { ("CustomTraders", "CustomTraders.dll"), ("ItemStatEditor", "ItemStatEditor.Server.dll") })
            if (File.Exists(Path.Combine(mods, folder, dll)))
                logger.Warning($"[ModernEditor] The old {folder} mod is still installed (user\\mods\\{folder}) — Modern Editor replaces it. " +
                               $"Open ModernEditor.exe once (it moves your files over) or delete that folder, or things load twice.");
    }
}
