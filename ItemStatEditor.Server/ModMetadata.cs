using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ItemStatEditor.Server;

// Lists the mod at server startup. It's separate from LevelGate: it only
// applies the item stat edits (meds, stims, food) made in the LevelGate
// Editor's Item Stats tab.
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.kkyangg.itemstateditor";
    public string Name { get; init; } = "ItemStatEditor";
    public string Author { get; init; } = "k_kyangg";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}
