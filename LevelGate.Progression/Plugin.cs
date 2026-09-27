using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LevelGate.Progression
{
    // -----------------------------------------------------------------------------
    // LevelGate Progression — a Call of Duty style "Progression" screen inside the
    // Tarkov main menu (a PROGRESSION button next to Character / Trading / Flea…).
    //
    // Separate from LevelGate.dll: it only READS LevelGate's
    // BepInEx\plugins\LevelGate\config\level_requirements.json (never writes it).
    //
    // Everything about the game's own classes is looked up by name at run time and
    // written to the log (lots of "[Progression]" lines), so a game update shows up
    // as a readable log line instead of a crash.
    // -----------------------------------------------------------------------------
    [BepInPlugin(Guid, Name, Version)]
    public class ProgressionPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.kkyangg.levelgate.progression";
        public const string Name = "LevelGate Progression";
        // MAJOR.MINOR.PATCH — see CHANGELOG.md
        public const string Version = "0.4.0";

        internal static ProgressionPlugin Instance;
        internal static ConfigEntry<KeyboardShortcut> OpenKey;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;
        internal static ConfigEntry<bool> InjectButton;
        internal static ConfigEntry<string> ButtonTemplate;
        internal static ConfigEntry<string> ButtonLabel;
        internal static ConfigEntry<bool> VerboseLog;
        internal static ConfigEntry<float> TopMargin;
        internal static ConfigEntry<float> BottomMargin;
        internal static ConfigEntry<int> SortOrder;
        internal static ConfigEntry<int> MaxTilesPerCategory;
        internal static ConfigEntry<bool> FreeItemsAtLevel1;
        internal static ConfigEntry<float> TileSize;
        internal static ConfigEntry<bool> EmbedInGameUi;
        internal static ConfigEntry<bool> HideMainMenu;
        internal static ConfigEntry<float> Opacity;

        private void Awake()
        {
            Instance = this;
            L.Source = Logger;

            OpenKey = Config.Bind("General", "OpenScreenKey", new KeyboardShortcut(KeyCode.P),
                "Opens / closes the Progression screen in the main menu (not while typing in a text box).");
            InjectButton = Config.Bind("Menu Button", "AddButton", true,
                "Add a PROGRESSION button to the main menu bar (next to Character, Trading, Flea Market…).");
            ButtonTemplate = Config.Bind("Menu Button", "CopyButton", "",
                "Which menu button to copy the look of (part of its object name, e.g. 'Handbook'). Empty = pick one by itself; the log lists the names it found.");
            ButtonLabel = Config.Bind("Menu Button", "Label", "PROGRESSION", "Text on the menu button.");
            TopMargin = Config.Bind("Screen", "TopMargin", 0f, "Space left free at the top of the screen (1920x1080 pixels).");
            BottomMargin = Config.Bind("Screen", "BottomMargin", 68f, "Space left free at the bottom, so the menu bar stays clickable (1920x1080 pixels).");
            SortOrder = Config.Bind("Screen", "SortOrder", 100, "Drawing order of the screen (higher = on top of more of the game's menus).");
            MaxTilesPerCategory = Config.Bind("Screen", "MaxTilesPerCategory", 60, "Items shown per category for one level (the rest are counted).");
            TileSize = Config.Bind("Screen", "TileSize", 92f, "Height of an item tile (the width is a bit more). Smaller = more items fit.");
            EmbedInGameUi = Config.Bind("Screen", "InsideGameUi", true,
                "Put the screen inside the game's own UI (right after the main menu), so the game's windows (inspect…) open on top of it. Off: its own canvas over everything.");
            Opacity = Config.Bind("Screen", "Opacity", .85f, new ConfigDescription("How solid the screen's background is (lower = more of the game's menu background shows through, like the battle pass).", new AcceptableValueRange<float>(.3f, 1f)));
            HideMainMenu = Config.Bind("Screen", "HideMainMenu", true, "Fade out the main menu (ESCAPE FROM TARKOV, CHARACTER, TRADING, EXIT…) while the screen is open.");
            FreeItemsAtLevel1 = Config.Bind("Screen", "CountFreeItemsAtLevel1", false,
                "Count items without a limit as level 1 unlocks (that's most of the game's items).");
            VerboseLog = Config.Bind("Debug", "VerboseLog", true,
                "Write a lot of detail to the BepInEx log (menu objects found, what was copied, every open / page / level change). For troubleshooting.");
            DumpKey = Config.Bind("Debug", "DumpKey", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl),
                "Writes everything the plugin knows (menu bar objects, data, screen state) to the log.");

            L.Info($"{Name} {Version} starting. Unity {Application.unityVersion}, plugins folder: {Paths.PluginPath}");
            try
            {
                ProgData.Init();
                var harmony = new Harmony(Guid);
                MenuHook.Apply(harmony);
            }
            catch (Exception e) { L.Error("startup", e); }
            L.Info($"{Name} started. Open key: {OpenKey.Value}, dump key: {DumpKey.Value}, menu button: {(InjectButton.Value ? "on" : "off")}.");
        }

        private void Update()
        {
            try
            {
                if (DumpKey.Value.IsDown()) Dump();
                else if (OpenKey.Value.IsDown())
                {
                    if (Typing()) L.Debug($"open key {OpenKey.Value} ignored: typing in a text box");
                    else if (!MenuHook.BarVisible && !ProgScreen.IsOpen) L.Debug($"open key {OpenKey.Value} ignored: not in the main menu");
                    else { L.Info($"open key {OpenKey.Value} pressed"); ProgScreen.Toggle("hotkey"); }
                }
                ProgData.Tick();
                MenuHook.Tick();
                ProgScreen.Tick();
            }
            catch (Exception e) { L.ErrorOnce("update", e); }
        }

        /// <summary>Is a text box (search, chat…) focused? Then letter keys are typing, not shortcuts.</summary>
        private static bool Typing()
        {
            var go = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            return go != null && go.GetComponents<Component>().Any(c => c != null && c.GetType().Name.Contains("InputField"));
        }

        internal static void Dump()
        {
            L.Info("===== LevelGate Progression dump =====");
            ProgData.Dump();
            MenuHook.Dump();
            ProgScreen.Dump();
            L.Info("===== end of dump =====");
        }
    }

    /// <summary>Logging with a common prefix; Debug lines only when Debug > VerboseLog is on.</summary>
    internal static class L
    {
        internal static ManualLogSource Source;
        private static readonly System.Collections.Generic.HashSet<string> _once = new System.Collections.Generic.HashSet<string>();

        public static bool Verbose => ProgressionPlugin.VerboseLog?.Value ?? true;
        public static void Info(string s) => Source?.LogInfo("[Progression] " + s);
        public static void Debug(string s) { if (Verbose) Source?.LogInfo("[Progression] (debug) " + s); }
        public static void Warn(string s) => Source?.LogWarning("[Progression] " + s);
        public static void Error(string where, Exception e) => Source?.LogError($"[Progression] error in {where}: {e}");
        /// <summary>Same error from a per-frame place: written once, not every frame.</summary>
        public static void ErrorOnce(string where, Exception e)
        {
            if (_once.Add(where + e.GetType().Name + e.Message)) Error(where + " (further identical errors not logged)", e);
        }
    }
}
