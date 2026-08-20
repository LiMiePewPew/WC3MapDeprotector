#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
PROJECT="${REPO_ROOT}/tools/StormLibMacSpike/StormLibMacSpike.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet was not found in PATH. Install/use the .NET 8 SDK first." >&2
  exit 127
fi

"${SCRIPT_DIR}/build-stormlib.sh"

case "$(uname -m)" in
  arm64)
    RID="osx-arm64"
    ;;
  x86_64)
    RID="osx-x64"
    ;;
  *)
    echo "ERROR: unsupported macOS architecture: $(uname -m)" >&2
    exit 2
    ;;
esac

STORMLIB_PATH="${REPO_ROOT}/native/${RID}/libstorm.dylib"

if [[ ! -f "${STORMLIB_PATH}" ]]; then
  echo "ERROR: StormLib build did not produce ${STORMLIB_PATH}" >&2
  exit 3
fi

echo
echo "Repository: ${REPO_ROOT}"
echo "dotnet:     $(dotnet --version)"
echo "StormLib:   ${STORMLIB_PATH}"
echo

dotnet restore "${PROJECT}"
WC3_STORMLIB_PATH="${STORMLIB_PATH}" \
  dotnet run --project "${PROJECT}" --configuration Release --no-restore -- "$@"
