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

        public static void Open() => Play("ButtonBottomBarClick", "ButtonClick", "Click");
        public static void Click() => Play("ButtonClick", "Click");
        public static void Page() => Play("MenuDropdownSelect", "ButtonClick", "Click");
    }
}
