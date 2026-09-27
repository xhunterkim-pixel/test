using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// A shortcut in the main menu's bottom-left corner, like the game's EXPANSIONS one: a red block with your rank
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
            if (level > 0 && ProgressionPlugin.LastSeenLevel.Value != level) { ProgressionPlugin.LastSeenLevel.Value = level; _shownSeen = -1; }
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + .5f;
            try
            {
                bool want = ProgressionPlugin.MenuShortcut.Value;
                if (!want) { if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; } return; }
                var menu = GameObject.Find("Common UI/Common UI/MenuScreen");
                if (menu == null || !menu.activeInHierarchy) return;
                if (_root == null) Build(menu.transform);
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
            _glow = Ui.Img(Ui.Box(root, "Glow", Vector2.zero, Vector2.zero, new Vector2(1300, 900)), Ui.Hex(Red, .30f), Ui.Radial());
            _glow.raycastTarget = false;

            // the clickable block, just above the bottom bar
            var block = Ui.Rect(root, "Block", Vector2.zero, Vector2.zero, new Vector2(52, 84), new Vector2(720, 204));
            var hit = Ui.Img(block, new Color(0, 0, 0, 0), null, true);

            // red icon (top-right corner cut) with the rank emblem as a dark silhouette in it
            var icon = Ui.Rect(block, "Icon", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 4), new Vector2(84, -4));
            Ui.Img(icon, Ui.Hex(Red), Ui.CutCornerTopRight());
            _emblem = Ui.Img(Ui.Fill(icon, "Emblem", 8), new Color(.08f, .05f, .05f, .9f));
            _emblem.preserveAspect = true;
            _emblem.raycastTarget = false;

            // NEW, PROGRESSION, level / XP
            _newTag = Ui.Rect(block, "New", new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -24), new Vector2(160, -2)).gameObject;
            Ui.Img((RectTransform)_newTag.transform, Ui.Hex("#6ad13a"));
            _newText = Ui.Label(_newTag.transform, "Text", "NEW", 15, Ui.Hex("#0e1a08"), TextAnchor.MiddleCenter, true, 1);
            _title = Ui.Label(Ui.Rect(block, "Title", new Vector2(0, 0), new Vector2(1, 1), new Vector2(102, 34), new Vector2(0, -22)), "Text", "PROGRESSION", 56, Ui.Hex(Red), TextAnchor.MiddleLeft, false, 1);
            _sub = Ui.Label(Ui.Rect(block, "Sub", new Vector2(0, 0), new Vector2(1, 0), new Vector2(104, 2), new Vector2(0, 34)), "Text", "", 20, Ui.Hex("#c9563f"), TextAnchor.MiddleLeft, false, 1);

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
            _emblem.color = new Color(.08f, .05f, .05f, .9f);
            _newTag.SetActive(level > seen);
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
