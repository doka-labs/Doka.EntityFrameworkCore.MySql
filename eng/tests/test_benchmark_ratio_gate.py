"""Regression tests for the complete cross-target performance gate wrapper."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

from eng.performance import cli as performance_evidence
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


class BenchmarkGateTests(unittest.TestCase):
    """Exercise complete, missing-target, and BDN-regression outcomes."""

    _repo_root = Path(__file__).resolve().parents[2]
    _script = _repo_root / "eng" / "performance" / "check-benchmark-ratios.sh"
    _contract = json.loads(
        (_repo_root / "benchmarks" / "performance-contract.json").read_text(encoding="utf-8")
    )

    def test_accepts_complete_current_run_for_every_target(self) -> None:
        """Accept every target-scoped workload matrix and same-run BDN controls."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            for target in self._contract["requiredTargets"]:
                self._write_target(root, target)

            result = self._run_gate(root)

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertIn("6 pass, 0 fail, 0 target(s) without current-run evidence", result.stdout)

    def test_missing_target_evidence_is_rejected_by_default(self) -> None:
        """Reject a run where one engine could otherwise conceal the missing family."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            missing_target = "mariadb123"
            for target in self._contract["requiredTargets"]:
                if target != missing_target:
                    self._write_target(root, target)

            result = self._run_gate(root)

            self.assertEqual(2, result.returncode)
            self.assertIn(f"SKIP [{missing_target}]", result.stderr)
            self.assertIn("Missing current-run target evidence", result.stderr)

    def test_absent_evidence_cannot_report_success(self) -> None:
        """Reject an empty evidence root even when missing targets are permitted."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            (root / "mysql84").mkdir()

            result = self._run_gate(root, allow_missing=True)

            self.assertEqual(2, result.returncode)
            self.assertIn("evaluated no target", result.stderr)

    def test_permitted_partial_run_accepts_one_target(self) -> None:
        """Accept a deliberately partial local run when the opt-out is explicit."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            self._write_target(root, "mysql84")

            result = self._run_gate(root, allow_missing=True)

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertIn("1 pass, 0 fail, 5 target(s)", result.stdout)

    def test_rejects_same_run_benchmarkdotnet_regression(self) -> None:
        """Reject one control regression even with complete workload evidence."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-gate-") as directory:
            root = Path(directory)
            failing_target = "mariadb118"
            report_path = None
            for target in self._contract["requiredTargets"]:
                written = self._write_target(root, target)
                if target == failing_target:
                    report_path = written

            self.assertIsNotNone(report_path)
            payload = json.loads(report_path.read_text(encoding="utf-8"))
            payload["Benchmarks"][1]["Statistics"]["Mean"] = 75
            report_path.write_text(json.dumps(payload), encoding="utf-8")

            result = self._run_gate(root)

            self.assertEqual(1, result.returncode)
            self.assertIn("identifier-quoting-throughput", result.stderr)
            self.assertIn(f"FAIL [{failing_target}]", result.stderr)

    def _write_target(
        self,
        root: Path,
        target: str,
    ) -> Path:
        """Write one complete smoke-profile target fixture and return its BDN report."""
        fixture = PerformanceEvidenceFixtureMixin()
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
                performance_evidence.expected_measurement_sample_count(
                    smoke_profile,
                    definition,
                )
            )
            samples = [
                float(100 + (index * 10))
                for index in range(sample_count)
            ]
            workload["warmupSamples"] = (
                performance_evidence.expected_warmup_sample_count(
                    smoke_profile,
                    definition,
                )
            )
            workload["sampleCount"] = sample_count
            workload["calibrationNanoseconds"] = [100.0] * sample_count
            workload["calibrationPulseNanoseconds"] = [100.0] * sample_count
            workload["calibrationPulseIndices"] = list(range(sample_count))
            fixture._replace_workload_samples(workload, samples)
        (evidence_directory / "workload-evidence.json").write_text(
            json.dumps(workload_report),
            encoding="utf-8",
        )
        (evidence_directory / "host-preflight.json").write_text(
            json.dumps(fixture._host_preflight()),
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
        *,
        allow_missing: bool = False,
    ) -> subprocess.CompletedProcess[str]:
        """Run the real wrapper against an isolated evidence root."""
        environment = os.environ.copy()
        # The shipped default must be what the tests exercise, so the opt-out is
        # set only where a test is specifically about permitted partial runs.
        environment.pop("DOKA_BENCHMARK_GATE_ALLOW_MISSING", None)
        if allow_missing:
            environment["DOKA_BENCHMARK_GATE_ALLOW_MISSING"] = "1"
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
