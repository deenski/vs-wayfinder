#!/usr/bin/env bash
# Install the .NET SDK pinned by global.json into ~/.dotnet.
# Optional argument: a directory to clone or fetch
# https://github.com/deenski/vs-wayfinder.git over HTTPS.
# Does not download Vintage Story, game files, or a server, and does not use tokens.
set -euo pipefail

repo_url="https://github.com/deenski/vs-wayfinder.git"

if [[ $# -gt 1 ]]; then
  echo "usage: $0 [target-dir]" >&2
  exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

resolve_target() {
  local raw="$1"
  if [[ -d "$raw" ]]; then
    (cd "$raw" && pwd)
    return
  fi
  if [[ -e "$raw" ]]; then
    echo "target exists and is not a directory: $raw" >&2
    exit 1
  fi
  local parent base
  parent="$(dirname -- "$raw")"
  base="$(basename -- "$raw")"
  if [[ ! -d "$parent" ]]; then
    echo "parent directory does not exist: $parent" >&2
    exit 1
  fi
  parent="$(cd "$parent" && pwd)"
  printf '%s/%s\n' "$parent" "$base"
}

# Public HTTPS only. Ignore ambient git config, SSH agents, and tokens.
git_https() {
  env \
    -u GH_TOKEN \
    -u GITHUB_TOKEN \
    -u GH_ENTERPRISE_TOKEN \
    -u GITHUB_PAT \
    -u GIT_ASKPASS \
    -u SSH_AUTH_SOCK \
    GIT_TERMINAL_PROMPT=0 \
    GIT_CONFIG_NOSYSTEM=1 \
    GIT_CONFIG_GLOBAL=/dev/null \
    GIT_CONFIG_SYSTEM=/dev/null \
    git -c credential.helper= "$@"
}

is_allowed_origin() {
  case "$1" in
    https://github.com/deenski/vs-wayfinder \
      | https://github.com/deenski/vs-wayfinder.git \
      | https://github.com/deenski/vs-wayfinder/)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

sync_repo() {
  local target="$1"
  if [[ -d "$target" ]]; then
    if ! git -C "$target" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
      echo "target exists and is not a git clone: $target" >&2
      exit 1
    fi
    local origin
    origin="$(git -C "$target" config --get remote.origin.url || true)"
    if ! is_allowed_origin "$origin"; then
      echo "refusing to fetch: origin must be ${repo_url}" >&2
      exit 1
    fi
    git_https -C "$target" fetch origin
    echo "fetched ${repo_url} in ${target}"
  else
    git_https clone -- "$repo_url" "$target"
    echo "cloned ${repo_url} into ${target}"
  fi
}

read_sdk_version() {
  local file="$1"
  local version=""
  if [[ ! -f "$file" ]]; then
    echo "global.json not found: $file" >&2
    exit 1
  fi
  if command -v python3 >/dev/null 2>&1; then
    version="$(
      python3 - "$file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    print(json.load(handle)["sdk"]["version"])
PY
    )"
  elif command -v jq >/dev/null 2>&1; then
    version="$(jq -er '.sdk.version' "$file")"
  else
    local line
    line="$(grep -m 1 -E '"version"[[:space:]]*:' "$file" || true)"
    version="$(printf '%s\n' "$line" | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')"
  fi
  if [[ -z "$version" || "$version" == "null" ]]; then
    echo "could not read sdk.version from $file" >&2
    exit 1
  fi
  if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9]+)*$ ]]; then
    echo "refusing unexpected SDK version from $file" >&2
    exit 1
  fi
  printf '%s\n' "$version"
}

sdk_present() {
  local version="$1"
  local dotnet="${HOME}/.dotnet/dotnet"
  if [[ ! -x "$dotnet" ]]; then
    return 1
  fi
  "$dotnet" --list-sdks | awk '{print $1}' | grep -Fxq -- "$version"
}

install_sdk() {
  local version="$1"
  if ! command -v curl >/dev/null 2>&1; then
    echo "curl is required to download https://dot.net/v1/dotnet-install.sh" >&2
    exit 1
  fi
  local installer
  installer="$(mktemp)"
  curl -fsSL --retry 3 --retry-delay 2 -o "$installer" https://dot.net/v1/dotnet-install.sh
  bash "$installer" --version "$version" --install-dir "${HOME}/.dotnet"
  rm -f "$installer"
}

global_json=""
if [[ $# -eq 1 ]]; then
  target="$(resolve_target "$1")"
  sync_repo "$target"
  global_json="${target}/global.json"
else
  global_json="${script_dir}/../global.json"
fi

version="$(read_sdk_version "$global_json")"

if sdk_present "$version"; then
  echo "SDK ${version} already present in ${HOME}/.dotnet; skipping install."
else
  echo "Installing .NET SDK ${version} into ${HOME}/.dotnet"
  install_sdk "$version"
  if ! sdk_present "$version"; then
    echo "SDK ${version} was not listed by ${HOME}/.dotnet/dotnet --list-sdks after install" >&2
    exit 1
  fi
fi

echo "export DOTNET_ROOT=\"${HOME}/.dotnet\""
echo "export PATH=\"${HOME}/.dotnet:\$PATH\""
