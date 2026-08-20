#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"

STORMLIB_COMMIT="c430a0c7ffc13b5d8fdaf0d7574be9e826a890af"
STORMLIB_VERSION="9.30.0"
CACHE_ROOT="${REPO_ROOT}/.cache/mac-native"
SOURCE_DIR="${CACHE_ROOT}/StormLib-${STORMLIB_COMMIT}"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "ERROR: this build script is intended for macOS." >&2
  exit 2
fi

case "$(uname -m)" in
  arm64)
    RID="osx-arm64"
    ARCH="arm64"
    ;;
  x86_64)
    RID="osx-x64"
    ARCH="x86_64"
    ;;
  *)
    echo "ERROR: unsupported macOS architecture: $(uname -m)" >&2
    exit 2
    ;;
esac

for tool in git cmake file; do
  if ! command -v "${tool}" >/dev/null 2>&1; then
    echo "ERROR: ${tool} was not found in PATH." >&2
    if [[ "${tool}" == "cmake" ]]; then
      echo "Install it with: brew install cmake" >&2
    fi
    exit 127
  fi
done

BUILD_DIR="${CACHE_ROOT}/StormLib-build-${RID}-${STORMLIB_COMMIT:0:12}"
OUTPUT_DIR="${REPO_ROOT}/native/${RID}"
OUTPUT_LIB="${OUTPUT_DIR}/libstorm.dylib"

mkdir -p "${CACHE_ROOT}" "${OUTPUT_DIR}"

if [[ -f "${OUTPUT_LIB}" ]] && file "${OUTPUT_LIB}" | grep -q "${ARCH}"; then
  echo "StormLib ${STORMLIB_VERSION} already available: ${OUTPUT_LIB}"
  file "${OUTPUT_LIB}"
  exit 0
fi

if [[ ! -d "${SOURCE_DIR}/.git" ]]; then
  echo "Fetching StormLib ${STORMLIB_VERSION} (${STORMLIB_COMMIT:0:12})..."
  rm -rf "${SOURCE_DIR}"
  mkdir -p "${SOURCE_DIR}"
  git -C "${SOURCE_DIR}" init -q
  git -C "${SOURCE_DIR}" remote add origin https://github.com/ladislav-zezula/StormLib.git
  git -C "${SOURCE_DIR}" fetch --depth 1 origin "${STORMLIB_COMMIT}"
  git -C "${SOURCE_DIR}" checkout --detach -q FETCH_HEAD
fi

ACTUAL_COMMIT="$(git -C "${SOURCE_DIR}" rev-parse HEAD)"
if [[ "${ACTUAL_COMMIT}" != "${STORMLIB_COMMIT}" ]]; then
  echo "ERROR: cached StormLib source is at ${ACTUAL_COMMIT}, expected ${STORMLIB_COMMIT}." >&2
  echo "Delete ${SOURCE_DIR} and retry." >&2
  exit 3
fi

rm -rf "${BUILD_DIR}"

cmake \
  -S "${SOURCE_DIR}" \
  -B "${BUILD_DIR}" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_ARCHITECTURES="${ARCH}" \
  -DBUILD_SHARED_LIBS=ON \
  -DSTORM_USE_BUNDLED_LIBRARIES=ON \
  -DSTORM_SKIP_INSTALL=ON \
  -DSTORM_BUILD_TESTS=OFF

cmake --build "${BUILD_DIR}" --config Release --parallel

STORM_BINARY=""
while IFS= read -r candidate; do
  STORM_BINARY="${candidate}"
  break
done < <(find "${BUILD_DIR}" -type f \( -path "*/storm.framework/storm" -o -path "*/storm.framework/Versions/*/storm" -o -name "libstorm*.dylib" \) | sort)

if [[ -z "${STORM_BINARY}" || ! -f "${STORM_BINARY}" ]]; then
  echo "ERROR: StormLib build completed but no shared library/framework binary was found." >&2
  find "${BUILD_DIR}" -maxdepth 5 -type f -name '*storm*' -print >&2 || true
  exit 4
fi

cp "${STORM_BINARY}" "${OUTPUT_LIB}"
chmod 755 "${OUTPUT_LIB}"

if command -v install_name_tool >/dev/null 2>&1; then
  install_name_tool -id "@rpath/libstorm.dylib" "${OUTPUT_LIB}" || true
fi

if ! file "${OUTPUT_LIB}" | grep -q "${ARCH}"; then
  echo "ERROR: built StormLib has unexpected architecture:" >&2
  file "${OUTPUT_LIB}" >&2
  exit 5
fi

echo
echo "Built StormLib ${STORMLIB_VERSION}: ${OUTPUT_LIB}"
file "${OUTPUT_LIB}"
if command -v otool >/dev/null 2>&1; then
  echo
otool -L "${OUTPUT_LIB}"
fi
