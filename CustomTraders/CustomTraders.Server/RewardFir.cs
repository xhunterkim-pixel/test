using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace CustomTraders.Server;

/// <summary>
/// SPT marks every quest reward item Found in Raid (RewardHelper.ProcessReward
/// ignores the reward's findInRaid flag). For item rewards of our quests with
/// "Found in Raid" switched off, this takes the status off again afterwards.
/// Vanilla and other mods' quests are left alone.
///
/// Hooked with the server's own Harmony (found by name, so no version is
/// pinned); if it can't be found the rewards just stay Found in Raid.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 150)]
public sealed class CustomTradersRewardFir(ISptLogger<CustomTradersRewardFir> logger) : IOnLoad
{
    /// <summary>Reward ids (filled while the quests are built) whose items must not be Found in Raid.</summary>
    internal static readonly HashSet<string> NotFoundInRaid = new();
    private static bool _patched;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (_patched || NotFoundInRaid.Count == 0) return Task.CompletedTask;
        try
        {
            var harmonyType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType("HarmonyLib.Harmony", false); } catch { return null; } })
                .FirstOrDefault(t => t != null);
            if (harmonyType == null)
            {
                try { harmonyType = Assembly.Load("0Harmony").GetType("HarmonyLib.Harmony"); } catch { }
            }
            var target = typeof(RewardHelper).GetMethod("ProcessReward", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var methodType = harmonyType?.Assembly.GetType("HarmonyLib.HarmonyMethod");
            var patch = harmonyType?.GetMethods().FirstOrDefault(m => m.Name == "Patch" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(MethodBase)
                && m.GetParameters().Any(p => p.Name == "postfix"));
            if (target == null || methodType == null || patch == null)
            {
                logger.Warning($"[CustomTraders] Couldn't hook quest rewards ({(target == null ? "RewardHelper.ProcessReward not found" : "Harmony not found")}) — rewards with Found in Raid off will still arrive Found in Raid.");
                return Task.CompletedTask;
            }
            var postfix = Activator.CreateInstance(methodType, typeof(CustomTradersRewardFir).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
            var harmony = Activator.CreateInstance(harmonyType!, "com.xhunterkim.customtraders.rewardfir");
            var ps = patch.GetParameters();
            var args = new object?[ps.Length];
            args[0] = target;
            for (int i = 1; i < ps.Length; i++) args[i] = ps[i].Name == "postfix" ? postfix : ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            patch.Invoke(harmony, args);
            _patched = true;
        }
        catch (Exception e)
        {
            logger.Warning($"[CustomTraders] Couldn't hook quest rewards ({e.GetBaseException().Message}) — rewards with Found in Raid off will still arrive Found in Raid.");
        }
        return Task.CompletedTask;
    }

    private static void Postfix(Reward reward, List<Item> __result)
    {
        if (__result == null || !NotFoundInRaid.Contains(reward.Id.ToString())) return;
        foreach (var item in __result)
            if (item.Upd != null) item.Upd.SpawnedInSession = false;
    }
}
