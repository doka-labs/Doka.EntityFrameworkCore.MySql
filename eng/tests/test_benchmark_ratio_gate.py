"""Regression tests for relative and absolute BenchmarkDotNet gates."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from typing import Any


class BenchmarkGateTests(unittest.TestCase):
    """Exercise pass, regression, and missing-evidence outcomes."""

    _script = Path(__file__).resolve().parents[1] / "check-benchmark-ratios.sh"

    def test_accepts_all_ratio_and_absolute_gates(self) -> None:
        """Accept measurements exactly at every configured threshold."""
        result = self._run_gate(self._complete_benchmarks())

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("8 pass, 0 fail, 0 gate(s) without data", result.stdout)

    def test_rejects_query_translation_allocation_regression(self) -> None:
        """Reject one byte beyond the query-translation allocation ceiling."""
        benchmarks = self._complete_benchmarks()
        benchmarks[-1]["Memory"]["BytesAllocatedPerOperation"] = 163841

        result = self._run_gate(benchmarks)

        self.assertEqual(1, result.returncode)
        self.assertIn("TranslateRepresentativeCorpus alloc = 163841 > 163840", result.stderr)

    def test_strict_mode_rejects_missing_absolute_gate_data(self) -> None:
        """Reject query-translation evidence missing from only one engine."""
        result = self._run_gate(
            self._complete_benchmarks(),
            self._complete_benchmarks()[:-1],
        )

        self.assertEqual(2, result.returncode)
        self.assertIn("SKIP [QueryTranslationBenchmarks] mariadb118", result.stderr)
        self.assertIn("Strict mode: missing data is a failure.", result.stderr)

    def _run_gate(
        self,
        mysql84_benchmarks: list[dict[str, Any]],
        mariadb118_benchmarks: list[dict[str, Any]] | None = None,
    ) -> subprocess.CompletedProcess[str]:
        if mariadb118_benchmarks is None:
            mariadb118_benchmarks = mysql84_benchmarks

        with tempfile.TemporaryDirectory(prefix="doka-benchmark-gate-") as directory:
            root = Path(directory)

            for target, benchmarks in (
                ("mysql84", mysql84_benchmarks),
                ("mariadb118", mariadb118_benchmarks),
            ):
                report = root / target / "reports" / "test-run" / "results" / "Doka.Benchmarks-report-full.json"
                report.parent.mkdir(parents=True)
                report.write_text(json.dumps({"Benchmarks": benchmarks}), encoding="utf-8")

            environment = os.environ.copy()
            environment["DOKA_BENCHMARK_GATE_STRICT"] = "1"

            return subprocess.run(
                ["bash", str(self._script), str(root)],
                check=False,
                capture_output=True,
                env=environment,
                text=True,
            )

    @staticmethod
    def _complete_benchmarks() -> list[dict[str, Any]]:
        return [
            BenchmarkGateTests._benchmark(
                "Doka.Benchmarks.IdentifierQuotingBenchmark",
                "NaiveDelimitStringPlain",
                mean=100,
            ),
            BenchmarkGateTests._benchmark(
                "Doka.Benchmarks.IdentifierQuotingBenchmark",
                "DelimitStringPlain",
                mean=50,
            ),
            BenchmarkGateTests._benchmark(
                "Doka.Benchmarks.BulkInsertBenchmark",
                "PerRowSaveChanges",
                mean=300,
            ),
            BenchmarkGateTests._benchmark(
                "Doka.Benchmarks.BulkInsertBenchmark",
                "MultiRowAddRangeSaveChanges",
                mean=99.9,
            ),
            BenchmarkGateTests._benchmark(
                "Doka.Benchmarks.JsonComparerBenchmark",
                "NaiveJsonElementEqualsLoop",
                allocated=1000,
            ),
            BenchmarkGateTests._benchmark(
                "Doka.Benchmarks.JsonComparerBenchmark",
                "JsonElementEqualsLoop",
                allocated=200,
            ),
            BenchmarkGateTests._benchmark(
                "Doka.Benchmarks.QueryTranslationBenchmarks",
                "TranslateRepresentativeCorpus",
                allocated=163840,
            ),
        ]

    @staticmethod
    def _benchmark(
        type_name: str,
        method: str,
        *,
        mean: float = 1,
        allocated: int = 1,
    ) -> dict[str, Any]:
        return {
            "Type": type_name,
            "Method": method,
            "Statistics": {"Mean": mean},
            "Memory": {"BytesAllocatedPerOperation": allocated},
        }


if __name__ == "__main__":
    unittest.main()
