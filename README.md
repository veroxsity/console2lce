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

This project now supports end-to-end Xbox360-to-LCE conversion on real saves, including playable `saveData.ms` output.

Working now:

- STFS/XContent package detection for `CON `, `LIVE`, and `PIRS`
- header-driven STFS metadata parsing
- `savegame.dat` extraction from real Xbox 360 `.bin` saves
- direct `savegame.dat` input support
- archive parsing for the decoded 4J world container
- extraction of the inner world tree into normal files and folders
- source-backed Xbox 360 region file parsing for `.mcr` metadata:
  - chunk table offsets
  - timestamps
  - per-chunk stored length and decompressed length
  - RLE flag detection
- first-pass Xbox chunk decode analysis:
  - native LZX candidate attempts
  - optional MCC ToolChest `XBOXSupport64.dll` oracle fallback for chunk payloads
  - payload-shape detection
  - sample chunk coordinate extraction per region
- `inspect` debug output for:
  - `stfs-files.json`
  - `savegame.dat`
  - `savegame-probe.json`
  - `savegame.decompressed.bin`
  - `archive-index.json`
  - `region-analysis.json`
  - `chunk-analysis.json`
  - extracted inner files under `archive/`

Still in progress:

- native decoding is not solved yet
- the current working decode path uses the external `minecraft.exe` helper shipped with MCC ToolChest when it is available
- chunk analysis can also use MCC ToolChest's `XBOXSupport64.dll` as an oracle when it is installed, which confirms real region chunks decode to recognizable payloads

Known conversion limitations right now:

- some stair endpoints can still rotate incorrectly and form unintended corners in specific builds
- some converted areas may load with imperfect lighting and require in-game updates/rebuild ticks to fully settle
- conversion quality is currently best-effort for complex metadata-heavy structures

## Roadmap

- replace the external decode fallback with a built-in implementation
- map extracted world data into the LCE save structure
- reduce remaining metadata edge cases (including stair endpoint rotation)
- improve first-load lighting consistency without requiring manual in-game fixes

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
- `region-analysis.json` when decoding succeeds
- `chunk-analysis.json` when decoding succeeds
- extracted inner files under `archive/` when decoding succeeds
- `chunk-analysis.json` now records decoded payload lengths so built-in failures can be compared directly against MCC oracle results

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

For chunk-level analysis, Console2LCE can also use MCC ToolChest's `XBOXSupport64.dll`:

- default lookup:
  - `%ProgramFiles(x86)%\MCCToolChest\XBOXSupport64.dll`
  - `%ProgramFiles%\MCCToolChest\XBOXSupport64.dll`
- override:
  - `CONSOLE2LCE_XBOX_SUPPORT_PATH=<full-path-to-XBOXSupport64.dll>`

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
- `inspect` now also parses the extracted `.mcr` metadata so chunk tables and Xbox chunk headers can be inspected without guessing
- `inspect` now also attempts a first sample chunk decode per region so chunk payload work can be validated against real data
- on the current sample, MCC ToolChest's chunk decoder resolves sampled region chunks into a recognizable compact NBT-like payload shape, which narrows the remaining built-in decoder work to the Xbox chunk compression step rather than the region/archive model

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
