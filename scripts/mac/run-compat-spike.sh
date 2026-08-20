#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
PROJECT="${REPO_ROOT}/tools/MacCompatibilitySpike/MacCompatibilitySpike.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet was not found in PATH. Install the .NET 8 SDK first." >&2
  exit 127
fi

DOTNET_VERSION="$(dotnet --version)"
if [[ "${DOTNET_VERSION}" != 8.* ]]; then
  echo "ERROR: this spike must run with the .NET 8 SDK. Current version: ${DOTNET_VERSION}" >&2
  echo "If dotnet@8 is installed with Homebrew, run:" >&2
  echo '  export DOTNET_ROOT="$(brew --prefix dotnet@8)/libexec"' >&2
  echo '  export PATH="$(brew --prefix dotnet@8)/bin:$PATH"' >&2
  exit 126
fi

echo "Repository: ${REPO_ROOT}"
echo "dotnet:     ${DOTNET_VERSION}"
echo

dotnet restore "${PROJECT}"
dotnet build "${PROJECT}" --configuration Release --no-restore
bash "${SCRIPT_DIR}/build-lua53.sh"
dotnet run --project "${PROJECT}" --configuration Release --no-build --no-restore
