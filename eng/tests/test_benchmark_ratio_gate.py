"""Regression tests for the complete cross-target performance gate wrapper."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

from eng.tests import test_performance_evidence as performance_test_helpers


class BenchmarkGateTests(unittest.TestCase):
    """Exercise complete, missing-target, and BDN-regression outcomes."""

    _repo_root = Path(__file__).resolve().parents[2]
    _script = _repo_root / "eng" / "check-benchmark-ratios.sh"
    _contract = json.loads(
        (_repo_root / "benchmarks" / "performance-contract.json").read_text(encoding="utf-8")
    )

    def test_accepts_complete_current_run_for_both_targets(self) -> None:
        """Accept both target-scoped workload matrices and same-run BDN controls."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            self._write_target(root, "mysql84")
            self._write_target(root, "mariadb118")

            result = self._run_gate(root)

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertIn("2 pass, 0 fail, 0 target(s) without current-run evidence", result.stdout)

    def test_strict_mode_rejects_missing_target_evidence(self) -> None:
        """Reject a run where one engine could otherwise conceal the missing family."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            self._write_target(root, "mysql84")

            result = self._run_gate(root)

            self.assertEqual(2, result.returncode)
            self.assertIn("SKIP [mariadb118]", result.stderr)
            self.assertIn("Strict mode", result.stderr)

    def test_rejects_same_run_benchmarkdotnet_regression(self) -> None:
        """Reject one control regression even with complete workload evidence."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            self._write_target(root, "mysql84")
            report_path = self._write_target(root, "mariadb118")
            payload = json.loads(report_path.read_text(encoding="utf-8"))
            payload["Benchmarks"][1]["Statistics"]["Mean"] = 75
            report_path.write_text(json.dumps(payload), encoding="utf-8")

            result = self._run_gate(root)

            self.assertEqual(1, result.returncode)
            self.assertIn("identifier-quoting-throughput", result.stderr)
            self.assertIn("FAIL [mariadb118]", result.stderr)

    def _write_target(
        self,
        root: Path,
        target: str,
    ) -> Path:
        """Write one complete smoke-profile target fixture and return its BDN report."""
        fixture = performance_test_helpers.PerformanceEvidenceTests()
        fixture.setUp()
        report_directory = root / target / "reports" / "test-run"
        evidence_directory = report_directory / "evidence"
        results_directory = report_directory / "results"
        evidence_directory.mkdir(parents=True)
        results_directory.mkdir(parents=True)

        workload_report = fixture._workload_report(target)
        workload_report["runId"] = "test-run"
        workload_report["profile"] = "smoke"
        smoke_profile = self._contract["profiles"]["smoke"]
        evidence = performance_test_helpers.performance_evidence
        definitions = {
            definition["id"]: definition
            for definition in self._contract["workloads"]
        }
        smoke_ids = {
            definition["id"]
            for definition in self._contract["workloads"]
            if definition.get("smoke") is True
        }
        workload_report["workloads"] = [
            workload
            for workload in workload_report["workloads"]
            if workload["id"] in smoke_ids
        ]
        for workload in workload_report["workloads"]:
            definition = definitions[workload["id"]]
            sample_count = (
                smoke_profile["expensiveMeasurementSamples"]
                if definition.get("cost", "standard") == "expensive"
                else smoke_profile["measurementSamples"]
            )
            samples = [
                float(100 + (index * 10))
                for index in range(sample_count)
            ]
            workload["warmupSamples"] = smoke_profile["warmupSamples"]
            workload["sampleCount"] = sample_count
            workload["samplesNanoseconds"] = samples
            workload["medianNanoseconds"] = evidence.percentile(
                samples,
                0.5,
            )
            workload["p95Nanoseconds"] = evidence.percentile(
                samples,
                0.95,
            )
            workload["p99Nanoseconds"] = evidence.percentile(
                samples,
                0.99,
            )
            workload["standardErrorNanoseconds"] = evidence.standard_error(samples)
        (evidence_directory / "workload-evidence.json").write_text(
            json.dumps(workload_report),
            encoding="utf-8",
        )

        report_path = results_directory / "Doka.Benchmarks-report-full.json"
        report_path.write_text(
            json.dumps(fixture._bdn_report()),
            encoding="utf-8",
        )
        return report_path

    def _run_gate(
        self,
        root: Path,
    ) -> subprocess.CompletedProcess[str]:
        """Run the real strict wrapper against an isolated evidence root."""
        environment = os.environ.copy()
        environment["DOKA_BENCHMARK_GATE_STRICT"] = "1"
        environment["DOKA_BENCHMARK_GATE_RUN_ID"] = "test-run"
        environment["DOKA_BENCHMARK_PROFILE"] = "smoke"
        environment["DOKA_BENCHMARK_BASELINE_PATH"] = str(root / "unused-baseline.json")
        environment["PYTHONDONTWRITEBYTECODE"] = "1"

        return subprocess.run(
            ["bash", str(self._script), str(root)],
            check=False,
            capture_output=True,
            env=environment,
            text=True,
        )


if __name__ == "__main__":
    unittest.main()
