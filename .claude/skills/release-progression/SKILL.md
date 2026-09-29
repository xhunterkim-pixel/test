---
name: release-progression
description: Build and ship a LevelGate Progression release as the FULL package (DLL + emblems + sounds). Use whenever a Progression change is ready for the user to test in game.
---

# Release LevelGate Progression

1. Bump `Version` in `LevelGate.Progression/Plugin.cs` (0.9.94 → 0.9.95 …). Update the top line of
   `LevelGate.Progression/README.md` and add a `## <version>` section at the top listing what changed, in plain words.
2. Build: on Windows `dotnet build LevelGate.Progression -c Release -p:SptDir=C:\SPT`. In a cloud session, compile with
   whatever toolchain you have, and say plainly that it's untested in game.
3. Package the **full** layout, never the DLL alone (a DLL-only zip once wiped the user's emblems and sounds):
   ```
   LevelGateProgression_<version>.zip
     README.md                                   (copy of LevelGate.Progression/README.md)
     Install/SPT/BepInEx/plugins/LevelGateProgression/
       LevelGate.Progression.dll
       emblems/   (from LevelGate.Progression/emblems, incl. emblems.txt)
       sounds/    (from LevelGate.Progression/sounds)
     Source/LevelGate.Progression/   (*.cs, *.csproj, README.md)
   ```
   Check that the zip lists the emblem PNGs, `emblems.txt` and the mp3s before sending.
4. Commit and push to the session's branch. Commit message: `LevelGate Progression <version>: <what>`.
5. Send the zip to the user: "copy Install\SPT into C:\SPT, overwrite". Say what to look for in game and in the log.
