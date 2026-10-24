#!/usr/bin/env bash
# 构建 Windows x64 内置 Python 运行时（含 tqsdk 及其依赖）。
# 产出：runtime/python-win-x64/ （python.exe + Lib/site-packages）
# 用法：bash scripts/build-runtime.sh [目标目录]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="${1:-$ROOT/../runtime/python-win-x64}"
CACHE="$ROOT/build/runtime-cache"
PYVER="3.11.9"
ZIP="$CACHE/python-$PYVER-embed-amd64.zip"
WHEELS="$CACHE/wheels"

mkdir -p "$CACHE" "$WHEELS"

if [ ! -f "$ZIP" ]; then
  echo "[runtime] 下载 CPython $PYVER embeddable ..."
  PRIMARY="https://www.python.org/ftp/python/$PYVER/python-$PYVER-embed-amd64.zip"
  MIRROR="https://mirrors.huaweicloud.com/python/$PYVER/python-$PYVER-embed-amd64.zip"
  if ! curl -fL --retry 2 --max-time 300 -o "$ZIP" "$MIRROR"; then
    echo "[runtime] 镜像下载失败，回退 python.org ..."
    curl -fL --retry 3 --max-time 900 -o "$ZIP" "$PRIMARY"
  fi
fi

echo "[runtime] 解压到 $TARGET"
rm -rf "$TARGET"
mkdir -p "$TARGET"
unzip -qo "$ZIP" -d "$TARGET"

# 启用 site-packages：embeddable 发行版默认不加载 site
PTH="$TARGET/python311._pth"
if [ -f "$PTH" ]; then
  grep -q 'Lib\\site-packages' "$PTH" || printf 'Lib\\site-packages\n' >> "$PTH"
  grep -q '^import site$' "$PTH" || printf 'import site\n' >> "$PTH"
fi

echo "[runtime] 下载 tqsdk 及 Windows x64 轮子 ..."
# ★ 科学计算栈必须钉版本（与 PullStackMeter 的 packaging/requirements-build.txt 一致）：
#   numpy 2.x / pandas 3.x 在 Wine 9.0 下 import 即崩（调到 ucrtbase.dll.crealf），
#   且体积更大、首次导入更容易被杀毒软件/OpenBLAS 初始化拖住；
#   numpy 1.26.4 + pandas 2.2.3 + scipy 1.13.1 与 tqsdk 3.10.2 完全兼容，导入几秒即可就绪。
python3 -m pip download \
  --dest "$WHEELS" \
  --only-binary=:all: \
  --platform win_amd64 \
  --python-version 3.11 \
  --implementation cp \
  --abi cp311 \
  "tqsdk==3.10.2" "numpy==1.26.4" "pandas==2.2.3" "scipy==1.13.1" "pytz" \
  "typing-extensions" "tzdata" >/dev/null

rm -rf "$TARGET/Lib/site-packages"
mkdir -p "$TARGET/Lib/site-packages"

echo "[runtime] 解包轮子 ..."
python3 - "$WHEELS" "$TARGET/Lib/site-packages" <<'PY'
import pathlib
import shutil
import sys
import zipfile

wheels = pathlib.Path(sys.argv[1])
site = pathlib.Path(sys.argv[2])
count = 0
for wheel in sorted(wheels.glob("*.whl")):
    with zipfile.ZipFile(wheel) as zf:
        for info in zf.infolist():
            name = info.filename
            if name.endswith("/"):
                continue
            parts = name.split("/")
            if len(parts) > 2 and parts[1] in ("purelib", "platlib"):
                name = "/".join(parts[2:])
            target = site / name
            target.parent.mkdir(parents=True, exist_ok=True)
            with zf.open(info) as src, open(target, "wb") as dst:
                shutil.copyfileobj(src, dst)
    count += 1
print(f"[runtime] 已解包 {count} 个轮子")
PY

# 删掉各包的 tests 目录：对运行毫无用处，却要多占上千个文件（首次导入被杀毒软件
# 逐个扫描时，文件数比体积更影响启动时间）。
echo "[runtime] 删除测试目录 ..."
find "$TARGET/Lib/site-packages" -type d -name tests -prune -exec rm -rf {} +
find "$TARGET/Lib/site-packages" -type d -name __pycache__ -prune -exec rm -rf {} +

echo "[runtime] 校验依赖闭包 ..."
python3 "$ROOT/scripts/check-runtime.py" "$TARGET"
du -sh "$TARGET"
echo "[runtime] 完成：$TARGET"
