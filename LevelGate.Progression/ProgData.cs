using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace LevelGate.Progression
{
    /// <summary>One item on the track.</summary>
    internal sealed class ProgItem
    {
        public string Tpl;
        public int Level;
        public string Name;
        public string Short;
        public string Group;
    }

    /// <summary>
    /// The level list (LevelGate's level_requirements.json, read only), the game's item
    /// templates (for names and categories) and the player's level.
    /// </summary>
    internal static class ProgData
    {
        public const int MaxLevel = 79;

        /// <summary>Category key, name, color, base class ids (the nearest ancestor that is listed wins).</summary>
        public static readonly (string Key, string Name, string Color, string[] Classes)[] Groups =
        {
            ("Weapons", "Weapons", "#f15e6c", new[] { "5422acb9af1c889c16000029" }),
            ("Melee", "Melee", "#ff8a65", new[] { "5447e1d04bdc2dff2f8b4567" }),
            ("Grenades", "Grenades", "#ffa42b", new[] { "543be6564bdc2df4348b4568" }),
            ("Ammo", "Ammo", "#f5cd46", new[] { "5485a8684bdc2da71d8b4567" }),
            ("AmmoPacks", "Ammo Packs", "#d9b44a", new[] { "543be5cb4bdc2deb348b4568" }),
            ("WeaponParts", "Weapon Parts", "#c7a36b", new[] { "5448fe124bdc2da5018b4567" }),
            ("Armor", "Armor", "#509bf5", new[] { "5448e54d4bdc2dcc718b4568", "644120aa86ffbe10ee032b6f" }),
            ("Headwear", "Headwear", "#6fb3ff", new[] { "5a341c4086f77401f2541505" }),
            ("Rigs", "Rigs", "#7d9cf0", new[] { "5448e5284bdc2dcb718b4567" }),
            ("Backpacks", "Backpacks", "#a082ff", new[] { "5448e53e4bdc2d60728b4567" }),
            ("Gear", "Other Gear", "#b39ddb", new[] { "543be5f84bdc2dd4348b456a", "57bef4c42459772e8d35a53b" }),
            ("Medical", "Medical", "#1ed760", new[] { "543be5664bdc2dd4348b4569" }),
            ("Food", "Food & Drink", "#8bd66b", new[] { "5448e8d04bdc2ddf718b4569", "5448e8d64bdc2dce718b4568" }),
            ("Electronics", "Electronics", "#4dd0e1", new[] { "57864a66245977548f04a81f" }),
            ("Barter", "Barter Items", "#bdbdbd", new[] { "5448eb774bdc2d0a728b4567", "5448ecbe4bdc2d60728b4568", "616eb7aea207f41933308f46" }),
            ("Keys", "Keys", "#e0c068", new[] { "543be5e94bdc2df1348b4568" }),
            ("Containers", "Containers", "#90a4ae", new[] { "5795f317245977243854e041", "5671435f4bdc2d96058b4569" }),
            ("Special", "Special", "#ff7ab6", new[] { "5447e0e74bdc2d3c308b4567", "567849dd4bdc2d150f8b456e" }),
            ("Other", "Other", "#8a8a8a", new string[0]),
        };
        private static readonly Dictionary<string, string> GroupOfClass = Groups.SelectMany(g => g.Classes.Select(c => (c, g.Key))).ToDictionary(x => x.c, x => x.Key);

        public static string ConfigPath;
        public static readonly Dictionary<string, int> Levels = new Dictionary<string, int>();
        public static event Action Changed;
        private static DateTime _stamp;
        private static float _nextCheck;

        // ---------------------------------------------------------------- the level list

        public static void Init()
        {
            ConfigPath = FindConfig();
            if (ConfigPath == null) L.Warn($"LevelGate's level_requirements.json wasn't found under {Paths.PluginPath} — the screen will be empty until it exists (checked every few seconds).");
            else L.Info("level list: " + ConfigPath);
            Load("start");
        }

        private static string FindConfig()
        {
            var direct = Path.Combine(Path.Combine(Path.Combine(Paths.PluginPath, "LevelGate"), "config"), "level_requirements.json");
            if (File.Exists(direct)) return direct;
            L.Debug("not at " + direct + " — searching the plugins folder");
            try
            {
                var found = Directory.GetFiles(Paths.PluginPath, "level_requirements.json", SearchOption.AllDirectories);
                foreach (var f in found) L.Debug("  found: " + f);
                return found.FirstOrDefault();
            }
            catch (Exception e) { L.Error("searching for level_requirements.json", e); return null; }
        }

        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = Time.realtimeSinceStartup + 2f;
            if (ConfigPath == null) { ConfigPath = FindConfig(); if (ConfigPath == null) return; L.Info("level list appeared: " + ConfigPath); }
            try
            {
                var stamp = File.GetLastWriteTimeUtc(ConfigPath);
                if (stamp != _stamp) Load("file changed on disk");
            }
            catch (Exception e) { L.ErrorOnce("checking the level list", e); }
        }

        private static readonly Regex Pair = new Regex("\"([0-9a-fA-F]{24})\"\\s*:\\s*\"?(\\d+)\"?", RegexOptions.Compiled);

        private static void Load(string why)
        {
            if (ConfigPath == null || !File.Exists(ConfigPath)) return;
            try
            {
                _stamp = File.GetLastWriteTimeUtc(ConfigPath);
                string json = File.ReadAllText(ConfigPath);
                // only the "items" block counts ("_examples" holds ids too)
                int at = json.IndexOf("\"items\"", StringComparison.Ordinal);
                if (at < 0) { L.Warn("no \"items\" block in " + ConfigPath); return; }
                int open = json.IndexOf('{', at), depth = 0, end = -1;
                for (int i = open; i >= 0 && i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}' && --depth == 0) { end = i; break; }
                }
                string block = end > open ? json.Substring(open, end - open + 1) : "";
                Levels.Clear();
                foreach (Match m in Pair.Matches(block))
                    if (int.TryParse(m.Groups[2].Value, out int lvl) && lvl > 0) Levels[m.Groups[1].Value.ToLowerInvariant()] = Math.Min(lvl, MaxLevel);
                var by = Levels.Values.GroupBy(v => v).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
                L.Info($"level list loaded ({why}): {Levels.Count} limited item(s)" + (Levels.Count > 0 ? $", levels {Levels.Values.Min()}–{Levels.Values.Max()}" : ""));
                L.Debug("items per level: " + string.Join(" ", by.ToArray()));
                _byLevel = null;
                Changed?.Invoke();
            }
            catch (Exception e) { L.Error("reading " + ConfigPath, e); }
        }

        // ---------------------------------------------------------------- per level

        private static Dictionary<int, List<ProgItem>> _byLevel;
        private static bool _byLevelFree;

        public static List<ProgItem> ItemsAt(int level)
        {
            bool free = ProgressionPlugin.FreeItemsAtLevel1.Value;
            if (_byLevel == null || _byLevelFree != free || (!_templatesTried && Templates() != null)) Rebuild(free);
            return _byLevel.TryGetValue(level, out var list) ? list : new List<ProgItem>();
        }

        public static int CountAt(int level) => ItemsAt(level).Count;

        /// <summary>Rebuild the per-level lists on next use (names / categories read again).</summary>
        public static void Invalidate() { _byLevel = null; }

        private static void Rebuild(bool free)
        {
            var t0 = Time.realtimeSinceStartup;
            _byLevel = new Dictionary<int, List<ProgItem>>();
            _byLevelFree = free;
            foreach (var kv in Levels) Add(kv.Key, kv.Value);
            if (free)
            {
                var all = Templates();
                if (all != null) foreach (var id in all.Keys) if (!Levels.ContainsKey(id) && IsRealItem(id)) Add(id, 1);
            }
            foreach (var list in _byLevel.Values) list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            L.Debug($"per-level lists rebuilt in {(Time.realtimeSinceStartup - t0) * 1000:0} ms ({_byLevel.Count} levels, templates {(Templates() == null ? "not loaded yet" : Templates().Count + " known")}, free items at level 1: {free})");
        }

        private static void Add(string tpl, int level)
        {
            if (!_byLevel.TryGetValue(level, out var list)) _byLevel[level] = list = new List<ProgItem>();
            list.Add(new ProgItem { Tpl = tpl, Level = level, Name = NameOf(tpl), Short = ShortOf(tpl), Group = GroupOf(tpl) });
        }

        // ---------------------------------------------------------------- the game's item templates

        private static bool _templatesTried;
        private static Dictionary<string, string> _parents;   // tpl -> parent tpl
        private static float _nextTemplateTry;

        /// <summary>id -> parent id of every item template, once the game has loaded them (after login).</summary>
        public static Dictionary<string, string> Templates()
        {
            if (_parents != null) return _parents;
            if (Time.realtimeSinceStartup < _nextTemplateTry) return null;
            _nextTemplateTry = Time.realtimeSinceStartup + 5f;
            _templatesTried = true;
            try
            {
                var dict = FindTemplateDictionary(out string from);
                if (dict == null) return null;
                var parents = new Dictionary<string, string>();
                string parentMember = null;
                foreach (DictionaryEntry e in dict)
                {
                    string id = e.Key?.ToString()?.ToLowerInvariant();
                    if (id == null || e.Value == null) continue;
                    object p = null;
                    foreach (var name in parentMember != null ? new[] { parentMember } : new[] { "_parent", "ParentId", "parentId", "Parent" })
                    {
                        p = Refl.Get(e.Value, name);
                        if (p != null) { parentMember ??= name; break; }
                    }
                    // Parent can be the parent template itself: its id then
                    if (p != null && !(p is string) && p.GetType().Name != "MongoID") p = Refl.Get(p, "_id") ?? Refl.Get(p, "Id") ?? p;
                    parents[id] = p?.ToString()?.ToLowerInvariant() ?? "";
                }
                L.Info($"item templates: {parents.Count} from {from} (parent read from '{parentMember ?? "nothing!"}')");
                if (parentMember == null) L.Warn("couldn't read item parents — every item will be listed under Other. Send the log with Debug > VerboseLog on.");
                _parents = parents;
                _byLevel = null;
                return _parents;
            }
            catch (Exception e) { L.ErrorOnce("reading item templates", e); return null; }
        }

        private static List<Type> _factoryTypes;

        private static IDictionary FindTemplateDictionary(out string from)
        {
            from = null;
            var singleton = AccessTools.TypeByName("Comfort.Common.Singleton`1");
            if (singleton == null) { L.Warn("Comfort.Common.Singleton not found"); return null; }
            if (_factoryTypes == null)
            {
                _factoryTypes = new List<Type>();
                var factory = AccessTools.TypeByName("ItemFactoryClass");
                if (factory != null) _factoryTypes.Add(factory);
                else
                {
                    L.Debug("ItemFactoryClass not found by name — looking for a class with an ItemTemplates member");
                    var asm = AccessTools.TypeByName("EFT.InventoryLogic.Item")?.Assembly;
                    if (asm != null)
                        foreach (var t in AccessTools.GetTypesFromAssembly(asm))
                            if (!t.IsGenericTypeDefinition && (t.GetField("ItemTemplates", Refl.All) != null || t.GetProperty("ItemTemplates", Refl.All) != null)) _factoryTypes.Add(t);
                }
                L.Info("item template source(s): " + (_factoryTypes.Count == 0 ? "none found — every item goes under Other" : string.Join(", ", _factoryTypes.Select(t => t.FullName).ToArray())));
            }
            foreach (var t in _factoryTypes)
            {
                object instance = null;
                try
                {
                    var st = singleton.MakeGenericType(t);
                    var instantiated = st.GetProperty("Instantiated", Refl.All)?.GetValue(null, null);
                    if (instantiated is bool b && !b) { L.Debug($"Singleton<{t.Name}> not instantiated yet"); continue; }
                    instance = st.GetProperty("Instance", Refl.All)?.GetValue(null, null);
                }
                catch (Exception e) { L.Debug($"Singleton<{t.Name}>: {e.GetBaseException().Message}"); }
                if (instance == null) continue;
                if (Refl.Get(instance, "ItemTemplates") is IDictionary d && d.Count > 0) { from = $"Singleton<{t.FullName}>.ItemTemplates"; return d; }
                L.Debug($"Singleton<{t.Name}> has no filled ItemTemplates yet");
            }
            return null;
        }

        private static bool IsRealItem(string tpl)
        {
            // skip dev / template-only nodes: an item with no category, or a node other items descend from
            return GroupOf(tpl) != "Other" && !Groups.Any(g => g.Classes.Contains(tpl));
        }

        public static string GroupOf(string tpl)
        {
            var parents = Templates();
            if (parents == null) return "Other";
            string cur = tpl;
            for (int i = 0; i < 25 && !string.IsNullOrEmpty(cur); i++)
            {
                if (GroupOfClass.TryGetValue(cur, out var g)) return g;
                if (!parents.TryGetValue(cur, out cur)) break;
            }
            return "Other";
        }

        // ---------------------------------------------------------------- names

        private static MethodInfo _localized;
        private static bool _localizedTried;
        private static readonly Regex LevelGateLabel = new Regex(@"^\s*\[[^\]]*\]\s*", RegexOptions.Compiled);

        private static string Localize(string key)
        {
            if (!_localizedTried)
            {
                _localizedTried = true;
                var t = AccessTools.TypeByName("EFT.LocalizationExtensions");
                _localized = t?.GetMethods(Refl.All).FirstOrDefault(m => m.Name == "Localized" && m.GetParameters().Length == 2 && m.GetParameters().All(p => p.ParameterType == typeof(string)));
                L.Info(_localized != null ? $"item names from {t.FullName}.Localized" : "EFT.LocalizationExtensions.Localized(string, string) not found — ids shown instead of names");
            }
            if (_localized == null) return null;
            try { return _localized.Invoke(null, new object[] { key, "" }) as string; }
            catch (Exception e) { L.ErrorOnce("Localized(" + key + ")", e); return null; }
        }

        /// <summary>The game's name without LevelGate's "[LOCKED - Lvl 40] " in front.</summary>
        public static string NameOf(string tpl)
        {
            var s = Localize(tpl + " Name");
            if (string.IsNullOrEmpty(s) || s == tpl + " Name") return tpl;
            return LevelGateLabel.Replace(s, "");
        }

        /// <summary>Short name; LevelGate turns a locked item's short name into just "[LOCKED]", then the full name is used.</summary>
        public static string ShortOf(string tpl)
        {
            var s = Localize(tpl + " ShortName");
            s = string.IsNullOrEmpty(s) || s == tpl + " ShortName" ? "" : LevelGateLabel.Replace(s, "");
            return string.IsNullOrEmpty(s) ? NameOf(tpl) : s;
        }

        // ---------------------------------------------------------------- the player

        private static Type _appType;
        private static bool _appTried;

        /// <summary>The logged-in PMC profile (null before login).</summary>
        public static object Profile()
        {
            try
            {
                if (!_appTried)
                {
                    _appTried = true;
                    _appType = AccessTools.TypeByName("EFT.TarkovApplication");
                    L.Info(_appType != null ? "player level from EFT.TarkovApplication.Session.Profile" : "EFT.TarkovApplication not found — player level unknown");
                }
                if (_appType == null) return null;
                object app = null;
                var exist = _appType.GetMethods(Refl.All).FirstOrDefault(m => m.Name == "Exist" && m.GetParameters().Length == 1 && m.GetParameters()[0].IsOut);
                if (exist != null) { var args = new object[] { null }; exist.Invoke(null, args); app = args[0]; }
                if (app == null && typeof(UnityEngine.Object).IsAssignableFrom(_appType)) app = UnityEngine.Object.FindObjectOfType(_appType);
                return Refl.Get(Refl.Get(app, "Session"), "Profile");
            }
            catch (Exception e) { L.ErrorOnce("reading the profile", e); return null; }
        }

        public static int PlayerLevel()
        {
            var lvl = Refl.Get(Refl.Get(Profile(), "Info"), "Level");
            return lvl is int n ? n : 0;
        }

        /// <summary>0..1 of the way to the next level (-1 if the experience table can't be read).</summary>
        public static float LevelProgress()
        {
            try
            {
                var info = Refl.Get(Profile(), "Info");
                if (!(Refl.Get(info, "Experience") is int exp)) return -1;
                int level = PlayerLevel();
                var table = ExpTable();
                if (table == null || level <= 0 || level >= table.Length) return -1;
                int from = table[level - 1], to = table[level];
                return to > from ? Mathf.Clamp01((exp - from) / (float)(to - from)) : -1;
            }
            catch (Exception e) { L.ErrorOnce("level progress", e); return -1; }
        }

        private static int[] _expTable;
        private static bool _expLogged;

        /// <summary>Total experience needed for each level (index 0 = level 1), from the game's globals.</summary>
        private static int[] ExpTable()
        {
            if (_expTable != null) return _expTable;
            var singleton = AccessTools.TypeByName("Comfort.Common.Singleton`1");
            var cfgType = AccessTools.TypeByName("BackendConfigSettingsClass");
            if (singleton == null || cfgType == null) { if (!_expLogged) { _expLogged = true; L.Debug("BackendConfigSettingsClass not found — no level progress %"); } return null; }
            object cfg = null;
            try { cfg = singleton.MakeGenericType(cfgType).GetProperty("Instance", Refl.All)?.GetValue(null, null); } catch { }
            var rows = Refl.Get(Refl.Get(Refl.Get(cfg, "Experience"), "Level"), "ExpTable") as IEnumerable;
            if (rows == null) { if (!_expLogged) { _expLogged = true; L.Debug("globals Experience.Level.ExpTable not readable — no level progress %"); } return null; }
            var steps = new List<int>();
            foreach (var r in rows) steps.Add(Refl.Get(r, "Experience") is int x ? x : 0);
            // the table holds the experience of each step; the totals are the running sum
            var totals = new int[steps.Count];
            int sum = 0;
            for (int i = 0; i < steps.Count; i++) { totals[i] = sum; sum += steps[i]; }
            _expTable = totals;
            L.Info($"experience table: {totals.Length} levels");
            return _expTable;
        }

        public static void Dump()
        {
            L.Info($"data: config {ConfigPath ?? "(none)"}, {Levels.Count} limited items, templates {(_parents == null ? "not loaded" : _parents.Count.ToString())}, player level {PlayerLevel()}, progress {LevelProgress():0.00}");
            if (_parents != null)
            {
                var counts = Levels.Keys.GroupBy(GroupOf).Select(g => $"{g.Key}:{g.Count()}");
                L.Info("limited items per category: " + string.Join(" ", counts.ToArray()));
            }
        }
    }

    /// <summary>Exception-safe "read a field or property by name" (cached).</summary>
    internal static class Refl
    {
        public const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        private static readonly Dictionary<(Type, string), Func<object, object>> _cache = new Dictionary<(Type, string), Func<object, object>>();

        public static object Get(object o, string name)
        {
            if (o == null) return null;
            var key = (o.GetType(), name);
            if (!_cache.TryGetValue(key, out var f))
            {
                f = null;
                for (var t = o.GetType(); t != null && f == null; t = t.BaseType)
                {
                    var p = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
                    if (p != null && p.GetIndexParameters().Length == 0) { f = x => p.GetValue(x, null); break; }
                    var fi = t.GetField(name, All | BindingFlags.DeclaredOnly);
                    if (fi != null) { f = x => fi.GetValue(x); break; }
                }
                _cache[key] = f;
            }
            try { return f?.Invoke(o); } catch { return null; }
        }

        public static bool Set(object o, string name, object value)
        {
            if (o == null) return false;
            try
            {
                var p = o.GetType().GetProperty(name, All);
                if (p != null && p.CanWrite) { p.SetValue(o, value, null); return true; }
                var f = o.GetType().GetField(name, All);
                if (f != null) { f.SetValue(o, value); return true; }
            }
            catch (Exception e) { L.Debug($"set {o.GetType().Name}.{name}: {e.GetBaseException().Message}"); }
            return false;
        }
    }
}
