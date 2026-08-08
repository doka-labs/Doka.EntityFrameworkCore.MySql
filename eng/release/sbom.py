#!/usr/bin/env python3
"""Materialize an immutable NuGet graph at its validated restore location."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path
from typing import Any


class AssetsMaterializationError(RuntimeError):
    """Report an invalid or unsafe NuGet assets materialization request."""


def _resolved(path: Path) -> Path:
    """Resolve a path without requiring its final component to exist."""
    return path.expanduser().resolve(strict=False)


def _require_repository_path(path: Path, repository_root: Path, label: str) -> None:
    """Reject paths that could write outside the checked-out repository."""
    if path != repository_root and repository_root not in path.parents:
        raise AssetsMaterializationError(
            f"{label} must remain below repository root: {path}"
        )


def _require_string(mapping: dict[str, Any], name: str) -> str:
    """Read one required non-empty string from a JSON object."""
    value = mapping.get(name)
    if not isinstance(value, str) or not value:
        raise AssetsMaterializationError(
            f"NuGet assets field project.restore.{name} is missing or invalid."
        )

    return value


def materialize_assets(
    repository_root: Path,
    assets_path: Path,
    project_path: Path,
    output_directory: Path,
) -> Path:
    """Validate and copy one immutable graph to its recorded NuGet output path."""
    repository_root = _resolved(repository_root)
    assets_path = _resolved(assets_path)
    project_path = _resolved(project_path)
    output_directory = _resolved(output_directory)

    _require_repository_path(assets_path, repository_root, "Assets path")
    _require_repository_path(project_path, repository_root, "Project path")
    _require_repository_path(output_directory, repository_root, "Output directory")

    if not assets_path.is_file():
        raise AssetsMaterializationError(
            f"NuGet assets source is not a regular file: {assets_path}"
        )
    if not project_path.is_file():
        raise AssetsMaterializationError(
            f"NuGet project is not a regular file: {project_path}"
        )

    try:
        document = json.loads(assets_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise AssetsMaterializationError(
            f"NuGet assets source is not valid UTF-8 JSON: {assets_path}"
        ) from error

    try:
        restore = document["project"]["restore"]
    except (KeyError, TypeError) as error:
        raise AssetsMaterializationError(
            "NuGet assets source does not contain project.restore metadata."
        ) from error
    if not isinstance(restore, dict):
        raise AssetsMaterializationError(
            "NuGet assets source does not contain a project.restore object."
        )

    recorded_project = _resolved(Path(_require_string(restore, "projectPath")))
    recorded_unique_name = _resolved(
        Path(_require_string(restore, "projectUniqueName"))
    )
    recorded_output = _resolved(Path(_require_string(restore, "outputPath")))

    if recorded_project != project_path or recorded_unique_name != project_path:
        raise AssetsMaterializationError(
            "NuGet assets project identity does not match the expected project."
        )
    if recorded_output != output_directory:
        raise AssetsMaterializationError(
            "NuGet assets output path does not match the expected restore location."
        )

    # The SBOM job must reuse the dependency graph qualified by the package
    # job. Re-running restore here could resolve a different graph, while
    # rewriting the JSON would invalidate the copied evidence.
    destination = output_directory / "project.assets.json"
    try:
        output_directory.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(assets_path, destination)
    except OSError as error:
        raise AssetsMaterializationError(
            f"NuGet assets could not be materialized at {destination}: {error}"
        ) from error

    return destination


def _parser() -> argparse.ArgumentParser:
    """Build the command-line contract used by the release orchestrator."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", required=True, type=Path)
    parser.add_argument("--assets", required=True, type=Path)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--output-directory", required=True, type=Path)
    return parser


def main() -> int:
    """Validate the immutable graph and materialize its expected restore path."""
    arguments = _parser().parse_args()

    try:
        destination = materialize_assets(
            arguments.repository_root,
            arguments.assets,
            arguments.project,
            arguments.output_directory,
        )
    except AssetsMaterializationError as error:
        print(f"SBOM component materialization failed: {error}", file=sys.stderr)
        return 1

    print(f"Materialized immutable NuGet graph: {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
