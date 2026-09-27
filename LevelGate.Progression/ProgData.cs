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
            Templates(); // first: loading them resets the lists
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
                if (parentMember == null) L.Warn("couldn't read item parents — every item will be listed under Other. Send the log with Advanced > VerboseLog on.");
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

        internal static string Localize(string key)
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

        /// <summary>The game's description text of an item ("" when there is none).</summary>
        public static string DescriptionOf(string tpl)
        {
            var s = Localize(tpl + " Description");
            return string.IsNullOrEmpty(s) || s == tpl + " Description" ? "" : s.Trim();
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
            if (!string.IsNullOrEmpty(s)) return s;
            if (_noShort++ < 5) L.Debug($"no short name for {tpl} — shortened from its full name");
            return Shorten(NameOf(tpl));
        }

        private static int _noShort;

        // what the type already says on the tile (its category label) doesn't need repeating in the name
        private static readonly string[] _typeWords =
        {
            " bolt-action sniper rifle", " bolt-action rifle", " sniper rifle", " marksman rifle", " assault rifle", " assault carbine",
            " carbine", " submachine gun", " machine pistol", " machine gun", " light machine gun", " pump-action shotgun", " shotgun",
            " pistol", " revolver", " grenade launcher", " armored rig", " plate carrier", " body armor", " bulletproof helmet",
            " helmet", " backpack", " stimulant injector", " injector", " armband",
        };

        /// <summary>A full name without its type words ("SIG MCX .300 Blackout assault rifle" → "SIG MCX .300 Blackout").</summary>
        private static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            foreach (var w in _typeWords)
            {
                int i = name.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                if (i > 3) { name = (name.Substring(0, i) + name.Substring(i + w.Length)).Trim(); break; }
            }
            return name;
        }

        // ---------------------------------------------------------------- the player

        private static Type _appType;
        private static bool _appTried;

        private static object App()
        {
            if (_appType == null) return null;
            object app = null;
            var exist = _appType.GetMethods(Refl.All).FirstOrDefault(m => m.Name == "Exist" && m.GetParameters().Length == 1 && m.GetParameters()[0].IsOut);
            if (exist != null) { var args = new object[] { null }; exist.Invoke(null, args); app = args[0]; }
            if (app == null && typeof(UnityEngine.Object).IsAssignableFrom(_appType)) app = UnityEngine.Object.FindObjectOfType(_appType);
            return app;
        }

        private static object Session(object app) => Refl.Get(app, "Session");

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
                return Refl.Get(Session(app), "Profile");
            }
            catch (Exception e) { L.ErrorOnce("reading the profile", e); return null; }
        }

        /// <summary>The profile's id (each character keeps its own XP / last seen level), or null.</summary>
        public static string ProfileId()
        {
            var p = Profile();
            return (Refl.Get(p, "Id") ?? Refl.Get(p, "ProfileId") ?? Refl.Get(Refl.Get(p, "Info"), "Nickname"))?.ToString();
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

        /// <summary>Experience into the current level and needed for the next one (false if unknown / max level).</summary>
        public static bool LevelExp(out int have, out int need)
        {
            have = need = 0;
            try
            {
                if (!(Refl.Get(Refl.Get(Profile(), "Info"), "Experience") is int exp)) return false;
                int level = PlayerLevel();
                var table = ExpTable();
                if (table == null || level <= 0 || level >= table.Length) return false;
                have = Mathf.Max(0, exp - table[level - 1]);
                need = table[level] - table[level - 1];
                return need > 0;
            }
            catch (Exception e) { L.ErrorOnce("level experience", e); return false; }
        }

        /// <summary>The profile's total experience (-1 if unknown).</summary>
        public static int TotalExp() => Refl.Get(Refl.Get(Profile(), "Info"), "Experience") is int exp ? exp : -1;

        /// <summary>XP still needed to reach a level (false if unknown or already there).</summary>
        public static bool XpTo(int level, out int xp)
        {
            xp = 0;
            try
            {
                if (!(Refl.Get(Refl.Get(Profile(), "Info"), "Experience") is int exp)) return false;
                var table = ExpTable();
                if (table == null || level < 1 || level > table.Length) return false;
                xp = table[level - 1] - exp;
                return xp > 0;
            }
            catch (Exception e) { L.ErrorOnce("xp to level", e); return false; }
        }

        /// <summary>Total experience at the start of a level (0 if unknown).</summary>
        public static int ExpAtLevel(int level)
        {
            var table = ExpTable();
            return table == null || level < 1 || level > table.Length ? 0 : table[level - 1];
        }

        /// <summary>The level a total experience amount reaches (0 if the table is unknown).</summary>
        public static int LevelOfExp(int exp)
        {
            var table = ExpTable();
            if (table == null || table.Length == 0) return 0;
            for (int l = table.Length; l >= 1; l--) if (exp >= table[l - 1]) return l;
            return 1;
        }

        /// <summary>Experience into a level and needed for the next one, for any total (false at max level / unknown).</summary>
        public static bool ExpInLevel(int exp, int level, out int have, out int need)
        {
            have = need = 0;
            var table = ExpTable();
            if (table == null || level <= 0 || level >= table.Length) return false;
            have = Mathf.Max(0, exp - table[level - 1]);
            need = table[level] - table[level - 1];
            return need > 0;
        }

        private static int[] _expTable;
        private static bool _expTried;

        /// <summary>Total experience needed for each level (index 0 = level 1), from the game's globals.</summary>
        private static int[] ExpTable()
        {
            if (_expTable != null) return _expTable;
            // SPT: ask the server for the game's globals (they hold exp_table) — works whatever the client calls its classes
            if (!_sptStarted) StartSptGlobals();
            if (_sptHandler || _expTried) return _expTable;
            try
            {
                // 1) the session's backend config: Session.BackEndConfig.Config.Experience.Level.ExpTable
                Profile(); // makes sure _appType is looked up
                var session = Session(App());
                if (session == null) return null; // not logged in yet: try again later
                object cfg = null;
                foreach (var n in new[] { "BackEndConfig", "BackendConfig", "BackEndConfigs" })
                {
                    var bc = Refl.Get(session, n);
                    if (bc == null) continue;
                    cfg = Refl.Get(bc, "Config") ?? bc;
                    if (Rows(cfg) != null) { L.Info($"experience table from Session.{n}.Config ({cfg.GetType().FullName})"); break; }
                    cfg = null;
                }
                _expTried = true; // from here on, looked up once: searching the game's classes every time made the screen lag
                // 2) any singleton whose type has Experience.Level.ExpTable (the class name changes between game versions)
                if (cfg == null) cfg = ScanForConfig();
                var rows = Rows(cfg) ?? FindTable(session);
                if (rows == null) { L.Info("experience table not found — the XP bar shows 'experience unknown'"); return null; }
                var steps = new List<int>();
                foreach (var r in rows) steps.Add(Refl.Get(r, "Experience") is int x ? x : Refl.Get(r, "exp") is int y ? y : FirstInt(r));
                // the table holds the experience of each step; the totals are the running sum
                var totals = new int[steps.Count];
                int sum = 0;
                for (int i = 0; i < steps.Count; i++) { totals[i] = sum; sum += steps[i]; }
                _expTable = totals;
                L.Info($"experience table: {totals.Length} levels (level 2 at {(totals.Length > 1 ? totals[1] : 0)} xp)");
            }
            catch (Exception e) { _expTried = true; L.ErrorOnce("experience table", e); }
            return _expTable;
        }

        private static bool _sptStarted, _sptHandler;

        public static bool HasExpTable => _expTable != null;

        /// <summary>Reads exp_table from SPT's /client/globals on a background thread (the answer is a few MB).</summary>
        private static void StartSptGlobals()
        {
            _sptStarted = true;
            try
            {
                MethodInfo getJson = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var an = asm.GetName().Name;
                    if (!(an.StartsWith("spt", StringComparison.OrdinalIgnoreCase) || an.StartsWith("aki", StringComparison.OrdinalIgnoreCase))) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                    var t = types.FirstOrDefault(x => x.Name == "RequestHandler");
                    getJson = t?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "GetJson" && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(string));
                    if (getJson != null) break;
                }
                if (getJson == null) { L.Info("experience table: SPT RequestHandler.GetJson not found — trying the game's classes"); return; }
                _sptHandler = true;
                L.Info($"experience table: asking the SPT server ({getJson.DeclaringType.FullName}.GetJson /client/globals)");
                var args = getJson.GetParameters().Select((p, i) => i == 0 ? (object)"/client/globals" : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    var t0 = DateTime.Now;
                    try
                    {
                        var json = getJson.Invoke(null, args) as string;
                        int at = json?.IndexOf("\"exp_table\"", StringComparison.Ordinal) ?? -1;
                        if (at < 0) { L.Info("experience table: the server's globals have no exp_table"); _sptHandler = false; return; }
                        int end = json.IndexOf(']', at);
                        var steps = Regex.Matches(json.Substring(at, end - at), "\"exp\"\\s*:\\s*(\\d+)").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
                        if (steps.Count < 2) { L.Info("experience table: exp_table is empty"); _sptHandler = false; return; }
                        // exp_table[i].exp is the XP from level i to i+1, with [0] = 0: level L starts at the sum of 0..L-1
                        var totals = new int[steps.Count];
                        int sum = 0;
                        for (int i = 0; i < steps.Count; i++) { sum += steps[i]; totals[i] = sum; }
                        _expTable = totals;
                        L.Info($"experience table: {totals.Length} levels from the SPT server (level 2 at {totals[1]} xp, level 3 at {(totals.Length > 2 ? totals[2] : 0)}; {(DateTime.Now - t0).TotalMilliseconds:0} ms)");
                    }
                    catch (Exception e) { L.Error("reading exp_table from the SPT server", e.GetBaseException()); _sptHandler = false; }
                });
            }
            catch (Exception e) { L.ErrorOnce("SPT globals", e); }
        }

        private static IEnumerable Rows(object cfg) => Refl.Get(Refl.Get(Refl.Get(cfg, "Experience"), "Level"), "ExpTable") as IEnumerable;

        private static int FirstInt(object o)
        {
            if (o is int i) return i;
            if (o == null) return 0;
            foreach (var f in o.GetType().GetFields(Refl.All)) if (!f.IsStatic && f.FieldType == typeof(int)) return (int)f.GetValue(o);
            foreach (var p in o.GetType().GetProperties(Refl.All)) if (p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0) try { return (int)p.GetValue(o, null); } catch { }
            return 0;
        }

        /// <summary>Last resort, by types only (no walking through live objects): finds the class that has the ExpTable
        /// list, then the classes that hold it (up to 3 steps), and reads it from whichever of those the game keeps as a Singleton.</summary>
        private static IEnumerable FindTable(object session)
        {
            var t0 = DateTime.Now;
            try
            {
                L.Step("experience table: type search");
                Type[] types;
                // every game assembly (the backend config classes are not always in Assembly-CSharp)
                var all = new List<Type>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var an = asm.GetName().Name;
                    if (an.StartsWith("System") || an.StartsWith("Unity") || an.StartsWith("mscorlib") || an.StartsWith("Mono.") || an.StartsWith("BepInEx") || an.StartsWith("0Harmony") || an.StartsWith("Newtonsoft")) continue;
                    try { all.AddRange(asm.GetTypes()); } catch (ReflectionTypeLoadException e) { all.AddRange(e.Types.Where(x => x != null)); } catch { }
                }
                types = all.ToArray();
                // for next time: what the game calls its experience classes
                L.Info("experience table: types named *Exp*Level* / *Level*Exp* / *ExpTable*: " + string.Join(", ", types.Where(t => { var n = t.Name; return n.IndexOf("ExpTable", StringComparison.OrdinalIgnoreCase) >= 0
                    || (n.IndexOf("Exp", StringComparison.Ordinal) >= 0 && n.IndexOf("Level", StringComparison.Ordinal) >= 0); }).Select(t => t.FullName).Take(20).ToArray()));
                MemberInfo TableMember(Type t) => (MemberInfo)t.GetFields(Refl.All).FirstOrDefault(f => !f.IsStatic && IsTableName(f.Name))
                    ?? t.GetProperties(Refl.All).FirstOrDefault(p => IsTableName(p.Name) && p.GetIndexParameters().Length == 0);
                var holders = types.Where(t => !t.IsGenericTypeDefinition && TableMember(t) != null).ToList();
                L.Info("experience table: classes with an ExpTable: " + (holders.Count == 0 ? "none" : string.Join(", ", holders.Select(t => t.FullName + "." + TableMember(t).Name).ToArray())));
                var singleton = AccessTools.TypeByName("Comfort.Common.Singleton`1");
                // chains: holder <- owner <- owner … ; each step is (type, member names from the top down)
                var level = holders.Select(h => (type: h, path: new List<string> { TableMember(h).Name })).ToList();
                for (int step = 0; step < 4 && level.Count > 0; step++)
                {
                    var next = new List<(Type type, List<string> path)>();
                    foreach (var (type, path) in level)
                    {
                        object inst = null;
                        if (singleton != null && !type.IsValueType)
                            try { inst = singleton.MakeGenericType(type).GetProperty("Instance", Refl.All)?.GetValue(null, null); } catch { }
                        if (inst != null)
                        {
                            object v = inst;
                            foreach (var m in path) v = Refl.Get(v, m);
                            if (v is IEnumerable list && !(v is string))
                            {
                                L.Info($"experience table found: Singleton<{type.FullName}>.{string.Join(".", path.ToArray())} ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
                                return list;
                            }
                        }
                        if (step == 3) continue;
                        foreach (var owner in types)
                        {
                            if (owner.IsGenericTypeDefinition || owner.IsEnum || owner.IsInterface) continue;
                            FieldInfo[] fs;
                            try { fs = owner.GetFields(Refl.All); } catch { continue; }
                            foreach (var f in fs)
                                if (!f.IsStatic && f.FieldType == type) next.Add((owner, new List<string> { f.Name }.Concat(path).ToList()));
                        }
                    }
                    level = next.Take(50).ToList();
                }
            }
            catch (Exception e) { L.ErrorOnce("searching for the experience table", e); }
            L.Info($"experience table: not found ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
            return null;
        }

        private static bool IsTableName(string n) => n.IndexOf("ExpTable", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("exp_table", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("ExperienceTable", StringComparison.OrdinalIgnoreCase) >= 0;

        private sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        private static object ScanForConfig()
        {
            var t0 = DateTime.Now;
            try
            {
                var singleton = AccessTools.TypeByName("Comfort.Common.Singleton`1");
                var asm = _appType?.Assembly;
                if (singleton == null || asm == null) return null;
                Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                foreach (var t in types)
                {
                    if (t.IsGenericTypeDefinition || t.IsInterface || t.IsAbstract) continue;
                    var exp = t.GetField("Experience", Refl.All)?.FieldType ?? t.GetProperty("Experience", Refl.All)?.PropertyType;
                    if (exp == null || exp.IsPrimitive) continue;
                    var lvl = exp.GetField("Level", Refl.All)?.FieldType ?? exp.GetProperty("Level", Refl.All)?.PropertyType;
                    if (lvl == null || (lvl.GetField("ExpTable", Refl.All) == null && lvl.GetProperty("ExpTable", Refl.All) == null)) continue;
                    object inst = null;
                    try { inst = singleton.MakeGenericType(t).GetProperty("Instance", Refl.All)?.GetValue(null, null); } catch { }
                    L.Info($"experience table: candidate {t.FullName}, singleton {(inst == null ? "empty" : "set")} ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
                    if (inst != null && Rows(inst) != null) return inst;
                }
            }
            catch (Exception e) { L.ErrorOnce("searching for the experience table", e); }
            return null;
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
