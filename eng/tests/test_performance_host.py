"""Host-admission tests for performance evidence."""

from __future__ import annotations

import unittest

from eng.performance import cli as performance_evidence
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


class PerformanceHostTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Verify interval CPU admission and host-evidence integrity."""

    def test_host_preflight_rejects_an_overloaded_runner(self) -> None:
        """Reject sustained CPU saturation before calibrated measurement starts."""
        report = self._host_preflight()
        report["samples"] = [
            {
                "sequence": sequence,
                "cpuUtilization": 0.95,
                "withinLimit": False,
            }
            for sequence in range(1, report["maximumSampleAttempts"] + 1)
        ]
        report["admittedCpuUtilization"] = None
        report["observedMaximumCpuUtilization"] = 0.95
        report["success"] = False

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "did not produce",
        ):
            performance_evidence.validate_host_preflight(
                report,
                self.contract,
                maximum_age_hours=12,
            )

    def test_cpu_admission_accepts_desktop_load_without_cpu_saturation(self) -> None:
        """Do not reject browser and window-server threads as CPU saturation."""
        report = self._host_preflight()
        report.update(
            {
                "admissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
                "loadAverage1Minute": 12.0,
                "loadAverage1MinutePerProcessor": 1.5,
            }
        )

        performance_evidence.validate_host_preflight(
            report,
            self.contract,
            maximum_age_hours=12,
        )

    def test_cpu_admission_rejects_actual_cpu_saturation(self) -> None:
        """Keep genuine host contention outside latency measurements."""
        report = self._host_preflight()
        report["samples"] = [
            {
                "sequence": sequence,
                "cpuUtilization": 0.95,
                "withinLimit": False,
            }
            for sequence in range(1, report["maximumSampleAttempts"] + 1)
        ]
        report["admittedCpuUtilization"] = None
        report["observedMaximumCpuUtilization"] = 0.95
        report["success"] = False

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "did not produce",
        ):
            performance_evidence.validate_host_preflight(
                report,
                self.contract,
                maximum_age_hours=12,
            )

    def test_linux_cpu_counters_produce_interval_utilization(self) -> None:
        """Calculate Linux utilization from aggregate counter deltas."""
        before = performance_evidence.parse_linux_cpu_counters(
            "cpu 100 20 30 400 50 5 6 7 0 0\n"
        )
        after = performance_evidence.parse_linux_cpu_counters(
            "cpu 120 22 40 460 60 7 8 8 0 0\n"
        )

        self.assertAlmostEqual(
            37 / 107,
            performance_evidence.calculate_host_cpu_utilization(before, after),
        )

    def test_wrapping_macos_cpu_counters_produce_interval_utilization(self) -> None:
        """Handle Darwin's natural_t tick counters wrapping independently."""
        modulus = 2**32
        before = performance_evidence.HostCpuCounterSnapshot(
            "macos-host-statistics64",
            (modulus - 5, 10, modulus - 2, 20),
            (0, 1, 3),
            modulus,
        )
        after = performance_evidence.HostCpuCounterSnapshot(
            "macos-host-statistics64",
            (5, 15, 8, 25),
            (0, 1, 3),
            modulus,
        )

        self.assertAlmostEqual(
            20 / 30,
            performance_evidence.calculate_host_cpu_utilization(before, after),
        )

    def test_cpu_admission_waits_out_one_transient_sample(self) -> None:
        """Admit after two current passing samples despite one build-tail spike."""
        samples = iter(
            (
                ("linux-proc-stat", 0.96),
                ("linux-proc-stat", 0.40),
                ("linux-proc-stat", 0.30),
            )
        )

        report = performance_evidence.capture_host_preflight(
            self.contract,
            sample_provider=lambda _: next(samples),
        )

        self.assertTrue(report["success"])
        self.assertEqual(3, len(report["samples"]))
        self.assertEqual(0.40, report["admittedCpuUtilization"])
        self.assertEqual(0.96, report["observedMaximumCpuUtilization"])
        performance_evidence.validate_host_preflight(
            report,
            self.contract,
            maximum_age_hours=12,
        )

    def test_cpu_admission_exhausts_sustained_saturation_window(self) -> None:
        """Consume every bounded attempt when current CPU remains saturated."""
        report = performance_evidence.capture_host_preflight(
            self.contract,
            sample_provider=lambda _: ("linux-proc-stat", 0.95),
        )

        self.assertFalse(report["success"])
        self.assertIsNone(report["admittedCpuUtilization"])
        self.assertEqual(0.95, report["observedMaximumCpuUtilization"])
        self.assertEqual(
            self.contract["hostPreconditions"]["maximumSampleAttempts"],
            len(report["samples"]),
        )

    def test_host_preflight_rejects_a_tampered_sample_decision(self) -> None:
        """Bind every persisted admission decision to its measured value."""
        report = self._host_preflight()
        report["samples"][0]["withinLimit"] = False

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "sample decision",
        ):
            performance_evidence.validate_host_preflight(
                report,
                self.contract,
                maximum_age_hours=12,
            )


if __name__ == "__main__":
    unittest.main()
