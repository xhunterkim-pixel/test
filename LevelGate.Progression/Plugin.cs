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
        public const string Version = "0.9.22";

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
        internal static ConfigEntry<GraphicsQuality> Quality;
        internal static ConfigEntry<bool> MenuShortcut;
        internal static ConfigEntry<bool> FixStashIcons;
        internal static ConfigEntry<int> LastSeenLevel;

        private void Awake()
        {
            Instance = this;
            L.Source = Logger;

            // F12 (Configuration Manager): three sections, sorted by their number. Order = top to bottom within one.
            const string G = "1. General", Gfx = "2. Graphics", A = "3. Advanced";
            OpenKey = Config.Bind(G, "OpenScreenKey", new KeyboardShortcut(KeyCode.P), Desc(
                "Opens / closes the Progression screen in the main menu (not while typing in a text box).", 100));
            InjectButton = Config.Bind(G, "MenuBarButton", true, Desc(
                "Add a PROGRESSION button to the main menu bar (next to Character, Trading, Flea Market…).", 90));
            MenuShortcut = Config.Bind(G, "MainMenuShortcut", true, Desc(
                "A PROGRESSION block in the main menu's bottom-left corner (like the game's EXPANSIONS one) that opens the screen.", 80));
            HideMainMenu = Config.Bind(G, "HideMainMenu", true, Desc(
                "Fade out the main menu (ESCAPE FROM TARKOV, CHARACTER, TRADING, EXIT…) while the screen is open.", 70));
            BlurBackground = Config.Bind(G, "BlurBackground", true, Desc(
                "Blur the menu's 3D background while the screen is open (if the game's camera has a blur effect).", 60));

            Quality = Config.Bind(Gfx, "Quality", GraphicsQuality.Medium, Desc(
                "Low: lighter pictures and still rank emblems, for slower PCs (same as the PERFORMANCE MODE box on the screen).\n" +
                "Medium: sharp item pictures; weapons drawn at stash size.\n" +
                "High: weapons drawn extra sharp too. The game can carry big weapon pictures over to weapons in your stash, so every level-list weapon " +
                "(~160) is redrawn at stash size in the background after you close the screen (main menu only, never in a raid, at most every 10 minutes).", 100));
            Quality.SettingChanged += (_, __) => { L.Info($"graphics quality: {Quality.Value}"); ProgScreen.Refresh(); };
            FixStashIcons = Config.Bind(Gfx, "FixStashIcons", false, Desc(
                "Tick once to redraw every level-list item's icon at stash size (use it if a stash icon ever looks too big). It turns itself off again. " +
                "Takes a minute on the main menu; the stash shows loading icons meanwhile.", 90));
            FixStashIcons.SettingChanged += (_, __) =>
            {
                if (!FixStashIcons.Value) return;
                FixStashIcons.Value = false;
                if (GameItems.RepairLeft == 0) GameItems.RepairAll(ProgData.Levels.Keys.ToList());
            };

            ButtonLabel = Config.Bind(A, "ButtonLabel", "PROGRESSION", Desc("Text on the menu bar button.", 100));
            ButtonTemplate = Config.Bind(A, "CopyButton", "", Desc(
                "Which menu bar button to copy the look of (part of its object name, e.g. 'Handbook'). Empty = pick one by itself; the log lists the names it found.", 95));
            TopMargin = Config.Bind(A, "TopMargin", 0f, Desc("Space left free at the top of the screen (1920x1080 pixels).", 90));
            BottomMargin = Config.Bind(A, "BottomMargin", 26f, Desc(
                "Space left free at the bottom, so the game's menu bar (Main menu, Character, Traders…) stays visible and clickable (1920x1080 pixels).", 85));
            TileSize = Config.Bind(A, "TileSize", 92f, Desc("Height of an item tile (the width is a bit more). Smaller = more items fit.", 80));
            MaxTilesPerCategory = Config.Bind(A, "MaxTilesPerCategory", 60, Desc("Items shown per category for one level (the rest are counted).", 75));
            Opacity = Config.Bind(A, "Opacity", .85f, Desc(
                "How solid the screen's background is (lower = more of the game's menu background shows through).", 70, new AcceptableValueRange<float>(.3f, 1f)));
            CameraTurn = Config.Bind(A, "CameraTurnDegrees", -75f, Desc(
                "How far the menu's 3D background turns while the screen is open, like the game does for Character / Traders (negative = to the left, positive = right, 0 = off).",
                65, new AcceptableValueRange<float>(-150f, 150f)));
            SortOrder = Config.Bind(A, "SortOrder", 100, Desc("Drawing order of the screen (higher = on top of more of the game's menus).", 60));
            EmbedInGameUi = Config.Bind(A, "InsideGameUi", true, Desc(
                "Put the screen inside the game's own UI (right after the main menu), so the game's windows (inspect…) open on top of it. Off: its own canvas over everything.", 55));
            FreeItemsAtLevel1 = Config.Bind(A, "CountFreeItemsAtLevel1", false, Desc(
                "Count items without a limit as level 1 unlocks (that's most of the game's items).", 50));
            VerboseLog = Config.Bind(A, "VerboseLog", true, Desc(
                "Write detailed lines to the log (icons, menu objects, screen changes…). Its cost is measured and written in each session line of Progression.log; turn it off if that ever gets noticeable.", 20));
            DumpKey = Config.Bind(A, "DumpKey", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl), Desc(
                "Writes everything the plugin knows (menu bar objects, data, screen state) to the log.", 10));
            LastSeenLevel = Config.Bind(A, "LastSeenLevel", 0, new ConfigDescription(
                "Your level when you last opened the screen (the NEW tag shows after a level-up). Set by the plugin.", null, new ConfigurationManagerAttributes { Browsable = false }));

            MigrateOldSettings();
            if (Mathf.Abs(BottomMargin.Value - 68f) < .01f) { BottomMargin.Value = 26f; } // old default: the screen now reaches down to the menu bar

            L.Info($"{Name} {Version} starting. Unity {Application.unityVersion}, plugins folder: {Paths.PluginPath}");
            // every setting, once (so a log says how the screen was configured)
            L.Info("settings: " + string.Join(", ", Config.Keys.Select(k => $"{k.Section}.{k.Key}={Config[k].BoxedValue}").ToArray()));
            try
            {
                ProgData.Init();
                var harmony = new Harmony(Guid);
                MenuHook.Apply(harmony);
            }
            catch (Exception e) { L.Error("startup", e); }
            L.Info($"{Name} started. Open key: {OpenKey.Value}, dump key: {DumpKey.Value}, menu button: {(InjectButton.Value ? "on" : "off")}.");
        }

        /// <summary>Graphics Low: lighter pictures, still emblems (the screen's PERFORMANCE MODE box).</summary>
        internal static bool Low => Quality?.Value == GraphicsQuality.Low;
        /// <summary>Graphics High: extra-sharp weapons in the centre picture.</summary>
        internal static bool High => Quality?.Value == GraphicsQuality.High;

        private static ConfigDescription Desc(string text, int order, AcceptableValueBase range = null)
            => new ConfigDescription(text, range, new ConfigurationManagerAttributes { Order = order });

        /// <summary>
        /// Up to 0.9.21 the settings lived in General / Menu Button / Screen / Debug. BepInEx keeps values it no longer
        /// knows as "orphaned" entries: carry them over to the new names once, so nobody loses their settings
        /// (or their last seen level, which would bring back an old NEW tag).
        /// </summary>
        private void MigrateOldSettings()
        {
            try
            {
                var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config, null)
                    as System.Collections.Generic.Dictionary<ConfigDefinition, string>;
                if (orphans == null || orphans.Count == 0) return;
                int moved = 0;
                string Take(string section, string key)
                {
                    var d = orphans.Keys.FirstOrDefault(k => k.Section == section && k.Key == key);
                    if (d == null) return null;
                    var v = orphans[d]; orphans.Remove(d); moved++;
                    return v;
                }
                void Move(ConfigEntryBase e, string section, string key)
                {
                    var v = Take(section, key);
                    if (v == null) return;
                    try { e.SetSerializedValue(v); } catch (Exception ex) { L.Warn($"settings: couldn't carry over {section}.{key}={v}: {ex.Message}"); }
                }
                Move(InjectButton, "Menu Button", "AddButton");
                Move(MenuShortcut, "Menu Button", "MainMenuShortcut");
                Move(ButtonLabel, "Menu Button", "Label");
                Move(ButtonTemplate, "Menu Button", "CopyButton");
                Move(LastSeenLevel, "Menu Button", "LastSeenLevel");
                Move(HideMainMenu, "Screen", "HideMainMenu");
                Move(BlurBackground, "Screen", "BlurBackground");
                Move(TopMargin, "Screen", "TopMargin");
                Move(BottomMargin, "Screen", "BottomMargin");
                Move(TileSize, "Screen", "TileSize");
                Move(MaxTilesPerCategory, "Screen", "MaxTilesPerCategory");
                Move(Opacity, "Screen", "Opacity");
                Move(CameraTurn, "Screen", "CameraTurnDegrees");
                Move(SortOrder, "Screen", "SortOrder");
                Move(EmbedInGameUi, "Screen", "InsideGameUi");
                Move(FreeItemsAtLevel1, "Screen", "CountFreeItemsAtLevel1");
                Move(VerboseLog, "Debug", "VerboseLog");
                Move(DumpKey, "Debug", "DumpKey");
                Move(OpenKey, "General", "OpenScreenKey"); // same place; only here in case the file lists it as an orphan
                Take("Screen", "FixStashIcons");
                // PerformanceMode + SharpWeaponPreview became one Graphics > Quality
                bool perf = string.Equals(Take("Screen", "PerformanceMode"), "true", StringComparison.OrdinalIgnoreCase);
                bool sharp = string.Equals(Take("Screen", "SharpWeaponPreview"), "true", StringComparison.OrdinalIgnoreCase);
                if (perf || sharp) Quality.Value = perf ? GraphicsQuality.Low : GraphicsQuality.High;
                if (moved == 0) return;
                Config.Save();
                L.Info($"settings: carried {moved} setting(s) over from the old layout (performance mode {perf}, sharp weapons {sharp} → quality {Quality.Value})");
            }
            catch (Exception e) { L.Error("settings migration", e); }
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

    /// <summary>Logging with a common prefix; Debug lines only when Advanced > VerboseLog is on.</summary>
    internal static class L
    {
        internal static ManualLogSource Source;
        private static readonly System.Collections.Generic.HashSet<string> _once = new System.Collections.Generic.HashSet<string>();

        public static bool Verbose => ProgressionPlugin.VerboseLog?.Value ?? true;
        public static void Info(string s) { long t = Now; Source?.LogInfo("[Progression] " + s); File("info ", s); Cost(t); }
        public static void Debug(string s) { if (!Verbose) return; long t = Now; Source?.LogInfo("[Progression] (debug) " + s); File("debug", s); Cost(t); }
        public static void Warn(string s) { long t = Now; Source?.LogWarning("[Progression] " + s); File("WARN ", s); Cost(t); }
        public static void Error(string where, Exception e) { Source?.LogError($"[Progression] error in {where}: {e}"); File("ERROR", where + ": " + e); }

        // What logging itself costs: time spent inside these calls (BepInEx log + Progression.log), so a session
        // line can say "logging took X ms of Y s" instead of guessing.
        private static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
        private static long _costTicks;
        private static int _costLines;
        private static void Cost(long start) { System.Threading.Interlocked.Add(ref _costTicks, Now - start); System.Threading.Interlocked.Increment(ref _costLines); }
        /// <summary>Logging time (ms) and lines since start.</summary>
        public static double CostMs => _costTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public static int CostLines => _costLines;

        // Progression.log next to the plugin, written to disk line by line: BepInEx's own log is buffered, so
        // after a game crash its last lines are missing — this file still shows the last step that ran.
        private static System.IO.StreamWriter _file;
        private static bool _fileTried;

        /// <summary>A step marker that only goes to Progression.log (for finding where a crash happened).</summary>
        public static void Step(string s) { long t = Now; LastStep = s; File("step ", s); Cost(t); }
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
                    var path = System.IO.Path.Combine(dir, "Progression.log");
                    // the previous game session's log is kept as Progression.prev.log (a restart doesn't wipe it)
                    try { if (System.IO.File.Exists(path)) System.IO.File.Copy(path, System.IO.Path.Combine(dir, "Progression.prev.log"), true); } catch { }
                    _file = new System.IO.StreamWriter(path, false) { AutoFlush = true };
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
    /// <summary>Read by BepInEx Configuration Manager (by name): hides a setting, or sets its place in the list.</summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public int? Order;
    }

    public enum GraphicsQuality { Low, Medium, High }
}
