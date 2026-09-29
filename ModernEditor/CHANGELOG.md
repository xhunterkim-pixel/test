# Modern Editor changelog

## 2.0.8 (editor)
- **Progression: can players get it?** Each item tile has a small green icon when a trader sells it (⇄ barter, ₽ buy) or a quest gives it (★), or a red **UNOBTAINABLE** tag. Hover it to see who sells it and for what. The level header counts the unobtainable ones. A new **Unobtainable Check** setting above the levels picks where to look: **Read Vanilla** (the game's own traders), **Read Modded** (your traders made here, switched on) or **Read Both**.
- **Click the obtainable / UNOBTAINABLE tag** for a pop-up of every way to get the item: each game trader (loyalty level and price, every barter option), each of your traders' offers and quest rewards with a **Go to Offer / Quest** button. Unobtainable items open it too.
- **Notes per level** (Progression): a notes box next to the level's title. Click it and type; it saves by itself. Levels with notes get a small gold underline in the level grid.
- **Other Gear split** on the item pages (Level Limits, Progression, Item Stats): **Headsets**, **Face Covers**, **Eyewear** and **Armbands** are categories of their own. The trader pages still group them as Other Gear (what the server knows).
- **Ctrl+C copies an ID**: hover any entry (item, offer, quest, trader, objective, reward, Progression tile or quest) and press Ctrl+C. No click needed; normal copy still works in text boxes.
- The trader box at the bottom left only shows on the trader pages (Trader, Offers & Barters, Quests).
- The header shows the editor's version only (the Level Gate version is in its tooltip).
- **Trader page regrouped**: Profile (name, nickname, surname and location two to a row), In the Game (on / unlocked / flea / position / restock), Prices & Selling, Loyalty Levels.
- **Long help texts moved behind a small ⓘ circle**: hover it to read. Short hints stay where they are. This applies on every page.
- **Long lists**: quest rows are about a third shorter (one line per objective and for the rewards; full text on hover). The Stock column is short ("2 · max 1", details on hover). Cut-off cells show their full text on hover. The Modded column only shows when something in the list uses a mod. "Level follows an item" is a ⟲ next to the quest's level instead of a long tag.

## 2.0.7 (server mod only)
- Quieter SPT server console. One line when the traders load, one when the item stats apply, plus any real warnings:
  `[ModernEditor] 2.0.7 ready — 5 traders · 41 offers · 21 quests (17 follow Level Gate levels) · details: user\mods\ModernEditor\logs\server_….log`
  `[ModernEditor] 58 item stat edits applied (meds, stims, food).`
  Everything that used to be printed goes to the log file: every trader, quest, icon, dynamic level move, switched-off trader and required mod.

## 2.0.6
- Fixed the false "can't be worn (not gear)" warning for chest rigs and backpacks. In Tarkov's item tree they don't sit under Equipment, so the editor filed them as "Other". They now count as gear, in the check and in the "wearing" item picker. The game's wearing check covers every equipment slot: helmet, earpiece, face cover, eyewear, armband, armor, rig, backpack, and the weapon and melee slots.
- "Must Be Wearing": a new **Full Set** switch. Off (the old behaviour): any one of the listed items is enough. On: all of them at once, e.g. a helmet and an armor together. It uses the same format as the game's own quests.
- New checks:
  - Melee-only kill objectives with a minimum distance are flagged, since they can never be met.
  - A boss kill on a map where that boss doesn't normally spawn gets a note (not a warning, since mods can move bosses).
- The log records the window size and the interface size picked.

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
