using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace CustomTraders.Server;

/// <summary>
/// Trader order ("Priority" on the Trader page). SPT sorts the trader list by id before sending
/// it to the game (TraderHelper.GetAllTraders). Every router that handles a url runs, in load
/// order, and the last answer is sent — so this one runs after SPT's own TraderStaticRouter
/// and answers with the same list, our traders moved to the place they asked for.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1000)]
public sealed class TraderOrderRouter(JsonUtil jsonUtil, TraderHelper traderHelper, HttpResponseUtil httpResponseUtil) : StaticRouter(jsonUtil, new RouteAction[]
{
    new StreamedRouteAction<EmptyRequestData>("/client/trading/api/traderSettings",
        (url, info, sessionId, cancellationToken) =>
        {
            var list = traderHelper.GetAllTraders(sessionId);
            var wanted = list.Where(t => CustomTradersMod.TraderPriorities.ContainsKey(t.Id.ToString()))
                .OrderBy(t => CustomTradersMod.TraderPriorities[t.Id.ToString()]).ToList();
            if (wanted.Count > 0)
            {
                foreach (var t in wanted) list.Remove(t);
                foreach (var t in wanted)
                    list.Insert(Math.Clamp(CustomTradersMod.TraderPriorities[t.Id.ToString()] - 1, 0, list.Count), t);
            }
            return new ValueTask<StreamedJsonBody>(httpResponseUtil.GetStreamedBody(list));
        }),
});

/// <summary>
/// Random prices: offers with a min / max price get a new price after every restock. SPT's
/// restock only refills the stock (TraderAssortHelper.ResetExpiredTrader), so this watches each
/// trader's next-restock time and re-rolls those prices when it moves on.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 200)]
public sealed class TraderRestockPrices(TradersTable tradersTable) : IOnUpdate
{
    private readonly Dictionary<string, double> _lastResupply = new();

    public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken)
    {
        if (secondsSinceLastRun < 10 || CustomTradersMod.RandomPrices.Count == 0) return Task.FromResult(false);
        foreach (var group in CustomTradersMod.RandomPrices.GroupBy(r => r.TraderId))
        {
            if (!tradersTable.TryGetValue(group.Key, out var trader) || trader?.Assort?.BarterScheme == null) continue;
            double next = trader.Base?.NextResupply ?? 0;
            if (_lastResupply.TryGetValue(group.Key, out var last) && last == next) continue;
            bool first = !_lastResupply.ContainsKey(group.Key);
            _lastResupply[group.Key] = next;
            if (first) continue; // prices were rolled at server start
            foreach (var r in group)
            {
                if (!trader.Assort.BarterScheme.TryGetValue(r.EntryId, out var schemes) || schemes.Count == 0) continue;
                var cost = schemes[0];
                if (r.CostIndex < cost.Count) cost[r.CostIndex].Count = CustomTradersMod.RollPrice(r.Min, r.Max, r.Multiplier);
            }
        }
        return Task.FromResult(true);
    }
}
