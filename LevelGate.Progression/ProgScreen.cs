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
    internal static class ProgScreen
    {
        private const int PerPage = 5;
        private static int Pages => Mathf.CeilToInt(ProgData.MaxLevel / (float)PerPage);

        // colors: muted, like the Tarkov battle pass
        private static readonly Color Text = Ui.Hex("#d5d9d6");
        private static readonly Color Grey = Ui.Hex("#7d8588");
        private static readonly Color Dim = Ui.Hex("#4f575a");
        private static readonly Color Accent = Ui.Hex("#c9d8d2");
        private static readonly Color Teal = Ui.Hex("#7fb3a8");
        private static readonly Color PanelBg = Ui.Hex("#10161a", .80f);
        private static readonly Color Border = Ui.Hex("#2b3438", .9f);
        private const string Green = "#8fb35a", Red = "#d0453a", Yellow = "#e3c26a";

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
        private static (int From, string Name, string Rim, string Light) TierOf(int level) => Tiers.Last(t => level >= t.From);

        private static GameObject _canvas;
        private static RectTransform _bottom;
        private static int _level = 1, _page;
        private static bool _built, _changedHooked;

        // header
        private static Badge _headBadge;
        private static Component _headSub;
        // left: unlocks
        private static Component _listTitle, _listCount;
        private static RectTransform _content;
        // centre + right: the featured item
        private static Image _featPic, _featLock;
        private static Component _featShort, _featMeta, _featType, _featName, _featReq, _featReqValue, _featStatus;
        private static Image _featCheck;
        private static Component _featFacts, _featFactValues, _featDesc;
        private static string _featTpl;
        private static object _featIcon;
        private static bool _featIconLogged;
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
            try
            {
                if (!_built || _canvas == null) { _built = false; Build(); }
                ProgData.Invalidate(); // names / categories again (the game may have finished loading them since)
                int player = ProgData.PlayerLevel();
                L.Info($"screen open ({why}); player level {player}, {ProgData.Levels.Count} limited items");
                if (player > 0 && _level == 1) { _level = Mathf.Clamp(player, 1, ProgData.MaxLevel); _page = (_level - 1) / PerPage; }
                _canvas.SetActive(true);
                _openedAt = Time.unscaledTime;
                if (_fade != null) _fade.alpha = 0;
                MenuHook.SetOn(true);
                HideMenu(true);
                MenuCamera.Turn(true);
                Sounds.Open();
                _frames = 0; _frameTime = 0; _frameLogAt = Time.unscaledTime + 5;
                ShowPage(_page, 0);
                ShowLevel(_level);
            }
            catch (Exception e) { L.Error("opening the screen", e); }
        }

        public static void Close(string why)
        {
            if (!IsOpen) return;
            _canvas.SetActive(false);
            MenuHook.SetOn(false);
            HideMenu(false);
            GameItems.RestoreIcons(); // the big renders replaced the stash's cached icons: put them back
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
                L.Info("screen on its own canvas on top " + (menu == null ? "(the game's MenuScreen wasn't found)" : "(Screen > InsideGameUi is off)"));
            }
            _canvas.AddComponent<GraphicRaycaster>();

            var root = Ui.Rect(_canvas.transform, "Panel", Vector2.zero, Vector2.one,
                new Vector2(0, ProgressionPlugin.BottomMargin.Value), new Vector2(0, -ProgressionPlugin.TopMargin.Value));
            Ui.Img(root, Ui.Hex("#0a0f12", ProgressionPlugin.Opacity.Value), null, true); // blocks clicks to the menu underneath
            // Arena-style colour bloom: red, strongest on the right edge and fading out to the left;
            // it always glows a little and flares up while the picked level is still locked
            _bloom.Clear(); _panelFrames.Clear();
            _bloom.Add(Ui.Img(Ui.Rect(root, "Bloom", new Vector2(.25f, 0), Vector2.one, Vector2.zero, Vector2.zero), new Color(0, 0, 0, 0), Ui.HorizontalFade()));
            _bloom.Add(Ui.Img(Ui.Box(root, "BloomCore", new Vector2(1, .5f), Vector2.zero, new Vector2(1400, 2200)), new Color(0, 0, 0, 0), Ui.Radial()));
            _mood = -1; // forces the first ApplyMood to paint

            var top = Ui.Rect(root, "Top", new Vector2(0, .33f), Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(top);
            BuildHeader(top);
            BuildUnlocks(top);
            BuildFeatured(top);
            _bottom = Ui.Rect(root, "Bottom", Vector2.zero, new Vector2(1, .33f), Vector2.zero, Vector2.zero);
            SubCanvas(_bottom);
            BuildBottom(_bottom);

            // vignette over everything (its own canvas, so it draws last), and the whole screen fades in on open
            var vig = Ui.Rect(root, "Vignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(vig);
            Ui.Img(vig, new Color(0, 0, 0, .7f), Ui.Vignette()).raycastTarget = false;
            _fade = root.gameObject.AddComponent<CanvasGroup>();

            if (!_changedHooked)
            {
                _changedHooked = true;
                ProgData.Changed += () => { if (IsOpen) { L.Debug("level list changed — redrawing"); ShowPage(_page, 0); ShowLevel(_level); } };
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
        private static bool Perf => ProgressionPlugin.PerformanceMode.Value;
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
            _headBadge = new Badge(top, new Vector2(0, 1), new Vector2(78, -58), 76);
            Ui.Label(Ui.Rect(top, "Title", new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(128, -58), new Vector2(0, -18)), "Text", "PROGRESSION", 34, Text, TextAnchor.MiddleLeft, false, 1);
            _headSub = Ui.Label(Ui.Rect(top, "Sub", new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(130, -86), new Vector2(0, -60)), "Text", "", 16, Grey, TextAnchor.MiddleLeft, false, 1);

            BuildXp(top);

            // the Tarkov logo (eft-logo.png next to the plugin)
            var logo = Ui.LoadPng(Path.Combine(Path.GetDirectoryName(typeof(ProgScreen).Assembly.Location) ?? Paths.PluginPath, "eft-logo.png"));
            if (logo != null) Ui.Img(Ui.Box(top, "Logo", new Vector2(1, 1), new Vector2(-150, -54), new Vector2(230, 80)), Color.white, logo).preserveAspect = true;

        }

        private static Component _xpLevel, _xpText, _xpNext;
        private static RectTransform _xpFill;
        private static Image _xpSquare;
        private static RectTransform _xpTag;

        /// <summary>Arena-style player block over the centre panel: [61] ▕████░░░░▏ 25 / 1 000 EXP · Next level reward: 2 ◆</summary>
        private static void BuildXp(RectTransform top)
        {
            L.Step("BuildXp");
            var xp = Ui.Rect(top, "Xp", new Vector2(.34f, 1), new Vector2(.74f, 1), new Vector2(16, -106), new Vector2(-8, -12));
            var sq = Ui.Rect(xp, "Level", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -40), new Vector2(80, 40));
            _xpSquare = Ui.Img(sq, Ui.Hex("#e0562f"), Ui.CutCorner());
            _xpLevel = Ui.Label(sq, "Text", "", 32, Color.white, TextAnchor.MiddleCenter, true);
            var right = Ui.Rect(xp, "Right", Vector2.zero, Vector2.one, new Vector2(96, 0), Vector2.zero);
            // the bar: dark frame, thin grey edge, orange fill
            var bar = Ui.Rect(right, "Bar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -26), new Vector2(0, -6));
            Ui.Img(bar, Ui.Hex("#4a5155"));
            var barIn = Ui.Fill(bar, "In", 2);
            Ui.Img(barIn, Ui.Hex("#15191b"));
            _xpFill = Ui.Rect(barIn, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(2, 2), new Vector2(0, -2));
            Ui.Img(_xpFill, Ui.Hex("#e0562f"));
            _xpText = Ui.Label(Ui.Rect(right, "Exp", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -62), new Vector2(0, -30)), "Text", "", 26, Ui.Hex("#b9c0c3"), TextAnchor.MiddleLeft, true, 1);
            // the orange EXP tag right after the numbers (moved to the text's end whenever it changes)
            _xpTag = Ui.Rect(right, "ExpTag", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -55), new Vector2(40, -37));
            Ui.Img(_xpTag, Ui.Hex("#e0562f"));
            Ui.Label(_xpTag, "Text", "EXP", 12, Ui.Hex("#1a1210"), TextAnchor.MiddleCenter, true, 1);
            var next = Ui.Rect(right, "Next", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -88), new Vector2(0, -62));
            _xpNext = Ui.Label(next, "Text", "", 15, Grey, TextAnchor.MiddleLeft, false, 1);
            var hit = Ui.Img(next, new Color(0, 0, 0, 0), null, true);
            var b = next.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { int p = ProgData.PlayerLevel(); if (p > 0 && p < ProgData.MaxLevel) { Sounds.Click(); ShowLevel(p + 1); } });
        }

        private static string Thousands(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

        private static void UpdateXp()
        {
            L.Step("UpdateXp");
            if (_xpLevel == null) return;
            int player = ProgData.PlayerLevel();
            Ui.SetText(_xpLevel, player > 0 ? player.ToString() : "?");
            _xpSquare.color = Ui.Hex("#e0562f");
            float frac = 0;
            if (ProgData.LevelExp(out int have, out int need))
            {
                frac = Mathf.Clamp01(have / (float)need);
                // your XP / needed XP, bold: yours a shade softer, the target bright (Arena)
                string text = $"<color=#b9c0c3>{Thousands(have)}</color><color=#6f777a>/</color><color=#eef2f3>{Thousands(need)}</color>";
                Ui.SetText(_xpText, text);
                float w = Ui.PreferredWidth(_xpText, text);
                _xpTag.gameObject.SetActive(true);
                _xpTag.offsetMin = new Vector2(w + 12, _xpTag.offsetMin.y);
                _xpTag.offsetMax = new Vector2(w + 12 + 40, _xpTag.offsetMax.y);
            }
            else if (player >= ProgData.MaxLevel && player > 0) { _xpTag.gameObject.SetActive(false); Ui.SetText(_xpText, "<color=#e0562f>MAX LEVEL</color>"); }
            else
            {
                // the game's XP table wasn't found: dashes, but the EXP tag stays
                string text = "<color=#6f777a>— / —</color>";
                Ui.SetText(_xpText, text);
                float w = Ui.PreferredWidth(_xpText, text);
                _xpTag.gameObject.SetActive(true);
                _xpTag.offsetMin = new Vector2(w + 12, _xpTag.offsetMin.y);
                _xpTag.offsetMax = new Vector2(w + 12 + 40, _xpTag.offsetMax.y);
            }
            _xpFill.anchorMax = new Vector2(frac, 1);
            int nextCount = player > 0 && player < ProgData.MaxLevel ? ProgData.CountAt(player + 1) : -1;
            Ui.SetText(_xpNext, nextCount < 0 ? "" : $"Next level reward:  <b><color=#e0562f>{nextCount}</color></b> unlock{(nextCount == 1 ? "" : "s")}  <color=#4f575a>·  level {player + 1}</color>");
        }

        private static void BuildUnlocks(RectTransform top)
        {
            // left third: the reward list
            var panel = Panel(top, "Unlocks", new Vector2(0, 0), new Vector2(.34f, 1), new Vector2(40, 18), new Vector2(0, -116));
            _listTitle = Ui.Label(Ui.Rect(panel, "Title", new Vector2(0, 1), Vector2.one, new Vector2(18, -46), new Vector2(-18, -8)), "Text", "UNLOCKS", 26, Text, TextAnchor.MiddleLeft, false, .5f);
            _listCount = Ui.Label(Ui.Rect(panel, "Count", new Vector2(0, 1), Vector2.one, new Vector2(18, -46), new Vector2(-18, -8)), "Text", "", 15, Grey, TextAnchor.MiddleRight, false, 1);
            Ui.Img(Ui.Rect(panel, "Rule", new Vector2(0, 1), Vector2.one, new Vector2(0, -52), new Vector2(0, -51)), Border);

            var view = Ui.Rect(panel, "Scroll", Vector2.zero, Vector2.one, new Vector2(12, 8), new Vector2(-6, -58));
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
            vl.spacing = 10; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
            vl.padding = new RectOffset(0, 8, 4, 10);
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = _content;
        }

        private static void BuildFeatured(RectTransform top)
        {
            // centre: the item, big, on a soft floor light; ITEM / CATEGORY / UNLOCKS AT in small grey at the bottom-left
            var stage = Panel(top, "Stage", new Vector2(.34f, 0), new Vector2(.74f, 1), new Vector2(16, 18), new Vector2(-8, -116));
            // the same preview box as the cards' pictures, big and square: thin frame, dark face, soft light, faded edges, lock
            var area = Ui.Rect(stage, "PicArea", Vector2.zero, Vector2.one, new Vector2(24, 100), new Vector2(-24, -24));
            var picBox = Ui.Fill(area, "Box");
            var fit = picBox.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1;
            Ui.Img(picBox, Ui.Hex("#2a3134"));
            var picFace = Ui.Fill(picBox, "Face", 2);
            Ui.Img(picFace, Ui.Hex("#0f1315"));
            Ui.Img(Ui.Fill(picFace, "Light"), new Color(1, 1, 1, .06f), Ui.Radial());
            _featPic = Ui.Img(Ui.Fill(picFace, "Pic", 36), Color.white);
            _featPic.preserveAspect = true;
            _featPic.enabled = false;
            _featShort = Ui.Label(Ui.Fill(picFace, "Short", 36), "Text", "", 34, Dim, TextAnchor.MiddleCenter, false, 1, true);
            Ui.EdgeFade(picFace, .14f, .55f);
            Ui.Grit(picFace, 2, .11f); // worn edges, like the battle pass frames
            _featLock = Ui.Img(Ui.Rect(picFace, "Lock", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-46, 12), new Vector2(-12, 46)), Ui.Hex("#aeb6b9", .85f), Ui.Lock());
            _featLock.enabled = false;
            // ITEM / CATEGORY / UNLOCKS AT: a label column and a value column, so the values line up
            Ui.Label(Ui.Rect(stage, "MetaKeys", new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 14), new Vector2(128, 90)), "Text", "ITEM\nCATEGORY\nUNLOCKS AT", 15, Dim, TextAnchor.LowerLeft, false, 1);
            _featMeta = Ui.Label(Ui.Rect(stage, "Meta", new Vector2(0, 0), new Vector2(1, 0), new Vector2(132, 14), new Vector2(-18, 90)), "Text", "", 15, Ui.Hex("#9aa2a5"), TextAnchor.LowerLeft, false, 1);

            // right: the item's details, like the battle pass reward panel
            var side = Panel(top, "Details", new Vector2(.74f, 0), new Vector2(1, 1), new Vector2(8, 18), new Vector2(-40, -116));
            _featType = Ui.Label(Ui.Rect(side, "Type", new Vector2(0, 1), Vector2.one, new Vector2(18, -46), new Vector2(-18, -8)), "Text", "", 26, Text, TextAnchor.MiddleLeft, false, .5f);
            Ui.Img(Ui.Rect(side, "Rule", new Vector2(0, 1), Vector2.one, new Vector2(0, -52), new Vector2(0, -51)), Border);
            _featName = Ui.Label(Ui.Rect(side, "Name", new Vector2(0, 1), Vector2.one, new Vector2(18, -120), new Vector2(-18, -64)), "Text", "", 20, Text, TextAnchor.UpperLeft, false, 0, true);
            Ui.SetWrap(_featName, true);
            // requirement row: [ ] Reach level 6        2 / 6   (red while not met, like the Arena battle pass)
            var req = Ui.Rect(side, "Req", new Vector2(0, 1), Vector2.one, new Vector2(18, -160), new Vector2(-18, -130));
            var box = Ui.Rect(req, "Box", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -8), new Vector2(16, 8));
            Ui.Img(box, Grey);
            Ui.Img(Ui.Fill(box, "In", 2), Ui.Hex("#10161a"));
            _featCheck = Ui.Img(Ui.Fill(box, "Check", 4), Ui.Hex(Green));
            _featReq = Ui.Label(Ui.Rect(req, "Text", Vector2.zero, Vector2.one, new Vector2(26, 0), Vector2.zero), "Text", "", 18, Text, TextAnchor.MiddleLeft, false);
            _featReqValue = Ui.Label(Ui.Rect(req, "Value", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), "Text", "", 17, Text, TextAnchor.MiddleRight, true);
            _featStatus = Ui.Label(Ui.Rect(side, "Status", new Vector2(0, 1), Vector2.one, new Vector2(18, -196), new Vector2(-18, -168)), "Text", "", 15, Grey, TextAnchor.MiddleLeft, false);
            // details (size, weight, damage…) and the game's description: the space the requirement doesn't need
            Ui.Label(Ui.Rect(side, "DetHead", new Vector2(0, 1), Vector2.one, new Vector2(18, -232), new Vector2(-18, -208)), "Text", "DETAILS", 13, Dim, TextAnchor.MiddleLeft, true, 2);
            Ui.Img(Ui.Rect(side, "DetRule", new Vector2(0, 1), Vector2.one, new Vector2(18, -234), new Vector2(-18, -233)), Border);
            _featFacts = Ui.Label(Ui.Rect(side, "Facts", new Vector2(0, 1), new Vector2(.62f, 1), new Vector2(18, -380), new Vector2(0, -242)), "Text", "", 15, Grey, TextAnchor.UpperLeft, false);
            _featFactValues = Ui.Label(Ui.Rect(side, "FactValues", new Vector2(.4f, 1), Vector2.one, new Vector2(0, -380), new Vector2(-18, -242)), "Text", "", 15, Text, TextAnchor.UpperRight, true);
            _featDesc = Ui.Label(Ui.Rect(side, "Desc", new Vector2(0, 0), Vector2.one, new Vector2(18, 100), new Vector2(-18, -392)), "Text", "", 14, Grey, TextAnchor.UpperLeft, false, 0, true);
            Ui.SetWrap(_featDesc, true);

            // INSPECT (like CLAIM REWARD) and the hint under it
            var btn = Ui.Rect(side, "Inspect", new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 44), new Vector2(-18, 84));
            var bimg = Ui.Img(btn, Ui.Hex("#34464c"), null, true);
            Ui.Label(btn, "Text", "INSPECT", 17, Text, TextAnchor.MiddleCenter, false, 1);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = bimg;
            var colors = b.colors; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f); b.colors = colors;
            b.onClick.AddListener(() => { if (_featTpl != null) Inspect(_featTpl); });
            Ui.Label(Ui.Rect(side, "Hint", new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 14), new Vector2(-18, 38)), "Text", "<i>or right-click any item</i>", 13, Dim, TextAnchor.MiddleCenter, false);
        }

        private static void BuildBottom(RectTransform bottom)
        {
            _prev = Arrow(bottom, "Prev", "‹", 0, () => ShowPage(_page - 1, -1));
            _next = Arrow(bottom, "Next", "›", 1, () => ShowPage(_page + 1, 1));

            var cards = Ui.Rect(bottom, "Cards", Vector2.zero, Vector2.one, new Vector2(96, 78), new Vector2(-96, -14));
            for (int i = 0; i < PerPage; i++)
            {
                float a = i / (float)PerPage, b = (i + 1) / (float)PerPage;
                var slot = Ui.Rect(cards, "Slot" + i, new Vector2(a, 0), new Vector2(b, 1), new Vector2(6, 0), new Vector2(-6, 0));
                _cards[i] = new Card(slot);
            }

            PerfToggle(bottom);

            // page bar like the Arena battle pass: [Q] ▬▬▬▬ … [E], the page numbers under the segments
            var bar = Ui.Rect(bottom, "Pages", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-560, 14), new Vector2(560, 60));
            KeyBox(bar, "Q", 0, () => ShowPage(_page - 1, -1));
            KeyBox(bar, "E", 1, () => ShowPage(_page + 1, 1));
            float x0 = 40, w = (1120 - 80) / (float)Pages;
            for (int i = 0; i < Pages; i++)
            {
                int page = i;
                var seg = Ui.Rect(bar, "Seg" + i, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0 + i * w + 2, -22), new Vector2(x0 + (i + 1) * w - 2, -12));
                var img = Ui.Img(seg, Ui.Hex("#2c3336"), null, true);
                var hit = Ui.Rect(bar, "Hit" + i, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x0 + i * w, 0), new Vector2(x0 + (i + 1) * w, 0));
                var hitImg = Ui.Img(hit, new Color(0, 0, 0, 0), null, true);
                var btn = hit.gameObject.AddComponent<Button>();
                btn.targetGraphic = hitImg;
                btn.onClick.AddListener(() => ShowPage(page, page > _page ? 1 : -1));
                _segments.Add(img);
                // thin divider between the page numbers, like Arena
                if (i > 0) Ui.Img(Ui.Rect(bar, "Div" + i, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x0 + i * w - 1, 4), new Vector2(x0 + i * w + 1, 22)), Ui.Hex("#3a4245"));
                _segmentNums.Add(Ui.Label(Ui.Rect(bar, "Num" + i, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x0 + i * w, 0), new Vector2(x0 + (i + 1) * w, 24)), "Text", (i + 1).ToString(), 15, Dim, TextAnchor.MiddleCenter, false));
            }
        }

        private static Image _perfCheck;

        /// <summary>[x] Performance mode — bottom-right, next to the page bar.</summary>
        private static void PerfToggle(RectTransform bottom)
        {
            var rt = Ui.Rect(bottom, "Perf", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-260, 22), new Vector2(-40, 50));
            var hit = Ui.Img(rt, new Color(0, 0, 0, 0), null, true);
            var box = Ui.Rect(rt, "Box", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -9), new Vector2(18, 9));
            Ui.Img(box, Ui.Hex("#5a6468"));
            Ui.Img(Ui.Fill(box, "In", 2), Ui.Hex("#161c1f"));
            _perfCheck = Ui.Img(Ui.Fill(box, "Check", 5), Ui.Hex(Orange));
            _perfCheck.enabled = Perf;
            Ui.Label(Ui.Rect(rt, "Text", Vector2.zero, Vector2.one, new Vector2(28, 0), Vector2.zero), "Text", "Performance mode", 15, Grey, TextAnchor.MiddleLeft, true, 1);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                ProgressionPlugin.PerformanceMode.Value = !Perf;
                _perfCheck.enabled = Perf;
                Sounds.Click();
                L.Info("performance mode " + (Perf ? "on" : "off"));
                ShowPage(_page, 0);
                ShowLevel(_level);
            });
        }

        private static void KeyBox(RectTransform parent, string key, float side, Action click)
        {
            // a small keycap, centred on the segments: grey 2 px rim, dark face, bold letter
            var rt = Ui.Rect(parent, "Key" + key, new Vector2(side, 1), new Vector2(side, 1), new Vector2(side == 0 ? 4 : -30, -30), new Vector2(side == 0 ? 30 : -4, -4));
            Ui.Img(rt, Ui.Hex("#5a6468"));
            var img = Ui.Img(Ui.Fill(rt, "In", 2), Ui.Hex("#161c1f"), null, true);
            Ui.Img(Ui.Rect(rt, "Shine", new Vector2(0, 1), Vector2.one, new Vector2(2, -4), new Vector2(-2, -2)), new Color(1, 1, 1, .08f));
            Ui.Label(rt, "Text", key, 15, Ui.Hex("#c3ccd0"), TextAnchor.MiddleCenter, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => click());
        }

        private static Button Arrow(RectTransform parent, string name, string glyph, float side, Action click)
        {
            var rt = Ui.Rect(parent, name, new Vector2(side, 0), new Vector2(side, 1), new Vector2(side == 0 ? 36 : -84, 78), new Vector2(side == 0 ? 84 : -36, -14));
            var img = Ui.Img(rt, new Color(1, 1, 1, 0), null, true);
            Ui.Label(rt, "Glyph", glyph, 60, Accent, TextAnchor.MiddleCenter, false);
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
        private static float _menuAlpha = 1;
        private static bool _menuRaycasts = true;

        private static GameObject MenuScreen() => GameObject.Find("Common UI/Common UI/MenuScreen");

        /// <summary>While the screen is open the main menu (ESCAPE FROM TARKOV, CHARACTER, TRADING, EXIT, the beta
        /// warning…) is faded out and not clickable, like the game's battle pass does. Restored on close.</summary>
        private static void HideMenu(bool hide)
        {
            try
            {
                if (!ProgressionPlugin.HideMainMenu.Value && hide) return;
                var menu = MenuScreen();
                if (menu == null) { if (hide) L.Debug("main menu (Common UI/Common UI/MenuScreen) not found — nothing hidden"); return; }
                if (hide)
                {
                    _menuGroup = menu.GetComponent<CanvasGroup>() ?? menu.AddComponent<CanvasGroup>();
                    _menuAlpha = _menuGroup.alpha; _menuRaycasts = _menuGroup.blocksRaycasts;
                    _menuGroup.alpha = 0; _menuGroup.blocksRaycasts = false;
                    L.Debug("main menu hidden");
                }
                else if (_menuGroup != null)
                {
                    _menuGroup.alpha = _menuAlpha; _menuGroup.blocksRaycasts = _menuRaycasts;
                    _menuGroup = null;
                    L.Debug("main menu shown again");
                }
            }
            catch (Exception e) { L.ErrorOnce("hiding the main menu", e); }
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
            if (changed && dir != 0) _level = levelAfter > 0 ? levelAfter : page * PerPage + 1;
            int first = page * PerPage + 1;
            L.Debug($"page {page + 1}/{Pages} (levels {first}–{Mathf.Min(ProgData.MaxLevel, first + PerPage - 1)}){(dir != 0 ? " slide " + (dir > 0 ? "right" : "left") : "")}");
            _prev.interactable = page > 0;
            _next.interactable = page < Pages - 1;
            UpdatePageBar();
            _hits.RemoveAll(h => h.Rect == null || h.Rect.IsChildOf(_bottom));
            _cardIcons.Clear(); DropRequests(_cardIcons);
            for (int i = 0; i < PerPage; i++) _cards[i].Show(first + i);
            _cardsDir = dir;
            _cardsStart = dir != 0 ? Time.unscaledTime : -10;
            if (changed && dir != 0) { Sounds.Page(); ShowLevel(_level); }
            else UpdateSelection();
        }

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
                _segments[i].color = done ? (cur ? Ui.Hex("#e8eef0") : Ui.Hex("#b4bec2")) : (cur ? Ui.Hex("#3a4346") : Ui.Hex("#262d30"));
                var rt = _segments[i].rectTransform;
                rt.offsetMin = new Vector2(rt.offsetMin.x, -22);
                rt.offsetMax = new Vector2(rt.offsetMax.x, cur ? -8 : -12);
                Ui.SetColor(_segmentNums[i], cur ? (done ? Color.white : Grey) : done ? Ui.Hex("#b4bec2") : Dim);
                Ui.SetText(_segmentNums[i], cur ? $"<b>{i + 1}</b>" : (i + 1).ToString());
            }
        }

        private static void ShowLevel(int level)
        {
            L.Step("ShowLevel " + level);
            level = Mathf.Clamp(level, 1, ProgData.MaxLevel);
            int page = (level - 1) / PerPage;
            _level = level;
            if (page != _page) { ShowPage(page, page > _page ? 1 : -1, level); return; }
            float t0 = Time.realtimeSinceStartup;
            var items = ProgData.ItemsAt(level);
            int player = ProgData.PlayerLevel();

            // header
            _headBadge.Set(level);
            string state = player <= 0 ? "" : level < player ? $"<color={Green}>Unlocked</color>" : level == player ? $"<color={Yellow}>Current level</color>"
                : level == player + 1 ? "<color=#7fb3a8>Next level</color>" : $"<color={Red}>Locked</color>";
            Ui.SetText(_headSub, $"Level {level}  ·  {TierOf(level).Name}  ·  {items.Count} unlock{(items.Count == 1 ? "" : "s")}" + (state != "" ? "  ·  " + state : ""));
            SetMood(player > 0 && level > player);
            UpdateXp();

            // the reward list
            var groups = ProgData.Groups.Select(g => (g, list: items.Where(it => it.Group == g.Key).ToList())).Where(x => x.list.Count > 0).ToList();
            Ui.SetText(_listTitle, $"UNLOCKS  <color=#7d8588>·  LEVEL {level}</color>");
            Ui.SetText(_listCount, level <= player || player <= 0 ? $"{items.Count}" : $"<color={Red}>{items.Count}  LOCKED</color>");

            foreach (Transform ch in _content) UnityEngine.Object.Destroy(ch.gameObject);
            _tiles.Clear();
            _tileFrames.Clear();
            _icons.Clear(); DropRequests(_icons);
            _hits.RemoveAll(h => h.Rect == null || !h.Rect.IsChildOf(_bottom));
            Feature(items.FirstOrDefault());
            int max = Mathf.Max(1, Perf ? Mathf.Min(12, ProgressionPlugin.MaxTilesPerCategory.Value) : ProgressionPlugin.MaxTilesPerCategory.Value), n = 0;
            foreach (var (g, list) in groups)
            {
                var color = Ui.Hex(g.Color);
                var section = Ui.Rect(_content, "Cat_" + g.Key, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var sl = section.gameObject.AddComponent<VerticalLayoutGroup>();
                sl.spacing = 6; sl.childControlHeight = true; sl.childControlWidth = true; sl.childForceExpandHeight = false; sl.childForceExpandWidth = true;

                var head = Ui.Rect(section, "Head", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                head.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;
                Ui.Label(Ui.Rect(head, "Name", Vector2.zero, Vector2.one, new Vector2(2, 0), Vector2.zero), "Text",
                    $"{g.Name}  <color=#7d8588>{list.Count}{(list.Count > max ? $" (showing {max})" : "")}</color>", 15, Text, TextAnchor.MiddleLeft, false, 1);
                Ui.Img(Ui.Rect(head, "Rule", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), Border);

                var grid = Ui.Rect(section, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
                float cell = Mathf.Clamp(ProgressionPlugin.TileSize.Value, 60, 200);
                gl.cellSize = new Vector2(cell * 1.1f, cell);
                gl.spacing = new Vector2(5, 5);
                gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
                gl.constraint = GridLayoutGroup.Constraint.Flexible;
                foreach (var it in list.Take(max)) Tile(grid, it, color, level <= player || player <= 0, n++);
            }
            if (groups.Count == 0)
            {
                var empty = Ui.Rect(_content, "Empty", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
                Ui.Label(empty, "Text", $"Nothing unlocks at level {level}\n<size=13><color=#7d8588>Set items to this level in the Level & Item Editor.</color></size>", 17, Text, TextAnchor.MiddleCenter, false);
            }
            _content.anchoredPosition = Vector2.zero;
            _tilesStart = Time.unscaledTime;
            UpdateSelection();
            L.Debug($"level {level}: {items.Count} item(s) in {groups.Count} categories, {n} tiles drawn in {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
        }

        /// <summary>A reward-list tile: dark cell, thin frame, the game's picture, the short name under it.</summary>
        private static void Tile(RectTransform grid, ProgItem it, Color color, bool reached, int index)
        {
            var rt = Ui.Rect(grid, "Tile", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            var inner = Ui.Fill(rt, "Inner");
            _tileFrames[it.Tpl] = Ui.Img(inner, it.Tpl == _featTpl ? Accent : Border);
            var slot = Ui.Rect(inner, "Slot", new Vector2(0, .22f), Vector2.one, new Vector2(1, 0), new Vector2(-1, -1));
            Ui.Img(slot, Ui.Hex("#12181b"));
            Ui.Img(Ui.Rect(slot, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, .03f), Ui.VerticalFade());
            Ui.Img(Ui.Rect(inner, "Strip", new Vector2(0, .22f), new Vector2(1, .22f), new Vector2(1, 0), new Vector2(-1, 1)), new Color(color.r, color.g, color.b, .55f));
            Ui.Img(Ui.Rect(inner, "Label", Vector2.zero, new Vector2(1, .22f), new Vector2(1, 1), new Vector2(-1, 0)), Ui.Hex("#0c1113"));
            var shortName = Ui.Label(Ui.Rect(slot, "Short", Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0)), "Text", it.Short, 12, Dim, TextAnchor.MiddleCenter, false, 0, true);
            Ui.Label(Ui.Rect(inner, "Name", Vector2.zero, new Vector2(1, .22f), new Vector2(5, 0), new Vector2(-5, 0)), "Text", it.Short, 11, Ui.Hex("#b9bfbc"), TextAnchor.MiddleLeft, false, 0, true);
            var pic = Ui.Img(Ui.Fill(slot, "Icon", 5), Color.white);
            pic.preserveAspect = true;
            pic.enabled = false;
            RequestIcon(_icons, it.Tpl, 1, pic, shortName);
            _hits.Add((rt, it));
            if (!reached) Ui.Img(Ui.Rect(slot, "Lock", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-9, -9), new Vector2(-4, -4)), Ui.Hex(Red));
            _tiles.Add((group, inner, Mathf.Min(index, 40) * .012f));
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
                r.Target.Add((GameItems.IconOf(GameItems.ItemOf(r.Tpl), r.Scale), r.Pic, r.Placeholder, r.Tpl));
            }
        }

        private static void ShowIcons(List<(object Icon, Image Pic, Component Placeholder, string Tpl)> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var (icon, pic, placeholder, tpl) = list[i];
                if (pic == null) { list.RemoveAt(i); continue; }
                var sprite = GameItems.SpriteOf(icon);
                if (sprite == null) continue;
                pic.sprite = sprite;
                pic.enabled = true;
                if (placeholder != null) placeholder.gameObject.SetActive(false);
                list.RemoveAt(i);
                if (_iconsShown++ == 0) L.Info($"first item icon shown ({tpl}, {sprite.rect.width:0}x{sprite.rect.height:0} px)");
            }
        }

        /// <summary>The item in the centre and its details on the right.</summary>
        private static void Feature(ProgItem it)
        {
            L.Step("Feature " + it?.Tpl);
            _featTpl = it?.Tpl;
            _featPic.enabled = false;
            _featShort.gameObject.SetActive(it != null);
            if (it == null)
            {
                foreach (var c in new[] { _featMeta, _featType, _featName, _featReq, _featReqValue, _featStatus, _featFacts, _featFactValues, _featDesc }) Ui.SetText(c, "");
                _featCheck.enabled = false;
                _featIcon = null;
                return;
            }
            var g = ProgData.Groups.FirstOrDefault(x => x.Key == it.Group);
            int player = ProgData.PlayerLevel();
            bool met = player <= 0 || it.Level <= player;
            Ui.SetText(_featShort, it.Short);
            Ui.SetText(_featMeta, $"{it.Short}\n{g.Name}\nLevel {it.Level}");
            Ui.SetText(_featType, (g.Name ?? "Item").ToUpperInvariant());
            Ui.SetText(_featName, it.Name);
            _featCheck.enabled = met;
            string col = met ? "#d5d9d6" : Red;
            Ui.SetText(_featReq, $"<color={col}>Reach level {it.Level}</color>");
            Ui.SetText(_featReqValue, player > 0 ? $"<color={col}>{Mathf.Min(player, it.Level)} / {it.Level}</color>" : "");
            Ui.SetText(_featStatus, player <= 0 ? "" : it.Level == player ? $"<color={Yellow}>Current level</color> <color=#7d8588>— you can use it</color>"
                : met ? $"<color={Green}>Unlocked</color> <color=#7d8588>— you can use it</color>" : $"<color={Red}>Locked</color> <color=#7d8588>· {it.Level - player} level{(it.Level - player == 1 ? "" : "s")} to go</color>");
            var facts = GameItems.Facts(it.Tpl);
            facts.Insert(0, ("Category", g.Name));
            Ui.SetText(_featFacts, string.Join("\n", facts.Select(f => f.Label).ToArray()));
            Ui.SetText(_featFactValues, string.Join("\n", facts.Select(f => f.Value).ToArray()));
            var desc = ProgData.DescriptionOf(it.Tpl);
            Ui.SetText(_featDesc, desc.Length > 520 ? desc.Substring(0, 520).TrimEnd() + "…" : desc);
            int you = ProgData.PlayerLevel();
            _featLock.enabled = you > 0 && it.Level > you;
            _featIcon = GameItems.IconOf(GameItems.ItemOf(it.Tpl), Perf ? 2 : 6);
            MarkSelectedTile();
        }

        private static readonly Dictionary<string, Image> _tileFrames = new Dictionary<string, Image>();

        private static void MarkSelectedTile()
        {
            foreach (var kv in _tileFrames) if (kv.Value != null) kv.Value.color = kv.Key == _featTpl ? Accent : Border;
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
            if (_bloom.Count > 0 && _bloom[0] != null) _bloom[0].color = new Color(.85f, .14f, .08f, Mathf.Lerp(.22f, .27f, m));
            if (_bloom.Count > 1 && _bloom[1] != null) _bloom[1].color = new Color(.95f, .2f, .1f, Mathf.Lerp(.18f, .22f, m));
            var border = Color.Lerp(Border, Ui.Hex("#6a2b25", .95f), m);
            foreach (var f in _panelFrames) if (f != null) f.color = border;
        }

        private static void UpdateSelection()
        {
            int player = ProgData.PlayerLevel();
            foreach (var c in _cards) c.Mark(_level, player);
        }

        // ---------------------------------------------------------------- every frame

        private static float _restoreAgainAt = -1, _openedAt = -10;
        private static CanvasGroup _fade;
        private static bool _xpKnown;

        public static void Tick()
        {
            if (_restoreAgainAt > 0 && Time.unscaledTime > _restoreAgainAt && !IsOpen) { _restoreAgainAt = -1; GameItems.RestoreIcons(); }
            if (!IsOpen) return;
            if (!_xpKnown && ProgData.HasExpTable) { _xpKnown = true; UpdateXp(); } // the SPT server's answer came in
            if (_fade != null && _fade.alpha < 1)
            {
                float f = Mathf.Clamp01((Time.unscaledTime - _openedAt) / .3f);
                _fade.alpha = 1 - (1 - f) * (1 - f); // ease out
            }
            var input = UnityInput.Current;
            bool window = GameWindowOpen();
            // the game may close its window on this same Esc before we look: a window seen a moment ago still owns the key
            if (window) _windowSeenAt = Time.unscaledTime;
            bool windowRecently = window || Time.unscaledTime - _windowSeenAt < .3f;
            if (!windowRecently)
            {
                if (input.GetKeyDown(KeyCode.RightArrow)) { ShowLevel(_level + 1); Sounds.Click(); }
                else if (input.GetKeyDown(KeyCode.LeftArrow)) { ShowLevel(_level - 1); Sounds.Click(); }
                else if (input.GetKeyDown(KeyCode.E) || input.GetKeyDown(KeyCode.PageDown)) ShowPage(_page + 1, 1);
                else if (input.GetKeyDown(KeyCode.Q) || input.GetKeyDown(KeyCode.PageUp)) ShowPage(_page - 1, -1);
                else if (input.GetKeyDown(KeyCode.Home)) ShowLevel(1);
                else if (input.GetKeyDown(KeyCode.End)) ShowLevel(ProgData.MaxLevel);
                // Esc closes us only when no game window (inspect…) is open — otherwise it's the window's Esc
                else if (input.GetKeyDown(KeyCode.Escape)) { Close("Escape"); return; }
            }
            else if (input.GetKeyDown(KeyCode.Escape)) L.Debug("Esc with a game window open: left to the window");

            float now = Time.unscaledTime;
            float wheel = input.mouseScrollDelta.y;
            if (!window && Mathf.Abs(wheel) > .01f && now - _wheelAt > .3f && MenuHook.Contains(_bottom, input.mousePosition))
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
            RunIconRequests();
            ShowIcons(_icons);
            ShowIcons(_cardIcons);
            ApplyMood(Time.unscaledDeltaTime);

            // mouse over an item: feature it; right-click: the game's inspect (checked here: the game's input
            // doesn't send right-clicks to our tiles)
            if (!window)
            {
                var mouse = (Vector2)input.mousePosition;
                ProgItem over = null;
                foreach (var (rect, item) in _hits)
                    if (rect != null && rect.gameObject.activeInHierarchy && MenuHook.Contains(rect, mouse)) { over = item; break; }
                if (input.GetMouseButtonDown(0) && over != null && over.Tpl != _featTpl) { Sounds.Play("MenuContextMenu", "ButtonClick"); Feature(over); }
                if (input.GetMouseButtonDown(1))
                {
                    L.Debug($"right-click at {mouse} over {(over == null ? "nothing" : over.Name + " (" + over.Tpl + ")")}");
                    if (over != null) Inspect(over.Tpl);
                }
            }
            if (_featIcon != null && !_featPic.enabled)
            {
                var sp = GameItems.SpriteOf(_featIcon);
                if (sp != null)
                {
                    _featPic.sprite = sp; _featPic.enabled = true; _featShort.gameObject.SetActive(false);
                    if (!_featIconLogged) { _featIconLogged = true; L.Info($"big picture: {sp.rect.width:0}x{sp.rect.height:0} px (asked the game for 6x)"); }
                }
            }
            if (_windowLogAt > 0 && now > _windowLogAt) { _windowLogAt = 0; LogWindows(); }

            // how smooth it runs while open (every 5 s in the log)
            _frames++; _frameTime += Time.unscaledDeltaTime;
            // stutter finder: a slow frame is logged with the last step that ran before it
            if (Time.unscaledDeltaTime > .06f && _frames > 2) L.Debug($"slow frame: {Time.unscaledDeltaTime * 1000:0} ms (after: {L.LastStep})");
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
            private readonly Image _frame, _bg, _glow;
            private readonly DottedLine _headL, _headR;
            private readonly Component _head, _count, _more, _state, _tier;
            private readonly Image[] _pics = new Image[3];
            private readonly Image[] _locks = new Image[3];
            private readonly Component[] _picNames = new Component[3];
            private readonly RectTransform[] _picRects = new RectTransform[3];
            private readonly Badge _badge;
            private int _level;

            /// <summary>A square picture slot, as big as fits in the given area (so icons keep their shape at any screen size).</summary>
            private static RectTransform Square(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
            {
                var area = Ui.Rect(parent, name, aMin, aMax, oMin, oMax);
                var sq = Ui.Fill(area, "Square");
                var fit = sq.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = 1;
                return sq;
            }

            public Card(RectTransform slot)
            {
                _body = Ui.Fill(slot, "Card");
                _group = _body.gameObject.AddComponent<CanvasGroup>();
                // |------ Level 6 ------|  dots bright by the label, fading out toward the ticks at the card's edges (Arena)
                var head = Ui.Rect(_body, "Head", new Vector2(0, 1), Vector2.one, new Vector2(0, -24), Vector2.zero);
                _head = Ui.Label(head, "Text", "", 17, Grey, TextAnchor.MiddleCenter, true);
                _headL = DottedLine.Add(Ui.Rect(head, "L", new Vector2(0, 0), new Vector2(.5f, 1), Vector2.zero, new Vector2(-46, 0)), false);
                _headR = DottedLine.Add(Ui.Rect(head, "R", new Vector2(.5f, 0), new Vector2(1, 1), new Vector2(46, 0), Vector2.zero), true);

                var card = Ui.Rect(_body, "Box", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -32));
                _frame = Ui.Img(card, Border);
                var inner = Ui.Fill(card, "In", BorderWidth);
                _bg = Ui.Img(inner, Ui.Hex("#12181b", .92f), null, true);
                // Arena-style warm glow in the top-right corner of the picked card
                var glow = Ui.Box(inner, "Glow", new Vector2(1, 1), Vector2.zero, new Vector2(260, 260));
                _glow = Ui.Img(glow, new Color(0, 0, 0, 0), Ui.Radial());
                inner.gameObject.AddComponent<RectMask2D>();
                // header row: small badge + rank title, tight to the top-left
                // one spacing unit (Pad) everywhere: card edge → badge, badge → title, header → pictures, pictures → bottom row
                // (the badge's diamond is ~B wide corner to corner, so its box sits exactly Pad from the edges)
                const float Pad = 12, B = 50, Head = Pad + B + Pad, Foot = 34;
                _badge = new Badge(inner, new Vector2(0, 1), new Vector2(Pad + B / 2, -(Pad + B / 2)), B);
                _tier = Ui.Label(Ui.Rect(inner, "Tier", new Vector2(0, 1), Vector2.one, new Vector2(Pad + B + Pad, -(Pad + B)), new Vector2(-Pad, -Pad)), "Text", "", 17, Text, TextAnchor.MiddleLeft, true, 1.5f);
                // one big square picture (the level's top category) and two small ones stacked beside it
                // the pictures sit in the area between header and footer; each is kept square
                var pics = Ui.Rect(inner, "Pics", Vector2.zero, Vector2.one, new Vector2(Pad, Foot), new Vector2(-Pad, -Head));
                _picRects[0] = Square(pics, "Pic0", new Vector2(0, 0), new Vector2(.58f, 1), Vector2.zero, new Vector2(-Pad / 2, 0));
                _picRects[1] = Square(pics, "Pic1", new Vector2(.58f, .5f), new Vector2(.86f, 1), new Vector2(Pad / 2, Pad / 4), Vector2.zero);
                _picRects[2] = Square(pics, "Pic2", new Vector2(.58f, 0), new Vector2(.86f, .5f), new Vector2(Pad / 2, 0), new Vector2(0, -Pad / 4));
                for (int i = 0; i < 3; i++)
                {
                    // Arena item preview: thin frame, dark face, the item, a soft dark fade around the edges, lock bottom-right
                    var picSlot = _picRects[i];
                    Ui.Img(picSlot, Ui.Hex("#2a3134"));
                    var face = Ui.Fill(picSlot, "Face", 2);
                    Ui.Img(face, Ui.Hex("#0f1315"));
                    Ui.Img(Ui.Fill(face, "Light"), new Color(1, 1, 1, .05f), Ui.Radial());
                    _picNames[i] = Ui.Label(face, "Name", "", 11, Grey, TextAnchor.MiddleCenter, false, 0, true);
                    _pics[i] = Ui.Img(Ui.Fill(face, "Img", i == 0 ? 8 : 4), Color.white);
                    _pics[i].preserveAspect = true;
                    _pics[i].enabled = false;
                    Ui.EdgeFade(face, .18f, .55f);
                    Ui.Grit(face, _gritSeed++, .13f);
                    float ls = i == 0 ? 22 : 16;
                    _locks[i] = Ui.Img(Ui.Rect(face, "Lock", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-ls - 6, 6), new Vector2(-6, ls + 6)), Ui.Hex("#aeb6b9", .85f), Ui.Lock());
                }
                _more = Ui.Label(Ui.Rect(inner, "More", new Vector2(.86f, 0), new Vector2(1, 1), new Vector2(Pad, Foot), new Vector2(-Pad, -Head)), "Text", "", 17, Accent, TextAnchor.MiddleCenter, true);
                // bottom row: count left, state right, right under the pictures
                _count = Ui.Label(Ui.Rect(inner, "Count", new Vector2(0, 0), new Vector2(.5f, 0), new Vector2(Pad, 4), new Vector2(0, Foot - 4)), "Text", "", 15, Text, TextAnchor.MiddleLeft, true);
                _state = Ui.Label(Ui.Rect(inner, "State", new Vector2(.5f, 0), new Vector2(1, 0), new Vector2(0, 4), new Vector2(-Pad, Foot - 4)), "Text", "", 15, Grey, TextAnchor.MiddleRight, true);
                var button = inner.gameObject.AddComponent<Button>();
                button.targetGraphic = _bg;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { L.Debug($"card level {_level} clicked"); Sounds.Click(); ShowLevel(_level); });
            }

            public void Show(int level)
            {
                L.Step("card " + level);
                _level = level;
                bool exists = level <= ProgData.MaxLevel;
                _body.gameObject.SetActive(exists);
                if (!exists) return;
                var items = ProgData.ItemsAt(level);
                var picks = CardPicks(items);
                Ui.SetText(_head, "Level " + level);
                _badge.Set(level, items.Count == 0);
                Ui.SetText(_tier, TierOf(level).Name);
                Ui.SetText(_count, items.Count == 0 ? "No unlocks" : $"{items.Count} unlock{(items.Count == 1 ? "" : "s")}");
                for (int i = 0; i < 3; i++)
                {
                    var it = i < picks.Count ? picks[i] : null;
                    _picRects[i].gameObject.SetActive(it != null);
                    _pics[i].enabled = false;
                    if (it == null) continue;
                    Ui.SetText(_picNames[i], it.Short);
                    _picNames[i].gameObject.SetActive(true);
                    RequestIcon(_cardIcons, it.Tpl, i == 0 && !Perf ? 2 : 1, _pics[i], _picNames[i]);
                    _hits.Add((_picRects[i], it));
                }
                Ui.SetText(_more, items.Count > 3 ? "+" + (items.Count - 3) : "");
                _group.alpha = items.Count == 0 ? .55f : 1f;
            }

            public void Mark(int picked, int player)
            {
                bool sel = _level == picked, current = _level == player;
                _frame.color = sel ? Ui.Hex(Orange, .9f) : Border;
                bool unlocked = player > 0 && _level <= player;
                // Arena: unlocked levels carry a warm red hue in the top-right corner; the picked card gets it strongest
                _glow.color = sel ? Ui.Hex(Orange, .32f) : unlocked ? Ui.Hex("#d0453a", .22f) : new Color(0, 0, 0, 0);
                foreach (var l in _locks) l.enabled = !unlocked;
                // a locked level's whole card is darker, so what you have and what you don't reads at a glance
                bool locked = player > 0 && _level > player;
                _bg.color = locked ? (sel ? Ui.Hex("#0e1214", .97f) : Ui.Hex("#090c0e", .97f)) : sel ? Ui.Hex("#171d20", .95f) : Ui.Hex("#12181b", .92f);
                Ui.SetColor(_tier, locked ? Grey : Text);
                // Arena: your current level in orange, the picked one white, the rest grey
                var c = current ? Ui.Hex(Orange) : sel ? Text : Grey;
                Ui.SetColor(_head, c);
                _headL.color = _headR.color = current ? Ui.Hex(Orange) : sel ? Text : Grey;
                string state = player <= 0 ? "" : _level < player ? "<color=#e8765a>Unlocked</color>" : _level == player ? $"<color={Yellow}>Current level</color>"
                    : _level == player + 1 ? "<color=#7fb3a8>Next level</color>" : $"<color={Red}>Locked</color>";
                Ui.SetText(_state, state);
            }

            public void Animate(float e, int dir)
            {
                if (!_body.gameObject.activeSelf) return;
                _body.anchoredPosition = new Vector2(dir == 0 ? 0 : dir * 90f * (1 - e), 0);
                float baseAlpha = ProgData.CountAt(_level) == 0 ? .55f : 1f;
                _group.alpha = baseAlpha * (dir == 0 ? 1 : e);
            }
        }
    }
}
