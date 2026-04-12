# Console2LCE

<p align="center">
  <img src="https://img.shields.io/github/license/veroxsity/console2lce?style=for-the-badge" alt="License" />
  <img src="https://img.shields.io/github/last-commit/veroxsity/console2lce?style=for-the-badge" alt="Last Commit" />
  <img src="https://img.shields.io/github/repo-size/veroxsity/console2lce?style=for-the-badge" alt="Repo Size" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Focus-Xbox%20360%20Saves-2A9D8F?style=flat-square" alt="Xbox 360 saves" />
  <img src="https://img.shields.io/badge/Output-LCE%20Saves-1D3557?style=flat-square" alt="LCE saves" />
  <img src="https://img.shields.io/badge/Status-Bootstrapping-E76F51?style=flat-square" alt="Bootstrapping" />
</p>

Console2LCE is a conversion tool for turning Minecraft Xbox 360 save `.dat` data into Minecraft Legacy Console Edition save output.

## Workspace Role

- Use `console2lce` when the source world comes from an Xbox 360 save dump rather than Java Edition or an existing LCE save folder
- Pair it with `LCEClient`, `LCEDebug`, or `LCEServer` to validate converted worlds inside the wider LCE workspace
- Keep it alongside `LCE-Save-Converter` as the Xbox-360-focused conversion path inside the hub

## Planned Scope

- Read Xbox 360 save `.dat` inputs
- Convert world data into LCE-compatible save output
- Provide a simple repeatable workflow for testing converted console saves in the rest of the workspace

## Status

This repository is currently being bootstrapped. The initial goal is Xbox 360 `.dat` to LCE save conversion, with implementation details to follow as the converter is built out.

## Related Repositories

- Hub repo: https://github.com/veroxsity/MinecraftLCE
- Client repo: https://github.com/veroxsity/LCEClient
- Debug client repo: https://github.com/veroxsity/LCEDebug
- Launcher repo: https://github.com/veroxsity/LCELauncher
- Save converter repo: https://github.com/veroxsity/LCE-Save-Converter
- Server repo: https://github.com/veroxsity/LCEServer
