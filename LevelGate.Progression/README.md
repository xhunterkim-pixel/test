# LevelGate Progression (0.9.28, test build)

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
- **1. General**: OpenScreenKey (P), MenuBarButton, MainMenuShortcut, HideMainMenu, BlurBackground.
- **2. Graphics > Quality**:
  - Low: lighter pictures and still emblems, for slower PCs (same as the PERFORMANCE MODE box bottom-right on the screen).
  - Medium (default): sharp item pictures; weapons at stash size.
  - High: extra-sharp weapons too (every level-list weapon is redrawn at stash size after the screen closes, since the game can carry big weapon pictures over to stash weapons).
- **2. Graphics > XpAnimation** (on by default): after you gain XP, the next open plays it out in beats: the XP bar and level ups, then a new rank emblem, then the new levels' cards unlocking, then your new level is selected. Click or Space skips.
- **2. Graphics > RefreshIcons**: tick once to redraw every item picture (the screen's own and the stash icons). A message at the top of the screen shows the progress and when it's done. Changing Quality does this by itself for the pictures it affects.
- The first open of a menu visit shows a short LOADING PROGRESSION screen while the pictures of your page and the pages next to it are drawn. Going into a raid lets them go (memory); the next open loads them again.
- **3. Advanced**: button label / look, margins, tile size, opacity, camera turn, draw order, logging. Settings from 0.9.21 and older are carried over automatically.
