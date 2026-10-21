# Third-party notices

SCUM Recipe Editor links against the following open-source packages (versions in the `.csproj` files). Each keeps its
own licence; no source code from these projects is copied into this repository.

| Package | Purpose | Licence |
|---|---|---|
| [CUE4Parse](https://github.com/FabianFG/CUE4Parse) 1.2.2 | reading the game's pak files: recipes, items, cookbooks, texts, icon textures | Apache-2.0 |
| [BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) 2.3.0 | decoding the game's compressed icon textures (BC7 / DXT) | MIT OR Unlicense |
| [CommunityToolkit.HighPerformance](https://github.com/CommunityToolkit/dotnet) 8.4.0 | used by BCnEncoder.Net | MIT |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) 8.0.0 | Windows DPAPI, to store the AES key encrypted for the current user | MIT |
| [System.IO.Hashing](https://github.com/dotnet/runtime) 8.0.0 | hashes in pak and registry files (used by ScumModKit) | MIT |
| [.NET 8 runtime and WPF](https://github.com/dotnet) | runtime and window UI, bundled in the self-contained executable | MIT |

Transitive dependencies of CUE4Parse (Newtonsoft.Json, K4os LZ4 and xxHash, ZstdSharp, Oodle.NET, Zlib-ng.NET,
BouncyCastle, Serilog, Blake3, LZMA-SDK, Infrablack.UE4Config, OffiUtils) keep their respective licences
(MIT / Apache-2.0 / BSD).

[ScumModKit](https://github.com/AlexanderDyom/scum-modkit), the mod builder the editor uses (cloning, recipe edits,
pak and registry writing), is a separate project by the same author with its own repository and notices.

## References (no code taken)

- [repak](https://github.com/trumank/repak) (MIT / Apache-2.0). The pak v11 layout ScumModKit writes was checked
  against repak's output; no repak code is included.
- [ScumRegistryMerger](https://github.com/AlexanderDyom/scum-registry-merger) builds the shared asset registry. The
  editor runs the merger the user has installed (or offers to download it from its GitHub releases); it does not
  include the merger's code.

## Fonts and icons

No font files are bundled. The window uses Bahnschrift and Segoe Fluent Icons / Segoe MDL2 Assets, which come with
Windows. The program icon is original pixel art. Item and dish icons shown in the editor are read from the user's
installed game at run time (see below).

## Game files

SCUM and its assets are the property of Gamepires / Jagex. This tool reads the user's own installed game files and
never redistributes them; the game's pak AES key is never stored in this repository or in the releases (the editor
keeps it only on the user's computer, encrypted for the Windows user). Icons are converted to PNG into a cache on the
user's computer (`%LOCALAPPDATA%\ScumRecipeEditor\cache`) and never published by this project. The mods it builds are
made only on the user's own computer, from that user's installed game; the releases contain no game files. This
project is not affiliated with Gamepires or Jagex.
