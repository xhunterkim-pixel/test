using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace LevelGate.Server;

// The server reads this record to list the mod at startup
// ("Mod: LevelGate version: ... loaded"). GUID and version match the
// BepInEx client plugin (LevelGatePlugin.PluginGuid / PluginVersion).
//
// SPT 4.1 API, checked against the SPTarkov.Server.Core 4.1.2 package and a
// working 4.1 server mod: metadata is the IModMetadata interface (init-only
// properties, incl. HasPrepatcher) — not the 4.0 AbstractModMetadata record.
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.yourname.levelgate";
    public string Name { get; init; } = "LevelGate";
    public string Author { get; init; } = "xhunterkim";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.1.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string? License { get; init; } = "MIT";
}

// Startup log line, after the server has finished loading. Also reports how
// many item limits the CLIENT plugin's config holds, if it can find it
// (<SPT>\BepInEx\plugins\LevelGate\config\level_requirements.json, a few
// folders up from the server's SPT_Runtime folder).
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class LevelGateServerMod(ISptLogger<LevelGateServerMod> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
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
