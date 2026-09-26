// -----------------------------------------------------------------------------
// CustomTraders — the on-disk format of one trader (traders/<folder>/trader.json).
//
// Shared by the server mod (reads it at startup and builds the trader, its
// offers and its quests in the game database) and the Windows editor (writes
// it). Both compile this same file, so the two can never disagree on a field.
//
// Item ids ("tpl") are the game's 24-character template ids, e.g.
// 5447a9cd4bdc2dbd208b4567 = M4A1, 5449016a4bdc2d6f028b456f = Roubles.
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomTraders.Shared;

public class TraderFile
{
    /// <summary>24-hex id of the trader. Never change it once players have used the trader.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "Iron";
    public string Nickname { get; set; } = "Iron";
    public string Surname { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>RUB, USD or EUR — the currency the trader deals in.</summary>
    public string Currency { get; set; } = "RUB";

    public bool UnlockedByDefault { get; set; } = true;

    /// <summary>
    /// When not unlocked from the start: the quest (one of yours, or a game
    /// quest id) whose completion unlocks the trader.
    /// </summary>
    public string? UnlockQuestId { get; set; }

    /// <summary>Off = the server skips this trader completely (its offers and quests aren't loaded). Nothing is deleted.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Show this trader's offers on the flea market.</summary>
    public bool ListOnFlea { get; set; } = true;

    /// <summary>Image file in the trader's folder (jpg or png).</summary>
    public string Avatar { get; set; } = "avatar.jpg";

    /// <summary>Restock interval range, in minutes.</summary>
    public int RefreshMinutesMin { get; set; } = 60;
    public int RefreshMinutesMax { get; set; } = 120;

    public List<LoyaltyLevelDef> LoyaltyLevels { get; set; } = LoyaltyLevelDef.Defaults();

    public List<OfferDef> Offers { get; set; } = new();

    public List<QuestDef> Quests { get; set; } = new();

    // --- (de)serialization shared by server and editor ---------------------

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep apostrophes, accents etc. readable in the file instead of \u0027.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TraderFile Load(string path) =>
        JsonSerializer.Deserialize<TraderFile>(File.ReadAllText(path), JsonOptions) ?? new TraderFile();

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
}

public class LoyaltyLevelDef
{
    public int MinLevel { get; set; } = 1;
    public long MinSalesSum { get; set; }
    public double MinStanding { get; set; }

    /// <summary>% of an item's value the trader pays when you sell to them.</summary>
    public int BuyPriceCoef { get; set; } = 50;

    public static List<LoyaltyLevelDef> Defaults() => new()
    {
        new LoyaltyLevelDef { MinLevel = 1,  MinSalesSum = 0,       MinStanding = 0,    BuyPriceCoef = 50 },
        new LoyaltyLevelDef { MinLevel = 15, MinSalesSum = 1000000, MinStanding = 0.2,  BuyPriceCoef = 45 },
        new LoyaltyLevelDef { MinLevel = 25, MinSalesSum = 2500000, MinStanding = 0.4,  BuyPriceCoef = 40 },
        new LoyaltyLevelDef { MinLevel = 35, MinSalesSum = 5000000, MinStanding = 0.7,  BuyPriceCoef = 35 },
    };
}

/// <summary>One thing the trader sells, for money or as a barter.</summary>
public class OfferDef
{
    /// <summary>24-hex id of the offer (stable: quests unlock offers by this id).</summary>
    public string Id { get; set; } = "";

    public string ItemTpl { get; set; } = "";

    /// <summary>Your own notes (editor only; the game never sees them).</summary>
    public string Notes { get; set; } = "";

    /// <summary>Your own labels for grouping offers (editor only).</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Weapons: sell the game's default fully-assembled preset instead of a bare receiver.</summary>
    public bool UseDefaultPreset { get; set; } = true;

    /// <summary>Trader loyalty level needed to see the offer (1-4).</summary>
    public int LoyaltyLevel { get; set; } = 1;

    /// <summary>Unlimited stock, or <see cref="Stock"/> units per restock.</summary>
    public bool Unlimited { get; set; } = true;
    public int Stock { get; set; } = 1;

    /// <summary>Max units a player may buy per restock (0 = no limit).</summary>
    public int BuyLimit { get; set; }

    /// <summary>What it costs: money (a currency tpl) and/or barter items. All are required.</summary>
    public List<CostDef> Cost { get; set; } = new();

    /// <summary>Offer only appears after completing this quest (set by a quest's UnlockOffer reward).</summary>
    public string? UnlockedByQuestId { get; set; }
}

public class CostDef
{
    public string ItemTpl { get; set; } = Currencies.Roubles;
    public double Count { get; set; } = 1;
}

public static class Currencies
{
    public const string Roubles = "5449016a4bdc2d6f028b456f";
    public const string Dollars = "5696686a4bdc2da3298b456a";
    public const string Euros = "569668774bdc2da2298b4568";

    public const string GpCoin = "5d235b4d86f7742e017bc88a";
    public const string LegaMedal = "6656560053eaaa7a23349c86";

    public static bool IsCurrency(string tpl) => tpl == Roubles || tpl == Dollars || tpl == Euros;

    public static string Symbol(string tpl) => tpl switch
    {
        Roubles => "₽",
        Dollars => "$",
        Euros => "€",
        GpCoin => "GP",
        LegaMedal => "Lega",
        _ => "",
    };
}

public class QuestDef
{
    /// <summary>24-hex quest id.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "New quest";
    public string Description { get; set; } = "";
    public string SuccessMessage { get; set; } = "";

    /// <summary>Your own notes (editor only; the game never sees them).</summary>
    public string Notes { get; set; } = "";

    /// <summary>Player level needed before the quest is offered.</summary>
    public int MinLevel { get; set; } = 1;

    /// <summary>Quests that must be completed first (ids, any trader).</summary>
    public List<string> PrerequisiteQuestIds { get; set; } = new();

    /// <summary>Optional image in the trader's folder shown for the quest (jpg/png).</summary>
    public string? Image { get; set; }

    /// <summary>One of the game's own quest pictures (file name without extension in SPT_Data/images/quests), used when Image is empty.</summary>
    public string? GameImage { get; set; }

    public List<ConditionDef> Conditions { get; set; } = new();

    public List<RewardDef> Rewards { get; set; } = new();

    /// <summary>Your own labels to keep track of quests (editor only; the game doesn't see them).</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>The quest fails when the player dies / goes MIA / leaves a raid while it's active; it can be restarted.</summary>
    public bool FailOnDeath { get; set; }

    /// <summary>The options (1-4) that have at least one objective, in order. Always at least one.</summary>
    public List<int> UsedOptions()
    {
        var used = Conditions.Select(c => Math.Clamp(c.Option, 1, 4)).Distinct().OrderBy(o => o).ToList();
        return used.Count == 0 ? new List<int> { 1 } : used;
    }

    public static string OptionLetter(int option) => ((char)('A' + Math.Clamp(option, 1, 4) - 1)).ToString();
}

public static class ConditionTypes
{
    public const string HandoverItem = "HandoverItem";   // hand items (or money) to the trader
    public const string FindItem = "FindItem";           // have items found in raid (no hand-in)
    public const string Kill = "Kill";                   // kill N targets (weapon / worn gear / caliber / maps)
    public const string Extract = "Extract";             // survive and extract N times (maps / worn gear)
    public const string UseItem = "UseItem";             // use (eat, drink, inject, apply) items N times in raid
    public const string Skill = "Skill";                 // reach level N in a skill

    public static readonly string[] All = { HandoverItem, FindItem, Kill, Extract, UseItem, Skill };

    public static string Label(string type) => type switch
    {
        HandoverItem => "Hand over items / money",
        FindItem => "Find in raid",
        Kill => "Kill",
        Extract => "Extract",
        UseItem => "Use items in raid",
        Skill => "Reach a skill level",
        _ => type,
    };
}

public class ConditionDef
{
    public string Id { get; set; } = "";

    /// <summary>HandoverItem, FindItem, Kill, Extract or UseItem.</summary>
    public string Type { get; set; } = ConditionTypes.HandoverItem;

    /// <summary>
    /// Which way of completing the quest this objective belongs to (1-4 = A-D).
    /// All objectives of one option must be done; finishing any one option
    /// completes the quest and cancels the other options.
    /// </summary>
    public int Option { get; set; } = 1;

    /// <summary>Objective text shown in the quest. Empty = generated.</summary>
    public string Text { get; set; } = "";

    /// <summary>HandoverItem/FindItem/UseItem: any of these items counts.</summary>
    public List<string> ItemTpls { get; set; } = new();

    public int Count { get; set; } = 1;

    public bool FoundInRaid { get; set; } = true;

    /// <summary>Kill: Any, Savage, AnyPmc, Usec, Bear or Boss.</summary>
    public string KillTarget { get; set; } = "Any";

    /// <summary>Kill with KillTarget Boss: which bosses count (bot roles, e.g. bossKilla). Empty = any boss.</summary>
    public List<string> BossRoles { get; set; } = new();

    /// <summary>Kill: the kill must be made with one of these weapons / grenades. Empty = any weapon.</summary>
    public List<string> WeaponTpls { get; set; } = new();

    /// <summary>Kill: the kill must be made with ammo of one of these calibers (e.g. Caliber556x45NATO). Empty = any.</summary>
    public List<string> Calibers { get; set; } = new();

    /// <summary>Kill/Extract: the player must be wearing one of these items. Empty = no requirement.</summary>
    public List<string> WearingTpls { get; set; } = new();

    /// <summary>Kill/Extract/UseItem: map ids (see <see cref="Maps"/>). Empty = any map.</summary>
    public List<string> Locations { get; set; } = new();

    /// <summary>Kill: body parts that must be hit for the kill (Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg). Empty = any.</summary>
    public List<string> BodyParts { get; set; } = new();

    /// <summary>Kill: distance in meters (0 = any), compared with <see cref="DistanceCompare"/> (">=" at least, "<=" within).</summary>
    public int Distance { get; set; }
    public string DistanceCompare { get; set; } = ">=";

    /// <summary>Kill: in-raid hours from–to (e.g. 21 → 7 = at night). Both 0 = any time.</summary>
    public int DaytimeFrom { get; set; }
    public int DaytimeTo { get; set; }

    /// <summary>Kill/Extract/UseItem: all of it in a single raid (the count resets each raid).</summary>
    public bool OneRaid { get; set; }

    /// <summary>Extract: which ways of leaving count (Survived, Runner, Killed, MissingInAction, Left). Empty = Survived + Runner.</summary>
    public List<string> ExitStatuses { get; set; } = new();

    /// <summary>Skill: the skill id (e.g. Endurance, Assault) — the level to reach is <see cref="Count"/>.</summary>
    public string Skill { get; set; } = "";
}

/// <summary>Map ids as the game uses them in quests, with readable names.</summary>
public static class Maps
{
    public static readonly (string Id, string Name)[] All =
    {
        ("bigmap", "Customs"),
        ("factory4_day", "Factory (day)"),
        ("factory4_night", "Factory (night)"),
        ("Woods", "Woods"),
        ("Shoreline", "Shoreline"),
        ("Interchange", "Interchange"),
        ("laboratory", "The Lab"),
        ("RezervBase", "Reserve"),
        ("Lighthouse", "Lighthouse"),
        ("TarkovStreets", "Streets of Tarkov"),
        ("Sandbox", "Ground Zero"),
        ("Sandbox_high", "Ground Zero (21+)"),
        ("Labyrinth", "The Labyrinth"),
    };

    public static string Name(string id) => All.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Name ?? id;
}

/// <summary>Kill targets and boss bot roles.</summary>
public static class KillTargets
{
    public static readonly (string Id, string Name)[] All =
    {
        ("Any", "Anyone (PMCs, Scavs, bosses...)"),
        ("AnyPmc", "Any PMC"),
        ("Usec", "USEC PMCs"),
        ("Bear", "BEAR PMCs"),
        ("Savage", "Scavs (incl. raiders, rogues, bosses)"),
        ("Boss", "Bosses (pick which)"),
    };

    public static readonly (string Role, string Name)[] Bosses =
    {
        ("bossBully", "Reshala"),
        ("bossKilla", "Killa"),
        ("bossKojaniy", "Shturman"),
        ("bossGluhar", "Glukhar"),
        ("bossSanitar", "Sanitar"),
        ("bossTagilla", "Tagilla"),
        ("bossKnight", "Knight"),
        ("followerBigPipe", "Big Pipe"),
        ("followerBirdEye", "Birdeye"),
        ("bossZryachiy", "Zryachiy"),
        ("bossBoar", "Kaban"),
        ("bossKolontay", "Kollontay"),
        ("bossPartisan", "Partisan"),
        ("sectantPriest", "Cultist priest"),
        ("pmcBot", "Raider"),
        ("exUsec", "Rogue"),
    };

    public static string Name(string id) => All.FirstOrDefault(t => t.Id == id).Name ?? id;
    public static string BossName(string role) => Bosses.FirstOrDefault(b => b.Role == role).Name ?? role;
}

public static class RewardTypes
{
    public const string Experience = "Experience";
    public const string TraderStanding = "TraderStanding";
    public const string Item = "Item";
    public const string UnlockOffer = "UnlockOffer";
    public const string Skill = "Skill";          // skill points (100 = one level) in Target skill
    public const string StashRows = "StashRows";  // extra stash rows

    public static readonly string[] All = { Experience, TraderStanding, Item, UnlockOffer, Skill, StashRows };
}

public class RewardDef
{
    public string Id { get; set; } = "";

    /// <summary>Experience, TraderStanding, Item or UnlockOffer.</summary>
    public string Type { get; set; } = RewardTypes.Experience;

    /// <summary>Experience points, or standing (e.g. 0.05).</summary>
    public double Value { get; set; }

    /// <summary>Item reward: the item and how many.</summary>
    public string ItemTpl { get; set; } = "";
    public int Count { get; set; } = 1;
    public bool FoundInRaid { get; set; } = true;

    /// <summary>UnlockOffer: which of this trader's offers the quest unlocks.</summary>
    public string OfferId { get; set; } = "";

    /// <summary>UnlockOffer: stock of the unlocked offer per restock (0 = keep the offer's own stock setting).</summary>
    public int Quantity { get; set; }

    /// <summary>Item: given when the quest is accepted instead of when it's completed.</summary>
    public bool OnStart { get; set; }

    /// <summary>Skill: the skill id that gets <see cref="Value"/> points.</summary>
    public string Skill { get; set; } = "";
}

/// <summary>Random 24-hex ids in the same shape as the game's ids.</summary>
public static class Ids
{
    public static string New()
    {
        var bytes = new byte[12];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bytes[0] = (byte)(seconds >> 24);
        bytes[1] = (byte)(seconds >> 16);
        bytes[2] = (byte)(seconds >> 8);
        bytes[3] = (byte)seconds;
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Deterministic id derived from a stable seed (same seed -> same id every start).</summary>
    public static string Derive(string seed)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    public static bool IsValid(string? id) =>
        id != null && id.Length == 24 && id.All(Uri.IsHexDigit);
}
