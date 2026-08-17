"""Performance attempt classification and selection contract tests."""

from __future__ import annotations

import json
import tempfile
import unittest
from argparse import Namespace
from inspect import Parameter, signature
from pathlib import Path
from unittest.mock import patch

from eng.performance import attempts, cli, evaluation
from eng.performance.contract import (
    ENVIRONMENT_NOT_COMPARABLE_EXIT_CODE,
    INVALID_EVIDENCE_EXIT_CODE,
    MEASUREMENT_QUALITY_EXIT_CODE,
    RECALIBRATION_REQUIRED_EXIT_CODE,
    MeasurementQualityError,
    PerformanceEvidenceError,
    sha256,
    write_json,
)


class PerformanceAttemptTests(unittest.TestCase):
    """Keep bounded retries from hiding benchmark or evidence failures."""

    _commit = "1" * 40
    _source_hash = "2" * 64

    def test_comparison_mode_has_no_implicit_default(self) -> None:
        """Keep every attempt boundary from silently selecting historical mode."""
        functions = (
            attempts.evaluation_path_for,
            attempts._validate_evaluation_identity,
            attempts._companion_bindings,
            attempts.record_attempt,
        )

        for function in functions:
            with self.subTest(function=function.__name__):
                parameter = signature(function).parameters["comparison_mode"]
                self.assertIs(Parameter.empty, parameter.default)

    def test_historical_writer_and_attempt_reader_share_document_identity(self) -> None:
        """Carry the real historical writer output through attempt recording."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            artifact_root = Path(temporary_directory) / "attempt-1"
            report_directory = artifact_root / "report"
            evidence_directory = report_directory / "evidence"
            evidence_directory.mkdir(parents=True)
            contract_path = artifact_root / "contract.json"
            host_path = artifact_root / "host.json"
            workload_path = artifact_root / "workloads.json"
            bdn_path = artifact_root / "bdn.json"
            write_json(
                contract_path,
                {
                    "contractVersion": "2026-08-16",
                    "evidenceMaximumAgeHours": 24,
                    "profiles": {
                        "scorecard": {
                            "baselineRequired": True,
                            "soakRequired": False,
                        }
                    },
                },
            )
            write_json(host_path, {})
            write_json(
                workload_path,
                {
                    "environment": {},
                    "commit": self._commit,
                    "sourceHash": self._source_hash,
                    "runnerClass": "github-ubuntu-24.04",
                },
            )
            write_json(
                bdn_path,
                {
                    "success": True,
                    "hostEnvironment": {},
                    "rawReports": [],
                    "controls": [],
                },
            )
            arguments = Namespace(
                contract=str(contract_path),
                host=str(host_path),
                workloads=str(workload_path),
                soak=None,
                bdn=str(bdn_path),
                baseline=str(artifact_root / "unused-baseline.json"),
                run_id="historical-run",
                target="mysql84",
                profile="scorecard",
                mode="seed",
            )
            with (
                patch.object(evaluation, "validate_contract"),
                patch.object(
                    evaluation,
                    "validate_host_preflight",
                    return_value={},
                ),
                patch.object(
                    evaluation,
                    "validate_workload_report",
                    return_value=[],
                ),
                patch.object(evaluation, "validate_host_workload_binding"),
                patch.object(
                    evaluation,
                    "validate_absolute_budgets",
                    return_value=[],
                ),
                patch.object(evaluation, "require_identity"),
                patch.object(evaluation, "validate_bdn_workload_environment"),
            ):
                document = evaluation.evaluate(arguments)

            self.assertEqual(3, document["schemaVersion"])
            self.assertEqual("performance-evaluation", document["kind"])
            write_json(
                evidence_directory / "performance-evaluation.json",
                document,
            )
            receipt = attempts.record_attempt(
                artifact_root=artifact_root,
                report_directory=report_directory,
                output=artifact_root / "performance-attempt.json",
                target="mysql84",
                profile="scorecard",
                attempt=1,
                run_id="historical-run",
                commit=self._commit,
                source_hash=self._source_hash,
                runner_class="github-ubuntu-24.04",
                exit_code=0,
                comparison_mode="historical",
            )

            self.assertEqual("passed", receipt["status"])

    def _write_evaluation(
        self,
        artifact_root: Path,
        *,
        run_id: str,
        target: str = "mysql84",
    ) -> Path:
        """Create the minimum identity-bearing successful evaluation."""
        path = (
            artifact_root
            / "report"
            / "evidence"
            / "performance-evaluation.json"
        )
        write_json(
            path,
            {
                "schemaVersion": 3,
                "kind": "performance-evaluation",
                "success": True,
                "target": target,
                "profile": "scorecard",
                "runId": run_id,
                "commit": self._commit,
                "sourceHash": self._source_hash,
                "runnerClass": "github-ubuntu-24.04",
            },
        )
        return path

    def _record(
        self,
        root: Path,
        *,
        attempt: int,
        exit_code: int,
        run_id: str | None = None,
        target: str = "mysql84",
    ) -> Path:
        """Record one fixture attempt and return its receipt path."""
        run_id = run_id or f"run-{attempt}"
        if exit_code == 0:
            self._write_evaluation(root, run_id=run_id, target=target)

        receipt = root / "performance-attempt.json"
        attempts.record_attempt(
            artifact_root=root,
            report_directory=root / "report",
            output=receipt,
            target=target,
            profile="scorecard",
            attempt=attempt,
            run_id=run_id,
            commit=self._commit,
            source_hash=self._source_hash,
            runner_class="github-ubuntu-24.04",
            exit_code=exit_code,
            comparison_mode="historical",
        )
        return receipt

    def _select(
        self,
        receipt_paths: list[Path],
        destination: Path,
    ) -> tuple[dict[str, object], Path]:
        """Select, persist, and independently verify one qualified attempt."""
        selection = attempts.select_attempt(
            receipt_paths=receipt_paths,
            destination=destination,
        )
        selection_path = destination / "performance-attempt-selection.json"
        write_json(selection_path, selection)
        attempts.verify_selection(
            artifact_root=destination,
            selection_path=selection_path,
        )
        return selection, selection_path

    def _record_dispersion_attempt(
        self,
        root: Path,
        *,
        attempt: int,
        state: str,
        contract_digest: str = "3" * 64,
        run_id: str | None = None,
    ) -> Path:
        """Record one retryable paired attempt with its drift projection."""
        run_id = run_id or f"paired-run-{attempt}"
        report_directory = root / "report"
        report_directory.mkdir(parents=True)
        realized = 0.08 if state == "drift" else 0.04
        write_json(
            report_directory / "paired-dispersion-observation.json",
            {
                "schemaVersion": 1,
                "kind": "paired-dispersion-observation",
                "target": "mysql84",
                "runId": run_id,
                "commit": self._commit,
                "sourceHash": self._source_hash,
                "runnerClass": "github-ubuntu-latest-x64",
                "contractDigest": contract_digest,
                "referenceCommit": "4" * 40,
                "metric": "normalizedMedian",
                "aggregation": "geometric-mean-across-workloads",
                "realizedLogRatioStandardDeviation": realized,
                "registeredUpperBound": 0.06,
                "state": state,
            },
        )
        receipt = root / "performance-attempt.json"
        attempts.record_attempt(
            artifact_root=root,
            report_directory=report_directory,
            output=receipt,
            target="mysql84",
            profile="paired-block",
            attempt=attempt,
            run_id=run_id,
            commit=self._commit,
            source_hash=self._source_hash,
            runner_class="github-ubuntu-latest-x64",
            exit_code=MEASUREMENT_QUALITY_EXIT_CODE,
            comparison_mode="paired",
        )
        return receipt

    def test_exit_codes_have_non_overlapping_states(self) -> None:
        """Give every terminating condition its own attempt state.

        The distinction is load-bearing: an infrastructure condition must be
        retryable while a verdict about the code must not, and an unclassified
        crash must not be reported as a provider regression.
        """
        self.assertEqual("passed", attempts.classify_exit_code(0))
        self.assertEqual("regression", attempts.classify_exit_code(1))
        self.assertEqual(
            "measurement-inconclusive",
            attempts.classify_exit_code(MEASUREMENT_QUALITY_EXIT_CODE),
        )
        self.assertEqual(
            "environment-not-comparable",
            attempts.classify_exit_code(ENVIRONMENT_NOT_COMPARABLE_EXIT_CODE),
        )
        self.assertEqual(
            "recalibration-required",
            attempts.classify_exit_code(RECALIBRATION_REQUIRED_EXIT_CODE),
        )
        self.assertEqual(
            "invalid-evidence",
            attempts.classify_exit_code(INVALID_EVIDENCE_EXIT_CODE),
        )
        self.assertEqual("invalid-evidence", attempts.classify_exit_code(139))

    def test_only_infrastructure_states_are_retryable(self) -> None:
        """Keep a retry from selecting away a verdict about the code."""
        self.assertTrue(attempts.is_retryable("measurement-inconclusive"))
        self.assertTrue(attempts.is_retryable("environment-not-comparable"))
        for state in ("passed", "regression", "recalibration-required",
                      "invalid-evidence"):
            with self.subTest(state=state):
                self.assertFalse(attempts.is_retryable(state))
        with self.assertRaises(PerformanceEvidenceError):
            attempts.is_retryable("unknown-state")

    def test_qualification_derives_from_the_final_attempt(self) -> None:
        """Separate the attempt domain from the release verdict.

        Two attempts that could not compare their environments block the
        release without ever asserting that the provider regressed.
        """
        cases = {
            ("passed",): "qualified",
            ("measurement-inconclusive", "passed"): "qualified",
            ("regression",): "regression",
            ("environment-not-comparable", "regression"): "regression",
            ("environment-not-comparable", "environment-not-comparable"):
                "inconclusive",
            ("measurement-inconclusive", "measurement-inconclusive"):
                "inconclusive",
            ("recalibration-required",): "recalibration-required",
            ("invalid-evidence",): "invalid-evidence",
        }
        for states, expected in cases.items():
            with self.subTest(states=states):
                self.assertEqual(expected, attempts.qualification_state(list(states)))
        with self.assertRaises(PerformanceEvidenceError):
            attempts.qualification_state([])

    def test_non_passing_attempt_does_not_require_report_artifacts(self) -> None:
        """Persist early measurement and gate failures even without reports."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory) / "attempt"

            receipt = self._record(
                root,
                attempt=1,
                exit_code=MEASUREMENT_QUALITY_EXIT_CODE,
            )

            payload = json.loads(receipt.read_text(encoding="utf-8"))
            self.assertEqual("measurement-inconclusive", payload["status"])
            self.assertIsNone(payload["evaluationRelativePath"])

    def test_first_pass_is_selected_without_retry(self) -> None:
        """Select a conclusive first attempt and preserve its evidence tree."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipt = self._record(root / "attempt-1", attempt=1, exit_code=0)
            destination = root / "selected"

            selection, _ = self._select([receipt], destination)

            self.assertEqual(1, selection["selectedAttempt"])
            self.assertEqual("historical", selection["comparisonMode"])
            selected_evaluation = destination / selection["evaluationRelativePath"]
            self.assertTrue(selected_evaluation.is_file())

    def test_one_inconclusive_attempt_allows_one_passing_retry(self) -> None:
        """Select attempt two only after the typed measurement-quality state."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            first = self._record(
                root / "attempt-1",
                attempt=1,
                exit_code=MEASUREMENT_QUALITY_EXIT_CODE,
            )
            second = self._record(root / "attempt-2", attempt=2, exit_code=0)

            selection, _ = self._select(
                [first, second],
                root / "selected",
            )

            self.assertEqual(2, selection["selectedAttempt"])
            self.assertEqual(
                ["measurement-inconclusive", "passed"],
                [receipt["status"] for receipt in selection["receipts"]],
            )
            for receipt in selection["receipts"]:
                selected_receipt = root / "selected" / receipt["relativePath"]
                self.assertTrue(selected_receipt.is_file())
                self.assertEqual(receipt["sha256"], sha256(selected_receipt))

    def test_retry_cannot_mask_first_hard_failure(self) -> None:
        """Reject a passing retry after a correctness or budget failure."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            first = self._record(root / "attempt-1", attempt=1, exit_code=1)
            second = self._record(root / "attempt-2", attempt=2, exit_code=0)

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "retry cannot mask",
            ):
                attempts.select_attempt(
                    receipt_paths=[first, second],
                    destination=root / "selected",
                )

    def test_two_inconclusive_attempts_remain_inconclusive(self) -> None:
        """Do not turn repeated runner noise into a provider regression."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record(
                    root / f"attempt-{attempt}",
                    attempt=attempt,
                    exit_code=MEASUREMENT_QUALITY_EXIT_CODE,
                )
                for attempt in (1, 2)
            ]

            with self.assertRaisesRegex(
                MeasurementQualityError,
                "Both benchmark attempts were measurement-inconclusive",
            ):
                attempts.select_attempt(
                    receipt_paths=receipts,
                    destination=root / "selected",
                )

    def test_selection_rejects_identity_drift_between_attempts(self) -> None:
        """Bind both attempts to the same target and immutable source state."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            first = self._record(
                root / "attempt-1",
                attempt=1,
                exit_code=MEASUREMENT_QUALITY_EXIT_CODE,
            )
            second = self._record(
                root / "attempt-2",
                attempt=2,
                exit_code=0,
                target="mariadb118",
            )

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "identity mismatch",
            ):
                attempts.select_attempt(
                    receipt_paths=[first, second],
                    destination=root / "selected",
                )

    def test_selection_rejects_evaluation_tampering(self) -> None:
        """Verify the selected evaluation digest immediately before copying."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            artifact_root = root / "attempt-1"
            receipt = self._record(artifact_root, attempt=1, exit_code=0)
            evaluation = (
                artifact_root
                / "report"
                / "evidence"
                / "performance-evaluation.json"
            )
            evaluation.write_text("{}\n", encoding="utf-8")

            with self.assertRaises(PerformanceEvidenceError):
                attempts.select_attempt(
                    receipt_paths=[receipt],
                    destination=root / "selected",
                )

    def test_selection_rejects_receipt_path_traversal(self) -> None:
        """Never resolve a receipt-owned evidence path outside its artifact."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipt = self._record(root / "attempt-1", attempt=1, exit_code=0)
            payload = json.loads(receipt.read_text(encoding="utf-8"))
            payload["evaluationRelativePath"] = "../performance-evaluation.json"
            write_json(receipt, payload)

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "must remain below",
            ):
                attempts.select_attempt(
                    receipt_paths=[receipt],
                    destination=root / "selected",
                )

    def test_passed_receipt_binds_evaluation_digest(self) -> None:
        """Record the exact successful evaluation used by later selection."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            artifact_root = Path(temporary_directory) / "attempt-1"
            receipt = self._record(artifact_root, attempt=1, exit_code=0)
            payload = json.loads(receipt.read_text(encoding="utf-8"))
            evaluation = artifact_root / payload["evaluationRelativePath"]

            self.assertEqual(sha256(evaluation), payload["evaluationSha256"])

    def test_selection_verification_rejects_tampered_evaluation(self) -> None:
        """Detect mutation after selection and before downstream consumption."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipt = self._record(root / "attempt-1", attempt=1, exit_code=0)
            selected_root = root / "selected"
            selection, selection_path = self._select([receipt], selected_root)
            evaluation = selected_root / str(selection["evaluationRelativePath"])
            evaluation.write_text("{}\n", encoding="utf-8")

            with self.assertRaises(PerformanceEvidenceError):
                attempts.verify_selection(
                    artifact_root=selected_root,
                    selection_path=selection_path,
                )

    def test_two_independent_drift_observations_are_confirmed(self) -> None:
        """Bind both fresh-runner attempts before declaring fleet drift."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / f"attempt-{attempt}",
                    attempt=attempt,
                    state="drift",
                )
                for attempt in (1, 2)
            ]
            output = root / "paired-dispersion-confirmation.json"

            confirmation = attempts.record_dispersion_confirmation(
                receipt_paths=receipts,
                output=output,
            )

            self.assertIsNotNone(confirmation)
            assert confirmation is not None
            self.assertEqual("confirmed-drift", confirmation["state"])
            self.assertEqual(
                [1, 2],
                [item["attempt"] for item in confirmation["attempts"]],
            )
            self.assertEqual(
                ["measurement-inconclusive", "measurement-inconclusive"],
                [item["status"] for item in confirmation["attempts"]],
            )
            self.assertTrue(output.is_file())
            self.assertEqual(
                [sha256(receipt) for receipt in receipts],
                [item["receiptSha256"] for item in confirmation["attempts"]],
            )

    def test_confirmation_cli_writes_the_bound_document(self) -> None:
        """Exercise the command invoked by the target workflow."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / f"attempt-{attempt}",
                    attempt=attempt,
                    state="drift",
                )
                for attempt in (1, 2)
            ]
            output = root / "paired-dispersion-confirmation.json"

            exit_code = cli.main(
                [
                    "record-dispersion-confirmation",
                    "--receipt",
                    str(receipts[0]),
                    "--receipt",
                    str(receipts[1]),
                    "--output",
                    str(output),
                ]
            )

            self.assertEqual(0, exit_code)
            self.assertEqual(
                "confirmed-drift",
                json.loads(output.read_text(encoding="utf-8"))["state"],
            )

    def test_one_stable_observation_does_not_confirm_drift(self) -> None:
        """Do not let one noisy runner create a governed drift event."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / "attempt-1", attempt=1, state="drift"
                ),
                self._record_dispersion_attempt(
                    root / "attempt-2", attempt=2, state="stable"
                ),
            ]

            confirmation = attempts.record_dispersion_confirmation(
                receipt_paths=receipts,
                output=root / "paired-dispersion-confirmation.json",
            )

            self.assertIsNotNone(confirmation)
            assert confirmation is not None
            self.assertEqual("not-confirmed", confirmation["state"])

    def test_missing_second_observation_produces_no_confirmation(self) -> None:
        """Distinguish absent measurement evidence from stable dispersion."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / f"attempt-{attempt}",
                    attempt=attempt,
                    state="drift",
                )
                for attempt in (1, 2)
            ]
            second_observation = (
                receipts[1].parent
                / "report"
                / "paired-dispersion-observation.json"
            )
            second_observation.unlink()
            second_observation.parent.rmdir()
            output = root / "paired-dispersion-confirmation.json"

            confirmation = attempts.record_dispersion_confirmation(
                receipt_paths=receipts,
                output=output,
            )

            self.assertIsNone(confirmation)
            self.assertFalse(output.exists())

    def test_confirmation_requires_distinct_attempt_run_identities(self) -> None:
        """Do not relabel one runner observation as two independent samples."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / f"attempt-{attempt}",
                    attempt=attempt,
                    state="drift",
                    run_id="same-run",
                )
                for attempt in (1, 2)
            ]

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "distinct attempt run identities",
            ):
                attempts.record_dispersion_confirmation(
                    receipt_paths=receipts,
                    output=root / "paired-dispersion-confirmation.json",
                )

    def test_confirmation_rejects_observation_identity_drift(self) -> None:
        """Refuse to combine observations produced by different contracts."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / "attempt-1",
                    attempt=1,
                    state="drift",
                ),
                self._record_dispersion_attempt(
                    root / "attempt-2",
                    attempt=2,
                    state="drift",
                    contract_digest="5" * 64,
                ),
            ]

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "contractDigest",
            ):
                attempts.record_dispersion_confirmation(
                    receipt_paths=receipts,
                    output=root / "paired-dispersion-confirmation.json",
                )

    def test_confirmation_rejects_a_forged_observation_state(self) -> None:
        """Recompute drift instead of trusting the projected state label."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / f"attempt-{attempt}",
                    attempt=attempt,
                    state="drift",
                )
                for attempt in (1, 2)
            ]
            observation_path = (
                receipts[1].parent
                / "report"
                / "paired-dispersion-observation.json"
            )
            observation = json.loads(observation_path.read_text(encoding="utf-8"))
            observation["state"] = "stable"
            write_json(observation_path, observation)

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "does not match",
            ):
                attempts.record_dispersion_confirmation(
                    receipt_paths=receipts,
                    output=root / "paired-dispersion-confirmation.json",
                )

    def test_confirmation_rejects_a_forged_retry_policy(self) -> None:
        """Require the first rerun to be authorized by its typed receipt."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipts = [
                self._record_dispersion_attempt(
                    root / f"attempt-{attempt}",
                    attempt=attempt,
                    state="drift",
                )
                for attempt in (1, 2)
            ]
            first = json.loads(receipts[0].read_text(encoding="utf-8"))
            first["retryEligible"] = False
            write_json(receipts[0], first)

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "retry policy",
            ):
                attempts.record_dispersion_confirmation(
                    receipt_paths=receipts,
                    output=root / "paired-dispersion-confirmation.json",
                )

if __name__ == "__main__":
    unittest.main()
