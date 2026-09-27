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
}
