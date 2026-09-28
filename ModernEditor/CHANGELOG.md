# Modern Editor changelog

## 2.0.5
- New quest writer (✨ Generate). It writes the way Tarkov's traders talk: short, in character, a story rather than a checklist (the game lists the objectives under it anyway).
  - An opener that knows the quest chain ("…went well. Don't let it go to your head.") and the player's level.
  - Why the trader wants it, from the items asked for, the targets and the maps.
  - The job in one plain line; several ways become "…Or…Your call."
  - What's in it: shop unlocks, money, items and standing.
  - Hardcore as a warning, and a sign-off with attitude.
  - The trader's voice comes from what they sell: medic, gunsmith, outfitter, fixer or broker.
  - The completed message thanks the player for what was done and hands over the rewards.
  - Each ✨ click gives another version, and the same version comes back the same way.
- **Keep Text Up to Date** (quest editor): the description and completed message rewrite themselves when objectives or rewards change. Typing your own words switches it off. Generating over text you wrote asks first (Ctrl+Z also brings it back).

## 2.0.4 (editor only)
- Right-click copying:
  - Offers: Copy Item Name, Copy Short Name, Copy Item ID.
  - Quests: Copy Quest Name, Copy Its Item Name, Copy Quest ID.
  - Level Limits: Copy Item Name, Copy Short Name, Copy Item ID.
  - With several rows picked, each one goes on its own line.

## 2.0.3
- Quests without a picture use the picture of the item they unlock or give (the first reward or barter item):
  - It shows in the editor right away.
  - On Save it's drawn as a picture file (quest_<id>_item.png in the trader's folder), so the game shows it too.
  - It follows the item if you change it, and your own or a game picture always wins.
- The quest editor is ordered the way you work: Quest (name, level, hardcore) → Objectives → Rewards → Unlock Requirements → Required Quests → Text & Picture → Tags & Notes.
- Appearance → Show → Help Text: hide the grey explanations once you know the editor. Warnings stay.
- The trader list opens right under the trader button instead of at the bottom of the panel.
- Pictures of any type are accepted for trader icons and quest pictures (webp, avif, svg, ico, png, jpg, bmp, gif, tif). They're converted to PNG, which the game reads.
  - A webp / avif / svg you put into a trader's folder by hand is converted on the next Save, and the trader file is updated.

## 2.0.2
- **Section switches** in the left panel replace the Add-ons page. The switches are saved in user\mods\ModernEditor\config.json, which the server mod reads too:
  - Traders off: the server loads none of your custom traders (files kept), and the trader pages are hidden.
  - Level Gate off: Level Limits and Progression are hidden, offers don't show levels, and dynamic quests keep their saved level. Level Gate itself keeps running in game.
  - Items off: item_stats.json isn't applied (edits kept), and Item Stats is hidden.
- **Interface size** (Appearance → Size):
  - Auto grows the whole page with the window: about 120% at 1080p and 160% at 1440p.
  - You can also pick 100–200% by hand.
- **Progression, plain and bigger:**
  - All 79 levels are in one clickable grid with their unlock counts.
  - Below the grid, the picked level shows larger item tiles with full names. Right-click a tile to change its level.
  - Q / E jump 5 levels.
  - No badges, logo or animations.
- Trader header is more compact, and the trader icon in the right panel is smaller.
- The log is quieter: settings saves are one short line.

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
