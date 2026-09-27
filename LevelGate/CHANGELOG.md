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

### 0.9.10 — final audit fixes
- **Level terms spelled out:**
  - The header has "CURRENT LEVEL" next to your level number, and the rank line reads "Rank 1 of 16 · next: Drifter (level 6)".
  - The reward link reads "Next level 3: 13 unlocks ›".
  - The list title reads "VIEWING LEVEL 1 REWARDS", with its state on the line below (CURRENT / UNLOCKED / NEXT / X LEVELS AWAY).
  - Card headers name their role: LEVEL 1 · VIEWING, LEVEL 2 · CURRENT, LEVEL 3 · NEXT. The card footer only says UNLOCKED, LOCKED or NEW.
- **Centre preview sharpness (real fix):**
  - The "big" copy was often the game's stash-size render (64 px a cell), because the first picture to arrive was accepted, so the preview was a stretched thumbnail.
  - Only a render of the requested size is kept now. A stash-size fallback is shown but never cached.
  - The render scale follows the item's cells and the preview's size on screen: 2–8x, about 440 px on the long side at 1080p.
  - Copies get mipmaps, so big renders shown small stay clean.
- **Performance mode:** back on screen as a quiet PERFORMANCE MODE checkbox (bottom-right, on the page-bar line), still also in the config. Still emblems in performance mode are intended. The open-screen log line says whether it's on.
- **Level cards:** about 30% of the screen height (was 33%). Future levels are dimmer, and only the viewing or current card has a bright count.
- **Narrow screens:**
  - The page bar stretches between fixed insets instead of a fixed 1120 px, so it can't run into the checkbox.
  - The list title and state are on two lines.
  - The rank line is shorter.

### 0.9.9 — UI/UX pass
- **Hierarchy:**
  - The header is about you only: your rank emblem, rank name, "Rank X of 16 · next rank at level N", your level square and XP.
  - "PROGRESSION" is a quiet 22 px grey page label. The selected level is shown only as **LEVEL X REWARDS**, on its card and in the reward content.
- **Colour roles:**
  - Orange means you (your level, XP, current level). Light neutral means selected. Grey means secondary, future or locked.
  - Red appears only on an unmet requirement count, and green is restrained for met or unlocked.
  - The background glow is about 40% weaker.
- **Right panel:**
  - Order: category → name → big damage / penetration / armor class / resource → weight / size / caliber → description → requirement box → INSPECT with an RMB keycap.
  - Laid out by content, so long names push things down instead of overlapping. The description scrolls instead of being cut off.
  - The duplicate "Category" row is gone.
- **Preview:** the centre ITEM / CATEGORY / UNLOCKS AT block is gone. The preview fills the stage with a soft key light and is the focal point.
- **Reward tiles:**
  - Inventory-style tiles on a responsive grid (4 columns at 1080p, fewer on narrower screens), with 8 px gaps and the icon at about 80%.
  - Category colour appears once per section, as a 3 px bar. Names get two lines, show the full name when short names collide, and hover shows a tooltip.
  - The coloured strips, red dots and name bars are gone. Locked tiles are dimmed.
- **Level cards:**
  - Current level: orange header and state. Selected: light 2 px frame with a top bar. Hover: brighter edge.
  - Future levels are muted, with one small lock and "LOCKED" in grey. The three per-card picture locks and the separate "+N" are gone ("147 unlocks").
  - The rank title is small caps. The NEW state shows on levels reached since your last visit.
- **Locked state:** one primary explanation, the requirement box with its unmet count in red. The preview lock stays as a quiet secondary signal. The list says "X LEVELS AWAY" in grey.
- **Interaction:**
  - Hover and selected states on tiles and cards, with short ~120 ms fades and the game's hover sound.
  - Hovering a tile lifts it slightly. Selection is shown by shape (a top bar), not colour alone.
  - The next-level-reward line is a visible link (underline on hover, "›").
- **Spacing and type:**
  - One spacing scale: 4, 8, 12, 16, 24 px.
  - A 48 px screen margin shared by the panels and cards, with the arrows outside in the margin. 16 px gutters and panel padding.
  - Six font sizes (12, 13, 15, 22, 24, 34) and two letter-spacing values.
- **Developer controls:** "Performance mode" and "Fix stash icons" are off the screen and in the config: `Screen > PerformanceMode` (applies live) and `Screen > FixStashIcons` (tick once).
- **Removed dead code:** the old diamond sprite, unused colour constants, the performance toggle and repair button UI, and the per-tile frame map.

### 0.9.8
- **Q / E load instantly:**
  - Once the current page's pictures are done, the card pictures of the previous and next page are drawn ahead, at most one per frame.
  - Paging uses those pictures right away. Up to 90 big copies are kept (was 40).
- **No names flashing in** the card picture slots while a picture loads.

### 0.9.7
- **Opening from another screen** (Character, Traders…): the game first goes back to the main menu, as the MAIN MENU button does, and then Progression opens. Before, the other screen stayed open underneath.
- **Big-icon copies actually work now.** The copy failed on the game's fractional sprite sizes (e.g. 293.85 px wide), so the game's enlarged icon was used instead.
- **"Fix stash icons" button** (bottom-right, next to Performance mode): redraws every item on the level list at stash size, a few per frame, with progress shown. It clears big icons left in the game's icon cache by older builds.
- **Picture quality:** the list tiles stay at 1x, the card pictures are now all 2x, and the big centre picture is 4x (was 6x). Performance mode lowers these to 1x / 1x / 2x.
- **Main menu shortcut:** half the size, and the rank emblem now shows in colour (animated) instead of as a dark silhouette.
- **Panel borders:** no longer turn red on locked levels; only the background glow does.

### 0.9.6
- **Main menu shortcut** (bottom-left, like the game's EXPANSIONS block):
  - A red block with your rank emblem as a dark silhouette, **PROGRESSION** in red, and "Level X · Rank | XP / needed EXP" under it.
  - A red glow comes out of the corner, and it brightens on hover with the game's hover sound.
  - Clicking it opens the screen. It's part of the game's main menu, so it hides and shows with it.
  - Turn it off with `Menu Button > MainMenuShortcut`.
- **NEW tag:** a green NEW shows on the shortcut after you level up, until you open the screen. The last seen level is saved in the config, so it survives restarts.
- **Tab icon:** the PROGRESSION tab's icon is now a red block with a cut corner and a dark diamond, instead of the white diamond.

### 0.9.5
- **XP bar:** fixed an off-by-one-level sum. At level 2 it showed your total XP (1 364 / 1 000) instead of the XP into the level (364 / 1 000).
- **Enlarged stash icons:**
  - A bigger render is now copied into the screen's own picture the moment it arrives, and the game's shared icon is redrawn at stash size right away, instead of only when the screen closes.
  - Copies are reused, so the same item isn't rendered big again.
  - The close-time restore still runs as a backup.
- **Background:** a faint Arena-style dot grid, which fades in with the screen.
- **Grit** on the preview frames is 85% lighter, and the **vignette** is 80% lighter.

### 0.9.4
- **XP bar:** now reads the experience table from the SPT server (`/client/globals` → `exp_table`) in the background, since the game client has no class holding it by name. The bar fills in as soon as the answer arrives.
- **Stutter:** icons the game hasn't drawn yet are requested a few per frame (cards first) instead of all at once. Performance mode halves the rate.
- **Inspect:** only one sound now; the game's inspect window already plays its own.
- **Stash icons:**
  - Everything drawn bigger is redrawn at stash size on close, and again two seconds later for big renders that were still in progress.
  - Icons already stuck big from older builds are in the game's on-disk icon cache: use "Clean temp files" in the SPT launcher once.
- **EFT battle pass style:** regular-weight uppercase panel headings (UNLOCKS, item type, PROGRESSION), a regular item name and requirement, and an italic hint.
- **Vignette:** darkens the screen's edges, and the whole screen fades in smoothly when opened.
- **Grit:** smudges, grit and a few scratches on the item preview frames (cards and the big picture).

### 0.9.3
- **Performance mode** (checkbox bottom-right, or `Screen > PerformanceMode` in the config):
  - The big picture and the cards' main pictures are drawn at normal size instead of extra sharp.
  - The emblems stand still.
  - The unlock list shows up to 12 items per category.
- **Centre picture:** now uses the same preview box as the card pictures (frame, dark face, soft light, faded edges) and shows a lock while the item's level is locked.
- **Locked levels:** the whole card is darker and the rank title dims, so unlocked and locked levels are easy to tell apart. The pictures inside are unchanged.
- **EXP tag:** always shown; while the XP table is unknown it reads "— / —  EXP".
- **XP table search:** now covers every game assembly and more names (ExpTable / exp_table / ExperienceTable). It also logs the game's experience-related classes, so a still-missing table can be fixed by name.
- **Stutter finder:** any frame slower than 60 ms is logged with the step that ran just before it.

### 0.9.2
- **Fix: game crash when opening the screen.** The two risky pieces added in 0.8.0 are gone:
  - The XP search no longer walks through the game session's live objects. It now looks by class only and reads the table from the class the game keeps as a Singleton.
  - Card pictures at 2x are no longer force-redrawn; only the big centre picture is, as in 0.7.0.
- **New `Progression.log` next to the DLL:**
  - It's written to disk line by line, including step markers (open, page, card, level, icon, emblem).
  - After a crash, its last line shows exactly what was running. BepInEx's own log is buffered and loses those lines.

### 0.9.1
- **Emblems:** replaced with your updated set.
  - A new animated emblem for 46–50 (Blacklisted).
  - The former 46–50 and 51–55 emblems moved up to 51–55 and 56–60, so all 16 ranks have their own animated emblem.
- **Versioning:** smaller steps from here on (0.9.x patches); 1.0.0 is kept for the finished screen.

### 0.9.0
- **New rank titles:** Scavenger, Drifter, Trespasser, Stranger, Contractor, Operator, Outlaw, Renegade, Insurgent, Blacklisted, Wanted Man, Ringleader, War Chief, Ghost, Myth and Legend of Tarkov, one per 5 levels (76–79 for the last).
- **New emblems, one per rank:**
  - `emblems.txt` now says which levels each emblem is for (its last two columns).
  - A level with no emblem of its own uses the closest lower one.
- **Install:** the old emblem01–19 files aren't used anymore; delete the old `emblems` folder before copying the new one in.

### 0.8.0
- **Fix: stash icons blown up after using the screen.**
  - The game keeps one cached icon per item look, shared with the stash, so a bigger render here replaced the stash's picture too.
  - Every item drawn bigger is now redrawn at stash size when the screen closes.
  - The left list's tiles are now drawn at stash size, so they no longer touch the cache at all.
- **XP bar:** if the session's config isn't where expected, it searches the session's objects for the experience table and logs where it found it (or the classes that hold one).
- **Cards (Arena look):**
  - Each item preview has a thin frame, a dark face with a soft light, and a dark fade around the edges.
  - A lock icon sits in the bottom-right while the level is locked and disappears once it's unlocked.
  - Unlocked levels get a warm red hue in the card's top-right corner, and "Unlocked" is in Arena's salmon colour.

### 0.7.0
- **Animated rank emblems:**
  - The 19 animated emblems replace the diamond badges, in the header and on every card.
  - Level 79 gets the final emblem; levels 1–78 are shared out evenly, 4 or 5 levels per emblem.
  - They're sprite sheets in `LevelGateProgression/emblems`, listed in `emblems.txt`, and each is loaded the first time it's shown.
  - Without that folder, the old diamonds come back.
- **XP bar:** it reads the experience table from the game session. 0.6.0 looked for a class this game version doesn't have, so it showed "experience unknown".
- **Esc with inspect open:** now closes only the inspect window. The game closed its window on the same key press before we checked, so we now remember a window was open a moment ago.
- **Red bloom:** strong on every level, not just locked ones; locked levels push it a little further.
- **Cards:**
  - One big square picture and two small square ones, so icons are no longer stretched.
  - The pictures take one item from each of the level's top categories, in this order: Weapons, Backpacks, Rigs, Armor, Headwear, Medical, Food, Other Gear, … and Ammo last.
  - A 12 px spacing grid, with a bigger badge and title.
  - The picked card gets an Arena-style orange border and corner glow.
- **Level headers:** evenly spaced dashes (drawn as a mesh instead of a stretched sprite), bright by the label and fading out to an end tick. Your current level is orange.
- **Borders:** 3 px on panels and cards (were 1), 2 px on small boxes.
- **Page bar (Arena):**
  - A page lights up once every level on it is unlocked.
  - The page you're looking at stands 4 px taller whether it's lit or not, and its number is bold.
  - Thin dividers sit between the page numbers.
  - Q and E are proper keycaps now.
- **XP block:**
  - The level box has its bottom-right corner cut, like Arena.
  - XP reads bold "your XP / needed XP", with an orange EXP tag right after the numbers.

### 0.6.0
- **Ranks:** a new rank title every 5 levels, from Scavenger (1–5) to Legend of Tarkov (76–79), each with its own badge colour.
- **Bloom:** an Arena-style red bloom glows from the right edge and fades to the left. It gets stronger, and the panel borders turn reddish, while the picked level is locked.
- **XP block (Arena style):**
  - It sits above the centre panel: an orange square with your level, an XP bar, "have / need EXP" for the next level, and "Next level reward: N unlocks".
  - Click the reward line to jump to that level.
- **Removed:** the old top-right "your level" block.
- **Selecting items:**
  - Hovering no longer changes the big view.
  - Left-click an item to show it (with the game's click sound); right-click still inspects.
- **Right panel:** now also shows DETAILS (size, weight, damage, penetration, armour class, calibre, resource) and the item's description.
- **Cards:**
  - Tighter spacing: a small badge and a bold rank title on one row, bigger pictures filling the card, and the count and state right under them.
  - Headers have Arena-style dots fading from the middle out.
- **Centre panel:** the ITEM / CATEGORY / UNLOCKS AT labels and their values now line up in two columns.
- **Icons:**
  - The small card icons load faster (no forced re-render).
  - The big picture is rendered at a higher resolution.
- **Text:** bold for headings and important text.

### 0.5.0
- **New layout** after the Tarkov and Arena battle passes:
  - **header:** PROGRESSION plus the picked level on the left, your level (Arena-style square and bar) and the Tarkov logo on the right;
  - **left panel "UNLOCKS":** the reward list;
  - **centre:** the item big, with ITEM / CATEGORY / UNLOCKS AT in small grey;
  - **right panel:** the item's details, a "Reach level X  2 / X" requirement (red until met) and an INSPECT button;
  - **bottom:** cards under dotted "Level X" headers and a numbered page bar with Q / E.
- **Colours and text:** muted, battle-pass-like colours and sentence-case text.
- **Wording:** the level you're on says **Current level** (was UNLOCKED at the top and YOUR LEVEL on the card).
- **Esc** only closes the screen when no game window (inspect…) is open; otherwise it's left to the window.
- **Q** on the first page (and E on the last) no longer plays a slide that goes nowhere.
- **Card pictures:** they are placed by fractions of the card, so they stay spaced at any size.
- **Big picture:** it asks the game to draw it again at 3x (forcedGeneration) instead of reusing the stash-size picture. The log says the size it got.
- **Blur:** the motion blur is no longer switched on (it does nothing on a still camera).
- **Sounds:** the game's bottom-bar click on open, and the inspector sound on inspect.
- **Log:** the inspect window's objects are logged once, to find the game's 3D item preview for the big picture.

### 0.4.1
- **Camera turn:** the menu's 3D background turns smoothly to the side while the screen is open, like the game does for Character / Traders, and turns back on close. Screen > CameraTurnDegrees (default -75, to the left; 0 = off). It also works with MoxoPixel Menu Overhaul's camera.
- **Blur:** a blur effect on that camera, if the game has one, is switched on while open (Screen > BlurBackground).

### 0.4.0
- **Inside the game's UI:** the screen now sits right after the main menu screen, so the game's windows (inspect) open on top of it. The setting Screen > InsideGameUi switches back to the old on-top canvas.
- **Main menu hidden:** ESCAPE FROM TARKOV, CHARACTER, TRADING, EXIT and the beta warning fade out while the screen is open, like the battle pass (Screen > HideMainMenu).
- **Transparency:** Screen > Opacity (default 0.85) sets how solid the background is.
- **Sounds:** the game's own UI sounds on open / close, level and page changes (GUISounds.PlayUISound).
- **Sharper pictures:** the game renders item pictures at 2x for tiles and 3x for the featured picture (LoadItemIcon's scaleFactor), so they're no longer pixelated.
- **Smaller tiles:** they now look like stash cells. Screen > TileSize (default 92) sets their size, so more items fit.
- **Featured panel:** battle pass style, with a thin frame, the item name and grey labels (Unlocks at / Category / Status).
- **Card icons fixed:** the level's tiles were clearing them.
- **No pulsing badge.**

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
