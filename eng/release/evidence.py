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

if __package__:
    from ..performance import cli as performance_evidence
    from ..performance.inputs import invalidates_release_reuse
else:
    from eng.performance import cli as performance_evidence
    from eng.performance.inputs import invalidates_release_reuse


SCHEMA_VERSION = 1
MANIFEST_NAME = "release-candidate-evidence.json"
CHECKSUM_NAME = "release-candidate-evidence.sha256"
REQUIRED_ENGINE_TARGETS = (
    "mariadb1011",
    "mariadb114",
    "mariadb118",
    "mariadb123",
    "mysql84",
    "mysql97",
)
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
PERFORMANCE_BASELINE_PATH = Path(
    "benchmarks/baselines/doka-benchmark-baseline.json"
)
PERFORMANCE_CONTRACT_PATH = Path("benchmarks/performance-contract.json")
# The reconciliation index is derived from the evidence policy rather than
# restated here. A second list is free to describe a different release than the
# one the qualification manifest selected, and the writer and this validator
# would each be internally consistent while disagreeing with each other.
RECONCILIATION_SCHEMA_VERSION = 2


def required_reconciliation_gates(policy_path: Path | None = None) -> tuple[str, ...]:
    """Return the gate identifiers a reconciliation index must carry."""
    resolved = policy_path or (Path(__file__).resolve().parent / "evidence-policy.json")
    try:
        policy = json.loads(resolved.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(
            f"Unable to read the evidence policy: {resolved}"
        ) from exception

    return tuple(sorted(gate["id"] for gate in policy["gates"]))
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
        "--no-renames",
        "--name-only",
        "--diff-filter=ACDMRTUXB",
        f"{base_commit}..{current_commit}",
        "--",
        cwd=repo,
    )
    return sorted(path for path in output.splitlines() if path)


def is_performance_input(path: str) -> bool:
    """Return whether a repository path can affect measured provider behavior."""
    return invalidates_release_reuse(path)


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


def performance_targets(contract_path: Path) -> tuple[str, ...]:
    """Return the canonical performance target set from its contract."""
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError(
            f"Unable to read the performance contract: {contract_path}"
        ) from exception

    targets = contract.get("requiredTargets")
    if not isinstance(targets, dict) or not targets:
        raise EvidenceError("The performance contract names no required targets.")

    return tuple(sorted(targets))


def accepted_matrix_identity(entries: list[dict[str, Any]]) -> dict[str, set[Any]]:
    """Return the identity an accepted baseline matrix has to agree on.

    The run identifier is deliberately not part of it. An accepted baseline is
    measured by the benchmark matrix, which runs one job per target and names
    that job in the identifier, so its entries can never share one. What this
    gate needs is that all engines measured the same software, and the commit
    together with its source hash carries exactly that.

    Evidence the release candidate measures itself is a different case: there
    the paired comparison binds each engine to its own run identifier, and the
    attempt receipt is what proves the verdict belongs to that run.
    """
    identities = {
        field: {entry.get(field) for entry in entries}
        for field in ("commit", "sourceHash")
    }
    mismatched = [field for field, values in identities.items() if len(values) != 1]
    if mismatched:
        raise EvidenceError(
            "The accepted hosted performance matrix has inconsistent identity field(s): "
            f"{', '.join(mismatched)}."
        )

    return identities


def validate_performance_baseline(args: argparse.Namespace) -> dict[str, Any]:
    """Prove that the accepted hosted matrix covers the current source state."""
    repo = Path(args.repo).resolve()
    contract_path = Path(args.contract).resolve()
    baseline_path = Path(args.baseline).resolve()

    try:
        performance_evidence.validate_baseline_file(
            argparse.Namespace(
                contract=str(contract_path),
                baseline=str(baseline_path),
            )
        )
        baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    except (
        OSError,
        json.JSONDecodeError,
        performance_evidence.PerformanceEvidenceError,
    ) as exception:
        raise EvidenceError(
            f"The accepted performance baseline is invalid: {exception}"
        ) from exception

    entries = [
        entry
        for entry in baseline.get("baselines", [])
        if isinstance(entry, dict)
        and entry.get("profile") == args.profile
        and entry.get("runnerClass") == args.runner_class
    ]
    required_targets = performance_targets(contract_path)
    observed_targets = {entry.get("target") for entry in entries}
    if len(entries) != len(required_targets) or observed_targets != set(
        required_targets
    ):
        raise EvidenceError(
            "The accepted hosted performance baseline is not a complete target matrix."
        )

    identities = accepted_matrix_identity(entries)

    evidence_commit = next(iter(identities["commit"]))
    source_hash = next(iter(identities["sourceHash"]))
    if not isinstance(evidence_commit, str) or not re.fullmatch(
        r"[0-9a-f]{40}",
        evidence_commit,
    ):
        raise EvidenceError("The accepted performance source commit is invalid.")
    if not isinstance(source_hash, str):
        raise EvidenceError("The accepted performance source hash is invalid.")
    if source_hash != clean_performance_source_hash(evidence_commit):
        raise EvidenceError(
            "The accepted performance source hash does not bind its source commit."
        )

    current_commit = run_command("git", "rev-parse", "HEAD", cwd=repo)
    if not commit_is_ancestor(repo, evidence_commit, current_commit):
        raise EvidenceError(
            "The accepted performance source commit is not an ancestor of HEAD."
        )
    stale_paths = [
        path
        for path in changed_paths(repo, evidence_commit, current_commit)
        if is_performance_input(path)
    ]
    if stale_paths:
        raise EvidenceError(
            "The accepted performance baseline predates relevant source changes: "
            f"{', '.join(stale_paths)}."
        )

    receipt = {
        "schemaVersion": 1,
        "kind": "performance-baseline-readiness",
        "success": True,
        "baselineVersion": baseline["baselineVersion"],
        "evidenceCommit": evidence_commit,
        "currentCommit": current_commit,
        "profile": args.profile,
        "runnerClass": args.runner_class,
        "targets": list(required_targets),
    }
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(receipt, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return receipt


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
    sources: dict[str, set[str]] = {}
    for path in sorted(root.rglob("test-database-evidence.json")):
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
                "image": target.get("image"),
            }
            source = target.get("source")
            if not all(identity.values()) or not source:
                raise EvidenceError(f"Engine evidence is missing an immutable identity: {path}")
            _, separator, digest = identity["image"].rpartition("@sha256:")
            if separator == "" or len(digest) != 64 or any(character not in "0123456789abcdef" for character in digest):
                raise EvidenceError(f"Engine image is not digest-pinned: {identity['image']}")
            previous = engines.setdefault(target_id, identity)
            if previous != identity:
                raise EvidenceError(f"Conflicting engine identities were recorded for {target_id}.")
            sources.setdefault(target_id, set()).add(source)

    missing = sorted(set(REQUIRED_ENGINE_TARGETS) - set(engines))
    if missing:
        raise EvidenceError(f"Missing engine evidence for: {', '.join(missing)}")
    unexpected = sorted(set(engines) - set(REQUIRED_ENGINE_TARGETS))
    if unexpected:
        raise EvidenceError(f"Unexpected engine evidence for: {', '.join(unexpected)}")
    return [
        {
            **engines[target],
            "source": "+".join(sorted(sources[target])),
        }
        for target in sorted(engines)
    ]


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

    required_targets = performance_targets(
        repo / PERFORMANCE_CONTRACT_PATH
    )
    evaluations = {
        target: load_performance_evaluation(source_root, target)
        for target in required_targets
    }
    payloads = [payload for _, payload in evaluations.values()]
    measured_commits = {payload["commit"] for payload in payloads}
    measured_source_hashes = {payload["sourceHash"] for payload in payloads}
    if len(measured_commits) != 1 or len(measured_source_hashes) != 1:
        raise EvidenceError(
            "Reusable performance targets do not share one source identity."
        )

    measured_commit = next(iter(measured_commits))
    measured_source_hash = next(iter(measured_source_hashes))
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
            "runId": evaluations[target][1]["runId"],
            "evaluationSha256": sha256(
                destination_performance / target / "evidence" / "gate-performance-evaluation.json"
            ),
        }
        for target in required_targets
    ]
    receipt = {
        "schemaVersion": 2,
        "runId": run_id,
        "sourceCommit": source_commit,
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
        payload.get("schemaVersion") != RECONCILIATION_SCHEMA_VERSION
        or payload.get("runId") != run_id
        or payload.get("sourceCommit") != source_commit
    ):
        raise EvidenceError("Release reconciliation does not match the candidate source and run.")

    gates = payload.get("gates")
    if not isinstance(gates, list):
        raise EvidenceError("Release reconciliation does not contain a gate inventory.")

    expected = required_reconciliation_gates()
    actual_gate_ids = tuple(
        sorted(gate.get("id") for gate in gates if isinstance(gate, dict))
    )
    if actual_gate_ids != expected:
        raise EvidenceError(
            "Release reconciliation gate mismatch. "
            f"Expected={list(expected)}; actual={list(actual_gate_ids)}"
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


def validate_qualification_manifest(
    root: Path,
    source_commit: str,
    release_version: str,
) -> dict[str, Any]:
    """Bind this document to the manifest that selected the gates.

    Two documents describing one release must not be able to disagree. The
    qualification manifest owns gate selection; this one owns the artifact
    inventory and the source identity. Reading the former here is what keeps
    the pair consistent instead of merely adjacent.
    """
    path = root / "release-qualification-manifest.json"
    if not path.is_file():
        raise EvidenceError(
            "The release candidate carries no qualification manifest; gate "
            "selection has not happened."
        )
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError("The qualification manifest is unreadable.") from exception

    if manifest.get("kind") != "release-qualification-manifest":
        raise EvidenceError("The qualification manifest has an unexpected kind.")
    if manifest.get("commit") != source_commit:
        raise EvidenceError(
            f"The qualification manifest describes commit "
            f"{manifest.get('commit')}, not the release source {source_commit}."
        )
    if manifest.get("releaseVersion") != release_version:
        raise EvidenceError(
            f"The qualification manifest describes version "
            f"{manifest.get('releaseVersion')}, not {release_version}."
        )
    gates = manifest.get("gates")
    if not isinstance(gates, list) or not gates:
        raise EvidenceError("The qualification manifest pins no gates.")

    return {
        "policyVersion": manifest["policyVersion"],
        "policyDigest": manifest["policyDigest"],
        "selectionRuleVersion": manifest["selectionRuleVersion"],
        "treeId": manifest["treeId"],
        "releaseTag": manifest["releaseTag"],
        "gates": sorted(entry["gate"] for entry in gates),
    }


def validate_paired_performance_evidence(
    root: Path,
    source_commit: str,
    required_targets: tuple[str, ...],
    contract_path: Path,
) -> dict[str, Any]:
    """Inventory the paired comparison the tag performed.

    The release measures performance once, as a paired comparison, and every
    required engine must have qualified. A partial set would let an engine
    whose comparison never concluded be represented by one that did.
    """
    evaluations = sorted(
        (root / "performance").rglob("paired-evaluation.json")
    )
    if not evaluations:
        raise EvidenceError(
            "The release candidate carries no paired performance evaluation."
        )

    engines: list[dict[str, Any]] = []
    for path in evaluations:
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exception:
            raise EvidenceError(
                f"Unable to read paired evaluation: {path}"
            ) from exception
        if payload.get("qualification") != "pending-run-wide-adjustment":
            raise EvidenceError(
                f"Paired performance for {payload.get('target')} is "
                f"{payload.get('qualification')!r}."
            )
        if payload.get("commit") != source_commit:
            raise EvidenceError(
                f"Paired performance describes commit {payload.get('commit')}, "
                f"not the release source {source_commit}."
            )
        engines.append(
            {
                "target": payload["target"],
                "profile": payload["profile"],
                "runId": payload["runId"],
                "runnerClass": payload["runnerClass"],
                "qualification": payload["qualification"],
                "relativePath": path.relative_to(root).as_posix(),
                "sha256": sha256(path),
            }
        )

    targets = sorted(entry["target"] for entry in engines)
    if len(set(targets)) != len(targets):
        raise EvidenceError("Paired performance reports a target twice.")
    expected_targets = set(required_targets)
    observed_targets = set(targets)
    missing = sorted(expected_targets - observed_targets)
    unexpected = sorted(observed_targets - expected_targets)
    if missing or unexpected:
        raise EvidenceError(
            "Paired performance target mismatch. "
            f"Missing={missing}; unexpected={unexpected}."
        )

    qualification_path = root / "performance" / "paired-scorecard-qualification.json"
    if qualification_path.is_symlink() or not qualification_path.is_file():
        raise EvidenceError(
            "Paired performance carries no regular scorecard qualification."
    )
    try:
        qualification = json.loads(qualification_path.read_text(encoding="utf-8"))
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
        performance_evidence.validate_registered_characterization(
            contract,
            contract_path.resolve().parent.parent,
        )
        expected = performance_evidence.evaluate_scorecard_qualification(
            [json.loads(path.read_text(encoding="utf-8")) for path in evaluations],
            contract,
            contract_digest=sha256(contract_path),
        )
    except performance_evidence.PerformanceEvidenceError as error:
        raise EvidenceError(
            f"Paired scorecard qualification is invalid: {error}"
        ) from error
    if qualification != expected or qualification.get("qualification") != "qualified":
        raise EvidenceError(
            "Paired scorecard qualification does not reproduce as qualified."
        )
    target_states = {
        entry["target"]: entry["state"] for entry in qualification["targets"]
    }
    for engine in engines:
        engine["qualification"] = target_states[engine["target"]]

    return {
        "comparisonMode": "paired",
        "qualification": qualification["qualification"],
        "scorecardRelativePath": qualification_path.relative_to(root).as_posix(),
        "scorecardSha256": sha256(qualification_path),
        "engines": engines,
    }


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
    runtime_posture = validate_runtime_posture(root, args.run_id, source["commit"])
    # The integration-configuration and live-example matrices, and the
    # historical performance comparison, are proven on the protected branch and
    # imported through the qualification manifest rather than repeated here.
    # Requiring them in this document is what made the release manifest ask for
    # evidence the tag never produces.
    qualification = validate_qualification_manifest(
        root, source["commit"], args.release_version
    )
    required_performance_targets = performance_targets(
        repo / PERFORMANCE_CONTRACT_PATH
    )
    performance_evidence = validate_paired_performance_evidence(
        root,
        source["commit"],
        required_performance_targets,
        repo / PERFORMANCE_CONTRACT_PATH,
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
        "qualification": qualification,
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

    validate_performance = subparsers.add_parser(
        "validate-performance-baseline",
        help="Validate hosted performance evidence against the current source state.",
    )
    validate_performance.add_argument("--repo", type=Path, required=True)
    validate_performance.add_argument("--contract", type=Path, required=True)
    validate_performance.add_argument("--baseline", type=Path, required=True)
    validate_performance.add_argument("--profile", required=True)
    validate_performance.add_argument("--runner-class", required=True)
    validate_performance.add_argument("--output", type=Path, required=True)

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
        elif args.command == "validate-performance-baseline":
            validate_performance_baseline(args)
        else:
            verify_manifest(args.root, args.repo)
    except EvidenceError as exception:
        print(f"Release evidence failed: {exception}", file=sys.stderr)
        return 1
    print(f"Release evidence {args.command} passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
