#!/usr/bin/env python3
"""Generate and verify the portable release-candidate evidence manifest.

Generation binds release artifacts to the clean, tagged source checkout, the
resolved dependency graph, and digest-pinned database images. Verification is
an independent readback: it rejects changes to the manifest, its inventory, or
any retained artifact before the evidence can be published.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import subprocess
import sys
from datetime import UTC, datetime
from pathlib import Path
from typing import Any


SCHEMA_VERSION = 1
MANIFEST_NAME = "release-candidate-evidence.json"
CHECKSUM_NAME = "release-candidate-evidence.sha256"
REQUIRED_ENGINE_TARGETS = ("mariadb114", "mariadb118", "mysql84")
REQUIRED_PACKAGES = (
    "Microsoft.EntityFrameworkCore.Design",
    "Microsoft.EntityFrameworkCore.Relational",
    "MySqlConnector",
)
INTEGRATION_MATRIX_EVIDENCE = Path("integration/compatibility-matrix-evidence.json")
SEMANTIC_VERSION_TAG = re.compile(r"v[0-9]+[.][0-9]+[.][0-9]+(?:[-.][0-9A-Za-z.-]+)?")


class EvidenceError(RuntimeError):
    """Raised when release evidence cannot prove its declared identity."""


def run_command(*arguments: str, cwd: Path | None = None) -> str:
    """Run a read-only identity probe and return its normalized output.

    This helper is intentionally reserved for Git and toolchain inspection.
    Evidence verification must never mutate the checkout it is attesting.
    """
    result = subprocess.run(
        arguments,
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        message = result.stderr.strip() or result.stdout.strip()
        raise EvidenceError(f"Command failed ({' '.join(arguments)}): {message}")
    return result.stdout.strip()


def sha256(path: Path) -> str:
    """Return the SHA-256 digest for one evidence file without buffering it."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def portable_path(path: Path, root: Path) -> str:
    """Return a POSIX relative path and reject evidence outside its root."""
    try:
        relative = path.resolve().relative_to(root.resolve())
    except ValueError as exception:
        raise EvidenceError(f"Evidence path escapes its root: {path}") from exception
    return relative.as_posix()


def artifact_role(relative_path: str) -> str:
    """Classify an artifact so consumers can select evidence without path guesses."""
    path = Path(relative_path)
    if path.parts and path.parts[0] == "packages":
        return "symbol-package" if path.suffix == ".snupkg" else "package"
    if path.parts and path.parts[0] == "sbom":
        return "sbom"
    if path.parts and path.parts[0] == "audit":
        return "vulnerability-audit"
    if path.name == "resolved-packages.json":
        return "dependency-graph"
    if path.name == "test-database-evidence.json":
        return "engine-evidence"
    return "verification-evidence"


def collect_artifacts(root: Path) -> list[dict[str, Any]]:
    """Build a complete, portable inventory of regular evidence files.

    Symbolic links are rejected because their target may resolve differently
    after upload. The manifest and checksum are excluded to avoid recursive
    self-hashing; the detached checksum binds the manifest separately.
    """
    artifacts: list[dict[str, Any]] = []
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise EvidenceError(f"Symbolic links are not allowed in release evidence: {path}")
        if not path.is_file() or path.name in (MANIFEST_NAME, CHECKSUM_NAME):
            continue
        relative = portable_path(path, root)
        artifacts.append(
            {
                "path": relative,
                "role": artifact_role(relative),
                "sha256": sha256(path),
                "sizeBytes": path.stat().st_size,
            }
        )
    if not artifacts:
        raise EvidenceError("The release evidence root contains no artifacts.")
    return artifacts


def collect_dependencies(path: Path) -> dict[str, str]:
    """Read back exact dependency versions required by the support contract.

    Every required package must resolve to one version across all reported
    frameworks. Missing or ambiguous resolution is evidence failure, not a
    best-effort inventory.
    """
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(f"Unable to read the resolved dependency graph: {path}") from exception

    resolved: dict[str, set[str]] = {package: set() for package in REQUIRED_PACKAGES}
    for project in payload.get("projects", []):
        for framework in project.get("frameworks", []):
            packages = framework.get("topLevelPackages", []) + framework.get("transitivePackages", [])
            for package in packages:
                package_id = package.get("id")
                if package_id in resolved and package.get("resolvedVersion"):
                    resolved[package_id].add(package["resolvedVersion"])

    result: dict[str, str] = {}
    for package_id, versions in resolved.items():
        if len(versions) != 1:
            rendered = ", ".join(sorted(versions)) or "missing"
            raise EvidenceError(f"Expected one resolved {package_id} version, found: {rendered}")
        result[package_id] = next(iter(versions))
    return result


def collect_engines(root: Path) -> list[dict[str, str]]:
    """Collect the complete digest-pinned engine matrix.

    Specification and integration runs may report the same target. Repeated
    identities must agree exactly, every advertised target must be present,
    and ephemeral connection data is deliberately excluded from the manifest.
    """
    engines: dict[str, dict[str, str]] = {}
    for path in root.rglob("test-database-evidence.json"):
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exception:
            raise EvidenceError(f"Unable to read engine evidence: {path}") from exception
        if payload.get("lifecycleState") != "cleanup-completed":
            raise EvidenceError(f"Engine lifecycle did not complete cleanly: {path}")
        for target in payload.get("targets", []):
            target_id = target.get("targetId")
            identity = {
                "targetId": target_id,
                "engine": target.get("engine"),
                "serverVersionToken": target.get("serverVersionToken"),
                "source": target.get("source"),
                "image": target.get("image"),
            }
            if not all(identity.values()):
                raise EvidenceError(f"Engine evidence is missing an immutable identity: {path}")
            _, separator, digest = identity["image"].rpartition("@sha256:")
            if separator == "" or len(digest) != 64 or any(character not in "0123456789abcdef" for character in digest):
                raise EvidenceError(f"Engine image is not digest-pinned: {identity['image']}")
            previous = engines.setdefault(target_id, identity)
            if previous != identity:
                raise EvidenceError(f"Conflicting engine identities were recorded for {target_id}.")

    missing = sorted(set(REQUIRED_ENGINE_TARGETS) - set(engines))
    if missing:
        raise EvidenceError(f"Missing engine evidence for: {', '.join(missing)}")
    unexpected = sorted(set(engines) - set(REQUIRED_ENGINE_TARGETS))
    if unexpected:
        raise EvidenceError(f"Unexpected engine evidence for: {', '.join(unexpected)}")
    return [engines[target] for target in sorted(engines)]


def validate_integration_configuration_matrix(root: Path) -> dict[str, Any]:
    """Require unfiltered successful integration evidence for every engine.

    Engine lifecycle records alone prove which containers existed, but they do
    not prove which test categories ran. The integration runner's own evidence
    closes that distinction and prevents a filtered smoke run from being
    sealed as release-candidate configuration and failure coverage.
    """
    path = root / INTEGRATION_MATRIX_EVIDENCE
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(f"Unable to read integration matrix evidence: {path}") from exception

    if payload.get("fullConfigurationMatrixRequired") is not True:
        raise EvidenceError("Integration evidence is not marked as the required full configuration matrix.")
    if payload.get("testFilter") != "":
        raise EvidenceError("Release integration evidence must not contain a test filter.")
    if payload.get("testExitCode") != 0:
        raise EvidenceError("The full integration configuration matrix did not pass.")

    targets = tuple(sorted(filter(None, str(payload.get("targetSelection", "")).split(","))))
    if targets != REQUIRED_ENGINE_TARGETS:
        raise EvidenceError(
            "Full integration matrix target mismatch. "
            f"Expected={list(REQUIRED_ENGINE_TARGETS)}; actual={list(targets)}"
        )

    return {
        "targets": list(targets),
        "testFilter": "",
        "fullConfigurationMatrixRequired": True,
        "testExitCode": 0,
    }


def validate_release_packages(artifacts: list[dict[str, Any]], release_version: str) -> None:
    """Require exactly the two version-aligned packages and symbol packages.

    Exact inventory matching prevents stale packages from an earlier run from
    acquiring the current source and workflow identity.
    """
    package_prefix = "packages/"
    expected = {
        f"{package_prefix}Doka.EntityFrameworkCore.MySql.{release_version}.nupkg",
        f"{package_prefix}Doka.EntityFrameworkCore.MySql.{release_version}.snupkg",
        f"{package_prefix}Doka.EntityFrameworkCore.MySql.NetTopologySuite.{release_version}.nupkg",
        f"{package_prefix}Doka.EntityFrameworkCore.MySql.NetTopologySuite.{release_version}.snupkg",
    }
    actual = {
        artifact["path"]
        for artifact in artifacts
        if artifact["role"] in ("package", "symbol-package")
    }
    if actual != expected:
        missing = sorted(expected - actual)
        unexpected = sorted(actual - expected)
        raise EvidenceError(f"Release package inventory mismatch. Missing={missing}; unexpected={unexpected}")


def git_source(repo: Path, release_version: str, expected_ref: str, require_tag: bool) -> dict[str, Any]:
    """Validate and return the immutable Git identity for the release source.

    Hosted workflow variables are checked against the checkout rather than
    trusted as declarations. Tagged releases require one unambiguous semantic
    version tag whose ref and package version agree.
    """
    commit = run_command("git", "rev-parse", "HEAD", cwd=repo)
    dirty = run_command("git", "status", "--porcelain", "--untracked-files=all", cwd=repo)
    if dirty:
        raise EvidenceError("Release evidence requires a clean Git worktree.")

    github_sha = os.environ.get("GITHUB_SHA", "")
    if github_sha and github_sha != commit:
        raise EvidenceError(f"GITHUB_SHA {github_sha} does not match checked-out commit {commit}.")

    exact_tags = run_command("git", "tag", "--points-at", commit, cwd=repo).splitlines()
    version_tag = f"v{release_version}"
    version_tags = sorted(tag for tag in exact_tags if SEMANTIC_VERSION_TAG.fullmatch(tag))
    tag = version_tag if version_tag in version_tags else ""
    if require_tag and version_tags != [version_tag]:
        rendered = ", ".join(version_tags) or "none"
        raise EvidenceError(
            f"Release source requires exactly semantic version tag {version_tag}; found: {rendered}."
        )

    actual_ref = os.environ.get("GITHUB_REF", "")
    if not actual_ref:
        actual_ref = (
            f"refs/tags/{tag}"
            if require_tag and tag
            else run_command("git", "symbolic-ref", "-q", "HEAD", cwd=repo)
        )
    if expected_ref and actual_ref != expected_ref:
        raise EvidenceError(f"Expected release ref {expected_ref}, found {actual_ref}.")
    if require_tag and actual_ref != f"refs/tags/{tag}":
        raise EvidenceError(f"Release evidence must run from refs/tags/{tag}, found {actual_ref}.")

    remote = run_command("git", "config", "--get", "remote.origin.url", cwd=repo)
    return {
        "repository": remote,
        "commit": commit,
        "ref": actual_ref,
        "tag": tag or None,
        "treeState": "clean",
    }


def workflow_identity(run_id: str) -> dict[str, Any]:
    """Capture hosted workflow and runner identities or an explicit local identity.

    A hosted run fails closed when GitHub omits an identity field needed to
    locate and reproduce the evidence-producing execution.
    """
    hosted = os.environ.get("GITHUB_ACTIONS") == "true"
    identity = {
        "provider": "github-actions" if hosted else "local",
        "runId": os.environ.get("GITHUB_RUN_ID", run_id),
        "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT", "1"),
        "workflow": os.environ.get("GITHUB_WORKFLOW", "local-release-candidate"),
        "workflowRef": os.environ.get("GITHUB_WORKFLOW_REF", "local"),
        "repository": os.environ.get("GITHUB_REPOSITORY", "local"),
        "runnerClass": os.environ.get("DOKA_BENCHMARK_RUNNER_CLASS", "local"),
        "runnerOs": os.environ.get("RUNNER_OS", platform.system()),
        "runnerArch": os.environ.get("RUNNER_ARCH", platform.machine()),
    }
    if hosted:
        required = ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_WORKFLOW_REF", "GITHUB_REPOSITORY")
        missing = [name for name in required if not os.environ.get(name)]
        if missing:
            raise EvidenceError(f"Hosted workflow identity is incomplete: {', '.join(missing)}")
    return identity


def write_manifest(args: argparse.Namespace) -> None:
    """Generate a canonical manifest and detached checksum.

    Source identity and all release-contract inventories are validated before
    either file is written. This ordering prevents a partial manifest from
    looking like successful release evidence after a validation failure.
    """
    repo = args.repo.resolve()
    root = args.root.resolve()
    root.mkdir(parents=True, exist_ok=True)
    dependency_graph = args.dependency_graph.resolve()
    if not dependency_graph.is_relative_to(root):
        raise EvidenceError("The dependency graph must live inside the evidence root.")

    source = git_source(repo, args.release_version, args.expected_ref, args.require_tag)
    artifacts = collect_artifacts(root)
    validate_release_packages(artifacts, args.release_version)
    engines = collect_engines(root)
    integration_matrix = validate_integration_configuration_matrix(root)
    dependencies = collect_dependencies(dependency_graph)
    roles: dict[str, int] = {}
    for artifact in artifacts:
        roles[artifact["role"]] = roles.get(artifact["role"], 0) + 1
    if roles.get("package", 0) < 2 or roles.get("symbol-package", 0) < 2:
        raise EvidenceError("Both release packages and both symbol packages are required.")
    if roles.get("sbom", 0) < 1:
        raise EvidenceError("At least one SBOM artifact is required.")

    manifest = {
        "schemaVersion": SCHEMA_VERSION,
        "generatedUtc": datetime.now(UTC).isoformat(),
        "releaseCandidateRunId": args.run_id,
        "releaseVersion": args.release_version,
        "source": source,
        "workflow": workflow_identity(args.run_id),
        "toolchain": {
            "dotnetSdk": run_command("dotnet", "--version", cwd=repo),
            "resolvedPackages": dependencies,
        },
        "engines": engines,
        "integrationConfigurationMatrix": integration_matrix,
        "artifacts": artifacts,
        "artifactCountsByRole": dict(sorted(roles.items())),
    }

    manifest_path = root / MANIFEST_NAME
    checksum_path = root / CHECKSUM_NAME
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    checksum_path.write_text(f"{sha256(manifest_path)}  {MANIFEST_NAME}\n", encoding="ascii")


def verify_manifest(root: Path, repo: Path | None) -> None:
    """Independently verify identity, inventory, size, and hash integrity.

    Verification compares both directions: every declared artifact must still
    match, and every current evidence file must have been declared. Supplying a
    repository additionally rebinds the manifest to the live clean checkout.
    """
    root = root.resolve()
    manifest_path = root / MANIFEST_NAME
    checksum_path = root / CHECKSUM_NAME
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        checksum_parts = checksum_path.read_text(encoding="ascii").strip().split()
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError("The release evidence manifest or checksum is unreadable.") from exception

    if manifest.get("schemaVersion") != SCHEMA_VERSION:
        raise EvidenceError(f"Unsupported release evidence schema: {manifest.get('schemaVersion')}")
    if checksum_parts != [sha256(manifest_path), MANIFEST_NAME]:
        raise EvidenceError("The detached release evidence checksum does not match the manifest.")

    expected = manifest.get("artifacts", [])
    if expected != sorted(expected, key=lambda artifact: artifact.get("path", "")):
        raise EvidenceError("Manifest artifacts are not in canonical path order.")
    expected_paths: set[str] = set()
    for artifact in expected:
        relative = artifact.get("path", "")
        if not relative or relative in expected_paths or Path(relative).is_absolute() or ".." in Path(relative).parts:
            raise EvidenceError(f"Manifest contains an invalid artifact path: {relative}")
        expected_paths.add(relative)
        path = root / relative
        if not path.is_file() or path.is_symlink():
            raise EvidenceError(f"Manifest artifact is missing or non-regular: {relative}")
        if path.stat().st_size != artifact.get("sizeBytes") or sha256(path) != artifact.get("sha256"):
            raise EvidenceError(f"Manifest artifact integrity check failed: {relative}")

    current_paths = {
        portable_path(path, root)
        for path in root.rglob("*")
        if path.is_file() and path.name not in (MANIFEST_NAME, CHECKSUM_NAME)
    }
    if current_paths != expected_paths:
        missing = sorted(expected_paths - current_paths)
        untracked = sorted(current_paths - expected_paths)
        raise EvidenceError(f"Evidence inventory drift. Missing={missing}; untracked={untracked}")

    if repo is not None:
        source = manifest.get("source", {})
        current_commit = run_command("git", "rev-parse", "HEAD", cwd=repo)
        if source.get("commit") != current_commit:
            raise EvidenceError("The manifest source commit does not match the current checkout.")
        if run_command("git", "status", "--porcelain", "--untracked-files=all", cwd=repo):
            raise EvidenceError("Manifest verification requires a clean Git worktree.")
        source_tag = source.get("tag")
        current_tags = run_command("git", "tag", "--points-at", current_commit, cwd=repo).splitlines()
        if source_tag and source_tag not in current_tags:
            raise EvidenceError("The manifest release tag no longer identifies the current commit.")
        github_ref = os.environ.get("GITHUB_REF", "")
        if github_ref and source.get("ref") != github_ref:
            raise EvidenceError("The manifest source ref does not match the hosted workflow ref.")


def parse_arguments() -> argparse.Namespace:
    """Parse the generate or verify command-line contract."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    generate = subparsers.add_parser("generate", help="Generate release evidence.")
    generate.add_argument("--repo", type=Path, required=True)
    generate.add_argument("--root", type=Path, required=True)
    generate.add_argument("--run-id", required=True)
    generate.add_argument("--release-version", required=True)
    generate.add_argument("--dependency-graph", type=Path, required=True)
    generate.add_argument("--expected-ref", default="")
    generate.add_argument("--require-tag", action="store_true")

    verify = subparsers.add_parser("verify", help="Verify existing release evidence.")
    verify.add_argument("--root", type=Path, required=True)
    verify.add_argument("--repo", type=Path)

    return parser.parse_args()


def main() -> int:
    """Run the requested evidence operation with concise operator diagnostics."""
    args = parse_arguments()
    try:
        if args.command == "generate":
            write_manifest(args)
            verify_manifest(args.root, args.repo)
        else:
            verify_manifest(args.root, args.repo)
    except EvidenceError as exception:
        print(f"Release evidence failed: {exception}", file=sys.stderr)
        return 1
    print(f"Release evidence {args.command} passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
