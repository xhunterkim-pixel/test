using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The Progression screen, laid out like the Tarkov / Arena battle passes:
    ///   header        PROGRESSION + the picked level on the left, your level + the Tarkov logo on the right
    ///   left panel    UNLOCKS — everything the picked level unlocks, grouped by category (the "reward list")
    ///   centre        the item under the mouse, big, with ITEM / CATEGORY / UNLOCKS AT in small grey
    ///   right panel   the item's name, its requirement (like "BattlePass level 61/122"), status and INSPECT
    ///   bottom        five level cards under dotted "Level X" headers, arrows, and a numbered page bar with Q / E
    /// It lives inside the game's own UI (after the main menu), so the game's windows open on top of it.
    /// </summary>
    internal static partial class ProgScreen
    {
        private const int PerPage = 5;
        private static int Pages => Mathf.CeilToInt(ProgData.MaxLevel / (float)PerPage);

        // ---- design tokens (use these, not one-off numbers) ----
        // spacing scale
        private const float S1 = 4, S2 = 8, S3 = 12, S4 = 16, S5 = 24;
        private const float Margin = 48;      // screen edge → panels / cards (left and right)
        private const float Gutter = 16;      // between panels
        private const float PanelPad = 16;    // inside a panel
        private const float PanelTop = 108;   // header height: panels start below it
        // type scale: caps label, body, strong body, heading, hero (item name / stats / XP), level number
        private const float TCaps = 12, TBody = 13, TStrong = 15, TTitle = 22, THero = 24, TLevel = 34;
        private const float Caps = 2;         // letter spacing for small caps labels (everything else: 0)
        // colour roles: orange = "you" (your level, XP, current level); light neutral = selection; red = only an unmet requirement
        private static readonly Color Select = Ui.Hex("#d9dfdc");
        private static readonly Color HoverEdge = Ui.Hex("#56636a");
        private static readonly Color Face = Ui.Hex("#0f1315"), FaceHover = Ui.Hex("#151b1e"), FaceSelect = Ui.Hex("#182023");

        // colors: muted, like the Tarkov battle pass
        private static readonly Color Text = Ui.Hex("#d5d9d6");
        private static readonly Color Grey = Ui.Hex("#7d8588");
        private static readonly Color Dim = Ui.Hex("#6a7376"); // the dimmest text: still readable at 12 px on the dark panels
        private static readonly Color XpFillColor = Ui.Hex("#c4c7c8"); // the XP bar's fill: light grey, like the game's bars
        private static readonly Color PanelBg = Ui.Hex("#0b0c0d", .72f); // near-black and see-through: the background shows faintly, like Tarkov
        private static readonly Color Border = Ui.Hex("#2b3438", .9f);
        private const string Green = "#8fae6a", Red = "#d0453a";

        /// <summary>Rank look per level band: name, rim color, light color.</summary>
        private static readonly (int From, string Name, string Rim, string Light)[] Tiers =
        {
            (1, "Scavenger", "#8a9199", "#d6dde3"), (6, "Drifter", "#8a9199", "#e6eef2"), (11, "Trespasser", "#8c6b3f", "#d9b37a"),
            (16, "Stranger", "#8c6b3f", "#e8c27a"), (21, "Contractor", "#9aa3ad", "#f4f7fa"), (26, "Operator", "#9aa3ad", "#ffffff"),
            (31, "Outlaw", "#b8901c", "#ffe27a"), (36, "Renegade", "#b8901c", "#fff0a0"), (41, "Insurgent", "#3f7fb8", "#9fd4ff"),
            (46, "Blacklisted", "#3f7fb8", "#c2e6ff"), (51, "Wanted Man", "#7b4fc9", "#d3b8ff"), (56, "Ringleader", "#7b4fc9", "#e6d6ff"),
            (61, "War Chief", "#b8323f", "#ff9aa5"), (66, "Ghost", "#b8323f", "#ffc0c6"), (71, "Myth", "#c9a227", "#fff1a8"),
            (76, "Legend of Tarkov", "#e0b84a", "#ffffff"),
        };
        internal static (int From, string Name, string Rim, string Light) TierOf(int level) => Tiers.Last(t => level >= t.From);

        private static GameObject _canvas;
        private static RectTransform _bottom;
        private static int _level = 1, _page;
        private static bool _built, _changedHooked;

        // header
        private static Badge _headBadge;
        // left: unlocks
        private static Component _listTitle;
        private static RectTransform _content;
        // centre + right: the featured item
        private static Image _featPic, _featLock, _featLight;
        private static int _featLevel;
        private static Image _heroBloom;
        private static Component _featShort, _featType, _featName, _featReq, _featReqValue, _featStatus;
        private static Image _featCheck;
        private static Component _featDesc;
        private static string _featTpl;
        private static object _featIcon;
        private static int _featScale;
        private static bool _sharpWeaponShown;
        private static float _lastWeaponRepair = -1000f;
        // bottom
        private static readonly Card[] _cards = new Card[PerPage];
        private static readonly List<Image> _segments = new List<Image>();
        private static readonly List<Component> _segmentNums = new List<Component>();
        private static Button _prev, _next;
        // animation / input
        private static float _cardsStart = -10, _tilesStart = -10, _wheelAt, _windowLogAt, _windowSeenAt = -10;
        private static int _cardsDir;
        private static readonly List<(CanvasGroup Group, RectTransform Rt, float Delay)> _tiles = new List<(CanvasGroup, RectTransform, float)>();
        private static readonly List<(RectTransform Rect, ProgItem Item)> _hits = new List<(RectTransform, ProgItem)>();
        private static readonly List<(object Icon, Image Pic, Component Placeholder, string Tpl)> _icons = new List<(object, Image, Component, string)>();
        private static readonly List<(object Icon, Image Pic, Component Placeholder, string Tpl)> _cardIcons = new List<(object, Image, Component, string)>();
        private static int _frames, _iconsShown;
        private static float _frameTime, _frameLogAt;

        public static bool IsOpen => _canvas != null && _canvas.activeSelf;

        public static void Toggle(string why) { if (IsOpen) Close(why); else Open(why); }

        public static void Open(string why)
        {
            L.Step("Open " + why);
            if (IsOpen) { L.Debug($"open ({why}): already open"); return; }
            if (!IsOpen && MenuHook.Blocked(out var blockedWhy)) { L.Info($"not opening ({why}): {blockedWhy}"); return; }
            if (!IsOpen && MenuHook.GoToMainMenuThen(why)) return;
            try
            {
                if (!_built || _canvas == null) { _built = false; Build(); }
                PickRandomPattern();
                ProgData.Invalidate(); // names / categories again (the game may have finished loading them since)
                int player = ProgData.PlayerLevel();
                int seen = SeenState.Level;
                _newFrom = seen > 0 && seen < player ? seen : 0;
                int added = NewTags.Reached(seen, player); // levels reached since the last visit: their rewards are NEW until clicked
                _sLevels = _sItems = _sInspects = _sPages = _sSlow = 0; _sSlowMax = 0; _sOpenedAt = Time.unscaledTime;
                _sLogMs = L.CostMs; _sLogLines = L.CostLines;
                int newItems = NewTags.Count;
                string xpNow = ProgData.LevelExp(out int xh, out int xn) ? $"{xh} / {xn} into level {player} (total {ProgData.TotalExp()})" : "XP unknown";
                L.Info($"open: player level {player}, {xpNow}; last seen level {seen}" +
                       (added > 0 ? $" → levels {_newFrom + 1}–{player} reached: {added} reward(s) tagged NEW" : " → no new levels since last visit") + $"; {newItems} reward(s) still NEW");
                MenuWidget.Seen(player); // the NEW tag on the main-menu shortcut goes away
                L.Info($"screen: game resolution {Screen.width}x{Screen.height} (UI scale {(_canvas.GetComponentInParent<Canvas>()?.scaleFactor ?? 1):0.00}), card pictures ~{CardPx():0} px");
                L.Info($"screen open ({why}); player level {player}, {ProgData.Levels.Count} limited items; graphics {ProgressionPlugin.Quality.Value}{(Perf ? " — emblems stand still, pictures drawn smaller" : "")}");
                if (player > 0 && _level == 1) { _level = Mathf.Clamp(player, 1, ProgData.MaxLevel); _page = (_level - 1) / PerPage; }
                _canvas.SetActive(true);
                _openedAt = Time.unscaledTime;
                if (_fade != null) _fade.alpha = 0;
                MenuHook.SetOn(true);
                HideMenu(true);
                MenuCamera.Turn(true);
                Sounds.Open();
                _frames = 0; _frameTime = 0; _frameLogAt = Time.unscaledTime + 5;
                PrepareXpAnim(); // may move the screen to your old level first (the animation walks it up)
                StartLoading(); // first open of this menu visit: pictures drawn behind a short loading screen
                ShowPage(_page, 0);
                ShowLevel(_level, true);
            }
            catch (Exception e) { L.Error("opening the screen", e); }
        }

        /// <summary>Redraws the open screen (a setting changed).</summary>
        public static void Refresh()
        {
            if (_perfCheck != null) _perfCheck.enabled = Perf;
            if (!IsOpen) return;
            ShowPage(_page, 0);
            ShowLevel(_level, true);
        }

        // what happened while the screen was open (written to the log on close, for play-testing)
        private static int _sLevels, _sItems, _sInspects, _sPages, _sSlow;
        private static float _sSlowMax, _sOpenedAt;
        private static double _sLogMs;
        private static int _sLogLines;

        /// <summary>The screen is thrown away and built again on the next open (text size changed…).</summary>
        public static void Rebuild(string why)
        {
            if (IsOpen) Close(why);
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
            _canvas = null; _built = false;
            L.Info($"screen will be rebuilt on the next open ({why})");
        }

        /// <summary>F12 Reduce Motion.</summary>
        internal static bool Calm => (ProgressionPlugin.ReduceMotion?.Value ?? false) || ProgressionPlugin.Low; // Performance Mode moves less too

        public static void Close(string why)
        {
            if (!IsOpen) return;
            L.Info($"session: open {Time.unscaledTime - _sOpenedAt:0} s — {_sLevels} level(s) viewed, {_sPages} page change(s), {_sItems} item(s) selected, {_sInspects} inspect(s); " +
                   $"{_sSlow} slow frame(s){(_sSlow > 0 ? $", worst {_sSlowMax * 1000:0} ms" : "")}; graphics {ProgressionPlugin.Quality.Value}");
            // logging's own cost: share of the time the screen was open, and of the whole game so far
            double open = Math.Max(.001, Time.unscaledTime - _sOpenedAt), logMs = L.CostMs - _sLogMs;
            L.Info($"logging: {L.CostLines - _sLogLines} line(s) took {logMs:0.0} ms while open ({logMs / 10 / open:0.00}% of the time); " +
                   $"since start {L.CostLines} line(s), {L.CostMs:0} ms ({L.CostMs / 10 / Math.Max(1, Time.realtimeSinceStartup):0.000}% of play time); verbose {(L.Verbose ? "on" : "off")}");
            FinishXpAnim("screen closed");
            EndPreview();
            _newFrom = 0; // the NEW tags themselves are kept (NewTags): they go when clicked / looked at, not when the screen closes
            _canvas.SetActive(false);
            MenuHook.SetOn(false);
            HideMenu(false);
            // the big renders' stash icons back to stash size: right away if we stay on the main menu; if the game is switching
            // to another screen (stash, traders…) not in the middle of that — at the next quiet moment on the main menu
            if (!why.StartsWith("game screen") && !why.StartsWith("blocked") && !why.StartsWith("another menu button")) GameItems.RestoreIcons();
            // at most every 10 minutes: each pass asks the game to redraw ~160 weapons, which it does in the background
            if (_sharpWeaponShown && GameItems.RepairLeft == 0 && Time.unscaledTime - _lastWeaponRepair > 600f)
            {
                _lastWeaponRepair = Time.unscaledTime;
                // sharp weapon pictures can leak onto other stash weapons: redraw every level-list weapon at stash size
                var weapons = ProgData.Levels.Keys.Where(t => ProgData.GroupOf(t) == "Weapons").ToList();
                L.Info($"sharp weapon previews were shown: redrawing {weapons.Count} weapon icon(s) at stash size");
                GameItems.RepairAll(weapons);
            }
            _sharpWeaponShown = false;
            _restoreAgainAt = Time.unscaledTime + 2f; // and once more, for big renders that were still being drawn
            bool gameSwitching = why.StartsWith("game screen");
            MenuCamera.Turn(false, instant: gameSwitching); // the game moves the camera itself when it switches screens
            if (!gameSwitching) Sounds.Click();
            L.Info($"screen closed ({why})");
        }

        // ---------------------------------------------------------------- building

        private static void Build()
        {
            L.Step("Build");
            float t0 = Time.realtimeSinceStartup;
            _segments.Clear(); _segmentNums.Clear(); _hits.Clear(); _icons.Clear(); _cardIcons.Clear();
            _canvas = new GameObject("LevelGateProgressionCanvas", typeof(RectTransform));
            Canvas canvas;
            // inside the game's own UI, right after the main menu screen: the game's windows (inspect…) then
            // draw over it, like over any of its screens. Our own on-top canvas only if that isn't found.
            var menu = MenuScreen();
            if (menu != null && ProgressionPlugin.EmbedInGameUi.Value)
            {
                var rt = (RectTransform)_canvas.transform;
                rt.SetParent(menu.transform.parent, false);
                rt.SetSiblingIndex(menu.transform.GetSiblingIndex() + 1);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                _canvas.layer = menu.layer;
                canvas = _canvas.AddComponent<Canvas>();
                var parent = menu.GetComponentInParent<Canvas>()?.rootCanvas;
                L.Info($"screen placed inside the game's UI after '{MenuHook.Path(menu.transform)}' (canvas '{parent?.name}', {parent?.renderMode}, order {parent?.sortingOrder})");
            }
            else
            {
                UnityEngine.Object.DontDestroyOnLoad(_canvas);
                canvas = _canvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = ProgressionPlugin.SortOrder.Value;
                var scaler = _canvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 1f;
                L.Info("screen on its own canvas on top " + (menu == null ? "(the game's MenuScreen wasn't found)" : "(Advanced > InsideGameUi is off)"));
            }
            _canvas.AddComponent<GraphicRaycaster>();

            var root = Ui.Rect(_canvas.transform, "Panel", Vector2.zero, Vector2.one,
                new Vector2(0, ProgressionPlugin.BottomMargin.Value), new Vector2(0, -ProgressionPlugin.TopMargin.Value));
            Ui.Img(root, Ui.Hex("#0a0f12", ProgressionPlugin.Opacity.Value), null, true); // blocks clicks to the menu underneath
            // the Arena dot grid, barely there (fades in with the rest of the screen)
            var grid = Ui.Img(Ui.Fill(root, "DotGrid"), new Color(1, 1, 1, .035f), Ui.DotGrid());
            _dotGrid = grid;
            grid.type = Image.Type.Tiled;
            grid.raycastTarget = false;
            // Arena-style colour bloom: red, strongest on the right edge and fading out to the left;
            // it always glows a little and flares up while the picked level is still locked
            _bloom.Clear(); _panelFrames.Clear();
            // kept to the top half (right side), so it doesn't pull the eye to the level cards in the bottom corner
            _bloom.Add(Ui.Img(Ui.Rect(root, "Bloom", new Vector2(.25f, 0), Vector2.one, Vector2.zero, Vector2.zero), new Color(0, 0, 0, 0), Ui.CornerGlow()));
            _bloom.Add(Ui.Img(Ui.Box(root, "BloomCore", new Vector2(1, .66f), Vector2.zero, new Vector2(1200, 1300)), new Color(0, 0, 0, 0), Ui.Radial()));
            _mood = -1; // forces the first ApplyMood to paint
            // the kept bigger pictures that are on screen now are never deleted to make room (they turned into white boxes)
            GameItems.Shown = () =>
            {
                var set = new HashSet<Sprite>();
                if (_canvas != null) foreach (var img in _canvas.GetComponentsInChildren<Image>(true)) if (img.sprite != null) set.Add(img.sprite);
                return set;
            };
            GameItems.Dropping = gone =>
            {
                if (_canvas == null) return;
                foreach (var img in _canvas.GetComponentsInChildren<Image>(true))
                    if (img.sprite != null && gone.Contains(img.sprite)) { img.sprite = null; img.enabled = false; }
            };
            BuildMotes(root); // faint specks of light drifting over the background (MW's title screen)

            // the level strip is navigation: ~30% of the height, the rest goes to the reward content
            var top = Ui.Rect(root, "Top", new Vector2(0, .30f), Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(top);
            BuildHeader(top);
            BuildUnlocks(top);
            BuildFeatured(top);
            _bottom = Ui.Rect(root, "Bottom", Vector2.zero, new Vector2(1, .30f), Vector2.zero, Vector2.zero);
            SubCanvas(_bottom);
            BuildBottom(_bottom);
            BuildMicroText();

            // vignette over everything (its own canvas, so it draws last), and the whole screen fades in on open
            var vig = Ui.Rect(root, "Vignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(vig);
            _vignette = Ui.Img(vig, new Color(0, 0, 0, .14f), Ui.Vignette());
            _vignette.raycastTarget = false;
            // film grain over the whole screen, like the game's menus (very faint; follows Graphics > UI Detailing)
            var grain = Ui.Img(Ui.Fill(vig, "Grain"), new Color(1, 1, 1, .02f), Ui.Grain());
            grain.type = Image.Type.Tiled; grain.raycastTarget = false;
            Ui.AddGrit(grain, .02f);
            ApplyLook();
            _fade = root.gameObject.AddComponent<CanvasGroup>();

            _splashRoot = root; // the new-rank splash is built here on first use (ProgScreen.Splash)
            // hover tooltip (full item names, setting hints): its own layer, over everything
            var tipLayer = Ui.Rect(root, "TooltipLayer", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(tipLayer);
            _tip = Ui.Rect(tipLayer, "Tooltip", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            _tip.pivot = new Vector2(.5f, 0);
            Ui.Img(_tip, Ui.Hex("#56636a")).raycastTarget = false;
            Ui.Img(Ui.Fill(_tip, "In", 1), Ui.Hex("#0b0f11", .97f)).raycastTarget = false;
            _tipText = Ui.Label(Ui.Fill(_tip, "Text", 0), "Text", "", TBody, Text, TextAnchor.MiddleCenter, false);
            _tip.gameObject.SetActive(false);
            BuildLoading(root);

            if (!_changedHooked)
            {
                _changedHooked = true;
                ProgData.Changed += () => { if (IsOpen) { L.Debug("level list changed — redrawing"); ShowPage(_page, 0); ShowLevel(_level, true); } };
            }
            _built = true;
            L.Info($"screen built in {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
        }

        private static void SubCanvas(RectTransform rt)
        {
            rt.gameObject.AddComponent<Canvas>();
            rt.gameObject.AddComponent<GraphicRaycaster>();
        }

        /// <summary>A panel like the battle pass ones: a 1 px frame around a dark see-through fill.</summary>
        private const float BorderWidth = 3;
        private static bool Perf => ProgressionPlugin.Low;
        private const string Orange = "#e0562f";

        /// <summary>Category order for the three card pictures.</summary>
        private static readonly string[] CardOrder = { "Weapons", "Backpacks", "Rigs", "Armor", "Headwear", "Medical", "Food", "Gear",
            "Melee", "Grenades", "Electronics", "Containers", "Keys", "Special", "WeaponParts", "Barter", "AmmoPacks", "Ammo", "Other" };

        /// <summary>The card's three pictures: one item from each of the level's top categories (weapons first, ammo last);
        /// if the level has fewer than three categories, more items from the top ones fill in.</summary>
        private static List<ProgItem> CardPicks(List<ProgItem> items)
        {
            int Rank(ProgItem it) { int r = Array.IndexOf(CardOrder, it.Group); return r < 0 ? CardOrder.Length : r; }
            var sorted = items.OrderBy(Rank).ToList();
            var picks = sorted.GroupBy(it => it.Group).Select(g => g.First()).Take(3).ToList();
            foreach (var it in sorted) { if (picks.Count >= 3) break; if (!picks.Contains(it)) picks.Add(it); }
            return picks;
        }

        private static RectTransform Panel(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var frame = Ui.Rect(parent, name, aMin, aMax, oMin, oMax);
            _panelFrames.Add(Ui.Img(frame, Border));
            Ui.CornerMarks(frame, Ui.Hex("#6f7375", .55f)); // CoD-style registration marks at the corners
            // light catches the border that faces the middle of the screen: the left panel's right edge, the right panel's
            // left edge, the centre panel's top edge (a glossy line fading out at both ends)
            float mid = (aMin.x + aMax.x) / 2;
            RectTransform edge = mid < .4f ? Ui.Rect(frame, "EdgeLight", new Vector2(1, .08f), new Vector2(1, .92f), new Vector2(-2, 0), new Vector2(0, 0))
                : mid > .6f ? Ui.Rect(frame, "EdgeLight", new Vector2(0, .08f), new Vector2(0, .92f), new Vector2(0, 0), new Vector2(2, 0))
                : Ui.Rect(frame, "EdgeLight", new Vector2(.08f, 1), new Vector2(.92f, 1), new Vector2(0, -2), new Vector2(0, 0));
            if (mid >= .4f && mid <= .6f) edge.localEulerAngles = Vector3.zero;
            var el = Ui.Detail(Ui.Img(edge, new Color(1, 1, 1, .16f), mid >= .4f && mid <= .6f ? Ui.Radial() : Ui.VerticalFade()), .16f);
            el.raycastTarget = false; el.type = Image.Type.Simple; el.preserveAspect = false;
            var inner = Ui.Fill(frame, "In", BorderWidth);
            Ui.Img(inner, PanelBg);
            // 70 / 30: the panel bodies stay calm; the detail sits in the focal 30% (the list's head and tabs, the picked /
            // your card, the picked tile, the XP header). The details panel only keeps a soft glass light (low detail).
            if (mid > .6f)
            {
                var glass = Ui.Img(Ui.Box(inner, "Glass", new Vector2(.15f, .9f), Vector2.zero, new Vector2(520, 360)), new Color(1, 1, 1, .03f), Ui.Radial());
                glass.raycastTarget = false; Ui.AddGrit(glass, .03f);
            }
            return inner;
        }

        // CoD's HUD micro-text: tiny, faint system labels near panel edges (texture, not information you need to read)
        private static Component _microList, _microStage, _microSide;

        private static void BuildMicroText()
        {
            Color c = Ui.Hex("#7d8285", .42f);
            Component Micro(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, TextAnchor al)
            {
                var l = Ui.Label(Ui.Rect(parent, name, aMin, aMax, oMin, oMax), "Text", "", 8.5f, c, al, false, 2);
                ((Graphic)l).raycastTarget = false;
                Ui.Detail((Graphic)l, c.a);
                return l;
            }
            if (_focusList != null) _microList = Micro(_focusList, "Micro", Vector2.zero, new Vector2(1, 0), new Vector2(PanelPad, 2), new Vector2(-PanelPad, 12), TextAnchor.LowerLeft);
            if (_focusStage != null) _microStage = Micro(_focusStage, "Micro", new Vector2(0, 1), new Vector2(1, 1), new Vector2(S3, -16), new Vector2(-S3, -4), TextAnchor.UpperLeft);
            if (_focusSide != null)
            {
                var st = Ui.Rect(_focusSide, "Stats", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-60, -6), new Vector2(0, 6));
                st.localEulerAngles = new Vector3(0, 0, -90); st.anchoredPosition = new Vector2(-4, 40);
                _microSide = Ui.Label(st, "Text", "STATS  —", 8.5f, c, TextAnchor.MiddleCenter, false, 2);
                ((Graphic)_microSide).raycastTarget = false;
            }
        }

        private static void BuildHeader(RectTransform top)
        {
            // left: who you are — your rank emblem, "PROGRESSION" as a quiet page label, your rank name, how far to the next rank
            const float badge = 72;
            // 8 px lower than before: the emblem's bottom meets the level square's, the last line meets "Next level …"
            const float drop = 8;
            _headBadge = new Badge(top, new Vector2(0, 1), new Vector2(Margin + badge / 2, -(S3 + drop + badge / 2)), badge, .3f);
            float x = Margin + badge + S3;
            // breadcrumb over the page title (like the game's "Weapons > Assault rifles" and MW's "CAREER / PROGRESSION /")
            Ui.Label(Ui.Rect(top, "Crumb", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -S3 - drop + 4), new Vector2(-Gutter, -drop + 4 + 2)), "Text", "CHARACTER  /  PROGRESSION  /", TCaps - 1, Dim, TextAnchor.LowerLeft, false, 1);
            Ui.Label(Ui.Rect(top, "Page", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -50 - drop), new Vector2(-Gutter, -S3 - 2 - drop)), "Text", "LEVEL UNLOCKS", TTitle, Ui.Hex("#d9dcdd"), TextAnchor.UpperLeft, false, 1);
            _headRank = Ui.Label(Ui.Rect(top, "Rank", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -66 - drop), new Vector2(-Gutter, -46 - drop)), "Text", "", TStrong, Text, TextAnchor.MiddleLeft, false);
            _headNext = Ui.Label(Ui.Rect(top, "NextRank", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -86 - drop), new Vector2(-Gutter, -66 - drop)), "Text", "", TBody, Grey, TextAnchor.MiddleLeft, false);

            BuildXp(top);

        }

        private static Component _headRank, _headNext;

        /// <summary>The header shows the player: rank emblem, rank name, levels to the next rank.</summary>
        /// <summary>rankLevel: the level whose rank (emblem, name, page) is shown — the XP animation holds the old one until its rank beat.</summary>
        private static void UpdateHeader(int player, int rankLevel = 0)
        {
            if (_headRank == null) return;
            int lv = Mathf.Max(1, rankLevel > 0 ? rankLevel : player);
            _headBadge.Set(lv);
            var tier = TierOf(lv);
            int index = Array.IndexOf(Tiers, tier) + 1;
            // rank name + the next rank on the first line; page and overall progress (limited items you can use) under it
            int total = ProgData.Levels.Count, owned = player > 0 ? ProgData.Levels.Values.Count(v => v <= player) : 0;
            var next = Tiers.FirstOrDefault(t => t.From > lv);
            string after = next.Name == null ? "top rank" : $"next: {next.Name} (level {next.From})";
            Ui.SetText(_headRank, player <= 0 ? "Progression" : $"{tier.Name}  <size={TBody}><color=#7d8588>·  {after}</color></size>");
            Ui.SetText(_headNext, player <= 0 ? "" : $"Page {index} of {Tiers.Length}" + (total > 0 ? $"  ·  {Thousands(owned)} / {Thousands(total)} items unlocked" : ""));
        }

        private static Component _xpLevel, _xpText, _xpNext, _xpCaption;
        private static RectTransform _xpRight;
        private static RectTransform _xpFill;
        private static Image _xpSquare;
        private static RectTransform _xpTag;

        /// <summary>Arena-style player block over the centre panel: [61] ▕████░░░░▏ 25 / 1 000 EXP · Next level reward: 2 ◆</summary>
        private static void BuildXp(RectTransform top)
        {
            L.Step("BuildXp");
            var xp = Ui.Rect(top, "Xp", new Vector2(.34f, 1), new Vector2(.74f, 1), new Vector2(Gutter / 2, -(S3 + 80)), new Vector2(-Gutter / 2, -S3));
            // "CURRENT LEVEL" heads the block, directly over the level square it names; the overall unlock count sits opposite
            _xpCaption = Ui.Label(Ui.Rect(xp, "CurrentLabel", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -14), Vector2.zero), "Text", "CURRENT LEVEL", TCaps, Grey, TextAnchor.MiddleLeft, false, Caps);
            var sq = Ui.Rect(xp, "Level", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -80), new Vector2(64, -18));
            _xpSquare = Ui.Img(sq, Ui.Hex("#e0562f"), Ui.CutCorner());
            _xpLevel = Ui.Label(sq, "Text", "", TLevel, Color.white, TextAnchor.MiddleCenter, true);
            var right = Ui.Rect(xp, "Right", Vector2.zero, Vector2.one, new Vector2(64 + S4, 0), Vector2.zero);
            _xpRight = right;
            // numbers on top, the bar right under them (12 px: it's the reward meter, not a divider), the next-level link below
            _focusXp = xp;
            // Tarkov's bars: a dark track, a thin 1 px outline, a light grey fill (orange stays for "you": the level square)
            var bar = Ui.Rect(right, "Bar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -58), new Vector2(0, -46));
            Ui.Img(bar, Ui.Hex("#55595b"));
            var barIn = Ui.Fill(bar, "In", 1);
            Ui.Img(barIn, Ui.Hex("#101112", .9f));
            _xpFill = Ui.Rect(barIn, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(2, 2), new Vector2(0, -2));
            Ui.Img(_xpFill, XpFillColor);
            _xpText = Ui.Label(Ui.Rect(right, "Exp", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -44), new Vector2(0, -18)), "Text", "", THero, Ui.Hex("#b9c0c3"), TextAnchor.MiddleLeft, true);
            // the orange EXP tag right after the numbers (moved to the text's end whenever it changes)
            _xpTag = Ui.Rect(right, "ExpTag", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -40), new Vector2(40, -22));
            // the game's own EXP badge (as on the character screen) when it can be found; else one drawn like it:
            // pale grey plate, dark rim, dark bold EXP
            var expSprite = Ui.GameSprite("exp");
            if (expSprite != null) { var ei = Ui.Img(_xpTag, Color.white, expSprite); ei.preserveAspect = true; }
            else
            {
                Ui.Img(_xpTag, Ui.Hex("#15191b"));
                Ui.Img(Ui.Fill(_xpTag, "In", 1), Ui.Hex("#c4c9cb"));
                Ui.Label(_xpTag, "Text", "EXP", TCaps, Ui.Hex("#15191b"), TextAnchor.MiddleCenter, true, .5f);
            }
            var next = Ui.Rect(right, "Next", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -80), new Vector2(420, -62));
            _xpNext = Ui.Label(next, "Text", "", TBody, Grey, TextAnchor.MiddleLeft, false);
            var hit = Ui.Img(next, new Color(0, 0, 0, 0), null, true);
            var b = next.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { int p = ProgData.PlayerLevel(); if (p > 0 && p < ProgData.MaxLevel) { Sounds.Click(); ShowLevel(p + 1); } });
            // it's a link: brightens and underlines on hover
            HoverHook.Add(next, on => { _xpNextHover = on; UpdateXpNext(); if (on) Sounds.Play("ButtonOver"); });
        }

        private static bool _xpNextHover;

        /// <summary>The level the screen treats as yours: the XP animation's / an F12 preview's level while one shows it,
        /// else your real one (the requirement box said "40 / 44 · 4 levels away" under a previewed CURRENT LEVEL 44).</summary>
        private static int Me => _xpCardLevel > 0 ? _xpCardLevel : ProgData.PlayerLevel();

        private static void UpdateXpNext()
        {
            int player = Me;
            int nextCount = player > 0 && player < ProgData.MaxLevel ? ProgData.CountAt(player + 1) : -1;
            string body = $"Next level {player + 1}: <color=#d5d9d6>{nextCount}</color> item{(nextCount == 1 ? "" : "s")}  ›";
            Ui.SetText(_xpNext, nextCount < 0 ? "" : _xpNextHover ? $"<color=#d5d9d6><u>{body}</u></color>" : body);
        }

        private static string Thousands(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

        private static void UpdateXp()
        {
            L.Step("UpdateXp");
            if (_xpLevel == null || XpAnimating || _simLevel > 0) return; // the XP animation / a preview draws the block itself
            int player = ProgData.PlayerLevel();
            bool known = ProgData.LevelExp(out int have, out int need);
            ShowXpState(player, have, need, known);
            UpdateXpNext();
            UpdateHeader(player);
        }

        /// <summary>The XP block for a level and XP into it (the XP animation calls this every frame).</summary>
        private static void ShowXpState(int level, int have, int need, bool known)
        {
            if (_rollT < 0) Ui.SetText(_xpLevel, level > 0 ? level.ToString() : "?"); // (while it rolls, the roll sets it)
            float frac = 0;
            if (known)
            {
                frac = Mathf.Clamp01(have / (float)need);
                // your XP / needed XP, bold: yours a shade softer, the target bright (Arena)
                // Tarkov's fractions: the value big and bright, "/ needed" smaller and dimmer
                SetXpText($"<color=#e6e8e9>{Thousands(have)}</color><size=70%><color=#7d8285> / {Thousands(need)}</color></size>");
            }
            else if (level >= ProgData.MaxLevel && level > 0) { _xpTag.gameObject.SetActive(false); Ui.SetText(_xpText, "<color=#e0562f>MAX LEVEL</color>"); }
            else SetXpText("<color=#6f777a>— / —</color>"); // the game's XP table wasn't found: dashes, but the EXP tag stays
            _xpFill.anchorMax = new Vector2(frac, 1);
        }

        private static float _xpTextWidth;

        private static void SetXpText(string text)
        {
            Ui.SetText(_xpText, text);
            float w = Ui.PreferredWidth(_xpText, text);
            _xpTextWidth = w;
            _xpTag.gameObject.SetActive(true);
            _xpTag.offsetMin = new Vector2(w + 12, _xpTag.offsetMin.y);
            _xpTag.offsetMax = new Vector2(w + 12 + 40, _xpTag.offsetMax.y);
        }

        private static Component _listState, _listChipText, _listRank;
        private static Image _listChipIcon, _listChipEdge;
        private static RectTransform _listChip;

        private static void BuildUnlocks(RectTransform top)
        {
            // left third: the selected level's rewards
            var panel = Panel(top, "Unlocks", new Vector2(0, 0), new Vector2(.34f, 1), new Vector2(Margin, S4), new Vector2(-Gutter / 2, -PanelTop));
            _focusList = panel;
            // HUD-style head on a lighter strip (Tarkov's panel titles, MW's level block):
            //   ┤ [✓ LEVEL ACHIEVED]                      30 ITEMS
            //   │ LEVEL 36
            //   │ RENEGADE
            const float HeadH = 84;
            var strip = Ui.Rect(panel, "TitleStrip", new Vector2(0, 1), Vector2.one, new Vector2(0, -HeadH), Vector2.zero);
            Ui.Img(strip, Ui.Hex("#1c1d1e", .75f));
            strip.gameObject.AddComponent<RectMask2D>();
            Ui.Handled(strip, 7, .06f);
            var headStripes = Ui.Img(Ui.Fill(strip, "Stripes"), new Color(1, 1, 1, .035f), Ui.VStripes()); // detail where the eye lands
            headStripes.type = Image.Type.Tiled; headStripes.raycastTarget = false; Ui.AddGrit(headStripes, .035f);
            Ui.Img(Ui.Rect(panel, "Bracket", new Vector2(0, 1), new Vector2(0, 1), new Vector2(PanelPad, -HeadH + 12), new Vector2(PanelPad + 1, -12)), Ui.Hex("#8a8e90", .8f));
            Ui.Img(Ui.Rect(panel, "BracketTick", new Vector2(0, 1), new Vector2(0, 1), new Vector2(PanelPad - 6, -21), new Vector2(PanelPad, -20)), Ui.Hex("#8a8e90", .8f));
            float hx = PanelPad + 12;
            var chip = Ui.Rect(panel, "Chip", new Vector2(0, 1), new Vector2(0, 1), new Vector2(hx, -33), new Vector2(hx + 200, -11));
            _listChipEdge = Ui.Img(chip, Ui.Hex("#6a6e70"));
            Ui.Img(Ui.Fill(chip, "In", 1), Ui.Hex("#141516", .95f));
            _listChipIcon = Ui.Img(Ui.Rect(chip, "Icon", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(6, -7), new Vector2(20, 7)), Ui.Hex(Green), Ui.Tick());
            _listChipText = Ui.Label(Ui.Rect(chip, "Text", Vector2.zero, Vector2.one, new Vector2(25, 0), new Vector2(-7, 0)), "Text", "", TCaps, Text, TextAnchor.MiddleLeft, false, 1);
            _listChip = chip;
            _listTitle = Ui.Label(Ui.Rect(panel, "Title", new Vector2(0, 1), Vector2.one, new Vector2(hx, -62), new Vector2(-PanelPad, -32)), "Text", "", TTitle + 6, Ui.Hex("#e6e8e9"), TextAnchor.MiddleLeft, false, 1);
            _listRank = Ui.Label(Ui.Rect(panel, "Rank", new Vector2(0, 1), Vector2.one, new Vector2(hx, -HeadH + 6), new Vector2(-PanelPad, -62)), "Text", "", TCaps, Grey, TextAnchor.MiddleLeft, false, 3);
            // the count on the right, in small caps
            _listState = Ui.Label(Ui.Rect(panel, "State", new Vector2(0, 1), Vector2.one, new Vector2(PanelPad, -30), new Vector2(-PanelPad, -12)), "Text", "", TCaps, Grey, TextAnchor.MiddleRight, false, Caps);
            Ui.Img(Ui.Rect(panel, "Rule", new Vector2(0, 1), Vector2.one, new Vector2(0, -HeadH - 1), new Vector2(0, -HeadH)), Border);

            var view = Ui.Rect(panel, "Scroll", Vector2.zero, Vector2.one, new Vector2(PanelPad, 14), new Vector2(-S2, -HeadH - S2)); // 14: room for the micro-text line
            SubCanvas(view); // its own drawing layer: tile fades / hovers redraw only the list, not the whole screen
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Ui.Fill(view, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            Ui.Img(viewport, new Color(0, 0, 0, 0), null, true);
            // faint diagonal hatching behind the tiles: the empty part of the panel reads like the game's empty slots
            var hatch = Ui.Img(Ui.Fill(viewport, "Hatch"), new Color(1, 1, 1, .03f), Ui.Hatch());
            hatch.type = Image.Type.Tiled; hatch.raycastTarget = false;
            hatch.transform.SetAsFirstSibling();
            _content = Ui.Rect(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            _content.pivot = new Vector2(.5f, 1);
            var vl = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = S3; // between sections
            vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
            vl.padding = new RectOffset(0, (int)S2, (int)S2, (int)S4);
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = _content;
            AddScrollCue(scroll, view);
            // soft fade at the bottom edge while there's more below
            var fade = Ui.Rect(view, "MoreBelow", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, S5));
            fade.localScale = new Vector3(1, -1, 1);
            _listFade = Ui.Img(fade, Ui.Hex("#10161a", .95f), Ui.VerticalFade());
            _listFade.raycastTarget = false;
            _listScroll = scroll;

        }

        // the hero's detail (MW's primary weapon panel): the most detailed place on the screen, because it's the most important
        private static Component _heroKind, _heroName;
        private static readonly Image[] _heroPips = new Image[5];
        private static Image _heroDiamond, _heroDiamondIn;

        private static void BuildHeroDetail(RectTransform face)
        {
            // viewfinder corners and faint scanlines over the lit face (the light has a texture)
            Ui.Brackets(face, 12, 22, Ui.Hex("#aab2b5", .45f));
            var scan = Ui.Detail(Ui.Img(Ui.Fill(face, "Scan"), new Color(1, 1, 1, .018f), Ui.Scanlines()), .018f);
            scan.type = Image.Type.Tiled; scan.raycastTarget = false;
            // a measuring scale along the bottom, fading out to both ends
            var ruler = Ui.Detail(Ui.Img(Ui.Rect(face, "Ruler", new Vector2(.1f, 0), new Vector2(.9f, 0), new Vector2(0, 14), new Vector2(0, 23)), Ui.Hex("#8f989b", .3f), Ui.Ruler()), .3f);
            ruler.type = Image.Type.Tiled; ruler.raycastTarget = false;
            // a short hatch strip top-right, under the pips (MW's FIELD UPGRADE plate)
            var hatch = Ui.Detail(Ui.Img(Ui.Rect(face, "Hatch", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-122, -44), new Vector2(-22, -38)), new Color(1, 1, 1, .12f), Ui.Hatch()), .12f);
            hatch.type = Image.Type.Tiled; hatch.raycastTarget = false;
            // top-left: what it is, in the rank's colour, and its short name (MW: "Assault Rifle / M4")
            _heroKind = Ui.Label(Ui.Rect(face, "Kind", new Vector2(0, 1), new Vector2(.6f, 1), new Vector2(24, -42), new Vector2(0, -22)), "Text", "", TBody, Ui.Hex("#c9b77a"), TextAnchor.MiddleLeft, false, 1);
            _heroName = Ui.Label(Ui.Rect(face, "Name", new Vector2(0, 1), new Vector2(.6f, 1), new Vector2(24, -68), new Vector2(0, -42)), "Text", "", TTitle, Ui.Hex("#e4e7e8"), TextAnchor.MiddleLeft, true, 1);
            // top-right: where this level sits in its rank (ranks are 5 levels): 5 squares, then + and a diamond for the next rank
            var row = Ui.Rect(face, "Pips", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-122, -32), new Vector2(-22, -22));
            for (int i = 0; i < 5; i++)
            {
                _heroPips[i] = Ui.Img(Ui.Rect(row, "Pip" + i, new Vector2(0, 0), new Vector2(0, 1), new Vector2(i * 14, 0), new Vector2(i * 14 + 10, 0)), Color.white);
                _heroPips[i].raycastTarget = false;
            }
            Ui.Label(Ui.Rect(row, "Plus", new Vector2(0, 0), new Vector2(0, 1), new Vector2(70, -2), new Vector2(82, 2)), "Text", "+", 11, Dim, TextAnchor.MiddleCenter, false);
            var dia = Ui.Box(row, "Diamond", new Vector2(0, .5f), new Vector2(92, 0), new Vector2(9, 9));
            dia.localEulerAngles = new Vector3(0, 0, 45);
            _heroDiamond = Ui.Img(dia, Color.white);
            _heroDiamondIn = Ui.Img(Ui.Fill(dia, "In", 1.5f), Face);
            _heroDiamond.raycastTarget = _heroDiamondIn.raycastTarget = false;
        }

        /// <summary>The hero's caption and rank pips for the featured item.</summary>
        private static void HeroFor(ProgItem it)
        {
            if (_heroKind == null) return;
            if (it == null) { Ui.SetText(_heroKind, ""); Ui.SetText(_heroName, ""); foreach (var p in _heroPips) p.enabled = false; _heroDiamond.enabled = _heroDiamondIn.enabled = false; return; }
            var tier = TierOf(it.Level);
            var light = Ui.Hex(tier.Light);
            string kind = KindOf(it.Tpl);
            if (string.IsNullOrEmpty(kind)) kind = ProgData.Groups.FirstOrDefault(x => x.Key == it.Group).Name ?? "";
            Ui.SetText(_heroKind, kind);
            Ui.SetColor(_heroKind, Color.Lerp(Ui.Hex(tier.Rim), light, .5f));
            Ui.SetText(_heroName, it.Short);
            int into = Mathf.Clamp(it.Level - tier.From + 1, 1, 5);
            for (int i = 0; i < 5; i++)
            {
                _heroPips[i].enabled = true;
                _heroPips[i].color = i < into ? Color.Lerp(light, Color.white, .1f) : new Color(1, 1, 1, .12f);
            }
            // the diamond: the next rank — lit (orange) on a rank's last level, where the next level up is a new rank
            _heroDiamond.enabled = _heroDiamondIn.enabled = true;
            bool rankUpNext = TierOf(it.Level + 1).From != tier.From;
            _heroDiamond.color = rankUpNext ? Ui.Hex(Orange, .9f) : new Color(1, 1, 1, .2f);
        }

        private static void BuildFeatured(RectTransform top)
        {
            // centre: the selected reward, the hero of the screen — one big square preview filling the stage
            var stage = Panel(top, "Stage", new Vector2(.34f, 0), new Vector2(.74f, 1), new Vector2(Gutter / 2, S4), new Vector2(-Gutter / 2, -PanelTop));
            _focusStage = stage;
            var area = Ui.Rect(stage, "PicArea", Vector2.zero, Vector2.one, new Vector2(PanelPad, PanelPad), new Vector2(-PanelPad, -PanelPad));
            var picBox = Ui.Fill(area, "Box");
            var fit = picBox.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1.3f; // landscape: weapons are wide (a square left them floating in an empty box)
            var picFace = Ui.Fill(picBox, "Face", 0); // no frame of its own (the panel is the frame): the lit face is the hero
            Ui.Img(picFace, Face);
            // lighting only: a soft key light from above and a centre glow, so the item sits on a lit surface
            Ui.Img(Ui.Rect(picFace, "KeyLight", new Vector2(0, .45f), Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, .03f), Ui.VerticalFade());
            Ui.Img(Ui.Fill(picFace, "Light"), new Color(1, 1, 1, .07f), Ui.Radial());
            // a spotlight from above onto a floor: a soft pool of light, the floor line, the item's shadow on it
            Ui.Img(Ui.Rect(picFace, "Spot", new Vector2(.15f, .12f), new Vector2(.85f, .95f), Vector2.zero, Vector2.zero), new Color(1, .97f, .9f, .05f), Ui.Radial());
            // the floor light is made of dots (CoD's lights carry a pattern) in the viewed rank's colour, drifting slowly
            var lights = Ui.Fill(picFace, "Lights");
            Ui.OwnCanvas(lights); // it moves every frame: only this redraws
            _featLight = Ui.Detail(Ui.Img(Ui.Box(lights, "DotLight", new Vector2(.5f, .2f), Vector2.zero, new Vector2(560, 170)), new Color(1, 1, 1, .1f), Ui.HalftoneGlow()), .1f);
            _featLight.raycastTarget = false;
            var floorR = Ui.Rect(picFace, "FloorR", new Vector2(.5f, .2f), new Vector2(.92f, .2f), Vector2.zero, new Vector2(0, 1));
            Ui.Img(floorR, new Color(1, 1, 1, .06f), Ui.HorizontalFade());
            var floorL = Ui.Rect(picFace, "FloorL", new Vector2(.08f, .2f), new Vector2(.5f, .2f), Vector2.zero, new Vector2(0, 1));
            Ui.Img(floorL, new Color(1, 1, 1, .06f), Ui.HorizontalFade());
            floorL.localScale = new Vector3(-1, 1, 1); // fades out to both sides
            Ui.Img(Ui.Rect(picFace, "Shadow", new Vector2(.2f, .14f), new Vector2(.8f, .26f), Vector2.zero, Vector2.zero), new Color(0, 0, 0, .5f), Ui.Radial());
            // item bloom: a glow in the picked item's own colours behind it (F12 > CURRENTLY TESTING > Item Bloom)
            _heroBloom = Ui.Img(Ui.Box(picFace, "ItemBloom", new Vector2(.5f, .52f), Vector2.zero, new Vector2(760, 440)), new Color(1, 1, 1, 0), Ui.Radial());
            _heroBloom.raycastTarget = false;
            _featPic = Ui.Img(Ui.Fill(picFace, "Pic", 40), Color.white);
            _featPic.preserveAspect = true;
            _featPic.enabled = false;
            BuildReveal(); // MW's load-in (ProgScreen.Fx)
            _featShort = Ui.Label(Ui.Fill(picFace, "Short", 40), "Text", "", THero, Dim, TextAnchor.MiddleCenter, false, 0, true);
            AddLoader(_featShort, -34, 5);
            Ui.EdgeFade(picFace, .14f, .55f);
            Ui.Grit(picFace, 2, .017f);
            BuildHeroDetail(picFace);
            // secondary locked signal only (the requirement on the right is the primary one)
            _featLock = Ui.Img(Ui.Rect(picFace, "Lock", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-S4 - 28, S4), new Vector2(-S4, S4 + 28)), Ui.Hex("#aeb6b9", .7f), Ui.Lock());
            _featLock.enabled = false;
            // locked: a band across the bottom of the preview says it where the eye is (the right panel explains it)
            var band = Ui.Rect(picFace, "LockedBand", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 40));
            Ui.Img(band, Ui.Hex("#07090a", .82f));
            Ui.Img(Ui.Rect(band, "Edge", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0)), Ui.Hex(Red, .9f));
            Ui.Img(Ui.Rect(band, "Lock", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(S4, -7), new Vector2(S4 + 12, 7)), Ui.Hex(Red, .9f), Ui.Lock());
            _featBandText = Ui.Label(Ui.Rect(band, "Text", Vector2.zero, Vector2.one, new Vector2(S4 + 12 + S2, 0), new Vector2(-S4, 0)), "Text", "", TCaps, Text, TextAnchor.MiddleLeft, false, Caps);
            _featBand = band.gameObject;
            _featBand.SetActive(false);

            // right: the selected reward's details, laid out in content order (nothing at fixed heights, so long names
            // and descriptions push the rest down instead of overlapping)
            var side = Panel(top, "Details", new Vector2(.74f, 0), new Vector2(1, 1), new Vector2(Gutter / 2, S4), new Vector2(-Margin, -PanelTop));
            _focusSide = side;
            // everything in one content flow, INSPECT included: it sits right under the description instead of at the bottom
            var info = Ui.Rect(side, "Info", Vector2.zero, Vector2.one, new Vector2(PanelPad, PanelPad), new Vector2(-PanelPad, -PanelPad));
            _info = info;
            var vl = info.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = S2; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;

            // CATEGORY (the handbook's path for everything but weapons) · the weight top-right, like the inspect window's header
            var typeRow = Ui.Rect(info, "TypeRow", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var trl = typeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            trl.spacing = 5; trl.childAlignment = TextAnchor.MiddleLeft;
            trl.childControlWidth = trl.childControlHeight = true; trl.childForceExpandWidth = trl.childForceExpandHeight = false;
            _featType = FlowText(typeRow, "Category", TCaps, Grey, false, Caps);
            var tle = ((Component)_featType).gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1; tle.minWidth = 0;
            var wIcon = Ui.Rect(typeRow, "WeightIcon", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var wle = wIcon.gameObject.AddComponent<LayoutElement>();
            wle.minWidth = wle.preferredWidth = wle.minHeight = wle.preferredHeight = 14;
            _featWeightIcon = Ui.Img(wIcon, Grey);
            _featWeightIcon.preserveAspect = true;
            _featWeight = FlowText(typeRow, "Weight", TBody, Text, false, 0);
            _featName = FlowText(info, "Name", THero, Text, false, 0, wrap: true);             // Item name
            // never squeezed when a long list of stats fills the panel (the name slid up into the category)
            { var tl = ((Component)_featType).gameObject.GetComponent<LayoutElement>(); if (tl != null) tl.minHeight = 15; }
            ((Component)_featName).gameObject.AddComponent<LayoutElement>().minHeight = 28;
            Rule(info, S1);
            _majorRow = Row(info, "Major", S4);                                                 // DAMAGE  PENETRATION …
            // WEIGHT  SIZE  CALIBER (a second line when there are more than fit the grid)
            _minorRow = Ui.Rect(info, "Minor", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mvl = _minorRow.gameObject.AddComponent<VerticalLayoutGroup>();
            mvl.spacing = S1; mvl.childControlHeight = true; mvl.childControlWidth = true; mvl.childForceExpandHeight = false; mvl.childForceExpandWidth = true;
            BuildRequirement(info);                                                             // REQUIREMENT (right under the item's stats)
            Rule(info, S1);
            // the game's description: scrolls when it's longer than the space left
            var descView = Ui.Rect(info, "Description", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dle = descView.gameObject.AddComponent<LayoutElement>();
            dle.flexibleHeight = 1; dle.minHeight = 0;
            _descSize = dle; _descView = descView;
            _descScroll = descView.gameObject.AddComponent<ScrollRect>();
            _descScroll.horizontal = false; _descScroll.scrollSensitivity = 30; _descScroll.movementType = ScrollRect.MovementType.Clamped;
            var dvp = Ui.Fill(descView, "Viewport");
            dvp.offsetMin = new Vector2(-4, dvp.offsetMin.y); // the mask reaches 4 px further left than the text (it cut the first pixels of g / p / r)
            dvp.gameObject.AddComponent<RectMask2D>();
            Ui.Img(dvp, new Color(0, 0, 0, 0), null, true);
            var dContent = Ui.Rect(dvp, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            dContent.pivot = new Vector2(.5f, 1);
            var dcl = dContent.gameObject.AddComponent<VerticalLayoutGroup>();
            dcl.childControlHeight = true; dcl.childControlWidth = true; dcl.childForceExpandHeight = false; dcl.padding = new RectOffset(4, (int)S2, 0, 0);
            dContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _featDesc = FlowText(dContent, "Text", TBody, Grey, false, 0, wrap: true);
            Refl.Set(_featDesc, "lineSpacing", 6f);
            _descScroll.viewport = dvp; _descScroll.content = dContent;
            AddScrollCue(_descScroll, descView);

            // INSPECT, full width, with its right-click shortcut as a keycap inside it (like Q / E)
            var gap = Ui.Rect(info, "Gap", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _inspectGap = gap.gameObject.AddComponent<LayoutElement>();
            _inspectGap.minHeight = S2; // + the group's 8 = 16 above INSPECT; takes what's left, so INSPECT sits at the panel's bottom
            var btn = Ui.Rect(info, "Inspect", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ble = btn.gameObject.AddComponent<LayoutElement>();
            ble.minHeight = ble.preferredHeight = 40;
            // the game's grey button: pale with dark text while hovered (like its tabs and FILTER BY ITEM / category titles)
            Color bFace = Ui.Hex("#2c3538"), bFaceOn = Ui.Hex("#c4c9cb"), bInk = Text, bInkOn = Ui.Hex("#15191b");
            var bimg = Ui.Img(btn, bFace, null, true);
            var bTop = Ui.Img(Ui.Rect(btn, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero), Ui.Hex("#465155"));
            btn.gameObject.AddComponent<RectMask2D>();
            Ui.Handled(btn, 11, .05f);
            var bText = Ui.Label(btn, "Text", "INSPECT", TStrong, bInk, TextAnchor.MiddleCenter, false, 1);
            var cap = Ui.Rect(btn, "Key", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-S3 - 38, -11), new Vector2(-S3, 11));
            var capEdge = Ui.Img(cap, Ui.Hex("#6a767b"));
            var capFace = Ui.Img(Ui.Fill(cap, "In", 2), Ui.Hex("#1b2326"));
            var capText = Ui.Label(cap, "Text", "RMB", TCaps, Ui.Hex("#c3ccd0"), TextAnchor.MiddleCenter, true);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = bimg;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { if (_featTpl != null) InspectSoon(_featTpl); });
            HoverHook.Add(bimg, on =>
            {
                if (on) Sounds.Play("ButtonOver");
                FadeTo(bimg, on ? bFaceOn : bFace, true);
                FadeTo(bTop, on ? Ui.Hex("#e1e5e6") : Ui.Hex("#465155"), true);
                FadeTo(bText as Graphic, on ? bInkOn : bInk, true);
                FadeTo(capEdge, on ? Ui.Hex("#3a4245") : Ui.Hex("#6a767b"), true);
                FadeTo(capFace, on ? Ui.Hex("#aeb4b6") : Ui.Hex("#1b2326"), true);
                FadeTo(capText as Graphic, on ? bInkOn : Ui.Hex("#c3ccd0"), true);
            });
        }

        private static LayoutElement _reqSize, _descSize, _inspectGap;
        private static Image _reqLeader;

        /// <summary>The requirement's dotted leader: from the end of its text to the start of its value (hidden with no value).</summary>
        private static void ReqLeader(string text, string value)
        {
            if (_reqLeader == null) return;
            string plain = System.Text.RegularExpressions.Regex.Replace(value ?? "", "<[^>]+>", "");
            _reqLeader.enabled = plain.Length > 0;
            if (!_reqLeader.enabled) return;
            float a = 16 + S2 + Ui.PreferredWidth(_featReq, text) + S2, b = Ui.PreferredWidth(_featReqValue, value) + S2;
            var rt = _reqLeader.rectTransform;
            rt.offsetMin = new Vector2(a, rt.offsetMin.y); rt.offsetMax = new Vector2(-b, rt.offsetMax.y);
        }
        private static Image _reqBg, _reqRed;
        private static GameObject _reqHead;
        private static RectTransform _reqRow;
        private static RectTransform _info, _descView;

        /// <summary>The description takes the height it needs (up to what's left, then it scrolls), so INSPECT follows the text.</summary>
        private static string _inspectTpl;
        private static int _inspectFrame;

        /// <summary>
        /// The game's inspect window, opened on the next frame: the click sound and highlight land first, then the window
        /// (opening it costs the game up to ~220 ms for a modded weapon; now it doesn't swallow the click's feedback).
        /// </summary>
        private static void InspectSoon(string tpl) { Sounds.Click(); _inspectTpl = tpl; _inspectFrame = Time.frameCount + 1; }

        private static void InspectPending()
        {
            if (_inspectTpl == null || Time.frameCount < _inspectFrame) return;
            var t = _inspectTpl; _inspectTpl = null;
            Inspect(t);
        }

        private static string _featWantTpl;
        private static int _featWantScale;
        private static float _featWantAt;

        /// <summary>The big picture asked for once the selection has settled (not in the same frame as the click / scroll).</summary>
        private static void FeatureDeferred()
        {
            if (_featWantTpl == null || Time.unscaledTime < _featWantAt || Browsing) return;
            if (_featWantTpl == _featTpl && _featIcon == null) _featIcon = GameItems.IconOf(GameItems.ItemOf(_featWantTpl), _featWantScale);
            _featWantTpl = null;
        }

        private static void FitDescription()
        {
            if (_descSize == null) return;
            _descSize.flexibleHeight = 1; _descSize.preferredHeight = -1;
            if (_inspectGap != null) _inspectGap.flexibleHeight = 0;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_info);
            float avail = _descView.rect.height;
            float need = Refl.Get(_featDesc, "preferredHeight") is float h ? h : avail;
            _descSize.flexibleHeight = 0;
            _descSize.preferredHeight = Mathf.Min(need + S1, avail);
            // the space left goes above INSPECT: it lines up with the bottom of the rewards list and the preview
            if (_inspectGap != null) _inspectGap.flexibleHeight = 1;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_info);
        }
        private static Component _featNote, _featWeight;
        private static Image _featWeightIcon;

        /// <summary>The one place that says whether a reward is yours: REQUIREMENT · state, "Reach level X  n / X",
        /// and for a locked one how much XP is left (and that it's a preview).</summary>
        private static GameObject _featBand;
        private static Component _featBandText;
        private static Image _featReqLock;
        private static Image _dotGrid, _vignette;

        /// <summary>
        /// The surface texture from F12 > Graphics: UI Detailing (grime, scratches, texture layers, the pattern)
        /// and Vignette, as multiples of their built-in strength. Live: changed in F12, it updates at once.
        /// </summary>
        private static BackgroundPattern _randomPattern = BackgroundPattern.Dots;
        private static readonly System.Random _rng = new System.Random();

        /// <summary>F12 Pattern = Random: a different pattern (never the same twice in a row) each time the screen opens.</summary>
        private static void PickRandomPattern()
        {
            if (ProgressionPlugin.Pattern?.Value != BackgroundPattern.Random) return;
            var all = ((BackgroundPattern[])Enum.GetValues(typeof(BackgroundPattern))).Where(p => p != BackgroundPattern.Random && p != _randomPattern).ToArray();
            _randomPattern = all[_rng.Next(all.Length)];
            L.Debug($"background pattern (random): {_randomPattern}");
            ApplyLook();
        }

        public static void PatternChanged()
        {
            if (ProgressionPlugin.Pattern?.Value == BackgroundPattern.Random) PickRandomPattern(); else ApplyLook();
        }

        public static void ApplyLook()
        {
            float sc = Ui.GritK, vg = ProgressionPlugin.Vignette?.Value ?? 1f;
            // the background pattern has its own opacity (F12 Background Pattern Opacity), not UI Detailing's; off in Performance Mode
            float pk = ProgressionPlugin.Low ? 0f : 2f * Mathf.Clamp01((ProgressionPlugin.PatternOpacity?.Value ?? 100) / 100f);
            Ui.ApplyDetail(); // F12 > Graphics > UI Detailing (0 in Performance Mode)
            if (_dotGrid != null)
            {
                // the background pattern (F12 > Graphics > Pattern): the dots tile; the line patterns cover the screen once
                var pat = ProgressionPlugin.Pattern?.Value ?? BackgroundPattern.Dots;
                if (pat == BackgroundPattern.Random) pat = _randomPattern;
                if (pk <= 0) { BgPattern.Use(_dotGrid, null); _dotGrid.enabled = false; } // no detail: no pattern at all (nothing worked out or drawn)
                else if (pat == BackgroundPattern.Dots) { BgPattern.Use(_dotGrid, null); _dotGrid.sprite = Ui.DotGrid(); _dotGrid.type = Image.Type.Tiled; _dotGrid.enabled = true; }
                else BgPattern.Use(_dotGrid, pat.ToString()); // worked out off the main thread; shows once ready, moves while open
                float basis = pat == BackgroundPattern.Dots || pat == BackgroundPattern.Streaks ? .035f
                    : pat == BackgroundPattern.Marble ? .03f : pat == BackgroundPattern.Pixels ? .04f : pat == BackgroundPattern.Terrain ? .055f
                    : pat == BackgroundPattern.Damascus2 ? .04f : .045f;
                _dotGrid.color = new Color(1, 1, 1, Mathf.Clamp01(basis * pk));
            }
            if (_vignette != null) _vignette.color = new Color(0, 0, 0, Mathf.Clamp01(.14f * vg));
            _mood = -1; // repaint the glow next frame
        }
        private static RectTransform _focusXp, _focusList, _focusStage, _focusSide, _pageBarRt;

        private static void BuildRequirement(RectTransform info)
        {
            var req = Ui.Rect(info, "Requirement", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _reqSize = req.gameObject.AddComponent<LayoutElement>();
            _reqSize.minHeight = _reqSize.preferredHeight = 64;
            _reqEdge = Ui.Img(req, Border);
            var reqIn = Ui.Fill(req, "In", 1);
            _reqBg = Ui.Img(reqIn, Ui.Hex("#0b0f11", .9f));
            // locked: a red edge on the left only (the red count is the signal; no red rectangle)
            _reqRed = Ui.Img(Ui.Rect(req, "RedEdge", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(2, 0)), Ui.Hex(Red, .8f));
            _reqHead = Ui.Label(Ui.Rect(reqIn, "Head", new Vector2(0, 1), Vector2.one, new Vector2(S4, -26), new Vector2(-S4, -S2)), "Text", "REQUIREMENT", TCaps, Grey, TextAnchor.MiddleLeft, false, Caps).gameObject;
            _featStatus = Ui.Label(Ui.Rect(reqIn, "State", new Vector2(0, 1), Vector2.one, new Vector2(S4, -26), new Vector2(-S4, -S2)), "Text", "", TCaps, Grey, TextAnchor.MiddleRight, false, Caps);
            var row = Ui.Rect(reqIn, "Row", new Vector2(0, 1), Vector2.one, new Vector2(S4, -54), new Vector2(-S4, -30));
            _reqRow = row;
            // a status icon, not a checkbox (a box read as something to click): a lock while locked, a green tick when met
            var box = Ui.Rect(row, "Box", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -8), new Vector2(16, 8));
            _featCheck = Ui.Img(Ui.Fill(box, "Check", -1), Ui.Hex(Green), Ui.Tick());
            _featReqLock = Ui.Img(Ui.Fill(box, "Lock", 1), Ui.Hex(Red, .9f), Ui.Lock());
            _featReq = Ui.Label(Ui.Rect(row, "Text", Vector2.zero, Vector2.one, new Vector2(16 + S2, 0), Vector2.zero), "Text", "", TStrong, Text, TextAnchor.MiddleLeft, false);
            _featReqValue = Ui.Label(Ui.Rect(row, "Value", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), "Text", "", TStrong, Text, TextAnchor.MiddleRight, true);
            // dotted leader from "Reach level 45" to "1 level away" (placed once the texts are known: ReqLeader)
            _reqLeader = Ui.Img(Ui.Rect(row, "Leader", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -7), new Vector2(0, -5)), Ui.Hex("#6a6e70", .7f), Ui.Leader());
            _reqLeader.type = Image.Type.Tiled; _reqLeader.raycastTarget = false;
            _featNote = Ui.Label(Ui.Rect(reqIn, "Note", new Vector2(0, 1), Vector2.one, new Vector2(S4 + 16 + S2, -78), new Vector2(-S4, -58)), "Text", "", TBody, Grey, TextAnchor.MiddleLeft, false);
        }

        private static ScrollRect _listScroll;
        private static int _scrollTopFrames;
        private static Image _listFade;

        /// <summary>A thin scrollbar on the right edge (the game's look), shown only when the content is taller than its view.</summary>
        private static void AddScrollCue(ScrollRect scroll, RectTransform view)
        {
            // 12 px wide to grab, 2 px wide to see
            var bar = Ui.Rect(view, "ScrollCue", new Vector2(1, 0), Vector2.one, new Vector2(-12, 0), Vector2.zero);
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            // like the game's: a dark 4 px track with a light thumb in it (brighter while hovered)
            Ui.Img(Ui.Rect(bar, "Track", new Vector2(1, 0), Vector2.one, new Vector2(-4, 0), Vector2.zero), Ui.Hex("#07090a", .75f)).raycastTarget = false;
            var area = Ui.Fill(bar, "Area");
            var handle = Ui.Fill(area, "Handle");
            var himg = Ui.Img(handle, new Color(0, 0, 0, 0), null, true);
            var line = Ui.Img(Ui.Rect(handle, "Line", new Vector2(1, 0), Vector2.one, new Vector2(-4, 0), Vector2.zero), Ui.Hex("#aeb5b8", .9f));
            line.raycastTarget = false;
            HoverHook.Add(handle, on => FadeTo(line, on ? Ui.Hex("#e1e5e6") : Ui.Hex("#aeb5b8", .9f)));
            sb.handleRect = handle;
            sb.targetGraphic = himg;
            sb.transition = Selectable.Transition.None;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static RectTransform _majorRow, _minorRow;
        private static ScrollRect _descScroll;
        private static Image _reqEdge;

        /// <summary>A text that sizes itself in a layout group (its height follows its content).</summary>
        private static Component FlowText(RectTransform parent, string name, float size, Color color, bool bold, float spacing, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var c = Ui.AddText(go, "", size, color, TextAnchor.UpperLeft, bold, spacing);
            if (wrap) Ui.SetWrap(c, true);
            return c;
        }

        private static void Rule(RectTransform parent, float gap)
        {
            var rt = Ui.Rect(parent, "Rule", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 1 + gap * 2;
            var line = Ui.Rect(rt, "Line", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 0), new Vector2(0, 1));
            line.localScale = new Vector3(-1, 1, 1); // solid at the start, fading out to the right, like CoD's rules
            Ui.Img(line, Ui.Hex("#5a5e60", .9f), Ui.HorizontalFade());
        }

        private static RectTransform Row(RectTransform parent, string name, float spacing)
        {
            var rt = Ui.Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var hl = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = spacing; hl.childControlHeight = true; hl.childControlWidth = true; hl.childForceExpandWidth = true; hl.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>One big stat: the game's stat icon and a small caps label, the value big under it.</summary>
        private static void Stat(RectTransform row, string label, string value, bool major)
        {
            var cell = Ui.Rect(row, label, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            // every cell the same width (not sized by its text), so the rows share columns
            var le = cell.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0; le.preferredWidth = 0; le.flexibleWidth = 1; le.layoutPriority = 2;
            var vl = cell.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            var head = Ui.Rect(cell, "Head", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var hl = head.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 5; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            if (label.Length > 0) StatIcon(head, label, 16);
            var l = FlowText(head, "Label", TCaps, Grey, false, Caps);
            Ui.SetText(l, label.ToUpperInvariant());
            var v = FlowText(cell, "Value", major ? THero : TStrong, Text, major, 0);
            Ui.SetText(v, value);
            // a meter under the number, like the bars in the game's inspect window (a scale per stat)
            if (major && MeterOf(label, value) is float frac)
            {
                var m = Ui.Rect(cell, "Meter", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var mle = m.gameObject.AddComponent<LayoutElement>();
                mle.minHeight = mle.preferredHeight = 3;
                Ui.Img(m, Ui.Hex("#1f272a"));
                Ui.Img(Ui.Rect(m, "Fill", Vector2.zero, new Vector2(Mathf.Clamp(frac, .03f, 1f), 1), Vector2.zero, Vector2.zero), Ui.Hex("#9aa3a6"));
            }
        }

        /// <summary>How full a stat's meter is (0..1), or null for stats without one.</summary>
        private static float? MeterOf(string label, string value)
        {
            var m = System.Text.RegularExpressions.Regex.Match(value ?? "", @"^[+-]?\d+(\.\d+)?");
            if (!m.Success || !float.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n)) return null;
            switch (label)
            {
                case "Fire rate": return n / 1200f;
                case "Ergonomics": return n / 100f;
                case "Recoil": return n / 400f;
                case "Armor class": return n / 6f;
                case "Penetration": return n / 70f;
                case "Damage": return n / 200f;
                case "Durability": return n / 100f;
                case "Container size": return n / 50f;
                default: return null;
            }
        }

        /// <summary>An icon from one of the game's own inspect rows (its space kept when there is none).</summary>
        private static void GameIcon(RectTransform parent, Sprite sp, float size)
        {
            var rt = Ui.Rect(parent, "Icon", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = size;
            if (sp == null) return;
            var img = Ui.Img(rt, Grey, sp);
            img.preserveAspect = true;
        }

        /// <summary>The game's own icon for a stat (as in its inspect window), or nothing when it has none.</summary>
        private static void StatIcon(RectTransform parent, string label, float size)
        {
            // the space is kept even without an icon, so every label starts at the same place
            var rt = Ui.Rect(parent, "Icon", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = size;
            var sp = StatIcons.Of(label);
            if (sp == null) return;
            var img = Ui.Img(rt, Grey, sp);
            img.preserveAspect = true;
        }

        /// <summary>
        /// One small stat as the game's inspect window shows them (CALIBER · 762x51, EFFECTIVE DISTANCE · 500 meters):
        /// a dark strip, icon and caps label on the left, the value on the right.
        /// </summary>
        private static readonly HashSet<string> WeaponExtras = new HashSet<string> { "CenterOfImpact", "Accuracy", "SightingRange", "RecoilBack", "Velocity", "BulletSpeed", "WeaponFireType" };

        /// <summary>A strip that fits half the panel: short label and value (else it gets a whole line).</summary>
        private static bool StripFitsHalf((string Label, string Value, Sprite Icon, bool Game) f)
            => (f.Label?.Length ?? 0) <= 13 && System.Text.RegularExpressions.Regex.Replace(f.Value ?? "", "<[^>]+>", "").Length <= 12;

        private static void StatStrip(RectTransform row, string label, string value, Sprite icon = null, bool game = false)
        {
            var cell = Ui.Rect(row, label.Length > 0 ? label : "Empty", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var le = cell.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0; le.preferredWidth = 0; le.flexibleWidth = 1; le.minHeight = le.preferredHeight = game ? 23 : 28;
            if (label.Length == 0) return;
            Ui.Img(cell, Ui.Hex("#0b1012", .75f));
            Ui.Img(Ui.Rect(cell, "Edge", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), Ui.Hex("#2b3438", .7f));
            var hl = cell.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(7, 8, 0, 0); hl.spacing = 6; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            if (game) GameIcon(cell, icon, 15); else StatIcon(cell, label, 15);
            var l = FlowText(cell, "Label", TCaps - 1, Grey, false, 1);
            Ui.SetText(l, label.ToUpperInvariant());
            var lle = ((Component)l).gameObject.AddComponent<LayoutElement>();
            lle.flexibleWidth = 0; lle.minWidth = 0;
            // Tarkov's dotted leader from the label to the value ("Character level ........ 25/25")
            var lead = Ui.Rect(cell, "Leader", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ldle = lead.gameObject.AddComponent<LayoutElement>();
            ldle.flexibleWidth = 1; ldle.minWidth = 6; ldle.minHeight = ldle.preferredHeight = 10;
            var dots = Ui.Img(Ui.Rect(lead, "Dots", Vector2.zero, new Vector2(1, 0), new Vector2(0, 1), new Vector2(0, 3)), Ui.Hex("#6a6e70", .7f), Ui.Leader());
            dots.type = Image.Type.Tiled; dots.raycastTarget = false;
            var vgo = new GameObject("Value", typeof(RectTransform));
            vgo.transform.SetParent(cell, false);
            // units a little smaller, never tiny ("3.2 kg" read as "32")
            string shown = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"<size=\d+%>", "<size=85%>");
            Ui.AddText(vgo, shown, TBody, Text, TextAnchor.MiddleRight, false, 0);
        }

        private static void BuildBottom(RectTransform bottom)
        {
            // the arrows move one level, like the A / D keycaps under them (pages: Q / E and the page bar)
            _prev = Arrow(bottom, "Prev", "‹", 0, () => { ShowLevel(_level - 1); Sounds.Click(); });
            _next = Arrow(bottom, "Next", "›", 1, () => { ShowLevel(_level + 1); Sounds.Click(); });
            // A / D move one level (like ← →): shown as keycaps under the arrows, like Q / E at the page bar
            LevelKey(_prev.GetComponent<RectTransform>(), "A", () => { ShowLevel(_level - 1); Sounds.Click(); });
            LevelKey(_next.GetComponent<RectTransform>(), "D", () => { ShowLevel(_level + 1); Sounds.Click(); });

            // cards share the panels' left/right guides (the slots' inner 8 px sit on the margin); arrows live outside, in the margin
            var cards = Ui.Rect(bottom, "Cards", Vector2.zero, Vector2.one, new Vector2(Margin - Gutter / 2, 84), new Vector2(-Margin + Gutter / 2, -S3));
            _cardsRt = cards;
            // MW-style timeline rail under the cards: a tick under each level, a light fill up to where you are (your XP
            // inside your level included), a small orange marker at "you"
            var rail = Ui.Rect(bottom, "Rail", new Vector2(0, 0), new Vector2(1, 0), new Vector2(Margin - Gutter / 2, 71), new Vector2(-Margin + Gutter / 2, 77));
            // MW's XP track: a waveform band along the rail — faint all along (drifting slowly), glowing around your level
            var wave = Ui.Rect(rail, "Wave", Vector2.zero, Vector2.one, new Vector2(0, -13), new Vector2(0, 13));
            Ui.OwnCanvas(wave);
            wave.gameObject.AddComponent<RectMask2D>();
            _waveBase = Ui.Detail(Ui.Img(Ui.Rect(wave, "Base", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(384, 0)), new Color(1, 1, 1, .07f), Ui.Waveform(false)), .07f);
            _waveBase.type = Image.Type.Tiled; _waveBase.raycastTarget = false;
            _waveHot = Ui.Detail(Ui.Img(Ui.Box(wave, "Hot", new Vector2(0, .5f), Vector2.zero, new Vector2(360, 26)), Ui.Hex("#f0c9a8", .3f), Ui.Waveform(true)), .3f);
            _waveHot.raycastTarget = false;
            Ui.Img(Ui.Rect(rail, "Line", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 0), new Vector2(0, 1)), Ui.Hex("#3c3e3f", .9f));
            _railFill = Ui.Rect(rail, "Fill", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -1), new Vector2(0, 1));
            Ui.Img(_railFill, Ui.Hex("#c4c7c8", .85f));
            _railTicks.Clear();
            for (int i = 0; i < PerPage; i++)
            {
                float x = (i + .5f) / PerPage;
                _railTicks.Add(Ui.Img(Ui.Rect(rail, "Tick" + i, new Vector2(x, 0), new Vector2(x, 1), new Vector2(-.5f, -2), new Vector2(.5f, 2)), Ui.Hex("#6a6e70")));
            }
            _railYou = Ui.Img(Ui.Box(rail, "You", new Vector2(0, .5f), Vector2.zero, new Vector2(8, 8)), Ui.Hex(Orange));
            _railYou.rectTransform.localEulerAngles = new Vector3(0, 0, 45); // a small diamond
            // CoD's sweep: a bright beam riding the fill's front while the XP animation moves it
            _railBeam = Ui.Img(Ui.Box(rail, "Beam", new Vector2(0, .5f), Vector2.zero, new Vector2(10, 56)), new Color(1f, .93f, .8f, 0), Ui.Radial());
            _railBeam.raycastTarget = false;
            _railYouText = Ui.Label(Ui.Rect(rail, "YouText", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -8), new Vector2(60, 8)), "Text", "", 10, Ui.Hex(Orange), TextAnchor.MiddleLeft, true, 1);
            _rail = rail;
            for (int i = 0; i < PerPage; i++)
            {
                float a = i / (float)PerPage, b = (i + 1) / (float)PerPage;
                var slot = Ui.Rect(cards, "Slot" + i, new Vector2(a, 0), new Vector2(b, 1), new Vector2(Gutter / 2, 0), new Vector2(-Gutter / 2, 0));
                _cards[i] = new Card(slot, i == 0) { Slot = i };
            }
            BuildLightWall(bottom);


            PerfToggle(bottom);
            HomeButton(bottom);

            // page bar like the Arena battle pass: [Q] ▬▬▬▬ … [E], the page numbers under the segments
            // stretches between fixed side insets (1120 px at 1920 wide, narrower on narrower screens), so it never runs into the checkbox
            var bar = Ui.Rect(bottom, "Pages", new Vector2(0, 0), new Vector2(1, 0), new Vector2(400, 14), new Vector2(-400, 60));
            _pageBarRt = bar;
            KeyBox(bar, "Q", 0, () => ShowPage(_page - 1, -1));
            KeyBox(bar, "E", 1, () => ShowPage(_page + 1, 1));
            var track = Ui.Rect(bar, "Track", Vector2.zero, Vector2.one, new Vector2(40, 0), new Vector2(-40, 0));
            // the page you're on: its range on the game's worn pale plate (dark text), like its selected sub-tabs
            _pagePlate = Ui.Img(Ui.Rect(track, "Plate", Vector2.zero, Vector2.zero, new Vector2(3, 3), new Vector2(-3, 22)), Color.white, Ui.WornPlate());
            _pagePlate.type = Image.Type.Sliced; _pagePlate.raycastTarget = false;
            for (int i = 0; i < Pages; i++)
            {
                int page = i;
                float a0 = i / (float)Pages, a1 = (i + 1) / (float)Pages;
                var seg = Ui.Rect(track, "Seg" + i, new Vector2(a0, 1), new Vector2(a1, 1), new Vector2(2, -22), new Vector2(-2, -12));
                var img = Ui.Img(seg, Ui.Hex("#2c3336"), null, true);
                var hit = Ui.Rect(track, "Hit" + i, new Vector2(a0, 0), new Vector2(a1, 1), Vector2.zero, Vector2.zero);
                var hitImg = Ui.Img(hit, new Color(0, 0, 0, 0), null, true);
                var btn = hit.gameObject.AddComponent<Button>();
                btn.targetGraphic = hitImg;
                btn.onClick.AddListener(() => ShowPage(page, page > _page ? 1 : -1));
                HoverHook.Add(hit, on => { _segHover = on ? page : (_segHover == page ? -1 : _segHover); if (on) Sounds.Play("ButtonOver"); UpdatePageBar(); });
                _segments.Add(img);
                // thin divider between the page numbers, like Arena
                if (i > 0) Ui.Img(Ui.Rect(track, "Div" + i, new Vector2(a0, 0), new Vector2(a0, 0), new Vector2(-1, 4), new Vector2(1, 22)), Ui.Hex("#3a4245"));
                _segmentNums.Add(Ui.Label(Ui.Rect(track, "Num" + i, new Vector2(a0, 0), new Vector2(a1, 0), Vector2.zero, new Vector2(0, 24)), "Text", (i + 1).ToString(), TBody, Dim, TextAnchor.MiddleCenter, false));
            }
        }

        private static Image _perfCheck;

        /// <summary>[ ] PERFORMANCE MODE — bottom-right on the page bar's line, quiet small caps like the other secondary labels.</summary>
        private static void PerfToggle(RectTransform bottom)
        {
            // right-aligned with the card strip's edge, centred on the page bar's row
            var rt = Ui.Rect(bottom, "Perf", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Margin - 200, 25), new Vector2(-Margin, 25 + 24));
            var hit = Ui.Img(rt, new Color(0, 0, 0, 0), null, true);
            var box = Ui.Rect(rt, "Box", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-14, -7), new Vector2(0, 7));
            var edge = Ui.Img(box, Ui.Hex("#5a6468"));
            Ui.Img(Ui.Fill(box, "In", 2), Ui.Hex("#161c1f"));
            _perfCheck = Ui.Img(Ui.Fill(box, "Check", 4), Grey);
            _perfCheck.enabled = Perf;
            var label = Ui.Label(Ui.Rect(rt, "Text", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-14 - S2, 0)), "Text", "PERFORMANCE MODE", TCaps, Dim, TextAnchor.MiddleRight, false, Caps);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                Sounds.Click();
                // = Graphics Low; unticking goes back to what it was before (Medium or High). The setting's change redraws the screen.
                var q = ProgressionPlugin.Quality;
                if (Perf) q.Value = _beforeLow; else { _beforeLow = q.Value; q.Value = GraphicsQuality.Low; }
                L.Info("performance mode " + (Perf ? "on" : "off"));
            });
            HoverHook.Add(rt, on =>
            {
                FadeTo(label as Graphic, on ? Grey : Dim); FadeTo(edge, on ? HoverEdge : Ui.Hex("#5a6468"));
                ShowTip(on ? rt : null, "Lighter pictures · no pattern, blur or texture layers · still emblems, no motion");
            });
        }

        private static GraphicsQuality _beforeLow = GraphicsQuality.Medium;
        private static Component _homeText;
        private static Image _homeEdge, _homePlate, _homeCapFace;
        private static Component _homeCapText;
        private static bool _homeAway, _homeHover;

        /// <summary>
        /// [HOME] BACK TO LEVEL 40 — bottom-left, the mirror of PERFORMANCE MODE bottom-right: same row, same small caps,
        /// left-aligned with the card strip's edge. Always there (the two sides balance); dimmed while you're on your own page.
        /// </summary>
        private static void HomeButton(RectTransform bottom)
        {
            // the plugin's version, very small, in the bottom-left corner under HOME (so a screenshot says which build it is)
            var ver = Ui.Label(Ui.Rect(bottom, "Version", Vector2.zero, Vector2.zero, new Vector2(Margin, 4), new Vector2(Margin + 300, 18)), "Text",
                "LEVELGATE PROGRESSION  v" + ProgressionPlugin.Version, 9, Ui.Hex("#6a7376", .7f), TextAnchor.MiddleLeft, false, 1.5f);
            ((Graphic)ver).raycastTarget = false;
            var rt = Ui.Rect(bottom, "Home", Vector2.zero, Vector2.zero, new Vector2(Margin, 25), new Vector2(Margin + 214, 25 + 24));
            var hit = Ui.Img(rt, new Color(0, 0, 0, 0), null, true);
            // hovered: a pale plate just around the keycap + text (sized to the text in RefreshHome), like the game's MAIN MENU
            _homePlate = Ui.Img(Ui.Rect(rt, "Plate", Vector2.zero, new Vector2(0, 1), new Vector2(-4, -2), new Vector2(200, 2)), new Color(0, 0, 0, 0));
            _homePlate.raycastTarget = false;
            // the keycap, like Q / E: grey rim, dark face, bold caps
            var cap = Ui.Rect(rt, "Key", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -11), new Vector2(46, 11));
            _homeEdge = Ui.Img(cap, Ui.Hex("#5a6468"));
            _homeCapFace = Ui.Img(Ui.Fill(cap, "In", 2), Ui.Hex("#161c1f"));
            Ui.Img(Ui.Rect(cap, "Shine", new Vector2(0, 1), Vector2.one, new Vector2(2, -4), new Vector2(-2, -2)), new Color(1, 1, 1, .08f));
            _homeCapText = Ui.Label(cap, "Text", "HOME", 10, Ui.Hex("#c3ccd0"), TextAnchor.MiddleCenter, true, 1);
            _homeText = Ui.Label(Ui.Rect(rt, "Text", Vector2.zero, Vector2.one, new Vector2(46 + S2, 0), Vector2.zero), "Text", "", TCaps, Dim, TextAnchor.MiddleLeft, false, Caps);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                int p = ProgData.PlayerLevel();
                if (p <= 0 || !_homeAway) return;
                Sounds.Click();
                int mine = Mathf.Min(p, ProgData.MaxLevel);
                if (!InWindow(mine)) { int pg = (mine - 1) / PerPage; ShowPage(pg, pg >= _page ? 1 : -1, mine); } // bring its card back into the row
                ShowLevel(mine);
            });
            HoverHook.Add(rt, on => { _homeHover = on; PaintHome(); if (on && _homeAway) Sounds.Play("ButtonOver"); });
            _homeBtn = rt.gameObject;
            _homeBtn.SetActive(false);
        }

        private static GameObject _homeBtn;

        /// <summary>Shown while you look at any level that isn't yours (hidden on your own level).</summary>
        private static void RefreshHome()
        {
            if (_homeBtn == null) return;
            int player = _xpCardLevel > 0 ? _xpCardLevel : ProgData.PlayerLevel();
            int mine = Mathf.Min(player, ProgData.MaxLevel);
            _homeAway = player > 0 && (_level != mine || !InWindow(mine)); // also when your card is dragged out of view
            if (_homeBtn.activeSelf != _homeAway) _homeBtn.SetActive(_homeAway);
            if (!_homeAway) return;
            string ht = $"BACK TO LEVEL {mine}";
            Ui.SetText(_homeText, ht);
            var pr = _homePlate.rectTransform; // 4 px around the keycap and the text, no further
            pr.offsetMax = new Vector2(46 + S2 + Ui.PreferredWidth(_homeText, ht) + 8, pr.offsetMax.y);
            PaintHome();
        }

        private static void PaintHome()
        {
            if (_homeText == null) return;
            // away from your page: readable (brighter on hover) · on it: dimmed, nothing to go back to
            // hovered: the game's pale plate with dark text, like its buttons
            bool on = _homeAway && _homeHover;
            FadeTo(_homePlate, on ? Ui.Hex("#c4c9cb") : new Color(0, 0, 0, 0), true);
            // the keycap turns over with it (pale face, dark rim and letters), like INSPECT's RMB
            if (_homeCapFace != null) FadeTo(_homeCapFace, on ? Ui.Hex("#aeb4b6") : Ui.Hex("#161c1f"), true);
            if (_homeCapText != null) FadeTo(_homeCapText as Graphic, on ? Ui.Hex("#15191b") : Ui.Hex("#c3ccd0"), true);
            FadeTo(_homeText as Graphic, !_homeAway ? Ui.Hex("#6a7376", .45f) : on ? Ui.Hex("#15191b") : Grey, true);
            FadeTo(_homeEdge, on ? Ui.Hex("#3a4245") : Ui.Hex("#5a6468", _homeAway ? 1f : .45f), true);
        }


        private static void LevelKey(RectTransform arrow, string key, Action click)
        {
            var rt = Ui.Rect(arrow, "Key" + key, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-12, -60), new Vector2(12, -36));
            Ui.Img(rt, Ui.Hex("#5a6468"));
            var img = Ui.Img(Ui.Fill(rt, "In", 2), Ui.Hex("#161c1f"), null, true);
            Ui.Label(rt, "Text", key, TCaps, Ui.Hex("#c3ccd0"), TextAnchor.MiddleCenter, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => click());
        }

        private static void KeyBox(RectTransform parent, string key, float side, Action click)
        {
            // a small keycap, centred on the segments: grey 2 px rim, dark face, bold letter
            var rt = Ui.Rect(parent, "Key" + key, new Vector2(side, 1), new Vector2(side, 1), new Vector2(side == 0 ? 4 : -30, -30), new Vector2(side == 0 ? 30 : -4, -4));
            Ui.Img(rt, Ui.Hex("#5a6468"));
            var img = Ui.Img(Ui.Fill(rt, "In", 2), Ui.Hex("#161c1f"), null, true);
            Ui.Img(Ui.Rect(rt, "Shine", new Vector2(0, 1), Vector2.one, new Vector2(2, -4), new Vector2(-2, -2)), new Color(1, 1, 1, .08f));
            Ui.Label(rt, "Text", key, TCaps, Ui.Hex("#c3ccd0"), TextAnchor.MiddleCenter, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => click());
        }

        private static Button Arrow(RectTransform parent, string name, string glyph, float side, Action click)
        {
            var rt = Ui.Rect(parent, name, new Vector2(side, 0), new Vector2(side, 1), new Vector2(side == 0 ? S1 : -Margin + S1, 78), new Vector2(side == 0 ? Margin - S1 : -S1, -S3));
            var img = Ui.Img(rt, new Color(1, 1, 1, 0), null, true);
            Ui.Label(rt, "Glyph", glyph, 48, Grey, TextAnchor.MiddleCenter, false);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.disabledColor = new Color(1, 1, 1, .25f);
            b.colors = colors;
            b.onClick.AddListener(() => click());
            return b;
        }

        // ---------------------------------------------------------------- the main menu behind

        private static CanvasGroup _menuGroup;

        private static GameObject MenuScreen() => GameObject.Find("Common UI/Common UI/MenuScreen");

        /// <summary>While the screen is open the main menu (ESCAPE FROM TARKOV, CHARACTER, TRADING, EXIT, the beta
        /// warning…) is faded out and not clickable, like the game's battle pass does. Restored on close.</summary>
        private static void HideMenu(bool hide)
        {
            try
            {
                if (hide)
                {
                    if (!ProgressionPlugin.HideMainMenu.Value) return;
                    if (_menuGroup != null) { _menuGroup.alpha = 0; _menuGroup.blocksRaycasts = false; return; } // already hidden: nothing to save again
                    var menu = MenuScreen();
                    if (menu == null) { L.Debug("main menu (Common UI/Common UI/MenuScreen) not found — nothing hidden"); return; }
                    _menuGroup = menu.GetComponent<CanvasGroup>() ?? menu.AddComponent<CanvasGroup>();
                    _menuGroup.alpha = 0; _menuGroup.blocksRaycasts = false;
                    L.Debug("main menu hidden");
                    VersionLabel(false);
                }
                else if (_menuGroup != null)
                {
                    // by the reference kept from hiding (the game may have switched the menu off meanwhile: a lookup by name
                    // wouldn't find it, and it stayed invisible and unclickable), and always back to fully shown — never to a
                    // "saved" state, which could itself be hidden
                    _menuGroup.alpha = 1; _menuGroup.blocksRaycasts = true; _menuGroup.interactable = true;
                    _menuGroup = null;
                    L.Debug("main menu shown again");
                    VersionLabel(true);
                }
            }
            catch (Exception e) { L.ErrorOnce("hiding the main menu", e); }
        }

        private static float _menuCheckAt;
        private static int _menuBad;

        /// <summary>
        /// Safety net while the screen is closed (once a second): the game's main menu must never stay hidden or unclickable
        /// because of us. If it's found that way, it's shown again (and logged).
        /// </summary>
        private static void CheckMenuShown()
        {
            if (Time.unscaledTime < _menuCheckAt) return;
            _menuCheckAt = Time.unscaledTime + 1f;
            try
            {
                if (_menuGroup != null) { L.Warn("main menu still hidden after the screen closed — showing it again"); HideMenu(false); return; }
                var menu = MenuScreen();
                var g = menu != null ? menu.GetComponent<CanvasGroup>() : null;
                bool bad = g != null && (g.alpha < .05f || !g.blocksRaycasts) && MenuHook.CurrentScreen == "MainMenu"
                           && Time.realtimeSinceStartup - MenuHook.ScreenChangedAt > 2f;
                _menuBad = bad ? _menuBad + 1 : 0;
                if (_menuBad >= 2) // two checks in a row (the game's own fades are shorter than that)
                {
                    _menuBad = 0;
                    g.alpha = 1; g.blocksRaycasts = true; g.interactable = true;
                    L.Warn("main menu was hidden / unclickable with the screen closed — shown again");
                }
            }
            catch (Exception e) { L.ErrorOnce("checking the main menu", e); }
        }

        private static Graphic _versionLabel;

        /// <summary>The "SPT 4.1.6 – … | PvE" corner label draws over the screen now that it reaches the menu bar:
        /// hidden while open (just the text switched off), back on close.</summary>
        private static void VersionLabel(bool show)
        {
            try
            {
                if (_versionLabel == null && !show)
                {
                    var types = new List<Type> { typeof(Text) };
                    if (Ui.TmpType != null) types.Add(Ui.TmpType);
                    foreach (var t in types)
                    {
                        foreach (var o in UnityEngine.Object.FindObjectsOfType(t))
                            if (o is Graphic g && g.isActiveAndEnabled && Refl.Get(g, "text") is string txt && txt.StartsWith("SPT ") && txt.Contains("|"))
                            { _versionLabel = g; L.Debug("version label: " + MenuHook.Path(g.transform)); break; }
                        if (_versionLabel != null) break;
                    }
                }
                if (_versionLabel != null) _versionLabel.enabled = show;
            }
            catch (Exception e) { L.ErrorOnce("version label", e); }
        }

        // ---------------------------------------------------------------- the game's windows

        private static Transform WindowsHolder() => GameObject.Find("Preloader UI/Preloader UI/UIContext/WindowsPlaceholder")?.transform;

        /// <summary>Is one of the game's windows (inspect…) open? Then Esc is for it, not for us.</summary>
        private static bool GameWindowOpen()
        {
            var holder = WindowsHolder();
            if (holder == null) return false;
            foreach (Transform t in holder) if (t.gameObject.activeSelf) return true;
            return false;
        }

        private static void Inspect(string tpl)
        {
            _sInspects++;
            L.Info($"inspect {tpl} ({ProgData.NameOf(tpl)})");
            GameItems.Inspect(tpl); // the game's inspect window plays its own opening sound
            _windowLogAt = Time.unscaledTime + .6f;
        }

        /// <summary>After an inspect: the window's objects (to find the game's 3D item preview for the big picture).</summary>
        private static bool _windowDumped;
        private static void LogWindows()
        {
            try
            {
                var holder = WindowsHolder();
                if (holder == null) { L.Debug("after inspect: WindowsPlaceholder not found"); return; }
                var open = holder.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToList();
                L.Info($"after inspect — open windows: {(open.Count == 0 ? "none" : string.Join(", ", open.Select(t => t.name).ToArray()))}");
                if (_windowDumped || open.Count == 0) return;
                _windowDumped = true;
                var sb = new System.Text.StringBuilder();
                void Walk(Transform t, int d)
                {
                    var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray();
                    sb.Append(new string(' ', d * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (hidden)").Append(" [").Append(string.Join(",", comps)).Append("]\n");
                    if (d < 6) foreach (Transform ch in t) Walk(ch, d + 1);
                }
                Walk(open.Last(), 0);
                L.Info("inspect window objects (for the 3D preview):\n" + sb);
            }
            catch (Exception e) { L.ErrorOnce("logging windows", e); }
        }

        // ---------------------------------------------------------------- showing

        /// <summary>levelAfter: the level to pick on the new page (0 = its first level).</summary>
        private static void ShowPage(int page, int dir, int levelAfter = 0)
        {
            L.Step($"ShowPage {page}");
            int want = page;
            page = Mathf.Clamp(page, 0, Pages - 1);
            bool changed = page != _page || _first != page * PerPage + 1; // a dragged row (44–48) re-aligns to its page
            if (!changed && dir != 0) { L.Debug($"page {want + 1}: already at the {(want < 0 ? "first" : "last")} page"); return; }
            _page = page;
            _first = page * PerPage + 1;
            _shiftStart = -10; SetCardOffset(0);
            if (changed) _sPages++;
            if (changed && dir != 0 && !_xpPaging) _level = levelAfter > 0 ? levelAfter : page * PerPage + 1;
            int first = page * PerPage + 1;
            L.Debug($"page {page + 1}/{Pages} (levels {first}–{Mathf.Min(ProgData.MaxLevel, first + PerPage - 1)}){(dir != 0 ? " slide " + (dir > 0 ? "right" : "left") : "")}");
            _prev.interactable = _level > 1;
            _next.interactable = _level < ProgData.MaxLevel;
            UpdatePageBar();
            _hits.RemoveAll(h => h.Rect == null || h.Rect.IsChildOf(_bottom));
            Adopt(_cardIcons); DropRequests(_cardIcons);
            for (int i = 0; i < PerPage; i++) _cards[i].Show(_first + i);
            _cardsDir = dir;
            _cardsStart = dir != 0 && !Calm ? Time.unscaledTime : -10;
            if (changed && dir != 0) { Sounds.Page(); if (_xpPaging) UpdateSelection(); else ShowLevel(_level); }
            else UpdateSelection();
        }

        private static int _segHover = -1;

        // ---------------------------------------------------------------- the card row as a sliding window (drag / one-card steps)
        private static int _first = 1;             // the first level in the card row (44 → 44–48); pages align it to 1, 6, 11…
        private static RectTransform _cardsRt;
        private static bool _fastTiles;
        private static float _shiftStart = -10, _shiftFrom;
        private static bool _shiftFade;
        private static bool _dragArmed, _dragging;
        private static float _dragBase, _dragSuppressUntil, _lastScrollAt;

        /// <summary>You're flying through levels (drag / wheel / keys in the last 0.35 s): pictures and effects hold back.</summary>
        private static bool Browsing => Dragging || Time.unscaledTime - _lastScrollAt < .35f;

        private static bool InWindow(int level) => level >= _first && level < _first + PerPage;
        private static int MaxFirst => Mathf.Max(1, ProgData.MaxLevel - PerPage + 1);
        private static float SlotWidth => _cardsRt != null ? _cardsRt.rect.width / PerPage : 300;

        private static void SetCardOffset(float x) { foreach (var c in _cards) c?.Offset(x); }

        /// <summary>
        /// Shows levels first … first+4 in the card row. animate: the cards glide over from where they were by one card
        /// (and the one coming in fades in). The page bar marks the page the row's middle is on.
        /// </summary>
        private static void ShiftWindow(int first, bool animate)
        {
            first = Mathf.Clamp(first, 1, MaxFirst);
            int delta = first - _first;
            if (delta == 0) return;
            _first = first;
            _page = Mathf.Clamp((_first + PerPage / 2 - 1) / PerPage, 0, Pages - 1);
            _lastScrollAt = Time.unscaledTime;
            _hits.RemoveAll(h => h.Rect == null || h.Rect.IsChildOf(_bottom));
            Adopt(_cardIcons); DropRequests(_cardIcons);
            for (int i = 0; i < PerPage; i++) _cards[i].Show(_first + i);
            _cardsStart = -10;
            if (animate && !Calm) { _shiftFrom = Mathf.Sign(delta) * SlotWidth; _shiftStart = Time.unscaledTime; _shiftFade = true; }
            UpdatePageBar();
            UpdateSelection();
        }

        /// <summary>
        /// Drag the card row with the mouse: it follows the pointer, a level comes in each time it passes half a card
        /// (so the row can show 44–48, not only pages), and it settles into place when let go. A drag is never a click.
        /// </summary>
        public static bool Dragging => _dragging || _flingV != 0;
        private static float _flingV, _dragLastX, _dragTickAt;

        /// <summary>Moves the row by dx (canvas units, + = right): each half card passed brings the next level in.</summary>
        private static float MoveRow(float dx)
        {
            float slot = SlotWidth;
            while (dx < -slot / 2 && _first < MaxFirst) { ShiftWindow(_first + 1, false); _dragBase -= slot; dx += slot; DragTick(); }
            while (dx > slot / 2 && _first > 1) { ShiftWindow(_first - 1, false); _dragBase += slot; dx -= slot; DragTick(); }
            return dx;
        }

        /// <summary>One soft tick as a level passes while you drag (not while a flick coasts), at most every 150 ms.</summary>
        private static void DragTick()
        {
            if (!_dragging || Time.unscaledTime - _dragTickAt < .15f) return; // none while a flick coasts; at most ~6 a second
            _dragTickAt = Time.unscaledTime;
            Sounds.Play("ButtonOver");
        }

        private static void DragCards(BepInEx.IInputSystem input, bool blocked)
        {
            if (_cardsRt == null) return;
            float dt = Mathf.Max(.001f, Time.unscaledDeltaTime);
            var canvas = _cardsRt.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 local;
            bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(_cardsRt, input.mousePosition, cam, out local);
            if (input.GetMouseButtonDown(0) && !blocked && inside && _cardsRt.rect.Contains(local))
            { _dragArmed = true; _dragging = false; _dragBase = local.x; _dragLastX = local.x; _flingV = 0; }
            if (_dragArmed && input.GetMouseButton(0))
            {
                float dx = local.x - _dragBase;
                if (!_dragging && Mathf.Abs(dx) > 10) { _dragging = true; _shiftStart = -10; _cardsStart = -10; L.Debug("cards: drag"); }
                if (_dragging)
                {
                    // how fast it's being pulled (smoothed), for the flick when let go
                    _flingV = Mathf.Lerp(_flingV, (local.x - _dragLastX) / dt, .35f);
                    _dragLastX = local.x;
                    dx = MoveRow(dx);
                    if ((dx < 0 && _first >= MaxFirst) || (dx > 0 && _first <= 1)) dx *= .3f; // resistance at the ends
                    SetCardOffset(dx);
                    _shiftFrom = dx;
                }
            }
            if (_dragArmed && !input.GetMouseButton(0))
            {
                if (_dragging)
                {
                    _dragSuppressUntil = Time.unscaledTime + .2f; // the release isn't a click on a card
                    if (Calm || Mathf.Abs(_flingV) < 500) { _flingV = 0; _shiftStart = Time.unscaledTime; _shiftFade = false; } // settle from where it was let go
                    L.Debug($"cards: dropped at levels {_first}–{Mathf.Min(ProgData.MaxLevel, _first + PerPage - 1)}{(_flingV != 0 ? $", flicked ({_flingV:0}/s)" : "")}");
                }
                else _flingV = 0;
                _dragArmed = _dragging = false;
            }
            // a flick: the row keeps going and slows down, then settles into place
            if (!_dragArmed && _flingV != 0)
            {
                float dx = _shiftFrom + _flingV * dt;
                _flingV *= Mathf.Exp(-dt * 5.5f);
                dx = MoveRow(dx);
                bool atEnd = (dx < 0 && _first >= MaxFirst) || (dx > 0 && _first <= 1);
                if (atEnd) { dx = Mathf.Clamp(dx, -SlotWidth * .25f, SlotWidth * .25f); _flingV = 0; }
                SetCardOffset(dx);
                _shiftFrom = dx;
                if (Mathf.Abs(_flingV) < 180) { _flingV = 0; _shiftStart = Time.unscaledTime; _shiftFade = false; }
            }
        }

        private static Image _pagePlate;

        private static void UpdatePageBar()
        {
            // Arena page bar: a page is lit once every level on it is unlocked; the page you're looking at stands
            // 4 px taller (lit or not), so you always see where you are
            int player = Me;
            for (int i = 0; i < _segments.Count; i++)
            {
                bool cur = i == _page;
                int last = Mathf.Min(ProgData.MaxLevel, (i + 1) * PerPage);
                bool done = player > 0 && last <= player;
                bool hov = i == _segHover && !cur;
                _segments[i].color = done ? (cur ? Ui.Hex("#e8eef0") : hov ? Ui.Hex("#d0d8db") : Ui.Hex("#b4bec2")) : (cur ? Ui.Hex("#3a4346") : hov ? Ui.Hex("#3a4346") : Ui.Hex("#262d30"));
                var rt = _segments[i].rectTransform;
                rt.offsetMin = new Vector2(rt.offsetMin.x, -22);
                rt.offsetMax = new Vector2(rt.offsetMax.x, cur ? -8 : -12);
                Ui.SetColor(_segmentNums[i], cur ? Ui.Hex("#1a1b1c") : done ? Ui.Hex("#b4bec2") : Dim);
                if (cur && _pagePlate != null)
                {
                    var pr = _pagePlate.rectTransform;
                    pr.anchorMin = new Vector2(i / (float)Pages, 0); pr.anchorMax = new Vector2((i + 1) / (float)Pages, 0);
                }
                // labelled by level range ("11–15"), not page number: every range when there's room, else only the current one
                string range = $"{i * PerPage + 1}–{Mathf.Min(ProgData.MaxLevel, (i + 1) * PerPage)}";
                bool room = _segments[i].rectTransform.parent is RectTransform tr && tr.rect.width / Mathf.Max(1, Pages) >= 44;
                Ui.SetText(_segmentNums[i], cur ? $"<b>{range}</b>" : room || _segHover == i ? range : "");
            }
            // YOU: an orange mark over your own page, so you can find your way back while browsing
            int mine = player > 0 ? (Mathf.Min(player, ProgData.MaxLevel) - 1) / PerPage : -1;
            RefreshHome();


        }


        private static int _shownLevel; // the level whose rewards the list shows right now (0 = none)

        private static void ShowLevel(int level, bool force = false)
        {
            L.Step("ShowLevel " + level);
            level = Mathf.Clamp(level, 1, ProgData.MaxLevel);
            if (!force && level == _shownLevel && InWindow(level)) { _level = level; UpdateSelection(); return; } // already showing it
            int page = (level - 1) / PerPage;
            _level = level;
            // one step past the row's edge (wheel, A / D, the arrows): the row slides by one card; further: the whole page
            if (!InWindow(level))
            {
                if (!XpAnimating && (level == _first - 1 || level == _first + PerPage)) ShiftWindow(level < _first ? level : level - PerPage + 1, true);
                else { ShowPage(page, page > _page ? 1 : -1, level); return; }
            }
            // (a card's NEW goes when you click that card, or once all its new rewards are clicked — not when you merely
            // scroll / arrow past it: scrolling one level per notch cleared every card on the way)
            bool fast = Time.unscaledTime - _lastScrollAt < .4f; // levels changing quickly: tiles show at once, no stagger
            _lastScrollAt = Time.unscaledTime;
            _fastTiles = fast;
            float t0 = Time.realtimeSinceStartup;
            var items = ProgData.ItemsAt(level);
            int player = Me;

            SetMood(player > 0 && level > player);
            UpdateXp();

            // the reward list
            var groups = ProgData.Groups.Select(g => (g, list: items.Where(it => it.Group == g.Key).ToList())).Where(x => x.list.Count > 0).ToList();
            Ui.SetText(_listTitle, $"LEVEL {level}");
            if (_microList != null) Ui.SetText(_microList, $"SYS_ONLINE  //  LVL {level:00} / {ProgData.MaxLevel}  //  {items.Count:00} ITEMS");
            Ui.SetText(_listRank, TierOf(level).Name.ToUpperInvariant());
            Ui.SetText(_listState, $"{items.Count} ITEM{(items.Count == 1 ? "" : "S")}");
            // the chip: ✓ LEVEL ACHIEVED · ● YOUR LEVEL · 🔒 LOCKED · 3 LEVELS AWAY
            bool lockedLv = player > 0 && level > player;
            string chipText = player <= 0 ? "" : level == player ? "YOUR LEVEL" : lockedLv ? $"LOCKED  ·  {level - player} LEVEL{(level - player == 1 ? "" : "S")} AWAY" : "LEVEL ACHIEVED";
            _listChip.gameObject.SetActive(chipText.Length > 0);
            if (chipText.Length > 0)
            {
                Ui.SetText(_listChipText, chipText);
                _listChipIcon.sprite = lockedLv ? Ui.Lock() : Ui.Tick();
                _listChipIcon.color = lockedLv ? Ui.Hex(Red, .9f) : level == player ? Ui.Hex(Orange) : Ui.Hex(Green);
                FadeTo(_listChipEdge, lockedLv ? Ui.Hex(Red, .6f) : level == player ? Ui.Hex(Orange, .8f) : Ui.Hex("#6a6e70"), true);
                float cw = 25 + Ui.PreferredWidth(_listChipText, chipText) + 10;
                _listChip.offsetMax = new Vector2(_listChip.offsetMin.x + cw, _listChip.offsetMax.y);
            }

            // old tiles out of the layout right away (Destroy only happens at the end of the frame, and for that frame the
            // new list was laid out under them — it opened scrolled down), and back to the top once it's built
            foreach (Transform ch in _content) { ch.gameObject.SetActive(false); UnityEngine.Object.Destroy(ch.gameObject); }
            _scrollTopFrames = 2;
            _tiles.Clear();
            _tileViews.Clear();
            _tileOrder.Clear();
            ShowTip(null);
            Adopt(_icons); DropRequests(_icons);
            _hits.RemoveAll(h => h.Rect == null || !h.Rect.IsChildOf(_bottom));
            Feature(CardPicks(items).FirstOrDefault()); // opens on the item the level's card shows big (weapons first)
            int max = Mathf.Max(1, Perf ? Mathf.Min(12, ProgressionPlugin.MaxTilesPerCategory.Value) : ProgressionPlugin.MaxTilesPerCategory.Value), n = 0;
            // a fixed column count from the panel width (4 at 1080p), all tiles the same size, 8 px apart
            float width = _content.rect.width - S2;
            if (width < 100) { Canvas.ForceUpdateCanvases(); width = _content.rect.width - S2; } // first open: layout not done yet
            if (width < 100) width = 560;
            int cols = Mathf.Clamp(Mathf.FloorToInt((width + S2) / (TileMin + S2)), 2, 6);
            float cell = Mathf.Floor((width - (cols - 1) * S2) / cols);
            // names that collide at this level (e.g. three "Stich Profi Ches…") show their full name instead
            var dupes = TileNames(items);
            // small levels (a handful of items over several categories): the categories sit side by side as small slots,
            // each its own tab over a box just as wide as its items, like the game's gear slots (EARPIECE › · HEADWEAR ›),
            // instead of a stack of one-tile sections down a ¾-empty panel
            bool compact = items.Count <= 8 && groups.Count > 1;
            // three or fewer: three bigger tiles across (at 4 across, 3 items left most of the panel empty)
            if (compact && items.Count <= 3 && cols > 3) { cols = 3; cell = Mathf.Floor((width - (cols - 1) * S2) / cols); }
            _tileCell = cell;
            const int Frame = 2 * (BoxPad + 1); // a box's frame + padding, both sides
            bool reachedLevel = level <= player || player <= 0;
            void AddTile(RectTransform grid, ProgItem it) => Tile(grid, it, reachedLevel, dupes.TryGetValue(it.Tpl, out var dn) ? dn : null, n++);
            if (compact)
            {
                // tiles sized so `cols` one-item slots fit across; a slot of k items is k tiles wide
                float slotCell = Mathf.Floor((width - cols * Frame - (cols - 1) * S2) / cols);
                RectTransform row = null; float used = 0;
                foreach (var (g, list) in groups)
                {
                    int k = Mathf.Min(list.Count, cols);
                    float w = k * slotCell + (k - 1) * S2 + Frame;
                    if (row == null || used + S2 + w > width + .5f)
                    {
                        row = Ui.Rect(_content, "Slots", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                        var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                        hl.spacing = S2; hl.childAlignment = TextAnchor.UpperLeft;
                        hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = false;
                        used = -S2;
                    }
                    used += S2 + w;
                    var unit = CategoryBlock(row, g, list, list.Count, k, slotCell, true, AddTile);
                    var ule = unit.gameObject.AddComponent<LayoutElement>();
                    ule.minWidth = ule.preferredWidth = w;
                }
            }
            else
            {
                // inside a category's framed box (1 px frame + 6 px each side) the same columns, a little narrower
                float boxCell = Mathf.Floor((width - Frame - (cols - 1) * S2) / cols);
                foreach (var (g, list) in groups) CategoryBlock(_content, g, list, max, cols, boxCell, false, AddTile);
            }
            if (groups.Count == 0)
            {
                var empty = Ui.Rect(_content, "Empty", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
                Ui.Label(empty, "Text", $"Nothing unlocks at level {level}\n<size=13><color=#7d8588>Set items to this level in the Level & Item Editor.</color></size>", TStrong, Text, TextAnchor.MiddleCenter, false);
            }
            _content.anchoredPosition = Vector2.zero;
            _shownLevel = level;
            _sLevels++;
            _tilesStart = Time.unscaledTime;
            UpdateSelection();
            L.Debug($"level {level}: {items.Count} item(s) in {groups.Count} categories, {n} tiles drawn in {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
        }

        /// <summary>
        /// One reward category like the game's slot / container titles (EARPIECE ›, TACTICAL RIG ⌄): a grey tab (bold caps on
        /// the left, count and chevron on the right) over a framed box of its tiles. Hover: the tab lights up pale with dark
        /// text, like the game's. Click: fold / open. Narrow: a small slot (a few tiles wide) instead of the panel's width.
        /// </summary>
        private static RectTransform CategoryBlock(RectTransform parent, (string Key, string Name, string Color, string[] Ids) g, List<ProgItem> list,
            int max, int tileCols, float tileCell, bool narrow, Action<RectTransform, ProgItem> addTile)
        {
            var section = Ui.Rect(parent, "Cat_" + g.Key, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var sl = section.gameObject.AddComponent<VerticalLayoutGroup>();
            sl.spacing = 0; sl.childControlHeight = true; sl.childControlWidth = true; sl.childForceExpandHeight = false; sl.childForceExpandWidth = true;

            var head = Ui.Rect(section, "Head", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            head.gameObject.AddComponent<LayoutElement>().preferredHeight = 26;
            Color face = Ui.Hex("#232a2d", .97f), faceHover = Ui.Hex("#c4c9cb"), ink = Ui.Hex("#d0d5d7"), inkHover = Ui.Hex("#15191b");
            Color dim = Ui.Hex("#7d8588"), dimHover = Ui.Hex("#3a4245"), chevInk = Ui.Hex("#aab2b5");
            var tabFace = Ui.Img(head, face, null, true);
            var stripes = Ui.Img(Ui.Fill(head, "Stripes"), new Color(1, 1, 1, .05f), Ui.VStripes()); // like CoD's BONUS bar
            stripes.type = Image.Type.Tiled; stripes.raycastTarget = false; Ui.Detail(stripes, .05f);
            var top = Ui.Img(Ui.Rect(head, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero), Ui.Hex("#3f494d"));
            var shade = Ui.Img(Ui.Rect(head, "Shade", Vector2.zero, new Vector2(1, .5f), Vector2.zero, Vector2.zero), new Color(0, 0, 0, .18f));
            // lights up from the left while it holds the picked item (ProgScreen.Fx)
            var headLit = Ui.Img(Ui.Fill(head, "Lit"), new Color(1, 1, 1, 0), Ui.HorizontalFade());
            headLit.rectTransform.localScale = new Vector3(-1, 1, 1); headLit.raycastTarget = false;
            string count = list.Count > max ? $"{max} of {list.Count}" : list.Count.ToString();
            float countW = narrow ? 12 : 120;
            var name = Ui.Label(Ui.Rect(head, "Name", Vector2.zero, Vector2.one, new Vector2(narrow ? 7 : 10, 0), new Vector2(-(countW + 30), 0)), "Text",
                g.Name.ToUpperInvariant(), narrow ? TCaps : TCaps + 1, ink, TextAnchor.MiddleLeft, true, narrow ? .5f : 1, true);
            var countText = Ui.Label(Ui.Rect(head, "Count", new Vector2(1, 0), Vector2.one, new Vector2(-28 - countW, 0), new Vector2(-26, 0)), "Text",
                count, TCaps, dim, TextAnchor.MiddleRight, false, 1);
            var chev = Ui.Rect(head, "Chevron", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-22, -8), new Vector2(-8, 8));
            var chevText = Ui.Label(chev, "Text", "›", TStrong + 3, chevInk, TextAnchor.MiddleCenter, false);
            if (!narrow)
            {
                // MW's section heads ("FEATURED ——— -- 01"): a faint rule from the name to the count, two dashes before it,
                // and a small dotted grip at the left edge
                float nameEnd = 10 + Ui.PreferredWidth(name, g.Name.ToUpperInvariant()) + 12;
                float countStart = 26 + Ui.PreferredWidth(countText, count) + 14;
                var rule = Ui.Rect(head, "Rule", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(nameEnd, 0), new Vector2(-(countStart + 14), 1));
                Ui.Detail(Ui.Img(rule, new Color(1, 1, 1, .12f)), .12f).raycastTarget = false;
                for (int d = 0; d < 2; d++)
                {
                    var dash = Ui.Rect(head, "Dash", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-(countStart + 10 - d * 5), 0), new Vector2(-(countStart + 7 - d * 5), 1));
                    Ui.Detail(Ui.Img(dash, new Color(1, 1, 1, .3f)), .3f).raycastTarget = false;
                }
                for (int d = 0; d < 3; d++)
                {
                    var dot = Ui.Rect(head, "Grip", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(3, -5 + d * 4), new Vector2(4, -4 + d * 4));
                    Ui.Detail(Ui.Img(dot, new Color(1, 1, 1, .35f)), .35f).raycastTarget = false;
                }
            }

            // the box: a 1 px frame, a darker inside, 6 px in from it
            var body = Ui.Rect(section, "Box", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Ui.Img(body, Ui.Hex("#2b3438", .9f));
            var boxIn = Ui.Img(Ui.Fill(body, "In", 1), Ui.Hex("#0a0e10", .7f));
            boxIn.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var boxLit = Ui.Img(Ui.Fill(body, "Lit", 1), new Color(1, 1, 1, 0), Ui.HorizontalFade());
            boxLit.rectTransform.localScale = new Vector3(-1, 1, 1); boxLit.raycastTarget = false;
            boxLit.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _sectionLit[g.Key] = (headLit, boxLit); _sectionA.Remove(g.Key);
            var bl = body.gameObject.AddComponent<VerticalLayoutGroup>();
            bl.padding = new RectOffset(BoxPad + 1, BoxPad + 1, BoxPad + 1, BoxPad + 1); bl.childControlHeight = true; bl.childControlWidth = true;
            bl.childForceExpandHeight = false; bl.childForceExpandWidth = true;
            var grid = Ui.Rect(body, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(tileCell, Mathf.Round(tileCell * TileAspect));
            gl.spacing = new Vector2(S2, S2);
            gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = tileCols;

            string catKey = g.Key;
            void ShowFolded()
            {
                bool folded = _foldedCats.Contains(catKey);
                body.gameObject.SetActive(!folded);
                chev.localRotation = Quaternion.Euler(0, 0, folded ? 0 : -90); // › folded, pointing down while open
            }
            ShowFolded();
            var hb = head.gameObject.AddComponent<Button>();
            hb.targetGraphic = tabFace; hb.transition = Selectable.Transition.None;
            hb.onClick.AddListener(() =>
            {
                if (!_foldedCats.Remove(catKey)) _foldedCats.Add(catKey);
                Sounds.Click();
                ShowFolded();
                L.Debug($"category {catKey} {(_foldedCats.Contains(catKey) ? "folded" : "opened")}");
            });
            HoverHook.Add(tabFace, on =>
            {
                if (on) Sounds.Play("ButtonOver");
                FadeTo(tabFace, on ? faceHover : face, true);
                FadeTo(top, on ? Ui.Hex("#e1e5e6") : Ui.Hex("#3f494d"), true);
                FadeTo(shade, new Color(0, 0, 0, on ? .08f : .18f), true);
                FadeTo(name as Graphic, on ? inkHover : ink, true);
                FadeTo(countText as Graphic, on ? dimHover : dim, true);
                FadeTo(chevText as Graphic, on ? inkHover : chevInk, true);
            });
            foreach (var it in list.Take(max)) addTile(grid, it);
            return section;
        }

        private static readonly HashSet<string> _foldedCats = new HashSet<string>(); // categories folded away (click their title)

        private const int BoxPad = 6;        // a category box's inner padding
        private const float TileMin = 118;   // smallest tile width before a column is dropped
        private const float TileTop = 18;    // room at the top for the name's first line (it overlaps the picture's edge, like the game's cells)
        private const float TileAspect = .8f; // tile height / width

        /// <summary>
        /// Tile names for items whose short names collide (three "Bastion" helmets): the short name plus what tells them
        /// apart, i.e. the full names minus the start they share ("Bastion (OD Green)"), instead of full names that got cut
        /// off right where the difference is.
        /// </summary>
        private static Dictionary<string, string> TileNames(List<ProgItem> items)
        {
            var r = new Dictionary<string, string>();
            foreach (var grp in items.GroupBy(x => x.Short).Where(x => x.Count() > 1))
            {
                var names = grp.Select(x => x.Name ?? "").ToList();
                int common = names.Aggregate(names[0].Length, (n, s) => { int i = 0; while (i < n && i < s.Length && s[i] == names[0][i]) i++; return i; });
                while (common > 0 && names[0][common - 1] != ' ') common--; // back to a word boundary
                foreach (var it in grp)
                {
                    string tail = (it.Name ?? "").Length > common ? it.Name.Substring(common).Trim() : "";
                    r[it.Tpl] = common == 0 ? it.Name : tail.Length == 0 ? it.Short : $"{it.Short} {tail}";
                }
            }
            return r;
        }

        /// <summary>An item's kind, singular, from its handbook category ("Assault rifles" → "Assault rifle"); "" if unknown.</summary>
        private static string KindOf(string tpl)
        {
            var k = (GameText.CategoryPath(tpl) ?? "").Split('›').Last().Trim();
            if (k.Length == 0) k = GameItems.WeaponClassOf(tpl) ?? ""; // modded guns the handbook doesn't list
            if (k.Length > 3 && k.EndsWith("s") && !k.EndsWith("ss")) k = k.Substring(0, k.Length - 1);
            return k;
        }

        /// <summary>The item's own background colour in the game's cells (its template's BackgroundColor), dark and see-through.</summary>
        private static Color TintOf(string tpl)
        {
            switch ((GameItems.BackgroundOf(tpl) ?? "").ToLowerInvariant())
            {
                case "blue": return Ui.Hex("#1f3b57", .45f);
                case "green": case "tracergreen": return Ui.Hex("#24421f", .45f);
                case "orange": return Ui.Hex("#5a3413", .45f);
                case "red": case "tracerred": return Ui.Hex("#5a1a18", .45f);
                case "violet": return Ui.Hex("#3f2757", .45f);
                case "yellow": case "traceryellow": return Ui.Hex("#56501a", .42f);
                case "black": return Ui.Hex("#050606", .5f);
                case "grey": return Ui.Hex("#3a3e40", .4f);
                default: return new Color(0, 0, 0, 0);
            }
        }

        /// <summary>One reward tile's parts, so hover / selection / locked can be restyled without rebuilding it.</summary>
        private sealed class TileView
        {
            public ProgItem Item;
            public RectTransform Rt;
            public Image Frame, Face, Top, Pic, Dither, Sheen, Gloss, Bloom;
            public Component Name;
            public bool Locked, Hover, WasSel;
            public SelFrame SelFx;
            public GameObject NewTag;
        }

        // NEW tags: an item's goes away once you click it, a level card's once you've looked at that level; all of them when
        // the screen closes (the last seen level is already yours by then)

        private static void ClickedNew(ProgItem it)
        {
            if (it == null || !NewTags.ClearItem(it.Tpl, it.Level)) return;
            if (_tileViews.TryGetValue(it.Tpl, out var v) && v.NewTag != null) { UnityEngine.Object.Destroy(v.NewTag); v.NewTag = null; }
            L.Debug($"NEW: {it.Name} seen");
            UpdateSelection(); // its level's card loses NEW once none of its rewards are new
        }

        private static readonly Dictionary<string, TileView> _tileViews = new Dictionary<string, TileView>();
        private static readonly List<TileView> _tileOrder = new List<TileView>(); // the list's tiles in reading order (keyboard)

        /// <summary>Up / Down (or W / S): the previous / next reward in the list; it's selected and scrolled into view.</summary>
        private static void StepTile(int dir)
        {
            var shown = _tileOrder.Where(t => t?.Rt != null && t.Rt.gameObject.activeInHierarchy).ToList();
            if (shown.Count == 0) return;
            int at = shown.FindIndex(t => t.Item.Tpl == _featTpl);
            int to = Mathf.Clamp(at < 0 ? 0 : at + dir, 0, shown.Count - 1);
            if (to == at) return;
            var v = shown[to];
            ClickedNew(v.Item);
            Feature(v.Item);
            Sounds.Play("ButtonOver");
            ScrollIntoView(v.Rt);
        }

        private static void ScrollIntoView(RectTransform rt)
        {
            if (_listScroll == null || _listScroll.viewport == null || _content == null) return;
            var vp = _listScroll.viewport; var c = new Vector3[4]; var vc = new Vector3[4];
            rt.GetWorldCorners(c); vp.GetWorldCorners(vc);
            float scale = vp.lossyScale.y > 0 ? vp.lossyScale.y : 1;
            float above = (c[1].y - vc[1].y) / scale, below = (vc[0].y - c[0].y) / scale; // how far the tile sticks out, in canvas units
            var p = _content.anchoredPosition;
            if (above > 0) p.y -= above + S2; else if (below > 0) p.y += below + S2;
            _content.anchoredPosition = p;
        }
        private static int _newFrom; // your level when you last opened the screen: rewards above it (up to yours) are new

        /// <summary>An inventory-style tile: thin frame, dark lit surface, big centred thumbnail, the name under it.</summary>
        private static void Tile(RectTransform grid, ProgItem it, bool reached, string shownName, int index, (string Key, string Name, string Color, string[] Ids)? category = null)
        {
            var rt = Ui.Rect(grid, "Tile", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            var v = new TileView { Item = it, Rt = rt, Locked = !reached };
            v.Frame = Ui.Img(rt, Border, Ui.Chamfer(), true); // cut corners, like the game's prestige reward tiles
            v.Frame.type = Image.Type.Sliced;
            var inner = Ui.Fill(rt, "Inner", 1);
            v.Face = Ui.Img(inner, Face, Ui.Chamfer());
            v.Face.type = Image.Type.Sliced;
            // like a stash cell / the prestige reward tiles: the picture using the whole tile, its short name top-right over it
            // (no per-item background tint: removed on request)
            var thumb = Ui.Rect(inner, "Thumb", Vector2.zero, Vector2.one, new Vector2(0, 4), new Vector2(0, -TileTop));
            Ui.Detail(Ui.Img(Ui.Fill(inner, "Light"), new Color(1, 1, 1, .045f), Ui.Radial()), .045f);
            var placeholder = Ui.Label(Ui.Fill(thumb, "Placeholder", S2), "Text", "", TCaps, Dim, TextAnchor.MiddleCenter, false, 0, true);
            AddLoader(placeholder, -2);
            v.Bloom = Ui.Img(Ui.Box(thumb, "ItemBloom", new Vector2(.5f, .5f), Vector2.zero, new Vector2(150, 110)), new Color(1, 1, 1, 0), Ui.Radial());
            v.Bloom.raycastTarget = false; v.Bloom.enabled = false;
            v.Pic = Ui.Img(Ui.Fill(thumb, "Icon", 0), Color.white);
            var prt = v.Pic.rectTransform;
            // long guns take the full width (they came out tiny at 80% of a box)
            prt.anchorMin = new Vector2(.05f, .04f); prt.anchorMax = new Vector2(.95f, .96f); prt.offsetMin = prt.offsetMax = Vector2.zero;
            v.Pic.preserveAspect = true;
            v.Pic.enabled = false;
            // selection: a 2 px light bar along the top (so selected isn't told by colour alone)
            v.Top = Ui.Img(Ui.Rect(inner, "Top", new Vector2(0, 1), Vector2.one, new Vector2(9, -2), Vector2.zero), Select); // clear of the cut corner
            v.Top.enabled = false;
            // texture: light from above and a fine grit on the face; picked: a pixel dissolve along the top edge
            Ui.Img(Ui.Rect(inner, "TopLight", new Vector2(0, .5f), Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, .025f), Ui.VerticalFade()).raycastTarget = false;
            v.Dither = Ui.Img(Ui.Rect(inner, "Dither", new Vector2(0, 1), new Vector2(.7f, 1), new Vector2(9, -8), new Vector2(0, -2)), new Color(1, 1, 1, .16f), Ui.Dither());
            v.Dither.raycastTarget = false; v.Dither.enabled = false; Ui.Detail(v.Dither, .16f);
            // picked: a very light reflection (a soft sheen over the upper half, a glossy line down the right edge), like CoD's
            v.Sheen = Ui.Img(Ui.Box(inner, "Sheen", new Vector2(.62f, .66f), Vector2.zero, new Vector2(170, 110)), new Color(1, 1, 1, .07f), Ui.Radial());
            v.Sheen.raycastTarget = false; v.Sheen.enabled = false; Ui.Detail(v.Sheen, .07f);
            v.Gloss = Ui.Img(Ui.Rect(inner, "Gloss", new Vector2(1, .1f), new Vector2(1, .9f), new Vector2(-3, 0), new Vector2(-2, 0)), new Color(1, 1, 1, .22f), Ui.VerticalFade());
            v.Gloss.raycastTarget = false; v.Gloss.enabled = false; Ui.Detail(v.Gloss, .22f);
            // the short name, top-right like the game's cells (two lines at most; look-alikes say what sets them apart)
            // a soft dark fade under the name, so two-line names stay readable over the picture
            var nameFade = Ui.Img(Ui.Rect(inner, "NameFade", new Vector2(0, 1), Vector2.one, new Vector2(0, -40), Vector2.zero), new Color(0, 0, 0, .5f), Ui.VerticalFade());
            nameFade.raycastTarget = false;
            // two lines when needed ("ACHHC (Coyote Brown)" was cut to one line "ACHHC (Coyote B…")
            string nm = shownName ?? it.Short;
            v.Name = Ui.Label(Ui.Rect(inner, "Name", new Vector2(0, 1), Vector2.one, new Vector2(S2, -TileTop - 22), new Vector2(-S1 - 2, -S1)), "Text",
                nm, nm.Length > 16 ? 12.5f : 13.5f, Grey, TextAnchor.UpperRight, false, 0, true);
            Ui.SetWrap(v.Name, true);
            Refl.Set(v.Name, "lineSpacing", -8f);
            // locked: just a small lock, bottom-left (the level is said once, in the list's head)
            if (!reached)
                Ui.Img(Ui.Rect(inner, "Lock", Vector2.zero, Vector2.zero, new Vector2(S1 + 2, S1 + 2), new Vector2(S1 + 13, S1 + 15)), Ui.Hex(Red, .95f), Ui.Lock());
            // newly reached since you last opened the screen: a small restrained tag (top-right)
            if (reached && NewTags.Item(it.Tpl))
            {
                // CoD's NEW: pinned to the tile's top-right corner, overhanging its edge; the name moves down to make room
                var tag = Ui.NewBadge(rt, new Vector2(1, 1), new Vector2(-15, 1));
                v.NewTag = tag.gameObject;
                var nr = ((Component)v.Name).transform.parent as RectTransform;
                if (nr != null) nr.offsetMax = new Vector2(nr.offsetMax.x, nr.offsetMax.y - 12);
            }
            RequestIcon(_icons, it.Tpl, TileScaleOf(it.Tpl), v.Pic, placeholder);
            _hits.Add((rt, it));
            HoverHook.Add(v.Frame, on =>
            {
                if (v.Hover == on) return;
                v.Hover = on;
                if (on) { Sounds.Play("ButtonOver"); PlayGlitch(v.Face.rectTransform); }
                ApplyTile(v);
                ShowTip(on ? v : null);
            });
            _tileViews[it.Tpl] = v;
            _tileOrder.Add(v);
            ApplyTile(v, true);
            if (_fastTiles || Calm) group.alpha = 1; // browsing: no one-by-one fade (it flickered while scrolling)
            else _tiles.Add((group, inner, Mathf.Min(index, 40) * .012f));
        }

        /// <summary>normal: neutral · hover: brighter edge and surface · selected: light edge + top bar + bright name · locked: muted.</summary>
        private static void ApplyTile(TileView v, bool instant = false)
        {
            if (v?.Frame == null) return;
            bool sel = v.Item.Tpl == _featTpl;
            if (sel && !v.WasSel && !instant) PlayShine(v.Face.rectTransform); // picked: one soft shine across it
            v.WasSel = sel;
            if (sel && v.SelFx == null) v.SelFx = MakeSelFrame(v.Rt, 3);
            if (v.SelFx != null) v.SelFx.On = sel;
            FadeTo(v.Frame, sel ? Select : v.Hover ? HoverEdge : Border, instant);
            FadeTo(v.Face, sel ? FaceSelect : v.Hover ? FaceHover : Face, instant);
            v.Top.enabled = sel;
            if (v.Dither != null) v.Dither.enabled = sel;
            if (v.Sheen != null) { v.Sheen.enabled = sel; v.Gloss.enabled = sel; }
            v.Pic.rectTransform.localScale = Vector3.one * (v.Hover ? 1.04f : 1f); // a slight lift on hover
            float pa = v.Locked ? (v.Hover || sel ? .8f : .6f) : 1f;
            FadeTo(v.Pic, new Color(1, 1, 1, pa), instant);
            // names stay readable; locked shows through the dimmed icon, not unreadable text
            FadeTo(v.Name as Graphic, sel ? Color.white : v.Hover ? Text : v.Locked ? Grey : Ui.Hex("#b9c0c3"), instant);
        }

        // hover tooltip: the full name above the tile
        private static RectTransform _tip;
        private static Component _tipText;

        private static void ShowTip(TileView v) => ShowTip(v?.Rt, v?.Item.Name);

        /// <summary>Shows text in the tooltip just above rt (null hides it).</summary>
        private static void ShowTip(RectTransform rt, string name) => ShowTip(rt, name, false);

        /// <summary>below: under the element instead of above it (card pictures: above would cover the rank label).</summary>
        private static void ShowTip(RectTransform rt, string name, bool below)
        {
            if (_tip == null) return;
            if (rt == null || string.IsNullOrEmpty(name)) { _tip.gameObject.SetActive(false); return; }
            Ui.SetText(_tipText, name);
            float w = Ui.PreferredWidth(_tipText, name) + S3 * 2;
            _tip.sizeDelta = new Vector2(Mathf.Min(w, 760), 26); // (420 cut long tips off: the text ran past the box and the screen)
            var r = rt.rect;
            _tip.pivot = new Vector2(.5f, below ? 1 : 0);
            _tip.position = rt.TransformPoint(new Vector3(r.center.x, below ? r.yMin : r.yMax, 0));
            _tip.anchoredPosition += new Vector2(0, below ? -S1 : S1);
            // keep it inside the screen
            if (_tip.parent is RectTransform layer)
            {
                float half = layer.rect.width / 2 - S2, w2 = _tip.sizeDelta.x / 2;
                var ap = _tip.anchoredPosition;
                ap.x = Mathf.Clamp(ap.x, -half + w2, half - w2);
                _tip.anchoredPosition = ap;
            }
            _tip.SetAsLastSibling();
            _tip.gameObject.SetActive(true);
        }

        // Icons the game hasn't drawn yet cost it 20-100 ms each, all in one frame when a big level opens (the stutter).
        // So they are asked for a few per frame instead: the cards' first, then the list in order.
        private static readonly List<(List<(object Icon, Image Pic, Component Placeholder, string Tpl)> Target, string Tpl, int Scale, Image Pic, Component Placeholder)> _iconRequests
            = new List<(List<(object, Image, Component, string)>, string, int, Image, Component)>();

        private static void RequestIcon(List<(object Icon, Image Pic, Component Placeholder, string Tpl)> target, string tpl, int scale, Image pic, Component placeholder)
        {
            // one already in the game's cache costs nothing: take it now
            _iconRequests.Add((target, tpl, scale, pic, placeholder));
        }

        /// <summary>
        /// Pictures still being drawn for a page / level we just left: not dropped — the pre-loader keeps collecting them,
        /// so they're kept (sharp) for when you come back, and the game's own icon goes back to stash size.
        /// </summary>
        private static void Adopt(List<(object Icon, Image Pic, Component Placeholder, string Tpl)> list)
        {
            foreach (var e in list) if (e.Icon != null) _prefetching.Add((e.Icon, e.Tpl));
            list.Clear();
        }

        private static void DropRequests(List<(object Icon, Image Pic, Component Placeholder, string Tpl)> target) => _iconRequests.RemoveAll(r => r.Target == target);

        private static void RunIconRequests()
        {
            if (_iconRequests.Count == 0) return;
            int budget = Browsing ? 1 : Perf ? 2 : 4; // one picture a frame while flying through levels (each can cost 60–140 ms)
            var t0 = Time.realtimeSinceStartup;
            // cards first
            _iconRequests.Sort((a, b) => (a.Target == _cardIcons ? 0 : 1).CompareTo(b.Target == _cardIcons ? 0 : 1));
            while (_iconRequests.Count > 0 && budget-- > 0 && Time.realtimeSinceStartup - t0 < .025f)
            {
                var r = _iconRequests[0];
                _iconRequests.RemoveAt(0);
                if (r.Pic == null) continue;
                var have = r.Scale != 1 ? GameItems.CopyOf(r.Tpl, r.Scale) : null;
                // drawn earlier this visit: reuse it (dragging the row back and forth redrew the AS VAL six times, 70–84 ms each)
                if (have == null && r.Scale == 1 && _drawn.TryGetValue(r.Tpl, out var seen) && seen != null && seen.texture != null) have = seen; // (not one the game has since redrawn)
                if (have != null) { r.Pic.sprite = have; r.Pic.enabled = true; if (r.Placeholder != null) r.Placeholder.gameObject.SetActive(false); budget++; continue; }
                r.Target.Add((GameItems.IconOf(GameItems.ItemOf(r.Tpl), r.Scale), r.Pic, r.Placeholder, r.Tpl));
            }
        }

        // Pre-loading: while nothing else is loading, the card pictures of the page before and after the current one are
        // drawn ahead (one per frame at most), so Q / E show them straight away.
        private static readonly List<(object Icon, string Tpl)> _prefetching = new List<(object, string)>();
        private static readonly HashSet<string> _prefetchAsked = new HashSet<string>();
        private static float _watchCardsAt;
        private static readonly Queue<(string Tpl, int Scale)> _landJobs = new Queue<(string, int)>();
        /// <summary>Pictures are asked for again from scratch (the kept ones were cleared).</summary>
        public static void ForgetPictures() { _prefetchAsked.Clear(); _prefetchQueue.Clear(); _prefetchPage = -1; }
        private static int _prefetchPage = -1;
        private static readonly Queue<(string Tpl, int Scale)> _prefetchQueue = new Queue<(string, int)>();

        /// <summary>Card pictures (and their pre-loading) share one scale per item: about 120 px on the long side.</summary>
        // weapons only on High (like the centre picture: High redraws the stash's weapons at stash size after closing)
        private static float _tileCell = 130;

        /// <summary>List tiles: stash size, except on High (up to 2x, for the tile's real size on screen).</summary>
        private static int TileScaleOf(string tpl)
        {
            if (!ProgressionPlugin.High) return 1;
            var c = _content != null ? _content.GetComponentInParent<Canvas>() : null;
            float px = _tileCell * .8f * (c != null && c.scaleFactor > 0 ? c.scaleFactor : 1f);
            int scale = Mathf.Min(2, GameItems.CardScale(tpl, px));
            if (scale > 1 && ProgData.GroupOf(tpl) == "Weapons") _sharpWeaponShown = true;
            return scale;
        }

        /// <summary>
        /// Real screen pixels of a card's big picture: ~200 wide at 1920x1080, scaled with the game's resolution
        /// (2560x1440: ~267, 3840x2160: ~400), so cards are drawn as sharp as the screen can show them.
        /// </summary>
        private static float CardPx()
        {
            var c = _bottom != null ? _bottom.GetComponentInParent<Canvas>() : null;
            float sf = c != null && c.scaleFactor > 0 ? c.scaleFactor : Screen.height / 1080f;
            return 200f * Mathf.Max(.5f, sf);
        }

        private static int CardScaleOf(string tpl)
        {
            bool weapon = ProgData.GroupOf(tpl) == "Weapons";
            if (Perf || (weapon && !ProgressionPlugin.High)) return 1;
            int scale = GameItems.CardScale(tpl, CardPx());
            if (weapon && scale > 1) _sharpWeaponShown = true; // weapons get repaired on close
            return scale;
        }

        /// <summary>Every picture these pages show bigger than stash size: each level's centre item and its card pictures.</summary>
        private static List<(string Tpl, int Scale)> PictureJobs(IEnumerable<int> pages)
        {
            var jobs = new List<(string, int)>();
            var seen = new HashSet<string>();
            void Add(string tpl, int scale) { if (scale != 1 && seen.Add(tpl + "@" + scale) && GameItems.CopyOf(tpl, scale) == null) jobs.Add((tpl, scale)); }
            foreach (var p in pages)
            {
                if (p < 0 || p >= Pages) continue;
                for (int level = p * PerPage + 1; level <= Mathf.Min(ProgData.MaxLevel, (p + 1) * PerPage); level++)
                {
                    var picks = CardPicks(ProgData.ItemsAt(level));
                    if (picks.Count > 0) Add(picks[0].Tpl, FeatScaleOf(picks[0])); // what the level opens on
                    foreach (var it in picks) Add(it.Tpl, CardScaleOf(it.Tpl));
                }
            }
            return jobs;
        }

        // ---------------------------------------------------------------- loading screen (first open of a menu visit)
        // Like the hideout's: the pictures of this page and the pages next to it are drawn once behind a short loading
        // screen, then everything shows up sharp. Reset before a raid (the kept pictures are let go to free memory).
        private static bool _warm, _loading;
        private static RectTransform _loadLayer;
        private static Component _loadText;
        private static CanvasGroup _loadGroup;
        private static readonly Queue<(string Tpl, int Scale)> _loadQueue = new Queue<(string, int)>();
        private static readonly List<(object Icon, string Tpl)> _loadWaiting = new List<(object, string)>();
        private static int _loadTotal, _loadDone;
        private static float _loadStart, _loadHideAt = -1;

        // like the game's own loading screen: black, a glowing hex mark in the middle, a quiet line bottom-left
        private static Image _loadMark, _loadGlow;

        private static void BuildLoading(RectTransform root)
        {
            _loadLayer = Ui.Rect(root, "Loading", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(_loadLayer);
            _loadGroup = _loadLayer.gameObject.AddComponent<CanvasGroup>();
            Ui.Img(_loadLayer, Color.black, null, true); // blocks clicks while loading
            _loadGlow = Ui.Img(Ui.Box(_loadLayer, "Glow", new Vector2(.5f, .5f), Vector2.zero, new Vector2(220, 220)), new Color(.85f, .79f, .62f, .22f), Ui.Radial());
            // the game's own loading mark (its object, with its own animation) when it can be found; else its sprite; else a stand-in
            var markBox = Ui.Box(_loadLayer, "Mark", new Vector2(.5f, .5f), Vector2.zero, new Vector2(76, 76));
            _loadClone = CloneGameLoader(markBox);
            _loadMark = Ui.Img(markBox, Ui.Hex("#e9e6df"), _loadClone != null ? null : GameSpinner() ?? Ui.HexSpinner());
            _loadMark.enabled = _loadClone == null;
            _loadMark.preserveAspect = true;
            _loadGlow.raycastTarget = _loadMark.raycastTarget = false;
            _loadText = Ui.Label(Ui.Rect(_loadLayer, "State", Vector2.zero, new Vector2(1, 0), new Vector2(12, 6), new Vector2(-12, 26)), "Text", "", TCaps, Ui.Hex("#5f6568"), TextAnchor.MiddleLeft, false, 1);
            _loadLayer.gameObject.SetActive(false);
        }

        private static GameObject _loadClone;

        /// <summary>
        /// Tarkov's own loading indicator (the glowing hex on its loading screens): looked up among the game's UI objects by
        /// name (loader / spinner / loading, with an image, small), copied under ours with its animation. Everything that looks
        /// like one is logged, so the right one can be pinned down if the pick is wrong.
        /// </summary>
        private static GameObject CloneGameLoader(RectTransform parent)
        {
            try
            {
                string PathOf(Transform t) { var p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }
                // by name under the game's Preloader UI (found even while the loader itself is switched off)
                var known = GameObject.Find("Preloader UI")?.transform.Find("Preloader UI/Loader");
                if (known is RectTransform krt && known.GetComponentInChildren<Image>(true) != null) return CopyLoader(krt, parent, "Preloader UI/Preloader UI/Loader");
                bool Named(string n) => n.IndexOf("loader", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("spinner", StringComparison.OrdinalIgnoreCase) >= 0
                                        || n.IndexOf("loading", StringComparison.OrdinalIgnoreCase) >= 0;
                var found = Resources.FindObjectsOfTypeAll<RectTransform>()
                    .Where(rt => rt != null && Named(rt.name) && !PathOf(rt).Contains("LevelGateProgression"))
                    .Select(rt => (Rt: rt, Path: PathOf(rt), Img: rt.GetComponentInChildren<Image>(true), Anim: rt.GetComponentInChildren<Animator>(true) != null || rt.GetComponentInChildren<Animation>(true) != null))
                    .Where(c => c.Img != null && c.Img.sprite != null && c.Rt.rect.width <= 320 && c.Rt.rect.height <= 320 && c.Rt.childCount <= 12)
                    .ToList();
                L.Info($"loading screen: {found.Count} game object(s) that look like a loading mark: " +
                       string.Join(" | ", found.Take(25).Select(c => $"{c.Path} [{c.Img.sprite.name}{(c.Anim ? ", animated" : "")}, {c.Rt.rect.width:0}x{c.Rt.rect.height:0}]").ToArray()));
                // the preloader / loading screen's own, animated one first
                var pick = found.OrderByDescending(c => (c.Path.IndexOf("preloader", StringComparison.OrdinalIgnoreCase) >= 0 ? 4 : 0)
                                                        + (c.Path.IndexOf("loadingscreen", StringComparison.OrdinalIgnoreCase) >= 0 ? 3 : 0)
                                                        + (c.Anim ? 2 : 0)
                                                        + (c.Rt.name.IndexOf("spinner", StringComparison.OrdinalIgnoreCase) >= 0 || c.Rt.name.IndexOf("loader", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0))
                               .FirstOrDefault();
                if (pick.Rt == null) { L.Info("loading screen: the game's loading mark wasn't found — using a stand-in"); return null; }
                return CopyLoader(pick.Rt, parent, pick.Path);
            }
            catch (Exception e) { L.Debug("loading screen: couldn't copy the game's loading mark: " + e.Message); return null; }
        }

        private static GameObject CopyLoader(RectTransform source, RectTransform parent, string path)
        {
            try
            {
                // copied under an inactive holder (none of its scripts wake up), stripped to images + animation, then shown
                var holder = new GameObject("LoaderHolder", typeof(RectTransform));
                holder.SetActive(false);
                holder.transform.SetParent(parent, false);
                var copy = UnityEngine.Object.Instantiate(source.gameObject, holder.transform, false);
                foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb is Graphic || mb is LayoutElement || mb is Mask || mb is RectMask2D || mb is CanvasScaler) continue;
                    UnityEngine.Object.DestroyImmediate(mb);
                }
                copy.transform.SetParent(parent, false);
                UnityEngine.Object.Destroy(holder);
                var crt = (RectTransform)copy.transform;
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(.5f, .5f);
                crt.anchoredPosition = Vector2.zero;
                foreach (var g in copy.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
                copy.SetActive(true);
                L.Info($"loading screen: using the game's '{path}'");
                return copy;
            }
            catch (Exception e) { L.Debug("loading screen: couldn't copy the game's loading mark: " + e.Message); return null; }
        }

        /// <summary>The game's own loading mark, if one of its sprites is clearly it (names logged once to find it).</summary>
        private static Sprite GameSpinner()
        {
            try
            {
                var want = new[] { "preloader", "loader", "spinner", "loading", "loading_icon", "loadingicon", "loading_spinner" };
                var all = Resources.FindObjectsOfTypeAll<Sprite>();
                var named = all.Where(sp => sp != null && (sp.name.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0 || sp.name.IndexOf("spin", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(sp => sp.name).Distinct().Take(40).ToArray();
                L.Info($"loading screen: {all.Length} sprites in memory; named like load / spin: {(named.Length == 0 ? "none" : string.Join(", ", named))}");
                var pick = all.FirstOrDefault(sp => sp != null && want.Any(w => sp.name.Equals(w, StringComparison.OrdinalIgnoreCase)) && sp.rect.width <= 512);
                if (pick != null) L.Info($"loading screen: using the game's '{pick.name}' mark");
                return pick;
            }
            catch (Exception e) { L.Debug("loading screen sprite: " + e.Message); return null; }
        }

        private static void StartLoading()
        {
            if (_warm || _loadLayer == null) return;
            Canvas.ForceUpdateCanvases(); // the preview's real size, for the render sizes
            _loadQueue.Clear(); _loadWaiting.Clear();
            foreach (var job in PictureJobs(new[] { _page, _page - 1, _page + 1 })) _loadQueue.Enqueue(job);
            // the rewards list it opens on (stash-size icons come from the game's cache, so they're quick)
            foreach (var it in ProgData.ItemsAt(_level).Take(24)) _loadQueue.Enqueue((it.Tpl, TileScaleOf(it.Tpl)));
            // the rank emblems of these pages (each sheet loaded once, ~15 ms) — nothing pops in afterwards
            for (int p = Mathf.Max(0, _page - 1); p <= Mathf.Min(Pages - 1, _page + 1); p++) Emblems.Preload(p * PerPage + 1);
            Emblems.Preload(Mathf.Max(1, ProgData.PlayerLevel()));
            _loadTotal = _loadQueue.Count; _loadDone = 0;
            _warm = true;
            if (_loadTotal == 0) return;
            _loading = true; _loadStart = Time.unscaledTime; _loadHideAt = -1;
            _loadGroup.alpha = 1;
            _loadLayer.gameObject.SetActive(true);
            L.Info($"loading: drawing {_loadTotal} picture(s) for pages {Mathf.Max(1, _page)}–{Mathf.Min(Pages, _page + 2)} (graphics {ProgressionPlugin.Quality.Value})");
        }

        /// <summary>True while the loading screen is up (the screen's own input waits).</summary>
        private static bool RunLoading()
        {
            if (_loadHideAt > 0)
            {
                float f = (_loadHideAt - Time.unscaledTime) / .25f;
                _loadGroup.alpha = Mathf.Clamp01(f);
                if (f <= 0) { _loadHideAt = -1; _loadLayer.gameObject.SetActive(false); }
            }
            if (!_loading) return false;
            for (int i = _loadWaiting.Count - 1; i >= 0; i--)
            {
                var (icon, tpl) = _loadWaiting[i];
                if (icon != null) { GameItems.TakeSprite(icon, tpl, out bool done); if (!done) continue; }
                _loadWaiting.RemoveAt(i); _loadDone++;
            }
            // a few at a time: the game draws them in the background
            while (_loadWaiting.Count < 4 && _loadQueue.Count > 0)
            {
                var (tpl, scale) = _loadQueue.Dequeue();
                if (GameItems.CopyOf(tpl, scale) != null) { _loadDone++; continue; }
                L.Step($"loading: {tpl} at {scale}x");
                _loadWaiting.Add((GameItems.IconOf(GameItems.ItemOf(tpl), scale), tpl));
            }
            // the mark turns slowly, its glow breathes
            float lt = Time.unscaledTime - _loadStart;
            if (_loadClone == null) _loadMark.rectTransform.localEulerAngles = new Vector3(0, 0, -lt * 60f); // the game's own animates itself
            _loadGlow.color = new Color(.85f, .79f, .62f, .16f + .08f * Mathf.Sin(lt * 3f));
            Ui.SetText(_loadText, $"LOADING  {_loadDone} / {_loadTotal}");
            float took = Time.unscaledTime - _loadStart;
            if ((_loadQueue.Count == 0 && _loadWaiting.Count == 0) || took > 12f)
            {
                _loading = false;
                foreach (var w in _loadWaiting) if (w.Icon != null) _prefetching.Add(w); // still coming: kept when they arrive
                _loadWaiting.Clear();
                L.Info($"loading: {_loadDone} / {_loadTotal} picture(s) in {took:0.0} s{(took > 12f ? " (stopped waiting; the rest keeps loading)" : "")}");
                _loadHideAt = Time.unscaledTime + .25f;
                ForgetPictures();
                Refresh(); // the page again, now from the kept pictures
            }
            return _loading;
        }

        /// <summary>Kept pictures were thrown away (new graphics quality, RefreshIcons): load again (now if open).</summary>
        public static void PicturesCleared()
        {
            ForgetPictures();
            _warm = _loading = false; _loadQueue.Clear(); _loadWaiting.Clear();
            if (!IsOpen) return;
            StartLoading();
            Refresh();
        }

        /// <summary>Before a raid: let go of the kept pictures (memory), the next visit loads them again.</summary>
        public static void Unload(string why)
        {
            BgPattern.Release();
            if (!_warm && !_loading) return;
            _warm = _loading = false;
            _loadQueue.Clear(); _loadWaiting.Clear();
            int n = GameItems.ClearCopies();
            ForgetPictures();
            _drawn.Clear();
            L.Info($"loading: {n} kept picture(s) let go ({why}); the next open loads them again");
        }

        private static void Prefetch()
        {
            if (Browsing) return; // pre-loading waits until you stop

            // pictures asked for earlier: take them once drawn (bigger ones become our own copy)
            for (int i = _prefetching.Count - 1; i >= 0; i--)
            {
                var (icon, tpl) = _prefetching[i];
                if (icon == null) { _prefetching.RemoveAt(i); continue; }
                GameItems.TakeSprite(icon, tpl, out bool done);
                if (done) _prefetching.RemoveAt(i);
            }
            if (_iconRequests.Count > 0 || _prefetching.Count > 2) return; // the current page first
            // the XP animation's landing level (so it's sharp when it lands)
            while (_landJobs.Count > 0)
            {
                var (ltpl, lscale) = _landJobs.Dequeue();
                if (lscale == 1 || GameItems.CopyOf(ltpl, lscale) != null || !_prefetchAsked.Add(ltpl + "@" + lscale)) continue;
                _prefetching.Add((GameItems.IconOf(GameItems.ItemOf(ltpl), lscale), ltpl));
                return;
            }
            if (_prefetchPage != _page)
            {
                _prefetchPage = _page;
                _prefetchQueue.Clear();
                foreach (var job in PictureJobs(new[] { _page, _page + 1, _page - 1 })) _prefetchQueue.Enqueue(job);
            }
            while (_prefetchQueue.Count > 0)
            {
                var (tpl, scale) = _prefetchQueue.Dequeue();
                if (!_prefetchAsked.Add(tpl + "@" + scale) || (scale != 1 && GameItems.CopyOf(tpl, scale) != null)) continue;
                _prefetching.Add((GameItems.IconOf(GameItems.ItemOf(tpl), scale), tpl));
                break; // one per frame
            }
        }

        private static readonly Dictionary<string, Sprite> _drawn = new Dictionary<string, Sprite>(); // this visit's finished pictures

        private static void ShowIcons(List<(object Icon, Image Pic, Component Placeholder, string Tpl)> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var (icon, pic, placeholder, tpl) = list[i];
                if (pic == null) { list.RemoveAt(i); continue; }
                var sprite = GameItems.TakeSprite(icon, tpl, out bool done);
                if (sprite == null) continue;
                if (pic.sprite != sprite) pic.sprite = sprite; // a stand-in first, then the sharper one when it's drawn
                pic.enabled = true;
                if (placeholder != null) placeholder.gameObject.SetActive(false);
                if (!done) continue;
                list.RemoveAt(i);
                _drawn[tpl] = sprite;
                if (_iconsShown++ == 0) L.Info($"first item icon shown ({tpl}, {sprite.rect.width:0}x{sprite.rect.height:0} px)");
            }
        }

        /// <summary>The item in the centre and its details on the right.</summary>
        private static void Feature(ProgItem it)
        {
            L.Step("Feature " + it?.Tpl);
            EndReveal(); _revealWanted = it != null; _featLevel = it?.Level ?? 0; // a new pick plays the load-in once its picture shows
            // the previous item's big picture may still be on its way: keep collecting it (else it came back blurry later)
            if (_featIcon != null && _featTpl != null) _prefetching.Add((_featIcon, _featTpl));
            _featIcon = null;
            if (it != null) _sItems++;
            _featTpl = it?.Tpl;
            _featPic.enabled = false;
            _featShort.gameObject.SetActive(it != null);
            foreach (Transform ch in _majorRow) UnityEngine.Object.Destroy(ch.gameObject);
            foreach (Transform ch in _minorRow) UnityEngine.Object.Destroy(ch.gameObject);
            if (it == null)
            {
                foreach (var c in new[] { _featType, _featName, _featReq, _featReqValue, _featStatus, _featDesc, _featNote, _featWeight }) Ui.SetText(c, "");
                if (_featWeightIcon != null) _featWeightIcon.enabled = false;
                _featCheck.enabled = false;
                _featReqLock.enabled = false;
                _featBand.SetActive(false);
                _featLock.enabled = false;
                _featIcon = null;
                HeroFor(null);
                MarkSelectedTile();
                return;
            }
            var g = ProgData.Groups.FirstOrDefault(x => x.Key == it.Group);
            int player = Me;
            bool met = player <= 0 || it.Level <= player;
            Ui.SetText(_featShort, it.Short);
            HeroFor(it);
            // weapons keep "WEAPONS"; everything else says where the handbook files it ("MEDICATION  ›  INJECTORS")
            string path = it.Group == "Weapons" ? null : GameText.CategoryPath(it.Tpl);
            Ui.SetText(_featType, (path ?? g.Name ?? "Item").ToUpperInvariant());
            Ui.SetText(_featName, it.Name);
            if (_microStage != null) Ui.SetText(_microStage, $"ID - {it.Tpl.Substring(Mathf.Max(0, it.Tpl.Length - 6)).ToUpperInvariant()}  //  LV {it.Level:00}");
            Ui.SetSize(_featName, it.Name.Length > 34 ? TTitle - 2 : it.Name.Length > 26 ? TTitle : THero); // long names: smaller, not a lone word on line 2
            // stats: damage / penetration / armor class / resource big; weight / size / caliber small (nothing invented, nothing dropped)
            var facts = GameItems.Facts(it.Tpl);
            var majorKeys = new[] { "Container size", "Damage", "Penetration", "Armor class", "Resource", "Energy", "Hydration", "Fire rate", "Ergonomics", "Recoil" };
            // armor: durability sits next to the class as a big stat (the class alone looked lost)
            if (facts.Any(f => f.Label == "Armor class")) majorKeys = majorKeys.Concat(new[] { "Durability" }).ToArray();
            var majors = facts.Where(f => majorKeys.Contains(f.Label)).ToList();
            facts.RemoveAll(f => f.Label == "Size"); // removed on request: not needed here
            // the weight goes up by the category, with the game's weight icon (as in its inspect window)
            var wf = facts.FirstOrDefault(f => f.Label == "Weight");
            Ui.SetText(_featWeight, wf.Label != null ? wf.Value : "");
            var wsp = wf.Label != null ? StatIcons.Of("Weight") : null;
            _featWeightIcon.sprite = wsp; _featWeightIcon.enabled = wsp != null;
            facts.RemoveAll(f => f.Label == "Weight");
            var minors = new[] { "Weight", "Caliber" }.Select(k => facts.FirstOrDefault(f => f.Label == k)).Where(f => f.Label != null)
                .Concat(facts.Where(f => !majorKeys.Contains(f.Label) && f.Label != "Weight" && f.Label != "Size" && f.Label != "Caliber")).ToList();
            // one grid of equal columns for every row (3, or more only if there are more big stats): the big stats never get
            // squeezed ("600 rpm" ran into ERGONOMICS at 4 columns)
            int cols = Mathf.Max(3, majors.Count);
            foreach (var f in majors) Stat(_majorRow, f.Label, f.Value, true);
            for (int c = majors.Count; c < cols && majors.Count > 0; c++) Stat(_majorRow, "", "", true);
            // the small stats: the game's inspect strips, two to a line; a long one (or the odd one out) gets the whole line
            var strips = minors.Select(f => (f.Label, f.Value, (Sprite)null, false)).ToList();
            // grenades, meds and stims: the game's own inspect rows too (explosion delay, fragments; use time, effects, side effects)
            if (it.Group == "Grenades" || it.Group == "Medical")
            {
                var have = new HashSet<string>(facts.Select(f => f.Label.ToUpperInvariant()));
                bool hasResource = have.Contains("RESOURCE");
                foreach (var (name, value, id) in GameItems.GameAttributes(it.Tpl))
                {
                    string ids = id?.ToString() ?? "";
                    if (hasResource && (ids == "HpResource" || ids == "MaxHpResource" || ids == "Resource")) continue; // already the big RESOURCE number
                    if (!have.Contains(name.ToUpperInvariant())) strips.Add((name, value, StatIcons.OfId(id), true));
                }
            }
            // weapons: the rest of the inspect window's list (accuracy, sighting range, horizontal recoil, muzzle velocity, fire modes)
            if (it.Group == "Weapons")
                foreach (var (name, value, id) in GameItems.GameAttributes(it.Tpl))
                    if (WeaponExtras.Contains(id?.ToString() ?? "")) strips.Add((name, value, StatIcons.OfId(id), true));
            for (int i = 0; i < strips.Count;)
            {
                var line = Row(_minorRow, "Line", S1);
                var a1 = strips[i];
                bool pair = i + 1 < strips.Count && StripFitsHalf(a1) && StripFitsHalf(strips[i + 1]);
                StatStrip(line, a1.Item1, a1.Item2, a1.Item3, a1.Item4);
                if (pair) StatStrip(line, strips[i + 1].Item1, strips[i + 1].Item2, strips[i + 1].Item3, strips[i + 1].Item4);
                i += pair ? 2 : 1;
            }
            _minorRow.gameObject.SetActive(strips.Count > 0);
            _majorRow.gameObject.SetActive(majors.Count > 0);
            Ui.SetText(_featDesc, ProgData.DescriptionOf(it.Tpl));
            _descScroll.verticalNormalizedPosition = 1;
            // requirement: the primary (and only red) locked signal
            _featCheck.enabled = met;
            _featReqLock.enabled = !met && player > 0;
            bool lockedHere = player > 0 && it.Level > player;
            _featBand.SetActive(false); // removed on request: the requirement on the right says it
            if (lockedHere) Ui.SetText(_featBandText, $"UNLOCKS AT LEVEL {it.Level}  ·  {it.Level - player} LEVEL{(it.Level - player == 1 ? "" : "S")} AWAY");
            Ui.SetText(_featReq, $"Reach level {it.Level}");
            // said once each: the level to reach (left), how far that is (right, the one red), the XP under it
            int away = it.Level - player;
            // one line: "Reach level 44 ........ 819 081 EXP to go" (LOCKED and "4 levels away" are said by the list's chip)
            string xpGo = _xpCardLevel <= 0 && ProgData.XpTo(it.Level, out int xpLeft) ? $"{Thousands(xpLeft)} EXP to go" : $"{away} level{(away == 1 ? "" : "s")} away";
            Ui.SetText(_featReqValue, player > 0 && !met ? $"<color={Red}>{xpGo}</color>" : "");
            Ui.SetText(_featStatus, player <= 0 ? "" : met ? "UNLOCKED" : "LOCKED");
            // unlocked: one quiet line ("✓ Unlocked at level 1"); locked: the full box, the one place that explains it
            bool full = player > 0 && !met;
            _reqHead.SetActive(false);
            _featStatus.gameObject.SetActive(false);
            _reqEdge.color = full ? Border : new Color(0, 0, 0, 0);
            _reqBg.color = full ? Ui.Hex("#0b0f11", .9f) : new Color(0, 0, 0, 0);
            _reqRed.enabled = full;
            _reqRow.offsetMin = new Vector2(full ? S4 : 0, full ? -34 : -26);
            _reqRow.offsetMax = new Vector2(full ? -S4 : 0, full ? -6 : -2);
            if (!full)
            {
                Ui.SetText(_featReq, player <= 0 ? $"Unlocks at level {it.Level}" : it.Level == player ? "Unlocked at your current level" : $"Unlocked at level {it.Level}");
                Ui.SetText(_featReqValue, "");
            }
            ReqLeader(Refl.Get(_featReq, "text") as string ?? "", Refl.Get(_featReqValue, "text") as string ?? "");
            string note = ""; // the EXP to go is the requirement's value now
            Ui.SetText(_featNote, note);
            _reqSize.minHeight = _reqSize.preferredHeight = full ? 40 : 28;
            FitDescription();
            _featLock.enabled = false; // the band says it now
            _featPic.color = player > 0 && it.Level > player ? new Color(.82f, .82f, .82f, 1) : Color.white; // locked: a shade darker
            int featScale = FeatScaleOf(it);
            if (it.Group == "Weapons" && featScale > 1) _sharpWeaponShown = true; // weapons get repaired on close
            _featScale = featScale;
            var kept = GameItems.CopyOf(it.Tpl, featScale);
            FitFeat(null);
            if (kept != null) { _featIcon = null; _featPic.sprite = kept; _featPic.enabled = true; _featShort.gameObject.SetActive(false); FitFeat(kept); LogSharpness(kept, "kept"); PictureShown(); }
            else if (!_loading) { _featWantTpl = it.Tpl; _featWantScale = featScale; _featWantAt = Time.unscaledTime + (Browsing ? .3f : .12f); } // drawn a moment later (it cost up to 136 ms right in the click)
            MarkSelectedTile();
        }

        /// <summary>
        /// The centre picture's render size for an item: enough real pixels for the preview at this resolution (Low: half).
        /// Weapons only on High: the game's weapon icons can leak a big render onto OTHER weapons in the stash (a VPO-215
        /// came out huge after VPO-136 / VPO-209 were drawn big), so High redraws them at stash size afterwards.
        /// </summary>
        private static int FeatScaleOf(ProgItem it)
        {
            // measured on the stage (the picture's box minus its 40 px padding) — never on the picture itself: FitFeat shrinks
            // that to small items' real size, and the next item was then asked for smaller (the same rifle at 4x, then 3x, 2x)
            var box = _featPic.rectTransform.parent is RectTransform pb ? pb.rect.size - new Vector2(80, 80) : Vector2.zero;
            float px = Mathf.Max(box.x, box.y) * (_featPic.canvas != null ? _featPic.canvas.scaleFactor : 1f);
            if (px >= 64) _featPx = px; else px = _featPx > 0 ? _featPx : 440;
            int featScale = GameItems.ScaleFor(it.Tpl, px);
            if (Perf) featScale = Mathf.Max(2, featScale / 2);
            if (it.Group == "Weapons" && !ProgressionPlugin.High) featScale = 1;
            return featScale;
        }
        private static float _featPx;

        /// <summary>
        /// The centre picture at an exact whole-pixel size (never enlarged past its real pixels; shrunk to fit the stage when
        /// bigger), then snapped onto the screen's pixel grid a frame later: a picture landing between pixels had every pixel
        /// blended with its neighbour — the "medium quality" look even at full resolution. Far smaller stand-ins still fill.
        /// </summary>
        private static void FitFeat(Sprite sp)
        {
            var rt = _featPic.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(40, 40); rt.offsetMax = new Vector2(-40, -40);
            _featSnap = 0;
            if (sp == null || !(rt.parent is RectTransform parent)) return;
            float sf = _featPic.canvas != null && _featPic.canvas.scaleFactor > 0 ? _featPic.canvas.scaleFactor : 1f;
            var box = parent.rect.size - new Vector2(80, 80);
            float fit = Mathf.Min(box.x * sf / sp.rect.width, box.y * sf / sp.rect.height);
            if (fit > 1.6f) return;
            float scale = Mathf.Min(fit, 1f);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(Mathf.Round(sp.rect.width * scale), Mathf.Round(sp.rect.height * scale)) / sf;
            rt.anchoredPosition = Vector2.zero;
            _featSnap = 2; // snapped once the layout has placed it
        }

        private static int _featSnap;

        /// <summary>Moves the centre picture so its corner sits exactly on a screen pixel.</summary>
        private static void SnapFeat()
        {
            if (_featSnap <= 0 || _featPic == null) return;
            if (--_featSnap > 0) return;
            var rt = _featPic.rectTransform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners); // an overlay canvas: world units are screen pixels
            float sf = _featPic.canvas != null && _featPic.canvas.scaleFactor > 0 ? _featPic.canvas.scaleFactor : 1f;
            var off = new Vector2(corners[0].x - Mathf.Round(corners[0].x), corners[0].y - Mathf.Round(corners[0].y));
            rt.anchoredPosition -= off / sf;
        }

        /// <summary>
        /// Play-test: how sharp the centre picture really is — its pixels vs the size it's shown at on screen.
        /// Over 1.0x = stretched (soft); the game caps some items' renders below what was asked for.
        /// </summary>
        private static void LogSharpness(Sprite sp, string how)
        {
            if (sp == null || !L.Verbose) return;
            var r = _featPic.rectTransform.rect;
            float scale = _featPic.canvas != null ? _featPic.canvas.scaleFactor : 1f;
            float fit = Mathf.Min(r.width * scale / sp.rect.width, r.height * scale / sp.rect.height); // preserve aspect
            L.Debug($"centre picture ({how}) {_featTpl}: {sp.rect.width:0}x{sp.rect.height:0} px shown at {sp.rect.width * fit:0}x{sp.rect.height * fit:0} on screen = " +
                    $"{fit:0.00}x{(fit > 1.05f ? " STRETCHED (soft)" : " (sharp)")} at {_featScale}x");
        }

        private static void MarkSelectedTile()
        {
            foreach (var v in _tileViews.Values) ApplyTile(v);
        }

        // ---------------------------------------------------------------- mood: red bloom for locked levels

        private static readonly List<Image> _panelFrames = new List<Image>();
        private static readonly List<Image> _bloom = new List<Image>();
        private static float _mood, _moodTarget;

        private static void SetMood(bool locked) { _moodTarget = locked ? 1 : 0; }

        private static void ApplyMood(float dt)
        {
            if (Mathf.Abs(_mood - _moodTarget) < .001f) return;
            _mood = _mood < 0 ? _moodTarget : Mathf.MoveTowards(_mood, _moodTarget, dt * 3f);
            float m = Mathf.Max(0, _mood);
            // the red glow is the screen's colour on every level; locked levels only push it a little further
            // ~40% under the old strength: it supports the screen, it doesn't compete with it
            // calmer than it was (it pulled the eye to an empty corner): a hint of red, a little more while the level is locked
            float glow = ProgressionPlugin.RedGlow.Value; // F12 > Graphics > RedGlow
            if (_bloom.Count > 0 && _bloom[0] != null) _bloom[0].color = new Color(.85f, .14f, .08f, Mathf.Clamp01(Mathf.Lerp(.07f, .1f, m) * glow));
            if (_bloom.Count > 1 && _bloom[1] != null) _bloom[1].color = new Color(.95f, .2f, .1f, Mathf.Clamp01(Mathf.Lerp(.05f, .08f, m) * glow));
            var border = Border; // the panel borders stay as they are (only the glow turns red)
            foreach (var f in _panelFrames) if (f != null) f.color = border;
        }

        private static RectTransform _rail, _railFill;
        private static readonly List<Image> _railTicks = new List<Image>();
        private static Image _railYou;
        private static Component _railYouText;
        private static Image _railBeam, _waveBase, _waveHot;
        private static float _railTarget, _railShown = -1;

        private static void TickRail(float dt)
        {
            if (_railFill == null) return;
            if (_railShown < 0) _railShown = _railTarget;
            if (_railBeam != null)
            {
                bool moving = !Calm && XpAnimating && Mathf.Abs(_railShown - _railTarget) > .001f;
                float ba = Mathf.MoveTowards(_railBeam.color.a, moving ? .9f : 0f, dt * 4);
                if (ba != _railBeam.color.a || moving)
                {
                    _railBeam.color = new Color(1f, .93f, .8f, ba);
                    var br = _railBeam.rectTransform; br.anchorMin = br.anchorMax = new Vector2(_railShown, .5f); br.anchoredPosition = Vector2.zero;
                }
            }
            if (Mathf.Abs(_railShown - _railTarget) < .0005f) { if (_railFill.anchorMax.x != _railTarget) _railFill.anchorMax = new Vector2(_railTarget, .5f); return; }
            _railShown = Mathf.Lerp(_railShown, _railTarget, 1 - Mathf.Exp(-dt * 9));
            _railFill.anchorMax = new Vector2(_railShown, .5f);
        }

        /// <summary>The rail under the cards: filled up to your place on this page (your XP into your level included).</summary>
        private static void RefreshRail()
        {
            if (_rail == null) return;
            int me = Me, first = _first;
            float prog = _xpCardLevel > 0 ? 0 : Mathf.Clamp01(ProgData.LevelProgress());
            float x = me <= 0 ? 0 : (me - first) + .5f + prog;
            float f = Mathf.Clamp01(x / PerPage);
            _railTarget = f; // eased there in Tick (it jumped during the XP animation)
            for (int i = 0; i < _railTicks.Count; i++)
            {
                int lv = first + i;
                _railTicks[i].color = lv <= me ? Ui.Hex("#d9dcdd") : Ui.Hex("#55595b");
                var tr = _railTicks[i].rectTransform; float th = lv == me ? 6 : 2; // your level's tick stands taller
                tr.offsetMin = new Vector2(tr.offsetMin.x, -th); tr.offsetMax = new Vector2(tr.offsetMax.x, th);
                _railTicks[i].gameObject.SetActive(lv <= ProgData.MaxLevel);
            }
            // your level on the row: the diamond on its tick; off the row: the diamond at the edge pointing to it ("◀ 40" / "40 ▶")
            bool here = me >= first && me < first + PerPage, before = me > 0 && me < first, after = me >= first + PerPage;
            _railYou.enabled = me > 0;
            var yr = _railYou.rectTransform;
            float ax = here ? ((me - first) + .5f) / PerPage : before ? 0 : 1;
            yr.anchorMin = yr.anchorMax = new Vector2(ax, .5f); yr.anchoredPosition = Vector2.zero;
            var tr2 = (RectTransform)((Component)_railYouText).transform.parent;
            Ui.SetText(_railYouText, before ? $"◀ {me}" : after ? $"{me} ▶" : "");
            tr2.anchorMin = tr2.anchorMax = new Vector2(ax, .5f);
            tr2.anchoredPosition = new Vector2(before ? 8 : -68, 0);
            Refl.Set(_railYouText, "alignment", before ? 4097 : 4100); // TMP: MidlineLeft / MidlineRight
            if (me <= 0) Ui.SetText(_railYouText, "");
        }

        private static void UpdateSelection()
        {
            RefreshRail();
            RefreshHome();
            if (_prev != null) { _prev.interactable = _level > 1; _next.interactable = _level < ProgData.MaxLevel; }
            int player = _xpCardLevel > 0 ? _xpCardLevel : ProgData.PlayerLevel(); // the XP animation walks the cards up
            foreach (var c in _cards) c.Mark(_level, player);
        }

        // ---------------------------------------------------------------- state colour fades (hover / select), ~120 ms

        private static readonly Dictionary<Graphic, (Color From, Color To, float Start)> _fades = new Dictionary<Graphic, (Color, Color, float)>();

        /// <summary>Moves a graphic's colour to target quickly (instant while the screen is hidden or first drawn).</summary>
        private static void FadeTo(Graphic g, Color to, bool instant = false)
        {
            if (g == null) return;
            if (instant || !IsOpen) { _fades.Remove(g); g.color = to; return; }
            if (g.color == to) { _fades.Remove(g); return; }
            _fades[g] = (g.color, to, Time.unscaledTime);
        }

        private static readonly List<Graphic> _fadeDone = new List<Graphic>();

        private static void RunFades()
        {
            if (_fades.Count == 0) return;
            float now = Time.unscaledTime;
            _fadeDone.Clear();
            foreach (var kv in _fades)
            {
                if (kv.Key == null) { _fadeDone.Add(kv.Key); continue; }
                float t = Mathf.Clamp01((now - kv.Value.Start) / .12f);
                kv.Key.color = Color.Lerp(kv.Value.From, kv.Value.To, 1 - (1 - t) * (1 - t));
                if (t >= 1) _fadeDone.Add(kv.Key);
            }
            foreach (var g in _fadeDone) _fades.Remove(g);
        }

        // ---------------------------------------------------------------- every frame

        private static float _restoreAgainAt = -1, _openedAt = -10;
        private static CanvasGroup _fade;
        private static bool _xpKnown;

        public static void Tick()
        {
            if (_restoreAgainAt > 0 && Time.unscaledTime > _restoreAgainAt && !IsOpen && MenuHook.QuietMenu()) { _restoreAgainAt = -1; GameItems.RestoreIcons(); }
            if (!IsOpen) { CheckMenuShown(); return; }
            BgPattern.Tick(); // the background pattern's slow motion: only ever while open
            TickLights();     // the drifting lights, the XP bar's tracer (F12 Detail Animation)
            SweepPictures();  // no picture left pointing at a deleted texture (the "wrong pictures")
            // the game can fade its main menu back in behind us (its own tween after a screen change): keep it hidden while open
            if (_menuGroup != null && (_menuGroup.alpha > 0 || _menuGroup.blocksRaycasts)) { _menuGroup.alpha = 0; _menuGroup.blocksRaycasts = false; }
            if (!_xpKnown && ProgData.HasExpTable) { _xpKnown = true; UpdateXp(); } // the SPT server's answer came in
            if (_fade != null && _fade.alpha < 1)
            {
                float f = Mathf.Clamp01((Time.unscaledTime - _openedAt) / .3f);
                _fade.alpha = 1 - (1 - f) * (1 - f); // ease out
            }
            var input = UnityInput.Current;
            if (RunLoading())
            {
                if (input.GetKeyDown(KeyCode.Escape)) Close("Escape (while loading)");
                return;
            }
            bool xpBusy = RunXpAnim();
            if (!IsOpen) return; // Esc during the animation closed the screen
            bool window = GameWindowOpen();
            // the game may close its window on this same Esc before we look: a window seen a moment ago still owns the key
            if (window) _windowSeenAt = Time.unscaledTime;
            bool windowRecently = window || Time.unscaledTime - _windowSeenAt < .3f;
            if (!windowRecently && !xpBusy)
            {
                bool typing = ProgressionPlugin.Typing();
                if (typing) { }
                else if (input.GetKeyDown(KeyCode.RightArrow) || input.GetKeyDown(KeyCode.D)) { ShowLevel(_level + 1); Sounds.Click(); }
                else if (input.GetKeyDown(KeyCode.LeftArrow) || input.GetKeyDown(KeyCode.A)) { ShowLevel(_level - 1); Sounds.Click(); }
                else if (input.GetKeyDown(KeyCode.E) || input.GetKeyDown(KeyCode.PageDown)) ShowPage(_page + 1, 1);
                else if (input.GetKeyDown(KeyCode.Q) || input.GetKeyDown(KeyCode.PageUp)) ShowPage(_page - 1, -1);
                else if (input.GetKeyDown(KeyCode.Home)) { int me = ProgData.PlayerLevel(); ShowLevel(me > 0 ? Mathf.Min(me, ProgData.MaxLevel) : 1); } // your level
                else if (input.GetKeyDown(KeyCode.End)) ShowLevel(ProgData.MaxLevel);
                else if (input.GetKeyDown(KeyCode.DownArrow) || input.GetKeyDown(KeyCode.S)) StepTile(1);
                else if (input.GetKeyDown(KeyCode.UpArrow) || input.GetKeyDown(KeyCode.W)) StepTile(-1);
                else if ((input.GetKeyDown(KeyCode.Return) || input.GetKeyDown(KeyCode.KeypadEnter) || input.GetKeyDown(KeyCode.R)) && _featTpl != null) InspectSoon(_featTpl);
                // Esc closes us only when no game window (inspect…) is open — otherwise it's the window's Esc
                else if (input.GetKeyDown(KeyCode.Escape)) { Close("Escape"); return; }
            }
            else if (!xpBusy && input.GetKeyDown(KeyCode.Escape)) L.Debug("Esc with a game window open: left to the window");

            float now = Time.unscaledTime;
            float wheel = input.mouseScrollDelta.y;
            if (!window && !xpBusy && Mathf.Abs(wheel) > .01f && now - _wheelAt > .2f && MenuHook.Contains(_bottom, input.mousePosition))
            {
                _wheelAt = now;
                // one level per notch (it used to jump a whole page); ShowLevel turns the page when it has to
                int to = Mathf.Clamp(_level + (wheel < 0 ? 1 : -1), 1, ProgData.MaxLevel);
                if (to != _level) { ShowLevel(to); Sounds.Click(); }
            }

            DragCards(input, window || xpBusy);
            TickRail(Time.unscaledDeltaTime);
            // the row slid by one card (or was let go after a drag): ease the cards back into their places
            if (!_dragging && _flingV == 0 && _shiftStart > 0)
            {
                float t = Mathf.Clamp01((now - _shiftStart) / .26f), e = 1 - Mathf.Pow(1 - t, 3);
                float x = _shiftFrom * (1 - e);
                for (int i = 0; i < PerPage; i++)
                {
                    bool incoming = _shiftFrom > 0 ? i == PerPage - 1 : i == 0; // the card that just came into the row fades in
                    _cards[i].Offset(x, _shiftFade && incoming ? e : 1f);
                }
                if (t >= 1) _shiftStart = -10;
            }
            // cards slide in from the side you went to (only while sliding: touching them every frame costs)
            if (now - _cardsStart < 1f)
                for (int i = 0; i < PerPage; i++)
                {
                    float delay = _cardsDir >= 0 ? i * .045f : (PerPage - 1 - i) * .045f;
                    float t = Mathf.Clamp01((now - _cardsStart - delay) / .38f);
                    _cards[i].Animate(1 - Mathf.Pow(1 - t, 3), _cardsDir);
                }
            RunFades();
            if (_scrollTopFrames > 0 && _listScroll != null) { _scrollTopFrames--; _listScroll.StopMovement(); _listScroll.verticalNormalizedPosition = 1; }
            if (_listFade != null && _listScroll != null)
            {
                bool more = _listScroll.content.rect.height > _listScroll.viewport.rect.height + 1 && _listScroll.verticalNormalizedPosition > .01f;
                if (_listFade.enabled != more) _listFade.enabled = more;
            }
            SnapFeat();
            RunIconRequests();
            if (now > _watchCardsAt) { _watchCardsAt = now + 1f; foreach (var c in _cards) c.Watch(); }
            Prefetch();
            ShowIcons(_icons);
            ShowIcons(_cardIcons);
            ApplyMood(Time.unscaledDeltaTime);

            // mouse over an item: feature it; right-click: the game's inspect (checked here: the game's input
            // doesn't send right-clicks to our tiles)
            if (!window && !xpBusy)
            {
                var mouse = (Vector2)input.mousePosition;
                ProgItem over = null;
                foreach (var (rect, item) in _hits)
                    if (rect != null && rect.gameObject.activeInHierarchy && MenuHook.Contains(rect, mouse)) { over = item; break; }
                if (input.GetMouseButtonDown(0) && over != null && over.Level == _level)
                {
                    ClickedNew(over); // its NEW tag goes (the item shown first can be clicked for that too)
                    if (over.Tpl != _featTpl) { Sounds.Play("MenuContextMenu", "ButtonClick"); Feature(over); }
                }
                if (input.GetMouseButtonDown(1))
                {
                    L.Debug($"right-click at {mouse} over {(over == null ? "nothing" : over.Name + " (" + over.Tpl + ")")}");
                    if (over != null) InspectSoon(over.Tpl);
                }
            }
            FeatureDeferred();
            InspectPending();
            if (_featIcon != null)
            {
                var sp = GameItems.TakeSprite(_featIcon, _featTpl, out bool done);
                if (sp != null)
                {
                    if (_featPic.sprite != sp) _featPic.sprite = sp; // stand-in first, the full render when it arrives
                    _featPic.enabled = true; _featShort.gameObject.SetActive(false);
                    PictureShown();
                    if (done)
                    {
                        _featIcon = null;
                        L.Debug($"big picture: {sp.rect.width:0}x{sp.rect.height:0} px (asked the game for {_featScale}x)");
                        FitFeat(sp);
                        LogSharpness(sp, "new");
                    }
                }
            }
            if (_windowLogAt > 0 && now > _windowLogAt) { _windowLogAt = 0; LogWindows(); }

            // how smooth it runs while open (every 5 s in the log)
            _frames++; _frameTime += Time.unscaledDeltaTime;
            // stutter finder: a slow frame is logged with the last step that ran before it
            if (Time.unscaledDeltaTime > .06f && _frames > 2)
            {
                _sSlow++; _sSlowMax = Mathf.Max(_sSlowMax, Time.unscaledDeltaTime);
                L.Debug($"slow frame: {Time.unscaledDeltaTime * 1000:0} ms (after: {L.LastStep})");
            }
            if (L.Verbose && now > _frameLogAt)
            {
                L.Debug($"screen open: {_frames / Mathf.Max(.001f, _frameTime):0} fps, icons waiting {_icons.Count}");
                _frames = 0; _frameTime = 0; _frameLogAt = now + 5;
            }
            // tiles fade in one after another
            if (_tiles.Count > 0)
            {
                bool done = true;
                foreach (var (group, rt, delay) in _tiles)
                {
                    if (group == null) continue;
                    float t = Mathf.Clamp01((now - _tilesStart - delay) / .28f);
                    float e = 1 - Mathf.Pow(1 - t, 3);
                    group.alpha = e;
                    rt.localScale = Vector3.one * (.95f + .05f * e);
                    if (t < 1) done = false;
                }
                if (done) _tiles.Clear();
            }
        }

        public static void Dump()
        {
            L.Info($"screen: built {_built}, open {IsOpen}, level {_level}, page {_page + 1}/{Pages}, game window open {GameWindowOpen()}, canvas {(_canvas == null ? "none" : MenuHook.Path(_canvas.transform))}");
        }

        // ---------------------------------------------------------------- pieces

        /// <summary>The rank badge: the level's animated emblem, or (without the emblems folder) a diamond with the number inside.</summary>
        private sealed class Badge
        {
            private readonly RectTransform _root;
            private readonly Image _rim, _inner;
            private readonly Component _num;

            private readonly Image _bloom;
            private readonly float _bloomA;
            private bool _bloomOn = true, _dim;

            /// <summary>bloom: the strength of a soft glow in the rank's colour behind the emblem (0 = none).</summary>
            public Badge(RectTransform parent, Vector2 anchor, Vector2 pos, float size, float bloom = 0)
            {
                _root = Ui.Box(parent, "Badge", anchor, pos, new Vector2(size, size));
                if (bloom > 0)
                {
                    // behind everything else of the badge; follows UI Detailing (off in Performance Mode)
                    _bloomA = bloom;
                    var br = Ui.Box(_root, "Bloom", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * 2.1f, size * 2.1f));
                    if (size >= 60) Ui.OwnCanvas(br); // the header's breathes every frame: only it redraws
                    _bloom = Ui.Detail(Ui.Img(br, new Color(1, 1, 1, bloom), Ui.Radial()), bloom);
                    _bloom.raycastTarget = false;
                }
                var rim = Ui.Box(_root, "Rim", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .68f, size * .68f));
                rim.localEulerAngles = new Vector3(0, 0, 45);
                _rim = Ui.Img(rim, Color.white);
                var inner = Ui.Box(_root, "Inner", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .52f, size * .52f));
                inner.localEulerAngles = new Vector3(0, 0, 45);
                _inner = Ui.Img(inner, Ui.Hex("#1a2023"));
                _num = Ui.Label(_root, "Number", "1", size * .34f, Color.white, TextAnchor.MiddleCenter, true);
                // the animated rank emblem (emblems folder) replaces the diamond when there is one
                _emblem = Ui.Img(Ui.Box(_root, "Emblem", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * 1.1f, size * 1.1f)), Color.white);
                _emblem.preserveAspect = true;
                _emblem.enabled = false;
            }

            private readonly Image _emblem;

            public RectTransform Root => _root;

            /// <summary>Still: the emblem stands on its first frame (only the picked / your card's emblem plays).</summary>
            public bool Still { set { var pl = _emblem.GetComponent<EmblemPlayer>(); if (pl != null && pl.Still != value) { pl.Still = value; } } }

            public bool Visible { set { if (_root.gameObject.activeSelf != value) _root.gameObject.SetActive(value); } }

            /// <summary>The header's glow breathes a little (0 = still): its strength and size, around the set level.</summary>
            public void Breathe(float wave)
            {
                if (_bloom == null || !_bloom.enabled) return;
                var c = _bloom.color; c.a = Mathf.Clamp01(_bloomA * Ui.DetailK * (1 + .25f * wave)); _bloom.color = c;
                _bloom.rectTransform.localScale = Vector3.one * (1 + .04f * wave);
            }

            /// <summary>The glow behind the emblem on / off (a card's badge: only your card and the picked one glow).</summary>
            public bool Bloom { set { _bloomOn = value; if (_bloom != null) { bool on = value && !_dim; if (_bloom.enabled != on) _bloom.enabled = on; } } }

            public void Set(int level, bool dim = false)
            {
                var tier = TierOf(level);
                var rim = Ui.Hex(tier.Rim);
                var light = Ui.Hex(tier.Light);
                _rim.color = dim ? Color.Lerp(rim, Color.gray, .7f) * .55f : Color.Lerp(rim, light, .3f);
                _inner.color = dim ? Ui.Hex("#12171a") : Ui.Hex("#1a2023");
                Ui.SetText(_num, level.ToString());
                Ui.SetColor(_num, dim ? Dim : light);
                bool emblem = Emblems.Show(_emblem, level);
                _emblem.color = dim ? new Color(.55f, .55f, .55f, .6f) : Color.white;
                if (_bloom != null)
                {
                    // the rank's own colour, a little warmer toward white at the core; locked (dim) badges don't glow
                    var g = Color.Lerp(light, Color.white, .25f); g.a = _bloom.color.a; _bloom.color = g;
                    _dim = dim; Bloom = _bloomOn;
                }
                _rim.enabled = _inner.enabled = !emblem;
                _num.gameObject.SetActive(!emblem);
            }
        }

        /// <summary>A level card like the Arena battle pass ones: a dotted "Level X" header above, three pictures,
        /// the unlock count and the state at the bottom. Everything is placed by fractions of the card, so it
        /// keeps its spacing at any size.</summary>
        private static int _gritSeed;

        private sealed class Card
        {
            private readonly RectTransform _body;
            private readonly CanvasGroup _group;
            private readonly Image _frame, _bg, _glow, _top, _stateLock, _selDots, _cur, _tagPlate, _cardLock, _stamp, _cardDither, _sheen, _gloss, _bloomFrame, _curScan;
            private readonly RectTransform _ticks;
            private readonly GameObject[] _stack = new GameObject[2];
            private readonly Component _activeTag;
            private readonly GameObject _activeMark;
            private readonly Image _unlockDots;
            private bool _wasSel, _isCurrent;
            private SelFrame _selFx;
            private readonly GameObject _selLights;
            private readonly Image _selBloom, _selDotLight;
            private float _lightSeed = UnityEngine.Random.value * 10;

            /// <summary>The picked card's inner bloom takes its main item's colours (item bloom); null: back to the rank's.</summary>
            public void ItemBloom(Color? c, float alpha, float size)
            {
                if (_selBloom == null || !_selLights.activeSelf) return;
                if (c.HasValue) { var col = c.Value; col.a = alpha; if (_selBloom.color != col) _selBloom.color = col; }
                var want = new Vector2(460, 300) * size;
                if (_selBloom.rectTransform.sizeDelta != want) _selBloom.rectTransform.sizeDelta = want;
            }

            public Sprite MainPicture => _pics[0] != null && _pics[0].enabled ? _pics[0].sprite : null;
            public bool Picked => _selLights != null && _selLights.activeSelf;

            /// <summary>Every frame (only the picked card has its lights on): they drift slowly, F12 Detail Animation.</summary>
            public void TickLight(float phase, float amount)
            {
                // active (your level): its bloom breathes slowly
                if (_isCurrent && _bloomFrame != null && _bloomFrame.enabled)
                {
                    var bc = _bloomFrame.color; bc.a = .5f * Ui.DetailK * (.7f + .3f * Mathf.Sin(phase * 1.6f) * amount + .3f * (1 - amount)); _bloomFrame.color = bc;
                }
                if (_selLights == null || !_selLights.activeSelf) return;
                float t = phase + _lightSeed;
                _selBloom.rectTransform.anchoredPosition = new Vector2(60 * amount * Mathf.Sin(t * .31f), 14 * amount * Mathf.Sin(t * .23f + 1));
                _selDotLight.rectTransform.anchoredPosition = new Vector2(30 * amount * Mathf.Sin(t * .19f + 2), 4 * amount * Mathf.Sin(t * .37f));
            }
            private readonly GameObject _handled;
            private readonly Badge _cardBadge;
            public int Slot = -1; // its place in the row (0 … 4): which edge catches the light
            private readonly GameObject _newBadge;
            private readonly RectTransform _tag;
            private readonly Component _tagText, _typeLine;
            private const float HeadH = 46;
            private Image _flash;
            private float _flashAt = -10;

            public int Level => _level;
            private float _shownAt;
            private readonly bool[] _retried = new bool[3];

            /// <summary>A picture still missing 3 s after the card was shown: asked for again at stash size (always there).</summary>
            public void Watch()
            {
                if (!_body.gameObject.activeSelf) return;
                for (int i = 0; i < 3; i++)
                {
                    var it = _picItems[i];
                    if (it == null || _pics[i].enabled || _retried[i] || Time.unscaledTime - _shownAt < 3f) continue;
                    _retried[i] = true;
                    L.Debug($"card {_level}: picture of {it.Name} didn't arrive — asked again at stash size");
                    RequestIcon(_cardIcons, it.Tpl, 1, _pics[i], _picNames[i]);
                }
            }
            private Component _float;
            private RectTransform _picsArea;
            private GameObject _rankChip;

            /// <summary>
            /// Places the pictures on the card's grid for this many items (the sizes come from the card's real height):
            /// 3 → big + two stacked squares · 2 → big + one square, centred on the big one · 1 → the big one alone, full width.
            /// </summary>
            private void Layout(int count)
            {
                var area = _picsArea;
                if (area.rect.height < 10) Canvas.ForceUpdateCanvases();
                float h = area.rect.height, g = S2;
                float sq = Mathf.Floor((h - g) / 2);
                var big = _picRects[0];
                big.anchorMin = Vector2.zero; big.anchorMax = Vector2.one;
                big.offsetMin = Vector2.zero;
                big.offsetMax = new Vector2(count > 1 ? -(sq + g) : 0, 0);
                void Square(RectTransform rt, float y0, float y1)
                {
                    rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0);
                    rt.offsetMin = new Vector2(-sq, y0); rt.offsetMax = new Vector2(0, y1);
                }
                if (count == 2) Square(_picRects[1], Mathf.Floor((h - sq) / 2), Mathf.Floor((h - sq) / 2) + sq); // centred beside the big one
                else
                {
                    Square(_picRects[1], h - sq, h);  // top, flush with the big picture's top
                    Square(_picRects[2], 0, sq);      // bottom, flush with its bottom
                }
            }
            private Image _emptyHatch;
            private float _floatAt = -10;

            /// <summary>XP animation: "+N ITEMS" rises out of the card and fades (0.7 s).</summary>
            public void Float(string text)
            {
                if (_float == null) return;
                Ui.SetText(_float, text);
                _float.gameObject.SetActive(true);
                _floatAt = Time.unscaledTime;
            }

            /// <summary>Just unlocked (XP animation): an orange flash over the card that fades out.</summary>
            public void Flash()
            {
                if (_flash == null) return;
                _flashAt = Time.unscaledTime;
                _flash.enabled = true;
                // MW4's unlock: no check stamp (0.9.69's read badly on "no new items" cards) — the card flashes white-hot and
                // its face lights up in a dot matrix that settles back
                if (_unlockDots != null) _unlockDots.enabled = true;
            }

            public void TickFlash()
            {
                TickFloat();
                if (_flash == null || !_flash.enabled) return;
                float t = Time.unscaledTime - _flashAt;
                // unlocked: the card lifts to a soft white (0.12 s, eased) and settles back to its unlocked look over 0.9 s
                float a = t < .12f ? 1 - Mathf.Pow(1 - t / .12f, 2) : Mathf.Pow(Mathf.Clamp01(1 - (t - .12f) / .9f), 2.2f);
                _flash.color = new Color(1f, .98f, .92f, .5f * a);
                if (_unlockDots != null)
                {
                    float da = t < .1f ? t / .1f : Mathf.Pow(Mathf.Clamp01(1 - (t - .1f) / 1.2f), 1.6f);
                    _unlockDots.color = new Color(1f, .93f, .78f, .5f * da);
                    if (t > 1.3f) _unlockDots.enabled = false;
                }
                // and a gentle lift: up to 102% and back in 0.35 s
                float sc = Calm ? 1f : 1 + .02f * Mathf.Sin(Mathf.Clamp01(t / .35f) * Mathf.PI);
                _body.localScale = new Vector3(sc, sc, 1);
                // CoD's unlock: a big check stamps down (130% → 100% in 0.18 s), glows, then fades out by 1.3 s
                if (_stamp != null && _stamp.enabled)
                {
                    float st = Mathf.Clamp01(t / .18f), se = 1 - Mathf.Pow(1 - st, 3);
                    _stamp.rectTransform.localScale = Vector3.one * (Calm ? 1f : Mathf.Lerp(1.3f, 1f, se));
                    float sa = t < .18f ? se : Mathf.Clamp01(1 - (t - .7f) / .6f);
                    _stamp.color = new Color(1f, 1f, 1f, .95f * sa); // hollow white
                    if (t > 1.3f) { _stamp.enabled = false; }
                }
                // and the level number above it pulses
                var hs = Calm ? 1f : 1 + .22f * Mathf.Sin(Mathf.Clamp01(t / .35f) * Mathf.PI);
                ((RectTransform)((Component)_head).transform).localScale = new Vector3(hs, hs, 1);
                if (t > 1.3f) { _flash.enabled = false; _body.localScale = Vector3.one; ((RectTransform)((Component)_head).transform).localScale = Vector3.one; }
            }

            private void TickFloat()
            {
                if (_float == null || !_float.gameObject.activeSelf) return;
                float t = Time.unscaledTime - _floatAt;
                float a = t < .1f ? t / .1f : Mathf.Clamp01(1 - (t - .35f) / .4f);
                Ui.SetColor(_float, Ui.Hex(Orange, a));
                ((RectTransform)_float.transform).anchoredPosition = new Vector2(0, 22f * (1 - Mathf.Pow(1 - Mathf.Clamp01(t / .75f), 3)));
                if (t > .75f) _float.gameObject.SetActive(false);
            }
            private readonly DottedLine _headL, _headR;
            private readonly Component _head, _count, _state, _tier, _empty;
            private readonly Image[] _pics = new Image[3];
            private readonly ProgItem[] _picItems = new ProgItem[3];
            private readonly Component[] _picNames = new Component[3];
            private readonly RectTransform[] _picRects = new RectTransform[3];
            private readonly Badge _badge;
            private int _level;
            private bool _hover;
            private float _baseAlpha = 1;
            private int _picked, _player;

            /// <summary>A square picture slot, as big as fits in the given area (so icons keep their shape at any screen size).</summary>
            private static RectTransform Square(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, float aspect = 1)
            {
                var area = Ui.Rect(parent, name, aMin, aMax, oMin, oMax);
                var sq = Ui.Fill(area, "Square");
                var fit = sq.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = aspect;
                return sq;
            }

            private readonly bool _first;

            /// <summary>first: the page's first card — the only one that shows the rank emblem (one emblem per page).</summary>
            public Card(RectTransform slot, bool first)
            {
                _first = first;
                _body = Ui.Fill(slot, "Card");
                _group = _body.gameObject.AddComponent<CanvasGroup>();
                // MW-style head: the level's rank emblem + its number big, a small role tag under it (VIEWING on the worn pale
                // plate like the game's selected sub-tabs; CURRENT; NEXT), thin rules either side
                var head = Ui.Rect(_body, "Head", new Vector2(0, 1), Vector2.one, new Vector2(0, -HeadH), Vector2.zero);
                var numRow = Ui.Rect(head, "NumRow", new Vector2(0, 1), Vector2.one, new Vector2(0, -26), Vector2.zero);
                _head = Ui.Label(numRow, "Text", "", TTitle, Grey, TextAnchor.MiddleCenter, true);
                _cardBadge = new Badge(numRow, new Vector2(.5f, .5f), new Vector2(-30, 0), 30, .35f);
                _headL = DottedLine.Add(Ui.Rect(numRow, "L", new Vector2(0, 0), new Vector2(.5f, 1), Vector2.zero, new Vector2(-46, 0)), false);
                _headR = DottedLine.Add(Ui.Rect(numRow, "R", new Vector2(.5f, 0), new Vector2(1, 1), new Vector2(46, 0), Vector2.zero), true);
                foreach (var d in new[] { _headL, _headR }) { d.Dash = 6; d.Gap = 0; d.Thickness = 1; } // gap 0: a continuous line that still fades out
                _tag = Ui.Rect(head, "Tag", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-40, 0), new Vector2(40, 18));
                _tagPlate = Ui.Img(_tag, Color.white, Ui.WornPlate());
                _tagPlate.type = Image.Type.Sliced; _tagPlate.raycastTarget = false;
                _tagText = Ui.Label(_tag, "Text", "", 11, Grey, TextAnchor.MiddleCenter, true, 1.5f);

                // your level: CoD's "important" look — a bloom around the border, scanlines inside, tick marks under it
                _bloomFrame = Ui.Img(Ui.Rect(_body, "Bloom", Vector2.zero, Vector2.one, new Vector2(-16, -16), new Vector2(16, -HeadH - 4 + 16)), Ui.Hex(Orange, .5f), Ui.GlowFrame());
                _bloomFrame.type = Image.Type.Sliced; _bloomFrame.raycastTarget = false; _bloomFrame.enabled = false; Ui.Detail(_bloomFrame, .5f);
                Ui.OwnCanvas(_bloomFrame.rectTransform); // your card's bloom breathes: only it redraws
                _ticks = Ui.Rect(_body, "Ticks", new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, -6), new Vector2(-12, -3));
                for (int k = 0; k < 9; k++)
                {
                    float x0 = k / 9f;
                    Ui.Detail(Ui.Img(Ui.Rect(_ticks, "T" + k, new Vector2(x0, 0), new Vector2(x0, 1), new Vector2(0, 0), new Vector2(k % 3 == 0 ? 14 : 5, 0)), Ui.Hex(Orange, k % 3 == 0 ? .55f : .3f)), k % 3 == 0 ? .55f : .3f).raycastTarget = false;
                }
                _ticks.gameObject.SetActive(false);
                // more than the three pictures: one or two cards stacked behind, their edges peeking out bottom-right
                for (int k = 2; k >= 1; k--)
                {
                    float o = 4 * k;
                    var st = Ui.Rect(_body, "Stack" + k, Vector2.zero, Vector2.one, new Vector2(o, -o), new Vector2(o, -HeadH - 4 - o));
                    var se = Ui.Img(st, Ui.Hex("#2b3438", k == 1 ? .75f : .45f), Ui.Chamfer()); se.type = Image.Type.Sliced; se.raycastTarget = false;
                    var sf = Ui.Img(Ui.Fill(st, "In", 1), Ui.Hex("#0c0f11", .95f), Ui.Chamfer()); sf.type = Image.Type.Sliced; sf.raycastTarget = false;
                    _stack[k - 1] = st.gameObject;
                    st.gameObject.SetActive(false);
                }
                var card = Ui.Rect(_body, "Box", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -HeadH - 4));
                // cut top-left / bottom-right corners like the game's prestige tiles (frame + face 2 px in = a cut outline)
                _frame = Ui.Img(card, Border, Ui.Chamfer(), true);
                _frame.type = Image.Type.Sliced;
                var inner = Ui.Fill(card, "In", 2);
                _bg = Ui.Img(inner, Ui.Hex("#12181b", .88f), Ui.Chamfer(), true);
                _bg.type = Image.Type.Sliced;
                // Arena-style warm glow in the top-right corner of the picked card
                var glow = Ui.Box(inner, "Glow", new Vector2(1, 1), Vector2.zero, new Vector2(260, 260));
                _glow = Ui.Img(glow, new Color(0, 0, 0, 0), Ui.Radial());
                inner.gameObject.AddComponent<RectMask2D>();
                // header row: small badge + rank title, tight to the top-left
                // one spacing unit (Pad) everywhere: card edge → badge, badge → title, header → pictures, pictures → bottom row
                // (the badge's diamond is ~B wide corner to corner, so its box sits exactly Pad from the edges)
                // the rank stays quiet: a small emblem and small caps (the level itself is the card's header)
                // ONE grid for every card (the spacing is the point of this strip):
                //   12 px padding on all four sides of the picture area · 8 px between any two pictures · the small squares'
                //   size comes from the height ((h - 8) / 2), they sit flush right · the big picture takes all the rest.
                // The first card's rank emblem sits on its big picture (top-left), so no card needs a row of its own.
                const float Pad = S3, Foot = 48;
                var pics = Ui.Rect(inner, "Pics", Vector2.zero, Vector2.one, new Vector2(Pad, Foot), new Vector2(-Pad, -Pad));
                _picsArea = pics;
                // selected: a 2 px light bar along the top edge (shape, not only colour)
                _top = Ui.Img(Ui.Rect(inner, "Top", new Vector2(0, 1), Vector2.one, new Vector2(10, -2), Vector2.zero), Select); // clear of the cut corner
                _top.enabled = false;
                // selected: a fine dot-matrix fill over the face (MW's picked card), under the pictures
                var dotsMask = Ui.Fill(inner, "SelDotsMask");
                var dm = Ui.Img(dotsMask, Color.white, Ui.Chamfer()); dm.type = Image.Type.Sliced; dm.raycastTarget = false;
                dotsMask.gameObject.AddComponent<Mask>().showMaskGraphic = false; // the dots follow the card's cut corners
                dotsMask.SetSiblingIndex(1);
                _handled = Ui.Handled(dotsMask, _gritSeed++, .07f).gameObject; // fingerprints / smudges, clipped to the card's cut shape
                _handled.SetActive(false);
                _selDots = Ui.Img(Ui.Fill(dotsMask, "SelDots"), new Color(1, 1, 1, .13f), Ui.DotGrid());
                _selDots.type = Image.Type.Tiled; _selDots.raycastTarget = false; _selDots.enabled = false;
                _unlockDots = Ui.Img(Ui.Fill(dotsMask, "UnlockDots"), new Color(1, 1, 1, 0), Ui.DotGrid());
                _unlockDots.type = Image.Type.Tiled; _unlockDots.raycastTarget = false; _unlockDots.enabled = false;
                // picked: a bloom in the rank's colour and a light made of dots under the pictures, both drifting a little
                // (their own canvas: moving them redraws only them), clipped to the card's cut shape
                var lit = Ui.Fill(dotsMask, "Lights");
                Ui.OwnCanvas(lit);
                _selLights = lit.gameObject;
                _selBloom = Ui.Detail(Ui.Img(Ui.Box(lit, "Bloom", new Vector2(.6f, .5f), Vector2.zero, new Vector2(460, 300)), new Color(1, 1, 1, .1f), Ui.Radial()), .1f);
                // bottom-right, clear of the name and "ASSAULT RIFLE · +9 ITEMS" on the left (0.9.66 lit them up and they were hard to read)
                _selDotLight = Ui.Detail(Ui.Img(Ui.Box(lit, "DotLight", new Vector2(.8f, .04f), Vector2.zero, new Vector2(300, 110)), new Color(1, 1, 1, .16f), Ui.HalftoneGlow()), .16f);
                _selBloom.raycastTarget = _selDotLight.raycastTarget = false;
                _selLights.SetActive(false);
                // locked: a small lock top-left on the picture area, like CoD's locked unlocks
                _cardLock = Ui.Img(Ui.Rect(inner, "Lock", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + 6, -Pad - 20), new Vector2(Pad + 18, -Pad - 6)), Ui.Hex("#c9cccd", .9f), Ui.Lock());
                _cardLock.raycastTarget = false; _cardLock.enabled = false;
                // unlocked (XP animation): a big check stamps onto the card and settles
                // centred on the pictures as a group (their area), not the card: the three pictures aren't centred on the card
                _stamp = Ui.Img(Ui.Box(pics, "Stamp", new Vector2(.5f, .5f), Vector2.zero, new Vector2(96, 96)), new Color(1, 1, 1, 0), Ui.HollowTick());
                _stamp.raycastTarget = false; _stamp.enabled = false;
                // your level: one thin orange line along the top (orange only ever means "you")
                _cardDither = Ui.Img(Ui.Rect(inner, "Dither", new Vector2(0, 1), new Vector2(.6f, 1), new Vector2(10, -12), new Vector2(0, -3)), new Color(1, 1, 1, .12f), Ui.Dither());
                _cardDither.raycastTarget = false; _cardDither.enabled = false; Ui.Detail(_cardDither, .12f);
                _sheen = Ui.Img(Ui.Box(inner, "Sheen", new Vector2(.6f, .7f), Vector2.zero, new Vector2(320, 170)), new Color(1, 1, 1, .06f), Ui.Radial());
                _sheen.raycastTarget = false; _sheen.enabled = false; Ui.Detail(_sheen, .06f);
                _gloss = Ui.Img(Ui.Rect(inner, "Gloss", new Vector2(1, .12f), new Vector2(1, .88f), new Vector2(-4, 0), new Vector2(-3, 0)), new Color(1, 1, 1, .2f), Ui.VerticalFade());
                _gloss.raycastTarget = false; _gloss.enabled = false; Ui.Detail(_gloss, .2f);
                _curScan = Ui.Img(Ui.Fill(inner, "Scan"), Ui.Hex(Orange, .045f), Ui.Scanlines());
                _curScan.type = Image.Type.Tiled; _curScan.raycastTarget = false; _curScan.enabled = false; Ui.Detail(_curScan, .045f);
                _cur = Ui.Img(Ui.Rect(inner, "Current", new Vector2(0, 1), Vector2.one, new Vector2(10, -3), Vector2.zero), Ui.Hex(Orange));
                // your level: MW's "LEVEL_ACTIVE" system tag top-right, and a crosshair tick on the card's left edge
                _activeTag = Ui.Label(Ui.Rect(inner, "Active", new Vector2(.45f, 1), Vector2.one, new Vector2(0, -12), new Vector2(-12, -3)), "Text", "", 8.5f, Ui.Hex(Orange, .85f), TextAnchor.MiddleRight, false, 1.5f);
                ((Graphic)_activeTag).raycastTarget = false;
                _activeTag.gameObject.SetActive(false);
                var mark = Ui.Rect(card, "ActiveMark", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(-12, -8), new Vector2(1, 8));
                Ui.Img(Ui.Rect(mark, "V", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 0), Vector2.zero), Ui.Hex(Orange, .9f)).raycastTarget = false;
                Ui.Img(Ui.Rect(mark, "H", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 0), new Vector2(0, 1)), Ui.Hex(Orange, .9f)).raycastTarget = false;
                _activeMark = mark.gameObject;
                _activeMark.SetActive(false);
                _newBadge = Ui.NewBadge(card, new Vector2(1, 1), new Vector2(-24, 0), 40, 19, 12.5f).gameObject;
                _newBadge.SetActive(false);
                _cur.enabled = false;
                for (int i = 0; i < 3; i++) _picRects[i] = Ui.Rect(pics, "Pic" + i, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                for (int i = 0; i < 3; i++)
                {
                    // Arena item preview: thin frame, dark face, the item, a soft dark fade around the edges, lock bottom-right
                    var picSlot = _picRects[i];
                    var edge = Ui.Img(picSlot, Ui.Hex("#2a3134"), null, true);
                    int k = i;
                    HoverHook.Add(edge, on =>
                    {
                        FadeTo(edge, on ? HoverEdge : Ui.Hex("#2a3134"));
                        ShowTip(on ? _picRects[k] : null, on && _picItems[k] != null ? _picItems[k].Name : null, true);
                    });
                    var face = Ui.Fill(picSlot, "Face", 2);
                    Ui.Img(face, Ui.Hex("#0f1315"));
                    Ui.Img(Ui.Fill(face, "Light"), new Color(1, 1, 1, .05f), Ui.Radial());
                    _picNames[i] = Ui.Label(face, "Name", "", TCaps, Grey, TextAnchor.MiddleCenter, false, 0, true);
                    AddLoader(_picNames[i], 0, i == 0 ? 4 : 3);
                    _pics[i] = Ui.Img(Ui.Fill(face, "Img", i == 0 ? 8 : 4), Color.white);
                    _pics[i].preserveAspect = true;
                    _pics[i].enabled = false;
                    Ui.EdgeFade(face, .18f, .55f);
                    Ui.Grit(face, _gritSeed++, .02f);
                }
                // the page's first card: its rank emblem and title on the big picture, top-left (a small dark chip behind the text)
                const float B = 22;
                var chip = Ui.Rect(_picRects[0], "Rank", new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -6 - B), new Vector2(6 + B + 110, -6));
                _rankChip = chip.gameObject;
                _badge = new Badge(chip, new Vector2(0, .5f), new Vector2(B / 2, 0), B);
                _tier = Ui.Label(Ui.Rect(chip, "Tier", Vector2.zero, Vector2.one, new Vector2(B + S1, 0), Vector2.zero), "Text", "", TCaps, Grey, TextAnchor.MiddleLeft, false, Caps);

                // a level with nothing on it: a quiet line in the picture area instead of empty boxes
                _emptyHatch = Ui.Img(Ui.Fill(pics, "Hatch"), new Color(1, 1, 1, .045f), Ui.Hatch());
                _emptyHatch.type = Image.Type.Tiled; _emptyHatch.raycastTarget = false;
                _emptyHatch.gameObject.SetActive(false);
                _empty = Ui.Label(pics, "Empty", "NO NEW ITEMS", TCaps, Dim, TextAnchor.MiddleCenter, false, Caps);
                _empty.gameObject.SetActive(false);
                // footer: "147 unlocks" left, the level's state right in small caps (one small lock for future levels)
                // footer like MW's cards: the main reward's name bold, its kind + the rest in small spaced caps under it;
                // the level's state (NEW / LOCKED) on the right of the name
                _count = Ui.Label(Ui.Rect(inner, "Name", new Vector2(0, 0), new Vector2(.72f, 0), new Vector2(Pad, 23), new Vector2(0, Foot - 5)), "Text", "", TBody + 1, Text, TextAnchor.MiddleLeft, true, 0, true);
                _typeLine = Ui.Label(Ui.Rect(inner, "Type", new Vector2(0, 0), new Vector2(1, 0), new Vector2(Pad, 7), new Vector2(-Pad, 22)), "Text", "", 10, Grey, TextAnchor.MiddleLeft, false, 2, true);
                _state = Ui.Label(Ui.Rect(inner, "State", new Vector2(.5f, 0), new Vector2(1, 0), new Vector2(0, 20), new Vector2(-Pad, Foot - 4)), "Text", "", TCaps, Grey, TextAnchor.MiddleRight, false, Caps);
                _stateLock = Ui.Img(Ui.Rect(inner, "StateLock", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Pad - 68 - 12, 26), new Vector2(-Pad - 68, 38)), Grey, Ui.Lock());
                _stateLock.enabled = false;
                var button = inner.gameObject.AddComponent<Button>();
                button.targetGraphic = _bg;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    if (_dragging || Time.unscaledTime < _dragSuppressUntil) return; // that was the end of a drag
                    L.Debug($"card level {_level} clicked");
                    Sounds.Click();
                    if (NewTags.ClearLevel(_level)) L.Debug($"NEW: level {_level} card clicked");
                    ShowLevel(_level);
                    UpdateSelection();
                });
                HoverHook.Add(_frame, on => { if (_hover == on) return; _hover = on; if (on) { Sounds.Play("ButtonOver"); PlayGlitch(_bg.rectTransform); } Mark(_picked, _player); });
                // XP animation: a warm flash over the whole card when it unlocks (on top of everything in it)
                _flash = Ui.Img(Ui.Fill(card, "Flash"), new Color(0, 0, 0, 0), Ui.Radial());
                _flash.raycastTarget = false;
                _flash.enabled = false;
                _float = Ui.Label(Ui.Rect(card, "Float", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -14), new Vector2(0, 14)), "Text", "", TStrong, Ui.Hex(Orange), TextAnchor.MiddleCenter, true, Caps);
                _float.gameObject.SetActive(false);
            }

            public void Show(int level)
            {
                L.Step("card " + level);
                _level = level;
                _shownAt = Time.unscaledTime;
                for (int i = 0; i < 3; i++) _retried[i] = false;
                bool exists = level <= ProgData.MaxLevel;
                _body.gameObject.SetActive(exists);
                if (!exists) return;
                var items = ProgData.ItemsAt(level);
                var picks = CardPicks(items);
                Ui.SetText(_head, level.ToString());
                _cardBadge.Set(level, items.Count == 0);
                // cards stacked behind: more than the three pictures (one), a lot more (two)
                _stack[0].SetActive(items.Count > 3); _stack[1].SetActive(items.Count > 12);
                // one emblem per page: on its first card, with the rank's name (the header shows yours)
                _badge.Set(level, items.Count == 0);
                _badge.Visible = _first;
                Ui.SetText(_tier, _first ? TierOf(level).Name : "");
                // the main reward (the big picture's) by name; its kind and the rest under it: "ASSAULT RIFLE  ·  +29 ITEMS"
                var main = picks.Count > 0 ? picks[0] : null;
                Ui.SetText(_count, main == null ? "" : main.Short);
                Ui.SetSize(_count, main != null && main.Short.Length > 22 ? TBody - 1 : TBody + 1); // long names a size down
                string kind = main == null ? "" : KindOf(main.Tpl);
                if (main != null && kind.Length == 0) kind = ProgData.Groups.FirstOrDefault(x => x.Key == main.Group).Name ?? "";
                int more = items.Count - 1;
                Ui.SetText(_typeLine, main == null ? "" : (kind.ToUpperInvariant() + (more > 0 ? $"  ·  +{more} ITEM{(more == 1 ? "" : "S")}" : "")).Trim());
                for (int i = 0; i < 3; i++)
                {
                    var it = i < picks.Count ? picks[i] : null;
                    _picItems[i] = it;
                    _picRects[i].gameObject.SetActive(it != null);
                    _pics[i].enabled = false;
                    if (it == null) continue;
                    Ui.SetText(_picNames[i], ""); // no name flashing in while the picture loads (the pages around are pre-loaded)
                    _picNames[i].gameObject.SetActive(true);
                    RequestIcon(_cardIcons, it.Tpl, CardScaleOf(it.Tpl), _pics[i], _picNames[i]);
                    _hits.Add((_picRects[i], it));
                }
                _empty.gameObject.SetActive(items.Count == 0);
                _emptyHatch.gameObject.SetActive(items.Count == 0);
                Layout(picks.Count);
                if (_rankChip != null) _rankChip.SetActive(false); // every card has its rank emblem in its head now
                _group.alpha = 1f;
            }

            /// <summary>current (your level): orange header + state · selected: light 2 px frame + top bar · hover: brighter edge ·
            /// future: muted, one small lock, "LOCKED" in grey · new since your last visit: a small orange NEW.</summary>
            public void Mark(int picked, int player)
            {
                _picked = picked; _player = player;
                bool sel = _level == picked, current = player > 0 && _level == player;
                bool locked = player > 0 && _level > player, reached = player > 0 && _level <= player;
                bool fresh = reached && NewTags.Level(_level); // kept until you pick the level or click its new rewards
                FadeTo(_frame, sel ? Select : _hover ? HoverEdge : Border);
                _top.enabled = sel && !current;
                _cardDither.enabled = sel;
                _sheen.enabled = _gloss.enabled = sel; // a very light reflection on the picked card
                if (sel && Slot >= 0)
                {
                    // the lit edge faces the middle of the row: right edge for the left cards, left edge for the right ones
                    bool left = Slot < PerPage / 2, right = Slot > PerPage / 2;
                    var gr = _gloss.rectTransform;
                    float x = right ? 0 : 1;
                    gr.anchorMin = new Vector2(x, .12f); gr.anchorMax = new Vector2(x, .88f);
                    gr.offsetMin = new Vector2(right ? 3 : -4, 0); gr.offsetMax = new Vector2(right ? 4 : -3, 0);
                    _gloss.enabled = left || right;
                    _sheen.rectTransform.anchorMin = _sheen.rectTransform.anchorMax = new Vector2(right ? .4f : left ? .6f : .5f, .7f);
                }
                _cur.enabled = current;       // a thin orange line: your level
                if (_activeTag.gameObject.activeSelf != current) _activeTag.gameObject.SetActive(current);
                if (current) Ui.SetText(_activeTag, $"LEVEL_ACTIVE  //  {_level:000}");
                if (_activeMark.activeSelf != current) _activeMark.SetActive(current);
                // the bloom around the border: orange on yours, the rank's colour on the picked one (CoD's coloured selection)
                var rank = Ui.Hex(TierOf(_level).Rim);
                var bc = current ? Ui.Hex(Orange) : Color.Lerp(rank, Color.white, .15f); bc.a = _bloomFrame.color.a; _bloomFrame.color = bc;
                _bloomFrame.enabled = current || (sel && !locked); _curScan.enabled = current; _ticks.gameObject.SetActive(current);
                if (_selLights.activeSelf != sel) _selLights.SetActive(sel);
                if (sel && !_wasSel) PlayShine(_bg.rectTransform); // picked: one soft shine across it
                if (_selFx == null) _selFx = MakeSelFrame((RectTransform)_frame.transform, 4);
                _selFx.On = sel;
                _wasSel = sel; _isCurrent = current;
                if (sel)
                {
                    var light = Ui.Hex(TierOf(_level).Light);
                    var b1 = Color.Lerp(rank, light, .3f); b1.a = _selBloom.color.a; _selBloom.color = b1;
                    var b2 = Color.Lerp(light, Color.white, .2f); b2.a = _selDotLight.color.a; _selDotLight.color = b2;
                }
                if (_handled.activeSelf != (sel || current)) _handled.SetActive(sel || current); // detail only on the focal cards
                if (current && !sel) FadeTo(_frame, Ui.Hex(Orange, .85f)); // its border glows orange
                _cardBadge.Still = !(sel || current); // five emblems playing at once was busy (and cost frames)
                _cardBadge.Bloom = sel || current; // a soft glow behind the emblem: only the focal cards
                _selDots.enabled = sel;       // the picked card's dot-matrix fill
                _cardLock.enabled = locked && ProgData.CountAt(_level) > 0;
                FadeTo(_bg, sel ? Ui.Hex("#1b1d1e", .92f) : _hover ? Ui.Hex("#161718", .9f) : locked ? Ui.Hex("#08090a", .94f) : Ui.Hex("#111213", .88f));
                _glow.color = new Color(0, 0, 0, 0);
                Ui.SetColor(_tier, locked ? Dim : Grey);
                Ui.SetColor(_count, sel || current ? Text : locked ? Dim : Grey);
                // head: the number bright on the picked / current card, quieter elsewhere, dim when locked
                var head = sel || current ? Ui.Hex("#eceeef") : locked ? Dim : Grey;
                Ui.SetColor(_head, head);
                _headL.color = _headR.color = new Color(head.r, head.g, head.b, current || sel ? .75f : .5f); // on every card, fainter
                float nw = Ui.PreferredWidth(_head, _level.ToString());
                ((RectTransform)_cardBadge.Root).anchoredPosition = new Vector2(-(nw / 2 + 22), 0);
                float hw = nw / 2 + 44; // clear of the emblem on the left, the same on the right
                _headL.rectTransform.offsetMax = new Vector2(-hw, _headL.rectTransform.offsetMax.y);
                _headR.rectTransform.offsetMin = new Vector2(hw - 20, _headR.rectTransform.offsetMin.y);
                // the role tag under the number: VIEWING on the worn pale plate (dark text), CURRENT in orange, NEXT in grey
                string role = sel ? (current ? "CURRENT" : "VIEWING") : current ? "CURRENT" : player > 0 && _level == player + 1 ? "NEXT" : "";
                _tag.gameObject.SetActive(role.Length > 0);
                if (role.Length > 0)
                {
                    Ui.SetText(_tagText, role);
                    _tagPlate.enabled = sel;
                    Ui.SetColor(_tagText, sel ? Ui.Hex("#1a1b1c") : current ? Ui.Hex(Orange) : Grey);
                    float tw = Ui.PreferredWidth(_tagText, role) / 2 + 12;
                    _tag.offsetMin = new Vector2(-tw, _tag.offsetMin.y); _tag.offsetMax = new Vector2(tw, _tag.offsetMax.y);
                }
                bool empty = ProgData.CountAt(_level) == 0;
                bool nextUp = player > 0 && _level == player + 1;
                string state = ""; // NEW is CoD's yellow badge on the card's top-right corner now
                if (_newBadge != null) _newBadge.SetActive(player > 0 && !empty && fresh);
                Ui.SetText(_state, state);
                _stateLock.enabled = false;
                if (_stateLock.enabled)
                {
                    float w = Ui.PreferredWidth(_state, "LOCKED");
                    var lr = _stateLock.rectTransform;
                    lr.offsetMin = new Vector2(-S3 - w - S1 - 12, lr.offsetMin.y);
                    lr.offsetMax = new Vector2(-S3 - w - S1, lr.offsetMax.y);
                }
                // future levels step back (unless picked or under the mouse)
                _baseAlpha = locked && !sel && !_hover && !empty ? .85f : 1f;
                // the pictures carry the weight: full only on the viewing / current card (or under the mouse)
                float pa = sel || current || _hover ? 1f : locked ? .85f : .9f; // .65 made future levels' items hard to make out
                // locked levels clearly darker: the pictures dimmed (a little less while hovered / picked) — this used to be
                // overwritten right here with plain white, so locked cards never looked darker
                float lum = !locked ? 1f : sel || _hover ? .7f : .48f;
                foreach (var pic in _pics) FadeTo(pic, new Color(lum, lum, lum, pa));
                _group.alpha = _baseAlpha;
            }

            /// <summary>The whole card moved sideways (drag / one-card slide); fade: its alpha factor (the card coming in).</summary>
            public void Offset(float x, float fade = 1f)
            {
                if (!_body.gameObject.activeSelf) return;
                _body.anchoredPosition = new Vector2(x, 0);
                _group.alpha = _baseAlpha * fade;
            }

            public void Animate(float e, int dir)
            {
                if (!_body.gameObject.activeSelf) return;
                _body.anchoredPosition = new Vector2(dir == 0 ? 0 : dir * 90f * (1 - e), 0);
                _group.alpha = _baseAlpha * (dir == 0 ? 1 : e);
            }
        }
    }
}
