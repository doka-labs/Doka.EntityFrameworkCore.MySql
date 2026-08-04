#!/usr/bin/env python3
"""Resolve and safely restore immutable release-stage artifacts.

GitHub creates a new run attempt when selected jobs are rerun, while jobs that
already succeeded can remain in an earlier attempt. This helper therefore
selects the newest immutable artifact for every required stage without
silently mixing workflow runs, accepting expired evidence, or trusting an
archive before its hosted SHA-256 digest has been verified.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import stat
import sys
import tempfile
import zipfile
from pathlib import Path, PurePosixPath
from typing import Any

SCHEMA_VERSION = 1
ARTIFACT_PREFIX = "release-stage-"
ATTEMPT_MARKER = "-attempt-"


class ArtifactResolutionError(RuntimeError):
    """Report malformed, ambiguous, or incomplete hosted artifact evidence."""


def sha256(path: Path) -> str:
    """Hash one regular file without loading it completely into memory."""
    digest = hashlib.sha256()

    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)

    return digest.hexdigest()


def validate_positive_integer(value: Any, label: str) -> int:
    """Reject booleans and non-positive identifiers or attempt numbers."""
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ArtifactResolutionError(f"{label} must be a positive integer.")

    return value


def validate_stage(stage: str) -> str:
    """Keep stage identifiers safe for artifact names and local paths."""
    if not stage or any(
        character not in "-0123456789abcdefghijklmnopqrstuvwxyz"
        for character in stage
    ):
        raise ArtifactResolutionError(f"Invalid release stage '{stage}'.")

    return stage


def validate_digest(value: Any) -> str:
    """Require the canonical digest exposed by the GitHub artifact API."""
    if not isinstance(value, str) or not value.startswith("sha256:"):
        raise ArtifactResolutionError("Artifact digest is not a SHA-256 digest.")

    digest = value.removeprefix("sha256:")
    if len(digest) != 64 or any(
        character not in "0123456789abcdef" for character in digest
    ):
        raise ArtifactResolutionError("Artifact digest is not a SHA-256 digest.")

    return digest


def parse_artifact_name(name: Any) -> tuple[str, int] | None:
    """Parse an attempt-qualified release-stage artifact name."""
    if not isinstance(name, str) or not name.startswith(ARTIFACT_PREFIX):
        return None

    stage_and_attempt = name.removeprefix(ARTIFACT_PREFIX)
    if ATTEMPT_MARKER not in stage_and_attempt:
        return None

    stage, attempt_text = stage_and_attempt.rsplit(ATTEMPT_MARKER, 1)
    stage = validate_stage(stage)
    if not attempt_text.isascii() or not attempt_text.isdecimal():
        raise ArtifactResolutionError(
            f"Artifact '{name}' has an invalid run attempt."
        )

    return stage, validate_positive_integer(int(attempt_text), "Run attempt")


def flatten_artifacts(payload: Any) -> list[dict[str, Any]]:
    """Flatten one or more paginated GitHub artifact responses."""
    pages = payload if isinstance(payload, list) else [payload]
    artifacts: list[dict[str, Any]] = []

    for page in pages:
        if not isinstance(page, dict) or not isinstance(
            page.get("artifacts"),
            list,
        ):
            raise ArtifactResolutionError(
                "Artifact metadata is not a GitHub workflow-run artifact response."
            )

        for artifact in page["artifacts"]:
            if not isinstance(artifact, dict):
                raise ArtifactResolutionError(
                    "Artifact metadata contains a non-object."
                )
            artifacts.append(artifact)

    return artifacts


def resolve_artifacts(
    payload: Any,
    *,
    run_id: int,
    maximum_attempt: int,
    required_stages: list[str],
) -> dict[str, Any]:
    """Select the newest complete artifact for every required stage."""
    run_id = validate_positive_integer(run_id, "Workflow run ID")
    maximum_attempt = validate_positive_integer(
        maximum_attempt,
        "Maximum run attempt",
    )
    stages = [validate_stage(stage) for stage in required_stages]
    if not stages or len(stages) != len(set(stages)):
        raise ArtifactResolutionError(
            "Required stages must be a non-empty set without duplicates."
        )

    expected = set(stages)
    candidates: dict[str, list[dict[str, Any]]] = {
        stage: [] for stage in stages
    }

    for artifact in flatten_artifacts(payload):
        parsed_name = parse_artifact_name(artifact.get("name"))
        if parsed_name is None:
            continue

        stage, attempt = parsed_name
        if stage not in expected or attempt > maximum_attempt:
            continue
        if artifact.get("expired") is not False:
            continue

        workflow_run = artifact.get("workflow_run")
        if not isinstance(workflow_run, dict) or workflow_run.get("id") != run_id:
            continue

        candidates[stage].append(
            {
                "stage": stage,
                "attempt": attempt,
                "id": validate_positive_integer(
                    artifact.get("id"),
                    "Artifact ID",
                ),
                "name": artifact["name"],
                "sha256": validate_digest(artifact.get("digest")),
            }
        )

    selected: list[dict[str, Any]] = []
    for stage in stages:
        stage_candidates = candidates[stage]
        if not stage_candidates:
            raise ArtifactResolutionError(
                f"No usable artifact exists for required stage '{stage}'."
            )

        newest_attempt = max(candidate["attempt"] for candidate in stage_candidates)
        newest = [
            candidate
            for candidate in stage_candidates
            if candidate["attempt"] == newest_attempt
        ]
        if len(newest) != 1:
            raise ArtifactResolutionError(
                f"Stage '{stage}' has ambiguous artifacts for attempt {newest_attempt}."
            )
        selected.append(newest[0])

    return {
        "schemaVersion": SCHEMA_VERSION,
        "workflowRunId": run_id,
        "maximumRunAttempt": maximum_attempt,
        "artifacts": selected,
    }


def validate_archive_member(info: zipfile.ZipInfo) -> PurePosixPath:
    """Reject archive entries that could escape or mutate the output tree."""
    name = info.filename
    if not name or "\\" in name or "\x00" in name:
        raise ArtifactResolutionError("Artifact archive contains an invalid path.")

    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in path.parts):
        raise ArtifactResolutionError(
            f"Artifact archive path '{name}' is not a canonical relative path."
        )

    mode = info.external_attr >> 16
    if stat.S_ISLNK(mode):
        raise ArtifactResolutionError(
            f"Artifact archive path '{name}' must not be a symbolic link."
        )

    return path


def copy_member(
    archive: zipfile.ZipFile,
    info: zipfile.ZipInfo,
    destination: Path,
) -> None:
    """Extract one file atomically and permit only identical merge collisions."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if not destination.is_file() or destination.is_symlink():
            raise ArtifactResolutionError(
                f"Artifact archive collides with non-file '{destination}'."
            )

        with archive.open(info) as incoming:
            incoming_digest = hashlib.file_digest(incoming, "sha256").hexdigest()
        if sha256(destination) != incoming_digest:
            raise ArtifactResolutionError(
                f"Artifact archive conflicts with existing file '{destination}'."
            )
        return

    file_descriptor, temporary_name = tempfile.mkstemp(
        dir=destination.parent,
        prefix=f".{destination.name}.",
    )
    try:
        with os.fdopen(file_descriptor, "wb") as target, archive.open(info) as source:
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                target.write(chunk)
        os.replace(temporary_name, destination)
    except BaseException:
        Path(temporary_name).unlink(missing_ok=True)
        raise


def restore_archive(archive_path: Path, destination: Path, digest: str) -> None:
    """Verify and safely merge one immutable GitHub artifact archive."""
    digest = validate_digest(f"sha256:{digest}")
    if not archive_path.is_file() or archive_path.is_symlink():
        raise ArtifactResolutionError(
            f"Artifact archive '{archive_path}' is not a regular file."
        )
    if sha256(archive_path) != digest:
        raise ArtifactResolutionError(
            f"Artifact archive '{archive_path}' does not match its hosted digest."
        )

    destination.mkdir(parents=True, exist_ok=True)
    destination_root = destination.resolve()

    with zipfile.ZipFile(archive_path) as archive:
        for info in archive.infolist():
            relative = validate_archive_member(info)
            output = destination_root.joinpath(*relative.parts)
            if info.is_dir():
                output.mkdir(parents=True, exist_ok=True)
                continue
            copy_member(archive, info, output)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    """Persist selection evidence atomically beside its final path."""
    path.parent.mkdir(parents=True, exist_ok=True)
    file_descriptor, temporary_name = tempfile.mkstemp(
        dir=path.parent,
        prefix=f".{path.name}.",
        text=True,
    )
    try:
        with os.fdopen(file_descriptor, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, indent=2, sort_keys=True)
            stream.write("\n")
        os.replace(temporary_name, path)
    except BaseException:
        Path(temporary_name).unlink(missing_ok=True)
        raise


def parse_arguments() -> argparse.Namespace:
    """Parse artifact selection or verified extraction input."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    select = subparsers.add_parser("select")
    select.add_argument("--metadata", type=Path, required=True)
    select.add_argument("--run-id", type=int, required=True)
    select.add_argument("--maximum-attempt", type=int, required=True)
    select.add_argument("--stage", action="append", required=True)
    select.add_argument("--output", type=Path, required=True)

    restore = subparsers.add_parser("restore")
    restore.add_argument("--archive", type=Path, required=True)
    restore.add_argument("--destination", type=Path, required=True)
    restore.add_argument("--sha256", required=True)

    return parser.parse_args()


def main() -> int:
    """Run the requested fail-closed artifact operation."""
    arguments = parse_arguments()
    try:
        if arguments.command == "select":
            with arguments.metadata.open(encoding="utf-8") as stream:
                payload = json.load(stream)
            selection = resolve_artifacts(
                payload,
                run_id=arguments.run_id,
                maximum_attempt=arguments.maximum_attempt,
                required_stages=arguments.stage,
            )
            write_json(arguments.output, selection)
        else:
            restore_archive(
                arguments.archive,
                arguments.destination,
                arguments.sha256,
            )
    except (
        ArtifactResolutionError,
        json.JSONDecodeError,
        OSError,
        zipfile.BadZipFile,
    ) as error:
        print(error, file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
