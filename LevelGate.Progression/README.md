# LevelGate Progression (0.6.0, test build)

A Call of Duty style **Progression** screen inside Tarkov's main menu. It adds a
**PROGRESSION** button to the menu bar next to Character, Trading, Flea Market and the rest.

- **Bottom half:** five level cards per page (16 pages for levels 1–79).
  - Each card has a rank badge and title (a new rank every 5 levels), the first items and its unlock count.
  - Each card also says whether the level is **Unlocked**, your **Current level**, the **Next level** or **Locked**.
- **Top half:** everything that unlocks at the picked level, grouped by category.
  - The header shows your level, your XP toward the next level and the next level's reward count.
  - Left-click an item to show it big; right-click to inspect it.
  - A red bloom glows from the right, stronger on locked levels.
- **Controls:**
  - ← → change the level by one.
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
- **Menu Button > CopyButton**: which menu button to copy (a part of its name from the log).
- **Screen > TopMargin / BottomMargin**: keep the game's bars uncovered.
- **Menu Button > AddButton = false**: use the P key only (General > OpenScreenKey).
