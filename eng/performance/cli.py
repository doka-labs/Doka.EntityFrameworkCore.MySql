#!/usr/bin/env python3
"""Stable CLI and aggregate API for performance evidence tooling.

The command surface composes responsibility-focused modules while tests and
shell automation retain one supported entry point.
"""

import argparse
import sys
from pathlib import Path
from typing import Sequence

if __package__:
    from .contract import (
        BASELINE_PATH,
        SOAK_SCENARIO_IDS,
        COMPARABLE_ENVIRONMENT_FIELDS,
        LATENCY_CONFIRMATION_RUNS,
        P99_CONFIRMATION_RUNS,
        P99_EXPECTED_EXCEEDANCE_PROBABILITY,
        P99_SIGNIFICANCE_LEVEL,
        LATENCY_METRICS,
        HOST_ADMISSION_METRIC,
        MEASUREMENT_QUALITY_EXIT_CODE,
        MeasurementQualityError,
        PerformanceEvidenceError,
        load_json,
        write_json,
        sha256,
        repository_source_hash,
        finite_number,
        required_string,
        required_positive_integer,
        non_negative_integer,
        expected_warmup_sample_count,
        expected_measurement_sample_count,
        expected_workload_timeout_seconds,
        required_sha256,
        required_commit,
        required_current_timestamp,
        require_identity,
        validate_contract,
        applicable_workloads,
        close_enough,
    )
    from .host import (
        HostCpuCounterSnapshot,
        calculate_host_cpu_utilization,
        capture_host_cpu_counters,
        capture_host_preflight,
        capture_linux_cpu_counters,
        capture_macos_cpu_counters,
        parse_linux_cpu_counters,
        resolve_processor_identity,
        sample_host_cpu_utilization,
        validate_host_preflight,
    )
    from .statistics import (
        percentile,
        standard_error,
        binomial_survival_probability,
        calibration_adjustment_factor,
        historical_p99_check,
        historical_latency_check,
        validate_confirmed_latency_check,
        validate_confirmed_p99_check,
    )
    from .reports import (
        validate_workload_report,
        public_workload_metrics,
        validate_absolute_budgets,
    )
    from .soak import (
        validate_soak_report,
        validate_soak_scenario,
        require_exact_keys,
        soak_budget,
    )
    from .benchmarkdotnet import (
        validate_bdn_reports,
        validate_bdn_entry,
        evaluate_bdn_control,
    )
    from .confirmation import (
        load_matching_baseline,
        historical_p99_confirmation_candidates,
        historical_latency_confirmation_candidates,
        plan_tail_confirmation,
        merge_workload_tail_samples,
        merge_tail_confirmations,
        validate_historical_budgets,
    )
    from .environment import (
        validate_environment_compatibility,
        validate_host_workload_binding,
        canonical_processor_identity,
        validate_bdn_workload_environment,
    )
    from .evaluation import (
        collect_gc_diagnostics,
        evaluate,
    )
    from .baseline import (
        reject_truncated_measurements,
        validate_seed_evaluation,
        validate_normalized_workloads,
        validate_compare_evaluation,
        promote_baseline,
        seed_baseline,
        compare_baseline_files,
        validate_baseline_file,
        resolve_baseline_mode,
    )
    from .attempts import (
        import_selection,
        record_attempt,
        select_attempt,
        verify_imported_selection,
        verify_selection,
    )
else:
    # Direct script and file-based imports do not provide a package context.
    # Add only this module directory so the direct-execution imports below resolve
    # identically whether the tool is launched by Bash, unittest, or `python -m`.
    module_directory = str(Path(__file__).resolve().parent)
    if module_directory not in sys.path:
        sys.path.insert(0, module_directory)

    from performance.contract import (
        BASELINE_PATH,
        SOAK_SCENARIO_IDS,
        COMPARABLE_ENVIRONMENT_FIELDS,
        LATENCY_CONFIRMATION_RUNS,
        P99_CONFIRMATION_RUNS,
        P99_EXPECTED_EXCEEDANCE_PROBABILITY,
        P99_SIGNIFICANCE_LEVEL,
        LATENCY_METRICS,
        HOST_ADMISSION_METRIC,
        MEASUREMENT_QUALITY_EXIT_CODE,
        MeasurementQualityError,
        PerformanceEvidenceError,
        load_json,
        write_json,
        sha256,
        repository_source_hash,
        finite_number,
        required_string,
        required_positive_integer,
        non_negative_integer,
        expected_warmup_sample_count,
        expected_measurement_sample_count,
        expected_workload_timeout_seconds,
        required_sha256,
        required_commit,
        required_current_timestamp,
        require_identity,
        validate_contract,
        applicable_workloads,
        close_enough,
    )
    from performance.host import (
        HostCpuCounterSnapshot,
        calculate_host_cpu_utilization,
        capture_host_cpu_counters,
        capture_host_preflight,
        capture_linux_cpu_counters,
        capture_macos_cpu_counters,
        parse_linux_cpu_counters,
        resolve_processor_identity,
        sample_host_cpu_utilization,
        validate_host_preflight,
    )
    from performance.statistics import (
        percentile,
        standard_error,
        binomial_survival_probability,
        calibration_adjustment_factor,
        historical_p99_check,
        historical_latency_check,
        validate_confirmed_latency_check,
        validate_confirmed_p99_check,
    )
    from performance.reports import (
        validate_workload_report,
        public_workload_metrics,
        validate_absolute_budgets,
    )
    from performance.soak import (
        validate_soak_report,
        validate_soak_scenario,
        require_exact_keys,
        soak_budget,
    )
    from performance.benchmarkdotnet import (
        validate_bdn_reports,
        validate_bdn_entry,
        evaluate_bdn_control,
    )
    from performance.confirmation import (
        load_matching_baseline,
        historical_p99_confirmation_candidates,
        historical_latency_confirmation_candidates,
        plan_tail_confirmation,
        merge_workload_tail_samples,
        merge_tail_confirmations,
        validate_historical_budgets,
    )
    from performance.environment import (
        validate_environment_compatibility,
        validate_host_workload_binding,
        canonical_processor_identity,
        validate_bdn_workload_environment,
    )
    from performance.evaluation import (
        collect_gc_diagnostics,
        evaluate,
    )
    from performance.baseline import (
        reject_truncated_measurements,
        validate_seed_evaluation,
        validate_normalized_workloads,
        validate_compare_evaluation,
        promote_baseline,
        seed_baseline,
        compare_baseline_files,
        validate_baseline_file,
        resolve_baseline_mode,
    )
    from performance.attempts import (
        import_selection,
        record_attempt,
        select_attempt,
        verify_imported_selection,
        verify_selection,
    )

def add_common_identity_arguments(parser: argparse.ArgumentParser) -> None:
    """Add current-run identity arguments shared by validation commands."""
    parser.add_argument("--contract", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--profile", required=True)
    parser.add_argument("--output", required=True)


def build_parser() -> argparse.ArgumentParser:
    """Build the command-line surface."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    bdn_parser = subparsers.add_parser("validate-bdn")
    add_common_identity_arguments(bdn_parser)
    bdn_parser.add_argument("--reports", required=True)

    evaluate_parser = subparsers.add_parser("evaluate")
    add_common_identity_arguments(evaluate_parser)
    evaluate_parser.add_argument("--host", required=True)
    evaluate_parser.add_argument("--workloads", required=True)
    evaluate_parser.add_argument("--soak")
    evaluate_parser.add_argument("--bdn", required=True)
    evaluate_parser.add_argument("--baseline", required=True)
    evaluate_parser.add_argument("--mode", choices=("compare", "seed"), required=True)

    tail_plan_parser = subparsers.add_parser("plan-tail-confirmation")
    add_common_identity_arguments(tail_plan_parser)
    tail_plan_parser.add_argument("--workloads", required=True)
    tail_plan_parser.add_argument("--baseline", required=True)

    tail_merge_parser = subparsers.add_parser("merge-tail-confirmations")
    add_common_identity_arguments(tail_merge_parser)
    tail_merge_parser.add_argument("--workloads", required=True)
    tail_merge_parser.add_argument("--plan", required=True)
    tail_merge_parser.add_argument("--confirmation", action="append", required=True)
    tail_merge_parser.add_argument(
        "--confirmation-host",
        action="append",
        required=True,
    )

    seed_parser = subparsers.add_parser("seed")
    seed_parser.add_argument("--contract", required=True)
    seed_parser.add_argument("--baseline", required=True)
    seed_parser.add_argument("--version", required=True)
    seed_parser.add_argument("--accepted-utc")
    seed_parser.add_argument("--evidence", action="append", required=True)
    seed_parser.add_argument("--merge-existing")

    promote_parser = subparsers.add_parser("promote")
    promote_parser.add_argument("--contract", required=True)
    promote_parser.add_argument("--baseline", required=True)
    promote_parser.add_argument("--version", required=True)
    promote_parser.add_argument("--accepted-utc")
    promote_parser.add_argument("--evidence", action="append", required=True)
    promote_parser.add_argument("--merge-existing", required=True)

    baseline_parser = subparsers.add_parser("validate-baseline")
    baseline_parser.add_argument("--contract", required=True)
    baseline_parser.add_argument("--baseline", required=True)
    baseline_parser.add_argument("--output", required=True)

    comparison_parser = subparsers.add_parser("compare-baselines")
    comparison_parser.add_argument("--contract", required=True)
    comparison_parser.add_argument("--current", required=True)
    comparison_parser.add_argument("--candidate", required=True)
    comparison_parser.add_argument("--output", required=True)

    resolve_parser = subparsers.add_parser("resolve-baseline-mode")
    resolve_parser.add_argument("--contract", required=True)
    resolve_parser.add_argument("--baseline", required=True)
    resolve_parser.add_argument("--profile", required=True)
    resolve_parser.add_argument("--runner-class", required=True)
    resolve_parser.add_argument(
        "--requested-mode",
        choices=("auto", "compare", "seed"),
        default="auto",
    )
    resolve_parser.add_argument("--output", required=True)

    source_hash_parser = subparsers.add_parser("source-hash")
    source_hash_parser.add_argument("--repo", required=True)

    host_parser = subparsers.add_parser("host-preflight")
    host_parser.add_argument("--contract", required=True)
    host_parser.add_argument("--output", required=True)

    attempt_parser = subparsers.add_parser("record-attempt")
    attempt_parser.add_argument("--artifact-root", required=True)
    attempt_parser.add_argument("--report-directory", required=True)
    attempt_parser.add_argument("--target", required=True)
    attempt_parser.add_argument("--profile", required=True)
    attempt_parser.add_argument("--attempt", required=True, type=int)
    attempt_parser.add_argument("--run-id", required=True)
    attempt_parser.add_argument("--commit", required=True)
    attempt_parser.add_argument("--source-hash", required=True)
    attempt_parser.add_argument("--runner-class", required=True)
    attempt_parser.add_argument("--exit-code", required=True, type=int)
    attempt_parser.add_argument("--output", required=True)

    selection_parser = subparsers.add_parser("select-attempt")
    selection_parser.add_argument("--receipt", action="append", required=True)
    selection_parser.add_argument("--destination", required=True)
    selection_parser.add_argument("--output", required=True)

    selection_verification_parser = subparsers.add_parser(
        "verify-attempt-selection",
    )
    selection_verification_parser.add_argument("--artifact-root", required=True)
    selection_verification_parser.add_argument("--selection", required=True)

    import_parser = subparsers.add_parser("import-attempt-selection")
    import_parser.add_argument("--artifact-root", required=True)
    import_parser.add_argument("--selection", required=True)
    import_parser.add_argument("--destination", required=True)
    import_parser.add_argument("--expected-target", required=True)
    import_parser.add_argument("--expected-commit", required=True)

    import_verification_parser = subparsers.add_parser(
        "verify-imported-attempt",
    )
    import_verification_parser.add_argument("--destination", required=True)
    import_verification_parser.add_argument("--expected-target", required=True)
    import_verification_parser.add_argument("--expected-commit", required=True)

    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Run one deterministic evidence command."""
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.command == "source-hash":
            print(repository_source_hash(Path(args.repo)))
            return 0
        if args.command == "host-preflight":
            contract = load_json(Path(args.contract))
            payload = capture_host_preflight(contract)
            write_json(Path(args.output), payload)
            if payload["success"] is not True:
                observed_samples = ", ".join(
                    f"{sample['cpuUtilization']:.4f}"
                    for sample in payload["samples"]
                )
                raise MeasurementQualityError(
                    "Host preflight interval CPU utilization remained above "
                    "the admission ceiling. "
                    f"Samples: [{observed_samples}]; maximum: "
                    f"{payload['observedMaximumCpuUtilization']:.4f}; ceiling: "
                    f"{payload['maximumCpuUtilization']:.4f}."
                )
            print(f"Performance host preflight passed: {args.output}")
            return 0
        if args.command == "record-attempt":
            payload = record_attempt(
                artifact_root=Path(args.artifact_root),
                report_directory=Path(args.report_directory),
                output=Path(args.output),
                target=args.target,
                profile=args.profile,
                attempt=args.attempt,
                run_id=args.run_id,
                commit=args.commit,
                source_hash=args.source_hash,
                runner_class=args.runner_class,
                exit_code=args.exit_code,
            )
            print(
                f"Recorded performance attempt {payload['attempt']} as "
                f"{payload['status']}: {args.output}"
            )
            return 0
        if args.command == "select-attempt":
            destination = Path(args.destination)
            output = Path(args.output)
            payload = select_attempt(
                receipt_paths=[Path(path) for path in args.receipt],
                destination=destination,
            )
            write_json(output, payload)
            verify_selection(
                artifact_root=destination,
                selection_path=output,
            )
            print(
                f"Selected performance attempt {payload['selectedAttempt']}: "
                f"{args.output}"
            )
            return 0
        if args.command == "verify-attempt-selection":
            payload = verify_selection(
                artifact_root=Path(args.artifact_root),
                selection_path=Path(args.selection),
            )
            print(
                f"Verified performance attempt {payload['selectedAttempt']}: "
                f"{args.selection}"
            )
            return 0
        if args.command == "import-attempt-selection":
            payload = import_selection(
                artifact_root=Path(args.artifact_root),
                selection_path=Path(args.selection),
                destination=Path(args.destination),
                expected_target=args.expected_target,
                expected_commit=args.expected_commit,
            )
            verify_imported_selection(
                destination=Path(args.destination),
                expected_target=args.expected_target,
                expected_commit=args.expected_commit,
            )
            print(
                f"Imported performance attempt {payload['selectedAttempt']}: "
                f"{args.destination}"
            )
            return 0
        if args.command == "verify-imported-attempt":
            payload = verify_imported_selection(
                destination=Path(args.destination),
                expected_target=args.expected_target,
                expected_commit=args.expected_commit,
            )
            print(
                f"Verified imported performance attempt "
                f"{payload['selectedAttempt']}: {args.destination}"
            )
            return 0
        if args.command == "validate-bdn":
            contract = load_json(Path(args.contract))
            validate_contract(contract)
            payload = validate_bdn_reports(
                contract,
                Path(args.reports),
                run_id=args.run_id,
                target=args.target,
                profile=args.profile,
            )
        elif args.command == "evaluate":
            payload = evaluate(args)
        elif args.command == "plan-tail-confirmation":
            payload = plan_tail_confirmation(args)
        elif args.command == "merge-tail-confirmations":
            payload = merge_tail_confirmations(args)
        elif args.command in {"seed", "promote"}:
            payload = (
                seed_baseline(args)
                if args.command == "seed"
                else promote_baseline(args)
            )
            write_json(Path(args.baseline), payload)
            print(
                f"Accepted performance baseline '{payload['baselineVersion']}' "
                f"with {len(payload['baselines'])} target records."
            )
            return 0
        elif args.command == "validate-baseline":
            payload = validate_baseline_file(args)
        elif args.command == "compare-baselines":
            payload = compare_baseline_files(args)
        elif args.command == "resolve-baseline-mode":
            payload = resolve_baseline_mode(args)
        else:
            parser.error(f"Unknown command '{args.command}'.")

        write_json(Path(args.output), payload)
        print(f"Performance evidence command '{args.command}' passed: {args.output}")
        return 0
    except MeasurementQualityError as error:
        print(f"Performance evidence inconclusive: {error}", file=sys.stderr)
        return MEASUREMENT_QUALITY_EXIT_CODE
    except PerformanceEvidenceError as error:
        print(f"Performance evidence failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
