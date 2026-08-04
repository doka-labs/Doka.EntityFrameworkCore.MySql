#!/usr/bin/env python3
"""Validate, publish-preflight, and read back NuGet release artifacts.

The release-candidate workflow proves that a package is eligible for release.
This module forms the separate publication boundary: it binds a manually
selected successful candidate run to the current trusted main commit and its
exact semantic version tag before any credential is requested. It also makes
publication retries safe by distinguishing an absent package from matching or
conflicting content already present on NuGet.org.
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
import urllib.request
import zipfile
from datetime import UTC, datetime
from io import BytesIO
from pathlib import Path, PurePosixPath
from typing import Any, Callable
from xml.etree import ElementTree

try:
    from eng import release_evidence
except ModuleNotFoundError:
    # Tests import this module through ``eng`` while GitHub Actions executes the
    # file directly. Support both entry points without altering import paths.
    import release_evidence


SCHEMA_VERSION = 1
PUBLICATION_RECEIPT_SCHEMA_VERSION = 2
PROVIDER_PACKAGE_ID = "Doka.EntityFrameworkCore.MySql"
SPATIAL_PACKAGE_ID = "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
CANDIDATE_WORKFLOW = "release-candidate"
CANDIDATE_WORKFLOW_PATH = ".github/workflows/release-candidate.yml"
NUGET_SOURCE = "https://api.nuget.org/v3/index.json"
NUGET_FLAT_CONTAINER = "https://api.nuget.org/v3-flatcontainer"
NUGET_SYMBOL_SERVER = "https://symbols.nuget.org/download/symbols"
NUGET_SIGNATURE_ENTRY = ".signature.p7s"
SHA1 = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
RUN_ID = re.compile(r"[1-9][0-9]*")
SYMBOL_KEY = re.compile(r"[0-9a-f]{32}FFFFFFFF")


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


def package_file_name(package_id: str, version: str, extension: str) -> str:
    """Return the exact file name sealed by the candidate evidence contract."""
    return f"{package_id}.{version}.{extension}"


def package_paths(root: Path, version: str) -> dict[str, dict[str, Path]]:
    """Resolve the two primary and symbol packages without using globs."""
    packages = root / "packages"
    return {
        "provider": {
            "package": packages / package_file_name(PROVIDER_PACKAGE_ID, version, "nupkg"),
            "symbols": packages / package_file_name(PROVIDER_PACKAGE_ID, version, "snupkg"),
        },
        "spatial": {
            "package": packages / package_file_name(SPATIAL_PACKAGE_ID, version, "nupkg"),
            "symbols": packages / package_file_name(SPATIAL_PACKAGE_ID, version, "snupkg"),
        },
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
    """Bind package-internal metadata to the manifest and tagged source."""
    candidate_root = require_candidate_root(root)
    resolved = package_paths(candidate_root, version)
    result: dict[str, dict[str, str]] = {}
    for role, package_id in (("provider", PROVIDER_PACKAGE_ID), ("spatial", SPATIAL_PACKAGE_ID)):
        primary = resolved[role]["package"]
        symbols = resolved[role]["symbols"]
        if not primary.is_file() or primary.is_symlink() or not symbols.is_file() or symbols.is_symlink():
            raise PublicationError(f"Candidate package pair is missing or non-regular: {package_id}")

        metadata = package_metadata(primary)
        symbol_metadata = package_metadata(symbols)
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

    return result


def validate_portable_receipt(
    receipt: dict[str, Any],
    candidate_root: Path,
) -> dict[str, dict[str, Path]]:
    """Revalidate a portable candidate receipt at a new trust boundary."""
    root = require_candidate_root(candidate_root)
    run_id = str(receipt.get("candidateRunId", ""))
    run_attempt = str(receipt.get("candidateRunAttempt", ""))
    candidate_id = f"github-{run_id}"
    version = str(receipt.get("releaseVersion", ""))
    release_tag = str(receipt.get("releaseTag", ""))
    repository = str(receipt.get("repository", ""))
    source_commit = str(receipt.get("sourceCommit", ""))
    if (
        receipt.get("schemaVersion") != PUBLICATION_RECEIPT_SCHEMA_VERSION
        or not RUN_ID.fullmatch(run_id)
        or not RUN_ID.fullmatch(run_attempt)
        or receipt.get("releaseCandidateRunId") != candidate_id
        or root.name != candidate_id
        or receipt.get("trustedRef") != "refs/heads/main"
        or release_tag != f"v{version}"
        or not release_evidence.SEMANTIC_VERSION_TAG.fullmatch(release_tag)
        or normalize_repository(repository) != repository.lower()
        or not SHA1.fullmatch(source_commit)
        or not isinstance(receipt.get("mysql84Image"), str)
        or not receipt["mysql84Image"]
    ):
        raise PublicationError("Publication receipt candidate identity is invalid.")

    packages = receipt.get("packages")
    expected_roles = {"provider": PROVIDER_PACKAGE_ID, "spatial": SPATIAL_PACKAGE_ID}
    if not isinstance(packages, dict) or set(packages) != set(expected_roles):
        raise PublicationError("Publication receipt package inventory is invalid.")

    resolved: dict[str, dict[str, Path]] = {}
    for role, package_id in expected_roles.items():
        package = packages.get(role)
        expected_keys = {"id", "package", "symbols", "contentDigest", "symbolsSha256"}
        if not isinstance(package, dict) or set(package) != expected_keys:
            raise PublicationError(f"Publication receipt package entry is invalid: {role}")

        expected_primary = f"packages/{package_file_name(package_id, version, 'nupkg')}"
        expected_symbols = f"packages/{package_file_name(package_id, version, 'snupkg')}"
        if (
            package.get("id") != package_id
            or package.get("package") != expected_primary
            or package.get("symbols") != expected_symbols
            or not SHA256.fullmatch(str(package.get("contentDigest", "")))
            or not SHA256.fullmatch(str(package.get("symbolsSha256", "")))
        ):
            raise PublicationError(f"Publication receipt package identity is invalid: {role}")

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
    expected_workflow_ref = (
        f"{repository}/{CANDIDATE_WORKFLOW_PATH}@refs/tags/{release_tag}"
    )
    if (
        manifest.get("releaseCandidateRunId") != candidate_id
        or manifest.get("releaseVersion") != version
        or source.get("commit") != source_commit
        or source.get("ref") != f"refs/tags/{release_tag}"
        or source.get("tag") != release_tag
        or normalize_repository(str(source.get("repository", ""))) != repository.lower()
        or workflow.get("provider") != "github-actions"
        or str(workflow.get("runId", "")) != run_id
        or str(workflow.get("runAttempt", "")) != run_attempt
        or workflow.get("workflow") != CANDIDATE_WORKFLOW
        or workflow.get("workflowRef") != expected_workflow_ref
        or str(workflow.get("repository", "")).lower() != repository.lower()
    ):
        raise PublicationError("Publication receipt disagrees with its release manifest.")

    return resolved


def validate_run_metadata(
    run: dict[str, Any],
    repository: str,
    release_tag: str,
    source_commit: str,
) -> tuple[str, str]:
    """Require one completed release-candidate run for the selected tag."""
    run_id = str(run.get("id", ""))
    run_attempt = str(run.get("run_attempt", ""))
    run_repository = run.get("repository") or {}
    if not RUN_ID.fullmatch(run_id) or not RUN_ID.fullmatch(run_attempt):
        raise PublicationError("Candidate run ID or attempt is invalid.")
    if run.get("event") != "workflow_dispatch":
        raise PublicationError("Candidate run was not manually dispatched.")
    if run.get("status") != "completed" or run.get("conclusion") != "success":
        raise PublicationError("Candidate run is not completed successfully.")
    if run.get("path") != f"{CANDIDATE_WORKFLOW_PATH}@{release_tag}":
        raise PublicationError("Candidate run did not execute release-candidate.yml.")
    if run.get("head_sha") != source_commit or run.get("head_branch") != release_tag:
        raise PublicationError("Candidate run source does not match the selected release tag.")
    if str(run_repository.get("full_name", "")).lower() != repository.lower():
        raise PublicationError("Candidate run belongs to a different repository.")
    return run_id, run_attempt


def validate_candidate(args: argparse.Namespace) -> None:
    """Validate one downloaded candidate and emit a publication receipt."""
    repo = args.repo.resolve()
    root = require_candidate_root(args.root)
    repository = args.repository
    release_tag = args.release_tag
    trusted_ref = args.trusted_ref
    trusted_commit = args.trusted_commit

    if not release_evidence.SEMANTIC_VERSION_TAG.fullmatch(release_tag):
        raise PublicationError(f"Release tag is not semantic: {release_tag}")
    if not SHA1.fullmatch(trusted_commit):
        raise PublicationError("The current trusted main commit is invalid.")
    if os.environ.get("GITHUB_REF") and os.environ["GITHUB_REF"] != trusted_ref:
        raise PublicationError(
            f"Publication workflow must run from {trusted_ref}, found {os.environ['GITHUB_REF']}."
        )

    try:
        # The candidate manifest was authored under the tag workflow, whereas
        # publication runs from main. Repository and hosted-workflow binding is
        # therefore checked explicitly below instead of reusing the caller ref.
        release_evidence.verify_manifest(root, None)
    except release_evidence.EvidenceError as exception:
        raise PublicationError(str(exception)) from exception

    manifest = read_json(root / release_evidence.MANIFEST_NAME, "release manifest")
    run = read_json(args.run_metadata.resolve(), "candidate run metadata")
    source = manifest.get("source") or {}
    workflow = manifest.get("workflow") or {}
    version = str(manifest.get("releaseVersion", ""))
    source_commit = str(source.get("commit", ""))

    if not SHA1.fullmatch(source_commit):
        raise PublicationError("Candidate manifest source commit is invalid.")
    if source.get("tag") != release_tag or source.get("ref") != f"refs/tags/{release_tag}":
        raise PublicationError("Candidate manifest tag and source ref disagree.")
    if release_tag != f"v{version}":
        raise PublicationError("Candidate package version does not match its release tag.")
    if normalize_repository(str(source.get("repository", ""))) != repository.lower():
        raise PublicationError("Candidate source remote belongs to a different repository.")

    current_commit = run_git(repo, "rev-parse", "HEAD")
    if current_commit != source_commit or current_commit != trusted_commit:
        raise PublicationError("Candidate source is not the current trusted main commit.")
    if os.environ.get("GITHUB_SHA") and os.environ["GITHUB_SHA"] != current_commit:
        raise PublicationError("Hosted publication SHA does not match the trusted checkout.")
    if run_git(repo, "status", "--porcelain", "--untracked-files=all"):
        raise PublicationError("Publication validation requires a clean trusted checkout.")
    if run_git(repo, "rev-parse", f"refs/tags/{release_tag}^{{commit}}") != current_commit:
        raise PublicationError("Release tag does not identify the trusted main commit.")
    semantic_tags = sorted(
        tag
        for tag in run_git(repo, "tag", "--points-at", current_commit).splitlines()
        if release_evidence.SEMANTIC_VERSION_TAG.fullmatch(tag)
    )
    # More than one release tag at the same commit makes a manually selected
    # run ambiguous even when all package metadata happens to match.
    if semantic_tags != [release_tag]:
        raise PublicationError(
            f"Trusted source requires exactly semantic tag {release_tag}; found {semantic_tags}."
        )

    run_id, run_attempt = validate_run_metadata(
        run,
        repository,
        release_tag,
        source_commit,
    )
    expected_workflow_ref = (
        f"{repository}/{CANDIDATE_WORKFLOW_PATH}@refs/tags/{release_tag}"
    )
    if (
        workflow.get("provider") != "github-actions"
        or str(workflow.get("runId", "")) != run_id
        or str(workflow.get("runAttempt", "")) != run_attempt
        or workflow.get("workflow") != CANDIDATE_WORKFLOW
        or workflow.get("workflowRef") != expected_workflow_ref
        or str(workflow.get("repository", "")).lower() != repository.lower()
    ):
        raise PublicationError("Candidate manifest workflow identity does not match the hosted run.")

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
        "schemaVersion": PUBLICATION_RECEIPT_SCHEMA_VERSION,
        "candidateRunId": run_id,
        "candidateRunAttempt": run_attempt,
        "releaseCandidateRunId": expected_candidate_id,
        "releaseTag": release_tag,
        "releaseVersion": version,
        "repository": repository,
        "sourceCommit": source_commit,
        "trustedRef": trusted_ref,
        "mysql84Image": mysql84["image"],
        "packages": packages,
    }
    write_json(args.output.resolve(), receipt)
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
        },
    )


def remote_package_url(package_id: str, version: str) -> str:
    """Return the immutable NuGet V3 flat-container URL for one package."""
    normalized_id = package_id.lower()
    normalized_version = version.lower()
    return (
        f"{NUGET_FLAT_CONTAINER}/{normalized_id}/{normalized_version}/"
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
    if manifest.get("schemaVersion") != SCHEMA_VERSION or manifest.get("releaseVersion") != version:
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

    expected_ids = {PROVIDER_PACKAGE_ID, SPATIAL_PACKAGE_ID}
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


def symbol_states(
    entries: list[dict[str, str]],
    fetcher: Callable[[dict[str, str], float], bytes | None] = fetch_remote_symbol,
    timeout_seconds: float = 30,
) -> dict[str, dict[str, Any]]:
    """Classify public symbols as absent or byte-identical to the candidate."""
    states: dict[str, dict[str, Any]] = {}
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
    return states


def remote_states(
    receipt: dict[str, Any],
    candidate_root: Path,
    fetcher: Callable[[str, float], bytes | None] = fetch_remote_package,
    timeout_seconds: float = 30,
) -> dict[str, dict[str, Any]]:
    """Classify both package versions as absent or matching the candidate."""
    version = str(receipt["releaseVersion"])
    package_map = {
        role: resolve_candidate_path(
            candidate_root,
            receipt["packages"][role]["package"],
            f"{role} package",
        )
        for role in ("provider", "spatial")
    }
    states: dict[str, dict[str, Any]] = {}
    for role, package_id in (("provider", PROVIDER_PACKAGE_ID), ("spatial", SPATIAL_PACKAGE_ID)):
        candidate_path = package_map[role]
        candidate_digest = canonical_package_digest(candidate_path)
        url = remote_package_url(package_id, version)
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
        states[role] = {
            "id": package_id,
            "status": "matching",
            "url": url,
            "candidateContentDigest": candidate_digest,
            "publishedContentDigest": remote_digest,
            "publishedSha256": hashlib.sha256(remote).hexdigest(),
        }

    # The extension cannot be usable without its exact provider dependency.
    # Treat the reversed partial state as corruption, not a retry opportunity.
    if states["spatial"]["status"] == "matching" and states["provider"]["status"] == "absent":
        raise PublicationError("Spatial package exists without its required provider package.")
    return states


def preflight(args: argparse.Namespace) -> None:
    """Record remote state before requesting the short-lived publish key."""
    receipt = read_json(args.receipt.resolve(), "validated candidate receipt")
    package_map = validate_portable_receipt(receipt, args.candidate_root)
    symbol_manifest = read_json(args.symbol_manifest.resolve(), "symbol readback manifest")
    symbol_entries = validated_symbol_entries(
        symbol_manifest,
        str(receipt["releaseVersion"]),
    )
    states = remote_states(
        receipt,
        args.candidate_root,
        timeout_seconds=args.timeout_seconds,
    )
    symbols = symbol_states(symbol_entries, timeout_seconds=args.timeout_seconds)
    for role, package_id in (("provider", PROVIDER_PACKAGE_ID), ("spatial", SPATIAL_PACKAGE_ID)):
        if symbols[package_id]["status"] == "matching" and states[role]["status"] == "absent":
            raise PublicationError(
                f"Public symbols exist without their required primary package: {package_id}."
            )

    output = {
        "schemaVersion": SCHEMA_VERSION,
        "checkedUtc": datetime.now(UTC).isoformat(),
        "releaseTag": receipt["releaseTag"],
        "releaseVersion": receipt["releaseVersion"],
        "sourceCommit": receipt["sourceCommit"],
        "packages": states,
        "symbols": symbols,
    }
    write_json(args.output.resolve(), output)
    publication_required = any(
        state["status"] == "absent" for state in (*states.values(), *symbols.values())
    )
    append_github_outputs(
        args.github_output.resolve() if args.github_output else None,
        {
            "provider_published": str(states["provider"]["status"] == "matching").lower(),
            "spatial_published": str(states["spatial"]["status"] == "matching").lower(),
            "provider_symbols_published": str(
                symbols[PROVIDER_PACKAGE_ID]["status"] == "matching"
            ).lower(),
            "spatial_symbols_published": str(
                symbols[SPATIAL_PACKAGE_ID]["status"] == "matching"
            ).lower(),
            "publication_required": str(publication_required).lower(),
            "provider_package": str(package_map["provider"]["package"]),
            "provider_symbols": str(package_map["provider"]["symbols"]),
            "spatial_package": str(package_map["spatial"]["package"]),
            "spatial_symbols": str(package_map["spatial"]["symbols"]),
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

    while True:
        try:
            states = remote_states(
                receipt,
                args.candidate_root,
                timeout_seconds=min(args.request_timeout_seconds, 30),
            )
            symbols = symbol_states(
                symbol_entries,
                timeout_seconds=min(args.request_timeout_seconds, 30),
            )
            if (
                all(state["status"] == "matching" for state in states.values())
                and all(state["status"] == "matching" for state in symbols.values())
            ):
                break
            last_error = "one or more packages or symbol files are not indexed"
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
    # Re-fetch immediately before persistence. The polling response proves
    # availability; this second validation proves the bytes retained as
    # evidence did not change between observation and the evidence write.
    for role, package_id in (("provider", PROVIDER_PACKAGE_ID), ("spatial", SPATIAL_PACKAGE_ID)):
        remote = fetch_remote_package(remote_package_url(package_id, version), args.request_timeout_seconds)
        if remote is None:
            raise PublicationError(f"Published package disappeared during readback: {package_id}")
        readback_digest = canonical_package_digest(remote)
        if readback_digest != states[role]["candidateContentDigest"]:
            raise PublicationError(
                f"Published package changed during readback: {package_id} {version}."
            )
        destination = output_dir / package_file_name(package_id, version, "nupkg")
        destination.write_bytes(remote)
        states[role]["readbackPath"] = str(destination)
        states[role]["publishedContentDigest"] = readback_digest
        states[role]["publishedSha256"] = hashlib.sha256(remote).hexdigest()

    symbols_dir = output_dir / "symbols"
    symbols_dir.mkdir(parents=True, exist_ok=True)
    for entry in symbol_entries:
        remote = fetch_remote_symbol(entry, args.request_timeout_seconds)
        if remote is None:
            raise PublicationError(
                f"Published symbols disappeared during readback: {entry['packageId']}"
            )
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
        "releaseVersion": version,
        "sourceCommit": receipt["sourceCommit"],
        "packages": states,
        "symbols": symbols,
    }
    write_json(args.output.resolve(), output)


def verify_restore(args: argparse.Namespace) -> None:
    """Prove the consumer resolved both exact packages only from NuGet.org."""
    assets = read_json(args.assets.resolve(), "consumer restore assets")
    package_cache = args.package_cache.resolve()
    # packageFolders is stronger evidence than environment variables alone: it
    # records where NuGet actually resolved packages for this restore graph.
    package_folders = [Path(path).resolve() for path in assets.get("packageFolders", {})]
    if package_folders != [package_cache]:
        raise PublicationError(
            f"Consumer restore escaped its isolated package cache: {package_folders}"
        )

    restore = (assets.get("project") or {}).get("restore") or {}
    sources = {source.rstrip("/") for source in restore.get("sources", {})}
    if sources != {NUGET_SOURCE.rstrip("/")}:
        raise PublicationError(f"Consumer restore used unexpected package sources: {sorted(sources)}")

    libraries = {name.casefold() for name in assets.get("libraries", {})}
    expected = {
        f"{PROVIDER_PACKAGE_ID}/{args.version}".casefold(),
        f"{SPATIAL_PACKAGE_ID}/{args.version}".casefold(),
    }
    missing = sorted(expected - libraries)
    if missing:
        raise PublicationError(f"Consumer restore did not resolve exact release packages: {missing}")

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
    }
    write_json(args.output.resolve(), receipt)


def parse_arguments() -> argparse.Namespace:
    """Parse the publication-boundary command contract."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate", help="Validate a downloaded candidate run.")
    validate.add_argument("--repo", type=Path, required=True)
    validate.add_argument("--root", type=Path, required=True)
    validate.add_argument("--run-metadata", type=Path, required=True)
    validate.add_argument("--release-tag", required=True)
    validate.add_argument("--repository", required=True)
    validate.add_argument("--trusted-ref", default="refs/heads/main")
    validate.add_argument("--trusted-commit", required=True)
    validate.add_argument("--output", type=Path, required=True)
    validate.add_argument("--github-output", type=Path)

    preflight_parser = subparsers.add_parser(
        "preflight", help="Classify existing NuGet.org package versions."
    )
    preflight_parser.add_argument("--receipt", type=Path, required=True)
    preflight_parser.add_argument("--candidate-root", type=Path, required=True)
    preflight_parser.add_argument("--symbol-manifest", type=Path, required=True)
    preflight_parser.add_argument("--output", type=Path, required=True)
    preflight_parser.add_argument("--github-output", type=Path)
    preflight_parser.add_argument("--timeout-seconds", type=float, default=30)

    readback_parser = subparsers.add_parser(
        "readback", help="Wait for and verify published package payloads."
    )
    readback_parser.add_argument("--receipt", type=Path, required=True)
    readback_parser.add_argument("--candidate-root", type=Path, required=True)
    readback_parser.add_argument("--symbol-manifest", type=Path, required=True)
    readback_parser.add_argument("--output-dir", type=Path, required=True)
    readback_parser.add_argument("--output", type=Path, required=True)
    readback_parser.add_argument("--timeout-seconds", type=float, default=3600)
    readback_parser.add_argument("--request-timeout-seconds", type=float, default=30)
    readback_parser.add_argument("--poll-interval-seconds", type=float, default=15)

    restore = subparsers.add_parser(
        "verify-restore", help="Verify the isolated public-package consumer restore."
    )
    restore.add_argument("--assets", type=Path, required=True)
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
        if args.command == "validate":
            validate_candidate(args)
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
