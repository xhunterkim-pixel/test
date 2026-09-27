using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;

namespace LevelGate.Progression
{
    /// <summary>
    /// Armor classes as the game data has them on disk (SPT_Data/…/templates/items.json), read-only, once, on a background
    /// thread. LevelGate (a server mod) sets armor's class to 0 in what the server hands the game, so the client can't see
    /// it; the file on disk is untouched. Nothing here writes anything or talks to LevelGate or the server.
    /// Per item: its own armorClass, and the default plates in its slots ("Plate": id) — armored rigs, armor, helmets.
    /// </summary>
    internal static class OriginalArmor
    {
        private static Dictionary<string, int> _class;
        private static Dictionary<string, List<string>> _plates;
        private static volatile bool _ready;
        private static bool _started;

        public static void Load()
        {
            if (_started) return;
            _started = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var path = Find();
                    if (path == null) { L.Info("armor: the game data file (SPT_Data/…/templates/items.json) wasn't found — armor class stays hidden"); return; }
                    var t0 = DateTime.Now;
                    Scan(File.ReadAllText(path));
                    _ready = true;
                    L.Info($"armor: read {path} in {(DateTime.Now - t0).TotalMilliseconds:0} ms — {_class.Count(kv => kv.Value > 0)} item(s) with a class, {_plates.Count} with default plates");
                }
                catch (Exception e) { L.Error("reading the original armor classes", e); }
            });
        }

        private static string Find()
        {
            var root = Paths.GameRootPath;
            var rel = new[] { "SPT_Data/database/templates/items.json", "SPT_Data/Server/database/templates/items.json", "SPT/SPT_Data/database/templates/items.json" };
            foreach (var r in rel) { var p = Path.Combine(root, r); if (File.Exists(p)) return p; }
            // one folder down (a server kept next to the game)
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                    foreach (var r in rel) { var p = Path.Combine(dir, r); if (File.Exists(p)) return p; }
            }
            catch { }
            return null;
        }

        /// <summary>A streaming pass over the JSON (no full parse): item ids at depth 1, their armorClass, their "Plate" ids.</summary>
        private static void Scan(string s)
        {
            var cls = new Dictionary<string, int>();
            var plates = new Dictionary<string, List<string>>();
            int depth = 0, i = 0, n = s.Length;
            string item = null, key = null;
            while (i < n)
            {
                char c = s[i];
                if (c == '{' || c == '[') { depth++; i++; continue; }
                if (c == '}' || c == ']') { depth--; i++; if (depth <= 1) item = depth == 1 ? item : null; continue; }
                if (c == '"')
                {
                    int start = ++i;
                    while (i < n && s[i] != '"') { if (s[i] == '\\') i++; i++; }
                    string str = s.Substring(start, Math.Max(0, i - start));
                    i++;
                    int j = i;
                    while (j < n && char.IsWhiteSpace(s[j])) j++;
                    if (j < n && s[j] == ':')
                    {
                        key = str; i = j + 1;
                        if (depth == 1) item = str; // the item's id (the root object's keys)
                        continue;
                    }
                    if (item != null && depth >= 3)
                    {
                        if (key == "Plate" && str.Length == 24) { if (!plates.TryGetValue(item, out var l)) plates[item] = l = new List<string>(); l.Add(str); }
                        else if (key == "armorClass" && int.TryParse(str, out int v)) cls[item] = Math.Max(cls.TryGetValue(item, out var o) ? o : 0, v);
                    }
                    continue;
                }
                if ((c == '-' || char.IsDigit(c)) && item != null && key == "armorClass" && depth >= 3)
                {
                    int start = i;
                    while (i < n && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '.')) i++;
                    if (double.TryParse(s.Substring(start, i - start), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                        cls[item] = Math.Max(cls.TryGetValue(item, out var o) ? o : 0, (int)d);
                    continue;
                }
                i++;
            }
            _class = cls; _plates = plates;
        }

        /// <summary>The item's armor class from the game data (its own, else its best default plate); 0 if none / not read yet.</summary>
        public static int Get(string tpl)
        {
            if (!_ready || tpl == null) return 0;
            int own = _class.TryGetValue(tpl, out var c) ? c : 0;
            if (own > 0) return own;
            int best = 0;
            if (_plates.TryGetValue(tpl, out var list))
                foreach (var p in list) if (_class.TryGetValue(p, out var pc) && pc > best) best = pc;
            return best;
        }

        public static bool Ready => _ready;
    }
}
