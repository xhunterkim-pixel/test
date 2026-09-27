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
        private static readonly Color Dim = Ui.Hex("#4f575a");
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
            if (!IsOpen && MenuHook.GoToMainMenuThen(why)) return;
            try
            {
                if (!_built || _canvas == null) { _built = false; Build(); }
                ProgData.Invalidate(); // names / categories again (the game may have finished loading them since)
                int player = ProgData.PlayerLevel();
                int seen = ProgressionPlugin.LastSeenLevel.Value;
                _newFrom = seen > 0 && seen < player ? seen : 0;
                MenuWidget.Seen(player); // the NEW tag on the main-menu shortcut goes away
                L.Info($"screen open ({why}); player level {player}, {ProgData.Levels.Count} limited items; performance mode {(Perf ? "ON — emblems stand still, pictures drawn smaller" : "off")}");
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
            // left: who you are — your rank emblem, "PROGRESSION" as a quiet page label, your rank name, how far to the next rank
            const float badge = 72;
            _headBadge = new Badge(top, new Vector2(0, 1), new Vector2(Margin + badge / 2, -(S3 + badge / 2)), badge);
            float x = Margin + badge + S3;
            Ui.Label(Ui.Rect(top, "Page", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -44), new Vector2(-Gutter, -S3 + 4)), "Text", "PROGRESSION", TTitle, Grey, TextAnchor.UpperLeft, false, 1);
            _headRank = Ui.Label(Ui.Rect(top, "Rank", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -66), new Vector2(-Gutter, -46)), "Text", "", TStrong, Text, TextAnchor.MiddleLeft, false);
            _headNext = Ui.Label(Ui.Rect(top, "NextRank", new Vector2(0, 1), new Vector2(.34f, 1), new Vector2(x, -86), new Vector2(-Gutter, -66)), "Text", "", TBody, Grey, TextAnchor.MiddleLeft, false);

            BuildXp(top);

            // the Tarkov logo (eft-logo.png next to the plugin), dimmed: it's branding, not information
            var logo = Ui.LoadPng(Path.Combine(Path.GetDirectoryName(typeof(ProgScreen).Assembly.Location) ?? Paths.PluginPath, "eft-logo.png"));
            if (logo != null) Ui.Img(Ui.Box(top, "Logo", new Vector2(1, 1), new Vector2(-(Margin + 90), -(S3 + 32)), new Vector2(180, 64)), new Color(1, 1, 1, .4f), logo).preserveAspect = true;
        }

        private static Component _headRank, _headNext;

        /// <summary>The header shows the player: rank emblem, rank name, levels to the next rank.</summary>
        private static void UpdateHeader(int player)
        {
            if (_headRank == null) return;
            int lv = Mathf.Max(1, player);
            _headBadge.Set(lv);
            var tier = TierOf(lv);
            int index = Array.IndexOf(Tiers, tier) + 1;
            // rank name, then overall progress quietly after it: how many of the limited items you can use
            int total = ProgData.Levels.Count, owned = player > 0 ? ProgData.Levels.Values.Count(v => v <= player) : 0;
            Ui.SetText(_headRank, player <= 0 ? "Progression" : total > 0 ? $"{tier.Name}  <size={TBody}><color=#7d8588>·  {Thousands(owned)} / {Thousands(total)} items unlocked</color></size>" : tier.Name);
            var next = Tiers.FirstOrDefault(t => t.From > lv);
            Ui.SetText(_headNext, player <= 0 ? "" : next.Name == null ? $"Rank {index} of {Tiers.Length}  ·  top rank"
                : $"Rank {index} of {Tiers.Length}  ·  next: {next.Name} (level {next.From})");
        }

        private static Component _xpLevel, _xpText, _xpNext;
        private static RectTransform _xpFill;
        private static Image _xpSquare;
        private static RectTransform _xpTag;

        /// <summary>Arena-style player block over the centre panel: [61] ▕████░░░░▏ 25 / 1 000 EXP · Next level reward: 2 ◆</summary>
        private static void BuildXp(RectTransform top)
        {
            L.Step("BuildXp");
            var xp = Ui.Rect(top, "Xp", new Vector2(.34f, 1), new Vector2(.74f, 1), new Vector2(Gutter / 2, -(S3 + 80)), new Vector2(-Gutter / 2, -S3));
            // "CURRENT LEVEL" heads the block, directly over the level square it names; the overall unlock count sits opposite
            Ui.Label(Ui.Rect(xp, "CurrentLabel", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -14), Vector2.zero), "Text", "CURRENT LEVEL", TCaps, Grey, TextAnchor.MiddleLeft, false, Caps);
            var sq = Ui.Rect(xp, "Level", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -80), new Vector2(64, -18));
            _xpSquare = Ui.Img(sq, Ui.Hex("#e0562f"), Ui.CutCorner());
            _xpLevel = Ui.Label(sq, "Text", "", TLevel, Color.white, TextAnchor.MiddleCenter, true);
            var right = Ui.Rect(xp, "Right", Vector2.zero, Vector2.one, new Vector2(64 + S4, 0), Vector2.zero);
            // the bar: dark frame, thin grey edge, orange fill
            var bar = Ui.Rect(right, "Bar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -30), new Vector2(0, -18));
            Ui.Img(bar, Ui.Hex("#4a5155"));
            var barIn = Ui.Fill(bar, "In", 2);
            Ui.Img(barIn, Ui.Hex("#15191b"));
            _xpFill = Ui.Rect(barIn, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(2, 2), new Vector2(0, -2));
            Ui.Img(_xpFill, Ui.Hex("#e0562f"));
            _xpText = Ui.Label(Ui.Rect(right, "Exp", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -60), new Vector2(0, -34)), "Text", "", THero, Ui.Hex("#b9c0c3"), TextAnchor.MiddleLeft, true);
            // the orange EXP tag right after the numbers (moved to the text's end whenever it changes)
            _xpTag = Ui.Rect(right, "ExpTag", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -56), new Vector2(40, -38));
            Ui.Img(_xpTag, Ui.Hex("#e0562f"));
            Ui.Label(_xpTag, "Text", "EXP", TCaps, Ui.Hex("#1a1210"), TextAnchor.MiddleCenter, true, 1);
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
            if (_xpLevel == null) return;
            int player = ProgData.PlayerLevel();
            Ui.SetText(_xpLevel, player > 0 ? player.ToString() : "?");
            _xpSquare.color = Ui.Hex("#e0562f");
            float frac = 0;
            if (ProgData.LevelExp(out int have, out int need))
            {
                frac = Mathf.Clamp01(have / (float)need);
                // your XP / needed XP, bold: yours a shade softer, the target bright (Arena)
                string text = $"<color=#b9c0c3>{Thousands(have)}</color><color=#6f777a> / </color><color=#eef2f3>{Thousands(need)}</color>";
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
            UpdateXpNext();
            UpdateHeader(player);

        }

        private static Component _listState;

        private static void BuildUnlocks(RectTransform top)
        {
            // left third: the selected level's rewards
            var panel = Panel(top, "Unlocks", new Vector2(0, 0), new Vector2(.34f, 1), new Vector2(Margin, S4), new Vector2(-Gutter / 2, -PanelTop));
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
            var area = Ui.Rect(stage, "PicArea", Vector2.zero, Vector2.one, new Vector2(PanelPad, PanelPad), new Vector2(-PanelPad, -PanelPad));
            var picBox = Ui.Fill(area, "Box");
            var fit = picBox.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1;
            var picFace = Ui.Fill(picBox, "Face", 0); // no frame of its own (the panel is the frame): the lit face is the hero
            Ui.Img(picFace, Face);
            // lighting only: a soft key light from above and a centre glow, so the item sits on a lit surface
            Ui.Img(Ui.Rect(picFace, "KeyLight", new Vector2(0, .45f), Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, .03f), Ui.VerticalFade());
            Ui.Img(Ui.Fill(picFace, "Light"), new Color(1, 1, 1, .07f), Ui.Radial());
            _featPic = Ui.Img(Ui.Fill(picFace, "Pic", 40), Color.white);
            _featPic.preserveAspect = true;
            _featPic.enabled = false;
            _featShort = Ui.Label(Ui.Fill(picFace, "Short", 40), "Text", "", THero, Dim, TextAnchor.MiddleCenter, false, 0, true);
            Ui.EdgeFade(picFace, .14f, .55f);
            Ui.Grit(picFace, 2, .017f);
            // secondary locked signal only (the requirement on the right is the primary one)
            _featLock = Ui.Img(Ui.Rect(picFace, "Lock", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-S4 - 28, S4), new Vector2(-S4, S4 + 28)), Ui.Hex("#aeb6b9", .7f), Ui.Lock());
            _featLock.enabled = false;

            // right: the selected reward's details, laid out in content order (nothing at fixed heights, so long names
            // and descriptions push the rest down instead of overlapping)
            var side = Panel(top, "Details", new Vector2(.74f, 0), new Vector2(1, 1), new Vector2(Gutter / 2, S4), new Vector2(-Margin, -PanelTop));
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
            dvp.gameObject.AddComponent<RectMask2D>();
            Ui.Img(dvp, new Color(0, 0, 0, 0), null, true);
            var dContent = Ui.Rect(dvp, "Content", new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            dContent.pivot = new Vector2(.5f, 1);
            var dcl = dContent.gameObject.AddComponent<VerticalLayoutGroup>();
            dcl.childControlHeight = true; dcl.childControlWidth = true; dcl.childForceExpandHeight = false; dcl.padding = new RectOffset(0, (int)S2, 0, 0);
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
            var box = Ui.Rect(row, "Box", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -8), new Vector2(16, 8));
            Ui.Img(box, Grey);
            Ui.Img(Ui.Fill(box, "In", 1), Ui.Hex("#10161a"));
            _featCheck = Ui.Img(Ui.Fill(box, "Check", -2), Ui.Hex(Green), Ui.Tick()); // a real tick, not a filled square
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
            _prev = Arrow(bottom, "Prev", "‹", 0, () => ShowPage(_page - 1, -1));
            _next = Arrow(bottom, "Next", "›", 1, () => ShowPage(_page + 1, 1));
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

            // page bar like the Arena battle pass: [Q] ▬▬▬▬ … [E], the page numbers under the segments
            // stretches between fixed side insets (1120 px at 1920 wide, narrower on narrower screens), so it never runs into the checkbox
            var bar = Ui.Rect(bottom, "Pages", new Vector2(0, 0), new Vector2(1, 0), new Vector2(400, 14), new Vector2(-400, 60));
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
                ProgressionPlugin.PerformanceMode.Value = !Perf; // the setting's change redraws the screen (Plugin: SettingChanged → Refresh)
                L.Info("performance mode " + (Perf ? "on" : "off"));
            });
            HoverHook.Add(rt, on =>
            {
                FadeTo(label as Graphic, on ? Grey : Dim); FadeTo(edge, on ? HoverEdge : Ui.Hex("#5a6468"));
                ShowTip(on ? rt : null, "Lighter pictures, still emblems, fewer items per category");
            });
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
                    VersionLabel(false);
                }
                else if (_menuGroup != null)
                {
                    _menuGroup.alpha = _menuAlpha; _menuGroup.blocksRaycasts = _menuRaycasts;
                    _menuGroup = null;
                    L.Debug("main menu shown again");
                    VersionLabel(true);
                }
            }
            catch (Exception e) { L.ErrorOnce("hiding the main menu", e); }
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
            _icons.Clear(); DropRequests(_icons);
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
            if (compact)
            {
                var grid = Ui.Rect(_content, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
                gl.cellSize = new Vector2(cell, cell + TileLabel);
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
                gl.cellSize = new Vector2(cell, cell + TileLabel);
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
            var thumb = Ui.Rect(inner, "Thumb", new Vector2(0, 0), Vector2.one, new Vector2(0, TileLabel - 1), Vector2.zero);
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
            if (_newFrom > 0 && reached && it.Level > _newFrom)
            {
                var tag = Ui.Rect(inner, "New", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-S1 - 30, -S1 - 14), new Vector2(-S1, -S1));
                Ui.Img(tag, Ui.Hex(Orange, .9f));
                Ui.Label(tag, "Text", "NEW", 10, Ui.Hex("#1a1210"), TextAnchor.MiddleCenter, true, 1);
            }
            RequestIcon(_icons, it.Tpl, 1, v.Pic, placeholder);
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
        private static void ShowTip(RectTransform rt, string name)
        {
            if (_tip == null) return;
            if (rt == null || string.IsNullOrEmpty(name)) { _tip.gameObject.SetActive(false); return; }
            Ui.SetText(_tipText, name);
            float w = Ui.PreferredWidth(_tipText, name) + S3 * 2;
            _tip.sizeDelta = new Vector2(Mathf.Min(w, 420), 26);
            var r = rt.rect;
            _tip.position = rt.TransformPoint(new Vector3(r.center.x, r.yMax, 0));
            _tip.anchoredPosition += new Vector2(0, S1);
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
        private static int _prefetchPage = -1;
        private static readonly Queue<string> _prefetchQueue = new Queue<string>();

        /// <summary>Card pictures (and their pre-loading) share one scale per item: about 120 px on the long side.</summary>
        private static int CardScaleOf(string tpl) => Perf ? 1 : GameItems.CardScale(tpl, 120);

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
            if (_prefetchPage != _page)
            {
                _prefetchPage = _page;
                _prefetchQueue.Clear();
                foreach (var p in new[] { _page + 1, _page - 1 })
                {
                    if (p < 0 || p >= Pages) continue;
                    for (int level = p * PerPage + 1; level <= Mathf.Min(ProgData.MaxLevel, (p + 1) * PerPage); level++)
                        foreach (var it in CardPicks(ProgData.ItemsAt(level))) _prefetchQueue.Enqueue(it.Tpl);
                }
            }
            while (_prefetchQueue.Count > 0)
            {
                var tpl = _prefetchQueue.Dequeue();
                int scale = CardScaleOf(tpl);
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
            _featTpl = it?.Tpl;
            _featPic.enabled = false;
            _featShort.gameObject.SetActive(it != null);
            foreach (Transform ch in _majorRow) UnityEngine.Object.Destroy(ch.gameObject);
            foreach (Transform ch in _minorRow) UnityEngine.Object.Destroy(ch.gameObject);
            if (it == null)
            {
                foreach (var c in new[] { _featType, _featName, _featReq, _featReqValue, _featStatus, _featDesc, _featNote }) Ui.SetText(c, "");
                _featCheck.enabled = false;
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
            for (int start = 0; start < minors.Count; start += cols)
            {
                var line = Row(_minorRow, "Line", S4);
                for (int c = 0; c < cols; c++)
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
            _featLock.enabled = player > 0 && it.Level > player;
            // sized to the preview on screen: enough real pixels for its size at this resolution (performance mode: half)
            float px = Mathf.Max(_featPic.rectTransform.rect.width, _featPic.rectTransform.rect.height) * (_featPic.canvas != null ? _featPic.canvas.scaleFactor : 1f);
            if (px < 64) px = 440;
            int featScale = GameItems.ScaleFor(it.Tpl, px);
            if (Perf) featScale = Mathf.Max(2, featScale / 2);
            _featScale = featScale;
            var kept = GameItems.CopyOf(it.Tpl, featScale);
            if (kept != null) { _featIcon = null; _featPic.sprite = kept; _featPic.enabled = true; _featShort.gameObject.SetActive(false); }
            else _featIcon = GameItems.IconOf(GameItems.ItemOf(it.Tpl), featScale);
            MarkSelectedTile();
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
            if (_bloom.Count > 0 && _bloom[0] != null) _bloom[0].color = new Color(.85f, .14f, .08f, Mathf.Lerp(.13f, .16f, m));
            if (_bloom.Count > 1 && _bloom[1] != null) _bloom[1].color = new Color(.95f, .2f, .1f, Mathf.Lerp(.11f, .13f, m));
            var border = Border; // the panel borders stay as they are (only the glow turns red)
            foreach (var f in _panelFrames) if (f != null) f.color = border;
        }

        private static void UpdateSelection()
        {
            int player = ProgData.PlayerLevel();
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
                bool typing = ProgressionPlugin.Typing();
                if (typing) { }
                else if (input.GetKeyDown(KeyCode.RightArrow) || input.GetKeyDown(KeyCode.D)) { ShowLevel(_level + 1); Sounds.Click(); }
                else if (input.GetKeyDown(KeyCode.LeftArrow) || input.GetKeyDown(KeyCode.A)) { ShowLevel(_level - 1); Sounds.Click(); }
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
            RunFades();
            if (_scrollTopFrames > 0 && _listScroll != null) { _scrollTopFrames--; _listScroll.StopMovement(); _listScroll.verticalNormalizedPosition = 1; }
            if (_listFade != null && _listScroll != null)
            {
                bool more = _listScroll.content.rect.height > _listScroll.viewport.rect.height + 1 && _listScroll.verticalNormalizedPosition > .01f;
                if (_listFade.enabled != more) _listFade.enabled = more;
            }
            RunIconRequests();
            Prefetch();
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
                if (input.GetMouseButtonDown(0) && over != null && over.Tpl != _featTpl && over.Level == _level) { Sounds.Play("MenuContextMenu", "ButtonClick"); Feature(over); }
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
                    }
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
            private readonly DottedLine _headL, _headR;
            private readonly Component _head, _count, _state, _tier;
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
            private static RectTransform Square(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
            {
                var area = Ui.Rect(parent, name, aMin, aMax, oMin, oMax);
                var sq = Ui.Fill(area, "Square");
                var fit = sq.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = 1;
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
                const float Pad = S3, B = 32, Head = Pad + B + S2, Foot = 32;
                _badge = new Badge(inner, new Vector2(0, 1), new Vector2(Pad + B / 2, -(Pad + B / 2)), B);
                _tier = Ui.Label(Ui.Rect(inner, "Tier", new Vector2(0, 1), Vector2.one, new Vector2(Pad + B + S2, -(Pad + B)), new Vector2(-Pad, -Pad)), "Text", "", TCaps, Grey, TextAnchor.MiddleLeft, false, Caps);
                // selected: a 2 px light bar along the top edge (shape, not only colour)
                _top = Ui.Img(Ui.Rect(inner, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero), Select);
                _top.enabled = false;
                // one big square picture (the level's top category) and two small ones stacked beside it
                // the pictures sit in the area between header and footer; each is kept square
                var pics = Ui.Rect(inner, "Pics", Vector2.zero, Vector2.one, new Vector2(Pad, Foot), new Vector2(-Pad, -Head));
                _picRects[0] = Square(pics, "Pic0", new Vector2(0, 0), new Vector2(.64f, 1), Vector2.zero, new Vector2(-S1, 0));
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
                        ShowTip(on ? _picRects[k] : null, on && _picItems[k] != null ? _picItems[k].Name : null);
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
                Ui.SetText(_head, "LEVEL " + level);
                // one emblem per page: on its first card, with the rank's name (the header shows yours)
                _badge.Set(level, items.Count == 0);
                _badge.Visible = _first;
                Ui.SetText(_tier, _first ? TierOf(level).Name : "");
                Ui.SetText(_count, items.Count == 0 ? "No items" : $"{items.Count} item{(items.Count == 1 ? "" : "s")}");
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
                _group.alpha = items.Count == 0 ? .55f : 1f;
            }

            /// <summary>current (your level): orange header + state · selected: light 2 px frame + top bar · hover: brighter edge ·
            /// future: muted, one small lock, "LOCKED" in grey · new since your last visit: a small orange NEW.</summary>
            public void Mark(int picked, int player)
            {
                _picked = picked; _player = player;
                bool sel = _level == picked, current = player > 0 && _level == player;
                bool locked = player > 0 && _level > player, reached = player > 0 && _level <= player;
                bool fresh = reached && !current && _newFrom > 0 && _level > _newFrom;
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
                string state = player <= 0 ? "" : fresh ? "<color=#e0562f>NEW</color>" : reached ? "" : "LOCKED";
                Ui.SetText(_state, state);
                _stateLock.enabled = locked;
                if (_stateLock.enabled)
                {
                    float w = Ui.PreferredWidth(_state, "LOCKED");
                    var lr = _stateLock.rectTransform;
                    lr.offsetMin = new Vector2(-S3 - w - S1 - 12, lr.offsetMin.y);
                    lr.offsetMax = new Vector2(-S3 - w - S1, lr.offsetMax.y);
                }
                // future levels step back (unless picked or under the mouse)
                _baseAlpha = (ProgData.CountAt(_level) == 0 ? .55f : 1f) * (locked && !sel && !_hover ? .85f : 1f);
                // the pictures carry the weight: full only on the viewing / current card (or under the mouse)
                float pa = sel || current || _hover ? 1f : locked ? .65f : .8f;
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
