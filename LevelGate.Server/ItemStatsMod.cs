using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Path = System.IO.Path;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace LevelGate.Server;

// Item stat edits made in the LevelGate Editor ("Item Stats"), kept in
// user\mods\LevelGate\item_stats.json:
//   { "<tpl>": { "medUseTime": 2, "MaxHpResource": 400, "hpResourceRate": 150,
//                "foodUseTime": 3, "MaxResource": 1,
//                "effects_health": { "Energy": { "value": 10 } },
//                "effects_damage": { "Pain": { "delay": 0, "duration": 120, "fadeOut": 5 } },
//                "buffName": "BuffsPropital", "effects_buffs": [ { "BuffType": "HealthRate", ... } ] } }
// Only the fields present are changed; effects lists are replaced as a whole.
// Runs late (after other mods such as BalancedMeds / Item Property Backport),
// so these edits win. Restart the server to apply changes.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 10000)]
public class ItemStatsMod(
    ISptLogger<ItemStatsMod> logger,
    ModHelper modHelper,
    JsonUtil jsonUtil,
    TemplateTable templateTable,
    GlobalTable globalTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var file = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "item_stats.json");
        if (!File.Exists(file)) return Task.CompletedTask;

        JsonObject? all;
        try
        {
            all = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        }
        catch (Exception e)
        {
            logger.Error($"[LevelGate] item_stats.json couldn't be read, no item stats changed: {e.Message}");
            return Task.CompletedTask;
        }
        if (all == null) return Task.CompletedTask;

        int done = 0;
        foreach (var (tpl, node) in all)
        {
            if (node is not JsonObject edit) continue;
            if (!templateTable.Items.TryGetValue(tpl, out var item) || item.Properties == null)
            {
                logger.Warning($"[LevelGate] item_stats.json: no item {tpl} in the database, skipped.");
                continue;
            }
            try
            {
                Apply(tpl, item.Properties, edit);
                done++;
            }
            catch (Exception e)
            {
                logger.Error($"[LevelGate] item_stats.json: {tpl} not changed — {e.Message}");
            }
        }
        logger.Info($"[LevelGate] Item stats: {done} item(s) changed from item_stats.json.");
        return Task.CompletedTask;
    }

    private void Apply(string tpl, TemplateItemProperties p, JsonObject e)
    {
        if (Num(e["medUseTime"]) is double medUse) p.MedUseTime = medUse;
        if (Num(e["MaxHpResource"]) is double hp) p.MaxHpResource = (int)Math.Round(hp);
        if (Num(e["hpResourceRate"]) is double rate) p.HpResourceRate = rate;
        if (Num(e["foodUseTime"]) is double foodUse) p.FoodUseTime = foodUse;
        if (Num(e["MaxResource"]) is double res) p.MaxResource = (int)Math.Round(res);
        if (e.ContainsKey("effects_health"))
            p.EffectsHealth = jsonUtil.Deserialize<Dictionary<HealthFactor, EffectsHealthProperties>>(AsObject(e["effects_health"]))
                              ?? new Dictionary<HealthFactor, EffectsHealthProperties>();
        if (e.ContainsKey("effects_damage"))
            p.EffectsDamage = jsonUtil.Deserialize<Dictionary<DamageEffectType, EffectsDamageProperties>>(AsObject(e["effects_damage"]))
                              ?? new Dictionary<DamageEffectType, EffectsDamageProperties>();
        if (e["effects_buffs"] is JsonArray list)
        {
            // buff lists live in globals by name (several items can share one)
            string name = (string?)e["buffName"] is { Length: > 0 } n ? n : p.StimulatorBuffs is { Length: > 0 } s ? s : $"LevelGate_{tpl}";
            var buffs = jsonUtil.Deserialize<List<Buff>>(list.ToJsonString()) ?? new List<Buff>();
            globalTable.Configuration.Health.Effects.Stimulator.Buffs[name] = buffs;
            p.StimulatorBuffs = buffs.Count > 0 ? name : p.StimulatorBuffs;
        }
    }

    /// <summary>An empty list ([]) in the file means "none".</summary>
    private static string AsObject(JsonNode? n) => n is JsonObject o ? o.ToJsonString() : "{}";

    private static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : null;
}
