#!/usr/bin/env python3
"""Persist and verify source-bound release-stage completion receipts.

Receipts live outside the candidate directory so they never enter the portable
release manifest. A resumed stage is skipped only when its source identity,
execution context, and every retained artifact still match the completed work.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SCHEMA_VERSION = 3
KIND = "release-stage-checkpoint"


class CheckpointError(RuntimeError):
    """Report malformed identity or artifact evidence."""


def sha256(path: Path) -> str:
    """Hash one regular file without loading it completely into memory."""
    digest = hashlib.sha256()

    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)

    return digest.hexdigest()


def source_commit(repository: Path) -> str:
    """Resolve the exact Git commit that owns a stage receipt."""
    result = subprocess.run(
        ["git", "-C", str(repository), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    )
    commit = result.stdout.strip()

    if len(commit) != 40 or any(
        character not in "0123456789abcdef"
        for character in commit
    ):
        raise CheckpointError("Release-stage source commit is not a full Git SHA-1.")

    return commit


def checkpoint_path(checkpoint_directory: Path, stage: str) -> Path:
    """Return a traversal-safe receipt path for one validated stage ID."""
    if not stage or any(
        character not in "-._0123456789abcdefghijklmnopqrstuvwxyz"
        for character in stage
    ):
        raise CheckpointError(f"Invalid release-stage ID '{stage}'.")

    return checkpoint_directory / f"{stage}.json"


def validate_identity_text(value: str, label: str) -> str:
    """Reject empty, non-ASCII, or control-bearing identity values."""
    if (
        not value
        or not value.isascii()
        or any(ord(character) < 0x20 or ord(character) == 0x7F for character in value)
    ):
        raise CheckpointError(f"Release-stage {label} is invalid.")

    return value


def validate_digest(value: str, label: str) -> str:
    """Require a canonical lowercase SHA-256 digest."""
    if (
        len(value) != 64
        or any(character not in "0123456789abcdef" for character in value)
    ):
        raise CheckpointError(f"Release-stage {label} is not a SHA-256 digest.")

    return value


def parse_utc(value: str, label: str) -> datetime:
    """Parse one canonical UTC timestamp used for duration evidence."""
    try:
        timestamp = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise CheckpointError(f"Release-stage {label} is not a UTC timestamp.") from error

    if timestamp.tzinfo is None or timestamp.utcoffset() != timezone.utc.utcoffset(timestamp):
        raise CheckpointError(f"Release-stage {label} is not a UTC timestamp.")

    return timestamp.astimezone(timezone.utc)


def format_utc(value: datetime) -> str:
    """Format one timezone-aware value as canonical UTC evidence."""
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def validate_run_attempt(value: int, label: str = "runAttempt") -> int:
    """Reject booleans and non-positive workflow attempt numbers."""
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise CheckpointError(f"Release-stage {label} must be a positive integer.")

    return value


def reject_symlink_components(
    root: Path,
    candidate: Path,
    label: str,
) -> None:
    """Reject a symlink at any component below the canonical evidence root."""
    try:
        relative = candidate.relative_to(root)
    except ValueError as error:
        raise CheckpointError(
            f"Release-stage artifact '{label}' is outside '{root}'."
        ) from error

    current = root
    for component in relative.parts:
        current /= component
        if current.is_symlink():
            raise CheckpointError(
                f"Release-stage artifact '{label}' must not use a symbolic link."
            )


def inventory_artifacts(root: Path, artifacts: list[Path]) -> list[dict[str, str]]:
    """Expand required files and directories into one canonical inventory."""
    lexical_root = Path(os.path.abspath(root))
    root = lexical_root.resolve()
    files: set[Path] = set()

    for artifact in artifacts:
        candidate = Path(os.path.abspath(artifact))
        reject_symlink_components(lexical_root, candidate, str(artifact))
        resolved = candidate.resolve()

        try:
            resolved.relative_to(root)
        except ValueError as error:
            raise CheckpointError(
                f"Release-stage artifact '{artifact}' is outside '{root}'."
            ) from error

        if resolved.is_file():
            files.add(resolved)
            continue
        if not resolved.is_dir():
            raise CheckpointError(f"Release-stage artifact '{artifact}' is missing.")

        directory_files = [
            path
            for path in resolved.rglob("*")
            if path.is_file() and not path.is_symlink()
        ]
        if not directory_files:
            raise CheckpointError(
                f"Release-stage artifact directory '{artifact}' is empty."
            )
        symlinks = [path for path in resolved.rglob("*") if path.is_symlink()]
        if symlinks:
            raise CheckpointError(
                f"Release-stage artifact directory '{artifact}' contains a symbolic link."
            )
        files.update(directory_files)

    if not files:
        raise CheckpointError("A release-stage receipt requires at least one artifact.")

    return [
        {
            "path": path.relative_to(root).as_posix(),
            "sha256": sha256(path),
        }
        for path in sorted(files)
    ]


def write_checkpoint(
    *,
    repository: Path,
    root: Path,
    checkpoint_directory: Path,
    run_id: str,
    stage: str,
    source_ref: str,
    expected_release_tag: str,
    run_attempt: int,
    runner_identity: str,
    started_utc: str,
    artifacts: list[Path],
) -> Path:
    """Write one completed stage receipt by atomic same-directory rename."""
    run_id = validate_identity_text(run_id, "runId")
    source_ref = validate_identity_text(source_ref, "sourceRef")
    expected_release_tag = validate_identity_text(
        expected_release_tag,
        "expectedReleaseTag",
    )
    runner_identity = validate_identity_text(runner_identity, "runnerIdentity")
    run_attempt = validate_run_attempt(run_attempt)
    started = parse_utc(started_utc, "startedUtc")
    completed = datetime.now(timezone.utc)
    if started > completed:
        raise CheckpointError("Release-stage startedUtc is after completedUtc.")

    receipt: dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": KIND,
        "runId": run_id,
        "stage": stage,
        "sourceCommit": source_commit(repository),
        "sourceRef": source_ref,
        "expectedReleaseTag": expected_release_tag,
        "runAttempt": run_attempt,
        "runnerIdentity": runner_identity,
        "startedUtc": format_utc(started),
        "completedUtc": format_utc(completed),
        "durationSeconds": round((completed - started).total_seconds(), 3),
        "artifacts": inventory_artifacts(root, artifacts),
    }
    checkpoint_directory.mkdir(parents=True, exist_ok=True)
    destination = checkpoint_path(checkpoint_directory, stage)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{stage}.",
        suffix=".tmp",
        dir=checkpoint_directory,
    )
    temporary = Path(temporary_name)

    try:
        with os.fdopen(descriptor, "w", encoding="ascii") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)

    return destination


def read_checkpoint(path: Path) -> dict[str, Any]:
    """Load one regular JSON receipt through the ASCII-only contract."""
    if not path.is_file() or path.is_symlink():
        raise CheckpointError(f"Release-stage checkpoint is missing: {path}")

    try:
        payload: Any = json.loads(path.read_text(encoding="ascii"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise CheckpointError(f"Unable to read release-stage checkpoint '{path}'.") from error

    if not isinstance(payload, dict):
        raise CheckpointError(f"Release-stage checkpoint '{path}' is not an object.")

    return payload


def verify_artifacts(path: Path, root: Path, payload: dict[str, Any]) -> None:
    """Recompute and compare every retained artifact digest."""
    artifacts = payload.get("artifacts")
    if not isinstance(artifacts, list) or not artifacts:
        raise CheckpointError(f"Release-stage checkpoint '{path}' has no artifacts.")

    lexical_root = Path(os.path.abspath(root))
    canonical_root = lexical_root.resolve()
    seen: set[str] = set()
    for index, entry in enumerate(artifacts):
        if not isinstance(entry, dict):
            raise CheckpointError(f"Release-stage artifact {index} is not an object.")
        relative = entry.get("path")
        expected_digest = entry.get("sha256")
        if (
            not isinstance(relative, str)
            or not relative
            or Path(relative).is_absolute()
            or ".." in Path(relative).parts
            or relative in seen
        ):
            raise CheckpointError(f"Release-stage artifact {index} has an invalid path.")
        if not isinstance(expected_digest, str):
            raise CheckpointError(
                f"Release-stage artifact '{relative}' has an invalid digest."
            )
        try:
            validate_digest(expected_digest, f"artifact '{relative}' digest")
        except CheckpointError as error:
            raise CheckpointError(
                f"Release-stage artifact '{relative}' has an invalid digest."
            ) from error

        candidate = lexical_root / relative
        reject_symlink_components(lexical_root, candidate, relative)
        artifact = candidate.resolve()
        try:
            artifact.relative_to(canonical_root)
        except ValueError as error:
            raise CheckpointError(
                f"Release-stage artifact '{relative}' escapes the candidate root."
            ) from error
        if not artifact.is_file():
            raise CheckpointError(f"Release-stage artifact '{relative}' is missing.")
        if sha256(artifact) != expected_digest:
            raise CheckpointError(
                f"Release-stage artifact '{relative}' does not match its digest."
            )
        seen.add(relative)


def verify_checkpoint(
    *,
    repository: Path,
    root: Path,
    checkpoint_directory: Path,
    run_id: str,
    stage: str,
    source_ref: str,
    expected_release_tag: str,
    maximum_run_attempt: int,
) -> Path:
    """Verify receipt identity and recompute every retained artifact digest."""
    validate_identity_text(run_id, "runId")
    validate_identity_text(source_ref, "sourceRef")
    validate_identity_text(expected_release_tag, "expectedReleaseTag")
    validate_run_attempt(maximum_run_attempt, "maximumRunAttempt")
    path = checkpoint_path(checkpoint_directory, stage)
    payload = read_checkpoint(path)
    expected_identity = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": KIND,
        "runId": run_id,
        "stage": stage,
        "sourceCommit": source_commit(repository),
        "sourceRef": source_ref,
        "expectedReleaseTag": expected_release_tag,
    }
    for key, expected in expected_identity.items():
        if payload.get(key) != expected:
            raise CheckpointError(
                f"Release-stage checkpoint '{path}' has invalid {key}."
            )

    run_attempt = payload.get("runAttempt")
    validate_run_attempt(run_attempt)
    if run_attempt > maximum_run_attempt:
        raise CheckpointError(
            f"Release-stage checkpoint '{path}' has a newer runAttempt."
        )

    runner_identity = payload.get("runnerIdentity")
    if not isinstance(runner_identity, str):
        raise CheckpointError(
            f"Release-stage checkpoint '{path}' has invalid runnerIdentity."
        )
    validate_identity_text(runner_identity, "runnerIdentity")

    started_value = payload.get("startedUtc")
    completed_value = payload.get("completedUtc")
    duration = payload.get("durationSeconds")
    if not isinstance(started_value, str) or not isinstance(completed_value, str):
        raise CheckpointError(
            f"Release-stage checkpoint '{path}' has invalid timestamps."
        )
    started = parse_utc(started_value, "startedUtc")
    completed = parse_utc(completed_value, "completedUtc")
    if started > completed:
        raise CheckpointError(
            f"Release-stage checkpoint '{path}' has inverted timestamps."
        )
    if (
        isinstance(duration, bool)
        or not isinstance(duration, (int, float))
        or duration < 0
        or abs(duration - (completed - started).total_seconds()) > 0.001
    ):
        raise CheckpointError(
            f"Release-stage checkpoint '{path}' has invalid durationSeconds."
        )

    verify_artifacts(path, root, payload)
    return path


def verify_checkpoint_set(
    *,
    repository: Path,
    root: Path,
    checkpoint_directory: Path,
    run_id: str,
    source_ref: str,
    expected_release_tag: str,
    maximum_run_attempt: int,
    expected_stages: list[str],
) -> list[Path]:
    """Verify one exact, exhaustive set of release-stage receipts."""
    if not expected_stages or len(set(expected_stages)) != len(expected_stages):
        raise CheckpointError("Expected release-stage IDs must be unique and non-empty.")

    expected = set(expected_stages)
    for stage in expected:
        checkpoint_path(checkpoint_directory, stage)

    if checkpoint_directory.is_symlink():
        raise CheckpointError("Release-stage checkpoint directory must not be a symlink.")
    actual = {
        path.stem
        for path in checkpoint_directory.glob("*.json")
        if path.is_file() and not path.is_symlink()
    }
    missing = sorted(expected - actual)
    unexpected = sorted(actual - expected)
    if missing:
        raise CheckpointError(
            "Release-stage checkpoint set is missing: " + ", ".join(missing)
        )
    if unexpected:
        raise CheckpointError(
            "Release-stage checkpoint set is unexpected: " + ", ".join(unexpected)
        )

    verified: list[Path] = []
    for stage in sorted(expected):
        verified.append(
            verify_checkpoint(
                repository=repository,
                root=root,
                checkpoint_directory=checkpoint_directory,
                run_id=run_id,
                stage=stage,
                source_ref=source_ref,
                expected_release_tag=expected_release_tag,
                maximum_run_attempt=maximum_run_attempt,
            )
        )

    return verified


def add_identity_arguments(parser: argparse.ArgumentParser) -> None:
    """Add the exact release identity shared by every command."""
    parser.add_argument("--repo", required=True, type=Path)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--checkpoint-directory", required=True, type=Path)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--source-ref", required=True)
    parser.add_argument("--expected-release-tag", required=True)


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse checkpoint commands through one shared identity surface."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    write_parser = subparsers.add_parser("write")
    add_identity_arguments(write_parser)
    write_parser.add_argument("--stage", required=True)
    write_parser.add_argument("--run-attempt", required=True, type=int)
    write_parser.add_argument("--runner-identity", required=True)
    write_parser.add_argument("--started-utc", required=True)
    write_parser.add_argument("--artifact", action="append", required=True, type=Path)

    verify_parser = subparsers.add_parser("verify")
    add_identity_arguments(verify_parser)
    verify_parser.add_argument("--stage", required=True)
    verify_parser.add_argument("--maximum-run-attempt", required=True, type=int)

    verify_set_parser = subparsers.add_parser("verify-set")
    add_identity_arguments(verify_set_parser)
    verify_set_parser.add_argument(
        "--maximum-run-attempt",
        required=True,
        type=int,
    )
    verify_set_parser.add_argument(
        "--expected-stage",
        action="append",
        required=True,
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    """Execute one checkpoint operation with concise fail-closed diagnostics."""
    args = parse_args(sys.argv[1:] if argv is None else argv)

    try:
        if args.command == "write":
            result: Path | list[Path] = write_checkpoint(
                repository=args.repo,
                root=args.root,
                checkpoint_directory=args.checkpoint_directory,
                run_id=args.run_id,
                stage=args.stage,
                source_ref=args.source_ref,
                expected_release_tag=args.expected_release_tag,
                run_attempt=args.run_attempt,
                runner_identity=args.runner_identity,
                started_utc=args.started_utc,
                artifacts=args.artifact,
            )
        elif args.command == "verify":
            result = verify_checkpoint(
                repository=args.repo,
                root=args.root,
                checkpoint_directory=args.checkpoint_directory,
                run_id=args.run_id,
                stage=args.stage,
                source_ref=args.source_ref,
                expected_release_tag=args.expected_release_tag,
                maximum_run_attempt=args.maximum_run_attempt,
            )
        else:
            result = verify_checkpoint_set(
                repository=args.repo,
                root=args.root,
                checkpoint_directory=args.checkpoint_directory,
                run_id=args.run_id,
                source_ref=args.source_ref,
                expected_release_tag=args.expected_release_tag,
                maximum_run_attempt=args.maximum_run_attempt,
                expected_stages=args.expected_stage,
            )
    except (CheckpointError, OSError, subprocess.SubprocessError) as error:
        print(error, file=sys.stderr)
        return 1

    if isinstance(result, list):
        for path in result:
            print(path)
    else:
        print(result)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
