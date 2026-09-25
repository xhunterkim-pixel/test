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

- **Traders list** — *New trader* (creates a folder with a placeholder icon),
  *Delete* (moves the folder to `deleted_traders`, nothing is erased).
- **Trader tab** — name, nickname, surname, location, description, currency,
  unlocked from the start, flea listing, restock time, loyalty levels, and
  **Choose icon from PC...** (any png/jpg; saved as a square `avatar.png`).
- **Offers / barters tab** — *Add offer* opens the item search (weapons by
  default, untick to see everything). Per offer: loyalty level, unlimited or
  limited stock, buy limit, and the cost: any mix of money
  (*+ Roubles / + Dollars / + Euros*) and barter items (*Add cost* → *Pick...*).
  Weapons are sold as the game's default assembled preset (untick
  *Weapon preset* for a bare receiver).
- **Quests tab** — name, description, completion message, unlock level,
  optional image, prerequisite quests (tick any quest of any of your
  traders), objectives and rewards:

  | Objective | Meaning |
  |---|---|
  | HandoverItem | hand N of the listed items to the trader (optionally found in raid) |
  | FindItem | have N of the listed items found in raid |
  | Kill | kill N of Any / Savage / AnyPmc / Usec / Bear, optionally only on certain maps (`bigmap, Woods, Shoreline, Interchange, factory4_day, factory4_night, laboratory, RezervBase, TarkovStreets, Lighthouse, Sandbox`) |

  | Reward | Meaning |
  |---|---|
  | Experience | XP |
  | TraderStanding | standing with this trader (e.g. 0.05) |
  | Item | N of an item (weapons come as presets) |
  | UnlockOffer | unlocks one of this trader's offers when the quest is done; **Unlocked quantity** sets its stock per restock (0 = the offer's own setting) |

Objective text is generated automatically (e.g. "Hand over found in raid
Bolts") unless you type your own.

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

## Building

Both need the **.NET 10 SDK** on Windows.

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
