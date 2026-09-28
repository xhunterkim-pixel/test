# Modern Editor v2.0.0 — traders, level limits, item stats and progression in one (SPT 4.1)

Modern Editor replaces three tools:

- the **Custom Trader Creator** (CustomTraders editor and server mod)
- the **Level & Item Editor** (LevelAndItemEditor.exe)
- the **ItemStatEditor** server mod

It is one Windows program and one server DLL:

| Part | What it is | Where it goes |
|---|---|---|
| **ModernEditor.exe** (`ModernEditor.Editor\`) | The editor. The interface is built into the exe. | Anywhere **outside BepInEx**, e.g. `C:\SPT\Modern Editor\` |
| **ModernEditor.dll** (`ModernEditor.Server\`) | The SPT 4.1.x server mod. It adds your traders, offers, barters and quests, applies item stat edits, and moves dynamic quests to their item's level. | `SPT_Runtime\user\mods\ModernEditor\ModernEditor.dll` |

Level Gate (the BepInEx plugin) and Level Gate Progression are separate mods and are **unchanged**.
Modern Editor edits Level Gate's own `BepInEx\plugins\LevelGate\config\level_requirements.json`, keeping the layout Level Gate reads.

## Files (each one separate, as before)

```
SPT_Runtime\user\mods\ModernEditor\
  ModernEditor.dll
  traders\<Trader>\trader.json     one per trader (offers, barters, quests), same layout as CustomTraders
  deleted_traders\<Trader>\        the trash (restore from the editor)
  item_stats.json                  meds / stims / food edits (was ItemStatEditor\item_stats.json)
  disabled_levels.json             level limits you switched off (see below)
BepInEx\plugins\LevelGate\config\level_requirements.json   Level Gate's file, same layout as before
%AppData%\ModernEditor\settings.json                        editor settings
```

## Moving over from the old mods

1. Install `ModernEditor.dll` into `user\mods\ModernEditor\`.
2. Open ModernEditor.exe. On first start it imports the settings of both old editors: SPT folder, Level Gate file, look, columns, mod list, mod ids and background picture.
3. If `user\mods\CustomTraders` or `user\mods\ItemStatEditor` are still there, the start page shows **Move My Files**. This does two things:
   - It copies your traders, deleted traders and item_stats.json into `user\mods\ModernEditor`.
   - It then moves the old mod folders to `user\ModernEditor_old_mods\` as a backup, with their DLLs renamed `.dll.old`. This stops the server from loading the same traders twice.
4. Restart the SPT server.

If the old mods are still installed next to ModernEditor.dll, the server log shows a warning.

## Pages

- **Traders**: Trader, Offers & Barters, Quests. Everything from the Custom Trader Creator is still here: sorting, tags, categories, columns, bulk edit, drag to reorder, on/off switches, the View menu, deleted traders, and add-ons.
- **Items**:
  - **Level Limits**: Level Gate's level per item, with categories, filters, multi-select and undo.
  - **Item Stats**: meds, stims and food.
  - **Progression**: what unlocks at each level.
- **More**: Mods, Add-ons, Checks & Log.

## New in 2.0.0

- **⚡ Batch barters / quests**:
  - Pick items in Level Limits, Offers or Progression, then press ⚡ or right-click.
  - It makes, for the chosen trader:
    - an offer per item: money, barter or both, with the loyalty level taken from the item's level;
    - and/or an unlock quest per item: pay, kill PMCs, kill Scavs, extract, or hand over the barter items, with XP scaled by level.
  - Quests can be chained in level order, and everything can be tagged.
- **Dynamic quest level**:
  - Any quest can follow an item's Level Gate level, plus or minus an offset (for example, item at 14 with offset 0 means the quest needs 14).
  - Change the item's level and the quest follows, in the editor and on the server (the server reads Level Gate's file at start).
  - Quests that follow an item show a ⟲ badge.
- **Right-click level in Progression**: set any item's level in place, without leaving the page.
- **Switch limits off / on** (Level Limits):
  - Batch switch items off: they are removed from level_requirements.json, so Level Gate no longer locks them, and their level is kept in `disabled_levels.json`.
  - Switching them back on puts the same level back into Level Gate's file.
  - Use the "Switched Off" filter to find them.
- **Used By**: an item shows the offers, barters and quests that use it, with one click to each.
- **Go To (Ctrl+K)**: jump to any trader, quest, offer, item or page.
- **Start page**: tool cards, your traders, and notes (files to move, a missing server mod).
- **Save All (Ctrl+S)** saves level limits, item stats and traders together. Undo / redo and the unsaved counter cover every page.

## Keyboard

F1 lists everything. The main ones:

- Ctrl+S: save all
- Ctrl+Z / Ctrl+Shift+Z: undo / redo
- Ctrl+K: go to
- Ctrl+M: start page
- Ctrl+F: search
- Ctrl+1…8: pages

In Level Limits and Progression, the old Level & Item Editor keys still work as before.

## Building

- `Build-Editor.bat`: publishes `Editor\ModernEditor.exe`.
- `Build-Server.bat`: builds `ModernEditor.dll` and copies it to `<SptServerDir>\user\mods\ModernEditor`. Set `SptServerDir` in `ModernEditor.Server\ModernEditor.Server.csproj` first.

Both need the .NET 10 SDK. The editor needs WebView2, which is part of Windows 10/11.

Made by discord : k_kyangg
