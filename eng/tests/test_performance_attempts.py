"""Performance attempt classification and selection contract tests."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from eng.performance import attempts
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

    def test_verified_selection_imports_complete_report(self) -> None:
        """Import the selected report without rerunning or reevaluating it."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipt = self._record(root / "attempt-1", attempt=1, exit_code=0)
            selected_root = root / "selected"
            selection, selection_path = self._select([receipt], selected_root)
            imported_root = root / "imported"

            import_receipt = attempts.import_selection(
                artifact_root=selected_root,
                selection_path=selection_path,
                destination=imported_root,
                expected_target="mysql84",
                expected_commit=self._commit,
            )
            verified = attempts.verify_imported_selection(
                destination=imported_root,
                expected_target="mysql84",
                expected_commit=self._commit,
            )

            self.assertEqual(selection["selectedRunId"], verified["selectedRunId"])
            self.assertEqual(import_receipt, verified)
            self.assertTrue(
                (
                    imported_root
                    / "evidence"
                    / "performance-evaluation.json"
                ).is_file()
            )
            self.assertTrue(
                (
                    imported_root
                    / "qualified-artifact"
                    / "performance-attempt-selection.json"
                ).is_file()
            )

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

    def test_import_verification_rejects_tampered_evaluation(self) -> None:
        """Detect mutation after a qualified report enters the RC stage."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipt = self._record(root / "attempt-1", attempt=1, exit_code=0)
            selected_root = root / "selected"
            _, selection_path = self._select([receipt], selected_root)
            imported_root = root / "imported"
            attempts.import_selection(
                artifact_root=selected_root,
                selection_path=selection_path,
                destination=imported_root,
            )
            evaluation = (
                imported_root / "evidence" / "performance-evaluation.json"
            )
            evaluation.write_text("{}\n", encoding="utf-8")

            with self.assertRaisesRegex(
                PerformanceEvidenceError,
                "digest does not match",
            ):
                attempts.verify_imported_selection(destination=imported_root)

    def test_import_rejects_another_target_or_commit(self) -> None:
        """Bind qualified evidence to the exact RC engine and source commit."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            receipt = self._record(root / "attempt-1", attempt=1, exit_code=0)
            selected_root = root / "selected"
            _, selection_path = self._select([receipt], selected_root)

            with self.assertRaises(PerformanceEvidenceError):
                attempts.import_selection(
                    artifact_root=selected_root,
                    selection_path=selection_path,
                    destination=root / "wrong-target",
                    expected_target="mariadb118",
                    expected_commit=self._commit,
                )

            with self.assertRaises(PerformanceEvidenceError):
                attempts.import_selection(
                    artifact_root=selected_root,
                    selection_path=selection_path,
                    destination=root / "wrong-commit",
                    expected_target="mysql84",
                    expected_commit="f" * 40,
                )


if __name__ == "__main__":
    unittest.main()
