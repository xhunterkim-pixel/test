using System.Text.Json;
using ModernEditor.Shared;

namespace ModernEditor.Editor;

/// <summary>
/// 2.0.8: turns another trader (a game trader or another mod's) into a Modern Editor trader: its base info (name, nickname,
/// surname, location, currency, unlocked, loyalty levels, what it buys), everything it sells (assort.json: item, price or
/// barter, loyalty level, stock, buy limit) and its picture. Quests aren't read. The trader gets a new id and new offer ids,
/// so it can't clash with the original if that mod stays installed.
/// </summary>
public static class TraderImport
{
    private const string AnyItem = "54009119af1c881c07000029";

    public sealed record Result(TraderFile File, string? AvatarPath, List<string> Notes);

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonDocument Read(string path)
    {
        var text = File.ReadAllText(path).TrimStart('﻿');
        return JsonDocument.Parse(text, Lenient);
    }

    private static string S(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double N(JsonElement e, string key, double fallback = 0) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
    private static bool B(JsonElement e, string key, bool fallback) => e.TryGetProperty(key, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;

    /// <param name="basePath">the trader's base.json (assort.json is looked for next to it)</param>
    /// <param name="isWeapon">is this item template a gun (then an offer with parts sells the assembled preset)</param>
    public static Result Load(string basePath, Func<string, bool> isWeapon, Func<string, bool> known)
    {
        var notes = new List<string>();
        var dir = Path.GetDirectoryName(basePath)!;
        var file = new TraderFile { Id = Ids.New(), Avatar = "avatar.png" };
        string avatarRef = "";
        using (var doc = Read(basePath))
        {
            var b = doc.RootElement;
            if (!b.TryGetProperty("nickname", out _) && !b.TryGetProperty("loyaltyLevels", out _))
                throw new InvalidOperationException("That file isn't a trader's base.json (no nickname or loyaltyLevels in it).");
            file.Nickname = S(b, "nickname");
            file.Name = S(b, "name") is { Length: > 0 } n ? n : file.Nickname;
            if (file.Nickname.Length == 0) file.Nickname = file.Name;
            file.Surname = S(b, "surname");
            file.Location = S(b, "location");
            file.Currency = S(b, "currency") is "USD" or "EUR" ? S(b, "currency") : "RUB";
            file.UnlockedByDefault = B(b, "unlockedByDefault", true);
            if (!file.UnlockedByDefault) notes.Add("It was locked at the start in the original: pick the quest that unlocks it on the Trader page (Unlocked By), or switch Unlocked on.");
            avatarRef = S(b, "avatar");
            if (b.TryGetProperty("loyaltyLevels", out var lls) && lls.ValueKind == JsonValueKind.Array && lls.GetArrayLength() > 0)
            {
                file.LoyaltyLevels = lls.EnumerateArray().Take(4).Select(l => new LoyaltyLevelDef
                {
                    MinLevel = Math.Clamp((int)N(l, "minLevel", 1), 1, 79),
                    MinSalesSum = (long)N(l, "minSalesSum"),
                    MinStanding = N(l, "minStanding"),
                    BuyPriceCoef = Math.Clamp((int)N(l, "buy_price_coef", 50), 0, 100),
                }).ToList();
            }
            // what players can sell to it
            if (b.TryGetProperty("items_buy", out var ib) && ib.TryGetProperty("category", out var cats) && cats.ValueKind == JsonValueKind.Array)
            {
                var list = cats.EnumerateArray().Select(c => c.GetString() ?? "").ToList();
                file.Buys = list.Contains(AnyItem) ? "Everything" : list.Count == 0 && !(ib.TryGetProperty("id_list", out var il) && il.GetArrayLength() > 0) ? "Nothing" : "Default";
            }
        }

        // what it sells
        var assortPath = Path.Combine(dir, "assort.json");
        if (!File.Exists(assortPath)) notes.Add("No assort.json next to base.json: the trader was imported without offers.");
        else
        {
            using var doc = Read(assortPath);
            var a = doc.RootElement;
            if (a.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                a.TryGetProperty("barter_scheme", out var schemes);
                a.TryGetProperty("loyal_level_items", out var loyal);
                var hasChildren = new HashSet<string>(items.EnumerateArray().Select(i => S(i, "parentId")));
                int multi = 0, unknown = 0;
                foreach (var it in items.EnumerateArray())
                {
                    if (S(it, "parentId") != "hideout") continue; // parts of an assembled gun hang under their offer
                    string id = S(it, "_id"), tpl = S(it, "_tpl");
                    if (tpl.Length == 0) continue;
                    var offer = new OfferDef { Id = Ids.New(), ItemTpl = tpl, UseDefaultPreset = false };
                    // 2.0.8: sold assembled (a gun with parts, armor with plates): keep that exact build
                    if (hasChildren.Contains(id))
                    {
                        var all = items.EnumerateArray().ToList();
                        var queue = new Queue<string>(); queue.Enqueue(id);
                        while (queue.Count > 0)
                        {
                            var pid = queue.Dequeue();
                            foreach (var ch in all.Where(x => S(x, "parentId") == pid))
                            {
                                string cid = S(ch, "_id");
                                offer.Parts.Add(new PresetPart { Id = cid, Tpl = S(ch, "_tpl"), ParentId = pid == id ? "root" : pid, SlotId = S(ch, "slotId") });
                                queue.Enqueue(cid);
                            }
                        }
                        if (offer.Parts.Count > 0) offer.BuildName = "Imported";
                    }
                    if (!known(tpl)) unknown++;
                    if (loyal.ValueKind == JsonValueKind.Object && loyal.TryGetProperty(id, out var ll) && ll.ValueKind == JsonValueKind.Number)
                        offer.LoyaltyLevel = Math.Clamp(ll.GetInt32(), 1, 4);
                    if (it.TryGetProperty("upd", out var upd) && upd.ValueKind == JsonValueKind.Object)
                    {
                        offer.Unlimited = B(upd, "UnlimitedCount", false);
                        offer.Stock = Math.Max(1, (int)Math.Min(N(upd, "StackObjectsCount", 1), 1_000_000));
                        if (upd.TryGetProperty("BuyRestrictionMax", out var br) && br.ValueKind == JsonValueKind.Number) offer.BuyLimit = Math.Max(0, br.GetInt32());
                    }
                    if (schemes.ValueKind == JsonValueKind.Object && schemes.TryGetProperty(id, out var scheme) && scheme.ValueKind == JsonValueKind.Array && scheme.GetArrayLength() > 0)
                    {
                        if (scheme.GetArrayLength() > 1) multi++;
                        foreach (var c in scheme[0].EnumerateArray())
                        {
                            var ctpl = S(c, "_tpl");
                            if (ctpl.Length > 0) offer.Cost.Add(new CostDef { ItemTpl = ctpl, Count = Math.Max(1, N(c, "count", 1)) });
                        }
                    }
                    file.Offers.Add(offer);
                }
                if (multi > 0) notes.Add($"{multi} offer(s) had more than one way to pay: the first one was kept.");
                if (unknown > 0) notes.Add($"{unknown} offer(s) sell items the editor doesn't know (another mod's items?): add that mod on the Mods page, or the checks will flag them.");
            }
        }

        // its picture: base.json says e.g. "/files/trader/avatar/vafelz.jpg" — look for that file name near base.json
        string? avatar = FindPicture(dir, Path.GetFileName(avatarRef));
        if (avatar == null && avatarRef.Length > 0) notes.Add($"Its picture ({Path.GetFileName(avatarRef)}) wasn't found near base.json: use Choose Icon From PC… on the Trader page.");
        notes.Insert(0, $"Imported {file.Nickname}: {file.Offers.Count} offer(s), {file.LoyaltyLevels.Count} loyalty level(s){(avatar != null ? ", its picture" : "")}. Quests aren't imported.");
        notes.Add("If the original trader mod is still installed, switch it off or remove it, or the game will have both.");
        return new Result(file, avatar, notes);
    }

    /// <summary>The trader's picture: the named file in the folder, its parents (a mod's root) and their sub-folders; else any
    /// avatar / trader picture right next to base.json.</summary>
    private static string? FindPicture(string dir, string name)
    {
        var exts = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
        var d = new DirectoryInfo(dir);
        for (int up = 0; up < 5 && d != null; up++, d = d.Parent)
        {
            if (up > 0 && d.Name is "mods" or "user" or "SPT_Runtime" or "SPT") break; // never search the whole SPT folder
            if (name.Length > 0)
            {
                try
                {
                    var hit = d.EnumerateFiles(name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault();
                    if (hit != null) return hit.FullName;
                }
                catch { }
            }
            if (up == 0)
                foreach (var f in d.EnumerateFiles())
                    if (exts.Contains(f.Extension.ToLowerInvariant())) return f.FullName;
        }
        return null;
    }
}
