#!/usr/bin/env python3
"""Validate, publish-preflight, and read back NuGet release artifacts.

The release-candidate workflow proves that a package is eligible for release.
This module enforces its same-run publication boundary: it binds the qualified
candidate bytes to the current trusted main commit and exact semantic version
tag before any credential is requested. It also makes publication retries safe
by distinguishing an absent package from matching or conflicting content
already present on NuGet.org.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from collections.abc import Sequence
from datetime import UTC, datetime
from io import BytesIO
from pathlib import Path, PurePosixPath
from typing import Any, Callable
from xml.etree import ElementTree

from . import evidence as release_evidence
from . import qualification as release_qualification
from . import trust as release_trust


SCHEMA_VERSION = 3
SYMBOL_MANIFEST_SCHEMA_VERSION = 1
CANDIDATE_RECEIPT_SCHEMA_VERSION = 1
PUBLICATION_RECEIPT_SCHEMA_VERSION = 4
PUBLICATION_READBACK_TIMEOUT_SECONDS = 3600
PUBLICATION_READBACK_POLL_INTERVAL_SECONDS = 30
CANDIDATE_RECEIPT_KIND = "release-candidate-receipt"
PUBLICATION_RECEIPT_KIND = "release-publication-receipt"
PROVIDER_PACKAGE_ID = "Doka.EntityFrameworkCore.MySql"
SPATIAL_PACKAGE_ID = "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
CACHE_PACKAGE_ID = "Doka.Caching.MySql"
PACKAGE_IDENTITIES = {
    "provider": PROVIDER_PACKAGE_ID,
    "spatial": SPATIAL_PACKAGE_ID,
    "cache": CACHE_PACKAGE_ID,
}
CANDIDATE_WORKFLOW = "release-candidate"
CANDIDATE_WORKFLOW_PATH = ".github/workflows/release-candidate.yml"
NUGET_SOURCE = "https://api.nuget.org/v3/index.json"
NUGET_PACKAGE_BASE_ADDRESS_TYPE = "PackageBaseAddress/3.0.0"
NUGET_SYMBOL_SERVER = "https://symbols.nuget.org/download/symbols"
NUGET_SIGNATURE_ENTRY = ".signature.p7s"
SHA1 = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
RUN_ID = re.compile(r"[1-9][0-9]*")
SYMBOL_KEY = re.compile(r"[0-9a-f]{32}FFFFFFFF")
SERVICE_INDEX_VERSION = re.compile(r"3[.][0-9]+[.][0-9]+(?:-[0-9A-Za-z.-]+)?")
NORMALIZED_RELEASE_VERSION = re.compile(
    r"(?:0|[1-9][0-9]*)[.](?:0|[1-9][0-9]*)[.](?:0|[1-9][0-9]*)"
    r"(?:-[0-9a-z-]+(?:[.][0-9a-z-]+)*)?"
)


class PublicationError(RuntimeError):
    """Raised when evidence cannot authorize a NuGet publication action."""


class RemotePackageUnavailable(PublicationError):
    """Raised for a transient NuGet.org response that readback may retry."""


def read_json(path: Path, label: str) -> dict[str, Any]:
    """Read one regular JSON object with a diagnostic tied to its role."""
    if not path.is_file() or path.is_symlink():
        raise PublicationError(f"{label} is missing or non-regular: {path}")

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise PublicationError(f"{label} is unreadable: {path}") from exception

    if not isinstance(value, dict):
        raise PublicationError(f"{label} must contain a JSON object: {path}")

    return value


def write_json(path: Path, value: dict[str, Any]) -> None:
    """Write one canonical receipt after its complete value is available."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def append_github_outputs(path: Path | None, values: dict[str, str]) -> None:
    """Append simple validated scalar outputs without workflow-command syntax."""
    if path is None:
        return

    with path.open("a", encoding="utf-8") as stream:
        for name, value in values.items():
            if "\n" in value or "\r" in value:
                raise PublicationError(f"GitHub output {name} contains a newline.")
            stream.write(f"{name}={value}\n")


def run_git(repo: Path, *arguments: str) -> str:
    """Run one read-only Git identity command against the trusted checkout."""
    result = subprocess.run(
        ("git", *arguments),
        cwd=repo,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        message = result.stderr.strip() or result.stdout.strip()
        raise PublicationError(f"Git identity check failed ({' '.join(arguments)}): {message}")
    return result.stdout.strip()


def normalize_repository(value: str) -> str:
    """Normalize the supported GitHub remote forms to ``owner/repository``."""
    normalized = value.strip()
    prefixes = (
        "git@github.com:",
        "ssh://git@github.com/",
        "https://github.com/",
        "http://github.com/",
    )
    for prefix in prefixes:
        if normalized.startswith(prefix):
            normalized = normalized[len(prefix) :]
            break
    if normalized.endswith(".git"):
        normalized = normalized[:-4]
    return normalized.strip("/").lower()


def require_normalized_release_version(value: str) -> str:
    """Require the canonical NuGet-version subset used by release tags."""
    if not NORMALIZED_RELEASE_VERSION.fullmatch(value):
        raise PublicationError(f"Release version is not canonical for NuGet: {value}")

    prerelease = value.partition("-")[2]
    if any(
        len(identifier) > 1 and identifier.isdigit() and identifier.startswith("0")
        for identifier in prerelease.split(".")
        if identifier
    ):
        raise PublicationError(f"Release version is not canonical for NuGet: {value}")
    return value


def validate_release_version(args: argparse.Namespace) -> None:
    """Validate one operator-supplied release version before qualification."""
    require_normalized_release_version(args.version)


def require_https_base_address(value: str) -> str:
    """Validate one dynamically discovered NuGet package-content endpoint."""
    parsed = urllib.parse.urlsplit(value)
    if (
        parsed.scheme != "https"
        or not parsed.hostname
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
    ):
        raise PublicationError(f"NuGet package base address is invalid: {value}")
    return value.rstrip("/")


def package_file_name(package_id: str, version: str, extension: str) -> str:
    """Return the exact file name sealed by the candidate evidence contract."""
    return f"{package_id}.{version}.{extension}"


def package_paths(root: Path, version: str) -> dict[str, dict[str, Path]]:
    """Resolve the exact primary and symbol packages without using globs."""
    packages = root / "packages"
    return {
        role: {
            "package": packages / package_file_name(package_id, version, "nupkg"),
            "symbols": packages / package_file_name(package_id, version, "snupkg"),
        }
        for role, package_id in PACKAGE_IDENTITIES.items()
    }


def require_candidate_root(root: Path) -> Path:
    """Return one regular, non-symlinked candidate evidence directory."""
    absolute = root.absolute()
    if not absolute.is_dir() or absolute.is_symlink():
        raise PublicationError(f"Candidate root is missing or non-regular: {root}")
    return absolute.resolve()


def resolve_candidate_path(root: Path, value: Any, label: str) -> Path:
    """Resolve one canonical relative receipt path below the candidate root."""
    candidate_root = require_candidate_root(root)
    raw = str(value)
    path = PurePosixPath(raw)
    if (
        not raw
        or "\\" in raw
        or path.is_absolute()
        or ".." in path.parts
        or raw != path.as_posix()
    ):
        raise PublicationError(f"{label} is not a canonical relative path: {raw}")

    candidate = candidate_root.joinpath(*path.parts)
    if not candidate.is_file() or candidate.is_symlink():
        raise PublicationError(f"{label} is missing or non-regular: {raw}")

    resolved = candidate.resolve()
    try:
        resolved.relative_to(candidate_root)
    except ValueError as exception:
        raise PublicationError(f"{label} escapes the candidate root: {raw}") from exception
    return resolved


def sha256_file(path: Path) -> str:
    """Hash one regular file without loading an entire package into memory."""
    if not path.is_file() or path.is_symlink():
        raise PublicationError(f"File is missing or non-regular: {path}")

    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def safe_zip_entries(package: zipfile.ZipFile) -> list[zipfile.ZipInfo]:
    """Return canonical regular entries while rejecting ambiguous ZIP paths."""
    entries: list[zipfile.ZipInfo] = []
    names: set[str] = set()
    casefolded_names: set[str] = set()
    for entry in package.infolist():
        if entry.is_dir():
            continue

        name = entry.filename
        path = PurePosixPath(name)
        if (
            not name
            or "\\" in name
            or path.is_absolute()
            or ".." in path.parts
            or name in names
            or name.casefold() in casefolded_names
        ):
            raise PublicationError(f"NuGet package contains an invalid or duplicate path: {name}")

        names.add(name)
        casefolded_names.add(name.casefold())
        entries.append(entry)

    return sorted(entries, key=lambda entry: entry.filename)


def canonical_package_digest(value: bytes | Path) -> str:
    """Hash package payload entries independently of repository signatures.

    NuGet.org may add ``.signature.p7s`` when repository-signing a package.
    Comparing the ZIP bytes directly would therefore reject a valid retry. The
    canonical digest binds every other path and byte while excluding only that
    repository-owned signature entry.
    """
    try:
        package_source = BytesIO(value) if isinstance(value, bytes) else value
        with zipfile.ZipFile(package_source) as package:
            digest = hashlib.sha256()
            digest.update(b"doka-nupkg-content-v1\0")
            for entry in safe_zip_entries(package):
                if entry.filename.casefold() == NUGET_SIGNATURE_ENTRY:
                    continue
                content = package.read(entry)
                digest.update(entry.filename.encode("utf-8"))
                digest.update(b"\0")
                digest.update(len(content).to_bytes(8, byteorder="big"))
                digest.update(hashlib.sha256(content).digest())
            return digest.hexdigest()
    except (OSError, zipfile.BadZipFile, UnicodeError) as exception:
        raise PublicationError("NuGet package is not a readable canonical ZIP archive.") from exception


def package_has_signature(value: bytes | Path) -> bool:
    """Return whether one canonical NuGet signature entry is present."""
    try:
        package_source = BytesIO(value) if isinstance(value, bytes) else value
        with zipfile.ZipFile(package_source) as package:
            return any(
                entry.filename.casefold() == NUGET_SIGNATURE_ENTRY
                for entry in safe_zip_entries(package)
            )
    except (OSError, zipfile.BadZipFile, UnicodeError) as exception:
        raise PublicationError("NuGet package is not a readable canonical ZIP archive.") from exception


def package_metadata(path: Path) -> dict[str, Any]:
    """Read the identity, repository, and dependencies from one package."""
    try:
        with zipfile.ZipFile(path) as package:
            entries = safe_zip_entries(package)
            nuspec_entries = [entry for entry in entries if entry.filename.endswith(".nuspec")]
            if len(nuspec_entries) != 1:
                raise PublicationError(
                    f"NuGet package must contain exactly one nuspec: {path.name}"
                )
            document = ElementTree.fromstring(package.read(nuspec_entries[0]))
            pdb_entries = [
                entry
                for entry in entries
                if entry.filename.casefold().endswith(".pdb")
            ]

            def is_portable_pdb(entry: zipfile.ZipInfo) -> bool:
                with package.open(entry) as stream:
                    return stream.read(4) == b"BSJB"

            portable_pdb_count = sum(is_portable_pdb(entry) for entry in pdb_entries)
    except (OSError, zipfile.BadZipFile, ElementTree.ParseError) as exception:
        raise PublicationError(f"NuGet metadata is unreadable: {path}") from exception

    def local_name(element: ElementTree.Element) -> str:
        return element.tag.rsplit("}", maxsplit=1)[-1]

    metadata = next((element for element in document.iter() if local_name(element) == "metadata"), None)
    if metadata is None:
        raise PublicationError(f"NuGet package has no metadata element: {path.name}")

    def text(name: str) -> str:
        element = next((item for item in metadata if local_name(item) == name), None)
        return "" if element is None or element.text is None else element.text.strip()

    repository = next((item for item in metadata if local_name(item) == "repository"), None)
    dependencies = [
        {
            "id": element.attrib.get("id", ""),
            "version": element.attrib.get("version", ""),
        }
        for element in metadata.iter()
        if local_name(element) == "dependency"
    ]
    package_types = [
        element.attrib.get("name", "")
        for element in metadata.iter()
        if local_name(element) == "packageType"
    ]
    return {
        "id": text("id"),
        "version": text("version"),
        "repositoryUrl": "" if repository is None else repository.attrib.get("url", ""),
        "repositoryCommit": "" if repository is None else repository.attrib.get("commit", ""),
        "dependencies": dependencies,
        "packageTypes": package_types,
        "entries": [entry.filename for entry in entries],
        "pdbCount": len(pdb_entries),
        "portablePdbCount": portable_pdb_count,
    }


def validate_package_metadata(
    root: Path,
    version: str,
    repository: str,
    source_commit: str,
) -> dict[str, dict[str, str]]:
    """Bind package-internal metadata to the qualified source commit."""
    candidate_root = require_candidate_root(root)
    resolved = package_paths(candidate_root, version)
    result: dict[str, dict[str, str]] = {}
    for role, package_id in PACKAGE_IDENTITIES.items():
        primary = resolved[role]["package"]
        symbols = resolved[role]["symbols"]
        if not primary.is_file() or primary.is_symlink() or not symbols.is_file() or symbols.is_symlink():
            raise PublicationError(f"Candidate package pair is missing or non-regular: {package_id}")

        metadata = package_metadata(primary)
        symbol_metadata = package_metadata(symbols)
        if package_has_signature(primary):
            raise PublicationError(
                f"Candidate package must be unsigned before NuGet.org ingestion: {primary.name}"
            )
        if (
            metadata["id"] != package_id
            or metadata["version"] != version
            or "SymbolsPackage" in metadata["packageTypes"]
        ):
            raise PublicationError(
                f"Package metadata mismatch for {primary.name}: "
                f"id={metadata['id']}; version={metadata['version']}"
            )
        if normalize_repository(metadata["repositoryUrl"]) != repository.lower():
            raise PublicationError(f"Package repository URL mismatch for {primary.name}.")
        if metadata["repositoryCommit"] != source_commit:
            raise PublicationError(f"Package repository commit mismatch for {primary.name}.")
        if (
            symbol_metadata["id"] != package_id
            or symbol_metadata["version"] != version
            or symbol_metadata["packageTypes"] != ["SymbolsPackage"]
            or symbol_metadata["pdbCount"] == 0
            or symbol_metadata["portablePdbCount"] != symbol_metadata["pdbCount"]
        ):
            raise PublicationError(f"Symbol package metadata mismatch for {symbols.name}.")

        result[role] = {
            "id": package_id,
            # Receipts cross runner and job boundaries. Canonical relative paths
            # keep the receipt portable while the digest fields retain identity.
            "package": primary.relative_to(candidate_root).as_posix(),
            "symbols": symbols.relative_to(candidate_root).as_posix(),
            "contentDigest": canonical_package_digest(primary),
            "symbolsSha256": sha256_file(symbols),
        }

        if role == "spatial":
            provider_dependencies = [
                dependency
                for dependency in metadata["dependencies"]
                if dependency["id"] == PROVIDER_PACKAGE_ID
            ]
            if provider_dependencies != [{"id": PROVIDER_PACKAGE_ID, "version": version}]:
                raise PublicationError(
                    "The spatial package must depend on the exact provider release version."
                )
        elif role == "cache" and any(
            dependency["id"].casefold().startswith(("doka.entityframeworkcore.", "microsoft.entityframeworkcore", "pomelo."))
            for dependency in metadata["dependencies"]
        ):
            raise PublicationError("The cache package must remain independent of EF Core and Pomelo.")

    return result


def validate_candidate_receipt(
    receipt: dict[str, Any],
    candidate_root: Path,
) -> dict[str, dict[str, Path]]:
    """Revalidate an untagged candidate receipt at a new trust boundary."""
    root = require_candidate_root(candidate_root)
    run_id = str(receipt.get("candidateRunId", ""))
    run_attempt = str(receipt.get("candidateRunAttempt", ""))
    candidate_id = f"github-{run_id}"
    version = str(receipt.get("releaseVersion", ""))
    require_normalized_release_version(version)
    expected_release_tag = str(receipt.get("expectedReleaseTag", ""))
    repository = str(receipt.get("repository", ""))
    source_commit = str(receipt.get("sourceCommit", ""))
    if (
        receipt.get("schemaVersion") != CANDIDATE_RECEIPT_SCHEMA_VERSION
        or receipt.get("kind") != CANDIDATE_RECEIPT_KIND
        or not RUN_ID.fullmatch(run_id)
        or not RUN_ID.fullmatch(run_attempt)
        or receipt.get("releaseCandidateRunId") != candidate_id
        or root.name != candidate_id
        or receipt.get("sourceRef") != "refs/heads/main"
        or expected_release_tag != f"v{version}"
        or not release_evidence.SEMANTIC_VERSION_TAG.fullmatch(expected_release_tag)
        or normalize_repository(repository) != repository.lower()
        or not SHA1.fullmatch(source_commit)
        or not isinstance(receipt.get("mysql84Image"), str)
        or not receipt["mysql84Image"]
    ):
        raise PublicationError("Candidate receipt identity is invalid.")

    packages = receipt.get("packages")
    expected_roles = PACKAGE_IDENTITIES
    if not isinstance(packages, dict) or set(packages) != set(expected_roles):
        raise PublicationError("Candidate receipt package inventory is invalid.")

    resolved: dict[str, dict[str, Path]] = {}
    for role, package_id in expected_roles.items():
        package = packages.get(role)
        expected_keys = {"id", "package", "symbols", "contentDigest", "symbolsSha256"}
        if not isinstance(package, dict) or set(package) != expected_keys:
            raise PublicationError(f"Candidate receipt package entry is invalid: {role}")

        expected_primary = f"packages/{package_file_name(package_id, version, 'nupkg')}"
        expected_symbols = f"packages/{package_file_name(package_id, version, 'snupkg')}"
        if (
            package.get("id") != package_id
            or package.get("package") != expected_primary
            or package.get("symbols") != expected_symbols
            or not SHA256.fullmatch(str(package.get("contentDigest", "")))
            or not SHA256.fullmatch(str(package.get("symbolsSha256", "")))
        ):
            raise PublicationError(f"Candidate receipt package identity is invalid: {role}")

        primary = resolve_candidate_path(root, package["package"], f"{role} package")
        symbols = resolve_candidate_path(root, package["symbols"], f"{role} symbols")
        if canonical_package_digest(primary) != package["contentDigest"]:
            raise PublicationError(f"Candidate package digest mismatch: {role}")
        if sha256_file(symbols) != package["symbolsSha256"]:
            raise PublicationError(f"Candidate symbol digest mismatch: {role}")
        resolved[role] = {"package": primary, "symbols": symbols}

    try:
        release_evidence.verify_manifest(root, None)
    except release_evidence.EvidenceError as exception:
        raise PublicationError(str(exception)) from exception

    manifest = read_json(root / release_evidence.MANIFEST_NAME, "release manifest")
    source = manifest.get("source") or {}
    workflow = manifest.get("workflow") or {}
    expected_workflow_ref = f"{repository}/{CANDIDATE_WORKFLOW_PATH}@refs/heads/main"
    if (
        manifest.get("releaseCandidateRunId") != candidate_id
        or manifest.get("releaseVersion") != version
        or manifest.get("expectedReleaseTag") != expected_release_tag
        or source.get("commit") != source_commit
        or source.get("ref") != "refs/heads/main"
        or source.get("tag") is not None
        or normalize_repository(str(source.get("repository", ""))) != repository.lower()
        or workflow.get("provider") != "github-actions"
        or str(workflow.get("runId", "")) != run_id
        or str(workflow.get("runAttempt", "")) != run_attempt
        or workflow.get("workflow") != CANDIDATE_WORKFLOW
        or workflow.get("workflowRef") != expected_workflow_ref
        or str(workflow.get("repository", "")).lower() != repository.lower()
    ):
        raise PublicationError("Candidate receipt disagrees with its release manifest.")

    return resolved


def validate_portable_receipt(
    receipt: dict[str, Any],
    candidate_root: Path,
) -> dict[str, dict[str, Path]]:
    """Revalidate a tag-bound publication receipt and its candidate bytes."""
    if (
        receipt.get("schemaVersion") != PUBLICATION_RECEIPT_SCHEMA_VERSION
        or receipt.get("kind") != PUBLICATION_RECEIPT_KIND
        or receipt.get("releaseTag") != receipt.get("expectedReleaseTag")
        or not SHA256.fullmatch(str(receipt.get("candidateReceiptSha256", "")))
        or not SHA256.fullmatch(str(receipt.get("tagTrustRootSha256", "")))
        or not SHA256.fullmatch(
            str(receipt.get("qualificationManifestSha256", ""))
        )
    ):
        raise PublicationError("Publication receipt identity is invalid.")

    qualification_path = candidate_root / "release-qualification-manifest.json"
    if (
        not qualification_path.is_file()
        or sha256_file(qualification_path)
        != receipt["qualificationManifestSha256"]
    ):
        raise PublicationError(
            "Publication receipt does not bind the qualification manifest."
        )

    candidate = dict(receipt)
    candidate["schemaVersion"] = CANDIDATE_RECEIPT_SCHEMA_VERSION
    candidate["kind"] = CANDIDATE_RECEIPT_KIND
    for field in (
        "releaseTag",
        "candidateReceiptSha256",
        "tagTrustRootSha256",
        "qualificationManifestSha256",
    ):
        candidate.pop(field, None)
    return validate_candidate_receipt(candidate, candidate_root)


def prepare_candidate(args: argparse.Namespace) -> None:
    """Bind one same-run candidate artifact to its untagged main source."""
    repo = args.repo.resolve()
    root = require_candidate_root(args.root)
    repository = args.repository
    if os.environ.get("GITHUB_REF") and os.environ["GITHUB_REF"] != "refs/heads/main":
        raise PublicationError(
            "Candidate preparation must run from refs/heads/main, found "
            f"{os.environ['GITHUB_REF']}."
        )

    try:
        release_evidence.verify_manifest(root, None)
    except release_evidence.EvidenceError as exception:
        raise PublicationError(str(exception)) from exception

    manifest = read_json(root / release_evidence.MANIFEST_NAME, "release manifest")
    source = manifest.get("source") or {}
    workflow = manifest.get("workflow") or {}
    version = str(manifest.get("releaseVersion", ""))
    expected_release_tag = str(manifest.get("expectedReleaseTag", ""))
    source_commit = str(source.get("commit", ""))

    if not SHA1.fullmatch(source_commit):
        raise PublicationError("Candidate manifest source commit is invalid.")
    if source.get("tag") is not None or source.get("ref") != "refs/heads/main":
        raise PublicationError("Candidate manifest must describe untagged main.")
    if expected_release_tag != f"v{version}":
        raise PublicationError("Candidate package version does not match its expected tag.")
    if normalize_repository(str(source.get("repository", ""))) != repository.lower():
        raise PublicationError("Candidate source remote belongs to a different repository.")

    current_commit = run_git(repo, "rev-parse", "HEAD")
    if current_commit != source_commit:
        raise PublicationError("Candidate source is not the checked-out main commit.")
    if os.environ.get("GITHUB_SHA") and os.environ["GITHUB_SHA"] != current_commit:
        raise PublicationError("Hosted publication SHA does not match the trusted checkout.")
    if run_git(repo, "status", "--porcelain", "--untracked-files=all"):
        raise PublicationError("Candidate preparation requires a clean checkout.")
    semantic_tags = sorted(
        tag
        for tag in run_git(repo, "tag", "--points-at", current_commit).splitlines()
        if release_evidence.SEMANTIC_VERSION_TAG.fullmatch(tag)
    )
    if semantic_tags:
        raise PublicationError(
            f"Candidate preparation requires an untagged commit; found {semantic_tags}."
        )

    run_id = str(workflow.get("runId", ""))
    run_attempt = str(workflow.get("runAttempt", ""))
    expected_workflow_ref = f"{repository}/{CANDIDATE_WORKFLOW_PATH}@refs/heads/main"
    if (
        not RUN_ID.fullmatch(run_id)
        or not RUN_ID.fullmatch(run_attempt)
        or workflow.get("provider") != "github-actions"
        or str(workflow.get("runId", "")) != run_id
        or str(workflow.get("runAttempt", "")) != run_attempt
        or workflow.get("workflow") != CANDIDATE_WORKFLOW
        or workflow.get("workflowRef") != expected_workflow_ref
        or str(workflow.get("repository", "")).lower() != repository.lower()
    ):
        raise PublicationError("Candidate manifest workflow identity is invalid.")

    # GitHub retains the run ID across job reruns. The attempt remains a
    # separately verified manifest field so a rerun can repair one failed stage
    # without changing the candidate artifact identity.
    expected_candidate_id = f"github-{run_id}"
    if manifest.get("releaseCandidateRunId") != expected_candidate_id or root.name != expected_candidate_id:
        raise PublicationError("Candidate evidence root does not match its hosted run identity.")

    packages = validate_package_metadata(root, version, repository, source_commit)
    mysql84 = next(
        (engine for engine in manifest.get("engines", []) if engine.get("targetId") == "mysql84"),
        None,
    )
    if mysql84 is None or not mysql84.get("image"):
        raise PublicationError("Candidate manifest does not contain the MySQL 8.4 image identity.")

    receipt = {
        "schemaVersion": CANDIDATE_RECEIPT_SCHEMA_VERSION,
        "kind": CANDIDATE_RECEIPT_KIND,
        "candidateRunId": run_id,
        "candidateRunAttempt": run_attempt,
        "releaseCandidateRunId": expected_candidate_id,
        "expectedReleaseTag": expected_release_tag,
        "releaseVersion": version,
        "repository": repository,
        "sourceCommit": source_commit,
        "sourceRef": "refs/heads/main",
        "mysql84Image": mysql84["image"],
        "packages": packages,
    }
    write_json(args.output.resolve(), receipt)
    validate_candidate_receipt(receipt, root)
    resolved_packages = package_paths(root, version)
    append_github_outputs(
        args.github_output.resolve() if args.github_output else None,
        {
            "release_version": version,
            "source_commit": source_commit,
            "mysql84_image": str(mysql84["image"]),
            "provider_package": str(resolved_packages["provider"]["package"]),
            "provider_symbols": str(resolved_packages["provider"]["symbols"]),
            "spatial_package": str(resolved_packages["spatial"]["package"]),
            "spatial_symbols": str(resolved_packages["spatial"]["symbols"]),
            "cache_package": str(resolved_packages["cache"]["package"]),
            "cache_symbols": str(resolved_packages["cache"]["symbols"]),
        },
    )


def bind_candidate(args: argparse.Namespace) -> None:
    """Bind a qualified candidate to its signed tag and trust-root receipt."""
    repo = args.repo.resolve()
    root = require_candidate_root(args.root)
    candidate_path = args.candidate_receipt.resolve()
    candidate = read_json(candidate_path, "candidate receipt")
    validate_candidate_receipt(candidate, root)

    tag = args.release_tag
    source_commit = str(candidate["sourceCommit"])
    if tag != candidate["expectedReleaseTag"]:
        raise PublicationError("Release tag does not match the qualified candidate version.")
    if os.environ.get("GITHUB_REF") and os.environ["GITHUB_REF"] != "refs/heads/main":
        raise PublicationError("Publication binding must execute from refs/heads/main.")
    if run_git(repo, "rev-parse", "HEAD") != source_commit:
        raise PublicationError("Publication checkout does not match the candidate commit.")
    try:
        run_git(
            repo,
            "merge-base",
            "--is-ancestor",
            source_commit,
            "refs/remotes/origin/main",
        )
    except PublicationError as error:
        raise PublicationError(
            "The qualified candidate is no longer on current remote main history."
        ) from error
    if run_git(repo, "status", "--porcelain", "--untracked-files=all"):
        raise PublicationError("Publication binding requires a clean checkout.")
    if run_git(repo, "cat-file", "-t", f"refs/tags/{tag}") != "tag":
        raise PublicationError("Release tag must be annotated.")
    if run_git(repo, "rev-parse", f"refs/tags/{tag}^{{commit}}") != source_commit:
        raise PublicationError("Release tag does not identify the candidate commit.")
    semantic_tags = sorted(
        value
        for value in run_git(repo, "tag", "--points-at", source_commit).splitlines()
        if release_evidence.SEMANTIC_VERSION_TAG.fullmatch(value)
    )
    if semantic_tags != [tag]:
        raise PublicationError(
            f"Candidate commit requires exactly semantic tag {tag}; found {semantic_tags}."
        )

    trust_path = args.tag_trust_receipt.resolve()
    trust = read_json(trust_path, "release tag trust-root receipt")
    qualification_path = root / "release-qualification-manifest.json"
    qualification = read_json(qualification_path, "release qualification manifest")
    policy = release_qualification.load_policy()
    tree_id = run_git(repo, "rev-parse", f"{source_commit}^{{tree}}")
    if (
        trust.get("schemaVersion") != 2
        or trust.get("kind") != "release-tag-trust-root"
        or trust.get("repository") != candidate["repository"]
        or trust.get("tag") != tag
        or trust.get("commit") != source_commit
        or not SHA256.fullmatch(str(trust.get("policyDigest", "")))
        or (trust.get("qualification") or {}).get("mergedCommit")
        != source_commit
    ):
        raise PublicationError("Release tag trust-root receipt is invalid.")
    if trust.get("policyDigest") != qualification.get("policyDigest"):
        raise PublicationError(
            "Tag trust root and qualification manifest use different policies."
        )
    try:
        release_trust.verify_frozen_qualification_receipt(
            trust["qualification"],
            manifest=qualification,
            policy=policy,
            repository=str(candidate["repository"]),
            commit=source_commit,
            tree_id=tree_id,
            expected_release_tag=tag,
        )
    except (KeyError, release_trust.TrustRootError) as error:
        raise PublicationError(
            f"Release tag trust root is not bound to qualification: {error}"
        ) from error

    receipt = dict(candidate)
    receipt.update(
        {
            "schemaVersion": PUBLICATION_RECEIPT_SCHEMA_VERSION,
            "kind": PUBLICATION_RECEIPT_KIND,
            "releaseTag": tag,
            "candidateReceiptSha256": sha256_file(candidate_path),
            "tagTrustRootSha256": sha256_file(trust_path),
            "qualificationManifestSha256": sha256_file(qualification_path),
        }
    )
    write_json(args.output.resolve(), receipt)
    validate_portable_receipt(receipt, root)
    append_github_outputs(
        args.github_output.resolve() if args.github_output else None,
        {
            "release_version": str(receipt["releaseVersion"]),
            "source_commit": source_commit,
            "mysql84_image": str(receipt["mysql84Image"]),
        },
    )


def fetch_json_document(url: str, timeout_seconds: float) -> Any:
    """Fetch one NuGet protocol document with retryable transport failures."""
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "Doka-NuGet-Publication/1"},
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            if response.status != 200:
                raise RemotePackageUnavailable(
                    f"Unexpected NuGet service status {response.status}: {url}"
                )
            try:
                return json.loads(response.read())
            except (UnicodeDecodeError, json.JSONDecodeError) as exception:
                raise RemotePackageUnavailable(
                    f"NuGet service returned invalid JSON: {url}"
                ) from exception
    except urllib.error.HTTPError as exception:
        if exception.code in (408, 429) or exception.code >= 500:
            raise RemotePackageUnavailable(
                f"Transient NuGet service status {exception.code}: {url}"
            ) from exception
        raise PublicationError(
            f"NuGet service rejected discovery with HTTP {exception.code}."
        ) from exception
    except (TimeoutError, urllib.error.URLError) as exception:
        raise RemotePackageUnavailable(
            f"NuGet service discovery is unavailable: {url}"
        ) from exception


def resolve_package_base_address(
    source: str = NUGET_SOURCE,
    timeout_seconds: float = 30,
    fetcher: Callable[[str, float], Any] = fetch_json_document,
) -> str:
    """Discover the stable package-content resource from a NuGet V3 source."""
    service_index = fetcher(source, timeout_seconds)
    if not isinstance(service_index, dict):
        raise PublicationError("NuGet service index must contain a JSON object.")

    version = service_index.get("version")
    resources = service_index.get("resources")
    if (
        not isinstance(version, str)
        or not SERVICE_INDEX_VERSION.fullmatch(version)
        or not isinstance(resources, list)
    ):
        raise PublicationError("NuGet service index contract is invalid.")

    addresses: set[str] = set()
    for resource in resources:
        if not isinstance(resource, dict):
            raise PublicationError("NuGet service index contains a non-object resource.")
        resource_types = resource.get("@type")
        if isinstance(resource_types, str):
            types = {resource_types}
        elif isinstance(resource_types, list) and all(
            isinstance(value, str) for value in resource_types
        ):
            types = set(resource_types)
        else:
            raise PublicationError("NuGet service index resource type is invalid.")
        if NUGET_PACKAGE_BASE_ADDRESS_TYPE in types:
            address = resource.get("@id")
            if not isinstance(address, str):
                raise PublicationError("NuGet package base address is missing.")
            addresses.add(require_https_base_address(address))

    if len(addresses) != 1:
        raise PublicationError(
            "NuGet service index must expose exactly one stable package base address."
        )
    return addresses.pop()


def remote_package_url(base_address: str, package_id: str, version: str) -> str:
    """Return the discovered NuGet V3 package-content URL for one package."""
    normalized_base_address = require_https_base_address(base_address)
    normalized_id = package_id.lower()
    normalized_version = require_normalized_release_version(version)
    return (
        f"{normalized_base_address}/{normalized_id}/{normalized_version}/"
        f"{normalized_id}.{normalized_version}.nupkg"
    )


def fetch_remote_package(url: str, timeout_seconds: float) -> bytes | None:
    """Fetch one public package, distinguishing absence from transient errors."""
    request = urllib.request.Request(url, headers={"User-Agent": "Doka-NuGet-Publication/1"})
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            if response.status != 200:
                raise PublicationError(f"Unexpected NuGet.org status {response.status}: {url}")
            return response.read()
    except urllib.error.HTTPError as exception:
        if exception.code == 404:
            return None
        if exception.code in (408, 429) or exception.code >= 500:
            raise RemotePackageUnavailable(
                f"Transient NuGet.org status {exception.code}: {url}"
            ) from exception
        raise PublicationError(
            f"NuGet.org rejected package readback with HTTP {exception.code}."
        ) from exception
    except (TimeoutError, urllib.error.URLError) as exception:
        raise RemotePackageUnavailable(f"NuGet.org package readback is unavailable: {url}") from exception


def validated_symbol_entries(
    manifest: dict[str, Any],
    version: str,
) -> list[dict[str, str]]:
    """Validate symbol probes produced from the candidate assembly metadata."""
    if (
        manifest.get("schemaVersion") != SYMBOL_MANIFEST_SCHEMA_VERSION
        or manifest.get("releaseVersion") != version
    ):
        raise PublicationError("Symbol readback manifest identity is invalid.")

    raw_entries = manifest.get("symbols")
    if not isinstance(raw_entries, list):
        raise PublicationError("Symbol readback manifest has no symbol entries.")

    entries: list[dict[str, str]] = []
    for raw_entry in raw_entries:
        if not isinstance(raw_entry, dict):
            raise PublicationError("Symbol readback manifest contains a non-object entry.")

        entry = {name: str(raw_entry.get(name, "")) for name in (
            "packageId",
            "packageVersion",
            "pdbName",
            "symbolKey",
            "symbolUrl",
            "checksumHeader",
            "sha256",
        )}
        expected_url = (
            f"{NUGET_SYMBOL_SERVER}/{entry['pdbName']}/"
            f"{entry['symbolKey']}/{entry['pdbName']}"
        )
        if (
            entry["packageVersion"] != version
            or entry["pdbName"] != f"{entry['packageId']}.pdb"
            or Path(entry["pdbName"]).name != entry["pdbName"]
            or not entry["pdbName"].endswith(".pdb")
            or not SYMBOL_KEY.fullmatch(entry["symbolKey"])
            or not SHA256.fullmatch(entry["sha256"])
            or not re.fullmatch(r"SHA256:[0-9a-f]{64}", entry["checksumHeader"])
            or entry["symbolUrl"] != expected_url
        ):
            raise PublicationError(
                f"Symbol readback entry is invalid for {entry['packageId'] or 'unknown package'}."
            )
        entries.append(entry)

    expected_ids = set(PACKAGE_IDENTITIES.values())
    actual_ids = [entry["packageId"] for entry in entries]
    if len(entries) != len(expected_ids) or set(actual_ids) != expected_ids:
        raise PublicationError(f"Symbol readback package set is invalid: {actual_ids}")
    if len({entry["symbolUrl"] for entry in entries}) != len(entries):
        raise PublicationError("Symbol readback manifest contains duplicate public probes.")

    return entries


def fetch_remote_symbol(
    entry: dict[str, str],
    timeout_seconds: float,
) -> bytes | None:
    """Fetch one indexed Portable PDB with the checksum required by NuGet.org."""
    request = urllib.request.Request(
        entry["symbolUrl"],
        headers={
            "User-Agent": "Doka-NuGet-Publication/1",
            # NuGet.org rejects portable-symbol requests that do not opt into
            # checksum validation. The second header binds the anonymous
            # response to the SHA-256 value sealed into the candidate DLL.
            "SymbolChecksumValidationSupported": "1",
            "SymbolChecksum": entry["checksumHeader"],
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            if response.status != 200:
                raise PublicationError(
                    f"Unexpected NuGet.org symbol status {response.status}: {entry['symbolUrl']}"
                )
            return response.read()
    except urllib.error.HTTPError as exception:
        if exception.code == 404:
            return None
        if exception.code in (408, 429) or exception.code >= 500:
            raise RemotePackageUnavailable(
                f"Transient NuGet.org symbol status {exception.code}: {entry['symbolUrl']}"
            ) from exception
        raise PublicationError(
            f"NuGet.org rejected symbol readback with HTTP {exception.code}."
        ) from exception
    except (TimeoutError, urllib.error.URLError) as exception:
        raise RemotePackageUnavailable(
            f"NuGet.org symbol readback is unavailable: {entry['symbolUrl']}"
        ) from exception


def observe_remote_symbols(
    entries: Sequence[dict[str, str]],
    fetcher: Callable[[dict[str, str], float], bytes | None] = fetch_remote_symbol,
    timeout_seconds: float = 30,
) -> tuple[dict[str, dict[str, Any]], dict[str, bytes]]:
    """Classify symbols and retain the exact matching bytes observed."""
    states: dict[str, dict[str, Any]] = {}
    payloads: dict[str, bytes] = {}
    for entry in entries:
        remote = fetcher(entry, timeout_seconds)
        if remote is None:
            states[entry["packageId"]] = {
                "pdbName": entry["pdbName"],
                "status": "absent",
                "url": entry["symbolUrl"],
                "candidateSha256": entry["sha256"],
            }
            continue

        published_sha256 = hashlib.sha256(remote).hexdigest()
        if not remote.startswith(b"BSJB") or published_sha256 != entry["sha256"]:
            raise PublicationError(
                f"NuGet.org returned conflicting symbols for {entry['packageId']}."
            )
        states[entry["packageId"]] = {
            "pdbName": entry["pdbName"],
            "status": "matching",
            "url": entry["symbolUrl"],
            "candidateSha256": entry["sha256"],
            "publishedSha256": published_sha256,
        }
        payloads[entry["packageId"]] = remote
    return states, payloads


def symbol_states(
    entries: list[dict[str, str]],
    fetcher: Callable[[dict[str, str], float], bytes | None] = fetch_remote_symbol,
    timeout_seconds: float = 30,
) -> dict[str, dict[str, Any]]:
    """Classify public symbols as absent or byte-identical to the candidate."""
    return observe_remote_symbols(entries, fetcher, timeout_seconds)[0]


def observe_remote_packages(
    receipt: dict[str, Any],
    candidate_root: Path,
    package_base_address: str,
    fetcher: Callable[[str, float], bytes | None] = fetch_remote_package,
    timeout_seconds: float = 30,
    roles: Sequence[str] = tuple(PACKAGE_IDENTITIES),
) -> tuple[dict[str, dict[str, Any]], dict[str, bytes]]:
    """Classify packages and retain the exact matching bytes observed."""
    version = str(receipt["releaseVersion"])
    package_map = {
        role: resolve_candidate_path(
            candidate_root,
            receipt["packages"][role]["package"],
            f"{role} package",
        )
        for role in PACKAGE_IDENTITIES
    }
    states: dict[str, dict[str, Any]] = {}
    payloads: dict[str, bytes] = {}
    package_ids = PACKAGE_IDENTITIES
    for role in roles:
        if role not in package_ids:
            raise PublicationError(f"Unknown NuGet package role '{role}'.")

        package_id = package_ids[role]
        candidate_path = package_map[role]
        candidate_digest = canonical_package_digest(candidate_path)
        url = remote_package_url(package_base_address, package_id, version)
        remote = fetcher(url, timeout_seconds)
        if remote is None:
            states[role] = {
                "id": package_id,
                "status": "absent",
                "url": url,
                "candidateContentDigest": candidate_digest,
            }
            continue

        remote_digest = canonical_package_digest(remote)
        if remote_digest != candidate_digest:
            raise PublicationError(
                f"NuGet.org already contains conflicting bytes for {package_id} {version}."
            )
        signature_present = package_has_signature(remote)
        states[role] = {
            "id": package_id,
            "status": "matching" if signature_present else "pending-signature",
            "url": url,
            "candidateContentDigest": candidate_digest,
            "publishedContentDigest": remote_digest,
            "publishedSha256": hashlib.sha256(remote).hexdigest(),
            "repositorySignaturePresent": signature_present,
        }
        payloads[role] = remote

    return states, payloads


def remote_states(
    receipt: dict[str, Any],
    candidate_root: Path,
    package_base_address: str,
    fetcher: Callable[[str, float], bytes | None] = fetch_remote_package,
    timeout_seconds: float = 30,
) -> dict[str, dict[str, Any]]:
    """Classify each package version independently during asynchronous indexing."""
    return observe_remote_packages(
        receipt,
        candidate_root,
        package_base_address,
        fetcher,
        timeout_seconds,
    )[0]


def preflight(args: argparse.Namespace) -> None:
    """Record remote state before requesting the short-lived publish key."""
    receipt = read_json(args.receipt.resolve(), "validated candidate receipt")
    if receipt.get("kind") == CANDIDATE_RECEIPT_KIND:
        package_map = validate_candidate_receipt(receipt, args.candidate_root)
    else:
        package_map = validate_portable_receipt(receipt, args.candidate_root)
    symbol_manifest = read_json(args.symbol_manifest.resolve(), "symbol readback manifest")
    symbol_entries = validated_symbol_entries(
        symbol_manifest,
        str(receipt["releaseVersion"]),
    )
    package_base_address = resolve_package_base_address(
        timeout_seconds=args.timeout_seconds,
    )
    states = remote_states(
        receipt,
        args.candidate_root,
        package_base_address,
        timeout_seconds=args.timeout_seconds,
    )
    symbols = symbol_states(symbol_entries, timeout_seconds=args.timeout_seconds)
    if args.require_absent:
        existing = [
            name
            for name, state in (
                ("provider package", states["provider"]),
                ("spatial package", states["spatial"]),
                ("cache package", states["cache"]),
                ("provider symbols", symbols[PROVIDER_PACKAGE_ID]),
                ("spatial symbols", symbols[SPATIAL_PACKAGE_ID]),
                ("cache symbols", symbols[CACHE_PACKAGE_ID]),
            )
            if state["status"] != "absent"
        ]
        if existing:
            raise PublicationError(
                "Candidate version is not fully available for publication: "
                + ", ".join(existing)
            )

    output = {
        "schemaVersion": SCHEMA_VERSION,
        "checkedUtc": datetime.now(UTC).isoformat(),
        "expectedReleaseTag": receipt["expectedReleaseTag"],
        "releaseVersion": receipt["releaseVersion"],
        "sourceCommit": receipt["sourceCommit"],
        "packageSource": NUGET_SOURCE,
        "packageBaseAddress": package_base_address,
        "packages": states,
        "symbols": symbols,
    }
    if "releaseTag" in receipt:
        output["releaseTag"] = receipt["releaseTag"]
    write_json(args.output.resolve(), output)
    publication_required = any(
        state["status"] != "matching" for state in (*states.values(), *symbols.values())
    )
    append_github_outputs(
        args.github_output.resolve() if args.github_output else None,
        {
            "provider_published": str(states["provider"]["status"] == "matching").lower(),
            "spatial_published": str(states["spatial"]["status"] == "matching").lower(),
            "cache_published": str(states["cache"]["status"] == "matching").lower(),
            "provider_symbols_published": str(
                symbols[PROVIDER_PACKAGE_ID]["status"] == "matching"
            ).lower(),
            "spatial_symbols_published": str(
                symbols[SPATIAL_PACKAGE_ID]["status"] == "matching"
            ).lower(),
            "cache_symbols_published": str(
                symbols[CACHE_PACKAGE_ID]["status"] == "matching"
            ).lower(),
            "publication_required": str(publication_required).lower(),
            "provider_package": str(package_map["provider"]["package"]),
            "provider_symbols": str(package_map["provider"]["symbols"]),
            "spatial_package": str(package_map["spatial"]["package"]),
            "spatial_symbols": str(package_map["spatial"]["symbols"]),
            "cache_package": str(package_map["cache"]["package"]),
            "cache_symbols": str(package_map["cache"]["symbols"]),
        },
    )


def readback(args: argparse.Namespace) -> None:
    """Wait for public packages and symbols, then persist byte-level proof."""
    receipt = read_json(args.receipt.resolve(), "validated candidate receipt")
    validate_portable_receipt(receipt, args.candidate_root)
    symbol_manifest = read_json(args.symbol_manifest.resolve(), "symbol readback manifest")
    symbol_entries = validated_symbol_entries(
        symbol_manifest,
        str(receipt["releaseVersion"]),
    )
    deadline = time.monotonic() + args.timeout_seconds
    last_error = "package or symbols are not indexed"
    package_base_address: str | None = None
    package_payloads: dict[str, bytes] = {}
    symbol_payloads: dict[str, bytes] = {}
    states: dict[str, dict[str, Any]] = {}
    symbols: dict[str, dict[str, Any]] = {}

    while True:
        try:
            if package_base_address is None:
                package_base_address = resolve_package_base_address(
                    timeout_seconds=min(args.request_timeout_seconds, 30),
                )
            pending_roles = tuple(
                role
                for role in PACKAGE_IDENTITIES
                if states.get(role, {}).get("status") != "matching"
            )
            if pending_roles:
                observed_states, observed_payloads = observe_remote_packages(
                    receipt,
                    args.candidate_root,
                    package_base_address,
                    timeout_seconds=min(args.request_timeout_seconds, 30),
                    roles=pending_roles,
                )
                states.update(observed_states)
                package_payloads.update(observed_payloads)

            pending_symbols = tuple(
                entry
                for entry in symbol_entries
                if symbols.get(entry["packageId"], {}).get("status") != "matching"
            )
            if pending_symbols:
                observed_symbols, observed_symbol_payloads = observe_remote_symbols(
                    pending_symbols,
                    timeout_seconds=min(args.request_timeout_seconds, 30),
                )
                symbols.update(observed_symbols)
                symbol_payloads.update(observed_symbol_payloads)

            if (
                all(state["status"] == "matching" for state in states.values())
                and all(state["status"] == "matching" for state in symbols.values())
                and len(states) == len(PACKAGE_IDENTITIES)
                and len(symbols) == len(PACKAGE_IDENTITIES)
            ):
                break
            pending = [
                (
                    f"{state['id']} repository signature"
                    if state["status"] == "pending-signature"
                    else f"{state['id']} package"
                )
                for state in states.values()
                if state["status"] != "matching"
            ]
            pending.extend(
                f"{package_id} symbols"
                for package_id, state in symbols.items()
                if state["status"] != "matching"
            )
            last_error = "not indexed: " + ", ".join(pending)
        except RemotePackageUnavailable as exception:
            last_error = str(exception)

        if time.monotonic() >= deadline:
            raise PublicationError(
                f"NuGet.org readback timed out after {args.timeout_seconds} seconds: {last_error}"
            )
        time.sleep(args.poll_interval_seconds)

    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    version = str(receipt["releaseVersion"])
    # The complete polling response proves availability and supplies the exact
    # bytes retained as evidence. Persisting that same response avoids a second
    # CDN request reintroducing an ordering race after the complete observation
    # has already passed.
    for role, package_id in PACKAGE_IDENTITIES.items():
        remote = package_payloads[role]
        readback_digest = canonical_package_digest(remote)
        if readback_digest != states[role]["candidateContentDigest"]:
            raise PublicationError(
                f"Published package changed during readback: {package_id} {version}."
            )
        if not package_has_signature(remote):
            raise PublicationError(
                f"Published package lost its repository signature: {package_id} {version}."
            )
        destination = output_dir / package_file_name(package_id, version, "nupkg")
        destination.write_bytes(remote)
        states[role]["readbackPath"] = str(destination)
        states[role]["publishedContentDigest"] = readback_digest
        states[role]["publishedSha256"] = hashlib.sha256(remote).hexdigest()

    symbols_dir = output_dir / "symbols"
    symbols_dir.mkdir(parents=True, exist_ok=True)
    for entry in symbol_entries:
        remote = symbol_payloads[entry["packageId"]]
        published_sha256 = hashlib.sha256(remote).hexdigest()
        if not remote.startswith(b"BSJB") or published_sha256 != entry["sha256"]:
            raise PublicationError(
                f"Published symbols changed during readback: {entry['packageId']}."
            )
        destination = symbols_dir / entry["pdbName"]
        destination.write_bytes(remote)
        symbols[entry["packageId"]]["readbackPath"] = str(destination)
        symbols[entry["packageId"]]["publishedSha256"] = published_sha256

    output = {
        "schemaVersion": SCHEMA_VERSION,
        "verifiedUtc": datetime.now(UTC).isoformat(),
        "releaseTag": receipt["releaseTag"],
        "expectedReleaseTag": receipt["expectedReleaseTag"],
        "releaseVersion": version,
        "sourceCommit": receipt["sourceCommit"],
        "packageSource": NUGET_SOURCE,
        "packageBaseAddress": package_base_address,
        "packages": states,
        "symbols": symbols,
    }
    write_json(args.output.resolve(), output)


def verify_restore(args: argparse.Namespace) -> None:
    """Prove isolated provider and standalone cache restores from NuGet.org."""
    package_cache = args.package_cache.resolve()
    # packageFolders is stronger evidence than environment variables alone: it
    # records where NuGet actually resolved packages for this restore graph.
    expected: set[str] = set()
    for path, package_ids in (
        (args.assets, (PROVIDER_PACKAGE_ID, SPATIAL_PACKAGE_ID)),
        (args.cache_assets, (CACHE_PACKAGE_ID,)),
    ):
        assets = read_json(path.resolve(), "consumer restore assets")
        package_folders = [Path(folder).resolve() for folder in assets.get("packageFolders", {})]
        if package_folders != [package_cache]:
            raise PublicationError(
                f"Consumer restore escaped its isolated package cache: {package_folders}"
            )
        restore = (assets.get("project") or {}).get("restore") or {}
        sources = {source.rstrip("/") for source in restore.get("sources", {})}
        if sources != {NUGET_SOURCE.rstrip("/")}:
            raise PublicationError(f"Consumer restore used unexpected package sources: {sorted(sources)}")
        libraries = {name.casefold(): entry for name, entry in assets.get("libraries", {}).items()}
        required = {f"{package_id}/{args.version}".casefold() for package_id in package_ids}
        expected.update(required)
        missing = sorted(required - libraries.keys())
        if missing:
            raise PublicationError(f"Consumer restore did not resolve exact release packages: {missing}")
        if any(libraries[name].get("type") != "package" for name in required) or any(
            entry.get("type") == "project" for entry in libraries.values()
        ):
            raise PublicationError("Consumer restore contains project references instead of package bytes.")
        if package_ids == (CACHE_PACKAGE_ID,) and any(
            name.startswith(("doka.entityframeworkcore.", "microsoft.entityframeworkcore", "pomelo."))
            for name in libraries
        ):
            raise PublicationError("The standalone cache consumer restored an EF Core or Pomelo dependency.")

    receipt = {
        "schemaVersion": SCHEMA_VERSION,
        "verifiedUtc": datetime.now(UTC).isoformat(),
        "releaseTag": args.release_tag,
        "releaseVersion": args.version,
        "sourceCommit": args.source_commit,
        "packageSource": NUGET_SOURCE,
        "packageCache": str(package_cache),
        "packages": sorted(expected),
        "dotnetSdk": args.dotnet_sdk,
        "engineImage": args.engine_image,
        "runtimeSmoke": "pass",
        "cacheRuntimeSmoke": "pass",
        "cacheEfCoreDependencies": 0,
    }
    write_json(args.output.resolve(), receipt)


def parse_arguments() -> argparse.Namespace:
    """Parse the publication-boundary command contract."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate_version = subparsers.add_parser(
        "validate-version",
        help="Reject a release version outside the canonical NuGet subset.",
    )
    validate_version.add_argument("--version", required=True)

    prepare = subparsers.add_parser("prepare", help="Validate an untagged same-run candidate.")
    prepare.add_argument("--repo", type=Path, required=True)
    prepare.add_argument("--root", type=Path, required=True)
    prepare.add_argument("--repository", required=True)
    prepare.add_argument("--output", type=Path, required=True)
    prepare.add_argument("--github-output", type=Path)

    bind = subparsers.add_parser("bind", help="Bind a candidate to its verified release tag.")
    bind.add_argument("--repo", type=Path, required=True)
    bind.add_argument("--root", type=Path, required=True)
    bind.add_argument("--candidate-receipt", type=Path, required=True)
    bind.add_argument("--tag-trust-receipt", type=Path, required=True)
    bind.add_argument("--release-tag", required=True)
    bind.add_argument("--output", type=Path, required=True)
    bind.add_argument("--github-output", type=Path)

    preflight_parser = subparsers.add_parser(
        "preflight", help="Classify existing NuGet.org package versions."
    )
    preflight_parser.add_argument("--receipt", type=Path, required=True)
    preflight_parser.add_argument("--candidate-root", type=Path, required=True)
    preflight_parser.add_argument("--symbol-manifest", type=Path, required=True)
    preflight_parser.add_argument("--output", type=Path, required=True)
    preflight_parser.add_argument("--github-output", type=Path)
    preflight_parser.add_argument("--timeout-seconds", type=float, default=30)
    preflight_parser.add_argument("--require-absent", action="store_true")

    readback_parser = subparsers.add_parser(
        "readback", help="Wait for and verify published package payloads."
    )
    readback_parser.add_argument("--receipt", type=Path, required=True)
    readback_parser.add_argument("--candidate-root", type=Path, required=True)
    readback_parser.add_argument("--symbol-manifest", type=Path, required=True)
    readback_parser.add_argument("--output-dir", type=Path, required=True)
    readback_parser.add_argument("--output", type=Path, required=True)
    readback_parser.add_argument(
        "--timeout-seconds",
        type=float,
        default=PUBLICATION_READBACK_TIMEOUT_SECONDS,
    )
    readback_parser.add_argument("--request-timeout-seconds", type=float, default=30)
    readback_parser.add_argument(
        "--poll-interval-seconds",
        type=float,
        default=PUBLICATION_READBACK_POLL_INTERVAL_SECONDS,
    )

    restore = subparsers.add_parser(
        "verify-restore", help="Verify the isolated public-package consumer restore."
    )
    restore.add_argument("--assets", type=Path, required=True)
    restore.add_argument("--cache-assets", type=Path, required=True)
    restore.add_argument("--package-cache", type=Path, required=True)
    restore.add_argument("--version", required=True)
    restore.add_argument("--release-tag", required=True)
    restore.add_argument("--source-commit", required=True)
    restore.add_argument("--dotnet-sdk", required=True)
    restore.add_argument("--engine-image", required=True)
    restore.add_argument("--output", type=Path, required=True)

    return parser.parse_args()


def main() -> int:
    """Run one publication operation with concise operator diagnostics."""
    args = parse_arguments()
    try:
        if args.command == "validate-version":
            validate_release_version(args)
        elif args.command == "prepare":
            prepare_candidate(args)
        elif args.command == "bind":
            bind_candidate(args)
        elif args.command == "preflight":
            preflight(args)
        elif args.command == "readback":
            readback(args)
        else:
            verify_restore(args)
    except (PublicationError, KeyError, TypeError, ValueError) as exception:
        print(f"NuGet publication failed: {exception}", file=sys.stderr)
        return 1

    print(f"NuGet publication {args.command} passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
