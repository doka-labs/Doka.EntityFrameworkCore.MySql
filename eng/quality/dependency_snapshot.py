#!/usr/bin/env python3
"""Build and submit a canonical GitHub snapshot from NuGet restore assets.

The implementation uses only repository-owned code and the Python standard
library. This keeps the pull-request security gate reproducible: a pinned .NET
SDK resolves the graph, and no action can download a moving detector executable
after review. Package ordering and classification are canonical; the scanned
timestamp intentionally records when each extraction occurred.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Iterable, Mapping
from pathlib import Path
from typing import Any


_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
_CORRELATOR_PATTERN = re.compile(r"^[A-Za-z0-9._-]{1,100}$")
_REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
_SUCCESSFUL_SUBMISSION_RESULTS = frozenset({"ACCEPTED", "SUCCESS"})

# project.assets.json is a NuGet restore contract consumed outside its owning
# SDK. A new version requires review instead of an optimistic partial parse.
_PROJECT_ASSETS_VERSION = 4
_DETECTOR_NAME = "Doka NuGet restore graph"
_DETECTOR_VERSION = "1.0.0"
_DETECTOR_URL = (
    "https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/"
    "blob/main/eng/quality/dependency_snapshot.py"
)


class DependencySnapshotError(RuntimeError):
    """Report an invalid restore graph or rejected dependency submission."""


def create_snapshot(
    repository_root: Path,
    assets_root: Path,
    commit_sha: str,
    git_ref: str,
    correlator: str,
    run_id: str,
    scanned_at: dt.datetime | None = None,
) -> dict[str, Any]:
    """Create a canonical payload with a supplied or current scan timestamp."""

    root = repository_root.resolve()
    assets = assets_root.resolve()
    _validate_identity(commit_sha, git_ref, correlator, run_id)

    asset_files = tuple(sorted(assets.rglob("project.assets.json")))
    if not asset_files:
        raise DependencySnapshotError(
            f"No project.assets.json files were found below '{assets}'."
        )

    manifests: dict[str, dict[str, Any]] = {}
    for asset_file in asset_files:
        source_location, manifest = _manifest_from_assets(root, asset_file)
        previous = manifests.setdefault(source_location, manifest)
        if previous != manifest:
            raise DependencySnapshotError(
                f"Multiple restore graphs disagree for '{source_location}'."
            )

    resolved_count = sum(len(manifest["resolved"]) for manifest in manifests.values())
    if resolved_count == 0:
        raise DependencySnapshotError(
            "The restore graph contains no resolved NuGet packages."
        )

    expected_projects = {
        path.relative_to(root).as_posix()
        for path in root.rglob("*.csproj")
        if "artifacts" not in path.relative_to(root).parts
        and path.relative_to(root).parts[:2] != ("eng", "templates")
    }
    missing_projects = expected_projects - manifests.keys()
    unexpected_projects = manifests.keys() - expected_projects
    if missing_projects or unexpected_projects:
        details: list[str] = []
        if missing_projects:
            details.append("missing " + ", ".join(sorted(missing_projects)))
        if unexpected_projects:
            details.append("unexpected " + ", ".join(sorted(unexpected_projects)))
        raise DependencySnapshotError(
            "Restore assets do not match the authored project set: "
            + "; ".join(details)
            + "."
        )

    timestamp = scanned_at or dt.datetime.now(dt.UTC)
    if timestamp.tzinfo is None:
        raise DependencySnapshotError("scanned_at must include a timezone.")

    return {
        "version": 0,
        "job": {
            "correlator": correlator,
            "id": run_id,
        },
        "sha": commit_sha,
        "ref": git_ref,
        "detector": {
            "name": _DETECTOR_NAME,
            "version": _DETECTOR_VERSION,
            "url": _DETECTOR_URL,
        },
        "scanned": timestamp.astimezone(dt.UTC)
        .isoformat()
        .replace(
            "+00:00",
            "Z",
        ),
        "manifests": dict(sorted(manifests.items())),
    }


def submit_snapshot(
    snapshot: Mapping[str, Any],
    api_url: str,
    repository: str,
    token: str,
) -> None:
    """Submit one validated snapshot without exposing its bearer token."""

    if not _REPOSITORY_PATTERN.fullmatch(repository):
        raise DependencySnapshotError("repository must use the 'owner/name' form.")
    if not token.strip():
        raise DependencySnapshotError("GITHUB_TOKEN is required for submission.")

    parsed_api_url = urllib.parse.urlsplit(api_url)
    if (
        parsed_api_url.scheme != "https"
        or not parsed_api_url.netloc
        or parsed_api_url.username is not None
        or parsed_api_url.password is not None
        or parsed_api_url.query
        or parsed_api_url.fragment
    ):
        raise DependencySnapshotError(
            "api_url must be an HTTPS origin without credentials, query, or fragment."
        )

    endpoint = f"{api_url.rstrip('/')}/repos/{repository}/dependency-graph/snapshots"
    request = urllib.request.Request(
        endpoint,
        data=json.dumps(snapshot, separators=(",", ":")).encode("utf-8"),
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
            "User-Agent": "Doka-Dependency-Snapshot/1.0",
            "X-GitHub-Api-Version": "2026-03-10",
        },
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            status = response.status
            response_body = response.read()
    except urllib.error.HTTPError as error:
        raise DependencySnapshotError(
            f"GitHub rejected the dependency snapshot with HTTP {error.code}."
        ) from error
    except urllib.error.URLError as error:
        raise DependencySnapshotError(
            "GitHub dependency submission could not be reached."
        ) from error

    if status != 201:
        raise DependencySnapshotError(
            f"GitHub returned unexpected HTTP status {status}."
        )

    try:
        result = json.loads(response_body).get("result")
    except (json.JSONDecodeError, AttributeError) as error:
        raise DependencySnapshotError(
            "GitHub returned an unreadable dependency-submission response."
        ) from error

    # GitHub documents SUCCESS but can return ACCEPTED while the dependency
    # graph is still propagating. The review job owns the bounded wait for that
    # propagation; every other result remains a submission failure here.
    if result not in _SUCCESSFUL_SUBMISSION_RESULTS:
        raise DependencySnapshotError(
            f"GitHub returned dependency-submission result '{result}'."
        )


def _validate_identity(
    commit_sha: str,
    git_ref: str,
    correlator: str,
    run_id: str,
) -> None:
    if not _COMMIT_PATTERN.fullmatch(commit_sha):
        raise DependencySnapshotError(
            "commit_sha must be a lowercase 40-character Git SHA."
        )
    if not git_ref.startswith("refs/heads/") or git_ref == "refs/heads/":
        raise DependencySnapshotError(
            "git_ref must identify a concrete branch below refs/heads/."
        )
    if not _CORRELATOR_PATTERN.fullmatch(correlator):
        raise DependencySnapshotError(
            "correlator must contain 1-100 safe identifier characters."
        )
    if not run_id.strip():
        raise DependencySnapshotError("run_id must not be empty.")


def _manifest_from_assets(
    repository_root: Path,
    asset_file: Path,
) -> tuple[str, dict[str, Any]]:
    try:
        document = json.loads(asset_file.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise DependencySnapshotError(
            f"Restore graph '{asset_file}' is unreadable."
        ) from error

    if not isinstance(document, dict):
        raise DependencySnapshotError(
            f"Restore graph '{asset_file}' must contain a JSON object."
        )
    if document.get("version") != _PROJECT_ASSETS_VERSION:
        raise DependencySnapshotError(
            f"Restore graph '{asset_file}' uses unsupported project.assets.json "
            f"version '{document.get('version')}'. Expected "
            f"{_PROJECT_ASSETS_VERSION}."
        )

    project = _mapping(document, "project", asset_file)
    restore = _mapping(project, "restore", asset_file)
    project_path_value = restore.get("projectPath")
    if not isinstance(project_path_value, str) or not project_path_value:
        raise DependencySnapshotError(
            f"Restore graph '{asset_file}' has no projectPath."
        )

    project_path = Path(project_path_value)
    if not project_path.is_absolute():
        project_path = repository_root / project_path
    project_path = project_path.resolve()
    try:
        source_location = project_path.relative_to(repository_root).as_posix()
    except ValueError as error:
        raise DependencySnapshotError(
            f"Restore graph '{asset_file}' points outside the repository."
        ) from error
    if not project_path.is_file():
        raise DependencySnapshotError(
            f"Restore graph '{asset_file}' points to a missing project file."
        )

    project_frameworks = _mapping(project, "frameworks", asset_file)
    targets = _mapping(document, "targets", asset_file)
    resolved: dict[str, dict[str, Any]] = {}

    for target_name, raw_target in sorted(targets.items()):
        if not isinstance(raw_target, dict):
            raise DependencySnapshotError(
                f"Target '{target_name}' in '{asset_file}' is not an object."
            )
        framework_name = target_name.split("/", 1)[0]
        framework = project_frameworks.get(framework_name)
        if not isinstance(framework, dict):
            raise DependencySnapshotError(
                f"Target '{target_name}' has no matching project framework."
            )

        _merge_target_packages(
            resolved,
            raw_target,
            framework,
            asset_file,
            target_name,
        )

    return source_location, {
        "name": source_location,
        "file": {"source_location": source_location},
        "resolved": dict(sorted(resolved.items())),
    }


def _merge_target_packages(
    resolved: dict[str, dict[str, Any]],
    target: Mapping[str, Any],
    framework: Mapping[str, Any],
    asset_file: Path,
    target_name: str,
) -> None:
    packages: dict[str, tuple[str, Mapping[str, Any]]] = {}
    for library_key, raw_library in target.items():
        if not isinstance(raw_library, dict) or raw_library.get("type") != "package":
            continue
        package_name, separator, version = library_key.rpartition("/")
        if not separator or not package_name or not version:
            raise DependencySnapshotError(
                f"Package key '{library_key}' in '{asset_file}' is invalid."
            )
        normalized_name = package_name.casefold()
        if normalized_name in packages:
            raise DependencySnapshotError(
                f"Target '{target_name}' resolves '{package_name}' more than once."
            )
        packages[normalized_name] = (
            _package_url(package_name, version),
            raw_library,
        )

    graph: dict[str, set[str]] = {}
    for package_url, raw_library in packages.values():
        dependencies = raw_library.get("dependencies", {})
        if not isinstance(dependencies, dict):
            raise DependencySnapshotError(
                f"Package '{package_url}' has an invalid dependency map."
            )
        graph[package_url] = set()
        for dependency_name in dependencies:
            dependency = packages.get(dependency_name.casefold())
            if dependency is None:
                raise DependencySnapshotError(
                    f"Package '{package_url}' references unresolved package "
                    f"'{dependency_name}'."
                )
            graph[package_url].add(dependency[0])

    dependency_specs = framework.get("dependencies", {})
    if not isinstance(dependency_specs, dict):
        raise DependencySnapshotError(
            f"Framework '{target_name}' has an invalid dependency map."
        )

    runtime_roots: set[str] = set()
    development_roots: set[str] = set()
    direct_packages: set[str] = set()
    for dependency_name, raw_specification in dependency_specs.items():
        if not isinstance(raw_specification, dict):
            raise DependencySnapshotError(
                f"Direct dependency '{dependency_name}' has an invalid contract."
            )
        if raw_specification.get("target", "Package") != "Package":
            continue
        package = packages.get(dependency_name.casefold())
        if package is None:
            raise DependencySnapshotError(
                f"Direct package '{dependency_name}' is absent from "
                f"target '{target_name}'."
            )

        # PrivateAssets=All is how restore records analyzers, build tools, and
        # test packages that must not flow into a consumer. Runtime reachability
        # still wins below when a package is shared with a production root.
        if raw_specification.get("suppressParent") == "All":
            development_roots.add(package[0])
        else:
            runtime_roots.add(package[0])
        direct_packages.add(package[0])

    # Assets reached through ProjectReference nodes are indirect packages for
    # this manifest, but they still form runtime reachability roots. Keeping
    # that distinction prevents examples and tests from losing the provider's
    # transitive graph or misreporting those packages as direct references.
    for raw_library in target.values():
        if not isinstance(raw_library, dict) or raw_library.get("type") != "project":
            continue
        dependencies = raw_library.get("dependencies", {})
        if not isinstance(dependencies, dict):
            raise DependencySnapshotError(
                f"Project target in '{asset_file}' has an invalid dependency map."
            )
        for dependency_name in dependencies:
            package = packages.get(dependency_name.casefold())
            if package is not None:
                runtime_roots.add(package[0])

    runtime_packages = _reachable_packages(runtime_roots, graph)
    development_packages = _reachable_packages(development_roots, graph)
    unclassified = set(graph) - runtime_packages - development_packages
    if unclassified:
        raise DependencySnapshotError(
            f"Target '{target_name}' contains packages with no direct root: "
            f"{', '.join(sorted(unclassified))} in '{asset_file}'."
        )

    for package_url, dependencies in graph.items():
        scope = "runtime" if package_url in runtime_packages else "development"
        relationship = "direct" if package_url in direct_packages else "indirect"
        candidate = {
            "package_url": package_url,
            "relationship": relationship,
            "scope": scope,
            "dependencies": sorted(dependencies),
        }
        existing = resolved.get(package_url)
        if existing is None:
            resolved[package_url] = candidate
            continue

        existing["dependencies"] = sorted(set(existing["dependencies"]) | dependencies)
        if relationship == "direct":
            existing["relationship"] = "direct"
        if scope == "runtime":
            existing["scope"] = "runtime"


def _reachable_packages(
    roots: Iterable[str],
    graph: Mapping[str, set[str]],
) -> set[str]:
    reachable: set[str] = set()
    pending = list(roots)
    while pending:
        package = pending.pop()
        if package in reachable:
            continue
        reachable.add(package)
        pending.extend(graph.get(package, ()))
    return reachable


def _package_url(package_name: str, version: str) -> str:
    encoded_name = urllib.parse.quote(package_name, safe="._-~")
    encoded_version = urllib.parse.quote(version, safe="._-~")
    return f"pkg:nuget/{encoded_name}@{encoded_version}"


def _mapping(
    document: Mapping[str, Any],
    key: str,
    asset_file: Path,
) -> Mapping[str, Any]:
    value = document.get(key)
    if not isinstance(value, dict):
        raise DependencySnapshotError(
            f"Restore graph '{asset_file}' has no valid '{key}' object."
        )
    return value


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Submit a NuGet restore graph to GitHub dependency review."
    )
    parser.add_argument("--repo-root", type=Path, default=Path("."))
    parser.add_argument("--assets-root", type=Path, required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--ref", required=True)
    parser.add_argument("--correlator", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--api-url", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--submit", action="store_true")
    return parser


def main(argv: list[str] | None = None) -> int:
    """Generate the canonical payload and optionally submit it to GitHub."""

    args = _parser().parse_args(argv)
    try:
        snapshot = create_snapshot(
            args.repo_root,
            args.assets_root,
            args.sha,
            args.ref,
            args.correlator,
            args.run_id,
        )
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(snapshot, indent=2, sort_keys=True) + "\n",
            encoding="ascii",
        )

        package_count = sum(
            len(manifest["resolved"]) for manifest in snapshot["manifests"].values()
        )
        print(
            "Generated dependency snapshot with "
            f"{len(snapshot['manifests'])} manifest(s) and "
            f"{package_count} resolved package entries."
        )

        if args.submit:
            submit_snapshot(
                snapshot,
                args.api_url,
                args.repository,
                os.environ.get("GITHUB_TOKEN", ""),
            )
            print("Dependency snapshot accepted by GitHub.")
    except (DependencySnapshotError, OSError) as error:
        print(f"Dependency snapshot failed: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
