"""Regression tests for the NuGet publication trust boundary."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import tempfile
import unittest
import zipfile
from io import BytesIO
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from eng.release import nuget as nuget_publication


class NuGetPublicationTests(unittest.TestCase):
    """Prove package identity, retry safety, and isolated consumer restore."""

    _VERSION = "10.0.0-rc.1"
    _COMMIT = "1" * 40
    _REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"

    def setUp(self) -> None:
        """Create an isolated candidate package directory."""
        self._temporary_directory = tempfile.TemporaryDirectory(prefix="doka-nuget-publication-")
        self.root = Path(self._temporary_directory.name)
        self.packages = self.root / "packages"
        self.packages.mkdir()
        self._write_candidate_packages()

    def tearDown(self) -> None:
        """Dispose the isolated publication fixture."""
        self._temporary_directory.cleanup()

    def test_package_metadata_binds_ids_version_commit_and_spatial_dependency(self) -> None:
        """Accept both package contracts only when their internal identities agree."""
        result = nuget_publication.validate_package_metadata(
            self.root,
            self._VERSION,
            self._REPOSITORY,
            self._COMMIT,
        )

        self.assertEqual(
            nuget_publication.PROVIDER_PACKAGE_ID,
            result["provider"]["id"],
        )
        self.assertEqual(
            nuget_publication.SPATIAL_PACKAGE_ID,
            result["spatial"]["id"],
        )

    def test_package_metadata_rejects_spatial_dependency_version_drift(self) -> None:
        """Reject a plugin that could restore a different provider release."""
        self._write_package(
            nuget_publication.SPATIAL_PACKAGE_ID,
            dependencies=[(nuget_publication.PROVIDER_PACKAGE_ID, "10.0.0")],
        )

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "exact provider release version",
        ):
            nuget_publication.validate_package_metadata(
                self.root,
                self._VERSION,
                self._REPOSITORY,
                self._COMMIT,
            )

    def test_package_metadata_rejects_a_misidentified_symbol_package(self) -> None:
        """Reject symbols that do not belong to the primary package pair."""
        symbols = self.packages / nuget_publication.package_file_name(
            nuget_publication.PROVIDER_PACKAGE_ID,
            self._VERSION,
            "snupkg",
        )
        symbols.write_bytes(
            self._symbol_package_bytes(nuget_publication.SPATIAL_PACKAGE_ID)
        )

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "Symbol package metadata mismatch",
        ):
            nuget_publication.validate_package_metadata(
                self.root,
                self._VERSION,
                self._REPOSITORY,
                self._COMMIT,
            )

    def test_package_metadata_rejects_non_portable_symbols(self) -> None:
        """Reject a symbol package NuGet.org cannot index for consumers."""
        symbols = self.packages / nuget_publication.package_file_name(
            nuget_publication.PROVIDER_PACKAGE_ID,
            self._VERSION,
            "snupkg",
        )
        symbols.write_bytes(
            self._symbol_package_bytes(
                nuget_publication.PROVIDER_PACKAGE_ID,
                pdb=b"not a portable PDB",
            )
        )

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "Symbol package metadata mismatch",
        ):
            nuget_publication.validate_package_metadata(
                self.root,
                self._VERSION,
                self._REPOSITORY,
                self._COMMIT,
            )

    def test_canonical_digest_ignores_only_repository_signature(self) -> None:
        """Allow NuGet repository signing without weakening payload comparison."""
        unsigned = self._package_bytes(nuget_publication.PROVIDER_PACKAGE_ID)
        signed = self._package_bytes(
            nuget_publication.PROVIDER_PACKAGE_ID,
            signature=b"repository signature",
        )

        self.assertEqual(
            nuget_publication.canonical_package_digest(unsigned),
            nuget_publication.canonical_package_digest(signed),
        )

    def test_canonical_digest_detects_payload_changes(self) -> None:
        """Reject an existing package version whose shipped payload differs."""
        candidate = self._package_bytes(nuget_publication.PROVIDER_PACKAGE_ID)
        conflicting = self._package_bytes(
            nuget_publication.PROVIDER_PACKAGE_ID,
            library=b"different assembly",
        )

        self.assertNotEqual(
            nuget_publication.canonical_package_digest(candidate),
            nuget_publication.canonical_package_digest(conflicting),
        )

    def test_preflight_allows_absent_packages_and_matching_retry(self) -> None:
        """Permit first publication and a byte-identical partial retry."""
        receipt = self._receipt()
        provider_bytes = (
            self.root / str(receipt["packages"]["provider"]["package"])
        ).read_bytes()

        def fetcher(url: str, _: float) -> bytes | None:
            if url == nuget_publication.remote_package_url(
                nuget_publication.PROVIDER_PACKAGE_ID,
                self._VERSION,
            ):
                return provider_bytes
            return None

        states = nuget_publication.remote_states(
            receipt,
            self.root,
            fetcher=fetcher,
        )

        self.assertEqual("matching", states["provider"]["status"])
        self.assertEqual("absent", states["spatial"]["status"])

    def test_preflight_rejects_conflicting_existing_package(self) -> None:
        """Never hide an immutable same-version conflict behind retry behavior."""
        receipt = self._receipt()
        conflicting = self._package_bytes(
            nuget_publication.PROVIDER_PACKAGE_ID,
            library=b"conflicting assembly",
        )

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "conflicting bytes",
        ):
            nuget_publication.remote_states(
                receipt,
                self.root,
                fetcher=lambda _url, _timeout: conflicting,
            )

    def test_preflight_rejects_spatial_without_provider(self) -> None:
        """Reject an impossible dependency publication order before login."""
        receipt = self._receipt()
        spatial_bytes = (
            self.root / str(receipt["packages"]["spatial"]["package"])
        ).read_bytes()

        def fetcher(url: str, _: float) -> bytes | None:
            if url == nuget_publication.remote_package_url(
                nuget_publication.SPATIAL_PACKAGE_ID,
                self._VERSION,
            ):
                return spatial_bytes
            return None

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "without its required provider",
        ):
            nuget_publication.remote_states(
                receipt,
                self.root,
                fetcher=fetcher,
            )

    def test_candidate_paths_reject_absolute_and_traversal_values(self) -> None:
        """Reject receipt paths that cannot remain portable across runners."""
        invalid_paths = (
            str(self.packages / "candidate.nupkg"),
            "../packages/candidate.nupkg",
            "packages/../candidate.nupkg",
            "packages\\candidate.nupkg",
        )

        for value in invalid_paths:
            with self.subTest(value=value):
                with self.assertRaisesRegex(
                    nuget_publication.PublicationError,
                    "canonical relative path",
                ):
                    nuget_publication.resolve_candidate_path(
                        self.root,
                        value,
                        "candidate package",
                    )

    def test_candidate_paths_reject_symlinked_files(self) -> None:
        """Reject a receipt file that redirects outside immutable evidence."""
        with tempfile.TemporaryDirectory() as outside_directory:
            outside = Path(outside_directory) / "candidate.nupkg"
            outside.write_bytes(b"untrusted package")
            link = self.root / "linked-candidate.nupkg"
            link.symlink_to(outside)

            with self.assertRaisesRegex(
                nuget_publication.PublicationError,
                "missing or non-regular",
            ):
                nuget_publication.resolve_candidate_path(
                    self.root,
                    link.name,
                    "candidate package",
                )

    def test_symbol_readback_requires_exact_public_portable_pdb_bytes(self) -> None:
        """Accept only the symbol bytes whose checksum is sealed into the DLL."""
        manifest, payloads = self._symbol_manifest()
        entries = nuget_publication.validated_symbol_entries(manifest, self._VERSION)

        states = nuget_publication.symbol_states(
            entries,
            fetcher=lambda entry, _timeout: payloads[entry["packageId"]],
        )

        self.assertEqual(
            "matching",
            states[nuget_publication.PROVIDER_PACKAGE_ID]["status"],
        )

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "conflicting symbols",
        ):
            nuget_publication.symbol_states(
                entries,
                fetcher=lambda _entry, _timeout: b"BSJB different portable PDB",
            )

    def test_symbol_readback_rejects_forged_or_incomplete_probe_sets(self) -> None:
        """Reject probes that could redirect readback or omit a shipped package."""
        manifest, _ = self._symbol_manifest()
        manifest["symbols"][0]["symbolUrl"] = "https://example.invalid/provider.pdb"

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "entry is invalid",
        ):
            nuget_publication.validated_symbol_entries(manifest, self._VERSION)

        manifest, _ = self._symbol_manifest()
        manifest["symbols"].pop()
        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "package set is invalid",
        ):
            nuget_publication.validated_symbol_entries(manifest, self._VERSION)

    def test_candidate_run_must_be_successful_and_tag_bound(self) -> None:
        """Bind the selected run to the exact repository, tag, and source SHA."""
        run = {
            "id": 123,
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "status": "completed",
            "conclusion": "success",
            "path": (
                f"{nuget_publication.CANDIDATE_WORKFLOW_PATH}@v{self._VERSION}"
            ),
            "head_sha": self._COMMIT,
            "head_branch": f"v{self._VERSION}",
            "repository": {"full_name": self._REPOSITORY},
        }

        self.assertEqual(
            ("123", "1"),
            nuget_publication.validate_run_metadata(
                run,
                self._REPOSITORY,
                f"v{self._VERSION}",
                self._COMMIT,
            ),
        )

        run["conclusion"] = "failure"
        with self.assertRaisesRegex(nuget_publication.PublicationError, "not completed successfully"):
            nuget_publication.validate_run_metadata(
                run,
                self._REPOSITORY,
                f"v{self._VERSION}",
                self._COMMIT,
            )

    def test_validate_candidate_binds_current_main_tag_run_and_manifest(self) -> None:
        """Accept a candidate only when every local and hosted identity agrees."""
        repository_root = self.root / "trusted-repository"
        repository_root.mkdir()
        self._git(repository_root, "init", "--initial-branch=main")
        self._git(repository_root, "config", "user.name", "Doka Test")
        self._git(repository_root, "config", "user.email", "doka-test@example.invalid")
        self._git(repository_root, "config", "commit.gpgSign", "false")
        self._git(repository_root, "config", "tag.gpgSign", "false")
        self._git(
            repository_root,
            "remote",
            "add",
            "origin",
            f"https://github.com/{self._REPOSITORY}.git",
        )
        (repository_root / "source.txt").write_text("reviewed source\n", encoding="ascii")
        self._git(repository_root, "add", "source.txt")
        self._git(repository_root, "commit", "-m", "test: seed trusted source")
        source_commit = self._git(repository_root, "rev-parse", "HEAD")
        release_tag = f"v{self._VERSION}"
        self._git(repository_root, "tag", release_tag)

        candidate_root = self.root / "github-123"
        candidate_packages = candidate_root / "packages"
        candidate_packages.mkdir(parents=True)
        self._write_package_at(
            candidate_packages,
            nuget_publication.PROVIDER_PACKAGE_ID,
            source_commit,
        )
        self._write_package_at(
            candidate_packages,
            nuget_publication.SPATIAL_PACKAGE_ID,
            source_commit,
            [(nuget_publication.PROVIDER_PACKAGE_ID, self._VERSION)],
        )

        manifest = {
            "releaseCandidateRunId": "github-123",
            "releaseVersion": self._VERSION,
            "source": {
                "commit": source_commit,
                "ref": f"refs/tags/{release_tag}",
                "tag": release_tag,
                "repository": f"https://github.com/{self._REPOSITORY}.git",
            },
            "workflow": {
                "provider": "github-actions",
                "runId": "123",
                "runAttempt": "1",
                "workflow": nuget_publication.CANDIDATE_WORKFLOW,
                "workflowRef": (
                    f"{self._REPOSITORY}/{nuget_publication.CANDIDATE_WORKFLOW_PATH}"
                    f"@refs/tags/{release_tag}"
                ),
                "repository": self._REPOSITORY,
            },
            "engines": [
                {
                    "targetId": "mysql84",
                    "image": "mysql:8.4@example",
                },
            ],
        }
        (candidate_root / nuget_publication.release_evidence.MANIFEST_NAME).write_text(
            json.dumps(manifest),
            encoding="utf-8",
        )
        run_metadata = self.root / "candidate-run.json"
        run_metadata.write_text(
            json.dumps(
                {
                    "id": 123,
                    "run_attempt": 1,
                    "event": "workflow_dispatch",
                    "status": "completed",
                    "conclusion": "success",
                    "path": f"{nuget_publication.CANDIDATE_WORKFLOW_PATH}@{release_tag}",
                    "head_sha": source_commit,
                    "head_branch": release_tag,
                    "repository": {"full_name": self._REPOSITORY},
                }
            ),
            encoding="utf-8",
        )
        output = self.root / "validated-candidate.json"
        github_output = self.root / "github-output.txt"
        arguments = SimpleNamespace(
            repo=repository_root,
            root=candidate_root,
            run_metadata=run_metadata,
            release_tag=release_tag,
            repository=self._REPOSITORY,
            trusted_ref="refs/heads/main",
            trusted_commit=source_commit,
            output=output,
            github_output=github_output,
        )

        with (
            mock.patch.object(nuget_publication.release_evidence, "verify_manifest"),
            mock.patch.dict(
                os.environ,
                {
                    "GITHUB_REF": "refs/heads/main",
                    "GITHUB_SHA": source_commit,
                },
            ),
        ):
            nuget_publication.validate_candidate(arguments)

        receipt = json.loads(output.read_text(encoding="utf-8"))
        self.assertEqual(source_commit, receipt["sourceCommit"])
        self.assertEqual(release_tag, receipt["releaseTag"])
        self.assertIn("provider_package=", github_output.read_text(encoding="utf-8"))

        arguments.trusted_commit = "2" * 40
        with (
            mock.patch.object(nuget_publication.release_evidence, "verify_manifest"),
            mock.patch.dict(
                os.environ,
                {
                    "GITHUB_REF": "refs/heads/main",
                    "GITHUB_SHA": source_commit,
                },
            ),
            self.assertRaisesRegex(
                nuget_publication.PublicationError,
                "not the current trusted main commit",
            ),
        ):
            nuget_publication.validate_candidate(arguments)

    def test_restore_receipt_requires_exact_packages_source_and_isolated_cache(self) -> None:
        """Reject a consumer proof that could have resolved repository-local bytes."""
        package_cache = self.root / "consumer-packages"
        assets = self.root / "project.assets.json"
        output = self.root / "consumer-readback.json"
        assets.write_text(
            json.dumps(
                {
                    "packageFolders": {f"{package_cache}/": {}},
                    "libraries": {
                        f"{nuget_publication.PROVIDER_PACKAGE_ID}/{self._VERSION}": {},
                        f"{nuget_publication.SPATIAL_PACKAGE_ID}/{self._VERSION}": {},
                    },
                    "project": {
                        "restore": {
                            "sources": {nuget_publication.NUGET_SOURCE: {}},
                        },
                    },
                }
            ),
            encoding="utf-8",
        )

        nuget_publication.verify_restore(
            SimpleNamespace(
                assets=assets,
                package_cache=package_cache,
                version=self._VERSION,
                release_tag=f"v{self._VERSION}",
                source_commit=self._COMMIT,
                dotnet_sdk="10.0.302",
                engine_image="mysql:8.4@example",
                output=output,
            )
        )

        self.assertEqual("pass", json.loads(output.read_text(encoding="utf-8"))["runtimeSmoke"])

    def _receipt(self) -> dict[str, object]:
        """Return the validated-candidate shape consumed by remote checks."""
        return {
            "releaseTag": f"v{self._VERSION}",
            "releaseVersion": self._VERSION,
            "sourceCommit": self._COMMIT,
            "packages": {
                "provider": {
                    "package": (
                        Path("packages")
                        / nuget_publication.package_file_name(
                            nuget_publication.PROVIDER_PACKAGE_ID,
                            self._VERSION,
                            "nupkg",
                        )
                    ).as_posix(),
                },
                "spatial": {
                    "package": (
                        Path("packages")
                        / nuget_publication.package_file_name(
                            nuget_publication.SPATIAL_PACKAGE_ID,
                            self._VERSION,
                            "nupkg",
                        )
                    ).as_posix(),
                },
            },
        }

    def _symbol_manifest(self) -> tuple[dict[str, object], dict[str, bytes]]:
        """Return two exact public symbol probes and their candidate payloads."""
        payloads = {
            nuget_publication.PROVIDER_PACKAGE_ID: b"BSJB provider portable PDB",
            nuget_publication.SPATIAL_PACKAGE_ID: b"BSJB spatial portable PDB",
        }
        symbols = []
        for index, (package_id, payload) in enumerate(payloads.items(), start=1):
            pdb_name = f"{package_id}.pdb"
            symbol_key = f"{index:032x}FFFFFFFF"
            sha256 = hashlib.sha256(payload).hexdigest()
            symbols.append(
                {
                    "packageId": package_id,
                    "packageVersion": self._VERSION,
                    "pdbName": pdb_name,
                    "symbolKey": symbol_key,
                    "symbolUrl": (
                        f"{nuget_publication.NUGET_SYMBOL_SERVER}/"
                        f"{pdb_name}/{symbol_key}/{pdb_name}"
                    ),
                    "checksumHeader": f"SHA256:{sha256}",
                    "sha256": sha256,
                }
            )
        return {
            "schemaVersion": nuget_publication.SCHEMA_VERSION,
            "releaseVersion": self._VERSION,
            "symbols": symbols,
        }, payloads

    def _write_candidate_packages(self) -> None:
        """Write both exact package and symbol file pairs."""
        self._write_package(nuget_publication.PROVIDER_PACKAGE_ID)
        self._write_package(
            nuget_publication.SPATIAL_PACKAGE_ID,
            dependencies=[(nuget_publication.PROVIDER_PACKAGE_ID, self._VERSION)],
        )

    def _write_package(
        self,
        package_id: str,
        dependencies: list[tuple[str, str]] | None = None,
    ) -> None:
        """Write one minimal valid primary package and its symbol companion."""
        self._write_package_at(
            self.packages,
            package_id,
            self._COMMIT,
            dependencies,
        )

    def _write_package_at(
        self,
        directory: Path,
        package_id: str,
        source_commit: str,
        dependencies: list[tuple[str, str]] | None = None,
    ) -> None:
        """Write one package pair with the requested source identity."""
        primary = directory / nuget_publication.package_file_name(
            package_id,
            self._VERSION,
            "nupkg",
        )
        symbols = directory / nuget_publication.package_file_name(
            package_id,
            self._VERSION,
            "snupkg",
        )
        primary.write_bytes(
            self._package_bytes(
                package_id,
                dependencies=dependencies,
                source_commit=source_commit,
            )
        )
        symbols.write_bytes(self._symbol_package_bytes(package_id))

    def _package_bytes(
        self,
        package_id: str,
        dependencies: list[tuple[str, str]] | None = None,
        signature: bytes | None = None,
        library: bytes = b"provider assembly",
        source_commit: str | None = None,
    ) -> bytes:
        """Build a deterministic synthetic NuGet archive for boundary tests."""
        dependency_xml = "".join(
            f'<dependency id="{dependency_id}" version="{version}" />'
            for dependency_id, version in dependencies or []
        )
        nuspec = (
            '<?xml version="1.0" encoding="utf-8"?>'
            '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
            "<metadata>"
            f"<id>{package_id}</id>"
            f"<version>{self._VERSION}</version>"
            f'<repository type="git" url="https://github.com/{self._REPOSITORY}" '
            f'commit="{source_commit or self._COMMIT}" />'
            f"<dependencies><group targetFramework=\"net10.0\">{dependency_xml}</group></dependencies>"
            "</metadata>"
            "</package>"
        ).encode("utf-8")

        output = BytesIO()
        with zipfile.ZipFile(output, mode="w") as package:
            package.writestr(f"{package_id}.nuspec", nuspec)
            package.writestr(f"lib/net10.0/{package_id}.dll", library)
            if signature is not None:
                package.writestr(nuget_publication.NUGET_SIGNATURE_ENTRY, signature)
        return output.getvalue()

    def _symbol_package_bytes(
        self,
        package_id: str,
        pdb: bytes = b"BSJB synthetic portable PDB",
    ) -> bytes:
        """Build one minimal symbol package with its required identity markers."""
        nuspec = (
            '<?xml version="1.0" encoding="utf-8"?>'
            '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
            "<metadata>"
            f"<id>{package_id}</id>"
            f"<version>{self._VERSION}</version>"
            '<packageTypes><packageType name="SymbolsPackage" /></packageTypes>'
            "</metadata>"
            "</package>"
        ).encode("utf-8")

        output = BytesIO()
        with zipfile.ZipFile(output, mode="w") as package:
            package.writestr(f"{package_id}.nuspec", nuspec)
            package.writestr(f"lib/net10.0/{package_id}.pdb", pdb)
        return output.getvalue()

    @staticmethod
    def _git(repository: Path, *arguments: str) -> str:
        """Run one deterministic Git fixture command."""
        result = subprocess.run(
            ("git", *arguments),
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
        )
        return result.stdout.strip()


if __name__ == "__main__":
    unittest.main()
