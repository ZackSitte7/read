#!/usr/bin/env python3
"""Targeted PE string/xref scanner for Delphi x86 samples.

Usage:
  pe-xref.py <binary> --strings <regex>          # list matching strings with VA
  pe-xref.py <binary> --xref <hex-va>            # disassemble code referencing VA
"""
import argparse
import re
import struct
import sys

import capstone
import pefile


def load(path):
    pe = pefile.PE(path, fast_load=True)
    return pe


def iter_sections(pe):
    for s in pe.sections:
        name = s.Name.rstrip(b"\x00").decode("latin1")
        yield name, s.VirtualAddress, s.Misc_VirtualSize, s.get_data(), s.PointerToRawData


def va_to_off(pe, va):
    for s in pe.sections:
        start = pe.OPTIONAL_HEADER.ImageBase + s.VirtualAddress
        end = start + max(s.Misc_VirtualSize, s.SizeOfRawData)
        if start <= va < end:
            return s.PointerToRawData + (va - start)
    return None


def scan_strings(pe, pattern):
    rx = re.compile(pattern, re.I)
    out = []
    base = pe.OPTIONAL_HEADER.ImageBase
    for name, va, vsize, data, _ in iter_sections(pe):
        # UTF-16LE: decode contiguous printable wide chars
        i = 0
        n = len(data)
        while i < n - 1:
            # try wide string starting at i
            j = i
            chars = []
            while j + 1 < n:
                lo, hi = data[j], data[j + 1]
                if hi == 0 and 0x20 <= lo < 0x7F:
                    chars.append(chr(lo))
                    j += 2
                elif hi == 0 and lo == 0:
                    break
                else:
                    break
            if len(chars) >= 4:
                text = "".join(chars)
                if rx.search(text):
                    out.append(
                        {
                            "encoding": "utf16le",
                            "va": base + va + i,
                            "section": name,
                            "text": text[:300],
                        }
                    )
                i = j
                continue
            i += 1
        # ASCII strings
        for m in re.finditer(rb"[\x20-\x7e]{4,}", data):
            text = m.group().decode("latin1")
            if rx.search(text):
                out.append(
                    {
                        "encoding": "ascii",
                        "va": base + va + m.start(),
                        "section": name,
                        "text": text[:300],
                    }
                )
    # de-dup identical va
    seen = set()
    uniq = []
    for s in out:
        k = (s["va"], s["text"])
        if k in seen:
            continue
        seen.add(k)
        uniq.append(s)
    return uniq


def find_xrefs(pe, target_va):
    """Find code locations containing a 32-bit immediate equal to target_va."""
    base = pe.OPTIONAL_HEADER.ImageBase
    hits = []
    for name, va, vsize, data, _ in iter_sections(pe):
        if not (name.startswith(".text") or name.startswith("CODE")):
            continue
        needle = struct.pack("<I", target_va)
        start = 0
        while True:
            idx = data.find(needle, start)
            if idx < 0:
                break
            hits.append({"section": name, "va": base + va + idx})
            start = idx + 1
    return hits


def disasm_window(pe, va, before=96, after=160):
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    off = va_to_off(pe, va)
    if off is None:
        return []
    data = pe.get_memory_mapped_image()
    eax = va - pe.OPTIONAL_HEADER.ImageBase
    start = max(0, eax - before)
    code = data[start : eax + after]
    lines = []
    for ins in md.disasm(code, pe.OPTIONAL_HEADER.ImageBase + start):
        marker = ">>" if ins.address == va else "  "
        lines.append(
            f"{marker} 0x{ins.address:08x}: {ins.mnemonic:<8} {ins.op_str}"
        )
    return lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("binary")
    ap.add_argument("--strings", default=None)
    ap.add_argument("--xref", default=None, help="hex VA to locate xrefs for")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args()

    pe = load(args.binary)
    if args.strings:
        for s in scan_strings(pe, args.strings):
            print(f"{s['encoding']:<8} 0x{s['va']:08x} [{s['section']}] {s['text']}")
    if args.xref:
        target = int(args.xref, 16)
        hits = find_xrefs(pe, target)
        print(f"# xrefs to 0x{target:08x}: {len(hits)}")
        for h in hits:
            print(f"\n=== xref at 0x{h['va']:08x} [{h['section']}] ===")
            for line in disasm_window(pe, h["va"]):
                print(line)


if __name__ == "__main__":
    sys.exit(main())
