using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// A shortcut in the main menu's bottom-left corner, like the game's EXPANSIONS one: a red block with your animated rank
    /// emblem, NEW (after a level-up you haven't looked at yet), PROGRESSION, and your level / XP under it, on a red
    /// glow from the corner. It lives inside the game's main menu, so it shows and hides with it. Click: opens the screen.
    /// </summary>
    internal static class MenuWidget
    {
        private const string Red = "#d8412f";
        private static GameObject _root;
        private static Image _glow, _emblem;
        private static Component _title, _sub, _newText;
        private static GameObject _newTag;
        private static float _next;
        private static int _shownLevel = -1, _shownSeen = -1;
        private static string _shownXp;

        public static void Seen(int level)
        {
            if (level > 0 && ProgressionPlugin.LastSeenLevel.Value != level)
            {
                L.Info($"NEW: screen opened — last seen level {ProgressionPlugin.LastSeenLevel.Value} → {level} (main-menu NEW tag clears)");
                ProgressionPlugin.LastSeenLevel.Value = level; _shownSeen = -1;
            }
        }

        // play-test log: every change of the profile's level / XP (after a raid, a quest, …), checked every 2 s in the menu
        private static int _watchLevel = -1, _watchExp = -1;
        private static float _watchNext;

        private static void WatchProfile()
        {
            if (Time.unscaledTime < _watchNext) return;
            _watchNext = Time.unscaledTime + 2f;
            int level = ProgData.PlayerLevel(), exp = ProgData.TotalExp();
            if (level <= 0) return;
            if (_watchLevel < 0) { _watchLevel = level; _watchExp = exp; L.Info($"profile: level {level}, {exp} total XP"); return; }
            if (level == _watchLevel && exp == _watchExp) return;
            string gained = exp >= 0 && _watchExp >= 0 ? $" (+{exp - _watchExp} XP)" : "";
            if (level != _watchLevel)
            {
                int items = ProgData.Levels.Values.Count(v => v > _watchLevel && v <= level);
                L.Info($"profile: LEVEL UP {_watchLevel} → {level}{gained}; {items} item(s) unlocked by it; last seen level {ProgressionPlugin.LastSeenLevel.Value}");
            }
            else L.Info($"profile: XP {_watchExp} → {exp}{gained}, still level {level}");
            _watchLevel = level; _watchExp = exp;
        }

        public static void Tick()
        {
            try { WatchProfile(); } catch (Exception e) { L.ErrorOnce("watching the profile", e); }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + .5f;
            try
            {
                bool want = ProgressionPlugin.MenuShortcut.Value;
                if (!want) { if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; } return; }
                var menu = GameObject.Find("Common UI/Common UI/MenuScreen");
                if (menu == null || !menu.activeInHierarchy) return;
                if (_root == null) Build(menu.transform);
                // not in a raid's Esc menu (or while deploying): nothing to open there
                bool blocked = MenuHook.Blocked();
                if (_root.activeSelf == blocked) { _root.SetActive(!blocked); L.Debug(blocked ? "main menu shortcut hidden (raid / deploying)" : "main menu shortcut shown"); }
                if (blocked) return;
                Refresh();
            }
            catch (Exception e) { L.ErrorOnce("main menu shortcut", e); }
        }

        private static void Build(Transform menu)
        {
            L.Step("MenuWidget build");
            _root = new GameObject("LevelGateProgressionShortcut", typeof(RectTransform));
            var root = (RectTransform)_root.transform;
            root.SetParent(menu, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetAsLastSibling();

            // red glow out of the bottom-left corner
            _glow = Ui.Img(Ui.Box(root, "Glow", Vector2.zero, Vector2.zero, new Vector2(700, 480)), Ui.Hex(Red, .30f), Ui.Radial());
            _glow.raycastTarget = false;

            // the clickable block, just above the bottom bar
            var block = Ui.Rect(root, "Block", Vector2.zero, Vector2.zero, new Vector2(40, 84), new Vector2(400, 144));
            var hit = Ui.Img(block, new Color(0, 0, 0, 0), null, true);

            // red icon (top-right corner cut) with the rank emblem as a dark silhouette in it
            var icon = Ui.Rect(block, "Icon", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 2), new Vector2(42, -2));
            Ui.Img(icon, Ui.Hex(Red), Ui.CutCornerTopRight());
            _emblem = Ui.Img(Ui.Fill(icon, "Emblem", 2), Color.white);
            _emblem.preserveAspect = true;
            _emblem.raycastTarget = false;

            // NEW, PROGRESSION, level / XP
            _newTag = Ui.Rect(block, "New", new Vector2(0, 1), new Vector2(0, 1), new Vector2(52, -13), new Vector2(82, -1)).gameObject;
            Ui.Img((RectTransform)_newTag.transform, Ui.Hex("#6ad13a"));
            _newText = Ui.Label(_newTag.transform, "Text", "NEW", 9, Ui.Hex("#0e1a08"), TextAnchor.MiddleCenter, true, 1);
            _title = Ui.Label(Ui.Rect(block, "Title", new Vector2(0, 0), new Vector2(1, 1), new Vector2(51, 17), new Vector2(0, -11)), "Text", "PROGRESSION", 28, Ui.Hex(Red), TextAnchor.MiddleLeft, false, 1);
            _sub = Ui.Label(Ui.Rect(block, "Sub", new Vector2(0, 0), new Vector2(1, 0), new Vector2(52, 1), new Vector2(0, 17)), "Text", "", 11, Ui.Hex("#c9563f"), TextAnchor.MiddleLeft, false, 1);

            var b = block.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { L.Info("main menu shortcut clicked"); ProgScreen.Open("main menu shortcut"); });
            block.gameObject.AddComponent<Hover>();
            _shownLevel = _shownSeen = -1; _shownXp = null;
            L.Info("main menu shortcut added (bottom-left)");
        }

        private static void Refresh()
        {
            int level = ProgData.PlayerLevel();
            if (level <= 0) return;
            int seen = ProgressionPlugin.LastSeenLevel.Value;
            if (seen <= 0) { ProgressionPlugin.LastSeenLevel.Value = seen = level; } // first run: nothing is new yet
            string xp = ProgData.LevelExp(out int have, out int need) ? $"{Thousands(have)} / {Thousands(need)} EXP" : "";
            if (level == _shownLevel && seen == _shownSeen && xp == _shownXp) return;
            _shownLevel = level; _shownSeen = seen; _shownXp = xp;
            Emblems.Show(_emblem, level);
            bool isNew = level > seen;
            if (_newTag.activeSelf != isNew) L.Info(isNew ? $"NEW: main-menu tag ON — level {level} > last seen {seen}" : $"NEW: main-menu tag off (level {level}, last seen {seen})");
            _newTag.SetActive(isNew);
            Ui.SetText(_sub, $"Level {level}  ·  {ProgScreen.TierOf(level).Name}" + (xp != "" ? "   <color=#8a3a2c>|</color>   " + xp : ""));
        }

        private static string Thousands(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

        /// <summary>Hover: the title and glow brighten, with the game's hover sound.</summary>
        private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public void OnPointerEnter(PointerEventData e)
            {
                Sounds.Play("ButtonOver");
                if (_title != null) Ui.SetColor(_title, Ui.Hex("#ff6a52"));
                if (_glow != null) _glow.color = Ui.Hex(Red, .42f);
            }

            public void OnPointerExit(PointerEventData e)
            {
                if (_title != null) Ui.SetColor(_title, Ui.Hex(Red));
                if (_glow != null) _glow.color = Ui.Hex(Red, .30f);
            }
        }
    }
}
