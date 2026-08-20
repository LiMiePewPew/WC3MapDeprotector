#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
LUA_VERSION="5.3.6"
LUA_SHA256="fc5fd69bb8736323f026672b1b7235da613d7177e72558893a0bdcd320466d60"
CACHE_ROOT="${REPO_ROOT}/.cache/mac-native"
TARBALL="${CACHE_ROOT}/lua-${LUA_VERSION}.tar.gz"
SOURCE_DIR="${CACHE_ROOT}/lua-${LUA_VERSION}"

case "$(uname -m)" in
  arm64)
    RID="osx-arm64"
    EXPECTED_ARCH="arm64"
    ;;
  x86_64)
    RID="osx-x64"
    EXPECTED_ARCH="x86_64"
    ;;
  *)
    echo "ERROR: unsupported macOS architecture: $(uname -m)" >&2
    exit 2
    ;;
esac

OUTPUT_DIR="${REPO_ROOT}/tools/MacCompatibilitySpike/bin/Release/net8.0/runtimes/${RID}/native"
OUTPUT_LIB="${OUTPUT_DIR}/liblua53.dylib"

mkdir -p "${CACHE_ROOT}" "${OUTPUT_DIR}"

if [[ -f "${OUTPUT_LIB}" ]] && file "${OUTPUT_LIB}" | grep -q "${EXPECTED_ARCH}"; then
  echo "Lua 5.3 native library already available: ${OUTPUT_LIB}"
  exit 0
fi

if [[ ! -f "${TARBALL}" ]]; then
  echo "Downloading Lua ${LUA_VERSION} source..."
  curl --fail --location --silent --show-error \
    "https://www.lua.org/ftp/lua-${LUA_VERSION}.tar.gz" \
    --output "${TARBALL}"
fi

ACTUAL_SHA256="$(shasum -a 256 "${TARBALL}" | awk '{print $1}')"
if [[ "${ACTUAL_SHA256}" != "${LUA_SHA256}" ]]; then
  echo "ERROR: Lua source checksum mismatch." >&2
  echo "Expected: ${LUA_SHA256}" >&2
  echo "Actual:   ${ACTUAL_SHA256}" >&2
  exit 3
fi

rm -rf "${SOURCE_DIR}"
tar -xzf "${TARBALL}" -C "${CACHE_ROOT}"

SOURCES=()
for source in "${SOURCE_DIR}"/src/*.c; do
  case "$(basename "${source}")" in
    lua.c|luac.c)
      continue
      ;;
  esac
  SOURCES+=("${source}")
done

echo "Building Lua ${LUA_VERSION} for ${RID}..."
cc \
  -O2 \
  -fPIC \
  -DLUA_USE_MACOSX \
  -dynamiclib \
  -Wl,-install_name,@rpath/liblua53.dylib \
  -o "${OUTPUT_LIB}" \
  "${SOURCES[@]}" \
  -lm

if ! file "${OUTPUT_LIB}" | grep -q "${EXPECTED_ARCH}"; then
  echo "ERROR: built Lua library has unexpected architecture:" >&2
  file "${OUTPUT_LIB}" >&2
  exit 4
fi

echo "Built: ${OUTPUT_LIB}"
file "${OUTPUT_LIB}"
