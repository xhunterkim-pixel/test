using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;

namespace LevelGate.Progression
{
    /// <summary>
    /// Names as the game data has them on disk, read-only, once, on a background thread:
    /// - short names (SPT_Data/…/locales/global/en.json "&lt;tpl&gt; ShortName"): LevelGate blanks the short names of items
    ///   still locked for you, so locked tiles showed a cut-off full name ("Accuracy International A…" instead of "AXMC");
    /// - the handbook's category path of an item (templates/handbook.json + the categories' names): "Medication › Injectors",
    ///   like the breadcrumb in the game's inspect window.
    /// Nothing is written; LevelGate and the server are not touched.
    /// </summary>
    internal static class GameText
    {
        private static Dictionary<string, string> _short = new Dictionary<string, string>();
        private static Dictionary<string, string> _catName = new Dictionary<string, string>();
        private static Dictionary<string, string> _catParent = new Dictionary<string, string>();
        private static Dictionary<string, string> _itemCat = new Dictionary<string, string>();
        private static volatile bool _ready;
        private static bool _started;

        public static bool Ready => _ready;

        public static void Load()
        {
            if (_started) return;
            _started = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var t0 = DateTime.Now;
                    var hb = Find("templates/handbook.json");
                    var catParent = new Dictionary<string, string>();
                    var itemCat = new Dictionary<string, string>();
                    if (hb != null)
                    {
                        string s = File.ReadAllText(hb);
                        int items = s.IndexOf("\"Items\"", StringComparison.Ordinal);
                        foreach (Match m in Regex.Matches(s, "\"Id\"\\s*:\\s*\"([0-9a-f]{24})\"\\s*,\\s*\"ParentId\"\\s*:\\s*(?:null|\"([0-9a-f]{24})\")"))
                        {
                            string id = m.Groups[1].Value, parent = m.Groups[2].Success ? m.Groups[2].Value : null;
                            if (items < 0 || m.Index < items) catParent[id] = parent; else if (parent != null) itemCat[id] = parent;
                        }
                    }
                    var loc = Find("locales/global/en.json");
                    var shorts = new Dictionary<string, string>();
                    var catName = new Dictionary<string, string>();
                    if (loc != null)
                    {
                        string s = File.ReadAllText(loc);
                        foreach (Match m in Regex.Matches(s, "\"([0-9a-f]{24})( ShortName)?\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                        {
                            string id = m.Groups[1].Value;
                            if (m.Groups[2].Success) shorts[id] = Unescape(m.Groups[3].Value);
                            else if (catParent.ContainsKey(id)) catName[id] = Unescape(m.Groups[3].Value);
                        }
                    }
                    _short = shorts; _catName = catName; _catParent = catParent; _itemCat = itemCat;
                    _ready = true;
                    L.Info($"names: read in {(DateTime.Now - t0).TotalMilliseconds:0} ms — {shorts.Count} short name(s) ({loc ?? "locale file not found"}), " +
                           $"{catParent.Count} handbook categories, {itemCat.Count} items in them ({hb ?? "handbook not found"})");
                }
                catch (Exception e) { L.Error("reading names from the game data", e); }
            });
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            try { return Regex.Unescape(s); } catch { return s.Replace("\\\"", "\"").Replace("\\\\", "\\"); }
        }

        private static string Find(string rel)
        {
            var root = Paths.GameRootPath;
            var bases = new[] { "SPT_Data/database/", "SPT_Data/Server/database/", "SPT/SPT_Data/database/", "SPT_Runtime/SPT_Data/database/" };
            foreach (var b in bases) { var p = Path.Combine(root, b + rel); if (File.Exists(p)) return p; }
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                    foreach (var b in bases) { var p = Path.Combine(dir, b + rel); if (File.Exists(p)) return p; }
            }
            catch { }
            return null;
        }

        /// <summary>The game data's own short name, or null (not read yet / none).</summary>
        public static string Short(string tpl)
            => _ready && tpl != null && _short.TryGetValue(tpl, out var s) && !string.IsNullOrEmpty(s) ? s : null;

        /// <summary>The handbook path of an item, top first ("Medication › Injectors"), or null.</summary>
        public static string CategoryPath(string tpl)
        {
            if (!_ready || tpl == null || !_itemCat.TryGetValue(tpl, out var cat)) return null;
            var parts = new List<string>();
            for (int guard = 0; cat != null && guard < 8; guard++)
            {
                if (_catName.TryGetValue(cat, out var n) && !string.IsNullOrEmpty(n)) parts.Insert(0, n);
                _catParent.TryGetValue(cat, out cat);
            }
            return parts.Count == 0 ? null : string.Join("  ›  ", parts.ToArray());
        }
    }
}
