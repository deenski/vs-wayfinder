#!/usr/bin/env python3
"""Fail unless the Release zip contains only the mod files."""

from __future__ import annotations

import glob
import os
import sys
import zipfile

EXPECTED = ["Wayfinding.Pathing.dll", "Wayfinding.dll", "modinfo.json"]


def main() -> None:
    paths = sys.argv[1:] or sorted(glob.glob("Releases/wayfinding_*.zip"))
    if len(paths) != 1:
        sys.exit(f"expected exactly one mod zip, found {paths}")
    path = paths[0]
    name = os.path.basename(path)
    if not name.startswith("wayfinding_") or not name.endswith(".zip"):
        sys.exit(f"unexpected zip name: {path}")
    with zipfile.ZipFile(path) as archive:
        names = sorted(
            info.filename.replace("\\", "/").rstrip("/")
            for info in archive.infolist()
            if not info.is_dir()
        )
    if names != EXPECTED:
        sys.exit(f"{path} entries {names} != {EXPECTED}")
    print(path)


if __name__ == "__main__":
    main()
