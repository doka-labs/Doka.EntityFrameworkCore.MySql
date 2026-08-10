"""Run the reconciliation writer's own output through the real validator.

The writer produced schema 2 with the six gates the qualification manifest
selects; the validator required schema 1 and a list of fifteen legacy gate
names. Both were internally consistent, both had passing tests, and the release
would have failed at the last step of an otherwise successful run.

The gap was that no test carried one side's output to the other. These tests
execute the writer exactly as the orchestrator does, then hand the file it
produced to the validator without touching it in between.
"""

from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from typing import Any

from eng.release import evidence as release_evidence


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ORCHESTRATOR = REPOSITORY_ROOT / "eng" / "release" / "release-candidate.sh"
POLICY_PATH = REPOSITORY_ROOT / "eng" / "release" / "evidence-policy.json"

RUN_ID = "github-909"
COMMIT = "c" * 40
RELEASE_TAG = "v10.0.0-reconcile"
RELEASE_VERSION = "10.0.0-reconcile"


def writer_body() -> str:
    """Return the reconciliation writer the orchestrator runs.

    The function's closing brace cannot be found by scanning for the first
    line-leading `}`: the embedded program contains one. The heredoc terminator
    is the unambiguous landmark.
    """
    body = ORCHESTRATOR.read_text(encoding="utf-8")
    start = body.index("write_reconciliation() {")
    end = body.index("\nRECONCILE\n", start) + len("\nRECONCILE\n")

    return body[start:end]


def embedded_writer() -> str:
    """Return the Python program the writer feeds the manifest into.

    Extracting it rather than reimplementing it is the point: a copy here could
    drift from the orchestrator exactly the way the validator drifted from the
    writer.
    """
    body = writer_body()
    start = body.index("<<'RECONCILE'\n") + len("<<'RECONCILE'\n")

    return body[start : body.index("\nRECONCILE\n")]


def manifest(gates: list[str]) -> dict[str, Any]:
    """Return a qualification manifest pinning the given gates."""
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    declared = {gate["id"]: gate for gate in policy["gates"]}

    return {
        "schemaVersion": 1,
        "kind": "release-qualification-manifest",
        "policyVersion": policy["policyVersion"],
        "policyDigest": "d" * 64,
        "selectionRuleVersion": policy["selectionRule"]["version"],
        "repository": "doka-labs/Doka.EntityFrameworkCore.MySql",
        "commit": COMMIT,
        "treeId": "e" * 40,
        "releaseTag": RELEASE_TAG,
        "releaseVersion": RELEASE_VERSION,
        "assemblingRunAttempt": 1,
        "requiredProtectedChecks": policy["requiredProtectedChecks"],
        "gates": [
            {
                "gate": identifier,
                "kind": declared[identifier]["kind"],
                "workflowRunId": 909,
                "runAttempt": 1,
            }
            for identifier in gates
        ],
    }


class ReconciliationWriterValidatorTests(unittest.TestCase):
    """Prove the writer produces exactly what the validator accepts."""

    def setUp(self) -> None:
        """Prepare a candidate root for the writer to fill."""
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))

    def tearDown(self) -> None:
        """Release the fixture."""
        self.directory.cleanup()

    def write(self, gates: list[str] | None = None) -> Path:
        """Run the orchestrator's reconciliation writer over a manifest."""
        manifest_path = self.root / "release-qualification-manifest.json"
        output = self.root / "release-candidate-reconciliation.json"
        manifest_path.write_text(
            json.dumps(
                manifest(gates or [gate["id"] for gate in self.policy["gates"]])
            ),
            encoding="utf-8",
        )
        result = subprocess.run(
            ["python3", "-", str(manifest_path), str(output), RUN_ID, COMMIT],
            input=embedded_writer(),
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)

        return output

    def test_the_writer_output_passes_the_validator_unchanged(self) -> None:
        """Carry one side's output to the other without editing it.

        This is the assertion that was missing. Each side had tests; the file
        never travelled between them.
        """
        written = self.write()

        summary = release_evidence.validate_reconciliation(self.root, RUN_ID, COMMIT)

        self.assertEqual(
            sorted(gate["id"] for gate in self.policy["gates"]), sorted(summary)
        )
        self.assertTrue(all(status == "pass" for status in summary.values()))
        self.assertTrue(written.is_file())

    def test_the_writer_and_the_validator_agree_on_the_schema_version(self) -> None:
        """Keep one schema version rather than two that happen to coexist."""
        written = json.loads(self.write().read_text(encoding="utf-8"))

        self.assertEqual(
            release_evidence.RECONCILIATION_SCHEMA_VERSION, written["schemaVersion"]
        )

    def test_the_gate_inventory_comes_from_the_policy(self) -> None:
        """Bind both sides to the policy instead of to a restated list."""
        self.assertEqual(
            tuple(sorted(gate["id"] for gate in self.policy["gates"])),
            release_evidence.required_reconciliation_gates(),
        )

    def test_a_reconciliation_missing_a_gate_is_refused(self) -> None:
        """Refuse an index that omits a gate the policy declares."""
        declared = [gate["id"] for gate in self.policy["gates"]]
        self.write(declared[:-1])

        with self.assertRaises(release_evidence.EvidenceError):
            release_evidence.validate_reconciliation(self.root, RUN_ID, COMMIT)

    def test_a_reconciliation_for_another_run_is_refused(self) -> None:
        """Refuse an index produced for a different run."""
        self.write()

        with self.assertRaises(release_evidence.EvidenceError):
            release_evidence.validate_reconciliation(self.root, "github-other", COMMIT)

    def test_a_reconciliation_for_another_commit_is_refused(self) -> None:
        """Refuse an index produced for a different commit."""
        self.write()

        with self.assertRaises(release_evidence.EvidenceError):
            release_evidence.validate_reconciliation(self.root, RUN_ID, "f" * 40)

    def test_the_writer_refuses_a_manifest_for_another_commit(self) -> None:
        """Refuse to reconcile a manifest that describes a different release."""
        manifest_path = self.root / "release-qualification-manifest.json"
        manifest_path.write_text(
            json.dumps(
                manifest([gate["id"] for gate in self.policy["gates"]])
                | {"commit": "9" * 40}
            ),
            encoding="utf-8",
        )
        result = subprocess.run(
            [
                "python3", "-",
                str(manifest_path),
                str(self.root / "out.json"),
                RUN_ID,
                COMMIT,
            ],
            input=embedded_writer(),
            capture_output=True,
            text=True,
        )

        self.assertNotEqual(0, result.returncode)


if __name__ == "__main__":
    unittest.main()
