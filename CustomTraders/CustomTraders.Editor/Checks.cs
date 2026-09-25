using CustomTraders.Shared;

namespace CustomTraders.Editor;

/// <summary>One trader loaded from traders/&lt;folder&gt;/trader.json.</summary>
public sealed class TraderEntry(string folder, TraderFile file)
{
    public string Folder { get; } = folder;
    public TraderFile File { get; } = file;
    public bool Dirty { get; set; }
    public override string ToString() => (Dirty ? "• " : "") + File.Name;
}

public enum CheckLevel { Error, Warning, Info, Ok }

/// <summary>One line in the Checks &amp; Log page. <see cref="Target"/> is the offer/quest it is about (for "Go to").</summary>
public sealed record CheckEntry(CheckLevel Level, string Where, string Message, TraderEntry? Trader = null, object? Target = null)
{
    public DateTime Time { get; init; } = DateTime.Now;
}

/// <summary>What a quest needs before it is offered: the level and every quest before it.</summary>
public sealed record QuestRequirements(
    int OwnLevel,
    int EffectiveLevel,
    List<(TraderEntry Trader, QuestDef Quest, int Depth)> Chain,
    List<string> MissingIds,
    bool HasCycle);

/// <summary>
/// Pre-flight checks of every trader: finds what would make the server skip
/// an offer or quest, or make a quest impossible, before you restart SPT.
/// Errors = will not work; warnings = probably not what you want; info = FYI.
/// </summary>
public static class Checks
{
    public static List<CheckEntry> Run(IReadOnlyList<TraderEntry> traders, ItemDatabase db)
    {
        var log = new List<CheckEntry>();
        var allQuests = traders.SelectMany(t => t.File.Quests.Select(q => (Trader: t, Quest: q))).ToList();

        // --- ids shared between traders -----------------------------------------
        foreach (var group in traders.GroupBy(t => t.File.Id).Where(g => g.Count() > 1))
            foreach (var t in group)
                log.Add(new(CheckLevel.Error, t.File.Name, $"Trader id {group.Key} is used by {group.Count()} traders ({string.Join(", ", group.Select(x => x.File.Name))}). Only the first one loads. Delete and re-create the copy.", t));
        foreach (var group in allQuests.GroupBy(x => x.Quest.Id).Where(g => g.Key.Length > 0 && g.Count() > 1))
            foreach (var (t, q) in group)
                log.Add(new(CheckLevel.Error, $"{t.File.Name} › {q.Name}", $"Quest id {group.Key} is used by {group.Count()} quests. Only one of them loads — use Duplicate (it makes new ids) instead of copying JSON.", t, q));

        foreach (var entry in traders)
        {
            CheckTrader(entry, db, log);
            foreach (var offer in entry.File.Offers) CheckOffer(entry, offer, db, log);
            foreach (var quest in entry.File.Quests) CheckQuest(entry, quest, traders, db, log);
        }
        return log;
    }

    private static void CheckTrader(TraderEntry t, ItemDatabase db, List<CheckEntry> log)
    {
        var f = t.File;
        string where = f.Name;
        int before = log.Count;

        if (!Ids.IsValid(f.Id))
            log.Add(new(CheckLevel.Warning, where, $"Trader id '{f.Id}' is not a valid 24-character id. The server gives it a new one on start (players lose progress with this trader if it keeps changing).", t));
        if (string.IsNullOrWhiteSpace(f.Name))
            log.Add(new(CheckLevel.Error, where, "Trader has no name.", t));
        if (!File.Exists(Path.Combine(t.Folder, f.Avatar ?? "")))
            log.Add(new(CheckLevel.Warning, where, $"Icon '{f.Avatar}' not found in the trader folder — the trader shows without a picture. Use \"Choose icon from PC\".", t));
        if (f.RefreshMinutesMin > f.RefreshMinutesMax)
            log.Add(new(CheckLevel.Warning, where, $"Restock min ({f.RefreshMinutesMin} min) is bigger than max ({f.RefreshMinutesMax} min); the server uses the min for both.", t));
        if (f.LoyaltyLevels.Count == 0)
            log.Add(new(CheckLevel.Error, where, "No loyalty levels — the trader needs at least LL1.", t));
        for (int i = 1; i < f.LoyaltyLevels.Count; i++)
        {
            var a = f.LoyaltyLevels[i - 1];
            var b = f.LoyaltyLevels[i];
            if (b.MinLevel < a.MinLevel || b.MinStanding < a.MinStanding || b.MinSalesSum < a.MinSalesSum)
                log.Add(new(CheckLevel.Warning, where, $"LL{i + 1} needs less than LL{i} (level, standing or sales) — loyalty levels should only go up.", t));
        }
        if (f.Offers.Count == 0)
            log.Add(new(CheckLevel.Info, where, "Trader sells nothing yet (Offers page → + Add offer).", t));
        if (!f.Enabled)
            log.Add(new(CheckLevel.Info, where, "Switched OFF — the server won't load this trader, its offers or its quests.", t));

        if (log.Count == before)
            log.Add(new(CheckLevel.Ok, where, $"Trader OK — {f.Offers.Count} offer(s), {f.Quests.Count} quest(s).", t));
    }

    private static void CheckOffer(TraderEntry t, OfferDef o, ItemDatabase db, List<CheckEntry> log)
    {
        string where = $"{t.File.Name} › offer {db.NameOf(o.ItemTpl)}";

        if (!Ids.IsValid(o.ItemTpl))
            log.Add(new(CheckLevel.Error, where, "Offer has no item (or the item id is invalid) — it will be skipped.", t, o));
        else if (db.IsLoaded && !db.Exists(o.ItemTpl))
            log.Add(new(CheckLevel.Error, where, $"Item id {o.ItemTpl} doesn't exist in the game — the offer will be skipped.", t, o));

        if (o.Cost.Count == 0)
            log.Add(new(CheckLevel.Error, where, "Offer has no cost — it will be skipped. Add roubles or barter items.", t, o));
        foreach (var c in o.Cost)
        {
            if (!Ids.IsValid(c.ItemTpl) || db.IsLoaded && !db.Exists(c.ItemTpl))
                log.Add(new(CheckLevel.Error, where, $"Cost item '{c.ItemTpl}' is not a valid item — that part of the cost is dropped.", t, o));
            if (c.Count <= 0)
                log.Add(new(CheckLevel.Error, where, $"Cost amount of {db.NameOf(c.ItemTpl)} is {c.Count} — must be at least 1.", t, o));
            if (c.Count != Math.Floor(c.Count) && !Currencies.IsCurrency(c.ItemTpl))
                log.Add(new(CheckLevel.Warning, where, $"{c.Count} x {db.NameOf(c.ItemTpl)} — barter items should be whole numbers.", t, o));
        }
        if (o.LoyaltyLevel < 1 || o.LoyaltyLevel > t.File.LoyaltyLevels.Count)
            log.Add(new(CheckLevel.Warning, where, $"Needs LL{o.LoyaltyLevel} but the trader only has {t.File.LoyaltyLevels.Count} loyalty level(s).", t, o));
        if (!o.Unlimited && o.Stock < 1)
            log.Add(new(CheckLevel.Error, where, "Limited stock of 0 — nobody can buy it.", t, o));
        if (!o.Unlimited && o.BuyLimit > o.Stock)
            log.Add(new(CheckLevel.Info, where, $"Buy limit ({o.BuyLimit}) is higher than the stock ({o.Stock}); the stock is the real limit.", t, o));

        var unlockers = t.File.Quests.Where(q => q.Rewards.Any(r => r.Type == RewardTypes.UnlockOffer && r.OfferId == o.Id)).ToList();
        if (unlockers.Count > 1)
            log.Add(new(CheckLevel.Info, where, $"Unlocked by {unlockers.Count} quests ({string.Join(", ", unlockers.Select(q => q.Name))}) — each quest unlocks its own copy.", t, o));
    }

    private static void CheckQuest(TraderEntry t, QuestDef q, IReadOnlyList<TraderEntry> traders, ItemDatabase db, List<CheckEntry> log)
    {
        string where = $"{t.File.Name} › {q.Name}";
        int errors = 0;
        void Add(CheckLevel level, string message)
        {
            if (level == CheckLevel.Error) errors++;
            log.Add(new(level, where, message, t, q));
        }

        if (!Ids.IsValid(q.Id)) Add(CheckLevel.Error, $"Quest id '{q.Id}' is not valid — the quest is skipped.");
        if (string.IsNullOrWhiteSpace(q.Name)) Add(CheckLevel.Warning, "Quest has no name.");
        if (q.MinLevel < 1 || q.MinLevel > 79) Add(CheckLevel.Warning, $"Unlock level {q.MinLevel} — player levels go from 1 to 79.");
        if (q.Conditions.Count == 0) Add(CheckLevel.Error, "No objectives — the quest would complete the moment it's accepted.");

        // --- objectives ------------------------------------------------------------
        foreach (var c in q.Conditions)
        {
            string what = $"Option {QuestDef.OptionLetter(c.Option)} · {ConditionTypes.Label(c.Type)}";
            if (!ConditionTypes.All.Contains(c.Type)) { Add(CheckLevel.Error, $"{what}: unknown objective type '{c.Type}' — skipped by the server."); continue; }
            if (c.Count < 1) Add(CheckLevel.Error, $"{what}: amount is {c.Count} — must be at least 1.");

            bool needsItems = c.Type is ConditionTypes.HandoverItem or ConditionTypes.FindItem or ConditionTypes.UseItem;
            if (needsItems && c.ItemTpls.Count == 0) Add(CheckLevel.Error, $"{what}: no items picked — the objective is skipped, so this option can't be done.");
            foreach (var tpl in needsItems ? c.ItemTpls : new List<string>())
            {
                if (!Ids.IsValid(tpl) || db.IsLoaded && !db.Exists(tpl)) Add(CheckLevel.Error, $"{what}: item '{tpl}' doesn't exist.");
            }

            bool money = c.ItemTpls.Count > 0 && c.ItemTpls.All(IsMoney);
            if (c.Type == ConditionTypes.FindItem && money)
                Add(CheckLevel.Warning, $"{what}: money can't really be \"found in raid\" — use Hand over for payments.");
            if (c.Type == ConditionTypes.UseItem)
            {
                foreach (var tpl in c.ItemTpls)
                    if (db.Get(tpl) is { } item && item.Category is not (ItemCategory.Food or ItemCategory.Meds))
                        Add(CheckLevel.Warning, $"{what}: {item.Name} is not food, drink or medicine — it can't be \"used\" in raid.");
                Add(CheckLevel.Info, $"{what}: uses the game's UseItem counter — test it once in raid.");
            }

            if (c.Type == ConditionTypes.Kill)
            {
                if (KillTargets.All.All(k => k.Id != c.KillTarget)) Add(CheckLevel.Error, $"{what}: unknown target '{c.KillTarget}'.");
                foreach (var tpl in c.WeaponTpls)
                {
                    var item = db.Get(tpl);
                    if (!Ids.IsValid(tpl) || db.IsLoaded && item == null) Add(CheckLevel.Error, $"{what}: weapon '{tpl}' doesn't exist.");
                    else if (item != null && item.Category is not (ItemCategory.Weapon or ItemCategory.Grenade))
                        Add(CheckLevel.Warning, $"{what}: {item.Name} is not a weapon or grenade — kills can never count with it.");
                }
                if (c.WeaponTpls.Count > 0 && c.Calibers.Count > 0)
                {
                    var calibers = c.WeaponTpls.Select(db.Get).Where(i => i?.Category == ItemCategory.Weapon).ToList();
                    if (calibers.Count > 0 && calibers.All(i => i!.Caliber.Length > 0 && !c.Calibers.Contains(i.Caliber)))
                        Add(CheckLevel.Error, $"{what}: none of the weapons shoot {string.Join("/", c.Calibers.Select(ItemDatabase.CaliberName))} — impossible.");
                }
                if (c.KillTarget is "AnyPmc" or "Usec" or "Bear" && c.BossRoles.Count > 0)
                    Add(CheckLevel.Info, $"{what}: bosses picked but the target is PMCs — the boss list is ignored.");
            }

            if (c.Type is ConditionTypes.Kill or ConditionTypes.Extract)
            {
                foreach (var tpl in c.WearingTpls)
                {
                    var item = db.Get(tpl);
                    if (!Ids.IsValid(tpl) || db.IsLoaded && item == null) Add(CheckLevel.Error, $"{what}: worn item '{tpl}' doesn't exist.");
                    else if (item != null && item.Category is not (ItemCategory.Gear or ItemCategory.Weapon))
                        Add(CheckLevel.Warning, $"{what}: {item.Name} can't be worn (not gear) — this can never be met.");
                }
            }

            foreach (var map in c.Locations.Where(m => Maps.All.All(x => !x.Id.Equals(m, StringComparison.OrdinalIgnoreCase))))
                Add(CheckLevel.Warning, $"{what}: unknown map id '{map}'.");
        }

        var options = q.UsedOptions();
        if (options.Count > 1)
            Add(CheckLevel.Info, $"{options.Count} ways to complete ({string.Join(", ", options.Select(QuestDef.OptionLetter))}). In game they show as {options.Count} quests; finishing one cancels the others.");

        // --- rewards -------------------------------------------------------------------
        if (q.Rewards.Count == 0) Add(CheckLevel.Info, "No rewards.");
        foreach (var r in q.Rewards)
        {
            switch (r.Type)
            {
                case RewardTypes.Experience when r.Value < 0:
                    Add(CheckLevel.Warning, $"Negative XP reward ({r.Value:N0}).");
                    break;
                case RewardTypes.TraderStanding when Math.Abs(r.Value) > 1:
                    Add(CheckLevel.Warning, $"Standing reward {r.Value} — standing is usually small (0.01 – 0.2). 1.0 = a whole loyalty bar.");
                    break;
                case RewardTypes.Item when !Ids.IsValid(r.ItemTpl) || db.IsLoaded && !db.Exists(r.ItemTpl):
                    Add(CheckLevel.Error, "Item reward has no valid item — skipped.");
                    break;
                case RewardTypes.Item when r.Count < 1:
                    Add(CheckLevel.Error, $"Item reward amount is {r.Count}.");
                    break;
                case RewardTypes.UnlockOffer when t.File.Offers.All(o => o.Id != r.OfferId):
                    Add(CheckLevel.Error, "\"Unlock offer\" reward points at an offer that doesn't exist (deleted?) — pick one.");
                    break;
            }
        }

        // --- unlock chain ---------------------------------------------------------------
        var req = Requirements(q, traders);
        foreach (var (reqTrader, reqQuest, _) in req.Chain.Where(c => !c.Trader.File.Enabled && t.File.Enabled))
            Add(CheckLevel.Error, $"Requires \"{reqQuest.Name}\" from {reqTrader.File.Name}, which is switched OFF — this quest can never unlock.");
        foreach (var id in req.MissingIds)
            Add(CheckLevel.Error, $"Requires quest {id}, which no longer exists — this quest can never unlock. Untick it under Required quests.");
        if (req.HasCycle)
            Add(CheckLevel.Error, "Required quests go in a circle (this quest ends up requiring itself) — it can never unlock.");
        if (req.EffectiveLevel > req.OwnLevel)
            Add(CheckLevel.Info, $"Set to unlock at level {req.OwnLevel}, but its required quests need level {req.EffectiveLevel} — so really level {req.EffectiveLevel}.");

        if (errors == 0)
        {
            string chain = req.Chain.Count == 0 ? "no quests before it" : $"after {req.Chain.Count} quest(s): {string.Join(" → ", req.Chain.OrderByDescending(c => c.Depth).Select(c => c.Quest.Name))}";
            Add(CheckLevel.Ok, $"Quest will work — unlocks at level {req.EffectiveLevel}, {chain}.");
        }
    }

    private static bool IsMoney(string tpl) => Currencies.IsCurrency(tpl) || tpl == Currencies.GpCoin || tpl == Currencies.LegaMedal;

    /// <summary>Every quest that has to be done before <paramref name="quest"/> (all the way back), and the real unlock level.</summary>
    public static QuestRequirements Requirements(QuestDef quest, IReadOnlyList<TraderEntry> traders)
    {
        var byId = new Dictionary<string, (TraderEntry, QuestDef)>();
        foreach (var t in traders)
            foreach (var q in t.File.Quests)
                byId.TryAdd(q.Id, (t, q));

        var chain = new List<(TraderEntry, QuestDef, int)>();
        var missing = new List<string>();
        var seen = new HashSet<string>();
        bool cycle = false;
        int level = Math.Max(1, quest.MinLevel);

        void Walk(QuestDef q, int depth, HashSet<string> path)
        {
            foreach (var id in q.PrerequisiteQuestIds)
            {
                if (id == quest.Id || path.Contains(id)) { cycle = true; continue; }
                if (!byId.TryGetValue(id, out var found)) { if (!missing.Contains(id)) missing.Add(id); continue; }
                if (!seen.Add(id)) continue;
                chain.Add((found.Item1, found.Item2, depth));
                level = Math.Max(level, found.Item2.MinLevel);
                Walk(found.Item2, depth + 1, new HashSet<string>(path) { id });
            }
        }

        Walk(quest, 1, new HashSet<string> { quest.Id });
        return new QuestRequirements(Math.Max(1, quest.MinLevel), level, chain, missing, cycle);
    }
}
