# Building SCUM Recipe Editor

## Requirements

- Windows 10/11 x64, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- [ScumModKit](https://github.com/AlexanderDyom/scum-modkit), the mod builder the editor uses. It is a git submodule
  in `modkit/`, pinned to a release tag, so clone with submodules:

  ```
  git clone --recursive https://github.com/AlexanderDyom/scum-recipe-editor
  ```

  In an existing clone: `git submodule update --init`. `RecipeEditor.Core` takes ScumModKit from `modkit/`; without it,
  from a `modkit` folder next to the editor's folder.

NuGet packages (restored by `dotnet build`): CUE4Parse 1.2.2, BCnEncoder.Net, System.Security.Cryptography.ProtectedData.

## Build and run

```
dotnet build RecipeEditor.sln -c Release
RecipeEditor.Gui\bin\Release\net8.0-windows\ScumRecipeEditor.exe
```

## Single exe for release

```
dotnet publish RecipeEditor.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

This gives one `publish/ScumRecipeEditor.exe` (~76 MB) that runs without .NET installed. The version is
`Version` / `InformationalVersion` in `RecipeEditor.Gui/RecipeEditor.Gui.csproj`.

## Tests

```
dotnet test RecipeEditor.Tests
```

Most tests read the installed game, so they need SCUM and the AES key of its paks:

- the game: env `SCUM_GAME` (game folder), else the SCUM install found in the Steam libraries;
- the key: env `SCUM_AES_KEY_FILE` (a text file whose first line is the key).

Without the game or the key those tests pass without checking anything.

**Never commit the key**, and don't paste it into issues, logs or screenshots. `key.txt` and `*.key` are in
`.gitignore`; keep the key file outside the repository if you can, and point `SCUM_AES_KEY_FILE` to it. The editor
itself keeps the key only on the user's PC, encrypted for the Windows user (DPAPI), in
`%APPDATA%\ScumRecipeEditor\settings.json`.

## Layout

- `RecipeEditor.Core`: game data (recipes, cookbooks, items, localization) read with CUE4Parse, icon textures to PNG,
  the recipe model and its modkit ops, projects (`modkit.json` + `editor.json`), the build (ScumModKit, install,
  shared registry through ScumRegistryMerger).
- `RecipeEditor.Gui`: the WPF window. UI strings: `Strings/<code>.json` (embedded).
- `RecipeEditor.Tests`: xUnit tests.
