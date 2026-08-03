#!/usr/bin/env python3
"""Persist and verify source-bound release-stage completion receipts.

Receipts live outside the candidate directory so they never enter the portable
release manifest. A resumed stage is skipped only when every recorded regular
file still exists with the exact digest captured after the stage succeeded.
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

SCHEMA_VERSION = 1
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
    artifacts: list[Path],
) -> Path:
    """Write one completed stage receipt by atomic same-directory rename."""
    receipt = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": KIND,
        "runId": run_id,
        "stage": stage,
        "sourceCommit": source_commit(repository),
        "completedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
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


def verify_checkpoint(
    *,
    repository: Path,
    root: Path,
    checkpoint_directory: Path,
    run_id: str,
    stage: str,
) -> Path:
    """Verify receipt identity and recompute every retained artifact digest."""
    path = checkpoint_path(checkpoint_directory, stage)
    if not path.is_file() or path.is_symlink():
        raise CheckpointError(f"Release-stage checkpoint is missing: {path}")

    try:
        payload: Any = json.loads(path.read_text(encoding="ascii"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise CheckpointError(f"Unable to read release-stage checkpoint '{path}'.") from error

    if not isinstance(payload, dict):
        raise CheckpointError(f"Release-stage checkpoint '{path}' is not an object.")
    expected_identity = {
        "schemaVersion": SCHEMA_VERSION,
        "kind": KIND,
        "runId": run_id,
        "stage": stage,
        "sourceCommit": source_commit(repository),
    }
    for key, expected in expected_identity.items():
        if payload.get(key) != expected:
            raise CheckpointError(
                f"Release-stage checkpoint '{path}' has invalid {key}."
            )

    artifacts = payload.get("artifacts")
    if not isinstance(artifacts, list) or not artifacts:
        raise CheckpointError(f"Release-stage checkpoint '{path}' has no artifacts.")

    lexical_root = Path(os.path.abspath(root))
    root = lexical_root.resolve()
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
        if (
            not isinstance(expected_digest, str)
            or len(expected_digest) != 64
            or any(character not in "0123456789abcdef" for character in expected_digest)
        ):
            raise CheckpointError(
                f"Release-stage artifact '{relative}' has an invalid digest."
            )

        candidate = lexical_root / relative
        reject_symlink_components(lexical_root, candidate, relative)
        artifact = candidate.resolve()
        try:
            artifact.relative_to(root)
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

    return path


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse write and verify commands through one shared identity surface."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    for command in ("write", "verify"):
        subparser = subparsers.add_parser(command)
        subparser.add_argument("--repo", required=True, type=Path)
        subparser.add_argument("--root", required=True, type=Path)
        subparser.add_argument("--checkpoint-directory", required=True, type=Path)
        subparser.add_argument("--run-id", required=True)
        subparser.add_argument("--stage", required=True)
        if command == "write":
            subparser.add_argument("--artifact", action="append", required=True, type=Path)

    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    """Execute one checkpoint operation with concise fail-closed diagnostics."""
    args = parse_args(sys.argv[1:] if argv is None else argv)

    try:
        if args.command == "write":
            path = write_checkpoint(
                repository=args.repo,
                root=args.root,
                checkpoint_directory=args.checkpoint_directory,
                run_id=args.run_id,
                stage=args.stage,
                artifacts=args.artifact,
            )
        else:
            path = verify_checkpoint(
                repository=args.repo,
                root=args.root,
                checkpoint_directory=args.checkpoint_directory,
                run_id=args.run_id,
                stage=args.stage,
            )
    except (CheckpointError, OSError, subprocess.SubprocessError) as error:
        print(error, file=sys.stderr)
        return 1

    print(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
