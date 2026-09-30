using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace LevelGate.Progression
{
    /// <summary>
    /// The game's own UI sounds (the click you hear on the menu tabs): GUISounds.PlayUISound(EUISoundType).
    /// Looked up by name; the sound names the game has are logged once.
    /// </summary>
    internal static class Sounds
    {
        private static bool _init;
        private static object _gui;
        private static MethodInfo _play;
        private static Type _enum;

        private static void Init()
        {
            if (_init && _gui != null) return;
            _init = true;
            try
            {
                var t = AccessTools.TypeByName("GUISounds") ?? AccessTools.TypeByName("EFT.UI.GUISounds");
                if (t == null) { L.Info("sounds: GUISounds not found — no sounds"); return; }
                _play ??= t.GetMethods(Refl.All).FirstOrDefault(m => m.Name == "PlayUISound" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
                if (_play == null) { L.Info("sounds: GUISounds.PlayUISound(enum) not found — no sounds"); return; }
                if (_enum == null)
                {
                    _enum = _play.GetParameters()[0].ParameterType;
                    L.Info($"sounds: GUISounds.PlayUISound({_enum.Name}); sounds: {string.Join(", ", Enum.GetNames(_enum))}");
                }
                _gui = AccessTools.TypeByName("Comfort.Common.Singleton`1")?.MakeGenericType(t).GetProperty("Instance", Refl.All)?.GetValue(null, null)
                    ?? (typeof(UnityEngine.Object).IsAssignableFrom(t) ? UnityEngine.Object.FindObjectOfType(t) : null);
                if (_gui == null) L.Debug("sounds: GUISounds not created yet");
            }
            catch (Exception e) { L.ErrorOnce("sounds", e); }
        }

        /// <summary>Plays the first of these sound names the game has.</summary>
        public static void Play(params string[] names)
        {
            Init();
            if (_gui == null || _play == null) return;
            try
            {
                var all = Enum.GetNames(_enum);
                var pick = names.Select(n => all.FirstOrDefault(a => a.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(n => n != null)
                    ?? names.Select(n => all.FirstOrDefault(a => a.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)).FirstOrDefault(n => n != null);
                if (pick == null) { L.ErrorOnce("sounds", new Exception("none of " + string.Join("/", names) + " exist")); return; }
                _play.Invoke(_gui, new[] { Enum.Parse(_enum, pick) });
                L.Trace("sound: " + pick);
            }
            catch (Exception e) { L.ErrorOnce("playing a sound", e); }
        }

        public static void Click() => Play("ButtonClick", "Click");

        // ---- 1.0.19: the screen's own UI sounds (sounds\<name>.wav, named for when they play); the game's sound if missing

        private static float _hoverAt = -1;

        private static bool Own(string name) => !Sfx.UseGame && Sfx.Play(name);

        /// <summary>Opening the screen (the tab, the shortcut, P).</summary>
        public static void Open() { if (!Own("enter_progression_ui")) Play("ButtonBottomBarClick", "ButtonClick", "Click"); }
        /// <summary>The pointer onto anything in the screen (tiles, cards, buttons); not more than ~25 a second.</summary>
        public static void Hover()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now - _hoverAt < .04f) return;
            _hoverAt = now;
            if (!Own("hover")) Play("ButtonOver");
        }
        /// <summary>The pointer onto the PROGRESSION tab / shortcut on the game's main menu.</summary>
        public static void MenuHover() { if (!Own("hover_over_progression_menu")) Play("ButtonOver"); }
        /// <summary>A level card clicked.</summary>
        public static void SelectCard() { if (!Own("select_level_card")) Click(); }
        /// <summary>One level left / right (A / D, the arrows, the ‹ › buttons, holding, dragging).</summary>
        /// <summary>1.0.20: its own sound each way — level_card_left_a.wav (A, left) / level_card_right_d.wav (D, right); then
        /// the older shared using_a_or_d_for_level_card.wav, then the game's click.</summary>
        public static void StepLevel(int dir = 0)
        {
            if (dir < 0 && Own("level_card_left_a")) return;
            if (dir > 0 && Own("level_card_right_d")) return;
            if (!Own("using_a_or_d_for_level_card")) Click();
        }
        /// <summary>Trying to go before level 1 or past the last level.</summary>
        public static void Edge() { if (!Own("going_before_level_1_or_level_79")) Play("ErrorMessage", "ButtonClick"); }
        /// <summary>A reward picked (clicked, or W / S).</summary>
        public static void SelectItem() { if (!Own("select_level_item")) Play("MenuContextMenu", "ButtonClick"); }
        /// <summary>A page of cards turned (Q / E, the wheel, the page bar).</summary>
        public static void Page() { if (!Own("pressing_q_or_e_for_next_pages")) Play("MenuDropdownSelect", "ButtonClick", "Click"); }
        /// <summary>1.0.20, XP animation: a level's card unlocking.</summary>
        public static void CardUnlocked() { if (!Own("card_unlocked")) Play("ButtonOver", "ButtonClick"); }
        /// <summary>1.0.20, XP animation: the bar passing each seventh of a level — xp_bar_tick_1..7, rising as it fills.</summary>
        public static void XpTick(int k) { if (!Own("xp_bar_tick_" + UnityEngine.Mathf.Clamp(k, 1, 7))) { } }
        /// <summary>A group of rewards opened / closed (its +N box).</summary>
        public static void Group(bool open) { if (!Own(open ? "expand_group_items" : "collapse_group_items")) Click(); }
    }
}
