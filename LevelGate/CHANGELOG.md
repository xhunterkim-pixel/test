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

### 0.1.0
- First test build: a PROGRESSION button in the main menu bar and a Call of Duty style progression screen. It reads LevelGate's level list and logs a lot for troubleshooting.
