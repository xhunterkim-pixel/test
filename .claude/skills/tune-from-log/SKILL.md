---
name: tune-from-log
description: Turn the user's Progression log (after they moved F12 "CURRENTLY TESTING" sliders in game) into new defaults. Use when the user uploads a Progression log after testing dials.
---

# Tune Progression defaults from the user's log

1. Find the last value of each dial the user moved:
   ```
   grep "testing:" LOG | grep -v "close and" | awk -F'testing: ' '{print $2}' \
     | awk -F' = ' '{last[$1]=$2} END{for(k in last) print k, last[k]}'
   ```
   Line 3 of the log (`settings: …`) has every F12 value at game start, if a dial wasn't moved.
2. Also scan the log: `grep -n "ERROR\|Exception"`, `slow frame` (worst ones and what caused them), `emblems:` / `sounds:`
   (a missing file means a bad install), plus any feature log lines (for example `name reflection:`, `character emblem:`).
3. For each tuned key, make that value the default in **both** places: the `Config.Bind(T, "<Key>", <default>, …)` in
   `Plugin.cs` and the `Pct(ProgressionPlugin.<Entry>, <default>)` fallback in `ProgScreen.Polish.cs`.
4. Remove the key from `testingNow` in `Plugin.cs` (hidden = tuned). Add a comment saying which values came from the user.
5. Tell the user which values became defaults, and report anything the log showed (errors, hitches, missing files), then release
   with the `release-progression` skill.
