# SCUM Recipe Editor

A Windows tool for SCUM cooking mods: change the game's recipes, make your own recipes and cookbooks, and build them
into a mod pak — without the Unreal editor.

Version **0.1.0b** (beta). It works end to end (edit → build → cook in game), but expect rough edges; see
[Known limitations](#known-limitations).

## What it does

- **Game recipes.** Change where a dish is cooked (pan, pot, oven...), cooking time and temperature, ingredient slots:
  add or remove slots and variants, amounts (whole item, uses, litres), the slot caption, how much of an item's
  nutrition goes into the dish. "As in game" undoes a recipe's changes.
- **Your own recipes.** From scratch or as a copy of a game recipe, with their own dish (name, description, icon).
- **Any item as an ingredient.** Food, drinks and liquids, and every other item of the game (category *Inedible*:
  bolts, tools, parts...). Checked in game: a slot of bolts takes one bolt from a stack.
- **Cookbooks.** Add recipes to the game's cookbooks, make your own cookbooks, or make a recipe known from the start.
- **Ingredients view.** Which recipes use an item, and how.
- **Build.** The mod pak (+ `.sig`) goes straight into `SCUM\Content\Paks\~mods`, or into a folder for a server or for
  Nexus. The shared asset registry is rebuilt with [ScumRegistryMerger](https://github.com/AlexanderDyom/scum-registry-merger),
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

## Projects

A project is a folder with `modkit.json` (your changes, in the format of ScumModKit, the builder inside the editor)
and `editor.json` (the editor's own notes). Projects live in `Documents\SCUM Recipe Editor` by default. A project
holds only your changes: the game is not touched until you build and install the mod.

## Known limitations

- **Own cookbooks don't appear in the world.** The game places items through its loot tables, and a cloned book is
  not in them; players get it with `#SpawnItem <BookId> 1` (shown in the Books view). Choosing where a book is found
  comes in a later update.
- **Dish model:** a new dish looks like the dish it was copied from. You can choose its icon among the game's icons;
  your own model or PNG icon comes later.
- **Names of new recipes, dishes and cookbooks are English only** for now; translations come later.
- **No delete** for a new recipe or a project from inside the editor yet.
- Changing a dish's model ("Model…") is not available yet.

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
