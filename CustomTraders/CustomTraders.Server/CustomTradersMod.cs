using System.Reflection;
using System.Text.Json.Nodes;
using Path = System.IO.Path;
using CustomTraders.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace CustomTraders.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.xhunterkim.customtraders";
    public string Name { get; init; } = "CustomTraders";
    public string Author { get; init; } = "xhunterkim";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

// -----------------------------------------------------------------------------
// Reads every traders/<folder>/trader.json (written by CustomTraders.Editor.exe)
// and adds each trader, its offers/barters and its quests to the database.
//
// Same registration steps as SPT's own custom-trader example (and the Natalya
// trader mod): trader base + assort into TradersTable, locale strings,
// avatar image route, restock time, flea listing. Runs right after the
// server's own trader registration (OnLoadOrder.TraderRegistration + 1).
//
// The trader base, assort and quests are produced as the game's own JSON
// formats and turned into server models with the server's JsonUtil, so they
// go through exactly the same deserialization as the vanilla database.
// -----------------------------------------------------------------------------
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class CustomTradersMod(
    ISptLogger<CustomTradersMod> logger,
    ModHelper modHelper,
    JsonUtil jsonUtil,
    ImageRouter imageRouter,
    TraderConfig traderConfig,
    RagfairConfig ragfairConfig,
    TradersTable tradersTable,
    LocaleTable localeTable,
    TemplateTable templateTable,
    GlobalTable globalTable) : IOnLoad
{
    // A vanilla quest icon, used when a quest has no image of its own.
    private const string DefaultQuestImage = "/files/quest/icon/65899d03adeac0191c51e880.jpg";

    private Dictionary<string, string>? _englishNames;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var tradersFolder = Path.Combine(modFolder, "traders");

        if (!Directory.Exists(tradersFolder) || !Directory.EnumerateFiles(tradersFolder, "trader.json", SearchOption.AllDirectories).Any())
        {
            CreateExampleTrader(tradersFolder);
        }

        int loaded = 0;
        foreach (var folder in Directory.GetDirectories(tradersFolder).OrderBy(f => f))
        {
            var file = Path.Combine(folder, "trader.json");
            if (!File.Exists(file)) continue;

            try
            {
                var trader = TraderFile.Load(file);
                if (EnsureIds(trader)) trader.Save(file); // give new offers/quests stable ids once
                if (AddTrader(trader, folder)) loaded++;
            }
            catch (Exception e)
            {
                logger.Error($"[CustomTraders] Failed to load {file}: {e.Message}", e);
            }
        }

        logger.Info($"[CustomTraders] Loaded {loaded} trader(s) from {tradersFolder}");
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Trader
    // -------------------------------------------------------------------------

    private bool AddTrader(TraderFile file, string folder)
    {
        MongoId traderId = file.Id;
        if (tradersTable.ContainsKey(traderId))
        {
            logger.Warning($"[CustomTraders] Trader id {file.Id} ({file.Name}) already exists — skipped.");
            return false;
        }

        // Avatar: the client asks for /files/trader/avatar/<id>.jpg; the image
        // router serves the file from the trader's folder (jpg or png).
        var avatarPath = Path.Combine(folder, file.Avatar ?? "");
        if (File.Exists(avatarPath))
            imageRouter.AddRoute($"/files/trader/avatar/{file.Id}", avatarPath);
        else
            logger.Warning($"[CustomTraders] {file.Name}: avatar '{file.Avatar}' not found in {folder}.");

        var traderBase = jsonUtil.Deserialize<TraderBase>(BuildTraderBase(file).ToJsonString())
                         ?? throw new InvalidOperationException("trader base did not deserialize");

        var questAssort = new JsonObject
        {
            ["started"] = new JsonObject(),
            ["success"] = new JsonObject(),
            ["fail"] = new JsonObject(),
        };
        var assort = jsonUtil.Deserialize<TraderAssort>(BuildAssort(file, questAssort).ToJsonString())
                     ?? throw new InvalidOperationException("assort did not deserialize");

        tradersTable[traderId] = new Trader
        {
            Base = traderBase,
            Assort = assort,
            QuestAssort = jsonUtil.Deserialize<Dictionary<string, Dictionary<MongoId, MongoId>>>(questAssort.ToJsonString())
                          ?? new Dictionary<string, Dictionary<MongoId, MongoId>>(),
            Dialogue = new Dictionary<string, List<string>?>(),
            Suits = new List<Suit>(),
            Services = new(),
        };

        AddLocales(new Dictionary<string, string>
        {
            [$"{file.Id} FullName"] = $"{file.Name} {file.Surname}".Trim(),
            [$"{file.Id} FirstName"] = file.Name,
            [$"{file.Id} Nickname"] = string.IsNullOrWhiteSpace(file.Nickname) ? file.Name : file.Nickname,
            [$"{file.Id} Location"] = file.Location,
            [$"{file.Id} Description"] = file.Description,
        });

        traderConfig.UpdateTime.Add(new UpdateTime
        {
            Name = file.Name,
            TraderId = traderId,
            Seconds = new MinMax<int>(Math.Max(1, file.RefreshMinutesMin) * 60, Math.Max(file.RefreshMinutesMin, file.RefreshMinutesMax) * 60),
        });

        if (file.ListOnFlea)
            ragfairConfig.Traders.TryAdd(traderId, true);

        int quests = 0;
        foreach (var quest in file.Quests)
        {
            if (AddQuest(file, quest, folder)) quests++;
        }

        logger.Info($"[CustomTraders] {file.Name}: {assort.LoyalLevelItems?.Count ?? 0} offer(s), {quests} quest(s)");
        return true;
    }

    private static JsonObject BuildTraderBase(TraderFile file)
    {
        var loyalty = new JsonArray();
        foreach (var level in file.LoyaltyLevels.Take(4))
        {
            loyalty.Add(new JsonObject
            {
                ["minLevel"] = level.MinLevel,
                ["minSalesSum"] = level.MinSalesSum,
                ["minStanding"] = level.MinStanding,
                ["buy_price_coef"] = level.BuyPriceCoef,
                ["repair_price_coef"] = 110,
                ["insurance_price_coef"] = 17,
                ["exchange_price_coef"] = 0,
                ["heal_price_coef"] = 80,
            });
        }

        return new JsonObject
        {
            ["_id"] = file.Id,
            ["availableInRaid"] = false,
            ["avatar"] = $"/files/trader/avatar/{file.Id}.jpg",
            ["balance_dol"] = 1000000,
            ["balance_eur"] = 1000000,
            ["balance_rub"] = 100000000,
            ["buyer_up"] = false,
            ["currency"] = file.Currency is "USD" or "EUR" ? file.Currency : "RUB",
            ["customization_seller"] = false,
            ["discount"] = 0,
            ["discount_end"] = 0,
            ["gridHeight"] = 100,
            ["insurance"] = new JsonObject
            {
                ["availability"] = false,
                ["excluded_category"] = new JsonArray(),
                ["max_return_hour"] = 1,
                ["max_storage_time"] = 96,
                ["min_payment"] = 0,
                ["min_return_hour"] = 0,
            },
            // What the trader buys from players: weapons, mods, gear, ammo.
            ["items_buy"] = new JsonObject
            {
                ["id_list"] = new JsonArray(),
                ["category"] = new JsonArray(
                    "5422acb9af1c889c16000029", "5448fe124bdc2da5018b4567", "5448e53e4bdc2d60728b4567",
                    "5448e5284bdc2dcb718b4567", "5448e54d4bdc2dcc718b4568", "543be5cb4bdc2deb348b4568",
                    "5485a8684bdc2da71d8b4567", "5a341c4086f77401f2541505", "5448f39d4bdc2d0a728b4568"),
            },
            ["items_buy_prohibited"] = new JsonObject
            {
                ["id_list"] = new JsonArray(Currencies.Roubles, Currencies.Dollars, Currencies.Euros),
                ["category"] = new JsonArray(),
            },
            ["location"] = file.Location,
            ["loyaltyLevels"] = loyalty,
            ["medic"] = false,
            ["name"] = file.Name,
            ["nextResupply"] = 0,
            ["nickname"] = string.IsNullOrWhiteSpace(file.Nickname) ? file.Name : file.Nickname,
            ["repair"] = new JsonObject
            {
                ["availability"] = true,
                ["currency"] = Currencies.Roubles,
                ["currency_coefficient"] = 1,
                ["excluded_category"] = new JsonArray(),
                ["excluded_id_list"] = new JsonArray(),
                ["price_rate"] = 0,
                ["quality"] = 0.5,
            },
            ["sell_category"] = new JsonArray(),
            ["surname"] = file.Surname,
            ["unlockedByDefault"] = file.UnlockedByDefault,
        };
    }

    // -------------------------------------------------------------------------
    // Offers
    // -------------------------------------------------------------------------

    private JsonObject BuildAssort(TraderFile file, JsonObject questAssort)
    {
        var items = new JsonArray();
        var barterScheme = new JsonObject();
        var loyalLevelItems = new JsonObject();

        // Quantity overrides and quest locks coming from UnlockOffer rewards.
        var unlockedBy = new Dictionary<string, (string QuestId, int Quantity)>();
        foreach (var quest in file.Quests)
        foreach (var reward in quest.Rewards.Where(r => r.Type == RewardTypes.UnlockOffer && Ids.IsValid(r.OfferId)))
            unlockedBy[reward.OfferId] = (quest.Id, reward.Quantity);

        foreach (var offer in file.Offers)
        {
            if (!Ids.IsValid(offer.Id) || !IsKnownItem(offer.ItemTpl, $"{file.Name} offer")) continue;

            var cost = new JsonArray();
            foreach (var c in offer.Cost.Where(c => c.Count > 0 && IsKnownItem(c.ItemTpl, $"{file.Name} offer cost")))
                cost.Add(new JsonObject { ["count"] = c.Count, ["_tpl"] = c.ItemTpl });
            if (cost.Count == 0)
            {
                logger.Warning($"[CustomTraders] {file.Name}: offer {offer.Id} ({ItemName(offer.ItemTpl)}) has no valid cost — skipped.");
                continue;
            }

            bool unlimited = offer.Unlimited;
            int stock = Math.Max(1, offer.Stock);
            string? questId = offer.UnlockedByQuestId;
            if (unlockedBy.TryGetValue(offer.Id, out var unlock))
            {
                questId = unlock.QuestId;
                if (unlock.Quantity > 0) { unlimited = false; stock = unlock.Quantity; }
            }

            var upd = new JsonObject
            {
                ["UnlimitedCount"] = unlimited,
                ["StackObjectsCount"] = unlimited ? 999999 : stock,
            };
            if (offer.BuyLimit > 0)
            {
                upd["BuyRestrictionMax"] = offer.BuyLimit;
                upd["BuyRestrictionCurrent"] = 0;
            }

            foreach (var node in BuildItemTree(offer.ItemTpl, offer.Id, offer.UseDefaultPreset, "hideout", "hideout", upd))
                items.Add(node);

            barterScheme[offer.Id] = new JsonArray(cost);
            loyalLevelItems[offer.Id] = Math.Clamp(offer.LoyaltyLevel, 1, 4);

            if (Ids.IsValid(questId))
                ((JsonObject)questAssort["success"]!)[offer.Id] = questId;
        }

        return new JsonObject
        {
            ["nextResupply"] = 0,
            ["items"] = items,
            ["barter_scheme"] = barterScheme,
            ["loyal_level_items"] = loyalLevelItems,
        };
    }

    /// <summary>
    /// The item as a list of assort/reward items: a bare item, or — for
    /// weapons with <paramref name="usePreset"/> — the game's default
    /// assembled preset with fresh, deterministic ids under <paramref name="rootId"/>.
    /// </summary>
    private List<JsonObject> BuildItemTree(string tpl, string rootId, bool usePreset, string? parentId, string? slotId, JsonObject? rootUpd)
    {
        var result = new List<JsonObject>();
        var root = new JsonObject { ["_id"] = rootId, ["_tpl"] = tpl };
        if (parentId != null) root["parentId"] = parentId;
        if (slotId != null) root["slotId"] = slotId;
        if (rootUpd != null) root["upd"] = rootUpd;
        result.Add(root);

        if (!usePreset) return result;

        var preset = globalTable.ItemPresets?.Values.FirstOrDefault(p =>
            p.Encyclopedia?.ToString() == tpl && p.Items != null && p.Items.Count > 1);
        if (preset == null) return result;

        string presetRootId = preset.Parent.ToString();
        var idMap = new Dictionary<string, string> { [presetRootId] = rootId };
        foreach (var item in preset.Items!)
        {
            var oldId = item.Id.ToString();
            if (oldId != presetRootId) idMap[oldId] = Ids.Derive(rootId + ":" + oldId);
        }

        foreach (var item in preset.Items!)
        {
            var oldId = item.Id.ToString();
            if (oldId == presetRootId || item.ParentId == null) continue;
            if (!idMap.TryGetValue(item.ParentId, out var newParent)) continue;
            result.Add(new JsonObject
            {
                ["_id"] = idMap[oldId],
                ["_tpl"] = item.Template.ToString(),
                ["parentId"] = newParent,
                ["slotId"] = item.SlotId,
            });
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Quests
    // -------------------------------------------------------------------------

    private bool AddQuest(TraderFile file, QuestDef def, string folder)
    {
        if (!Ids.IsValid(def.Id)) return false;
        MongoId questId = def.Id;
        if (templateTable.Quests.ContainsKey(questId))
        {
            logger.Warning($"[CustomTraders] Quest id {def.Id} ({def.Name}) already exists — skipped.");
            return false;
        }

        string image = DefaultQuestImage;
        if (!string.IsNullOrWhiteSpace(def.Image) && File.Exists(Path.Combine(folder, def.Image)))
        {
            imageRouter.AddRoute($"/files/quest/icon/{def.Id}", Path.Combine(folder, def.Image));
            image = $"/files/quest/icon/{def.Id}.jpg";
        }

        var locales = new Dictionary<string, string>
        {
            [$"{def.Id} name"] = def.Name,
            [$"{def.Id} description"] = def.Description,
            [$"{def.Id} note"] = "",
            [$"{def.Id} startedMessageText"] = def.Description,
            [$"{def.Id} successMessageText"] = string.IsNullOrWhiteSpace(def.SuccessMessage) ? "Good work." : def.SuccessMessage,
            [$"{def.Id} failMessageText"] = "",
            [$"{def.Id} changeQuestMessageText"] = "",
            [$"{def.Id} acceptPlayerMessage"] = "",
            [$"{def.Id} declinePlayerMessage"] = "",
            [$"{def.Id} completePlayerMessage"] = "",
        };

        // --- start conditions: level + prerequisite quests -------------------
        var start = new JsonArray
        {
            new JsonObject
            {
                ["id"] = Ids.Derive(def.Id + ":level"),
                ["index"] = 0,
                ["parentId"] = "",
                ["dynamicLocale"] = false,
                ["globalQuestCounterId"] = "",
                ["visibilityConditions"] = new JsonArray(),
                ["conditionType"] = "Level",
                ["compareMethod"] = ">=",
                ["value"] = Math.Max(1, def.MinLevel),
            },
        };
        int index = 1;
        foreach (var prereq in def.PrerequisiteQuestIds.Where(Ids.IsValid))
        {
            start.Add(new JsonObject
            {
                ["id"] = Ids.Derive(def.Id + ":after:" + prereq),
                ["index"] = index++,
                ["parentId"] = "",
                ["dynamicLocale"] = false,
                ["globalQuestCounterId"] = "",
                ["visibilityConditions"] = new JsonArray(),
                ["conditionType"] = "Quest",
                ["target"] = prereq,
                ["status"] = new JsonArray(4), // Success
                ["availableAfter"] = 0,
                ["dispersion"] = 0,
            });
        }

        // --- objectives -------------------------------------------------------
        var finish = new JsonArray();
        index = 0;
        bool allKills = def.Conditions.Count > 0;
        foreach (var condition in def.Conditions)
        {
            if (!Ids.IsValid(condition.Id)) condition.Id = Ids.Derive(def.Id + ":cond:" + index);
            var node = BuildCondition(condition, index++);
            if (node == null) continue;
            finish.Add(node);
            allKills &= condition.Type == ConditionTypes.Kill;
            locales[condition.Id] = string.IsNullOrWhiteSpace(condition.Text) ? DescribeCondition(condition) : condition.Text;
        }

        // --- rewards ----------------------------------------------------------
        var success = new JsonArray();
        index = 0;
        foreach (var reward in def.Rewards)
        {
            if (!Ids.IsValid(reward.Id)) reward.Id = Ids.Derive(def.Id + ":reward:" + index);
            var node = BuildReward(file, reward, index);
            if (node == null) continue;
            success.Add(node);
            index++;
        }

        var quest = new JsonObject
        {
            ["_id"] = def.Id,
            ["QuestName"] = def.Name,
            ["templateId"] = def.Id,
            ["traderId"] = file.Id,
            ["location"] = "any",
            ["image"] = image,
            ["type"] = allKills ? "Elimination" : "PickUp",
            ["side"] = "Pmc",
            ["isKey"] = false,
            ["restartable"] = false,
            ["instantComplete"] = false,
            ["secretQuest"] = false,
            ["canShowNotificationsInGame"] = true,
            ["name"] = $"{def.Id} name",
            ["description"] = $"{def.Id} description",
            ["note"] = $"{def.Id} note",
            ["startedMessageText"] = $"{def.Id} startedMessageText",
            ["successMessageText"] = $"{def.Id} successMessageText",
            ["failMessageText"] = $"{def.Id} failMessageText",
            ["changeQuestMessageText"] = $"{def.Id} changeQuestMessageText",
            ["acceptPlayerMessage"] = $"{def.Id} acceptPlayerMessage",
            ["declinePlayerMessage"] = $"{def.Id} declinePlayerMessage",
            ["completePlayerMessage"] = $"{def.Id} completePlayerMessage",
            ["conditions"] = new JsonObject
            {
                ["AvailableForStart"] = start,
                ["AvailableForFinish"] = finish,
                ["Fail"] = new JsonArray(),
            },
            ["rewards"] = new JsonObject
            {
                ["Started"] = new JsonArray(),
                ["Success"] = success,
                ["Fail"] = new JsonArray(),
            },
        };

        var parsed = jsonUtil.Deserialize<Quest>(quest.ToJsonString());
        if (parsed == null)
        {
            logger.Warning($"[CustomTraders] Quest {def.Name} did not deserialize — skipped.");
            return false;
        }

        templateTable.Quests[questId] = parsed;
        AddLocales(locales);
        return true;
    }

    private JsonObject? BuildCondition(ConditionDef c, int index)
    {
        var common = new JsonObject
        {
            ["id"] = c.Id,
            ["index"] = index,
            ["parentId"] = "",
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = "",
            ["visibilityConditions"] = new JsonArray(),
        };

        switch (c.Type)
        {
            case ConditionTypes.HandoverItem:
            case ConditionTypes.FindItem:
            {
                var targets = new JsonArray();
                foreach (var tpl in c.ItemTpls.Where(t => IsKnownItem(t, "quest condition"))) targets.Add(tpl);
                if (targets.Count == 0) return null;

                common["conditionType"] = c.Type;
                common["target"] = targets;
                common["value"] = Math.Max(1, c.Count);
                common["onlyFoundInRaid"] = c.FoundInRaid;
                common["minDurability"] = 0;
                common["maxDurability"] = 100;
                common["dogtagLevel"] = 0;
                common["isEncoded"] = false;
                if (c.Type == ConditionTypes.FindItem) common["countInRaid"] = false;
                return common;
            }

            case ConditionTypes.Kill:
            {
                var counterConditions = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = Ids.Derive(c.Id + ":kills"),
                        ["dynamicLocale"] = false,
                        ["conditionType"] = "Kills",
                        ["target"] = string.IsNullOrWhiteSpace(c.KillTarget) ? "Any" : c.KillTarget,
                        ["value"] = 1,
                        ["compareMethod"] = ">=",
                        ["bodyPart"] = new JsonArray(),
                        ["daytime"] = new JsonObject { ["from"] = 0, ["to"] = 0 },
                        ["distance"] = new JsonObject { ["compareMethod"] = ">=", ["value"] = 0 },
                        ["enemyEquipmentExclusive"] = new JsonArray(),
                        ["enemyEquipmentInclusive"] = new JsonArray(),
                        ["enemyHealthEffects"] = new JsonArray(),
                        ["resetOnSessionEnd"] = false,
                        ["savageRole"] = new JsonArray(),
                        ["weapon"] = new JsonArray(),
                        ["weaponCaliber"] = new JsonArray(),
                        ["weaponModsExclusive"] = new JsonArray(),
                        ["weaponModsInclusive"] = new JsonArray(),
                    },
                };
                var maps = c.Locations.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (maps.Count > 0)
                {
                    var mapArray = new JsonArray();
                    foreach (var map in maps) mapArray.Add(map);
                    counterConditions.Add(new JsonObject
                    {
                        ["id"] = Ids.Derive(c.Id + ":location"),
                        ["dynamicLocale"] = false,
                        ["conditionType"] = "Location",
                        ["target"] = mapArray,
                    });
                }

                common["conditionType"] = "CounterCreator";
                common["type"] = "Elimination";
                common["value"] = Math.Max(1, c.Count);
                common["oneSessionOnly"] = false;
                common["doNotResetIfCounterCompleted"] = false;
                common["completeInSeconds"] = 0;
                common["counter"] = new JsonObject
                {
                    ["id"] = Ids.Derive(c.Id + ":counter"),
                    ["conditions"] = counterConditions,
                };
                return common;
            }

            default:
                logger.Warning($"[CustomTraders] Unknown condition type '{c.Type}' — skipped.");
                return null;
        }
    }

    private JsonObject? BuildReward(TraderFile file, RewardDef r, int index)
    {
        switch (r.Type)
        {
            case RewardTypes.Experience:
                return new JsonObject { ["id"] = r.Id, ["index"] = index, ["type"] = "Experience", ["value"] = r.Value };

            case RewardTypes.TraderStanding:
                return new JsonObject { ["id"] = r.Id, ["index"] = index, ["type"] = "TraderStanding", ["value"] = r.Value, ["target"] = file.Id };

            case RewardTypes.Item:
            {
                if (!IsKnownItem(r.ItemTpl, "quest reward")) return null;
                var rootId = Ids.Derive(r.Id + ":item");
                var tree = BuildItemTree(r.ItemTpl, rootId, true, null, null,
                    new JsonObject { ["StackObjectsCount"] = Math.Max(1, r.Count) });
                var items = new JsonArray();
                foreach (var node in tree) items.Add(node);
                return new JsonObject
                {
                    ["id"] = r.Id,
                    ["index"] = index,
                    ["type"] = "Item",
                    ["value"] = Math.Max(1, r.Count),
                    ["target"] = rootId,
                    ["findInRaid"] = r.FoundInRaid,
                    ["unknown"] = false,
                    ["items"] = items,
                };
            }

            case RewardTypes.UnlockOffer:
            {
                var offer = file.Offers.FirstOrDefault(o => o.Id == r.OfferId);
                if (offer == null)
                {
                    logger.Warning($"[CustomTraders] {file.Name}: UnlockOffer reward points at a missing offer {r.OfferId} — skipped.");
                    return null;
                }
                return new JsonObject
                {
                    ["id"] = r.Id,
                    ["index"] = index,
                    ["type"] = "AssortmentUnlock",
                    ["target"] = offer.Id,
                    ["traderId"] = file.Id,
                    ["loyaltyLevel"] = Math.Clamp(offer.LoyaltyLevel, 1, 4),
                    ["unknown"] = false,
                    ["items"] = new JsonArray(new JsonObject { ["_id"] = offer.Id, ["_tpl"] = offer.ItemTpl }),
                };
            }

            default:
                logger.Warning($"[CustomTraders] Unknown reward type '{r.Type}' — skipped.");
                return null;
        }
    }

    private string DescribeCondition(ConditionDef c)
    {
        string items = string.Join(" / ", c.ItemTpls.Select(ItemName));
        switch (c.Type)
        {
            case ConditionTypes.HandoverItem:
                return $"Hand over {(c.FoundInRaid ? "found in raid " : "")}{items}";
            case ConditionTypes.FindItem:
                return $"Find {items} in raid";
            case ConditionTypes.Kill:
                string target = c.KillTarget switch
                {
                    "Savage" => "Scavs",
                    "AnyPmc" => "PMC operatives",
                    "Usec" => "USEC operatives",
                    "Bear" => "BEAR operatives",
                    _ => "enemies",
                };
                string where = c.Locations.Count > 0 ? " on " + string.Join(", ", c.Locations) : "";
                return $"Eliminate {target}{where}";
            default:
                return c.Type;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Adds the same strings to every game language (so non-English clients still show text).</summary>
    private void AddLocales(Dictionary<string, string> strings)
    {
        foreach (var (_, lazy) in localeTable.Global)
        {
            lazy.AddTransformer(dictionary =>
            {
                if (dictionary == null) return dictionary!;
                foreach (var (key, value) in strings) dictionary[key] = value;
                return dictionary;
            });
        }
    }

    private bool IsKnownItem(string? tpl, string context)
    {
        if (Ids.IsValid(tpl) && templateTable.Items.ContainsKey(tpl!)) return true;
        logger.Warning($"[CustomTraders] Unknown item id '{tpl}' in {context} — skipped.");
        return false;
    }

    private string ItemName(string tpl)
    {
        try
        {
            if (_englishNames == null)
            {
                _englishNames = new Dictionary<string, string>();
                if (localeTable.Global.TryGetValue("en", out var en) && en.Value is { } english)
                    foreach (var (key, value) in english) if (key.EndsWith(" Name")) _englishNames[key[..^5]] = value;
            }
            return _englishNames.TryGetValue(tpl, out var name) ? name : tpl;
        }
        catch
        {
            return tpl;
        }
    }

    /// <summary>Fills in missing offer/quest/condition/reward ids. Returns true if anything changed.</summary>
    private static bool EnsureIds(TraderFile file)
    {
        bool changed = false;
        if (!Ids.IsValid(file.Id)) { file.Id = Ids.New(); changed = true; }
        foreach (var offer in file.Offers.Where(o => !Ids.IsValid(o.Id))) { offer.Id = Ids.New(); changed = true; }
        foreach (var quest in file.Quests)
        {
            if (!Ids.IsValid(quest.Id)) { quest.Id = Ids.New(); changed = true; }
            foreach (var c in quest.Conditions.Where(c => !Ids.IsValid(c.Id))) { c.Id = Ids.New(); changed = true; }
            foreach (var r in quest.Rewards.Where(r => !Ids.IsValid(r.Id))) { r.Id = Ids.New(); changed = true; }
        }
        return changed;
    }

    /// <summary>First run: an "Iron" trader selling an M4A1 for roubles, with a placeholder avatar.</summary>
    private void CreateExampleTrader(string tradersFolder)
    {
        var folder = Path.Combine(tradersFolder, "Iron");
        Directory.CreateDirectory(folder);

        var trader = new TraderFile
        {
            Id = Ids.New(),
            Name = "Iron",
            Nickname = "Iron",
            Location = "Unknown",
            Description = "Sells guns. Doesn't ask questions.",
            Avatar = "avatar.png",
            Offers =
            {
                new OfferDef
                {
                    Id = Ids.New(),
                    ItemTpl = "5447a9cd4bdc2dbd208b4567", // Colt M4A1
                    Cost = { new CostDef { ItemTpl = Currencies.Roubles, Count = 75000 } },
                },
            },
        };
        trader.Save(Path.Combine(folder, "trader.json"));
        File.WriteAllBytes(Path.Combine(folder, "avatar.png"), PlaceholderAvatar.Create());
        logger.Info($"[CustomTraders] Created example trader 'Iron' in {folder} — edit it with CustomTraders.Editor.exe");
    }
}

/// <summary>A plain 128x128 PNG so a new trader has a working avatar until you pick one.</summary>
internal static class PlaceholderAvatar
{
    public static byte[] Create()
    {
        const int size = 128;
        var raw = new byte[size * (size * 3 + 1)];
        for (int y = 0; y < size; y++)
        {
            int row = y * (size * 3 + 1);
            raw[row] = 0; // filter: none
            for (int x = 0; x < size; x++)
            {
                bool border = x < 4 || y < 4 || x >= size - 4 || y >= size - 4;
                byte v = border ? (byte)150 : (byte)(55 + (x + y) / 8);
                raw[row + 1 + x * 3] = v;
                raw[row + 2 + x * 3] = v;
                raw[row + 3 + x * 3] = border ? (byte)150 : (byte)(v + 10);
            }
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(png, "IHDR", BigEndian(size).Concat(BigEndian(size)).Concat(new byte[] { 8, 2, 0, 0, 0 }).ToArray());
        using (var compressed = new MemoryStream())
        {
            using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
                z.Write(raw);
            WriteChunk(png, "IDAT", compressed.ToArray());
        }
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static byte[] BigEndian(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(BigEndian(data.Length));
        s.Write(typeBytes);
        s.Write(data);
        s.Write(BigEndian((int)Crc32(typeBytes.Concat(data).ToArray())));
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
