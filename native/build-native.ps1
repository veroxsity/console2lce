param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$nativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildRoot = Join-Path $nativeRoot "build\win-x64"

cmake -S $nativeRoot -B $buildRoot -G "Visual Studio 17 2022" -A x64
cmake --build $buildRoot --config $Configuration
