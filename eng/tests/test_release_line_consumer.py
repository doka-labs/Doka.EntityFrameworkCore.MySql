"""Positive and adversarial public-platform-RC consumer contracts."""

from __future__ import annotations

import base64
import hashlib
import json
import tempfile
import unittest
import zipfile
from collections.abc import Callable
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from eng.release import consumer
from eng.release import nuget


class PublicRcConsumerTests(unittest.TestCase):
    """Keep public RC restore independent from local source and floating graphs."""

    VERSION = "11.0.0-rc.3"
    SDK = "11.0.100-rc.2.26504.105"
    EF = "11.0.0-rc.2.26504.105"
    COMMIT = "a" * 40

    def setUp(self) -> None:
        """Create isolated source, cache and independently hashed public readback."""
        self.temporary = tempfile.TemporaryDirectory(prefix="doka-public-rc-consumer-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.receipt: dict = {
            "schemaVersion": 3,
            "releaseVersion": self.VERSION,
            "releaseTag": f"v{self.VERSION}",
            "expectedReleaseTag": f"v{self.VERSION}",
            "sourceCommit": self.COMMIT,
            "packageSource": nuget.NUGET_SOURCE,
            "packages": {},
        }
        self.assets: dict = {
            "packageFolders": {str(self.root / ".packages"): {}},
            "project": {"restore": {"sources": {nuget.NUGET_SOURCE: {}}}},
            "libraries": {f"Microsoft.EntityFrameworkCore.Relational/{self.EF}": {"type": "package"}},
        }
        for role, package_id in nuget.PACKAGE_IDENTITIES.items():
            self._write_package(role, package_id)

        self._save_assets()
        self.readback = self.root / "readback.json"
        self._save_receipt()

    def _save_assets(self) -> None:
        """Persist only the fixture's current restore graph."""
        (self.root / "obj").mkdir(exist_ok=True)
        (self.root / "obj/project.assets.json").write_text(json.dumps(self.assets), encoding="ascii")

    @staticmethod
    def _failure(action: Callable[[], object]) -> Exception:
        """Capture the act-phase rejection for separate assert-phase checks."""
        try:
            action()
        except (consumer.ConsumerError, nuget.PublicationError) as exception:
            return exception

        raise AssertionError("The invalid public consumer identity was accepted.")

    def _save_receipt(self) -> None:
        """Refresh the approved checksum only while arranging receipt scenarios."""
        self.readback.write_text(json.dumps(self.receipt), encoding="ascii")
        self.receipt_hash = hashlib.sha256(self.readback.read_bytes()).hexdigest()

    def _write_package(self, role: str, package_id: str, *, version: str | None = None) -> None:
        """Create a signed-shape package and matching independent identity fields."""
        version = version or self.VERSION
        relative = f"{package_id.lower()}/{self.VERSION}"
        package_root = self.root / ".packages" / relative
        package_root.mkdir(parents=True, exist_ok=True)
        package = package_root / f"{package_id.lower()}.{self.VERSION}.nupkg"
        with zipfile.ZipFile(package, "w") as archive:
            archive.writestr(
                f"{package_id}.nuspec",
                f'<package><metadata><id>{package_id}</id><version>{version}</version>'
                '<repository url="https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql" '
                f'commit="{self.COMMIT}" /></metadata></package>',
            )
            archive.writestr(".signature.p7s", b"fixture-signature-not-a-real-certificate")
            archive.writestr("lib/net11.0/fixture.txt", package_id)

        payload = package.read_bytes()
        digest = nuget.canonical_package_digest(payload)
        sha512 = base64.b64encode(hashlib.sha512(payload).digest()).decode("ascii")
        (package_root / f"{package.name}.sha512").write_text(sha512, encoding="ascii")
        (package_root / ".nupkg.metadata").write_text(
            json.dumps({"source": nuget.NUGET_SOURCE, "contentHash": sha512}), encoding="ascii",
        )
        self.assets["libraries"][f"{package_id}/{self.VERSION}"] = {
            "type": "package", "path": relative, "sha512": sha512,
        }
        self.receipt["packages"][role] = {
            "id": package_id, "status": "matching", "candidateContentDigest": digest,
            "publishedContentDigest": digest, "publishedSha256": hashlib.sha256(payload).hexdigest(),
        }

    def test_doka_rc_number_is_independent_of_upstream_rc_number(self) -> None:
        """Accept another Doka RC on the exact same upstream RC graph."""
        # Arrange
        request = (self.VERSION, self.SDK, self.EF, "net11.0")

        # Act
        result = consumer.validate_request(*request)

        # Assert
        self.assertIsNone(result)

    def test_nonexact_or_cross_major_request_is_rejected(self) -> None:
        """Reject minimum ranges, floating versions and platform substitutions."""
        # Arrange
        requests = (
            ("10.6.1", self.SDK, self.EF, "net11.0"),
            ("[11.0.0-rc.3]", self.SDK, self.EF, "net11.0"),
            ("11.0.*", self.SDK, self.EF, "net11.0"),
            ("11.0.0-rc.03", self.SDK, self.EF, "net11.0"),
            (self.VERSION, "11.0.100", self.EF, "net11.0"),
            (self.VERSION, self.SDK, "11.0.0-rc.*", "net11.0"),
            (self.VERSION, self.SDK, "10.0.0-rc.2", "net11.0"),
            (self.VERSION, self.SDK, self.EF, "net10.0"),
        )

        # Act
        failures = [self._failure(lambda: consumer.validate_request(*request)) for request in requests]

        # Assert
        self.assertEqual(len(requests), len(failures))

    def test_public_readback_receipt_requires_approved_checksum(self) -> None:
        """Accept the exact existing public readback schema and coherent package set."""
        # Arrange
        expected = self.receipt

        # Act
        result = consumer.read_public_receipt(self.readback, self.receipt_hash, self.VERSION)

        # Assert
        self.assertEqual(expected, result)

    def test_tampered_readback_is_rejected(self) -> None:
        """An attacker cannot replace both package bytes and their local evidence."""
        # Arrange
        self.readback.write_text("{}", encoding="ascii")

        # Act
        failure = self._failure(lambda: consumer.read_public_receipt(self.readback, self.receipt_hash, self.VERSION))

        # Assert
        self.assertIn("checksum differs", str(failure))

    def test_incomplete_or_wrong_release_readback_is_rejected(self) -> None:
        """Reject an approved receipt describing a different RC or package set."""
        # Arrange
        del self.receipt["packages"]["cache"]
        self._save_receipt()

        # Act
        failure = self._failure(lambda: consumer.read_public_receipt(self.readback, self.receipt_hash, self.VERSION))

        # Assert
        self.assertIn("three-package", str(failure))

    def test_exact_public_package_graph_and_bytes_are_accepted(self) -> None:
        """Bind assets, NuGet source, archive content and repository source identity."""
        # Arrange
        receipt = self.receipt

        # Act
        result = consumer.verify_restore(self.root, self.VERSION, self.EF, receipt)

        # Assert
        self.assertIsNone(result)

    def test_local_feed_fallback_is_rejected(self) -> None:
        """Adding a workstation feed cannot satisfy public consumer qualification."""
        # Arrange
        self.assets["project"]["restore"]["sources"]["/tmp/local-feed"] = {}
        self._save_assets()

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("NuGet.org-only", str(failure))

    def test_project_reference_substitution_is_rejected(self) -> None:
        """A successful local source build is not proof of published package consumption."""
        # Arrange
        self.assets["libraries"][f"{nuget.PROVIDER_PACKAGE_ID}/{self.VERSION}"]["type"] = "project"
        self._save_assets()

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("project instead", str(failure))

    def test_wrong_doka_rc_is_rejected(self) -> None:
        """Another same-major prerelease is not an equivalent consumer identity."""
        # Arrange
        entry = self.assets["libraries"].pop(f"{nuget.PROVIDER_PACKAGE_ID}/{self.VERSION}")
        self.assets["libraries"][f"{nuget.PROVIDER_PACKAGE_ID}/11.0.0-rc.2"] = entry
        self._save_assets()

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("exact Doka RC", str(failure))

    def test_wrong_ef_patch_is_rejected(self) -> None:
        """The installed SDK cannot stand in for proof of the resolved EF graph."""
        # Arrange
        self.assets["libraries"]["Microsoft.EntityFrameworkCore/11.0.0-rc.1.1"] = {"type": "package"}
        self._save_assets()

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("different EF Core graph", str(failure))

    def test_changed_restored_archive_is_rejected(self) -> None:
        """A correct requested version cannot authorize different restored bytes."""
        # Arrange
        package = next((self.root / ".packages").rglob("*.nupkg"))
        with zipfile.ZipFile(package, "a") as archive:
            archive.writestr("unexpected.txt", "modified")

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("content differs", str(failure))

    def test_workstation_global_package_cache_is_rejected(self) -> None:
        """Restore must actually use the isolated cache, not merely request it."""
        # Arrange
        self.assets["packageFolders"]["/tmp/workstation-global-cache"] = {}
        self._save_assets()

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("isolated", str(failure))

    def test_trusted_readback_still_rejects_forged_local_cache_checksums(self) -> None:
        """Local metadata cannot authorize a replacement archive with the right version."""
        # Arrange
        package_id = nuget.PROVIDER_PACKAGE_ID
        relative = f"{package_id.lower()}/{self.VERSION}"
        package_root = self.root / ".packages" / relative
        package = package_root / f"{package_id.lower()}.{self.VERSION}.nupkg"
        with zipfile.ZipFile(package, "a") as archive:
            archive.writestr("forged.txt", "different public bytes")

        sha512 = base64.b64encode(hashlib.sha512(package.read_bytes()).digest()).decode("ascii")
        self.assets["libraries"][f"{package_id}/{self.VERSION}"]["sha512"] = sha512
        (package_root / ".nupkg.metadata").write_text(
            json.dumps({"source": nuget.NUGET_SOURCE, "contentHash": sha512}), encoding="ascii",
        )
        (package_root / f"{package.name}.sha512").write_text(sha512, encoding="ascii")
        self._save_assets()

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("content differs", str(failure))

    def test_package_source_commit_must_match_approved_public_readback(self) -> None:
        """A same-version package from another source is not the released candidate."""
        # Arrange
        receipt = {**self.receipt, "sourceCommit": "b" * 40}

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, receipt))

        # Assert
        self.assertIn("metadata differs", str(failure))

    def test_symlinked_package_cache_is_rejected(self) -> None:
        """A rewritten assets file cannot move the isolated cache to another tree."""
        # Arrange
        cache = self.root / ".packages"
        external = self.root / "external-cache"
        cache.rename(external)
        cache.symlink_to(external, target_is_directory=True)
        self.assets["packageFolders"] = {str(external): {}}
        self._save_assets()

        # Act
        failure = self._failure(lambda: consumer.verify_restore(self.root, self.VERSION, self.EF, self.receipt))

        # Assert
        self.assertIn("isolated", str(failure))

    def test_generated_fixture_has_exact_external_references_and_sdk(self) -> None:
        """The runnable project stays outside central policy and needs no consumer RC."""
        # Arrange
        destination = self.root / "source"

        # Act
        project = consumer.prepare_fixture(destination, self.VERSION, self.SDK, self.EF, "net11.0")

        # Assert
        text = project.read_text(encoding="ascii")
        self.assertEqual(3, text.count(f'Version="[{self.VERSION}]"'))
        self.assertIn(f'Version="[{self.EF}]"', text)
        self.assertNotIn("ProjectReference", text)
        self.assertNotIn("SafeMigrations", text)
        self.assertEqual(self.SDK, consumer.read_json(destination / "global.json")["sdk"]["version"])
        self.assertIn('<clear />', (destination / "NuGet.Config").read_text(encoding="ascii"))
        self.assertIn('value="require"', (destination / "NuGet.Config").read_text(encoding="ascii"))

    def test_fixture_cannot_overwrite_existing_work(self) -> None:
        """Output isolation is not permission to replace an existing consumer tree."""
        # Arrange
        destination = self.root

        # Act
        failure = self._failure(
            lambda: consumer.prepare_fixture(destination, self.VERSION, self.SDK, self.EF, "net11.0"),
        )

        # Assert
        self.assertIn("must be empty", str(failure))

    def test_orchestration_builds_only_after_exact_restore_and_rechecks_after_execution(self) -> None:
        """Exercise the full command boundary offline without pretending to publish."""
        # Arrange
        destination = self.root / "source"
        args = SimpleNamespace(
            version=self.VERSION, sdk_version=self.SDK, ef_version=self.EF, target_framework="net11.0",
            readback=self.readback, readback_sha256=self.receipt_hash, output_dir=destination,
        )
        observed = []

        def command(root: Path, *arguments: str) -> str:
            observed.append(arguments[0])

            return self.SDK if arguments[0] == "--version" else consumer.PASS_MARKER if arguments[0] == "run" else ""

        def verify(*unused) -> None:
            observed.append("verify")

        # Act
        with mock.patch.object(consumer, "run_dotnet", side_effect=command), \
                mock.patch.object(consumer, "verify_restore", side_effect=verify), \
                mock.patch("builtins.print"):
            consumer.run(args)

        # Assert
        self.assertEqual(["--version", "restore", "verify", "build", "run", "verify"], observed)
        self.assertEqual("pass", consumer.read_json(destination / "public-rc-consumer.json")["runtimeAssertions"])

    def test_orchestration_stops_before_build_on_wrong_sdk(self) -> None:
        """A local stable SDK cannot qualify the requested exact upstream RC."""
        # Arrange
        args = SimpleNamespace(
            version=self.VERSION, sdk_version=self.SDK, ef_version=self.EF, target_framework="net11.0",
            readback=self.readback, readback_sha256=self.receipt_hash, output_dir=self.root / "source",
        )

        # Act
        with mock.patch.object(consumer, "run_dotnet", return_value="10.0.402") as command:
            failure = self._failure(lambda: consumer.run(args))

        # Assert
        self.assertIn("SDK differs", str(failure))
        self.assertEqual(1, command.call_count)


if __name__ == "__main__":
    unittest.main()
