# LevelGate Progression (0.9.94, test build)

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
