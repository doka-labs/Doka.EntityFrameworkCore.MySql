"""Regression tests for the NuGet publication trust boundary."""

from __future__ import annotations

import copy
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile
from collections.abc import Callable
from contextlib import redirect_stderr, redirect_stdout
from io import BytesIO, StringIO
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from eng.release import github as github_release
from eng.release import nuget as nuget_publication
from eng.release import lines as release_lines
from eng.release import publication as release_publication
from eng.tests.test_release_lines import initial_policy


def _record_exception(action: Callable[[], object]) -> Exception | None:
    """Execute a negative-test action so exception assertions remain a separate phase."""
    try:
        action()
    except Exception as exception:
        return exception

    return None


class NuGetPublicationTests(unittest.TestCase):
    """Prove package identity, retry safety, and isolated consumer restore."""

    _VERSION = "10.0.0-rc.1"
    _COMMIT = "1" * 40
    _REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"
    _PACKAGE_BASE_ADDRESS = "https://packages.example.test/v3-flatcontainer"

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
        self.assertEqual(nuget_publication.CACHE_PACKAGE_ID, result["cache"]["id"])

    def test_package_metadata_rejects_cache_dependency_on_ef_core(self) -> None:
        """Keep the independently consumable cache free from a provider dependency."""
        self._write_package(
            nuget_publication.CACHE_PACKAGE_ID,
            dependencies=[(nuget_publication.PROVIDER_PACKAGE_ID, self._VERSION)],
        )
        with self.assertRaisesRegex(nuget_publication.PublicationError, "independent of EF Core"):
            nuget_publication.validate_package_metadata(
                self.root, self._VERSION, self._REPOSITORY, self._COMMIT
            )

    def test_package_metadata_rejects_missing_cache_package(self) -> None:
        """Do not let the two existing package pairs satisfy the new release."""
        (self.packages / f"{nuget_publication.CACHE_PACKAGE_ID}.{self._VERSION}.nupkg").unlink()
        with self.assertRaisesRegex(nuget_publication.PublicationError, "Doka.Caching.MySql"):
            nuget_publication.validate_package_metadata(
                self.root, self._VERSION, self._REPOSITORY, self._COMMIT
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

    def test_package_metadata_rejects_a_prepublication_signature(self) -> None:
        """Keep author signing outside the keyless publication contract."""
        provider = self.packages / nuget_publication.package_file_name(
            nuget_publication.PROVIDER_PACKAGE_ID,
            self._VERSION,
            "nupkg",
        )
        provider.write_bytes(
            self._package_bytes(
                nuget_publication.PROVIDER_PACKAGE_ID,
                signature=b"unexpected author signature",
            )
        )

        with self.assertRaisesRegex(
            nuget_publication.PublicationError,
            "must be unsigned before NuGet.org ingestion",
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

    def test_package_base_address_is_discovered_from_the_service_index(self) -> None:
        """Use the source capability document instead of a fixed NuGet.org path."""
        service_index = {
            "version": "3.0.0",
            "resources": [
                {
                    "@id": "https://search.example.test/query",
                    "@type": "SearchQueryService/3.5.0",
                },
                {
                    "@id": f"{self._PACKAGE_BASE_ADDRESS}/",
                    "@type": nuget_publication.NUGET_PACKAGE_BASE_ADDRESS_TYPE,
                },
            ],
        }

        address = nuget_publication.resolve_package_base_address(
            timeout_seconds=7,
            fetcher=lambda source, timeout: (
                service_index
                if source == nuget_publication.NUGET_SOURCE and timeout == 7
                else self.fail("Service discovery used an unexpected request contract.")
            ),
        )

        self.assertEqual(self._PACKAGE_BASE_ADDRESS, address)
        self.assertEqual(
            (
                f"{self._PACKAGE_BASE_ADDRESS}/"
                "doka.entityframeworkcore.mysql/10.0.0-rc.1/"
                "doka.entityframeworkcore.mysql.10.0.0-rc.1.nupkg"
            ),
            nuget_publication.remote_package_url(
                address,
                nuget_publication.PROVIDER_PACKAGE_ID,
                self._VERSION,
            ),
        )

    def test_package_urls_reject_noncanonical_nuget_release_versions(self) -> None:
        """Keep release tags inside a normalized NuGet-version subset."""
        for version in (
            "10.00.0-rc.1",
            "10.0.0.0",
            "10.0.0-RC.1",
            "10.0.0-rc.01",
            "10.0.0+build.1",
        ):
            with self.subTest(version=version):
                with self.assertRaisesRegex(
                    nuget_publication.PublicationError,
                    "not canonical for NuGet",
                ):
                    nuget_publication.remote_package_url(
                        self._PACKAGE_BASE_ADDRESS,
                        nuget_publication.PROVIDER_PACKAGE_ID,
                        version,
                    )

    def test_validate_version_command_owns_the_canonical_release_contract(self) -> None:
        """Expose the package-version validator to the earliest workflow gate."""
        with (
            mock.patch.object(
                nuget_publication,
                "parse_arguments",
                return_value=SimpleNamespace(
                    command="validate-version",
                    version=self._VERSION,
                ),
            ),
            redirect_stdout(StringIO()),
        ):
            self.assertEqual(0, nuget_publication.main())

        with (
            mock.patch.object(
                nuget_publication,
                "parse_arguments",
                return_value=SimpleNamespace(
                    command="validate-version",
                    version="10.00.0-rc.1",
                ),
            ),
            redirect_stderr(StringIO()),
        ):
            self.assertEqual(1, nuget_publication.main())

    def test_service_discovery_rejects_ambiguous_or_insecure_resources(self) -> None:
        """Fail closed when the package-content capability is not trustworthy."""
        cases = (
            [],
            [
                {
                    "@id": "http://packages.example.test/v3-flatcontainer",
                    "@type": nuget_publication.NUGET_PACKAGE_BASE_ADDRESS_TYPE,
                }
            ],
            [
                {
                    "@id": self._PACKAGE_BASE_ADDRESS,
                    "@type": nuget_publication.NUGET_PACKAGE_BASE_ADDRESS_TYPE,
                },
                {
                    "@id": "https://secondary.example.test/v3-flatcontainer",
                    "@type": nuget_publication.NUGET_PACKAGE_BASE_ADDRESS_TYPE,
                },
            ],
        )

        for resources in cases:
            with self.subTest(resources=resources):
                with self.assertRaises(nuget_publication.PublicationError):
                    nuget_publication.resolve_package_base_address(
                        fetcher=lambda _source, _timeout: {
                            "version": "3.0.0",
                            "resources": resources,
                        }
                    )

    def test_preflight_allows_absent_packages_and_matching_retry(self) -> None:
        """Permit first publication and a byte-identical partial retry."""
        receipt = self._receipt()
        provider_bytes = self._package_bytes(
            nuget_publication.PROVIDER_PACKAGE_ID,
            signature=b"repository signature",
        )

        def fetcher(url: str, _: float) -> bytes | None:
            if url == nuget_publication.remote_package_url(
                self._PACKAGE_BASE_ADDRESS,
                nuget_publication.PROVIDER_PACKAGE_ID,
                self._VERSION,
            ):
                return provider_bytes
            return None

        states = nuget_publication.remote_states(
            receipt,
            self.root,
            self._PACKAGE_BASE_ADDRESS,
            fetcher=fetcher,
        )

        self.assertEqual("matching", states["provider"]["status"])
        self.assertTrue(states["provider"]["repositorySignaturePresent"])
        self.assertEqual("absent", states["spatial"]["status"])

    def test_preflight_treats_an_unsigned_matching_public_package_as_pending(self) -> None:
        """Wait for repository signing without weakening payload equality."""
        receipt = self._receipt()
        provider_bytes = (
            self.root / str(receipt["packages"]["provider"]["package"])
        ).read_bytes()

        def fetcher(url: str, _: float) -> bytes | None:
            return (
                provider_bytes
                if url == nuget_publication.remote_package_url(
                    self._PACKAGE_BASE_ADDRESS,
                    nuget_publication.PROVIDER_PACKAGE_ID,
                    self._VERSION,
                )
                else None
            )

        states = nuget_publication.remote_states(
            receipt,
            self.root,
            self._PACKAGE_BASE_ADDRESS,
            fetcher=fetcher,
        )

        self.assertEqual("pending-signature", states["provider"]["status"])
        self.assertFalse(states["provider"]["repositorySignaturePresent"])

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
                self._PACKAGE_BASE_ADDRESS,
                fetcher=lambda _url, _timeout: conflicting,
            )

    def test_preflight_allows_spatial_while_provider_is_still_indexing(self) -> None:
        """Treat independently indexed package visibility as retryable state."""
        receipt = self._receipt()
        spatial_bytes = self._package_bytes(
            nuget_publication.SPATIAL_PACKAGE_ID,
            dependencies=[(nuget_publication.PROVIDER_PACKAGE_ID, self._VERSION)],
            signature=b"repository signature",
        )

        def fetcher(url: str, _: float) -> bytes | None:
            if url == nuget_publication.remote_package_url(
                self._PACKAGE_BASE_ADDRESS,
                nuget_publication.SPATIAL_PACKAGE_ID,
                self._VERSION,
            ):
                return spatial_bytes
            return None

        states = nuget_publication.remote_states(
            receipt,
            self.root,
            self._PACKAGE_BASE_ADDRESS,
            fetcher=fetcher,
        )

        self.assertEqual("absent", states["provider"]["status"])
        self.assertEqual("matching", states["spatial"]["status"])

    def test_preflight_requires_publication_while_signature_is_pending(self) -> None:
        """Request credentials whenever an exact package still needs signing."""
        receipt = self._receipt()
        receipt["expectedReleaseTag"] = receipt["releaseTag"]
        manifest, symbol_payloads = self._symbol_manifest()
        symbol_entries = nuget_publication.validated_symbol_entries(
            manifest,
            self._VERSION,
        )
        packages = {
            "provider": {
                "id": nuget_publication.PROVIDER_PACKAGE_ID,
                "status": "pending-signature",
                "url": "https://example.invalid/provider",
                "candidateContentDigest": "1" * 64,
                "publishedContentDigest": "1" * 64,
                "publishedSha256": "2" * 64,
                "repositorySignaturePresent": False,
            },
            "spatial": {
                "id": nuget_publication.SPATIAL_PACKAGE_ID,
                "status": "matching",
                "url": "https://example.invalid/spatial",
                "candidateContentDigest": "2" * 64,
            },
            "cache": {
                "id": nuget_publication.CACHE_PACKAGE_ID,
                "status": "matching",
                "url": "https://example.invalid/cache",
                "candidateContentDigest": "3" * 64,
            },
        }
        symbols = nuget_publication.symbol_states(
            symbol_entries,
            fetcher=lambda entry, _timeout: symbol_payloads[entry["packageId"]],
        )
        receipt_path = self.root / "preflight-receipt.json"
        manifest_path = self.root / "preflight-symbol-manifest.json"
        output = self.root / "preflight.json"
        github_output = self.root / "github-output.txt"
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        arguments = SimpleNamespace(
            receipt=receipt_path,
            candidate_root=self.root,
            symbol_manifest=manifest_path,
            output=output,
            github_output=github_output,
            timeout_seconds=5,
            require_absent=False,
        )

        with (
            mock.patch.object(
                nuget_publication,
                "validate_portable_receipt",
                return_value=nuget_publication.package_paths(self.root, self._VERSION),
            ),
            mock.patch.object(
                nuget_publication,
                "resolve_package_base_address",
                return_value=self._PACKAGE_BASE_ADDRESS,
            ),
            mock.patch.object(nuget_publication, "remote_states", return_value=packages),
            mock.patch.object(nuget_publication, "symbol_states", return_value=symbols),
        ):
            nuget_publication.preflight(arguments)

        evidence = json.loads(output.read_text(encoding="utf-8"))
        self.assertEqual(nuget_publication.NUGET_SOURCE, evidence["packageSource"])
        self.assertEqual(self._PACKAGE_BASE_ADDRESS, evidence["packageBaseAddress"])
        self.assertEqual(
            "pending-signature",
            evidence["packages"]["provider"]["status"],
        )
        outputs = github_output.read_text(encoding="utf-8")
        self.assertIn("provider_published=false", outputs)
        self.assertIn("publication_required=true", outputs)

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

    def test_readback_retries_independent_package_indexing_order(self) -> None:
        """Retry package visibility and repository-signature propagation."""
        receipt = self._receipt()
        manifest, symbol_payloads = self._symbol_manifest()
        symbol_entries = nuget_publication.validated_symbol_entries(
            manifest,
            self._VERSION,
        )
        provider_bytes = self._package_bytes(
            nuget_publication.PROVIDER_PACKAGE_ID,
            signature=b"repository signature",
        )
        unsigned_provider_bytes = self._package_bytes(
            nuget_publication.PROVIDER_PACKAGE_ID,
        )
        spatial_bytes = self._package_bytes(
            nuget_publication.SPATIAL_PACKAGE_ID,
            dependencies=[(nuget_publication.PROVIDER_PACKAGE_ID, self._VERSION)],
            signature=b"repository signature",
        )

        def package_fetcher(url: str, _: float) -> bytes | None:
            if url == nuget_publication.remote_package_url(
                self._PACKAGE_BASE_ADDRESS, nuget_publication.CACHE_PACKAGE_ID, self._VERSION
            ):
                return self._package_bytes(
                    nuget_publication.CACHE_PACKAGE_ID, signature=b"repository signature"
                )
            if url == nuget_publication.remote_package_url(
                self._PACKAGE_BASE_ADDRESS,
                nuget_publication.PROVIDER_PACKAGE_ID,
                self._VERSION,
            ):
                return provider_bytes
            return spatial_bytes

        partial_packages = nuget_publication.observe_remote_packages(
            receipt,
            self.root,
            self._PACKAGE_BASE_ADDRESS,
            fetcher=lambda url, timeout: (
                None
                if url == nuget_publication.remote_package_url(
                    self._PACKAGE_BASE_ADDRESS,
                    nuget_publication.PROVIDER_PACKAGE_ID,
                    self._VERSION,
                )
                else package_fetcher(url, timeout)
            ),
        )
        pending_signature_packages = nuget_publication.observe_remote_packages(
            receipt,
            self.root,
            self._PACKAGE_BASE_ADDRESS,
            fetcher=lambda url, timeout: (
                unsigned_provider_bytes
                if url == nuget_publication.remote_package_url(
                    self._PACKAGE_BASE_ADDRESS,
                    nuget_publication.PROVIDER_PACKAGE_ID,
                    self._VERSION,
                )
                else package_fetcher(url, timeout)
            ),
        )
        complete_packages = nuget_publication.observe_remote_packages(
            receipt,
            self.root,
            self._PACKAGE_BASE_ADDRESS,
            fetcher=package_fetcher,
        )
        absent_symbols = nuget_publication.observe_remote_symbols(
            symbol_entries,
            fetcher=lambda _entry, _timeout: None,
        )
        complete_symbols = nuget_publication.observe_remote_symbols(
            symbol_entries,
            fetcher=lambda entry, _timeout: symbol_payloads[entry["packageId"]],
        )
        receipt_path = self.root / "publication-receipt.json"
        manifest_path = self.root / "symbol-manifest.json"
        output = self.root / "publication-readback.json"
        output_dir = self.root / "public-packages"
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        arguments = SimpleNamespace(
            receipt=receipt_path,
            candidate_root=self.root,
            symbol_manifest=manifest_path,
            output_dir=output_dir,
            output=output,
            timeout_seconds=60,
            request_timeout_seconds=5,
            poll_interval_seconds=1,
        )

        with (
            mock.patch.object(nuget_publication, "validate_portable_receipt"),
            mock.patch.object(
                nuget_publication,
                "resolve_package_base_address",
                return_value=self._PACKAGE_BASE_ADDRESS,
            ),
            mock.patch.object(
                nuget_publication,
                "observe_remote_packages",
                side_effect=(
                    partial_packages,
                    pending_signature_packages,
                    complete_packages,
                ),
            ) as observe_remote_packages,
            mock.patch.object(
                nuget_publication,
                "observe_remote_symbols",
                side_effect=(absent_symbols, complete_symbols, complete_symbols),
            ) as observe_remote_symbols,
            mock.patch.object(
                nuget_publication.time,
                "monotonic",
                side_effect=(0.0, 1.0, 2.0),
            ),
            mock.patch.object(nuget_publication.time, "sleep") as sleep,
        ):
            nuget_publication.readback(arguments)

        self.assertEqual(3, observe_remote_packages.call_count)
        self.assertEqual(
            [
                ("provider", "spatial", "cache"),
                ("provider",),
                ("provider",),
            ],
            [
                call.kwargs["roles"]
                for call in observe_remote_packages.call_args_list
            ],
        )
        self.assertEqual(2, observe_remote_symbols.call_count)
        self.assertEqual(
            "pending-signature",
            pending_signature_packages[0]["provider"]["status"],
        )
        self.assertEqual([mock.call(1), mock.call(1)], sleep.call_args_list)
        evidence = json.loads(output.read_text(encoding="utf-8"))
        self.assertEqual(receipt["expectedReleaseTag"], evidence["expectedReleaseTag"])
        self.assertEqual(nuget_publication.NUGET_SOURCE, evidence["packageSource"])
        self.assertEqual(self._PACKAGE_BASE_ADDRESS, evidence["packageBaseAddress"])
        self.assertEqual("matching", evidence["packages"]["provider"]["status"])
        self.assertEqual("matching", evidence["packages"]["spatial"]["status"])
        github_release.require_release_identity(
            evidence,
            "NuGet publication readback",
            receipt,
            "verifiedUtc",
        )
        github_release.validate_observation_set(
            evidence,
            receipt,
            {entry["packageId"]: entry for entry in symbol_entries},
            require_matching=True,
        )

    def test_readback_command_registers_the_publication_poll_budget(self) -> None:
        """Keep the one-hour deadline while polling NuGet every 30 seconds."""
        with mock.patch.object(
            sys,
            "argv",
            [
                "nuget.py",
                "readback",
                "--receipt",
                "receipt.json",
                "--candidate-root",
                "candidate",
                "--symbol-manifest",
                "symbols.json",
                "--output-dir",
                "packages",
                "--output",
                "readback.json",
            ],
        ):
            arguments = nuget_publication.parse_arguments()

        self.assertEqual(
            nuget_publication.PUBLICATION_READBACK_TIMEOUT_SECONDS,
            arguments.timeout_seconds,
        )
        self.assertEqual(
            nuget_publication.PUBLICATION_READBACK_POLL_INTERVAL_SECONDS,
            arguments.poll_interval_seconds,
        )

    def test_prepare_and_bind_preserve_identity_after_main_advances(self) -> None:
        """Bind the exact candidate while allowing later protected-main merges."""
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
        self._git(
            repository_root,
            "update-ref",
            "refs/remotes/origin/main",
            source_commit,
        )
        release_tag = f"v{self._VERSION}"

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
        self._write_package_at(candidate_packages, nuget_publication.CACHE_PACKAGE_ID, source_commit)

        manifest = {
            "releaseCandidateRunId": "github-123",
            "releaseVersion": self._VERSION,
            "expectedReleaseTag": release_tag,
            "source": {
                "commit": source_commit,
                "ref": "refs/heads/main",
                "tag": None,
                "repository": f"https://github.com/{self._REPOSITORY}.git",
            },
            "workflow": {
                "provider": "github-actions",
                "runId": "123",
                "runAttempt": "1",
                "workflow": nuget_publication.CANDIDATE_WORKFLOW,
                "workflowRef": (
                    f"{self._REPOSITORY}/{nuget_publication.CANDIDATE_WORKFLOW_PATH}"
                    "@refs/heads/main"
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
        output = candidate_root / "candidate-receipt.json"
        github_output = self.root / "github-output.txt"
        arguments = SimpleNamespace(
            repo=repository_root,
            root=candidate_root,
            repository=self._REPOSITORY,
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
            nuget_publication.prepare_candidate(arguments)

        candidate_receipt = json.loads(output.read_text(encoding="utf-8"))
        self.assertEqual(source_commit, candidate_receipt["sourceCommit"])
        self.assertEqual(release_tag, candidate_receipt["expectedReleaseTag"])
        self.assertNotIn("releaseTag", candidate_receipt)
        self.assertIn("provider_package=", github_output.read_text(encoding="utf-8"))

        self._git(repository_root, "tag", "-a", "-m", "release", release_tag)
        tree_id = self._git(repository_root, "rev-parse", "HEAD^{tree}")
        qualification_receipt = {
            "name": "repository-qualification",
            "id": 5001,
            "checkSuiteId": 7001,
            "conclusion": "success",
            "workflowPath": ".github/workflows/ci.yml",
            "workflowRunId": 9001,
            "runAttempt": 1,
            "event": "pull_request",
            "headBranch": "feature/provider-change",
            "commit": "f" * 40,
            "workflowConclusion": "success",
            "pullRequestNumber": 64,
            "baseBranch": "main",
            "mergedCommit": source_commit,
            "mergedTreeId": tree_id,
            "qualifiedTreeId": tree_id,
            "qualificationArtifactId": 8001,
        }
        qualification_manifest, policy = self._qualification_manifest(
            qualification_receipt,
            commit=source_commit,
            tree_id=tree_id,
            release_tag=release_tag,
        )
        qualification_path = candidate_root / "release-qualification-manifest.json"
        qualification_path.write_text(
            json.dumps(qualification_manifest),
            encoding="utf-8",
        )
        (repository_root / "later.txt").write_text(
            "unrelated later main change\n",
            encoding="ascii",
        )
        self._git(repository_root, "add", "later.txt")
        self._git(repository_root, "commit", "-m", "test: advance protected main")
        later_commit = self._git(repository_root, "rev-parse", "HEAD")
        self._git(
            repository_root,
            "update-ref",
            "refs/remotes/origin/main",
            "HEAD",
        )
        self._git(repository_root, "checkout", "--detach", source_commit)
        trust_receipt = self.root / "release-tag-trust-root.json"
        trust_receipt.write_text(
            json.dumps(
                {
                    "schemaVersion": 2,
                    "kind": "release-tag-trust-root",
                    "repository": self._REPOSITORY,
                    "tag": release_tag,
                    "commit": source_commit,
                    "policyDigest": policy["policyDigest"],
                    "qualification": qualification_receipt,
                }
            ),
            encoding="utf-8",
        )
        publication_receipt = candidate_root / "release-publication-receipt.json"
        bind_arguments = SimpleNamespace(
            repo=repository_root,
            root=candidate_root,
            candidate_receipt=output,
            tag_trust_receipt=trust_receipt,
            release_tag=release_tag,
            output=publication_receipt,
            github_output=None,
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
            nuget_publication.bind_candidate(bind_arguments)

        bound_receipt = json.loads(publication_receipt.read_text(encoding="utf-8"))
        self.assertEqual(nuget_publication.PUBLICATION_RECEIPT_KIND, bound_receipt["kind"])
        self.assertEqual(release_tag, bound_receipt["releaseTag"])
        self.assertEqual(
            nuget_publication.sha256_file(output),
            bound_receipt["candidateReceiptSha256"],
        )
        self.assertEqual(
            nuget_publication.sha256_file(qualification_path),
            bound_receipt["qualificationManifestSha256"],
        )

        unrelated_commit = self._git(
            repository_root,
            "commit-tree",
            f"{source_commit}^{{tree}}",
            "-m",
            "unrelated main",
        )
        self._git(
            repository_root,
            "update-ref",
            "refs/remotes/origin/main",
            unrelated_commit,
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
            self.assertRaisesRegex(
                nuget_publication.PublicationError,
                "no longer on current remote main history",
            ),
        ):
            nuget_publication.bind_candidate(bind_arguments)
        self._git(
            repository_root,
            "update-ref",
            "refs/remotes/origin/main",
            later_commit,
        )

        qualification_path.write_text("{}\n", encoding="utf-8")
        with (
            mock.patch.object(nuget_publication.release_evidence, "verify_manifest"),
            self.assertRaisesRegex(
                nuget_publication.PublicationError,
                "does not bind the qualification manifest",
            ),
        ):
            nuget_publication.validate_portable_receipt(
                bound_receipt,
                candidate_root,
            )

    def test_restore_receipt_requires_exact_packages_source_and_isolated_cache(self) -> None:
        """Reject a consumer proof that could have resolved repository-local bytes."""
        package_cache = self.root / "consumer-packages"
        assets = self.root / "project.assets.json"
        cache_assets = self.root / "cache.assets.json"
        output = self.root / "consumer-readback.json"
        assets.write_text(
            json.dumps(
                {
                    "packageFolders": {f"{package_cache}/": {}},
                    "libraries": {
                        f"{nuget_publication.PROVIDER_PACKAGE_ID}/{self._VERSION}": {"type": "package"},
                        f"{nuget_publication.SPATIAL_PACKAGE_ID}/{self._VERSION}": {"type": "package"},
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

        cache_graph = json.loads(assets.read_text(encoding="utf-8"))
        cache_graph["libraries"] = {
            f"{nuget_publication.CACHE_PACKAGE_ID}/{self._VERSION}": {"type": "package"}
        }
        cache_assets.write_text(json.dumps(cache_graph), encoding="utf-8")

        arguments = SimpleNamespace(
            assets=assets,
            cache_assets=cache_assets,
            package_cache=package_cache,
            version=self._VERSION,
            release_tag=f"v{self._VERSION}",
            source_commit=self._COMMIT,
            dotnet_sdk="10.0.302",
            engine_image="mysql:8.4@example",
            output=output,
        )
        nuget_publication.verify_restore(arguments)

        self.assertEqual("pass", json.loads(output.read_text(encoding="utf-8"))["runtimeSmoke"])
        self.assertEqual("pass", json.loads(output.read_text(encoding="utf-8"))["cacheRuntimeSmoke"])

        for invalid_name in ("Microsoft.EntityFrameworkCore/10.0.11", "pomelo.entityframeworkcore.mysql/9.0.0"):
            with self.subTest(invalid_name=invalid_name):
                invalid_graph = {
                    **cache_graph,
                    "libraries": {**cache_graph["libraries"], invalid_name: {"type": "package"}},
                }
                cache_assets.write_text(json.dumps(invalid_graph), encoding="utf-8")
                with self.assertRaisesRegex(nuget_publication.PublicationError, "EF Core or Pomelo"):
                    nuget_publication.verify_restore(arguments)

    def _receipt(self) -> dict[str, object]:
        """Return the validated-candidate shape consumed by remote checks."""
        provider_package = (
            Path("packages")
            / nuget_publication.package_file_name(
                nuget_publication.PROVIDER_PACKAGE_ID,
                self._VERSION,
                "nupkg",
            )
        )
        spatial_package = (
            Path("packages")
            / nuget_publication.package_file_name(
                nuget_publication.SPATIAL_PACKAGE_ID,
                self._VERSION,
                "nupkg",
            )
        )
        cache_package = Path("packages") / nuget_publication.package_file_name(
            nuget_publication.CACHE_PACKAGE_ID, self._VERSION, "nupkg"
        )
        return {
            "releaseTag": f"v{self._VERSION}",
            "expectedReleaseTag": f"v{self._VERSION}",
            "releaseVersion": self._VERSION,
            "sourceCommit": self._COMMIT,
            "packages": {
                "provider": {
                    "package": provider_package.as_posix(),
                    "contentDigest": nuget_publication.canonical_package_digest(
                        self.root / provider_package
                    ),
                },
                "spatial": {
                    "package": spatial_package.as_posix(),
                    "contentDigest": nuget_publication.canonical_package_digest(
                        self.root / spatial_package
                    ),
                },
                "cache": {
                    "package": cache_package.as_posix(),
                    "contentDigest": nuget_publication.canonical_package_digest(self.root / cache_package),
                },
            },
        }

    def _qualification_manifest(
        self,
        receipt: dict[str, object],
        *,
        commit: str,
        tree_id: str,
        release_tag: str,
    ) -> tuple[dict[str, object], dict[str, object]]:
        """Return complete frozen qualification for the publication fixture."""
        policy = nuget_publication.release_qualification.load_policy()
        policy_digest = nuget_publication.release_qualification.policy_digest(policy)
        entries: list[dict[str, object]] = []
        for index, gate in enumerate(policy["gates"], start=1):
            entry: dict[str, object] = {
                "gate": gate["id"],
                "kind": gate["kind"],
            }
            values: dict[str, object] = {
                "commit": commit,
                "treeId": tree_id,
                "workflowPath": gate["producerWorkflow"],
                "workflowRunId": (
                    receipt["workflowRunId"]
                    if gate["kind"] == "protected-check"
                    else 1000 + index
                ),
                "runAttempt": 1,
                "event": receipt["event"],
                "conclusion": "success",
                "apiResourceId": receipt["id"],
                "pullRequestNumber": receipt["pullRequestNumber"],
                "baseBranch": receipt["baseBranch"],
                "qualifiedCommit": receipt["commit"],
                "qualifiedTreeId": receipt["qualifiedTreeId"],
                "qualificationArtifactId": receipt["qualificationArtifactId"],
                "responseDigest": hashlib.sha256(
                    json.dumps(
                        receipt,
                        sort_keys=True,
                        separators=(",", ":"),
                    ).encode("utf-8")
                ).hexdigest(),
                "sourceHash": "c" * 64,
                "dependencySnapshotDigest": "d" * 64,
                "artifactId": 2000 + index,
                "artifactDigest": "e" * 64,
            }
            for field in gate["boundIdentities"]:
                entry[field] = values[field]
            entries.append(entry)

        manifest = {
            "schemaVersion": 2,
            "kind": "release-qualification-manifest",
            "policyVersion": policy["policyVersion"],
            "policyDigest": policy_digest,
            "selectionRuleVersion": policy["selectionRule"]["version"],
            "repository": self._REPOSITORY,
            "commit": commit,
            "treeId": tree_id,
            "expectedReleaseTag": release_tag,
            "releaseVersion": release_tag.removeprefix("v"),
            "assemblingRunAttempt": 1,
            "requiredProtectedChecks": policy["requiredProtectedChecks"],
            "gates": entries,
        }

        return manifest, {"policyDigest": policy_digest}

    def _symbol_manifest(self) -> tuple[dict[str, object], dict[str, bytes]]:
        """Return exact public symbol probes and their candidate payloads."""
        payloads = {
            nuget_publication.PROVIDER_PACKAGE_ID: b"BSJB provider portable PDB",
            nuget_publication.SPATIAL_PACKAGE_ID: b"BSJB spatial portable PDB",
            nuget_publication.CACHE_PACKAGE_ID: b"BSJB cache portable PDB",
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
            "schemaVersion": nuget_publication.SYMBOL_MANIFEST_SCHEMA_VERSION,
            "releaseVersion": self._VERSION,
            "symbols": symbols,
        }, payloads

    def _write_candidate_packages(self) -> None:
        """Write all exact package and symbol file pairs."""
        self._write_package(nuget_publication.PROVIDER_PACKAGE_ID)
        self._write_package(
            nuget_publication.SPATIAL_PACKAGE_ID,
            dependencies=[(nuget_publication.PROVIDER_PACKAGE_ID, self._VERSION)],
        )
        self._write_package(nuget_publication.CACHE_PACKAGE_ID)

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


class ReleaseLineReceiptTests(unittest.TestCase):
    """Bind new receipts without reinterpreting or rewriting historical evidence."""

    def setUp(self) -> None:
        """Build real package bytes and a frozen main-line qualification fixture."""
        # WHY: Hosted identity is test input, not the ambient job running the
        # suite; each context-sensitive case supplies it explicitly.
        environment = mock.patch.dict(os.environ, {}, clear=True)
        environment.start()
        self.addCleanup(environment.stop)
        self.fixture = NuGetPublicationTests()
        self.fixture.setUp()
        self.root = self.fixture.root / "github-123"
        shutil.copytree(self.fixture.packages, self.root / "packages")
        self.policy = initial_policy()
        self.line = release_lines.freeze_line(self.policy, "refs/heads/main", self.fixture._VERSION)
        self.authority = self._authority(self.policy)
        self.manifest = {
            "releaseCandidateRunId": self.root.name,
            "releaseVersion": self.fixture._VERSION,
            "expectedReleaseTag": f"v{self.fixture._VERSION}",
            "releaseLine": self.line,
            "source": {
                "repository": self.fixture._REPOSITORY,
                "ref": "refs/heads/main",
                "tag": None,
                "commit": self.fixture._COMMIT,
            },
            "workflow": {
                "provider": "github-actions",
                "runId": "123",
                "runAttempt": "1",
                "workflow": "release-candidate",
                "workflowRef": f"{self.fixture._REPOSITORY}/{self.line['workflowPath']}@refs/heads/main",
                "repository": self.fixture._REPOSITORY,
            },
            "toolchain": {"approvedDotnetSdk": "10.0.402", "dotnetSdk": "10.0.402"},
        }
        nuget_publication.write_json(self.root / nuget_publication.release_evidence.MANIFEST_NAME, self.manifest)
        self.candidate = {
            "schemaVersion": 2,
            "kind": nuget_publication.CANDIDATE_RECEIPT_KIND,
            "candidateRunId": "123",
            "candidateRunAttempt": "1",
            "releaseCandidateRunId": self.root.name,
            "releaseVersion": self.fixture._VERSION,
            "expectedReleaseTag": f"v{self.fixture._VERSION}",
            "repository": self.fixture._REPOSITORY,
            "sourceRef": "refs/heads/main",
            "sourceCommit": self.fixture._COMMIT,
            "releaseLine": self.line,
            "workflowRef": self.manifest["workflow"]["workflowRef"],
            "toolchain": self.manifest["toolchain"],
            "mysql84Image": "mysql:8.4.6",
            "packages": nuget_publication.validate_package_metadata(
                self.root, self.fixture._VERSION, self.fixture._REPOSITORY, self.fixture._COMMIT
            ),
        }
        self.candidate_path = self.fixture.root / "candidate-receipt.json"
        nuget_publication.write_json(self.candidate_path, self.candidate)
        identity = release_publication.candidate_identity(self.root, self.candidate_path, "1")
        self.admission = release_publication.create_admission(identity, self.authority)
        qualification = self.root / "release-qualification-manifest.json"
        qualification.write_bytes(b"qualified immutable evidence\n")
        self.receipt = {
            **self.candidate,
            "schemaVersion": 5,
            "kind": nuget_publication.PUBLICATION_RECEIPT_KIND,
            "releaseTag": self.candidate["expectedReleaseTag"],
            "candidateReceiptSha256": nuget_publication.sha256_file(self.candidate_path),
            "tagTrustRootSha256": "a" * 64,
            "qualificationManifestSha256": nuget_publication.sha256_file(qualification),
            "publicationAdmission": self.admission,
            "publicationAuthority": self.authority,
        }

    def tearDown(self) -> None:
        """Dispose the shared package fixture owned by this test only."""
        self.fixture.tearDown()

    @staticmethod
    def _authority(policy: dict[str, object]) -> dict[str, object]:
        """Create one protected-main observation with a reproducible digest."""
        return {
            "schemaVersion": 1,
            "kind": release_publication.AUTHORITY_KIND,
            "repository": policy["repository"],
            "controlCommit": "b" * 40,
            "authorizationRevision": policy["revision"],
            "authorizationDigest": release_lines.policy_digest(policy),
            "observedUtc": "2026-10-22T12:00:00Z",
            "policy": policy,
        }

    def test_current_candidate_preserves_line_workflow_toolchain_and_packages(self) -> None:
        """The new receipt validates the exact frozen evidence, not local defaults."""
        # Arrange
        manifest_path = self.root / nuget_publication.release_evidence.MANIFEST_NAME
        before = manifest_path.read_bytes()

        # Act
        with mock.patch.object(nuget_publication.release_evidence, "verify_manifest"):
            packages = nuget_publication.validate_candidate_receipt(self.candidate, self.root)

        # Assert
        self.assertEqual({"provider", "spatial", "cache"}, set(packages))
        self.assertEqual(before, manifest_path.read_bytes())
        self.assertEqual(self.line, self.candidate["releaseLine"])

    def test_prepare_emits_new_schema_only_when_sealed_line_evidence_exists(self) -> None:
        """Preparation copies the sealed toolchain instead of reconstructing it."""
        # Arrange
        manifest = copy.deepcopy(self.manifest)
        manifest["engines"] = [{"targetId": "mysql84", "image": "mysql:8.4.6"}]
        nuget_publication.write_json(self.root / nuget_publication.release_evidence.MANIFEST_NAME, manifest)
        arguments = SimpleNamespace(
            repo=self.root,
            root=self.root,
            repository=self.fixture._REPOSITORY,
            output=self.fixture.root / "prepared-candidate.json",
            github_output=None,
        )

        # Act
        with (
            mock.patch.object(nuget_publication.release_evidence, "verify_manifest"),
            mock.patch.object(nuget_publication, "run_git", side_effect=[self.fixture._COMMIT, "", ""]),
        ):
            nuget_publication.prepare_candidate(arguments)

        # Assert
        receipt = nuget_publication.read_json(arguments.output, "prepared candidate")
        self.assertEqual(2, receipt["schemaVersion"])
        self.assertEqual(manifest["toolchain"], receipt["toolchain"])
        self.assertEqual(manifest["releaseLine"], receipt["releaseLine"])
        self.assertEqual(manifest["workflow"]["workflowRef"], receipt["workflowRef"])

    def test_unknown_candidate_schema_is_not_treated_as_legacy(self) -> None:
        """Only the two explicit receipt versions enter their own reader."""
        # Arrange
        candidate = {**self.candidate, "schemaVersion": 3}

        # Act
        failure = _record_exception(lambda: nuget_publication.validate_candidate_receipt(candidate, self.root))

        # Assert
        self.assertIsInstance(failure, nuget_publication.PublicationError)
        self.assertIn("identity", str(failure))

    def test_candidate_rejects_cross_line_workflow_or_toolchain_evidence(self) -> None:
        """Nearby valid-looking identities cannot substitute for frozen evidence."""
        # Arrange
        cases = (
            ("sourceRef", "refs/heads/maintenance/10.x"),
            ("workflowRef", f"{self.fixture._REPOSITORY}/.github/workflows/release-lines.yml@refs/heads/main"),
            ("toolchain", {"approvedDotnetSdk": "11.0.100", "dotnetSdk": "11.0.100"}),
        )
        candidates = [{**self.candidate, field: value} for field, value in cases]

        # Act
        with mock.patch.object(nuget_publication.release_evidence, "verify_manifest"):
            failures = [
                _record_exception(lambda: nuget_publication.validate_candidate_receipt(candidate, self.root))
                for candidate in candidates
            ]

        # Assert
        for (field, _), failure in zip(cases, failures, strict=True):
            with self.subTest(field=field):
                self.assertIsInstance(failure, nuget_publication.PublicationError)

    def test_historical_schema_cannot_discard_new_line_identity(self) -> None:
        """A schema downgrade is not a fallback for missing line metadata."""
        # Arrange
        candidate = {**self.candidate, "schemaVersion": 1}

        # Act
        with mock.patch.object(nuget_publication.release_evidence, "verify_manifest"):
            failure = _record_exception(lambda: nuget_publication.validate_candidate_receipt(candidate, self.root))

        # Assert
        self.assertIsInstance(failure, nuget_publication.PublicationError)
        self.assertIn("Historical", str(failure))

    def test_new_publication_validates_without_mutating_sealed_manifest(self) -> None:
        """Admission remains a sidecar rather than changing the signed candidate."""
        # Arrange
        manifest_path = self.root / nuget_publication.release_evidence.MANIFEST_NAME
        before = manifest_path.read_bytes()

        # Act
        with mock.patch.object(nuget_publication.release_evidence, "verify_manifest"):
            nuget_publication.validate_portable_receipt(self.receipt, self.root)

        # Assert
        self.assertEqual(before, manifest_path.read_bytes())

    def test_updated_active_authority_preserves_original_admission(self) -> None:
        """An active retry keeps the original immutable intent bytes."""
        # Arrange
        policy = copy.deepcopy(self.policy)
        policy["revision"] = "2026-10-22.2"
        authority = self._authority(policy)
        before = json.dumps(self.receipt, sort_keys=True)

        # Act
        nuget_publication.validate_publication_authority(self.receipt, self.root, self.admission, authority)

        # Assert
        self.assertEqual(before, json.dumps(self.receipt, sort_keys=True))
        self.assertNotEqual(
            authority["authorizationDigest"], self.receipt["publicationAuthority"]["authorizationDigest"]
        )

    def test_inactive_authority_rejects_the_same_candidate(self) -> None:
        """Past qualification does not grant permission on an inactive line."""
        # Arrange
        policy = copy.deepcopy(self.policy)
        policy["lines"][0]["state"] = "inactive"
        authority = self._authority(policy)

        # Act
        failure = _record_exception(
            lambda: nuget_publication.validate_publication_authority(self.receipt, self.root, self.admission, authority)
        )

        # Assert
        self.assertIsInstance(failure, nuget_publication.PublicationError)
        self.assertIn("not eligible", str(failure))

    def test_publication_rejects_cross_role_admission_even_with_valid_digest(self) -> None:
        """An internally self-consistent admission cannot change the candidate line."""
        # Arrange
        admission = copy.deepcopy(self.admission)
        admission["identity"]["releaseLineSha256"] = "f" * 64
        admission["admissionId"] = release_publication.canonical_digest(admission["identity"])
        receipt = {**self.receipt, "publicationAdmission": admission}

        # Act
        failure = _record_exception(lambda: nuget_publication.validate_publication_authority(receipt, self.root))

        # Assert
        self.assertIsInstance(failure, nuget_publication.PublicationError)
        self.assertIn("different line", str(failure))

    def test_current_boundary_requires_explicit_authority_files(self) -> None:
        """New publication cannot accidentally take a legacy no-authority route."""
        # Arrange
        arguments = SimpleNamespace(candidate_root=self.root)

        # Act
        failure = _record_exception(lambda: nuget_publication.validate_current_publication(arguments, self.receipt))

        # Assert
        self.assertIsInstance(failure, nuget_publication.PublicationError)
        self.assertIn("--admission", str(failure))

    def _binding_arguments(self) -> SimpleNamespace:
        """Prepare deterministic tag and authority sidecars for bind-only tests."""
        trust_path = self.fixture.root / "release-tag-trust-root.json"
        qualification = {"schemaVersion": 3, "releaseLine": self.line, "policyDigest": "d" * 64}
        nuget_publication.write_json(self.root / "release-qualification-manifest.json", qualification)
        nuget_publication.write_json(trust_path, {
            "schemaVersion": 3,
            "kind": "release-tag-trust-root",
            "repository": self.fixture._REPOSITORY,
            "tag": self.candidate["expectedReleaseTag"],
            "commit": self.fixture._COMMIT,
            "policyDigest": "d" * 64,
            "qualification": {"mergedCommit": self.fixture._COMMIT},
            "releaseLine": self.line,
        })
        admission_path = self.fixture.root / "publication-admission.json"
        authority_path = self.fixture.root / "authorization-observation.json"
        nuget_publication.write_json(admission_path, self.admission)
        nuget_publication.write_json(authority_path, self.authority)

        return SimpleNamespace(
            repo=self.root,
            root=self.root,
            candidate_receipt=self.candidate_path,
            tag_trust_receipt=trust_path,
            release_tag=self.candidate["expectedReleaseTag"],
            output=self.fixture.root / "release-publication-receipt.json",
            github_output=None,
            admission=admission_path,
            authority=authority_path,
        )

    def _binding_git(self, repo: Path, *arguments: str) -> str:
        """Model only the already-tested Git read contract used during binding."""
        del repo
        tag_commit_arguments = ("rev-parse", f"refs/tags/{self.candidate['expectedReleaseTag']}^{{commit}}")
        if arguments in (("rev-parse", "HEAD"), tag_commit_arguments):
            return self.fixture._COMMIT

        if arguments[0] == "cat-file":
            return "tag"

        if arguments[0] == "tag":
            return str(self.candidate["expectedReleaseTag"])

        if arguments[0] == "rev-parse":
            return "c" * 40

        return ""

    def test_binding_new_candidate_freezes_original_admission_and_authority(self) -> None:
        """Tag binding writes schema five without moving sidecars into the manifest."""
        # Arrange
        arguments = self._binding_arguments()
        manifest_before = (self.root / nuget_publication.release_evidence.MANIFEST_NAME).read_bytes()

        # Act
        with (
            mock.patch.object(nuget_publication.release_evidence, "verify_manifest"),
            mock.patch.object(nuget_publication, "run_git", side_effect=self._binding_git),
            mock.patch.object(nuget_publication.release_qualification, "load_policy", return_value={}),
            mock.patch.object(nuget_publication.release_trust, "verify_frozen_qualification_receipt"),
        ):
            nuget_publication.bind_candidate(arguments)

        # Assert
        receipt = nuget_publication.read_json(arguments.output, "bound publication")
        self.assertEqual(5, receipt["schemaVersion"])
        self.assertEqual(self.admission, receipt["publicationAdmission"])
        self.assertEqual(self.authority, receipt["publicationAuthority"])
        self.assertEqual(manifest_before, (self.root / nuget_publication.release_evidence.MANIFEST_NAME).read_bytes())

    def test_binding_retry_keeps_original_receipt_under_changed_current_authority(self) -> None:
        """An active retry preserves the bytes already staged in its draft."""
        # Arrange
        arguments = self._binding_arguments()
        with (
            mock.patch.object(nuget_publication.release_evidence, "verify_manifest"),
            mock.patch.object(nuget_publication, "run_git", side_effect=self._binding_git),
            mock.patch.object(nuget_publication.release_qualification, "load_policy", return_value={}),
            mock.patch.object(nuget_publication.release_trust, "verify_frozen_qualification_receipt"),
        ):
            nuget_publication.bind_candidate(arguments)

        before = arguments.output.read_bytes()
        policy = copy.deepcopy(self.policy)
        policy["revision"] = "2026-10-22.2"
        nuget_publication.write_json(arguments.authority, self._authority(policy))

        # Act
        with (
            mock.patch.object(nuget_publication.release_evidence, "verify_manifest"),
            mock.patch.object(nuget_publication, "run_git", side_effect=self._binding_git),
            mock.patch.object(nuget_publication.release_qualification, "load_policy", return_value={}),
            mock.patch.object(nuget_publication.release_trust, "verify_frozen_qualification_receipt"),
        ):
            nuget_publication.bind_candidate(arguments)

        # Assert
        self.assertEqual(before, arguments.output.read_bytes())

    def test_current_boundary_rejects_another_workflow_run(self) -> None:
        """The right candidate cannot be republished from an unrelated workflow run."""
        # Arrange
        arguments = self._binding_arguments()
        arguments.candidate_root = self.root
        environment = {
            "GITHUB_ACTIONS": "true",
            "DOKA_PUBLICATION_LOCK_GROUP": "doka-publication",
            "DOKA_PUBLICATION_LOCK_HELD": "1",
            "GITHUB_REPOSITORY": self.fixture._REPOSITORY,
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_SHA": self.fixture._COMMIT,
            "GITHUB_RUN_ID": "999",
            "GITHUB_RUN_ATTEMPT": "1",
            "GITHUB_WORKFLOW_REF": self.candidate["workflowRef"],
        }

        # Act
        with mock.patch.dict(os.environ, environment, clear=True):
            failure = _record_exception(lambda: nuget_publication.validate_current_publication(arguments, self.receipt))

        # Assert
        self.assertIsInstance(failure, nuget_publication.PublicationError)
        self.assertIn("same-run workflow", str(failure))


class QualificationManifestGateTests(unittest.TestCase):
    """Prove one workflow owns candidate qualification and publication."""

    WORKFLOW = (
        Path(__file__).resolve().parents[2]
        / ".github" / "workflows" / "release-candidate.yml"
    )

    def setUp(self) -> None:
        """Read the qualifier and the exact local composite it invokes."""
        self.workflow = self.WORKFLOW.read_text(encoding="utf-8")
        publisher_path = self.WORKFLOW.parents[1] / "actions" / "publish-release" / "action.yml"
        self.publisher = publisher_path.read_text(encoding="utf-8")
        self.text = self.workflow + "\n" + self.publisher

    def test_the_manifest_is_verified_before_tag_binding(self) -> None:
        """Require canonical qualification before any irreversible operation."""
        # Arrange
        publisher_job = self.workflow.split("\n  publish:\n", maxsplit=1)[1]

        # Act
        evidence_position = publisher_job.index("python3 -m eng.release.evidence verify")
        admission_position = publisher_job.index("python3 -m eng.release.publication admit")
        upload_position = publisher_job.index("name: publication-admission-attempt-")
        publisher_position = publisher_job.index("uses: $/.github/actions/publish-release\n")
        trust_position = self.publisher.index("python3 -m eng.release.trust verify")
        binding_position = self.publisher.index("python3 -m eng.release.nuget bind")

        # Assert
        self.assertIn("python3 -m eng.release.qualification verify", self.workflow)
        self.assertIn("      - assemble\n", publisher_job)
        self.assertIn("      - attest\n", publisher_job)
        self.assertIn("uses: $/.github/actions/publish-release\n", publisher_job)
        self.assertLess(evidence_position, admission_position)
        self.assertLess(admission_position, upload_position)
        self.assertLess(upload_position, publisher_position)
        self.assertLess(
            trust_position,
            binding_position,
        )

    def test_the_manifest_and_tag_are_bound_to_this_candidate(self) -> None:
        """Reject evidence that describes another commit or expected tag."""
        for argument in (
            "--expected-commit",
            "--expected-release-tag",
            "--policy eng/release/evidence-policy.json",
            "--qualification-manifest",
        ):
            with self.subTest(argument=argument):
                self.assertIn(argument, self.text)

    def test_the_qualification_manifest_is_an_immutable_release_asset(self) -> None:
        """Retain the frozen protected-check selection with the release."""
        self.assertIn(
            "release-qualification-manifest.json",
            github_release.RELEASE_CANDIDATE_EVIDENCE_FILES,
        )

    def test_no_cross_run_publication_workflow_remains(self) -> None:
        """Keep candidate bytes and publication inside one approved workflow run."""
        self.assertFalse((self.WORKFLOW.parent / "nuget-publish.yml").exists())
        self.assertNotIn("candidate_run_id", self.text)
        self.assertNotIn("gh run download", self.text)


if __name__ == "__main__":
    unittest.main()
