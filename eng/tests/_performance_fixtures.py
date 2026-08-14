"""Shared builders for performance-evidence contract tests.

The mixin owns deterministic evidence construction only. Assertions stay in
the responsibility-focused test modules so production contracts can evolve
without recreating the same large fixture graph.
"""

from __future__ import annotations

import json
import math
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from eng.performance import cli as performance_evidence


class PerformanceEvidenceFixtureMixin:
    """Construct coherent workload, soak, BDN, and baseline evidence."""

    _repo_root = Path(__file__).resolve().parents[2]
    _contract_path = _repo_root / "benchmarks" / "performance-contract.json"

    def setUp(self) -> None:
        """Load the versioned contract shared by every evidence fixture."""
        self.contract = performance_evidence.load_json(self._contract_path)

    def _workload_report(
        self,
        target: str,
        profile_name: str = "scorecard",
    ) -> dict[str, Any]:
        """Build a complete workload report for one target under one profile.

        The profile is a parameter because the paired comparison measures under
        its own registered block profile, and a report built for another one
        would be rejected by the very contract these fixtures exist to satisfy.
        """
        maximum_cpu_utilization = self.contract["hostPreconditions"][
            "maximumCpuUtilization"
        ]
        workloads = []
        profile = self.contract["profiles"][profile_name]

        for definition in self.contract["workloads"]:
            sample_count = performance_evidence.expected_measurement_sample_count(
                profile,
                definition,
            )
            configured_operations_per_sample = definition.get(
                "operationsPerSample",
                1,
            )
            operations_per_sample = configured_operations_per_sample
            minimum_duration_nanoseconds = (
                profile["minimumMeasurementDurationMilliseconds"]
                * 1_000_000
            )
            minimum_sample_nanoseconds = (
                minimum_duration_nanoseconds
                / sample_count
                / operations_per_sample
            )
            # A real run keeps the per-operation time inside the family budget
            # and reaches the duration floor by extending the population; it
            # does not stretch a fixed number of samples until each one is slow
            # enough. Deriving the base from the duration floor alone produced
            # samples above the absolute ceilings for any profile that starts
            # small, which describes no run the runner would ever emit.
            family_budget = self.contract["familyBudgets"][definition["family"]]
            budget_sample_nanoseconds = (
                family_budget["medianNanoseconds"] / operations_per_sample / 2
            )
            sample_base = max(100.0, min(
                minimum_sample_nanoseconds * 1.05,
                budget_sample_nanoseconds,
            ))
            operation_batching_mode = "fixed"
            pilot_samples_elapsed_ticks: list[int] = []
            if profile["adaptiveOperationsPerSample"]:
                operation_batching_mode = "pilot"
                pilot_elapsed_ticks = max(
                    1,
                    math.ceil(sample_base * configured_operations_per_sample),
                )
                target_sample_ticks_numerator = (
                    profile["minimumMeasurementDurationMilliseconds"]
                    * 1_000_000_000
                    * profile["operationBatchingDurationHeadroomPercent"]
                )
                target_sample_ticks_denominator = 1000 * 100 * sample_count
                target_sample_ticks = (
                    target_sample_ticks_numerator
                    + target_sample_ticks_denominator
                    - 1
                ) // target_sample_ticks_denominator
                pilot_samples_elapsed_ticks = [
                    pilot_elapsed_ticks
                    for _ in range(profile["operationBatchingPilotSamples"])
                ]
                required_multiplier = max(
                    1,
                    math.ceil(target_sample_ticks / pilot_elapsed_ticks),
                )
                operations_per_sample *= min(
                    required_multiplier,
                    profile["maximumOperationsPerSampleMultiplier"],
                )
            # Whatever the population has to be for that per-sample time to
            # clear the duration floor, exactly as the adaptive extension does.
            if sample_base * operations_per_sample > 0:
                required = math.ceil(
                    minimum_duration_nanoseconds
                    / (sample_base * operations_per_sample)
                )
                sample_count = max(sample_count, required)
            samples = [sample_base + (index % 10) for index in range(sample_count)]
            sorted_samples = sorted(samples)
            calibration_samples = [100.0] * sample_count
            calibration_interval = profile["calibrationIntervalSamples"]
            calibration_pulses = [
                100.0
                for _ in range(math.ceil(sample_count / calibration_interval))
            ]
            calibration_pulse_indices = [
                index // calibration_interval
                for index in range(sample_count)
            ]
            normalized_samples = [sample / 100.0 for sample in samples]
            sorted_normalized_samples = sorted(normalized_samples)
            calibration_kind = (
                "cpu"
                if definition["family"] in self.contract["calibration"]["cpuFamilies"]
                else "database"
            )
            workloads.append(
                {
                    "id": definition["id"],
                    "family": definition["family"],
                    "warmupSamples": performance_evidence.expected_warmup_sample_count(
                        profile,
                        definition,
                    ),
                    "sampleCount": len(samples),
                    "terminationReason": "precision_reached",
                    "minimumDurationReached": True,
                    "configuredOperationsPerSample": configured_operations_per_sample,
                    "operationBatchingMode": operation_batching_mode,
                    "pilotSamplesElapsedTicks": pilot_samples_elapsed_ticks,
                    "operationsPerSample": operations_per_sample,
                    "checksum": 1,
                    "measuredUtc": datetime.now(timezone.utc).isoformat(),
                    "medianNanoseconds": performance_evidence.percentile(sorted_samples, 0.5),
                    "p95Nanoseconds": performance_evidence.percentile(sorted_samples, 0.95),
                    "p99Nanoseconds": performance_evidence.percentile(sorted_samples, 0.99),
                    "standardErrorNanoseconds": performance_evidence.standard_error(samples),
                    "calibrationKind": calibration_kind,
                    "calibrationMedianNanoseconds": 100.0,
                    "calibrationStandardErrorNanoseconds": 0.0,
                    "normalizedMedian": performance_evidence.percentile(
                        sorted_normalized_samples,
                        0.5,
                    ),
                    "normalizedP95": performance_evidence.percentile(
                        sorted_normalized_samples,
                        0.95,
                    ),
                    "normalizedP99": performance_evidence.percentile(
                        sorted_normalized_samples,
                        0.99,
                    ),
                    "allocatedBytesPerOperation": 1000,
                    "retainedBytes": 0,
                    "gen0CollectionsPer1000": 0,
                    "gen1CollectionsPer1000": 0,
                    "gen2CollectionsPer1000": 0,
                    "samplesNanoseconds": samples,
                    "calibrationNanoseconds": calibration_samples,
                    "calibrationPulseNanoseconds": calibration_pulses,
                    "calibrationPulseIndices": calibration_pulse_indices,
                    "normalizedSamples": normalized_samples,
                }
            )

        target_contract = self.contract["requiredTargets"][target]
        image_version = target_contract["serverImage"].split(":", 1)[1].split("@", 1)[0]
        server_version = (
            f"{image_version}-MariaDB"
            if target_contract["engineFamily"] == "MariaDB"
            else image_version
        )

        return {
            "schemaVersion": 5,
            "kind": "performance-workloads",
            "contractVersion": self.contract["contractVersion"],
            "runId": "run-1",
            "target": target,
            "profile": profile_name,
            "commit": "0" * 40,
            "sourceHash": "a" * 64,
            "runnerClass": "test-runner",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "stopwatchFrequency": 1000000000,
            "environment": {
                "frameworkDescription": ".NET 10",
                "osDescription": "test",
                "osArchitecture": "X64",
                "processArchitecture": "X64",
                "processor": "test CPU",
                "processorCount": 8,
                "hostLoadAverage1Minute": 0.5,
                "hostLoadAverage5Minutes": 0.5,
                "hostLoadAverage15Minutes": 0.5,
                "hostLoadAverage1MinutePerProcessor": 0.0625,
                "hostAdmissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
                "admittedHostCpuUtilization": 0.2,
                "maximumHostCpuUtilization": maximum_cpu_utilization,
                "engineFamily": target_contract["engineFamily"],
                "serverVersion": server_version,
                "serverImage": target_contract["serverImage"],
            },
            "workloads": workloads,
        }

    def _grow_to_sample_cap(
        self,
        entry: dict[str, Any],
        samples: list[float] | None = None,
    ) -> int:
        """Grow one workload entry to the contract's maximum sample population.

        Tests that need a genuinely capped run must produce the population the
        runner would have produced, not merely relabel a smaller one.
        """
        profile = self.contract["profiles"]["scorecard"]
        definition = next(
            candidate
            for candidate in self.contract["workloads"]
            if candidate["id"] == entry["id"]
        )
        cap = performance_evidence.expected_measurement_sample_count(
            profile,
            definition,
        ) * int(profile["maximumMeasurementSampleMultiplier"])
        interval = int(profile["calibrationIntervalSamples"])

        entry["sampleCount"] = cap
        entry["calibrationNanoseconds"] = [100.0] * cap
        entry["calibrationPulseNanoseconds"] = [100.0] * math.ceil(cap / interval)
        entry["calibrationPulseIndices"] = [index // interval for index in range(cap)]

        if samples is None:
            base = entry["samplesNanoseconds"][0]
            samples = [base] * cap
        self._replace_workload_samples(entry, samples)

        return cap

    def _samples_missing_the_error_budget(
        self,
        base_nanoseconds: float,
        population: int,
        profile: dict[str, Any] | None = None,
    ) -> list[float]:
        """Build a population whose relative standard error clears the ceiling.

        The standard error falls with the square root of the population, so a
        fixed outlier factor silently stops clearing the ceiling once the
        contract raises its sample cap. Deriving the outlier from the ceiling
        and the population keeps the fixture's intent stable across cap
        changes instead of re-tuning constants after every one.
        """
        profile = profile or self.contract["profiles"]["scorecard"]
        ceiling = float(profile["maximumRelativeStandardError"])

        # For a single outlier above an otherwise flat population the relative
        # standard error reduces to (outlier - base) / (population * base).
        # The margin keeps the fixture clear of the boundary itself.
        outlier = base_nanoseconds * (1.0 + ceiling * population * 1.2)
        samples = [base_nanoseconds] * (population - 1) + [outlier]

        achieved = performance_evidence.standard_error(
            samples
        ) / performance_evidence.percentile(sorted(samples), 0.5)
        if achieved <= ceiling:
            raise AssertionError(
                f"Fixture produced a relative standard error of {achieved:.6f}, "
                f"which does not clear the {ceiling} ceiling it must exceed."
            )

        return samples

    @staticmethod
    def _replace_workload_samples(
        entry: dict[str, Any],
        samples: list[float],
    ) -> None:
        """Replace raw samples while keeping every derived statistic coherent."""
        calibration_samples = entry["calibrationNanoseconds"]
        normalized_samples = [
            sample / calibration
            for sample, calibration in zip(samples, calibration_samples)
        ]
        sorted_samples = sorted(samples)
        sorted_normalized_samples = sorted(normalized_samples)

        entry.update(
            {
                "samplesNanoseconds": samples,
                "normalizedSamples": normalized_samples,
                "medianNanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.5,
                ),
                "p95Nanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.95,
                ),
                "p99Nanoseconds": performance_evidence.percentile(
                    sorted_samples,
                    0.99,
                ),
                "standardErrorNanoseconds": performance_evidence.standard_error(
                    samples
                ),
                "normalizedMedian": performance_evidence.percentile(
                    sorted_normalized_samples,
                    0.5,
                ),
                "normalizedP95": performance_evidence.percentile(
                    sorted_normalized_samples,
                    0.95,
                ),
                "normalizedP99": performance_evidence.percentile(
                    sorted_normalized_samples,
                    0.99,
                ),
            }
        )

    def _host_preflight(self) -> dict[str, Any]:
        """Build a passing host-preflight fixture bound to the test CPU."""
        preconditions = self.contract["hostPreconditions"]

        return {
            "schemaVersion": 4,
            "kind": "performance-host-preflight",
            "contractVersion": self.contract["contractVersion"],
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "processor": "test CPU",
            "processorCount": 8,
            "loadAverage1Minute": 0.5,
            "loadAverage5Minutes": 0.5,
            "loadAverage15Minutes": 0.5,
            "loadAverage1MinutePerProcessor": 0.0625,
            "admissionMetric": performance_evidence.HOST_ADMISSION_METRIC,
            "samplingSource": "linux-proc-stat",
            "sampleIntervalMilliseconds": preconditions[
                "sampleIntervalMilliseconds"
            ],
            "requiredConsecutivePassingSamples": preconditions[
                "requiredConsecutivePassingSamples"
            ],
            "maximumSampleAttempts": preconditions["maximumSampleAttempts"],
            "samples": [
                {
                    "sequence": 1,
                    "cpuUtilization": 0.1,
                    "withinLimit": True,
                },
                {
                    "sequence": 2,
                    "cpuUtilization": 0.2,
                    "withinLimit": True,
                },
            ],
            "admittedCpuUtilization": 0.2,
            "observedMaximumCpuUtilization": 0.2,
            "maximumCpuUtilization": preconditions["maximumCpuUtilization"],
            "success": True,
        }

    def _soak_report(self, target: str) -> dict[str, Any]:
        """Build a complete passing soak report for one target."""
        budgets = self.contract["soakBudgets"]
        return {
            "schemaVersion": 2,
            "kind": "performance-soak",
            "contractVersion": self.contract["contractVersion"],
            "runId": "run-1",
            "target": target,
            "profile": "scorecard",
            "commit": "0" * 40,
            "sourceHash": "a" * 64,
            "runnerClass": "test-runner",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "success": True,
            "scenarios": [
                {
                    "id": "soak.hilo-cache-bound",
                    "success": True,
                    "metrics": {"cacheEntries": 0},
                    "budgets": {
                        "maximumCacheEntries": budgets["hiloCacheMaximumEntries"],
                    },
                },
                {
                    "id": "soak.pooled-buffer-return",
                    "success": True,
                    "metrics": {
                        "rentCount": 1,
                        "returnCount": 1,
                        "outstandingBuffers": 0,
                    },
                    "budgets": {
                        "maximumOutstandingBuffers": budgets[
                            "pooledBufferMaximumOutstanding"
                        ],
                    },
                },
                {
                    "id": "soak.connection-cleanup",
                    "success": True,
                    "metrics": {
                        "threadsConnectedBefore": 1,
                        "threadsConnectedAfter": 1,
                        "connectionDelta": 0,
                    },
                    "budgets": {
                        "maximumConnectionDelta": budgets["connectionMaximumDelta"],
                    },
                },
                {
                    "id": "soak.migration-lock-cleanup",
                    "success": True,
                    "metrics": {"heldLocks": 0},
                    "budgets": {
                        "maximumHeldLocks": budgets["migrationLockMaximumHeld"],
                    },
                },
                {
                    "id": "soak.working-set-stabilization",
                    "success": True,
                    "metrics": {
                        "workingSetFirstBytes": 100,
                        "workingSetLastBytes": 100,
                        "workingSetGrowthBytes": 0,
                        "managedHeapFirstBytes": 100,
                        "managedHeapLastBytes": 100,
                        "managedHeapGrowthBytes": 0,
                    },
                    "budgets": {
                        "maximumWorkingSetGrowthBytes": budgets[
                            "workingSetMaximumGrowthBytes"
                        ],
                        "maximumManagedHeapGrowthBytes": budgets[
                            "managedHeapMaximumGrowthBytes"
                        ],
                    },
                },
                {
                    "id": "soak.concurrent-throughput-retention",
                    "success": True,
                    "metrics": {
                        "initialOperationsPerSecond": 100,
                        "finalOperationsPerSecond": 100,
                        "throughputRetentionRatio": 1,
                    },
                    "budgets": {
                        "minimumThroughputRetentionRatio": budgets[
                            "minimumThroughputRetentionRatio"
                        ],
                    },
                },
            ],
        }

    @staticmethod
    def _bdn_report() -> dict[str, Any]:
        """Build raw BenchmarkDotNet controls with stable sample statistics."""

        def benchmark(type_name: str, method: str, mean: float, allocated: int) -> dict[str, Any]:
            """Build one raw BenchmarkDotNet benchmark result."""
            values = [mean - 1, mean, mean + 1]
            return {
                "Type": type_name,
                "Method": method,
                "Statistics": {
                    "OriginalValues": values,
                    "N": 3,
                    "Mean": mean,
                    "Median": mean,
                    "StandardError": 1,
                    "Percentiles": {"P95": mean + 1},
                },
                "Memory": {
                    "Gen0Collections": 0,
                    "Gen1Collections": 0,
                    "Gen2Collections": 0,
                    "TotalOperations": 1,
                    "BytesAllocatedPerOperation": allocated,
                },
            }

        return {
            "HostEnvironmentInfo": {
                "BenchmarkDotNetVersion": "0.15.8",
                "OsVersion": "test",
                "ProcessorName": "test CPU",
                "PhysicalProcessorCount": 1,
                "PhysicalCoreCount": 4,
                "LogicalCoreCount": 8,
                "RuntimeVersion": ".NET 10",
                "Architecture": "X64",
                "Configuration": "RELEASE",
            },
            "Benchmarks": [
                benchmark("IdentifierQuotingBenchmark", "NaiveDelimitStringPlain", 100, 100),
                benchmark("IdentifierQuotingBenchmark", "DelimitStringPlain", 50, 50),
                benchmark("BulkInsertBenchmark", "PerRowSaveChanges", 300, 1000),
                benchmark("BulkInsertBenchmark", "MultiRowAddRangeSaveChanges", 90, 500),
                benchmark("JsonComparerBenchmark", "NaiveJsonElementEqualsLoop", 100, 1000),
                benchmark("JsonComparerBenchmark", "JsonElementEqualsLoop", 100, 100),
                benchmark(
                    "QueryTranslationBenchmarks",
                    "TranslateRepresentativeCorpus",
                    100,
                    140000,
                ),
                benchmark(
                    "TemporalProviderBenchmarks",
                    "GenerateTemporalAndCteQuerySql",
                    100,
                    200000,
                ),
                benchmark(
                    "TemporalProviderBenchmarks",
                    "GenerateTemporalMigrationSql",
                    100,
                    400000,
                ),
                benchmark(
                    "MigrationOperationHandlerDispatchBenchmark",
                    "DispatchExactTypeMatrix",
                    100,
                    0,
                ),
            ],
        }

    def _evaluation(self, target: str) -> dict[str, Any]:
        """Evaluate a complete fixture into the baseline seed representation."""
        report = self._workload_report(target)
        workloads = performance_evidence.validate_workload_report(
            report,
            self.contract,
            run_id="run-1",
            target=target,
            profile="scorecard",
        )
        soak_scenarios = performance_evidence.validate_soak_report(
            self._soak_report(target),
            self.contract,
            run_id="run-1",
            target=target,
            profile="scorecard",
        )
        return {
            "schemaVersion": 3,
            "kind": "performance-evaluation",
            "contractVersion": self.contract["contractVersion"],
            "runId": "run-1",
            "mode": "seed",
            "success": True,
            "target": target,
            "profile": "scorecard",
            "runnerClass": "test-runner",
            "commit": "0" * 40,
            "sourceHash": "a" * 64,
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "environment": report["environment"],
            "hostPreflight": self._host_preflight(),
            "artifactHashes": {
                "contract": performance_evidence.sha256(self._contract_path),
                "hostPreflight": "f" * 64,
                "workloads": "b" * 64,
                "benchmarkDotNet": "c" * 64,
                "soak": "d" * 64,
            },
            "rawReports": [
                {
                    "path": "results/fixture-report-full.json",
                    "sha256": "e" * 64,
                },
            ],
            "benchmarkDotNetHostEnvironment": self._bdn_report()[
                "HostEnvironmentInfo"
            ],
            "benchmarkDotNetControls": [
                {
                    "id": control["id"],
                    "metric": control["metric"],
                    "actual": 0,
                    "maximum": control["maximum"],
                    "passed": True,
                }
                for control in self.contract["benchmarkDotNetControls"]
            ],
            "historicalChecks": [],
            "workloads": workloads,
            "soakScenarios": soak_scenarios,
        }

    def _write_seed_evaluations(
        self,
        root: Path,
        runner_class: str,
    ) -> list[Path]:
        """Persist one seed evaluation per required target for a runner class.

        Each target carries its own run identifier because that is what the
        hosted matrix produces: one measurement job per engine, and the run
        identifier names the job. A fixture that gave all targets the same
        identifier would describe a single-process local run and hide every
        defect specific to the matrix path.
        """
        paths = []
        for target in self.contract["requiredTargets"]:
            evaluation = self._evaluation(target)
            evaluation["runnerClass"] = runner_class
            evaluation["runId"] = f"github-1000-{target}-attempt-1"
            path = root / f"{runner_class}-{target}.json"
            path.write_text(json.dumps(evaluation), encoding="utf-8")
            paths.append(path)

        return paths

    def _write_baseline(self, root: Path, paths: list[Path]) -> Path:
        """Persist a current accepted baseline for the supplied seed evaluations."""
        baseline = performance_evidence.seed_baseline(
            type(
                "Args",
                (),
                {
                    "contract": str(self._contract_path),
                    "evidence": [str(path) for path in paths],
                    "version": "test",
                    "accepted_utc": "2026-08-06T00:00:00Z",
                    "merge_existing": None,
                },
            )()
        )
        path = root / "baseline.json"
        path.write_text(json.dumps(baseline), encoding="utf-8")
        return path

    def _resolve_baseline_mode(
        self,
        baseline_path: Path,
        runner_class: str,
        requested_mode: str,
    ) -> dict[str, Any]:
        """Resolve one test baseline without invoking the command-line wrapper."""
        return performance_evidence.resolve_baseline_mode(
            type(
                "Args",
                (),
                {
                    "contract": str(self._contract_path),
                    "baseline": str(baseline_path),
                    "profile": "scorecard",
                    "runner_class": runner_class,
                    "requested_mode": requested_mode,
                },
            )()
        )
