# Console2LCE

<p align="center">
  <img src="https://img.shields.io/github/license/veroxsity/console2lce?style=for-the-badge" alt="License" />
  <img src="https://img.shields.io/github/last-commit/veroxsity/console2lce?style=for-the-badge" alt="Last Commit" />
  <img src="https://img.shields.io/github/repo-size/veroxsity/console2lce?style=for-the-badge" alt="Repo Size" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Focus-Xbox%20360%20Saves-2A9D8F?style=flat-square" alt="Xbox 360 saves" />
  <img src="https://img.shields.io/badge/Output-LCE%20Saves-1D3557?style=flat-square" alt="LCE saves" />
  <img src="https://img.shields.io/badge/Status-Extraction%20Working-E9C46A?style=flat-square" alt="Extraction working" />
</p>

Console2LCE is a tool for opening Minecraft Xbox 360 saves and moving them toward Minecraft Legacy Console Edition output.

## What It Does

- Reads Minecraft Xbox 360 saves from either:
  - STFS/XContent `.bin` packages
  - raw `savegame.dat` files
- Extracts the embedded `savegame.dat`
- Decodes the inner Xbox 360 world archive
- Extracts files such as:
  - `level.dat`
  - `data/map_*.dat`
  - `players/*.dat`
  - `r.*.*.mcr`
- Builds the groundwork for later LCE conversion

## Project Status

This project is not a finished Xbox360-to-LCE converter yet, but the extraction path is now working on real saves.

Working now:

- STFS/XContent package detection for `CON `, `LIVE`, and `PIRS`
- header-driven STFS metadata parsing
- `savegame.dat` extraction from real Xbox 360 `.bin` saves
- direct `savegame.dat` input support
- archive parsing for the decoded 4J world container
- extraction of the inner world tree into normal files and folders
- `inspect` debug output for:
  - `stfs-files.json`
  - `savegame.dat`
  - `savegame-probe.json`
  - `savegame.decompressed.bin`
  - `archive-index.json`
  - extracted inner files under `archive/`

Still in progress:

- native decoding is not solved yet
- the current working decode path uses the external `minecraft.exe` helper shipped with MCC ToolChest when it is available
- final LCE save generation is still to come

## Roadmap

- replace the external decode fallback with a built-in implementation
- map extracted world data into the LCE save structure
- produce a first playable Xbox360-to-LCE conversion path

## CLI

```text
Console2Lce inspect <path-to-save.bin-or-savegame.dat> --out <debug-dir>
Console2Lce extract <path-to-save.bin-or-savegame.dat> --out <extract-dir>
Console2Lce convert <path-to-save.bin> --out <lce-output-dir>
```

### `inspect`

Writes:

- `stfs-files.json` when the input is a `.bin` package
- `savegame.dat`
- `savegame-probe.json`
- `savegame.decompressed.bin` when decoding succeeds
- `archive-index.json` when decoding succeeds
- extracted inner files under `archive/` when decoding succeeds

### `extract`

Writes:

- `savegame.dat`
- `savegame.decompressed.bin` when decoding succeeds
- `archive-index.json` when decoding succeeds
- extracted inner files under `archive/` when decoding succeeds

## External Decode Fallback

If MCC ToolChest is installed, Console2LCE can use its bundled helper automatically:

- default lookup:
  - `%ProgramFiles(x86)%\MCCToolChest\support\minecraft.exe`
  - `%ProgramFiles%\MCCToolChest\support\minecraft.exe`
- override:
  - `CONSOLE2LCE_MINECRAFT_TOOLKIT_PATH=<full-path-to-minecraft.exe>`

## Sample Progress

Against the current local sample in `.local_testing/`:

- package type is detected as `CON`
- `savegame.dat` is found and extracted successfully
- extracted `savegame.dat` size is `8,748,032` bytes
- decoded output size is `13,855,786` bytes with the external fallback
- the decoded archive currently yields 14 files, including:
  - `level.dat`
  - `data/map_0.dat`
  - `data/map_1.dat`
  - `data/mapDataMappings.dat`
  - two player `.dat` files
  - Overworld and Nether `.mcr` region files

## Notes

- This repository is focused on opening and converting Xbox 360 world data, not repacking retail-valid Xbox packages.
- The current priority is reliable built-in decoding and LCE output generation.
- The links below are useful background for inner file formats once the archive has been decoded:
  - https://minecraft.fandom.com/wiki/Chunk_format
  - https://minecraft.fandom.com/wiki/Data_values
  - https://minecraft.fandom.com/wiki/NBT_format
  - https://minecraft-ids.grahamedgecombe.com/potion-calculator

## Related Projects

- Hub repo: https://github.com/veroxsity/MinecraftLCE
- Client repo: https://github.com/veroxsity/LCEClient
- Debug client repo: https://github.com/veroxsity/LCEDebug
- Launcher repo: https://github.com/veroxsity/LCELauncher
- Save converter repo: https://github.com/veroxsity/LCE-Save-Converter
- Server repo: https://github.com/veroxsity/LCEServer
