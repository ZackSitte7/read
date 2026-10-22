#!/usr/bin/env bash
# Build a reproducible inventory for the target directory:
#   artifacts/manifests/source-archive.sha256
#   artifacts/manifests/target-files.sha256
#   artifacts/manifests/target-files.txt
#   artifacts/manifests/target-inventory.json
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="$ROOT/target/1102"
OUT="$ROOT/artifacts/manifests"
ARCHIVE="${1:-$(ls -t "$ROOT"/.uploads/*.zip 2>/dev/null | head -1)}"

if [ ! -d "$TARGET" ]; then
  echo "error: $TARGET not found; extract the source archive first" >&2
  exit 1
fi

mkdir -p "$OUT"

if [ -n "${ARCHIVE:-}" ] && [ -f "$ARCHIVE" ]; then
  (cd "$(dirname "$ARCHIVE")" && sha256sum "$(basename "$ARCHIVE")") > "$OUT/source-archive.sha256"
  echo "archive: $OUT/source-archive.sha256"
fi

(cd "$TARGET" && find . -type f -print0 | sort -z | xargs -0 sha256sum) > "$OUT/target-files.sha256"
(cd "$TARGET" && find . -type f -printf '%10s  %P\n' | sort -k2) > "$OUT/target-files.txt"

python3 - "$TARGET" "$OUT/target-inventory.json" <<'PY'
import collections
import hashlib
import json
import os
import sys

target, output = sys.argv[1], sys.argv[2]
by_ext = collections.Counter()
by_ext_bytes = collections.Counter()
total_bytes = 0
files = 0
for base, _, names in os.walk(target):
    for name in names:
        path = os.path.join(base, name)
        size = os.path.getsize(path)
        ext = os.path.splitext(name)[1].lower() or "<none>"
        files += 1
        total_bytes += size
        by_ext[ext] += 1
        by_ext_bytes[ext] += size

archive = None
archive_manifest = os.path.join(os.path.dirname(output), "source-archive.sha256")
if os.path.exists(archive_manifest):
    with open(archive_manifest, encoding="utf-8") as handle:
        archive = handle.read().strip()

payload = {
    "target": os.path.basename(target),
    "files": files,
    "bytes": total_bytes,
    "source_archive": archive,
    "by_extension": {
        ext: {"files": by_ext[ext], "bytes": by_ext_bytes[ext]}
        for ext in sorted(by_ext, key=lambda item: (-by_ext[item], item))
    },
}
with open(output, "w", encoding="utf-8") as handle:
    json.dump(payload, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
print(f"inventory: {output} ({files} files, {total_bytes} bytes)")
PY

printf 'sha256:    %s lines\n' "$(wc -l < "$OUT/target-files.sha256")"
