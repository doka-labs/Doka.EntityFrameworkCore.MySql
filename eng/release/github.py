#!/usr/bin/env python3
"""Stage and finalize one candidate as an immutable GitHub release.

The complete release asset set is staged before NuGet publication. After both
primary package pushes succeed, the same immutable plan publishes immediately.
Availability and repository-signature evidence is then validated and retained
as workflow completion evidence rather than added to the immutable release.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
import time
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Protocol

from . import evidence as release_evidence
from . import nuget as nuget_publication


SCHEMA_VERSION = 2
GITHUB_API_VERSION = "2022-11-28"
READBACK_DELAY_SECONDS = 2.0
SHA1 = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+")
STAGED_EVIDENCE_FILES = (
    "candidate-receipt.json",
    "release-publication-receipt.json",
    "release-tag-trust-root.json",
    "candidate-publication-preflight.json",
    "symbol-readback-manifest.json",
)
PUBLICATION_EVIDENCE_FILES = (
    "publication-preflight.json",
    "nuget-publication-readback.json",
    "nuget-signature-verification.txt",
)
RELEASE_CANDIDATE_EVIDENCE_FILES = (
    release_evidence.MANIFEST_NAME,
    release_evidence.CHECKSUM_NAME,
    "release-qualification-manifest.json",
    "release-candidate-summary.md",
    "release-candidate-reconciliation.json",
    "resolved-packages.json",
    "local-package-consumer/local-package-consumer.json",
    "local-package-consumer/local-package-runtime.json",
)
PACKAGE_IDENTITIES = (
    ("provider", nuget_publication.PROVIDER_PACKAGE_ID),
    ("spatial", nuget_publication.SPATIAL_PACKAGE_ID),
)


class GitHubReleaseError(RuntimeError):
    """Raised when a GitHub release cannot be finalized without ambiguity."""


class ReleaseClient(Protocol):
    """Describe the remote operations used by the reconciliation algorithm."""

    def verify_tag(self, repository: str, tag: str, commit: str) -> None:
        """Require an annotated remote tag at the planned source commit."""

    def get_release(self, repository: str, tag: str) -> dict[str, Any] | None:
        """Return the release for ``tag`` or ``None`` when it does not exist."""

    def create_draft(self, plan: dict[str, Any]) -> None:
        """Create a draft from a verified existing tag."""

    def upload_assets(
        self,
        repository: str,
        tag: str,
        paths: list[Path],
    ) -> None:
        """Upload only assets that are absent from a matching draft."""

    def publish_release(self, plan: dict[str, Any]) -> None:
        """Publish a complete draft using its planned release classification."""

    def download_asset(
        self,
        repository: str,
        asset_id: int,
    ) -> bytes:
        """Download one uploaded asset for independent digest readback."""

    def is_latest(self, repository: str, release_id: int) -> bool:
        """Return whether GitHub exposes the release as the latest release."""


def read_json(path: Path, label: str) -> dict[str, Any]:
    """Read one regular JSON object and tie failures to its contract role."""
    if not path.is_file() or path.is_symlink():
        raise GitHubReleaseError(f"{label} is missing or non-regular: {path}")

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise GitHubReleaseError(f"{label} is unreadable: {path}") from exception

    if not isinstance(value, dict):
        raise GitHubReleaseError(f"{label} must contain a JSON object: {path}")

    return value


def write_json(path: Path, value: dict[str, Any]) -> None:
    """Write one canonical receipt only after its value is complete."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def sha256_bytes(value: bytes) -> str:
    """Return the SHA-256 digest of an in-memory readback payload."""
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    """Hash one regular local asset without following symbolic links."""
    if not path.is_file() or path.is_symlink():
        raise GitHubReleaseError(f"Release asset is missing or non-regular: {path}")

    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as exception:
        raise GitHubReleaseError(f"Release asset is unreadable: {path}") from exception

    return digest.hexdigest()


def normalize_markdown(value: str) -> str:
    """Normalize transport newlines while preserving meaningful Markdown."""
    return value.replace("\r\n", "\n").replace("\r", "\n").rstrip()


def extract_release_notes(changelog: Path, version: str) -> str:
    """Extract the exact Keep a Changelog section for one package version."""
    if not changelog.is_file() or changelog.is_symlink():
        raise GitHubReleaseError(f"Changelog is missing or non-regular: {changelog}")

    try:
        text = changelog.read_text(encoding="utf-8")
    except OSError as exception:
        raise GitHubReleaseError(f"Changelog is unreadable: {changelog}") from exception

    heading = re.compile(
        rf"^## \[{re.escape(version)}\] - [0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}$",
        re.MULTILINE,
    )
    matches = list(heading.finditer(text))
    if len(matches) != 1:
        raise GitHubReleaseError(
            f"Changelog must contain exactly one dated section for {version}."
        )

    start = matches[0].end()
    next_heading = re.search(r"^## ", text[start:], re.MULTILINE)
    end = start + next_heading.start() if next_heading else len(text)
    notes = normalize_markdown(text[start:end]).strip()
    if not notes:
        raise GitHubReleaseError(f"Changelog section for {version} is empty.")

    return notes


def require_candidate_artifact(
    root: Path,
    manifest_artifacts: dict[str, dict[str, Any]],
    relative_path: str,
) -> Path:
    """Resolve one candidate asset and verify its manifest size and digest."""
    entry = manifest_artifacts.get(relative_path)
    if entry is None:
        raise GitHubReleaseError(
            f"Candidate manifest does not inventory required asset: {relative_path}"
        )

    path = root / relative_path
    digest = sha256_file(path)
    size = path.stat().st_size
    if entry.get("sha256") != digest or entry.get("sizeBytes") != size:
        raise GitHubReleaseError(
            f"Candidate asset no longer matches its manifest: {relative_path}"
        )

    return path


def asset_record(path: Path) -> dict[str, Any]:
    """Create the immutable local identity used for upload and readback."""
    return {
        "name": path.name,
        "path": str(path.resolve()),
        "sha256": sha256_file(path),
        "sizeBytes": path.stat().st_size,
    }


def require_timestamp(value: Any, label: str) -> None:
    """Require an ISO-8601 timestamp with an explicit time-zone offset."""
    if not isinstance(value, str):
        raise GitHubReleaseError(f"{label} timestamp is missing.")

    try:
        timestamp = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exception:
        raise GitHubReleaseError(f"{label} timestamp is invalid.") from exception

    if timestamp.tzinfo is None:
        raise GitHubReleaseError(f"{label} timestamp has no time-zone offset.")


def require_release_identity(
    evidence: dict[str, Any],
    label: str,
    receipt: dict[str, Any],
    timestamp_field: str,
) -> None:
    """Bind one publication receipt to the selected source and version."""
    if (
        evidence.get("schemaVersion") != nuget_publication.SCHEMA_VERSION
        or evidence.get("releaseTag") != receipt["releaseTag"]
        or evidence.get("expectedReleaseTag") != receipt["expectedReleaseTag"]
        or evidence.get("releaseVersion") != receipt["releaseVersion"]
        or evidence.get("sourceCommit") != receipt["sourceCommit"]
    ):
        raise GitHubReleaseError(f"{label} identity is invalid.")

    require_timestamp(evidence.get(timestamp_field), label)


def validate_candidate_receipt(
    receipt: dict[str, Any],
    repository: str,
    candidate_root: Path,
) -> dict[str, dict[str, Path]]:
    """Revalidate the portable publication receipt before repository writes."""
    if str(receipt.get("repository", "")).lower() != repository.lower():
        raise GitHubReleaseError(
            "Publication receipt does not match the selected repository."
        )

    try:
        return nuget_publication.validate_portable_receipt(receipt, candidate_root)
    except nuget_publication.PublicationError as exception:
        raise GitHubReleaseError(
            f"Publication receipt validation failed: {exception}"
        ) from exception


def validate_candidate_package_digests(
    receipt: dict[str, Any],
    package_map: dict[str, dict[str, Path]],
) -> None:
    """Recompute the signature-independent digest of each candidate package."""
    for role, _ in PACKAGE_IDENTITIES:
        try:
            digest = nuget_publication.canonical_package_digest(
                package_map[role]["package"]
            )
        except nuget_publication.PublicationError as exception:
            raise GitHubReleaseError(
                f"Candidate package content is invalid: {role}"
            ) from exception
        if digest != receipt["packages"][role]["contentDigest"]:
            raise GitHubReleaseError(
                f"Candidate package disagrees with publication receipt: {role}"
            )


def validate_package_state(
    state: Any,
    role: str,
    package_id: str,
    receipt: dict[str, Any],
    require_matching: bool,
) -> None:
    """Validate one NuGet package observation against candidate content."""
    if not isinstance(state, dict):
        raise GitHubReleaseError(f"Publication package state is invalid: {role}")

    status = state.get("status")
    candidate_digest = receipt["packages"][role]["contentDigest"]
    if (
        status not in {"absent", "matching"}
        or (require_matching and status != "matching")
        or state.get("id") != package_id
        or state.get("url")
        != nuget_publication.remote_package_url(
            package_id,
            str(receipt["releaseVersion"]),
        )
        or state.get("candidateContentDigest") != candidate_digest
    ):
        raise GitHubReleaseError(f"Publication package state is invalid: {role}")

    if status == "matching":
        if (
            state.get("publishedContentDigest") != candidate_digest
            or not SHA256.fullmatch(str(state.get("publishedSha256", "")))
            or state.get("repositorySignaturePresent") is not True
        ):
            raise GitHubReleaseError(
                f"Published package evidence is invalid: {role}"
            )
    elif any(
        key in state
        for key in (
            "publishedContentDigest",
            "publishedSha256",
            "repositorySignaturePresent",
        )
    ):
        raise GitHubReleaseError(
            f"Absent package evidence contains published content: {role}"
        )


def validate_symbol_state(
    state: Any,
    entry: dict[str, str],
    require_matching: bool,
) -> None:
    """Validate one NuGet symbol observation against its candidate PDB."""
    if not isinstance(state, dict):
        raise GitHubReleaseError(
            f"Publication symbol state is invalid: {entry['packageId']}"
        )

    status = state.get("status")
    if (
        status not in {"absent", "matching"}
        or (require_matching and status != "matching")
        or state.get("pdbName") != entry["pdbName"]
        or state.get("url") != entry["symbolUrl"]
        or state.get("candidateSha256") != entry["sha256"]
    ):
        raise GitHubReleaseError(
            f"Publication symbol state is invalid: {entry['packageId']}"
        )

    if status == "matching":
        if state.get("publishedSha256") != entry["sha256"]:
            raise GitHubReleaseError(
                f"Published symbol evidence is invalid: {entry['packageId']}"
            )
    elif "publishedSha256" in state:
        raise GitHubReleaseError(
            f"Absent symbol evidence contains published content: {entry['packageId']}"
        )


def validate_observation_set(
    evidence: dict[str, Any],
    receipt: dict[str, Any],
    symbol_entries: dict[str, dict[str, str]],
    require_matching: bool,
) -> None:
    """Validate the exact package and symbol observations in one receipt."""
    packages = evidence.get("packages")
    symbols = evidence.get("symbols")
    expected_roles = {role for role, _ in PACKAGE_IDENTITIES}
    expected_ids = {package_id for _, package_id in PACKAGE_IDENTITIES}
    if (
        not isinstance(packages, dict)
        or set(packages) != expected_roles
        or not isinstance(symbols, dict)
        or set(symbols) != expected_ids
    ):
        raise GitHubReleaseError("Publication observation inventory is invalid.")

    for role, package_id in PACKAGE_IDENTITIES:
        validate_package_state(
            packages[role],
            role,
            package_id,
            receipt,
            require_matching,
        )
        validate_symbol_state(
            symbols[package_id],
            symbol_entries[package_id],
            require_matching,
        )


def validate_public_readback_files(
    publication_evidence: Path,
    readback: dict[str, Any],
    receipt: dict[str, Any],
) -> None:
    """Re-hash the public package and symbol bytes retained as evidence."""
    version = str(receipt["releaseVersion"])
    packages_root = publication_evidence / "packages"
    for role, package_id in PACKAGE_IDENTITIES:
        package_path = packages_root / nuget_publication.package_file_name(
            package_id,
            version,
            "nupkg",
        )
        state = readback["packages"][role]
        if sha256_file(package_path) != state["publishedSha256"]:
            raise GitHubReleaseError(
                f"Retained public package bytes are invalid: {role}"
            )
        try:
            digest = nuget_publication.canonical_package_digest(package_path)
        except nuget_publication.PublicationError as exception:
            raise GitHubReleaseError(
                f"Retained public package content is invalid: {role}"
            ) from exception
        if digest != state["publishedContentDigest"]:
            raise GitHubReleaseError(
                f"Retained public package content is invalid: {role}"
            )
        try:
            repository_signature_present = nuget_publication.package_has_signature(
                package_path
            )
        except nuget_publication.PublicationError as exception:
            raise GitHubReleaseError(
                f"Retained public package signature is invalid: {role}"
            ) from exception
        if not repository_signature_present:
            raise GitHubReleaseError(
                f"Retained public package has no repository signature: {role}"
            )

        symbol_state = readback["symbols"][package_id]
        symbol_path = packages_root / "symbols" / symbol_state["pdbName"]
        if (
            sha256_file(symbol_path) != symbol_state["publishedSha256"]
            or not symbol_path.read_bytes().startswith(b"BSJB")
        ):
            raise GitHubReleaseError(
                f"Retained public symbol bytes are invalid: {package_id}"
            )


def validate_publication_evidence(
    publication_evidence: Path,
    receipt: dict[str, Any],
) -> list[Path]:
    """Prove that NuGet package, symbol, and signature readback passed."""
    paths = {
        name: publication_evidence / name for name in PUBLICATION_EVIDENCE_FILES
    }
    preflight = read_json(paths["publication-preflight.json"], "publication preflight")
    symbol_manifest = read_json(
        publication_evidence / "symbol-readback-manifest.json",
        "symbol readback manifest",
    )
    readback = read_json(
        paths["nuget-publication-readback.json"],
        "NuGet publication readback",
    )
    signature_evidence = paths["nuget-signature-verification.txt"]
    if (
        not signature_evidence.is_file()
        or signature_evidence.is_symlink()
        or signature_evidence.stat().st_size == 0
    ):
        raise GitHubReleaseError(
            "NuGet signature verification evidence is missing or empty: "
            f"{signature_evidence}"
        )

    try:
        entries = nuget_publication.validated_symbol_entries(
            symbol_manifest,
            str(receipt["releaseVersion"]),
        )
    except nuget_publication.PublicationError as exception:
        raise GitHubReleaseError(
            f"Symbol readback manifest is invalid: {exception}"
        ) from exception
    symbol_entries = {entry["packageId"]: entry for entry in entries}

    require_release_identity(preflight, "publication preflight", receipt, "checkedUtc")
    validate_observation_set(
        preflight,
        receipt,
        symbol_entries,
        require_matching=False,
    )
    require_release_identity(readback, "NuGet publication readback", receipt, "verifiedUtc")
    validate_observation_set(
        readback,
        receipt,
        symbol_entries,
        require_matching=True,
    )
    validate_public_readback_files(publication_evidence, readback, receipt)
    return [paths[name] for name in PUBLICATION_EVIDENCE_FILES]


def validate_staged_evidence(
    publication_evidence: Path,
    candidate_root: Path,
    receipt: dict[str, Any],
) -> list[Path]:
    """Validate the pre-publish evidence that authorizes draft staging."""
    paths = {name: publication_evidence / name for name in STAGED_EVIDENCE_FILES}
    candidate_path = paths["candidate-receipt.json"]
    candidate = read_json(candidate_path, "candidate receipt")
    try:
        nuget_publication.validate_candidate_receipt(candidate, candidate_root)
    except nuget_publication.PublicationError as exception:
        raise GitHubReleaseError(f"Candidate receipt is invalid: {exception}") from exception
    if sha256_file(candidate_path) != receipt["candidateReceiptSha256"]:
        raise GitHubReleaseError("Publication receipt does not bind the candidate receipt.")

    trust_path = paths["release-tag-trust-root.json"]
    trust = read_json(trust_path, "release tag trust-root receipt")
    if (
        sha256_file(trust_path) != receipt["tagTrustRootSha256"]
        or trust.get("schemaVersion") != 2
        or trust.get("kind") != "release-tag-trust-root"
        or trust.get("repository") != receipt["repository"]
        or trust.get("tag") != receipt["releaseTag"]
        or trust.get("commit") != receipt["sourceCommit"]
    ):
        raise GitHubReleaseError("Release tag trust-root evidence is invalid.")

    symbol_manifest = read_json(
        paths["symbol-readback-manifest.json"],
        "symbol readback manifest",
    )
    try:
        entries = nuget_publication.validated_symbol_entries(
            symbol_manifest,
            str(receipt["releaseVersion"]),
        )
    except nuget_publication.PublicationError as exception:
        raise GitHubReleaseError(f"Symbol readback manifest is invalid: {exception}") from exception
    symbols = {entry["packageId"]: entry for entry in entries}

    preflight = read_json(
        paths["candidate-publication-preflight.json"],
        "candidate publication preflight",
    )
    if (
        preflight.get("schemaVersion") != nuget_publication.SCHEMA_VERSION
        or preflight.get("expectedReleaseTag") != receipt["expectedReleaseTag"]
        or "releaseTag" in preflight
        or preflight.get("releaseVersion") != receipt["releaseVersion"]
        or preflight.get("sourceCommit") != receipt["sourceCommit"]
    ):
        raise GitHubReleaseError("Candidate publication preflight identity is invalid.")
    validate_observation_set(
        preflight,
        receipt,
        symbols,
        require_matching=False,
    )
    if any(
        state.get("status") != "absent"
        for state in (
            *preflight["packages"].values(),
            *preflight["symbols"].values(),
        )
    ):
        raise GitHubReleaseError("Candidate version was not fully absent before tagging.")

    runtime = read_json(
        candidate_root / "local-package-consumer/local-package-runtime.json",
        "local package runtime qualification",
    )
    if (
        runtime.get("schemaVersion") != 1
        or runtime.get("kind") != "local-package-runtime-qualification"
        or runtime.get("releaseTag") != receipt["expectedReleaseTag"]
        or runtime.get("releaseVersion") != receipt["releaseVersion"]
        or runtime.get("sourceCommit") != receipt["sourceCommit"]
        or runtime.get("engineImage") != receipt["mysql84Image"]
        or runtime.get("consumerBoundary") != "isolated-local-package"
        or runtime.get("projectReferences") != 0
        or runtime.get("runtimeSmoke") != "pass"
    ):
        raise GitHubReleaseError("Local package runtime qualification is invalid.")

    for path in paths.values():
        sha256_file(path)
    return list(paths.values())


def build_release_plan(
    repository: str,
    candidate_root: Path,
    publication_evidence: Path,
    changelog: Path,
) -> dict[str, Any]:
    """Build the deterministic pre-publication release plan."""
    if not REPOSITORY.fullmatch(repository):
        raise GitHubReleaseError(f"GitHub repository identity is invalid: {repository}")

    candidate_root = candidate_root.resolve()
    publication_evidence = publication_evidence.resolve()
    try:
        # Repeat the independent manifest readback in the write-capable job.
        # This keeps an earlier job's acceptance from becoming implicit trust.
        release_evidence.verify_manifest(candidate_root, None)
    except release_evidence.EvidenceError as exception:
        raise GitHubReleaseError(
            f"Release-candidate evidence verification failed: {exception}"
        ) from exception

    receipt = read_json(
        publication_evidence / "release-publication-receipt.json",
        "release publication receipt",
    )
    package_map = validate_candidate_receipt(receipt, repository, candidate_root)
    manifest = read_json(
        candidate_root / release_evidence.MANIFEST_NAME,
        "release-candidate manifest",
    )

    version = str(receipt.get("releaseVersion", ""))
    tag = str(receipt.get("releaseTag", ""))
    source_commit = str(receipt.get("sourceCommit", ""))
    candidate_run_id = str(receipt.get("releaseCandidateRunId", ""))
    source = manifest.get("source") or {}
    if (
        manifest.get("releaseCandidateRunId") != candidate_run_id
        or manifest.get("releaseVersion") != version
        or manifest.get("expectedReleaseTag") != tag
        or source.get("ref") != "refs/heads/main"
        or source.get("tag") is not None
        or source.get("commit") != source_commit
        or nuget_publication.normalize_repository(str(source.get("repository", "")))
        != repository.lower()
    ):
        raise GitHubReleaseError(
            "Release-candidate manifest and publication receipt disagree."
        )

    artifact_entries = manifest.get("artifacts")
    if not isinstance(artifact_entries, list):
        raise GitHubReleaseError("Release-candidate manifest has no artifact inventory.")

    manifest_artifacts: dict[str, dict[str, Any]] = {}
    for entry in artifact_entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("path"), str):
            raise GitHubReleaseError("Release-candidate artifact inventory is invalid.")
        path = entry["path"]
        if path in manifest_artifacts:
            raise GitHubReleaseError(f"Candidate manifest repeats asset path: {path}")
        manifest_artifacts[path] = entry

    candidate_paths: list[Path] = []
    validate_candidate_package_digests(receipt, package_map)
    for package in package_map.values():
        for path in package.values():
            relative = path.relative_to(candidate_root).as_posix()
            candidate_paths.append(
                require_candidate_artifact(
                    candidate_root,
                    manifest_artifacts,
                    relative,
                )
            )

    for relative in RELEASE_CANDIDATE_EVIDENCE_FILES:
        path = candidate_root / relative
        if relative in (release_evidence.MANIFEST_NAME, release_evidence.CHECKSUM_NAME):
            # The detached checksum binds the manifest; neither file can list
            # itself without creating a recursive digest dependency.
            sha256_file(path)
            candidate_paths.append(path)
        else:
            candidate_paths.append(
                require_candidate_artifact(
                    candidate_root,
                    manifest_artifacts,
                    relative,
                )
            )

    sbom_paths = sorted(
        entry["path"]
        for entry in artifact_entries
        if isinstance(entry, dict) and entry.get("role") == "sbom"
    )
    if not sbom_paths:
        raise GitHubReleaseError("Candidate manifest does not inventory an SBOM.")
    for relative in sbom_paths:
        candidate_paths.append(
            require_candidate_artifact(
                candidate_root,
                manifest_artifacts,
                relative,
            )
        )

    staged_paths = validate_staged_evidence(
        publication_evidence,
        candidate_root,
        receipt,
    )
    assets = [
        asset_record(path)
        for path in candidate_paths + staged_paths
    ]
    asset_names = [asset["name"] for asset in assets]
    if len(asset_names) != len(set(asset_names)):
        raise GitHubReleaseError("GitHub release asset names must be unique.")

    notes = extract_release_notes(changelog.resolve(), version)
    prerelease = "-" in version
    return {
        "schemaVersion": SCHEMA_VERSION,
        "phase": "staged",
        "repository": repository,
        "releaseTag": tag,
        "releaseVersion": version,
        "sourceCommit": source_commit,
        "name": f"Doka.EntityFrameworkCore.MySql {version}",
        "prerelease": prerelease,
        "latest": not prerelease,
        "notes": notes,
        "notesSha256": sha256_bytes(notes.encode("utf-8")),
        "assets": assets,
    }


def validate_plan(plan: dict[str, Any]) -> None:
    """Reject a tampered or incomplete plan before any remote mutation."""
    version = str(plan.get("releaseVersion", ""))
    tag = str(plan.get("releaseTag", ""))
    source_commit = str(plan.get("sourceCommit", ""))
    notes = str(plan.get("notes", ""))
    if (
        plan.get("schemaVersion") != SCHEMA_VERSION
        or plan.get("phase") != "staged"
        or not REPOSITORY.fullmatch(str(plan.get("repository", "")))
        or tag != f"v{version}"
        or not release_evidence.SEMANTIC_VERSION_TAG.fullmatch(tag)
        or not SHA1.fullmatch(source_commit)
        or plan.get("name") != f"Doka.EntityFrameworkCore.MySql {version}"
        or plan.get("prerelease") != ("-" in version)
        or plan.get("latest") != ("-" not in version)
        or not notes
        or plan.get("notesSha256") != sha256_bytes(notes.encode("utf-8"))
    ):
        raise GitHubReleaseError("GitHub release plan identity is invalid.")

    assets = plan.get("assets")
    if not isinstance(assets, list) or not assets:
        raise GitHubReleaseError("GitHub release plan has no assets.")

    names: set[str] = set()
    for asset in assets:
        if not isinstance(asset, dict):
            raise GitHubReleaseError("GitHub release plan contains an invalid asset.")
        path = Path(str(asset.get("path", "")))
        name = str(asset.get("name", ""))
        digest = sha256_file(path)
        size = path.stat().st_size
        if (
            name != path.name
            or name in names
            or asset.get("sizeBytes") != size
            or not SHA256.fullmatch(str(asset.get("sha256", "")))
            or asset.get("sha256") != digest
        ):
            raise GitHubReleaseError(f"GitHub release plan asset is invalid: {name}")
        names.add(name)


def run_git(repo: Path, *arguments: str) -> str:
    """Run a read-only Git command against the trusted publication checkout."""
    result = subprocess.run(
        ("git", *arguments),
        cwd=repo,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        message = result.stderr.strip() or result.stdout.strip()
        raise GitHubReleaseError(
            f"Git release identity check failed ({' '.join(arguments)}): {message}"
        )
    return result.stdout.strip()


def verify_local_tag(repo: Path, plan: dict[str, Any]) -> None:
    """Require an annotated local tag at the exact planned source commit."""
    tag = str(plan["releaseTag"])
    if run_git(repo, "cat-file", "-t", f"refs/tags/{tag}") != "tag":
        raise GitHubReleaseError(f"Release tag must be annotated: {tag}")
    if run_git(repo, "rev-parse", f"refs/tags/{tag}^{{commit}}") != plan["sourceCommit"]:
        raise GitHubReleaseError("Release tag does not identify the planned source commit.")


class GitHubCliClient:
    """Execute the narrow GitHub release protocol through the official CLI."""

    @staticmethod
    def _run_text(*arguments: str, allow_not_found: bool = False) -> str | None:
        """Run a GitHub CLI command and map an explicitly permitted 404 to absence.

        Every other non-zero exit remains a hard failure so authentication,
        authorization, transport, and server errors cannot be mistaken for a
        missing remote resource.
        """
        result = subprocess.run(
            ("gh", *arguments),
            check=False,
            capture_output=True,
            text=True,
        )
        if result.returncode == 0:
            return result.stdout

        message = result.stderr.strip() or result.stdout.strip()
        if allow_not_found and "HTTP 404" in message:
            return None
        raise GitHubReleaseError(
            f"GitHub CLI command failed ({' '.join(arguments)}): {message}"
        )

    def get_release(self, repository: str, tag: str) -> dict[str, Any] | None:
        """Read a published release or draft from the complete inventory."""
        value = self._run_text(
            "api",
            "--paginate",
            "--slurp",
            "-H",
            "Accept: application/vnd.github+json",
            "-H",
            f"X-GitHub-Api-Version: {GITHUB_API_VERSION}",
            f"/repos/{repository}/releases?per_page=100",
        )
        try:
            pages = json.loads(str(value))
        except json.JSONDecodeError as exception:
            raise GitHubReleaseError(
                "GitHub returned invalid release inventory JSON."
            ) from exception
        if not isinstance(pages, list) or any(
            not isinstance(page, list) for page in pages
        ):
            raise GitHubReleaseError("GitHub returned an invalid release inventory.")

        matches: list[dict[str, Any]] = []
        for page in pages:
            for release in page:
                if not isinstance(release, dict):
                    raise GitHubReleaseError(
                        "GitHub returned an invalid release inventory object."
                    )
                if release.get("tag_name") == tag:
                    matches.append(release)

        if len(matches) > 1:
            raise GitHubReleaseError(f"GitHub returned duplicate releases for tag: {tag}")
        return matches[0] if matches else None

    def verify_tag(self, repository: str, tag: str, commit: str) -> None:
        """Resolve the remote annotated tag chain to its final commit."""
        value = self._run_text("api", f"/repos/{repository}/git/ref/tags/{tag}")
        try:
            reference = json.loads(str(value))
        except json.JSONDecodeError as exception:
            raise GitHubReleaseError("GitHub returned invalid tag reference JSON.") from exception

        if not isinstance(reference, dict):
            raise GitHubReleaseError("GitHub returned an invalid tag reference.")

        target = reference.get("object")
        if not isinstance(target, dict) or target.get("type") != "tag":
            raise GitHubReleaseError(f"Release tag must be annotated on GitHub: {tag}")

        seen: set[str] = set()
        for _ in range(8):
            target_sha = str(target.get("sha", ""))
            if not SHA1.fullmatch(target_sha) or target_sha in seen:
                raise GitHubReleaseError("GitHub release tag chain is invalid or cyclic.")
            seen.add(target_sha)

            tag_value = self._run_text(
                "api",
                f"/repos/{repository}/git/tags/{target_sha}",
            )
            try:
                tag_object = json.loads(str(tag_value))
            except json.JSONDecodeError as exception:
                raise GitHubReleaseError(
                    "GitHub returned invalid annotated-tag JSON."
                ) from exception

            if not isinstance(tag_object, dict) or not isinstance(
                tag_object.get("object"),
                dict,
            ):
                raise GitHubReleaseError("GitHub returned an invalid annotated-tag object.")

            target = tag_object["object"]
            target_type = target.get("type")
            if target_type == "commit":
                if target.get("sha") != commit:
                    raise GitHubReleaseError(
                        "GitHub release tag does not identify the planned source commit."
                    )
                return
            if target_type != "tag":
                raise GitHubReleaseError(
                    "GitHub release tag must ultimately identify a commit."
                )

        raise GitHubReleaseError("GitHub release tag chain exceeds the safety limit.")

    def create_draft(self, plan: dict[str, Any]) -> None:
        """Create a draft without granting the CLI authority to create a tag."""
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            prefix="doka-release-notes-",
            suffix=".md",
        ) as notes:
            notes.write(str(plan["notes"]) + "\n")
            notes.flush()
            arguments = [
                "release",
                "create",
                str(plan["releaseTag"]),
                "--repo",
                str(plan["repository"]),
                "--draft",
                "--verify-tag",
                "--title",
                str(plan["name"]),
                "--notes-file",
                notes.name,
            ]
            if plan["prerelease"]:
                arguments.append("--prerelease")
            if not plan["latest"]:
                arguments.append("--latest=false")
            self._run_text(*arguments)

    def upload_assets(
        self,
        repository: str,
        tag: str,
        paths: list[Path],
    ) -> None:
        """Upload missing assets and intentionally omit the destructive clobber flag."""
        if not paths:
            return
        self._run_text(
            "release",
            "upload",
            tag,
            "--repo",
            repository,
            *(str(path) for path in paths),
        )

    def publish_release(self, plan: dict[str, Any]) -> None:
        """Publish the complete draft with an explicit latest-release policy."""
        self._run_text(
            "release",
            "edit",
            str(plan["releaseTag"]),
            "--repo",
            str(plan["repository"]),
            "--draft=false",
            f"--prerelease={str(bool(plan['prerelease'])).lower()}",
            f"--latest={str(bool(plan['latest'])).lower()}",
        )

    def download_asset(self, repository: str, asset_id: int) -> bytes:
        """Download an asset through the authenticated octet-stream endpoint."""
        result = subprocess.run(
            (
                "gh",
                "api",
                "-H",
                "Accept: application/octet-stream",
                f"/repos/{repository}/releases/assets/{asset_id}",
            ),
            check=False,
            capture_output=True,
        )
        if result.returncode != 0:
            message = result.stderr.decode("utf-8", errors="replace").strip()
            raise GitHubReleaseError(
                f"Unable to read back GitHub release asset {asset_id}: {message}"
            )
        return result.stdout

    def is_latest(self, repository: str, release_id: int) -> bool:
        """Compare the release with GitHub's canonical latest-release endpoint."""
        value = self._run_text(
            "api",
            f"/repos/{repository}/releases/latest",
            allow_not_found=True,
        )
        if value is None:
            return False
        try:
            latest = json.loads(value)
        except json.JSONDecodeError as exception:
            raise GitHubReleaseError("GitHub returned invalid latest-release JSON.") from exception
        if not isinstance(latest, dict):
            raise GitHubReleaseError("GitHub returned an invalid latest-release object.")
        return latest.get("id") == release_id


def verify_release_metadata(
    release: dict[str, Any],
    plan: dict[str, Any],
) -> None:
    """Require remote release metadata to match the deterministic plan."""
    # target_commitish is not authoritative when a release uses an existing
    # tag. The independently resolved remote annotated tag is authoritative.
    if (
        release.get("tag_name") != plan["releaseTag"]
        or release.get("name") != plan["name"]
        or not isinstance(release.get("draft"), bool)
        or not isinstance(release.get("prerelease"), bool)
        or normalize_markdown(str(release.get("body", "")))
        != normalize_markdown(str(plan["notes"]))
        or release.get("prerelease") != plan["prerelease"]
    ):
        raise GitHubReleaseError("Existing GitHub release metadata conflicts with the plan.")


def verify_release_assets(
    release: dict[str, Any],
    plan: dict[str, Any],
    client: ReleaseClient,
) -> list[Path]:
    """Read back planned assets and return the exact missing local paths."""
    planned_assets = {asset["name"]: asset for asset in plan["assets"]}
    remote_assets = release.get("assets")
    if not isinstance(remote_assets, list):
        raise GitHubReleaseError("GitHub release has no readable asset inventory.")

    remote_names: set[str] = set()
    for asset in remote_assets:
        if not isinstance(asset, dict):
            raise GitHubReleaseError("GitHub release contains an invalid asset object.")
        name = str(asset.get("name", ""))
        if name in remote_names or name not in planned_assets:
            raise GitHubReleaseError(f"GitHub release contains an unexpected asset: {name}")
        remote_names.add(name)

        asset_id = asset.get("id")
        planned = planned_assets[name]
        if (
            asset.get("state") != "uploaded"
            or asset.get("size") != planned["sizeBytes"]
            or isinstance(asset_id, bool)
            or not isinstance(asset_id, int)
        ):
            raise GitHubReleaseError(f"GitHub release asset metadata conflicts: {name}")

        payload = client.download_asset(str(plan["repository"]), asset_id)
        if len(payload) != planned["sizeBytes"] or sha256_bytes(payload) != planned["sha256"]:
            raise GitHubReleaseError(f"GitHub release asset readback conflicts: {name}")

    return [
        Path(planned_assets[name]["path"])
        for name in sorted(set(planned_assets) - remote_names)
    ]


def wait_for_release(
    client: ReleaseClient,
    repository: str,
    tag: str,
    sleep: Callable[[float], None],
    readback_attempts: int,
) -> dict[str, Any] | None:
    """Wait a bounded interval for a newly created release to become visible."""
    for attempt in range(readback_attempts):
        release = client.get_release(repository, tag)
        if release is not None:
            return release
        if attempt + 1 < readback_attempts:
            sleep(READBACK_DELAY_SECONDS)
    return None


def wait_for_complete_assets(
    client: ReleaseClient,
    repository: str,
    tag: str,
    plan: dict[str, Any],
    sleep: Callable[[float], None],
    readback_attempts: int,
) -> dict[str, Any] | None:
    """Wait for every uploaded asset to appear and pass exact readback."""
    for attempt in range(readback_attempts):
        release = client.get_release(repository, tag)
        if release is not None:
            verify_release_metadata(release, plan)
            if not verify_release_assets(release, plan, client):
                return release
        if attempt + 1 < readback_attempts:
            sleep(READBACK_DELAY_SECONDS)
    return None


def build_receipt(
    release: dict[str, Any],
    plan: dict[str, Any],
) -> dict[str, Any]:
    """Build the durable finalization receipt from verified remote state."""
    release_id = release.get("id")
    release_url = release.get("html_url")
    published_at = release.get("published_at")
    expected_url = (
        f"https://github.com/{plan['repository']}/releases/tag/{plan['releaseTag']}"
    )
    if (
        isinstance(release_id, bool)
        or not isinstance(release_id, int)
        or release_id <= 0
        or release_url != expected_url
        or not isinstance(published_at, str)
        or not published_at
    ):
        raise GitHubReleaseError("Published GitHub release identity is incomplete.")

    remote_assets = {asset["name"]: asset for asset in release["assets"]}
    return {
        "schemaVersion": SCHEMA_VERSION,
        "status": "published-and-verified",
        "repository": plan["repository"],
        "releaseId": release_id,
        "releaseTag": plan["releaseTag"],
        "releaseVersion": plan["releaseVersion"],
        "sourceCommit": plan["sourceCommit"],
        "releaseUrl": release_url,
        "publishedAt": published_at,
        "immutable": True,
        "prerelease": plan["prerelease"],
        "latest": plan["latest"],
        "notesSha256": plan["notesSha256"],
        "assets": [
            {
                "name": asset["name"],
                "assetId": remote_assets[asset["name"]]["id"],
                "sizeBytes": asset["sizeBytes"],
                "sha256": asset["sha256"],
            }
            for asset in plan["assets"]
        ],
    }


def finalize_release(
    plan: dict[str, Any],
    client: ReleaseClient,
    sleep: Callable[[float], None] = time.sleep,
    readback_attempts: int = 6,
) -> dict[str, Any]:
    """Finalize one immutable release without replacing remote state.

    The protocol validates the tag before mutation, reconciles an existing
    draft or published release, and uploads only missing assets. It verifies
    the tag and remote readback around publication so stale or conflicting
    state cannot become release evidence.
    """
    validate_plan(plan)
    if plan["phase"] != "staged":
        raise GitHubReleaseError("Only the verified staged plan may be published.")
    repository = str(plan["repository"])
    tag = str(plan["releaseTag"])
    client.verify_tag(repository, tag, str(plan["sourceCommit"]))
    release = client.get_release(repository, tag)
    if release is None:
        client.create_draft(plan)
        release = wait_for_release(
            client,
            repository,
            tag,
            sleep,
            readback_attempts,
        )
        if release is None:
            raise GitHubReleaseError(
                "GitHub did not return the created release draft within the "
                "readback window."
            )

    verify_release_metadata(release, plan)
    missing = verify_release_assets(release, plan, client)
    if release["draft"] is False:
        if missing:
            raise GitHubReleaseError("Published GitHub release is missing planned assets.")
        if release.get("immutable") is not True:
            raise GitHubReleaseError("Published GitHub release is not immutable.")
        release_id = release.get("id")
        if (
            isinstance(release_id, bool)
            or not isinstance(release_id, int)
            or client.is_latest(repository, release_id) != plan["latest"]
        ):
            raise GitHubReleaseError("Published GitHub release latest status conflicts.")
        client.verify_tag(repository, tag, str(plan["sourceCommit"]))
        return build_receipt(release, plan)

    client.upload_assets(repository, tag, missing)
    release = wait_for_complete_assets(
        client,
        repository,
        tag,
        plan,
        sleep,
        readback_attempts,
    )
    if release is None:
        raise GitHubReleaseError(
            "GitHub release draft did not expose every uploaded asset within "
            "the readback window."
        )

    client.verify_tag(repository, tag, str(plan["sourceCommit"]))
    client.publish_release(plan)
    for attempt in range(readback_attempts):
        release = client.get_release(repository, tag)
        if release is not None:
            verify_release_metadata(release, plan)
            if release["draft"] is False and release.get("immutable") is True:
                if verify_release_assets(release, plan, client):
                    raise GitHubReleaseError(
                        "Published GitHub release is missing planned assets."
                    )
                release_id = release.get("id")
                if (
                    isinstance(release_id, bool)
                    or not isinstance(release_id, int)
                    or client.is_latest(repository, release_id) != plan["latest"]
                ):
                    raise GitHubReleaseError(
                        "Published GitHub release latest status conflicts."
                    )
                client.verify_tag(repository, tag, str(plan["sourceCommit"]))
                return build_receipt(release, plan)
        if attempt + 1 < readback_attempts:
            sleep(READBACK_DELAY_SECONDS)

    raise GitHubReleaseError(
        "GitHub release did not become published and immutable within the readback window."
    )


def stage_release(
    plan: dict[str, Any],
    client: ReleaseClient,
    sleep: Callable[[float], None] = time.sleep,
    readback_attempts: int = 6,
) -> dict[str, Any]:
    """Create or reconcile a complete draft without publishing it."""
    validate_plan(plan)
    if plan["phase"] != "staged":
        raise GitHubReleaseError("Only a staged release plan may create a draft.")

    repository = str(plan["repository"])
    tag = str(plan["releaseTag"])
    client.verify_tag(repository, tag, str(plan["sourceCommit"]))
    release = client.get_release(repository, tag)
    if release is None:
        client.create_draft(plan)
        release = wait_for_release(
            client,
            repository,
            tag,
            sleep,
            readback_attempts,
        )
        if release is None:
            raise GitHubReleaseError(
                "GitHub did not return the created release draft within the "
                "readback window."
            )

    verify_release_metadata(release, plan)
    missing = verify_release_assets(release, plan, client)
    if release["draft"] is False:
        if missing:
            raise GitHubReleaseError(
                "Published GitHub release is missing staged identity assets."
            )
        if release.get("immutable") is not True:
            raise GitHubReleaseError("Published GitHub release is not immutable.")
        release_id = release.get("id")
        if (
            isinstance(release_id, bool)
            or not isinstance(release_id, int)
            or client.is_latest(repository, release_id) != plan["latest"]
        ):
            raise GitHubReleaseError("Published GitHub release latest status conflicts.")
        client.verify_tag(repository, tag, str(plan["sourceCommit"]))
        return {
            "schemaVersion": SCHEMA_VERSION,
            "status": "release-already-published",
            "repository": repository,
            "releaseTag": tag,
            "releaseVersion": plan["releaseVersion"],
            "sourceCommit": plan["sourceCommit"],
            "assetCount": len(plan["assets"]),
        }

    client.upload_assets(repository, tag, missing)
    release = wait_for_complete_assets(
        client,
        repository,
        tag,
        plan,
        sleep,
        readback_attempts,
    )
    if release is None:
        raise GitHubReleaseError(
            "GitHub release draft did not expose every uploaded asset within "
            "the readback window."
        )
    client.verify_tag(repository, tag, str(plan["sourceCommit"]))

    return {
        "schemaVersion": SCHEMA_VERSION,
        "status": "draft-staged-and-verified",
        "repository": repository,
        "releaseTag": tag,
        "releaseVersion": plan["releaseVersion"],
        "sourceCommit": plan["sourceCommit"],
        "assetCount": len(plan["assets"]),
    }


def validate_release_receipt(
    receipt: dict[str, Any],
    plan: dict[str, Any],
) -> None:
    """Bind the immutable GitHub readback receipt to the staged plan."""
    expected_url = (
        f"https://github.com/{plan['repository']}/releases/tag/"
        f"{plan['releaseTag']}"
    )
    release_id = receipt.get("releaseId")
    if (
        receipt.get("schemaVersion") != SCHEMA_VERSION
        or receipt.get("status") != "published-and-verified"
        or isinstance(release_id, bool)
        or not isinstance(release_id, int)
        or release_id <= 0
        or receipt.get("repository") != plan["repository"]
        or receipt.get("releaseTag") != plan["releaseTag"]
        or receipt.get("releaseVersion") != plan["releaseVersion"]
        or receipt.get("sourceCommit") != plan["sourceCommit"]
        or receipt.get("releaseUrl") != expected_url
        or receipt.get("immutable") is not True
        or receipt.get("prerelease") != plan["prerelease"]
        or receipt.get("latest") != plan["latest"]
        or receipt.get("notesSha256") != plan["notesSha256"]
    ):
        raise GitHubReleaseError("GitHub release readback identity is invalid.")
    require_timestamp(receipt.get("publishedAt"), "GitHub release readback")

    raw_assets = receipt.get("assets")
    if not isinstance(raw_assets, list):
        raise GitHubReleaseError("GitHub release readback asset inventory is invalid.")
    actual: dict[str, tuple[int, str]] = {}
    for asset in raw_assets:
        if not isinstance(asset, dict):
            raise GitHubReleaseError(
                "GitHub release readback asset inventory is invalid."
            )
        name = str(asset.get("name", ""))
        if name in actual:
            raise GitHubReleaseError(
                f"GitHub release readback repeats asset: {name}"
            )
        size = asset.get("sizeBytes")
        digest = str(asset.get("sha256", ""))
        asset_id = asset.get("assetId")
        if (
            isinstance(size, bool)
            or not isinstance(size, int)
            or size < 0
            or not SHA256.fullmatch(digest)
            or isinstance(asset_id, bool)
            or not isinstance(asset_id, int)
            or asset_id <= 0
        ):
            raise GitHubReleaseError(
                f"GitHub release readback asset is invalid: {name}"
            )
        actual[name] = (size, digest)

    expected = {
        str(asset["name"]): (int(asset["sizeBytes"]), str(asset["sha256"]))
        for asset in plan["assets"]
    }
    if actual != expected:
        raise GitHubReleaseError(
            "GitHub release readback asset inventory conflicts with the plan."
        )


def build_completion_receipt(
    repository: str,
    candidate_root: Path,
    publication_evidence: Path,
    changelog: Path,
) -> dict[str, Any]:
    """Validate and bind all post-publication completion evidence."""
    plan = build_release_plan(
        repository,
        candidate_root,
        publication_evidence,
        changelog,
    )
    release_receipt_path = publication_evidence / "github-release-readback.json"
    release_receipt = read_json(
        release_receipt_path,
        "GitHub release readback",
    )
    validate_release_receipt(release_receipt, plan)

    publication_receipt = read_json(
        publication_evidence / "release-publication-receipt.json",
        "release publication receipt",
    )
    completion_paths = [
        release_receipt_path,
        *validate_publication_evidence(
            publication_evidence,
            publication_receipt,
        ),
    ]
    readback = read_json(
        publication_evidence / "nuget-publication-readback.json",
        "NuGet publication readback",
    )
    completed_utc = readback.get("verifiedUtc")
    require_timestamp(completed_utc, "NuGet publication readback")
    return {
        "schemaVersion": 1,
        "kind": "release-publication-completion",
        "status": "published-and-verified",
        "repository": repository,
        "releaseTag": plan["releaseTag"],
        "releaseVersion": plan["releaseVersion"],
        "sourceCommit": plan["sourceCommit"],
        "completedUtc": completed_utc,
        "evidence": [asset_record(path) for path in completion_paths],
    }


def prepare_command(args: argparse.Namespace) -> None:
    """Create and persist the deterministic plan without mutating GitHub."""
    plan = build_release_plan(
        args.repository,
        args.candidate_root,
        args.publication_evidence,
        args.changelog,
    )
    write_json(args.output.resolve(), plan)


def publish_command(args: argparse.Namespace) -> None:
    """Finalize one planned release and persist its verified receipt."""
    plan = read_json(args.plan.resolve(), "GitHub release plan")
    validate_plan(plan)
    verify_local_tag(args.repo.resolve(), plan)
    receipt = finalize_release(plan, GitHubCliClient())
    write_json(args.output.resolve(), receipt)


def stage_command(args: argparse.Namespace) -> None:
    """Stage one planned draft and persist its verified receipt."""
    plan = read_json(args.plan.resolve(), "GitHub release plan")
    validate_plan(plan)
    verify_local_tag(args.repo.resolve(), plan)
    receipt = stage_release(plan, GitHubCliClient())
    write_json(args.output.resolve(), receipt)


def complete_command(args: argparse.Namespace) -> None:
    """Persist one validated post-publication completion receipt."""
    receipt = build_completion_receipt(
        args.repository,
        args.candidate_root.resolve(),
        args.publication_evidence.resolve(),
        args.changelog.resolve(),
    )
    write_json(args.output.resolve(), receipt)


def build_parser() -> argparse.ArgumentParser:
    """Build the staged-release and completion command-line contract."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    prepare = subparsers.add_parser("prepare")
    prepare.add_argument("--repository", required=True)
    prepare.add_argument("--candidate-root", type=Path, required=True)
    prepare.add_argument("--publication-evidence", type=Path, required=True)
    prepare.add_argument("--changelog", type=Path, required=True)
    prepare.add_argument("--output", type=Path, required=True)
    prepare.set_defaults(handler=prepare_command)

    stage = subparsers.add_parser("stage")
    stage.add_argument("--repo", type=Path, required=True)
    stage.add_argument("--plan", type=Path, required=True)
    stage.add_argument("--output", type=Path, required=True)
    stage.set_defaults(handler=stage_command)

    publish = subparsers.add_parser("publish")
    publish.add_argument("--repo", type=Path, required=True)
    publish.add_argument("--plan", type=Path, required=True)
    publish.add_argument("--output", type=Path, required=True)
    publish.set_defaults(handler=publish_command)

    complete = subparsers.add_parser("complete")
    complete.add_argument("--repository", required=True)
    complete.add_argument("--candidate-root", type=Path, required=True)
    complete.add_argument("--publication-evidence", type=Path, required=True)
    complete.add_argument("--changelog", type=Path, required=True)
    complete.add_argument("--output", type=Path, required=True)
    complete.set_defaults(handler=complete_command)

    return parser


def main() -> int:
    """Execute one command and render expected failures without a traceback."""
    parser = build_parser()
    args = parser.parse_args()
    try:
        args.handler(args)
    except GitHubReleaseError as exception:
        print(f"GitHub release finalization failed: {exception}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
