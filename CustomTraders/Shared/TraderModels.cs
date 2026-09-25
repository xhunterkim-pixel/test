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

    public static bool IsCurrency(string tpl) => tpl == Roubles || tpl == Dollars || tpl == Euros;
}

public class QuestDef
{
    /// <summary>24-hex quest id.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "New quest";
    public string Description { get; set; } = "";
    public string SuccessMessage { get; set; } = "";

    /// <summary>Player level needed before the quest is offered.</summary>
    public int MinLevel { get; set; } = 1;

    /// <summary>Quests that must be completed first (ids, any trader).</summary>
    public List<string> PrerequisiteQuestIds { get; set; } = new();

    /// <summary>Optional image in the trader's folder shown for the quest (jpg/png).</summary>
    public string? Image { get; set; }

    public List<ConditionDef> Conditions { get; set; } = new();

    public List<RewardDef> Rewards { get; set; } = new();
}

public static class ConditionTypes
{
    public const string HandoverItem = "HandoverItem";   // hand items to the trader
    public const string FindItem = "FindItem";           // have items found in raid (no hand-in)
    public const string Kill = "Kill";                   // kill N targets, optional maps

    public static readonly string[] All = { HandoverItem, FindItem, Kill };
}

public class ConditionDef
{
    public string Id { get; set; } = "";

    /// <summary>HandoverItem, FindItem or Kill.</summary>
    public string Type { get; set; } = ConditionTypes.HandoverItem;

    /// <summary>Objective text shown in the quest. Empty = generated.</summary>
    public string Text { get; set; } = "";

    /// <summary>HandoverItem/FindItem: any of these items counts.</summary>
    public List<string> ItemTpls { get; set; } = new();

    public int Count { get; set; } = 1;

    public bool FoundInRaid { get; set; } = true;

    /// <summary>Kill: Any, Savage, AnyPmc, Usec or Bear.</summary>
    public string KillTarget { get; set; } = "Any";

    /// <summary>Kill: map ids (bigmap, factory4_day, Woods, Shoreline, Interchange, laboratory, RezervBase, TarkovStreets, Lighthouse, Sandbox...). Empty = any map.</summary>
    public List<string> Locations { get; set; } = new();
}

public static class RewardTypes
{
    public const string Experience = "Experience";
    public const string TraderStanding = "TraderStanding";
    public const string Item = "Item";
    public const string UnlockOffer = "UnlockOffer";

    public static readonly string[] All = { Experience, TraderStanding, Item, UnlockOffer };
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
