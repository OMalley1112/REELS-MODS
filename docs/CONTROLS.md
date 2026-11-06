# Controls

Quest 2 / 3 / 3S / Pro, both hands supported.

| Do | Result |
|---|---|
| Press **X** or **Y** (left hand) | open / close the menu |
| Press **A** or **B** (right hand) | open / close the menu |
| **Trigger** on either hand | click the row the laser points at |
| Look at a row | row highlights |
| **<** and **>** buttons in the footer | previous / next page |
| **ESC** (desk testing) | close |
| **Left / Right arrows** (desk testing) | change page |
| **Mouse click** (desk testing) | click a row |

With no headset attached the app still runs: camera parked at eye height, menu
in front of you, mouse does the clicking. Useful for checking layout without
putting the Quest on.

## Tabs

| Tab | What's on it |
|---|---|
| **MAIN** | menu settings - hand side, notifications, laser, pop, panel size, close |
| **REELS** | the mods - Give Coins, Give Bundles, Unlock Every Rod, Instant Reel, Auto Catch, Monster Lure, Zero Lure Cooldown, Never Lose A Fish |
| **PAGES** | page nav, save/reset loadout, about |

Six rows per page. Page counter sits in the footer.

## What the toggles actually do in this build

This is the **standalone menu build**. The UI, tabs, rows, pop, notifications
and laser all work. The REELS toggles flip state and toast correctly - they
are not yet wired to Reel's economy, because that needs the game APK pulled
from a headset and dumped (see `docs/ENGINE.md`).

Once the dump lands, `OnRowClick` in `MenuManager.cs` is the single place
each toggle gets a real effect.
