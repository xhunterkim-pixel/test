using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// Our animated rank emblem (and rank name) on the game's Character > Overall screen, right of its big level number.
    /// Added as our own objects under that number (the game's screen itself is not changed), so it shows and hides with the
    /// screen and only animates while it's visible. What was found is logged once; if the number can't be found, nothing
    /// is added. F12 > General > CharacterEmblem turns it off.
    /// </summary>
    internal static class OverallEmblem
    {
        private static float _next;
        private static Type _type;
        private static bool _typeTried, _gaveUp;
        private static Component _screen;
        private static RectTransform _root;
        private static Image _img;
        private static Component _rank;
        private static int _shown = -1;
        private static float _findAt;

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + .5f;
            bool on = ProgressionPlugin.CharacterEmblem?.Value ?? true;
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on || _gaveUp || MenuHook.InRaid()) return;
            if (!_typeTried)
            {
                _typeTried = true;
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                var types = asm?.GetTypes() ?? new Type[0];
                _type = types.FirstOrDefault(t => t.Name == "OverallScreen" && typeof(Component).IsAssignableFrom(t));
                L.Info($"character emblem: {(_type != null ? "found " + _type.FullName : "no OverallScreen type (" + string.Join(", ", types.Where(t => t.Name.IndexOf("Overall", StringComparison.OrdinalIgnoreCase) >= 0).Select(t => t.FullName).Take(8).ToArray()) + ")")}");
                if (_type == null) { _gaveUp = true; return; }
            }
            if (_screen == null)
            {
                // by its path (cheap), and only while the game's character screen is up — 0.9.57 searched every object in the
                // game every 2 s until you first opened it (FindObjectOfType): 60–90 ms frames all over the log
                if (Time.unscaledTime < _findAt) return;
                _findAt = Time.unscaledTime + 1.5f;
                var inv = GameObject.Find("Common UI/Common UI/InventoryScreen");
                if (inv == null) return;
                var overall = inv.transform.Find("Overall Panel");
                _screen = overall != null ? overall.GetComponent(_type) : null;
                if (_screen == null) { _screen = inv.GetComponentInChildren(_type, true); }
                if (_screen == null) return;
                L.Info($"character emblem: Overall screen at {MenuHook.Path(_screen.transform)}");
            }
            if (!_screen.gameObject.activeInHierarchy) return;
            int level = ProgData.PlayerLevel();
            if (level <= 0) return;
            if (_root == null && !Build(level)) return;
            Place(); // follows the game's column if it moves (the prestige icon showing up later…)
            if (level != _shown)
            {
                _shown = level;
                Emblems.Show(_img, level);
                Ui.SetText(_rank, ProgScreen.TierOf(level).Name.ToUpperInvariant());
            }
        }

        /// <summary>Finds the big level number ("40") on the screen and puts the emblem to its right. False: not found (logged).</summary>
        private static bool Build(int level)
        {
            try
            {
                string want = level.ToString();
                Component best = null; float bestSize = 0;
                foreach (var c in _screen.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c.GetType().Name.IndexOf("TextMeshPro", StringComparison.Ordinal) < 0) continue;
                    if (!(Refl.Get(c, "text") is string t) || t.Trim() != want) continue;
                    float size = Refl.Get(c, "fontSize") is float f ? f : 0;
                    if (size > bestSize) { best = c; bestSize = size; }
                }
                if (best == null || bestSize < 30)
                {
                    _gaveUp = true;
                    L.Info($"character emblem: the level number \"{want}\" wasn't found on the Overall screen — not added");
                    return false;
                }
                var text = (RectTransform)best.transform;
                // under the faction logo (USEC / BEAR) in the left column, in its own spacing; else under the level number
                RectTransform logo = null;
                var panel = text.parent != null ? text.parent.parent : null; // …/CharacterPanel
                if (panel != null)
                    foreach (var img in panel.GetComponentsInChildren<Image>(true))
                    {
                        var sn = img.sprite != null ? img.sprite.name.ToLowerInvariant() : "";
                        var gn = img.gameObject.name.ToLowerInvariant();
                        if (sn.Contains("usec") || sn.Contains("bear") || gn.Contains("side") || gn.Contains("faction") || sn.Contains("side"))
                        { logo = (RectTransform)img.transform; break; }
                    }
                float size2 = 96;
                // the column of the game's own icons (faction logo, prestige…): ours goes below the lowest of them, in that
                // column's spacing — as its next child when the column lays itself out, else placed under the lowest one
                var column = logo != null ? logo.parent?.parent as RectTransform : null; // …/IconsContainer
                // never part of the game's own layout (0.9.60 joined its icon column, which centres its items, and pushed the
                // faction logo up into the level number): ours floats just below the column's lowest icon (prestige)
                RectTransform lowest = column != null ? LowestChild(column) : null;
                var host = column ?? (RectTransform)(logo ?? text).parent;
                var below = lowest ?? logo ?? text;
                _root = Ui.Rect(host, "LevelGateEmblem", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
                _root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                _root.sizeDelta = new Vector2(size2, size2 + 20);
                _root.pivot = new Vector2(.5f, 1);
                _host = host; _column = column; _below = below;
                Place();
                L.Info($"character emblem: below {MenuHook.Path(below)} (kept out of the game's layout)");
                _img = Ui.Img(Ui.Rect(_root, "Emblem", new Vector2(0, 1), Vector2.one, new Vector2(0, -size2), Vector2.zero), Color.white);
                _img.preserveAspect = true; _img.raycastTarget = false;
                _rank = Ui.Label(Ui.Rect(_root, "Rank", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-90, 0), new Vector2(90, 18)), "Text", "", 13, Ui.Hex("#b9bdbf"), TextAnchor.MiddleCenter, false, 2);
                L.Info($"character emblem: placed under {(logo != null ? "the faction logo " + MenuHook.Path(logo) : "the level number (no faction logo found)")}");
                L.Debug($"character emblem: level number at {MenuHook.Path(text)} ({bestSize:0} px text)");
                return true;
            }
            catch (Exception e) { _gaveUp = true; L.Info("character emblem: " + e.GetBaseException().Message); return false; }
        }

        private static RectTransform _host, _column, _below;

        /// <summary>Just below the column's lowest icon (its bottom-centre in the host's space, then the column's gap).</summary>
        private static void Place()
        {
            if (_root == null || _host == null) return;
            if (_column != null) { var low = LowestChild(_column); if (low != null) _below = low; }
            if (_below == null) return;
            var corners = new Vector3[4];
            _below.GetWorldCorners(corners);
            Vector2 local = _host.InverseTransformPoint((corners[0] + corners[3]) * .5f);
            var want = new Vector2(local.x - _host.rect.center.x, local.y - _host.rect.center.y - 22);
            if ((_root.anchoredPosition - want).sqrMagnitude > .25f) _root.anchoredPosition = want;
        }

        /// <summary>The active child of a column that reaches lowest on screen (the game's last icon: prestige, else the logo).</summary>
        private static RectTransform LowestChild(RectTransform column)
        {
            RectTransform best = null; float lowY = float.MaxValue;
            var corners = new Vector3[4];
            foreach (Transform ch in column)
            {
                if (!ch.gameObject.activeInHierarchy || !(ch is RectTransform rt) || ch.name == "LevelGateEmblem") continue;
                rt.GetWorldCorners(corners);
                if (corners[0].y < lowY) { lowY = corners[0].y; best = rt; }
            }
            return best;
        }

        /// <summary>Entering a raid: the reference goes (the menu is rebuilt / unloaded); found again next time.</summary>
        public static void Forget() { _screen = null; _root = null; _img = null; _rank = null; _shown = -1; _gaveUp = false; }
    }
}
