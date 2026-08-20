#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
PROJECT="${REPO_ROOT}/tools/MacCompatibilitySpike/MacCompatibilitySpike.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet was not found in PATH. Install the .NET 8 SDK first." >&2
  exit 127
fi

echo "Repository: ${REPO_ROOT}"
echo "dotnet:     $(dotnet --version)"
echo

dotnet restore "${PROJECT}"
dotnet run --project "${PROJECT}" --configuration Release --no-restore
