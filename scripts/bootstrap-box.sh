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

# Prints "v:<version>\nr:<rollForward>". rollForward is empty when the key is missing.
read_sdk_pin() {
  local file="$1"
  local version="" roll=""
  if [[ ! -f "$file" ]]; then
    echo "global.json not found: $file" >&2
    exit 1
  fi
  if command -v python3 >/dev/null 2>&1; then
    local parsed
    parsed="$(
      python3 - "$file" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    sdk = json.load(handle)["sdk"]
version = sdk["version"]
roll = sdk.get("rollForward") or ""
print(f"v:{version}")
print(f"r:{roll}")
PY
    )"
    version="${parsed#v:}"
    version="${version%%$'\n'*}"
    roll="${parsed#*$'\n'r:}"
  elif command -v jq >/dev/null 2>&1; then
    version="$(jq -er '.sdk.version' "$file")"
    roll="$(jq -r '.sdk.rollForward // ""' "$file")"
  else
    local line
    line="$(grep -m 1 -E '"version"[[:space:]]*:' "$file" || true)"
    version="$(printf '%s\n' "$line" | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')"
    line="$(grep -m 1 -E '"rollForward"[[:space:]]*:' "$file" || true)"
    if [[ -n "$line" ]]; then
      roll="$(printf '%s\n' "$line" | sed -E 's/.*"rollForward"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')"
    fi
  fi
  if [[ -z "$version" || "$version" == "null" ]]; then
    echo "could not read sdk.version from $file" >&2
    exit 1
  fi
  if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9]+)*$ ]]; then
    echo "refusing unexpected SDK version from $file" >&2
    exit 1
  fi
  if [[ -n "$roll" && ! "$roll" =~ ^[A-Za-z]+$ ]]; then
    echo "refusing unexpected rollForward from $file" >&2
    exit 1
  fi
  printf 'v:%s\nr:%s\n' "$version" "$roll"
}

# Feature band is the hundreds group of the SDK patch: 10.0.402 -> 400, 10.0.100 -> 100.
feature_band_satisfies() {
  local installed="$1"
  local pinned="$2"
  local i_major i_minor i_band p_major p_minor p_band
  local parsed
  parsed="$(sdk_band_parts "$installed")" || return 1
  read -r i_major i_minor i_band <<<"$parsed"
  parsed="$(sdk_band_parts "$pinned")" || return 1
  read -r p_major p_minor p_band <<<"$parsed"
  [[ "$i_major" -eq "$p_major" && "$i_minor" -eq "$p_minor" && "$i_band" -ge "$p_band" ]]
}

sdk_band_parts() {
  local version="$1"
  local core major minor patch
  core="${version%%-*}"
  core="${core%%+*}"
  IFS=. read -r major minor patch <<<"$core"
  if [[ ! "$major" =~ ^[0-9]+$ || ! "$minor" =~ ^[0-9]+$ || ! "$patch" =~ ^[0-9]+$ ]]; then
    return 1
  fi
  printf '%s %s %s\n' "$major" "$minor" "$((10#$patch / 100 * 100))"
}

# disable, missing, patch, latestPatch, and unknown policies require an exact version.
# latestFeature and policies that roll at least that far (feature, minor, latestMinor,
# major, latestMajor) accept the same major.minor with a feature band >= the pin.
sdk_satisfies() {
  local installed="$1"
  local pinned="$2"
  local roll="$3"
  local roll_lower
  roll_lower="$(printf '%s' "$roll" | tr '[:upper:]' '[:lower:]')"
  case "$roll_lower" in
    feature | latestfeature | minor | latestminor | major | latestmajor)
      feature_band_satisfies "$installed" "$pinned"
      ;;
    *)
      [[ "$installed" == "$pinned" ]]
      ;;
  esac
}

# Prints the installed SDK that satisfies the pin. Prefers the exact version.
find_satisfying_sdk() {
  local pinned="$1"
  local roll="$2"
  local dotnet="${HOME}/.dotnet/dotnet"
  local listed installed exact="" fallback=""
  if [[ ! -x "$dotnet" ]]; then
    return 1
  fi
  if ! listed="$("$dotnet" --list-sdks)"; then
    return 1
  fi
  while IFS= read -r installed; do
    installed="${installed%% *}"
    [[ -z "$installed" ]] && continue
    if [[ "$installed" == "$pinned" ]]; then
      exact="$installed"
      break
    fi
    if [[ -z "$fallback" ]] && sdk_satisfies "$installed" "$pinned" "$roll"; then
      fallback="$installed"
    fi
  done <<<"$listed"
  if [[ -n "$exact" ]]; then
    printf '%s\n' "$exact"
    return 0
  fi
  if [[ -n "$fallback" ]]; then
    printf '%s\n' "$fallback"
    return 0
  fi
  return 1
}

exact_sdk_present() {
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
  installer="$(mktemp)"
  trap 'rm -f "$installer"' EXIT
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

pin="$(read_sdk_pin "$global_json")"
version="${pin#v:}"
version="${version%%$'\n'*}"
roll="${pin#*$'\n'r:}"

if match="$(find_satisfying_sdk "$version" "$roll")"; then
  if [[ "$match" == "$version" ]]; then
    echo "SDK ${version} already present in ${HOME}/.dotnet; skipping install."
  else
    echo "SDK ${match} satisfies global.json ${version} (rollForward: ${roll}) in ${HOME}/.dotnet; skipping install."
  fi
else
  echo "Installing .NET SDK ${version} into ${HOME}/.dotnet"
  install_sdk "$version"
  if ! exact_sdk_present "$version"; then
    echo "SDK ${version} was not listed by ${HOME}/.dotnet/dotnet --list-sdks after install" >&2
    exit 1
  fi
fi

echo "export DOTNET_ROOT=\"${HOME}/.dotnet\""
echo "export PATH=\"${HOME}/.dotnet:\$PATH\""
