# Native LZX Helper

`console2lce_lzx_native` is a small Windows-only helper that wraps the raw LZX decompressor from `libmspack`.

- `libmspack` copyright: Stuart Caie
- upstream project: https://www.cabextract.org.uk/libmspack/
- license: LGPL 2.1

The helper is built as a separate native DLL so the third-party LZX implementation stays isolated from the managed project.
