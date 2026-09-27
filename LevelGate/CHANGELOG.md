# Changelog

Versions are **MAJOR.MINOR.PATCH**:

- **MAJOR**: files or settings change so older copies can't read them (you'd need to update everything together).
- **MINOR**: new features; older files keep working.
- **PATCH**: fixes only.

## Level & Item Editor (LevelAndItemEditor.exe)

### 1.1.0
- **Progression** tab: a Call of Duty style unlock track. It shows five levels per page (arrows, Q / E, the mouse wheel or the page dots) and everything that unlocks at the picked level, grouped by category.
- Notes column (both lists), and columns you can resize like the Custom Trader Creator.
- Price column in both lists, with a Flea / Handbook switch.
- The Overall chart follows the picked category.
- The header shows the installed Level Gate plugin's version.

### 1.0.0
- Level Limits and Item Stats tabs, modded items, tags, custom order, category moves.

## Level Gate (BepInEx plugin)

### 1.6.3
- Tooltip: any `[LOCKED - Lvl N]` / `[LOCKED]` left in a rewritten tooltip is removed (also when Show Me The Money wraps the name in color tags).
- Debug > TooltipLog (on for now): the first 80 tooltips of each game start are written to the log, before and after the rewrite.

### 1.6.2
- Tooltip: the `[LOCKED - Lvl 40]` name prefix no longer stays in front of the "LOCKED - Name" header (it happened on guns, ammo, face gear… whenever only the plain name was matched).

### 1.6.1
- The tooltip shows on the first hover in the stash and in raid (not only after hovering the checkmark first).

### 1.6.0
- Picks up config changes while the game runs.

## ItemStatEditor (server mod)

### 1.0.0
- Applies the Item Stats edits (meds, stims, food) at server start.

## LevelGate Progression (BepInEx plugin, separate DLL)

### 0.3.0
- **Tab:** left of Character. The icon keeps the original icon size, which also fixes the extra width and spacing.
- **P** opens and closes the screen (setting General > OpenScreenKey). It does nothing while you're typing in a text box or outside the main menu.
- **Item icons:** the game's icon loader is found by what it does rather than by name (the old name doesn't exist in this Tarkov version).
- **Right-click:** checked directly under the mouse (the game doesn't pass right-clicks to our tiles). It opens the game's inspect window using an ItemContext.
- **Cards:** three item pictures instead of a line of names. States are UNLOCKED, YOUR LEVEL, NEXT LEVEL or LOCKED.
- **Featured item panel** on the right, like the battle pass: a big picture of the item under the mouse, its category and unlock level.
- **Page dots:** evenly spaced. The current page is taller, not wider.
- **Closing:** the screen closes when the game changes screen (EftScreenManager.OnScreenChanged, the same event MoxoPixel's Menu Overhaul uses).

### 0.2.3
- Clicking the tab did nothing: the game's tab code handles clicks itself without telling anyone. The tab now has its own click catcher.
- The tab is copied from Character instead of Handbook (Handbook's counter area made the copy wider than the others). The log shows the sizes of both.

### 0.2.2
- The tab didn't appear in 0.2.1: adding it crashed on a log line that read the name of the counter it had just removed, and each retry failed the same way. Fixed; a failed attempt now also leaves nothing behind.

### 0.2.1
- The PROGRESSION tab was greyed out and couldn't be clicked: it was copied at start-up, while the game still has every tab disabled. Now it's copied once the game has enabled its tabs, then forced enabled and clickable and kept that way. Anything it has to fix is logged.

### 0.2.0
- The PROGRESSION button is a real tab in the menu bar (a copy of the Handbook tab, with the same hover and lit look). 0.1.0 copied the whole row of tabs.
- No more lag: each level change took ~0.6 s because a failed lookup (the XP table for "% to next level") searched all the game's classes every time. It's now looked up once. The top and bottom halves also redraw separately.
- Item icons from the game itself (weapons as their default preset). Right-click a tile to open the game's inspect window.
- Fixed an error on the first open, and big titles and the page label being cut off.

### 0.1.0
- First test build: a PROGRESSION button in the main menu bar and a Call of Duty style progression screen. It reads LevelGate's level list and logs a lot for troubleshooting.
