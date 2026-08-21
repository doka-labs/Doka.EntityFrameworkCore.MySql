"""Workload, soak, and BenchmarkDotNet report contract tests."""

from __future__ import annotations

import copy
import json
import tempfile
import unittest
from pathlib import Path

from eng.performance import cli as performance_evidence
from eng.tests._performance_fixtures import PerformanceEvidenceFixtureMixin


class PerformanceReportTests(PerformanceEvidenceFixtureMixin, unittest.TestCase):
    """Verify report integrity, environment binding, and absolute budgets."""

    def test_workload_report_recomputes_tail_statistics_and_complete_matrix(self) -> None:
        """Accept exact scorecard cells with independently recomputable statistics."""
        report = self._workload_report("mysql84")
        # The report keeps contract order while the validator returns sorted
        # results, so the comparison matches by id. Indexing both sides only
        # agreed while every workload shared one operationsPerSample.
        source = report["workloads"][0]
        first_samples = sorted(source["samplesNanoseconds"])

        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

        self.assertEqual(len(self.contract["workloads"]), len(workloads))
        validated = next(
            workload
            for workload in workloads
            if workload["id"] == source["id"]
        )
        self.assertEqual(
            performance_evidence.percentile(first_samples, 0.95),
            validated["p95Nanoseconds"],
        )
        self.assertEqual(
            performance_evidence.percentile(first_samples, 0.99),
            validated["p99Nanoseconds"],
        )

    def test_workload_report_rejects_missing_matrix_cell(self) -> None:
        """Reject one missing workload even when every remaining statistic is valid."""
        report = self._workload_report("mysql84")
        report["workloads"].pop()

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "Workload matrix drift",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_diagnostic_workload_report_cannot_satisfy_release_evidence(self) -> None:
        """Keep targeted root-cause measurements outside the release contract."""
        report = self._workload_report("mysql84")
        report["kind"] = "performance-workload-diagnostic"
        report["workloads"] = report["workloads"][:1]

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "schema or kind",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_binds_cpu_admission_without_load_rejection(self) -> None:
        """Accept high desktop load only when the bound CPU admission passed."""
        report = self._workload_report("mysql84")
        report["environment"].update(
            {
                "hostAdmissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
                "admittedHostCpuUtilization": 0.2,
                "maximumHostCpuUtilization": 0.9,
                "hostLoadAverage1Minute": 12.0,
                "hostLoadAverage1MinutePerProcessor": 1.5,
            }
        )

        performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

    def test_workload_report_rejects_operations_per_sample_drift(self) -> None:
        """Reject evidence that normalizes a workload with an unapproved batch size."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["operationsPerSample"] += 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "operationsPerSample",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_adaptive_workload_report_binds_batch_size_to_the_pilot(self) -> None:
        """Reject a paired sample batch that its recorded pilot cannot derive."""
        report = self._workload_report("mysql84", profile_name="paired-block")
        report["workloads"][0]["pilotSamplesElapsedTicks"] = [10_000_000] * 3

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "pilot requires",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="paired-block",
            )

    def test_adaptive_workload_report_binds_the_pilot_population(self) -> None:
        """Reject a fast workload that omits a registered pilot observation."""
        report = self._workload_report("mysql84", profile_name="paired-block")
        report["workloads"][0]["pilotSamplesElapsedTicks"] = report[
            "workloads"
        ][0]["pilotSamplesElapsedTicks"][:2]

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "no adaptive batching pilot",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="paired-block",
            )

    def test_adaptive_workload_report_requires_pilot_provenance(self) -> None:
        """Reject a paired batch presented as a fixed contract value."""
        report = self._workload_report("mysql84", profile_name="paired-block")
        report["workloads"][0]["operationBatchingMode"] = "fixed"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "no adaptive batching pilot",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="paired-block",
            )

    def test_workload_report_rejects_warmup_count_drift(self) -> None:
        """Reject a report produced with fewer warmups than the active profile."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["warmupSamples"] -= 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "warmups",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_an_insufficient_measurement_window(self) -> None:
        """Reject a large sample count that covers too little measured time."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        self._replace_workload_samples(entry, [1.0] * entry["sampleCount"])
        entry["minimumDurationReached"] = False
        entry["terminationReason"] = "sample_cap_reached"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "may fall short",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_sample_count_drift(self) -> None:
        """Reject a report produced with fewer samples than the active profile."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["sampleCount"] -= 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "samples",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_wrong_server_image(self) -> None:
        """Reject evidence whose observed container image differs from the contract."""
        report = self._workload_report("mysql84")
        report["environment"]["serverImage"] = "mysql:8.4@sha256:" + ("0" * 64)

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "serverImage",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_wrong_observed_engine_version(self) -> None:
        """Reject a target label backed by a different database engine."""
        report = self._workload_report("mysql84")
        report["environment"]["serverVersion"] = "11.8.8-MariaDB"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "serverVersion",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_non_finite_sample(self) -> None:
        """Reject NaN before it can produce a misleading successful summary."""
        report = self._workload_report("mysql84")
        report["workloads"][0]["samplesNanoseconds"][0] = float("nan")

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "must be finite",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_workload_report_rejects_excessive_measurement_noise(self) -> None:
        """Reject a statistically unstable workload even when every sample is finite."""
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        # Noise this large means the runner exhausted its sample budget, so the
        # population must sit at the contract cap for the state to be reachable.
        cap = self._grow_to_sample_cap(entry)
        self._replace_workload_samples(
            entry,
            self._samples_missing_the_error_budget(8_000_000.0, cap),
        )
        entry["terminationReason"] = "sample_cap_reached"

        # The run exhausted its budget, so the capped verdict owns this outcome
        # and reports the achieved error as part of its diagnostic.
        with self.assertRaisesRegex(
            performance_evidence.MeasurementQualityError,
            "(?i)relative standard error",
        ):
            performance_evidence.validate_workload_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_observe_policy_records_measurement_noise_without_failing(self) -> None:
        """Record noisy evidence when the selected profile observes measurement quality."""
        contract = copy.deepcopy(self.contract)
        contract["profiles"]["scorecard"]["measurementQualityPolicy"] = "observe"
        report = self._workload_report("mysql84")
        entry = report["workloads"][0]
        # Noise this large means the runner exhausted its sample budget, so the
        # population must sit at the contract cap for the state to be reachable.
        cap = self._grow_to_sample_cap(entry)
        self._replace_workload_samples(
            entry,
            self._samples_missing_the_error_budget(8_000_000.0, cap),
        )
        entry["terminationReason"] = "sample_cap_reached"

        workloads = performance_evidence.validate_workload_report(
            report,
            contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )

        observed = next(
            workload
            for workload in workloads
            if workload["id"] == entry["id"]
        )
        self.assertGreater(
            observed["relativeStandardError"],
            contract["profiles"]["scorecard"]["maximumRelativeStandardError"],
        )

    def test_absolute_budget_rejects_latency_regression(self) -> None:
        """Reject a current measurement beyond its family's absolute p99 ceiling."""
        report = self._workload_report("mysql84")
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        workloads[0]["p99Nanoseconds"] = 1000000000000

        with self.assertRaisesRegex(
            performance_evidence.PerformanceRegressionError,
            "Absolute budget failed",
        ):
            performance_evidence.validate_absolute_budgets(workloads, self.contract)

    def test_gc_policy_variance_is_diagnostic_not_a_provider_failure(self) -> None:
        """Retain GC evidence without attributing host policy choices to the provider."""
        report = self._workload_report("mysql84")
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target="mysql84",
            profile="scorecard",
        )
        baseline_entry = {"workloads": copy.deepcopy(workloads)}
        workload = workloads[0]
        workload["gen0CollectionsPer1000"] = 1000000
        workload["gen1CollectionsPer1000"] = 1000000
        workload["gen2CollectionsPer1000"] = 1000000

        performance_evidence.validate_absolute_budgets(workloads, self.contract)
        performance_evidence.validate_historical_budgets(
            workloads,
            baseline_entry,
            self.contract,
        )
        diagnostics = performance_evidence.collect_gc_diagnostics(
            workloads,
            baseline_entry,
            self.contract,
        )

        workload_diagnostics = [
            diagnostic
            for diagnostic in diagnostics
            if diagnostic["workloadId"] == workload["id"]
        ]
        self.assertEqual(4, len(workload_diagnostics))
        self.assertTrue(
            all(
                diagnostic["withinReferenceRange"] is False
                for diagnostic in workload_diagnostics
            )
        )

    def test_soak_report_rejects_resource_failure(self) -> None:
        """Reject a failed cleanup invariant even when all scenario rows are present."""
        report = self._soak_report("mysql84")
        report["success"] = False
        report["scenarios"][1]["success"] = False
        report["scenarios"][1]["error"] = "buffer remained rented"
        report["scenarios"][1]["metrics"]["outstandingBuffers"] = 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "Soak gate failed",
        ):
            performance_evidence.validate_soak_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_soak_report_rejects_a_budget_that_does_not_match_the_contract(self) -> None:
        """Reject a report that weakens its own working-set ceiling."""
        report = self._soak_report("mysql84")
        report["scenarios"][4]["budgets"]["maximumWorkingSetGrowthBytes"] += 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "expected",
        ):
            performance_evidence.validate_soak_report(
                report,
                self.contract,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

    def test_benchmarkdotnet_reports_require_statistics_memory_and_controls(self) -> None:
        """Accept a current-run report only with complete BDN controls and memory data."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-bdn-") as directory:
            reports = Path(directory) / "reports" / "run-1"
            result_directory = reports / "results"
            result_directory.mkdir(parents=True)
            report_path = result_directory / "Doka.Benchmarks-report-full.json"
            report_path.write_text(
                json.dumps(self._bdn_report()),
                encoding="utf-8",
            )

            evidence = performance_evidence.validate_bdn_reports(
                self.contract,
                reports,
                run_id="run-1",
                target="mysql84",
                profile="scorecard",
            )

            self.assertTrue(evidence["success"])
            self.assertEqual(7, len(evidence["controls"]))
            self.assertEqual(1, len(evidence["rawReports"]))

    def test_benchmarkdotnet_reports_reject_missing_statistics(self) -> None:
        """Reject a benchmark error row represented by absent statistics."""
        with tempfile.TemporaryDirectory(prefix="doka-performance-bdn-") as directory:
            reports = Path(directory) / "reports" / "run-1"
            result_directory = reports / "results"
            result_directory.mkdir(parents=True)
            payload = self._bdn_report()
            payload["Benchmarks"][0]["Statistics"] = None
            (result_directory / "Doka.Benchmarks-report-full.json").write_text(
                json.dumps(payload),
                encoding="utf-8",
            )

            with self.assertRaisesRegex(
                performance_evidence.PerformanceEvidenceError,
                "has no statistics",
            ):
                performance_evidence.validate_bdn_reports(
                    self.contract,
                    reports,
                    run_id="run-1",
                    target="mysql84",
                    profile="scorecard",
                )

    def test_benchmarkdotnet_host_must_match_workload_processor(self) -> None:
        """Reject same-run controls captured under a different host identity."""
        environment = self._workload_report("mysql84")["environment"]
        bdn_host = self._bdn_report()["HostEnvironmentInfo"]
        bdn_host["ProcessorName"] = "different CPU"

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "different processors",
        ):
            performance_evidence.validate_bdn_workload_environment(
                bdn_host,
                environment,
            )

    def test_benchmarkdotnet_host_accepts_equivalent_processor_descriptions(self) -> None:
        """Accept the OS and CPUID descriptions emitted for one hosted-runner CPU."""
        descriptions = (
            (
                "AMD EPYC 9V74 96-Core Processor",
                "AMD EPYC 9V74 2.87GHz",
            ),
            (
                "Intel(R) Xeon(R) Platinum 8370C CPU @ 2.80GHz",
                "Intel Xeon Platinum 8370C 2.80GHz",
            ),
        )
        for target in ("mysql84", "mariadb118"):
            for operating_system_name, benchmark_dotnet_name in descriptions:
                with self.subTest(target=target, processor=operating_system_name):
                    environment = self._workload_report(target)["environment"]
                    environment["processor"] = operating_system_name
                    bdn_host = self._bdn_report()["HostEnvironmentInfo"]
                    bdn_host["ProcessorName"] = benchmark_dotnet_name

                    performance_evidence.validate_bdn_workload_environment(
                        bdn_host,
                        environment,
                    )

    def test_benchmarkdotnet_host_must_match_logical_processor_count(self) -> None:
        """Reject same-model evidence captured with a different effective CPU count."""
        environment = self._workload_report("mysql84")["environment"]
        bdn_host = self._bdn_report()["HostEnvironmentInfo"]
        bdn_host["LogicalCoreCount"] += 1

        with self.assertRaisesRegex(
            performance_evidence.PerformanceEvidenceError,
            "different logical processor counts",
        ):
            performance_evidence.validate_bdn_workload_environment(
                bdn_host,
                environment,
            )


if __name__ == "__main__":
    unittest.main()
