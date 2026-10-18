#!/usr/bin/env bash
# Copy /vs out of ghcr.io/deenski/vs-wayfinder-server:<version>.
# A missing image or a denied token is not fatal: acquired=false tells CI to
# use the Actions cache or a checksum-verified download instead.
set -euo pipefail

acquired=false

copy_image() {
  local cid=""
  rm -rf "$DEST"
  mkdir -p "$DEST"
  cid="$(docker create "$IMAGE")"
  if ! docker cp "$cid:/vs/." "$DEST/"; then
    docker rm "$cid" >/dev/null 2>&1 || true
    rm -rf "$DEST"
    return 1
  fi
  docker rm "$cid" >/dev/null 2>&1 || true
  chmod -R u+rwX "$DEST" 2>/dev/null || true
  if [[ ! -r "$DEST/VintagestoryAPI.dll" ]]; then
    sudo chown -R "$(id -u):$(id -g)" "$DEST"
  fi
  [[ -f "$DEST/VintagestoryAPI.dll" ]]
}

if [[ -z "${GH_TOKEN:-}" || -z "${IMAGE:-}" || -z "${DEST:-}" ]]; then
  echo "GHCR pull skipped (missing token, image, or dest)."
elif ! command -v docker >/dev/null 2>&1; then
  echo "docker is not available; skipping GHCR."
elif echo "$GH_TOKEN" | docker login ghcr.io -u "${GH_ACTOR:-github}" --password-stdin \
  && docker pull "$IMAGE" \
  && copy_image; then
  acquired=true
else
  echo "GHCR image unavailable; falling back to the cache or a verified download."
  rm -rf "$DEST"
fi

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "acquired=$acquired" >> "$GITHUB_OUTPUT"
fi
echo "GHCR acquired=$acquired"
