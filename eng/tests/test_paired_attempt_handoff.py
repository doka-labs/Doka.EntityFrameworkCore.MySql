"""Carry a paired measurement through the attempt path on the file system.

A paired run produced a verdict under its own name while the attempt recorder
looked for the historical one. Every check passed in isolation: the comparison
was correct, the receipt logic was correct, and the workflow wired them
together. The handoff between them was never executed, so a successful
measurement failed in the step after it and no selectable result existed.

These tests write the files a real paired run writes, then drive
`record-attempt` and `select-attempt` through their real entry points. They
assert the handoff, not the parts on either side of it.
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any

from eng.performance import attempts
from eng.performance.contract import PerformanceEvidenceError


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


TARGET = "mariadb118"
PROFILE = "paired-block"
RUN_ID = "github-4242-mariadb118-attempt-1"
COMMIT = "a" * 40
SOURCE_HASH = "b" * 64
RUNNER_CLASS = "github-ubuntu-latest-x64"


def paired_evaluation(**overrides: Any) -> dict[str, Any]:
    """Return the verdict shape `evaluate-paired` writes."""
    evaluation = {
        "schemaVersion": 2,
        "kind": "paired-performance-evaluation",
        "target": TARGET,
        "profile": PROFILE,
        "runId": RUN_ID,
        "commit": COMMIT,
        "sourceHash": SOURCE_HASH,
        "runnerClass": RUNNER_CLASS,
        "qualification": "qualified",
        "success": True,
        "families": ["normalizedMedian"],
        "results": [],
        "resourceResults": [],
        "absoluteCeilings": [],
        "soakScenarios": [],
        "soakAppliesTo": "candidate",
    }
    evaluation.update(overrides)

    return evaluation


class WorkflowProfileHandoffTests(unittest.TestCase):
    """Prove the receipt records the profile that actually measured.

    The workflow dispatches both comparison modes under one profile and the
    paired runner measures under the registered block profile. The recorder
    requires the two to agree, so a receipt built from the dispatch profile
    fails after a successful measurement -- which is what shipped. The values
    here are read out of the workflow and the contract rather than restated,
    because restating them is how the previous handoff test passed while the
    real one broke.
    """

    def setUp(self) -> None:
        """Read the shipped workflow and contract."""
        self.workflow = (
            REPOSITORY_ROOT / ".github" / "workflows" / "benchmark-scorecard.yml"
        ).read_text(encoding="utf-8")
        self.contract_path = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
        self.contract = json.loads(self.contract_path.read_text(encoding="utf-8"))

    def resolve(self, mode: str) -> str:
        """Resolve the attempt profile through the production entry point."""
        return subprocess.run(
            [
                sys.executable, "-m", "eng.performance.cli", "attempt-profile",
                "--contract", str(self.contract_path),
                "--profile", self.dispatch_profile(),
                "--comparison-mode", mode,
            ],
            cwd=REPOSITORY_ROOT,
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()

    def dispatch_profile(self) -> str:
        """Return the profile the workflow dispatches every job under."""
        found = set(re.findall(r"DISPATCH_PROFILE: (\S+)", self.workflow))
        self.assertEqual(1, len(found), "the workflow must dispatch one profile")

        return found.pop()

    def test_the_workflow_records_the_resolved_profile(self) -> None:
        """Reject a workflow that passes its dispatch profile to the recorder."""
        recorded = set(
            re.findall(r"--profile (\S+) \\\n\s+--attempt", self.workflow)
        ) or set(re.findall(r"--profile (\S+)", self.workflow))

        self.assertIn('"${ATTEMPT_PROFILE}"', recorded)
        self.assertNotIn(self.dispatch_profile(), recorded)

    def test_a_paired_run_records_the_block_profile(self) -> None:
        """Resolve to the profile the paired runner actually measures under."""
        expected = self.contract["pairedPolicy"]["blocks"]["profile"]

        self.assertEqual(expected, self.resolve("paired"))
        self.assertNotEqual(self.dispatch_profile(), self.resolve("paired"))

    def test_a_historical_run_keeps_the_dispatch_profile(self) -> None:
        """Leave the historical path exactly as it was."""
        self.assertEqual(self.dispatch_profile(), self.resolve("historical"))

    def test_the_resolved_profile_records_a_paired_measurement(self) -> None:
        """Run the handoff with the workflow's own values, end to end.

        This is the case the previous test could not see: it built both sides
        from the same literal, so a workflow that passed a different profile
        stayed invisible to it.
        """
        profile = self.resolve("paired")
        with tempfile.TemporaryDirectory() as directory:
            artifact_root = Path(directory) / "benchmarks" / TARGET
            report = artifact_root / "reports" / RUN_ID
            report.mkdir(parents=True)
            (report / "paired-evaluation.json").write_text(
                json.dumps(paired_evaluation(profile=profile)), encoding="utf-8"
            )
            for name in ("paired-evidence.json", "paired-soak.json"):
                (report / name).write_text(json.dumps({}), encoding="utf-8")

            receipt = attempts.record_attempt(
                artifact_root=artifact_root,
                report_directory=report,
                output=artifact_root / "performance-attempt.json",
                target=TARGET,
                profile=profile,
                attempt=1,
                run_id=RUN_ID,
                commit=COMMIT,
                source_hash=SOURCE_HASH,
                runner_class=RUNNER_CLASS,
                exit_code=0,
                comparison_mode="paired",
            )

            self.assertEqual("passed", receipt["status"])
            self.assertEqual(profile, receipt["profile"])

    def test_the_dispatch_profile_is_refused_for_a_paired_measurement(self) -> None:
        """Reproduce the break: the dispatch profile cannot record this run."""
        measured = self.resolve("paired")
        with tempfile.TemporaryDirectory() as directory:
            artifact_root = Path(directory) / "benchmarks" / TARGET
            report = artifact_root / "reports" / RUN_ID
            report.mkdir(parents=True)
            (report / "paired-evaluation.json").write_text(
                json.dumps(paired_evaluation(profile=measured)), encoding="utf-8"
            )
            for name in ("paired-evidence.json", "paired-soak.json"):
                (report / name).write_text(json.dumps({}), encoding="utf-8")

            with self.assertRaises(PerformanceEvidenceError) as captured:
                attempts.record_attempt(
                    artifact_root=artifact_root,
                    report_directory=report,
                    output=artifact_root / "performance-attempt.json",
                    target=TARGET,
                    profile=self.dispatch_profile(),
                    attempt=1,
                    run_id=RUN_ID,
                    commit=COMMIT,
                    source_hash=SOURCE_HASH,
                    runner_class=RUNNER_CLASS,
                    exit_code=0,
                    comparison_mode="paired",
                )

            self.assertIn("profile", str(captured.exception))


class EvaluateCliExitCodeTests(unittest.TestCase):
    """Prove the real command line reports broken evidence as broken.

    The exit code is what the attempt recorder reads. Exit 1 is `regression` --
    a verdict about the provider -- so a contradictory document that leaves
    through the wrong code convicts code it never measured.
    """

    def run_evaluate(self, evidence: dict[str, Any]) -> int:
        """Invoke `evaluate-paired` exactly as the runner does."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "paired-evidence.json"
            path.write_text(json.dumps(evidence), encoding="utf-8")

            return subprocess.run(
                [
                    sys.executable, "-m", "eng.performance.cli", "evaluate-paired",
                    "--contract", "benchmarks/performance-contract.json",
                    "--evidence", str(path),
                    "--output", str(Path(directory) / "evaluation.json"),
                ],
                cwd=REPOSITORY_ROOT,
                capture_output=True,
                text=True,
            ).returncode

    def test_a_contradictory_document_exits_seventy_eight(self) -> None:
        """Report invalid evidence as invalid, never as a regression."""
        contract = json.loads(
            (REPOSITORY_ROOT / "benchmarks" / "performance-contract.json").read_text(
                encoding="utf-8"
            )
        )
        registered = [workload["id"] for workload in contract["workloads"]]

        # Deliberately impossible: the runner cannot report convergence while
        # the minimum measurement duration was never reached.
        from eng.tests.test_performance_paired import (
            PairedEvidenceBuilder,
            uniform_blocks,
        )

        builder = PairedEvidenceBuilder(
            json.loads(
                (REPOSITORY_ROOT / "benchmarks" / "performance-contract.json")
                .read_text(encoding="utf-8")
            )
        )
        document = builder.evidence(
            [
                {"workloadId": identifier, "blocks": uniform_blocks(12, 1.0)}
                for identifier in registered
            ]
        )
        for record in document["tests"][0]["terminations"]:
            for side in ("reference", "candidate"):
                record[side]["minimumDurationReached"] = False

        self.assertEqual(78, self.run_evaluate(document))

    def test_combined_structural_damage_exits_seventy_eight(self) -> None:
        """Run the combined case through the real command line.

        Each of these passed on its own while the others were intact. Removing
        them together is what a truncated or hand-edited document actually
        looks like, and the exit code is what the attempt recorder reads.
        """
        from eng.tests.test_performance_paired import (
            PairedEvidenceBuilder,
            uniform_blocks,
        )

        contract_path = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
        builder = PairedEvidenceBuilder(
            json.loads(contract_path.read_text(encoding="utf-8"))
        )
        document = builder.evidence(
            [
                {"workloadId": workload["id"], "blocks": uniform_blocks(12, 1.0)}
                for workload in contract["workloads"]
            ]
        )

        document.pop("blockCount")
        document.pop("environments")
        document["candidateWorkloads"] = document["candidateWorkloads"][:1]
        document["executionOrder"]["executedBlockPatterns"] = (
            document["executionOrder"]["executedBlockPatterns"][:8]
        )

        self.assertEqual(78, self.run_evaluate(document))

    def test_a_foreign_contract_digest_exits_seventy_eight(self) -> None:
        """Run the digest case through the real command line.

        The evaluator loads a contract file; evidence naming another one was
        decided against other budgets, caps, and workloads than the ones about
        to judge it.
        """
        document = self.complete_document()
        document["contractDigest"] = "a" * 64

        self.assertEqual(78, self.run_evaluate(document))

    def test_an_incomplete_environment_exits_seventy_eight(self) -> None:
        """Run the empty-environment case through the real command line."""
        document = self.complete_document()
        document["environments"] = {"reference": {}, "candidate": {}}

        self.assertEqual(78, self.run_evaluate(document))

    def test_a_mistyped_candidate_structure_exits_seventy_eight(self) -> None:
        """Prove broken structure leaves as invalid evidence, not as exit 1.

        Exit 1 is what the attempt recorder reads as a regression, so these
        used to convict the provider they never measured.
        """
        for description, mutate in (
            ("missing block identifier",
             lambda d: d["candidateWorkloads"][0].pop("block")),
            ("null workload entry",
             lambda d: d["candidateWorkloads"][0]["workloads"].__setitem__(0, None)),
            ("boolean block identifier",
             lambda d: d["candidateWorkloads"][0].__setitem__("block", True)),
        ):
            with self.subTest(case=description):
                document = self.complete_document()
                mutate(document)

                self.assertEqual(78, self.run_evaluate(document))

    def complete_document(self) -> dict[str, Any]:
        """Return evidence the evaluator accepts, ready to be damaged."""
        from eng.performance.contract import sha256
        from eng.tests.test_performance_paired import (
            PairedEvidenceBuilder,
            uniform_blocks,
        )

        contract_path = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
        builder = PairedEvidenceBuilder(
            json.loads(contract_path.read_text(encoding="utf-8"))
        )
        document = builder.evidence(
            [
                {"workloadId": workload["id"], "blocks": uniform_blocks(12, 1.0)}
                for workload in contract["workloads"]
            ]
        )
        document["contractDigest"] = sha256(contract_path)

        return document

    def test_a_re_declared_ceiling_family_exits_seventy_eight(self) -> None:
        """Prove the command line refuses a self-chosen absolute budget.

        The `write` family allows twenty times the median `concurrency` does.
        Re-declaring one workload turned a real ceiling breach into a qualified
        release, and exit 0 is what the attempt recorder writes into a receipt.
        """
        contract_path = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
        subject = next(
            workload
            for workload in contract["workloads"]
            if workload["family"] == "concurrency"
        )
        budget = contract["familyBudgets"]["concurrency"]["medianNanoseconds"]

        slow = float(budget) + 50000000.0

        def breach(**fields: Any) -> dict[str, Any]:
            document = self.complete_document()
            for test in document["tests"]:
                if test["workloadId"] != subject["id"]:
                    continue
                for field in ("blocks", "latencies"):
                    for measured in test[field]:
                        for side in ("reference", "candidate"):
                            measured[side] = [slow] * len(measured[side])
            for block in document["candidateWorkloads"]:
                for workload in block["workloads"]:
                    if workload["id"] == subject["id"]:
                        workload.update(
                            medianNanoseconds=slow,
                            p95Nanoseconds=slow,
                            p99Nanoseconds=slow,
                            **fields,
                        )

            return document

        self.assertEqual(1, self.run_evaluate(breach()))
        self.assertEqual(78, self.run_evaluate(breach(family="write")))

    def test_a_pair_that_degraded_together_exits_one(self) -> None:
        """Prove the absolute ceiling reaches the command line.

        Both sides equally slow gives a ratio of one, so only the absolute
        budget can catch it -- and it read a per-block summary the document
        wrote freely rather than the samples the pairing was formed from.
        """
        contract_path = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
        subject = next(
            workload
            for workload in contract["workloads"]
            if workload["family"] == "concurrency"
        )
        slow = float(
            contract["familyBudgets"]["concurrency"]["medianNanoseconds"]
        ) + 50000000.0

        document = self.complete_document()
        for test in document["tests"]:
            if test["workloadId"] != subject["id"]:
                continue
            for field in ("blocks", "latencies"):
                for measured in test[field]:
                    for side in ("reference", "candidate"):
                        measured[side] = [slow] * len(measured[side])
        honest = json.loads(json.dumps(document))
        for block in honest["candidateWorkloads"]:
            for workload in block["workloads"]:
                if workload["id"] == subject["id"]:
                    for field in (
                        "medianNanoseconds",
                        "p95Nanoseconds",
                        "p99Nanoseconds",
                    ):
                        workload[field] = slow

        # The untouched projection still claims the original latency, which no
        # longer follows from the samples it summarizes.
        self.assertEqual(78, self.run_evaluate(document))
        self.assertEqual(1, self.run_evaluate(honest))

    def test_mismatched_sample_populations_exit_seventy_eight(self) -> None:
        """Prove the command line refuses a block measured in three pieces.

        The pairing reads the normalized samples and the absolute ceiling reads
        the raw ones; a document that carried different populations for the two
        qualified, because nothing said they describe the same operations.
        """
        cases = {
            "one raw sample against sixteen normalized":
                lambda test: test["latencies"][0].__setitem__(
                    "candidate", test["latencies"][0]["candidate"][:1]),
            "calibration population differs":
                lambda test: test["calibrations"][0].__setitem__(
                    "candidate", test["calibrations"][0]["candidate"][:1]),
            "normalized sample does not follow from its latency":
                lambda test: test["blocks"][0]["candidate"].__setitem__(
                    0, test["blocks"][0]["candidate"][0] * 2),
        }
        for description, mutate in cases.items():
            with self.subTest(case=description):
                document = self.complete_document()
                mutate(document["tests"][0])

                self.assertEqual(78, self.run_evaluate(document))

    def test_a_projection_beyond_its_shape_exits_seventy_eight(self) -> None:
        """Prove the command line refuses a second measurement record.

        The audit summary is documented as a checked projection; carrying the
        workload report's raw arrays put an unchecked copy of every sample and
        pulse into the same document.
        """
        cases = {
            "raw samples": ("normalizedSamples", [99.0]),
            "raw latencies": ("samplesNanoseconds", [1.0]),
            "calibration pulses": ("calibrationPulseNanoseconds", [7.0]),
            "termination record": ("terminationReason", "sample_cap_reached"),
        }
        for description, (field, value) in cases.items():
            with self.subTest(case=description):
                document = self.complete_document()
                document["candidateWorkloads"][0]["workloads"][0][field] = value

                self.assertEqual(78, self.run_evaluate(document))

    def test_an_unmeasured_calibration_exits_seventy_eight(self) -> None:
        """Prove the command line refuses a divisor no pulse produced.

        The calibration scales the normalized samples the pairing decides on,
        so a freely chosen divisor moves the release outcome with every raw
        latency left untouched.
        """
        cases = {
            "divisor no pulse measured": lambda test: (
                test["calibrations"][0].__setitem__(
                    "candidate",
                    [value * 1.3 for value in test["calibrations"][0]["candidate"]]),
                test["blocks"][0].__setitem__(
                    "candidate",
                    [
                        latency / divisor
                        for latency, divisor in zip(
                            test["latencies"][0]["candidate"],
                            [
                                value * 1.3
                                for value in test["calibrations"][0]["candidate"]
                            ],
                        )
                    ]),
            ),
            "pulse index outside the list":
                lambda test: test["calibrationPulseIndices"][0][
                    "candidate"].__setitem__(-1, 99),
            "does not start at pulse zero":
                lambda test: test["calibrationPulseIndices"][0].__setitem__(
                    "candidate",
                    [
                        index + 1
                        for index in test["calibrationPulseIndices"][0]["candidate"]
                    ]),
            "a pulse no sample used":
                lambda test: test["calibrationPulses"][0]["candidate"].append(1.0),
        }
        for description, mutate in cases.items():
            with self.subTest(case=description):
                document = self.complete_document()
                mutate(document["tests"][0])

                self.assertEqual(78, self.run_evaluate(document))

    def test_malformed_ceiling_metrics_exit_seventy_eight(self) -> None:
        """Prove a broken ceiling metric never convicts the provider.

        These left through the general error domain as exit 1, which the
        attempt recorder reads as a regression.
        """
        cases = {
            "unknown family": {"family": "not-a-family"},
            "null family": {"family": None},
            "text median": {"medianNanoseconds": "bad"},
            "null median": {"medianNanoseconds": None},
            "boolean allocation": {"allocatedBytesPerOperation": True},
            "negative collections": {"gen2CollectionsPer1000": -1},
            "infinite p99": {"p99Nanoseconds": float("inf")},
        }
        for description, fields in cases.items():
            with self.subTest(case=description):
                document = self.complete_document()
                for block in document["candidateWorkloads"]:
                    for workload in block["workloads"]:
                        workload.update(fields)

                self.assertEqual(78, self.run_evaluate(document))

    def test_mistyped_test_structures_exit_seventy_eight(self) -> None:
        """Prove broken test and resource shapes leave as invalid evidence.

        Building a set from `workloadId` before checking it raised a plain
        TypeError on a list, and a null block raised one on comparison. Both
        leave the command line as exit 1 -- which the attempt recorder reads as
        a regression about the provider.
        """
        for description, mutate in (
            ("null resource block",
             lambda d: d["tests"][0]["resources"].__setitem__(0, None)),
            ("null measurement block",
             lambda d: d["tests"][0]["blocks"].__setitem__(0, None)),
            ("null workload identifier",
             lambda d: d["tests"][0].__setitem__("workloadId", None)),
            ("list workload identifier",
             lambda d: d["tests"][0].__setitem__("workloadId", [])),
            ("missing resource metric",
             lambda d: d["tests"][0]["resources"][0]["candidate"].pop(
                 "allocatedBytesPerOperation")),
        ):
            with self.subTest(case=description):
                document = self.complete_document()
                mutate(document)

                self.assertEqual(78, self.run_evaluate(document))

    def test_a_meaningless_revision_exits_seventy_eight(self) -> None:
        """Refuse provenance that names no revision at all.

        No later gate re-checks the reference revision: the release comparison
        protects the candidate commit and takes the reference on trust.
        """
        document = self.complete_document()
        document["referenceCommit"] = "not-a-commit"

        self.assertEqual(78, self.run_evaluate(document))

    def test_a_truncated_document_exits_seventy_eight(self) -> None:
        """Report a document missing its identity as invalid evidence."""
        self.assertEqual(78, self.run_evaluate({"tests": []}))

    def test_a_partial_matrix_exits_seventy_eight(self) -> None:
        """Report evidence covering part of the matrix as invalid evidence."""
        from eng.tests.test_performance_paired import (
            PairedEvidenceBuilder,
            uniform_blocks,
        )

        builder = PairedEvidenceBuilder(
            json.loads(
                (REPOSITORY_ROOT / "benchmarks" / "performance-contract.json")
                .read_text(encoding="utf-8")
            )
        )
        document = builder.evidence(
            [{"workloadId": "steady", "blocks": uniform_blocks(12, 1.0)}]
        )

        self.assertEqual(78, self.run_evaluate(document))


class PairedAttemptHandoffTests(unittest.TestCase):
    """Prove a qualified paired run becomes a selectable attempt."""

    def setUp(self) -> None:
        """Lay out the artifact tree a paired run leaves behind."""
        self.directory = tempfile.TemporaryDirectory()
        self.artifact_root = Path(self.directory.name) / "benchmarks" / TARGET
        self.report_directory = self.artifact_root / "reports" / RUN_ID
        self.report_directory.mkdir(parents=True)
        self.write_paired_output()

    def tearDown(self) -> None:
        """Release the fixture."""
        self.directory.cleanup()

    def write_paired_output(self, **overrides: Any) -> None:
        """Write the three files a paired run produces."""
        (self.report_directory / "paired-evaluation.json").write_text(
            json.dumps(paired_evaluation(**overrides)), encoding="utf-8"
        )
        (self.report_directory / "paired-evidence.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 2,
                    "kind": "paired-performance-evidence",
                    "runId": RUN_ID,
                    "target": TARGET,
                    "profile": PROFILE,
                    "commit": COMMIT,
                    "sourceHash": SOURCE_HASH,
                    "runnerClass": RUNNER_CLASS,
                    "candidateCommit": COMMIT,
                    "referenceCommit": "c" * 40,
                }
            ),
            encoding="utf-8",
        )
        (self.report_directory / "paired-soak.json").write_text(
            json.dumps({"schemaVersion": 2, "kind": "performance-soak"}),
            encoding="utf-8",
        )

    def record(self, *, exit_code: int = 0, attempt: int = 1,
               comparison_mode: str = "paired") -> dict[str, Any]:
        """Record one attempt through the production entry point."""
        return attempts.record_attempt(
            artifact_root=self.artifact_root,
            report_directory=self.report_directory,
            output=self.artifact_root / "performance-attempt.json",
            target=TARGET,
            profile=PROFILE,
            attempt=attempt,
            run_id=RUN_ID,
            commit=COMMIT,
            source_hash=SOURCE_HASH,
            runner_class=RUNNER_CLASS,
            exit_code=exit_code,
            comparison_mode=comparison_mode,
        )

    def test_a_qualified_paired_run_records_as_passed(self) -> None:
        """Close the handoff the release path failed at."""
        receipt = self.record()

        self.assertEqual("passed", receipt["status"])
        self.assertEqual("paired", receipt["comparisonMode"])
        self.assertEqual(
            f"reports/{RUN_ID}/paired-evaluation.json",
            receipt["evaluationRelativePath"],
        )

    def test_the_historical_layout_is_not_accepted_for_a_paired_run(self) -> None:
        """Reproduce the original break and prove it is mode-specific.

        A paired run that is recorded as historical still finds no
        `evidence/performance-evaluation.json`, which is exactly what happened
        to every successful paired measurement.
        """
        with self.assertRaises(PerformanceEvidenceError) as captured:
            self.record(comparison_mode="historical")

        self.assertIn("performance-evaluation.json", str(captured.exception))

    def test_the_measurements_behind_the_verdict_are_bound(self) -> None:
        """Bind the evidence, not only the verdict.

        A receipt that pinned the verdict alone would keep pointing at a
        conclusion whose measurements had been replaced.
        """
        receipt = self.record()
        bound = {entry["relativePath"] for entry in receipt["evidenceBindings"]}

        self.assertEqual(
            {
                f"reports/{RUN_ID}/paired-evidence.json",
                f"reports/{RUN_ID}/paired-soak.json",
            },
            bound,
        )

    def test_a_missing_companion_is_refused(self) -> None:
        """Refuse a verdict whose measurements are not present."""
        (self.report_directory / "paired-soak.json").unlink()

        with self.assertRaises(PerformanceEvidenceError):
            self.record()

    def test_a_verdict_for_another_run_is_refused(self) -> None:
        """Refuse an evaluation that describes a different run."""
        self.write_paired_output(runId="some-other-run")

        with self.assertRaises(PerformanceEvidenceError):
            self.record()

    def test_an_unqualified_verdict_cannot_pass(self) -> None:
        """Refuse to record a regression as a passing attempt.

        The exit code and the document must agree. A caller that lost the exit
        code would otherwise record a regression as a successful measurement.
        """
        self.write_paired_output(qualification="regression", success=False)

        with self.assertRaises(PerformanceEvidenceError):
            self.record()

    def test_selection_carries_the_paired_run_to_a_qualified_artifact(self) -> None:
        """Walk the full chain: paired output, receipt, selection, artifact."""
        self.record()
        destination = Path(self.directory.name) / "selected"

        selection = attempts.select_attempt(
            receipt_paths=[self.artifact_root / "performance-attempt.json"],
            destination=destination,
        )

        self.assertEqual(1, selection["selectedAttempt"])
        for name in ("paired-evaluation.json", "paired-evidence.json",
                     "paired-soak.json"):
            with self.subTest(file=name):
                self.assertTrue(
                    (destination / "reports" / RUN_ID / name).is_file(),
                    f"{name} did not reach the selected artifact",
                )

    def test_selection_refuses_replaced_measurements(self) -> None:
        """Refuse a selection whose bound evidence changed after recording."""
        self.record()
        (self.report_directory / "paired-evidence.json").write_text(
            json.dumps({"replaced": True}), encoding="utf-8"
        )

        with self.assertRaises(PerformanceEvidenceError):
            attempts.select_attempt(
                receipt_paths=[self.artifact_root / "performance-attempt.json"],
                destination=Path(self.directory.name) / "selected",
            )

    def test_two_modes_cannot_be_mixed_across_attempts(self) -> None:
        """Refuse a retry that measured the same commit a different way."""
        first = self.artifact_root / "attempt-1.json"
        second = self.artifact_root / "attempt-2.json"
        attempts.record_attempt(
            artifact_root=self.artifact_root,
            report_directory=self.report_directory,
            output=first,
            target=TARGET, profile=PROFILE, attempt=1, run_id=RUN_ID,
            commit=COMMIT, source_hash=SOURCE_HASH, runner_class=RUNNER_CLASS,
            exit_code=75, comparison_mode="paired",
        )
        attempts.record_attempt(
            artifact_root=self.artifact_root,
            report_directory=self.report_directory,
            output=second,
            target=TARGET, profile=PROFILE, attempt=2, run_id=RUN_ID,
            commit=COMMIT, source_hash=SOURCE_HASH, runner_class=RUNNER_CLASS,
            exit_code=75, comparison_mode="historical",
        )

        with self.assertRaises(PerformanceEvidenceError) as captured:
            attempts.select_attempt(
                receipt_paths=[first, second],
                destination=Path(self.directory.name) / "selected",
            )

        self.assertIn("comparisonMode", str(captured.exception))


if __name__ == "__main__":
    unittest.main()
