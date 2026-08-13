#!/usr/bin/env python3
"""Qualify dependency-review snapshot symmetry before policy evaluation.

GitHub's dependency-review action can continue after its snapshot-warning
retry expires. This module keeps the repository's stronger contract explicit:
normal pull requests require a successful canonical base submission and a
warning-free base/head comparison before the third-party action runs.
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
_BASE_CHECK_NAME = "dependency-submission"
_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
_REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
_SNAPSHOT_WARNING_HEADER = "x-github-dependency-graph-snapshot-warnings"
_WORKFLOW_PATH = ".github/workflows/dependency-review.yml"
_CANONICAL_SUBMISSION_MARKER = "\n  dependency-submission:\n"


class DependencySnapshotReadinessError(RuntimeError):
    """Report an invalid bootstrap state or incomplete snapshot comparison."""


def resolve_mode(
    event_name: str,
    base_revision: str | None,
    api_url: str,
    repository: str,
    token: str,
) -> dict[str, Any]:
    """Resolve the one-time bootstrap without a manual workflow input.

    A push always authors canonical evidence. A pull request is canonical only
    after its exact base revision contains the submission job. This structural
    marker makes bootstrap unreachable after the migration reaches ``main``.
    """

    if event_name == "push":
        return {
            "schemaVersion": 1,
            "mode": "canonical",
            "reason": "main-push",
        }
    if event_name != "pull_request":
        raise DependencySnapshotReadinessError(
            "event_name must be 'push' or 'pull_request'."
        )

    base = _validate_revision(base_revision, "base_revision")
    workflow = _read_base_workflow(api_url, repository, base, token)
    mode = (
        "canonical"
        if _CANONICAL_SUBMISSION_MARKER in f"\n{workflow}"
        else "bootstrap"
    )
    return {
        "schemaVersion": 1,
        "mode": mode,
        "baseRevision": base,
        "reason": (
            "base-declares-canonical-submission"
            if mode == "canonical"
            else "base-predates-canonical-submission"
        ),
    }


def verify_readiness(
    base_revision: str,
    head_revision: str,
    api_url: str,
    repository: str,
    token: str,
    wait_seconds: float,
) -> dict[str, Any]:
    """Qualify both readiness phases within one shared retry window.

    The base receipt and graph propagation are one evidence chain, so
    ``wait_seconds`` bounds their combined retry window rather than granting a
    fresh allowance to each phase. Every API attempt retains its own transport
    timeout; a response received after the retry deadline is still evaluated.
    """

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
    base_check = _wait_for_base_check(
        api_url,
        repository,
        token,
        base,
        deadline,
    )
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
        "schemaVersion": 1,
        "status": "ready",
        "baseRevision": base,
        "headRevision": head,
        "baseCheck": {
            "name": _BASE_CHECK_NAME,
            "id": base_check["id"],
            "conclusion": "success",
        },
        "snapshotWarnings": [],
    }


def _read_base_workflow(
    api_url: str,
    repository: str,
    revision: str,
    token: str,
) -> str:
    _validate_api_identity(api_url, repository, token)
    workflow_path = urllib.parse.quote(_WORKFLOW_PATH, safe="/")
    endpoint = (
        f"{api_url.rstrip('/')}/repos/{repository}/contents/{workflow_path}"
        f"?ref={revision}"
    )
    document, _ = _request_json(endpoint, token)
    if not isinstance(document, dict) or document.get("encoding") != "base64":
        raise DependencySnapshotReadinessError(
            "GitHub returned an invalid base-workflow response."
        )

    content = document.get("content")
    if not isinstance(content, str):
        raise DependencySnapshotReadinessError(
            "GitHub returned a base workflow without encoded content."
        )
    try:
        # The Contents API wraps base64 at fixed columns. Remove transport
        # whitespace before strict decoding so malformed alphabet bytes still
        # fail instead of turning a missing contract into bootstrap.
        encoded = "".join(content.split())
        return base64.b64decode(encoded, validate=True).decode("utf-8")
    except (binascii.Error, UnicodeDecodeError) as error:
        raise DependencySnapshotReadinessError(
            "GitHub returned unreadable base-workflow content."
        ) from error


def _wait_for_base_check(
    api_url: str,
    repository: str,
    token: str,
    revision: str,
    deadline: float,
) -> Mapping[str, Any]:
    endpoint = (
        f"{api_url.rstrip('/')}/repos/{repository}/commits/{revision}/check-runs"
        f"?check_name={_BASE_CHECK_NAME}&filter=all&per_page=100"
    )

    retry_attempt = 0
    while True:
        document, _ = _request_json(endpoint, token)
        checks = _github_action_checks(document, revision)
        successful = next(
            (
                check
                for check in checks
                if check.get("status") == "completed"
                and check.get("conclusion") == "success"
            ),
            None,
        )
        if successful is not None:
            return successful

        terminal = [
            check
            for check in checks
            if check.get("status") == "completed"
        ]
        active = [
            check
            for check in checks
            if check.get("status") in {"queued", "in_progress", "pending"}
        ]
        if terminal and not active:
            conclusions = ", ".join(
                sorted(str(check.get("conclusion")) for check in terminal)
            )
            raise DependencySnapshotReadinessError(
                "The base dependency-submission check did not succeed "
                f"({conclusions})."
            )
        if not _wait_before_retry(deadline, retry_attempt):
            raise DependencySnapshotReadinessError(
                "No successful dependency-submission check is available for "
                "the exact base revision. Rebase onto a current main commit "
                "whose dependency-submission check succeeded."
            )
        retry_attempt += 1


def _github_action_checks(
    document: Any,
    revision: str,
) -> list[Mapping[str, Any]]:
    if not isinstance(document, dict) or not isinstance(
        document.get("check_runs"),
        list,
    ):
        raise DependencySnapshotReadinessError(
            "GitHub returned an invalid check-run response."
        )

    checks: list[Mapping[str, Any]] = []
    for check in document["check_runs"]:
        if not isinstance(check, dict):
            raise DependencySnapshotReadinessError(
                "GitHub returned a malformed check run."
            )
        app = check.get("app")
        if (
            check.get("name") == _BASE_CHECK_NAME
            and check.get("head_sha") == revision
            and isinstance(app, dict)
            and app.get("slug") == "github-actions"
        ):
            checks.append(check)
    return checks


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

    # GitHub recommends exponential backoff for direct dependency-review API
    # access. Cap it at ten seconds so the shared workflow deadline still
    # permits several observations of eventual graph propagation.
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
            "User-Agent": "Doka-Dependency-Snapshot-Readiness/1.0",
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
        description="Qualify canonical dependency-review snapshot evidence."
    )
    commands = parser.add_subparsers(dest="command", required=True)

    resolve = commands.add_parser("resolve-mode")
    resolve.add_argument("--event-name", required=True)
    resolve.add_argument("--base-revision")
    resolve.add_argument("--api-url", required=True)
    resolve.add_argument("--repository", required=True)
    resolve.add_argument("--output", type=Path, required=True)

    verify = commands.add_parser("verify")
    verify.add_argument("--base-revision", required=True)
    verify.add_argument("--head-revision", required=True)
    verify.add_argument("--api-url", required=True)
    verify.add_argument("--repository", required=True)
    verify.add_argument(
        "--wait-seconds",
        type=float,
        default=120,
        help="shared retry window for the base receipt and graph comparison",
    )
    verify.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    """Resolve or verify dependency-review snapshot readiness."""

    args = _parser().parse_args(argv)
    token = os.environ.get("GITHUB_TOKEN", "")
    try:
        if args.command == "resolve-mode":
            document = resolve_mode(
                args.event_name,
                args.base_revision,
                args.api_url,
                args.repository,
                token,
            )
        else:
            document = verify_readiness(
                args.base_revision,
                args.head_revision,
                args.api_url,
                args.repository,
                token,
                args.wait_seconds,
            )
        _write_output(args.output, document)
        print(
            "Dependency snapshot readiness: "
            f"{document.get('mode', document.get('status'))}."
        )
    except (DependencySnapshotReadinessError, OSError) as error:
        print(f"Dependency snapshot readiness failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
