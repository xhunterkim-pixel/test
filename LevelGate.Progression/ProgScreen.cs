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
    /// The Progression screen: the picked level's unlocks on top (grouped by category),
    /// five level cards per page along the bottom (arrows, Q / E, mouse wheel, page dots,
    /// ← → for one level). Built once in code on its own canvas, shown over the menu.
    /// </summary>
    internal static class ProgScreen
    {
        private const int PerPage = 5;
        private static int Pages => Mathf.CeilToInt(ProgData.MaxLevel / (float)PerPage);
        private static readonly Color Blue = Ui.Hex("#5ab4e6");
        private static readonly Color Panel = Ui.Hex("#0b1016", .97f);

        /// <summary>Rank look per level band: name, rim color, light color.</summary>
        private static readonly (int From, string Name, string Rim, string Light)[] Tiers =
        {
            (1, "Recruit", "#9aa3ad", "#dfe6ee"), (10, "Private", "#8c6b3f", "#e8c27a"), (20, "Corporal", "#9aa3ad", "#f4f7fa"),
            (30, "Sergeant", "#b8901c", "#ffe27a"), (40, "Lieutenant", "#3f7fb8", "#9fd4ff"), (50, "Captain", "#7b4fc9", "#d3b8ff"),
            (60, "Major", "#b8323f", "#ff9aa5"), (70, "Colonel", "#c9a227", "#fff1a8"),
        };
        private static (int From, string Name, string Rim, string Light) TierOf(int level) => Tiers.Last(t => level >= t.From);

        private static GameObject _canvas;
        private static RectTransform _bottom;
        private static int _level = 1, _page;
        private static bool _built;

        // hero (top)
        private static Badge _heroBadge;
        private static Component _heroLevel, _heroTier, _heroState, _heroCats, _playerLine;
        private static RectTransform _content;
        private static ScrollRect _scroll;
        // cards (bottom)
        private static readonly Card[] _cards = new Card[PerPage];
        private static Component _range;
        private static Image _trackFill;
        private static readonly List<Image> _ticks = new List<Image>();
        private static readonly List<Image> _dots = new List<Image>();
        private static Component _pageLabel;
        private static Button _prev, _next;
        // animation
        private static float _cardsStart = -10, _tilesStart = -10;
        private static int _cardsDir;
        private static readonly List<(CanvasGroup Group, RectTransform Rt, float Delay)> _tiles = new List<(CanvasGroup, RectTransform, float)>();
        private static float _wheelAt;
        private static readonly List<(object Icon, Image Pic, Component Placeholder, string Tpl)> _icons = new List<(object, Image, Component, string)>();
        private static int _frames, _iconsShown;
        private static float _frameTime, _frameLogAt;

        public static bool IsOpen => _canvas != null && _canvas.activeSelf;

        public static void Toggle(string why) { if (IsOpen) Close(why); else Open(why); }

        public static void Open(string why)
        {
            try
            {
                if (!_built) Build();
                ProgData.Invalidate(); // names / categories again (the game may have finished loading them since)
                int player = ProgData.PlayerLevel();
                L.Info($"screen open ({why}); player level {player}, {ProgData.Levels.Count} limited items");
                if (player > 0 && _level == 1) { _level = Mathf.Clamp(player, 1, ProgData.MaxLevel); _page = (_level - 1) / PerPage; }
                _canvas.SetActive(true);
                MenuHook.SetOn(true);
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
            L.Info($"screen closed ({why})");
        }

        // ---------------------------------------------------------------- building

        private static void Build()
        {
            float t0 = Time.realtimeSinceStartup;
            _canvas = new GameObject("LevelGateProgressionCanvas", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = ProgressionPlugin.SortOrder.Value;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            _canvas.AddComponent<GraphicRaycaster>();

            var root = Ui.Rect(_canvas.transform, "Panel", Vector2.zero, Vector2.one,
                new Vector2(0, ProgressionPlugin.BottomMargin.Value), new Vector2(0, -ProgressionPlugin.TopMargin.Value));
            Ui.Img(root, Panel, null, true); // blocks clicks to the menu underneath
            Ui.Img(Ui.Fill(root, "Glow"), new Color(.35f, .7f, .9f, .10f), Ui.VerticalFade());

            // top and bottom get their own canvas, so an animation in one doesn't make the game redraw the other
            var topRect = Ui.Rect(root, "Top", new Vector2(0, .4f), Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(topRect);
            BuildTop(topRect);
            _bottom = Ui.Rect(root, "Bottom", Vector2.zero, new Vector2(1, .4f), Vector2.zero, Vector2.zero);
            SubCanvas(_bottom);
            BuildBottom(_bottom);

            ProgData.Changed += () => { if (IsOpen) { L.Debug("level list changed — redrawing"); ShowPage(_page, 0); ShowLevel(_level); } };
            _built = true;
            L.Info($"screen built in {(Time.realtimeSinceStartup - t0) * 1000:0} ms (sort order {canvas.sortingOrder}, margins top {ProgressionPlugin.TopMargin.Value} / bottom {ProgressionPlugin.BottomMargin.Value})");
        }

        private static void SubCanvas(RectTransform rt)
        {
            rt.gameObject.AddComponent<Canvas>();
            rt.gameObject.AddComponent<GraphicRaycaster>();
        }

        private static void BuildTop(RectTransform top)
        {
            // hero: badge + level + rank
            _heroBadge = new Badge(top, new Vector2(0, 1), new Vector2(110, -100), 120);
            var text = Ui.Rect(top, "HeroText", new Vector2(0, 1), new Vector2(.7f, 1), new Vector2(210, -190), new Vector2(0, -24));
            Ui.Label(Ui.Rect(text, "Kicker", new Vector2(0, 1), Vector2.one, new Vector2(0, -24), Vector2.zero), "Kicker", "PROGRESSION", 16, Blue, TextAnchor.UpperLeft, true, 8);
            _heroLevel = Ui.Label(Ui.Rect(text, "Level", new Vector2(0, 1), Vector2.one, new Vector2(0, -96), new Vector2(0, -26)), "LevelText", "LEVEL 1", 60, Color.white, TextAnchor.MiddleLeft, true, 4);
            _heroTier = Ui.Label(Ui.Rect(text, "Tier", new Vector2(0, 1), Vector2.one, new Vector2(0, -122), new Vector2(0, -94)), "TierText", "", 18, Ui.Hex("#cfd8e0"), TextAnchor.UpperLeft, true, 4);
            _heroState = Ui.Label(Ui.Rect(text, "State", new Vector2(0, 1), Vector2.one, new Vector2(0, -146), new Vector2(0, -124)), "StateText", "", 16, Color.white, TextAnchor.UpperLeft, true, 2);
            _heroCats = Ui.Label(Ui.Rect(text, "Cats", new Vector2(0, 1), Vector2.one, new Vector2(0, -172), new Vector2(0, -150)), "CatsText", "", 15, Ui.Hex("#aab7c2"), TextAnchor.UpperLeft, true);

            // the Tarkov logo (eft-logo.png next to the plugin) and the player's level
            var logo = Ui.LoadPng(Path.Combine(Path.GetDirectoryName(typeof(ProgScreen).Assembly.Location) ?? Paths.PluginPath, "eft-logo.png"));
            if (logo != null)
            {
                var lr = Ui.Box(top, "Logo", new Vector2(1, 1), new Vector2(-190, -70), new Vector2(320, 130));
                var li = Ui.Img(lr, Color.white, logo);
                li.preserveAspect = true;
            }
            _playerLine = Ui.Label(Ui.Rect(top, "Player", new Vector2(.6f, 1), new Vector2(1, 1), new Vector2(0, -170), new Vector2(-40, -140)), "PlayerText", "", 16, Ui.Hex("#cfe6f5"), TextAnchor.UpperRight, true, 3);

            // the unlocks: a scrolling list of categories
            var view = Ui.Rect(top, "Scroll", Vector2.zero, Vector2.one, new Vector2(34, 10), new Vector2(-34, -200));
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.scrollSensitivity = 40;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Ui.Fill(view, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            Ui.Img(viewport, new Color(0, 0, 0, 0), null, true);
            _content = Ui.Rect(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            _content.pivot = new Vector2(.5f, 1);
            var vl = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 14; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
            vl.padding = new RectOffset(0, 12, 4, 12);
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.viewport = viewport;
            _scroll.content = _content;
        }

        private static void BuildBottom(RectTransform bottom)
        {
            Ui.Img(Ui.Rect(bottom, "Line", new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero), new Color(Blue.r, Blue.g, Blue.b, .35f));
            Ui.Img(Ui.Fill(bottom, "Shade"), new Color(0, 0, 0, .35f));
            _range = Ui.Label(Ui.Rect(bottom, "Range", new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(90, -44), new Vector2(0, -12)), "RangeText", "LEVELS 1–5", 16, Blue, TextAnchor.MiddleLeft, true, 8);
            Ui.Label(Ui.Rect(bottom, "Hint", new Vector2(.5f, 1), Vector2.one, new Vector2(0, -44), new Vector2(-90, -12)), "HintText", "← →  LEVEL     Q / E  PAGE     " + ProgressionPlugin.OpenKey.Value + "  CLOSE", 13, Ui.Hex("#7f8c97"), TextAnchor.MiddleRight, true, 3);

            _prev = Arrow(bottom, "Prev", "‹", 0, () => ShowPage(_page - 1, -1));
            _next = Arrow(bottom, "Next", "›", 1, () => ShowPage(_page + 1, 1));

            var cards = Ui.Rect(bottom, "Cards", Vector2.zero, Vector2.one, new Vector2(90, 96), new Vector2(-90, -56));
            for (int i = 0; i < PerPage; i++)
            {
                float a = i / (float)PerPage, b = (i + 1) / (float)PerPage;
                var slot = Ui.Rect(cards, "Slot" + i, new Vector2(a, 0), new Vector2(b, 1), new Vector2(7, 0), new Vector2(-7, 0));
                _cards[i] = new Card(slot, i);
            }

            // track: the player's progress along this page's levels
            var track = Ui.Rect(bottom, "Track", new Vector2(0, 0), new Vector2(1, 0), new Vector2(97, 70), new Vector2(-97, 74));
            Ui.Img(track, new Color(Blue.r, Blue.g, Blue.b, .18f));
            var fill = Ui.Rect(track, "Fill", Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            _trackFill = Ui.Img(fill, Blue);
            for (int i = 0; i < PerPage; i++)
            {
                float x = (i + .5f) / PerPage;
                var tick = Ui.Box(track, "Tick" + i, new Vector2(x, .5f), Vector2.zero, new Vector2(11, 11));
                tick.localEulerAngles = new Vector3(0, 0, 45);
                _ticks.Add(Ui.Img(tick, Ui.Hex("#1b2630")));
            }

            // page dots
            var dots = Ui.Rect(bottom, "Dots", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-300, 24), new Vector2(300, 44));
            float w = 26, gap = 7, total = Pages * (w + gap) - gap, x0 = -total / 2 + w / 2 - 60;
            for (int i = 0; i < Pages; i++)
            {
                int page = i;
                var d = Ui.Box(dots, "Dot" + i, new Vector2(.5f, .5f), new Vector2(x0 + i * (w + gap), 0), new Vector2(w, 5));
                var img = Ui.Img(d, new Color(1, 1, 1, .2f), null, true);
                var btn = d.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => ShowPage(page, page > _page ? 1 : -1));
                _dots.Add(img);
            }
            _pageLabel = Ui.Label(Ui.Box(dots, "Page", new Vector2(.5f, .5f), new Vector2(total / 2 + 20 + 90, 0), new Vector2(180, 24)), "PageText", "", 13, Ui.Hex("#7f8c97"), TextAnchor.MiddleLeft, true, 6);
        }

        private static Button Arrow(RectTransform parent, string name, string glyph, float side, Action click)
        {
            var rt = Ui.Rect(parent, name, new Vector2(side, 0), new Vector2(side, 1), new Vector2(side == 0 ? 24 : -76, 100), new Vector2(side == 0 ? 76 : -24, -60));
            var img = Ui.Img(rt, new Color(1, 1, 1, .05f), null, true);
            Ui.Label(rt, "Glyph", glyph, 54, Ui.Hex("#dfe9f1"), TextAnchor.MiddleCenter, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(.6f, .85f, 1f, 1f);
            colors.disabledColor = new Color(1, 1, 1, .3f);
            b.colors = colors;
            b.onClick.AddListener(() => click());
            return b;
        }

        // ---------------------------------------------------------------- showing

        /// <summary>levelAfter: the level to pick on the new page (0 = its first level).</summary>
        private static void ShowPage(int page, int dir, int levelAfter = 0)
        {
            page = Mathf.Clamp(page, 0, Pages - 1);
            bool changed = page != _page;
            _page = page;
            if (changed && dir != 0) _level = levelAfter > 0 ? levelAfter : page * PerPage + 1;
            int first = page * PerPage + 1, last = Mathf.Min(ProgData.MaxLevel, first + PerPage - 1);
            L.Debug($"page {page + 1}/{Pages} (levels {first}–{last}){(dir != 0 ? " slide " + (dir > 0 ? "right" : "left") : "")}");
            Ui.SetText(_range, $"LEVELS {first}–{last}");
            Ui.SetText(_pageLabel, $"PAGE {page + 1} / {Pages}");
            _prev.interactable = page > 0;
            _next.interactable = page < Pages - 1;
            for (int i = 0; i < _dots.Count; i++)
            {
                _dots[i].color = i == page ? Blue : new Color(1, 1, 1, .2f);
                _dots[i].rectTransform.sizeDelta = new Vector2(i == page ? 40 : 26, 5);
            }
            for (int i = 0; i < PerPage; i++) _cards[i].Show(first + i);
            _cardsDir = dir;
            _cardsStart = dir != 0 ? Time.unscaledTime : -10;
            if (changed && dir != 0) ShowLevel(_level);
            else UpdateSelection();
            UpdateTrack();
        }

        private static void ShowLevel(int level)
        {
            level = Mathf.Clamp(level, 1, ProgData.MaxLevel);
            int page = (level - 1) / PerPage;
            _level = level;
            if (page != _page) { ShowPage(page, page > _page ? 1 : -1, level); return; }
            float t0 = Time.realtimeSinceStartup;
            var items = ProgData.ItemsAt(level);
            var tier = TierOf(level);
            int player = ProgData.PlayerLevel();

            _heroBadge.Set(level);
            Ui.SetText(_heroLevel, "LEVEL " + level);
            Ui.SetText(_heroTier, $"{tier.Name.ToUpperInvariant()}  ·  {items.Count} UNLOCK{(items.Count == 1 ? "" : "S")}");
            Ui.SetText(_heroState, player <= 0 ? "" : level <= player ? "<color=#1ed760>UNLOCKED</color>" : $"<color=#f15e6c>LOCKED</color>  <color=#7f8c97>·  {level - player} LEVEL{(level - player == 1 ? "" : "S")} TO GO</color>");
            float pct = ProgData.LevelProgress();
            Ui.SetText(_playerLine, player <= 0 ? "" : $"YOUR LEVEL  <color=#ffffff>{player}</color>{(pct >= 0 ? $"   <color=#5ab4e6>{Mathf.RoundToInt(pct * 100)}%</color>" : "")}");

            // categories, in the editor's order
            var groups = ProgData.Groups.Select(g => (g, list: items.Where(it => it.Group == g.Key).ToList())).Where(x => x.list.Count > 0).ToList();
            Ui.SetText(_heroCats, string.Join("    ", groups.Select(x => $"<color={x.g.Color}>{x.g.Name.ToUpperInvariant()}</color> {x.list.Count}").ToArray()));

            foreach (Transform ch in _content) UnityEngine.Object.Destroy(ch.gameObject);
            _tiles.Clear();
            _icons.Clear();
            int max = Mathf.Max(1, ProgressionPlugin.MaxTilesPerCategory.Value), n = 0;
            foreach (var (g, list) in groups)
            {
                var color = Ui.Hex(g.Color);
                var section = Ui.Rect(_content, "Cat_" + g.Key, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var sl = section.gameObject.AddComponent<VerticalLayoutGroup>();
                sl.spacing = 8; sl.childControlHeight = true; sl.childControlWidth = true; sl.childForceExpandHeight = false; sl.childForceExpandWidth = true;

                var head = Ui.Rect(section, "Head", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                head.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
                Ui.Img(Ui.Rect(head, "Bar", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 5), new Vector2(4, -5)), color);
                Ui.Label(Ui.Rect(head, "Name", Vector2.zero, Vector2.one, new Vector2(16, 0), Vector2.zero), "Text",
                    $"{g.Name.ToUpperInvariant()}  <color={g.Color}>{list.Count}</color>{(list.Count > max ? $"  <color=#7f8c97>(showing {max})</color>" : "")}", 17, Ui.Hex("#e8eef3"), TextAnchor.MiddleLeft, true, 6);
                Ui.Img(Ui.Rect(head, "Rule", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), new Color(1, 1, 1, .07f));

                var grid = Ui.Rect(section, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
                gl.cellSize = new Vector2(150, 112);
                gl.spacing = new Vector2(10, 10);
                gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
                gl.constraint = GridLayoutGroup.Constraint.Flexible;
                foreach (var it in list.Take(max)) Tile(grid, it, color, level <= player || player <= 0, n++);
            }
            if (groups.Count == 0)
            {
                var empty = Ui.Rect(_content, "Empty", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
                Ui.Label(empty, "Text", $"NOTHING UNLOCKS AT LEVEL {level}\n<size=14><color=#7f8c97>Set items to this level in the Level & Item Editor (or LevelGate's F9 menu).</color></size>", 20, Ui.Hex("#cfd8e0"), TextAnchor.MiddleCenter, true, 3);
            }
            _content.anchoredPosition = Vector2.zero;
            _tilesStart = Time.unscaledTime;
            UpdateSelection();
            UpdateTrack();
            L.Debug($"level {level}: {items.Count} item(s) in {groups.Count} categories, {n} tiles drawn in {(Time.realtimeSinceStartup - t0) * 1000:0} ms ({_icons.Count(x => x.Icon != null)} icons requested)");
        }

        private static void Tile(RectTransform grid, ProgItem it, Color color, bool reached, int index)
        {
            var rt = Ui.Rect(grid, "Tile", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            var inner = Ui.Fill(rt, "Inner");
            Ui.Img(inner, Ui.Hex("#161d24"));
            Ui.Img(Ui.Rect(inner, "Shine", new Vector2(0, .45f), Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, .045f));
            Ui.Img(Ui.Rect(inner, "Strip", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 3)), color);
            var shortName = Ui.Label(Ui.Rect(inner, "Short", new Vector2(0, .38f), Vector2.one, new Vector2(8, 0), new Vector2(-8, -6)), "Text", it.Short, 20, color, TextAnchor.MiddleCenter, true, 0, true);
            var name = Ui.Label(Ui.Rect(inner, "Name", Vector2.zero, new Vector2(1, .38f), new Vector2(8, 6), new Vector2(-8, 0)), "Text", it.Name, 12, Ui.Hex("#cdd6de"), TextAnchor.MiddleCenter, false, 0, true);
            Ui.SetWrap(name, true);
            // the game's own picture of the item (arrives a few frames later); the short name shows until then
            var pic = Ui.Img(Ui.Rect(inner, "Icon", new Vector2(0, .38f), Vector2.one, new Vector2(10, 2), new Vector2(-10, -8)), Color.white);
            pic.preserveAspect = true;
            pic.enabled = false;
            _icons.Add((GameItems.IconOf(GameItems.ItemOf(it.Tpl)), pic, shortName, it.Tpl));
            var click = rt.gameObject.AddComponent<TileClick>();
            click.Tpl = it.Tpl;
            click.Name = it.Name;
            Ui.Img(Ui.Fill(rt, "Hit"), new Color(0, 0, 0, 0), null, true);
            if (!reached) Ui.Img(Ui.Rect(inner, "Lock", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -14), new Vector2(-6, -6)), Ui.Hex("#f15e6c"));
            _tiles.Add((group, inner, Mathf.Min(index, 40) * .012f));
        }

        private static void UpdateSelection()
        {
            int player = ProgData.PlayerLevel();
            foreach (var c in _cards) c.Mark(_level, player);
        }

        private static void UpdateTrack()
        {
            int player = ProgData.PlayerLevel();
            int first = _page * PerPage + 1;
            float pct = ProgData.LevelProgress();
            float fill;
            if (player <= 0 || player < first) fill = 0;
            else if (player >= first + PerPage) fill = 1;
            else fill = Mathf.Clamp01((player - first + .5f + Mathf.Max(0, pct)) / PerPage);
            _trackFill.rectTransform.anchorMax = new Vector2(fill, 1);
            for (int i = 0; i < _ticks.Count; i++)
                _ticks[i].color = player > 0 && first + i <= player ? Blue : Ui.Hex("#1b2630");
        }

        // ---------------------------------------------------------------- every frame

        public static void Tick()
        {
            if (!IsOpen) return;
            var input = UnityInput.Current;
            if (input.GetKeyDown(KeyCode.RightArrow)) ShowLevel(_level + 1);
            else if (input.GetKeyDown(KeyCode.LeftArrow)) ShowLevel(_level - 1);
            else if (input.GetKeyDown(KeyCode.E) || input.GetKeyDown(KeyCode.PageDown)) ShowPage(_page + 1, 1);
            else if (input.GetKeyDown(KeyCode.Q) || input.GetKeyDown(KeyCode.PageUp)) ShowPage(_page - 1, -1);
            else if (input.GetKeyDown(KeyCode.Home)) ShowLevel(1);
            else if (input.GetKeyDown(KeyCode.End)) ShowLevel(ProgData.MaxLevel);
            else if (input.GetKeyDown(KeyCode.Escape)) { Close("Escape"); return; }

            float wheel = input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > .01f && Time.unscaledTime - _wheelAt > .3f && MenuHook.Contains(_bottom, input.mousePosition))
            {
                _wheelAt = Time.unscaledTime;
                ShowPage(_page + (wheel < 0 ? 1 : -1), wheel < 0 ? 1 : -1);
            }

            // cards slide in from the side you went to (only while sliding: touching them every frame costs)
            float now = Time.unscaledTime;
            if (now - _cardsStart < 1f)
                for (int i = 0; i < PerPage; i++)
                {
                    float delay = _cardsDir >= 0 ? i * .045f : (PerPage - 1 - i) * .045f;
                    float t = Mathf.Clamp01((now - _cardsStart - delay) / .38f);
                    float e = 1 - Mathf.Pow(1 - t, 3);
                    _cards[i].Animate(e, _cardsDir);
                }
            // icons: shown as soon as the game has drawn them
            for (int i = _icons.Count - 1; i >= 0; i--)
            {
                var (icon, pic, placeholder, tpl) = _icons[i];
                if (pic == null) { _icons.RemoveAt(i); continue; }
                var sprite = GameItems.SpriteOf(icon);
                if (sprite == null) continue;
                pic.sprite = sprite;
                pic.enabled = true;
                if (placeholder != null) placeholder.gameObject.SetActive(false);
                _icons.RemoveAt(i);
                if (_iconsShown++ == 0) L.Info($"first item icon shown ({tpl})");
            }
            // how smooth it runs while open (every 5 s in the log)
            _frames++; _frameTime += Time.unscaledDeltaTime;
            if (L.Verbose && now > _frameLogAt)
            {
                L.Debug($"screen open: {_frames / Mathf.Max(.001f, _frameTime):0} fps, icons waiting {_icons.Count}");
                _frames = 0; _frameTime = 0; _frameLogAt = now + 5;
            }
            // tiles pop in one after another
            if (_tiles.Count > 0)
            {
                bool done = true;
                foreach (var (group, rt, delay) in _tiles)
                {
                    if (group == null) continue;
                    float t = Mathf.Clamp01((now - _tilesStart - delay) / .3f);
                    float e = 1 - Mathf.Pow(1 - t, 3);
                    group.alpha = e;
                    rt.localScale = Vector3.one * (.92f + .08f * e);
                    if (t < 1) done = false;
                }
                if (done) _tiles.Clear();
            }
            // the picked card's badge breathes
            foreach (var c in _cards) c.Pulse(now);
        }

        public static void Dump()
        {
            L.Info($"screen: built {_built}, open {IsOpen}, level {_level}, page {_page + 1}/{Pages}, tiles animating {_tiles.Count}, canvas {(_canvas == null ? "none" : MenuHook.Path(_canvas.transform))}");
        }

        // ---------------------------------------------------------------- pieces

        /// <summary>The rank diamond: a square turned 45° with a metal rim, the number upright inside.</summary>
        private sealed class Badge
        {
            private readonly RectTransform _root;
            private readonly Image _rim, _inner;
            private readonly Component _num;

            public Badge(RectTransform parent, Vector2 anchor, Vector2 pos, float size)
            {
                _root = Ui.Box(parent, "Badge", anchor, pos, new Vector2(size, size));
                var shadow = Ui.Box(_root, "Shadow", new Vector2(.5f, .5f), new Vector2(0, -4), new Vector2(size * .72f, size * .72f));
                shadow.localEulerAngles = new Vector3(0, 0, 45);
                Ui.Img(shadow, new Color(0, 0, 0, .55f));
                var rim = Ui.Box(_root, "Rim", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .7f, size * .7f));
                rim.localEulerAngles = new Vector3(0, 0, 45);
                _rim = Ui.Img(rim, Color.white);
                var inner = Ui.Box(_root, "Inner", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .52f, size * .52f));
                inner.localEulerAngles = new Vector3(0, 0, 45);
                _inner = Ui.Img(inner, Ui.Hex("#1a2129"));
                _num = Ui.Label(_root, "Number", "1", size * .3f, Color.white, TextAnchor.MiddleCenter, true);
            }

            public RectTransform Root => _root;

            public void Set(int level, bool dim = false)
            {
                var tier = TierOf(level);
                var rim = Ui.Hex(tier.Rim);
                var light = Ui.Hex(tier.Light);
                _rim.color = dim ? Color.Lerp(rim, Color.gray, .7f) * .6f : Color.Lerp(rim, light, .35f);
                _inner.color = dim ? Ui.Hex("#141a20") : Ui.Hex("#1f2831");
                Ui.SetText(_num, level.ToString());
                Ui.SetColor(_num, dim ? Ui.Hex("#5f6b75") : light);
            }
        }

        /// <summary>One of the five level cards along the bottom.</summary>
        private sealed class Card
        {
            private readonly RectTransform _slot, _body;
            private readonly CanvasGroup _group;
            private readonly Image _bg, _frame, _strip;
            private readonly Component _title, _tier, _count, _preview, _state;
            private readonly Badge _badge;
            private int _level;
            private bool _picked;

            public Card(RectTransform slot, int index)
            {
                _slot = slot;
                _body = Ui.Fill(slot, "Card");
                _group = _body.gameObject.AddComponent<CanvasGroup>();
                _frame = Ui.Img(Ui.Rect(_body, "Frame", Vector2.zero, Vector2.one, new Vector2(-2, -2), new Vector2(2, 2)), Color.clear);
                _bg = Ui.Img(Ui.Fill(_body, "Bg"), Ui.Hex("#141b22"), null, true);
                Ui.Img(Ui.Rect(_body, "Shine", new Vector2(0, .5f), Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, .04f));
                _title = Ui.Label(Ui.Rect(_body, "Title", new Vector2(0, 1), Vector2.one, new Vector2(0, -44), new Vector2(0, -10)), "Text", "", 24, Blue, TextAnchor.MiddleCenter, true, 4);
                _badge = new Badge(_body, new Vector2(.5f, 1), new Vector2(0, -98), 96);
                _tier = Ui.Label(Ui.Rect(_body, "Tier", new Vector2(0, 1), Vector2.one, new Vector2(0, -170), new Vector2(0, -150)), "Text", "", 12, Ui.Hex("#7f8c97"), TextAnchor.MiddleCenter, true, 8);
                _preview = Ui.Label(Ui.Rect(_body, "Preview", new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 46), new Vector2(-12, 72)), "Text", "", 13, Ui.Hex("#cfe6f5"), TextAnchor.MiddleCenter, true);
                _count = Ui.Label(Ui.Rect(_body, "Count", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 24), new Vector2(0, 44)), "Text", "", 12, Ui.Hex("#aab7c2"), TextAnchor.MiddleCenter, true, 6);
                _state = Ui.Label(Ui.Rect(_body, "State", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 6), new Vector2(0, 24)), "Text", "", 11, Ui.Hex("#7f8c97"), TextAnchor.MiddleCenter, true, 6);
                _strip = Ui.Img(Ui.Rect(_body, "Strip", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 3)), Color.clear);
                var button = _body.gameObject.AddComponent<Button>();
                button.targetGraphic = _bg;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { L.Debug($"card level {_level} clicked"); ShowLevel(_level); });
            }

            public void Show(int level)
            {
                _level = level;
                bool exists = level <= ProgData.MaxLevel;
                _body.gameObject.SetActive(exists);
                if (!exists) return;
                var items = ProgData.ItemsAt(level);
                Ui.SetText(_title, "LEVEL " + level);
                _badge.Set(level, items.Count == 0);
                Ui.SetText(_tier, TierOf(level).Name.ToUpperInvariant());
                Ui.SetText(_count, items.Count == 0 ? "NO UNLOCKS" : $"{items.Count} UNLOCK{(items.Count == 1 ? "" : "S")}");
                var names = items.Take(3).Select(i => i.Short).ToArray();
                Ui.SetText(_preview, items.Count == 0 ? "" : string.Join("  ·  ", names) + (items.Count > 3 ? $"  <color=#5ab4e6>+{items.Count - 3}</color>" : ""));
                _group.alpha = items.Count == 0 ? .55f : 1f;
            }

            public void Mark(int picked, int player)
            {
                _picked = _level == picked;
                _frame.color = _picked ? Blue : Color.clear;
                _bg.color = _picked ? Ui.Hex("#1a2a36") : Ui.Hex("#141b22");
                _strip.color = _picked ? Blue : Color.clear;
                Ui.SetColor(_title, _picked ? Color.white : Blue);
                string state = player <= 0 ? "" : _level < player ? "<color=#1ed760>UNLOCKED</color>" : _level == player ? "<color=#ffd24a>YOUR LEVEL</color>" : "LOCKED";
                Ui.SetText(_state, state);
            }

            public void Animate(float e, int dir)
            {
                if (!_body.gameObject.activeSelf) return;
                float shift = dir == 0 ? 0 : dir * 90f * (1 - e);
                _body.anchoredPosition = new Vector2(shift, 0);
                float baseAlpha = ProgData.CountAt(_level) == 0 ? .55f : 1f;
                _group.alpha = baseAlpha * (dir == 0 ? 1 : e);
            }

            public void Pulse(float now)
            {
                float s = _picked ? 1f + .035f * Mathf.Sin(now * 2.6f) : 1f;
                _badge.Root.localScale = new Vector3(s, s, 1);
            }
        }
    }
}

namespace LevelGate.Progression
{
    /// <summary>Right-click on a tile: the game's inspect window for the item.</summary>
    internal sealed class TileClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public string Tpl, Name;

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            L.Debug($"tile {Name} ({Tpl}) clicked with {e.button}");
            if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) GameItems.Inspect(Tpl);
        }
    }
}
