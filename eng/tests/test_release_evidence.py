"""Regression tests for the immutable release-candidate evidence boundary.

The suite creates an isolated untagged Git repository and exercises both valid
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

from eng.release import evidence as release_evidence, lines
from eng.tests.test_release_lines import ga_specification_baseline, initial_policy


def release_line(source_ref: str = "refs/heads/main") -> dict[str, object]:
    """Return a frozen admission fixture, never a publication authority."""
    return {
        "schemaVersion": 1,
        "repository": "doka-labs/Doka.EntityFrameworkCore.MySql",
        "sourceRef": source_ref,
        "role": "main" if source_ref == "refs/heads/main" else "maintenance",
        "packageMajor": 10,
        "channel": "stable",
        "upstreamStage": "ga",
        "stateAtQualification": "active",
        "workflowPath": ".github/workflows/release-candidate.yml",
        "publisherEnvironment": "nuget",
        "authorizationRevision": "2026-10-22.1",
        "authorizationDigest": "a" * 64,
    }


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
        """Create an untagged repository with an ignored evidence fixture."""
        self._temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-release-evidence-"
        )
        self.repo = Path(self._temporary_directory.name)
        self.root = self.repo / "evidence"
        self._git("init", "--initial-branch=main")
        self._git("config", "user.name", "Doka Test")
        self._git("config", "user.email", "doka-test@example.invalid")
        self._git("config", "commit.gpgSign", "false")
        self._git("config", "tag.gpgSign", "false")
        self._git(
            "remote",
            "add",
            "origin",
            "https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql.git",
        )
        # Evidence remains ignored so negative tests can mutate it without
        # turning an artifact-integrity failure into a source-dirty failure.
        # WHY: These are historical GA-10 scenarios even on an RC-11 checkout.
        # Simulate only their SDK observation; Git identity probes remain real.
        dotnet_sdk = "10.0.402"
        original_run_command = release_evidence.run_command

        def run_fixture_command(*arguments: str, cwd: Path | None = None) -> str:
            """Supply the owned fixture's SDK while forwarding every other probe."""
            if arguments == ("dotnet", "--version") and cwd is not None and cwd.resolve() == self.repo.resolve():
                return dotnet_sdk

            return original_run_command(*arguments, cwd=cwd)

        sdk_probe = mock.patch.object(release_evidence, "run_command", side_effect=run_fixture_command)
        sdk_probe.start()
        self.addCleanup(sdk_probe.stop)
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
        (self.repo / "Directory.Build.props").write_text(
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n",
            encoding="ascii",
        )
        (self.repo / "Directory.Packages.props").write_text(
            "<Project><PropertyGroup><DokaEfCoreVersion>"
            "[10.0.8, 10.1.0)</DokaEfCoreVersion></PropertyGroup></Project>\n",
            encoding="ascii",
        )
        # WHY: Keep the reviewed GA-10 versions independent of the active branch's
        # specification inventory; full suite validation belongs to its own gate.
        baseline = self.repo / lines.SPECIFICATION_BASELINE_PATH
        baseline.parent.mkdir(parents=True)
        baseline.write_text(json.dumps(ga_specification_baseline()), encoding="ascii")
        self._git("add", ".gitignore", "global.json", "source.txt", "Directory.Build.props",
                  "Directory.Packages.props", str(lines.SPECIFICATION_BASELINE_PATH))
        self._git("commit", "-m", "test: seed release source")
        self._write_complete_evidence()

    def tearDown(self) -> None:
        """Dispose the isolated repository fixture."""
        self._temporary_directory.cleanup()

    def test_generate_and_verify_bind_every_portable_artifact(self) -> None:
        """Generate canonical relative paths and verify the detached checksum."""
        self._generate()

        self._verify()

        manifest = json.loads(
            (self.root / release_evidence.MANIFEST_NAME).read_text(encoding="utf-8")
        )
        paths = [artifact["path"] for artifact in manifest["artifacts"]]
        self.assertEqual(sorted(paths), paths)
        self.assertTrue(all(not Path(path).is_absolute() for path in paths))
        self.assertIn("release-qualification-manifest.json", paths)
        selection_path = "artifact-selections/assemble-input-artifacts.json"
        self.assertIn(selection_path, paths)
        selection = next(
            artifact
            for artifact in manifest["artifacts"]
            if artifact["path"] == selection_path
        )
        self.assertEqual("verification-evidence", selection["role"])
        self.assertEqual("refs/heads/main", manifest["source"]["ref"])
        self.assertIsNone(manifest["source"]["tag"])
        self.assertEqual("clean", manifest["source"]["treeState"])
        self.assertEqual(
            manifest["toolchain"]["approvedDotnetSdk"],
            manifest["toolchain"]["dotnetSdk"],
        )
        self.assertEqual(
            "2.5.0", manifest["toolchain"]["resolvedPackages"]["MySqlConnector"]
        )

        self.assertEqual(
            list(release_evidence.REQUIRED_ENGINE_TARGETS),
            [engine["targetId"] for engine in manifest["engines"]],
        )
        # Gate selection lives in the qualification manifest; this document
        # binds itself to it so the two cannot describe different releases.
        self.assertEqual(
            "v10.2.3",
            manifest["qualification"]["expectedReleaseTag"],
        )
        self.assertTrue(manifest["qualification"]["gates"])
        self.assertTrue(manifest["runtimePosture"]["publishTrimmed"])
        self.assertEqual("full", manifest["runtimePosture"]["trimMode"])
        self.assertNotIn("performanceEvidence", manifest)
        self.assertNotIn("runnerClass", manifest["workflow"])
        self.assertEqual("local", manifest["workflow"]["runnerName"])
        self.assertEqual(
            list(release_evidence.required_reconciliation_gates()),
            sorted(manifest["verificationReconciliation"]),
        )

    def test_generate_rejects_dirty_release_source(self) -> None:
        """Reject a candidate whose source differs from the reviewed commit."""
        (self.repo / "source.txt").write_text("unreviewed source\n", encoding="ascii")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "clean Git worktree"
        ):
            self._generate()

    def test_source_accepts_the_exact_frozen_maintenance_ref(self) -> None:
        """Allow an admitted line without making sibling branches eligible."""
        # Arrange
        self._git("switch", "-c", "maintenance/10.x")
        snapshot = release_line("refs/heads/maintenance/10.x")

        # Act
        with mock.patch.dict("os.environ", self._LOCAL_RELEASE_ENVIRONMENT):
            source = release_evidence.git_source(
                self.repo, snapshot["sourceRef"], release_line=snapshot
            )

        # Assert
        self.assertEqual(snapshot["sourceRef"], source["ref"])

    def test_source_rejects_a_different_frozen_line(self) -> None:
        """Do not let a valid maintenance grant authorize the main checkout."""
        # Arrange
        snapshot = release_line("refs/heads/maintenance/10.x")

        # Act / Assert
        with mock.patch.dict("os.environ", self._LOCAL_RELEASE_ENVIRONMENT):
            with self.assertRaisesRegex(release_evidence.EvidenceError, "source ref"):
                release_evidence.git_source(
                    self.repo, "refs/heads/main", release_line=snapshot
                )

    def test_preview_sdk_requires_an_explicit_preview_line(self) -> None:
        """Permit an exact prerelease SDK only for admitted upstream previews."""
        # Arrange
        path = self.repo / "global.json"
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload["sdk"].update(version="11.0.100-rc.2.25451.107", allowPrerelease=True)
        path.write_text(json.dumps(payload), encoding="ascii")
        snapshot = {**release_line(), "upstreamStage": "rc"}

        # Act
        sdk = release_evidence.approved_dotnet_sdk(self.repo, snapshot)

        # Assert
        self.assertEqual("11.0.100-rc.2.25451.107", sdk)

    def test_portable_evidence_rejects_a_cross_major_line(self) -> None:
        """Keep a validly checksummed manifest bound to its package major."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["releaseLine"]["packageMajor"] = 11
        path.write_text(json.dumps(manifest), encoding="ascii")
        self._reseal_manifest()

        # Act / Assert
        with self.assertRaisesRegex(release_evidence.EvidenceError, "line identity"):
            release_evidence.verify_manifest(self.root, None)

    def test_portable_evidence_accepts_historical_schema_three_main(self) -> None:
        """Read old proof explicitly without requiring a modern authority grant."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["schemaVersion"] = 3
        del manifest["releaseLine"]
        path.write_text(json.dumps(manifest), encoding="ascii")
        self._reseal_manifest()

        # Act
        result = release_evidence.verify_manifest(self.root, None)

        # Assert
        self.assertIsNone(result)

    def test_portable_historical_evidence_cannot_claim_maintenance(self) -> None:
        """Do not reinterpret a legacy source assertion as a branch grant."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["schemaVersion"] = 3
        del manifest["releaseLine"]
        manifest["source"]["ref"] = "refs/heads/maintenance/10.x"
        path.write_text(json.dumps(manifest), encoding="ascii")
        self._reseal_manifest()

        # Act / Assert
        with self.assertRaisesRegex(release_evidence.EvidenceError, "Historical"):
            release_evidence.verify_manifest(self.root, None)

    def test_portable_evidence_rejects_a_different_target_framework(self) -> None:
        """A checksum rewrite cannot hide framework drift from the frozen line."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["toolchain"]["targetFramework"] = "net11.0"
        path.write_text(json.dumps(manifest), encoding="ascii")
        self._reseal_manifest()

        # Act / Assert
        with self.assertRaisesRegex(release_evidence.EvidenceError, "toolchain"):
            release_evidence.verify_manifest(self.root, None)

    def test_portable_evidence_rejects_a_different_upstream_stage(self) -> None:
        """Keep GA evidence from borrowing a prerelease toolchain identity."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["toolchain"]["upstreamStage"] = "rc"
        path.write_text(json.dumps(manifest), encoding="ascii")
        self._reseal_manifest()

        # Act / Assert
        with self.assertRaisesRegex(release_evidence.EvidenceError, "toolchain"):
            release_evidence.verify_manifest(self.root, None)

    def test_hosted_source_admission_accepts_independently_observed_authority(self) -> None:
        """Consume the exact frozen grant handed across the preflight boundary."""
        # Arrange
        environment, expected = self._admission_fixture()

        # Act
        with mock.patch.dict("os.environ", environment):
            actual = release_evidence.admitted_release_line(self.repo, "refs/heads/main", "10.2.3")

        # Assert
        self.assertEqual(expected, actual)

    def test_hosted_source_admission_rejects_a_tampered_authority_observation(self) -> None:
        """A branch-local policy copy cannot stand in for current authority."""
        # Arrange
        environment, _ = self._admission_fixture()
        path = Path(environment["DOKA_RELEASE_LINE_AUTHORITY"])
        authority = json.loads(path.read_text(encoding="utf-8"))
        authority["authorizationDigest"] = "0" * 64
        path.write_text(json.dumps(authority), encoding="ascii")

        # Act / Assert
        with mock.patch.dict("os.environ", environment):
            with self.assertRaisesRegex(release_evidence.EvidenceError, "authority is invalid"):
                release_evidence.admitted_release_line(self.repo, "refs/heads/main", "10.2.3")

    def test_hosted_source_admission_never_falls_back_to_checkout_policy(self) -> None:
        """Missing protected-main observation fails before any qualification gate."""
        # Arrange
        environment, _ = self._admission_fixture()
        environment["DOKA_RELEASE_LINE_AUTHORITY"] = ""

        # Act / Assert
        with mock.patch.dict("os.environ", environment):
            with self.assertRaisesRegex(release_evidence.EvidenceError, "protected-main authority observation"):
                release_evidence.admitted_release_line(self.repo, "refs/heads/main", "10.2.3")

    def _admission_fixture(self) -> tuple[dict[str, str], dict[str, object]]:
        """Write an explicit observation of a reviewed protected-main policy."""
        policy = initial_policy()
        snapshot = release_evidence.release_lines.freeze_line(policy, "refs/heads/main", "10.2.3")
        authority = {
            "schemaVersion": 1,
            "kind": "release-line-authority-observation",
            "repository": policy["repository"],
            "controlCommit": "a" * 40,
            "authorizationRevision": policy["revision"],
            "authorizationDigest": release_evidence.release_lines.policy_digest(policy),
            "policy": policy,
            "observedUtc": "2026-10-22T12:00:00Z",
        }
        files = {}
        for name, payload in (("line", snapshot), ("policy", policy), ("authority", authority)):
            path = self.root / f"{name}.json"
            path.write_text(json.dumps(payload), encoding="ascii")
            files[name] = str(path)

        return {
            "GITHUB_ACTIONS": "true",
            "DOKA_RELEASE_LINE_FILE": files["line"],
            "DOKA_RELEASE_LINES_POLICY": files["policy"],
            "DOKA_RELEASE_LINE_AUTHORITY": files["authority"],
            "DOKA_RELEASE_WORKFLOW_PATH": snapshot["workflowPath"],
        }, snapshot

    def _reseal_manifest(self) -> None:
        """Model an adversary able to rewrite the detached integrity checksum."""
        path = self.root / release_evidence.MANIFEST_NAME
        (self.root / release_evidence.CHECKSUM_NAME).write_text(
            f"{release_evidence.sha256(path)}  {release_evidence.MANIFEST_NAME}\n",
            encoding="ascii",
        )

    def test_portable_manifest_requires_its_resolved_dependency_graph(self) -> None:
        """A sealed candidate cannot omit the graph that qualification resolved."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="ascii"))
        del manifest["toolchain"]["resolvedPackages"]
        path.write_text(json.dumps(manifest) + "\n", encoding="ascii")
        self._reseal_manifest()

        # Act
        with self.assertRaises(release_evidence.EvidenceError) as failure:
            release_evidence.verify_manifest(self.root, None)

        # Assert
        self.assertIn("resolved dependency graph", str(failure.exception))

    def test_portable_manifest_cannot_disable_graph_validation_with_null(self) -> None:
        """Optional source preflight data is mandatory in the portable evidence."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="ascii"))
        manifest["toolchain"]["resolvedPackages"] = None
        path.write_text(json.dumps(manifest) + "\n", encoding="ascii")
        self._reseal_manifest()

        # Act
        with self.assertRaises(release_evidence.EvidenceError) as failure:
            release_evidence.verify_manifest(self.root, None)

        # Assert
        self.assertIn("resolved dependency graph", str(failure.exception))

    def test_portable_manifest_requires_an_explicit_source_ref(self) -> None:
        """Missing/null cannot disable the frozen line's source-ref binding."""
        for remove in (True, False):
            with self.subTest(missing=remove):
                # Arrange
                self._generate()
                path = self.root / release_evidence.MANIFEST_NAME
                manifest = json.loads(path.read_text(encoding="ascii"))
                if remove:
                    del manifest["source"]["ref"]
                else:
                    manifest["source"]["ref"] = None

                path.write_text(json.dumps(manifest) + "\n", encoding="ascii")
                self._reseal_manifest()

                # Act
                with self.assertRaises(release_evidence.EvidenceError) as failure:
                    release_evidence.verify_manifest(self.root, None)

                # Assert
                self.assertIn("explicit source ref", str(failure.exception))

    def test_portable_manifest_graph_must_match_its_sealed_dependency_artifact(self) -> None:
        """An in-range but invented graph is not the graph that was qualified."""
        # Arrange
        self._generate()
        path = self.root / release_evidence.MANIFEST_NAME
        manifest = json.loads(path.read_text(encoding="ascii"))
        for package in manifest["toolchain"]["resolvedPackages"]:
            if package.startswith("Microsoft.EntityFrameworkCore"):
                manifest["toolchain"]["resolvedPackages"][package] = "10.0.9"

        path.write_text(json.dumps(manifest) + "\n", encoding="ascii")
        self._reseal_manifest()

        # Act
        with self.assertRaises(release_evidence.EvidenceError) as failure:
            release_evidence.verify_manifest(self.root, None)

        # Assert
        self.assertIn("sealed dependency artifact", str(failure.exception))

    def test_sdk_contract_rejects_roll_forward(self) -> None:
        """Reject a release toolchain contract that can select a newer SDK."""
        path = self.repo / "global.json"
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload["sdk"]["rollForward"] = "latestFeature"
        path.write_text(json.dumps(payload) + "\n", encoding="ascii")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "disable .NET SDK roll-forward"
        ):
            release_evidence.approved_dotnet_sdk(self.repo)

    def test_generate_rejects_a_different_observed_sdk(self) -> None:
        """Keep fixture-local SDK simulation subject to the exact identity guard."""
        # Arrange
        original_probe = release_evidence.run_command

        def wrong_sdk(*arguments: str, cwd: Path | None = None) -> str:
            """Change only the owned fixture's SDK observation."""
            if arguments == ("dotnet", "--version") and cwd is not None and cwd.resolve() == self.repo.resolve():
                return "10.0.999"

            return original_probe(*arguments, cwd=cwd)

        # Act
        with mock.patch.object(release_evidence, "run_command", side_effect=wrong_sdk):
            with self.assertRaises(release_evidence.EvidenceError) as failure:
                self._generate()

        # Assert
        self.assertIn("active .NET SDK 10.0.999 does not match the approved SDK 10.0.402", str(failure.exception))

    def test_sdk_contract_rejects_prerelease_version(self) -> None:
        """Reject an exact SDK identity that is not a stable release."""
        path = self.repo / "global.json"
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload["sdk"]["version"] = "11.0.100-rc.1.25451.107"
        path.write_text(json.dumps(payload) + "\n", encoding="ascii")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "exact stable .NET SDK version"
        ):
            release_evidence.approved_dotnet_sdk(self.repo)

    def test_verify_rejects_artifact_tampering(self) -> None:
        """Reject package bytes changed after the canonical manifest was generated."""
        self._generate()
        package = self.root / "packages" / "Doka.EntityFrameworkCore.MySql.10.2.3.nupkg"
        package.write_bytes(b"tampered package")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "integrity check failed"
        ):
            self._verify()

    def test_fixture_verification_isolated_from_hosted_environment(self) -> None:
        """Keep the local fixture independent from GitHub runner identity variables."""
        with mock.patch.dict(
            "os.environ",
            {
                "GITHUB_ACTIONS": "true",
                "GITHUB_REF": "refs/heads/main",
                "GITHUB_RUN_ATTEMPT": "3",
                "GITHUB_RUN_ID": "1000002080",
                "GITHUB_SHA": "hosted-runner-commit",
                "GITHUB_WORKFLOW": "CI",
                "GITHUB_WORKFLOW_REF": "doka/repo/.github/workflows/ci.yml@main",
                "GITHUB_REPOSITORY": "doka/repo",
                "RUNNER_ARCH": "X64",
                "RUNNER_NAME": "GitHub Actions 1000002080",
                "RUNNER_OS": "Linux",
            },
        ):
            self._generate()
            self._verify()

        manifest = json.loads(
            (self.root / release_evidence.MANIFEST_NAME).read_text(encoding="utf-8")
        )
        self.assertEqual("local", manifest["workflow"]["provider"])
        self.assertEqual("test-run", manifest["workflow"]["runId"])
        self.assertEqual("local", manifest["workflow"]["runnerName"])

    def test_generate_rejects_incomplete_engine_matrix(self) -> None:
        """Reject a release whose manifest would omit one advertised engine line."""
        missing = (
            self.root / "specification" / "mariadb114" / "test-database-evidence.json"
        )
        missing.unlink()

        with self.assertRaises(release_evidence.EvidenceError):
            self._generate()

    def test_generate_reconciles_identical_engine_evidence_from_two_lifecycles(
        self,
    ) -> None:
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

    def test_generate_rejects_a_qualification_manifest_for_another_commit(self) -> None:
        """Refuse two documents that describe different releases.

        The manifest owns gate selection and this document owns the inventory.
        If they can disagree, neither is authoritative.
        """
        path = self.root / "release-qualification-manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        manifest["commit"] = "9" * 40
        path.write_text(json.dumps(manifest), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "not the release source"
        ):
            self._generate()

    def test_generate_rejects_a_missing_qualification_manifest(self) -> None:
        """Refuse a candidate on which gate selection never happened."""
        (self.root / "release-qualification-manifest.json").unlink()

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "qualification manifest"
        ):
            self._generate()

    def test_generate_rejects_mutable_engine_image(self) -> None:
        """Reject a release matrix that records a mutable tag without its digest."""
        path = self.root / "specification" / "mysql84" / "test-database-evidence.json"
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["targets"][0]["image"] = "mysql:8.4"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "not digest-pinned"
        ):
            self._generate()

    def test_generate_rejects_incomplete_runtime_posture(self) -> None:
        """Reject publish-only evidence that never executes the trimmed binary."""
        path = self.root / release_evidence.RUNTIME_POSTURE_EVIDENCE
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["trimmedExecution"] = "not-run"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "full-trim execution"
        ):
            self._generate()

    def test_generate_rejects_a_full_floor_matrix_rerun(self) -> None:
        """Keep the protected floor proof distinct from candidate-only latest work."""
        path = (
            self.root
            / "efcore-patch-matrix"
            / "minimum-10-0-8"
            / "efcore-contract-evidence.json"
        )
        receipt = json.loads(path.read_text(encoding="utf-8"))
        receipt["validationScope"] = "full"
        path.write_text(json.dumps(receipt), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "invalid validationScope"
        ):
            self._generate()

    def test_generate_rejects_an_unbound_latest_dependency_graph(self) -> None:
        """Reject a receipt whose resolved version differs from its graph bytes."""
        path = (
            self.root
            / "efcore-patch-matrix"
            / "latest-10-0"
            / "efcore-contract-evidence.json"
        )
        receipt = json.loads(path.read_text(encoding="utf-8"))
        receipt["requestedVersion"] = "10.0.10"
        receipt["resolvedVersion"] = "10.0.10"
        path.write_text(json.dumps(receipt), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "dependency graph does not match"
        ):
            self._generate()

    def test_generate_rejects_a_floating_release_dependency_request(self) -> None:
        """Keep upstream publication from changing a reviewed release graph."""
        path = (
            self.root
            / "efcore-patch-matrix"
            / "latest-10-0"
            / "efcore-contract-evidence.json"
        )
        receipt = json.loads(path.read_text(encoding="utf-8"))
        receipt["requestedVersion"] = "10.0.*"
        path.write_text(json.dumps(receipt), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "requested an unexpected version"
        ):
            self._generate()

    def test_generate_rejects_a_different_requested_and_resolved_patch(self) -> None:
        """Bind the requested release patch to the exact resolved graph."""
        path = (
            self.root
            / "efcore-patch-matrix"
            / "latest-10-0"
            / "efcore-contract-evidence.json"
        )
        receipt = json.loads(path.read_text(encoding="utf-8"))
        receipt["requestedVersion"] = "10.0.11"
        path.write_text(json.dumps(receipt), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError,
            "did not request its exact resolved version",
        ):
            self._generate()

    def test_generate_rejects_malformed_efcore_matrix_documents(self) -> None:
        """Keep malformed matrix JSON inside the typed evidence failure boundary."""
        directory = self.root / "efcore-patch-matrix" / "latest-10-0"
        documents = (
            (directory / "efcore-contract-evidence.json", []),
            (directory / "resolved-packages.json", {"projects": [None]}),
        )
        for path, malformed in documents:
            with self.subTest(path=path.name):
                original = path.read_text(encoding="utf-8")
                path.write_text(json.dumps(malformed), encoding="utf-8")
                with self.assertRaisesRegex(
                    release_evidence.EvidenceError,
                    "EF Core matrix evidence is invalid|dependency graph is invalid",
                ):
                    self._generate()
                path.write_text(original, encoding="utf-8")

    def test_generate_rejects_a_floor_without_repository_qualification(self) -> None:
        """Do not accept a graph-only floor row without its behavioral proof."""
        path = self.root / "release-qualification-manifest.json"
        qualification = json.loads(path.read_text(encoding="utf-8"))
        qualification["gates"] = [
            gate
            for gate in qualification["gates"]
            if gate["gate"] != "repository-qualification"
        ]
        path.write_text(json.dumps(qualification), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError,
            "requires commit-exact repository-qualification",
        ):
            self._generate()

    def test_generate_rejects_missing_latest_specification_results(self) -> None:
        """Require retained TRX and engine evidence for each declared target."""
        path = (
            self.root
            / "efcore-patch-matrix"
            / "latest-10-0"
            / "mysql84"
            / "results.trx"
        )
        path.unlink()

        with self.assertRaisesRegex(
            release_evidence.EvidenceError,
            "incomplete specification evidence for mysql84",
        ):
            self._generate()

    def test_generate_rejects_failed_latest_integration_results(self) -> None:
        """Do not let a receipt claim integration work its retained result failed."""
        path = (
            self.root
            / "efcore-patch-matrix"
            / "latest-10-0"
            / "integration"
            / "compatibility-matrix-evidence.json"
        )
        integration = json.loads(path.read_text(encoding="utf-8"))
        integration["testExitCode"] = 1
        path.write_text(json.dumps(integration), encoding="utf-8")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError,
            "latest integration evidence is invalid",
        ):
            self._generate()

    def test_generate_rejects_runtime_evidence_from_another_run(self) -> None:
        """Reject a green runtime artifact copied from an earlier candidate."""
        path = self.root / release_evidence.RUNTIME_POSTURE_EVIDENCE
        evidence = json.loads(path.read_text(encoding="utf-8"))
        evidence["runId"] = "earlier-run"
        path.write_text(json.dumps(evidence), encoding="utf-8")

        with self.assertRaisesRegex(release_evidence.EvidenceError, "does not match"):
            self._generate()

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

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "package inventory mismatch"
        ):
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

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "Unexpected engine evidence"
        ):
            self._generate()

    def test_generate_rejects_a_premature_semantic_release_tag(self) -> None:
        """Reject a candidate commit whose release identity already exists."""
        self._git("tag", "v1.2.4")

        with self.assertRaisesRegex(
            release_evidence.EvidenceError, "must be untagged"
        ):
            self._generate()

    def test_verify_cli_accepts_one_root_argument(self) -> None:
        """Keep the real verify command-line contract constructible and unambiguous."""
        arguments = ["release_evidence.py", "verify", "--root", str(self.root)]

        with mock.patch.object(sys, "argv", arguments):
            parsed = release_evidence.parse_arguments()

        self.assertEqual("verify", parsed.command)
        self.assertEqual(self.root, parsed.root)

    def test_source_check_cli_accepts_untagged_local_main(self) -> None:
        """Exercise the early orchestrator guard without starting any gate."""
        # Arrange
        arguments = [
            "evidence.py", "source-check", "--repo", str(self.repo),
            "--source-ref", "refs/heads/main", "--release-version", "10.2.3",
        ]

        # Act
        with mock.patch.dict("os.environ", self._LOCAL_RELEASE_ENVIRONMENT):
            with mock.patch.object(sys, "argv", arguments):
                result = release_evidence.main()

        # Assert
        self.assertEqual(0, result)

    def test_source_check_cli_rejects_an_unadmitted_feature_branch(self) -> None:
        """A matching expected-ref string is not release authorization."""
        # Arrange
        self._git("switch", "-c", "feature/unadmitted")
        arguments = [
            "evidence.py", "source-check", "--repo", str(self.repo),
            "--source-ref", "refs/heads/feature/unadmitted", "--release-version", "10.2.3",
        ]

        # Act
        with mock.patch.dict("os.environ", self._LOCAL_RELEASE_ENVIRONMENT):
            with mock.patch.object(sys, "argv", arguments):
                result = release_evidence.main()

        # Assert
        self.assertEqual(1, result)

    def _generate(self) -> None:
        """Generate untagged release evidence under an explicit local identity."""
        arguments = SimpleNamespace(
            repo=self.repo,
            root=self.root,
            run_id="test-run",
            release_version="10.2.3",
            expected_release_tag="v10.2.3",
            dependency_graph=self.root / "resolved-packages.json",
            expected_ref="refs/heads/main",
            release_line=release_line(),
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
        artifact_selections = self.root / "artifact-selections"
        packages.mkdir(parents=True)
        sbom.mkdir(parents=True)
        sbom_components.mkdir(parents=True)
        artifact_selections.mkdir(parents=True)
        (artifact_selections / "assemble-input-artifacts.json").write_text(
            '{"schemaVersion":1}\n',
            encoding="ascii",
        )
        for package_id in (
            "Doka.EntityFrameworkCore.MySql",
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
            "Doka.Caching.MySql",
        ):
            (packages / f"{package_id}.10.2.3.nupkg").write_bytes(
                f"{package_id} package".encode("ascii")
            )
            (packages / f"{package_id}.10.2.3.snupkg").write_bytes(
                f"{package_id} symbols".encode("ascii")
            )
        (sbom / "manifest.spdx.json").write_text(
            '{"spdxVersion":"SPDX-2.3"}\n', encoding="ascii"
        )
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
            "mariadb114": (
                "MariaDb",
                "mariadb:11.4",
                f"mariadb:11.4.12@sha256:{'1' * 64}",
            ),
            "mariadb118": (
                "MariaDb",
                "mariadb:11.8",
                f"mariadb:11.8.8@sha256:{'2' * 64}",
            ),
            "mariadb123": (
                "MariaDb",
                "mariadb:12.3",
                f"mariadb:12.3.2@sha256:{'3' * 64}",
            ),
        }
        self._write_efcore_patch_matrix(identities)
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

        source_commit = release_evidence.run_command(
            "git", "rev-parse", "HEAD", cwd=self.repo
        )
        self._write_qualification_manifest(source_commit)
        runtime_directory = self.root / "runtime"
        runtime_directory.mkdir()
        (runtime_directory / "runtime-posture-evidence.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 2,
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
                    "cache": {
                        "ordinaryExecution": "pass",
                        "trimmedExecution": "pass",
                        "nativeAotExecution": "pass",
                        "trimmedExecutable": {"sha256": "b" * 64, "sizeBytes": 4096},
                        "nativeAotExecutable": {"sha256": "c" * 64, "sizeBytes": 8192},
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

    def _write_efcore_patch_matrix(
        self,
        identities: dict[str, tuple[str, str, str]],
    ) -> None:
        """Write the floor-graph and full-latest release receipts."""
        required_packages = (
            "Microsoft.EntityFrameworkCore.Design",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.EntityFrameworkCore.Relational.Specification.Tests",
        )
        legs = {
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
        for leg, receipt in legs.items():
            directory = self.root / "efcore-patch-matrix" / leg
            directory.mkdir(parents=True)
            receipt["schemaVersion"] = 2
            receipt.setdefault("results", {"dependencies": "resolved-packages.json"})
            (directory / "efcore-contract-evidence.json").write_text(
                json.dumps(receipt),
                encoding="utf-8",
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
                                    for package in required_packages
                                ]
                            }
                        ]
                    },
                    {
                        "frameworks": [
                            {
                                "framework": "net10.0",
                            }
                        ]
                    },
                ]
            }
            (directory / "resolved-packages.json").write_text(
                json.dumps(graph),
                encoding="utf-8",
            )
            if leg == "latest-10-0":
                self._write_efcore_full_results(directory, identities)

    @staticmethod
    def _write_efcore_full_results(
        directory: Path,
        identities: dict[str, tuple[str, str, str]],
    ) -> None:
        """Write successful specification and integration evidence."""
        selected_targets = ("mysql84", "mariadb118")
        retained_targets = []
        for target_id in selected_targets:
            engine, version, image = identities[target_id]
            target = {
                "targetId": target_id,
                "engine": engine,
                "serverVersionToken": version,
                "source": "testcontainers",
                "image": image,
            }
            target_directory = directory / target_id
            target_directory.mkdir()
            (target_directory / "results.trx").write_text(
                "<TestRun><ResultSummary>"
                '<Counters total="1" failed="0" />'
                "</ResultSummary></TestRun>",
                encoding="ascii",
            )
            (target_directory / "test-database-evidence.json").write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "lifecycleState": "cleanup-completed",
                        "targets": [target],
                    }
                ),
                encoding="utf-8",
            )
            retained_targets.append(target)

        integration_directory = directory / "integration"
        integration_directory.mkdir()
        (integration_directory / "compatibility-matrix-evidence.json").write_text(
            json.dumps(
                {
                    "mode": "testcontainers",
                    "targetSelection": ",".join(selected_targets),
                    "testFilter": "",
                    "testExitCode": 0,
                    "testDatabase": {
                        "schemaVersion": 1,
                        "lifecycleState": "cleanup-completed",
                        "targets": retained_targets,
                    },
                }
            ),
            encoding="utf-8",
        )

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
                    "schemaVersion": 3,
                    "kind": "release-qualification-manifest",
                    "releaseLine": release_line(),
                    "policyVersion": policy["policyVersion"],
                    "policyDigest": "d" * 64,
                    "selectionRuleVersion": policy["selectionRule"]["version"],
                    "repository": "doka-labs/Doka.EntityFrameworkCore.MySql",
                    "commit": source_commit,
                    "treeId": "e" * 40,
                    "expectedReleaseTag": "v10.2.3",
                    "releaseVersion": "10.2.3",
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
