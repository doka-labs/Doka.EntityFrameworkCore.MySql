#!/usr/bin/env python3
"""Fail closed until GitHub reports a complete dependency comparison.

Automatic Dependency Submission and dependency review execute independently.
GitHub exposes missing or still-propagating snapshots through a response
header on the exact base/head comparison. This module applies the documented
exponential-backoff contract without accepting the official action's soft
timeout behavior for trusted pull requests.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Mapping
from pathlib import Path
from typing import Any


_API_VERSION = "2026-03-10"
_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
_REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
_SNAPSHOT_WARNING_HEADER = "x-github-dependency-graph-snapshot-warnings"


class DependencySnapshotReadinessError(RuntimeError):
    """Report an invalid input or incomplete dependency comparison."""


def verify_readiness(
    base_revision: str,
    head_revision: str,
    api_url: str,
    repository: str,
    token: str,
    wait_seconds: float,
) -> dict[str, Any]:
    """Wait within one deadline for an exact warning-free comparison."""

    base = _validate_revision(base_revision, "base_revision")
    head = _validate_revision(head_revision, "head_revision")
    if base == head:
        raise DependencySnapshotReadinessError(
            "base_revision and head_revision must identify different commits."
        )
    if wait_seconds < 0:
        raise DependencySnapshotReadinessError("wait_seconds cannot be negative.")

    _validate_api_identity(api_url, repository, token)
    deadline = time.monotonic() + wait_seconds
    warning = _wait_for_warning_free_comparison(
        api_url,
        repository,
        token,
        base,
        head,
        deadline,
    )
    if warning is not None:
        raise DependencySnapshotReadinessError(
            "The dependency comparison remained incomplete: " + warning
        )

    return {
        "schemaVersion": 2,
        "status": "ready",
        "baseRevision": base,
        "headRevision": head,
        "dependencyComparison": {
            "status": "complete",
            "snapshotWarnings": [],
        },
    }


def _wait_for_warning_free_comparison(
    api_url: str,
    repository: str,
    token: str,
    base_revision: str,
    head_revision: str,
    deadline: float,
) -> str | None:
    endpoint = (
        f"{api_url.rstrip('/')}/repos/{repository}/dependency-graph/compare/"
        f"{base_revision}...{head_revision}?per_page=1"
    )
    retry_attempt = 0
    while True:
        document, headers = _request_json(endpoint, token)
        if not isinstance(document, list):
            raise DependencySnapshotReadinessError(
                "GitHub returned an invalid dependency-comparison response."
            )
        warning = _decode_snapshot_warning(headers)
        if warning is None:
            return None
        if not _wait_before_retry(deadline, retry_attempt):
            return warning
        retry_attempt += 1


def _decode_snapshot_warning(headers: Mapping[str, str]) -> str | None:
    encoded = next(
        (
            value
            for name, value in headers.items()
            if name.casefold() == _SNAPSHOT_WARNING_HEADER
        ),
        None,
    )
    if not encoded:
        return None
    try:
        warning = base64.b64decode(encoded, validate=True).decode("utf-8").strip()
    except (binascii.Error, UnicodeDecodeError) as error:
        raise DependencySnapshotReadinessError(
            "GitHub returned an unreadable snapshot-warning header."
        ) from error
    if not warning:
        raise DependencySnapshotReadinessError(
            "GitHub returned an empty encoded snapshot-warning header."
        )
    return warning


def _wait_before_retry(deadline: float, retry_attempt: int) -> bool:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        return False

    # GitHub recommends exponential backoff when submission and review execute
    # independently. The cap leaves multiple observations inside the workflow
    # deadline without turning readiness into an unbounded runner wait.
    delay = min(10.0, 2.0 ** min(retry_attempt, 4), remaining)
    time.sleep(delay)
    return True


def _request_json(
    endpoint: str,
    token: str,
) -> tuple[Any, Mapping[str, str]]:
    request = urllib.request.Request(
        endpoint,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "Doka-Dependency-Snapshot-Readiness/2.0",
            "X-GitHub-Api-Version": _API_VERSION,
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            status = response.status
            body = response.read()
            headers = dict(response.headers.items())
    except urllib.error.HTTPError as error:
        raise DependencySnapshotReadinessError(
            f"GitHub rejected the readiness request with HTTP {error.code}."
        ) from error
    except urllib.error.URLError as error:
        raise DependencySnapshotReadinessError(
            "GitHub dependency readiness could not be reached."
        ) from error

    if status != 200:
        raise DependencySnapshotReadinessError(
            f"GitHub returned unexpected readiness HTTP status {status}."
        )
    try:
        return json.loads(body), headers
    except json.JSONDecodeError as error:
        raise DependencySnapshotReadinessError(
            "GitHub returned unreadable dependency-readiness JSON."
        ) from error


def _validate_revision(revision: str | None, name: str) -> str:
    if revision is None or not _COMMIT_PATTERN.fullmatch(revision):
        raise DependencySnapshotReadinessError(
            f"{name} must be a lowercase 40-character Git SHA."
        )
    return revision


def _validate_api_identity(api_url: str, repository: str, token: str) -> None:
    if not _REPOSITORY_PATTERN.fullmatch(repository):
        raise DependencySnapshotReadinessError(
            "repository must use the 'owner/name' form."
        )
    if not token.strip():
        raise DependencySnapshotReadinessError(
            "GITHUB_TOKEN is required for dependency readiness."
        )

    parsed = urllib.parse.urlsplit(api_url)
    if (
        parsed.scheme != "https"
        or not parsed.netloc
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
    ):
        raise DependencySnapshotReadinessError(
            "api_url must be an HTTPS origin without credentials, query, or fragment."
        )


def _write_output(path: Path, document: Mapping[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(document, indent=2, sort_keys=True) + "\n",
        encoding="ascii",
    )


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Qualify automatic dependency-submission readiness."
    )
    commands = parser.add_subparsers(dest="command", required=True)

    verify = commands.add_parser("verify")
    verify.add_argument("--base-revision", required=True)
    verify.add_argument("--head-revision", required=True)
    verify.add_argument("--api-url", required=True)
    verify.add_argument("--repository", required=True)
    verify.add_argument(
        "--wait-seconds",
        type=float,
        default=180,
        help="retry window for automatic snapshot propagation",
    )
    verify.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    """Verify dependency-review readiness and write its receipt."""

    args = _parser().parse_args(argv)
    token = os.environ.get("GITHUB_TOKEN", "")
    try:
        document = verify_readiness(
            args.base_revision,
            args.head_revision,
            args.api_url,
            args.repository,
            token,
            args.wait_seconds,
        )
        _write_output(args.output, document)
        print(f"Dependency snapshot readiness: {document['status']}.")
    except (DependencySnapshotReadinessError, OSError) as error:
        print(f"Dependency snapshot readiness failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
