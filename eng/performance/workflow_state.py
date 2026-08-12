#!/usr/bin/env python3
"""Resolve the cheap control plane for the hosted benchmark workflow.

The scorecard matrix is intentionally expensive. This module keeps the event,
comparison, baseline, and open-proposal decisions in locally testable Python
instead of embedding them in GitHub Actions shell conditions.
"""

from __future__ import annotations

import argparse
import json
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from . import cli as performance_evidence
    from .inputs import (
        MEASUREMENT_INPUT_FILES,
        MEASUREMENT_INPUT_PREFIXES,
        affects_measurement,
    )
else:
    import cli as performance_evidence
    from inputs import (
        MEASUREMENT_INPUT_FILES,
        MEASUREMENT_INPUT_PREFIXES,
        affects_measurement,
    )


ZERO_REVISION = "0" * 40

PERFORMANCE_INPUT_FILES = MEASUREMENT_INPUT_FILES
PERFORMANCE_INPUT_PREFIXES = MEASUREMENT_INPUT_PREFIXES
COMPARISON_MODES_BY_BASELINE_MODE = {
    "compare": "paired",
    "seed": "historical",
}


class WorkflowStateError(RuntimeError):
    """Report an invalid or uninspectable benchmark workflow state."""


def comparison_mode_for_baseline_mode(baseline_mode: str) -> str:
    """Select CPU-independent comparison or historical seed measurement.

    A normal comparison must measure reference and candidate on one allocated
    runner. A seed has no compatible accepted baseline yet, so it measures one
    candidate historically and submits that evidence for review instead.
    """
    try:
        return COMPARISON_MODES_BY_BASELINE_MODE[baseline_mode]
    except KeyError as error:
        raise WorkflowStateError(
            f"Unsupported resolved baseline mode: {baseline_mode}",
        ) from error


@dataclass(frozen=True)
class ProposalState:
    """Describe whether one open baseline proposal is reusable."""

    disposition: str
    reason: str
    source_commit: str | None = None
    relevant_changes: tuple[str, ...] = ()
    behind_current: bool = False


def is_performance_input(path: str) -> bool:
    """Return whether a repository path can change measured behavior.

    Gate policy and workflow orchestration deliberately stay outside this
    set. They can change whether evidence passes, but cannot invalidate the
    measured provider or host workload represented by an open proposal.
    """
    return affects_measurement(path)


def run_git(
    repository: Path,
    arguments: Sequence[str],
    *,
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
    """Run one read-only Git query with deterministic captured output."""
    result = subprocess.run(
        ["git", "-C", str(repository), *arguments],
        check=False,
        capture_output=True,
        text=True,
    )

    if check and result.returncode != 0:
        detail = result.stderr.strip() or result.stdout.strip()
        raise WorkflowStateError(
            f"git {' '.join(arguments)} failed: {detail}",
        )

    return result


def relevant_changes(
    repository: Path,
    before_revision: str,
    current_revision: str,
) -> tuple[str, ...]:
    """Return performance inputs changed between two repository revisions."""
    if before_revision == ZERO_REVISION:
        return ("<initial-push>",)

    result = run_git(
        repository,
        [
            "diff",
            "--no-renames",
            "--name-only",
            "--diff-filter=ACDMRTUXB",
            before_revision,
            current_revision,
            "--",
        ],
    )
    return tuple(
        sorted(
            path
            for path in result.stdout.splitlines()
            if is_performance_input(path)
        ),
    )


def event_requires_scorecard(
    repository: Path,
    event_name: str,
    before_revision: str | None,
    current_revision: str,
) -> tuple[bool, tuple[str, ...]]:
    """Resolve whether the current event requires fresh scorecard evidence."""
    if event_name in {"schedule", "workflow_dispatch"}:
        return True, (f"<{event_name}>",)

    if event_name != "push":
        raise WorkflowStateError(
            f"Unsupported benchmark workflow event: {event_name}",
        )

    if not before_revision:
        raise WorkflowStateError("A push event requires its before revision.")

    changes = relevant_changes(
        repository,
        before_revision,
        current_revision,
    )

    return bool(changes), changes


def matching_baseline_entries(
    baseline: dict[str, Any],
    profile: str,
    runner_class: str,
) -> list[dict[str, Any]]:
    """Select the complete scorecard group represented by one proposal."""
    entries = baseline.get("baselines")
    if not isinstance(entries, list):
        raise WorkflowStateError("The proposed baseline has no baselines array.")

    return [
        entry
        for entry in entries
        if isinstance(entry, dict)
        and entry.get("profile") == profile
        and entry.get("runnerClass") == runner_class
    ]


def inspect_proposal(
    repository: Path,
    contract_path: Path,
    contract: dict[str, Any],
    proposed_baseline: Path | None,
    proposal_head_ref: str | None,
    current_revision: str,
    profile: str,
    runner_class: str,
) -> ProposalState:
    """Validate and classify an existing review-only baseline proposal."""
    if proposed_baseline is None:
        return ProposalState("absent", "No open baseline proposal exists.")

    try:
        if proposal_head_ref is None:
            raise WorkflowStateError(
                "The proposed baseline has no corresponding branch revision.",
            )

        validation_args = argparse.Namespace(
            contract=contract_path,
            baseline=proposed_baseline,
            output=None,
        )
        performance_evidence.validate_baseline_file(validation_args)
        baseline = performance_evidence.load_json(proposed_baseline)
        entries = matching_baseline_entries(
            baseline,
            profile,
            runner_class,
        )

        required_targets = set(contract["requiredTargets"])
        actual_targets = {entry.get("target") for entry in entries}
        if actual_targets != required_targets:
            raise WorkflowStateError(
                "The proposed baseline does not contain the exact required "
                "target pair.",
            )

        source_commits = {entry.get("commit") for entry in entries}
        if len(source_commits) != 1:
            raise WorkflowStateError(
                "The proposed target pair does not share one source commit.",
            )

        source_commit = next(iter(source_commits))
        if not isinstance(source_commit, str) or not source_commit:
            raise WorkflowStateError(
                "The proposed baseline source commit is missing.",
            )

        run_git(
            repository,
            ["rev-parse", "--verify", f"{source_commit}^{{commit}}"],
        )
        source_ancestry = run_git(
            repository,
            [
                "merge-base",
                "--is-ancestor",
                source_commit,
                current_revision,
            ],
            check=False,
        )
        if source_ancestry.returncode not in {0, 1}:
            detail = (
                source_ancestry.stderr.strip()
                or source_ancestry.stdout.strip()
            )
            raise WorkflowStateError(
                "The baseline source ancestry could not be inspected: "
                + detail,
            )

        if source_ancestry.returncode == 1:
            raise WorkflowStateError(
                "The baseline source commit is not an ancestor of the "
                "current main revision.",
            )

        changes = relevant_changes(
            repository,
            source_commit,
            current_revision,
        )
        ancestry = run_git(
            repository,
            [
                "merge-base",
                "--is-ancestor",
                current_revision,
                proposal_head_ref,
            ],
            check=False,
        )
        if ancestry.returncode not in {0, 1}:
            detail = ancestry.stderr.strip() or ancestry.stdout.strip()
            raise WorkflowStateError(
                "The proposal ancestry could not be inspected: " + detail,
            )

        behind_current = ancestry.returncode == 1

        if changes:
            return ProposalState(
                "stale",
                "Performance inputs changed after the proposal evidence was "
                "captured.",
                source_commit,
                changes,
                behind_current,
            )

        return ProposalState(
            "current",
            "The open proposal contains current, valid scorecard evidence.",
            source_commit,
            (),
            behind_current,
        )
    except (
        KeyError,
        OSError,
        performance_evidence.PerformanceEvidenceError,
        WorkflowStateError,
    ) as error:
        return ProposalState(
            "invalid",
            f"The open proposal cannot be reused: {error}",
        )


def decide_work(
    baseline_mode: str,
    event_requires_fresh_evidence: bool,
    proposal: ProposalState,
) -> tuple[bool, bool, bool]:
    """Return scorecard, proposal-sync, and proposal-write decisions.

    Compare runs are immutable evidence. Only an explicit or automatically
    selected seed run may mutate the reviewed accepted-baseline proposal.
    """
    if baseline_mode not in {"compare", "seed"}:
        raise WorkflowStateError(
            f"Unsupported resolved baseline mode: {baseline_mode}",
        )

    if not event_requires_fresh_evidence:
        sync_required = (
            baseline_mode == "seed"
            and proposal.disposition == "current"
            and proposal.behind_current
        )
        return False, sync_required, False

    return True, False, baseline_mode == "seed"


def parse_args() -> argparse.Namespace:
    """Parse the GitHub workflow state resolver command line."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--event-name", required=True)
    parser.add_argument("--before-revision")
    parser.add_argument("--current-revision", required=True)
    parser.add_argument(
        "--baseline-mode",
        choices=("compare", "seed"),
        required=True,
    )
    parser.add_argument("--contract", type=Path, required=True)
    parser.add_argument("--proposed-baseline", type=Path)
    parser.add_argument("--proposal-head-ref")
    parser.add_argument("--profile", required=True)
    parser.add_argument("--runner-class", required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    """Resolve and persist the benchmark workflow control-plane decision."""
    args = parse_args()
    repository = args.repo.resolve()
    contract = performance_evidence.load_json(args.contract)
    performance_evidence.validate_contract(contract)

    event_required, event_changes = event_requires_scorecard(
        repository,
        args.event_name,
        args.before_revision,
        args.current_revision,
    )
    proposal = inspect_proposal(
        repository,
        args.contract,
        contract,
        args.proposed_baseline,
        args.proposal_head_ref,
        args.current_revision,
        args.profile,
        args.runner_class,
    )
    scorecard_required, sync_required, proposal_required = decide_work(
        args.baseline_mode,
        event_required,
        proposal,
    )

    payload = {
        "baselineMode": args.baseline_mode,
        "comparisonMode": comparison_mode_for_baseline_mode(args.baseline_mode),
        "eventRequiresScorecard": event_required,
        "eventRelevantChanges": list(event_changes),
        "proposal": {
            "disposition": proposal.disposition,
            "reason": proposal.reason,
            "sourceCommit": proposal.source_commit,
            "relevantChanges": list(proposal.relevant_changes),
            "behindCurrent": proposal.behind_current,
        },
        "scorecardRequired": scorecard_required,
        "syncRequired": sync_required,
        "proposalRequired": proposal_required,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(payload, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
