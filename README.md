# C&N Jewelry

A gameplay mod for [Vintage Story](https://www.vintagestory.at/) that adds a full gemstone crafting chain: find rough gems, cut and grind them, and socket them into tools, weapons and armor for stat bonuses.

**[Mod page](https://mods.vintagestory.at/canjewelry)** · C# / .NET · Vintage Story API

## Features

- **Gem processing chain:** rough gems are cut on a gem cutting table and ground on a jewel grinder; a wire drawing bench produces wire for jewelry.
- **Socket system:** tools, weapons and armor get gem sockets; each gem type grants its own buff depending on the item it's set into.
- **Visible gems:** socketed gems are rendered on the item model itself (in hand, on armor, in display holders).
- **Jewelry:** 18 adornment slots and a jeweler's table.
- **Mod API:** other mods can add their own jewelry and socketable items through a documented contract ([docs/API.md](canjewelry/docs/API.md)).
- **Configurable:** balance and per-gem effects are controlled by a server config and synced to clients.

## Technical highlights

- **Client/server split:** game logic is server-authoritative. The client only renders and sends requests, and the config is synced over a custom network packet.
- **Custom rendering:** gem meshes are generated and composited onto item models at runtime, with cached meshes to keep the frame cost low.
- **Unit tests:** a separate `canjewelry.Tests` project.
- **Build pipeline:** a Cake build script (`CakeBuild`) packages the release zip.

## Credits

Author: **KenigVovan**. Contributors: justOmi, DarkPaapi, Wailwolf, FourLanguages, sadCarb0ne, ripls, Jikoo.
