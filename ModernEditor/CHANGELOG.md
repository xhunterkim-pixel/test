# Modern Editor changelog

## 2.0.1
- Dynamic quest levels follow the offer's new item when you swap the item of the offer the quest unlocks, or point the quest at another offer. A quest set on purpose to follow some other item is left alone.
- Quests saved before this fix, that follow a different item than the one they unlock, get a warning in Checks and a "Follow <item>" button in the quest editor.
- Item Stats shows its own file (user\mods\ModernEditor\item_stats.json) instead of Level Gate's. 📂 opens that folder, and a note appears if edits were read from an old ItemStatEditor file.
- Level Limits, Progression and Item Stats: the big header is replaced by one slim line (counts and file).
- Sidebar groups: Traders / Level Gate (Level Limits, Progression) / Items (Item Stats) / More. Ctrl+4 Level Limits, Ctrl+5 Progression, Ctrl+6 Item Stats.
- Start page offers to clean up old files. Only what you tick is removed, and it goes to the Recycle Bin:
  - the ModernEditor_old_mods backups;
  - the old editors' settings and icon caches (icons are kept);
  - the old editor programs;
  - the first editor's user\mods\LevelGate\item_stats.json (merged first).
- Logs:
  - The editor writes Logs\ModernEditor_<date>_<time>.log next to ModernEditor.exe: every action, file, page error and crash. There's a Logs card on the start page and a button on Checks & Log.
  - The server mod writes user\mods\ModernEditor\logs\server_<date>_<time>.log.

## 2.0.0
- Merged the Custom Trader Creator, the Level & Item Editor and the ItemStatEditor server mod into Modern Editor:
  - one exe with the interface built in;
  - one server DLL (`user\mods\ModernEditor\ModernEditor.dll`).
- New pages: Level Limits, Item Stats and Progression, inside the trader editor's layout.
- Files stay separate: one trader.json per trader, item_stats.json and disabled_levels.json. Level Gate's level_requirements.json layout is unchanged.
- One-click move of the old CustomTraders / ItemStatEditor files, with backups. The old editors' settings are imported.
- Batch barters and quests from selected items, by their level.
- Dynamic quest levels that follow an item's Level Gate level, with an offset.
- Right-click level edit in Progression.
- Switch level limits off and on (kept in disabled_levels.json).
- Used By cross-links, the Go To palette (Ctrl+K), a start page with tools and notes, and bigger main buttons.
