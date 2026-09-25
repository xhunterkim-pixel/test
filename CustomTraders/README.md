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

## The editor (Trader Editor)

Laid out like Spotify: your traders on the left, the selected trader in the
middle (big header, then **Trader / Offers & barters / Quests / Checks & log**),
and whatever you click opens on the right. The interface is a web page drawn
by the Edge engine that's built into Windows 10/11 (WebView2), so scrolling
and switching pages are smooth. Drag the thin line left of the right panel,
or the column edges in list headers, to resize.

- **Appearance** (top right) — background picture from your PC (bright /
  medium / dark) and the button color (any color).
- **Trader** — on/off switch (off = the server skips the trader, nothing is
  deleted), names, description, currency, unlocked from the start, flea,
  restock time, loyalty levels. The currency is what the trader pays when you
  sell to him and what "Spent" for loyalty levels is counted in; each offer's
  price is set on the offer.
- **Offers & barters** — 🔒 orange = unlocked by a quest (which one), green
  = for sale from the start. Stock per restock (for everyone) and buy limit
  per player live on the offer. Price = any mix of money and barter items.
- **Quests** — each row shows the ways (1 WAY / 2 WAYS...), what it needs
  first, objectives and all rewards. On the right:
  - **Unlock requirements** — level and every quest needed before it.
  - **Required quests** — switch on the quests that must be done first; for a
    quest with several ways, any finished way counts.
  - **Hardcore** — the quest fails if the player dies / goes MIA / leaves a
    raid (it can be restarted).
  - **Objectives**, each in a way A–D (the player finishes any ONE way):

    | Type | Options |
    |---|---|
    | Hand over | items or money (₽ $ € GP coin Lega medal), found in raid |
    | Find | items found in raid |
    | Kill | anyone / PMC / USEC / BEAR / Scavs / bosses (pick which); specific weapon or grenade; ammo caliber; wearing something; maps; body parts (headshots); distance (at least / within N m); in-raid hours; all in one raid |
    | Extract | which exits count (survived, run-through, killed, MIA, left); wearing something; maps; one raid |
    | Use item | food / drinks / meds N times; maps; one raid |
    | Skill level | reach level N in a skill |

    In game each way is its own quest; when one is turned in, the others are
    marked completed (no rewards) and disappear from the list.
  - **Rewards** — XP, standing, items (optionally given when the quest is
    accepted), unlock an offer, skill points, extra stash rows.
- **Checks & log** — runs on open, shortly after every change and before
  saving: ✖ errors (the server would skip it, or a quest can never be done /
  unlocked), ⚠ warnings, i info, ✔ "Quest will work — unlocks at level N
  after ...". Double-click a line to jump there. Saves are logged too.

If the editor ever hits an error it shows a message and writes
`CustomTraders.Editor.crash.txt` next to the exe.

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
