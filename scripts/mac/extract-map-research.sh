#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: bash scripts/mac/extract-map-research.sh <map.w3x|map.w3m> [output-directory]" >&2
  exit 2
fi

MAP_PATH="$1"
if [[ ! -f "${MAP_PATH}" ]]; then
  echo "ERROR: map does not exist: ${MAP_PATH}" >&2
  exit 2
fi

MAP_BASENAME="$(basename -- "${MAP_PATH}")"
MAP_STEM="${MAP_BASENAME%.*}"
OUTPUT_DIR="${2:-${REPO_ROOT}/mac-port-results/${MAP_STEM}-research}"

mkdir -p "${OUTPUT_DIR}"

bash "${SCRIPT_DIR}/run-stormlib-spike.sh" \
  "${MAP_PATH}" \
  --extract \
  "${OUTPUT_DIR}"

echo
echo "Research package: ${OUTPUT_DIR}"
echo "Manifest:         ${OUTPUT_DIR}/metadata/manifest.json"
echo "Script candidates:${OUTPUT_DIR}/metadata/script-candidates.txt"
echo "Object data:      ${OUTPUT_DIR}/metadata/object-data-files.txt"
