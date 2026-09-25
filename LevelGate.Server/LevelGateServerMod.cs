using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;

namespace LevelGate.Server;

// The server reads this record to list the mod at startup
// ("Mod: LevelGate version: ... loaded"). GUID and version match the
// BepInEx client plugin (LevelGatePlugin.PluginGuid / PluginVersion).
public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.yourname.levelgate";
    public override string Name { get; init; } = "LevelGate";
    public override string Author { get; init; } = "xhunterkim";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.1.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string? License { get; init; } = "MIT";
}

// Startup banner line, after the database has loaded. Also reports how many
// item limits the CLIENT plugin's config holds, if it can find it
// (<SPT>\BepInEx\plugins\LevelGate\config\level_requirements.json, one
// folder up from the server's SPT_Runtime folder).
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class LevelGateServerMod(ISptLogger<LevelGateServerMod> logger) : IOnLoad
{
    public Task OnLoad()
    {
        string detail = "client plugin config not found (install BepInEx/plugins/LevelGate)";
        try
        {
            var configPath = FindClientConfig();
            if (configPath != null)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                int count = doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object
                    ? items.EnumerateObject().Count()
                    : 0;
                detail = $"{count} item level limit(s) configured";
            }
        }
        catch (Exception e)
        {
            detail = "could not read client config: " + e.Message;
        }

        logger.Info($"[LevelGate] Loaded — {detail}. Level gating runs in the client plugin.");
        return Task.CompletedTask;
    }

    private static string? FindClientConfig()
    {
        var relative = Path.Combine("BepInEx", "plugins", "LevelGate", "config", "level_requirements.json");
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
