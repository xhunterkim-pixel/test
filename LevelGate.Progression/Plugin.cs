using System;
using System.Collections.Generic;
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
        public const string Version = "1.0.23";

        internal static ProgressionPlugin Instance;
        internal static ConfigEntry<KeyboardShortcut> OpenKey;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;
        internal static ConfigEntry<bool> InjectButton, CharacterEmblem, ReduceMotion;
        internal static ConfigEntry<int> TextSize, Detailing, PatternOpacity, DetailAnimation;
        // 5. CURRENTLY TESTING: new animation features to tune in game; the chosen values become the defaults after the patch
        internal static ConfigEntry<int> TestShine, TestShineWidth, TestGlitch, TestRevealRandom, TestBloomOpacity, TestBloomSize, TestWallBrightness, TestMotionSpeed, TestStagger;
        /// <summary>The ten MW4 features, each switchable while they're being tried (index 1…10; [0] unused).</summary>
        internal static readonly ConfigEntry<bool>[] TestMw = new ConfigEntry<bool>[11];
        // 0.9.9 polish pass (F12 > CURRENTLY TESTING): Polish off = exactly 0.9.81
        internal static ConfigEntry<bool> TestPolish, TestQuietFx, TestNeutralPips;
        internal static ConfigEntry<int> TestNameGlow, TestNameGlowSoftness, TestNameShadow, TestNameShadowSoftness, TestNameShadowDistance, TestPanelLight, TestCardOpacity, TestCardLockedOpacity, TestDecorNoise, TestMicroLabels, TestAmbient, TestFlourish, TestBorderFade, TestCardRest, TestCardLocked, TestHeroSubtitleOpacity, TestAmbientLight;
        internal static ConfigEntry<float> TestSmallText, TestTipDelay;
        internal static ConfigEntry<ProgScreen.HeroLine> TestHeroSubtitle;
        internal static ConfigEntry<ProgScreen.LightHue> TestLightHue;
        internal static ConfigEntry<ProgScreen.NewTagLook> TestNewTag;
        // 0.9.95 (F12 > CURRENTLY TESTING): from the user's MW4 crops
        internal static ConfigEntry<int> TestWallXpBacking, TestWallXpLift;
        internal static ConfigEntry<bool> TestBigXpDuringSweep;
        internal static ConfigEntry<bool> TestFadeCardLine, TestFadeSectionHead, TestFadeMeters, TestFadeXpBar; // 0.9.97
        internal static ConfigEntry<float> TestXpStartDelay; // 1.0.4
        internal static ConfigEntry<bool> TestDirectionalBorders, TestCardAccentLight; // 1.0.9
        internal static bool Mw(int n) => TestMw[n]?.Value ?? true;
        internal static ConfigEntry<float> TestRevealTime;
        internal static ConfigEntry<ProgScreen.LoadInStyle> TestRevealStyle;
        internal static ConfigEntry<ProgScreen.RankUpStyle> TestRankStyle;
        internal static ConfigEntry<ProgScreen.GroupLook> GroupStyle;
        internal static ConfigEntry<string> ButtonTemplate;
        internal static ConfigEntry<string> ButtonLabel;
        internal static ConfigEntry<bool> VerboseLog;
        internal static ConfigEntry<bool> PerfReadout;
        internal static ConfigEntry<ProgScreen.XpEdgeLook> TestXpEdge;
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
        internal static ConfigEntry<bool> RefreshIcons;
        internal static ConfigEntry<int> LastSeenLevel;
        internal static ConfigEntry<bool> XpAnimation;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<bool> GameSounds;
        internal static ConfigEntry<float> Scratches, Vignette, RedGlow;
        internal static ConfigEntry<BackgroundPattern> Pattern;
        internal static ConfigEntry<float> PatternMotion;
        internal static ConfigEntry<int> PreviewLevels;
        internal static ConfigEntry<bool> PreviewLevelUp, PreviewRank, PreviewUnlock;
        internal static ConfigEntry<int> ShownXp;
        internal static ConfigEntry<string> ProfileState, NewState;

        private void Awake()
        {
            Instance = this;
            L.Source = Logger;
            Diag.Start(); // 1.0.2: system info, other plugins, the game's errors
            Application.quitting += OnQuit; // 0.9.99: log the game's exit and stop our work first (the user saw a freeze on exit)

            // F12 (Configuration Manager): three sections, sorted by their number. Order = top to bottom within one.
            const string G = "1. General", A = "3. Advanced";
            OpenKey = Config.Bind(G, "OpenScreenKey", new KeyboardShortcut(KeyCode.P), Desc(
                "Opens / closes the Progression screen in the main menu (not while typing in a text box).", 100));
            InjectButton = Config.Bind(G, "MenuBarButton", true, Desc(
                "Add a PROGRESSION button to the main menu bar (next to Character, Trading, Flea Market…).", 90));
            MenuShortcut = Config.Bind(G, "MainMenuShortcut", true, Desc(
                "A PROGRESSION block in the main menu's bottom-left corner (like the game's EXPANSIONS one) that opens the screen.", 80));
            HideMainMenu = Config.Bind(G, "HideMainMenu", true, Desc(
                "Fade out the main menu (ESCAPE FROM TARKOV, CHARACTER, TRADING, EXIT…) while the screen is open.", 70));
            SoundVolume = Config.Bind(G, "SoundVolume", 1f, Desc(
                "Volume of the level-up and new-rank sounds (sounds folder). 0 = off.", 55, new AcceptableValueRange<float>(0f, 1f)));
            GameSounds = Config.Bind(G, "UseGameSounds", false, Desc(
                "Level up / new rank: the game's own UI sounds instead of the plugin's (sounds folder). SoundVolume doesn't apply to them.", 54));
            BlurBackground = Config.Bind(G, "BlurBackground", true, Desc(
                "Blur the menu's 3D background while the screen is open (if the game's camera has a blur effect).", 60));
            TextSize = Config.Bind(G, "TextSize", 100, Desc(
                "Size of the screen's small text (labels, tags, tile names, stat names) in percent: 100, 115 or 130. Takes effect the next time the screen opens.",
                45, new AcceptableValueList<int>(100, 115, 130)));
            TextSize.SettingChanged += (_, __) => ProgScreen.Rebuild("text size changed");
            ReduceMotion = Config.Bind(G, "ReduceMotion", false, Desc(
                "Less movement: background patterns stand still, cards and pages change without sliding, no flashes, pulses, beams or flicks (the XP animation still counts up).",
                44));
            CharacterEmblem = Config.Bind(G, "CharacterEmblem", true, Desc(
                "Show your animated rank emblem and rank name on the game's Character > Overall screen, next to your level.", 50));

            Quality = Config.Bind(Gfx, "Quality", GraphicsQuality.Medium, Desc(
                "Low = Performance Mode (the box on the screen), for slower PCs. It turns off what costs the most: item pictures drawn smaller, fewer and " +
                "one at a time; no moving background (and no pattern); no background blur; no texture layers (UI Detailing off); still rank emblems and " +
                "no slides, flashes or pulses.\n" +
                "Medium: sharp item pictures; weapons drawn at stash size.\n" +
                "High: weapons drawn extra sharp too. The game can carry big weapon pictures over to weapons in your stash, so every level-list weapon " +
                "(~160) is redrawn at stash size in the background after you close the screen (main menu only, never in a raid, at most every 10 minutes).", 100));
            Quality.SettingChanged += (_, __) =>
            {
                L.Info($"graphics quality: {Quality.Value}");
                ProgScreen.ApplyLook();  // texture layers / pattern on or off
                MenuCamera.ReBlur();     // background blur on or off while the screen is open
                RefreshPictures(false);
                Toast.Show($"Graphics: {Quality.Value} — pictures redrawn");
            };
            XpAnimation = Config.Bind(Gfx, "XpAnimation", true, Desc(
                "After you gain experience (a raid, a quest…), the next time you open the screen the XP bar fills up from where you last saw it: " +
                "level ups, the new level number, a new rank emblem. Click or Space skips it. Off: the screen just shows your current XP.", 95));
            Detailing = Config.Bind(Gfx, "UIDetailing", 100, Desc(
                "How much surface detail the screen has, all in one: scratches, smudges and fingerprints, film grain, " +
                "corner marks, edge lights, dither, scanlines, the reflection on the picked card, the coloured bloom and dotted lights on the picked card, the bloom around your card and behind the rank emblems, the XP bar's tracer. " +
                "100 = the full look, 0 = clean and flat (those layers aren't drawn at all). Performance Mode turns them all off.",
                85, new AcceptableValueRange<int>(0, 100)));
            DetailAnimation = Config.Bind(Gfx, "DetailAnimation", 100, Desc(
                "How much the lights move: the red glow drifting, the rank emblem's glow breathing, the picked card's coloured bloom and dotted light, " +
                "the light under the big picture, and the tracer running along the XP bar. 100 = a gentle drift, 0 = all still. Off with Reduce Motion / Performance Mode.",
                84, new AcceptableValueRange<int>(0, 100)));
            PatternOpacity = Config.Bind(Gfx, "PatternOpacity", 82, Desc(
                "How visible the background pattern is, on its own (UI Detailing no longer changes it). 100 = as designed, 0 = no pattern (not worked out or drawn).",
                88, new AcceptableValueRange<int>(0, 100)));
            // the old Wear And Scratches (0–5): now part of UI Detailing (hidden; carried over once below)
            Scratches = Config.Bind(Gfx, "Scratches", 2f, new ConfigDescription(
                "Replaced by UI Detailing.", null, new ConfigurationManagerAttributes { Browsable = false }));
            if (Mathf.Abs(Scratches.Value - 2f) > .01f)
            {
                Detailing.Value = Mathf.Clamp(Mathf.RoundToInt(Scratches.Value / 2f * 100f), 0, 100);
                L.Info($"settings: Wear And Scratches {Scratches.Value:0.##} → UI Detailing {Detailing.Value}%");
                Scratches.Value = 2f;
            }
            Vignette = Config.Bind(Gfx, "Vignette", 1f, Desc("How dark the screen's corners are (1 = the original, 0 = none).", 84, new AcceptableValueRange<float>(0f, 3f)));
            RedGlow = Config.Bind(Gfx, "RedGlow", 1.1f, Desc("How strong the red glow in the top-right is (1 = the original, 0 = none).", 83, new AcceptableValueRange<float>(0f, 3f)));
            Pattern = Config.Bind(Gfx, "Pattern", BackgroundPattern.Damascus1, Desc(
                "The faint pattern behind the screen: Dots (grid of dots), Streaks (vertical streaks), Damascus 1 (busy topographic lines), Damascus 2 (big organic flowing lines), Damascus 3 (rings), Damascus 4 (mirrored lines), Marble (mirrored marbling), Pixels (LED wall with light bands), Terrain (3D ridge lines), Random (a different one each open). Its strength: Background Pattern Opacity.",
                89));
            PatternMotion = Config.Bind(Gfx, "PatternMotion", 2.5f, Desc(
                "How fast the animated patterns move (0 = still, 1 = a gentle drift). Only while the screen is open; it stops completely when you leave it.",
                87, new AcceptableValueRange<float>(0f, 3f)));
            Pattern.SettingChanged += (_, __) => ProgScreen.PatternChanged();
            Detailing.SettingChanged += (_, __) => ProgScreen.ApplyLook();
            GroupStyle = Config.Bind(Gfx, "GroupSimilarItems", ProgScreen.GroupLook.Stack, Desc(
                "Similar items in a category (balaclavas, caps, berets, bandanas, variants of one helmet…, 3 or more) as one group: " +
                "Stack (one tile with cards stacked behind it and a +N box that opens the group), Folder (4 pictures in one tile and the +N box), " +
                "Rows (all shown, each group under its own label), Off.", 83));
            GroupStyle.SettingChanged += (_, __) => { L.Info($"settings: group similar items = {GroupStyle.Value}"); ProgScreen.Refresh(); };
            PatternOpacity.SettingChanged += (_, __) => ProgScreen.ApplyLook();
            Vignette.SettingChanged += (_, __) => ProgScreen.ApplyLook();
            RedGlow.SettingChanged += (_, __) => ProgScreen.ApplyLook();
            RefreshIcons = Config.Bind(Gfx, "RefreshIcons", false, Desc(
                "Tick once to redraw every item picture: the Progression screen's own pictures are thrown away and drawn again, " +
                "and every level-list item's stash icon is redrawn at stash size (use it if a stash icon ever looks too big). It turns itself off again. " +
                "A message at the top of the screen shows the progress and says when it's done (about a minute, on the main menu).", 90));
            RefreshIcons.SettingChanged += (_, __) =>
            {
                if (!RefreshIcons.Value) return;
                RefreshIcons.Value = false;
                RefreshPictures(true);
            };

            // 5. CURRENTLY TESTING — animation features being tuned; their values here become the defaults after the patch
            const string T = "5. Testing";
            TestGlitch = Config.Bind(T, "HoverGlitch", 23, Desc(
                "Hover: how strong the burst of glitch streaks across a tile or card is, in % of 0.9.70's (0 = off).", 100, new AcceptableValueRange<int>(0, 200)));
            TestShine = Config.Bind(T, "SelectShine", 285, Desc(
                "Picking a tile or card: how bright the light sweeping across it is, in % (0 = off).", 96, new AcceptableValueRange<int>(0, 300)));
            TestShineWidth = Config.Bind(T, "SelectShineWidth", 157, Desc(
                "Picking a tile or card: how wide the sweeping light is, in %.", 95, new AcceptableValueRange<int>(30, 300)));
            TestRevealTime = Config.Bind(T, "RevealTime", .2f, Desc(
                "The big picture's load-in: how long it takes to appear from the top down, in seconds.", 90, new AcceptableValueRange<float>(.2f, 2.5f)));
            TestRevealStyle = Config.Bind(T, "RevealStyle", ProgScreen.LoadInStyle.Lines, Desc(
                "The big picture's load-in light: Dot Columns (loose vertical columns of dots), Dot Cloud (a scattered cluster), Lines (0.9.71's lines). The dots breathe (size and brightness).", 89));
            TestRevealStyle.SettingChanged += (_, __) => L.Info($"testing: RevealStyle = {TestRevealStyle.Value}");
            TestRankStyle = Config.Bind(T, "RankUpStyle", ProgScreen.RankUpStyle.Dogtag, Desc(
                "The new-rank moment in a level up: Banner (a dark tactical strip across mid-screen), Dossier (a personnel file with a PROMOTED stamp), " +
                "Terminal (a comms readout typing it out), Dogtag (a metal tag dropping in on its chain), Full (0.9.74's full-screen splash). Try them with 3. Preview > Play Next Rank.", 88));
            TestRankStyle.SettingChanged += (_, __) => L.Info($"testing: RankUpStyle = {TestRankStyle.Value}");
            TestXpEdge = Config.Bind(T, "XpBarEdge", ProgScreen.XpEdgeLook.Shadow, Desc(
                "The XP bar's light (how far into this level you are), set off by black on the empty side: Shadow (a short black fade right after it), " +
                "Long Fade (the empty bar darkened from the light, fading out over a third of it), Notch (a black gap each side of a small glow at the light), Off.", 87));
            TestXpEdge.SettingChanged += (_, __) => { L.Info($"testing: XpBarEdge = {TestXpEdge.Value}"); ProgScreen.XpEdgeChanged(); };
            TestRevealRandom = Config.Bind(T, "RevealRandom", 100, Desc(
                "The big picture's load-in: how much the lines of light vary and flicker while it appears (0 = one fixed pattern, 100 = changing every frame).", 85, new AcceptableValueRange<int>(0, 100)));
            TestBloomOpacity = Config.Bind(T, "BloomOpacity", 166, Desc(
                "Item bloom: how strong the glow in the picked item's own colours is (behind the big picture, the picked tile and card), in %. 0 = off.", 80, new AcceptableValueRange<int>(0, 250)));
            TestBloomSize = Config.Bind(T, "BloomSize", 120, Desc(
                "Item bloom: how far the glow spreads, in %.", 75, new AcceptableValueRange<int>(40, 250)));
            TestWallBrightness = Config.Bind(T, "LightWall", 122, Desc(
                "The light wall on the level track during level ups: brightness of the wall, its wash and trail, in %.", 70, new AcceptableValueRange<int>(0, 250)));
            TestMotionSpeed = Config.Bind(T, "MotionSpeed", 145, Desc(
                "The shared motion system: how fast every transition on the screen plays, in % (100 = as designed, 200 = twice as fast).", 99, new AcceptableValueRange<int>(25, 300)));
            TestStagger = Config.Bind(T, "Stagger", 30, Desc(
                "The shared motion system: the step between items coming in one after another (lights, specks, rows), in milliseconds.", 98, new AcceptableValueRange<int>(0, 120)));
            string[] mw =
            {
                null,
                "Additive glow: glows add light to what's under them (a real glow) instead of tinting it, where the game has an additive shader (the log says which it found)",
                "Card flood: cards unlocked during a level up fill with their rank's colour and a dot matrix, and stay lit until the screen closes",
                "Level numbers glow: reached levels' numbers above the cards glow in the rank's colour, with a small chevron over them (brightest on yours)",
                "XP counter on the light wall: +XP with a crosshair rides the light wall along the track as it counts",
                "Reactive waveform: bars around the light wall jump like audio as it passes; a low idle bounce at your place",
                "Screen flashes: a quick full-screen wash in the rank's colour on each level up, a softer one on each card unlock",
                "Title glitch: the item's name smears and splits (red / cyan) for a moment when the picked item changes",
                "Row pips: every list tile shows 5 pips (where its level sits in its rank) and its level (LV 17)",
                "Locked hologram: locked items' pictures look like a cold, scanlined hologram instead of just dimmed",
                "Wave surfaces: dotted wave surfaces drifting in the background's bottom corners (and behind the new-rank emblem)",
            };
            for (int i = 1; i <= 10; i++)
            {
                int n = i;
                TestMw[n] = Config.Bind(T, $"MW{n:00}", n != 6 && n != 8, // your picks after testing 0.9.74: all on but 6 and 8
                    Desc($"MW4 feature {n} — {mw[n]}. On/off is written to the log.", 60 - n));
                TestMw[n].SettingChanged += (_, __) => { L.Info($"testing: MW{n:00} {(TestMw[n].Value ? "on" : "off")} — {mw[n].Split(':')[0]}"); ProgScreen.MwChanged(n); };
            }
            foreach (var e in new ConfigEntryBase[] { TestShine, TestShineWidth, TestGlitch, TestRevealTime, TestRevealRandom, TestBloomOpacity, TestBloomSize, TestWallBrightness, TestMotionSpeed, TestStagger })
            {
                var entry = e;
                if (entry is ConfigEntry<int> ei) ei.SettingChanged += (_, __) => L.Info($"testing: {entry.Definition.Key} = {entry.BoxedValue}");
                else if (entry is ConfigEntry<float> ef) ef.SettingChanged += (_, __) => L.Info($"testing: {entry.Definition.Key} = {entry.BoxedValue:0.##}");
            }

            // 0.9.9: the polish pass — one switch for before / after, then a dial per change (order = importance)
            TestPolish = Config.Bind(T, "Polish", true, Desc(
                "0.9.9's polish pass on (the dials below) or off (exactly 0.9.81) — flip it to compare before / after.", 200));
            TestDecorNoise = Config.Bind(T, "DecorNoise", 55, Desc(
                "Decorative layers — scanlines, rulers, corner marks, scratches, dither, inner glows — in % of 0.9.81 (100 = as before).", 199, new AcceptableValueRange<int>(0, 150)));
            TestMicroLabels = Config.Bind(T, "MicroLabels", 60, Desc(
                "The tiny system labels (ID - …, STATS —, LEVEL_ACTIVE) in % of 0.9.81.", 198, new AcceptableValueRange<int>(0, 150)));
            TestBorderFade = Config.Bind(T, "BorderFade", 70, Desc(
                "MW4 frames: panel, tile and card borders at full strength in the middle of each side, fading toward the ends. 0 = solid (0.9.81), 100 = gone at the ends.", 197, new AcceptableValueRange<int>(0, 100)));
            TestAmbient = Config.Bind(T, "AmbientMotion", 50, Desc(
                "Idle movement — drifting lights, the breathing emblem, the background pattern — in % of 0.9.81.", 196, new AcceptableValueRange<int>(0, 150)));
            TestQuietFx = Config.Bind(T, "QuietDuringEffects", true, Desc(
                "The idle movers rest while a level up, the XP fill or a picture load-in plays, so one thing moves at a time.", 195));
            TestFlourish = Config.Bind(T, "Flourish", 65, Desc(
                "The one-off show effects — selection shine, hover glitch, title glitch — in % of their own settings.", 194, new AcceptableValueRange<int>(0, 150)));
            TestAmbientLight = Config.Bind(T, "AmbientLight", 140, Desc(
                "The picked card's soft ambient lights (the coloured bloom and the dotted light), in % of 0.9.81.", 193, new AcceptableValueRange<int>(0, 300)));
            TestLightHue = Config.Bind(T, "LightColours", ProgScreen.LightHue.Mixed, Desc(
                "The picked card's ambient light: Mixed (its item / rank colour plus a pink-purple accent drifting on the other side) or Rank (0.9.81: one colour).", 192));
            TestCardRest = Config.Bind(T, "CardRest", 90, Desc(
                "Bottom cards that are neither yours nor viewed (reached levels): how visible, in %. 0.9.81 = 100.", 191, new AcceptableValueRange<int>(40, 100)));
            TestCardLocked = Config.Bind(T, "CardLocked", 72, Desc(
                "Bottom cards of levels you haven't reached (unless viewed or hovered): how visible, in %. 0.9.81 = 85.", 190, new AcceptableValueRange<int>(30, 100)));
            TestSmallText = Config.Bind(T, "SmallText", 10.5f, Desc(
                "Information text (tile LV, group captions, card type lines…) is never smaller than this at 1080p. Decorative micro text isn't changed.", 189, new AcceptableValueRange<float>(9f, 13f)));
            TestNeutralPips = Config.Bind(T, "NeutralPips", true, Desc(
                "The hero's rank pips in the screen's state colours (light grey, orange when it's your level) instead of loot colours — red is kept for unmet requirements.", 188));
            TestHeroSubtitle = Config.Bind(T, "HeroSubtitle", ProgScreen.HeroLine.Off, Desc(
                "A line under the big name, like MW4: Description (its first sentence, grey italic), FullName (the long name), Off.", 187));
            TestHeroSubtitleOpacity = Config.Bind(T, "HeroSubtitleOpacity", 70, Desc(
                "How bright the line under the big name is, in %.", 186, new AcceptableValueRange<int>(20, 100)));
            TestTipDelay = Config.Bind(T, "TooltipDelay", 1f, Desc(
                "The full-name tooltip shows only after resting on a tile / card this long, in seconds (0 = at once, like 0.9.81).", 185, new AcceptableValueRange<float>(0f, 2.5f)));
            // 0.9.91: MW4's soft glow behind the big name
            TestNameGlow = Config.Bind(T, "NameGlow", 60, Desc(
                "A soft light halo hugging the big name's letters, like MW4's weapon names, in % (0 = off).", 210, new AcceptableValueRange<int>(0, 200)));
            TestNameGlowSoftness = Config.Bind(T, "NameGlowSoftness", 85, Desc(
                "How soft (spread out) that glow is, in %.", 209, new AcceptableValueRange<int>(10, 100)));
            // 0.9.92: the name's glow → a reflection falling down, like MW4's "HAN 86" (new keys: 0.9.91's halo values don't carry over)
            TestNameShadow = Config.Bind(T, "NameReflection", 29, Desc(
                "The big name's letters cast a soft light reflection falling down, like MW4's weapon names, in % (0 = off).", 208, new AcceptableValueRange<int>(0, 150)));
            TestNameShadowSoftness = Config.Bind(T, "NameReflectionSoftness", 26, Desc(
                "How soft (blurred) that reflection is, in %.", 207, new AcceptableValueRange<int>(10, 100)));
            TestNameShadowDistance = Config.Bind(T, "NameReflectionDistance", 47, Desc(
                "How far down the reflection falls from the letters, in % (0 = right behind them, a plain glow).", 206, new AcceptableValueRange<int>(0, 100)));
            TestPanelLight = Config.Bind(T, "PanelLight", 100, Desc(
                "The panels' and cards' soft light (inner glow, lit edge, glass, sheen), in % of 0.9.81 (0.9.9–0.9.91 had it at 55).", 204, new AcceptableValueRange<int>(0, 150)));
            TestCardOpacity = Config.Bind(T, "CardOpacity", 100, Desc(
                "Level cards that aren't picked / current, in % (0.9.81 = 100; 0.9.9–0.9.91 had 90).", 203, new AcceptableValueRange<int>(50, 100)));
            TestCardLockedOpacity = Config.Bind(T, "CardLockedOpacity", 100, Desc(
                "Locked level cards, in % (0.9.81 = 85; 0.9.9–0.9.91 had 72).", 202, new AcceptableValueRange<int>(40, 100)));
            // 0.9.95: a new key (NewTagLook's saved MW4 would stick): MW4 Dark is the user's MW4 crop — dark see-through box, yellow text
            TestNewTag = Config.Bind(T, "NewTagStyle", ProgScreen.NewTagLook.MW4, Desc(
                "The NEW tag: Mw4Dark (MW4's crop: a dark see-through box, bright yellow NEW, thin gold outline, soft glow), MW4 (0.9.92's gold box fading to the left) or Old (0.9.91's outlined box).", 205));
            TestWallXpBacking = Config.Bind(T, "SweepXpBacking", 100, Desc(
                "The +XP riding the light sweep gets a soft dark backing so it stays readable over card names, in % (0 = none, like 0.9.94).", 196, new AcceptableValueRange<int>(0, 100)));
            TestWallXpLift = Config.Bind(T, "SweepXpHeight", 44, Desc(
                "How high the sweep's +XP sits above the line, in px (0.9.94 = 44; the card names are at about that height).", 195, new AcceptableValueRange<int>(10, 160)));
            TestBigXpDuringSweep = Config.Bind(T, "BigXpDuringSweep", true, Desc(
                "Also show the big +XP over the item picture while the sweep plays (0.9.94 = on). MW4 shows only the one on the sweep.", 194));
            // 0.9.97: MW4-style left / right fades on flat elements (from the user's close-ups)
            TestFadeCardLine = Config.Bind(T, "FadeCardLine", true, Desc(
                "The current card's top line fades out towards the left (full on the right), like MW4's card headers. Off = a flat line (0.9.96).", 193));
            TestFadeSectionHead = Config.Bind(T, "FadeSectionHead", true, Desc(
                "The reward list's category bars (WEAPONS, ARMOR…) fade out towards the right. Off = flat bars (0.9.96).", 192));
            TestFadeMeters = Config.Bind(T, "FadeStatMeters", true, Desc(
                "The meters under FIRE RATE / ERGONOMICS / RECOIL: the fill brightens towards its end, the track fades out to the right. Off = flat (0.9.96).", 191));
            TestFadeXpBar = Config.Bind(T, "FadeXpBar", true, Desc(
                "The XP bar at the top: the fill brightens towards its end, the outline fades out to the right. Off = flat (0.9.96).", 190));
            // 1.0.4: the XP animation waits for the screen to finish loading and settle, then this long before it starts
            TestXpStartDelay = Config.Bind(T, "XpStartDelay", .6f, Desc(
                "After the loading screen is gone and the screen has settled (pictures in, no stutter), wait this long before the XP animation starts, in seconds.", 189, new AcceptableValueRange<float>(0f, 2f)));
            // 1.0.9: the cards' / tiles' borders as MW4's (each edge a one-way fade), and the picked card's pink accent light
            TestDirectionalBorders = Config.Bind(T, "DirectionalBorders", true, Desc(
                "Level cards and reward tiles: each border line fades one way, like MW4's cards (toward the two square corners: top brighter to the right, right brighter upward, bottom brighter to the left, left brighter downward; the cut corners are the faint ends). Off = both ends fade (1.0.8).", 186));
            TestCardAccentLight = Config.Bind(T, "CardAccentLight", false, Desc(
                "The picked level card's pink / purple accent light behind its name (1.0.8 had it on). Off = neutral.", 185));
            var polishDials = new HashSet<ConfigEntryBase> { TestNameShadow, TestNameShadowSoftness, TestNameShadowDistance, TestNewTag, TestPanelLight, TestCardOpacity, TestCardLockedOpacity, TestNameGlow, TestNameGlowSoftness, TestPolish, TestDecorNoise, TestMicroLabels, TestBorderFade, TestAmbient, TestQuietFx, TestFlourish, TestAmbientLight, TestLightHue,
                TestCardRest, TestCardLocked, TestSmallText, TestNeutralPips, TestHeroSubtitle, TestHeroSubtitleOpacity, TestTipDelay,
                TestWallXpBacking, TestWallXpLift, TestBigXpDuringSweep,
                TestFadeCardLine, TestFadeSectionHead, TestFadeMeters, TestFadeXpBar, TestXpStartDelay, TestDirectionalBorders, TestCardAccentLight };
            var liveDials = new HashSet<ConfigEntryBase> { TestTipDelay, TestAmbient, TestQuietFx, TestFlourish, TestWallXpBacking, TestWallXpLift, TestBigXpDuringSweep, TestXpStartDelay, TestCardAccentLight };
            // 1.0.2: every setting change, not only the testing dials
            Config.SettingChanged += (_, a) => { var e = a?.ChangedSetting; if (e != null) L.Trace($"setting: {e.Definition.Section} › {e.Definition.Key} = {(e.BoxedValue is float f ? f.ToString("0.###") : e.BoxedValue)}"); };
            Config.SettingChanged += (_, a) =>
            {
                var entry = a?.ChangedSetting;
                if (entry == null || !polishDials.Contains(entry)) return;
                L.Info($"testing: {entry.Definition.Key} = {(entry.BoxedValue is float f ? f.ToString("0.##") : entry.BoxedValue)}");
                // live ones need nothing; the rest are baked in when the screen is built (applied on the next open)
                if (!liveDials.Contains(entry)) ProgScreen.PolishChanged("testing: " + entry.Definition.Key);
            };

            // 4. Preview: plays the XP animation with made-up numbers — nothing real changes (closing the screen brings yours back)
            const string P = "4. Preview";
            PreviewLevels = Config.Bind(P, "Levels", 1, Desc("How many levels the preview buttons below play.", 100, new AcceptableValueRange<int>(1, 40)));
            PreviewLevelUp = Config.Bind(P, "PlayLevelUp", false, Desc(
                "Tick: plays level ups (the Levels above) on the Progression screen, as after a raid. Only a preview: your XP, level and NEW tags don't change; closing the screen brings them back. Starts when F12 closes.", 90));
            PreviewRank = Config.Bind(P, "PlayNextRank", false, Desc(
                "Tick: plays the level ups up to the next rank, with the new emblem. Only a preview (nothing changes).", 80));
            PreviewUnlock = Config.Bind(P, "PlayUnlock", false, Desc(
                "Tick: plays only the cards unlocking for the next levels (the Levels above). Only a preview (nothing changes).", 70));
            void Button(ConfigEntry<bool> e, string kind)
            {
                e.SettingChanged += (_, __) =>
                {
                    if (!e.Value) return;
                    e.Value = false;
                    ProgScreen.Preview(kind, PreviewLevels.Value);
                };
            }
            Button(PreviewLevelUp, "levels"); Button(PreviewRank, "rank"); Button(PreviewUnlock, "unlock");

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
            PerfReadout = Config.Bind(A, "PerformanceReadout", false, Desc(
                "A small readout in the screen's bottom-left corner: frames per second, the slowest frame of the last second, how long the last level took to build and how many tiles are showing. For measuring, not for play.", 22));
            VerboseLog = Config.Bind(A, "VerboseLog", true, Desc(
                "Write detailed lines to the log (icons, menu objects, screen changes…). Its cost is measured and written in each session line of Progression.log; turn it off if that ever gets noticeable.", 20));
            DumpKey = Config.Bind(A, "DumpKey", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl), Desc(
                "Writes everything the plugin knows (menu bar objects, data, screen state) to the log.", 10));
            ProfileState = Config.Bind(A, "ProfileState", "", new ConfigDescription(
                "Per character: the XP and level the screen last showed (XP animation, NEW tags). Set by the plugin.", null, new ConfigurationManagerAttributes { Browsable = false }));
            NewState = Config.Bind(A, "NewState", "", new ConfigDescription(
                "Per character: the rewards and levels still tagged NEW (they stay until clicked / looked at). Set by the plugin.", null, new ConfigurationManagerAttributes { Browsable = false }));
            ShownXp = Config.Bind(A, "ShownXp", 0, new ConfigDescription(
                "Your total experience the last time the screen showed it (the XP animation plays from here). Set by the plugin.", null, new ConfigurationManagerAttributes { Browsable = false }));
                        LastSeenLevel = Config.Bind(A, "LastSeenLevel", 0, new ConfigDescription(
                "Your level when you last opened the screen (the NEW tag shows after a level-up). Set by the plugin.", null, new ConfigurationManagerAttributes { Browsable = false }));

            MigrateOldSettings();
            MigratePatternNames();
            NameSettings();
            if (Mathf.Abs(BottomMargin.Value - 68f) < .01f) { BottomMargin.Value = 26f; } // old default: the screen now reaches down to the menu bar

            L.Info($"{Name} {Version} starting. Unity {Application.unityVersion}, plugins folder: {Paths.PluginPath}");
            // every setting, once (so a log says how the screen was configured)
            L.Info("settings: " + string.Join(", ", Config.Keys.Select(k => $"{k.Section}.{k.Key}={Config[k].BoxedValue}").ToArray()));
            try
            {
                ProgData.Init();
                Sfx.Load();
                OriginalArmor.Load(); // background thread, read-only
                GameText.Load();      // short names / handbook categories from the game data (background thread, read-only)
                var harmony = new Harmony(Guid);
                MenuHook.Apply(harmony);
            }
            catch (Exception e) { L.Error("startup", e); }
            L.Info($"{Name} started. Open key: {OpenKey.Value}, dump key: {DumpKey.Value}, menu button: {(InjectButton.Value ? "on" : "off")}.");
        }

        /// <summary>Graphics Low = the screen's PERFORMANCE MODE box: lighter / fewer pictures, no pattern, blur or texture layers, still emblems, no motion.</summary>
        internal static bool Low => Quality?.Value == GraphicsQuality.Low;
        /// <summary>Graphics High: extra-sharp weapons in the centre picture.</summary>
        internal static bool High => Quality?.Value == GraphicsQuality.High;

        /// <summary>
        /// How F12 shows the settings: readable names ("Background Pattern"), clear section titles, and the ones almost nobody
        /// needs (margins, drawing order, debug…) only with F12's "Advanced settings" ticked. The stored names don't change,
        /// so nobody's settings are lost.
        /// </summary>
        private void NameSettings()
        {
            var names = new Dictionary<string, string>
            {
                ["OpenScreenKey"] = "Open Screen Key", ["MenuBarButton"] = "Menu Bar Button", ["MainMenuShortcut"] = "Main Menu Shortcut",
                ["HideMainMenu"] = "Hide Main Menu While Open", ["BlurBackground"] = "Blur Background", ["SoundVolume"] = "Sound Volume",
                ["UseGameSounds"] = "Use Game Sounds", ["CharacterEmblem"] = "Rank Emblem On Character Screen", ["TextSize"] = "Text Size (%)", ["ReduceMotion"] = "Reduce Motion",
                ["Quality"] = "Picture Quality", ["XpAnimation"] = "Level Up Animation", ["RefreshIcons"] = "Redraw All Item Pictures",
                ["Pattern"] = "Background Pattern", ["PatternMotion"] = "Pattern Animation Speed", ["UIDetailing"] = "UI Detailing (%)", ["HoverGlitch"] = "Hover Glitch (%)", ["RevealTime"] = "Picture Load-In Time (s)",
                ["RevealRandom"] = "Picture Load-In Randomness (%)", ["RevealStyle"] = "Picture Load-In Style", ["RankUpStyle"] = "Rank Up Style", ["GroupSimilarItems"] = "Group Similar Items", ["BloomOpacity"] = "Item Bloom Opacity (%)", ["BloomSize"] = "Item Bloom Size (%)", ["LightWall"] = "Light Wall Brightness (%)", ["MotionSpeed"] = "Motion Speed (%)", ["SelectShine"] = "Selection Shine (%)", ["SelectShineWidth"] = "Selection Shine Width (%)", ["Stagger"] = "Stagger (ms)",
                ["MW01"] = "MW 1 · Additive Glow", ["MW02"] = "MW 2 · Card Flood", ["MW03"] = "MW 3 · Level Numbers Glow", ["MW04"] = "MW 4 · XP Counter On Light Wall",
                ["MW05"] = "MW 5 · Reactive Waveform", ["MW06"] = "MW 6 · Screen Flashes", ["MW07"] = "MW 7 · Title Glitch", ["MW08"] = "MW 8 · Row Pips",
                ["MW09"] = "MW 9 · Locked Hologram", ["MW10"] = "MW 10 · Wave Surfaces", ["DetailAnimation"] = "Detail Animation (%)", ["PatternOpacity"] = "Background Pattern Opacity (%)",
                ["Vignette"] = "Dark Corners", ["RedGlow"] = "Red Glow",
                ["Levels"] = "Levels To Play", ["PlayLevelUp"] = "Play Level Ups", ["PlayNextRank"] = "Play Next Rank", ["PlayUnlock"] = "Play Card Unlocks",
                ["ButtonLabel"] = "Menu Button Text", ["CopyButton"] = "Copy Look Of Button", ["TopMargin"] = "Top Margin", ["BottomMargin"] = "Bottom Margin",
                ["TileSize"] = "Tile Size", ["MaxTilesPerCategory"] = "Max Items Per Category", ["Opacity"] = "Background Opacity",
                ["CameraTurnDegrees"] = "Camera Turn (Degrees)", ["SortOrder"] = "Drawing Order", ["InsideGameUi"] = "Inside Game UI",
                ["CountFreeItemsAtLevel1"] = "Count Free Items At Level 1", ["VerboseLog"] = "Detailed Log", ["DumpKey"] = "Debug Dump Key", ["PerformanceReadout"] = "Performance Readout", ["XpBarEdge"] = "XP Bar Edge",
                ["Polish"] = "Polish 0.9.9 (Off = 0.9.81)", ["DecorNoise"] = "Decor Noise (%)", ["MicroLabels"] = "Micro Labels (%)", ["BorderFade"] = "Border Fade (%)",
                ["AmbientMotion"] = "Ambient Motion (%)", ["QuietDuringEffects"] = "Quiet During Effects", ["Flourish"] = "Flourish (%)", ["AmbientLight"] = "Ambient Light (%)",
                ["LightColours"] = "Ambient Light Colours", ["CardRest"] = "Card Rest Opacity (%)", ["CardLocked"] = "Card Locked Opacity (%)", ["SmallText"] = "Small Text Minimum (px)",
                ["NeutralPips"] = "Neutral Rank Pips", ["HeroSubtitle"] = "Line Under Name", ["HeroSubtitleOpacity"] = "Line Under Name Opacity (%)", ["TooltipDelay"] = "Tooltip Delay (s)", ["NameGlow"] = "Name Glow (%)", ["NameGlowSoftness"] = "Name Glow Softness (%)",
                ["NameReflection"] = "Name Reflection (%)", ["NameReflectionSoftness"] = "Name Reflection Softness (%)", ["NameReflectionDistance"] = "Name Reflection Distance (%)", ["NewTagLook"] = "NEW Tag Look", ["NewTagStyle"] = "NEW Tag Look", ["SweepXpBacking"] = "Sweep +XP Backing (%)", ["SweepXpHeight"] = "Sweep +XP Height (px)", ["BigXpDuringSweep"] = "Big +XP During Sweep", ["FadeCardLine"] = "Fade: Current Card Top Line", ["FadeSectionHead"] = "Fade: Category Bars", ["FadeStatMeters"] = "Fade: Stat Meters", ["FadeXpBar"] = "Fade: XP Bar", ["XpStartDelay"] = "XP Animation Start Delay (s)", ["DirectionalBorders"] = "Card Borders Fade One Way (MW4)", ["CardAccentLight"] = "Picked Card Pink Light", ["PanelLight"] = "Panel Light (%)", ["CardOpacity"] = "Card Opacity (%)", ["CardLockedOpacity"] = "Locked Card Opacity (%)",
            };
            var titles = new Dictionary<string, string>
            {
                ["1. General"] = "1. General", ["2. Graphics"] = "2. Look & Graphics", ["4. Preview"] = "3. Preview (Test The Animations)", ["3. Advanced"] = "4. Advanced",
                ["5. Testing"] = "5. CURRENTLY TESTING",
            };
            // what most players never touch: under F12's "Advanced settings" (the Preview tools too); the tuning section is hidden
            // 0.9.92: the 0.9.91 glow is replaced by the reflection below; being tried now: the reflection and the MW4 NEW tag
            // 0.9.91: the 0.9.9 dials are tuned (your values are the defaults: Ambient Motion 50, the line under the name off); being tried now:
            // 0.9.94: all tuned (your 0.9.93 values are the defaults)
            // 0.9.95 dials tuned in 0.9.96 (your log): NEW tag MW4, Locked Blueprint 0 (off), XP Fill 146, Orange, Sweep +XP Backing 100, Height 44, Big +XP During Sweep on
            // 0.9.99: the 0.9.97 fades are tuned (all four on, from your log)
            // 1.0.4: being tried now: how long after loading the XP animation waits
            // 1.0.11: DirectionalBorders on / CardAccentLight off — your picks (1.0.9 log), now the defaults (hidden)
            var testingNow = new HashSet<string> { "XpStartDelay" };
            // 0.9.98: taken out of F12 (the user: "stuff most users wouldn't use"); still in the .cfg file, their values still apply
            var hiddenSetup = new HashSet<string> { "ButtonLabel", "CopyButton", "TopMargin", "BottomMargin", "TileSize", "MaxTilesPerCategory",
                "CameraTurnDegrees", "SortOrder", "InsideGameUi", "PerformanceReadout", "VerboseLog", "DumpKey", "UseGameSounds", "PatternMotion" };
            var advanced = new HashSet<string> { "RefreshIcons", "UseGameSounds", "Vignette", "RedGlow", "BlurBackground", "HideMainMenu", "PatternMotion" };
            foreach (var kv in Config)
            {
                var a = kv.Value.Description?.Tags?.OfType<ConfigurationManagerAttributes>().FirstOrDefault();
                if (a == null) continue;
                if (names.TryGetValue(kv.Key.Key, out var dn)) a.DispName = dn;
                if (titles.TryGetValue(kv.Key.Section, out var cat)) a.Category = cat;
                if (kv.Key.Section == "3. Advanced" || kv.Key.Section == "4. Preview" || advanced.Contains(kv.Key.Key)) a.IsAdvanced = true;
                if (kv.Key.Section == "5. Testing" && !testingNow.Contains(kv.Key.Key)) a.Browsable = false; // tuned: its values are the defaults now
                if (hiddenSetup.Contains(kv.Key.Key)) a.Browsable = false; // 0.9.98: set-up / debug options hardly anyone touches (values kept)
            }
        }

        /// <summary>0.9.57 and older: Pattern = Topo / Contours → Damascus 1 / Damascus 2 (read from the settings file once).</summary>
        private void MigratePatternNames()
        {
            try
            {
                var text = System.IO.File.Exists(Config.ConfigFilePath) ? System.IO.File.ReadAllText(Config.ConfigFilePath) : "";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^Pattern\s*=\s*(Topo|Contours)\s*$");
                if (!m.Success) return;
                Pattern.Value = m.Groups[1].Value == "Topo" ? BackgroundPattern.Damascus1 : BackgroundPattern.Damascus2;
                L.Info($"settings: Pattern {m.Groups[1].Value} is now called {Pattern.Value}");
            }
            catch { }
        }

        private static ConfigDescription Desc(string text, int order, AcceptableValueBase range = null)
            => new ConfigDescription(text, range, new ConfigurationManagerAttributes { Order = order });

        /// <summary>
        /// Up to 0.9.21 the settings lived in General / Menu Button / Screen / Debug. BepInEx keeps values it no longer
        /// knows as "orphaned" entries: carry them over to the new names once, so nobody loses their settings
        /// (or their last seen level, which would bring back an old NEW tag).
        /// </summary>
        /// <summary>
        /// New graphics quality or RefreshIcons: the screen's kept pictures are thrown away and asked for again, and stash
        /// icons are redrawn at stash size in the background (main menu only). all = every level-list item; else only the
        /// ones this session drew bigger.
        /// </summary>
        internal static void RefreshPictures(bool all)
        {
            GameItems.ClearCopies();
            ProgScreen.PicturesCleared();
            var tpls = all ? ProgData.Levels.Keys.ToList() : GameItems.ScaledTpls();
            if (tpls.Count > 0) GameItems.RepairAll(tpls, all);
            else if (all) Toast.Show("Item icons refreshed");
        }

        private const string Gfx = "2. Graphics";

        /// <summary>Is the F12 settings window (BepInEx Configuration Manager) open? A preview waits until it's closed.</summary>
        internal static bool ConfigWindowOpen()
        {
            try
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.configurationmanager", out var info) || info?.Instance == null) return false;
                return Refl.Get(info.Instance, "DisplayingWindow") is bool b && b;
            }
            catch { return false; }
        }

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
                Take(Gfx, "FixStashIcons"); // 0.9.22 name
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
            if (Quitting) return;
            try { MenuCamera.LateTick(); } catch (Exception e) { L.ErrorOnce("late update", e); }
        }

        internal static bool Quitting;

        /// <summary>0.9.99: the game is closing. Stop our per-frame work, save the settings and close the log cleanly, and log
        /// how long that took: if the game still hangs after "quit: done", the hang is not in this plugin.</summary>
        private static void OnQuit()
        {
            if (Quitting) return;
            Quitting = true;
            var t0 = DateTime.Now;
            L.Info("quit: the game is closing" + (ProgScreen.IsOpen ? " (the Progression screen was open)" : ""));
            try { Instance?.Config?.Save(); } catch (Exception e) { L.Error("quit: saving settings", e); }
            L.Info($"quit: done ({(DateTime.Now - t0).TotalMilliseconds:0} ms); Progression does nothing more from here");
            L.Close();
        }

        private void Update()
        {
            if (Quitting) return;
            try
            {
                if (DumpKey.Value.IsDown()) Dump();
                else if (OpenKey.Value.IsDown())
                {
                    if (Typing()) L.Debug($"open key {OpenKey.Value} ignored: typing in a text box");
                    else if (!MenuHook.BarVisible && !ProgScreen.IsOpen) L.Debug($"open key {OpenKey.Value} ignored: not in the main menu");
                    else { L.Info($"open key {OpenKey.Value} pressed"); ProgScreen.Toggle("hotkey"); }
                }
                Diag.Tick();
                ProgData.Tick();
                MenuHook.Tick();
                ProgScreen.Tick();
                MenuWidget.Tick();
                OverallEmblem.Tick();
                GameItems.RepairTick();
                Toast.Tick();
                Sfx.Tick();
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
        // 1.0.2: Info / Warn / Error also go to BepInEx's log; Debug, Step and Trace only to Progression.log (BepInEx's
        // console + file per line was the expensive part). Everything is queued and written by a background thread.
        public static void Info(string s) { long t = Now; Source?.LogInfo("[Progression] " + s); File("info ", s); Cost(t); }
        public static void Debug(string s) { if (!Verbose) return; long t = Now; File("debug", s); Cost(t); }
        /// <summary>1.0.2: the testing-phase detail (clicks, keys, timings, game errors, frame stats…): Progression.log only.</summary>
        public static void Trace(string s) { if (!Verbose) return; long t = Now; File("trace", s); Cost(t); }
        public static void Warn(string s) { long t = Now; Source?.LogWarning("[Progression] " + s); File("WARN ", s); Cost(t); }
        public static void Error(string where, Exception e) { Source?.LogError($"[Progression] error in {where}: {e}"); File("ERROR", where + ": " + e); FlushNow(); }

        // What logging itself costs: time spent inside these calls on the game's thread, so a line can say
        // "logging took X ms of Y s" instead of guessing.
        private static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
        private static long _costTicks;
        private static int _costLines;
        private static void Cost(long start) { System.Threading.Interlocked.Add(ref _costTicks, Now - start); System.Threading.Interlocked.Increment(ref _costLines); }
        /// <summary>Logging time (ms) and lines since start.</summary>
        public static double CostMs => _costTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public static int CostLines => _costLines;

        // Progression.log next to the plugin. Lines are queued and a background thread writes them every 200 ms (and at
        // once for an error and at quit), so a crash loses at most the last 0.2 s.
        private static System.IO.StreamWriter _file;
        private static bool _closed;
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _queue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private static System.Threading.Thread _writer;
        private static readonly DateTime _t0 = DateTime.Now;
        private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>0.9.99: closes Progression.log at quit (nothing more is written after this).</summary>
        public static void Close()
        {
            FlushNow();
            lock (_fileLock) { try { _file?.Flush(); _file?.Dispose(); } catch { } _file = null; _closed = true; }
        }
        private static bool _fileTried;

        /// <summary>A step marker that only goes to Progression.log (for finding where a crash happened).</summary>
        public static void Step(string s) { long t = Now; LastStep = s; File("step ", s); Cost(t); }
        public static string LastStep = "";

        private static readonly object _fileLock = new object();

        private static void File(string kind, string s)
        {
            if (_closed) return;
            var when = _t0 + _clock.Elapsed; // cheaper than DateTime.Now (no time zone lookup per line)
            _queue.Enqueue($"{when:HH:mm:ss.fff} {kind} {s}");
            if (_writer == null)
            {
                lock (_fileLock)
                {
                    if (_writer == null)
                    {
                        _writer = new System.Threading.Thread(() => { while (!_closed) { System.Threading.Thread.Sleep(200); FlushNow(); } })
                        { IsBackground = true, Name = "Progression log writer", Priority = System.Threading.ThreadPriority.BelowNormal };
                        _writer.Start();
                    }
                }
            }
        }

        /// <summary>Writes every queued line to disk now.</summary>
        public static void FlushNow()
        {
            lock (_fileLock)
            try
            {
                if (_queue.IsEmpty || _closed) return;
                if (_file == null)
                {
                    if (_fileTried) { while (_queue.TryDequeue(out _)) { } return; }
                    _fileTried = true;
                    var dir = System.IO.Path.GetDirectoryName(typeof(L).Assembly.Location) ?? ".";
                    var path = System.IO.Path.Combine(dir, "Progression.log");
                    // the previous game session's log is kept as Progression.prev.log (a restart doesn't wipe it)
                    try { if (System.IO.File.Exists(path)) System.IO.File.Copy(path, System.IO.Path.Combine(dir, "Progression.prev.log"), true); } catch { }
                    _file = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false), 1 << 16);
                    _file.WriteLine($"LevelGate Progression {ProgressionPlugin.Version} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                while (_queue.TryDequeue(out var line)) _file.WriteLine(line);
                _file.Flush();
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
        public string DispName;   // the name F12 shows ("Background Pattern" instead of "Pattern")
        public string Category;   // the section title F12 shows
        public bool? IsAdvanced;  // only with F12's "Advanced settings" ticked
    }

    public enum GraphicsQuality { Low, Medium, High }

    public enum BackgroundPattern
    {
        Dots, Streaks,
        [System.ComponentModel.Description("Damascus 1")] Damascus1,   // was Topo
        [System.ComponentModel.Description("Damascus 2")] Damascus2,   // was Contours (redrawn: organic, uneven spacing)
        [System.ComponentModel.Description("Damascus 3")] Damascus3,   // rings
        [System.ComponentModel.Description("Damascus 4")] Damascus4,   // mirrored lines
        Marble, Pixels, Terrain, Random
    }
}
