# SCUM Recipe Editor

[![license MIT](https://img.shields.io/badge/license-MIT-2ea44f)](LICENSE)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4)
![SCUM UE 4.27](https://img.shields.io/badge/SCUM-UE%204.27-1f2937)
![platform Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D6)
![UI 2 languages](https://img.shields.io/badge/UI-2%20languages-E67E22)

A Windows tool for SCUM cooking mods: change the game's recipes, make your own recipes and cookbooks, and build them
into a mod pak — without the Unreal editor.

Version **0.1.1b** (beta). It works end to end (edit → build → cook in game), but expect rough edges; see
[Known limitations](#known-limitations).

## What it does

- **Game recipes.** Change where a dish is cooked (pan, pot, oven...), cooking time and temperature, ingredient slots:
  add or remove slots and variants, amounts (whole item, uses, litres), the slot caption, how much of an item's
  nutrition goes into the dish. "As in game" undoes a recipe's changes.
- **Your own recipes.** From scratch or as a copy of a game recipe, with their own dish (name, description, icon).
- **3D model of a new dish.** Any ready dish of the game, with a 3D view: the dish in hands and on the ground, and its
  look in the pot, pan or oven while cooking (shown in the vessel as the game places it). "Model…" shows any recipe's dish.
- **Any item as an ingredient.** Food, drinks and liquids, and every other item of the game (category *Inedible*:
  bolts, tools, parts...). Checked in game: a slot of bolts takes one bolt from a stack.
- **Cookbooks.** Add recipes to the game's cookbooks, make your own cookbooks, or make a recipe known from the start.
- **Ingredients view.** Which recipes use an item, and how.
- **Build.** The mod pak (+ `.sig`) goes straight into `SCUM\Content\Paks\~mods`, or into a folder for a server or for
  Nexus. For SCUM Server it says whether players need the mod too, and can install it into the SCUM client as well
  (see [Servers](#servers-who-needs-the-mod)). The shared asset registry is rebuilt with [ScumRegistryMerger](https://github.com/AlexanderDyom/scum-registry-merger),
  so the mod works together with other mods that add items.
- Russian and English interface.

## What you need

- Windows 10/11 x64. The exe is self-contained: no .NET install needed.
- SCUM from Steam (the client; a dedicated server folder works too).
- **The AES key of SCUM's paks.** The game's files are encrypted, and the editor reads recipes, items and icons from
  them. The key is not shipped with the editor: ask for it in the SCUM modding community. The editor stores it
  encrypted for your Windows user (DPAPI) in `%APPDATA%\ScumRecipeEditor\settings.json`. If ScumRegistryMerger is
  set up on the same PC, the editor takes the game folder and key from it.
- **[ScumRegistryMerger](https://github.com/AlexanderDyom/scum-registry-merger/releases)** — needed when the mod adds new
  recipes, dishes or cookbooks (changes of game recipes work without it). The build window offers to download it.

## Quick start

1. Run `ScumRecipeEditor.exe`. On first start choose the game folder and enter the AES key.
2. Create a project: a mod name (Latin letters, digits, `_`). The pak name and number are chosen for you
   (`pakchunk9xx-<Name>_P.pak`, the first free number).
3. Edit recipes. `Ctrl+S` saves, `Ctrl+Z` undoes.
4. **Build mod.** Close SCUM first: it locks its paks.
5. Start SCUM and cook. The build window lists `#SpawnItem` commands for the ingredients and books to test with.

Mods in `~mods` are for **single player and your own server only**. Playing modded on official or BattlEye servers
risks a ban.

## Servers: who needs the mod

On a server, the **cooking window runs in the player's game**: it matches the ingredients against the recipes the
player's game has, and only then asks the server to cook. So it depends on the change:

| What the mod changes | Mod on the server only | Mod on the server and on players' PCs |
|---|---|---|
| Cooking time, temperature | works: the server cooks with the new time, but players see the old time in the cooking window | works, players see the new time |
| Ingredients (slots, variants, amounts), where to cook | doesn't work: players' games use their own recipe | works |
| New recipes, dishes, cookbooks, a dish's model or icon | doesn't work | works (on both: the shared registry, see ScumRegistryMerger) |

Checked on a test server: cooking time (server only: cooks with the new time, shows the old one) and an added optional
ingredient (works only with the mod on the client too). The other rows follow from the same rule.

The editor tells you which case your project is: choose **SCUM Server** (button in the top bar, or Settings → Game) and
open **Build mod**. It can also install the same mod into the SCUM client on the same PC, to test as a player. Players
put the mod (`.pak` + `.sig`) into their own `SCUM\Content\Paks\~mods` and start SCUM with `-fileopenlog -nobattleye`.

SCUM Server ships no textures: the editor offers to take icons and 3D models from the SCUM client (only for its own
view, the mod is not affected).

## Projects

A project is a folder with `modkit.json` (your changes, in the format of ScumModKit, the builder inside the editor)
and `editor.json` (the editor's own notes). Projects live in `Documents\SCUM Recipe Editor` by default. A project
holds only your changes: the game is not touched until you build and install the mod.

## Liquids, containers and icons

Some things exist in the game twice under one name: as a **liquid** (measured in ml) and as a **container item** that
holds it. Example, isopropyl alcohol:

- **Isopropyl alcohol, liquid** (`IsopropylAlcohol`, shown as "liquid, ml"): the recipe takes the set amount of
  millilitres from a bottle of alcohol, and the bottle stays. This liquid has no picture of its own in the game, so the
  cooking window shows some other icon in its slot (a piece of meat, for example) — that is only the picture: the
  recipe really uses the alcohol.
- **Isopropyl alcohol, bottle** (`Isopropyl_alcohol`, shown as "container, whole"): the recipe takes the whole bottle,
  with everything in it; nothing is left, not even an empty bottle.

The editor warns about both: when you pick an ingredient without an icon, and when a slot takes a whole container.
Ingredients without an icon of their own in this version: isopropyl alcohol, beer, coffee, cocktail, energy drink and
the waters (clean, dirty, ocean water) as liquids, and the two wall flags.

## Known limitations

- **Own cookbooks don't appear in the world.** The game places items through its loot tables, and a cloned book is
  not in them; players get it with `#SpawnItem <BookId> 1` (shown in the Books view). Choosing where a book is found
  comes in a later update.
- **Dish model:** any ready dish of the game; models of other items, your own FBX model or PNG icon come later. The
  way a dish is held in hands comes with its model.
- **Names of new recipes, dishes and cookbooks are English only** for now; translations come later.
- **No delete** for a new recipe or a project from inside the editor yet.
- **On a server**, only cooking time and temperature work with the mod on the server alone; everything else needs the
  mod on the players' PCs too (see [Servers](#servers-who-needs-the-mod)).
- The 3D view shows the pot and the pan; the oven, grill and skewer show the cooking look alone.

Building from source: see [BUILDING.md](BUILDING.md).

## Credits

Thanks to [CUE4Parse](https://github.com/FabianFG/CUE4Parse) (reading the game's paks),
[BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) (icon textures). Pak layout after
[repak](https://github.com/trumank/repak).

Licences of the packages the editor uses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Disclaimer

This is an unofficial fan tool, not affiliated with Gamepires or Jagex; SCUM and its assets are their property. The
editor and its releases contain no game files and no AES key: it reads the game installed on your own PC, and the mods
it builds are made there.
