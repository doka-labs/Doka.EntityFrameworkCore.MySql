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

from eng.release import evidence as release_evidence


class ReleaseEvidenceTests(unittest.TestCase):
    """Prove source identity, portability, completeness, and tamper detection.

    Each test owns a real repository so Git state, tags, and remote identity
    cannot be accidentally mocked into agreement with the manifest.
    """

    _LOCAL_RELEASE_ENVIRONMENT = {
        "GITHUB_ACTIONS": "false",
        "GITHUB_REF": "",
        "GITHUB_SHA": "",
    }

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
        dotnet_sdk = release_evidence.run_command("dotnet", "--version", cwd=self.repo)
        (self.repo / ".gitignore").write_text("/evidence/\n", encoding="ascii")
        (self.repo / "global.json").write_text(
            json.dumps(
                {
                    "sdk": {
                        "version": dotnet_sdk,
                        "rollForward": "disable",
                        "allowPrerelease": False,
                    }
                }
            )
            + "\n",
            encoding="ascii",
        )
        (self.repo / "source.txt").write_text("reviewed source\n", encoding="ascii")
        self._git("add", ".gitignore", "global.json", "source.txt")
        self._git("commit", "-m", "test: seed release source")
        self._git("tag", "v1.2.3")
        self._write_complete_evidence()

    def tearDown(self) -> None:
        """Dispose the isolated repository fixture."""
        self._temporary_directory.cleanup()

    def test_generate_and_verify_bind_every_portable_artifact(self) -> None:
        """Generate canonical relative paths and verify the detached checksum."""
        self._generate()

        self._verify()

        manifest = json.loads((self.root / release_evidence.MANIFEST_NAME).read_text(encoding="utf-8"))
        paths = [artifact["path"] for artifact in manifest["artifacts"]]
        self.assertEqual(sorted(paths), paths)
        self.assertTrue(all(not Path(path).is_absolute() for path in paths))
        self.assertIn("release-qualification-manifest.json", paths)
        self.assertEqual("refs/tags/v1.2.3", manifest["source"]["ref"])
        self.assertEqual("clean", manifest["source"]["treeState"])
        self.assertEqual(
            manifest["toolchain"]["approvedDotnetSdk"],
            manifest["toolchain"]["dotnetSdk"],
        )
        self.assertEqual("2.5.0", manifest["toolchain"]["resolvedPackages"]["MySqlConnector"])

        self.assertEqual(
            list(release_evidence.REQUIRED_ENGINE_TARGETS),
            [engine["targetId"] for engine in manifest["engines"]],
        )
        # Gate selection lives in the qualification manifest; this document
        # binds itself to it so the two cannot describe different releases.
        self.assertEqual("v1.2.3", manifest["qualification"]["releaseTag"])
        self.assertTrue(manifest["qualification"]["gates"])
        self.assertTrue(manifest["runtimePosture"]["publishTrimmed"])
        self.assertEqual("full", manifest["runtimePosture"]["trimMode"])
        self.assertEqual("paired", manifest["performanceEvidence"]["comparisonMode"])
        self.assertEqual(
            sorted(release_evidence.PERFORMANCE_TARGETS),
            sorted(
                engine["target"]
                for engine in manifest["performanceEvidence"]["engines"]
            ),
        )
        self.assertEqual(
            list(release_evidence.required_reconciliation_gates()),
            sorted(manifest["verificationReconciliation"]),
        )

    def test_performance_input_contract_covers_execution_dependencies(
        self,
    ) -> None:
        """Invalidate evidence when measured code or its harness changes."""
        included = (
            ".github/workflows/benchmark-scorecard.yml",
            "src/Doka.EntityFrameworkCore.MySql/Storage/Mapping.cs",
            "benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/Program.cs",
            "docker/compose.yml",
            "eng/benchmark.sh",
            "eng/performance/check-benchmark-ratios.sh",
            "eng/common/deadline.py",
            "eng/common/verify-dotnet.sh",
            "eng/performance/__init__.py",
            "eng/performance/benchmark.sh",
            "eng/performance/check-benchmark-ratios.sh",
            "eng/performance/cli.py",
            "eng/performance/confirmation.py",
            "eng/performance/contract.py",
            "eng/performance/environment.py",
            "eng/performance/evaluation.py",
            "eng/performance/reports.py",
            "eng/performance/statistics.py",
            "eng/performance/cli.py",
            "eng/common/deadline.py",
            "global.json",
        )
        excluded = (
            ".github/workflows/benchmark.yml",
            "docs/operations/performance-evidence.md",
            "eng/performance/workflow_state.py",
            "eng/performance/inputs.py",
            "eng/performance/workflow_state.py",
            "tests/Doka.EntityFrameworkCore.MySql.Tests/MySqlOptionsTests.cs",
        )

        for path in included:
            with self.subTest(path=path):
                self.assertTrue(release_evidence.is_performance_input(path))

        for path in excluded:
            with self.subTest(path=path):
                self.assertFalse(release_evidence.is_performance_input(path))

    def test_changed_paths_preserve_both_sides_of_a_rename(self) -> None:
        """Invalidate evidence when source moves outside a measured path."""
        source_path = self.repo / "src/Provider.cs"
        source_path.parent.mkdir()
        source_path.write_text(
            "internal sealed class Provider {}\n",
            encoding="ascii",
        )
        self._git("add", "src/Provider.cs")
        self._git("commit", "-m", "test: add provider source")
        source_revision = release_evidence.run_command(
            "git",
            "rev-parse",
            "HEAD",
            cwd=self.repo,
        )

        (self.repo / "docs").mkdir()
        self._git("mv", "src/Provider.cs", "docs/Provider.cs")
        self._git("commit", "-m", "test: move provider source")
        moved_revision = release_evidence.run_command(
            "git",
            "rev-parse",
            "HEAD",
            cwd=self.repo,
        )

        paths = release_evidence.changed_paths(
            self.repo,
            source_revision,
            moved_revision,
        )

        self.assertEqual(
            ["docs/Provider.cs", "src/Provider.cs"],
            paths,
        )
        self.assertEqual(
            ["src/Provider.cs"],
            [
                path
                for path in paths
                if release_evidence.is_performance_input(path)
            ],
        )

    def test_generate_rejects_dirty_release_source(self) -> None:
        """Reject a tag whose checked-out source differs from the reviewed commit."""
        (self.repo / "source.txt").write_text("unreviewed source\n", encoding="ascii")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "clean Git worktree"):
            self._generate()

    def test_sdk_contract_rejects_roll_forward(self) -> None:
        """Reject a release toolchain contract that can select a newer SDK."""
        path = self.repo / "global.json"
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload["sdk"]["rollForward"] = "latestFeature"
        path.write_text(json.dumps(payload) + "\n", encoding="ascii")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "disable .NET SDK roll-forward"):
            release_evidence.approved_dotnet_sdk(self.repo)

    def test_verify_rejects_artifact_tampering(self) -> None:
        """Reject package bytes changed after the canonical manifest was generated."""
        self._generate()
        package = self.root / "packages" / "Doka.EntityFrameworkCore.MySql.1.2.3.nupkg"
        package.write_bytes(b"tampered package")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "integrity check failed"):
            self._verify()

    def test_fixture_verification_isolated_from_hosted_environment(self) -> None:
        """Keep the local fixture independent from GitHub runner identity variables."""
        with mock.patch.dict(
            "os.environ",
            {
                "GITHUB_ACTIONS": "true",
                "GITHUB_REF": "refs/heads/main",
                "GITHUB_SHA": "hosted-runner-commit",
            },
        ):
            self._generate()
            self._verify()

    def test_generate_rejects_incomplete_engine_matrix(self) -> None:
        """Reject a release whose manifest would omit one advertised engine line."""
        missing = self.root / "specification" / "mariadb114" / "test-database-evidence.json"
        missing.unlink()

        with self.assertRaises(release_evidence.EvidenceError):
            self._generate()

    def test_generate_reconciles_identical_engine_evidence_from_two_lifecycles(self) -> None:
        """Retain every producer without treating its lifecycle as identity drift."""
        source = self.root / "specification" / "mysql84" / "test-database-evidence.json"
        evidence = json.loads(source.read_text(encoding="utf-8"))
        evidence["targets"][0]["source"] = "compose"
        destination = self.root / "migration-deployment" / "test-database-evidence.json"
        destination.parent.mkdir()
        destination.write_text(json.dumps(evidence), encoding="utf-8")

        self._generate()

        manifest = json.loads(
            (self.root / release_evidence.MANIFEST_NAME).read_text(encoding="utf-8")
        )
        mysql84 = next(
            engine for engine in manifest["engines"] if engine["targetId"] == "mysql84"
        )
        self.assertEqual("compose+testcontainers", mysql84["source"])

    def test_generate_rejects_conflicting_duplicate_engine_evidence(self) -> None:
        """Keep a second lifecycle from changing an already observed image."""
        source = self.root / "specification" / "mysql84" / "test-database-evidence.json"
        evidence = json.loads(source.read_text(encoding="utf-8"))
        evidence["targets"][0]["source"] = "compose"
        evidence["targets"][0]["image"] = f"mysql:8.4.11@sha256:{'7' * 64}"
        destination = self.root / "migration-deployment" / "test-database-evidence.json"
        destination.parent.mkdir()
        destination.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError,
            "Conflicting engine identities",
        ):
            self._generate()

    def test_generate_rejects_an_unqualified_paired_engine(self) -> None:
        """Reject a release whose paired comparison did not qualify.

        This is the case the removed historical scorecard test covered, moved
        to the evidence the tag now actually produces.
        """
        evaluation = next(
            (self.root / "performance").rglob("paired-evaluation.json")
        )
        payload = json.loads(evaluation.read_text(encoding="utf-8"))
        payload["qualification"] = "regression"
        evaluation.write_text(json.dumps(payload), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "regression"):
            self._generate()

    def test_generate_rejects_a_missing_paired_engine(self) -> None:
        """Refuse a release measured on fewer engines than the contract names."""
        import shutil

        shutil.rmtree(self.root / "performance" / "mysql84")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "missing engine"):
            self._generate()

    def test_generate_rejects_a_qualification_manifest_for_another_commit(self) -> None:
        """Refuse two documents that describe different releases.

        The manifest owns gate selection and this document owns the inventory.
        If they can disagree, neither is authoritative.
        """
        path = self.root / "release-qualification-manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["commit"] = "9" * 40
        path.write_text(json.dumps(manifest), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "not the release source"):
            self._generate()

    def test_generate_rejects_a_missing_qualification_manifest(self) -> None:
        """Refuse a candidate on which gate selection never happened."""
        (self.root / "release-qualification-manifest.json").unlink()

        with self.assertRaisesRegex(release_evidence.EvidenceError, "qualification manifest"):
            self._generate()

    def test_generate_rejects_mutable_engine_image(self) -> None:
        """Reject a release matrix that records a mutable tag without its digest."""
        path = self.root / "specification" / "mysql84" / "test-database-evidence.json"
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["targets"][0]["image"] = "mysql:8.4"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "not digest-pinned"):
            self._generate()

    def test_generate_rejects_incomplete_runtime_posture(self) -> None:
        """Reject publish-only evidence that never executes the trimmed binary."""
        path = self.root / release_evidence.RUNTIME_POSTURE_EVIDENCE
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["trimmedExecution"] = "not-run"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "full-trim execution"):
            self._generate()

    def test_generate_rejects_runtime_evidence_from_another_run(self) -> None:
        """Reject a green runtime artifact copied from an earlier candidate."""
        path = self.root / release_evidence.RUNTIME_POSTURE_EVIDENCE
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["runId"] = "earlier-run"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "does not match"):
            self._generate()

    def test_reuse_performance_accepts_an_unrelated_source_delta(self) -> None:
        """Retain expensive scorecards when only release evidence code changed."""
        measured_commit = release_evidence.run_command("git", "rev-parse", "HEAD", cwd=self.repo)
        prior_root = self.root / "prior"
        self._write_performance_evidence(prior_root, "prior-run", measured_commit)

        release_directory = self.repo / "eng" / "release"
        release_directory.mkdir(parents=True)
        (release_directory / "evidence.py").write_text("# validation change\n", encoding="ascii")
        self._git("add", "eng/release/evidence.py")
        self._git("commit", "-m", "test: change release validation")

        candidate_root = self.root / "candidate"
        release_evidence.reuse_performance_evidence(
            self.repo,
            prior_root,
            candidate_root,
            "candidate-run",
        )

        # The reuse receipt is the record. The separate re-validation this case
        # used to perform belonged to the historical release manifest, which no
        # longer inventories that evidence; what remains testable, and what
        # matters, is that reuse recorded exactly which paths moved and that
        # none of them were performance inputs.
        receipt = json.loads(
            (candidate_root / release_evidence.PERFORMANCE_REUSE_EVIDENCE).read_text(encoding="utf-8")
        )
        self.assertEqual(["eng/release/evidence.py"], receipt["changedPaths"])
        self.assertEqual([], receipt["performanceInputChanges"])

    def test_reuse_performance_rejects_a_provider_source_delta(self) -> None:
        """Never bind an earlier measurement to changed provider behavior."""
        measured_commit = release_evidence.run_command("git", "rev-parse", "HEAD", cwd=self.repo)
        prior_root = self.root / "prior"
        self._write_performance_evidence(prior_root, "prior-run", measured_commit)

        source_directory = self.repo / "src"
        source_directory.mkdir()
        (source_directory / "Provider.cs").write_text("internal sealed class Provider { }\n", encoding="ascii")
        self._git("add", "src/Provider.cs")
        self._git("commit", "-m", "test: change provider source")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "performance input changes"):
            release_evidence.reuse_performance_evidence(
                self.repo,
                prior_root,
                self.root / "candidate",
                "candidate-run",
            )

    def test_generate_rejects_incomplete_reconciliation(self) -> None:
        """Reject a candidate whose final index silently omits one named gate."""
        path = self.root / release_evidence.RECONCILIATION_EVIDENCE
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["gates"] = evidence["gates"][:-1]
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "gate mismatch"):
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
            self._LOCAL_RELEASE_ENVIRONMENT,
        ):
            release_evidence.write_manifest(arguments)

    def _verify(self) -> None:
        """Verify under the same explicit local identity used for generation."""
        with mock.patch.dict(
            "os.environ",
            self._LOCAL_RELEASE_ENVIRONMENT,
        ):
            release_evidence.verify_manifest(self.root, self.repo)

    def _write_complete_evidence(self) -> None:
        """Write the minimum complete package, dependency, and engine matrix."""
        packages = self.root / "packages"
        sbom = self.root / "sbom"
        sbom_components = self.root / "sbom-components" / "runtime"
        packages.mkdir(parents=True)
        sbom.mkdir(parents=True)
        sbom_components.mkdir(parents=True)
        for package_id in (
            "Doka.EntityFrameworkCore.MySql",
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
        ):
            (packages / f"{package_id}.1.2.3.nupkg").write_bytes(f"{package_id} package".encode("ascii"))
            (packages / f"{package_id}.1.2.3.snupkg").write_bytes(f"{package_id} symbols".encode("ascii"))
        (sbom / "manifest.spdx.json").write_text('{"spdxVersion":"SPDX-2.3"}\n', encoding="ascii")
        (sbom_components / "project.assets.json").write_text("{}\n", encoding="ascii")

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
            "mysql84": ("MySql", "mysql:8.4", f"mysql:8.4.11@sha256:{'8' * 64}"),
            "mysql97": ("MySql", "mysql:9.7", f"mysql:9.7.2@sha256:{'9' * 64}"),
            "mariadb1011": (
                "MariaDb",
                "mariadb:10.11",
                f"mariadb:10.11.18@sha256:{'0' * 64}",
            ),
            "mariadb114": ("MariaDb", "mariadb:11.4", f"mariadb:11.4.12@sha256:{'1' * 64}"),
            "mariadb118": ("MariaDb", "mariadb:11.8", f"mariadb:11.8.8@sha256:{'2' * 64}"),
            "mariadb123": ("MariaDb", "mariadb:12.3", f"mariadb:12.3.2@sha256:{'3' * 64}"),
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

        source_commit = release_evidence.run_command("git", "rev-parse", "HEAD", cwd=self.repo)
        self._write_qualification_manifest(source_commit)
        self._write_paired_performance(source_commit)
        runtime_directory = self.root / "runtime"
        runtime_directory.mkdir()
        (runtime_directory / "runtime-posture-evidence.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "runId": "test-run",
                    "source": {
                        "commit": source_commit,
                        "treeState": "clean",
                    },
                    "target": {
                        "targetId": "mysql84",
                        "image": f"mysql:8.4.11@sha256:{'8' * 64}",
                    },
                    "runtimeIdentifier": "linux-x64",
                    "dotnetSdk": release_evidence.run_command(
                        "dotnet",
                        "--version",
                        cwd=self.repo,
                    ),
                    "configuration": "Release",
                    "ordinaryExecution": "pass",
                    "publish": {
                        "selfContained": True,
                        "publishTrimmed": True,
                        "trimMode": "full",
                        "status": "pass",
                    },
                    "trimmedExecution": "pass",
                    "executable": {
                        "sha256": "a" * 64,
                        "sizeBytes": 4096,
                    },
                }
            ),
            encoding="utf-8",
        )
        # The inventory comes from the policy the validator reads, so this
        # fixture cannot describe a release the validator would reject for a
        # reason no producer could ever cause.
        (self.root / "release-candidate-reconciliation.json").write_text(
            json.dumps(
                {
                    "schemaVersion": release_evidence.RECONCILIATION_SCHEMA_VERSION,
                    "runId": "test-run",
                    "sourceCommit": source_commit,
                    "gates": [
                        {"id": gate_id, "status": "pass"}
                        for gate_id in release_evidence.required_reconciliation_gates()
                    ],
                }
            ),
            encoding="utf-8",
        )
        self._write_performance_evidence(self.root, "test-run", source_commit)

    def _write_qualification_manifest(self, source_commit: str) -> None:
        """Write the manifest the release document is bound to.

        Gate selection belongs to the qualification manifest; this document
        owns the artifact inventory. Writing both here is what lets the test
        prove they cannot disagree.
        """
        # The fixture repository is a temporary tree; the policy is the one
        # this repository ships, so the manifest under test is bound to the
        # gates a release actually declares.
        policy = json.loads(
            (
                Path(release_evidence.__file__).resolve().parent
                / "evidence-policy.json"
            ).read_text(encoding="utf-8")
        )
        (self.root / "release-qualification-manifest.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "kind": "release-qualification-manifest",
                    "policyVersion": policy["policyVersion"],
                    "policyDigest": "d" * 64,
                    "selectionRuleVersion": policy["selectionRule"]["version"],
                    "repository": "doka-labs/Doka.EntityFrameworkCore.MySql",
                    "commit": source_commit,
                    "treeId": "e" * 40,
                    "releaseTag": "v1.2.3",
                    "releaseVersion": "1.2.3",
                    "assemblingRunAttempt": 1,
                    "requiredProtectedChecks": policy["requiredProtectedChecks"],
                    "gates": [
                        {"gate": gate["id"], "kind": gate["kind"]}
                        for gate in policy["gates"]
                    ],
                }
            ),
            encoding="utf-8",
        )

    def _write_paired_performance(self, source_commit: str) -> None:
        """Write one qualified paired evaluation per required engine."""
        for target in release_evidence.PERFORMANCE_TARGETS:
            directory = self.root / "performance" / target / "reports" / "run-1"
            directory.mkdir(parents=True)
            (directory / "paired-evaluation.json").write_text(
                json.dumps(
                    {
                        "schemaVersion": 2,
                        "kind": "paired-performance-evaluation",
                        "target": target,
                        "profile": "paired-block",
                        "runId": f"run-1-{target}",
                        "commit": source_commit,
                        "sourceHash": "f" * 64,
                        "runnerClass": "test-runner",
                        "qualification": "qualified",
                        "success": True,
                    }
                ),
                encoding="utf-8",
            )

    def _write_performance_evidence(
        self,
        root: Path,
        run_id: str,
        source_commit: str,
    ) -> None:
        """Write minimal internally hashed scorecards for both release targets."""
        source_hash = release_evidence.clean_performance_source_hash(source_commit)
        for target in release_evidence.PERFORMANCE_TARGETS:
            target_root = root / "performance" / target
            evidence_directory = target_root / "evidence"
            results_directory = target_root / "results"
            evidence_directory.mkdir(parents=True)
            results_directory.mkdir()

            evidence_files = {
                "benchmarkDotNet": evidence_directory / "gate-benchmarkdotnet-evidence.json",
                "hostPreflight": evidence_directory / "host-preflight.json",
                "soak": evidence_directory / "soak-evidence.json",
                "workloads": evidence_directory / "workload-evidence.json",
            }
            for artifact_id, path in evidence_files.items():
                path.write_text(json.dumps({"kind": artifact_id}) + "\n", encoding="utf-8")

            raw_report = results_directory / "Benchmark-report-full.json"
            raw_report.write_text('{"benchmarks":[]}\n', encoding="ascii")
            evaluation = {
                "schemaVersion": 3,
                "runId": run_id,
                "target": target,
                "profile": "scorecard",
                "mode": "compare",
                "success": True,
                "commit": source_commit,
                "sourceHash": source_hash,
                "artifactHashes": {
                    artifact_id: release_evidence.sha256(path)
                    for artifact_id, path in evidence_files.items()
                },
                "rawReports": [
                    {
                        "path": "results/Benchmark-report-full.json",
                        "sha256": release_evidence.sha256(raw_report),
                    }
                ],
            }
            (evidence_directory / "gate-performance-evaluation.json").write_text(
                json.dumps(evaluation),
                encoding="utf-8",
            )

    def test_accepted_pair_identity_allows_one_run_per_measurement_job(self) -> None:
        """Accept a baseline pair whose engines were measured in separate jobs.

        The benchmark matrix runs one job per engine and names that job in the
        run identifier, so the accepted pair can never share one. Requiring it
        blocked every release candidate at the readiness gate, after the same
        assumption had already been removed from baseline promotion.
        """
        entries = [
            {
                "target": "mysql84",
                "commit": "a" * 40,
                "sourceHash": "b" * 64,
                "runId": "github-1000-mysql84-attempt-1",
            },
            {
                "target": "mariadb118",
                "commit": "a" * 40,
                "sourceHash": "b" * 64,
                "runId": "github-1000-mariadb118-attempt-2",
            },
        ]

        identity = release_evidence.accepted_pair_identity(entries)

        self.assertEqual({"a" * 40}, identity["commit"])
        self.assertEqual({"b" * 64}, identity["sourceHash"])

    def test_accepted_pair_identity_rejects_a_divergent_source(self) -> None:
        """Reject a pair whose engines did not measure the same software."""
        entries = [
            {"target": "mysql84", "commit": "a" * 40, "sourceHash": "b" * 64},
            {"target": "mariadb118", "commit": "c" * 40, "sourceHash": "b" * 64},
        ]

        with self.assertRaisesRegex(
            release_evidence.EvidenceError,
            "inconsistent identity field\\(s\\): commit",
        ):
            release_evidence.accepted_pair_identity(entries)

    def test_accepted_baseline_in_the_repository_passes_the_identity_gate(self) -> None:
        """Keep the checked-in baseline acceptable to the release readiness gate.

        The gate runs against this exact file during a release, so a rollover
        that produced an unacceptable pair has to fail here rather than on the
        tag.
        """
        baseline = json.loads(
            (
                Path(__file__).resolve().parents[2]
                / "benchmarks"
                / "baselines"
                / "doka-benchmark-baseline.json"
            ).read_text(encoding="utf-8")
        )

        identity = release_evidence.accepted_pair_identity(baseline["baselines"])

        self.assertEqual(1, len(identity["commit"]))
        self.assertEqual(1, len(identity["sourceHash"]))

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
