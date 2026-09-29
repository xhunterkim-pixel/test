# LevelGate Progression (0.9.99, test build)

A Call of Duty style **Progression** screen inside Tarkov's main menu. It adds a
**PROGRESSION** button to the menu bar next to Character, Trading, Flea Market and the rest.

- **Bottom half:** five level cards per page (16 pages for levels 1–79).
  - Each card has an animated rank emblem, a rank title (a new rank every 5 levels), pictures from its top categories and its unlock count.
  - The emblems are in the `emblems` folder (sprite sheets listed in `emblems.txt`); delete it to get plain diamond badges.
  - Each card also says whether the level is **Unlocked**, your **Current level**, the **Next level** or **Locked** (with a small lock).
- **Header:** you — your rank emblem and rank, your level, your XP toward the next level and the next level's rewards (click it to jump there).
- **Top half:** the picked level's rewards (**LEVEL X REWARDS**), grouped by category, the selected reward big in the middle,
  and its details on the right (category, name, key stats, weight / size / caliber, description, requirement, INSPECT).
  - Left-click an item to show it big; right-click (RMB) to inspect it. Hover a tile for its full name.
- **Controls:**
  - ← → or A / D change the level by one.
  - Q / E, the arrows, the mouse wheel over the cards or the page dots change the page.
  - Home / End jump to level 1 or 79.
  - P or Esc closes the screen.
- It works on its own and never changes LevelGate. It only **reads**
  `BepInEx\plugins\LevelGate\config\level_requirements.json`, and picks up changes to that file while the game runs.

## 0.9.99
- **New option: F12 › Look & Graphics › XP Progress On Level Card** (on): your level's card fills with light up to your XP. Turn it off for a plain card.
- **Game exit:** the log now says `quit: the game is closing` and `quit: done (N ms)` when you quit. Progression stops all its work at that point, saves its settings and closes its log. Nothing in its code waits on anything at exit, so if the game still freezes after `quit: done`, the freeze is in the game or another mod.
- The CURRENTLY TESTING section is finished: the four fades (current card line, category bars, stat meters, XP bar) are on by default, as in your log, and the section is empty again.

## 0.9.98
- **Your level's XP fill is back to 0.9.95's look** (it fills the card from the left up to your XP), with the white line at its edge much fainter. 0.9.96's version lined up with the bottom line, but it didn't look right.
- **Colour Theme removed.** Red is the only look again (the old "Red Glow" name is back too).
- **F12 is shorter**: set-up and debug options most players never touch are no longer shown: Menu Button Text, Copy Look Of Button, Top / Bottom Margin, Tile Size, Max Items Per Category, Camera Turn, Drawing Order, Inside Game UI, Performance Readout, Detailed Log, Debug Dump Key, Use Game Sounds, Pattern Animation Speed. Their values still apply (they stay in the .cfg file).
- The four **Fade** switches from 0.9.97 are still in F12 › CURRENTLY TESTING.

## 0.9.97 (F12 › CURRENTLY TESTING: four on / off switches, all on)
- **Fade: Current Card Top Line**: your level card's top line fades out towards the left (full on the right), like MW4's card headers.
- **Fade: Category Bars**: the reward list's WEAPONS / ARMOR… bars fade out towards the right.
- **Fade: Stat Meters**: the meters under FIRE RATE / ERGONOMICS / RECOIL: the fill brightens towards its end, the track fades out to the right.
- **Fade: XP Bar**: the XP bar at the top: the fill brightens towards its end, the outline fades out to the right.
- Turn off the ones you don't like; the level header's lines ("2 ——— NEXT") already fade out at their ends, so they're unchanged.

## 0.9.96
- **New: F12 › Look & Graphics › Colour Theme**: Red (the original), Amber, Green, Teal, Blue, Purple, Pink, White. It recolours everything that was red / orange for "you": the level box, the current card's border, line, tag and XP fill, the diamond on the line under the cards, the top-right corner glow, EXP, and the main menu's PROGRESSION button (that one after a game restart). Unmet requirements stay red and NEW stays gold, since those colours carry a meaning. Changing it rebuilds the screen when you next open it.
- **Your level's XP fill now lines up with the line under the cards**: at 0 XP it ends at the middle of the card (on its tick and diamond) and moves right with the line's fill and waveform spike. No more hard white line at its edge (MW4 has none): the light just fades out softly.
- Your 0.9.95 picks are the defaults and the dials are hidden: NEW tag MW4, Locked Card Blueprint off, XP Fill 146%, Orange (= the theme colour), Sweep +XP Backing 100%, Big +XP During Sweep on.

## 0.9.95 (new looks in F12 › CURRENTLY TESTING, from your MW4 crops)
- **Fix: no freeze on the first right-click / Inspect** (848 ms in your log). The game's inspect classes are now looked up in the background when the screen opens.
- **Fix: the rank-up's name showed through the Dogtag window.** The window now fades in only once the rank emblem and name have lifted to the top.
- **NEW Tag Look → Mw4Dark** (new default, as in your MW4 crop): a dark see-through box, bright yellow NEW, a thin gold outline lit along the top, a soft glow. MW4 (0.9.92's gold box) and Old are still there.
- **Locked Card Blueprint (%)**: locked cards show their items like MW4's: the picture dark, only its outline lit in a cold blueprint colour, with a faint sideways streak. 0 = 0.9.94's dimmed picture.
- **Current Card XP Fill (%)** and **Colour** (Orange / Green): your level's card fills with light from the left up to your XP towards the next level, with a lit edge and a small equalizer at its foot. Hidden while the level-up sweep plays.
- **Sweep +XP Backing (%)** and **Sweep +XP Height (px)**: the +XP riding the level-up sweep gets a soft dark backing (it ran into the card names), and you can move it up.
- **Big +XP During Sweep** (off): the big "+84 975 EXP" over the item picture is hidden while the sweep plays, since MW4 shows only the one on the sweep. Tick it to get 0.9.94's back.
- The project file now lists all the Unity modules it uses (Audio, Web Request, Animation), so `dotnet build` works straight from a clean checkout.

## 0.9.94
- **Fix: the rank emblem on the Character › Overall screen was missing at levels 1–9.** The game writes the level as "01"; the plugin looked for "1" and gave up.
- Your 0.9.93 values are now the defaults and the dials are hidden: Name Reflection 29%, Softness 26%, Distance 47%, Card Opacity 100%, Locked Card Opacity 100%, Panel Light 100%, NEW Tag MW4.

## 0.9.93
- **Panels and cards back to 0.9.81's look** (you preferred it): the soft panel light (inner glow, lit edge, glass, tile / card sheen) is no longer dimmed by Decor Noise, and the cards are back at 100% (locked 85%). New F12 › CURRENTLY TESTING dials if you want to move them: **Panel Light** (100 = 0.9.81), **Card Opacity**, **Locked Card Opacity**. The scanlines / scratches / dither stay at the 0.9.9 Decor Noise level.

## 0.9.92 (F12 › CURRENTLY TESTING)
- **Name Reflection** replaces 0.9.91's Name Glow: the big name's letters cast a soft light-grey reflection that falls down (a touch to the right), like MW4's "HAN 86", instead of a halo all round. Dials: Name Reflection, Softness, Distance (0 = right behind the letters).
- **NEW Tag Look: MW4** — a gold box solid on the right and fading out to the left, lit top/left edges, and a second dark box behind it offset down-right (the double box). No glow. **Old** brings back 0.9.91's tag. The main-menu NEW tag changes after a game restart; the screen's tags on the next open.

## 0.9.91
- **Name Glow** (F12 › CURRENTLY TESTING, with Name Glow Softness): a soft light halo hugging the big name's letters, like MW4's weapon names. It's drawn by the text's own underlay; shaders without an underlay get a faint light behind the name instead, and the log says which.
- The 0.9.9 dials are tuned and your values are now the defaults: Ambient Motion 50%, the rest as shipped. The line under the name is off.

## 0.9.9 — polish pass (all in F12 › CURRENTLY TESTING)

**Polish** switches the whole pass off, to get exactly 0.9.81 back for a before / after comparison. The dials:

- **Decor Noise:** scanlines, rulers, corner marks, scratches, dither and inner glows at a % of 0.9.81.
- **Micro Labels:** the tiny system labels at a % of 0.9.81.
- **Border Fade:** MW4 frames. Borders are full strength in the middle of each side and fade toward the ends (panels, tiles, cards).
- **Ambient Motion:** drifting lights, breathing emblems and background pattern drift at a % of 0.9.81.
- **Quiet During Effects:** the idle movers rest while a level up, the XP fill or a picture load-in plays.
- **Flourish:** selection shine, hover glitch and title glitch at a % of their own settings.
- **Ambient Light / Ambient Light Colours:** the picked card's soft lights are stronger. Mixed adds a drifting pink / purple accent.
- **Card Rest / Card Locked:** how far the bottom cards that are neither yours nor viewed step back.
- **Small Text Minimum:** information text is never smaller than this at 1080p.
- **Neutral Rank Pips:** state colours only (grey, orange for your level). Red is kept for unmet requirements.
- **Line Under Name / its opacity:** MW4's grey italic line under the big name (the description's first sentence, or the full name).
- **Tooltip Delay:** the full-name tooltip waits until you rest on a tile (1 s).

Other changes:
- Clicking an earlier level card or an earlier tile runs the light sweep right to left, like A / W.
- XP Bar Edge is fixed at **Shadow** (your pick).

Build-time dials apply the next time the screen opens (close and reopen with P). Every change is written to the log.

## Install
Copy `BepInEx\plugins\LevelGateProgression\` (LevelGate.Progression.dll + eft-logo.png) into your SPT folder.

## If something's off
Everything it does is written to `BepInEx\LogOutput.log` in lines starting with `[Progression]`:
- the menu bar objects it found;
- which button it copied;
- the font it used;
- how many items and categories it read;
- every open, close and page change.

**Ctrl+F10** writes a full report to the log. Send that log.

Settings are in `BepInEx\config\com.kkyangg.levelgate.progression.cfg` (or the F12 menu):
- **1. General**: OpenScreenKey (P), MenuBarButton, MainMenuShortcut, HideMainMenu, SoundVolume (level-up / new-rank sounds), UseGameSounds, BlurBackground.
- **2. Graphics > Quality**:
  - Low = Performance Mode (the box bottom-right on the screen): the five heaviest things off — pictures drawn smaller, fewer and one at a time; no moving background pattern; no background blur; no texture layers (UI Detailing off); still emblems and no slides / flashes / pulses.
  - Medium (default): sharp item pictures; weapons at stash size.
  - High: extra-sharp weapons too (every level-list weapon is redrawn at stash size after the screen closes, since the game can carry big weapon pictures over to stash weapons).
- **2. Graphics > XpAnimation** (on by default): after you gain XP, the next open plays it out in beats: the XP bar and level ups, then a new rank emblem, then the new levels' cards unlocking, then your new level is selected. Space (or a click): tap = next moment (next level up, the rank, the cards, the summary, the end), hold Space = next part. Esc closes.
- **2. Graphics > UI Detailing (0–100%)**: all the surface detail in one slider: scratches, smudges, fingerprints, grain, corner marks, edge lights, dither, scanlines, the selection reflection and the bloom (your card, the rank emblems). 0 = clean and flat; those layers aren't drawn at all. Live.
- **2. Graphics > Detail Animation (0–100%)**: how much the lights move: the red glow drifting, specks of light over the background, the rank emblem's glow breathing, the picked card's coloured bloom and dotted light, the light under the big picture, the tracer along the XP bar. 0 = still; off with Reduce Motion / Performance Mode.
- **2. Graphics > Background Pattern Opacity (0–100%)**: the pattern's own strength (separate from UI Detailing).
- **Advanced > Vignette / RedGlow**: the dark corners and the red top-right glow (1 = original; live).
- The right panel shows each stat with the game's own inspect icon; the small stats are inspect-style strips, two per line. Each reward category is a full-width title tab with its items in a framed box under it, like the game's containers; click the tab to fold / open it.
- **2. Graphics > Pattern**: the faint background pattern: Dots (default grid), Streaks (vertical grain), Contours (smooth wavy lines), Topo (busy topographic lines), Marble (mirrored marbling), Pixels (LED wall with light bands), Terrain (3D ridge lines) or Random (a different one each open). Neutral white; its strength: Background Pattern Opacity. Live.
- **2. Graphics > PatternMotion** (0–3, default 1): how fast Streaks / Contours / Topo slowly move (0 = still). The pattern is worked out on a worker thread and refreshed about 10 times a second (5 on Low), only while the screen is open; closed, it does nothing, and going into a raid frees it.
- **2. Graphics > RefreshIcons**: tick once to redraw every item picture (the screen's own and the stash icons). A message at the top of the screen shows the progress and when it's done. Changing Quality does this by itself for the pictures it affects.
- The first open of a menu visit shows a short LOADING PROGRESSION screen while the pictures of your page and the pages next to it are drawn. Going into a raid lets them go (memory); the next open loads them again.
- **4. Preview**: PlayLevelUp / PlayNextRank / PlayUnlock (with Levels) play the XP animation with made-up numbers. Nothing real changes; closing the screen brings your real level back.
- **3. Advanced**: button label / look, margins, tile size, opacity, camera turn, draw order, logging. Settings from 0.9.21 and older are carried over automatically.
