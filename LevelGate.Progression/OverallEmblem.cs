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
                if (Time.unscaledTime < _findAt) return;
                _findAt = Time.unscaledTime + 2f; // looking for it costs a little: every 2 s until the screen has been opened once
                _screen = UnityEngine.Object.FindObjectOfType(_type) as Component;
                if (_screen == null) return;
                L.Info($"character emblem: Overall screen at {MenuHook.Path(_screen.transform)}");
            }
            if (!_screen.gameObject.activeInHierarchy) return;
            int level = ProgData.PlayerLevel();
            if (level <= 0) return;
            if (_root == null && !Build(level)) return;
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
                float w = Refl.Get(best, "preferredWidth") is float pw ? pw : bestSize * .6f * want.Length;
                float size2 = Mathf.Round(bestSize * 1.25f);
                // right of the number: from the text's left edge + its width + a gap
                _root = Ui.Rect(text, "LevelGateEmblem", new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, Vector2.zero);
                _root.sizeDelta = new Vector2(size2, size2);
                _root.pivot = new Vector2(0, .5f);
                _root.anchoredPosition = new Vector2(w + 14, 0);
                _img = Ui.Img(_root, Color.white);
                _img.preserveAspect = true; _img.raycastTarget = false;
                _rank = Ui.Label(Ui.Rect(_root, "Rank", new Vector2(0, 0), new Vector2(0, 0), new Vector2(-10, -18), new Vector2(size2 + 60, -2)), "Text", "", 12, Ui.Hex("#b9bdbf"), TextAnchor.MiddleLeft, false, 2);
                L.Info($"character emblem: added right of the level number ({MenuHook.Path(text)}, {bestSize:0} px text, emblem {size2:0} px)");
                return true;
            }
            catch (Exception e) { _gaveUp = true; L.Info("character emblem: " + e.GetBaseException().Message); return false; }
        }

        /// <summary>Entering a raid: the reference goes (the menu is rebuilt / unloaded); found again next time.</summary>
        public static void Forget() { _screen = null; _root = null; _img = null; _rank = null; _shown = -1; _gaveUp = false; }
    }
}
