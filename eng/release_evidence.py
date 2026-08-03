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
import shutil
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
LIVE_EXAMPLE_MATRIX_EVIDENCE = Path("integration/examples/live-example-matrix-evidence.json")
RUNTIME_POSTURE_EVIDENCE = Path("runtime/runtime-posture-evidence.json")
RECONCILIATION_EVIDENCE = Path("release-candidate-reconciliation.json")
PERFORMANCE_REUSE_EVIDENCE = Path("performance/reuse-evidence.json")
PERFORMANCE_TARGETS = ("mariadb118", "mysql84")
PERFORMANCE_INPUT_PREFIXES = ("benchmarks/", "docker/", "src/")
PERFORMANCE_INPUT_FILES = {
    ".config/dotnet-tools.json",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "NuGet.config",
    "global.json",
    "eng/benchmark.sh",
    "eng/check-benchmark-ratios.sh",
    "eng/performance_evidence.py",
    "eng/verify-dotnet.sh",
}
REQUIRED_LIVE_EXAMPLES = (
    "BulkOperations",
    "CharSetAndCollation",
    "CrudOperations",
    "DockerIntegration",
    "GeneratedColumns",
    "GettingStarted",
    "GuidFormats",
    "InheritancePatterns",
    "JsonColumns",
    "MultiTenancy",
    "PerformanceBestPractices",
    "Relationships",
    "SpatialQueries",
)
REQUIRED_RECONCILIATION_GATES = (
    "source-identity",
    "adr-validation",
    "repository-quality",
    "repository-tests",
    "live-specification",
    "integration-configuration-failure",
    "live-examples",
    "migration-deployment",
    "runtime-full-trim",
    "coverage-union",
    "package-contract",
    "vulnerability-audit",
    "sbom",
    "performance-memory",
    "publication-readiness",
)
SEMANTIC_VERSION_TAG = re.compile(r"v[0-9]+[.][0-9]+[.][0-9]+(?:[-.][0-9A-Za-z.-]+)?")
SHA256_DIGEST = re.compile(r"[0-9a-f]{64}")


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


def approved_dotnet_sdk(repo: Path) -> str:
    """Return the exact SDK identity approved by the repository contract."""
    path = repo / "global.json"

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(f"Unable to read the repository SDK contract: {path}") from exception

    sdk = payload.get("sdk", {})
    version = sdk.get("version")

    if not isinstance(version, str) or re.fullmatch(r"[0-9]+[.][0-9]+[.][0-9]+", version) is None:
        raise EvidenceError("global.json must declare one exact stable .NET SDK version.")
    if sdk.get("rollForward") != "disable":
        raise EvidenceError("global.json must disable .NET SDK roll-forward.")
    if sdk.get("allowPrerelease") is not False:
        raise EvidenceError("global.json must reject prerelease .NET SDK selection.")

    return version


def sha256(path: Path) -> str:
    """Return the SHA-256 digest for one evidence file without buffering it."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def clean_performance_source_hash(commit: str) -> str:
    """Return the benchmark source hash for an unchanged commit checkout."""
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise EvidenceError(f"Invalid performance source commit: {commit}")

    digest = hashlib.sha256()
    digest.update(b"doka-performance-source-v1\0")
    digest.update(commit.encode("ascii"))
    digest.update(b"\0tracked-patch\0")
    return digest.hexdigest()


def changed_paths(repo: Path, base_commit: str, current_commit: str) -> list[str]:
    """Return the canonical tracked delta between two repository commits."""
    output = run_command(
        "git",
        "diff",
        "--name-only",
        "--diff-filter=ACDMRTUXB",
        f"{base_commit}..{current_commit}",
        "--",
        cwd=repo,
    )
    return sorted(path for path in output.splitlines() if path)


def is_performance_input(path: str) -> bool:
    """Return whether a repository path can affect measured provider behavior."""
    if path in PERFORMANCE_INPUT_FILES:
        return True
    if path.startswith(PERFORMANCE_INPUT_PREFIXES):
        return True

    # Root build configuration is performance input even when a new file did
    # not exist when this contract was authored.
    return "/" not in path and (
        path.startswith("Directory.Build.")
        or path.endswith((".sln", ".slnx"))
    )


def commit_is_ancestor(repo: Path, ancestor: str, descendant: str) -> bool:
    """Return whether Git proves a direct ancestry relation between commits."""
    result = subprocess.run(
        ("git", "merge-base", "--is-ancestor", ancestor, descendant),
        cwd=repo,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode in (0, 1):
        return result.returncode == 0

    message = result.stderr.strip() or result.stdout.strip()
    raise EvidenceError(f"Unable to validate performance evidence ancestry: {message}")


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
    # pathlib sorts by path components, which places a directory named
    # ``sbom`` before its sibling ``sbom-components``. Manifest verification
    # uses the portable path string as its canonical ordering contract, so the
    # inventory must apply that same comparison while it is constructed.
    for path in sorted(
        root.rglob("*"),
        key=lambda candidate: portable_path(candidate, root),
    ):
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


def validate_live_example_matrix(
    root: Path,
    run_id: str,
    engines: list[dict[str, str]],
) -> dict[str, Any]:
    """Require every public live example to pass on every supported engine.

    Building examples protects API compatibility. This evidence additionally
    proves that each advertised scenario completed its runtime invariant and
    that the test-owned database resources were removed afterwards.
    """
    path = root / LIVE_EXAMPLE_MATRIX_EVIDENCE
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(f"Unable to read live example matrix evidence: {path}") from exception

    expected_pairs = {
        (target, example)
        for target in REQUIRED_ENGINE_TARGETS
        for example in REQUIRED_LIVE_EXAMPLES
    }
    expected_count = len(expected_pairs)
    if payload.get("schemaVersion") != 1 or payload.get("runId") != run_id:
        raise EvidenceError("Live example evidence does not match the release-candidate run.")
    if (
        payload.get("expectedCount") != expected_count
        or payload.get("completedCount") != expected_count
        or payload.get("passedCount") != expected_count
        or payload.get("failedCount") != 0
        or payload.get("matrixExitCode") != 0
    ):
        raise EvidenceError("The complete live example matrix did not pass.")

    cleanup = payload.get("cleanup", {})
    if (
        cleanup.get("completed") is not True
        or cleanup.get("exitCode") != 0
        or cleanup.get("volumesRemoved") is not True
    ):
        raise EvidenceError("Live example resources were not completely removed.")

    expected_images = {engine["targetId"]: engine["image"] for engine in engines}
    actual_targets: set[str] = set()
    for engine in payload.get("engines", []):
        if not isinstance(engine, dict):
            raise EvidenceError("Live example evidence contains an invalid engine entry.")

        target = engine.get("target")
        image_reference = engine.get("imageReference")
        image_id = engine.get("imageId")
        endpoint = engine.get("endpoint")
        if target in actual_targets or target not in expected_images:
            raise EvidenceError(f"Live example evidence contains an unexpected target: {target}")
        if image_reference != expected_images[target]:
            raise EvidenceError(f"Live example image identity conflicts for {target}.")

        _, separator, digest = str(image_reference).rpartition("@sha256:")
        if (
            separator == ""
            or not SHA256_DIGEST.fullmatch(digest)
            or not re.fullmatch(r"sha256:[0-9a-f]{64}", str(image_id))
        ):
            raise EvidenceError(f"Live example image ID is not immutable for {target}.")
        if not isinstance(endpoint, str) or not re.fullmatch(r"127[.]0[.]0[.]1:[0-9]{1,5}", endpoint):
            raise EvidenceError(f"Live example endpoint is invalid for {target}.")

        actual_targets.add(target)

    if actual_targets != set(REQUIRED_ENGINE_TARGETS):
        raise EvidenceError("Live example evidence does not cover every supported engine.")

    actual_pairs: set[tuple[str, str]] = set()
    results = payload.get("results")
    if not isinstance(results, list) or len(results) != expected_count:
        raise EvidenceError("Live example evidence contains an incomplete result inventory.")

    for result in results:
        if not isinstance(result, dict):
            raise EvidenceError("Live example evidence contains an invalid result entry.")

        pair = (result.get("target"), result.get("example"))
        if pair in actual_pairs or pair not in expected_pairs:
            raise EvidenceError(f"Live example evidence contains an unexpected result: {pair}")
        if result.get("exitCode") != 0 or result.get("status") != "pass":
            raise EvidenceError(f"Live example did not pass: {pair}")
        actual_pairs.add(pair)

    if actual_pairs != expected_pairs:
        raise EvidenceError("Live example evidence does not cover every required scenario.")

    return {
        "targets": list(REQUIRED_ENGINE_TARGETS),
        "examples": list(REQUIRED_LIVE_EXAMPLES),
        "runCount": expected_count,
        "cleanupCompleted": True,
    }


def validate_runtime_posture(
    root: Path,
    run_id: str,
    source_commit: str,
) -> dict[str, Any]:
    """Require an executed ordinary and full-trim runtime contract.

    A successful publish is insufficient: the evidence must bind the executed
    host-specific binary, immutable engine image, and clean source identity to
    the same run that owns the release candidate.
    """
    path = root / RUNTIME_POSTURE_EVIDENCE
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(f"Unable to read runtime posture evidence: {path}") from exception

    if payload.get("schemaVersion") != 1 or payload.get("runId") != run_id:
        raise EvidenceError("Runtime posture evidence does not match the release-candidate run.")

    source = payload.get("source", {})
    if source.get("commit") != source_commit or source.get("treeState") != "clean":
        raise EvidenceError("Runtime posture evidence does not bind the clean release source.")

    target = payload.get("target", {})
    target_image = target.get("image", "")
    _, separator, image_digest = target_image.rpartition("@sha256:")
    if (
        target.get("targetId") != "mysql84"
        or separator == ""
        or not SHA256_DIGEST.fullmatch(image_digest)
    ):
        raise EvidenceError("Runtime posture evidence does not bind the digest-pinned MySQL 8.4 target.")

    publish = payload.get("publish", {})
    if (
        payload.get("configuration") != "Release"
        or payload.get("ordinaryExecution") != "pass"
        or publish.get("selfContained") is not True
        or publish.get("publishTrimmed") is not True
        or publish.get("trimMode") != "full"
        or publish.get("status") != "pass"
        or payload.get("trimmedExecution") != "pass"
    ):
        raise EvidenceError("Runtime posture evidence does not prove the complete full-trim execution contract.")

    executable = payload.get("executable", {})
    if (
        not SHA256_DIGEST.fullmatch(str(executable.get("sha256", "")))
        or not isinstance(executable.get("sizeBytes"), int)
        or executable["sizeBytes"] <= 0
    ):
        raise EvidenceError("Runtime posture evidence does not bind the executed trimmed binary.")

    runtime_identifier = payload.get("runtimeIdentifier")
    dotnet_sdk = payload.get("dotnetSdk")
    if not isinstance(runtime_identifier, str) or not runtime_identifier:
        raise EvidenceError("Runtime posture evidence is missing its runtime identifier.")
    if not isinstance(dotnet_sdk, str) or not dotnet_sdk:
        raise EvidenceError("Runtime posture evidence is missing its .NET SDK identity.")

    return {
        "targetId": "mysql84",
        "image": target_image,
        "runtimeIdentifier": runtime_identifier,
        "dotnetSdk": dotnet_sdk,
        "publishTrimmed": True,
        "trimMode": "full",
        "selfContained": True,
        "ordinaryExecution": "pass",
        "trimmedExecution": "pass",
        "executableSha256": executable["sha256"],
        "executableSizeBytes": executable["sizeBytes"],
    }


def load_performance_evaluation(root: Path, target: str) -> tuple[Path, dict[str, Any]]:
    """Load and structurally validate one strict scorecard evaluation."""
    path = root / "performance" / target / "evidence" / "gate-performance-evaluation.json"
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(f"Unable to read performance evaluation: {path}") from exception

    if (
        payload.get("schemaVersion") != 3
        or payload.get("target") != target
        or payload.get("profile") != "scorecard"
        or payload.get("mode") != "compare"
        or payload.get("success") is not True
    ):
        raise EvidenceError(f"Performance evaluation is not a passing strict scorecard: {path}")

    run_id = payload.get("runId")
    commit = payload.get("commit")
    source_hash = payload.get("sourceHash")
    if not isinstance(run_id, str) or not run_id:
        raise EvidenceError(f"Performance evaluation is missing its run identity: {path}")
    if not isinstance(commit, str) or not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise EvidenceError(f"Performance evaluation is missing its source commit: {path}")
    if source_hash != clean_performance_source_hash(commit):
        raise EvidenceError(f"Performance evaluation does not bind a clean source checkout: {path}")

    target_root = path.parents[1]
    artifact_hashes = payload.get("artifactHashes")
    if not isinstance(artifact_hashes, dict):
        raise EvidenceError(f"Performance evaluation is missing artifact hashes: {path}")

    evidence_artifacts = {
        "benchmarkDotNet": target_root / "evidence" / "gate-benchmarkdotnet-evidence.json",
        "hostPreflight": target_root / "evidence" / "host-preflight.json",
        "soak": target_root / "evidence" / "soak-evidence.json",
        "workloads": target_root / "evidence" / "workload-evidence.json",
    }
    for artifact_id, artifact_path in evidence_artifacts.items():
        if (
            not artifact_path.is_file()
            or artifact_path.is_symlink()
            or artifact_hashes.get(artifact_id) != sha256(artifact_path)
        ):
            raise EvidenceError(
                f"Performance evaluation artifact '{artifact_id}' failed integrity validation: {artifact_path}"
            )

    raw_reports = payload.get("rawReports")
    if not isinstance(raw_reports, list) or not raw_reports:
        raise EvidenceError(f"Performance evaluation contains no raw benchmark reports: {path}")
    for report in raw_reports:
        if not isinstance(report, dict):
            raise EvidenceError(f"Performance evaluation contains an invalid raw report entry: {path}")
        relative_path = report.get("path")
        if not isinstance(relative_path, str) or not relative_path:
            raise EvidenceError(f"Performance evaluation contains a raw report without a path: {path}")
        report_path = target_root / relative_path
        try:
            report_path.resolve().relative_to(target_root.resolve())
        except ValueError as exception:
            raise EvidenceError(f"Performance raw report escapes its target root: {relative_path}") from exception
        if (
            not report_path.is_file()
            or report_path.is_symlink()
            or report.get("sha256") != sha256(report_path)
        ):
            raise EvidenceError(f"Performance raw report failed integrity validation: {report_path}")

    return path, payload


def validate_performance_evidence(
    root: Path,
    repo: Path,
    run_id: str,
    source_commit: str,
) -> dict[str, Any]:
    """Validate fresh or explicitly reusable performance and memory evidence."""
    evaluations = {
        target: load_performance_evaluation(root, target)
        for target in PERFORMANCE_TARGETS
    }
    evaluation_payloads = [payload for _, payload in evaluations.values()]
    measured_commits = {payload["commit"] for payload in evaluation_payloads}
    measured_source_hashes = {payload["sourceHash"] for payload in evaluation_payloads}
    measured_run_ids = {payload["runId"] for payload in evaluation_payloads}
    if len(measured_commits) != 1 or len(measured_source_hashes) != 1 or len(measured_run_ids) != 1:
        raise EvidenceError("Performance targets do not share one source and run identity.")

    measured_commit = next(iter(measured_commits))
    measured_source_hash = next(iter(measured_source_hashes))
    measured_run_id = next(iter(measured_run_ids))
    reuse_path = root / PERFORMANCE_REUSE_EVIDENCE

    if measured_commit == source_commit:
        if measured_run_id != run_id:
            raise EvidenceError("Fresh performance evidence does not match the release-candidate run.")
        if reuse_path.exists():
            raise EvidenceError("Fresh performance evidence must not contain a reuse receipt.")
        return {
            "reused": False,
            "measuredCommit": measured_commit,
            "measuredRunId": measured_run_id,
            "targets": list(PERFORMANCE_TARGETS),
        }

    try:
        receipt = json.loads(reuse_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError("Performance evidence from another commit requires a valid reuse receipt.") from exception

    if (
        receipt.get("schemaVersion") != 1
        or receipt.get("runId") != run_id
        or receipt.get("sourceCommit") != source_commit
        or receipt.get("reusedRunId") != measured_run_id
        or receipt.get("reusedSourceCommit") != measured_commit
        or receipt.get("reusedSourceHash") != measured_source_hash
    ):
        raise EvidenceError("Performance reuse receipt does not match the candidate and measured source identities.")
    if not commit_is_ancestor(repo, measured_commit, source_commit):
        raise EvidenceError("Reused performance evidence does not originate from an ancestor commit.")

    actual_changed_paths = changed_paths(repo, measured_commit, source_commit)
    if receipt.get("changedPaths") != actual_changed_paths:
        raise EvidenceError("Performance reuse receipt does not contain the exact source delta.")
    relevant_changes = [path for path in actual_changed_paths if is_performance_input(path)]
    if relevant_changes:
        raise EvidenceError(
            "Performance evidence cannot be reused after performance input changes: "
            + ", ".join(relevant_changes)
        )

    target_receipts = receipt.get("targets")
    if not isinstance(target_receipts, list):
        raise EvidenceError("Performance reuse receipt does not contain target receipts.")
    expected_target_receipts = [
        {
            "target": target,
            "evaluationSha256": sha256(evaluations[target][0]),
        }
        for target in PERFORMANCE_TARGETS
    ]
    if target_receipts != expected_target_receipts:
        raise EvidenceError("Performance reuse target receipts do not match the copied evaluations.")

    return {
        "reused": True,
        "measuredCommit": measured_commit,
        "measuredRunId": measured_run_id,
        "targets": list(PERFORMANCE_TARGETS),
        "changedPaths": actual_changed_paths,
    }


def reuse_performance_evidence(
    repo: Path,
    source_root: Path,
    root: Path,
    run_id: str,
) -> None:
    """Copy reusable scorecards only when Git proves their inputs unchanged."""
    repo = repo.resolve()
    source_root = source_root.resolve()
    root = root.resolve()
    source_performance = source_root / "performance"
    destination_performance = root / "performance"
    if destination_performance.exists():
        raise EvidenceError(f"Performance destination already exists: {destination_performance}")
    if (source_performance / "reuse-evidence.json").exists():
        raise EvidenceError("Chained performance evidence reuse is not allowed.")
    for path in source_performance.rglob("*"):
        if path.is_symlink():
            raise EvidenceError(f"Symbolic links are not allowed in reusable performance evidence: {path}")

    evaluations = {
        target: load_performance_evaluation(source_root, target)
        for target in PERFORMANCE_TARGETS
    }
    payloads = [payload for _, payload in evaluations.values()]
    measured_commits = {payload["commit"] for payload in payloads}
    measured_source_hashes = {payload["sourceHash"] for payload in payloads}
    measured_run_ids = {payload["runId"] for payload in payloads}
    if len(measured_commits) != 1 or len(measured_source_hashes) != 1 or len(measured_run_ids) != 1:
        raise EvidenceError("Reusable performance targets do not share one source and run identity.")

    measured_commit = next(iter(measured_commits))
    measured_source_hash = next(iter(measured_source_hashes))
    measured_run_id = next(iter(measured_run_ids))
    source_commit = run_command("git", "rev-parse", "HEAD", cwd=repo)
    if run_command("git", "status", "--porcelain", "--untracked-files=all", cwd=repo):
        raise EvidenceError("Performance reuse requires a clean Git worktree.")
    if measured_commit == source_commit:
        raise EvidenceError("Performance evidence already belongs to the current commit; run it as fresh evidence.")
    if not commit_is_ancestor(repo, measured_commit, source_commit):
        raise EvidenceError("Reusable performance evidence does not originate from an ancestor commit.")

    source_delta = changed_paths(repo, measured_commit, source_commit)
    relevant_changes = [path for path in source_delta if is_performance_input(path)]
    if relevant_changes:
        raise EvidenceError(
            "Performance evidence cannot be reused after performance input changes: "
            + ", ".join(relevant_changes)
        )

    destination_performance.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source_performance, destination_performance)
    target_receipts = [
        {
            "target": target,
            "evaluationSha256": sha256(
                destination_performance / target / "evidence" / "gate-performance-evaluation.json"
            ),
        }
        for target in PERFORMANCE_TARGETS
    ]
    receipt = {
        "schemaVersion": 1,
        "runId": run_id,
        "sourceCommit": source_commit,
        "reusedRunId": measured_run_id,
        "reusedSourceCommit": measured_commit,
        "reusedSourceHash": measured_source_hash,
        "changedPaths": source_delta,
        "performanceInputChanges": [],
        "targets": target_receipts,
    }
    (destination_performance / "reuse-evidence.json").write_text(
        json.dumps(receipt, indent=2) + "\n",
        encoding="utf-8",
    )


def validate_reconciliation(
    root: Path,
    run_id: str,
    source_commit: str,
) -> dict[str, str]:
    """Require one exhaustive, duplicate-free release-contract index."""
    path = root / RECONCILIATION_EVIDENCE
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(f"Unable to read release reconciliation: {path}") from exception

    if (
        payload.get("schemaVersion") != 1
        or payload.get("runId") != run_id
        or payload.get("sourceCommit") != source_commit
    ):
        raise EvidenceError("Release reconciliation does not match the candidate source and run.")

    gates = payload.get("gates")
    if not isinstance(gates, list):
        raise EvidenceError("Release reconciliation does not contain a gate inventory.")

    actual_gate_ids = tuple(gate.get("id") for gate in gates if isinstance(gate, dict))
    if actual_gate_ids != REQUIRED_RECONCILIATION_GATES:
        raise EvidenceError(
            "Release reconciliation gate mismatch. "
            f"Expected={list(REQUIRED_RECONCILIATION_GATES)}; actual={list(actual_gate_ids)}"
        )
    if any(gate.get("status") != "pass" for gate in gates):
        raise EvidenceError("Release reconciliation contains a non-passing gate.")

    return {gate["id"]: gate["status"] for gate in gates}


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
    live_example_matrix = validate_live_example_matrix(root, args.run_id, engines)
    runtime_posture = validate_runtime_posture(root, args.run_id, source["commit"])
    performance_evidence = validate_performance_evidence(
        root,
        repo,
        args.run_id,
        source["commit"],
    )
    reconciliation = validate_reconciliation(root, args.run_id, source["commit"])
    dependencies = collect_dependencies(dependency_graph)
    approved_sdk = approved_dotnet_sdk(repo)
    dotnet_sdk = run_command("dotnet", "--version", cwd=repo)

    if dotnet_sdk != approved_sdk:
        raise EvidenceError(
            f"The active .NET SDK {dotnet_sdk} does not match the approved SDK {approved_sdk}.")

    mysql84_engine = next(engine for engine in engines if engine["targetId"] == "mysql84")
    if runtime_posture["image"] != mysql84_engine["image"]:
        raise EvidenceError("Runtime posture and matrix evidence disagree on the MySQL 8.4 image.")
    if runtime_posture["dotnetSdk"] != dotnet_sdk:
        raise EvidenceError("Runtime posture and manifest evidence disagree on the .NET SDK.")

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
            "approvedDotnetSdk": approved_sdk,
            "dotnetSdk": dotnet_sdk,
            "resolvedPackages": dependencies,
        },
        "engines": engines,
        "integrationConfigurationMatrix": integration_matrix,
        "liveExampleMatrix": live_example_matrix,
        "runtimePosture": runtime_posture,
        "performanceEvidence": performance_evidence,
        "verificationReconciliation": reconciliation,
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

    toolchain = manifest.get("toolchain", {})
    approved_sdk = toolchain.get("approvedDotnetSdk")
    observed_sdk = toolchain.get("dotnetSdk")

    if not isinstance(approved_sdk, str) or approved_sdk != observed_sdk:
        raise EvidenceError("Release evidence does not bind the observed SDK to the approved SDK.")

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
        repository_sdk = approved_dotnet_sdk(repo)
        active_sdk = run_command("dotnet", "--version", cwd=repo)

        if approved_sdk != repository_sdk or observed_sdk != active_sdk:
            raise EvidenceError("The manifest .NET SDK identity does not match the current repository contract.")

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

    reuse_performance = subparsers.add_parser(
        "reuse-performance",
        help="Reuse immutable performance evidence after fail-closed source-delta validation.",
    )
    reuse_performance.add_argument("--repo", type=Path, required=True)
    reuse_performance.add_argument("--source-root", type=Path, required=True)
    reuse_performance.add_argument("--root", type=Path, required=True)
    reuse_performance.add_argument("--run-id", required=True)

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
        elif args.command == "reuse-performance":
            reuse_performance_evidence(
                args.repo,
                args.source_root,
                args.root,
                args.run_id,
            )
        else:
            verify_manifest(args.root, args.repo)
    except EvidenceError as exception:
        print(f"Release evidence failed: {exception}", file=sys.stderr)
        return 1
    print(f"Release evidence {args.command} passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
