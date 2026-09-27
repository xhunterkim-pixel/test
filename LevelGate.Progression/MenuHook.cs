using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// Puts a PROGRESSION button into the main menu bar (EFT.UI.MenuTaskBar): one of the
    /// game's own buttons is copied (so it looks the same), its old behaviour switched
    /// off, and a click opens the Progression screen. Every step is logged.
    /// </summary>
    internal static class MenuHook
    {
        private static Type _barType;
        private static Component _bar;
        private static GameObject _button;
        private static float _nextTry;
        private static int _tries;
        private static bool _dumped;
        private static readonly List<Transform> _candidates = new List<Transform>();

        public static bool BarVisible => _bar != null && _bar.gameObject.activeInHierarchy;
        public static RectTransform BarRect => _bar != null ? _bar.transform as RectTransform : null;
        public static RectTransform ButtonRect => _button != null ? _button.transform as RectTransform : null;

        public static void Apply(Harmony harmony)
        {
            _barType = AccessTools.TypeByName("EFT.UI.MenuTaskBar");
            if (_barType == null) { L.Warn("EFT.UI.MenuTaskBar not found — no menu button (use the open key instead)."); return; }
            L.Info("menu bar type: " + _barType.FullName);
            L.Debug("MenuTaskBar methods: " + string.Join(", ", _barType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(m => m.Name).Distinct().ToArray()));
            L.Debug("MenuTaskBar fields: " + string.Join(", ", _barType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(f => f.Name + ":" + f.FieldType.Name).ToArray()));
            int patched = 0;
            foreach (var m in _barType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != "Awake" && m.Name != "Show" && m.Name != "Init" || m.IsAbstract || m.ContainsGenericParameters) continue;
                try
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(MenuHook).GetMethod(nameof(AfterShow), BindingFlags.Static | BindingFlags.NonPublic)));
                    patched++;
                    L.Debug($"hooked MenuTaskBar.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name).ToArray())})");
                }
                catch (Exception e) { L.Warn($"couldn't hook MenuTaskBar.{m.Name}: {e.GetBaseException().Message}"); }
            }
            L.Info($"menu bar: {patched} method(s) hooked; the bar is also looked for every 1.5 s until the button is in.");
        }

        private static void AfterShow(object __instance, MethodBase __originalMethod)
        {
            try
            {
                L.Debug($"MenuTaskBar.{__originalMethod?.Name} ran");
                if (__instance is Component c) { _bar = c; TryInject("after MenuTaskBar." + __originalMethod?.Name); }
            }
            catch (Exception e) { L.ErrorOnce("MenuTaskBar hook", e); }
        }

        // ---------------------------------------------------------------- game screen changes

        private static bool _screenHooked;
        private static float _nextScreenTry;

        /// <summary>Close the screen whenever the game changes screen (Character, Trading, Hideout, a raid…):
        /// EftScreenManager.Instance.OnScreenChanged — the same event MoxoPixel's Menu Overhaul listens to.</summary>
        private static void HookScreenChanges()
        {
            if (_screenHooked || Time.realtimeSinceStartup < _nextScreenTry) return;
            _nextScreenTry = Time.realtimeSinceStartup + 2f;
            try
            {
                var t = AccessTools.TypeByName("EFT.UI.Screens.EftScreenManager");
                var mgr = t?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)?.GetValue(null, null);
                if (mgr == null) return;
                var ev = mgr.GetType().GetEvent("OnScreenChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (ev == null) { _screenHooked = true; L.Warn("EftScreenManager.OnScreenChanged not found — the screen closes only on menu bar clicks / Esc / the key"); return; }
                var invoke = ev.EventHandlerType.GetMethod("Invoke");
                var ps = invoke.GetParameters().Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name)).ToArray();
                var call = System.Linq.Expressions.Expression.Call(typeof(MenuHook).GetMethod(nameof(OnScreenChanged), BindingFlags.Static | BindingFlags.NonPublic),
                    ps.Length > 0 ? (System.Linq.Expressions.Expression)System.Linq.Expressions.Expression.Convert(ps[0], typeof(object)) : System.Linq.Expressions.Expression.Constant(null));
                var handler = System.Linq.Expressions.Expression.Lambda(ev.EventHandlerType, call, ps).Compile();
                ev.AddEventHandler(mgr, handler);
                _screenHooked = true;
                L.Info($"listening to the game's screen changes ({t.FullName}.OnScreenChanged)");
            }
            catch (Exception e) { _screenHooked = true; L.Error("hooking screen changes", e); }
        }

        private static void OnScreenChanged(object screen)
        {
            L.Debug($"game screen changed to {screen}");
            CurrentScreen = screen?.ToString() ?? "";
            ScreenChangedAt = Time.realtimeSinceStartup;
            if (ProgScreen.IsOpen) ProgScreen.Close("game screen changed to " + screen);
            if (_pendingOpen != null && CurrentScreen == "MainMenu") _pendingAt = Time.realtimeSinceStartup + .1f; // open just after the menu is back
        }

        /// <summary>The game screen shown right now (from EftScreenManager), e.g. MainMenu, Inventory, Trader.</summary>
        public static string CurrentScreen = "MainMenu";
        private static string _pendingOpen;
        private static float _pendingAt;

        // ---- when Progression must stay out of the way: deploying to a raid, in a raid, loading…
        // screens it can be opened from (it goes back to the main menu first); everything else — raid side / location
        // selection, matchmaking, "time has come", countdown, the raid itself, post-raid screens — blocks it
        private static readonly HashSet<string> OpenFrom = new HashSet<string> { "MainMenu", "Inventory", "Trader", "FleaMarket", "Handbook", "Messenger", "Hideout", "Settings" };
        private static PropertyInfo _worldInstantiated;
        private static bool _worldTried;

        /// <summary>In a raid: the game world exists (also while the in-raid Esc menu shows the main menu).</summary>
        public static bool InRaid()
        {
            try
            {
                if (!_worldTried)
                {
                    _worldTried = true;
                    var gw = AccessTools.TypeByName("EFT.GameWorld");
                    _worldInstantiated = gw == null ? null : AccessTools.TypeByName("Comfort.Common.Singleton`1")?.MakeGenericType(gw).GetProperty("Instantiated", BindingFlags.Public | BindingFlags.Static);
                    L.Info(_worldInstantiated != null ? "raid check: Singleton<EFT.GameWorld>.Instantiated" : "raid check: GameWorld not found — only the screen type is used");
                }
                return _worldInstantiated != null && _worldInstantiated.GetValue(null, null) is bool b && b;
            }
            catch (Exception e) { L.ErrorOnce("raid check", e); return false; }
        }

        /// <summary>True while Progression must not open (and its tab / shortcut are greyed out or hidden).</summary>
        public static bool Blocked(out string why)
        {
            why = null;
            if (InRaid()) why = "in raid";
            else if (!string.IsNullOrEmpty(CurrentScreen) && !OpenFrom.Contains(CurrentScreen)) why = "on " + CurrentScreen;
            return why != null;
        }

        public static bool Blocked() => Blocked(out _);

        public static float ScreenChangedAt = -100;

        /// <summary>Safe moment for background icon work: on the main menu itself (not the stash / traders / flea, which
        /// draw their own icons), Progression closed, no screen change in the last 2 s, not deploying / in a raid.</summary>
        public static bool QuietMenu() => CurrentScreen == "MainMenu" && !ProgScreen.IsOpen && !Blocked()
            && Time.realtimeSinceStartup - ScreenChangedAt > 2f;

        private static bool _blockedShown;
        private static float _blockCheckAt;

        /// <summary>Greys our tab out (not clickable) while blocked, like the game's own tabs during deployment.</summary>
        private static void ApplyBlocked()
        {
            if (_button == null || Time.realtimeSinceStartup < _blockCheckAt) return;
            _blockCheckAt = Time.realtimeSinceStartup + .25f;
            bool blocked = Blocked(out var why);
            if (blocked == _blockedShown) return;
            _blockedShown = blocked;
            var g = _button.GetComponent<CanvasGroup>() ?? _button.AddComponent<CanvasGroup>();
            g.alpha = blocked ? .35f : 1f; g.interactable = !blocked; g.blocksRaycasts = !blocked;
            L.Info(blocked ? $"PROGRESSION tab disabled ({why})" : "PROGRESSION tab enabled again");
            if (blocked && ProgScreen.IsOpen) ProgScreen.Close("blocked: " + why);
            // going into a raid (picking a side / map, deploying): the screen's kept pictures are let go
            if (blocked && (why == "in raid" || why.StartsWith("on SelectRaidSide") || why.StartsWith("on SelectLocation") || why.StartsWith("on MatchMaker") || why.StartsWith("on TimeHasCome")))
                ProgScreen.Unload(why);
            if (!blocked) Enable(_button, "unblocked");
        }

        /// <summary>Opening from another game screen (Character, Traders…): first go back to the main menu like the
        /// MAIN MENU button does, then open — otherwise that screen stays up under ours. True if it is doing that.</summary>
        public static bool GoToMainMenuThen(string why)
        {
            if (string.IsNullOrEmpty(CurrentScreen) || CurrentScreen == "MainMenu" || _bar == null || Blocked()) return false;
            try
            {
                var btn = _bar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MainMenuButton");
                if (btn == null) { L.Debug("MainMenuButton not found — opening on top"); return false; }
                L.Info($"on {CurrentScreen}: going back to the main menu first, then opening");
                _pendingOpen = why;
                _pendingAt = Time.realtimeSinceStartup + 1.5f; // opens anyway if no screen change comes
                var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                if (ExecuteEvents.ExecuteHierarchy(btn.gameObject, data, ExecuteEvents.pointerClickHandler) == null)
                {
                    var toggle = btn.GetComponentInChildren<Toggle>(true);
                    if (toggle != null) toggle.isOn = true;
                    else btn.GetComponentInChildren<Button>(true)?.onClick.Invoke();
                }
                return true;
            }
            catch (Exception e) { L.ErrorOnce("going back to the main menu", e); _pendingOpen = null; return false; }
        }

        public static void Tick()
        {
            if (_pendingOpen != null && Time.realtimeSinceStartup >= _pendingAt)
            {
                var why = _pendingOpen;
                _pendingOpen = null;
                CurrentScreen = "MainMenu";
                ProgScreen.Open(why + ", after going back to the main menu");
            }
            HookScreenChanges();
            ApplyBlocked();
            // the screen closes when the menu bar goes away (raid, loading screen…)
            if (ProgScreen.IsOpen && _bar != null && !_bar.gameObject.activeInHierarchy) ProgScreen.Close("menu bar hidden");
            if (ProgScreen.IsOpen && _bar != null && UnityInput.Current.GetMouseButtonDown(0)) ClickedElsewhereOnBar();

            if (_barType == null || !ProgressionPlugin.InjectButton.Value) return;
            if (_button != null)
            {
                // the game greys tabs out at times (e.g. while loading); ours must come back with the others
                if (Time.realtimeSinceStartup >= _nextTry)
                {
                    _nextTry = Time.realtimeSinceStartup + 1.5f;
                    var real = _candidates.FirstOrDefault(t => t != null && t.name != "LevelGateProgressionButton");
                    if ((real == null || Usable(real)) && !Blocked()) Enable(_button, "check");
                }
                return;
            }
            if (Time.realtimeSinceStartup < _nextTry) return;
            _nextTry = Time.realtimeSinceStartup + 1.5f;
            if (_bar == null)
            {
                _bar = UnityEngine.Object.FindObjectOfType(_barType) as Component;
                if (_bar == null) { if (++_tries % 20 == 1) L.Debug($"menu bar not in the scene yet (try {_tries})"); return; }
                L.Info($"menu bar found in the scene: '{Path(_bar.transform)}'");
            }
            TryInject("timer");
        }

        /// <summary>A click on another menu bar button (Character, Trading…) closes the screen.</summary>
        private static void ClickedElsewhereOnBar()
        {
            var mouse = (Vector2)UnityInput.Current.mousePosition;
            var bar = BarRect;
            if (bar == null || !Contains(bar, mouse)) return;
            var own = ButtonRect;
            if (own != null && Contains(own, mouse)) return;
            ProgScreen.Close("another menu button clicked");
        }

        /// <summary>Is a screen point inside a rect — with the camera of the rect's canvas (the game's menus are drawn by a camera).</summary>
        public static bool Contains(RectTransform rt, Vector2 screenPoint)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, cam);
        }

        private static void TryInject(string why)
        {
            if (_button != null || _bar == null || !ProgressionPlugin.InjectButton.Value) return;
            if (!_bar.gameObject.activeInHierarchy) { L.Debug($"menu bar not active yet ({why})"); return; }
            if (!_dumped) { _dumped = true; L.Info("menu bar objects (for troubleshooting):\n" + Hierarchy(_bar.transform, 5)); }

            FindCandidates();
            if (_candidates.Count == 0) { L.Warn($"no buttons found in the menu bar ({why}) — trying again later. The object list is above."); return; }
            L.Info($"menu buttons found: {string.Join(" | ", _candidates.Select(t => $"{t.name}{(t.gameObject.activeSelf ? "" : " (hidden)")} [{string.Join(",", t.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().Name).ToArray())}]").ToArray())}");

            var template = PickTemplate();
            // at start-up every tab is greyed out until the profile is loaded; a copy made then stays greyed out
            if (!Usable(template))
            {
                if (_waitLogged++ % 10 == 0) L.Debug($"'{template.name}' is still greyed out ({why}) — waiting until the game enables its tabs before copying it");
                return;
            }
            L.Info($"copying menu button '{Path(template)}' ({why})");
            try
            {
                // copied inside a hidden holder, so the game's button code doesn't start up on the copy before it's switched off
                GameObject holder = null, clone = null;
                try
                {
                holder = new GameObject("LevelGateProgressionHolder");
                holder.SetActive(false);
                holder.transform.SetParent(template.parent, false);
                clone = UnityEngine.Object.Instantiate(template.gameObject, holder.transform, false);
                clone.name = "LevelGateProgressionButton";
                clone.SetActive(true);
                Neutralize(clone);
                SetLabel(clone);
                SetIcon(clone);
                MakeClickable(clone);
                clone.transform.SetParent(template.parent, false);
                clone.transform.SetSiblingIndex(template.GetSiblingIndex()); // left of the tab it was copied from
                UnityEngine.Object.Destroy(holder);
                Enable(clone, "just added");
                var layout = template.parent.GetComponent<LayoutGroup>();
                if (layout == null && clone.transform is RectTransform crt && template is RectTransform trt)
                {
                    crt.anchoredPosition = trt.anchoredPosition + new Vector2(trt.rect.width + 6, 0);
                    L.Info($"the button row has no layout group — placed the button by hand at {crt.anchoredPosition}");
                }
                else L.Debug("button row layout: " + (layout == null ? "none" : layout.GetType().Name));
                _button = clone;
                LogLayout(template, clone);
                clone = null;
                L.Info("PROGRESSION button added to the menu bar.");
                }
                finally
                {
                    // a failed attempt leaves nothing behind (the next try starts clean)
                    if (clone != null) { UnityEngine.Object.Destroy(clone); _toggle = null; }
                    if (holder != null) UnityEngine.Object.Destroy(holder);
                }
            }
            catch (Exception e) { L.Error("adding the menu button", e); _button = null; }
        }

        private static int _waitLogged;

        /// <summary>Is this tab enabled (its Toggle interactable, no CanvasGroup above it switching it off)?</summary>
        private static bool Usable(Transform tab)
        {
            var toggle = tab.GetComponentInChildren<Toggle>(true);
            if (toggle != null && !toggle.interactable) return false;
            foreach (var g in tab.GetComponentsInParent<CanvasGroup>(true))
                if (!g.interactable || !g.blocksRaycasts || g.alpha < .6f) return false;
            return true;
        }

        /// <summary>Makes sure our copy is enabled and clickable (logs what it had to change).</summary>
        private static void Enable(GameObject clone, string why)
        {
            var fixes = new List<string>();
            foreach (var g in clone.GetComponentsInChildren<CanvasGroup>(true))
            {
                if (!g.interactable || !g.blocksRaycasts || g.alpha < 1f) fixes.Add($"CanvasGroup on '{g.name}' (alpha {g.alpha:0.00}, interactable {g.interactable}, raycasts {g.blocksRaycasts})");
                g.alpha = 1f; g.interactable = true; g.blocksRaycasts = true; g.ignoreParentGroups = false;
            }
            foreach (var t in clone.GetComponentsInChildren<Toggle>(true))
            {
                if (!t.interactable) fixes.Add($"Toggle on '{t.name}' not interactable");
                if (!t.enabled) fixes.Add($"Toggle on '{t.name}' disabled");
                t.interactable = true; t.enabled = true;
            }
            foreach (var gr in clone.GetComponentsInChildren<Graphic>(true))
                if (gr.GetComponent<Toggle>() != null && !gr.raycastTarget) { gr.raycastTarget = true; fixes.Add($"raycast on '{gr.name}'"); }
            if (fixes.Count > 0) L.Info($"PROGRESSION tab re-enabled ({why}): " + string.Join("; ", fixes.ToArray()));
        }

        private static void FindCandidates()
        {
            _candidates.Clear();
            // each tab is a holder (Character, Merchants, Handbook…) with a Toggle inside (CharacterButton…)
            foreach (var toggle in _bar.GetComponentsInChildren<Toggle>(true))
            {
                var holder = toggle.transform.parent;
                if (holder == null || holder == _bar.transform || holder.name == "LevelGateProgressionButton" || _candidates.Contains(holder)) continue;
                _candidates.Add(holder);
            }
        }

        private static Transform PickTemplate()
        {
            string want = ProgressionPlugin.ButtonTemplate.Value?.Trim();
            if (string.IsNullOrEmpty(want)) want = "Character"; // a plain tab: no counters next to it (Handbook's made the copy wider)
            if (!string.IsNullOrEmpty(want))
            {
                var hit = _candidates.FirstOrDefault(t => t.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) return hit;
                L.Warn($"Advanced > CopyButton '{want}' matches no button name — picking one by itself.");
            }
            // the row holding the most tabs is the bar; take its last visible tab
            var row = _candidates.GroupBy(t => t.parent).OrderByDescending(g => g.Count()).First();
            L.Debug($"button row: '{Path(row.Key)}' with {row.Count()} buttons");
            return row.LastOrDefault(t => t.gameObject.activeSelf) ?? row.Last();
        }

        /// <summary>Switches off the copied button's own behaviour (toggle group, localisation that would reset the text).</summary>
        private static Toggle _toggle;

        /// <summary>The copy keeps its Toggle (so it hovers / lights up like the others) but loses its old wiring:
        /// no toggle group, fresh events, no localisation (it would reset the text), no counters or tooltip.</summary>
        private static void Neutralize(GameObject clone)
        {
            foreach (Transform ch in clone.transform.Cast<Transform>().ToList())
            {
                string n = ch.name; // read before destroying it
                if (n.StartsWith("NewInformation")) { UnityEngine.Object.DestroyImmediate(ch.gameObject); L.Debug($"  removed '{n}' (counters)"); }
            }
            foreach (var c in clone.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (c is ToggleGroup g) { g.enabled = false; continue; }
                if (c is Toggle tg)
                {
                    tg.group = null;
                    tg.onValueChanged = new Toggle.ToggleEvent();
                    tg.isOn = false;
                    _toggle ??= tg;
                    L.Debug($"  kept {n} on '{c.name}' (fresh events, no group)");
                    continue;
                }
                if (c is Behaviour b && (n.Contains("Localiz") || n.Contains("Tooltip") || n.Contains("Hover")))
                {
                    b.enabled = false;
                    L.Debug($"  disabled {n} on '{c.name}'");
                }
            }
        }

        /// <summary>Lights our tab up while the screen is open (and not otherwise).</summary>
        public static void SetOn(bool on)
        {
            if (_toggle == null || _toggle.isOn == on) return;
            try { _toggle.isOn = on; } catch (Exception e) { L.ErrorOnce("tab state", e); }
        }

        private static void SetLabel(GameObject clone)
        {
            string label = ProgressionPlugin.ButtonLabel.Value;
            int set = 0;
            foreach (var c in clone.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c is Text t) { L.Debug($"  label (Text) '{t.text}' -> '{label}' on '{c.name}'"); t.text = label; set++; continue; }
                if (Ui.IsTmp(c))
                {
                    L.Debug($"  label (TMP {c.GetType().Name}) '{Refl.Get(c, "text")}' -> '{label}' on '{c.name}'");
                    Refl.Set(c, "text", label);
                    if (Ui.GameFont == null) { Ui.GameFont = Refl.Get(c, "font"); L.Info("game font for the screen: " + (Ui.GameFont as UnityEngine.Object)?.name); }
                    set++;
                }
            }
            if (set == 0) L.Warn("the copied button has no text to change — it keeps its old label.");
        }

        /// <summary>The copied button's icon becomes a small rank diamond.</summary>
        private static void SetIcon(GameObject clone)
        {
            var icons = clone.GetComponentsInChildren<Image>(true).Where(i => i.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            L.Debug($"  icon images: {(icons.Count == 0 ? "none named *icon*" : string.Join(", ", icons.Select(i => i.name).ToArray()))}");
            foreach (var img in icons)
            {
                // the layout sizes the icon from its picture: keep the original icon's size (a bit smaller: the diamond is bold)
                var size = img.rectTransform.rect.size;
                if (size.x < 4) size = new Vector2(26, 26);
                img.sprite = Ui.TabIcon(); // red block, like the EXPANSIONS icon
                img.color = Color.white;
                img.preserveAspect = true;
                var le = img.GetComponent<LayoutElement>() ?? img.gameObject.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = size.x * .8f;
                le.minHeight = le.preferredHeight = size.y * .8f;
                le.flexibleWidth = le.flexibleHeight = 0;
                L.Debug($"  icon '{img.name}' kept at {le.preferredWidth:0}x{le.preferredHeight:0} (original {size.x:0}x{size.y:0})");
            }
        }

        private static void MakeClickable(GameObject clone)
        {
            // the game's tab (AnimatedToggle) handles clicks itself without firing onValueChanged, so a click
            // catcher goes on every object that can receive the click (Unity calls all of them on that object)
            int n = 0;
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                if (t == clone.transform || t.GetComponent<Selectable>() != null || t.GetComponent<Graphic>() is Graphic g && g.raycastTarget)
                {
                    t.gameObject.AddComponent<TabClick>();
                    n++;
                }
            L.Debug($"  click catchers on {n} object(s): {string.Join(", ", clone.GetComponentsInChildren<TabClick>(true).Select(c => c.name).ToArray())}");
            if (_toggle != null) return;
            var graphic = clone.GetComponent<Graphic>();
            if (graphic == null)
            {
                var img = clone.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                graphic = img;
                L.Debug("  added a see-through Image so the button can be clicked");
            }
            graphic.raycastTarget = true;
            var button = clone.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { L.Info("PROGRESSION button clicked"); ProgScreen.Toggle("menu button"); });
        }

        /// <summary>Size and spacing of the copy next to the tab it was copied from (for "it's wider than the others").</summary>
        private static void LogLayout(Transform template, GameObject clone)
        {
            string Describe(Transform t)
            {
                var parts = new List<string>();
                foreach (var x in t.GetComponentsInChildren<Transform>(true).Take(6))
                {
                    var rt = x as RectTransform;
                    var hl = x.GetComponent<HorizontalLayoutGroup>();
                    var le = x.GetComponent<LayoutElement>();
                    parts.Add($"{x.name} w={rt?.rect.width:0}" + (hl != null ? $" pad={hl.padding.left}/{hl.padding.right} gap={hl.spacing:0}" : "") + (le != null ? $" le(min {le.minWidth:0}, pref {le.preferredWidth:0})" : ""));
                }
                return string.Join(" | ", parts.ToArray());
            }
            L.Debug("layout of the original: " + Describe(template));
            L.Debug("layout of the copy:     " + Describe(clone.transform));
        }

        public static void Dump()
        {
            L.Info($"menu: bar type {(_barType == null ? "missing" : _barType.FullName)}, bar {(_bar == null ? "not found" : Path(_bar.transform) + (_bar.gameObject.activeInHierarchy ? " (visible)" : " (hidden)"))}, button {(_button == null ? "not added" : Path(_button.transform))}");
            if (_bar != null) L.Info("menu bar objects:\n" + Hierarchy(_bar.transform, 6));
        }

        public static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts.ToArray());
        }

        private static string Hierarchy(Transform root, int depth)
        {
            var sb = new StringBuilder();
            void Walk(Transform t, int d)
            {
                var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray();
                string text = "";
                foreach (var c in t.GetComponents<Component>())
                    if (c != null && (c is Text || Ui.IsTmp(c))) text = $" text='{(c is Text tx ? tx.text : Refl.Get(c, "text"))}'";
                sb.Append(new string(' ', d * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (hidden)")
                  .Append(" [").Append(string.Join(",", comps)).Append("]").Append(text).Append('\n');
                if (d < depth) foreach (Transform ch in t) Walk(ch, d + 1);
            }
            Walk(root, 0);
            return sb.ToString();
        }
    }
}

namespace LevelGate.Progression
{
    /// <summary>Catches the click on our copied menu tab (the game's own tab code doesn't tell us).</summary>
    internal sealed class TabClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        private static int _lastFrame = -1;

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left || Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            L.Info($"PROGRESSION tab clicked (on '{name}')");
            ProgScreen.Toggle("menu tab");
        }
    }
}
