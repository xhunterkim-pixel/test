using System.Text.Json;
using Path = System.IO.Path;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace LevelGate.Progression.Server;

public record ModMetadata : SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata
{
    public string ModGuid { get; init; } = "com.xhunterkim.levelgateprogression";
    public string Name { get; init; } = "LevelGateProgression";
    public string Author { get; init; } = "xhunterkim";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(RankTags.ModVersion);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

/// <summary>
/// Rank Tags: one item per rank (the 16 emblems of the Progression screen), each with its own id. They are dogtags (worn
/// in the Dogtag slot, listed in its "Available" list) shaped like a coin: the game's Physical Bitcoin model, which the
/// Progression plugin re-skins in game with the rank's animated emblem. Each is mailed once, when its rank is reached
/// (players already past a rank get theirs on the next server check). Sent ones are remembered in sent.json.
/// </summary>
public static class RankTags
{
    public const string ModVersion = "1.0.0";

    /// <summary>The ranks: the level each starts at, and its name (same as the Progression screen).</summary>
    public static readonly (int From, string Name)[] Ranks =
    {
        (1, "Scavenger"), (6, "Drifter"), (11, "Trespasser"), (16, "Stranger"), (21, "Contractor"), (26, "Operator"),
        (31, "Outlaw"), (36, "Renegade"), (41, "Insurgent"), (46, "Blacklisted"), (51, "Wanted Man"), (56, "Ringleader"),
        (61, "War Chief"), (66, "Ghost"), (71, "Myth"), (76, "Legend of Tarkov"),
    };

    /// <summary>Rank k's item id (fixed, so saved items keep working): 6c67706d 72616e6b 000000 kk ("lgpm" "rank").</summary>
    public static string IdOf(int k) => "6c67706d72616e6b000000" + k.ToString("x2");

    public const string UsecDogtag = "59f32c3b86f77472a31742f0";
    public const string Bitcoin = "59faff1d86f7746c51718c9c";
    public const string DefaultInventory = "55d7217a4bdc2d86028b456d";
}

[Injectable(TypePriority = OnLoadOrder.TraderRegistration - 1)] // after the database, before traders / handbook / flea
public sealed class RankTagItems(CustomItemService customItems, TemplateTable templates, ISptLogger<RankTagItems> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!templates.Items.TryGetValue(new MongoId(RankTags.UsecDogtag), out var dogtag)) { logger.Error("[LevelGateProgression] USEC dogtag template not found — no Rank Tags"); return Task.CompletedTask; }
            templates.Items.TryGetValue(new MongoId(RankTags.Bitcoin), out var coin);
            var handbookParent = templates.Handbook.Items.FirstOrDefault(h => h.Id == dogtag.Id)?.ParentId.ToString();
            int made = 0;
            var ids = new List<MongoId>();
            for (int k = 0; k < RankTags.Ranks.Length; k++)
            {
                var (from, name) = RankTags.Ranks[k];
                var id = new MongoId(RankTags.IdOf(k));
                ids.Add(id);
                var result = customItems.CreateItemFromClone(new NewItemFromCloneDetails
                {
                    ItemTplToClone = dogtag.Id,
                    ParentId = dogtag.Parent,
                    NewId = id,
                    NewItemName = "levelgate_ranktag_" + (k + 1),
                    AddToHandbook = handbookParent != null,
                    HandbookParentId = handbookParent,
                    HandbookPriceRoubles = 5000 * (k + 1),
                    AddToFleaPriceDb = false,
                    Locales = new Dictionary<string, LocaleDetails>
                    {
                        ["en"] = new LocaleDetails
                        {
                            Name = $"Rank Tag: {name}",
                            ShortName = name.Length <= 10 ? name : name.Split(' ')[0],
                            Description = $"A heavy steel tag struck with the {name} emblem, rank {k + 1} of {RankTags.Ranks.Length}. " +
                                          $"Earned by reaching level {from}. Wear it in your Dogtag slot.",
                        },
                    },
                }, typeof(RankTags).Assembly);
                if (result.Success != true) { logger.Warning($"[LevelGateProgression] Rank Tag {k + 1}: {string.Join("; ", result.Errors ?? new List<string>())}"); continue; }
                var t = templates.Items[id];
                if (t.Properties != null)
                {
                    if (coin?.Properties?.Prefab != null) t.Properties.Prefab = coin.Properties.Prefab; // coin-shaped (re-skinned in game)
                    t.Properties.CanSellOnRagfair = false;
                }
                made++;
            }
            // the Dogtag slot takes them (the slot's "Available" list shows them)
            int slots = 0;
            if (templates.Items.TryGetValue(new MongoId(RankTags.DefaultInventory), out var inv) && inv.Properties?.Slots != null)
                foreach (var slot in inv.Properties.Slots.Where(s => s.Name == "Dogtag"))
                    foreach (var f in slot.Properties?.Filters ?? Enumerable.Empty<SlotFilter>())
                    {
                        f.Filter ??= new HashSet<MongoId>();
                        foreach (var id in ids) f.Filter.Add(id);
                        slots++;
                    }
            logger.Info($"[LevelGateProgression] {made} Rank Tag(s) added ({(coin == null ? "dogtag model" : "coin model")}); Dogtag slot filters updated: {slots}");
        }
        catch (Exception e) { logger.Error("[LevelGateProgression] adding the Rank Tags failed", e); }
        return Task.CompletedTask;
    }
}

/// <summary>Every 20 s: a profile that reached a rank without its tag yet gets it by mail (one mail for all that are due).</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class RankTagMail(SaveServer saveServer, MailSendService mail, ISptLogger<RankTagMail> logger) : IOnUpdate
{
    private Dictionary<string, List<int>>? _sent;
    private static string SentFile => Path.Combine(Path.GetDirectoryName(typeof(RankTags).Assembly.Location)!, "sent.json");

    public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken)
    {
        if (secondsSinceLastRun < 20) return Task.FromResult(false);
        try
        {
            _sent ??= File.Exists(SentFile) ? JsonSerializer.Deserialize<Dictionary<string, List<int>>>(File.ReadAllText(SentFile)) ?? new() : new();
            bool changed = false;
            foreach (var (sessionId, profile) in saveServer.GetProfiles())
            {
                var pmc = profile.CharacterData?.PmcData;
                int level = pmc?.Info?.Level ?? 0;
                if (pmc?.Inventory?.Items == null || level <= 0) continue;
                string key = sessionId.ToString();
                if (!_sent.TryGetValue(key, out var had)) _sent[key] = had = new List<int>();
                var due = Enumerable.Range(0, RankTags.Ranks.Length).Where(k => RankTags.Ranks[k].From <= level && !had.Contains(k)).ToList();
                if (due.Count == 0) continue;
                // the tag's details: a copy of the player's own worn dogtag's (what the game expects), else made up
                var worn = pmc.Inventory.Items.FirstOrDefault(i => i.SlotId == "Dogtag")?.Upd?.Dogtag;
                var items = due.Select(k => new Item
                {
                    Id = new MongoId(),
                    Template = new MongoId(RankTags.IdOf(k)),
                    Upd = new Upd
                    {
                        SpawnedInSession = true,
                        Dogtag = new UpdDogtag
                        {
                            AccountId = worn?.AccountId ?? profile.ProfileInfo?.Aid?.ToString(),
                            ProfileId = worn?.ProfileId ?? pmc.Id.ToString(),
                            Nickname = pmc.Info?.Nickname,
                            Side = (pmc.Info?.Side ?? "").ToLowerInvariant() == "bear" ? DogtagSide.Bear : DogtagSide.Usec,
                            Level = level,
                            Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                            Status = "Rank reached",
                        },
                    },
                }).ToList();
                string names = string.Join(", ", due.Select(k => RankTags.Ranks[k].Name));
                string text = due.Count == 1
                    ? $"Rank reached: {RankTags.Ranks[due[0]].Name}. Your Rank Tag is attached. Wear it in your Dogtag slot."
                    : $"Ranks reached: {names}. Your Rank Tags are attached. Wear one in your Dogtag slot.";
                mail.SendSystemMessageToPlayer(sessionId, text, items, 60L * 60 * 24 * 365);
                had.AddRange(due);
                changed = true;
                logger.Info($"[LevelGateProgression] {pmc.Info?.Nickname} (level {level}): Rank Tag(s) mailed: {names}");
            }
            if (changed) File.WriteAllText(SentFile, JsonSerializer.Serialize(_sent, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { logger.Error("[LevelGateProgression] mailing Rank Tags failed", e); }
        return Task.FromResult(true);
    }
}
