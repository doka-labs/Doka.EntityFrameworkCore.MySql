"""Contracts for selecting and freezing the evidence that qualifies a tag.

Reruns are legitimate and make selection ambiguous: a rerun keeps its workflow
run identifier and increments its attempt, and a conditionally skipped job
reports success. A selector that ignored either would pin a skipped job or an
older attempt and still call the release verified. These tests assert that the
ambiguity is resolved by a versioned rule and then frozen, rather than being
resolved again at verification time.
"""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from eng.release import qualification


REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"
COMMIT = "9f7f097b1edc4719572411504f2b64095baa49c2"
TREE = "0ca65bda4f73c90dba094b482e3eba081bfe2d85"


class PolicyTests(unittest.TestCase):
    """Prove the shipped policy is complete and rejects malformed variants."""

    def test_the_shipped_policy_loads(self) -> None:
        """Keep the checked-in policy inside its own shape."""
        policy = qualification.load_policy()

        self.assertEqual(5, len(policy["gates"]))
        self.assertIn("repository-qualification", policy["requiredProtectedChecks"])

    def test_performance_results_have_no_release_authority(self) -> None:
        """Keep advisory benchmark evidence outside release qualification."""
        policy = qualification.load_policy()

        self.assertNotIn(
            "performance-qualification",
            {gate["id"] for gate in policy["gates"]},
        )
        self.assertIn(
            "Performance evidence is produced independently",
            " ".join(policy["documentation"]),
        )

    def test_every_top_level_field_is_required(self) -> None:
        """Reject a policy missing any structural field."""
        policy = qualification.load_policy()
        for field in (
            "schemaVersion",
            "policyVersion",
            "selectionRule",
            "gates",
            "trustedTagSigners",
            "requiredProtectedChecks",
        ):
            with self.subTest(field=field):
                broken = json.loads(json.dumps(policy))
                del broken[field]
                with tempfile.TemporaryDirectory() as directory:
                    path = Path(directory) / "policy.json"
                    path.write_text(json.dumps(broken), encoding="utf-8")
                    with self.assertRaises(qualification.QualificationError):
                        qualification.load_policy(path)

    def test_a_duplicate_gate_is_rejected(self) -> None:
        """Reject a policy that declares one gate twice."""
        policy = qualification.load_policy()
        broken = json.loads(json.dumps(policy))
        broken["gates"].append(json.loads(json.dumps(broken["gates"][0])))

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "policy.json"
            path.write_text(json.dumps(broken), encoding="utf-8")
            with self.assertRaises(qualification.QualificationError):
                qualification.load_policy(path)

    def test_the_digest_changes_with_the_policy(self) -> None:
        """Keep the digest load-bearing rather than decorative."""
        policy = qualification.load_policy()
        changed = json.loads(json.dumps(policy))
        changed["policyVersion"] = "2999-01-01"

        self.assertNotEqual(
            qualification.policy_digest(policy),
            qualification.policy_digest(changed),
        )


class ProtectedCheckReceiptTests(unittest.TestCase):
    """Prove GitHub-side checks become bindable evidence."""

    GATE = {
        "id": "repository-qualification",
        "kind": "protected-check",
        "producerWorkflow": ".github/workflows/ci.yml",
        "checkName": "repository-qualification",
        "requiredEvent": "push",
        "requiredRef": "refs/heads/main",
        "boundIdentities": [],
    }

    def response(self, **overrides: object) -> dict[str, object]:
        """Return a successful check response with optional overrides."""
        payload = {
            "id": 4242,
            "name": "repository-qualification",
            "conclusion": "success",
            "head_sha": COMMIT,
            "event": "push",
            "ref": "refs/heads/main",
            "workflow_run_id": 900,
            "run_attempt": 1,
        }
        payload.update(overrides)
        return payload

    def receipt(self, **overrides: object) -> dict[str, object]:
        """Normalize one response into a receipt."""
        response = self.response(**overrides)
        return qualification.protected_check_receipt(
            response,
            gate=self.GATE,
            commit=COMMIT,
            repository=REPOSITORY,
            digest_source=json.dumps(response, sort_keys=True),
        )

    def test_a_successful_push_check_becomes_a_receipt(self) -> None:
        """Bind the API resource and its response digest."""
        receipt = self.receipt()

        self.assertEqual(qualification.RECEIPT_KIND, receipt["kind"])
        self.assertEqual(4242, receipt["apiResourceId"])
        self.assertEqual(64, len(receipt["responseDigest"]))

    def test_a_pull_request_result_is_not_branch_evidence(self) -> None:
        """Reject a result for the same commit produced by a pull request.

        The same commit can carry a green check from a pull request without
        that check ever having run on the protected branch.
        """
        with self.assertRaises(qualification.QualificationError):
            self.receipt(event="pull_request")

    def test_a_foreign_ref_is_rejected(self) -> None:
        """Reject a result produced outside the protected branch."""
        with self.assertRaises(qualification.QualificationError):
            self.receipt(ref="refs/heads/topic")

    def test_a_result_for_another_commit_is_rejected(self) -> None:
        """Reject evidence that describes a different commit."""
        with self.assertRaises(qualification.QualificationError):
            self.receipt(head_sha="0" * 40)

    def test_a_non_success_conclusion_is_rejected(self) -> None:
        """Reject a check that did not conclude successfully."""
        for conclusion in ("failure", "skipped", "cancelled", "neutral"):
            with self.subTest(conclusion=conclusion):
                with self.assertRaises(qualification.QualificationError):
                    self.receipt(conclusion=conclusion)

    def test_a_response_for_another_check_is_rejected(self) -> None:
        """Reject a response whose name is not the expected check."""
        with self.assertRaises(qualification.QualificationError):
            self.receipt(name="some-other-check")


class SelectionTests(unittest.TestCase):
    """Prove selection is deterministic, bounded, and fails closed."""

    def setUp(self) -> None:
        """Load the policy and its digest once per case."""
        self.policy = qualification.load_policy()
        self.digest = qualification.policy_digest(self.policy)
        self.gate = next(
            gate
            for gate in self.policy["gates"]
            if gate["id"] == "migration-deployment"
        )

    def result(self, **overrides: object) -> dict[str, object]:
        """Return one eligible gate result with optional overrides."""
        payload = {
            "gate": "migration-deployment",
            "commit": COMMIT,
            "repository": REPOSITORY,
            "workflowPath": ".github/workflows/release-candidate.yml",
            "policyDigest": self.digest,
            "conclusion": "success",
            "workflowRunId": 100,
            "runAttempt": 1,
            "artifactId": 1,
        }
        payload.update(overrides)
        return payload

    def select(self, results: list[dict[str, object]], attempt: int | None = 2):
        """Select under the shipped policy."""
        return qualification.select_result(
            results,
            gate=self.gate,
            commit=COMMIT,
            repository=REPOSITORY,
            digest=self.digest,
            assembling_attempt=attempt,
        )

    def test_the_greatest_run_then_attempt_wins(self) -> None:
        """Resolve reruns by run identifier first, then attempt."""
        results = [
            self.result(workflowRunId=100, runAttempt=1, artifactId=1),
            self.result(workflowRunId=100, runAttempt=2, artifactId=2),
            self.result(workflowRunId=99, runAttempt=9, artifactId=3),
        ]

        selected = self.select(results)

        self.assertEqual(100, selected["workflowRunId"])
        self.assertEqual(2, selected["runAttempt"])

    def test_attempts_order_numerically_not_lexically(self) -> None:
        """Keep attempt 10 ahead of attempt 9.

        String ordering would place '10' before '9' and silently pin the older
        rerun.
        """
        results = [
            self.result(runAttempt="9", artifactId=1),
            self.result(runAttempt="10", artifactId=2),
        ]

        self.assertEqual("10", self.select(results, attempt=10)["runAttempt"])

    def test_a_newer_attempt_than_the_assembly_is_not_selected(self) -> None:
        """Refuse evidence that did not exist when the manifest was written."""
        results = [
            self.result(runAttempt=1, artifactId=1),
            self.result(runAttempt=5, artifactId=2),
        ]

        selected = self.select(results, attempt=1)

        self.assertEqual(1, selected["runAttempt"])

    def test_an_ineligible_result_is_never_selected(self) -> None:
        """Reject each identity mismatch as a different measurement."""
        mismatches = (
            {"commit": "0" * 40},
            {"repository": "someone/else"},
            {"workflowPath": ".github/workflows/ci.yml"},
            {"policyDigest": "0" * 64},
            {"conclusion": "skipped"},
            {"gate": "runtime-posture"},
        )
        for override in mismatches:
            with self.subTest(override=override):
                with self.assertRaises(qualification.QualificationError):
                    self.select([self.result(**override)])

    def test_a_tie_at_the_selected_key_fails_closed(self) -> None:
        """Refuse to pick arbitrarily between two identical keys."""
        results = [
            self.result(workflowRunId=100, runAttempt=2, artifactId=7),
            self.result(workflowRunId=100, runAttempt=2, artifactId=8),
        ]

        with self.assertRaises(qualification.QualificationError):
            self.select(results)

    def test_an_unorderable_identifier_fails_closed(self) -> None:
        """Reject an identifier that cannot be ordered numerically."""
        for value in ("latest", None, True, 1.5):
            with self.subTest(value=value):
                with self.assertRaises(qualification.QualificationError):
                    self.select([self.result(workflowRunId=value)])

    def test_no_eligible_evidence_fails_closed(self) -> None:
        """Refuse to qualify a gate that produced nothing."""
        with self.assertRaises(qualification.QualificationError):
            self.select([])


class ManifestTests(unittest.TestCase):
    """Prove the manifest pins one result per gate and is not reselected."""

    def setUp(self) -> None:
        """Build a complete eligible result set for every declared gate."""
        self.policy = qualification.load_policy()
        self.digest = qualification.policy_digest(self.policy)
        self.results = []
        for index, gate in enumerate(self.policy["gates"], start=1):
            entry = {
                "gate": gate["id"],
                "commit": COMMIT,
                "repository": REPOSITORY,
                "workflowPath": gate["producerWorkflow"],
                "policyDigest": self.digest,
                "conclusion": "success",
                "workflowRunId": 500 + index,
                "runAttempt": 1,
            }
            for field in gate["boundIdentities"]:
                entry.setdefault(field, f"{gate['id']}-{field}")
            entry["commit"] = COMMIT
            entry["workflowRunId"] = 500 + index
            entry["runAttempt"] = 1
            entry["conclusion"] = "success"
            entry["workflowPath"] = gate["producerWorkflow"]
            self.results.append(entry)

    def assemble(self) -> dict[str, object]:
        """Assemble the canonical manifest from the prepared results."""
        return qualification.assemble_manifest(
            self.results,
            commit=COMMIT,
            tree_id=TREE,
            repository=REPOSITORY,
            expected_release_tag="v10.0.0-rc.8",
            release_version="10.0.0-rc.8",
            assembling_attempt=1,
            policy=self.policy,
        )

    def test_every_declared_gate_is_pinned_once(self) -> None:
        """Produce exactly one selection per declared gate."""
        manifest = self.assemble()

        pinned = [entry["gate"] for entry in manifest["gates"]]
        self.assertEqual(
            sorted(gate["id"] for gate in self.policy["gates"]), sorted(pinned)
        )
        self.assertEqual(len(pinned), len(set(pinned)))

    def test_a_missing_bound_identity_fails_closed(self) -> None:
        """Refuse to pin evidence that omits an identity the policy binds."""
        gate = self.policy["gates"][1]
        target = next(item for item in self.results if item["gate"] == gate["id"])
        del target[gate["boundIdentities"][0]]

        with self.assertRaises(qualification.QualificationError):
            self.assemble()

    def test_verification_does_not_reselect(self) -> None:
        """Accept a pinned manifest even when newer evidence has appeared.

        Verification re-checks what was pinned. If it reselected, a rerun that
        landed after assembly could change what the release was qualified on
        without anyone editing the manifest.
        """
        manifest = self.assemble()
        self.results.append(
            {
                **self.results[0],
                "workflowRunId": 99999,
                "runAttempt": 9,
            }
        )

        qualification.verify_manifest(manifest, policy=self.policy)

    def test_a_manifest_from_another_policy_is_rejected(self) -> None:
        """Reject evidence selected under different rules."""
        manifest = self.assemble()
        manifest["policyDigest"] = "0" * 64

        with self.assertRaises(qualification.QualificationError):
            qualification.verify_manifest(manifest, policy=self.policy)

    def test_a_manifest_missing_a_gate_is_rejected(self) -> None:
        """Reject a manifest that does not cover every declared gate."""
        manifest = self.assemble()
        manifest["gates"].pop()

        with self.assertRaises(qualification.QualificationError):
            qualification.verify_manifest(manifest, policy=self.policy)

    def test_a_manifest_with_an_undeclared_gate_is_rejected(self) -> None:
        """Reject a manifest that pins something the policy never declared."""
        manifest = self.assemble()
        manifest["gates"].append(
            {"gate": "invented", "kind": "candidate-produced"}
        )

        with self.assertRaises(qualification.QualificationError):
            qualification.verify_manifest(manifest, policy=self.policy)


class FileDigestTests(unittest.TestCase):
    """Prove integrity verification fails closed rather than warning."""

    def build(self, directory: Path, files: dict[str, str]) -> list[dict[str, str]]:
        """Write files and return their canonical inventory."""
        inventory = []
        for name, content in files.items():
            path = directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
            inventory.append(
                {
                    "path": name,
                    "sha256": hashlib.sha256(content.encode("utf-8")).hexdigest(),
                }
            )
        return inventory

    def test_a_matching_tree_is_accepted(self) -> None:
        """Accept evidence whose every file matches its recorded digest."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = self.build(root, {"a.json": "{}", "sub/b.txt": "hello"})

            qualification.verify_file_digests(root, inventory)

    def test_a_changed_file_is_rejected(self) -> None:
        """Reject content that no longer matches its digest."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = self.build(root, {"a.json": "{}"})
            (root / "a.json").write_text('{"tampered": true}', encoding="utf-8")

            with self.assertRaises(qualification.QualificationError):
                qualification.verify_file_digests(root, inventory)

    def test_a_missing_file_is_rejected(self) -> None:
        """Reject evidence that lost a recorded file."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = self.build(root, {"a.json": "{}", "b.json": "[]"})
            (root / "b.json").unlink()

            with self.assertRaises(qualification.QualificationError):
                qualification.verify_file_digests(root, inventory)

    def test_an_additional_file_is_rejected(self) -> None:
        """Reject evidence that gained an unrecorded file.

        An extra file is not harmless: the inventory is what the attestation
        covers, so anything outside it travels unattested.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = self.build(root, {"a.json": "{}"})
            (root / "extra.json").write_text("{}", encoding="utf-8")

            with self.assertRaises(qualification.QualificationError):
                qualification.verify_file_digests(root, inventory)


if __name__ == "__main__":
    unittest.main()
