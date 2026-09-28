# Modern Editor changelog

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
