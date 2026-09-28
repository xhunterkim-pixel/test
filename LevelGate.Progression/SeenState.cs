using System;
using System.Collections.Generic;
using System.Linq;

namespace LevelGate.Progression
{
    /// <summary>
    /// What the screen last showed, per character (profile id): the total XP (the XP animation plays from there) and the
    /// level (rewards above it are NEW). Kept in one hidden setting, "id=xp,level;id=xp,level".
    /// A character the plugin hasn't seen yet starts where it is now (set at start and by the 30 s profile check), so
    /// what it earns afterwards plays out — also for a fresh character at 0 XP.
    /// </summary>
    internal static class SeenState
    {
        private static Dictionary<string, (int Xp, int Level)> _map;

        private static Dictionary<string, (int Xp, int Level)> Map()
        {
            if (_map != null) return _map;
            _map = new Dictionary<string, (int, int)>();
            foreach (var part in (ProgressionPlugin.ProfileState.Value ?? "").Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var v = kv[1].Split(',');
                if (v.Length == 2 && int.TryParse(v[0], out int xp) && int.TryParse(v[1], out int lvl)) _map[kv[0]] = (xp, lvl);
            }
            return _map;
        }

        private static void Save() => ProgressionPlugin.ProfileState.Value = string.Join(";", Map().Select(e => $"{e.Key}={e.Value.Xp},{e.Value.Level}").ToArray());

        private static string Id => ProgData.ProfileId();

        /// <summary>Known for this character? If not (and the profile is readable), starts it at its XP / level now.</summary>
        public static bool Ensure()
        {
            var id = Id;
            int xp = ProgData.TotalExp(), level = ProgData.PlayerLevel();
            if (id == null || xp < 0 || level <= 0) return false;
            if (Map().ContainsKey(id)) return true;
            // 0.9.29 and older kept one value for every character: carried over only when it fits this one
            int oldXp = ProgressionPlugin.ShownXp.Value, oldLevel = ProgressionPlugin.LastSeenLevel.Value;
            bool fits = oldXp > 0 && oldXp <= xp && oldLevel > 0 && oldLevel <= level && Map().Count == 0;
            Map()[id] = fits ? (oldXp, oldLevel) : (xp, level);
            Save();
            L.Info($"seen: character {id} {(fits ? "carried over from the old setting" : "new here")} — XP {Map()[id].Xp}, level {Map()[id].Level}");
            return true;
        }

        /// <summary>Total XP the screen last showed this character (-1: unknown).</summary>
        public static int Xp
        {
            get => Ensure() ? Map()[Id].Xp : -1;
            set { if (!Ensure()) return; var e = Map()[Id]; if (e.Xp == value) return; Map()[Id] = (value, e.Level); Save(); }
        }

        /// <summary>This character's level when the screen was last opened (0: unknown).</summary>
        public static int Level
        {
            get => Ensure() ? Map()[Id].Level : 0;
            set { if (!Ensure()) return; var e = Map()[Id]; if (e.Level == value) return; Map()[Id] = (e.Xp, value); Save(); }
        }
    }

    /// <summary>
    /// The NEW tags, per character, kept between visits and restarts ("id=tpl,tpl…|level,level…;…" in a hidden setting):
    /// a reward stays NEW until you click it; a level card until you pick that level (or click all its new rewards).
    /// Rewards of levels you reached since the screen last saw you are added when it opens.
    /// </summary>
    internal static class NewTags
    {
        private static Dictionary<string, (HashSet<string> Items, HashSet<int> Levels)> _map;

        private static Dictionary<string, (HashSet<string>, HashSet<int>)> Map()
        {
            if (_map != null) return _map;
            _map = new Dictionary<string, (HashSet<string>, HashSet<int>)>();
            foreach (var part in (ProgressionPlugin.NewState.Value ?? "").Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var halves = kv[1].Split('|');
                var items = new HashSet<string>(halves[0].Split(',').Where(x => x.Length > 0));
                var levels = new HashSet<int>(halves.Length > 1 ? halves[1].Split(',').Select(x => int.TryParse(x, out int n) ? n : 0).Where(n => n > 0) : new int[0]);
                _map[kv[0]] = (items, levels);
            }
            return _map;
        }

        private static void Save() => ProgressionPlugin.NewState.Value = string.Join(";", Map().Where(e => e.Value.Item1.Count > 0 || e.Value.Item2.Count > 0)
            .Select(e => $"{e.Key}={string.Join(",", e.Value.Item1.ToArray())}|{string.Join(",", e.Value.Item2.Select(n => n.ToString()).ToArray())}").ToArray());

        private static (HashSet<string> Items, HashSet<int> Levels) Mine()
        {
            var id = ProgData.ProfileId() ?? "";
            if (!Map().TryGetValue(id, out var e)) { e = (new HashSet<string>(), new HashSet<int>()); Map()[id] = e; }
            return e;
        }

        /// <summary>Levels from+1 … to reached: their rewards and cards become NEW. Returns how many rewards were added.</summary>
        public static int Reached(int from, int to)
        {
            if (from <= 0 || to <= from) return 0;
            var m = Mine(); int n = 0;
            foreach (var kv in ProgData.Levels)
                if (kv.Value > from && kv.Value <= to && m.Items.Add(kv.Key)) n++;
            for (int l = from + 1; l <= to; l++) if (ProgData.CountAt(l) > 0) m.Levels.Add(l);
            Save();
            return n;
        }

        // an F12 preview's levels: NEW for the preview only (never saved, gone when it ends)
        private static readonly HashSet<string> _simItems = new HashSet<string>();
        private static readonly HashSet<int> _simLevels = new HashSet<int>();

        public static void Preview(int from, int to)
        {
            _simItems.Clear(); _simLevels.Clear();
            foreach (var kv in ProgData.Levels) if (kv.Value > from && kv.Value <= to) _simItems.Add(kv.Key);
            for (int l = from + 1; l <= to; l++) if (ProgData.CountAt(l) > 0) _simLevels.Add(l);
        }

        public static void EndPreview() { _simItems.Clear(); _simLevels.Clear(); }

        public static bool Item(string tpl) => tpl != null && (_simItems.Contains(tpl) || Mine().Items.Contains(tpl));
        public static bool Level(int level) => _simLevels.Contains(level) || Mine().Levels.Contains(level);
        public static int Count => Mine().Items.Count;

        /// <summary>Does this level still have rewards NEW for this character (not clicked yet)?</summary>
        public static bool AnyItemAt(int level)
        {
            var m = Mine();
            foreach (var kv in ProgData.Levels) if (kv.Value == level && (m.Items.Contains(kv.Key) || _simItems.Contains(kv.Key))) return true;
            return false;
        }

        /// <summary>A reward clicked: no longer NEW; its level's card too once none of that level's rewards are NEW.</summary>
        public static bool ClearItem(string tpl, int level)
        {
            if (_simItems.Remove(tpl))
            {
                if (!ProgData.Levels.Any(kv => kv.Value == level && _simItems.Contains(kv.Key))) _simLevels.Remove(level);
                return true;
            }
            var m = Mine();
            if (!m.Items.Remove(tpl)) return false;
            if (!ProgData.Levels.Any(kv => kv.Value == level && m.Items.Contains(kv.Key))) m.Levels.Remove(level);
            Save();
            return true;
        }

        public static bool ClearLevel(int level)
        {
            if (_simLevels.Remove(level)) return true;
            if (!Mine().Levels.Remove(level)) return false;
            Save();
            return true;
        }
    }
}
