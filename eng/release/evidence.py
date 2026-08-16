#!/usr/bin/env python3
"""Generate and verify the portable release-candidate evidence manifest.

Generation binds release artifacts to the clean, untagged candidate checkout,
the expected release tag, the resolved dependency graph, and digest-pinned
database images. Verification is an independent readback: it rejects changes
to the manifest, its inventory, or any retained artifact before the evidence
can authorize publication.
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
from xml.etree import ElementTree


SCHEMA_VERSION = 3
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
EFCORE_PATCH_MATRIX_ROOT = Path("efcore-patch-matrix")
RECONCILIATION_EVIDENCE = Path("release-candidate-reconciliation.json")
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


def validate_efcore_full_results(
    matrix_root: Path,
    targets: list[str],
) -> None:
    """Verify that a full matrix row retained successful live evidence."""
    for target in targets:
        target_root = matrix_root / "latest-10-0" / target
        trx_files = sorted(target_root.glob("*.trx"))
        database_path = target_root / "test-database-evidence.json"
        if len(trx_files) != 1 or not database_path.is_file():
            raise EvidenceError(
                f"EF Core latest matrix retained incomplete specification evidence for {target}."
            )
        try:
            trx_root = ElementTree.parse(trx_files[0]).getroot()
            counters = next(
                element
                for element in trx_root.iter()
                if element.tag.endswith("}Counters") or element.tag == "Counters"
            )
            database = json.loads(database_path.read_text(encoding="utf-8"))
        except (OSError, ElementTree.ParseError, StopIteration, json.JSONDecodeError) as exception:
            raise EvidenceError(
                f"EF Core latest matrix evidence is unreadable for {target}."
            ) from exception
        total = counters.get("total", "")
        if (
            counters.get("failed") != "0"
            or not total.isdigit()
            or int(total) <= 0
        ):
            raise EvidenceError(f"EF Core latest matrix tests did not pass for {target}.")
        if not isinstance(database, dict):
            raise EvidenceError(
                f"EF Core latest matrix retained invalid engine evidence for {target}."
            )
        identities = database.get("targets", [])
        if (
            not isinstance(identities, list)
            or any(not isinstance(identity, dict) for identity in identities)
            or database.get("lifecycleState") != "cleanup-completed"
            or len(identities) != 1
            or identities[0].get("targetId") != target
        ):
            raise EvidenceError(
                f"EF Core latest matrix retained invalid engine evidence for {target}."
            )

    integration_path = (
        matrix_root
        / "latest-10-0"
        / "integration"
        / "compatibility-matrix-evidence.json"
    )
    try:
        integration = json.loads(integration_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise EvidenceError("EF Core latest integration evidence is unreadable.") from exception
    if not isinstance(integration, dict) or not isinstance(
        integration.get("testDatabase"), dict
    ):
        raise EvidenceError("EF Core latest integration evidence is invalid.")
    test_database = integration["testDatabase"]
    database_targets = test_database.get("targets")
    if not isinstance(database_targets, list) or any(
        not isinstance(target, dict) for target in database_targets
    ):
        raise EvidenceError("EF Core latest integration evidence is invalid.")
    raw_integration_targets = [
        target.get("targetId") for target in database_targets
    ]
    target_selection = integration.get("targetSelection")
    if (
        any(not isinstance(target, str) for target in raw_integration_targets)
        or not isinstance(target_selection, str)
        or integration.get("mode") != "testcontainers"
        or integration.get("testExitCode") != 0
        or integration.get("testFilter") != ""
        or sorted(target_selection.split(",")) != targets
        or test_database.get("lifecycleState") != "cleanup-completed"
        or sorted(raw_integration_targets) != targets
    ):
        raise EvidenceError("EF Core latest integration evidence is invalid.")


def validate_efcore_patch_matrix(
    root: Path,
    qualification_gates: list[str],
) -> None:
    """Require one floor graph and one fully executed latest EF Core row.

    The protected branch owns behavior at the deterministic dependency floor.
    The candidate still resolves and records that exact graph, while the
    additional candidate-produced test budget is reserved for the newest
    compatible patch. Receipts make that division explicit and non-optional.
    """
    matrix_root = root / EFCORE_PATCH_MATRIX_ROOT
    receipts = sorted(matrix_root.rglob("efcore-contract-evidence.json"))
    expected_receipts = [
        matrix_root / "latest-10-0" / "efcore-contract-evidence.json",
        matrix_root / "minimum-10-0-8" / "efcore-contract-evidence.json",
    ]
    if receipts != expected_receipts:
        rendered = [portable_path(path, root) for path in receipts]
        raise EvidenceError(
            "EF Core patch evidence must contain exactly the floor and latest receipts; "
            f"found: {rendered}."
        )

    expected = {
        "minimum-10-0-8": {
            "requestedVersion": "10.0.8",
            "resolvedPattern": r"10[.]0[.]8",
            "validationScope": "dependency-graph",
            "qualificationSource": "repository-qualification",
            "specificationTargets": [],
            "integrationTargets": [],
            "contracts": ["resolved-package-graph", "version-contract-preflight"],
            "results": {"dependencies": "resolved-packages.json"},
        },
        "latest-10-0": {
            "requestedVersion": "10.0.*",
            "resolvedPattern": r"10[.]0[.][0-9]+",
            "validationScope": "full",
            "qualificationSource": None,
            "specificationTargets": ["mariadb118", "mysql84"],
            "integrationTargets": ["mariadb118", "mysql84"],
            "contracts": [
                "integration-matrix",
                "live-suite",
                "repository-test-path",
                "resolved-package-graph",
                "specification-suite",
                "version-contract-preflight",
            ],
            "results": {
                "dependencies": "resolved-packages.json",
                "integration": "integration/compatibility-matrix-evidence.json",
            },
        },
    }
    required_packages = {
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Relational.Specification.Tests",
    }

    for leg, contract in expected.items():
        receipt_path = matrix_root / leg / "efcore-contract-evidence.json"
        graph_path = matrix_root / leg / "resolved-packages.json"
        try:
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            graph = json.loads(graph_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exception:
            raise EvidenceError(f"EF Core matrix evidence is unreadable for {leg}.") from exception
        if not isinstance(receipt, dict) or not isinstance(graph, dict):
            raise EvidenceError(f"EF Core matrix evidence is invalid for {leg}.")

        for field in (
            "requestedVersion",
            "validationScope",
            "qualificationSource",
            "specificationTargets",
            "integrationTargets",
            "contracts",
            "results",
        ):
            if receipt.get(field) != contract[field]:
                raise EvidenceError(f"EF Core matrix {leg} has an invalid {field} contract.")
        if receipt.get("schemaVersion") != 2:
            raise EvidenceError(f"EF Core matrix {leg} has an unsupported receipt schema.")

        resolved_version = receipt.get("resolvedVersion")
        if (
            not isinstance(resolved_version, str)
            or re.fullmatch(contract["resolvedPattern"], resolved_version) is None
        ):
            raise EvidenceError(f"EF Core matrix {leg} resolved an unexpected version.")
        projects = graph.get("projects")
        if not isinstance(projects, list) or any(
            not isinstance(project, dict) for project in projects
        ):
            raise EvidenceError(f"EF Core matrix {leg} dependency graph is invalid.")
        versions: dict[str, set[str]] = {package: set() for package in required_packages}
        for project in projects:
            frameworks = project.get("frameworks")
            if not isinstance(frameworks, list) or any(
                not isinstance(framework, dict) for framework in frameworks
            ):
                raise EvidenceError(f"EF Core matrix {leg} dependency graph is invalid.")
            for framework in frameworks:
                # dotnet package list omits both package arrays for projects
                # that have no PackageReference in the selected framework.
                packages = framework.get("topLevelPackages", [])
                if not isinstance(packages, list) or any(
                    not isinstance(package, dict) for package in packages
                ):
                    raise EvidenceError(f"EF Core matrix {leg} dependency graph is invalid.")
                for package in packages:
                    package_id = package.get("id")
                    if package_id in versions and package.get("resolvedVersion"):
                        versions[package_id].add(package["resolvedVersion"])
        if any(values != {resolved_version} for values in versions.values()):
            raise EvidenceError(
                f"EF Core matrix {leg} dependency graph does not match {resolved_version}."
            )

    if "repository-qualification" not in qualification_gates:
        raise EvidenceError(
            "The EF Core floor receipt requires commit-exact repository-qualification."
        )
    validate_efcore_full_results(matrix_root, expected["latest-10-0"]["specificationTargets"])


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


def git_source(repo: Path, expected_ref: str) -> dict[str, Any]:
    """Validate and return the untagged candidate source identity."""
    commit = run_command("git", "rev-parse", "HEAD", cwd=repo)
    dirty = run_command("git", "status", "--porcelain", "--untracked-files=all", cwd=repo)
    if dirty:
        raise EvidenceError("Release evidence requires a clean Git worktree.")

    github_sha = os.environ.get("GITHUB_SHA", "")
    if github_sha and github_sha != commit:
        raise EvidenceError(f"GITHUB_SHA {github_sha} does not match checked-out commit {commit}.")

    exact_tags = run_command("git", "tag", "--points-at", commit, cwd=repo).splitlines()
    version_tags = sorted(tag for tag in exact_tags if SEMANTIC_VERSION_TAG.fullmatch(tag))
    if version_tags:
        rendered = ", ".join(version_tags)
        raise EvidenceError(
            "Release candidate source must be untagged during qualification; "
            f"found: {rendered}."
        )

    actual_ref = os.environ.get("GITHUB_REF", "")
    if not actual_ref:
        actual_ref = run_command("git", "symbolic-ref", "-q", "HEAD", cwd=repo)
    if expected_ref and actual_ref != expected_ref:
        raise EvidenceError(f"Expected release ref {expected_ref}, found {actual_ref}.")
    if actual_ref != "refs/heads/main":
        raise EvidenceError(
            f"Release candidate evidence must run from refs/heads/main, found {actual_ref}."
        )

    remote = run_command("git", "config", "--get", "remote.origin.url", cwd=repo)
    return {
        "repository": remote,
        "commit": commit,
        "ref": actual_ref,
        "tag": None,
        "treeState": "clean",
    }


def workflow_identity(run_id: str) -> dict[str, Any]:
    """Capture hosted workflow and runner identities or an explicit local identity.

    A hosted run fails closed when GitHub omits an identity field needed to
    locate and reproduce the evidence-producing execution.
    """
    hosted = os.environ.get("GITHUB_ACTIONS") == "true"
    if not hosted:
        return {
            "provider": "local",
            "runId": run_id,
            "runAttempt": "1",
            "workflow": "local-release-candidate",
            "workflowRef": "local",
            "repository": "local",
            "runnerName": "local",
            "runnerOs": platform.system(),
            "runnerArch": platform.machine(),
        }

    required = (
        "GITHUB_RUN_ID",
        "GITHUB_RUN_ATTEMPT",
        "GITHUB_WORKFLOW_REF",
        "GITHUB_REPOSITORY",
    )
    missing = [name for name in required if not os.environ.get(name)]
    if missing:
        raise EvidenceError(f"Hosted workflow identity is incomplete: {', '.join(missing)}")

    return {
        "provider": "github-actions",
        "runId": os.environ["GITHUB_RUN_ID"],
        "runAttempt": os.environ["GITHUB_RUN_ATTEMPT"],
        "workflow": os.environ.get("GITHUB_WORKFLOW", "local-release-candidate"),
        "workflowRef": os.environ["GITHUB_WORKFLOW_REF"],
        "repository": os.environ["GITHUB_REPOSITORY"],
        "runnerName": os.environ.get("RUNNER_NAME", "local"),
        "runnerOs": os.environ.get("RUNNER_OS", platform.system()),
        "runnerArch": os.environ.get("RUNNER_ARCH", platform.machine()),
    }


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
        "expectedReleaseTag": manifest["expectedReleaseTag"],
        "gates": sorted(entry["gate"] for entry in gates),
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

    if args.expected_release_tag != f"v{args.release_version}":
        raise EvidenceError(
            "Expected release tag does not match the candidate version."
        )
    source = git_source(repo, args.expected_ref)
    artifacts = collect_artifacts(root)
    validate_release_packages(artifacts, args.release_version)
    engines = collect_engines(root)
    runtime_posture = validate_runtime_posture(root, args.run_id, source["commit"])
    # Integration-configuration and live-example matrices are proven on the
    # protected branch and imported through the qualification manifest rather
    # than repeated here. Requiring them in this document is what made the
    # release manifest ask for evidence the tag never produces.
    qualification = validate_qualification_manifest(
        root, source["commit"], args.release_version
    )
    validate_efcore_patch_matrix(root, qualification["gates"])
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
        "expectedReleaseTag": args.expected_release_tag,
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
    if manifest.get("expectedReleaseTag") != f"v{manifest.get('releaseVersion', '')}":
        raise EvidenceError(
            "Release evidence expected tag does not match its candidate version."
        )
    source = manifest.get("source") or {}
    if source.get("ref") != "refs/heads/main" or source.get("tag") is not None:
        raise EvidenceError("Release candidate evidence source identity is not untagged main.")
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
        current_tags = run_command("git", "tag", "--points-at", current_commit, cwd=repo).splitlines()
        semantic_tags = [
            tag for tag in current_tags if SEMANTIC_VERSION_TAG.fullmatch(tag)
        ]
        if semantic_tags:
            raise EvidenceError(
                "Candidate verification requires the source commit to remain untagged."
            )
        github_ref = os.environ.get("GITHUB_REF", "")
        if github_ref and source.get("ref") != github_ref:
            raise EvidenceError("The manifest source ref does not match the hosted workflow ref.")

    qualification = manifest.get("qualification", {})
    qualification_gates = qualification.get("gates", [])
    if not isinstance(qualification_gates, list):
        raise EvidenceError("Release evidence has an invalid qualification gate inventory.")
    validate_efcore_patch_matrix(root, qualification_gates)


def parse_arguments() -> argparse.Namespace:
    """Parse the generate or verify command-line contract."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    generate = subparsers.add_parser("generate", help="Generate release evidence.")
    generate.add_argument("--repo", type=Path, required=True)
    generate.add_argument("--root", type=Path, required=True)
    generate.add_argument("--run-id", required=True)
    generate.add_argument("--release-version", required=True)
    generate.add_argument("--expected-release-tag", required=True)
    generate.add_argument("--dependency-graph", type=Path, required=True)
    generate.add_argument("--expected-ref", default="")

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
