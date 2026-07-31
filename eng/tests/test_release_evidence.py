"""Regression tests for the immutable release-candidate evidence boundary.

The suite creates an isolated tagged Git repository and exercises both valid
generation and fail-closed rejection paths. Fixtures use synthetic artifact
bytes because this layer verifies identity and inventory, not NuGet contents.
"""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from eng import release_evidence


class ReleaseEvidenceTests(unittest.TestCase):
    """Prove source identity, portability, completeness, and tamper detection.

    Each test owns a real repository so Git state, tags, and remote identity
    cannot be accidentally mocked into agreement with the manifest.
    """

    def setUp(self) -> None:
        """Create a tagged repository with an ignored evidence fixture."""
        self._temporary_directory = tempfile.TemporaryDirectory(prefix="doka-release-evidence-")
        self.repo = Path(self._temporary_directory.name)
        self.root = self.repo / "evidence"
        self._git("init", "--initial-branch=main")
        self._git("config", "user.name", "Doka Test")
        self._git("config", "user.email", "doka-test@example.invalid")
        self._git("config", "commit.gpgSign", "false")
        self._git("config", "tag.gpgSign", "false")
        self._git("remote", "add", "origin", "https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql.git")
        # Evidence remains ignored so negative tests can mutate it without
        # turning an artifact-integrity failure into a source-dirty failure.
        (self.repo / ".gitignore").write_text("/evidence/\n", encoding="ascii")
        (self.repo / "source.txt").write_text("reviewed source\n", encoding="ascii")
        self._git("add", ".gitignore", "source.txt")
        self._git("commit", "-m", "test: seed release source")
        self._git("tag", "v1.2.3")
        self._write_complete_evidence()

    def tearDown(self) -> None:
        """Dispose the isolated repository fixture."""
        self._temporary_directory.cleanup()

    def test_generate_and_verify_bind_every_portable_artifact(self) -> None:
        """Generate canonical relative paths and verify the detached checksum."""
        self._generate()

        release_evidence.verify_manifest(self.root, self.repo)

        manifest = json.loads((self.root / release_evidence.MANIFEST_NAME).read_text(encoding="utf-8"))
        paths = [artifact["path"] for artifact in manifest["artifacts"]]
        self.assertEqual(sorted(paths), paths)
        self.assertTrue(all(not Path(path).is_absolute() for path in paths))
        self.assertIn("integration/test-database-evidence.json", paths)
        self.assertEqual("refs/tags/v1.2.3", manifest["source"]["ref"])
        self.assertEqual("clean", manifest["source"]["treeState"])
        self.assertEqual("2.5.0", manifest["toolchain"]["resolvedPackages"]["MySqlConnector"])
        self.assertEqual(
            list(release_evidence.REQUIRED_ENGINE_TARGETS),
            [engine["targetId"] for engine in manifest["engines"]],
        )
        self.assertEqual(
            list(release_evidence.REQUIRED_ENGINE_TARGETS),
            manifest["integrationConfigurationMatrix"]["targets"],
        )
        self.assertTrue(manifest["integrationConfigurationMatrix"]["fullConfigurationMatrixRequired"])
        self.assertEqual("", manifest["integrationConfigurationMatrix"]["testFilter"])

    def test_generate_rejects_dirty_release_source(self) -> None:
        """Reject a tag whose checked-out source differs from the reviewed commit."""
        (self.repo / "source.txt").write_text("unreviewed source\n", encoding="ascii")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "clean Git worktree"):
            self._generate()

    def test_verify_rejects_artifact_tampering(self) -> None:
        """Reject package bytes changed after the canonical manifest was generated."""
        self._generate()
        package = self.root / "packages" / "Doka.EntityFrameworkCore.MySql.1.2.3.nupkg"
        package.write_bytes(b"tampered package")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "integrity check failed"):
            release_evidence.verify_manifest(self.root, self.repo)

    def test_generate_rejects_incomplete_engine_matrix(self) -> None:
        """Reject a release whose manifest would omit one advertised engine line."""
        missing = self.root / "specification" / "mariadb114" / "test-database-evidence.json"
        missing.unlink()
        integration_path = self.root / "integration" / "test-database-evidence.json"
        integration_evidence = json.loads(integration_path.read_text(encoding="utf-8"))
        integration_evidence["targets"] = [
            target
            for target in integration_evidence["targets"]
            if target["targetId"] != "mariadb114"
        ]
        integration_path.write_text(json.dumps(integration_evidence), encoding="utf-8")
        tls_integration_path = self.root / "integration" / "tls" / "test-database-evidence.json"
        tls_integration_evidence = json.loads(tls_integration_path.read_text(encoding="utf-8"))
        tls_integration_evidence["targets"] = [
            target
            for target in tls_integration_evidence["targets"]
            if target["targetId"] != "mariadb114"
        ]
        tls_integration_path.write_text(json.dumps(tls_integration_evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "mariadb114"):
            self._generate()

    def test_generate_rejects_mutable_engine_image(self) -> None:
        """Reject a release matrix that records a mutable tag without its digest."""
        path = self.root / "specification" / "mysql84" / "test-database-evidence.json"
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["targets"][0]["image"] = "mysql:8.4"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "not digest-pinned"):
            self._generate()

    def test_generate_rejects_filtered_integration_matrix(self) -> None:
        """Reject smoke-filter evidence presented as a complete release matrix."""
        path = self.root / release_evidence.INTEGRATION_MATRIX_EVIDENCE
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["testFilter"] = "Category!=SecurityConfigurationContract"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "must not contain a test filter"):
            self._generate()

    def test_generate_rejects_unrequired_integration_matrix(self) -> None:
        """Reject evidence from a runner that did not enforce the release contract."""
        path = self.root / release_evidence.INTEGRATION_MATRIX_EVIDENCE
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["fullConfigurationMatrixRequired"] = False
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "not marked as the required"):
            self._generate()

    def test_generate_rejects_unexpected_release_package(self) -> None:
        """Reject stale or unrelated package files before they enter the manifest."""
        package = self.root / "packages" / "Doka.EntityFrameworkCore.MySql.1.2.2.nupkg"
        package.write_bytes(b"stale package")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "package inventory mismatch"):
            self._generate()

    def test_generate_rejects_unexpected_engine_evidence(self) -> None:
        """Reject engine evidence outside the advertised release matrix."""
        target_directory = self.root / "specification" / "mysql80"
        target_directory.mkdir(parents=True)
        evidence = {
            "schemaVersion": 1,
            "lifecycleState": "cleanup-completed",
            "targets": [
                {
                    "targetId": "mysql80",
                    "engine": "MySql",
                    "serverVersionToken": "mysql:8.0",
                    "source": "external",
                    "image": f"mysql:8.0.45@sha256:{'0' * 64}",
                }
            ],
        }
        (target_directory / "test-database-evidence.json").write_text(
            json.dumps(evidence),
            encoding="utf-8",
        )

        with self.assertRaisesRegex(release_evidence.EvidenceError, "Unexpected engine evidence"):
            self._generate()

    def test_generate_rejects_ambiguous_semantic_release_tags(self) -> None:
        """Reject a commit that has more than one possible release identity."""
        self._git("tag", "v1.2.4")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "exactly semantic version tag"):
            self._generate()

    def test_verify_cli_accepts_one_root_argument(self) -> None:
        """Keep the real verify command-line contract constructible and unambiguous."""
        arguments = ["release_evidence.py", "verify", "--root", str(self.root)]

        with mock.patch.object(sys, "argv", arguments):
            parsed = release_evidence.parse_arguments()

        self.assertEqual("verify", parsed.command)
        self.assertEqual(self.root, parsed.root)

    def _generate(self) -> None:
        """Generate tagged release evidence under an explicit local identity."""
        arguments = SimpleNamespace(
            repo=self.repo,
            root=self.root,
            run_id="test-run",
            release_version="1.2.3",
            dependency_graph=self.root / "resolved-packages.json",
            expected_ref="refs/tags/v1.2.3",
            require_tag=True,
        )
        with mock.patch.dict(
            "os.environ",
            {
                "GITHUB_ACTIONS": "false",
                "GITHUB_REF": "",
                "GITHUB_SHA": "",
            },
        ):
            release_evidence.write_manifest(arguments)

    def _write_complete_evidence(self) -> None:
        """Write the minimum complete package, dependency, and engine matrix."""
        packages = self.root / "packages"
        sbom = self.root / "sbom"
        packages.mkdir(parents=True)
        sbom.mkdir(parents=True)
        for package_id in (
            "Doka.EntityFrameworkCore.MySql",
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
        ):
            (packages / f"{package_id}.1.2.3.nupkg").write_bytes(f"{package_id} package".encode("ascii"))
            (packages / f"{package_id}.1.2.3.snupkg").write_bytes(f"{package_id} symbols".encode("ascii"))
        (sbom / "manifest.spdx.json").write_text('{"spdxVersion":"SPDX-2.3"}\n', encoding="ascii")

        dependencies = {
            "version": 1,
            "projects": [
                {
                    "frameworks": [
                        {
                            "topLevelPackages": [
                                {
                                    "id": "Microsoft.EntityFrameworkCore.Design",
                                    "resolvedVersion": "10.0.8",
                                },
                                {
                                    "id": "Microsoft.EntityFrameworkCore.Relational",
                                    "resolvedVersion": "10.0.8",
                                },
                                {
                                    "id": "MySqlConnector",
                                    "resolvedVersion": "2.5.0",
                                },
                            ]
                        }
                    ]
                }
            ],
        }
        (self.root / "resolved-packages.json").write_text(
            json.dumps(dependencies),
            encoding="utf-8",
        )

        identities = {
            "mysql84": ("MySql", "mysql:8.4", f"mysql:8.4.10@sha256:{'8' * 64}"),
            "mariadb114": ("MariaDb", "mariadb:11.4", f"mariadb:11.4.12@sha256:{'1' * 64}"),
            "mariadb118": ("MariaDb", "mariadb:11.8", f"mariadb:11.8.8@sha256:{'2' * 64}"),
        }
        integration_targets = []
        for target_id, (engine, version, image) in identities.items():
            target_directory = self.root / "specification" / target_id
            target_directory.mkdir(parents=True)
            target = {
                "targetId": target_id,
                "engine": engine,
                "serverVersionToken": version,
                "source": "testcontainers",
                "image": image,
            }
            evidence = {
                "schemaVersion": 1,
                "lifecycleState": "cleanup-completed",
                "targets": [target],
            }
            (target_directory / "test-database-evidence.json").write_text(
                json.dumps(evidence),
                encoding="utf-8",
            )
            integration_targets.append(target)

        # Duplicate target identities model independent specification and
        # integration producers; the collector must merge only exact matches.
        integration_directory = self.root / "integration"
        integration_directory.mkdir()
        (integration_directory / "test-database-evidence.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "lifecycleState": "cleanup-completed",
                    "targets": integration_targets,
                }
            ),
            encoding="utf-8",
        )
        tls_directory = integration_directory / "tls"
        tls_directory.mkdir()
        (tls_directory / "test-database-evidence.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "lifecycleState": "cleanup-completed",
                    "targets": integration_targets,
                }
            ),
            encoding="utf-8",
        )
        (integration_directory / "compatibility-matrix-evidence.json").write_text(
            json.dumps(
                {
                    "targetSelection": "mysql84,mariadb114,mariadb118",
                    "testFilter": "",
                    "fullConfigurationMatrixRequired": True,
                    "testExitCode": 0,
                }
            ),
            encoding="utf-8",
        )

    def _git(self, *arguments: str) -> None:
        """Run one fixture-local Git mutation with output kept out of test logs."""
        subprocess.run(
            ("git", *arguments),
            cwd=self.repo,
            check=True,
            capture_output=True,
            text=True,
        )


if __name__ == "__main__":
    unittest.main()
