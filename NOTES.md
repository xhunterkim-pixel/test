# Handoff notes

State as of 2026-09-29, branch `claude/m4a1-reload-levelgate-bugs-asmzj6` (everything is committed and pushed there;
no PR open). Rules and the build / tuning method are in `CLAUDE.md`.

## Current versions

| Thing | Version | Status |
|---|---|---|
| LevelGate plugin / server | 1.6.3 | Frozen: do not touch without the user's go-ahead |
| LevelGate Progression | **1.0.1** | Sent (full package), untested in game. 0.9.95 tested: dials tuned from the log, inspect warm-up works (24 ms, background) |
| Modern Editor (editor exe) | 2.0.8 | Sent, untested on Windows (UI checked in the browser mock) |
| Modern Editor (server mod) | 2.0.7 | Sent: compact SPT console output |

## Done recently

### Progression (0.9.9 → 0.9.94): polish pass, no redesign
- 0.9.9: polish dials (decor noise, micro labels, ambient motion, flourish, border fade at the ends like MW4, card opacity,
  small-text floor, neutral pips, quiet-during-effects, tooltip 1.0 s delay, ambient light with a green / pink / purple mix,
  A/D-direction light sweep also when clicking an earlier card).
- 0.9.91: the line under the hero name was switched off (the user wanted a glow on the name, not a description).
- 0.9.92: the name glow became a **directional reflection** (TMP underlay on the label's own material, offset down and to the right,
  like MW4's "HAN 86"). New **MW4 NEW tag**: a gold box that fades out from right to left, lit top and left edges,
  a second dark box behind it offset down-right (`Ui.NewBadgeMw4`). The old tag is kept as `NewBadgeOld`.
- 0.9.93: panels and cards are back to 0.9.81 brightness (the user preferred it). The panel light layers (inner glow, lit edge,
  glass, sheen, gloss) got their own `Polish.PanelLight`, separate from Decor Noise.
- 0.9.94: **fixed the missing character-screen emblem at levels 1–9.** The game shows "01", and the code matched the text "1"
  exactly (`OverallEmblem.Build` now compares the number). The user's 0.9.93 values became the defaults, and `testingNow` is empty.

**Tuned defaults (the user's in-game picks):** Ambient Motion 50, Card Locked 72 (later replaced by the new CardLockedOpacity 100),
Card Opacity 100, Panel Light 100, Name Reflection 29 / Softness 26 / Distance 47, NEW tag MW4, Tooltip delay 1.0 s,
Decor Noise 55, Micro Labels 60, Flourish 65, Border Fade 70, Ambient Light 140 (Mixed).

### Modern Editor (2.0.0 → 2.0.7)
- It merges the Custom Trader Creator, the Level & Item Editor and ItemStatEditor into one editor plus one server mod.
- Sidebar groups: Traders / Level Gate (Level Limits + Progression) / Items (Item Stats) / More, with on/off switches per section
  (`config.json` sections are read by the server too).
- Quest writer (`ui/writer.js`): Tarkov-voice descriptions and completion messages based on the trader's type, the conditions and the rewards.
- Quest checks match what Tarkov's code allows (kill while wearing: gear, weapons and melee accepted; "Full set" writes
  `equipmentInclusive [[a,b]]`). Killing a boss on a map where it doesn't spawn is a note, not a warning.
- Logs go to `Logs\ModernEditor_<date>.log` next to the exe (30 kept). Clean Up sends old mods, editors and settings to the Recycle Bin.
- Pictures: any image type is converted automatically. A quest without a picture uses its first reward or barter item's icon.
- The server log is one line at startup, and the details go to `user\mods\ModernEditor\logs\server_*.log`.

## 0.9.95 (this session: branch `claude/busy-clarke-5vn996`)
- Fixes: 848 ms freeze on the first right-click (inspect ctor lookup now on a worker thread at screen open, `GameItems.WarmInspect`);
  rank-up Dogtag window faded in while the stage was still in the middle (now waits .35 s).
- In F12 › CURRENTLY TESTING (`testingNow`): NewTagStyle (Mw4Dark default; new key), LockedBlueprint, CurrentXpFill, CurrentXpColour
  (user's colour answer was unclear → Orange default), SweepXpBacking, SweepXpHeight, BigXpDuringSweep.
- User said NO to MW4's XP ring around the level badge (the bar + emblem cover it).
- Asked the user for MW4 crops still missing: sweep leading edge, a card unlocking (flash / LEVEL_ACTIVE / check), the LEVEL UP slab,
  locked blueprint at 1:1, MW4's rank-up screen, the full-screen dot grid.
- Cloud build: `tools/cloud-build/setup.sh` (see CLAUDE.md).

## 0.9.96
- Colour Theme (F12 › Graphics, permanent option): `ProgScreen.AccentHex` replaces the old `Orange` const; MenuWidget / Toast use it too.
  Unmet red (`Red` #d0453a) and NEW gold are NOT themed on purpose.
- Current card XP fill: its edge is placed at the rail fill's end in world space (so it matches the rail diamond / waveform spike);
  no hard edge line (user: MW4 has none), a soft 60 px fade instead.
- Rule from the user: a dial they set to 0 / off = they don't like the change.
- Known: 545 ms hitch when the game's own Dogtag inspect window opens during the rank-up (game's 3D preview, not our code).

## 0.9.97
- Testing now (all default on): FadeCardLine, FadeSectionHead, FadeStatMeters, FadeXpBar (`Ui.RampFade(fadeRight)`: 30%→100% ramp;
  never flip a parent with children using a negative scale, it mirrors their text / fills).
- Still wanted from the user: MW4 close-ups of the same elements (they sent ours only).

## 0.9.98
- User rejected: the Colour Theme (removed completely) and 0.9.96's rail-aligned XP fill (back to 0.9.95's fill across the card,
  edge line alpha .7 → .18). Don't bring either back.
- F12 trimmed: `hiddenSetup` in Plugin.cs hides set-up / debug keys (values still apply). VerboseLog stays on, hidden.
- The 0.9.97 Fade switches are still being tested (the user's F12 screenshot was still 0.9.96).

## 0.9.99
- F12 › CURRENTLY TESTING finished: the four fades are on (the user's log), `testingNow` is empty.
- New normal option `CardXpProgress` (Look & Graphics, "XP Progress On Level Card", on).
- Exit freeze reported: the 0.9.98 log shows no errors and ends at "game screen changed to None" (quit started). Nothing in our code
  blocks at exit (background jobs are short one-offs). 0.9.99 adds `Application.quitting` → `quit: the game is closing` /
  `quit: done (N ms)`, stops Update / LateUpdate, saves the config, closes Progression.log. Next log tells whether it's ours.

## 1.0.0
- Removed for good (user: "not good", no option): the current card's XP line / fill (0.9.95–0.9.99) and the locked blueprint (0.9.95, never used).
  Don't bring them back.
- `GameItems.Facts` / `GameAttributes` cached per tpl (copies returned). `Feature` logs `pick <tpl>: N ms (item data, stat rows, rest)`
  when ≥ 25 ms: use that to decide whether pooling the stat rows (Destroy + rebuild every pick) is still worth doing.
- Exit: 0.9.99 log shows `quit: done (1 ms)`: the exit freeze is not ours.

## Modern Editor 2.0.8
- Obtainable / UNOBTAINABLE on Progression tiles: vanilla from database/traders/*/assort.json (`ItemDatabase.GameOffers`,
  sent as `ob` / `ot` per item), modded from `usedIndex()` (switched-on traders: offers + quest rewards). Setting
  `S.ui.progObtain` (vanilla / modded / both). NB items.js has its own `S` (not app.js's): pass settings in explicitly.
- Ctrl+C over any entry copies its ID (tools.js `hoveredId`). Trader box (#me) only on trader pages (`navOn`).
- Long `.hint`s (> 70 chars) are auto-tucked behind an ⓘ (tools.js `tuckHints`, MutationObserver) on every page.
- Cloud build of the editor works: `dotnet publish … -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -p:EnableWindowsTargeting=true`
  (without SelfContained=false it bundles the runtime: 118 MB).

## Progression 1.0.1
- Icon restore on close only redraws icons still big (`GameItems.StillBig`, items dropped from `_scaled` once fine); the blanket
  ~160-weapon redraw is removed. Cause of stash / trader icon reloading and probably the one endless trader load.
- NEW cleared for a whole closed group (`_closedGroups`, `_groupMembers`). Hero picture capped by stash cells (`FeatCap`).
- Containers group (with MobContainer / secure containers) second in `ProgData.Groups` and `CardOrder`.
- Not found yet: one reward tile (Gamma "Loui Peeton", the picked one) showing its picture smaller — ask for a close-up / log.

## In progress / open

Nothing is half-done in code. Waiting on the user's in-game test of 0.9.94: the emblem should now appear on
Character › Overall at level 1, and the log should say `character emblem: placed under ...`.

## Next steps (proposed to the user, none picked yet)

1. **The item-pick hitch in Progression** (60–110 ms "slow frame … after: Feature" in every log). The cause is the stats panel rows
   being rebuilt on each pick. The fix is to pool and reuse the rows. The user hasn't approved it yet, so it hasn't been done.
2. The hero name ("VKBO") is small next to MW4's. A bigger name would also show the reflection better. Try it as an F12 dial first.
3. The right details panel has about a third of its height empty below the description.
4. The name position differs: reward tiles put the name top-right, level cards put it bottom-left.
5. The earlier list of MW4 gaps (from the user's MW4 reference screenshots). The user may pick some of these.

## Decisions and why

- **Full package every Progression release:** a DLL-only zip wiped the user's custom emblems and sounds (0.9.9 incident).
- **New config keys instead of changing defaults** when an old saved value would otherwise stick
  (for example NameGlow → NameReflection, CardRest/CardLocked → CardOpacity/CardLockedOpacity).
- `Polish.Subtitle` is hard-coded Off, so a saved 0.9.9 value can't bring the line back.
- The frame border fade keeps the frame at alpha .004 with `cullTransparentMesh` off, so hover still hits it.
- The Progression Level Gate data is read-only (`BepInEx\plugins\LevelGate\config\level_requirements.json`). Progression never writes LevelGate files.

## Not in the repo (lost with the old session)

- The user's MW4 reference screenshots and MW_Transitions video (ask again if needed).
- The cloud build toolchain (Roslyn and reference DLLs in the old scratchpad). On desktop, build normally with `dotnet build` against `C:\SPT`.
