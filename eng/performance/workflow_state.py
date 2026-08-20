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
from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from xml.etree import ElementTree

if __package__:
    from . import cli as performance_evidence
    from .inputs import (
        MEASUREMENT_INPUT_FILES,
        MEASUREMENT_INPUT_PREFIXES,
        NO_MEASUREMENT,
        SCORECARD_MEASUREMENT,
        SMOKE_MEASUREMENT,
        affects_measurement,
        measurement_tier,
    )
else:
    import cli as performance_evidence
    from inputs import (
        MEASUREMENT_INPUT_FILES,
        MEASUREMENT_INPUT_PREFIXES,
        NO_MEASUREMENT,
        SCORECARD_MEASUREMENT,
        SMOKE_MEASUREMENT,
        affects_measurement,
        measurement_tier,
    )


ZERO_REVISION = "0" * 40

PERFORMANCE_INPUT_FILES = MEASUREMENT_INPUT_FILES
PERFORMANCE_INPUT_PREFIXES = MEASUREMENT_INPUT_PREFIXES
COMPARISON_MODES_BY_BASELINE_MODE = {
    "compare": "paired",
    "seed": "historical",
}

CENTRAL_PACKAGE_FILE = "Directory.Packages.props"
SCORECARD_PACKAGE_GROUPS = frozenset(
    {
        "Benchmarks",
        "Production",
    }
)
NON_MEASUREMENT_PACKAGE_GROUPS = frozenset(
    {
        "Example hosts",
        "Production analyzers",
        "Tests",
    }
)
KNOWN_PACKAGE_GROUPS = SCORECARD_PACKAGE_GROUPS | NON_MEASUREMENT_PACKAGE_GROUPS


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


@dataclass(frozen=True)
class MeasurementChanges:
    """Group changed inputs by the measurement they require."""

    scorecard: tuple[str, ...] = ()
    smoke: tuple[str, ...] = ()

    @property
    def tier(self) -> str:
        """Return the strongest measurement required by this change set."""
        if self.scorecard:
            return SCORECARD_MEASUREMENT
        if self.smoke:
            return SMOKE_MEASUREMENT

        return NO_MEASUREMENT

    @property
    def all(self) -> tuple[str, ...]:
        """Return every measurement-relevant path in deterministic order."""
        return tuple(sorted((*self.scorecard, *self.smoke)))


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


def revision_file(
    repository: Path,
    revision: str,
    path: str,
) -> str:
    """Read one repository file from an exact revision."""
    return run_git(
        repository,
        ["show", f"{revision}:{path}"],
    ).stdout


def central_package_contract(
    document: str,
) -> dict[str, tuple[str, tuple[tuple[str, str], ...]]]:
    """Parse the centrally managed package inputs that can affect measurement.

    The resolver consumes this file outside MSBuild, so it accepts only the
    small contract the repository owns. Unknown items fail closed into a full
    scorecard instead of being optimistically treated as tooling-only.
    """
    try:
        root = ElementTree.fromstring(document)
    except ElementTree.ParseError as error:
        raise WorkflowStateError(
            f"The central package contract is malformed: {error}",
        ) from error

    if root.tag != "Project":
        raise WorkflowStateError(
            "The central package contract must have a Project root.",
        )

    scorecard_inputs: dict[str, tuple[str, tuple[tuple[str, str], ...]]] = {}
    package_ids: set[str] = set()

    for group in root:
        if group.tag == "PropertyGroup":
            if group.attrib:
                raise WorkflowStateError(
                    "The central package contract contains an attributed "
                    "PropertyGroup.",
                )

            for property_element in group:
                if len(property_element):
                    raise WorkflowStateError(
                        "The central package contract contains a nested property.",
                    )
                input_name = f"property:{property_element.tag}"
                if input_name in scorecard_inputs:
                    raise WorkflowStateError(
                        "The central package contract contains duplicate property "
                        f"'{property_element.tag}'.",
                    )
                scorecard_inputs[input_name] = (
                    (property_element.text or "").strip(),
                    tuple(sorted(property_element.attrib.items())),
                )
            continue

        if group.tag != "ItemGroup":
            raise WorkflowStateError(
                "The central package contract contains an unsupported root item "
                f"'{group.tag}'.",
            )

        if set(group.attrib) != {"Label"}:
            raise WorkflowStateError(
                "Every central package ItemGroup requires only a Label.",
            )

        group_name = group.attrib["Label"]
        if group_name not in KNOWN_PACKAGE_GROUPS:
            raise WorkflowStateError(
                "The central package contract contains an unclassified group "
                f"'{group_name}'.",
            )

        for package_element in group:
            if package_element.tag != "PackageVersion" or len(package_element):
                raise WorkflowStateError(
                    "The central package contract contains an unsupported item "
                    f"'{package_element.tag}'.",
                )

            package_id = package_element.attrib.get("Include")
            version = package_element.attrib.get("Version")
            if not package_id or not version:
                raise WorkflowStateError(
                    "Every central PackageVersion requires Include and Version.",
                )
            if package_id in package_ids:
                raise WorkflowStateError(
                    "The central package contract contains duplicate package "
                    f"'{package_id}'.",
                )
            package_ids.add(package_id)

            if group_name in SCORECARD_PACKAGE_GROUPS:
                scorecard_inputs[f"package:{package_id}"] = (
                    version,
                    tuple(sorted(package_element.attrib.items())),
                )

    return scorecard_inputs


def central_package_change_requires_scorecard(
    repository: Path,
    before_revision: str,
    current_revision: str,
) -> bool:
    """Return whether a central package edit can affect measured execution.

    Production, benchmark, SDK-property, and unknown structural changes remain
    fail-closed. Packages in the repository's classified test, analyzer, and
    example groups do not allocate six long-running scorecards merely because
    their CVE patch is centrally managed beside production dependencies.
    """
    try:
        before_contract = central_package_contract(
            revision_file(repository, before_revision, CENTRAL_PACKAGE_FILE),
        )
        current_contract = central_package_contract(
            revision_file(repository, current_revision, CENTRAL_PACKAGE_FILE),
        )
    except WorkflowStateError:
        return True

    return before_contract != current_contract


def changed_measurement_inputs(
    repository: Path,
    before_revision: str,
    current_revision: str,
) -> MeasurementChanges:
    """Classify changed repository inputs into smoke and scorecard tiers."""
    if before_revision == ZERO_REVISION:
        return MeasurementChanges(scorecard=("<initial-push>",))

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
    scorecard: list[str] = []
    smoke: list[str] = []

    for path in result.stdout.splitlines():
        tier = measurement_tier(path)
        if path == CENTRAL_PACKAGE_FILE:
            tier = (
                SCORECARD_MEASUREMENT
                if central_package_change_requires_scorecard(
                    repository,
                    before_revision,
                    current_revision,
                )
                else NO_MEASUREMENT
            )

        if tier == SCORECARD_MEASUREMENT:
            scorecard.append(path)
        elif tier == SMOKE_MEASUREMENT:
            smoke.append(path)

    return MeasurementChanges(
        scorecard=tuple(sorted(scorecard)),
        smoke=tuple(sorted(smoke)),
    )


def relevant_changes(
    repository: Path,
    before_revision: str,
    current_revision: str,
) -> tuple[str, ...]:
    """Return performance inputs changed between two repository revisions."""
    return changed_measurement_inputs(
        repository,
        before_revision,
        current_revision,
    ).all


def event_measurement_tier(
    repository: Path,
    event_name: str,
    before_revision: str | None,
    current_revision: str,
) -> tuple[str, tuple[str, ...]]:
    """Resolve the measurement tier requested by the current event."""
    if event_name in {"schedule", "workflow_dispatch"}:
        return SCORECARD_MEASUREMENT, (f"<{event_name}>",)

    if event_name != "push":
        raise WorkflowStateError(
            f"Unsupported benchmark workflow event: {event_name}",
        )

    if not before_revision:
        raise WorkflowStateError("A push event requires its before revision.")

    changes = changed_measurement_inputs(
        repository,
        before_revision,
        current_revision,
    )

    return changes.tier, changes.all


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
                "target matrix.",
            )

        source_commits = {entry.get("commit") for entry in entries}
        if len(source_commits) != 1:
            raise WorkflowStateError(
                "The proposed target matrix does not share one source commit.",
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
    event_measurement: str,
    proposal: ProposalState,
) -> tuple[str, bool, bool]:
    """Return measurement, proposal-sync, and proposal-write decisions.

    Compare runs are immutable evidence. Only an explicit or automatically
    selected seed run may mutate the reviewed accepted-baseline proposal.
    Seed work upgrades a provider smoke request to a complete scorecard because
    a reviewed baseline can only be produced from the full target contract.
    """
    if baseline_mode not in {"compare", "seed"}:
        raise WorkflowStateError(
            f"Unsupported resolved baseline mode: {baseline_mode}",
        )
    if event_measurement not in {
        NO_MEASUREMENT,
        SMOKE_MEASUREMENT,
        SCORECARD_MEASUREMENT,
    }:
        raise WorkflowStateError(
            f"Unsupported event measurement tier: {event_measurement}",
        )

    if event_measurement == NO_MEASUREMENT:
        sync_required = (
            baseline_mode == "seed"
            and proposal.disposition == "current"
            and proposal.behind_current
        )
        return NO_MEASUREMENT, sync_required, False

    selected_measurement = event_measurement
    if baseline_mode == "seed":
        selected_measurement = SCORECARD_MEASUREMENT

    return selected_measurement, False, baseline_mode == "seed"


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

    event_measurement, event_changes = event_measurement_tier(
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
    selected_measurement, sync_required, proposal_required = decide_work(
        args.baseline_mode,
        event_measurement,
        proposal,
    )

    payload = {
        "baselineMode": args.baseline_mode,
        "comparisonMode": comparison_mode_for_baseline_mode(args.baseline_mode),
        "eventMeasurementTier": event_measurement,
        "eventRelevantChanges": list(event_changes),
        "proposal": {
            "disposition": proposal.disposition,
            "reason": proposal.reason,
            "sourceCommit": proposal.source_commit,
            "relevantChanges": list(proposal.relevant_changes),
            "behindCurrent": proposal.behind_current,
        },
        "measurementTier": selected_measurement,
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
