#!/usr/bin/env python3
"""Qualify an exact published Doka RC without workstation or project fallback."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

from . import nuget as publication


RC_VERSION = re.compile(r"(?P<major>[1-9][0-9]*)[.][0-9]+[.][0-9]+-rc[.][1-9][0-9]*(?:[.][0-9]+)*")
SOURCE_COMMIT = re.compile(r"[0-9a-f]{40}")
PASS_MARKER = "DOKA_PUBLIC_RC_CONSUMER_PASS"


class ConsumerError(RuntimeError):
    """Raised when public RC identity or isolated consumer execution differs."""


def read_json(path: Path) -> dict[str, Any]:
    """Read one object without treating malformed evidence as an empty result."""
    try:
        result = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise ConsumerError(f"Consumer evidence is unreadable: {path}") from exception

    if not isinstance(result, dict):
        raise ConsumerError(f"Consumer evidence is not an object: {path}")

    return result


def validate_request(version: str, sdk: str, ef: str, target_framework: str) -> None:
    """Require explicit canonical, same-major Doka and upstream RC identities."""
    majors = []
    for label, value in (("Doka", version), ("SDK", sdk), ("EF Core", ef)):
        publication.require_normalized_release_version(value)
        match = RC_VERSION.fullmatch(value)
        if match is None:
            raise ConsumerError(f"{label} must be an exact canonical RC version.")

        majors.append(int(match["major"]))

    if len(set(majors)) != 1 or target_framework != f"net{majors[0]}.0":
        raise ConsumerError("Doka, SDK, EF Core and target framework must use the same major.")


def read_public_receipt(path: Path, expected_sha256: str, version: str) -> dict[str, Any]:
    """Bind the existing NuGet readback schema to its independently verified hash."""
    if not publication.SHA256.fullmatch(expected_sha256):
        raise ConsumerError("The approved readback SHA-256 must be exact lowercase hexadecimal.")

    if hashlib.sha256(path.read_bytes()).hexdigest() != expected_sha256:
        raise ConsumerError("Published readback receipt checksum differs.")

    receipt = read_json(path)
    if (
        receipt.get("schemaVersion") != 3
        or receipt.get("releaseVersion") != version
        or receipt.get("releaseTag") != f"v{version}"
        or receipt.get("expectedReleaseTag") != f"v{version}"
        or receipt.get("packageSource") != publication.NUGET_SOURCE
        or not SOURCE_COMMIT.fullmatch(str(receipt.get("sourceCommit", "")))
    ):
        raise ConsumerError("Published readback receipt has the wrong release or public source.")

    packages = receipt.get("packages")
    if not isinstance(packages, dict) or set(packages) != set(publication.PACKAGE_IDENTITIES):
        raise ConsumerError("Published readback must contain the coherent three-package set.")

    for role, package_id in publication.PACKAGE_IDENTITIES.items():
        entry = packages[role]
        if (
            not isinstance(entry, dict)
            or entry.get("id") != package_id
            or entry.get("status") != "matching"
            or not publication.SHA256.fullmatch(str(entry.get("publishedContentDigest", "")))
            or entry.get("publishedContentDigest") != entry.get("candidateContentDigest")
            or not publication.SHA256.fullmatch(str(entry.get("publishedSha256", "")))
        ):
            raise ConsumerError(f"Published package identity is incomplete: {package_id}")

    return receipt


def verify_restore(root: Path, version: str, ef: str, receipt: dict[str, Any]) -> None:
    """Check resolved versions, source, isolated cache and actual restored bytes."""
    assets = read_json(root / "obj/project.assets.json")
    cache = root.resolve() / ".packages"
    folders = [Path(path).resolve() for path in assets.get("packageFolders", {})]
    restore = (assets.get("project") or {}).get("restore") or {}
    sources = set(restore.get("sources", {}))
    libraries = assets.get("libraries") or {}
    if cache.is_symlink() or folders != [cache] or sources != {publication.NUGET_SOURCE}:
        raise ConsumerError("Consumer restore escaped its isolated NuGet.org-only cache/source.")

    if any(entry.get("type") != "package" for entry in libraries.values()):
        raise ConsumerError("Consumer restore contains a project instead of a package.")

    ef_entries = {
        name: entry for name, entry in libraries.items()
        if name.casefold().startswith("microsoft.entityframeworkcore")
    }

    if not ef_entries or any(name.rsplit("/", 1)[-1] != ef for name in ef_entries):
        raise ConsumerError("Consumer restore resolved a different EF Core graph.")

    for role, package_id in publication.PACKAGE_IDENTITIES.items():
        identities = [name for name in libraries if name.partition("/")[0].casefold() == package_id.casefold()]
        if identities != [f"{package_id}/{version}"]:
            raise ConsumerError(f"Consumer restore did not resolve the exact Doka RC: {package_id}")

        library = libraries[identities[0]]
        relative = f"{package_id.lower()}/{version}"
        if library.get("path") != relative:
            raise ConsumerError(f"Restored package path differs: {package_id}")

        package_root = cache / relative
        package = package_root / f"{package_id.lower()}.{version}.nupkg"
        if (
            not package.is_file()
            or package.is_symlink()
            or package.resolve().parent != package_root
        ):
            raise ConsumerError(f"Restored package is absent or non-regular: {package_id}")

        content = package.read_bytes()
        actual_sha512 = base64.b64encode(hashlib.sha512(content).digest()).decode("ascii")
        metadata = read_json(package_root / ".nupkg.metadata")
        expected = receipt["packages"][role]
        if (
            library.get("sha512") != actual_sha512
            or metadata.get("contentHash") != actual_sha512
            or metadata.get("source") != publication.NUGET_SOURCE
            or (package_root / f"{package.name}.sha512").read_text(encoding="ascii").strip() != actual_sha512
            or hashlib.sha256(content).hexdigest() != expected["publishedSha256"]
            or publication.canonical_package_digest(content) != expected["publishedContentDigest"]
            or not publication.package_has_signature(content)
        ):
            raise ConsumerError(f"Restored public package content differs: {package_id}")

        internal = publication.package_metadata(package)
        if (
            internal["id"] != package_id
            or internal["version"] != version
            or internal["repositoryCommit"] != receipt["sourceCommit"]
            or publication.normalize_repository(internal["repositoryUrl"])
            != "doka-labs/doka.entityframeworkcore.mysql"
        ):
            raise ConsumerError(f"Restored public package metadata differs: {package_id}")


def prepare_fixture(root: Path, version: str, sdk: str, ef: str, target_framework: str) -> Path:
    """Materialize source-only fixtures outside repository build policy and caches."""
    validate_request(version, sdk, ef, target_framework)
    repository = Path(__file__).resolve().parents[2]
    root = root.resolve()
    if root == repository or repository in root.parents:
        raise ConsumerError("The public consumer must be outside the product repository.")

    if root.exists() and any(root.iterdir()):
        raise ConsumerError("The public consumer output directory must be empty.")

    root.mkdir(parents=True, exist_ok=True)
    template_root = repository / "examples/PublicRcConsumer"
    for name in ("Imports.cs", "Program.cs"):
        shutil.copyfile(template_root / name, root / name)

    template = (template_root / "PublicRcConsumer.csproj.template").read_text(encoding="ascii")
    for token, value in (("@TFM@", target_framework), ("@DOKA@", version), ("@EF@", ef)):
        template = template.replace(token, value)

    project = root / "PublicRcConsumer.csproj"
    project.write_text(template, encoding="ascii")
    (root / "global.json").write_text(
        json.dumps({"sdk": {"version": sdk, "rollForward": "disable", "allowPrerelease": True}}, indent=2) + "\n",
        encoding="ascii",
    )
    # WHY: Empty nearest files prevent a caller's parent MSBuild/central package
    # policy from replacing the exact graph being qualified by this fixture.
    for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
        (root / name).write_text("<Project />\n", encoding="ascii")

    (root / "NuGet.Config").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<configuration>\n'
        '  <packageSources><clear /><add key="nuget.org" value="' + publication.NUGET_SOURCE + '" /></packageSources>\n'
        '  <fallbackPackageFolders><clear /></fallbackPackageFolders>\n'
        '  <config><add key="signatureValidationMode" value="require" /></config>\n'
        '</configuration>\n', encoding="ascii",
    )

    return project


def run_dotnet(root: Path, *arguments: str) -> str:
    """Run the installed SDK without global cache or environment source fallback."""
    environment = {
        key: value for key, value in os.environ.items()
        if not key.upper().startswith(("NUGET_", "MSBUILD", "DOTNET_"))
    }
    environment.update({
        "DOTNET_CLI_HOME": str(root / ".dotnet-home"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "true",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
        "DOTNET_NOLOGO": "true",
        "NUGET_PACKAGES": str(root / ".packages"),
        "NUGET_HTTP_CACHE_PATH": str(root / ".http-cache"),
        "NUGET_PLUGINS_CACHE_PATH": str(root / ".plugin-cache"),
    })
    completed = subprocess.run(
        ("dotnet", *arguments), cwd=root, env=environment, capture_output=True, text=True, check=False,
    )
    if completed.returncode:
        raise ConsumerError(
            f"Public RC consumer dotnet {arguments[0]} failed ({completed.returncode}):\n"
            f"{completed.stdout}\n{completed.stderr}"
        )

    return completed.stdout.strip()


def run(args: argparse.Namespace) -> None:
    """Restore, build and execute assertions for one already published exact RC."""
    validate_request(args.version, args.sdk_version, args.ef_version, args.target_framework)
    receipt = read_public_receipt(args.readback, args.readback_sha256, args.version)
    root = args.output_dir.resolve()
    project = prepare_fixture(root, args.version, args.sdk_version, args.ef_version, args.target_framework)
    if run_dotnet(root, "--version") != args.sdk_version:
        raise ConsumerError("The installed selected SDK differs from the exact approved RC.")

    run_dotnet(
        root, "restore", str(project), "--configfile", str(root / "NuGet.Config"),
        "--packages", str(root / ".packages"), "--no-http-cache", "--force", "--use-lock-file",
    )
    verify_restore(root, args.version, args.ef_version, receipt)
    run_dotnet(root, "build", str(project), "--configuration", "Release", "--no-restore")
    output = run_dotnet(
        root, "run", "--project", str(project), "--configuration", "Release", "--no-build", "--no-restore",
    )
    if output.splitlines() != [PASS_MARKER]:
        raise ConsumerError("The public consumer runtime assertions did not complete.")

    # WHY: Recheck after MSBuild execution so a target cannot silently replace
    # the graph or retained package bytes between restore and the final receipt.
    verify_restore(root, args.version, args.ef_version, receipt)
    result = {
        "schemaVersion": 1, "kind": "public-platform-rc-consumer", "releaseVersion": args.version,
        "dotnetSdk": args.sdk_version, "efCore": args.ef_version, "targetFramework": args.target_framework,
        "sourceCommit": receipt["sourceCommit"], "readbackSha256": args.readback_sha256,
        "packageSource": publication.NUGET_SOURCE, "restore": "pass", "build": "pass",
        "runtimeAssertions": "pass", "databaseRuntime": "not-exercised",
    }

    (root / "public-rc-consumer.json").write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="ascii")
    print(json.dumps(result, sort_keys=True))


def main() -> int:
    """Parse the explicit, non-publishing public-consumer entry point."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--sdk-version", required=True)
    parser.add_argument("--ef-version", required=True)
    parser.add_argument("--target-framework", required=True)
    parser.add_argument("--readback", type=Path, required=True)
    parser.add_argument("--readback-sha256", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    try:
        run(parser.parse_args())
    except (ConsumerError, publication.PublicationError, OSError, ValueError) as exception:
        print(f"Public RC consumer rejected: {exception}", file=sys.stderr)

        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
