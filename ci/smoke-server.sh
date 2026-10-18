#!/usr/bin/env bash
# Start the headless server with the mod zip, require the load line, and stop.
# Game data stays in a temp directory and is deleted on exit.
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: smoke-server.sh <server-install> <mod-zip>" >&2
  exit 1
fi

install="$1"
mod_zip="$2"
if [[ ! -f "$install/VintagestoryAPI.dll" ]]; then
  echo "not a Vintage Story install: $install" >&2
  exit 1
fi
if [[ ! -f "$mod_zip" ]]; then
  echo "mod zip not found: $mod_zip" >&2
  exit 1
fi

data="$(mktemp -d)"
pid=""
log=""

dump_logs() {
  echo "----- server-main.log -----" >&2
  if [[ -n "$log" && -f "$log" ]]; then
    cat "$log" >&2 || true
  fi
  echo "----- stdout -----" >&2
  if [[ -f "$data/stdout.log" ]]; then
    cat "$data/stdout.log" >&2 || true
  fi
}

cleanup() {
  status=$?
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    sleep 1
    kill -9 "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
  rm -rf "$data"
  exit "$status"
}
trap cleanup EXIT

pushd "$install" >/dev/null
dotnet VintagestoryServer.dll --dataPath "$data" --genconfig >/dev/null
popd >/dev/null

mkdir -p "$data/Mods"
cp "$mod_zip" "$data/Mods/"
DATA_DIR="$data" python3 - <<'PY'
import json
import os
from pathlib import Path

path = Path(os.environ["DATA_DIR"]) / "serverconfig.json"
config = json.loads(path.read_text(encoding="utf-8"))
config["AdvertiseServer"] = False
config["Upnp"] = False
world = config["WorldConfig"]
world["Seed"] = "ci-wayfinding"
world["WorldName"] = "ci"
world["WorldType"] = "superflat"
path.write_text(json.dumps(config), encoding="utf-8")
PY

log="$data/Logs/server-main.log"
fifo="$data/stdin"
mkfifo "$fifo"
pushd "$install" >/dev/null
dotnet VintagestoryServer.dll --dataPath "$data" <"$fifo" >"$data/stdout.log" 2>&1 &
pid=$!
popd >/dev/null
exec 3>"$fifo"

loaded=0
deadline=$((SECONDS + 240))
while (( SECONDS < deadline )); do
  if [[ -f "$log" ]] && grep -q '\[wayfinding\] loaded on Server' "$log"; then
    loaded=1
    break
  fi
  if ! kill -0 "$pid" 2>/dev/null; then
    echo "server exited before the mod loaded" >&2
    break
  fi
  sleep 2
done

if [[ "$loaded" -ne 1 ]]; then
  echo "log did not contain [wayfinding] loaded on Server" >&2
  dump_logs
  exit 1
fi

if grep -E '\[(Error|Fatal)\]' "$log"; then
  echo "server log contains errors" >&2
  dump_logs
  exit 1
fi

printf '/stop\n' >&3 || true
for _ in $(seq 1 30); do
  if ! kill -0 "$pid" 2>/dev/null; then
    break
  fi
  sleep 1
done
echo "headless server logged [wayfinding] loaded on Server with no errors"
