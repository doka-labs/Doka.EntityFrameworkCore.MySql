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

from eng.release import provenance


class ReleaseProvenanceTests(unittest.TestCase):
    """Pin the Sigstore envelope and exact-subject release contract."""

    def setUp(self) -> None:
        """Create two local subjects and one matching action bundle."""
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


class ReleaseSubjectSelectionTests(unittest.TestCase):
    """Execute the hosted selector against the real four-package shape."""

    def setUp(self) -> None:
        """Create two primary packages, two symbol packages, and gate inputs."""
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
        """Select the same four package files that actions/attest receives."""
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
            },
            package_names,
        )

    def test_release_selector_rejects_a_missing_symbol_package(self) -> None:
        """Do not let a two-primary-only glob satisfy the release contract."""
        next((self.candidate / "packages").glob("*.snupkg")).unlink()

        with self.assertRaisesRegex(
            provenance.ProvenanceError,
            "two primary and two symbol packages",
        ):
            provenance.release_subjects(
                self.candidate,
                self.publication,
                self.checkpoints,
            )


if __name__ == "__main__":
    unittest.main()
