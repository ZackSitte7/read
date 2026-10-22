#!/usr/bin/env bash
# Verify the toolchain, target presence and REA readiness.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck disable=SC1091
. "$ROOT/config/rea/rea.env"

echo "== Git =="
git -C "$ROOT" status -sb
echo
echo "== Toolchain =="
printf 'node       %s\n' "$(node --version)"
printf 'npm        %s\n' "$(npm --version)"
printf 'rea        %s\n' "$(rea --version)"
printf 'java       %s\n' "$(java -version 2>&1 | head -1)"
if [ -x "${GHIDRA_INSTALL_DIR:-}/support/analyzeHeadless" ]; then
  printf 'ghidra     %s\n' "$GHIDRA_INSTALL_DIR"
else
  printf 'ghidra     MISSING (%s)\n' "${GHIDRA_INSTALL_DIR:-<unset>}"
fi
printf 'ps         %s\n' "$(ps --version 2>&1 | head -1)"
echo
echo "== Target =="
if [ -d "$ROOT/target/1102" ]; then
  printf 'path       %s\n' "$ROOT/target/1102"
  printf 'files      %s\n' "$(find "$ROOT/target/1102" -type f | wc -l)"
  printf 'size       %s\n' "$(du -sh "$ROOT/target/1102" | cut -f1)"
  printf 'main exe   %s\n' "$(file -b "$ROOT/target/1102/0931.exe" 2>/dev/null || echo missing)"
else
  echo "target/1102 is missing; run: unzip -q <archive> -d target/"
fi
echo
echo "== REA doctor =="
doctor_json="$(rea doctor --format json 2>/dev/null || true)"
printf '%s' "$doctor_json" | python3 -c '
import json, sys
d = json.load(sys.stdin)
print("healthy:", d.get("healthy"))
for check in d.get("scope_checks", []):
    status = "OK  " if check.get("ok") else "FAIL"
    print("  {} {}: {}".format(status, check.get("name"), check.get("detail") or check.get("classification")))
'
