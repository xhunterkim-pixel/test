using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace CustomTraders.Server;

// -----------------------------------------------------------------------------
// Quests with several ways (options A-D).
//
// Every way is its own game quest. The game lists FAILED quests forever, so
// instead of leaving the other ways "Task failed" on the trader's page, they
// are marked COMPLETED (no rewards) the moment one way is turned in:
// completed quests are hidden unless "Show completed" is ticked, and quests
// that require this one still unlock (they accept Success).
//
// Each way also keeps a "fail when another way succeeds" condition, so even
// without this handler nothing breaks — the other ways would just show as
// failed.
// -----------------------------------------------------------------------------

/// <summary>Which game quests are ways of the same quest (filled in while quests are added).</summary>
public static class QuestOptions
{
    private static readonly Dictionary<string, List<string>> Groups = new();

    public static void Register(List<string> gameQuestIds)
    {
        if (gameQuestIds.Count < 2) return;
        foreach (var id in gameQuestIds) Groups[id] = gameQuestIds;
    }

    public static IEnumerable<List<string>> AllGroups => Groups.Values.Distinct();

    /// <summary>
    /// After <paramref name="completedId"/> was turned in: mark its other ways
    /// completed and tell the game right away (in the same response).
    /// Returns how many ways were closed.
    /// </summary>
    public static int CloseOtherWays(PmcData pmc, string completedId, ItemEventRouterResponse? response, MongoId sessionId, double now)
    {
        if (!Groups.TryGetValue(completedId, out var group) || pmc.Quests == null) return 0;
        if (pmc.Quests.FirstOrDefault(q => q.QId.ToString() == completedId)?.Status != QuestStatusEnum.Success) return 0; // not actually completed

        ProfileChange? changes = null;
        if (response?.ProfileChanges != null) response.ProfileChanges.TryGetValue(sessionId, out changes);

        int closed = 0;
        foreach (var sibling in group.Where(id => id != completedId))
        {
            var status = MarkDone(pmc, sibling, now);
            if (status == null) continue;
            closed++;
            if (changes != null)
            {
                changes.QuestsStatus ??= new List<QuestStatus>();
                changes.QuestsStatus.RemoveAll(q => q.QId.ToString() == sibling);
                changes.QuestsStatus.Add(status);
            }
        }
        return closed;
    }

    /// <summary>Sets the quest to completed (Success) in the profile; null if it already was.</summary>
    private static QuestStatus? MarkDone(PmcData pmc, string questId, double now)
    {
        var status = pmc.Quests!.FirstOrDefault(q => q.QId.ToString() == questId);
        if (status == null)
        {
            status = new QuestStatus
            {
                QId = questId,
                StartTime = now,
                Status = QuestStatusEnum.Success,
                StatusTimers = new Dictionary<QuestStatusEnum, double> { [QuestStatusEnum.Success] = now },
            };
            pmc.Quests.Add(status);
            return status;
        }
        if (status.Status == QuestStatusEnum.Success) return null;
        status.Status = QuestStatusEnum.Success;
        status.StatusTimers ??= new Dictionary<QuestStatusEnum, double>();
        status.StatusTimers[QuestStatusEnum.Success] = now;
        return status;
    }
}

/// <summary>
/// Handles "turn in quest" before SPT's own quest router (lower TypePriority
/// = registered first = asked first): lets SPT complete the quest exactly as
/// usual, then closes the other ways of a multi-way quest.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers - 1000)]
public sealed class CustomTradersQuestRouter(QuestCallbacks questCallbacks, TimeUtil timeUtil) : ItemEventRouter(new ItemRouteAction[]
{
    new ItemRouteAction<CompleteQuestRequestData>("QuestComplete",
        async (url, pmcData, body, sessionId, output, cancellationToken) =>
        {
            var response = await questCallbacks.CompleteQuest(pmcData, body, sessionId);
            QuestOptions.CloseOtherWays(pmcData, body.QuestId.ToString(), response, sessionId, timeUtil.GetTimeStamp());
            return response;
        }),
});

/// <summary>
/// At server start: profiles where a way was completed before this fix (the
/// other ways show "Task failed") get those ways closed properly too.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 100)]
public sealed class CustomTradersProfileFix(SaveServer saveServer, TimeUtil timeUtil, ISptLogger<CustomTradersProfileFix> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        int fixedWays = 0;
        try
        {
            foreach (var profile in saveServer.GetProfiles().Values)
            {
                var pmc = profile.CharacterData?.PmcData;
                if (pmc?.Quests == null) continue;
                foreach (var group in QuestOptions.AllGroups)
                {
                    var done = group.FirstOrDefault(id => pmc.Quests.Any(q => q.QId.ToString() == id && q.Status == QuestStatusEnum.Success));
                    if (done != null) fixedWays += QuestOptions.CloseOtherWays(pmc, done, null, default, timeUtil.GetTimeStamp());
                }
            }
        }
        catch (Exception e)
        {
            logger.Warning($"[CustomTraders] Could not check profiles for finished quest ways: {e.Message}");
        }
        if (fixedWays > 0)
            logger.LogWithColor($"[CustomTraders] Closed {fixedWays} leftover quest way(s) in player profiles (they showed as failed).", Spectre.Console.Color.DodgerBlue1, null, null);
        return Task.CompletedTask;
    }
}
