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

    // Quest id -> the game quest ids of its options (A, B, ...). A quest with one
    // option is just itself; with several, every option is its own game quest
    // and completing one fails the others (see AddQuest).
    private readonly Dictionary<string, List<string>> _questOptionIds = new();
    private readonly Dictionary<string, QuestDef> _questDefs = new();

    // (offer id, game quest id that unlocks it) -> the assort entry id used for it.
    private readonly Dictionary<(string OfferId, string QuestId), string> _offerEntries = new();

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var tradersFolder = Path.Combine(modFolder, "traders");

        if (!Directory.Exists(tradersFolder) || !Directory.EnumerateFiles(tradersFolder, "trader.json", SearchOption.AllDirectories).Any())
        {
            CreateExampleTrader(tradersFolder);
        }

        // Read every trader first: quests may require quests of other traders,
        // and those need to be known (with their options) before building.
        var traders = new List<(TraderFile Trader, string Folder)>();
        foreach (var folder in Directory.GetDirectories(tradersFolder).OrderBy(f => f))
        {
            var file = Path.Combine(folder, "trader.json");
            if (!File.Exists(file)) continue;

            try
            {
                var trader = TraderFile.Load(file);
                if (!trader.Enabled)
                {
                    LogBlue($"[CustomTraders] {trader.Name}: switched off in the editor — not loaded.");
                    continue;
                }
                if (EnsureIds(trader)) trader.Save(file); // give new offers/quests stable ids once
                traders.Add((trader, folder));
            }
            catch (Exception e)
            {
                logger.Error($"[CustomTraders] Failed to read {file}: {e.Message}", e);
            }
        }

        foreach (var (trader, _) in traders)
        foreach (var quest in trader.Quests.Where(q => Ids.IsValid(q.Id)))
        {
            _questOptionIds[quest.Id] = OptionQuestIds(quest);
            _questDefs[quest.Id] = quest;
        }

        int loaded = 0;
        foreach (var (trader, folder) in traders)
        {
            try
            {
                if (AddTrader(trader, folder)) loaded++;
            }
            catch (Exception e)
            {
                logger.Error($"[CustomTraders] Failed to load {trader.Name} ({folder}): {e.Message}", e);
            }
        }

        LogBlue($"[CustomTraders] Loaded {loaded} trader(s) from {tradersFolder}");
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

        // Avatar: the client asks for /files/trader/avatar/<id>_<hash>.jpg; the image
        // router serves the file from the trader's folder (jpg or png).
        // The game caches trader images by URL, so replacing avatar.png alone
        // kept showing the old picture. The URL carries a fingerprint of the
        // file: new image -> new URL -> the game downloads it again.
        var avatarPath = Path.Combine(folder, file.Avatar ?? "");
        string avatarKey = $"/files/trader/avatar/{file.Id}";
        if (File.Exists(avatarPath))
        {
            avatarKey += "_" + FileFingerprint(avatarPath);
            imageRouter.AddRoute(avatarKey, avatarPath);
        }
        else
        {
            logger.Warning($"[CustomTraders] {file.Name}: avatar '{file.Avatar}' not found in {folder}.");
        }

        var traderBase = jsonUtil.Deserialize<TraderBase>(BuildTraderBase(file, avatarKey + ".jpg").ToJsonString())
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

        LogBlue($"[CustomTraders] {file.Name}: {file.Offers.Count} offer(s), {quests} quest(s)");
        return true;
    }

    private static JsonObject BuildTraderBase(TraderFile file, string avatarUrl)
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
            ["avatar"] = avatarUrl,
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

        // Quest locks and quantity overrides coming from UnlockOffer rewards.
        // The game ties an offer to ONE quest, so an offer unlocked by several
        // different quests is added once per quest. A quest with several ways
        // needs only one copy, tied to way A: finishing any way marks all its
        // ways completed (QuestOptions), way A included.
        var unlockedBy = new Dictionary<string, List<(string QuestId, int Quantity)>>();
        foreach (var quest in file.Quests.Where(q => Ids.IsValid(q.Id)))
        foreach (var reward in quest.Rewards.Where(r => r.Type == RewardTypes.UnlockOffer && Ids.IsValid(r.OfferId)))
        {
            if (!unlockedBy.TryGetValue(reward.OfferId, out var list)) unlockedBy[reward.OfferId] = list = new();
            if (list.All(u => u.QuestId != quest.Id)) list.Add((quest.Id, reward.Quantity));
        }

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

            var entries = unlockedBy.GetValueOrDefault(offer.Id) ?? new List<(string QuestId, int Quantity)>
            {
                (Ids.IsValid(offer.UnlockedByQuestId) ? offer.UnlockedByQuestId! : "", 0),
            };

            for (int i = 0; i < entries.Count; i++)
            {
                var (questId, quantity) = entries[i];
                string entryId = i == 0 ? offer.Id : Ids.Derive(offer.Id + ":" + questId);
                if (questId.Length > 0) _offerEntries[(offer.Id, questId)] = entryId;

                bool unlimited = offer.Unlimited;
                int stock = Math.Max(1, offer.Stock);
                if (quantity > 0) { unlimited = false; stock = quantity; }

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

                foreach (var node in BuildItemTree(offer.ItemTpl, entryId, offer.UseDefaultPreset, "hideout", "hideout", upd))
                    items.Add(node);

                barterScheme[entryId] = new JsonArray(new JsonArray(cost.Select(c => c!.DeepClone()).ToArray()));
                loyalLevelItems[entryId] = Math.Clamp(offer.LoyaltyLevel, 1, 4);

                if (questId.Length > 0)
                    ((JsonObject)questAssort["success"]!)[entryId] = questId;
            }
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

    /// <summary>The game quest id of each option: option A keeps the quest's own id.</summary>
    private static List<string> OptionQuestIds(QuestDef def) =>
        def.UsedOptions().Select((option, position) => position == 0 ? def.Id : Ids.Derive(def.Id + ":option:" + option)).ToList();

    /// <summary>
    /// Adds the quest. A quest whose objectives are split into options (A-D)
    /// becomes one game quest per option, all offered together; each fails
    /// when another one is completed (the game's own "one of these quests"
    /// mechanic), so finishing any one option completes the quest.
    /// </summary>
    private bool AddQuest(TraderFile file, QuestDef def, string folder)
    {
        if (!Ids.IsValid(def.Id)) return false;

        string image = DefaultQuestImage;
        if (!string.IsNullOrWhiteSpace(def.Image) && File.Exists(Path.Combine(folder, def.Image)))
        {
            var imagePath = Path.Combine(folder, def.Image);
            string key = $"/files/quest/icon/{def.Id}_{FileFingerprint(imagePath)}"; // new image -> new URL (see avatar)
            imageRouter.AddRoute(key, imagePath);
            image = key + ".jpg";
        }

        var options = def.UsedOptions();
        var gameIds = _questOptionIds.GetValueOrDefault(def.Id) ?? OptionQuestIds(def);
        LogQuestPlan(file, def, options);
        QuestOptions.Register(gameIds);
        bool added = false;
        for (int i = 0; i < options.Count; i++)
        {
            var siblings = gameIds.Where((_, j) => j != i).ToList();
            added |= AddQuestOption(file, def, image, options[i], gameIds[i], i == 0, options.Count, siblings);
        }
        return added;
    }

    /// <summary>
    /// Spells out in the server log how the quest works, so it's easy to see
    /// that options and unlocks are wired up (and warns when it can never unlock).
    /// </summary>
    private void LogQuestPlan(TraderFile file, QuestDef def, List<int> options)
    {
        string ways = options.Count > 1
            ? $"{options.Count} ways ({string.Join("/", options.Select(QuestDef.OptionLetter))}) — finishing one closes the others"
            : "1 way";
        var after = new List<string>();
        foreach (var prereq in def.PrerequisiteQuestIds.Where(Ids.IsValid))
        {
            if (_questDefs.TryGetValue(prereq, out var before))
            {
                int n = before.UsedOptions().Count;
                after.Add(n > 1 ? $"'{before.Name}' (any of its {n} ways)" : $"'{before.Name}'");
            }
            else if (templateTable.Quests.ContainsKey(prereq))
            {
                after.Add($"game quest {prereq}");
            }
            else
            {
                logger.Warning($"[CustomTraders] {file.Name}: quest '{def.Name}' requires quest {prereq}, which isn't loaded " +
                               "(deleted, or its trader is switched off) — it will NEVER unlock.");
            }
        }
        LogBlue($"[CustomTraders]   {file.Name} › {def.Name}: level {Math.Max(1, def.MinLevel)}, {ways}" +
                (after.Count > 0 ? $", unlocks after {string.Join(" + ", after)}" : ""));
    }

    private bool AddQuestOption(TraderFile file, QuestDef def, string image, int option, string gameId, bool firstOption, int optionCount, List<string> siblings)
    {
        MongoId questId = gameId;
        if (templateTable.Quests.ContainsKey(questId))
        {
            logger.Warning($"[CustomTraders] Quest id {gameId} ({def.Name}) already exists — skipped.");
            return false;
        }

        // Ids of objectives/rewards: option A keeps the ids from the file,
        // the other options derive their own from them.
        string Sub(string id) => firstOption ? id : Ids.Derive(id + ":" + gameId);

        string letter = QuestDef.OptionLetter(option);
        string name = optionCount > 1 ? $"{def.Name} — Option {letter}" : def.Name;
        string description = def.Description;
        if (optionCount > 1)
        {
            string letters = string.Join(", ", def.UsedOptions().Select(QuestDef.OptionLetter));
            description = (description.Length > 0 ? description + "\n\n" : "") +
                          $"This job can be done {optionCount} ways (options {letters}). Finish any ONE option — the others are then closed.";
        }

        var locales = new Dictionary<string, string>
        {
            [$"{gameId} name"] = name,
            [$"{gameId} description"] = description,
            [$"{gameId} note"] = "",
            [$"{gameId} startedMessageText"] = description,
            [$"{gameId} successMessageText"] = string.IsNullOrWhiteSpace(def.SuccessMessage) ? "Good work." : def.SuccessMessage,
            [$"{gameId} failMessageText"] = "",
            [$"{gameId} changeQuestMessageText"] = "",
            [$"{gameId} acceptPlayerMessage"] = "",
            [$"{gameId} declinePlayerMessage"] = "",
            [$"{gameId} completePlayerMessage"] = "",
        };

        // --- start conditions: level + prerequisite quests -------------------
        var start = new JsonArray { QuestCondition(Ids.Derive(gameId + ":level"), 0, "Level", c => { c["compareMethod"] = ">="; c["value"] = Math.Max(1, def.MinLevel); }) };
        int index = 1;
        foreach (var prereq in def.PrerequisiteQuestIds.Where(Ids.IsValid))
        {
            // A prerequisite with several options is done when one option
            // succeeded and the rest failed: require every option in Success
            // or Fail (they only fail when a sibling is completed).
            var prereqIds = _questOptionIds.GetValueOrDefault(prereq) ?? new List<string> { prereq };
            var statuses = prereqIds.Count > 1 ? new[] { 4, 5 } : new[] { 4 };
            foreach (var target in prereqIds)
            {
                start.Add(QuestCondition(Ids.Derive(gameId + ":after:" + target), index++, "Quest", c =>
                {
                    c["target"] = target;
                    c["status"] = new JsonArray(statuses.Select(x => (JsonNode)x).ToArray());
                    c["availableAfter"] = 0;
                    c["dispersion"] = 0;
                }));
            }
        }

        // --- objectives of this option ----------------------------------------
        var finish = new JsonArray();
        index = 0;
        bool allKills = true;
        foreach (var condition in def.Conditions.Where(c => Math.Clamp(c.Option, 1, 4) == option))
        {
            if (!Ids.IsValid(condition.Id)) condition.Id = Ids.Derive(def.Id + ":cond:" + index);
            string conditionId = Sub(condition.Id);
            var node = BuildCondition(condition, conditionId, index++);
            if (node == null) continue;
            finish.Add(node);
            allKills &= condition.Type == ConditionTypes.Kill;
            locales[conditionId] = string.IsNullOrWhiteSpace(condition.Text) ? DescribeCondition(condition) : condition.Text;
        }
        if (finish.Count == 0) allKills = false;

        // --- fail when another option is completed ----------------------------
        var fail = new JsonArray();
        index = 0;
        foreach (var sibling in siblings)
        {
            string failId = Ids.Derive(gameId + ":failwith:" + sibling);
            fail.Add(QuestCondition(failId, index++, "Quest", c =>
            {
                c["target"] = sibling;
                c["status"] = new JsonArray(4); // Success
                c["availableAfter"] = 0;
                c["dispersion"] = 0;
            }));
            locales[failId] = "Another option of this job was completed";
        }
        if (def.FailOnDeath)
        {
            // Fails when the player dies, goes missing or leaves a raid while the quest is active (it can be restarted).
            string deathId = Ids.Derive(gameId + ":failondeath");
            fail.Add(new JsonObject
            {
                ["id"] = deathId,
                ["index"] = index++,
                ["parentId"] = "",
                ["dynamicLocale"] = false,
                ["globalQuestCounterId"] = "",
                ["visibilityConditions"] = new JsonArray(),
                ["conditionType"] = "CounterCreator",
                ["type"] = "Completion",
                ["value"] = 1,
                ["oneSessionOnly"] = false,
                ["doNotResetIfCounterCompleted"] = false,
                ["completeInSeconds"] = 0,
                ["counter"] = new JsonObject
                {
                    ["id"] = Ids.Derive(deathId + ":counter"),
                    ["conditions"] = new JsonArray(new JsonObject
                    {
                        ["id"] = Ids.Derive(deathId + ":exit"),
                        ["dynamicLocale"] = false,
                        ["conditionType"] = "ExitStatus",
                        ["status"] = Strings(new[] { "Killed", "MissingInAction", "Left" }),
                    }),
                },
            });
            locales[deathId] = "Don't die, go missing or leave a raid";
        }

        // --- rewards (the same for every option) ------------------------------
        var success = new JsonArray();
        var started = new JsonArray();
        index = 0;
        foreach (var reward in def.Rewards)
        {
            if (!Ids.IsValid(reward.Id)) reward.Id = Ids.Derive(def.Id + ":reward:" + index);
            bool onStart = reward.OnStart && reward.Type == RewardTypes.Item;
            // Items given on accept: only on way A, or players could collect them once per way.
            if (onStart && !firstOption) continue;
            var node = BuildReward(file, reward, Sub(reward.Id), def.Id, onStart ? started.Count : success.Count);
            if (node == null) continue;
            (onStart ? started : success).Add(node);
            index++;
        }

        var quest = new JsonObject
        {
            ["_id"] = gameId,
            ["QuestName"] = name,
            ["templateId"] = gameId,
            ["traderId"] = file.Id,
            ["location"] = "any",
            ["image"] = image,
            ["type"] = allKills ? "Elimination" : "PickUp",
            ["side"] = "Pmc",
            ["isKey"] = false,
            ["restartable"] = def.FailOnDeath,
            ["instantComplete"] = false,
            ["secretQuest"] = false,
            ["canShowNotificationsInGame"] = true,
            ["name"] = $"{gameId} name",
            ["description"] = $"{gameId} description",
            ["note"] = $"{gameId} note",
            ["startedMessageText"] = $"{gameId} startedMessageText",
            ["successMessageText"] = $"{gameId} successMessageText",
            ["failMessageText"] = "", // no "task failed" message when another way is completed
            ["changeQuestMessageText"] = $"{gameId} changeQuestMessageText",
            ["acceptPlayerMessage"] = $"{gameId} acceptPlayerMessage",
            ["declinePlayerMessage"] = $"{gameId} declinePlayerMessage",
            ["completePlayerMessage"] = $"{gameId} completePlayerMessage",
            ["conditions"] = new JsonObject
            {
                ["AvailableForStart"] = start,
                ["AvailableForFinish"] = finish,
                ["Fail"] = fail,
            },
            ["rewards"] = new JsonObject
            {
                ["Started"] = started,
                ["Success"] = success,
                ["Fail"] = new JsonArray(),
            },
        };

        var parsed = jsonUtil.Deserialize<Quest>(quest.ToJsonString());
        if (parsed == null)
        {
            logger.Warning($"[CustomTraders] Quest {name} did not deserialize — skipped.");
            return false;
        }

        templateTable.Quests[questId] = parsed;
        AddLocales(locales);
        return true;
    }

    private static JsonObject QuestCondition(string id, int index, string type, Action<JsonObject> fill)
    {
        var node = new JsonObject
        {
            ["id"] = id,
            ["index"] = index,
            ["parentId"] = "",
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = "",
            ["visibilityConditions"] = new JsonArray(),
            ["conditionType"] = type,
        };
        fill(node);
        return node;
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());

    private JsonObject? BuildCondition(ConditionDef c, string id, int index)
    {
        var common = new JsonObject
        {
            ["id"] = id,
            ["index"] = index,
            ["parentId"] = "",
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = "",
            ["visibilityConditions"] = new JsonArray(),
        };

        var maps = c.Locations.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var wearing = c.WearingTpls.Where(t => IsKnownItem(t, "quest condition (wearing)")).ToList();

        // Parts of a counter ("do X N times, while Y, on map Z").
        JsonObject Counter(string key, string conditionType, Action<JsonObject>? fill = null)
        {
            var node = new JsonObject
            {
                ["id"] = Ids.Derive(id + ":" + key),
                ["dynamicLocale"] = false,
                ["conditionType"] = conditionType,
            };
            fill?.Invoke(node);
            return node;
        }

        JsonObject CounterCreator(string type, JsonArray counterConditions)
        {
            if (wearing.Count > 0)
            {
                counterConditions.Add(Counter("equipment", "Equipment", n =>
                {
                    // Any one of the listed items (each inner list is one set to wear).
                    n["equipmentInclusive"] = new JsonArray(wearing.Select(t => (JsonNode)new JsonArray(t)).ToArray());
                    n["equipmentExclusive"] = new JsonArray();
                    n["IncludeNotEquippedItems"] = false;
                }));
            }
            if (maps.Count > 0)
                counterConditions.Add(Counter("location", "Location", n => n["target"] = Strings(maps)));

            common["conditionType"] = "CounterCreator";
            common["type"] = type;
            common["value"] = Math.Max(1, c.Count);
            common["oneSessionOnly"] = c.OneRaid;
            common["doNotResetIfCounterCompleted"] = false;
            common["completeInSeconds"] = 0;
            common["counter"] = new JsonObject
            {
                ["id"] = Ids.Derive(id + ":counter"),
                ["conditions"] = counterConditions,
            };
            return common;
        }

        switch (c.Type)
        {
            case ConditionTypes.HandoverItem:
            case ConditionTypes.FindItem:
            {
                var targets = c.ItemTpls.Where(t => IsKnownItem(t, "quest condition")).ToList();
                if (targets.Count == 0) return null;

                bool money = targets.All(IsMoney);
                common["conditionType"] = c.Type;
                common["target"] = Strings(targets);
                common["value"] = Math.Max(1, c.Count);
                common["onlyFoundInRaid"] = c.FoundInRaid && !money;
                common["minDurability"] = 0;
                common["maxDurability"] = 100;
                common["dogtagLevel"] = 0;
                common["isEncoded"] = false;
                if (c.Type == ConditionTypes.FindItem) common["countInRaid"] = false;
                return common;
            }

            case ConditionTypes.Kill:
            {
                bool boss = c.KillTarget == "Boss";
                var roles = boss ? (c.BossRoles.Count > 0 ? c.BossRoles : KillTargets.Bosses.Select(b => b.Role).ToList()) : new List<string>();
                var weapons = c.WeaponTpls.Where(t => IsKnownItem(t, "quest condition (weapon)")).ToList();
                var kills = Counter("kills", "Kills", n =>
                {
                    n["target"] = boss ? "Savage" : string.IsNullOrWhiteSpace(c.KillTarget) ? "Any" : c.KillTarget;
                    n["value"] = 1;
                    n["compareMethod"] = ">=";
                    n["bodyPart"] = Strings(c.BodyParts.Where(p => !string.IsNullOrWhiteSpace(p)));
                    n["daytime"] = new JsonObject { ["from"] = Math.Clamp(c.DaytimeFrom, 0, 23), ["to"] = Math.Clamp(c.DaytimeTo, 0, 23) };
                    n["distance"] = new JsonObject { ["compareMethod"] = c.DistanceCompare == "<=" ? "<=" : ">=", ["value"] = Math.Max(0, c.Distance) };
                    n["enemyEquipmentExclusive"] = new JsonArray();
                    n["enemyEquipmentInclusive"] = new JsonArray();
                    n["enemyHealthEffects"] = new JsonArray();
                    n["resetOnSessionEnd"] = false;
                    n["savageRole"] = Strings(roles);
                    n["weapon"] = Strings(weapons);
                    n["weaponCaliber"] = Strings(c.Calibers.Where(x => !string.IsNullOrWhiteSpace(x)));
                    n["weaponModsExclusive"] = new JsonArray();
                    n["weaponModsInclusive"] = new JsonArray();
                });
                return CounterCreator("Elimination", new JsonArray { kills });
            }

            case ConditionTypes.Extract:
            {
                var statuses = c.ExitStatuses.Count > 0 ? c.ExitStatuses : new List<string> { "Survived", "Runner" };
                var exit = Counter("exit", "ExitStatus", n => n["status"] = Strings(statuses));
                return CounterCreator("Completion", new JsonArray { exit });
            }

            case ConditionTypes.UseItem:
            {
                var targets = c.ItemTpls.Where(t => IsKnownItem(t, "quest condition (use item)")).ToList();
                if (targets.Count == 0) return null;
                var use = Counter("use", "UseItem", n =>
                {
                    n["target"] = Strings(targets);
                    n["value"] = 1;
                    n["compareMethod"] = ">=";
                });
                return CounterCreator("Completion", new JsonArray { use });
            }

            case ConditionTypes.Skill:
            {
                if (string.IsNullOrWhiteSpace(c.Skill)) return null;
                common["conditionType"] = "Skill";
                common["target"] = c.Skill;
                common["value"] = Math.Max(1, c.Count);
                common["compareMethod"] = ">=";
                return common;
            }

            default:
                logger.Warning($"[CustomTraders] Unknown condition type '{c.Type}' — skipped.");
                return null;
        }
    }

    private static bool IsMoney(string tpl) => Currencies.IsCurrency(tpl) || tpl == Currencies.GpCoin || tpl == Currencies.LegaMedal;

    private JsonObject? BuildReward(TraderFile file, RewardDef r, string id, string gameQuestId, int index)
    {
        switch (r.Type)
        {
            case RewardTypes.Experience:
                return new JsonObject { ["id"] = id, ["index"] = index, ["type"] = "Experience", ["value"] = r.Value };

            case RewardTypes.TraderStanding:
                return new JsonObject { ["id"] = id, ["index"] = index, ["type"] = "TraderStanding", ["value"] = r.Value, ["target"] = file.Id };

            case RewardTypes.Skill:
                if (string.IsNullOrWhiteSpace(r.Skill)) return null;
                return new JsonObject { ["id"] = id, ["index"] = index, ["type"] = "Skill", ["target"] = r.Skill, ["value"] = r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) };

            case RewardTypes.StashRows:
                return new JsonObject { ["id"] = id, ["index"] = index, ["type"] = "StashRows", ["value"] = Math.Max(1, (int)r.Value).ToString() };

            case RewardTypes.Item:
            {
                if (!IsKnownItem(r.ItemTpl, "quest reward")) return null;
                var rootId = Ids.Derive(id + ":item");
                var tree = BuildItemTree(r.ItemTpl, rootId, true, null, null,
                    new JsonObject { ["StackObjectsCount"] = Math.Max(1, r.Count) });
                var items = new JsonArray();
                foreach (var node in tree) items.Add(node);
                return new JsonObject
                {
                    ["id"] = id,
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
                // The copy of the offer that this quest unlocks (all its ways share it), see BuildAssort.
                string entryId = _offerEntries.GetValueOrDefault((offer.Id, gameQuestId)) ?? offer.Id;
                return new JsonObject
                {
                    ["id"] = id,
                    ["index"] = index,
                    ["type"] = "AssortmentUnlock",
                    ["target"] = entryId,
                    ["traderId"] = file.Id,
                    ["loyaltyLevel"] = Math.Clamp(offer.LoyaltyLevel, 1, 4),
                    ["unknown"] = false,
                    ["items"] = new JsonArray(new JsonObject { ["_id"] = entryId, ["_tpl"] = offer.ItemTpl }),
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
        string where = c.Locations.Count > 0 ? " on " + string.Join(", ", c.Locations.Select(Maps.Name)) : "";
        string wearing = c.WearingTpls.Count > 0 ? " while wearing " + string.Join(" or ", c.WearingTpls.Select(ItemName)) : "";
        switch (c.Type)
        {
            case ConditionTypes.HandoverItem:
                return c.ItemTpls.All(IsMoney)
                    ? $"Hand over {items}"
                    : $"Hand over {(c.FoundInRaid ? "found in raid " : "")}{items}";
            case ConditionTypes.FindItem:
                return $"Find {items} in raid";
            case ConditionTypes.Kill:
            {
                string target = c.KillTarget switch
                {
                    "Savage" => "Scavs",
                    "AnyPmc" => "PMC operatives",
                    "Usec" => "USEC operatives",
                    "Bear" => "BEAR operatives",
                    "Boss" => c.BossRoles.Count > 0 ? string.Join(" or ", c.BossRoles.Select(KillTargets.BossName)) : "bosses",
                    _ => "enemies",
                };
                string with = c.WeaponTpls.Count > 0 ? " using " + string.Join(" or ", c.WeaponTpls.Select(ItemName)) : "";
                string caliber = c.Calibers.Count > 0 ? " with " + string.Join(" or ", c.Calibers.Select(CaliberName)) + " ammo" : "";
                string parts = c.BodyParts.Count > 0 ? " with shots to the " + string.Join(" or ", c.BodyParts.Select(BodyPartName)) : "";
                string distance = c.Distance > 0 ? (c.DistanceCompare == "<=" ? $" from within {c.Distance} m" : $" from at least {c.Distance} m") : "";
                string time = c.DaytimeFrom != c.DaytimeTo ? $" between {c.DaytimeFrom:00}:00 and {c.DaytimeTo:00}:00" : "";
                string raid = c.OneRaid ? " in a single raid" : "";
                return $"Eliminate {target}{with}{caliber}{parts}{distance}{wearing}{where}{time}{raid}";
            }
            case ConditionTypes.Extract:
                return $"Survive and extract{where}{wearing}{(c.OneRaid ? " in a single raid" : "")}";
            case ConditionTypes.Skill:
                return $"Reach level {c.Count} in the {c.Skill} skill";
            case ConditionTypes.UseItem:
                return $"Use {items} in raid{where}";
            default:
                return c.Type;
        }
    }

    private static string BodyPartName(string part) => part switch
    {
        "LeftArm" => "left arm",
        "RightArm" => "right arm",
        "LeftLeg" => "left leg",
        "RightLeg" => "right leg",
        _ => part.ToLowerInvariant(),
    };

    /// <summary>"Caliber556x45NATO" -> "5.56x45".</summary>
    private static string CaliberName(string caliber)
    {
        var s = caliber.StartsWith("Caliber") ? caliber[7..] : caliber;
        return s.Replace("NATO", "").Replace("PARA", "");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Normal CustomTraders messages in bright blue so they stand out in the
    /// busy server console. Warnings stay yellow and errors red.
    /// </summary>
    private void LogBlue(string message) =>
        logger.LogWithColor(message, Spectre.Console.Color.DodgerBlue1, null, null);

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

    /// <summary>8 hex characters that change whenever the file's content changes.</summary>
    private static string FileFingerprint(string path)
    {
        var hash = System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(path));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
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
        LogBlue($"[CustomTraders] Created example trader 'Iron' in {folder} — edit it with CustomTraders.Editor.exe");
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
