# CLAUDE.md

SPT (Single Player Tarkov 4.1.6) mods and tools. Read `NOTES.md` for where things stand before starting work.

## What's in the repo

| Folder | What | Built with |
|---|---|---|
| `LevelGate/` | BepInEx client plugin: locks items by player level (v1.6.3) | net472, `SptDir` |
| `LevelGate.Server/` | SPT server mod that goes with LevelGate | net10.0 |
| `LevelGate.Progression/` | BepInEx client plugin: the CoD / MW4-style **Progression** screen in the main menu (v0.9.94) | net472, `SptDir` (default `C:\SPT`) |
| `ModernEditor/ModernEditor.Editor/` | Windows editor app (WinForms + WebView2): traders, quests, Level Limits, Progression, Item Stats. UI in `ui/` (html/js/css), embedded as resources | net10.0-windows, `Build-Editor.bat` |
| `ModernEditor/ModernEditor.Server/` | SPT server mod `ModernEditor.dll` (custom traders, item stats, quest levels following Level Gate) | net10.0, `Build-Server.bat`, `SptServerDir` |
| `ModernEditor/Shared/` | `TraderModels.cs`, shared by editor and server | — |
| `tools/ui-mock/` | Browser mock of the editor host, to run and test the editor UI without Windows | node |

The user's install: SPT at `C:\SPT`, server at `C:\SPT\SPT_Runtime`, Progression plugin at
`C:\SPT\BepInEx\plugins\LevelGateProgression\` (with `emblems\` and `sounds\` folders next to the DLL).

## Hard rules (from the user)

- **Do NOT touch `LevelGate/` (plugin) or `LevelGate.Server/`** unless the user explicitly says so. Progression and Modern Editor are fair game.
- **Progression releases are ALWAYS the full package**: DLL + `emblems/` + `sounds/` (the user's own custom sounds / emblems). A DLL-only zip once wiped their emblems and sounds when they replaced the folder. Zip layout: `Install/SPT/BepInEx/plugins/LevelGateProgression/{LevelGate.Progression.dll, emblems/, sounds/}` + `README.md` + `Source/`. Tell them: "copy Install\SPT into C:\SPT, overwrite".
- **Send every changed build as a zip** to the user (SendUserFile, or put it where they can grab it) — they install and test in game, then send back a log.
- **New Progression visuals go into F12 › "5. CURRENTLY TESTING"** as sliders / on-off switches first. The user tunes them in game, sends the log, and their final values become the defaults (then the dial is hidden). See "F12 tuning workflow" below.
- **Polish, don't redesign** Progression: no new panels, no extra colours, no extra decorative effects, keep the hierarchy. Colour meaning: current = orange, selected/viewing = light neutral, locked = grey, unmet = red, unlocked = subdued green, NEW = yellow/gold.
- **Be honest**: say when something is untested in game (nothing here can be run in the real game from a cloud session), and read the user's log before guessing.
- Bump the version on every release (`Plugin.cs` `Version`, README top line + a section at the top of `README.md` saying what changed). Modern Editor: `HostForm.Version` (editor), `ModLog.ModVersion` (server), `CHANGELOG.md`.
- Commit with clear messages; no PR unless asked. Work goes on the branch the session names (so far `claude/m4a1-reload-levelgate-bugs-asmzj6`).
- Functionality over visuals in the editor; it's for game designers — big-screen and 1080p friendly.
- Explain things plainly and briefly; the user often asks "list me what you changed" — keep a short list ready.

## F12 tuning workflow (Progression)

1. Add a `ConfigEntry` in `Plugin.cs` in section `T` ("5. Testing"), with a display name in the `names` dictionary, and add it to `polishDials` (it rebuilds the screen on change) or `liveDials` (read every frame).
2. Put its key in `testingNow` — only those keys are shown in F12 (the rest of the Testing section is hidden: tuned).
3. Read it through `ProgScreen.Polish` (`ProgScreen.Polish.cs`), e.g. `Pct(ProgressionPlugin.TestX, default)`.
4. Every change is logged as `testing: Key = value`. From the user's log take the **last** value per key:
   `grep "testing:" log | grep -v "close and" | awk -F'testing: ' '{print $2}' | awk -F' = ' '{last[$1]=$2} END{for(k in last) print k, last[k]}'`
5. Make those the defaults (both the `Config.Bind` default and the `Pct(...)` fallback), empty them out of `testingNow`.
   If an old saved value must not carry over, use a **new key** instead of changing the default.

## Reading the user's Progression log

`BepInEx\LogOutput`-style file they upload (e.g. `Progression - Copy.log`). Useful greps: `ERROR|Exception`,
`slow frame` (hitches, with what caused them), `testing:`, `emblems:` / `sounds:` (missing files = bad install),
`character emblem`, `name reflection`, `settings:` (line 3: every F12 value at start).

## Building

- **On Windows (desktop)**: `dotnet build LevelGate.Progression -c Release -p:SptDir=C:\SPT` (refs come from the game's `EscapeFromTarkov_Data\Managed` and `BepInEx\core`). Modern Editor: `ModernEditor\Build-Editor.bat`, `ModernEditor\Build-Server.bat`. Needs the .NET 10 SDK (and net472 targeting pack for the plugins).
- **In a Linux cloud session**: there's no game install; the earlier sessions compiled with Roslyn `csc` against reference DLLs downloaded into the scratchpad (NuGet ref packs + Unity/BepInEx refs). Those aren't in the repo (game DLLs must not be committed). Compile to check syntax, but in-game testing is always the user's.
- Editor UI without Windows: `node tools/ui-mock/serve.mjs` then open http://localhost:8766 (Playwright + Chromium are available in cloud sessions).
