#!/usr/bin/env bash
# Converts the GEDCOM and builds the site into public/.
#   ./build.sh path/to/tree.ged          build once
#   ./build.sh path/to/tree.ged serve    build, then preview at http://127.0.0.1:1313
set -euo pipefail
cd "$(dirname "$0")"

GEDCOM="${1:?usage: ./build.sh <tree.ged> [serve]}"
TOOL="${GEDCOM_HUGO_PROJECT:-../src/GedcomHugo}"   # path to the converter project

dotnet run --project "$TOOL" -c Release -- \
  --gedcom "$GEDCOM" --out _generated --overrides overrides.txt

# Hugo never deletes pages it didn't render this time, so a person withheld since
# the last build would otherwise linger in public/. Always start from empty.
rm -rf public

if [[ "${2:-}" == "serve" ]]; then
  exec hugo server --bind 127.0.0.1
else
  hugo --minify --gc
fi
