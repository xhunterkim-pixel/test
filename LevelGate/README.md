# LevelGate — level-gated item usage for SPT 4.1.6

Blocks equipping/using specific items (weapons, ammo, armor, meds, food,
grenades) until your **PMC** hits a level you choose per item. Only ever
affects your own human-controlled PMC — bot PMCs, scavs, and AI never check
this and are completely unaffected.

## The hook (verified, not guessed)

Every hook below was confirmed by directly scanning your actual
`Assembly-CSharp.dll` — not just its class/method names, but which methods
actually *call* which, traced through the raw IL bytecode.

| Action | Real hook |
|---|---|
| Equip, throw, eat/drink, or use a med | `EFT.InventoryLogic.ItemController.CanExecute(AbstractOperation)` — the gate called at the top of `Execute()` for every operation type, before anything runs |
| Loading ammo into a magazine | `LoadMagazineProcess.TryProceedForItem(Item item)` (found by simple type name at runtime, since it's a nested class) |

### Ammo loading / reloading (updated)

The table above is out of date for ammo. Loading gated ammo is now refused in
three places (see the comments in `Plugin.cs` for the full reasoning):

- **Inventory operation** — `PlayerInventoryController.CanExecute` /
  `MoveResult.CanExecute`: any Move/Split/Transfer that puts a gated round
  into a chamber, box magazine, tube, internal magazine or cylinder (a
  slot/stack slot/grid whose parent is a `Weapon` or `Magazine`, ammo boxes
  excluded). Previously only moves into gear slots were checked, which is
  why the Mosin, MP-153 and M4A1 manual chamber still loaded gated rounds.
- **Reload entry** — `Player.FirearmController.ReloadWithAmmo` /
  `ReloadCylinderMagazine` / `ReloadGrenadeLauncher` / `ReloadBarrels` (and
  overrides): refuses before any reload animation starts. Each hooked method
  is logged at startup as `LevelGate: reload entry hooked: ...`.
- **Firing** — `ClientFirearmController.CanPressTrigger` and every
  `FirearmController.SetTriggerPressed(true)` (logged at startup as
  `LevelGate: trigger hooked: ...`) check the chambered round(s) **and every
  round in the current magazine / internal magazine / tube**. A gun that
  comes pre-loaded with gated rounds (e.g. an MP-153 or Mosin off a body)
  won't fire until those rounds are ejected/unloaded.

The bottom-right **"Ammo Level Too High (requires level X)"** message is shown
only for four deliberate actions, once per press (no cooldown):

1. packing a gated round into a magazine,
2. manually chambering / single-round loading (MP-153 tube, empty M4A1 with
   no magazine),
3. pressing R on internal-magazine guns (Mosin),
4. pressing M1 with a gated round chambered or anywhere in the magazine.

Everything else (dragging/hovering ammo, the game polling "can the trigger be
pressed", per-round insert helpers) still blocks but stays silent. Only your
own actions count — bots' reloads/shots never trigger it, and the old global
"caliber lock" (which also stopped bots from using that ammo) is retired.
SPT 4.1.6 has no `NotificationManagerClass`, so the game's notification
method is found by name at startup (logged as `LevelGate: on-screen messages
use ...`); if none is found, LevelGate draws its own bottom-right box.

**SEMI LOCKED magazines:** a magazine you may use but that holds at least one
gated round shows `[SEMI LOCKED]` (short name) / `[SEMI LOCKED - Lvl X] name`
with an **orange** background, next to `[LOCKED]`/red. Unlocked items keep
their own name (so the short name stays readable) with a **green** background.
Magazines only — ammo boxes are excluded.

**Labels & colors:** the LOCKED / SEMI LOCKED text, the `[ ]` brackets, the
" - Lvl X" part and each state's background color are in the BepInEx config
(sections "Labels" and "Colors", also editable in the Configuration Manager).
The F9 window keeps just the switches you'd actually flip in game: stripes
ON/OFF, game / LevelGate style, tooltip 2 lines, and the stripe strength
(shown only for LevelGate-style stripes, the only ones it changes).

**Hover tooltip:** the name line becomes two lines (level in yellow):

| Item | Tooltip |
| --- | --- |
| locked | `LOCKED - Bandages` / `Unlocks At Level 2` |
| semi locked magazine | `SEMI LOCKED - PMAG …` / `Rounds Unlock At Level 2` |
| unlocked | `Bandages` / `Unlocked At Level 1` |

It works everywhere an item can be hovered (stash, inventory, containers,
trader screens, in raid): the item is recognised from its name, and like Show
Me The Money from the grid cell under the pointer (`GridItemView`). Other
mods' tooltip lines (Show Me The Money's prices) stay below. F9: "Tooltip 2
lines" ON/OFF (BepInEx config `Labels / TooltipTwoLines`). Startup log:
`LevelGate: tooltip layout hooked: SimpleTooltip.Show + N SetText + GridItemView hover.`
The SetText hook matters with Show Me The Money: the first time an item is
hovered it replaces the tooltip text once the price is ready.

**Striped backgrounds:** LOCKED, UNLOCKED and SEMI LOCKED items get the
game's own striped background, the built-in layer the game shows on items
you pin / lock in the stash (`GridItemView._pinBackground`). LevelGate just
switches that layer on for them. Your items are NOT pinned or locked, and
sorting moves them as usual. No scanning: it's applied when the game
repaints an item cell (postfix on the cell's own `UpdateColor` & co), plus
once when you change a Stripes setting or level up. F9: Stripes ON/OFF and
"Game style" / "LevelGate style" (LevelGate's own drawn stripes, with LG
strength Subtle / Medium / Strong). BepInEx config section "Stripes". The
startup log names the hooked methods: `LevelGate: stripes hooked: ...`.
(The old lock icon is gone: it searched the screen every 0.1–0.5 s and
cost FPS.)

**Loose loot with an empty gear slot:** picking up a gated weapon, helmet,
headset, armor, face cover or eyewear no longer says "No space" — your own
empty gear slots answer "can't accept" for gated items, so the game puts the
item in your backpack/rig/pockets instead of trying to auto-equip it (logged
at startup as `LevelGate: gear slot check hooked: ...`).

**Works from launch:** out of raid (main menu, stash, traders) there is no
in-raid player, so the level now comes from the logged-in PMC profile.
Labels, colors and equip blocks apply right away instead of only after
visiting the hideout or a raid.

Unloading is never blocked: taking gated rounds out of a magazine/gun
(including the internal transfer an `UnloadMagOperation` performs) always
works. Only putting gated rounds *into* a gun or magazine they aren't
already in is refused.

`FirearmHandsInputTranslator.LoadAmmoToChamber` and the reload operations'
`OnAddAmmoInChamber` are diagnostic-only now: the first read a reused buffer
holding the *previous* call's rounds, and the second is an animation event
that fires after the round has already moved (skipping it left hands stuck).

For the Mosin / MP-153 path (`ReloadWithAmmo(AmmoPack, Callback)`), the
rounds are read from the ammo pack (searched several levels deep) and, if
that comes back empty, from the input translator's ammo buffer for the same
key press. If a reload still gets through, the log line
`ReloadEntry ... pack=[...] translator=[...]` plus an `AmmoPack layout:`
line show exactly what the game passed.

All of these skip bots' controllers, so bots are never judged by your level.

### Locked meds / food (0/60)

While locked, a med or food item's remaining amount is set to 0 so the game
itself refuses to use it. The original amount is saved per item in
`config/neutralized_resources.json` and put back when you reach the level
**or** the item is removed from the limiter — including after a game restart.
Items zeroed by an older build (which only remembered the amount in memory)
have no saved value, so they are refilled to full instead of staying at 0.

Two earlier approaches were tried and abandoned before landing on this one —
kept as history in the code comments, in case a future SPT update breaks
the current hook and this context helps re-find it:

1. **Skipping `MoveOperation.ExecuteInternal` directly.** This runs *after*
   the drop is accepted and also drives the hands/weapon-switch animation
   state, so skipping it left that state machine stuck — the "hands busy" /
   flashing-slot bug.
2. **Patching `Slot.CheckCompatibility`.** Tracing its actual callers in
   the IL showed it's only used by `Slot.CanReplace`, `Slot.CheckConditions`,
   and the "auto-equip a better item you just picked up" feature — none of
   which run on the normal drag/drop or double-click equip path, so it
   silently never fired for a real equip action.

The current hook was confirmed by finding its actual call sites in the
compiled bytecode: it's called from `ItemController.Execute()` and
`ClientPlayerInventoryController.Execute()`, and neither that class nor its
base classes (`PlayerOwnerInventoryController`, `BackEndInventoryController`)
override it — so patching the base method reliably intercepts the real
gameplay call. Returning `false` rejects the operation before
`Execute`/`RunOperation` does anything, so nothing — hands state included —
is ever touched.

## What's included

- `Plugin.cs` — the plugin: config loading, the `CanExecute` patch covering
  equip/throw/eat/heal, the ammo-loading patch, and an in-game editor
  window (default key **F9**).
- `LevelGate.csproj` — project file to build it.
- `config/level_requirements.json` — example config. Edit by hand, through
  the F9 menu once it's running, or with the LevelGate Editor.
- `LevelGate.Editor/` + `Build-Editor.bat` — the level limits editor (below).

## LevelGate Editor (the level limits editor)

`LevelGate.Editor\` is a Windows app for editing `level_requirements.json` without hunting ids:

- every game item (and modded items) by category tab — Weapons, Ammo,
  Medical, Backpacks, Rigs, Armor, Keys… — with its picture, short name,
  id and price; items that aren't limited are listed too
- type a level straight into the Level column (empty = no limit), or use
  the slider / quick buttons on the right; pick several (Ctrl / Shift-click,
  Ctrl+A) to set or remove them all at once, or +1 / −1 them
- filters (All / Limited / Not Limited), search, sort by name, level,
  category, price or "changed first"
- Mods page: **Scan My Mods Folder** finds server mods that add items
  (reads their item json files); switch each mod on/off
- Save shows every change first, then writes only those changes into the
  file as it is on disk right now (entries added in the game's F9 window
  meanwhile are kept); undo / redo; changes made in game show up live

While the game runs, LevelGate (1.6.0+) notices the saved file within ~2 s
and reloads it (F9 → Reload from disk does the same). Reopen the inventory
to refresh item names.

The prebuilt editor is in `Editor\` of the release zip. Put that folder
anywhere **outside BepInEx** (e.g. `C:\SPT\LevelGate Editor\`) — BepInEx
would try to load its dlls as plugins. It finds
`C:\SPT\BepInEx\plugins\LevelGate\config\level_requirements.json` by
itself (or use Browse…). To build it yourself: `Build-Editor.bat` (.NET 10
SDK).

## How to find an item's TplId (verified way)

Don't trust wiki/market sites blindly for IDs — variants (e.g. sawed-off vs.
full-length) can have different IDs, and I've seen a mismatch already in
this conversation. The reliable source is your own server's locale file:
```
C:\\SPT\\SPT_Runtime\\SPT_Data\\database\\locales\\global\\en.json
```
Search that file for the item's name — you'll find lines like:
```
"5580223e4bdc2d1c128b457f Name": "MP-43-1C 12ga double-barrel shotgun",
```
The ID is the part before ` Name` — copy that exactly into the F9 menu or
`level_requirements.json`.

## Server mod (optional)

`../LevelGate.Server` is a tiny SPT 4.1.x server mod. It changes nothing in
gameplay; it only makes the server list LevelGate at startup
(`Mod: LevelGate version: 1.1.0 (GUID: com.yourname.levelgate | targets SPT:
~4.1.0) ... loaded`) and log how many item limits the client config holds.
Open `LevelGate.Server/LevelGate.Server.csproj`, check `<SptServerDir>` (the
folder containing `user\mods`, e.g. `C:\SPT\SPT_Runtime`), and build; the
DLL is copied to `user\mods\LevelGate\`. Needs the .NET 10 SDK (SPT 4.1.x server packages target net10.0).

## Build steps

1. Visual Studio 2022 Community, ".NET desktop development" workload.
2. Open `LevelGate.csproj`, set `<SptDir>` to your SPT folder.
3. Build → Build Solution. The post-build step copies `LevelGate.dll` and
   `config/` into `SPT\\BepInEx\\plugins\\LevelGate\\` automatically.
4. Launch SPT. Press **F9** to open the editor.

## Confirming it's working

Check `BepInEx/LogOutput.log` for:
```
[Info   :   BepInEx] Loading [LevelGate 1.1.0]
[Info   : LevelGate] LevelGate loaded, N restricted item(s).
```
with no warning lines underneath. If ammo-loading specifically logs a
warning about not finding `LoadMagazineProcess`, that one patch is applied
via reflection at runtime rather than a compile-time attribute — see the
comment above `LevelGatePatches.PatchAmmoLoad` in `Plugin.cs` if it ever
needs adjusting after an SPT update.

**Important:** if you edit `level_requirements.json` directly (rather than
through the F9 menu) while the game is running, the plugin won't see the
change until you either restart or hit "Reload from disk" in the F9 window.
Also make sure you're editing the file under
`SPT\\BepInEx\\plugins\\LevelGate\\config\\` — that's the copy the running
game actually reads, not whatever's sitting in your Visual Studio project
folder (those only sync when you rebuild).

## Notes on behavior

- Only fires for `player.IsYourPlayer && !player.IsAI` — your own PMC.
  Your Scav has its own level/profile, so restrictions apply on Scav runs
  too if your Scav is under the threshold — let me know if you'd rather
  scope this to PMC runs only.
- Blocked actions fail silently with an on-screen console message
  ("LevelGate: requires level X to use this item") — nothing crashes.
- This does **not** hide restricted items from your stash, traders, or the
  flea, and doesn't stop you from carrying them in your backpack/pockets/
  secure container — it only blocks equipping/using/throwing/loading, per
  your original request.
- Since SPT updates sometimes rename or restructure internal classes/
  methods, if a future version breaks one of these hooks, the same approach
  used here (reading the actual DLL's metadata and tracing real call sites
  in its bytecode) can re-locate the new ones — just re-upload the new
  `Assembly-CSharp.dll` along with a fresh `LogOutput.log` showing what
  broke, and I can redo the analysis.
