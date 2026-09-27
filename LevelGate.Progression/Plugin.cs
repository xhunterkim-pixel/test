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
        public const string Version = "0.9.16";

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
        internal static ConfigEntry<float> CameraTurn;
        internal static ConfigEntry<bool> BlurBackground;
        internal static ConfigEntry<bool> PerformanceMode;
        internal static ConfigEntry<bool> MenuShortcut;
        internal static ConfigEntry<bool> FixStashIcons;
        internal static ConfigEntry<int> LastSeenLevel;

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
            BottomMargin = Config.Bind("Screen", "BottomMargin", 26f, "Space left free at the bottom, so the game's menu bar (Main menu, Character, Traders…) stays visible and clickable (1920x1080 pixels).");
            if (Mathf.Abs(BottomMargin.Value - 68f) < .01f) { BottomMargin.Value = 26f; } // old default: the screen now reaches down to the menu bar
            SortOrder = Config.Bind("Screen", "SortOrder", 100, "Drawing order of the screen (higher = on top of more of the game's menus).");
            MaxTilesPerCategory = Config.Bind("Screen", "MaxTilesPerCategory", 60, "Items shown per category for one level (the rest are counted).");
            TileSize = Config.Bind("Screen", "TileSize", 92f, "Height of an item tile (the width is a bit more). Smaller = more items fit.");
            EmbedInGameUi = Config.Bind("Screen", "InsideGameUi", true,
                "Put the screen inside the game's own UI (right after the main menu), so the game's windows (inspect…) open on top of it. Off: its own canvas over everything.");
            Opacity = Config.Bind("Screen", "Opacity", .85f, new ConfigDescription("How solid the screen's background is (lower = more of the game's menu background shows through, like the battle pass).", new AcceptableValueRange<float>(.3f, 1f)));
            CameraTurn = Config.Bind("Screen", "CameraTurnDegrees", -75f, new ConfigDescription(
                "How far the menu's 3D background turns while the screen is open, like the game does for Character / Traders (negative = to the left, positive = right, 0 = off).",
                new AcceptableValueRange<float>(-150f, 150f)));
            BlurBackground = Config.Bind("Screen", "BlurBackground", true, "Switch on a blur effect on the background camera while open (if the game's camera has one).");
            PerformanceMode = Config.Bind("Screen", "PerformanceMode", false,
                "Lighter pictures for slower PCs: the big item picture and the card pictures are drawn at a smaller size, the rank emblems stand still, and the list shows up to 12 items per category.");
            PerformanceMode.SettingChanged += (_, __) => ProgScreen.Refresh();
            FixStashIcons = Config.Bind("Screen", "FixStashIcons", false,
                "Tick once to redraw every level-list item's icon at stash size (clears big icons left in the game's icon cache by older builds). It turns itself off again. Takes a few minutes; the stash shows loading icons meanwhile.");
            FixStashIcons.SettingChanged += (_, __) =>
            {
                if (!FixStashIcons.Value) return;
                FixStashIcons.Value = false;
                if (GameItems.RepairLeft == 0) GameItems.RepairAll(ProgData.Levels.Keys.ToList());
            };
            MenuShortcut = Config.Bind("Menu Button", "MainMenuShortcut", true, "A PROGRESSION block in the main menu's bottom-left corner (like the game's EXPANSIONS one) that opens the screen.");
            LastSeenLevel = Config.Bind("Menu Button", "LastSeenLevel", 0, new ConfigDescription("Your level when you last opened the screen (the NEW tag shows after a level-up). Set by the plugin.", null, new ConfigurationManagerAttributes { Browsable = false }));
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

        private void LateUpdate()
        {
            try { MenuCamera.LateTick(); } catch (Exception e) { L.ErrorOnce("late update", e); }
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
                MenuWidget.Tick();
                GameItems.RepairTick();
            }
            catch (Exception e) { L.ErrorOnce("update", e); }
        }

        /// <summary>Is a text box (search, chat…) focused? Then letter keys are typing, not shortcuts.</summary>
        internal static bool Typing()
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
        public static void Info(string s) { Source?.LogInfo("[Progression] " + s); File("info ", s); }
        public static void Debug(string s) { if (Verbose) { Source?.LogInfo("[Progression] (debug) " + s); File("debug", s); } }
        public static void Warn(string s) { Source?.LogWarning("[Progression] " + s); File("WARN ", s); }
        public static void Error(string where, Exception e) { Source?.LogError($"[Progression] error in {where}: {e}"); File("ERROR", where + ": " + e); }

        // Progression.log next to the plugin, written to disk line by line: BepInEx's own log is buffered, so
        // after a game crash its last lines are missing — this file still shows the last step that ran.
        private static System.IO.StreamWriter _file;
        private static bool _fileTried;

        /// <summary>A step marker that only goes to Progression.log (for finding where a crash happened).</summary>
        public static void Step(string s) { LastStep = s; File("step ", s); }
        public static string LastStep = "";

        private static readonly object _fileLock = new object();

        private static void File(string kind, string s)
        {
            lock (_fileLock)
            try
            {
                if (_file == null)
                {
                    if (_fileTried) return;
                    _fileTried = true;
                    var dir = System.IO.Path.GetDirectoryName(typeof(L).Assembly.Location) ?? ".";
                    _file = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "Progression.log"), false) { AutoFlush = true };
                    _file.WriteLine($"LevelGate Progression {ProgressionPlugin.Version} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                _file.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {kind} {s}");
            }
            catch { }
        }
        /// <summary>Same error from a per-frame place: written once, not every frame.</summary>
        public static void ErrorOnce(string where, Exception e)
        {
            if (_once.Add(where + e.GetType().Name + e.Message)) Error(where + " (further identical errors not logged)", e);
        }
    }
}

namespace LevelGate.Progression
{
    /// <summary>Read by BepInEx Configuration Manager (by name): hides a setting from its window.</summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
    }
}
