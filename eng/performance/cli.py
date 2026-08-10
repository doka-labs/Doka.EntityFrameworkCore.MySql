#!/usr/bin/env python3
"""Stable CLI and aggregate API for performance evidence tooling.

The command surface composes responsibility-focused modules while tests and
shell automation retain one supported entry point.

Names imported in the `name as name` form are that aggregate API: this module
does not use them itself, it publishes them for callers that import it as one
facade. Anything imported plainly is used here, so an import this module has
outgrown shows up as a lint finding rather than accumulating.
"""

import argparse
import sys
from pathlib import Path
from typing import Sequence

if __package__:
    from .contract import (
        HOST_ADMISSION_METRIC as HOST_ADMISSION_METRIC,
        ENVIRONMENT_NOT_COMPARABLE_EXIT_CODE,
        INVALID_EVIDENCE_EXIT_CODE,
        MEASUREMENT_QUALITY_EXIT_CODE,
        RECALIBRATION_REQUIRED_EXIT_CODE,
        EnvironmentNotComparableError,
        InvalidEvidenceError,
        MeasurementQualityError,
        RecalibrationRequiredError,
        PerformanceEvidenceError,
        load_json,
        write_json,
        sha256,
        repository_source_hash,
        expected_warmup_sample_count as expected_warmup_sample_count,
        expected_measurement_sample_count as expected_measurement_sample_count,
        expected_workload_timeout_seconds as expected_workload_timeout_seconds,
        validate_contract,
    )
    from .paired import (
        assemble_evidence as assemble_paired_evidence,
        evaluate_paired_comparison,
    )
    from .host import (
        HostCpuCounterSnapshot as HostCpuCounterSnapshot,
        calculate_host_cpu_utilization as calculate_host_cpu_utilization,
        capture_host_preflight,
        parse_linux_cpu_counters as parse_linux_cpu_counters,
        validate_host_preflight as validate_host_preflight,
    )
    from .statistics import (
        percentile as percentile,
        historical_p99_check as historical_p99_check,
        validate_confirmed_p99_check as validate_confirmed_p99_check,
        standard_error as standard_error,
    )
    from .reports import (
        validate_workload_report as validate_workload_report,
        validate_absolute_budgets as validate_absolute_budgets,
    )
    from .soak import (
        validate_soak_report as validate_soak_report,
    )
    from .benchmarkdotnet import (
        validate_bdn_reports,
    )
    from .confirmation import (
        historical_latency_confirmation_candidates as historical_latency_confirmation_candidates,
        historical_p99_confirmation_candidates as historical_p99_confirmation_candidates,
        plan_tail_confirmation,
        merge_workload_tail_samples as merge_workload_tail_samples,
        merge_tail_confirmations,
        validate_historical_budgets as validate_historical_budgets,
    )
    from .environment import (
        validate_environment_compatibility as validate_environment_compatibility,
        validate_bdn_workload_environment as validate_bdn_workload_environment,
    )
    from .evaluation import (
        collect_gc_diagnostics as collect_gc_diagnostics,
        evaluate,
    )
    from .baseline import (
        reject_truncated_measurements as reject_truncated_measurements,
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
        HOST_ADMISSION_METRIC as HOST_ADMISSION_METRIC,
        ENVIRONMENT_NOT_COMPARABLE_EXIT_CODE,
        INVALID_EVIDENCE_EXIT_CODE,
        MEASUREMENT_QUALITY_EXIT_CODE,
        RECALIBRATION_REQUIRED_EXIT_CODE,
        EnvironmentNotComparableError,
        InvalidEvidenceError,
        MeasurementQualityError,
        RecalibrationRequiredError,
        PerformanceEvidenceError,
        load_json,
        write_json,
        sha256,
        repository_source_hash,
        expected_warmup_sample_count as expected_warmup_sample_count,
        expected_measurement_sample_count as expected_measurement_sample_count,
        expected_workload_timeout_seconds as expected_workload_timeout_seconds,
        validate_contract,
    )
    from performance.paired import (
        assemble_evidence as assemble_paired_evidence,
        evaluate_paired_comparison,
    )
    from performance.host import (
        HostCpuCounterSnapshot as HostCpuCounterSnapshot,
        calculate_host_cpu_utilization as calculate_host_cpu_utilization,
        capture_host_preflight,
        parse_linux_cpu_counters as parse_linux_cpu_counters,
        validate_host_preflight as validate_host_preflight,
    )
    from performance.statistics import (
        percentile as percentile,
        historical_p99_check as historical_p99_check,
        validate_confirmed_p99_check as validate_confirmed_p99_check,
        standard_error as standard_error,
    )
    from performance.reports import (
        validate_workload_report as validate_workload_report,
        validate_absolute_budgets as validate_absolute_budgets,
    )
    from performance.soak import (
        validate_soak_report as validate_soak_report,
    )
    from performance.benchmarkdotnet import (
        validate_bdn_reports,
    )
    from performance.confirmation import (
        historical_latency_confirmation_candidates as historical_latency_confirmation_candidates,
        historical_p99_confirmation_candidates as historical_p99_confirmation_candidates,
        plan_tail_confirmation,
        merge_workload_tail_samples as merge_workload_tail_samples,
        merge_tail_confirmations,
        validate_historical_budgets as validate_historical_budgets,
    )
    from performance.environment import (
        validate_environment_compatibility as validate_environment_compatibility,
        validate_bdn_workload_environment as validate_bdn_workload_environment,
    )
    from performance.evaluation import (
        collect_gc_diagnostics as collect_gc_diagnostics,
        evaluate,
    )
    from performance.baseline import (
        reject_truncated_measurements as reject_truncated_measurements,
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

    # A paired comparison measures a reference and a candidate provider under
    # one benchmark driver on one machine, so it needs neither a baseline nor
    # an environment match against a recorded host.
    assemble_paired_parser = subparsers.add_parser("assemble-paired-evidence")
    assemble_paired_parser.add_argument("--blocks", required=True)
    assemble_paired_parser.add_argument("--contract", required=True)
    assemble_paired_parser.add_argument("--target", required=True)
    assemble_paired_parser.add_argument("--run-id", required=True)
    assemble_paired_parser.add_argument("--candidate-commit", required=True)
    assemble_paired_parser.add_argument("--reference-commit", required=True)
    assemble_paired_parser.add_argument("--driver-source-hash", required=True)
    assemble_paired_parser.add_argument("--contract-digest", required=True)
    assemble_paired_parser.add_argument("--profile", required=True)
    assemble_paired_parser.add_argument("--source-hash", required=True)
    assemble_paired_parser.add_argument("--runner-class", required=True)
    assemble_paired_parser.add_argument("--execution-order", required=True)
    assemble_paired_parser.add_argument("--soak", required=True)
    assemble_paired_parser.add_argument("--output", required=True)

    # The workflow reads the attempt decision from the receipt rather than
    # comparing against a state name in YAML, so the retry policy has exactly
    # one home.
    attempt_outputs_parser = subparsers.add_parser("attempt-outputs")
    attempt_outputs_parser.add_argument("--receipt", required=True)

    # The profile a receipt records is the profile that measured, which is not
    # the profile the caller orchestrates with: a paired run is dispatched
    # under the caller's profile and measures under the registered block
    # profile. Resolving that in one place is what keeps the receipt and the
    # evaluation from disagreeing about which profile produced the numbers.
    attempt_profile_parser = subparsers.add_parser("attempt-profile")
    attempt_profile_parser.add_argument("--contract", required=True)
    attempt_profile_parser.add_argument("--profile", required=True)
    attempt_profile_parser.add_argument(
        "--comparison-mode",
        choices=("historical", "paired"),
        default="historical",
    )

    evaluate_paired_parser = subparsers.add_parser("evaluate-paired")
    evaluate_paired_parser.add_argument("--contract", required=True)
    evaluate_paired_parser.add_argument("--evidence", required=True)
    evaluate_paired_parser.add_argument("--output", required=True)

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
    attempt_parser.add_argument(
        "--comparison-mode",
        choices=("historical", "paired"),
        default="historical",
    )
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
        if args.command == "attempt-profile":
            contract = load_json(Path(args.contract))
            if args.comparison_mode == "paired":
                print(contract["pairedPolicy"]["blocks"]["profile"])
            else:
                print(args.profile)
            return 0
        if args.command == "attempt-outputs":
            receipt = load_json(Path(args.receipt))
            print(f"status={receipt['status']}")
            print(f"retry={str(receipt['retryEligible']).lower()}")
            return 0
        if args.command == "assemble-paired-evidence":
            payload = assemble_paired_evidence(
                Path(args.blocks),
                contract=load_json(Path(args.contract)),
                target=args.target,
                run_id=args.run_id,
                candidate_commit=args.candidate_commit,
                reference_commit=args.reference_commit,
                driver_source_hash=args.driver_source_hash,
                contract_digest=args.contract_digest,
                profile=args.profile,
                source_hash=args.source_hash,
                runner_class=args.runner_class,
                execution_order=load_json(Path(args.execution_order)),
                soak_report=load_json(Path(args.soak)),
            )
            write_json(Path(args.output), payload)
            print(f"Paired evidence assembled: {args.output}")
            return 0
        if args.command == "evaluate-paired":
            contract_path = Path(args.contract)
            contract = load_json(contract_path)
            evidence = load_json(Path(args.evidence))
            # The digest of the file this evaluation actually read. Evidence
            # that names another contract was decided against other budgets,
            # caps, and workloads than the ones about to judge it.
            payload = evaluate_paired_comparison(
                evidence, contract, contract_digest=sha256(contract_path)
            )
            write_json(Path(args.output), payload)
            # The qualification state is the release verdict, so it leaves
            # through the exit code rather than only through the document. A
            # caller that ignored the document would otherwise treat a
            # regression as a successful measurement.
            qualification = payload["qualification"]
            print(f"Paired comparison {qualification}: {args.output}")
            if qualification == "qualified":
                return 0
            if qualification == "regression":
                return 1
            if qualification == "inconclusive":
                return MEASUREMENT_QUALITY_EXIT_CODE
            if qualification == "recalibration-required":
                return RECALIBRATION_REQUIRED_EXIT_CODE
            return INVALID_EVIDENCE_EXIT_CODE
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
                comparison_mode=args.comparison_mode,
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
    except EnvironmentNotComparableError as error:
        print(f"Performance environment not comparable: {error}", file=sys.stderr)
        return ENVIRONMENT_NOT_COMPARABLE_EXIT_CODE
    except RecalibrationRequiredError as error:
        print(f"Performance comparator requires recalibration: {error}",
              file=sys.stderr)
        return RECALIBRATION_REQUIRED_EXIT_CODE
    except InvalidEvidenceError as error:
        print(f"Performance evidence is invalid: {error}", file=sys.stderr)
        return INVALID_EVIDENCE_EXIT_CODE
    except PerformanceEvidenceError as error:
        print(f"Performance evidence failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
