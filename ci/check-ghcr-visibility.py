#!/usr/bin/env python3
"""Fail closed unless ghcr.io/deenski/vs-wayfinder-server is private.

GitHub documents two user-package reads:

- GET /users/{username}/packages/{package_type}/{package_name}
  "Gets a specific package metadata for a public package owned by a user."
  https://docs.github.com/en/rest/packages/packages#get-a-package-for-a-user
  A 200 from this route means the package is publicly readable. A private
  package is not returned here (typically 404).

- GET /user/packages/{package_type}/{package_name}
  The authenticated-user route. It can return ``visibility``.
  https://docs.github.com/en/rest/packages/packages#get-a-package-for-the-authenticated-user
  Actions ``GITHUB_TOKEN`` is an installation token, not a user PAT, so this
  route often answers 401 or 403. Managing packages through the REST API is
  documented for a classic PAT, which this workflow does not use.

There is no org route: the image is owned by the user ``deenski``.

A response that shows the package is public always fails, including when the
repository variable GHCR_PACKAGE_CONFIRMED_PRIVATE is ``true``. That variable
is only the fallback when every probe is unreadable (401, 403, 404, or any
other non-200) and nothing has proved ``visibility == private``. 404 is not
treated as private.

Modes:
  require-private  Real publish. The package must be proved private, or the
                   variable must be exactly ``true`` when it cannot be proved.
  refuse-public    Empty bootstrap push. Fail only when a probe proves the
                   package is public. Missing or unreadable is allowed,
                   because the package does not exist until the first push.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request

OWNER = "deenski"
PACKAGE = "vs-wayfinder-server"
API = "https://api.github.com"
ACCEPT = "application/vnd.github+json"
API_VERSION = "2022-11-28"


def decide(probes: list[dict], confirmed: str, mode: str) -> tuple[bool, str]:
    if mode not in {"require-private", "refuse-public"}:
        return False, f"unknown visibility check mode: {mode}"

    public = False
    private = False
    lines = []
    for probe in probes:
        status = int(probe["status"])
        visibility = probe.get("visibility") or ""
        label = probe["label"]
        lines.append(f"{label}: HTTP {status} visibility={visibility or '<none>'}")
        if status != 200:
            continue
        if visibility == "private":
            private = True
        else:
            # Includes visibility "public", "internal", or a 200 with no field.
            # GET /users/.../packages is documented for a public package, so a
            # 200 that does not say private is public proof.
            public = True

    detail = "; ".join(lines)
    if public:
        return (
            False,
            "Refusing to push: ghcr.io/"
            f"{OWNER}/{PACKAGE} is public or publicly readable ({detail}). "
            "A public package cannot be made private again. Do not publish the game server.",
        )

    if mode == "refuse-public":
        return (
            True,
            "Bootstrap: the package is not publicly readable "
            f"({detail}). An empty image may be pushed so the package can be created.",
        )

    if private:
        return True, f"Package visibility is private ({detail})."

    if confirmed == "true":
        return (
            True,
            "No GITHUB_TOKEN-readable endpoint proved the package is private "
            f"({detail}). GET /users/{OWNER}/packages/container/{PACKAGE} is "
            "documented for a public package and 404s when the package is private "
            "or missing. GET /user/packages is the authenticated-user route and "
            "is not reliable for an Actions installation token. Continuing because "
            "repository variable GHCR_PACKAGE_CONFIRMED_PRIVATE is true. "
            "A public reading is never overridden by that variable.",
        )

    return (
        False,
        "Refusing to push: cannot prove "
        f"ghcr.io/{OWNER}/{PACKAGE} exists and is private ({detail}). "
        "Confirm the package is Private in its settings, grant deenski/vs-wayfinder "
        "Actions access, then set repository variable GHCR_PACKAGE_CONFIRMED_PRIVATE "
        "to the string true. The variable does not override a public reading, and a "
        "404 is not treated as private.",
    )


def _visibility(body: bytes) -> str:
    try:
        data = json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return ""
    if not isinstance(data, dict):
        return ""
    visibility = data.get("visibility")
    return visibility if isinstance(visibility, str) else ""


def fetch(url: str, token: str) -> tuple[int, str]:
    request = urllib.request.Request(
        url,
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": ACCEPT,
            "X-GitHub-Api-Version": API_VERSION,
            "User-Agent": "vs-wayfinder-server-image",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, _visibility(response.read())
    except urllib.error.HTTPError as exc:
        exc.read()
        return exc.code, ""


def probes_from_token(token: str) -> list[dict]:
    endpoints = [
        (
            "user",
            f"{API}/user/packages/container/{PACKAGE}",
        ),
        (
            "users",
            f"{API}/users/{OWNER}/packages/container/{PACKAGE}",
        ),
    ]
    probes = []
    for label, url in endpoints:
        status, visibility = fetch(url, token)
        probes.append({"label": label, "status": status, "visibility": visibility})
    return probes


def self_test() -> None:
    cases = [
        (
            "unreadable fails closed",
            [{"label": "user", "status": 403, "visibility": ""}, {"label": "users", "status": 404, "visibility": ""}],
            "",
            "require-private",
            False,
        ),
        (
            "variable allows unreadable",
            [{"label": "user", "status": 401, "visibility": ""}, {"label": "users", "status": 404, "visibility": ""}],
            "true",
            "require-private",
            True,
        ),
        (
            "public users endpoint fails even when the variable is set",
            [{"label": "user", "status": 404, "visibility": ""}, {"label": "users", "status": 200, "visibility": "public"}],
            "true",
            "require-private",
            False,
        ),
        (
            "authenticated private proof passes without the variable",
            [{"label": "user", "status": 200, "visibility": "private"}, {"label": "users", "status": 404, "visibility": ""}],
            "",
            "require-private",
            True,
        ),
        (
            "users endpoint private proof passes",
            [{"label": "user", "status": 404, "visibility": ""}, {"label": "users", "status": 200, "visibility": "private"}],
            "",
            "require-private",
            True,
        ),
        (
            "200 without visibility is public proof",
            [{"label": "user", "status": 404, "visibility": ""}, {"label": "users", "status": 200, "visibility": ""}],
            "true",
            "require-private",
            False,
        ),
        (
            "internal is not private",
            [{"label": "user", "status": 200, "visibility": "internal"}, {"label": "users", "status": 404, "visibility": ""}],
            "true",
            "require-private",
            False,
        ),
        (
            "variable must be exactly true",
            [{"label": "user", "status": 403, "visibility": ""}, {"label": "users", "status": 404, "visibility": ""}],
            "True",
            "require-private",
            False,
        ),
        (
            "bootstrap allows a missing package",
            [{"label": "user", "status": 403, "visibility": ""}, {"label": "users", "status": 404, "visibility": ""}],
            "",
            "refuse-public",
            True,
        ),
        (
            "bootstrap refuses a public package",
            [{"label": "user", "status": 404, "visibility": ""}, {"label": "users", "status": 200, "visibility": "public"}],
            "",
            "refuse-public",
            False,
        ),
        (
            "public proof wins over a private probe",
            [{"label": "user", "status": 200, "visibility": "private"}, {"label": "users", "status": 200, "visibility": "public"}],
            "",
            "require-private",
            False,
        ),
    ]
    failed = 0
    for name, probes, confirmed, mode, expect in cases:
        ok, message = decide(probes, confirmed, mode)
        if ok != expect:
            failed += 1
            print(f"FAIL {name}: expected {expect} got {ok}: {message}", file=sys.stderr)
        else:
            print(f"ok {name}")
    if failed:
        raise SystemExit(f"{failed} visibility decision(s) failed")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("require-private", "refuse-public"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return

    token = os.environ.get("GH_TOKEN", "")
    if not token:
        raise SystemExit("GH_TOKEN is required")
    confirmed = os.environ.get("GHCR_PACKAGE_CONFIRMED_PRIVATE", "")
    ok, message = decide(probes_from_token(token), confirmed, args.mode)
    print(message)
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
