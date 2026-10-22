#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""校验内置 Windows Python 运行时的第三方依赖是否闭合。

发布包里的 site-packages 是手工解包 wheel 得到的，容易漏装传递依赖
（例如 aiohttp 在 Python < 3.13 上依赖 typing_extensions）。
本脚本按 Windows / CPython 3.11 的环境标记计算依赖闭包，缺失时以非 0 退出。

用法：python3 scripts/check-runtime.py [runtime/python-win-x64]
"""

from __future__ import annotations

import pathlib
import sys

try:
    from packaging.markers import default_environment
    from packaging.requirements import Requirement
except ImportError:  # pragma: no cover - 构建机通常已安装 packaging
    print("需要 packaging：python3 -m pip install packaging", file=sys.stderr)
    raise SystemExit(2)


def environment() -> dict[str, str]:
    env = default_environment()
    env.update(
        {
            "sys_platform": "win32",
            "platform_system": "Windows",
            "os_name": "nt",
            "python_version": "3.11",
            "python_full_version": "3.11.9",
            "platform_machine": "AMD64",
            "implementation_name": "cpython",
            "platform_python_implementation": "CPython",
        }
    )
    return env


def installed(site: pathlib.Path) -> set[str]:
    names: set[str] = set()
    for entry in site.iterdir():
        if entry.is_dir() and entry.name.endswith(".dist-info"):
            names.add(entry.name.split("-")[0].lower().replace("_", "-"))
        elif entry.suffix == ".py":
            names.add(entry.stem.lower().replace("_", "-"))
    return names


def main() -> int:
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "runtime/python-win-x64")
    site = root / "Lib" / "site-packages"
    if not site.is_dir():
        print(f"未找到 {site}", file=sys.stderr)
        return 2

    have = installed(site)
    env = environment()
    missing: list[str] = []

    for metadata in site.glob("*.dist-info/METADATA"):
        package = metadata.parent.name.split("-")[0].lower().replace("_", "-")
        text = metadata.read_text(encoding="utf-8", errors="replace")
        for line in text.splitlines():
            if not line.lower().startswith("requires-dist:"):
                continue
            raw = line.split(":", 1)[1].strip()
            try:
                requirement = Requirement(raw)
            except Exception:
                continue
            if requirement.marker is not None and not requirement.marker.evaluate(env):
                continue
            name = requirement.name.lower().replace("_", "-")
            if name not in have:
                missing.append(f"{raw}  ← {package}")

    if missing:
        print("内置运行时缺少依赖：", file=sys.stderr)
        for item in sorted(missing):
            print(f"  {item}", file=sys.stderr)
        return 1

    print(f"内置运行时依赖完整（{len(have)} 个包）。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
