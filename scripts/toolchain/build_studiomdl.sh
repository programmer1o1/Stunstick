#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mdlforge_root="$repo_root/tools/MDLForge"
install_prefix="$repo_root/tools/studiomdl"

if [[ ! -d "$mdlforge_root" ]]; then
  echo "MDLForge not found: $mdlforge_root" >&2
  exit 1
fi

if ! command -v cmake >/dev/null 2>&1; then
  echo "cmake not found on PATH." >&2
  exit 1
fi

build_type="${CMAKE_BUILD_TYPE:-Release}"
build_i686="${STUDIOMDL_BUILD_I686:-OFF}"

export CCACHE_DISABLE="${CCACHE_DISABLE:-1}"

build_dir="${STUDIOMDL_BUILD_DIR:-}"
cleanup_build_dir=0
if [[ -z "$build_dir" ]]; then
  build_dir="$(mktemp -d -t mdlforge-build-XXXXXXXX)"
  cleanup_build_dir=1
fi

build_dir_i686="${STUDIOMDL_BUILD_DIR_I686:-}"
cleanup_build_dir_i686=0
if [[ -z "$build_dir_i686" ]]; then
  build_dir_i686="$(mktemp -d -t mdlforge-build-i686-XXXXXXXX)"
  cleanup_build_dir_i686=1
fi

cleanup() {
  if [[ "$cleanup_build_dir" == "1" ]]; then
    rm -rf "$build_dir"
  fi
  if [[ "$cleanup_build_dir_i686" == "1" ]]; then
    rm -rf "$build_dir_i686"
  fi
}
trap cleanup EXIT

cmake -S "$mdlforge_root" -B "$build_dir" \
  -DCMAKE_BUILD_TYPE="$build_type" \
  -DSTUDIOMDL_REQUIRE_32BIT=OFF

cmake --build "$build_dir" --parallel --config "$build_type"
cmake --install "$build_dir" --prefix "$install_prefix" --config "$build_type"

if [[ "$build_i686" == "1" || "$build_i686" == "ON" || "$build_i686" == "on" || "$build_i686" == "true" ]]; then
  cmake -S "$mdlforge_root" -B "$build_dir_i686" \
    -DCMAKE_BUILD_TYPE="$build_type" \
    -DSTUDIOMDL_REQUIRE_32BIT=ON \
    -DCMAKE_CXX_FLAGS="-m32 -msse -msse2 -mfpmath=sse" \
    -DCMAKE_EXE_LINKER_FLAGS="-m32"

  cmake --build "$build_dir_i686" --parallel --config "$build_type"

  mkdir -p "$install_prefix/bin"
  cp -f "$build_dir_i686/utils/studiomdl/studiomdl" "$install_prefix/bin/studiomdl_i686"
  echo "Installed i686 studiomdl to: $install_prefix/bin/studiomdl_i686"
fi

echo "Installed studiomdl to: $install_prefix"
