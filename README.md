# Toll Plaza

American-style highway toll plazas for **Cities: Skylines II**. Pick a plaza with 2 to 6 booths from the Toll Plazas tab in the Roads menu and drop it on a highway. Vehicles slow through the booths and pay a toll that goes straight to the city treasury, with a Toll Plazas line in the budget screen.

Get it on [Paradox Mods](https://mods.paradoxplaza.com/) (search "Toll Plaza", Mod ID 164944).

## Features

- Plazas with 2 to 6 booths and a canopy
- Cash and E-Toll lanes, closable booths and animated gate arms
- Live roof, toll-rate and speed signs that follow your settings
- A control panel with city totals (this month, last month, all time) and a plaza panel for per-plaza rates
- An Options page for plaza prices and the rates a new city starts with, plus buttons to reset a city's toll data
- Toll data is stored outside the save file and written crash-safe

## Building

You need the official CS2 modding toolchain (Options > Modding in the game), which sets the `CSII_TOOLPATH` user environment variable, plus Node.js 18+ for the UI.

```bash
cd ui && npm install && npm run build
cd .. && dotnet build src/TollPlaza.csproj
```

**A build also copies the mod into your game's local `Mods` folder.** If you also subscribe to the Paradox copy, disable one of them before testing.

## Layout

- `src/`: the mod (C#). Collection and stats in `TollCollectionSystem.cs`, plaza geometry in `PlazaGeometry.cs`, signs in `PlazaSignSystem.cs`, UI bindings in `TollUISystem.cs`.
- `ui/`: the React panels shown in game.
- `textures/` and `tools/PreviewExport`: texture sources and the baker that produces `src/Textures`.
- `ROADMAP.md`: development log and to-do list.

## Credits and license

See [CREDITS.md](CREDITS.md). The code is released under the [MIT License](LICENSE).

This is an unofficial fan mod. It is not affiliated with or endorsed by Colossal Order or Paradox Interactive.
