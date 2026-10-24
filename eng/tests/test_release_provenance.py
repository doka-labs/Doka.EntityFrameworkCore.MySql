"""Regression tests for portable release-provenance materialization."""

from __future__ import annotations

import base64
import copy
import hashlib
import json
import tempfile
import unittest
from collections.abc import Callable
from pathlib import Path
from typing import Any
from unittest import mock

from eng.release import provenance
from eng.tests.test_release_evidence import release_line


class ReleaseProvenanceTests(unittest.TestCase):
    """Pin the Sigstore envelope and exact-subject release contract."""

    def setUp(self) -> None:
        """Create two local subjects and one matching action bundle."""
        # WHY: synthetic run 123 is not the Actions run executing this suite.
        # Hosted-context tests opt in with their own exact run observation.
        environment = mock.patch.dict("os.environ", {"GITHUB_ACTIONS": "false"})
        environment.start()
        self.addCleanup(environment.stop)

        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.package = self.root / "provider.1.0.0.nupkg"
        self.evidence = self.root / "release-candidate-evidence.json"
        self.package.write_bytes(b"provider package\n")
        self.evidence.write_text('{"status":"pass"}\n', encoding="utf-8")
        self.subjects = [self.package, self.evidence]
        self.bundle = self._bundle(self.subjects)
        self.source = self.root / "attestation.json"
        self.output = self.root / provenance.PORTABLE_PROVENANCE_NAME
        self.source.write_text(
            json.dumps(self.bundle, indent=2) + "\n",
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        """Remove the isolated provenance fixture."""
        self.temporary_directory.cleanup()

    @staticmethod
    def _bundle(subjects: list[Path]) -> dict[str, object]:
        """Build a minimally valid actions/attest SLSA bundle fixture."""
        statement = {
            "_type": provenance.IN_TOTO_STATEMENT_TYPE,
            "subject": [
                {
                    "name": subject.name,
                    "digest": {
                        "sha256": hashlib.sha256(subject.read_bytes()).hexdigest()
                    },
                }
                for subject in subjects
            ],
            "predicateType": provenance.SLSA_PROVENANCE_TYPE,
            "predicate": {"buildDefinition": {}, "runDetails": {}},
        }
        payload = base64.b64encode(
            json.dumps(statement, separators=(",", ":")).encode("utf-8")
        ).decode("ascii")
        return {
            "mediaType": provenance.SIGSTORE_BUNDLE_MEDIA_TYPE,
            "verificationMaterial": {"certificate": {"rawBytes": "AA=="}},
            "dsseEnvelope": {
                "payloadType": provenance.IN_TOTO_PAYLOAD_TYPE,
                "payload": payload,
                "signatures": [{"sig": "AA=="}],
            },
        }

    @staticmethod
    def _replace_statement(
        bundle: dict[str, object],
        mutate: Callable[[dict[str, Any]], None],
    ) -> dict[str, object]:
        """Return a bundle whose decoded statement has been mutated."""
        changed = copy.deepcopy(bundle)
        envelope = changed["dsseEnvelope"]
        assert isinstance(envelope, dict)
        statement = json.loads(base64.b64decode(envelope["payload"]))
        mutate(statement)
        envelope["payload"] = base64.b64encode(
            json.dumps(statement, separators=(",", ":")).encode("utf-8")
        ).decode("ascii")
        return changed

    def test_materialize_writes_one_verified_portable_jsonl_record(self) -> None:
        """Preserve the action bundle while binding every required subject."""
        provenance.materialize_bundle(self.source, self.output, self.subjects)

        self.assertEqual(
            1,
            len(self.output.read_text(encoding="utf-8").splitlines()),
        )
        self.assertEqual(
            self.bundle,
            json.loads(self.output.read_text(encoding="utf-8")),
        )
        provenance.verify_portable_bundle(self.output, self.subjects)

    def test_materialize_rejects_a_missing_required_subject(self) -> None:
        """Do not publish provenance that omits one selected release input."""
        missing = self.root / "spatial.1.0.0.nupkg"
        missing.write_bytes(b"spatial package\n")

        with self.assertRaisesRegex(provenance.ProvenanceError, "spatial.1.0.0"):
            provenance.materialize_bundle(
                self.source,
                self.output,
                [*self.subjects, missing],
            )

    def test_materialize_rejects_a_wrong_subject_digest(self) -> None:
        """Do not let a subject name stand in for exact byte identity."""
        changed = self._replace_statement(
            self.bundle,
            lambda statement: statement["subject"][0]["digest"].update(sha256="0" * 64),
        )
        self.source.write_text(json.dumps(changed), encoding="utf-8")

        with self.assertRaisesRegex(provenance.ProvenanceError, self.package.name):
            provenance.materialize_bundle(self.source, self.output, self.subjects)

    def test_materialize_rejects_non_slsa_attestation(self) -> None:
        """Require build provenance instead of any valid in-toto statement."""
        changed = self._replace_statement(
            self.bundle,
            lambda statement: statement.update(
                predicateType="https://in-toto.io/attestation/release/v0.2"
            ),
        )
        self.source.write_text(json.dumps(changed), encoding="utf-8")

        with self.assertRaisesRegex(provenance.ProvenanceError, "SLSA"):
            provenance.materialize_bundle(self.source, self.output, self.subjects)

    def test_materialize_rejects_duplicate_subject_names(self) -> None:
        """Reject ambiguous name-to-digest mappings at the release boundary."""
        changed = self._replace_statement(
            self.bundle,
            lambda statement: statement["subject"].append(
                copy.deepcopy(statement["subject"][0])
            ),
        )
        self.source.write_text(json.dumps(changed), encoding="utf-8")

        with self.assertRaisesRegex(provenance.ProvenanceError, "inventory"):
            provenance.materialize_bundle(self.source, self.output, self.subjects)

    def test_verify_rejects_more_than_one_jsonl_record(self) -> None:
        """Keep one release asset bound to one unambiguous attestation bundle."""
        record = json.dumps(self.bundle, separators=(",", ":"))
        self.output.write_text(f"{record}\n{record}\n", encoding="utf-8")

        with self.assertRaisesRegex(provenance.ProvenanceError, "exactly one"):
            provenance.verify_portable_bundle(self.output, self.subjects)

    def test_materialize_requires_the_portable_release_asset_name(self) -> None:
        """Keep the OpenSSF-discoverable release-asset suffix stable."""
        output = self.root / "attestation.json"

        with self.assertRaisesRegex(
            provenance.ProvenanceError,
            provenance.PORTABLE_PROVENANCE_NAME,
        ):
            provenance.materialize_bundle(self.source, output, self.subjects)

    def _workflow_fixture(self) -> tuple[dict[str, Any], dict[str, Any]]:
        """Return separately validated caller and signer facts for one entry."""
        line = release_line("refs/heads/maintenance/10.x")
        repository = line["repository"]
        commit = "b" * 40
        manifest = {
            "schemaVersion": 4,
            "releaseVersion": "10.6.2",
            "releaseLine": line,
            "source": {"ref": line["sourceRef"], "commit": commit},
            "workflow": {
                "provider": "github-actions",
                "repository": repository,
                "workflowRef": f"{repository}/{line['workflowPath']}@{line['sourceRef']}",
                "signerWorkflowRef": f"{repository}/.github/workflows/release-candidate.yml@{line['sourceRef']}",
                "signerDigest": commit,
                "runId": "123",
                "runAttempt": "2",
            },
        }

        def configure(statement: dict[str, Any]) -> None:
            """Model the documented caller-oriented SLSA workflow predicate."""
            statement["predicate"] = {
                "buildDefinition": {
                    "externalParameters": {"workflow": {
                        "repository": f"https://github.com/{repository}",
                        "path": line["workflowPath"],
                        "ref": line["sourceRef"],
                    }},
                    "resolvedDependencies": [{
                        "uri": f"git+https://github.com/{repository}@{line['sourceRef']}",
                        "digest": {"gitCommit": commit},
                    }],
                },
                "runDetails": {
                    "builder": {"id": "https://github.com/actions/runner/github-hosted"},
                    "metadata": {"invocationId": f"https://github.com/{repository}/actions/runs/123/attempts/2"},
                },
            }

        return self._replace_statement(self.bundle, configure), manifest

    def test_provenance_validates_caller_and_signer_for_the_same_public_entry(self) -> None:
        """The single entry retains both independent identity checks."""
        # Arrange
        bundle, manifest = self._workflow_fixture()

        # Act
        result = provenance.validate_workflow_identity(bundle, manifest)

        # Assert
        self.assertIsNone(result)
        self.assertEqual(manifest["workflow"]["workflowRef"], manifest["workflow"]["signerWorkflowRef"])

    def test_provenance_rejects_an_unregistered_signer(self) -> None:
        """Matching caller facts cannot excuse a different attestation signer."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["workflow"]["signerWorkflowRef"] = manifest["workflow"]["workflowRef"].replace(
            "release-candidate.yml", "ci.yml")

        # Act
        with self.assertRaises(provenance.ProvenanceError) as failure:
            provenance.validate_workflow_identity(bundle, manifest)

        # Assert
        self.assertIn("reusable signer", str(failure.exception))

    def test_provenance_rejects_a_different_signer_digest(self) -> None:
        """Pin the reusable workflow revision to the qualified source SHA."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["workflow"]["signerDigest"] = "c" * 40

        # Act / Assert
        with self.assertRaisesRegex(provenance.ProvenanceError, "reusable signer"):
            provenance.validate_workflow_identity(bundle, manifest)

    def test_provenance_rejects_an_unregistered_statement_workflow(self) -> None:
        """Keep signed external workflow identity bound to the public entry."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        changed = self._replace_statement(
            bundle,
            lambda statement: statement["predicate"]["buildDefinition"]["externalParameters"]["workflow"].update(
                path=".github/workflows/ci.yml"
            ),
        )

        # Act
        with self.assertRaises(provenance.ProvenanceError) as failure:
            provenance.validate_workflow_identity(changed, manifest)

        # Assert
        self.assertIn("different caller", str(failure.exception))

    def test_provenance_rejects_a_future_publication_attempt(self) -> None:
        """A signed attestation cannot come from a not-yet-observed attempt."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        changed = self._replace_statement(
            bundle,
            lambda statement: statement["predicate"]["runDetails"]["metadata"].update(
                invocationId="https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/123/attempts/3"
            ),
        )

        # Act / Assert
        with self.assertRaisesRegex(provenance.ProvenanceError, "publication attempt"):
            provenance.validate_workflow_identity(
                changed, manifest, publication_run_attempt=2
            )

    def test_provenance_accepts_later_attestation_of_frozen_qualification(self) -> None:
        """A rerun may attest the unchanged candidate produced by attempt one."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["workflow"]["runAttempt"] = "1"
        original = copy.deepcopy(manifest)

        # Act
        result = provenance.validate_workflow_identity(
            bundle, manifest, signer_run_attempt=2, publication_run_attempt=2
        )

        # Assert
        self.assertIsNone(result)
        self.assertEqual(original, manifest)

    def test_provenance_rejects_attestation_before_qualification(self) -> None:
        """An earlier signed invocation cannot attest a future candidate."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["workflow"]["runAttempt"] = "3"

        # Act / Assert
        with self.assertRaisesRegex(provenance.ProvenanceError, "precedes qualification"):
            provenance.validate_workflow_identity(bundle, manifest)

    def test_provenance_rejects_another_workflow_run(self) -> None:
        """Nondecreasing attempts do not authorize a different Actions run."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        changed = self._replace_statement(
            bundle,
            lambda statement: statement["predicate"]["runDetails"]["metadata"].update(
                invocationId="https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/124/attempts/2"
            ),
        )

        # Act / Assert
        with self.assertRaisesRegex(provenance.ProvenanceError, "different workflow run"):
            provenance.validate_workflow_identity(changed, manifest)

    def test_materialization_binds_the_exact_attesting_attempt(self) -> None:
        """The portable bytes retain signer attempt two and candidate attempt one."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["workflow"]["runAttempt"] = "1"
        self.source.write_text(json.dumps(bundle), encoding="ascii")
        original = copy.deepcopy(manifest)

        # Act
        provenance.materialize_bundle(
            self.source, self.output, self.subjects,
            release_manifest=manifest, signer_run_attempt=2,
        )

        # Assert
        self.assertEqual(bundle, provenance.read_bundle(self.output, json_lines=True))
        self.assertEqual(original, manifest)

    def test_materialization_rejects_a_mismatched_attester_artifact_attempt(self) -> None:
        """Artifact attempt three must not contain attempt-two signed bytes."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        self.source.write_text(json.dumps(bundle), encoding="ascii")

        # Act / Assert
        with self.assertRaisesRegex(provenance.ProvenanceError, "attesting attempt"):
            provenance.materialize_bundle(
                self.source, self.output, self.subjects,
                release_manifest=manifest, signer_run_attempt=3,
            )
        self.assertFalse(self.output.exists())

    def test_portable_verification_rejects_a_future_hosted_attempt(self) -> None:
        """Readback bounds the signed attempt even without an explicit CLI flag."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["workflow"]["runAttempt"] = "1"
        self.output.write_text(json.dumps(bundle) + "\n", encoding="ascii")
        environment = {"GITHUB_ACTIONS": "true", "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1"}

        # Act / Assert
        with mock.patch.dict("os.environ", environment, clear=True):
            with self.assertRaisesRegex(provenance.ProvenanceError, "current publication attempt"):
                provenance.verify_portable_bundle(
                    self.output, self.subjects, release_manifest=manifest
                )

    def test_portable_verification_rejects_another_hosted_run(self) -> None:
        """The publication job must belong to the same run as its frozen proof."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        self.output.write_text(json.dumps(bundle) + "\n", encoding="ascii")
        environment = {"GITHUB_ACTIONS": "true", "GITHUB_RUN_ID": "124", "GITHUB_RUN_ATTEMPT": "2"}

        # Act / Assert
        with mock.patch.dict("os.environ", environment, clear=True):
            with self.assertRaisesRegex(provenance.ProvenanceError, "different workflow run"):
                provenance.verify_portable_bundle(
                    self.output, self.subjects, release_manifest=manifest
                )

    def test_materialization_observes_the_exact_hosted_attester(self) -> None:
        """A later attestation preserves qualification bytes from attempt one."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["workflow"]["runAttempt"] = "1"
        original = copy.deepcopy(manifest)
        self.source.write_text(json.dumps(bundle), encoding="ascii")
        environment = {"GITHUB_ACTIONS": "true", "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "2"}

        # Act
        with mock.patch.dict("os.environ", environment, clear=True):
            provenance.materialize_bundle(
                self.source, self.output, self.subjects,
                release_manifest=manifest, signer_run_attempt="2",
            )

        # Assert
        self.assertEqual(bundle, provenance.read_bundle(self.output, json_lines=True))
        self.assertEqual(original, manifest)

    def test_historical_portable_verification_accepts_later_attester_offline(self) -> None:
        """Historical main-only proof remains independently verifiable offline."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        manifest["schemaVersion"] = 3
        del manifest["releaseLine"]
        manifest["source"]["ref"] = "refs/heads/main"
        manifest["workflow"]["runAttempt"] = "1"
        manifest["workflow"]["workflowRef"] = (
            "doka-labs/Doka.EntityFrameworkCore.MySql/"
            ".github/workflows/release-candidate.yml@refs/heads/main"
        )

        def use_historical_main(statement: dict[str, Any]) -> None:
            """Keep the signed historical caller and dependency on main."""
            definition = statement["predicate"]["buildDefinition"]
            definition["externalParameters"]["workflow"].update(
                path=".github/workflows/release-candidate.yml", ref="refs/heads/main"
            )
            definition["resolvedDependencies"][0]["uri"] = (
                "git+https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql@refs/heads/main"
            )

        changed = self._replace_statement(bundle, use_historical_main)
        self.output.write_text(json.dumps(changed) + "\n", encoding="ascii")

        # Act
        with mock.patch.dict("os.environ", {}, clear=True):
            result = provenance.verify_portable_bundle(
                self.output, self.subjects, release_manifest=manifest
            )

        # Assert
        self.assertIsNone(result)

    def test_invalid_attesting_attempt_is_rejected(self) -> None:
        """A bool cannot become an Actions attempt through numeric coercion."""
        # Arrange
        bundle, manifest = self._workflow_fixture()

        # Act / Assert
        with self.assertRaisesRegex(provenance.ProvenanceError, "positive workflow attempt"):
            provenance.validate_workflow_identity(bundle, manifest, signer_run_attempt=True)

    def test_provenance_rejects_a_different_resolved_source(self) -> None:
        """Matching caller metadata is insufficient when the Git SHA differs."""
        # Arrange
        bundle, manifest = self._workflow_fixture()
        changed = self._replace_statement(
            bundle,
            lambda statement: statement["predicate"]["buildDefinition"]["resolvedDependencies"][0]["digest"].update(
                gitCommit="c" * 40
            ),
        )

        # Act / Assert
        with self.assertRaisesRegex(provenance.ProvenanceError, "exact release source commit"):
            provenance.validate_workflow_identity(changed, manifest)


class ReleaseSubjectSelectionTests(unittest.TestCase):
    """Execute the hosted selector against the real four-package shape."""

    def setUp(self) -> None:
        """Create three primary packages, three symbol packages, and gate inputs."""
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.candidate = self.root / "candidate"
        self.publication = self.root / "publication"
        self.checkpoints = self.root / "checkpoints"
        packages = self.candidate / "packages"
        packages.mkdir(parents=True)
        self.publication.mkdir()
        self.checkpoints.mkdir()

        for name in (
            "Doka.EntityFrameworkCore.MySql.1.0.0.nupkg",
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite.1.0.0.nupkg",
            "Doka.EntityFrameworkCore.MySql.1.0.0.snupkg",
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite.1.0.0.snupkg",
            "Doka.Caching.MySql.1.0.0.nupkg",
            "Doka.Caching.MySql.1.0.0.snupkg",
        ):
            (packages / name).write_bytes(f"{name}\n".encode("ascii"))

        for path in (
            self.candidate / "release-candidate-evidence.json",
            self.candidate / "release-candidate-evidence.sha256",
            self.publication / "candidate-receipt.json",
            self.publication / "candidate-publication-preflight.json",
            self.publication / "symbol-readback-manifest.json",
            self.checkpoints / "package.json",
        ):
            path.write_text("{}\n", encoding="ascii")

    def tearDown(self) -> None:
        """Remove the isolated hosted-layout fixture."""
        self.temporary_directory.cleanup()

    def test_release_selector_includes_primary_and_symbol_packages(self) -> None:
        """Select the same six package files that actions/attest receives."""
        subjects = provenance.release_subjects(
            self.candidate,
            self.publication,
            self.checkpoints,
        )
        package_names = {
            subject.name
            for subject in subjects
            if subject.suffix in {".nupkg", ".snupkg"}
        }

        self.assertEqual(
            {
                "Doka.EntityFrameworkCore.MySql.1.0.0.nupkg",
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite.1.0.0.nupkg",
                "Doka.EntityFrameworkCore.MySql.1.0.0.snupkg",
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite.1.0.0.snupkg",
                "Doka.Caching.MySql.1.0.0.nupkg",
                "Doka.Caching.MySql.1.0.0.snupkg",
            },
            package_names,
        )

    def test_release_selector_rejects_a_missing_symbol_package(self) -> None:
        """Do not let a two-primary-only glob satisfy the release contract."""
        next((self.candidate / "packages").glob("*.snupkg")).unlink()

        with self.assertRaisesRegex(
            provenance.ProvenanceError,
            "three primary and three symbol packages",
        ):
            provenance.release_subjects(
                self.candidate,
                self.publication,
                self.checkpoints,
            )


if __name__ == "__main__":
    unittest.main()
