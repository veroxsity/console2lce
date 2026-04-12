# Console2LCE

<p align="center">
  <img src="https://img.shields.io/github/license/veroxsity/console2lce?style=for-the-badge" alt="License" />
  <img src="https://img.shields.io/github/last-commit/veroxsity/console2lce?style=for-the-badge" alt="Last Commit" />
  <img src="https://img.shields.io/github/repo-size/veroxsity/console2lce?style=for-the-badge" alt="Repo Size" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Focus-Xbox%20360%20Saves-2A9D8F?style=flat-square" alt="Xbox 360 saves" />
  <img src="https://img.shields.io/badge/Output-LCE%20Saves-1D3557?style=flat-square" alt="LCE saves" />
  <img src="https://img.shields.io/badge/Status-Phase%202%20Research-E76F51?style=flat-square" alt="Phase 2 Research" />
</p>

Console2LCE is a conversion tool for turning Minecraft Xbox 360 save `.dat` data into Minecraft Legacy Console Edition save output.

## What It Does

- Reads Minecraft Xbox 360 save containers stored as `.bin` STFS/XContent packages
- Extracts the embedded `savegame.dat`
- Investigates and validates the compressed inner save payload
- Aims to convert the extracted world data into LCE-compatible save output

## Project Status

This project is currently in active reverse-engineering and tooling work. It is not a finished converter yet.

Working now:

- STFS/XContent package detection for `CON `, `LIVE`, and `PIRS`
- header-driven STFS metadata parsing
- file table parsing and directory listing
- `savegame.dat` extraction from a real Xbox 360 `.bin`
- `inspect` debug output for raw STFS entries, extracted `savegame.dat`, and decompression probe results
- candidate decompression probe support for:
  - stored/uncompressed payloads
  - RLE-only payloads
  - zlib-only payloads
  - zlib-then-RLE payloads

Current blocker:

- the real Xbox 360 sample does not match the Win64 zlib path
- the current sample now shows a recovered `00000000` prefix immediately before the computed first data block and a plausible big-endian save envelope, which is consistent with Xbox 360 save endianness
- the remaining unknown is the Xbox-native `LZXRLE` / `XMemDecompress` path, plus whether the missing 4-byte prefix should be treated as a real extraction adjustment or only as a package-level recovery heuristic

## Roadmap

- finish reliable Xbox 360 `savegame.dat` decompression
- parse the decompressed inner archive and recover embedded files
- map extracted world data into the LCE save structure
- produce a first playable Xbox360-to-LCE conversion path

## CLI

```text
Console2Lce inspect <path-to-save.bin> --out <debug-dir>
Console2Lce extract <path-to-save.bin> --out <extract-dir>
Console2Lce convert <path-to-save.bin> --out <lce-output-dir>
```

### `inspect`

Writes:

- `stfs-files.json`
- `savegame.dat`
- `savegame-probe.json`
- `savegame.decompressed.bin` when a candidate decoder succeeds

### `extract`

Writes the raw `savegame.dat` payload extracted from the STFS package.

## Current Output

The tool currently produces debug artifacts intended to make format work repeatable:

- `stfs-files.json`
- `savegame.dat`
- `savegame-probe.json`
- `savegame.decompressed.bin` when a candidate decoder succeeds

## Sample Progress

Against the current local sample in `.local_testing/`:

- package type is detected as `CON`
- `savegame.dat` is found and extracted successfully
- extracted size is `8,748,032` bytes
- the Phase 2 probe currently reports no valid decompression match
- the probe now records:
  - computed first data block offset: `0x227000`
  - recovered prefix immediately before that block: `00000000`
  - a plausible recovered big-endian save envelope, consistent with Xbox 360 save endianness
- current evidence supports the theory that either:
  - the STFS extraction is still missing the first 4 bytes of the file payload
  - or the package-level prefix is only a recovery hint and the real remaining requirement is the native `XMem` decoder before the archive header will make sense

## Notes

- This repository is focused on Xbox 360 save ingestion first, not on repacking retail-valid Xbox packages.
- The current priority is extraction, decompression, and archive inspection.
- Full LCE conversion comes after the Xbox 360 side is proven against real save samples.

## Related Projects

- Hub repo: https://github.com/veroxsity/MinecraftLCE
- Client repo: https://github.com/veroxsity/LCEClient
- Debug client repo: https://github.com/veroxsity/LCEDebug
- Launcher repo: https://github.com/veroxsity/LCELauncher
- Save converter repo: https://github.com/veroxsity/LCE-Save-Converter
- Server repo: https://github.com/veroxsity/LCEServer
