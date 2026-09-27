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
        private static readonly Color PanelBg = Ui.Hex("#10161a", .80f);
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
        private static Image _featPic, _featLock;
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
                ProgData.Invalidate(); // names / categories again (the game may have finished loading them since)
                int player = ProgData.PlayerLevel();
                int seen = SeenState.Level;
                _newFrom = seen > 0 && seen < player ? seen : 0;
                _sLevels = _sItems = _sInspects = _sPages = _sSlow = 0; _sSlowMax = 0; _sOpenedAt = Time.unscaledTime;
                _sLogMs = L.CostMs; _sLogLines = L.CostLines;
                int newItems = _newFrom > 0 ? ProgData.Levels.Values.Count(v => v > _newFrom && v <= player) : 0;
                string xpNow = ProgData.LevelExp(out int xh, out int xn) ? $"{xh} / {xn} into level {player} (total {ProgData.TotalExp()})" : "XP unknown";
                L.Info($"open: player level {player}, {xpNow}; last seen level {seen}" +
                       (_newFrom > 0 ? $" → NEW: levels {_newFrom + 1}–{player}, {newItems} item(s) tagged NEW" : " → nothing new since last visit"));
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
            if (_newFrom > 0) L.Info($"NEW: cleared ({_newClicked.Count} item(s) clicked, {_newViewed.Count} level(s) looked at)");
            _newFrom = 0; _newClicked.Clear(); _newViewed.Clear();
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
            grid.type = Image.Type.Tiled;
            grid.raycastTarget = false;
            // Arena-style colour bloom: red, strongest on the right edge and fading out to the left;
            // it always glows a little and flares up while the picked level is still locked
            _bloom.Clear(); _panelFrames.Clear();
            // kept to the top half (right side), so it doesn't pull the eye to the level cards in the bottom corner
            _bloom.Add(Ui.Img(Ui.Rect(root, "Bloom", new Vector2(.25f, 0), Vector2.one, Vector2.zero, Vector2.zero), new Color(0, 0, 0, 0), Ui.CornerGlow()));
            _bloom.Add(Ui.Img(Ui.Box(root, "BloomCore", new Vector2(1, .66f), Vector2.zero, new Vector2(1200, 1300)), new Color(0, 0, 0, 0), Ui.Radial()));
            _mood = -1; // forces the first ApplyMood to paint

            // the level strip is navigation: ~30% of the height, the rest goes to the reward content
            var top = Ui.Rect(root, "Top", new Vector2(0, .30f), Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(top);
            BuildHeader(top);
            BuildUnlocks(top);
            BuildFeatured(top);
            _bottom = Ui.Rect(root, "Bottom", Vector2.zero, new Vector2(1, .30f), Vector2.zero, Vector2.zero);
            SubCanvas(_bottom);
            BuildBottom(_bottom);

            // vignette over everything (its own canvas, so it draws last), and the whole screen fades in on open
            var vig = Ui.Rect(root, "Vignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(vig);
            Ui.Img(vig, new Color(0, 0, 0, .14f), Ui.Vignette()).raycastTarget = false;
            _fade = root.gameObject.AddComponent<CanvasGroup>();

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
            var inner = Ui.Fill(frame, "In", BorderWidth);
            Ui.Img(inner, PanelBg);
            return inner;
        }

        private static void BuildHeader(RectTransform top)
        {
            // left: who you are — your rank emblem, "PROGRESSION" as a quiet page label, your rank name, how far to the next rank
            const float badge = 72;
            // 8 px lower than before: the emblem's bottom meets the level square's, the last line meets "Next level …"
            const float drop = 8;
            _headBadge = new Badge(top, new Vector2(0, 1), new Vector2(Margin + badge / 2, -(S3 + drop + badge / 2)), badge);
            float x = Margin + badge + S3;
            Ui.Label(Ui.Rect(top, "Page", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -50 - drop), new Vector2(-Gutter, -S3 - 2 - drop)), "Text", "PROGRESSION", TTitle, Grey, TextAnchor.UpperLeft, false, 1);
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
            var bar = Ui.Rect(right, "Bar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -58), new Vector2(0, -46));
            Ui.Img(bar, Ui.Hex("#4a5155"));
            var barIn = Ui.Fill(bar, "In", 2);
            Ui.Img(barIn, Ui.Hex("#15191b"));
            _xpFill = Ui.Rect(barIn, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(2, 2), new Vector2(0, -2));
            Ui.Img(_xpFill, Ui.Hex("#e0562f"));
            _xpText = Ui.Label(Ui.Rect(right, "Exp", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -44), new Vector2(0, -18)), "Text", "", THero, Ui.Hex("#b9c0c3"), TextAnchor.MiddleLeft, true);
            // the orange EXP tag right after the numbers (moved to the text's end whenever it changes)
            _xpTag = Ui.Rect(right, "ExpTag", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -40), new Vector2(40, -22));
            // outlined, not a solid block: the level square is the one solid orange shape here
            Ui.Img(_xpTag, Ui.Hex("#e0562f", .9f));
            Ui.Img(Ui.Fill(_xpTag, "In", 1), Ui.Hex("#1c1310"));
            Ui.Label(_xpTag, "Text", "EXP", TCaps, Ui.Hex("#e0562f"), TextAnchor.MiddleCenter, true, 1);
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

        private static void UpdateXpNext()
        {
            int player = ProgData.PlayerLevel();
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
                SetXpText($"<color=#b9c0c3>{Thousands(have)}</color><color=#6f777a> / </color><color=#eef2f3>{Thousands(need)}</color>");
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

        private static Component _listState;

        private static void BuildUnlocks(RectTransform top)
        {
            // left third: the selected level's rewards
            var panel = Panel(top, "Unlocks", new Vector2(0, 0), new Vector2(.34f, 1), new Vector2(Margin, S4), new Vector2(-Gutter / 2, -PanelTop));
            _focusList = panel;
            _listTitle = Ui.Label(Ui.Rect(panel, "Title", new Vector2(0, 1), Vector2.one, new Vector2(PanelPad, -48), new Vector2(-PanelPad, -S2)), "Text", "", TTitle, Text, TextAnchor.LowerLeft, false);
            // state on the right in small caps: "20 ITEMS · CURRENT" / "4 ITEMS"
            _listState = Ui.Label(Ui.Rect(panel, "State", new Vector2(0, 1), Vector2.one, new Vector2(PanelPad, -45), new Vector2(-PanelPad, -S2)), "Text", "", TCaps, Grey, TextAnchor.LowerRight, false, Caps);
            Ui.Img(Ui.Rect(panel, "Rule", new Vector2(0, 1), Vector2.one, new Vector2(0, -56), new Vector2(0, -55)), Border);

            var view = Ui.Rect(panel, "Scroll", Vector2.zero, Vector2.one, new Vector2(PanelPad, S2), new Vector2(-S2, -56 - S2));
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Ui.Fill(view, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            Ui.Img(viewport, new Color(0, 0, 0, 0), null, true);
            _content = Ui.Rect(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            _content.pivot = new Vector2(.5f, 1);
            var vl = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = S4; // between sections
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
            var floorR = Ui.Rect(picFace, "FloorR", new Vector2(.5f, .2f), new Vector2(.92f, .2f), Vector2.zero, new Vector2(0, 1));
            Ui.Img(floorR, new Color(1, 1, 1, .06f), Ui.HorizontalFade());
            var floorL = Ui.Rect(picFace, "FloorL", new Vector2(.08f, .2f), new Vector2(.5f, .2f), Vector2.zero, new Vector2(0, 1));
            Ui.Img(floorL, new Color(1, 1, 1, .06f), Ui.HorizontalFade());
            floorL.localScale = new Vector3(-1, 1, 1); // fades out to both sides
            Ui.Img(Ui.Rect(picFace, "Shadow", new Vector2(.2f, .14f), new Vector2(.8f, .26f), Vector2.zero, Vector2.zero), new Color(0, 0, 0, .5f), Ui.Radial());
            _featPic = Ui.Img(Ui.Fill(picFace, "Pic", 40), Color.white);
            _featPic.preserveAspect = true;
            _featPic.enabled = false;
            _featShort = Ui.Label(Ui.Fill(picFace, "Short", 40), "Text", "", THero, Dim, TextAnchor.MiddleCenter, false, 0, true);
            Ui.EdgeFade(picFace, .14f, .55f);
            Ui.Grit(picFace, 2, .017f);
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

            _featType = FlowText(info, "Category", TCaps, Grey, false, Caps);                  // CATEGORY
            _featName = FlowText(info, "Name", THero, Text, false, 0, wrap: true);             // Item name
            Rule(info, S1);
            _majorRow = Row(info, "Major", S4);                                                 // DAMAGE  PENETRATION …
            // WEIGHT  SIZE  CALIBER (a second line when there are more than fit the grid)
            _minorRow = Ui.Rect(info, "Minor", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mvl = _minorRow.gameObject.AddComponent<VerticalLayoutGroup>();
            mvl.spacing = S2; mvl.childControlHeight = true; mvl.childControlWidth = true; mvl.childForceExpandHeight = false; mvl.childForceExpandWidth = true;
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
            gap.gameObject.AddComponent<LayoutElement>().minHeight = S2; // + the group's 8 = 16 above INSPECT
            var btn = Ui.Rect(info, "Inspect", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ble = btn.gameObject.AddComponent<LayoutElement>();
            ble.minHeight = ble.preferredHeight = 40;
            var bimg = Ui.Img(btn, Ui.Hex("#34464c"), null, true);
            Ui.Label(btn, "Text", "INSPECT", TStrong, Text, TextAnchor.MiddleCenter, false, 1);
            var cap = Ui.Rect(btn, "Key", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-S3 - 38, -11), new Vector2(-S3, 11));
            Ui.Img(cap, Ui.Hex("#6a767b"));
            Ui.Img(Ui.Fill(cap, "In", 2), Ui.Hex("#22303a"));
            Ui.Label(cap, "Text", "RMB", TCaps, Ui.Hex("#c3ccd0"), TextAnchor.MiddleCenter, true);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = bimg;
            var colors = b.colors; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f); colors.pressedColor = new Color(.8f, .8f, .8f, 1f); b.colors = colors;
            b.onClick.AddListener(() => { if (_featTpl != null) Inspect(_featTpl); });
        }

        private static LayoutElement _reqSize, _descSize;
        private static Image _reqBg, _reqRed;
        private static GameObject _reqHead;
        private static RectTransform _reqRow;
        private static RectTransform _info, _descView;

        /// <summary>The description takes the height it needs (up to what's left, then it scrolls), so INSPECT follows the text.</summary>
        private static void FitDescription()
        {
            if (_descSize == null) return;
            _descSize.flexibleHeight = 1; _descSize.preferredHeight = -1;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_info);
            float avail = _descView.rect.height;
            float need = Refl.Get(_featDesc, "preferredHeight") is float h ? h : avail;
            _descSize.flexibleHeight = 0;
            _descSize.preferredHeight = Mathf.Min(need + S1, avail);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_info);
        }
        private static Component _featNote;

        /// <summary>The one place that says whether a reward is yours: REQUIREMENT · state, "Reach level X  n / X",
        /// and for a locked one how much XP is left (and that it's a preview).</summary>
        private static GameObject _featBand;
        private static Component _featBandText;
        private static Image _featReqLock;
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
            _featNote = Ui.Label(Ui.Rect(reqIn, "Note", new Vector2(0, 1), Vector2.one, new Vector2(S4 + 16 + S2, -78), new Vector2(-S4, -58)), "Text", "", TBody, Grey, TextAnchor.MiddleLeft, false);
        }

        private static ScrollRect _listScroll;
        private static int _scrollTopFrames;
        private static Image _listFade;

        /// <summary>A thin 2 px scroll thumb on the right edge, shown only when the content is taller than its view.</summary>
        private static void AddScrollCue(ScrollRect scroll, RectTransform view)
        {
            // 12 px wide to grab, 2 px wide to see
            var bar = Ui.Rect(view, "ScrollCue", new Vector2(1, 0), Vector2.one, new Vector2(-12, 0), Vector2.zero);
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            var area = Ui.Fill(bar, "Area");
            var handle = Ui.Fill(area, "Handle");
            var himg = Ui.Img(handle, new Color(0, 0, 0, 0), null, true);
            var line = Ui.Img(Ui.Rect(handle, "Line", new Vector2(1, 0), Vector2.one, new Vector2(-2, 0), Vector2.zero), Ui.Hex("#56636a", .9f));
            line.raycastTarget = false;
            HoverHook.Add(handle, on => FadeTo(line, on ? Ui.Hex("#8a969b") : Ui.Hex("#56636a", .9f)));
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
            Ui.Img(Ui.Rect(rt, "Line", new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, 0), new Vector2(0, 1)), Border);
        }

        private static RectTransform Row(RectTransform parent, string name, float spacing)
        {
            var rt = Ui.Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var hl = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = spacing; hl.childControlHeight = true; hl.childControlWidth = true; hl.childForceExpandWidth = true; hl.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>One stat: small caps label over its value (major: big and bold; minor: body size).</summary>
        private static void Stat(RectTransform row, string label, string value, bool major)
        {
            var cell = Ui.Rect(row, label, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            // every cell the same width (not sized by its text), so the rows share columns
            var le = cell.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0; le.preferredWidth = 0; le.flexibleWidth = 1; le.layoutPriority = 2;
            var vl = cell.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            var l = FlowText(cell, "Label", TCaps, Dim, false, Caps);
            Ui.SetText(l, label.ToUpperInvariant());
            var v = FlowText(cell, "Value", major ? THero : TStrong, Text, major, 0);
            Ui.SetText(v, value);
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
            var cards = Ui.Rect(bottom, "Cards", Vector2.zero, Vector2.one, new Vector2(Margin - Gutter / 2, 78), new Vector2(-Margin + Gutter / 2, -S3));
            for (int i = 0; i < PerPage; i++)
            {
                float a = i / (float)PerPage, b = (i + 1) / (float)PerPage;
                var slot = Ui.Rect(cards, "Slot" + i, new Vector2(a, 0), new Vector2(b, 1), new Vector2(Gutter / 2, 0), new Vector2(-Gutter / 2, 0));
                _cards[i] = new Card(slot, i == 0);
            }


            PerfToggle(bottom);
            BackToYou(bottom);

            // page bar like the Arena battle pass: [Q] ▬▬▬▬ … [E], the page numbers under the segments
            // stretches between fixed side insets (1120 px at 1920 wide, narrower on narrower screens), so it never runs into the checkbox
            var bar = Ui.Rect(bottom, "Pages", new Vector2(0, 0), new Vector2(1, 0), new Vector2(400, 14), new Vector2(-400, 60));
            _pageBarRt = bar;
            KeyBox(bar, "Q", 0, () => ShowPage(_page - 1, -1));
            KeyBox(bar, "E", 1, () => ShowPage(_page + 1, 1));
            var track = Ui.Rect(bar, "Track", Vector2.zero, Vector2.one, new Vector2(40, 0), new Vector2(-40, 0));
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
            var rt = Ui.Rect(bottom, "Perf", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Margin - PanelPad - 200, 14), new Vector2(-Margin - PanelPad, 14 + 24));
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
                ShowTip(on ? rt : null, "Lighter pictures, still emblems, fewer items per category");
            });
        }

        private static GraphicsQuality _beforeLow = GraphicsQuality.Medium;
        private static GameObject _backBtn;
        private static Component _backText;

        /// <summary>
        /// "‹ BACK TO LEVEL 40", bottom-left, only while you're looking at another page than your own: one click home
        /// (the Home key does the same). Styled like the game's own flat buttons: thin frame, dark face, small caps.
        /// </summary>
        private static void BackToYou(RectTransform bottom)
        {
            var rt = Ui.Rect(bottom, "BackToYou", Vector2.zero, Vector2.zero, new Vector2(Margin, 22), new Vector2(Margin + 200, 50));
            var edge = Ui.Img(rt, Ui.Hex("#3a4346"), null, true);
            var face = Ui.Img(Ui.Fill(rt, "In", 1), Ui.Hex("#0f1315", .92f));
            Ui.Img(Ui.Rect(rt, "Mark", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(2, 0)), Ui.Hex(Orange));
            _backText = Ui.Label(Ui.Rect(rt, "Text", Vector2.zero, Vector2.one, new Vector2(S3, 0), new Vector2(-S2, 0)), "Text", "", TCaps, Text, TextAnchor.MiddleLeft, false, Caps);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = edge;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { int p = ProgData.PlayerLevel(); if (p > 0) { Sounds.Click(); ShowLevel(Mathf.Min(p, ProgData.MaxLevel)); } });
            HoverHook.Add(rt, on => { FadeTo(edge, on ? HoverEdge : Ui.Hex("#3a4346")); FadeTo(face, on ? Ui.Hex("#151b1e", .95f) : Ui.Hex("#0f1315", .92f)); if (on) Sounds.Play("ButtonOver"); });
            _backBtn = rt.gameObject;
            _backBtn.SetActive(false);
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
            bool changed = page != _page;
            if (!changed && dir != 0) { L.Debug($"page {want + 1}: already at the {(want < 0 ? "first" : "last")} page"); return; }
            _page = page;
            if (changed) _sPages++;
            if (changed && dir != 0 && !_xpPaging) _level = levelAfter > 0 ? levelAfter : page * PerPage + 1;
            int first = page * PerPage + 1;
            L.Debug($"page {page + 1}/{Pages} (levels {first}–{Mathf.Min(ProgData.MaxLevel, first + PerPage - 1)}){(dir != 0 ? " slide " + (dir > 0 ? "right" : "left") : "")}");
            _prev.interactable = _level > 1;
            _next.interactable = _level < ProgData.MaxLevel;
            UpdatePageBar();
            _hits.RemoveAll(h => h.Rect == null || h.Rect.IsChildOf(_bottom));
            Adopt(_cardIcons); DropRequests(_cardIcons);
            for (int i = 0; i < PerPage; i++) _cards[i].Show(first + i);
            _cardsDir = dir;
            _cardsStart = dir != 0 ? Time.unscaledTime : -10;
            if (changed && dir != 0) { Sounds.Page(); if (_xpPaging) UpdateSelection(); else ShowLevel(_level); }
            else UpdateSelection();
        }

        private static int _segHover = -1;

        private static void UpdatePageBar()
        {
            // Arena page bar: a page is lit once every level on it is unlocked; the page you're looking at stands
            // 4 px taller (lit or not), so you always see where you are
            int player = ProgData.PlayerLevel();
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
                Ui.SetColor(_segmentNums[i], cur ? (done ? Color.white : Grey) : done ? Ui.Hex("#b4bec2") : Dim);
                // labelled by level range ("11–15"), not page number: every range when there's room, else only the current one
                string range = $"{i * PerPage + 1}–{Mathf.Min(ProgData.MaxLevel, (i + 1) * PerPage)}";
                bool room = _segments[i].rectTransform.parent is RectTransform tr && tr.rect.width / Mathf.Max(1, Pages) >= 44;
                Ui.SetText(_segmentNums[i], cur ? $"<b>{range}</b>" : room || _segHover == i ? range : "");
            }
            // YOU: an orange mark over your own page, so you can find your way back while browsing
            int mine = player > 0 ? (Mathf.Min(player, ProgData.MaxLevel) - 1) / PerPage : -1;
            if (_backBtn != null)
            {
                bool away = mine >= 0 && _page != mine;
                if (_backBtn.activeSelf != away) _backBtn.SetActive(away);
                if (away) Ui.SetText(_backText, $"‹  BACK TO LEVEL {Mathf.Min(player, ProgData.MaxLevel)}");
            }

        }


        private static int _shownLevel; // the level whose rewards the list shows right now (0 = none)

        private static void ShowLevel(int level, bool force = false)
        {
            L.Step("ShowLevel " + level);
            level = Mathf.Clamp(level, 1, ProgData.MaxLevel);
            if (!force && level == _shownLevel && (level - 1) / PerPage == _page) { _level = level; UpdateSelection(); return; } // already showing it
            int page = (level - 1) / PerPage;
            _level = level;
            if (page != _page) { ShowPage(page, page > _page ? 1 : -1, level); return; }
            if (!XpAnimating) _newViewed.Add(level); // looked at: its card's NEW goes (its items keep theirs until clicked)
            float t0 = Time.realtimeSinceStartup;
            var items = ProgData.ItemsAt(level);
            int player = ProgData.PlayerLevel();

            SetMood(player > 0 && level > player);
            UpdateXp();

            // the reward list
            var groups = ProgData.Groups.Select(g => (g, list: items.Where(it => it.Group == g.Key).ToList())).Where(x => x.list.Count > 0).ToList();
            Ui.SetText(_listTitle, $"LEVEL {level} <color=#7d8588>REWARDS</color>");
            string count = $"{items.Count} ITEM{(items.Count == 1 ? "" : "S")}";
            // the count, plus the level's role only when it has one; how far away (and locked / unlocked) is said once, in the requirement box
            string st = player <= 0 ? count : level == player ? $"{count}  ·  <color=#e0562f>CURRENT</color>" : level == player + 1 ? $"{count}  ·  NEXT" : count;
            Ui.SetText(_listState, st);

            // old tiles out of the layout right away (Destroy only happens at the end of the frame, and for that frame the
            // new list was laid out under them — it opened scrolled down), and back to the top once it's built
            foreach (Transform ch in _content) { ch.gameObject.SetActive(false); UnityEngine.Object.Destroy(ch.gameObject); }
            _scrollTopFrames = 2;
            _tiles.Clear();
            _tileViews.Clear();
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
            var dupes = new HashSet<string>(items.GroupBy(x => x.Short).Where(x => x.Count() > 1).Select(x => x.Key));
            // small levels (a handful of items over several categories): one grid, the category as a label on each tile,
            // instead of a one-tile section per category (a single column with the panel ¾ empty)
            bool compact = items.Count <= 8 && groups.Count > 1;
            // three or fewer: three bigger tiles across (at 4 across, 3 items left most of the panel empty)
            if (compact && items.Count <= 3 && cols > 3) { cols = 3; cell = Mathf.Floor((width - (cols - 1) * S2) / cols); }
            _tileCell = cell;
            if (compact)
            {
                var grid = Ui.Rect(_content, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
                gl.cellSize = new Vector2(cell, Mathf.Round(cell * .78f) + TileLabel); // 4:3 thumbnails
                gl.spacing = new Vector2(S2, S2);
                gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
                gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gl.constraintCount = cols;
                foreach (var (g, list) in groups)
                    foreach (var it in list) Tile(grid, it, level <= player || player <= 0, dupes.Contains(it.Short), n++, g);
            }
            else foreach (var (g, list) in groups)
            {
                var section = Ui.Rect(_content, "Cat_" + g.Key, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var sl = section.gameObject.AddComponent<VerticalLayoutGroup>();
                sl.spacing = S2; sl.childControlHeight = true; sl.childControlWidth = true; sl.childForceExpandHeight = false; sl.childForceExpandWidth = true;

                // section header: the category's colour once, as a 3 px bar; name in small caps; the count quieter
                var head = Ui.Rect(section, "Head", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                head.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;
                Ui.Img(Ui.Rect(head, "Bar", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -7), new Vector2(3, 7)), Ui.Hex(g.Color, .85f));
                Ui.Label(Ui.Rect(head, "Name", Vector2.zero, Vector2.one, new Vector2(3 + S2, 0), Vector2.zero), "Text",
                    $"{g.Name.ToUpperInvariant()}  <color=#7d8588>{list.Count}{(list.Count > max ? $" · showing {max}" : "")}</color>", TCaps, Ui.Hex("#b9c0c3"), TextAnchor.MiddleLeft, false, Caps);

                var grid = Ui.Rect(section, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
                gl.cellSize = new Vector2(cell, Mathf.Round(cell * .78f) + TileLabel); // 4:3 thumbnails
                gl.spacing = new Vector2(S2, S2);
                gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
                gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gl.constraintCount = cols;
                foreach (var it in list.Take(max)) Tile(grid, it, level <= player || player <= 0, dupes.Contains(it.Short), n++);
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

        private const float TileMin = 118;   // smallest tile width before a column is dropped
        private const float TileLabel = 34;  // name area under the thumbnail (two lines of 12 px)

        /// <summary>One reward tile's parts, so hover / selection / locked can be restyled without rebuilding it.</summary>
        private sealed class TileView
        {
            public ProgItem Item;
            public RectTransform Rt;
            public Image Frame, Face, Top, Pic;
            public Component Name;
            public bool Locked, Hover;
            public GameObject NewTag;
        }

        // NEW tags: an item's goes away once you click it, a level card's once you've looked at that level; all of them when
        // the screen closes (the last seen level is already yours by then)
        private static readonly HashSet<string> _newClicked = new HashSet<string>();
        private static readonly HashSet<int> _newViewed = new HashSet<int>();

        private static void ClickedNew(ProgItem it)
        {
            if (it == null || !_newClicked.Add(it.Tpl)) return;
            if (_tileViews.TryGetValue(it.Tpl, out var v) && v.NewTag != null) { UnityEngine.Object.Destroy(v.NewTag); v.NewTag = null; L.Debug($"NEW: {it.Name} seen"); }
        }

        private static readonly Dictionary<string, TileView> _tileViews = new Dictionary<string, TileView>();
        private static int _newFrom; // your level when you last opened the screen: rewards above it (up to yours) are new

        /// <summary>An inventory-style tile: thin frame, dark lit surface, big centred thumbnail, the name under it.</summary>
        private static void Tile(RectTransform grid, ProgItem it, bool reached, bool fullName, int index, (string Key, string Name, string Color, string[] Ids)? category = null)
        {
            var rt = Ui.Rect(grid, "Tile", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            var v = new TileView { Item = it, Rt = rt, Locked = !reached };
            v.Frame = Ui.Img(rt, Border, null, true);
            var inner = Ui.Fill(rt, "Inner", 1);
            v.Face = Ui.Img(inner, Face);
            // thumbnail: a square on top, lit like the big preview; the icon uses ~80% of it
            var thumb = Ui.Rect(inner, "Thumb", new Vector2(0, 0), Vector2.one, new Vector2(0, TileLabel - 1), new Vector2(0, category.HasValue ? -16 : 0));
            Ui.Img(Ui.Fill(thumb, "Light"), new Color(1, 1, 1, .05f), Ui.Radial());
            var placeholder = Ui.Label(Ui.Fill(thumb, "Placeholder", S2), "Text", "", TCaps, Dim, TextAnchor.MiddleCenter, false, 0, true);
            v.Pic = Ui.Img(Ui.Fill(thumb, "Icon", 0), Color.white);
            var prt = v.Pic.rectTransform;
            prt.anchorMin = new Vector2(.1f, .1f); prt.anchorMax = new Vector2(.9f, .9f); prt.offsetMin = prt.offsetMax = Vector2.zero;
            v.Pic.preserveAspect = true;
            v.Pic.enabled = false;
            // selection: a 2 px light bar along the top (so selected isn't told by colour alone)
            v.Top = Ui.Img(Ui.Rect(inner, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero), Select);
            v.Top.enabled = false;
            // name: two lines of 12 px, the full name when short names collide
            v.Name = Ui.Label(Ui.Rect(inner, "Name", Vector2.zero, new Vector2(1, 0), new Vector2(S2, S1), new Vector2(-S2, TileLabel - S1)), "Text",
                fullName ? it.Name : it.Short, TCaps, Grey, TextAnchor.MiddleLeft, false, 0, true);
            Ui.SetWrap(v.Name, true);
            Refl.Set(v.Name, "lineSpacing", -8f);
            // compact list: the category on the tile itself (its colour bar + small caps), top-left
            if (category is var cat && cat.HasValue)
            {
                Ui.Img(Ui.Rect(inner, "CatBar", new Vector2(0, 1), new Vector2(0, 1), new Vector2(S2, -S2 - 10), new Vector2(S2 + 2, -S2)), Ui.Hex(cat.Value.Color, .85f));
                Ui.Label(Ui.Rect(inner, "Cat", new Vector2(0, 1), Vector2.one, new Vector2(S2 + 2 + S1, -S2 - 12), new Vector2(-S2, -S2 + 2)), "Text",
                    cat.Value.Name.ToUpperInvariant(), 10, Grey, TextAnchor.MiddleLeft, false, 1, true);
            }
            // newly reached since you last opened the screen: a small restrained tag (top-right)
            if (_newFrom > 0 && reached && it.Level > _newFrom && !_newClicked.Contains(it.Tpl))
            {
                var tag = Ui.Rect(inner, "New", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-S1 - 30, -S1 - 14), new Vector2(-S1, -S1));
                v.NewTag = tag.gameObject;
                Ui.Img(tag, Ui.Hex(Orange, .9f));
                Ui.Label(tag, "Text", "NEW", 10, Ui.Hex("#1a1210"), TextAnchor.MiddleCenter, true, 1);
            }
            RequestIcon(_icons, it.Tpl, TileScaleOf(it.Tpl), v.Pic, placeholder);
            _hits.Add((rt, it));
            HoverHook.Add(v.Frame, on =>
            {
                if (v.Hover == on) return;
                v.Hover = on;
                if (on) Sounds.Play("ButtonOver");
                ApplyTile(v);
                ShowTip(on ? v : null);
            });
            _tileViews[it.Tpl] = v;
            ApplyTile(v, true);
            _tiles.Add((group, inner, Mathf.Min(index, 40) * .012f));
        }

        /// <summary>normal: neutral · hover: brighter edge and surface · selected: light edge + top bar + bright name · locked: muted.</summary>
        private static void ApplyTile(TileView v, bool instant = false)
        {
            if (v?.Frame == null) return;
            bool sel = v.Item.Tpl == _featTpl;
            FadeTo(v.Frame, sel ? Select : v.Hover ? HoverEdge : Border, instant);
            FadeTo(v.Face, sel ? FaceSelect : v.Hover ? FaceHover : Face, instant);
            v.Top.enabled = sel;
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
            _tip.sizeDelta = new Vector2(Mathf.Min(w, 420), 26);
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
            int budget = Perf ? 2 : 4;
            var t0 = Time.realtimeSinceStartup;
            // cards first
            _iconRequests.Sort((a, b) => (a.Target == _cardIcons ? 0 : 1).CompareTo(b.Target == _cardIcons ? 0 : 1));
            while (_iconRequests.Count > 0 && budget-- > 0 && Time.realtimeSinceStartup - t0 < .025f)
            {
                var r = _iconRequests[0];
                _iconRequests.RemoveAt(0);
                if (r.Pic == null) continue;
                var have = r.Scale != 1 ? GameItems.CopyOf(r.Tpl, r.Scale) : null;
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
            if (!_warm && !_loading) return;
            _warm = _loading = false;
            _loadQueue.Clear(); _loadWaiting.Clear();
            int n = GameItems.ClearCopies();
            ForgetPictures();
            L.Info($"loading: {n} kept picture(s) let go ({why}); the next open loads them again");
        }

        private static void Prefetch()
        {

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
                if (_iconsShown++ == 0) L.Info($"first item icon shown ({tpl}, {sprite.rect.width:0}x{sprite.rect.height:0} px)");
            }
        }

        /// <summary>The item in the centre and its details on the right.</summary>
        private static void Feature(ProgItem it)
        {
            L.Step("Feature " + it?.Tpl);
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
                foreach (var c in new[] { _featType, _featName, _featReq, _featReqValue, _featStatus, _featDesc, _featNote }) Ui.SetText(c, "");
                _featCheck.enabled = false;
                _featReqLock.enabled = false;
                _featBand.SetActive(false);
                _featLock.enabled = false;
                _featIcon = null;
                MarkSelectedTile();
                return;
            }
            var g = ProgData.Groups.FirstOrDefault(x => x.Key == it.Group);
            int player = ProgData.PlayerLevel();
            bool met = player <= 0 || it.Level <= player;
            Ui.SetText(_featShort, it.Short);
            Ui.SetText(_featType, (g.Name ?? "Item").ToUpperInvariant());
            Ui.SetText(_featName, it.Name);
            // stats: damage / penetration / armor class / resource big; weight / size / caliber small (nothing invented, nothing dropped)
            var facts = GameItems.Facts(it.Tpl);
            var majorKeys = new[] { "Damage", "Penetration", "Armor class", "Resource", "Fire rate", "Ergonomics", "Recoil" };
            var majors = facts.Where(f => majorKeys.Contains(f.Label)).ToList();
            var minors = new[] { "Weight", "Size", "Caliber" }.Select(k => facts.FirstOrDefault(f => f.Label == k)).Where(f => f.Label != null)
                .Concat(facts.Where(f => !majorKeys.Contains(f.Label) && f.Label != "Weight" && f.Label != "Size" && f.Label != "Caliber")).ToList();
            // one grid of equal columns for every row (3, or more only if there are more big stats): the big stats never get
            // squeezed ("600 rpm" ran into ERGONOMICS at 4 columns); extra small stats wrap onto another line
            int cols = Mathf.Max(3, majors.Count);
            foreach (var f in majors) Stat(_majorRow, f.Label, f.Value, true);
            for (int c = majors.Count; c < cols && majors.Count > 0; c++) Stat(_majorRow, "", "", true);
            int minorCols = minors.Count == 4 ? 4 : cols; // four small stats: one row of four, not three and an orphan
            for (int start = 0; start < minors.Count; start += minorCols)
            {
                var line = Row(_minorRow, "Line", S4);
                for (int c = 0; c < minorCols; c++)
                {
                    var f = start + c < minors.Count ? minors[start + c] : ("", "");
                    Stat(line, f.Item1, f.Item2, false);
                }
            }
            _majorRow.gameObject.SetActive(majors.Count > 0);
            _minorRow.gameObject.SetActive(minors.Count > 0);
            Ui.SetText(_featDesc, ProgData.DescriptionOf(it.Tpl));
            _descScroll.verticalNormalizedPosition = 1;
            // requirement: the primary (and only red) locked signal
            _featCheck.enabled = met;
            _featReqLock.enabled = !met && player > 0;
            bool lockedHere = player > 0 && it.Level > player;
            _featBand.SetActive(lockedHere);
            if (lockedHere) Ui.SetText(_featBandText, $"UNLOCKS AT LEVEL {it.Level}  ·  {it.Level - player} LEVEL{(it.Level - player == 1 ? "" : "S")} AWAY");
            Ui.SetText(_featReq, $"Reach level {it.Level}");
            Ui.SetText(_featReqValue, player > 0 ? (met ? $"{Mathf.Min(player, it.Level)} / {it.Level}" : $"<color={Red}>{Mathf.Min(player, it.Level)} / {it.Level}</color>") : "");
            Ui.SetText(_featStatus, player <= 0 ? "" : it.Level == player ? "<color=#e0562f>CURRENT LEVEL</color>" : met ? "UNLOCKED" : $"{it.Level - player} LEVEL{(it.Level - player == 1 ? "" : "S")} AWAY");
            // unlocked: one quiet line ("✓ Unlocked at level 1"); locked: the full box, the one place that explains it
            bool full = player > 0 && !met;
            _reqHead.SetActive(full);
            _featStatus.gameObject.SetActive(full);
            _reqEdge.color = full ? Border : new Color(0, 0, 0, 0);
            _reqBg.color = full ? Ui.Hex("#0b0f11", .9f) : new Color(0, 0, 0, 0);
            _reqRed.enabled = full;
            _reqRow.offsetMin = new Vector2(full ? S4 : 0, full ? -54 : -26);
            _reqRow.offsetMax = new Vector2(full ? -S4 : 0, full ? -30 : -2);
            if (!full)
            {
                Ui.SetText(_featReq, player <= 0 ? $"Unlocks at level {it.Level}" : it.Level == player ? "Unlocked at your current level" : $"Unlocked at level {it.Level}");
                Ui.SetText(_featReqValue, "");
            }
            string note = full ? (ProgData.XpTo(it.Level, out int xp) ? $"{Thousands(xp)} EXP to go  ·  Preview only" : "Preview only") : "";
            Ui.SetText(_featNote, note);
            _reqSize.minHeight = _reqSize.preferredHeight = full ? 84 : 28;
            FitDescription();
            _featLock.enabled = false; // the band says it now
            _featPic.color = player > 0 && it.Level > player ? new Color(.82f, .82f, .82f, 1) : Color.white; // locked: a shade darker
            int featScale = FeatScaleOf(it);
            if (it.Group == "Weapons" && featScale > 1) _sharpWeaponShown = true; // weapons get repaired on close
            _featScale = featScale;
            var kept = GameItems.CopyOf(it.Tpl, featScale);
            FitFeat(null);
            if (kept != null) { _featIcon = null; _featPic.sprite = kept; _featPic.enabled = true; _featShort.gameObject.SetActive(false); FitFeat(kept); LogSharpness(kept, "kept"); }
            else if (!_loading) _featIcon = GameItems.IconOf(GameItems.ItemOf(it.Tpl), featScale);
            MarkSelectedTile();
        }

        /// <summary>
        /// The centre picture's render size for an item: enough real pixels for the preview at this resolution (Low: half).
        /// Weapons only on High: the game's weapon icons can leak a big render onto OTHER weapons in the stash (a VPO-215
        /// came out huge after VPO-136 / VPO-209 were drawn big), so High redraws them at stash size afterwards.
        /// </summary>
        private static int FeatScaleOf(ProgItem it)
        {
            float px = Mathf.Max(_featPic.rectTransform.rect.width, _featPic.rectTransform.rect.height) * (_featPic.canvas != null ? _featPic.canvas.scaleFactor : 1f);
            if (px >= 64) _featPx = px; else px = _featPx > 0 ? _featPx : 440;
            int featScale = GameItems.ScaleFor(it.Tpl, px);
            if (Perf) featScale = Mathf.Max(2, featScale / 2);
            if (it.Group == "Weapons" && !ProgressionPlugin.High) featScale = 1;
            return featScale;
        }
        private static float _featPx;

        /// <summary>
        /// The centre picture never stretched a little past its real pixels (the game draws some items smaller than asked):
        /// up to 1.6× too small it's shown at its own size, sharp; far smaller (a stand-in) it still fills the box.
        /// </summary>
        private static void FitFeat(Sprite sp)
        {
            var rt = _featPic.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(40, 40); rt.offsetMax = new Vector2(-40, -40);
            if (sp == null || !(rt.parent is RectTransform parent)) return;
            float sf = _featPic.canvas != null && _featPic.canvas.scaleFactor > 0 ? _featPic.canvas.scaleFactor : 1f;
            var box = parent.rect.size - new Vector2(80, 80);
            float fit = Mathf.Min(box.x * sf / sp.rect.width, box.y * sf / sp.rect.height);
            if (fit <= 1.02f || fit > 1.6f) return;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(sp.rect.width, sp.rect.height) / sf;
            rt.anchoredPosition = Vector2.zero;
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
            if (_bloom.Count > 0 && _bloom[0] != null) _bloom[0].color = new Color(.85f, .14f, .08f, Mathf.Lerp(.07f, .1f, m));
            if (_bloom.Count > 1 && _bloom[1] != null) _bloom[1].color = new Color(.95f, .2f, .1f, Mathf.Lerp(.05f, .08f, m));
            var border = Border; // the panel borders stay as they are (only the glow turns red)
            foreach (var f in _panelFrames) if (f != null) f.color = border;
        }

        private static void UpdateSelection()
        {
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
                // Esc closes us only when no game window (inspect…) is open — otherwise it's the window's Esc
                else if (input.GetKeyDown(KeyCode.Escape)) { Close("Escape"); return; }
            }
            else if (!xpBusy && input.GetKeyDown(KeyCode.Escape)) L.Debug("Esc with a game window open: left to the window");

            float now = Time.unscaledTime;
            float wheel = input.mouseScrollDelta.y;
            if (!window && !xpBusy && Mathf.Abs(wheel) > .01f && now - _wheelAt > .3f && MenuHook.Contains(_bottom, input.mousePosition))
            {
                _wheelAt = now;
                ShowPage(_page + (wheel < 0 ? 1 : -1), wheel < 0 ? 1 : -1);
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
                    if (over != null) Inspect(over.Tpl);
                }
            }
            if (_featIcon != null)
            {
                var sp = GameItems.TakeSprite(_featIcon, _featTpl, out bool done);
                if (sp != null)
                {
                    if (_featPic.sprite != sp) _featPic.sprite = sp; // stand-in first, the full render when it arrives
                    _featPic.enabled = true; _featShort.gameObject.SetActive(false);
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

            public Badge(RectTransform parent, Vector2 anchor, Vector2 pos, float size)
            {
                _root = Ui.Box(parent, "Badge", anchor, pos, new Vector2(size, size));
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

            public bool Visible { set { if (_root.gameObject.activeSelf != value) _root.gameObject.SetActive(value); } }

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
            private readonly Image _frame, _bg, _glow, _top, _stateLock;
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
            }

            public void TickFlash()
            {
                TickFloat();
                if (_flash == null || !_flash.enabled) return;
                float t = Time.unscaledTime - _flashAt;
                // up in 0.08 s, out over 0.7 s
                float a = t < .08f ? t / .08f : Mathf.Clamp01(1 - (t - .08f) / .7f);
                _flash.color = new Color(.88f, .34f, .18f, .5f * a);
                // and a small punch: up to 104% and back in 0.3 s
                float sc = 1 + .04f * Mathf.Sin(Mathf.Clamp01(t / .3f) * Mathf.PI);
                _body.localScale = new Vector3(sc, sc, 1);
                if (t > .8f) { _flash.enabled = false; _body.localScale = Vector3.one; }
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
                // |------ Level 6 ------|  dots bright by the label, fading out toward the ticks at the card's edges (Arena)
                var head = Ui.Rect(_body, "Head", new Vector2(0, 1), Vector2.one, new Vector2(0, -24), Vector2.zero);
                _head = Ui.Label(head, "Text", "", TStrong, Grey, TextAnchor.MiddleCenter, true);
                _headL = DottedLine.Add(Ui.Rect(head, "L", new Vector2(0, 0), new Vector2(.5f, 1), Vector2.zero, new Vector2(-46, 0)), false);
                _headR = DottedLine.Add(Ui.Rect(head, "R", new Vector2(.5f, 0), new Vector2(1, 1), new Vector2(46, 0), Vector2.zero), true);
                // a single 1 px rule each side (ten dotted runs across the strip were noise)
                foreach (var d in new[] { _headL, _headR }) { d.Dash = 6; d.Gap = 0; d.Thickness = 1; } // gap 0: a continuous line that still fades out

                var card = Ui.Rect(_body, "Box", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -32));
                _frame = Ui.Img(card, Border, null, true);
                var inner = Ui.Fill(card, "In", 2);
                _bg = Ui.Img(inner, Ui.Hex("#12181b", .92f), null, true);
                // Arena-style warm glow in the top-right corner of the picked card
                var glow = Ui.Box(inner, "Glow", new Vector2(1, 1), Vector2.zero, new Vector2(260, 260));
                _glow = Ui.Img(glow, new Color(0, 0, 0, 0), Ui.Radial());
                inner.gameObject.AddComponent<RectMask2D>();
                // header row: small badge + rank title, tight to the top-left
                // one spacing unit (Pad) everywhere: card edge → badge, badge → title, header → pictures, pictures → bottom row
                // (the badge's diamond is ~B wide corner to corner, so its box sits exactly Pad from the edges)
                // the rank stays quiet: a small emblem and small caps (the level itself is the card's header)
                const float Pad = S3, B = 26, Head = S2 + B + S2 + 2, Foot = 32;
                _badge = new Badge(inner, new Vector2(0, 1), new Vector2(Pad + B / 2, -(S2 + B / 2)), B);
                _tier = Ui.Label(Ui.Rect(inner, "Tier", new Vector2(0, 1), Vector2.one, new Vector2(Pad + B + S2, -(S2 + B)), new Vector2(-Pad, -S2)), "Text", "", TCaps, Grey, TextAnchor.MiddleLeft, false, Caps);
                // selected: a 2 px light bar along the top edge (shape, not only colour)
                _top = Ui.Img(Ui.Rect(inner, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero), Select);
                _top.enabled = false;
                // one big picture (the level's top category) and two small square ones stacked beside it
                // the pictures sit in the area between header and footer; each is kept square
                // the page's first card keeps a row for its rank emblem; the others centre their pictures (same size) instead of
                // leaving that row empty over them
                // every card: the same picture box at the same height (the first card's smaller emblem sits above it)
                float mid = (Head + Foot) / 2;
                var pics = Ui.Rect(inner, "Pics", Vector2.zero, Vector2.one, new Vector2(Pad, mid), new Vector2(-Pad, -mid));
                // the big one is landscape (fills its column): weapons are wide — in a square they came out tiny
                _picRects[0] = Square(pics, "Pic0", new Vector2(0, 0), new Vector2(.64f, 1), Vector2.zero, new Vector2(-S1, 0), 1.9f);
                _picRects[1] = Square(pics, "Pic1", new Vector2(.64f, .5f), new Vector2(1, 1), new Vector2(S1, S1 / 2), Vector2.zero);
                _picRects[2] = Square(pics, "Pic2", new Vector2(.64f, 0), new Vector2(1, .5f), new Vector2(S1, 0), new Vector2(0, -S1 / 2));
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
                    _pics[i] = Ui.Img(Ui.Fill(face, "Img", i == 0 ? 8 : 4), Color.white);
                    _pics[i].preserveAspect = true;
                    _pics[i].enabled = false;
                    Ui.EdgeFade(face, .18f, .55f);
                    Ui.Grit(face, _gritSeed++, .02f);
                }
                // a level with nothing on it: a quiet line in the picture area instead of empty boxes
                _empty = Ui.Label(pics, "Empty", "NO NEW ITEMS", TCaps, Dim, TextAnchor.MiddleCenter, false, Caps);
                _empty.gameObject.SetActive(false);
                // footer: "147 unlocks" left, the level's state right in small caps (one small lock for future levels)
                _count = Ui.Label(Ui.Rect(inner, "Count", new Vector2(0, 0), new Vector2(.5f, 0), new Vector2(Pad, S1), new Vector2(0, Foot - S1)), "Text", "", TBody, Text, TextAnchor.MiddleLeft, false);
                _state = Ui.Label(Ui.Rect(inner, "State", new Vector2(.5f, 0), new Vector2(1, 0), new Vector2(0, S1), new Vector2(-Pad, Foot - S1)), "Text", "", TCaps, Grey, TextAnchor.MiddleRight, false, Caps);
                _stateLock = Ui.Img(Ui.Rect(inner, "StateLock", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Pad - 68 - 12, Foot / 2 - 6), new Vector2(-Pad - 68, Foot / 2 + 6)), Grey, Ui.Lock());
                _stateLock.enabled = false;
                var button = inner.gameObject.AddComponent<Button>();
                button.targetGraphic = _bg;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { L.Debug($"card level {_level} clicked"); Sounds.Click(); ShowLevel(_level); });
                HoverHook.Add(_frame, on => { if (_hover == on) return; _hover = on; if (on) Sounds.Play("ButtonOver"); Mark(_picked, _player); });
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
                Ui.SetText(_head, "LEVEL " + level);
                // one emblem per page: on its first card, with the rank's name (the header shows yours)
                _badge.Set(level, items.Count == 0);
                _badge.Visible = _first;
                Ui.SetText(_tier, _first ? TierOf(level).Name : "");
                Ui.SetText(_count, items.Count == 0 ? "" : $"{items.Count} item{(items.Count == 1 ? "" : "s")}");
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
                // one item: its picture centred in the card (not in the left column with an empty one beside it)
                if (_picRects[0].parent is RectTransform area)
                {
                    bool single = picks.Count == 1;
                    area.anchorMin = new Vector2(single ? .18f : 0, 0);
                    area.anchorMax = new Vector2(single ? .82f : .64f, 1);
                    area.offsetMax = new Vector2(single ? 0 : -S1, area.offsetMax.y);
                }
                _group.alpha = 1f;
            }

            /// <summary>current (your level): orange header + state · selected: light 2 px frame + top bar · hover: brighter edge ·
            /// future: muted, one small lock, "LOCKED" in grey · new since your last visit: a small orange NEW.</summary>
            public void Mark(int picked, int player)
            {
                _picked = picked; _player = player;
                bool sel = _level == picked, current = player > 0 && _level == player;
                bool locked = player > 0 && _level > player, reached = player > 0 && _level <= player;
                bool fresh = reached && !current && _newFrom > 0 && _level > _newFrom && !_newViewed.Contains(_level);
                FadeTo(_frame, sel ? Select : _hover ? HoverEdge : Border);
                _top.enabled = sel;
                FadeTo(_bg, sel ? Ui.Hex("#172024", .96f) : _hover ? Ui.Hex("#141b1e", .94f) : locked ? Ui.Hex("#0b0e10", .96f) : Ui.Hex("#11171a", .92f));
                _glow.color = new Color(0, 0, 0, 0); // the orange header is enough for the current level
                Ui.SetColor(_tier, locked ? Dim : Grey);
                Ui.SetColor(_count, sel || current ? Text : Grey); // counts stay readable, only the viewing / current one is bright
                var head = current ? Ui.Hex(Orange) : sel ? Text : locked ? Dim : Grey;
                // the card's role, spelled out once in its header: CURRENT (you), VIEWING (picked), NEXT
                string role = current ? "CURRENT" : sel ? "VIEWING" : player > 0 && _level == player + 1 ? "NEXT" : "";
                string label = role == "" ? $"LEVEL {_level}" : $"LEVEL {_level}  <color=#{ColorUtility.ToHtmlStringRGB(current ? Ui.Hex(Orange) : sel ? Text : Grey)}>·  {role}</color>";
                Ui.SetText(_head, label);
                Ui.SetColor(_head, head);
                _headL.color = _headR.color = new Color(head.r, head.g, head.b, current || sel ? .9f : .45f);
                // keep the dots clear of the (now variable-width) label
                float hw = Ui.PreferredWidth(_head, label) / 2 + S2;
                _headL.rectTransform.offsetMax = new Vector2(-hw, _headL.rectTransform.offsetMax.y);
                _headR.rectTransform.offsetMin = new Vector2(hw, _headR.rectTransform.offsetMin.y);
                bool empty = ProgData.CountAt(_level) == 0;
                bool nextUp = player > 0 && _level == player + 1;
                string state = player <= 0 || empty ? "" : fresh ? "<color=#e0562f>NEW</color>" : reached ? "" : nextUp ? "LOCKED" : ""; // said once, on the next level
                Ui.SetText(_state, state);
                _stateLock.enabled = locked && !empty && nextUp;
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
                foreach (var pic in _pics) FadeTo(pic, new Color(1, 1, 1, pa));
                _group.alpha = _baseAlpha;
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
