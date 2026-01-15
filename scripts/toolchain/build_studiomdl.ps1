$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$MdlForgeRoot = Join-Path $RepoRoot "tools\\MDLForge"
$BuildDir = Join-Path $MdlForgeRoot "build"
$InstallPrefix = Join-Path $RepoRoot "tools\\studiomdl"

if (-not (Test-Path $MdlForgeRoot)) {
  throw "MDLForge not found: $MdlForgeRoot"
}

if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
  throw "cmake not found on PATH."
}

$BuildType = if ($env:CMAKE_BUILD_TYPE) { $env:CMAKE_BUILD_TYPE } else { "Release" }
$Require32Bit = if ($env:STUDIOMDL_REQUIRE_32BIT) { $env:STUDIOMDL_REQUIRE_32BIT } else { "ON" }
$Arch = if ($env:MDLFORGE_CMAKE_ARCH) { $env:MDLFORGE_CMAKE_ARCH } else { "Win32" }

cmake -S $MdlForgeRoot -B $BuildDir -A $Arch -DSTUDIOMDL_REQUIRE_32BIT=$Require32Bit
cmake --build $BuildDir --config $BuildType --parallel
cmake --install $BuildDir --config $BuildType --prefix $InstallPrefix

Write-Host "Installed studiomdl to: $InstallPrefix"
