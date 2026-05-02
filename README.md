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

Console2LCE is a tool designed for reading Minecraft Xbox 360 saves and converting them to the Minecraft Legacy Console Edition format.

## What It Does

- Reads Minecraft Xbox 360 saves from:
  - STFS/XContent `.bin` packages
  - Raw `savegame.dat` files
- Extracts the embedded `savegame.dat`
- Decodes the inner Xbox 360 world archive
- Extracts files such as:
  - `level.dat`
  - `data/map_*.dat`
  - `players/*.dat`
  - `r.*.*.mcr`
- Writes converted LCE `saveData.ms` output

## Project Status

This project supports end-to-end Xbox 360 to LCE conversion on real saves, including playable `saveData.ms` output.

**Currently Working:**

- STFS/XContent package detection for `CON `, `LIVE`, and `PIRS`
- Header-driven STFS metadata parsing
- `savegame.dat` extraction from real Xbox 360 `.bin` saves
- Direct `savegame.dat` input support
- Archive parsing for the decoded 4J world container
- Extraction of the inner world tree into normal files and folders
- Source-backed Xbox 360 region file parsing for `.mcr` metadata:
  - Chunk table offsets
  - Timestamps
  - Per-chunk stored length and decompressed length
  - RLE flag detection
- Built-in XMem/LZX savegame and chunk decoding through XNA native compression
- Conversion of decoded Xbox 360 region chunks into LCE region files
- Preservation of auxiliary archive files such as maps and player data
- Remapping of the first Xbox player file to the Windows64 host player slot
- Outputs `inspect` debug information for:
  - `stfs-files.json`
  - `savegame.dat`
  - `savegame-probe.json`
  - `savegame.decompressed.bin`
  - `archive-index.json`
  - `region-analysis.json`
  - `chunk-analysis.json`
  - Extracted inner files under `archive/`

**In Progress:**

- Mashup/template-pack world metadata edge cases
- Remaining block metadata repairs for some structures
- First-load lighting consistency

**Known Limitations:**

- Some mashup packs may trigger fallback world generation in the target.
- Some stair endpoints can rotate incorrectly and form unintended corners in specific builds.
- Some converted areas may load with imperfect lighting and require in-game updates or rebuild ticks to fully settle.
- Conversion quality is currently best-effort for complex metadata-heavy structures.

## Roadmap

- Improve mashup/template-pack metadata handling
- Reduce remaining metadata edge cases (including stair endpoint rotation)
- Improve first-load lighting consistency without requiring manual in-game fixes

## CLI

```text
Console2Lce.Cli.exe inspect <path-to-save.bin-or-savegame.dat> --out <debug-dir>
Console2Lce.Cli.exe extract <path-to-save.bin-or-savegame.dat> --out <extract-dir>
Console2Lce.Cli.exe convert <path-to-save.bin-or-savegame.dat> --out <lce-output-dir>
```

### `inspect`

Writes:

- `stfs-files.json` when the input is a `.bin` package
- `savegame.dat`
- `savegame-probe.json`
- `savegame.decompressed.bin` when decoding succeeds
- `archive-index.json` when decoding succeeds
- `region-analysis.json` when decoding succeeds
- `chunk-analysis.json` when decoding succeeds
- Extracted inner files under `archive/` when decoding succeeds

### `extract`

Writes:

- `savegame.dat`
- `savegame.decompressed.bin` when decoding succeeds
- `archive-index.json` when decoding succeeds
- Extracted inner files under `archive/` when decoding succeeds

## Notes

- This repository is focused on opening and converting Xbox 360 world data, not repacking retail-valid Xbox packages.
- The converter uses XNA native compression for Xbox XMem/LZX streams. If XNA is not installed, place a compatible `XnaNative.dll` next to the executable.
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
