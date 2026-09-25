# CustomTraders — your own traders, barters and quests for SPT 4.1

Two parts:

| Part | What it is | Where it goes |
|---|---|---|
| **CustomTraders.Server** | SPT 4.1.x server mod. At server start it reads every `traders/<folder>/trader.json` and adds the trader, its offers/barters and its quests. | `SPT_Runtime\user\mods\CustomTraders\CustomTraders.dll` |
| **CustomTraders.Editor** | Windows program (`CustomTraders.Editor.exe`) to create and edit those traders — no JSON editing needed. | Anywhere on your PC |

Completely separate from LevelGate.

## First start

1. Build the server mod (below) and start the SPT server once. It creates an
   example trader **Iron** (unlocked from the start, sells an M4A1 for
   roubles) in `user\mods\CustomTraders\traders\Iron\`.
2. Open `CustomTraders.Editor.exe`, click **Browse...** and pick
   `SPT_Runtime\user\mods\CustomTraders`. The editor also finds the server's
   item database (`SPT_Data\database`) so you can search items by name.
3. Edit, click **Save all**, **restart the SPT server**.

## The editor

Laid out like Spotify: your traders on the left (like playlists), the
selected trader in the middle with its offers/quests as a numbered list, and
the details of whatever you click on the right.

- **Top bar** — mod folder (**Browse...** / **Reload**), **Background** (any
  picture from your PC, darkened so text stays readable — bright / medium /
  dark), and the checks counter.
- **Your traders** — **+** new trader (placeholder icon), **✕** remove
  (moves the folder to `deleted_traders`, nothing is erased).
- **Trader** page — name, nickname, location, description, currency,
  unlocked from the start, flea listing, restock time, loyalty levels (big
  text). Right side: the icon and **Choose icon from PC...**
- **Offers & barters** — every offer shows its price and, in orange with 🔒,
  which quest unlocks it (or "From start" in green), its LL and stock.
  **Duplicate** copies an offer. Right side: the lock status (with **Go to
  quest**), item, stock, buy limit, and the price: any mix of money
  (**+ ₽ / + $ / + €**) and barter items.
- **Quests** — level, how many ways to complete it, what it unlocks.
  **Duplicate** copies a quest with new ids. Right side:
  - **Unlock requirements** — the level and *every* quest needed before
    it (all the way back, across traders), and the real unlock level.
  - **Required quests** — tick the quests that must be finished first.
  - **Objectives** — each objective belongs to a *way* (A–D):

    | Type | Options |
    |---|---|
    | Hand over | items or money (**+ ₽ $ € GP coin Lega medal**), found in raid |
    | Find | items found in raid |
    | Kill | Anyone / PMC / USEC / BEAR / Scavs / **Bosses** (pick which); ☐ with a specific weapon or grenade; ☐ with specific ammo (caliber); ☐ while wearing something; ☐ only on specific maps |
    | Extract | survive and extract N times; ☐ wearing something; ☐ specific maps |
    | Use item | use food / drinks / meds N times in raid; ☐ specific maps |

    **Ways (options):** put objectives in way A, B, C, D and the player
    finishes **any one** way (all objectives inside it). Example — level 38
    "Grizzly": A = hand in 20 FIR Grizzly, B = use 10 Grizzly in raid,
    C = hand over 5,000,000 ₽. In game each way appears as its own quest
    ("Grizzly — Option A"...), and finishing one cancels the others (the
    game's own "one of these quests" mechanic). Quests that require it
    unlock after whichever way was done.
  - **Rewards** — XP, standing, items, **Unlock offer** (with the stock
    after unlocking). Every way gives the same rewards.
- **Checks & log** — runs when the editor opens, a moment after every
  change, and before saving. ✖ errors (the server would skip it, or the quest
  can never be done/unlocked: unknown item ids, 0 amounts, a weapon that
  can't fire the chosen caliber, a gear item used as a weapon, missing or
  circular required quests...), ⚠ warnings, i info, ✔ "Quest will work —
  unlocks at level N after ...". Double-click a line (or **Go to it**) to
  jump there. Saves and icon changes are logged too.

Kill "with ammo" works by caliber (the game counts kills per caliber, not per
exact bullet): pick any bullet and its caliber is used.

## Files

```
user\mods\CustomTraders\
  CustomTraders.dll
  traders\
    Iron\
      trader.json      <- everything about the trader
      avatar.png       <- the icon
      quest_<id>.png   <- optional quest images
    <next trader>\ ...
```

Ids (trader, offers, quests) are generated once and then stay the same —
don't change them after players have used a trader, or bought offers and
quest progress won't match any more.

## Building (double-click)

Both need the **.NET 10 SDK** on Windows.

- **`Build-Editor.bat`** — makes a single `Editor\CustomTraders.Editor.exe`
  next to the script and opens that folder. Put a shortcut to it on your
  desktop; that's the only thing you need to run from then on.
- **`Build-Server.bat`** — builds the server mod and copies
  `CustomTraders.dll` into `SPT_Runtime\user\mods\CustomTraders` (path set by
  `<SptServerDir>` in `CustomTraders.Server.csproj`, default
  `C:\SPT\SPT_Runtime`).

Changing a trader's icon: use **Choose icon from PC...** in the editor (or
replace `avatar.png`) and restart the server. The icon's URL includes a
fingerprint of the image, so the game downloads the new one instead of
showing its cached copy.

Building by hand instead:

- **Server mod:** open `CustomTraders.Server\CustomTraders.Server.csproj`,
  set `<SptServerDir>` to the folder containing `user\mods`
  (e.g. `C:\SPT\SPT_Runtime`), build. The DLL is copied into
  `user\mods\CustomTraders\`. SPT's packages are published as
  `SPTushonka.*` on NuGet (4.1.6 matches the server).
- **Editor:** open `CustomTraders.Editor\CustomTraders.Editor.csproj`, build;
  the exe is in `bin\Debug\net10.0-windows\` (or *Publish* for a single
  folder you can copy anywhere).

The server log shows what was loaded:

```
[CustomTraders] Iron: 3 offer(s), 2 quest(s)
[CustomTraders] Loaded 1 trader(s) from ...\user\mods\CustomTraders\traders
```

and warns about anything it skipped (e.g. an unknown item id).
