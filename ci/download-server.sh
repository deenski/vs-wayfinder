#!/usr/bin/env bash
# Download an official Vintage Story linux server tarball, verify its MD5, and
# extract it to DEST. The tarball is deleted. Do not upload DEST as an artifact.
set -euo pipefail

if [[ -z "${URL:-}" || -z "${EXPECTED_MD5:-}" || -z "${DEST:-}" ]]; then
  echo "URL, EXPECTED_MD5, and DEST are required" >&2
  exit 1
fi

if [[ ! "$EXPECTED_MD5" =~ ^[0-9a-fA-F]{32}$ ]]; then
  echo "MD5 must be 32 hex characters: $EXPECTED_MD5" >&2
  exit 1
fi

case "$URL" in
  https://cdn.vintagestory.at/gamefiles/*) ;;
  *)
    echo "Refusing URL outside the official Vintage Story CDN: $URL" >&2
    exit 1
    ;;
esac

tmp="$(mktemp)"
cleanup() {
  rm -f "$tmp"
}
trap cleanup EXIT

curl -fsSL --retry 3 --retry-delay 2 -o "$tmp" "$URL"
actual="$(md5sum "$tmp" | awk '{print $1}')"
expected_lc="$(printf '%s' "$EXPECTED_MD5" | tr '[:upper:]' '[:lower:]')"
actual_lc="$(printf '%s' "$actual" | tr '[:upper:]' '[:lower:]')"
if [[ "$actual_lc" != "$expected_lc" ]]; then
  echo "MD5 mismatch: expected $EXPECTED_MD5 got $actual" >&2
  exit 1
fi

rm -rf "$DEST"
mkdir -p "$DEST"
tar -xzf "$tmp" -C "$DEST"
if [[ ! -f "$DEST/VintagestoryAPI.dll" ]]; then
  echo "Archive did not contain VintagestoryAPI.dll" >&2
  exit 1
fi
echo "Verified $EXPECTED_MD5 and extracted the server to $DEST"
