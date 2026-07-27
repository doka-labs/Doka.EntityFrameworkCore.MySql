"""Regression tests for assembly, critical-class, and freshness coverage gates."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from eng.coverage_policy import evaluate, evaluate_freshness


class CoveragePolicyTests(unittest.TestCase):
    """Exercise positive and negative coverage-policy transitions."""

    def test_accepts_fresh_report_at_every_floor(self) -> None:
        """Accept exact floors for both a shipped assembly and critical class."""
        lines, errors = self._evaluate()

        self.assertEqual(2, len(lines))
        self.assertEqual([], errors)

    def test_rejects_stale_missing_and_below_floor_evidence(self) -> None:
        """Reject stale evidence and missing or regressed required surfaces."""
        _, stale_errors = self._evaluate(now_timestamp=2000)
        _, missing_errors = self._evaluate(
            assembly_name="Other.Assembly",
            class_name="Other.Critical",
        )
        _, threshold_errors = self._evaluate(
            line_hits=(1, 0),
            branch_fraction="0% (0/2)",
        )

        self.assertTrue(any("old" in error for error in stale_errors))
        self.assertTrue(any("missing shipped assembly" in error for error in missing_errors))
        self.assertTrue(any("line coverage" in error for error in threshold_errors))
        self.assertTrue(any("branch coverage" in error for error in threshold_errors))

    def test_rejects_stale_raw_input_before_merge(self) -> None:
        """Reject an old source report even if a merge would get a fresh timestamp."""
        with tempfile.TemporaryDirectory(prefix="doka-coverage-freshness-") as directory:
            root = Path(directory)
            report = root / "coverage.cobertura.xml"
            policy = root / "coverage-policy.json"
            report.write_text('<coverage timestamp="1000" />', encoding="utf-8")
            policy.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "evidenceMaxAgeSeconds": 100,
                    }
                ),
                encoding="utf-8",
            )

            errors = evaluate_freshness(
                [report],
                policy,
                now_timestamp=2000,
            )

            self.assertEqual(1, len(errors))
            self.assertIn("old", errors[0])

    def _evaluate(
        self,
        *,
        now_timestamp: int = 1001,
        assembly_name: str = "Provider",
        class_name: str = "Provider.Critical",
        line_hits: tuple[int, int] = (1, 1),
        branch_fraction: str = "50% (1/2)",
    ) -> tuple[list[str], list[str]]:
        with tempfile.TemporaryDirectory(prefix="doka-coverage-policy-") as directory:
            root = Path(directory)
            report = root / "coverage.cobertura.xml"
            policy = root / "coverage-policy.json"
            report.write_text(
                (
                    '<coverage timestamp="1000"><packages>'
                    f'<package name="{assembly_name}"><classes>'
                    f'<class name="{class_name}"><lines>'
                    f'<line number="1" hits="{line_hits[0]}" branch="true" '
                    f'condition-coverage="{branch_fraction}" />'
                    f'<line number="2" hits="{line_hits[1]}" />'
                    "</lines></class></classes></package>"
                    "</packages></coverage>"
                ),
                encoding="utf-8",
            )
            policy.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "evidenceMaxAgeSeconds": 100,
                        "assemblies": [
                            {
                                "name": "Provider",
                                "minimumLinePercent": 100,
                                "minimumBranchPercent": 50,
                            }
                        ],
                        "criticalClasses": [
                            {
                                "assembly": "Provider",
                                "name": "Provider.Critical",
                                "minimumLinePercent": 100,
                                "minimumBranchPercent": 50,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            return evaluate(report, policy, now_timestamp=now_timestamp)
