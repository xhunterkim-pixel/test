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

### 0.9.63
- **The main menu stays hidden while the screen is open**: the game could fade it back in behind us after a screen change, and its CHARACTER / TRADING text and the orange PROGRESSION block showed through between the panels.
- **Selection reflection**: the picked tile and card get a very light sheen and a glossy line down the edge that faces the middle of the screen (left cards: right edge; right cards: left edge).
- **Borders catch the light by position**: the reward list's right border, the details panel's left border and the picture panel's top edge carry a faint glossy highlight.
- **Your level's card looks important** (CoD's bloom): an orange glowing border, faint orange scanlines inside and a row of tick marks under it.
- **Each panel its own texture**: fine vertical stripes on the reward list (and CoD's striped look on its category tabs), a blueprint grid behind the picture, a glassy light with fine hairlines on the details; all faint, following Wear And Scratches.

### 0.9.62
- **CoD's NEW tag everywhere**: bright yellow bold NEW in a dark see-through box with a thin yellow outline, a fainter echo outline offset down-right, and a soft yellow glow — pinned to the top-right corner of new reward tiles (overhanging the edge, the name moves down to make room), on newly reached level cards (top-right, instead of the orange NEW text) and on the main menu's PROGRESSION shortcut after a level up (was a green block).
- **Handled texture on the important surfaces** (level cards, the reward list's head, INSPECT): faint fingerprints and smudges with a slight purple / green mottling, like CoD's cards; follows Wear And Scratches.

### 0.9.61
- **Character > Overall emblem fixed**: 0.9.60 added it to the game's icon column, which centres its items, so the USEC logo was pushed up into the level number. Ours now stays out of the game's layout and floats just below the column's lowest icon (the prestige mark), following it if the column moves.
- **Text Size** (F12 > General, 100 / 115 / 130%): the small text (labels, tags, tile names, stat names) grows; titles and big numbers stay. Applies on the next open.
- **Reduce Motion** (F12 > General): patterns stand still; cards and pages change without sliding; no flicks, flashes, pulses, stamps scaling or rail beam; tiles appear at once.
- **Keyboard in the reward list**: Up / Down (or W / S) move to the previous / next reward (selected and scrolled into view), Enter (or R) opens the game's inspect.
- The dotted strips on the picked card / tile are softer (they read as a glitch bar).

### 0.9.60
- **Armor / helmet / rig stats for every item**: armor class and durability are now also read from built-in parts (a slot allowing exactly one item: soft armor inserts, helmet tops / ears…), not only default "Plate" parts — the 6B45, Titan helmets and others showed neither.
- **Character > Overall emblem**: bigger (96 px), placed below the game's icon column (faction logo, prestige) in that column's own spacing, instead of over the prestige icon.
- **Drag sounds**: one tick at most every 0.15 s while dragging, none while a flick coasts.
- The micro-text line under the reward list no longer overlaps the last row of tiles.

### 0.9.59
More texture, the CoD way (all drawn once, no per-frame cost):
- Registration marks: a small "+" just outside each corner of the three main panels.
- HUD micro-text: tiny faint system labels ("SYS_ONLINE // LVL 38 / 79 // 24 ITEMS" under the reward list, "ID - 7009 // LV 38" on the picture, a sideways "STATS —" on the details panel).
- Pixel dissolve: a dithered dot fade along the top of the picked card and the picked tile.
- Tiles get a light from above and a fine grit (follows Wear And Scratches).
- Divider lines start solid and fade out instead of running hard edge to edge.

### 0.9.58
- **Performance regression fixed**: 0.9.57's character-screen emblem searched every object in the game every 2 s until you first opened the Character screen (60–90 ms frames, most of that log's 92 slow frames). It now looks the screen up by its path, only while the Character screen is up.
- **Character > Overall emblem** sits under the USEC / BEAR logo (in the column's own spacing) with the rank name under it, instead of next to the ✕.
- **Unlock check mark**: hollow and white, drawn at high resolution, centred on the card's pictures (not the card).
- **HOME / BACK TO LEVEL** also shows when your own level's card has been dragged out of view (even while it's the selected level) and brings its card back.
- **F12 menu tidied**: readable names ("Background Pattern", "Wear And Scratches", "Rank Emblem On Character Screen"…), clearer sections ("2. Look & Graphics", "3. Preview (Test The Animations)"), and the settings almost nobody needs (margins, drawing order, button copy, debug, game sounds, redraw pictures) only with F12's "Advanced settings" ticked. Stored settings are kept.
- **Patterns**: Topo is now **Damascus 1**, Contours is **Damascus 2** (redrawn: bold organic lines that crowd and open up, like forged steel), plus **Damascus 3** (growth rings folding into each other) and **Damascus 4** (mirrored lines); all animated. A saved Topo / Contours carries over.
- **Rigs / backpacks show CONTAINER SIZE** again (from the game's own inspect row when the grids can't be counted: the JayPC's 15 was missing).
- **Penalties keep their decimals** ("-0.5%" was rounded to "-1%").

### 0.9.57
Performance (from the 0.9.56 log):
- Pictures drawn once are kept for the visit: dragging the row back and forth redrew the AS VAL's card picture six times in 3 s (70–84 ms each, 6 of 21 slow frames).
- The game's inspect window opens on the frame after the click (its own ~220 ms for a modded gun no longer swallows the click's sound and highlight); the big picture waits 0.12 s after a click.
- Background patterns hold still while you drag.
Drag:
- No hover sounds or highlights while dragging (it was a flood of sounds); one soft tick per level passing, at most every 80 ms.
- Flick it: let go while moving and the row keeps going, slows down and settles.
Fixes:
- The requirement box is one line: "Reach level 44 ........ 819 081 EXP to go" (LOCKED / levels away are said by the list's chip); NEXT cards no longer also say LOCKED.
- The timeline shows where you are when your level isn't on the row: "◀ 40" / "40 ▶" at its edge.
- Two-line tile names get a soft dark fade behind them.
- Helmets and plate carriers show DURABILITY (read from the game data's armor parts, next to the armor class).
- NEW on tiles: a small dark chip with an orange outline, bottom-left (it was a loud solid block in the cut bottom-right corner).
- Selected card: stronger dot fill that follows the cut corners; faint rules beside the number on every card; locked cards get a small lock top-left.
CoD-style level up (XP animation):
- A bright beam rides the timeline's fill to your new level.
- Each unlocked card gets a gold check stamped onto it, and its level number pulses.
- A big "+12 345 EXP" counts up under the picture while the bar fills, then fades.
Character screen:
- Your animated rank emblem and rank name next to the level number on Character > Overall (only animates while shown; F12 > General > CharacterEmblem turns it off; what it found is logged).

### 0.9.56
- **Drag the level cards with the mouse**: the row follows the pointer, a level comes in each time it passes half a card (so it can show 44–48, not only pages), and it settles into place when let go; a drag is never a click. The mouse wheel, A / D and the arrows slide the row by one card at its edges (a gentle slide, the new card fading in) instead of flipping a whole page; Q / E and the page bar still jump by pages (and line the row up again). The page bar marks the page the row's middle is on.
- **Names**: modded variants without a short name borrow their base item's ("SR-25 (Taupe)" instead of "Knight's Armament Company SR-25 7…"); fallback names lose their calibre and "special" ("AS VAL MOD.4", "Custom Guns NL545 (DI)"); modded guns the handbook doesn't list say their kind from the game data (ASSAULT RIFLE…) instead of "WEAPONS".
- **Animation**: only the picked card's and your card's emblems play (five at once was busy); tiles don't fade in one by one while you fly through levels; the XP bar's level-up flash is warm again (it vanished on the grey bar); the timeline rail eases to its new fill; background patterns fade in when ready.
- **Look**: the worn pale plates are readable (lighter wear, taller, bold 11 px: VIEWING, the page's range); bigger emblems in the card heads with even rules either side; the rail sits a little higher with your level's tick taller; the list head's chip is bigger; card footers have more room (long names a size down).
- **Performance**: while dragging / scrolling, one picture is drawn per frame and pre-loading waits until you stop (the game's picture drawing caused 13 of 17 slow frames); the big picture is asked for a moment after a click instead of inside it (a 136 ms frame); the reward list draws on its own layer (its fades and hovers no longer redraw the whole screen).

### 0.9.55
- Locked reward tiles show just a small lock bottom-left (the "LV 42" text is gone; the list head already says the level).

### 0.9.54
Tarkov's textures:
- **Neutral grey everywhere**: the panels, borders and text lose their blue tint (only real colours stay: orange = you, red = warning, blue / green = bonus); the panels are near-black and see-through, so the background shows faintly, like the game's.
- **Worn pale plate** (the game's selected sub-tab look, drawn in code): behind the VIEWING tag on the picked card and the current page's range on the page bar, with dark text.
- **Dotted leaders** from label to value in the stat rows and the requirement box; the XP numbers in the game's fraction style (yours big, "/ needed" smaller and dimmer).
- **Cut corners** (top-left / bottom-right) on the level cards and the reward tiles, like the prestige rewards.
- **Panel title strip**: the reward list's head sits on a lighter strip with big, thinner capitals.
- **Grey XP bar**: a dark track, thin 1 px outline, light grey fill (orange stays only for "you": the level square, your card's line, the rail's marker).
- **Film grain** over the whole screen, very faint (follows Scratches).
MW-style level progression:
- **Timeline rail** under the cards: a tick per level, a light fill up to where you are (your XP inside your level included), a small orange diamond at "you".
- **Card heads**: each card shows its level's rank emblem and the level number big, with a small role tag under it (VIEWING on the pale plate, CURRENT, NEXT).
- **Card footers**: the main reward's name bold, and its kind + the rest in small spaced caps under it ("ASSAULT RIFLE · +29 ITEMS").
- **Picked card**: a fine dot-matrix fill and a crisp light frame; **your level's card**: one thin orange line along its top.
- **HUD head on the reward list**: a chip ("✓ LEVEL ACHIEVED", "YOUR LEVEL", "🔒 LOCKED · 3 LEVELS AWAY"), a big "LEVEL 36" and the rank name, with a thin bracket on the left; the item count on the right.
- **Breadcrumb**: "CHARACTER / PROGRESSION /" over the page title, now "LEVEL UNLOCKS".
Fixes from the 0.9.53 log / screenshots:
- **Locked cards really are darker now**: the dimming from 0.9.52 was overwritten a few lines later with plain white.
- **Level cards keep NEW while you scroll past**: a card's NEW now goes when you click that card (or once all its new rewards are clicked); scrolling one level per notch was clearing every card on the way.
- **HOME hover**: the pale plate hugs the keycap and text (it ran 260 px wide), and the keycap turns over with it like INSPECT's RMB.
- **EXP badge**: the game's bigger EXP icon (icon_experience_big; the small one looked squashed).
- **Tile names get two lines** when needed ("ACHHC (Coyote Brown)" was cut to one); long ones a size smaller.
- **Medkits** don't repeat HP RESOURCE under the big RESOURCE number.
- **No background tint on reward tiles** (removed on request).
- Mouse wheel over the cards a touch calmer (one level per 0.2 s), fewer slow frames from drawing pictures while flying past levels.

### 0.9.53
- **Locked items show their real short names** ("AXMC", "AP SX" instead of "Accuracy International A…" / "4.6x30mm AP SX"): LevelGate blanks the short names of items still locked for you, so they're now read from the game data on disk (locales, read-only, background thread; LevelGate untouched).
- **Reward tiles like the game's cells / prestige rewards**: the short name top-right over the picture, the picture using the whole tile (long rifles were tiny), the item's own background tint from its template (violet keys, yellow ammo, green meds…), NEW bottom-right, and locked items get a lock + "LV 45" badge bottom-left like the traders' loyalty-locked items.
- **Penalties coloured like the inspect window**: red when it costs you (movement / turning / ergo penalties), blue when it helps.
- **Category path from the handbook** for everything but weapons ("MEDICATION › INJECTORS", "GEAR › BACKPACKS"); weapons still say WEAPONS.
- **Weight moved up by the category**, top-right with the game's weight icon, like the inspect window's header (one strip less below).
- **Buttons like the game's**: INSPECT and HOME turn pale with dark text while hovered.
- **Scrollbars like the game's**: a dark track with a light thumb (brighter while hovered).
- **Weapons list more of the inspect window**: accuracy, sighting range, horizontal recoil, muzzle velocity and fire modes, from the game's own rows.
- **EXP badge like the game's**: its own EXP sprite when found (the candidates are logged), else a pale-grey badge with dark bold EXP.

### 0.9.52
- **Backpacks (and rigs) lead with CONTAINER SIZE**: how many cells they hold, as a big stat with its meter (it was a small "Capacity … slots" strip).
- **Mouse wheel over the cards moves one level** at a time (it jumped a whole page); the page turns when you scroll past its edge.
- **F12 Preview shows NEW tags** too: the previewed levels' rewards and cards are tagged NEW like a real level-up (for the preview only, nothing saved).
- **Locked level cards are darker** than reached ones (their pictures dimmed, a little less when hovered / picked).
- **New unlock animation on the cards**: the orange flash is gone; each newly unlocked card now lifts to a soft white and eases back to its unlocked look (with a small 2% lift), one per unlock.
- A stim's long list of effects no longer squeezes the category and name together at the top of the right panel (effect rows a little shorter, the title keeps its height).

### 0.9.51
- **NEW tags stay until you deal with them** (they used to vanish as soon as the screen closed): every reward from levels you reached since your last visit is tagged NEW and keeps it (across visits and game restarts, per character) until you click it. Each of those level cards shows NEW in its bottom-right corner (your current level's too) until you pick that level yourself or click all its new rewards. Kept in a hidden setting (NewState).

### 0.9.50
- **Category tabs light up like the game's**: hovering a title (WEAPONS ›, AMMO ›) turns it pale grey with dark text, like EYEWEAR › in the inventory (it used to only get a shade lighter).
- **Small levels look like the gear slots**: with a handful of items, each category is a small slot side by side (its own tab over a box as wide as its items) instead of one grid with a coloured label on each tile.
- **Bigger item names in the tiles** (13.5 px, was 12; the name area is taller to fit two lines).

### 0.9.49
- **Grenades, meds and stims show their details** in the right panel, straight from the game's own inspect rows with their icons: explosion delay, contact delay, radius, fragments, damage per fragment; use time, every effect with its duration / strength (SKILL "ATTENTION" Dur. 240sec (+30)…) and the side effects (hands tremor, energy loss…). The first item of each category logs its rows (verbose) so names / values can be checked.
- **Semi-autos no longer say "Bolt action"** (Desert Eagle…): only the game's own bolt-action flag makes a weapon bolt action; a low template rate reads "Semi-auto".
- **No lone half strip**: a small stat without a partner (or with a long value) gets the whole line.
- **Look-alike items are told apart** in the reward list: "Bastion (OD Green)" / "Bastion (MultiCam)" instead of full names cut off right where they differ.
- **Armor**: durability sits next to the armor class as a big stat with its meter.
- **Random pattern**: the last 3 worked-out patterns are kept, so one that comes up again shows at once (Marble took ~0.65 s each time).

### 0.9.48
- The right panel no longer shows the item's SIZE (grid cells).

### 0.9.47
- **Three more background patterns, all animated**: **Marble** (broad mirrored marbling with faint scan lines, the bands slowly flow), **Pixels** (an LED wall of small squares with soft bands of light rolling across) and **Terrain** (a 3D look: ridge lines over rolling hills, nearer ridges hiding the ones behind, flying slowly forward). Neutral white like the others; PatternMotion sets their speed.
- **Pattern = Random**: a different pattern each time you open the screen (never the same one twice in a row).
- **Reward list like the game's containers**: each category is one full-width grey title tab (bold caps on the left, count and chevron on the right) with its items in a framed box attached under it; click the tab to fold / open it. The coloured side bars are gone.
- **Right panel**:
  - stat icons found for Damage, Penetration, Energy / Hydration and Ergo penalty too (the game's own ids: MaxAmmoDamage, AmmoPenetrationPower, FoodResource…); the icon space is kept even when a stat has none, so every label lines up;
  - big stats: 16 px icons and a thin meter under the number, like the inspect window's bars (fire rate / 1200, ergonomics / 100, recoil / 400, armor class / 6…);
  - small stat strips: taller, brighter labels, units no longer tiny ("3.2 kg" read as "32");
  - the requirement box says it once: "Reach level 44 · 4 levels away" and the EXP to go under it (no more 40 / 44 + LEVELS AWAY + Preview only);
  - it now agrees with the header during an F12 preview / the XP animation (it used your real level under a previewed CURRENT LEVEL);
  - long item names are a size smaller so they don't wrap into a lone word.

### 0.9.46
- **Stat icons** in the right panel: every stat shows the game's own icon from its inspect window (fire rate, ergonomics, recoil, caliber, effective distance, weight, armor class…), found by name at runtime; a stat the game has no icon for just shows without one. The log lists what was found ("stat icons: …", "stat icon 'Caliber': …").
- **Small stats laid out like the inspect window**: dark strips with icon + CAPS label on the left and the value on the right, two to a line in even columns (they used to be a 3/4-column grid that didn't line up with the big stats above).
- **Category titles work now**: WEAPONS › / AMMO › are grey tabs like the game's slot titles (EARPIECE ›, HEADWEAR ›). Click one to fold that category away (›) or open it again (chevron points down); it stays folded on other levels until the screen is reloaded.

### 0.9.45
- **Streaks, Contours and Topo now move**, very slowly: contour lines flow along the slope (rings grow and shrink on Topo), streaks shimmer and drift. **2. Graphics > PatternMotion** (0–3, default 1) sets how fast; 0 = still.
- **Many more lines**: Contours is now ~55 long wavy lines across the screen; Topo is a dense topographic map with every fifth line a little brighter (index lines).
- **Light on performance**: the pattern is worked out on a worker thread (the game's main thread only takes the finished picture, ~10×/s, 5×/s on Low). It only runs while the progression screen is open and stops completely otherwise; going into a raid frees its memory.

### 0.9.44
- **2. Graphics > Pattern** (F12): pick the faint background behind the screen: **Dots** (the old grid, default), **Streaks** (vertical grain lines), **Contours** (smooth wavy lines) or **Topo** (busier topographic lines). Drawn in code, no colour; its strength follows the Scratches slider. Each pattern is drawn once the first time you pick it (a fraction of a second) and kept.

### 0.9.43
- **[HOME] BACK TO LEVEL X** (bottom-left) shows whenever you're looking at a level that isn't yours, on any page, and is hidden on your own level. It used to be always there and dimmed on your page.

### 0.9.42
- **[HOME] BACK TO LEVEL 40**, bottom-left: the mirror of PERFORMANCE MODE bottom-right. Same row, same small caps, left-aligned with the card strip's edge, with a HOME keycap (the Home key does the same).
  - Always there, so both sides balance. It's readable while you browse another page and dimmed on your own page.

### 0.9.41
- **Level cards: one spacing grid.** Measured on 0.9.40, the cards had 13 px left padding, a 39 px gap between the big picture and the small squares, and 43 px empty on the right (the squares floated in a fixed column); 40 px above the pictures, ~20 below.
  - 12 px padding on all sides of the picture area, and 8 px between any two pictures.
  - The small squares are sized from the card's height and sit flush right; the big picture takes the rest.
  - 2 items: the square is centred beside the big picture. 1 item: the big picture spans the width.
  - The first card's rank emblem and title now sit on its big picture (top-left), so every card has the same grid.

### 0.9.40
- **Fix: after visiting the hideout, the PROGRESSION tab and shortcut stayed greyed out / hidden.** The hideout has its own game world, which can still be there back on the main menu, and the raid check only asked "is there a game world". The hideout's world no longer counts as a raid (logged once).
- **Food & drink:** Energy and Hydration shown as big stats.
- **Main-menu shortcut:** level and XP are back under PROGRESSION ("Level 40 · Renegade | 62 039 / 206 188 EXP").
- (0.9.38's armor class from the game data works: the log shows items.json read in 144 ms, 452 items with a class.)

### 0.9.39
- **Main-menu shortcut:** the line under PROGRESSION shows only your rank title ("Renegade"). It showed "Level 40 · Renegade | 62 039 / 206 188 EXP"; level and XP are on the screen itself.

### 0.9.38
- **Armor class from the game data on disk.** The 0.9.37 log shows the client's armor data has class 0 and no parts: LevelGate (server side) sets it before the game receives it. The plugin now reads SPT_Data/…/templates/items.json once, read-only, on a background thread: each item's own armorClass and its default plates ("Plate" ids in its slots). Armor, armored rigs and helmets show the original class. LevelGate, the server and the file are untouched. The log says where the file was found and how many items have a class.
- **Tarkov-style section titles** in the rewards list: a dark strip with small caps and a › chevron, like the game's slot titles (the category colour kept as a 2 px edge).
- **Diagonal hatching** (like the game's empty slots) behind the list's tiles and on a card with no new items.
- **Big number, small unit** for the small stats: weight "3.31 kg", eff. range "300 m", movement / turning "%".
- **Removed:** the "UNLOCKS AT LEVEL X" band over the preview (the requirement on the right says it).

### 0.9.37
- **Armor class for armor, armored rigs and helmets, second try:** the item is made filled (ItemFactory.CreateAndFillItem: its default plates inserted, as the game does), else from its preset, else bare; then the default plate ids in its slots' filters. The first armored item logs what each route found and the template's armor / class / slot members.
- **F12 > 2. Graphics, new sliders (live):**
  - **Scratches** (0–5, now 2): the scratches / smudges on panels, cards and pictures, and the dot grid. At the original strength they were barely visible.
  - **Vignette** (0–3, 1): how dark the corners are.
  - **RedGlow** (0–3, now 1.1): the red top-right glow, 10% stronger by default as asked.

### 0.9.36
- **Centre picture crisper:**
  - It's shown at an exact whole-pixel size (never enlarged, shrunk to fit when bigger) and snapped onto the screen's pixel grid.
  - A picture landing between pixels had every pixel blended with its neighbour, which looked like medium quality even at full resolution.
  - Kept copies also use a -0.5 mip bias, so shrinking them to fit the stage doesn't soften them.
  - The 0.9.35 log shows every item now comes back at the same size each visit. The game's own icon renderer tops out around 700×350 px (weapons ~690 px wide, pistols 301 px tall), which is as big as a game-drawn picture gets.
- **Armor class** for armor, armored rigs and helmets: the item's own class, else the best of its default plates / armor parts (read from its preset, once per item). LevelGate is untouched.
- **XP animation:** the "+N ITEMS" float over unlocking cards is removed.

### 0.9.35
- **Fix: sharp (High) pictures dropping to medium after clicking around.**
  - The centre picture's render size was measured on the picture itself. Since 0.9.32 it shrinks small items to their real size (no stretching), so after any of those the next item was asked for smaller. The log shows the same rifle at 4x, then 3x, then 2x, each kept and shown again later.
  - It's measured on the stage now, so the size is the same every time.
- **Right panel:** INSPECT sits at the bottom, level with the rewards list and the preview; the space left goes above it.
- **More stats for gear** (only when the item's template has them, not zero): Durability, Material, Capacity (grid slots), Movement / Turning / Ergo penalties. Armor class still only shows when it isn't 0 (LevelGate sets armor to 0; LevelGate is untouched).
- **Removed** the "‹ BACK TO LEVEL X" button (Home still takes you to your level).
- **PERFORMANCE MODE** box: right-aligned with the card strip's edge and centred on the page bar's row.

### 0.9.34
- **Header lined up:** the emblem and the three lines beside it are 8 px lower. The emblem's bottom now meets the level square's, and "Page X of 16" sits on the same line as "Next level …".
- **Removed:** the summary banner after the XP animation (it didn't fit the game) and the orange YOU mark over the page bar.
- **List tiles (compact list):** the picture starts under the category label; a helmet covered HEADWEAR.
- **Loading screen:** the game's own loader is found by its path (Preloader UI/Preloader UI/Loader, as 0.9.33 picked) instead of scanning every object, which cost ~230 ms on the first open. The scan is only a fallback now.

### 0.9.33
- **Fix (major): the game's main menu could stay invisible and unclickable** (HIDEOUT, TRADING, the PROGRESSION shortcut: nothing responded).
  - The screen hides the main menu while open and saved its state to restore on close. Opening an already-open screen (the log shows 8 shortcut clicks in a row) saved the hidden state as the one to restore, so every close afterwards restored a dead menu.
  - Separately, a close while the game had the menu switched off (changing screens) couldn't find it by name, so it was never restored.
  - Now:
    - opening an open screen does nothing;
    - the menu is kept by reference and always restored to fully shown and clickable;
    - while the screen is closed, a check once a second shows the main menu again if it's ever found hidden (logged as a warning).
- **Loading screen like the game's:** black, the game's own loading mark in the middle when it can be found (copied with its animation, its scripts stripped), and a quiet "LOADING n / total" bottom-left. The candidates are logged. If none is found, a hex mark stand-in turns slowly.
  - It now also loads the opening level's list pictures and the rank emblem sheets of the pages around, so nothing pops in after it.
- **Level cards:** a picture that hasn't arrived 3 s after its card was shown is asked for again at stash size (logged).
- **UI:**
  - The preview stage is landscape (1.3:1), with a spotlight, a floor line and a shadow under the item.
  - List tiles have 4:3 thumbnails. Names without a short name in the game's locale drop their type words ("… assault rifle").
  - The first card's rank emblem is smaller and sits above the same picture box as every other card, so all pictures line up.
  - Card headers use one 1 px rule each side instead of dotted runs.
  - The small stats use one row of 4 when there are 4 (no orphan EFF. RANGE row).
  - "‹ BACK TO LEVEL 40" (bottom-left) appears while you browse another page; Home goes to your level too.
  - The red corner glow is calmer. The EFT logo is removed. The PROGRESSION title moved down to line up with the emblem.

### 0.9.32
- **XP animation (from the animation reviews):**
  - **A click no longer skips everything:** it acts like a Space tap (next moment), is ignored for the first 0.8 s, and ignores the click that closes F12 (the 0.9.31 log shows two previews lost to it within a second).
  - **Controls prompt** where the page bar is while it plays: SPACE Next · HOLD SPACE Skip part · ESC Close.
  - **Focus:** the active part stays bright and everything else dims to 45% (0.3 s ease). A warm light sweeps from one part to the next between beats (level square → emblem → cards).
  - **Earn:**
    - "LEVEL 23 → 40" under the numbers, and the "+N" pool drains as the bar takes it.
    - Each fill is 4% quicker than the last (down to 0.45 s), and the level-up sound goes up a semitone every 3 levels (at most +4; timing follows the pitch).
    - The new number rolls in from below and lands on the pop. "LEVEL UP" rises over the caption row.
    - The full bar flashes, clears and starts over instead of snapping to empty.
    - The last level up pops bigger (1.4×) and flashes the whole XP block.
  - **Rank:** the glow pulses on Emblemup.mp3's two early hits (0.25 s, 1.05 s) before the swap on its peak.
  - **Unlock:** "+N ITEMS" floats up out of each card as it unlocks; runs over more than 2 pages go quicker (0.18 s a card, 0.35 s a page).
  - **Summary banner** over the cards (1.8 s): "17 LEVELS · 254 ITEMS UNLOCKED", with the new rank or "NOW LEVEL N".
  - **Land:**
    - The list and preview fade out, switch, and fade in (0.15 + 0.25 s).
    - The landing level's pictures load while it plays.
    - It lands on the nearest level with rewards if the new level has none.
  - **Space jumps catch up in 0.12 s (0.2 s for a part)** instead of cutting, with a tick and a bump on the level square.
  - **Sound timing:** each sound's start is measured at load (Unity's decoder can add silence in front) and the cues are moved up by it.
- **UI (from the audit):**
  - **XP block:** numbers on top, then a 12 px bar right under them, then the next-level link. The EXP tag is outlined instead of a solid orange block.
  - **Arrows** move one level, like the A / D keycaps under them (pages: Q / E, page bar).
  - **Page bar:** an orange YOU mark over your own page.
  - **Locked item:** a band across the bottom of the preview reads "UNLOCKS AT LEVEL 43 · 3 LEVELS AWAY", and the item is a shade darker. The small corner lock is gone.
  - **Requirement:** a lock / green tick icon instead of a checkbox (it looked clickable).
  - **Level cards:** "LOCKED" only on the next level's card; later ones are just dimmed.
  - **Readability:** the dimmest text colour went from #4f575a to #6a7376.
  - **Centre picture:** no longer stretched a little past its real pixels (shown at its own size when the game drew it up to 1.6× too small).

### 0.9.31
- **Space during the XP animation:**
  - Tap: on to the next moment (the next level up, arriving as its sound starts so the peak still lands on the pop; then the rank, the cards, the end).
  - Hold (0.35 s): on to the next part (levels → rank → cards → end).
  - Everything skipped past gets its end state quietly, and the playing sound is stopped first, so sounds never stack. A mouse click still skips it all.
- **F12 > 4. Preview** (nothing real changes): Levels (1–40), PlayLevelUp, PlayNextRank, PlayUnlock.
  - Plays on the Progression screen (opens it if needed) and starts once F12 is closed; the next preview continues from where the last left off.
  - Your XP, level, saved state and NEW tags are never touched; closing the screen brings the real state back.
- **General > UseGameSounds:** the game's UI sounds instead of the mp3s (played on the pop / the swap).
- **Sounds 30% louder** (from the originals ×3.25 with a limiter: level up peaks at -10.1 dB, new rank at -1.5 dB).
- **Timing and feel:**
  - 0.3 s more after the emblem settles before the cards (1.3 s).
  - XP bar: the part just earned glows lighter with a bright leading edge while it fills, then melts into the orange.
  - Emblem: a soft ring bursts out at the swap, and the new rank name rises 8 px into place as it fades in.
  - Cards: each unlocking card punches to 104% with its flash.
- **Level cards:** cards 2–5 (no rank emblem row) centre their pictures between header and footer instead of leaving an empty row above them. The first card is unchanged.

### 0.9.30
- **Every level up plays in full:** no more rushing through the first ones (5 → 23 jumped to 19 in 3 s without sound).
  - Each level: the bar fills in 0.7 s, Levelup.mp3 peaks on the pop, a short hold, then the next level (~1.25 s a level).
  - The unlock beat also shows every new level's card, page by page (it was capped at the last two pages).
- **Fix: a new character's XP never animated.** The saved XP and last seen level were one value for every character: a new character (0 XP) reset it to 0, and 0 counted as "never saved", so its first +10 000 EXP didn't play.
  - Both are now kept per character (profile id).
  - A character met for the first time starts where it is at game start / the 30 s check, so what it earns afterwards plays out (from 0 XP too).
  - The old single value is carried over to the current character only when it fits.
- **Sounds 2.5× louder:** the MP3s themselves are boosted with a limiter, so they don't clip (level up peaks at -12.4 dB, new rank at -3.8 dB).

### 0.9.29
- **Own sounds, and the animation is timed to them.** `sounds\levelup.mp3` (1.46 s, peaks at 0.45 s) and `sounds\emblemup.mp3` (4.28 s, build-up, peaks at 1.45 s) are loaded at start. General > SoundVolume sets their volume. If a file is missing, a game sound is used instead.
  - **Every** full level up plays Levelup.mp3, started so its peak lands on the number's pop. Consecutive level ups are ~1 s apart, so peaks don't collide. After the last one: the sound plays out, then 0.4 s, then the next beat.
  - More than 5 level ups: the first ones rush past (the number ticks up with a small bump, no sound, ~1.6 s at most) and the last 5 get the full treatment. 0.9.28 played 8 pops in ~3 s with the sound twice, which didn't match.
  - **Rank:** Emblemup.mp3 starts; during its build-up the emblem draws in slightly and a faint glow gathers. The shrink-swap-overshoot (unchanged) lands the swap on the sound's peak, and the glow swells to full there. The cards start 1 s after the emblem settles, while the sound's tail fades under them.
  - Skipping or closing stops the sound.
- **NEW tags:** an item's NEW goes once you click it (the item shown first too); a level card's NEW goes once you've looked at that level; all of them go when you leave the screen.

### 0.9.28
- **The XP animation is now four beats, each landing before the next, with ~1 s of stillness between:**
  1. **Earn:** the "+N" chip slides in; the bar fills; each level up pops the number and restarts the bar.
     - Many levels accelerate as they go, and the bar beat stays within ~5 s.
     - The level-up sound plays on the first and last level up only, at most once every 0.6 s. 8 levels at once chimed 8 times, which was too much.
  2. **Rank** (only if a new rank was reached): the emblem shrinks and swaps at its smallest (the swap hides in the motion), then overshoots and settles in a warm glow. The rank name and page line fade across.
  3. **Unlock:** the level cards page to the first new level. Left to right, each new card flashes orange, LOCKED turns into NEW, and the CURRENT marker walks along (at most the last two pages; earlier ones unlock off screen).
  4. **Land:** your new level is selected, so the rewards list shows what just unlocked.
- The screen opens on your old level while it plays. Its keys (A/D, Q/E, wheel) wait until it's done; click or Space skips to the end, and Esc closes (it finishes too).

### 0.9.27
- **XP animation (Graphics > XpAnimation, on by default).** The screen remembers the total XP it last showed. When you have more (a raid, a quest…), the next open plays it out:
  - the bar fills from where you last saw it, with the XP numbers counting and a "+N" in orange after the EXP tag;
  - level up: the level number pops with a white flash, the caption reads LEVEL UP, the bar flashes and starts over, and the unlocked count grows;
  - new rank (next page): the header emblem swaps with a shrink-overshoot and a warm glow, and the rank name changes;
  - then more fills until your current XP. Many levels at once speed up to ~7 s in total.
  - Click or Space skips; closing finishes it. The first open after installing sets the starting point (no animation).
- The profile XP check runs every 30 s outside a raid (was every 2 s) and notes XP not shown yet.
- **UI fixes from the 0.9.26 screenshot:**
  - The description's first letters were clipped at the left edge (g / p / r); the mask now leaves room.
  - Bolt-action rifles showed "30 rpm", which read like a bug; they now show "Bolt action".
  - Calibers with a one-letter suffix match the game's names ("7.62x54R", not "7.62x54 R").
  - Levels with 3 or fewer items: three bigger tiles across instead of four small ones.
  - High: list tiles are drawn up to 2x for their size on screen (Medium keeps stash size).

### 0.9.26
- **Fix: weapon size and weight were the receiver's, not the whole gun's** (DS Arms SA58 showed 2 × 1 and 1.46 kg). Size and weight now come from the assembled weapon (the preset the game shows in the stash).
  - The same size also sets how big the game is asked to draw a weapon, so the renders are asked for at the right scale.
- **Level cards: the big picture is landscape** (fills its column) instead of a small square. Weapons are wide, and in the square they came out tiny.
- **Card pictures follow the game's screen resolution:** drawn for ~200 px at 1920x1080, scaled up at 1440p / 4K (the centre picture already did this). The resolution and UI scale are logged on open.
- **Level cards: pictures on future levels are less dimmed** (85% instead of 65%; other cards 90%). Dark weapons on levels ahead were hard to make out.

### 0.9.25
- 0.9.24 play-test: the loading screen took 2–8 s (20 pictures). Every sharp picture was drawn and kept at the size asked for, including when flipping levels fast.
- High: weapons on the level cards may be drawn bigger too (same stash repair on close as the centre picture).
- Play-test log: each centre picture logs its real pixels vs its size on screen ("1.00x (sharp)" or "1.40x STRETCHED (soft)"), to find where the game draws an item smaller than asked for.

### 0.9.24
- **Fix: High quality only held for the first level you looked at.** Flipping levels while a sharp picture was still being drawn dropped it: it was never kept, and coming back showed the stash-size stand-in (looked like Medium).
  - Pictures still being drawn for a level or page you left are now collected in the background and kept.
  - The pre-loader also draws each nearby level's centre picture at its real size, not only the card pictures.
- **Loading screen:** the first open of a menu visit shows "LOADING PROGRESSION" with a bar while the pictures of your page and the pages on either side are drawn (up to 12 s; the rest keeps loading after). Then everything shows up sharp. Esc still closes it.
  - Picking a raid side / map / deploying lets the kept pictures go (memory); the next open loads again.
  - Changing Graphics > Quality or RefreshIcons reloads the same way (right away if the screen is open).
- Kept picture cache raised from 90 to 150.

### 0.9.23
- **Fix: changing Graphics > Quality now redraws the pictures.** The screen's kept pictures were only keyed by size, and FixStashIcons only redrew stash icons, so going back up to High could keep showing old pictures with no sign that anything happened.
  - Changing Quality clears the screen's kept pictures, asks for fresh ones, and redraws the stash icons of items that were drawn bigger.
  - FixStashIcons is now **RefreshIcons**: it clears the screen's pictures too and redraws every level-list item's stash icon.
  - A message at the top of the screen shows "Refreshing item icons… n / total", "paused — continues on the main menu", and "Item icons refreshed ✓".
- **Header:** the first line is "{Rank} · next: {Rank} (level N)", and the second is "Page X of 16 · owned / total items unlocked" (it said "Rank X of 16").

### 0.9.22
- **F12 menu cleaned up into three sections:** 1. General, 2. Graphics, 3. Advanced.
  - General: open key, menu bar button, main-menu shortcut, hide main menu, blur.
  - Graphics > Quality: Low (was PerformanceMode), Medium (default), High (was SharpWeaponPreview). Plus FixStashIcons.
  - Advanced: label, copy button, margins, tile size, tiles per category, opacity, camera turn, sort order, inside game UI, free items at level 1, verbose log, dump key.
  - Old settings (including the last seen level for the NEW tag) are carried over once from the old sections.
  - The screen's PERFORMANCE MODE box sets Low; unticking it goes back to the previous quality.
- **Logging cost is measured:** every session line is followed by "logging: N lines took X ms (Y% of the time)", plus the total since start.

### 0.9.21
- **Fix (likely cause of a crash): background icon redraws ran during fast screen switching.** A 903-icon FixStashIcons pass was still forcing redraws while Inventory → Flea → Trader were switched quickly (each draws its own icons), and the game crashed natively.
  - Background redraws (FixStashIcons, the SharpWeaponPreview weapon repair, the second restore pass) now run only on the main menu itself: Progression closed, no screen change in the last 2 s, not deploying or in a raid. They pause otherwise and continue later.
  - They run at 2 icons per frame, and pausing, continuing and progress are logged.
  - Closing Progression because another screen opened no longer redraws icons in the middle of that switch.

### 0.9.20
- **Fix: Progression could be opened while deploying to a raid.** Clicking the tab on the deploying screen ("TimeHasCome") made the plugin go back to the main menu, which could break the raid load.
  - The PROGRESSION tab is now greyed out and not clickable (and P does nothing) everywhere except the main menu, inventory, traders, flea market, handbook, messenger, hideout and settings. That covers raid side / location selection, matchmaking, deploying, countdown, the raid and post-raid screens.
  - In a raid the game world is detected, so the in-raid Esc menu counts too.
  - An open screen closes when this kicks in, and every change is logged.
- **The main-menu shortcut** is hidden in the in-raid Esc menu and while deploying.
- **No background icon work during deployment or a raid:** the stash-size restore and redraw passes wait until you're back in the menus.
- **SharpWeaponPreview's weapon redraw** (~160 icons) now runs at most every 10 minutes (it ran on every close). The option says it can cost performance.

### 0.9.19
- **New option `Screen > SharpWeaponPreview`** (F12, off by default):
  - Weapons get the extra-sharp render in the centre picture again.
  - To undo the game carrying those big pictures over to stash weapons, every weapon on the level list is redrawn at stash size when the screen closes (the same repair as FixStashIcons, weapons only). This is logged.
  - Changing the option redraws the open screen.

### 0.9.18
- **Fix: a stash weapon came out huge again.** A VPO-215 was only ever drawn at stash size by the plugin, but other weapons (VPO-136, VPO-209…) had been drawn big. The game's weapon icons can carry a big render over to other weapons, and that can't be put back per item.
  - Weapons now never get a bigger render: the centre picture and the cards use the stash-size icon, which is already 4–5 cells (~256–320 px) wide.
  - Small items (ammo, meds, food, gear) keep their sharp big renders.
  - Every big render the plugin asks for is logged with the item's category.
- **An icon that's already stuck big:** tick `Screen > FixStashIcons` once, or clear the game's icon cache.

### 0.9.17 — play-test build
- **Centre render capped at 6x** (was 8x). 8x was the heaviest request: one item took 426 ms and still came back small. 6x is still sharp at 1080p.
- **Card-picture tooltips** show below the picture, so they don't cover the first card's rank label.
- **Play-test logging** (in `Progression.log`; the previous game session's is kept as `Progression.prev.log`):
  - At start: every setting.
  - Profile changes, checked every 2 s: XP gained, and LEVEL UP from → to with how many items it unlocked.
  - The main-menu NEW tag turning on or off, and why; the last-seen level updating when the screen opens.
  - On open: level, XP into the level, total XP, the last seen level, and which levels / how many items are tagged NEW.
  - On close: a session summary with time open, levels viewed, page changes, items selected, inspects, slow frames (worst) and performance mode.
  - Every inspect, with the item's name.

### 0.9.16
- **Empty levels** (nothing assigned in the level list): the card shows a quiet "NO NEW ITEMS" instead of empty boxes. There's no LOCKED state (nothing to unlock) and no extra fading.
- **Single-item levels:** the picture is centred in the card instead of sitting in the left column next to an empty one.

### 0.9.15
- **Fix: a 3-digit fire rate ran into ERGONOMICS** ("600 rpm30"). The stats now use three equal columns. Small stats wrap onto a second line (effective range goes under weight) instead of squeezing the big stats into four columns. "rpm" is drawn smaller than the number.

### 0.9.14
- **A / D move level by level** (like ← →), not while typing. They're shown as keycaps under the ‹ › arrows.
- **Weapon stats:** fire rate, ergonomics and recoil are the big numbers for weapons; effective range sits with weight / size / caliber. All read from the weapon's template.
- **Requirement:**
  - Unlocked items get one quiet line, "✓ Unlocked at level 1".
  - Locked items keep the full box, now with a neutral border and a red edge on the left only, plus "… EXP to go · Preview only".
- **Fix: the reward list opened scrolled down**, with the first section's header out of view. The old tiles left the layout only at the end of the frame, so the new list was laid out under them for one frame; now they leave straight away, and the list is put back to the top.
- **Small levels (8 items or fewer):** one grid, with the category as a small label on each tile, instead of a one-tile section per category.
- **Tooltip** stays inside the screen.
- **SPT version label:** hidden while the screen is open, back on close.
- **Cards:** the page's rank emblem is 32 px (was 24). Locked card pictures show at 65% (was 50%), readable.

### 0.9.13
- **Red glow:** fades out on both axes (no hard edge) and is gone before the card row.
- **Screen height:** reaches down to the game's menu bar. The bottom margin is now 26; an old saved 68 is migrated.
- **Clicking a level:**
  - Opens on the item its card shows big (weapons first), not the first item in data order.
  - Clicking the level you're already viewing no longer rebuilds all its tiles (about 55 ms and a burst of slow frames each click).
  - A click on another level's card picture just opens that level (no two-step pick).
- **One emblem per page:** only the page's first card shows the rank emblem and name, in a slimmer header row.
- **Wording:** "items" everywhere ("147 items", "Next level 3: 13 items ›").
- **Header:** "167 / 903 items unlocked" moved off the XP bar and onto the rank line.
- **Card pictures:** hover edge plus a full-name tooltip, like the tiles. The tooltip is on its own top layer.
- **Stats:** both rows share one grid of equal columns (empty cells pad the shorter row). Calibers read ".366 TKM", "5.56x45 NATO", "12.7x55"… (the game's own name when it has one).
- **Right panel:**
  - INSPECT follows the content (the description takes only the height it needs, then scrolls), so there's no dead zone.
  - Requirement box: only the tick is green, with 16 px padding.
- **Preview:** no frame of its own inside the panel's frame.
- **Small items:**
  - No orange corner glow on the current card.
  - The performance checkbox sits on the right panel's guide and has a hover hint.
  - The scroll thumb is 2 px to see, 12 px to grab.

### 0.9.12 — audit fixes
- **Page bar:**
  - Pages are labelled by their level range ("1–5", "6–10"…), not page numbers that read like levels. On narrow screens only the current range shows.
  - Segments brighten on hover.
- **Each state said once:**
  - The list header shows only the item count (plus CURRENT / NEXT). "X levels away" and the lock are gone from it.
  - Cards for past levels show no state (NEW and LOCKED remain).
  - The requirement box is the one place for UNLOCKED / LOCKED / levels away.
- **Requirement box:**
  - Moved up under the item's stats, into the content flow, which removes the right panel's dead zone.
  - A real tick instead of a filled square.
  - Locked items add "7 453 EXP to go · preview only".
- **INSPECT:** full width, with its RMB keycap inside the button (no separate "Inspect" text).
- **Readability:**
  - Tile names are light grey (locked: mid grey, shown by the dimmed icon instead).
  - Name padding is 8 px all round.
  - Future cards keep readable text; their pictures are dimmed instead of the whole card.
- **Stat columns:** equal widths, so SIZE lines up under PENETRATION.
- **Scroll cues:** a thin thumb on the reward list and the description when they scroll, and a fade at the list's bottom edge while more is below.
- **Header:**
  - "CURRENT LEVEL" sits directly over the level square it names, with "212 / 903 ITEMS UNLOCKED" opposite it.
  - XP reads "564 / 3 017".
  - The next-level count isn't orange (orange is for you).
  - The title is on the 12 px top guide.
- **Less competition:**
  - The red glow stays in the top half.
  - The rank title shows only on the card where a rank starts.
  - Card pictures are full strength only on the viewing / current card.
- **Details:**
  - Title and state share a baseline.
  - The performance checkbox sits on the page-number line.
  - Ammo now shows its caliber (the game's field name differs from weapons).

### 0.9.11
- **Reward list header:** back to the cleaner 0.9.9 look, "LEVEL 21 REWARDS" with "4 ITEMS · 19 LEVELS AWAY" and a small lock on the right. The state words stay the 0.9.10 ones (CURRENT / UNLOCKED / NEXT / X LEVELS AWAY).
- **Pictures, fixes to the 0.9.10 sharpness work (found in the log):**
  - The session's first picture went through the loader probe at stash size, so the centre showed "64x64 (asked for 8x)". Once the loader is known, it now goes through the normal scaled path.
  - 2x renders weren't forced, so the game just returned its cached stash-size icon, and the size check then held card pictures back for up to 6 s. 2x renders are forced now.
  - While a bigger render is being drawn, the stash-size picture shows straight away and is swapped for the sharp one when it arrives.
  - Card pictures (and their pre-loading) use one scale per item: stash size when that's already about 120 px (rifles, backpacks), 2–3x for small items.

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
