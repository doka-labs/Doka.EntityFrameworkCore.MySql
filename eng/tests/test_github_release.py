"""Regression tests for conflict-safe GitHub release finalization."""

from __future__ import annotations

import copy
import hashlib
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

from eng.release import evidence as release_evidence
from eng.release import github as github_release
from eng.release import nuget as nuget_publication


class FakeReleaseClient:
    """Model the narrow remote release contract without external mutations."""

    def __init__(self) -> None:
        """Initialize an absent release and observable mutation counters."""
        self.release: dict[str, object] | None = None
        self.payloads: dict[int, bytes] = {}
        self.create_calls = 0
        self.upload_calls: list[list[str]] = []
        self.publish_calls = 0
        self.verified_tags: list[tuple[str, str, str]] = []
        self.latest = False
        self.tag_error: github_release.GitHubReleaseError | None = None
        self.tag_error_on_verification: int | None = None
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
        return copy.deepcopy(self.release)

    def create_draft(self, plan: dict[str, object]) -> None:
        """Create exactly the draft metadata requested by the plan."""
        self.create_calls += 1
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
        del plan
        if self.release is None:
            raise AssertionError("A draft must exist before publication.")

        self.publish_calls += 1
        self.release["draft"] = False
        self.release["immutable"] = True
        self.release["published_at"] = "2026-08-04T12:00:00Z"

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


class GitHubReleaseTests(unittest.TestCase):
    """Exercise planning, recovery, publication, and remote conflict gates."""

    _REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"
    _VERSION = "10.0.0-rc.1"
    _TAG = f"v{_VERSION}"
    _COMMIT = "a" * 40

    @staticmethod
    def _zip_payload(name: str, payload: bytes) -> bytes:
        """Build a deterministic package-shaped ZIP for digest validation."""
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as package:
            package.writestr(name, payload)
        return buffer.getvalue()

    def setUp(self) -> None:
        """Create one checksum-bound candidate and publication receipt set."""
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
                "resolved-packages.json": (b'{"packages":[]}\n', "evidence"),
                "sbom/provider.cdx.json": (b'{"bomFormat":"CycloneDX"}\n', "sbom"),
            }
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
            "schemaVersion": release_evidence.SCHEMA_VERSION,
            "releaseCandidateRunId": self.candidate.name,
            "releaseVersion": self._VERSION,
            "source": {
                "repository": self._REPOSITORY,
                "ref": f"refs/tags/{self._TAG}",
                "tag": self._TAG,
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
                    f"@refs/tags/{self._TAG}"
                ),
                "repository": self._REPOSITORY,
            },
            "toolchain": {
                "approvedDotnetSdk": "10.0.302",
                "dotnetSdk": "10.0.302",
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

        receipt: dict[str, object] = {
            "schemaVersion": nuget_publication.PUBLICATION_RECEIPT_SCHEMA_VERSION,
            "candidateRunId": "12345",
            "candidateRunAttempt": "1",
            "repository": self._REPOSITORY,
            "releaseTag": self._TAG,
            "releaseVersion": self._VERSION,
            "sourceCommit": self._COMMIT,
            "releaseCandidateRunId": self.candidate.name,
            "trustedRef": "refs/heads/main",
            "mysql84Image": "mysql:8.4.6",
            "packages": receipt_packages,
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
        preflight_packages: dict[str, dict[str, str]] = {}
        readback_packages: dict[str, dict[str, str]] = {}
        preflight_symbols: dict[str, dict[str, str]] = {}
        readback_symbols: dict[str, dict[str, str]] = {}
        public_packages = self.publication / "packages"
        public_symbols = public_packages / "symbols"
        public_symbols.mkdir(parents=True)
        for role, package_id in github_release.PACKAGE_IDENTITIES:
            candidate_digest = receipt_packages[role]["contentDigest"]
            package_url = nuget_publication.remote_package_url(
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
            package_path.write_bytes(package_payloads[role])
            readback_packages[role] = {
                "id": package_id,
                "status": "matching",
                "url": package_url,
                "candidateContentDigest": candidate_digest,
                "publishedContentDigest": candidate_digest,
                "publishedSha256": hashlib.sha256(
                    package_payloads[role]
                ).hexdigest(),
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
            "validated-candidate.json": receipt,
            "publication-preflight.json": {
                "schemaVersion": nuget_publication.SCHEMA_VERSION,
                "checkedUtc": "2026-08-04T10:00:00+00:00",
                "releaseTag": self._TAG,
                "releaseVersion": self._VERSION,
                "sourceCommit": self._COMMIT,
                "packages": preflight_packages,
                "symbols": preflight_symbols,
            },
            "symbol-readback-manifest.json": {
                "schemaVersion": nuget_publication.SCHEMA_VERSION,
                "releaseVersion": self._VERSION,
                "symbols": symbols,
            },
            "nuget-publication-readback.json": {
                "schemaVersion": nuget_publication.SCHEMA_VERSION,
                "verifiedUtc": "2026-08-04T10:10:00+00:00",
                "releaseTag": self._TAG,
                "releaseVersion": self._VERSION,
                "sourceCommit": self._COMMIT,
                "packages": readback_packages,
                "symbols": readback_symbols,
            },
            "consumer-runtime-readback.json": {
                "schemaVersion": nuget_publication.SCHEMA_VERSION,
                "verifiedUtc": "2026-08-04T10:20:00+00:00",
                "releaseTag": self._TAG,
                "releaseVersion": self._VERSION,
                "sourceCommit": self._COMMIT,
                "packageSource": nuget_publication.NUGET_SOURCE,
                "packageCache": "/consumer/.nuget/packages",
                "packages": sorted(
                    f"{package_id}/{self._VERSION}".casefold()
                    for _, package_id in github_release.PACKAGE_IDENTITIES
                ),
                "dotnetSdk": "10.0.302",
                "engineImage": receipt["mysql84Image"],
                "runtimeSmoke": "pass",
            },
        }
        for name, value in evidence.items():
            (self.publication / name).write_text(
                json.dumps(value) + "\n",
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

    def tearDown(self) -> None:
        """Remove the isolated candidate fixture."""
        self.temporary_directory.cleanup()

    def test_plan_binds_candidate_publication_notes_and_classification(self) -> None:
        """Bind every public asset and derive prerelease policy from the version."""
        names = {asset["name"] for asset in self.plan["assets"]}

        self.assertEqual(self._COMMIT, self.plan["sourceCommit"])
        self.assertEqual("### Added\n\n- Release notes.", self.plan["notes"])
        self.assertTrue(self.plan["prerelease"])
        self.assertFalse(self.plan["latest"])
        self.assertIn(release_evidence.MANIFEST_NAME, names)
        self.assertIn("provider.cdx.json", names)
        self.assertEqual(
            set(github_release.PUBLICATION_EVIDENCE_FILES),
            set(github_release.PUBLICATION_EVIDENCE_FILES) & names,
        )

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

    def test_plan_rejects_failed_consumer_runtime_evidence(self) -> None:
        """Require a successful public-package runtime smoke receipt."""
        path = self.publication / "consumer-runtime-readback.json"
        receipt = json.loads(path.read_text(encoding="utf-8"))
        receipt["runtimeSmoke"] = "fail"
        path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "Consumer runtime readback is invalid",
        ):
            github_release.build_release_plan(
                self._REPOSITORY,
                self.candidate,
                self.publication,
                self.changelog,
            )

    def test_plan_rejects_tampered_retained_public_package(self) -> None:
        """Bind release finalization to the persisted NuGet readback bytes."""
        package = next((self.publication / "packages").glob("*.nupkg"))
        package.write_bytes(b"tampered public package\n")

        with self.assertRaisesRegex(
            github_release.GitHubReleaseError,
            "Retained public package bytes are invalid",
        ):
            github_release.build_release_plan(
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
