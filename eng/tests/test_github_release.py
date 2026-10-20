"""Regression tests for conflict-safe GitHub release finalization."""

from __future__ import annotations

import base64
import copy
import hashlib
import io
import json
import os
import tempfile
import unittest
import zipfile
from collections.abc import Callable
from pathlib import Path
from unittest import mock

from eng.release import evidence as release_evidence
from eng.release import github as github_release
from eng.release import nuget as nuget_publication
from eng.release import lines as release_lines
from eng.release import publication as release_publication
from eng.release import provenance as release_provenance
from eng.tests.test_release_lines import initial_policy


def _record_exception(action: Callable[[], object]) -> Exception | None:
    """Execute a negative-test action so exception assertions remain a separate phase."""
    try:
        action()
    except Exception as exception:
        return exception

    return None


class FakeReleaseClient:
    """Model the narrow remote release contract without external mutations."""

    def __init__(self) -> None:
        """Initialize an absent release and observable mutation counters."""
        self.release: dict[str, object] | None = None
        self.payloads: dict[int, bytes] = {}
        self.create_calls = 0
        self.upload_calls: list[list[str]] = []
        self.delete_calls: list[tuple[str, int]] = []
        self.publish_calls = 0
        self.verified_tags: list[tuple[str, str, str]] = []
        self.latest = False
        self.latest_release: dict[str, object] | None = None
        self.latest_error: github_release.GitHubReleaseError | None = None
        self.latest_reads = 0
        self.published_latest: list[bool] = []
        self.tag_commits: dict[str, str] = {}
        self.tag_error: github_release.GitHubReleaseError | None = None
        self.tag_error_on_verification: int | None = None
        self.release_visibility_delay = 0
        self.asset_visibility_delay = 0
        self._pending_release_reads = 0
        self._pending_asset_reads = 0
        self._assets_before_upload: list[dict[str, object]] = []
        self._next_asset_id = 100

    def verify_tag(self, repository: str, tag: str, commit: str) -> None:
        """Record tag verification and optionally reject the remote identity."""
        self.verified_tags.append((repository, tag, commit))
        if self.tag_error is not None and (
            self.tag_error_on_verification is None
            or len(self.verified_tags) == self.tag_error_on_verification
        ):
            raise self.tag_error

    def get_release(
        self,
        repository: str,
        tag: str,
    ) -> dict[str, object] | None:
        """Return a detached snapshot like a real remote API response."""
        del repository, tag
        if self._pending_release_reads > 0:
            self._pending_release_reads -= 1
            return None

        release = copy.deepcopy(self.release)
        if release is not None and self._pending_asset_reads > 0:
            self._pending_asset_reads -= 1
            release["assets"] = copy.deepcopy(self._assets_before_upload)
        return release

    def create_draft(self, plan: dict[str, object]) -> None:
        """Create exactly the draft metadata requested by the plan."""
        self.create_calls += 1
        self._pending_release_reads = self.release_visibility_delay
        self.tag_commits[str(plan["releaseTag"])] = str(plan["sourceCommit"])
        self.release = {
            "id": 42,
            "tag_name": plan["releaseTag"],
            "name": plan["name"],
            "body": plan["notes"],
            "draft": True,
            "prerelease": plan["prerelease"],
            "immutable": False,
            "assets": [],
            "html_url": (
                f"https://github.com/{plan['repository']}/releases/tag/"
                f"{plan['releaseTag']}"
            ),
            "published_at": None,
        }

    def upload_assets(
        self,
        repository: str,
        tag: str,
        paths: list[Path],
    ) -> None:
        """Append missing assets while preserving every existing payload."""
        del repository, tag
        if self.release is None:
            raise AssertionError("A draft must exist before assets are uploaded.")

        self.upload_calls.append([path.name for path in paths])
        assets = self.release["assets"]
        if not isinstance(assets, list):
            raise AssertionError("The fake release asset inventory is invalid.")

        self._assets_before_upload = copy.deepcopy(assets)
        self._pending_asset_reads = self.asset_visibility_delay
        for path in paths:
            payload = path.read_bytes()
            asset_id = self._next_asset_id
            self._next_asset_id += 1
            self.payloads[asset_id] = payload
            assets.append(
                {
                    "id": asset_id,
                    "name": path.name,
                    "state": "uploaded",
                    "size": len(payload),
                }
            )

    def publish_release(self, plan: dict[str, object]) -> None:
        """Publish and lock the matching draft."""
        if self.release is None:
            raise AssertionError("A draft must exist before publication.")

        self.publish_calls += 1
        self.published_latest.append(bool(plan["latest"]))
        self.release["draft"] = False
        self.release["immutable"] = True
        self.release["published_at"] = "2026-08-04T12:00:00Z"
        if plan["latest"]:
            self.latest = True
            self.latest_release = copy.deepcopy(self.release)

    def delete_asset(self, repository: str, asset_id: int) -> None:
        """Remove one exact remote asset while retaining every unrelated payload."""
        if self.release is None:
            raise AssertionError("A release must exist before an asset is deleted.")

        assets = self.release["assets"]
        if not isinstance(assets, list) or sum(asset["id"] == asset_id for asset in assets) != 1:
            raise AssertionError("Deletion must identify exactly one remote asset.")

        self.delete_calls.append((repository, asset_id))
        self.release["assets"] = [asset for asset in assets if asset["id"] != asset_id]
        del self.payloads[asset_id]

    def get_latest(self, repository: str) -> dict[str, object] | None:
        """Read the complete Latest identity or reproduce a remote failure."""
        del repository
        self.latest_reads += 1
        if self.latest_error is not None:
            raise self.latest_error

        if self.latest:
            return copy.deepcopy(self.release)

        return copy.deepcopy(self.latest_release)

    def resolve_tag(self, repository: str, tag: str) -> str:
        """Resolve only explicitly seeded immutable tag identities."""
        del repository

        return self.tag_commits[tag]

    def download_asset(self, repository: str, asset_id: int) -> bytes:
        """Return the exact stored payload for digest readback."""
        del repository
        return self.payloads[asset_id]

    def is_latest(self, repository: str, release_id: int) -> bool:
        """Return the configured canonical latest-release state."""
        del repository, release_id
        return self.latest

    def add_asset(self, asset: dict[str, object], payload: bytes) -> None:
        """Seed one remote asset in an existing draft or release."""
        if self.release is None:
            raise AssertionError("A release must exist before seeding an asset.")

        assets = self.release["assets"]
        if not isinstance(assets, list):
            raise AssertionError("The fake release asset inventory is invalid.")

        asset_id = self._next_asset_id
        self._next_asset_id += 1
        self.payloads[asset_id] = payload
        assets.append(
            {
                "id": asset_id,
                "name": asset["name"],
                "state": "uploaded",
                "size": len(payload),
            }
        )


class MonotonicLatestTests(unittest.TestCase):
    """Keep stable publication ordered without coupling immutable retries to Latest."""

    def setUp(self) -> None:
        """Create a small historical plan with one checksum-bound asset."""
        self.temporary_directory = tempfile.TemporaryDirectory()
        asset = Path(self.temporary_directory.name) / "evidence.json"
        asset.write_bytes(b"verified identity\n")
        self.plan = {
            "schemaVersion": 3,
            "phase": "staged",
            "repository": "doka-labs/Doka.EntityFrameworkCore.MySql",
            "releaseTag": "v10.6.2",
            "releaseVersion": "10.6.2",
            "sourceCommit": "a" * 40,
            "name": "Doka.EntityFrameworkCore.MySql 10.6.2",
            "prerelease": False,
            "latest": True,
            "notes": "Verified release.",
            "notesSha256": github_release.sha256_bytes(b"Verified release."),
            "assets": [github_release.asset_record(asset)],
        }

    def tearDown(self) -> None:
        """Remove the isolated fixture without touching repository state."""
        self.temporary_directory.cleanup()

    def _newer_latest(self, client: FakeReleaseClient, version: str) -> None:
        """Seed one independently published stable release identity."""
        client.latest_release = {
            "id": 900,
            "tag_name": f"v{version}",
            "draft": False,
            "prerelease": False,
            "immutable": True,
        }
        client.tag_commits[f"v{version}"] = "b" * 40

    def test_older_stable_finishes_without_demoting_newer_latest(self) -> None:
        """An out-of-order 10.x completion cannot replace stable 11.x."""
        # Arrange
        client = FakeReleaseClient()
        self._newer_latest(client, "11.0.0")

        # Act
        receipt = github_release.finalize_release(self.plan, client)

        # Assert
        self.assertEqual([False], client.published_latest)
        self.assertFalse(receipt["latest"])
        self.assertEqual("v11.0.0", client.latest_release["tag_name"])

    def test_newer_stable_replaces_older_latest(self) -> None:
        """Canonical SemVer compares numeric components, not lexical strings."""
        # Arrange
        client = FakeReleaseClient()
        self._newer_latest(client, "10.6.1")

        # Act
        receipt = github_release.finalize_release(self.plan, client)

        # Assert
        self.assertEqual([True], client.published_latest)
        self.assertTrue(receipt["latest"])

    def test_historical_immutable_retry_neither_reads_nor_changes_latest(self) -> None:
        """A completed release remains verifiable after a newer publication."""
        # Arrange
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.upload_assets(
            str(self.plan["repository"]),
            str(self.plan["releaseTag"]),
            [Path(self.plan["assets"][0]["path"])],
        )
        client.publish_release(self.plan)
        client.latest = False
        self._newer_latest(client, "11.0.0")
        client.latest_error = github_release.GitHubReleaseError("network unavailable")
        client.publish_calls = 0

        # Act
        receipt = github_release.finalize_release(self.plan, client)

        # Assert
        self.assertEqual("published-and-verified", receipt["status"])
        self.assertEqual(0, client.publish_calls)
        self.assertEqual(0, client.latest_reads)

    def test_unknown_latest_identity_blocks_publication(self) -> None:
        """Do not replace an unparsable or noncanonical current Latest."""
        # Arrange
        client = FakeReleaseClient()
        self._newer_latest(client, "unknown")

        # Act
        failure = _record_exception(lambda: github_release.finalize_release(self.plan, client))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("not canonical", str(failure))
        self.assertEqual(0, client.publish_calls)

    def test_latest_network_failure_blocks_publication(self) -> None:
        """An unavailable Latest endpoint is not evidence of absence."""
        # Arrange
        client = FakeReleaseClient()
        client.latest_error = github_release.GitHubReleaseError("network unavailable")

        # Act
        failure = _record_exception(lambda: github_release.finalize_release(self.plan, client))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("network", str(failure))
        self.assertEqual(0, client.publish_calls)

    def test_equal_version_conflicting_commit_blocks_publication(self) -> None:
        """The same canonical version cannot identify a different source tree."""
        # Arrange
        client = FakeReleaseClient()
        self._newer_latest(client, str(self.plan["releaseVersion"]))

        # Act
        failure = _record_exception(lambda: github_release.finalize_release(self.plan, client))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("identity", str(failure))
        self.assertEqual(0, client.publish_calls)

    def test_latest_is_read_after_draft_upload_not_from_a_stale_plan(self) -> None:
        """A release published while this draft was staging wins the comparison."""
        # Arrange
        client = FakeReleaseClient()
        upload = client.upload_assets

        def concurrent_publication(repository: str, tag: str, paths: list[Path]) -> None:
            """Model a completed newer release before this serialized write."""
            upload(repository, tag, paths)
            self._newer_latest(client, "11.0.0")

        # Act
        with mock.patch.object(client, "upload_assets", side_effect=concurrent_publication):
            receipt = github_release.finalize_release(self.plan, client)

        # Assert
        self.assertEqual(1, client.latest_reads)
        self.assertEqual([False], client.published_latest)
        self.assertFalse(receipt["latest"])

    def test_first_stable_release_becomes_latest(self) -> None:
        """Only an explicit absent Latest response permits initial promotion."""
        # Arrange
        client = FakeReleaseClient()

        # Act
        receipt = github_release.finalize_release(self.plan, client)

        # Assert
        self.assertEqual([True], client.published_latest)
        self.assertTrue(receipt["latest"])

    def test_preview_never_displaces_current_stable_latest(self) -> None:
        """Stable 10.x remains Latest-eligible while 11.x is only prerelease."""
        # Arrange
        client = FakeReleaseClient()
        self._newer_latest(client, "10.6.2")
        plan = {
            **self.plan,
            "releaseTag": "v11.0.0-rc.1",
            "releaseVersion": "11.0.0-rc.1",
            "name": "Doka.EntityFrameworkCore.MySql 11.0.0-rc.1",
            "prerelease": True,
            "latest": False,
        }

        # Act
        receipt = github_release.finalize_release(plan, client)

        # Assert
        self.assertEqual([False], client.published_latest)
        self.assertFalse(receipt["latest"])
        self.assertEqual("v10.6.2", client.latest_release["tag_name"])
        self.assertEqual(1, client.latest_reads)

    def test_historical_stage_does_not_repromote_an_older_release(self) -> None:
        """Draft reconciliation remains a no-op for a fully published older tag."""
        # Arrange
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.upload_assets(
            str(self.plan["repository"]), str(self.plan["releaseTag"]), [Path(self.plan["assets"][0]["path"])]
        )
        client.publish_release(self.plan)
        client.latest = False
        self._newer_latest(client, "11.0.0")
        client.latest_error = github_release.GitHubReleaseError("network unavailable")
        client.publish_calls = 0

        # Act
        receipt = github_release.stage_release(self.plan, client)

        # Assert
        self.assertEqual("release-already-published", receipt["status"])
        self.assertEqual(0, client.latest_reads)
        self.assertEqual(0, client.publish_calls)

    def test_latest_endpoint_accepts_only_explicit_not_found_as_absent(self) -> None:
        """Transport errors cannot silently trigger an initial Latest promotion."""
        # Arrange
        client = github_release.GitHubCliClient()

        # Act
        with mock.patch.object(client, "_run_text", side_effect=github_release.GitHubReleaseError("HTTP 503")):
            failure = _record_exception(lambda: client.get_latest(str(self.plan["repository"])))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("503", str(failure))

    def test_latest_endpoint_not_found_is_absent(self) -> None:
        """The protocol recognizes repositories with no stable release yet."""
        # Arrange
        client = github_release.GitHubCliClient()

        # Act
        with mock.patch.object(client, "_run_text", return_value=None):
            latest = client.get_latest(str(self.plan["repository"]))

        # Assert
        self.assertIsNone(latest)

    def test_latest_endpoint_rejects_nonobject_json(self) -> None:
        """A malformed successful API response is not an absent release."""
        # Arrange
        client = github_release.GitHubCliClient()

        # Act
        with mock.patch.object(client, "_run_text", return_value="[]"):
            failure = _record_exception(lambda: client.get_latest(str(self.plan["repository"])))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("invalid", str(failure))


class GitHubReleaseTests(unittest.TestCase):
    """Exercise planning, recovery, publication, and remote conflict gates."""

    _REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"
    _VERSION = "10.0.0-rc.1"
    _TAG = f"v{_VERSION}"
    _PACKAGE_BASE_ADDRESS = "https://packages.example.test/v3-flatcontainer"
    _COMMIT = "a" * 40

    @staticmethod
    def _zip_payload(name: str, payload: bytes) -> bytes:
        """Build a deterministic package-shaped ZIP for digest validation."""
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as package:
            package.writestr(name, payload)
        return buffer.getvalue()

    @staticmethod
    def _repository_signed_payload(payload: bytes) -> bytes:
        """Add the NuGet-owned signature entry to a package fixture."""
        buffer = io.BytesIO()
        with (
            zipfile.ZipFile(io.BytesIO(payload)) as source,
            zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as destination,
        ):
            for entry in source.infolist():
                destination.writestr(entry.filename, source.read(entry))
            destination.writestr(
                nuget_publication.NUGET_SIGNATURE_ENTRY,
                b"repository signature",
            )
        return buffer.getvalue()

    def setUp(self) -> None:
        """Create one checksum-bound candidate and publication receipt set."""
        # WHY: These fixtures model their own workflow run. Ambient CI run
        # identities must not relabel them as the test runner's publication.
        environment = mock.patch.dict(os.environ, {}, clear=True)
        environment.start()
        self.addCleanup(environment.stop)
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.candidate = self.root / "github-12345"
        self.publication = self.root / "publication-evidence"
        self.changelog = self.root / "CHANGELOG.md"
        self.candidate.mkdir()
        self.publication.mkdir()

        files: dict[str, tuple[bytes, str]] = {}
        package_payloads: dict[str, bytes] = {}
        package_map = nuget_publication.package_paths(
            self.candidate,
            self._VERSION,
        )
        for role, package in package_map.items():
            primary_payload = self._zip_payload(
                f"payload/{role}.txt",
                f"{role} primary package\n".encode("ascii"),
            )
            symbols_payload = self._zip_payload(
                f"payload/{role}.pdb",
                b"BSJB candidate symbols\n",
            )
            files[package["package"].relative_to(self.candidate).as_posix()] = (
                primary_payload,
                "package",
            )
            files[package["symbols"].relative_to(self.candidate).as_posix()] = (
                symbols_payload,
                "symbol-package",
            )
            package_payloads[role] = primary_payload

        files.update(
            {
                "release-candidate-summary.md": (b"# Candidate summary\n", "evidence"),
                "release-candidate-reconciliation.json": (
                    b'{"status":"pass"}\n',
                    "evidence",
                ),
                "release-qualification-manifest.json": (
                    b'{"kind":"release-qualification-manifest"}\n',
                    "evidence",
                ),
                "resolved-packages.json": (b'{"packages":[]}\n', "evidence"),
                "local-package-consumer/local-package-consumer.json": (
                    b'{"runtimeSmoke":"pass"}\n',
                    "evidence",
                ),
                "local-package-consumer/local-package-runtime.json": (
                    (
                        json.dumps(
                            {
                                "schemaVersion": 2,
                                "kind": "local-package-runtime-qualification",
                                "releaseTag": self._TAG,
                                "releaseVersion": self._VERSION,
                                "sourceCommit": self._COMMIT,
                                "engineImage": "mysql:8.4.6",
                                "consumerBoundary": "isolated-local-package",
                                "projectReferences": 0,
                                "runtimeSmoke": "pass",
                                "cacheRuntimeSmoke": "pass",
                                "cacheEfCoreDependencies": 0,
                            }
                        )
                        + "\n"
                    ).encode("ascii"),
                    "evidence",
                ),
                "sbom/provider.cdx.json": (b'{"bomFormat":"CycloneDX"}\n', "sbom"),
            }
        )
        # WHY: Historical GA-10 evidence retains its floor/latest receipts
        # even when this test runs on an upstream RC preparation branch.
        efcore_packages = (
            "Microsoft.EntityFrameworkCore.Design",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.EntityFrameworkCore.Relational.Specification.Tests",
        )
        efcore_legs = {
            "minimum-10-0-8": {
                "requestedVersion": "10.0.8",
                "resolvedVersion": "10.0.8",
                "validationScope": "dependency-graph",
                "qualificationSource": "repository-qualification",
                "specificationTargets": [],
                "integrationTargets": [],
                "contracts": [
                    "resolved-package-graph",
                    "version-contract-preflight",
                ],
            },
            "latest-10-0": {
                "requestedVersion": "10.0.12",
                "resolvedVersion": "10.0.12",
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
        for leg, receipt in efcore_legs.items():
            receipt["schemaVersion"] = 2
            receipt.setdefault("results", {"dependencies": "resolved-packages.json"})
            prefix = f"efcore-patch-matrix/{leg}"
            files[f"{prefix}/efcore-contract-evidence.json"] = (
                (json.dumps(receipt) + "\n").encode("ascii"),
                "evidence",
            )
            graph = {
                "projects": [
                    {
                        "frameworks": [
                            {
                                "topLevelPackages": [
                                    {
                                        "id": package,
                                        "resolvedVersion": receipt["resolvedVersion"],
                                    }
                                    for package in efcore_packages
                                ]
                            }
                        ]
                    }
                ]
            }
            files[f"{prefix}/resolved-packages.json"] = (
                (json.dumps(graph) + "\n").encode("ascii"),
                "evidence",
            )
            if leg == "latest-10-0":
                retained_targets = []
                for target in ("mysql84", "mariadb118"):
                    target_evidence = {
                        "targetId": target,
                        "engine": "MySql" if target == "mysql84" else "MariaDb",
                    }
                    retained_targets.append(target_evidence)
                    files[f"{prefix}/{target}/results.trx"] = (
                        b"<TestRun><ResultSummary>"
                        b'<Counters total="1" failed="0" />'
                        b"</ResultSummary></TestRun>",
                        "evidence",
                    )
                    files[f"{prefix}/{target}/test-database-evidence.json"] = (
                        (
                            json.dumps(
                                {
                                    "lifecycleState": "cleanup-completed",
                                    "targets": [target_evidence],
                                }
                            )
                            + "\n"
                        ).encode("ascii"),
                        "evidence",
                    )
                files[
                    f"{prefix}/integration/compatibility-matrix-evidence.json"
                ] = (
                    (
                        json.dumps(
                            {
                                "mode": "testcontainers",
                                "targetSelection": "mysql84,mariadb118",
                                "testFilter": "",
                                "testExitCode": 0,
                                "testDatabase": {
                                    "lifecycleState": "cleanup-completed",
                                    "targets": retained_targets,
                                },
                            }
                        )
                        + "\n"
                    ).encode("ascii"),
                    "evidence",
                )

        artifacts: list[dict[str, object]] = []
        for relative, (payload, role) in sorted(files.items()):
            path = self.candidate / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(payload)
            artifacts.append(
                {
                    "path": relative,
                    "role": role,
                    "sha256": hashlib.sha256(payload).hexdigest(),
                    "sizeBytes": len(payload),
                }
            )

        manifest = {
            "schemaVersion": 3,
            "releaseCandidateRunId": self.candidate.name,
            "releaseVersion": self._VERSION,
            "expectedReleaseTag": self._TAG,
            "source": {
                "repository": self._REPOSITORY,
                "ref": "refs/heads/main",
                "tag": None,
                "commit": self._COMMIT,
            },
            "workflow": {
                "provider": "github-actions",
                "runId": "12345",
                "runAttempt": "1",
                "workflow": nuget_publication.CANDIDATE_WORKFLOW,
                "workflowRef": (
                    f"{self._REPOSITORY}/"
                    f"{nuget_publication.CANDIDATE_WORKFLOW_PATH}"
                    "@refs/heads/main"
                ),
                "repository": self._REPOSITORY,
            },
            "toolchain": {
                "approvedDotnetSdk": "10.0.302",
                "dotnetSdk": "10.0.302",
            },
            "qualification": {
                "gates": ["repository-qualification"],
            },
            "artifacts": artifacts,
        }
        manifest_path = self.candidate / release_evidence.MANIFEST_NAME
        manifest_path.write_text(
            json.dumps(manifest, indent=2) + "\n",
            encoding="utf-8",
        )
        checksum = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
        (self.candidate / release_evidence.CHECKSUM_NAME).write_text(
            f"{checksum}  {release_evidence.MANIFEST_NAME}\n",
            encoding="ascii",
        )

        receipt_packages: dict[str, dict[str, str]] = {}
        for role, package_id in github_release.PACKAGE_IDENTITIES:
            paths = package_map[role]
            receipt_packages[role] = {
                "id": package_id,
                "package": paths["package"].relative_to(self.candidate).as_posix(),
                "symbols": paths["symbols"].relative_to(self.candidate).as_posix(),
                "contentDigest": nuget_publication.canonical_package_digest(
                    paths["package"]
                ),
                "symbolsSha256": nuget_publication.sha256_file(paths["symbols"]),
            }

        candidate_receipt: dict[str, object] = {
            "schemaVersion": 1,
            "kind": nuget_publication.CANDIDATE_RECEIPT_KIND,
            "candidateRunId": "12345",
            "candidateRunAttempt": "1",
            "repository": self._REPOSITORY,
            "expectedReleaseTag": self._TAG,
            "releaseVersion": self._VERSION,
            "sourceCommit": self._COMMIT,
            "sourceRef": "refs/heads/main",
            "releaseCandidateRunId": self.candidate.name,
            "mysql84Image": "mysql:8.4.6",
            "packages": receipt_packages,
        }
        candidate_receipt_path = self.publication / "candidate-receipt.json"
        candidate_receipt_path.write_text(
            json.dumps(candidate_receipt) + "\n",
            encoding="utf-8",
        )
        trust_receipt: dict[str, object] = {
            "schemaVersion": 2,
            "kind": "release-tag-trust-root",
            "repository": self._REPOSITORY,
            "tag": self._TAG,
            "commit": self._COMMIT,
            "policyDigest": "d" * 64,
            "qualification": {"commit": self._COMMIT},
        }
        trust_receipt_path = self.publication / "release-tag-trust-root.json"
        trust_receipt_path.write_text(
            json.dumps(trust_receipt) + "\n",
            encoding="utf-8",
        )
        qualification_path = self.candidate / "release-qualification-manifest.json"
        receipt: dict[str, object] = {
            **candidate_receipt,
            "schemaVersion": 4,
            "kind": nuget_publication.PUBLICATION_RECEIPT_KIND,
            "releaseTag": self._TAG,
            "candidateReceiptSha256": nuget_publication.sha256_file(
                candidate_receipt_path
            ),
            "tagTrustRootSha256": nuget_publication.sha256_file(
                trust_receipt_path
            ),
            "qualificationManifestSha256": nuget_publication.sha256_file(
                qualification_path
            ),
        }

        symbols: list[dict[str, str]] = []
        symbol_payloads: dict[str, bytes] = {}
        for index, (_, package_id) in enumerate(
            github_release.PACKAGE_IDENTITIES,
            start=1,
        ):
            pdb_name = f"{package_id}.pdb"
            payload = b"BSJB" + f" public symbols {package_id}\n".encode("ascii")
            digest = hashlib.sha256(payload).hexdigest()
            symbol_key = f"{index:032x}FFFFFFFF"
            symbols.append(
                {
                    "packageId": package_id,
                    "packageVersion": self._VERSION,
                    "pdbName": pdb_name,
                    "symbolKey": symbol_key,
                    "symbolUrl": (
                        f"{nuget_publication.NUGET_SYMBOL_SERVER}/{pdb_name}/"
                        f"{symbol_key}/{pdb_name}"
                    ),
                    "checksumHeader": f"SHA256:{digest}",
                    "sha256": digest,
                }
            )
            symbol_payloads[package_id] = payload

        symbol_entries = {entry["packageId"]: entry for entry in symbols}
        preflight_packages: dict[str, dict[str, object]] = {}
        readback_packages: dict[str, dict[str, object]] = {}
        preflight_symbols: dict[str, dict[str, str]] = {}
        readback_symbols: dict[str, dict[str, str]] = {}
        public_packages = self.publication / "packages"
        public_symbols = public_packages / "symbols"
        public_symbols.mkdir(parents=True)
        for role, package_id in github_release.PACKAGE_IDENTITIES:
            candidate_digest = receipt_packages[role]["contentDigest"]
            package_url = nuget_publication.remote_package_url(
                self._PACKAGE_BASE_ADDRESS,
                package_id,
                self._VERSION,
            )
            preflight_packages[role] = {
                "id": package_id,
                "status": "absent",
                "url": package_url,
                "candidateContentDigest": candidate_digest,
            }

            package_path = public_packages / nuget_publication.package_file_name(
                package_id,
                self._VERSION,
                "nupkg",
            )
            public_package_payload = self._repository_signed_payload(
                package_payloads[role]
            )
            package_path.write_bytes(public_package_payload)
            readback_packages[role] = {
                "id": package_id,
                "status": "matching",
                "url": package_url,
                "candidateContentDigest": candidate_digest,
                "publishedContentDigest": candidate_digest,
                "publishedSha256": hashlib.sha256(
                    public_package_payload
                ).hexdigest(),
                "repositorySignaturePresent": True,
                "readbackPath": str(package_path),
            }

            symbol_entry = symbol_entries[package_id]
            preflight_symbols[package_id] = {
                "pdbName": symbol_entry["pdbName"],
                "status": "absent",
                "url": symbol_entry["symbolUrl"],
                "candidateSha256": symbol_entry["sha256"],
            }
            symbol_path = public_symbols / symbol_entry["pdbName"]
            symbol_path.write_bytes(symbol_payloads[package_id])
            readback_symbols[package_id] = {
                "pdbName": symbol_entry["pdbName"],
                "status": "matching",
                "url": symbol_entry["symbolUrl"],
                "candidateSha256": symbol_entry["sha256"],
                "publishedSha256": symbol_entry["sha256"],
                "readbackPath": str(symbol_path),
            }

        evidence: dict[str, dict[str, object]] = {
            "release-publication-receipt.json": receipt,
            "candidate-publication-preflight.json": {
                "schemaVersion": nuget_publication.SCHEMA_VERSION,
                "checkedUtc": "2026-08-04T09:50:00+00:00",
                "expectedReleaseTag": self._TAG,
                "releaseVersion": self._VERSION,
                "sourceCommit": self._COMMIT,
                "packageSource": nuget_publication.NUGET_SOURCE,
                "packageBaseAddress": self._PACKAGE_BASE_ADDRESS,
                "packages": preflight_packages,
                "symbols": preflight_symbols,
            },
            "publication-preflight.json": {
                "schemaVersion": nuget_publication.SCHEMA_VERSION,
                "checkedUtc": "2026-08-04T10:00:00+00:00",
                "releaseTag": self._TAG,
                "expectedReleaseTag": self._TAG,
                "releaseVersion": self._VERSION,
                "sourceCommit": self._COMMIT,
                "packageSource": nuget_publication.NUGET_SOURCE,
                "packageBaseAddress": self._PACKAGE_BASE_ADDRESS,
                "packages": preflight_packages,
                "symbols": preflight_symbols,
            },
            "symbol-readback-manifest.json": {
                "schemaVersion": nuget_publication.SYMBOL_MANIFEST_SCHEMA_VERSION,
                "releaseVersion": self._VERSION,
                "symbols": symbols,
            },
            "nuget-publication-readback.json": {
                "schemaVersion": nuget_publication.SCHEMA_VERSION,
                "verifiedUtc": "2026-08-04T10:10:00+00:00",
                "releaseTag": self._TAG,
                "expectedReleaseTag": self._TAG,
                "releaseVersion": self._VERSION,
                "sourceCommit": self._COMMIT,
                "packageSource": nuget_publication.NUGET_SOURCE,
                "packageBaseAddress": self._PACKAGE_BASE_ADDRESS,
                "packages": readback_packages,
                "symbols": readback_symbols,
            },
        }
        for name, value in evidence.items():
            (self.publication / name).write_text(
                json.dumps(value) + "\n",
                encoding="utf-8",
            )

        provenance_subjects = [
            *(path for package in package_map.values() for path in package.values()),
            self.candidate / release_evidence.MANIFEST_NAME,
            self.candidate / release_evidence.CHECKSUM_NAME,
            self.publication / "candidate-receipt.json",
            self.publication / "candidate-publication-preflight.json",
            self.publication / "symbol-readback-manifest.json",
        ]
        statement = {
            "_type": release_provenance.IN_TOTO_STATEMENT_TYPE,
            "subject": [
                {
                    "name": subject.name,
                    "digest": {
                        "sha256": hashlib.sha256(subject.read_bytes()).hexdigest()
                    },
                }
                for subject in provenance_subjects
            ],
            "predicateType": release_provenance.SLSA_PROVENANCE_TYPE,
            "predicate": {"buildDefinition": {}, "runDetails": {}},
        }
        bundle = {
            "mediaType": release_provenance.SIGSTORE_BUNDLE_MEDIA_TYPE,
            "verificationMaterial": {"certificate": {"rawBytes": "AA=="}},
            "dsseEnvelope": {
                "payloadType": release_provenance.IN_TOTO_PAYLOAD_TYPE,
                "payload": base64.b64encode(
                    json.dumps(statement, separators=(",", ":")).encode("utf-8")
                ).decode("ascii"),
                "signatures": [{"sig": "AA=="}],
            },
        }
        (self.publication / release_provenance.PORTABLE_PROVENANCE_NAME).write_text(
            json.dumps(bundle, separators=(",", ":")) + "\n",
            encoding="utf-8",
        )
        (self.publication / "nuget-signature-verification.txt").write_text(
            "Successfully verified both NuGet.org repository signatures.\n",
            encoding="utf-8",
        )

        self.changelog.write_text(
            "# Changelog\n\n"
            "## [Unreleased]\n\n"
            "## [10.0.0-rc.1] - 2026-08-03\n\n"
            "### Added\n\n"
            "- Release notes.\n\n"
            "## [9.0.0] - 2025-01-01\n\n"
            "- Previous release.\n",
            encoding="utf-8",
        )
        self.plan = github_release.build_release_plan(
            self._REPOSITORY,
            self.candidate,
            self.publication,
            self.changelog,
        )
        self.staged_plan = self.plan
        (self.publication / "github-release-readback.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 3,
                    "status": "published-and-verified",
                    "repository": self._REPOSITORY,
                    "releaseId": 1,
                    "releaseTag": self._TAG,
                    "releaseVersion": self._VERSION,
                    "sourceCommit": self._COMMIT,
                    "releaseUrl": (
                        f"https://github.com/{self._REPOSITORY}/releases/tag/"
                        f"{self._TAG}"
                    ),
                    "publishedAt": "2026-08-04T10:05:00+00:00",
                    "immutable": True,
                    "prerelease": True,
                    "latest": False,
                    "notesSha256": self.plan["notesSha256"],
                    "assets": [
                        {
                            "name": asset["name"],
                            "assetId": index,
                            "sizeBytes": asset["sizeBytes"],
                            "sha256": asset["sha256"],
                        }
                        for index, asset in enumerate(
                            self.plan["assets"],
                            start=1,
                        )
                    ],
                }
            )
            + "\n",
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        """Remove the isolated candidate fixture."""
        self.temporary_directory.cleanup()

    def _enable_release_line_receipts(self) -> dict[str, object]:
        """Upgrade fixture evidence through the real new-schema readers."""
        policy = initial_policy()
        line = release_lines.freeze_line(policy, "refs/heads/main", self._VERSION)
        manifest_path = self.candidate / release_evidence.MANIFEST_NAME
        manifest = github_release.read_json(manifest_path, "fixture manifest")
        manifest["schemaVersion"] = 4
        manifest["releaseLine"] = line
        manifest["workflow"]["signerWorkflowRef"] = manifest["workflow"]["workflowRef"]
        manifest["workflow"]["signerDigest"] = self._COMMIT
        manifest["toolchain"].update({
            "targetFramework": "net10.0",
            "efCoreVersion": "[10.0.8, 10.1.0)",
            "testRunner": "VSTest",
            "upstreamStage": "ga",
            "resolvedPackages": {
                "Microsoft.EntityFrameworkCore.Design": "10.0.12",
                "Microsoft.EntityFrameworkCore.Relational": "10.0.12",
                "MySqlConnector": "2.5.0",
            },
        })
        # WHY: Schema4 binds the observed toolchain to the sealed package-list
        # artifact; upgrading only the manifest would invent a different graph.
        graph_path = self.candidate / "resolved-packages.json"
        github_release.write_json(graph_path, {"projects": [{"frameworks": [{
            "topLevelPackages": [
                {"id": package, "resolvedVersion": version}
                for package, version in manifest["toolchain"]["resolvedPackages"].items()
            ],
        }]}]})
        qualification_path = self.candidate / "release-qualification-manifest.json"
        github_release.write_json(qualification_path, {
            "schemaVersion": 3,
            "kind": "release-qualification-manifest",
            "releaseLine": line,
            "commit": self._COMMIT,
            "releaseVersion": self._VERSION,
            "expectedReleaseTag": self._TAG,
            "policyVersion": "fixture",
            "policyDigest": "d" * 64,
            "selectionRuleVersion": 1,
            "treeId": "c" * 40,
            "gates": [{"gate": "repository-qualification"}],
        })
        for artifact in manifest["artifacts"]:
            if artifact["path"] in {qualification_path.name, graph_path.name}:
                path = self.candidate / artifact["path"]
                artifact["sizeBytes"] = path.stat().st_size
                artifact["sha256"] = github_release.sha256_file(path)

        github_release.write_json(manifest_path, manifest)
        (self.candidate / release_evidence.CHECKSUM_NAME).write_text(
            f"{github_release.sha256_file(manifest_path)}  {release_evidence.MANIFEST_NAME}\n",
            encoding="ascii",
        )
        candidate_path = self.publication / "candidate-receipt.json"
        candidate = github_release.read_json(candidate_path, "fixture candidate")
        candidate.update({
            "schemaVersion": 2,
            "releaseLine": line,
            "workflowRef": manifest["workflow"]["workflowRef"],
            "toolchain": manifest["toolchain"],
        })
        github_release.write_json(candidate_path, candidate)
        trust_path = self.publication / "release-tag-trust-root.json"
        trust = github_release.read_json(trust_path, "fixture trust")
        trust.update({"schemaVersion": 3, "releaseLine": line})
        github_release.write_json(trust_path, trust)
        authority = {
            "schemaVersion": 1,
            "kind": release_publication.AUTHORITY_KIND,
            "repository": self._REPOSITORY,
            "controlCommit": "b" * 40,
            "authorizationRevision": policy["revision"],
            "authorizationDigest": release_lines.policy_digest(policy),
            "observedUtc": "2026-10-22T12:00:00Z",
            "policy": policy,
        }
        admission = release_publication.create_admission(
            release_publication.candidate_identity(self.candidate, candidate_path, "1"),
            authority,
        )
        receipt_path = self.publication / "release-publication-receipt.json"
        receipt = github_release.read_json(receipt_path, "fixture publication")
        receipt.update({
            **candidate,
            "schemaVersion": 5,
            "kind": nuget_publication.PUBLICATION_RECEIPT_KIND,
            "candidateReceiptSha256": github_release.sha256_file(candidate_path),
            "tagTrustRootSha256": github_release.sha256_file(trust_path),
            "qualificationManifestSha256": github_release.sha256_file(qualification_path),
            "publicationAdmission": admission,
            "publicationAuthority": authority,
        })
        github_release.write_json(receipt_path, receipt)
        bundle_path = self.publication / release_provenance.PORTABLE_PROVENANCE_NAME
        bundle = github_release.read_json(bundle_path, "fixture provenance")
        statement = json.loads(base64.b64decode(bundle["dsseEnvelope"]["payload"]))
        subjects = {
            path.name: path
            for directory in (self.candidate, self.publication)
            for path in directory.rglob("*")
            if path.is_file()
        }
        # WHY: Published repository-signed packages share filenames with the
        # unsigned candidate; provenance binds the candidate bytes only.
        for path in (self.candidate / "packages").iterdir():
            subjects[path.name] = path

        for subject in statement["subject"]:
            subject["digest"]["sha256"] = github_release.sha256_file(subjects[subject["name"]])

        statement["predicate"] = {
            "buildDefinition": {
                "externalParameters": {"workflow": {
                    "repository": f"https://github.com/{self._REPOSITORY}",
                    "path": line["workflowPath"],
                    "ref": line["sourceRef"],
                }},
                "resolvedDependencies": [{
                    "uri": f"git+https://github.com/{self._REPOSITORY}@{line['sourceRef']}",
                    "digest": {"gitCommit": self._COMMIT},
                }],
            },
            "runDetails": {"metadata": {
                "invocationId": (
                    f"https://github.com/{self._REPOSITORY}/actions/runs/"
                    f"{candidate['candidateRunId']}/attempts/{candidate['candidateRunAttempt']}"
                ),
            }},
        }
        bundle["dsseEnvelope"]["payload"] = base64.b64encode(
            json.dumps(statement, separators=(",", ":")).encode("ascii")
        ).decode("ascii")
        bundle_path.write_text(json.dumps(bundle, separators=(",", ":")) + "\n", encoding="ascii")
        github_release.write_json(self.publication / "publication-admission.json", admission)
        github_release.write_json(self.publication / "authorization-observation.json", authority)

        return github_release.build_release_plan(self._REPOSITORY, self.candidate, self.publication, self.changelog)

    def test_release_line_plan_and_receipt_keep_frozen_publication_identity(self) -> None:
        """The new schema publishes the exact candidate without promoting a prerelease."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()
        manifest_before = (self.candidate / release_evidence.MANIFEST_NAME).read_bytes()
        environment = {"DOKA_PUBLICATION_LOCK_GROUP": "doka-publication", "DOKA_PUBLICATION_LOCK_HELD": "1"}
        authority = copy.deepcopy(plan["publicationAuthority"])
        authority["policy"]["revision"] = "2026-10-22.2"
        authority["authorizationRevision"] = authority["policy"]["revision"]
        authority["authorizationDigest"] = release_lines.policy_digest(authority["policy"])
        authority["controlCommit"] = "c" * 40

        # Act
        with mock.patch.dict(os.environ, environment):
            receipt = github_release.finalize_release(plan, client, authority=authority)

        # Assert
        github_release.validate_release_receipt(receipt, plan)
        self.assertEqual(4, receipt["schemaVersion"])
        self.assertEqual("never", plan["promotionIntent"])
        self.assertEqual("not-promoted", receipt["latestDecision"])
        self.assertEqual([False], client.published_latest)
        self.assertEqual(authority, receipt["promotion"]["authorization"])
        self.assertNotEqual(
            plan["publicationAuthority"]["authorizationRevision"],
            receipt["promotion"]["authorization"]["authorizationRevision"],
        )
        self.assertEqual(manifest_before, (self.candidate / release_evidence.MANIFEST_NAME).read_bytes())

    def test_release_line_stage_only_uploads_and_never_publishes(self) -> None:
        """A complete immutable-intent draft is staged before any package write."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()
        environment = {"DOKA_PUBLICATION_LOCK_GROUP": "doka-publication", "DOKA_PUBLICATION_LOCK_HELD": "1"}

        # Act
        with mock.patch.dict(os.environ, environment):
            receipt = github_release.stage_release(plan, client)

        # Assert
        self.assertEqual("draft-staged-and-verified", receipt["status"])
        self.assertEqual(0, client.publish_calls)
        self.assertTrue(client.release["draft"])

    def test_release_line_plan_rejects_role_and_schema_downgrades(self) -> None:
        """No plan field can substitute a different release authorization route."""
        # Arrange
        plan = self._enable_release_line_receipts()
        variants = (
            {**plan, "sourceRef": "refs/heads/maintenance/10.x"},
            {**plan, "schemaVersion": 3},
            {**plan, "promotionIntent": "monotonic-stable"},
            {**plan, "latest": 0},
            {**plan, "prerelease": 1},
        )

        # Act
        failures = [_record_exception(lambda: github_release.validate_plan(variant)) for variant in variants]

        # Assert
        for variant, failure in zip(variants, failures, strict=True):
            with self.subTest(source=variant["sourceRef"], schema=variant["schemaVersion"]):
                self.assertIsInstance(failure, github_release.GitHubReleaseError)

    def test_release_line_publication_without_shared_lock_is_rejected(self) -> None:
        """Branch-local success cannot bypass cross-line write serialization."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()

        # Act
        with mock.patch.dict(os.environ, {}, clear=True):
            failure = _record_exception(lambda: github_release.finalize_release(plan, client))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("shared publisher lock", str(failure))
        self.assertEqual(0, client.create_calls)

    def test_new_schema_readback_cannot_change_frozen_promotion_intent(self) -> None:
        """A valid remote byte inventory does not authorize preview Latest."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()
        client.create_draft(plan)
        client.upload_assets(self._REPOSITORY, self._TAG, [Path(asset["path"]) for asset in plan["assets"]])
        client.publish_release(plan)
        receipt = github_release.build_receipt(client.release, plan, False, promotion={
            "schemaVersion": 1,
            "observedLatest": None,
            "comparison": None,
            "disposition": "not-promoted",
            "authorization": plan["publicationAuthority"],
        })
        receipt["latest"] = True
        receipt["latestDecision"] = "promoted"

        # Act
        failure = _record_exception(lambda: github_release.validate_release_receipt(receipt, plan))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("identity", str(failure))

    def test_release_line_retry_records_no_new_promotion_or_latest_observation(self) -> None:
        """An exact immutable retry records verification rather than a new promotion."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()
        client.create_draft(plan)
        client.upload_assets(self._REPOSITORY, self._TAG, [Path(asset["path"]) for asset in plan["assets"]])
        client.publish_release(plan)
        client.publish_calls = 0
        before = copy.deepcopy(client.release)
        environment = {"DOKA_PUBLICATION_LOCK_GROUP": "doka-publication", "DOKA_PUBLICATION_LOCK_HELD": "1"}

        # Act
        with mock.patch.dict(os.environ, environment):
            receipt = github_release.finalize_release(plan, client, authority=plan["publicationAuthority"])

        # Assert
        github_release.validate_release_receipt(receipt, plan)
        self.assertEqual("historical-no-promotion", receipt["promotion"]["disposition"])
        self.assertIsNone(receipt["promotion"]["observedLatest"])
        self.assertIsNone(receipt["promotion"]["comparison"])
        self.assertEqual(0, client.latest_reads)
        self.assertEqual(0, client.publish_calls)
        self.assertEqual(before, client.release)

    def test_promotion_receipt_freezes_current_latest_identity_and_comparison(self) -> None:
        """The final receipt retains the stable identity seen inside the lock."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()
        client.latest_release = {
            "id": 900,
            "tag_name": "v10.0.0",
            "draft": False,
            "prerelease": False,
            "immutable": True,
        }
        client.tag_commits["v10.0.0"] = "b" * 40
        environment = {"DOKA_PUBLICATION_LOCK_GROUP": "doka-publication", "DOKA_PUBLICATION_LOCK_HELD": "1"}

        # Act
        with mock.patch.dict(os.environ, environment):
            receipt = github_release.finalize_release(plan, client, authority=plan["publicationAuthority"])

        # Assert
        github_release.validate_release_receipt(receipt, plan)
        self.assertEqual({
            "releaseId": 900,
            "releaseTag": "v10.0.0",
            "releaseVersion": "10.0.0",
            "sourceCommit": "b" * 40,
        }, receipt["promotion"]["observedLatest"])
        self.assertEqual(-1, receipt["promotion"]["comparison"])
        self.assertEqual("not-promoted", receipt["promotion"]["disposition"])

    def test_completion_rejects_a_tampered_latest_comparison(self) -> None:
        """A correct asset inventory cannot mask a contradictory promotion decision."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()
        client.create_draft(plan)
        client.upload_assets(self._REPOSITORY, self._TAG, [Path(asset["path"]) for asset in plan["assets"]])
        client.publish_release(plan)
        receipt = github_release.build_receipt(client.release, plan, False, promotion={
            "schemaVersion": 1,
            "observedLatest": {
                "releaseId": 900,
                "releaseTag": "v10.0.0",
                "releaseVersion": "10.0.0",
                "sourceCommit": "b" * 40,
            },
            "comparison": 1,
            "disposition": "not-promoted",
            "authorization": plan["publicationAuthority"],
        })
        github_release.write_json(self.publication / "github-release-readback.json", receipt)

        # Act
        failure = _record_exception(lambda: github_release.build_completion_receipt(
            self._REPOSITORY, self.candidate, self.publication, self.changelog
        ))

        # Assert
        self.assertIsInstance(failure, github_release.GitHubReleaseError)
        self.assertIn("comparison", str(failure))

    def test_completion_preserves_valid_historical_promotion_evidence(self) -> None:
        """A complete new-schema release verifies without observing mutable Latest."""
        # Arrange
        plan = self._enable_release_line_receipts()
        client = FakeReleaseClient()
        client.create_draft(plan)
        client.upload_assets(self._REPOSITORY, self._TAG, [Path(asset["path"]) for asset in plan["assets"]])
        client.publish_release(plan)
        receipt = github_release.build_receipt(client.release, plan, promotion={
            "schemaVersion": 1,
            "observedLatest": None,
            "comparison": None,
            "disposition": "historical-no-promotion",
            "authorization": plan["publicationAuthority"],
        })
        github_release.write_json(self.publication / "github-release-readback.json", receipt)

        # Act
        completion = github_release.build_completion_receipt(
            self._REPOSITORY, self.candidate, self.publication, self.changelog
        )

        # Assert
        self.assertEqual("published-and-verified", completion["status"])
        self.assertEqual(self._COMMIT, completion["sourceCommit"])
        self.assertEqual(0, client.latest_reads)
        readback_asset = next(
            asset for asset in completion["evidence"] if asset["name"] == "github-release-readback.json"
        )

        self.assertEqual(
            github_release.sha256_file(self.publication / "github-release-readback.json"),
            readback_asset["sha256"],
        )

    def test_plan_binds_candidate_publication_notes_and_classification(self) -> None:
        """Bind every public asset and derive prerelease policy from the version."""
        names = {asset["name"] for asset in self.plan["assets"]}

        self.assertEqual(self._COMMIT, self.plan["sourceCommit"])
        self.assertEqual("### Added\n\n- Release notes.", self.plan["notes"])
        self.assertTrue(self.plan["prerelease"])
        self.assertFalse(self.plan["latest"])
        self.assertIn(release_evidence.MANIFEST_NAME, names)
        self.assertIn("provider.cdx.json", names)
        self.assertIn(release_provenance.PORTABLE_PROVENANCE_NAME, names)
        self.assertFalse(set(github_release.PUBLICATION_EVIDENCE_FILES) & names)

    def test_plan_rejects_missing_portable_provenance(self) -> None:
        """Require the offline-verifiable SLSA bundle before draft creation."""
        (self.publication / release_provenance.PORTABLE_PROVENANCE_NAME).unlink()

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "Portable release provenance is invalid",
        ):
            github_release.build_release_plan(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_plan_rejects_provenance_for_different_package_bytes(self) -> None:
        """Prevent a valid-shaped bundle from describing another candidate."""
        path = self.publication / release_provenance.PORTABLE_PROVENANCE_NAME
        bundle = json.loads(path.read_text(encoding="utf-8"))
        statement = json.loads(base64.b64decode(bundle["dsseEnvelope"]["payload"]))
        statement["subject"][0]["digest"]["sha256"] = "0" * 64
        bundle["dsseEnvelope"]["payload"] = base64.b64encode(
            json.dumps(statement, separators=(",", ":")).encode("utf-8")
        ).decode("ascii")
        path.write_text(json.dumps(bundle) + "\n", encoding="utf-8")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "Portable release provenance is invalid",
        ):
            github_release.build_release_plan(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_cli_applies_prerelease_and_stable_classification(self) -> None:
        """Pin GitHub draft and publication flags for both release classes."""
        stable_plan = copy.deepcopy(self.plan)
        stable_plan.update(
            {
                "releaseVersion": "10.0.0",
                "releaseTag": "v10.0.0",
                "name": "Doka.EntityFrameworkCore.MySql 10.0.0",
                "prerelease": False,
                "latest": True,
            }
        )
        github_release.validate_plan(stable_plan)

        cases = (
            (self.plan, {"--prerelease", "--latest=false"}, {"--prerelease=true", "--latest=false"}),
            (stable_plan, {"--latest=false"}, {"--prerelease=false", "--latest=true"}),
        )
        for plan, expected_create_flags, expected_publish_flags in cases:
            with self.subTest(version=plan["releaseVersion"]):
                client = github_release.GitHubCliClient()
                with mock.patch.object(client, "_run_text", return_value="") as run_text:
                    client.create_draft(plan)
                    client.publish_release(plan)

                create_arguments = set(run_text.call_args_list[0].args)
                publish_arguments = set(run_text.call_args_list[1].args)
                self.assertEqual(
                    expected_create_flags,
                    create_arguments & {"--prerelease", "--latest=false"},
                )
                self.assertTrue(expected_publish_flags <= publish_arguments)

    def test_plan_rejects_candidate_tampering_after_manifest_generation(self) -> None:
        """Refuse candidate bytes that no longer match immutable evidence."""
        package = next((self.candidate / "packages").glob("*.nupkg"))
        package.write_bytes(b"tampered package\n")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "evidence verification failed",
        ):
            github_release.build_release_plan(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_plan_rejects_failed_local_package_runtime_evidence(self) -> None:
        """Require provider and independent cache runtime correctness before publication."""
        relative = "local-package-consumer/local-package-runtime.json"
        path = self.candidate / relative
        receipt = json.loads(path.read_text(encoding="utf-8"))
        manifest_path = self.candidate / release_evidence.MANIFEST_NAME
        for field, value in (("runtimeSmoke", "fail"), ("cacheRuntimeSmoke", "fail"), ("cacheEfCoreDependencies", 1)):
            with self.subTest(field=field):
                invalid_receipt = {**receipt, field: value}
                path.write_text(json.dumps(invalid_receipt) + "\n", encoding="utf-8")
                manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                artifact = next(entry for entry in manifest["artifacts"] if entry["path"] == relative)
                artifact["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
                artifact["sizeBytes"] = path.stat().st_size
                manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
                checksum = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
                (self.candidate / release_evidence.CHECKSUM_NAME).write_text(
                    f"{checksum}  {release_evidence.MANIFEST_NAME}\n", encoding="ascii"
                )

                with self.assertRaisesRegex(
                    github_release.GitHubReleaseError, "Local package runtime qualification is invalid"
                ):
                    github_release.build_release_plan(
                        self._REPOSITORY, self.candidate, self.publication, self.changelog
                    )

    def test_plan_rejects_tampered_retained_public_package(self) -> None:
        """Bind completion evidence to the persisted NuGet readback bytes."""
        package = next((self.publication / "packages").glob("*.nupkg"))
        package.write_bytes(b"tampered public package\n")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "Retained public package bytes are invalid",
        ):
            github_release.build_completion_receipt(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_plan_rejects_unsigned_retained_public_package(self) -> None:
        """Require repository signing at the completion boundary."""
        role = "provider"
        package = self.publication / "packages" / nuget_publication.package_file_name(
            nuget_publication.PROVIDER_PACKAGE_ID,
            self._VERSION,
            "nupkg",
        )
        unsigned_payload = self._zip_payload(
            f"payload/{role}.txt",
            f"{role} primary package\n".encode("ascii"),
        )
        package.write_bytes(unsigned_payload)
        readback_path = self.publication / "nuget-publication-readback.json"
        readback = json.loads(readback_path.read_text(encoding="utf-8"))
        readback["packages"][role]["publishedSha256"] = hashlib.sha256(
            unsigned_payload
        ).hexdigest()
        readback_path.write_text(json.dumps(readback) + "\n", encoding="utf-8")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "has no repository signature",
        ):
            github_release.build_completion_receipt(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_plan_rejects_unbound_repository_signature_claim(self) -> None:
        """Reject readback JSON that omits the repository-signature result."""
        path = self.publication / "nuget-publication-readback.json"
        readback = json.loads(path.read_text(encoding="utf-8"))
        readback["packages"]["provider"].pop("repositorySignaturePresent")
        path.write_text(json.dumps(readback) + "\n", encoding="utf-8")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "Published package evidence is invalid",
        ):
            github_release.build_completion_receipt(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_plan_rejects_empty_signature_verification_evidence(self) -> None:
        """Bind the successful cryptographic verifier output into completion."""
        (self.publication / "nuget-signature-verification.txt").write_text(
            "",
            encoding="utf-8",
        )

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "signature verification evidence is missing or empty",
        ):
            github_release.build_completion_receipt(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_completion_receipt_binds_post_publication_evidence(self) -> None:
        """Retain completion evidence without mutating the immutable release."""
        receipt = github_release.build_completion_receipt(
            self._REPOSITORY,
            self.candidate,
            self.publication,
            self.changelog,
        )

        self.assertEqual("published-and-verified", receipt["status"])
        self.assertEqual(self._TAG, receipt["releaseTag"])
        self.assertEqual(
            {
                "github-release-readback.json",
                *github_release.PUBLICATION_EVIDENCE_FILES,
            },
            {entry["name"] for entry in receipt["evidence"]},
        )

    def test_completion_rejects_incomplete_public_symbol_readback(self) -> None:
        """Removing the handoff sidecar must retain real publication-completion guards."""
        # Arrange
        path = self.publication / "nuget-publication-readback.json"
        readback = github_release.read_json(path, "public readback")
        symbol = next(iter(readback["symbols"].values()))
        symbol["status"] = "absent"
        github_release.write_json(path, readback)

        # Act
        error = _record_exception(lambda: github_release.build_completion_receipt(
            self._REPOSITORY, self.candidate, self.publication, self.changelog,
        ))

        # Assert
        self.assertIsInstance(error, github_release.GitHubReleaseError)
        self.assertIn("symbol", str(error).lower())

    def test_completion_accepts_signature_propagation_in_the_preflight(self) -> None:
        """Allow publication to finish once a formerly unsigned match is signed."""
        preflight_path = self.publication / "publication-preflight.json"
        readback_path = self.publication / "nuget-publication-readback.json"
        preflight = json.loads(preflight_path.read_text(encoding="utf-8"))
        readback = json.loads(readback_path.read_text(encoding="utf-8"))
        pending = copy.deepcopy(readback["packages"]["provider"])
        pending["status"] = "pending-signature"
        pending["repositorySignaturePresent"] = False
        pending.pop("readbackPath", None)
        preflight["packages"]["provider"] = pending
        preflight_path.write_text(json.dumps(preflight) + "\n", encoding="utf-8")

        receipt = github_release.build_completion_receipt(
            self._REPOSITORY,
            self.candidate,
            self.publication,
            self.changelog,
        )

        self.assertEqual("published-and-verified", receipt["status"])

    def test_retry_varying_completion_evidence_cannot_change_release_assets(self) -> None:
        """Keep availability observations outside the immutable asset plan."""
        preflight_path = self.publication / "publication-preflight.json"
        preflight = json.loads(preflight_path.read_text(encoding="utf-8"))
        preflight["checkedUtc"] = "2026-08-04T11:00:00+00:00"
        preflight_path.write_text(json.dumps(preflight) + "\n", encoding="utf-8")

        retry_plan = github_release.build_release_plan(
            self._REPOSITORY,
            self.candidate,
            self.publication,
            self.changelog,
        )

        self.assertEqual(self.plan, retry_plan)
        github_release.build_completion_receipt(
            self._REPOSITORY,
            self.candidate,
            self.publication,
            self.changelog,
        )

    def test_absent_release_is_created_uploaded_published_and_verified(self) -> None:
        """Complete the clean path only after every asset reads back exactly."""
        client = FakeReleaseClient()

        receipt = github_release.finalize_release(self.plan, client, sleep=lambda _: None)

        self.assertEqual("published-and-verified", receipt["status"])
        self.assertEqual(1, client.create_calls)
        self.assertEqual(1, client.publish_calls)
        self.assertEqual(
            {asset["name"] for asset in self.plan["assets"]},
            set(client.upload_calls[0]),
        )
        self.assertEqual(
            [(self._REPOSITORY, self._TAG, self._COMMIT)] * 3,
            client.verified_tags,
        )

    def test_finalize_waits_for_draft_and_asset_inventory_visibility(self) -> None:
        """Apply bounded write readback before crossing into publication."""
        client = FakeReleaseClient()
        client.release_visibility_delay = 2
        client.asset_visibility_delay = 2
        sleeps: list[float] = []

        receipt = github_release.finalize_release(
            self.plan,
            client,
            sleep=sleeps.append,
            readback_attempts=4,
        )

        self.assertEqual("published-and-verified", receipt["status"])
        self.assertEqual(
            [github_release.READBACK_DELAY_SECONDS] * 4,
            sleeps,
        )
        self.assertEqual(1, client.publish_calls)

    def test_stage_creates_a_verified_draft_without_publishing(self) -> None:
        """Create the public release identity before the first NuGet push."""
        client = FakeReleaseClient()

        receipt = github_release.stage_release(self.staged_plan, client)

        self.assertEqual("draft-staged-and-verified", receipt["status"])
        self.assertEqual(1, client.create_calls)
        self.assertEqual(0, client.publish_calls)
        self.assertTrue(client.release["draft"])

    def test_stage_waits_for_draft_and_asset_inventory_visibility(self) -> None:
        """Tolerate bounded GitHub read-after-write propagation on both writes."""
        client = FakeReleaseClient()
        client.release_visibility_delay = 2
        client.asset_visibility_delay = 2
        sleeps: list[float] = []

        receipt = github_release.stage_release(
            self.staged_plan,
            client,
            sleep=sleeps.append,
            readback_attempts=4,
        )

        self.assertEqual("draft-staged-and-verified", receipt["status"])
        self.assertEqual(
            [github_release.READBACK_DELAY_SECONDS] * 4,
            sleeps,
        )

    def test_stage_fails_when_the_created_draft_never_becomes_visible(self) -> None:
        """Keep an unavailable draft fail-closed after the bounded retry window."""
        client = FakeReleaseClient()
        client.release_visibility_delay = 3
        sleeps: list[float] = []

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "created release draft within the readback window",
        ):
            github_release.stage_release(
                self.staged_plan,
                client,
                sleep=sleeps.append,
                readback_attempts=2,
            )

        self.assertEqual([github_release.READBACK_DELAY_SECONDS], sleeps)
        self.assertEqual([], client.upload_calls)

    def test_stage_rejects_completion_evidence_as_a_release_asset(self) -> None:
        """Keep retry-varying observations outside the immutable release."""
        client = FakeReleaseClient()
        client.create_draft(self.staged_plan)
        name = github_release.PUBLICATION_EVIDENCE_FILES[0]
        client.add_asset({"name": name}, f"prior {name}\n".encode("ascii"))

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "unexpected asset",
        ):
            github_release.stage_release(self.staged_plan, client)

    def test_stage_recovers_after_the_release_was_already_published(self) -> None:
        """Let a rerun reach complete readback after a prior finalization."""
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.upload_assets(
            self._REPOSITORY,
            self._TAG,
            [Path(asset["path"]) for asset in self.plan["assets"]],
        )
        client.publish_release(self.plan)
        client.upload_calls.clear()
        client.publish_calls = 0

        receipt = github_release.stage_release(self.staged_plan, client)

        self.assertEqual("release-already-published", receipt["status"])
        self.assertEqual([], client.upload_calls)
        self.assertEqual(0, client.publish_calls)

    def test_stage_rejects_published_release_with_wrong_latest_status(self) -> None:
        """Apply stable/prerelease classification during retry reconciliation."""
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.upload_assets(
            self._REPOSITORY,
            self._TAG,
            [Path(asset["path"]) for asset in self.plan["assets"]],
        )
        client.publish_release(self.plan)
        client.latest = True

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "latest status conflicts",
        ):
            github_release.stage_release(self.staged_plan, client)

    def test_stage_rejects_an_unknown_asset_from_an_existing_draft(self) -> None:
        """Do not hide unrelated remote state behind retry reconciliation."""
        client = FakeReleaseClient()
        client.create_draft(self.staged_plan)
        client.add_asset({"name": "unexpected.txt"}, b"unexpected\n")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "unexpected asset",
        ):
            github_release.stage_release(self.staged_plan, client)

    def test_matching_partial_draft_resumes_only_missing_assets(self) -> None:
        """Resume a matching draft without recreating or replacing an asset."""
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.create_calls = 0
        first = self.plan["assets"][0]
        payload = Path(first["path"]).read_bytes()
        client.add_asset(first, payload)

        github_release.finalize_release(self.plan, client, sleep=lambda _: None)

        self.assertEqual(0, client.create_calls)
        self.assertEqual(1, client.publish_calls)
        self.assertNotIn(first["name"], client.upload_calls[0])
        self.assertEqual(len(self.plan["assets"]) - 1, len(client.upload_calls[0]))

    def test_identical_immutable_release_is_an_idempotent_success(self) -> None:
        """Accept a prior exact publication without performing any mutation."""
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.upload_assets(
            self._REPOSITORY,
            self._TAG,
            [Path(asset["path"]) for asset in self.plan["assets"]],
        )
        client.publish_release(self.plan)
        client.create_calls = 0
        client.upload_calls.clear()
        client.publish_calls = 0

        receipt = github_release.finalize_release(self.plan, client)

        self.assertEqual("published-and-verified", receipt["status"])
        self.assertEqual(0, client.create_calls)
        self.assertEqual([], client.upload_calls)
        self.assertEqual(0, client.publish_calls)

    def test_conflicting_remote_asset_fails_without_mutation(self) -> None:
        """Reject a same-name payload conflict instead of clobbering it."""
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.create_calls = 0
        first = self.plan["assets"][0]
        conflicting_payload = b"x" * int(first["sizeBytes"])
        client.add_asset(first, conflicting_payload)

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "asset readback conflicts",
        ):
            github_release.finalize_release(self.plan, client)

        self.assertEqual([], client.upload_calls)
        self.assertEqual(0, client.publish_calls)

    def test_remote_tag_failure_precedes_every_release_mutation(self) -> None:
        """Reject a moved or lightweight remote tag before creating a draft."""
        client = FakeReleaseClient()
        client.tag_error = github_release.GitHubReleaseError("remote tag conflict")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "remote tag conflict",
        ):
            github_release.finalize_release(self.plan, client)

        self.assertEqual(0, client.create_calls)
        self.assertEqual([], client.upload_calls)
        self.assertEqual(0, client.publish_calls)

    def test_remote_tag_movement_after_upload_prevents_publication(self) -> None:
        """Keep a moved tag from acquiring a completed draft's assets."""
        client = FakeReleaseClient()
        client.tag_error = github_release.GitHubReleaseError("remote tag moved")
        client.tag_error_on_verification = 2

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "remote tag moved",
        ):
            github_release.finalize_release(self.plan, client)

        self.assertEqual(1, client.create_calls)
        self.assertEqual(1, len(client.upload_calls))
        self.assertEqual(0, client.publish_calls)

    def test_latest_release_mismatch_is_not_an_idempotent_success(self) -> None:
        """Require GitHub's canonical latest endpoint to match release policy."""
        client = FakeReleaseClient()
        client.create_draft(self.plan)
        client.upload_assets(
            self._REPOSITORY,
            self._TAG,
            [Path(asset["path"]) for asset in self.plan["assets"]],
        )
        client.publish_release(self.plan)
        client.latest = True

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "latest status conflicts",
        ):
            github_release.finalize_release(self.plan, client)

    def test_local_annotated_tag_resolves_to_the_planned_commit(self) -> None:
        """Accept an annotated local tag that peels to the planned commit."""
        with mock.patch.object(
            github_release,
            "run_git",
            side_effect=("tag", self._COMMIT),
        ) as run_git:
            github_release.verify_local_tag(self.root, self.plan)

        self.assertEqual(
            [
                mock.call(
                    self.root,
                    "cat-file",
                    "-t",
                    f"refs/tags/{self._TAG}",
                ),
                mock.call(
                    self.root,
                    "rev-parse",
                    f"refs/tags/{self._TAG}^{{commit}}",
                ),
            ],
            run_git.call_args_list,
        )

    def test_local_lightweight_tag_is_rejected(self) -> None:
        """Prevent a lightweight local tag from authorizing finalization."""
        with (
            mock.patch.object(
                github_release,
                "run_git",
                return_value="commit",
            ),
            self.assertRaisesRegex(
                github_release.GitHubReleaseError,
                "must be annotated",
            ),
        ):
            github_release.verify_local_tag(self.root, self.plan)

    def test_local_tag_must_resolve_to_the_planned_commit(self) -> None:
        """Reject an annotated local tag that peels to another commit."""
        with (
            mock.patch.object(
                github_release,
                "run_git",
                side_effect=("tag", "b" * 40),
            ),
            self.assertRaisesRegex(
                github_release.GitHubReleaseError,
                "does not identify",
            ),
        ):
            github_release.verify_local_tag(self.root, self.plan)

    def test_remote_annotated_tag_chain_resolves_to_the_planned_commit(self) -> None:
        """Follow an annotated remote tag instead of trusting a release target."""
        client = github_release.GitHubCliClient()
        with mock.patch.object(
            client,
            "_run_text",
            side_effect=(
                json.dumps({"object": {"type": "tag", "sha": "b" * 40}}),
                json.dumps(
                    {"object": {"type": "commit", "sha": self._COMMIT}}
                ),
            ),
        ):
            client.verify_tag(self._REPOSITORY, self._TAG, self._COMMIT)

    def test_client_discovers_drafts_through_the_paginated_inventory(self) -> None:
        """Use the REST surface that includes drafts for write-capable callers."""
        client = github_release.GitHubCliClient()
        expected = {
            "id": 42,
            "tag_name": self._TAG,
            "draft": True,
            "assets": [],
        }
        with mock.patch.object(
            client,
            "_run_text",
            return_value=json.dumps(
                [
                    [{"id": 41, "tag_name": "v9.9.9", "draft": False}],
                    [expected],
                ]
            ),
        ) as run_text:
            release = client.get_release(self._REPOSITORY, self._TAG)

        self.assertEqual(expected, release)
        run_text.assert_called_once_with(
            "api",
            "--paginate",
            "--slurp",
            "-H",
            "Accept: application/vnd.github+json",
            "-H",
            f"X-GitHub-Api-Version: {github_release.GITHUB_API_VERSION}",
            f"/repos/{self._REPOSITORY}/releases?per_page=100",
        )

    def test_client_returns_none_when_no_release_matches_the_tag(self) -> None:
        """Distinguish a missing release from an invalid inventory response."""
        client = github_release.GitHubCliClient()
        with mock.patch.object(
            client,
            "_run_text",
            return_value=json.dumps(
                [[{"id": 41, "tag_name": "v9.9.9", "draft": False}]]
            ),
        ):
            release = client.get_release(self._REPOSITORY, self._TAG)

        self.assertIsNone(release)

    def test_client_rejects_duplicate_releases_for_one_tag(self) -> None:
        """Fail closed if the paginated inventory violates tag uniqueness."""
        client = github_release.GitHubCliClient()
        duplicate = {"id": 42, "tag_name": self._TAG, "draft": True}
        with (
            mock.patch.object(
                client,
                "_run_text",
                return_value=json.dumps([[duplicate], [duplicate]]),
            ),
            self.assertRaisesRegex(
                github_release.GitHubReleaseError,
                "duplicate releases",
            ),
        ):
            client.get_release(self._REPOSITORY, self._TAG)

    def test_remote_lightweight_tag_is_rejected(self) -> None:
        """Prevent a lightweight tag from authorizing release finalization."""
        client = github_release.GitHubCliClient()
        with (
            mock.patch.object(
                client,
                "_run_text",
                return_value=json.dumps(
                    {"object": {"type": "commit", "sha": self._COMMIT}}
                ),
            ),
            self.assertRaisesRegex(
                github_release.GitHubReleaseError,
                "must be annotated",
            ),
        ):
            client.verify_tag(self._REPOSITORY, self._TAG, self._COMMIT)


if __name__ == "__main__":
    unittest.main()
