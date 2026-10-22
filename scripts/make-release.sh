#!/usr/bin/env bash
# 生成 Windows x64 发布包：自包含 exe + 内置 Python 运行时（含 tqsdk）。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/release/Replica-Desktop-win-x64"
RUNTIME="$ROOT/runtime/python-win-x64"

rm -rf "$OUT"
dotnet publish "$ROOT/src/Replica.Desktop/Replica.Desktop.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUT" --nologo

if [ -d "$RUNTIME" ]; then
  echo "校验内置 Python 运行时依赖..."
  python3 "$ROOT/scripts/check-runtime.py" "$RUNTIME"
  echo "复制内置 Python 运行时..."
  cp -a "$RUNTIME" "$OUT/python"
else
  echo "警告：未找到 $RUNTIME，发布包将依赖目标机 Python。" >&2
fi

cd "$ROOT/release"
rm -f Replica-Desktop-win-x64.zip
zip -qr Replica-Desktop-win-x64.zip Replica-Desktop-win-x64
echo "完成：$ROOT/release/Replica-Desktop-win-x64.zip"
