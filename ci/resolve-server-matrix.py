#!/usr/bin/env python3
"""Resolve official Vintage Story linux server downloads for CI.

The stable version comes from ci/vs-version.txt. Its MD5 is pinned for 1.22.7.
The unstable leg is the current linux server in unstable.json, when one exists.
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.request
from pathlib import Path

STABLE_URL = "https://api.vintagestory.at/stable.json"
UNSTABLE_URL = "https://api.vintagestory.at/unstable.json"
CDN_PREFIX = "https://cdn.vintagestory.at/gamefiles/"
PINNED_MD5 = {
    "1.22.7": "3031212903b1c283084d5fe0cda6bce6",
}
FALLBACK_STABLE = "1.22.7"


def fetch(url: str) -> dict:
    with urllib.request.urlopen(url, timeout=60) as response:
        data = json.load(response)
    if not isinstance(data, dict):
        raise SystemExit(f"{url} was not a JSON object")
    return data


def version_file() -> str:
    path = Path(__file__).resolve().parent / "vs-version.txt"
    if not path.is_file():
        return FALLBACK_STABLE
    version = path.read_text(encoding="utf-8").strip()
    return version or FALLBACK_STABLE


def linux_server(entry: object) -> dict | None:
    if not isinstance(entry, dict):
        return None
    server = entry.get("linuxserver")
    if not isinstance(server, dict):
        return None
    urls = server.get("urls")
    if not isinstance(urls, dict):
        return None
    cdn = urls.get("cdn")
    md5 = server.get("md5")
    if not isinstance(cdn, str) or not isinstance(md5, str):
        return None
    if not cdn.startswith(CDN_PREFIX):
        raise SystemExit(f"linux server URL is not on the official CDN: {cdn}")
    if len(md5) != 32 or any(char not in "0123456789abcdefABCDEF" for char in md5):
        raise SystemExit(f"linux server MD5 is not 32 hex characters: {md5}")
    return {"url": cdn, "md5": md5.lower(), "latest": server.get("latest") == 1}


def stable_entry(catalog: dict, version: str) -> dict:
    entry = catalog.get(version)
    if entry is None:
        raise SystemExit(f"stable.json has no {version} entry")
    server = linux_server(entry)
    if server is None:
        raise SystemExit(f"stable.json {version} has no linux server entry")
    pinned = PINNED_MD5.get(version)
    if pinned is not None and server["md5"] != pinned.lower():
        raise SystemExit(
            f"stable.json MD5 for {version} is {server['md5']}, expected {pinned}"
        )
    return {"version": version, "md5": server["md5"], "url": server["url"]}


def current_unstable(catalog: dict) -> dict | None:
    latest: list[dict] = []
    found: list[dict] = []
    for version, entry in catalog.items():
        server = linux_server(entry)
        if server is None:
            continue
        row = {"version": version, "md5": server["md5"], "url": server["url"]}
        found.append(row)
        if server["latest"]:
            latest.append(row)
    if latest:
        return latest[0]
    if found:
        return found[0]
    return None


def emit_env(row: dict) -> None:
    for key in ("version", "md5", "url"):
        value = row[key]
        if any(char in value for char in "\n\r"):
            raise SystemExit(f"unsafe {key}")
        print(f"{key}={value}")


def matrix(stable: dict, unstable: dict | None) -> dict:
    include = [
        {
            "channel": "stable",
            "check_name": "build-and-load",
            "version": stable["version"],
            "md5": stable["md5"],
            "url": stable["url"],
            "use_ghcr": True,
            "continue_on_error": False,
        }
    ]
    if unstable is not None:
        include.append(
            {
                "channel": "unstable",
                "check_name": "build-and-load (unstable)",
                "version": unstable["version"],
                "md5": unstable["md5"],
                "url": unstable["url"],
                "use_ghcr": False,
                "continue_on_error": True,
            }
        )
    return {"include": include}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--env-stable", action="store_true")
    parser.add_argument("--env-version")
    args = parser.parse_args()

    stable_catalog = fetch(STABLE_URL)
    if args.env_stable or args.env_version:
        emit_env(stable_entry(stable_catalog, args.env_version or version_file()))
        return

    stable = stable_entry(stable_catalog, version_file())
    unstable = current_unstable(fetch(UNSTABLE_URL))
    if unstable is None:
        print(
            "unstable.json has no linux server entry; skipping that matrix leg",
            file=sys.stderr,
        )
    else:
        print(
            f"unstable linux server {unstable['version']} md5={unstable['md5']}",
            file=sys.stderr,
        )
    json.dump(matrix(stable, unstable), sys.stdout)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main()
