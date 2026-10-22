#!/usr/bin/env python3
"""Dump Delphi DFM (TPF0) form resources from a PE file.

Usage:
    python3 scripts/dfm-dump.py target/1102/0931.exe [--out /tmp/dfm]
"""

from __future__ import annotations

import argparse
import os
import struct
import sys

import pefile


VA_NULL = 0
VA_LIST = 1
VA_INT8 = 2
VA_INT16 = 3
VA_INT32 = 4
VA_EXTENDED = 5
VA_STRING = 6
VA_IDENT = 7
VA_FALSE = 8
VA_TRUE = 9
VA_BINARY = 10
VA_SET = 11
VA_SINGLE = 12
VA_CURRENCY = 13
VA_COLLECTION = 14
VA_DATE = 15
VA_WSTRING = 16
VA_INT64 = 17
VA_UTF16 = 18
VA_DOUBLE = 19
VA_UTF8STRING = 20


class Reader:
    def __init__(self, data: bytes) -> None:
        self.data = data
        self.pos = 0

    def eof(self) -> bool:
        return self.pos >= len(self.data)

    def peek(self) -> int:
        if self.eof():
            raise EOFError("unexpected end of DFM")
        return self.data[self.pos]

    def u8(self) -> int:
        value = self.peek()
        self.pos += 1
        return value

    def i8(self) -> int:
        return struct.unpack("<b", bytes([self.u8()]))[0]

    def i16(self) -> int:
        value = struct.unpack("<h", self.data[self.pos : self.pos + 2])[0]
        self.pos += 2
        return value

    def i32(self) -> int:
        value = struct.unpack("<i", self.data[self.pos : self.pos + 4])[0]
        self.pos += 4
        return value

    def i64(self) -> int:
        value = struct.unpack("<q", self.data[self.pos : self.pos + 8])[0]
        self.pos += 8
        return value

    def take(self, size: int) -> bytes:
        value = self.data[self.pos : self.pos + size]
        if len(value) != size:
            raise EOFError("unexpected end of DFM")
        self.pos += size
        return value

    def short_string(self) -> str:
        size = self.u8()
        return self.take(size).decode("latin1")

    def unicode_string(self) -> str:
        # In this sample the Unicode tag stores a 32-bit character count
        # followed by UTF-16LE data (verified on Chinese font/caption values).
        count = self.i32()
        raw = self.take(count * 2)
        return raw.decode("utf-16le", "replace")

    def utf8_string(self) -> str:
        size = self.i32()
        raw = self.take(size)
        try:
            return raw.decode("utf-8")
        except UnicodeDecodeError:
            return raw.decode("gb18030", "replace")


def read_extended(reader: Reader) -> float:
    raw = reader.take(10)
    # 80-bit extended: 64-bit mantissa + 16-bit sign/exponent.
    mantissa, se = struct.unpack("<QH", raw)
    sign = -1 if se & 0x8000 else 1
    exponent = se & 0x7FFF
    if exponent == 0 and mantissa == 0:
        return 0.0
    return sign * (mantissa / (1 << 63)) * (2.0 ** (exponent - 16383))


def read_value(reader: Reader, tag: int):
    if tag == VA_NULL:
        return None
    if tag == VA_LIST:
        items = []
        while True:
            item_tag = reader.u8()
            if item_tag == VA_NULL:
                return items
            items.append(read_value(reader, item_tag))
    if tag == VA_INT8:
        return reader.i8()
    if tag == VA_INT16:
        return reader.i16()
    if tag == VA_INT32:
        return reader.i32()
    if tag == VA_INT64:
        return reader.i64()
    if tag == VA_EXTENDED:
        return read_extended(reader)
    if tag == VA_SINGLE:
        return struct.unpack("<f", reader.take(4))[0]
    if tag == VA_DOUBLE:
        return struct.unpack("<d", reader.take(8))[0]
    if tag == VA_CURRENCY:
        return reader.i64() / 10000.0
    if tag == VA_DATE:
        return struct.unpack("<d", reader.take(8))[0]
    if tag in (VA_STRING, VA_IDENT):
        return reader.short_string()
    if tag in (VA_WSTRING, VA_UTF16):
        return reader.unicode_string()
    if tag == VA_UTF8STRING:
        return reader.utf8_string()
    if tag == VA_FALSE:
        return False
    if tag == VA_TRUE:
        return True
    if tag == VA_BINARY:
        size = reader.i32()
        reader.take(size)
        return f"<binary {size} bytes>"
    if tag == VA_SET:
        values = []
        while True:
            value = reader.short_string()
            if value == "":
                return values
            values.append(value)
    if tag == VA_COLLECTION:
        items = []
        while True:
            item_tag = reader.u8()
            if item_tag == VA_NULL:
                return items
            index = None
            if item_tag == VA_INT8:
                index = reader.i8()
                item_tag = reader.u8()
            if item_tag != VA_LIST:
                raise ValueError(f"unexpected collection item tag {item_tag:#x}")
            properties = read_properties(reader)
            items.append({"index": index, "properties": properties})
    raise ValueError(f"unsupported DFM value tag {tag:#x} at {reader.pos:#x}")


def read_properties(reader: Reader) -> dict:
    properties = {}
    while True:
        name = reader.short_string()
        if name == "":
            return properties
        tag = reader.u8()
        properties[name] = read_value(reader, tag)


def read_object(reader: Reader) -> dict:
    # Delphi emits an object prefix only when flags are present (0xFF + flags).
    if reader.peek() == 0xFF:
        reader.u8()
        reader.u8()  # object flags
    class_name = reader.short_string()
    name = reader.short_string()
    properties = read_properties(reader)
    children = []
    while True:
        marker = reader.peek()
        if marker == VA_NULL:
            reader.u8()
            break
        children.append(read_object(reader))
    return {"class": class_name, "name": name, "properties": properties, "children": children}


def format_value(value) -> str:
    if isinstance(value, list):
        return "[" + ", ".join(format_value(item) for item in value) + "]"
    if isinstance(value, bool):
        return "True" if value else "False"
    if isinstance(value, float):
        return f"{value:g}"
    if isinstance(value, str):
        escaped = value.replace("\r", "\\r").replace("\n", "\\n")
        return f"'{escaped}'"
    if isinstance(value, dict):
        body = ", ".join(f"{key}={format_value(val)}" for key, val in value["properties"].items())
        prefix = f"#{value['index']} " if value["index"] is not None else ""
        return "{" + prefix + body + "}"
    return str(value)


def dump_object(obj: dict, depth: int, lines: list[str]) -> None:
    indent = "  " * depth
    lines.append(f"{indent}{obj['class']} {obj['name']}")
    for key, value in obj["properties"].items():
        lines.append(f"{indent}  .{key} = {format_value(value)}")
    for child in obj["children"]:
        dump_object(child, depth + 1, lines)


def iter_rcdata(pe: pefile.PE):
    if not hasattr(pe, "DIRECTORY_ENTRY_RESOURCE"):
        return
    for type_entry in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        type_id = type_entry.id
        type_name = str(type_entry.name) if type_entry.name else None
        if type_id != 10 and type_name != "RCDATA":
            continue
        for name_entry in type_entry.directory.entries:
            resource_name = (
                str(name_entry.name) if name_entry.name else f"#{name_entry.id}"
            )
            for lang_entry in name_entry.directory.entries:
                data = lang_entry.data.struct
                blob = pe.get_data(data.OffsetToData, data.Size)
                yield resource_name, blob


def decode_blob_name(name: str) -> str:
    # Delphi compiles resource names as UTF-16LE bytes.
    return name.encode("latin1", "ignore").decode("utf-16le", "ignore") if name else name


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("pe_file")
    parser.add_argument("--out", default="/tmp/dfm")
    args = parser.parse_args()

    pe = pefile.PE(args.pe_file, fast_load=False)
    os.makedirs(args.out, exist_ok=True)
    found = 0
    for resource_name, blob in iter_rcdata(pe):
        if len(blob) < 4 or blob[:4] != b"TPF0":
            continue
        reader = Reader(blob[4:])
        try:
            root = read_object(reader)
        except Exception as exc:  # noqa: BLE001 - report and continue
            print(f"FAIL {resource_name}: {exc}", file=sys.stderr)
            continue

        safe_name = "".join(ch if ch.isalnum() or ch in "-_." else "_" for ch in resource_name)
        lines = [f"# resource: {resource_name} ({len(blob)} bytes)", ""]
        dump_object(root, 0, lines)
        path = os.path.join(args.out, f"{safe_name}.txt")
        with open(path, "w", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
        print(f"{resource_name}: {root['class']} -> {path}")
        found += 1

    print(f"dumped {found} form(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
