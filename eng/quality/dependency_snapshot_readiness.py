#!/usr/bin/env python3
"""Synchronize automatic submission with exact dependency review.

Automatic Dependency Submission and dependency review execute independently.
The producer check and dependency-graph propagation are separate asynchronous
phases: a successful producer does not mean that the comparison API can already
read its snapshot. This module waits for both exact producer receipts before it
starts a fresh, bounded propagation window and remains fail-closed throughout.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import math
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
_AUTOMATIC_SUBMISSION_APP_SLUG = "github-actions"
_AUTOMATIC_SUBMISSION_CHECK_NAME = "submit-nuget"
_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
_PRODUCER_RETRY_MAX_SECONDS = 10.0
_PROPAGATION_RETRY_MAX_SECONDS = 30.0
_REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
_SNAPSHOT_WARNING_HEADER = "x-github-dependency-graph-snapshot-warnings"


class DependencySnapshotReadinessError(RuntimeError):
    """Report invalid input or incomplete dependency-readiness evidence."""


class DependencySnapshotProducerUnavailableError(DependencySnapshotReadinessError):
    """Report an automatic-submission producer that did not become available."""


class DependencySnapshotProducerFailedError(DependencySnapshotReadinessError):
    """Report an exact automatic-submission producer that failed."""


class DependencySnapshotComparisonIncompleteError(DependencySnapshotReadinessError):
    """Report snapshots that did not propagate into the exact comparison."""


def verify_readiness(
    base_revision: str,
    head_revision: str,
    api_url: str,
    repository: str,
    token: str,
    producer_wait_seconds: float,
    propagation_wait_seconds: float,
) -> dict[str, Any]:
    """Wait for exact producers, then grant graph propagation a fresh budget."""

    base = _validate_revision(base_revision, "base_revision")
    head = _validate_revision(head_revision, "head_revision")
    if base == head:
        raise DependencySnapshotReadinessError(
            "base_revision and head_revision must identify different commits."
        )
    _validate_wait_seconds(producer_wait_seconds, "producer_wait_seconds")
    _validate_wait_seconds(propagation_wait_seconds, "propagation_wait_seconds")

    _validate_api_identity(api_url, repository, token)
    producer_started_at = time.monotonic()
    producer_deadline = producer_started_at + producer_wait_seconds
    base_check = _wait_for_automatic_submission(
        api_url,
        repository,
        token,
        base,
        "base",
        producer_deadline,
    )
    head_check = _wait_for_automatic_submission(
        api_url,
        repository,
        token,
        head,
        "head",
        producer_deadline,
    )

    # Producer setup and execution must not consume graph-propagation time.
    # The 2026-08-14 incident completed submission successfully but required
    # several more minutes before the comparison warning disappeared.
    producer_completed_at = time.monotonic()
    propagation_started_at = producer_completed_at
    propagation_deadline = propagation_started_at + propagation_wait_seconds
    warning = _wait_for_warning_free_comparison(
        api_url,
        repository,
        token,
        base,
        head,
        propagation_deadline,
    )
    if warning is not None:
        raise DependencySnapshotComparisonIncompleteError(
            "The dependency comparison remained incomplete: " + warning
        )
    propagation_completed_at = time.monotonic()

    return {
        "schemaVersion": 3,
        "status": "ready",
        "baseRevision": base,
        "headRevision": head,
        "automaticSubmission": {
            "base": _producer_receipt(base_check),
            "head": _producer_receipt(head_check),
        },
        "dependencyComparison": {
            "status": "complete",
            "snapshotWarnings": [],
        },
        "waitBudgetsSeconds": {
            "producer": producer_wait_seconds,
            "propagation": propagation_wait_seconds,
        },
        "observedWaitSeconds": {
            "producer": _elapsed_seconds(
                producer_started_at,
                producer_completed_at,
            ),
            "propagation": _elapsed_seconds(
                propagation_started_at,
                propagation_completed_at,
            ),
        },
    }


def _wait_for_automatic_submission(
    api_url: str,
    repository: str,
    token: str,
    revision: str,
    revision_role: str,
    deadline: float,
) -> Mapping[str, Any]:
    endpoint = (
        f"{api_url.rstrip('/')}/repos/{repository}/commits/{revision}/check-runs"
        f"?check_name={_AUTOMATIC_SUBMISSION_CHECK_NAME}&filter=all&per_page=100"
    )
    retry_attempt = 0
    while True:
        document, _ = _request_json(endpoint, token)
        checks = _automatic_submission_checks(document, revision)
        successful = [
            check
            for check in checks
            if check.get("status") == "completed"
            and check.get("conclusion") == "success"
        ]
        if successful:
            return max(successful, key=_check_id)

        terminal = [check for check in checks if check.get("status") == "completed"]
        active = [check for check in checks if check.get("status") != "completed"]
        if terminal and not active:
            conclusions = ", ".join(
                sorted(str(check.get("conclusion")) for check in terminal)
            )
            raise DependencySnapshotProducerFailedError(
                f"The exact {revision_role} automatic dependency submission "
                f"failed ({conclusions})."
            )
        if not _wait_before_retry(
            deadline,
            retry_attempt,
            maximum_delay_seconds=_PRODUCER_RETRY_MAX_SECONDS,
        ):
            state = "did not complete" if active else "was not found"
            raise DependencySnapshotProducerUnavailableError(
                f"The exact {revision_role} automatic dependency submission "
                f"{state} within the producer wait budget."
            )
        retry_attempt += 1


def _automatic_submission_checks(
    document: Any,
    revision: str,
) -> list[Mapping[str, Any]]:
    if not isinstance(document, dict) or not isinstance(
        document.get("check_runs"),
        list,
    ):
        raise DependencySnapshotReadinessError(
            "GitHub returned an invalid automatic-submission check response."
        )

    checks: list[Mapping[str, Any]] = []
    for check in document["check_runs"]:
        if not isinstance(check, dict):
            raise DependencySnapshotReadinessError(
                "GitHub returned a malformed automatic-submission check."
            )
        app = check.get("app")
        if (
            check.get("name") == _AUTOMATIC_SUBMISSION_CHECK_NAME
            and check.get("head_sha") == revision
            and isinstance(app, dict)
            and app.get("slug") == _AUTOMATIC_SUBMISSION_APP_SLUG
        ):
            _check_id(check)
            checks.append(check)
    return checks


def _check_id(check: Mapping[str, Any]) -> int:
    check_id = check.get("id")
    if not isinstance(check_id, int) or isinstance(check_id, bool) or check_id <= 0:
        raise DependencySnapshotReadinessError(
            "GitHub returned an automatic-submission check without a valid ID."
        )
    return check_id


def _producer_receipt(check: Mapping[str, Any]) -> dict[str, Any]:
    completed_at = check.get("completed_at")
    if not isinstance(completed_at, str) or not completed_at.strip():
        raise DependencySnapshotReadinessError(
            "GitHub returned a successful automatic-submission check without "
            "a completion timestamp."
        )
    return {
        "appSlug": _AUTOMATIC_SUBMISSION_APP_SLUG,
        "checkId": _check_id(check),
        "checkName": _AUTOMATIC_SUBMISSION_CHECK_NAME,
        "completedAt": completed_at,
        "conclusion": "success",
        "revision": check["head_sha"],
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
        if not _wait_before_retry(
            deadline,
            retry_attempt,
            maximum_delay_seconds=_PROPAGATION_RETRY_MAX_SECONDS,
        ):
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


def _wait_before_retry(
    deadline: float,
    retry_attempt: int,
    *,
    maximum_delay_seconds: float,
) -> bool:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        return False

    # Producer state changes on a workflow timescale, while dependency-graph
    # propagation changes on a minutes scale. Callers cap the same exponential
    # policy independently so prompt producer detection does not force frequent
    # comparison polling throughout the longer propagation window.
    delay = min(
        maximum_delay_seconds,
        2.0 ** min(retry_attempt, 10),
        remaining,
    )
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


def _validate_wait_seconds(wait_seconds: float, name: str) -> None:
    if not math.isfinite(wait_seconds) or wait_seconds < 0:
        raise DependencySnapshotReadinessError(
            f"{name} must be a finite non-negative number."
        )


def _elapsed_seconds(started_at: float, completed_at: float) -> float:
    return round(max(0.0, completed_at - started_at), 3)


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
        "--producer-wait-seconds",
        type=float,
        default=300,
        help="retry window for exact automatic-submission check completion",
    )
    verify.add_argument(
        "--propagation-wait-seconds",
        type=float,
        default=900,
        help="fresh retry window for submitted snapshots to reach comparison",
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
            args.producer_wait_seconds,
            args.propagation_wait_seconds,
        )
        _write_output(args.output, document)
        observed = document["observedWaitSeconds"]
        print(
            "Dependency snapshot readiness: "
            f"{document['status']} "
            f"(producer={observed['producer']:.3f}s, "
            f"propagation={observed['propagation']:.3f}s)."
        )
    except (DependencySnapshotReadinessError, OSError) as error:
        print(f"Dependency snapshot readiness failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
