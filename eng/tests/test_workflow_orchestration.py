"""Cross-workflow orchestration contracts that a single-workflow lint cannot see.

actionlint already rejects unknown `needs:` targets and invalid
`needs.<job>.outputs.<key>` references inside one workflow, so this module
deliberately does not repeat that. What no linter checks is whether the
handoffs between workflows actually line up: an artifact consumed by name in
one workflow and produced by name in another, a cross-workflow dispatch that
names an input value the callee accepts, and the baseline rollover chain whose
breakage is what forces a manual release-candidate recovery.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_ROOT = REPOSITORY_ROOT / ".github" / "workflows"

UPLOAD_STEP = re.compile(r"uses: actions/upload-artifact@")
DOWNLOAD_STEP = re.compile(r"uses: actions/download-artifact@")
ARTIFACT_NAME = re.compile(r"^\s*name:\s*(?P<name>\S.*?)\s*$")
DISPATCH_CALL = re.compile(
    r"gh workflow run (?P<workflow>[\w.-]+)\b"
)
DISPATCH_FIELD = re.compile(r"--field (?P<key>[\w-]+)=(?P<value>[\w-]+)")
LIST_ITEM = re.compile(r"^\s*- (?P<option>[\w.-]+)\s*$")


def indent_of(line: str) -> int:
    """Return a line's leading-space count."""
    return len(line) - len(line.lstrip(" "))


def block_body(lines: list[str], start: int) -> list[tuple[int, str]]:
    """Return the (index, line) pairs nested under the mapping key at `start`."""
    parent = indent_of(lines[start])
    body: list[tuple[int, str]] = []

    for index in range(start + 1, len(lines)):
        line = lines[index]
        if not line.strip():
            continue
        if indent_of(line) <= parent:
            break
        body.append((index, line))

    return body


def find_key(lines: list[str], candidates: list[tuple[int, str]], key: str) -> int | None:
    """Return the index of `key` declared directly inside the given block."""
    if not candidates:
        return None

    own_indent = min(indent_of(line) for _, line in candidates)
    pattern = re.compile(rf"^ {{{own_indent}}}{re.escape(key)}:\s*(?:#.*)?$")

    for index, line in candidates:
        if pattern.match(line):
            return index

    return None


def dispatch_choice_options(text: str, input_name: str) -> set[str]:
    """Return the options declared for one workflow_dispatch choice input.

    Scoping matters: collecting every `- value` line in a workflow would accept
    a value belonging to an unrelated input, a branch filter, or a matrix leg.
    The walk therefore descends workflow_dispatch -> inputs -> <input_name> ->
    options and reads only that list.
    """
    lines = text.splitlines()

    dispatch_index = next(
        (
            index
            for index, line in enumerate(lines)
            if re.match(r"^\s*workflow_dispatch:\s*$", line)
        ),
        None,
    )
    if dispatch_index is None:
        return set()

    inputs_index = find_key(lines, block_body(lines, dispatch_index), "inputs")
    if inputs_index is None:
        return set()

    input_index = find_key(lines, block_body(lines, inputs_index), input_name)
    if input_index is None:
        return set()

    options_index = find_key(lines, block_body(lines, input_index), "options")
    if options_index is None:
        return set()

    options: set[str] = set()
    for _, line in block_body(lines, options_index):
        match = LIST_ITEM.match(line)
        if match is not None:
            options.add(match.group("option"))

    return options


def workflow_text(name: str) -> str:
    """Return one workflow's raw text."""
    return (WORKFLOW_ROOT / name).read_text(encoding="utf-8")


def artifact_names(text: str, step_pattern: re.Pattern[str]) -> set[str]:
    """Collect artifact names belonging to upload or download steps.

    The `name:` that follows the step's `uses:` within its `with:` block is the
    artifact identity. Names carrying a matrix or step expression are reduced to
    a template so a producer and a consumer that share one expression compare
    equal without evaluating GitHub's expression language.
    """
    names: set[str] = set()
    lines = text.splitlines()

    for index, line in enumerate(lines):
        if not step_pattern.search(line):
            continue

        for candidate in lines[index + 1:index + 8]:
            if candidate.strip().startswith("- name:") or candidate.strip() == "steps:":
                break
            match = ARTIFACT_NAME.match(candidate)
            if match is None:
                continue
            names.add(normalize(match.group("name")))
            break

    return names


def normalize(name: str) -> str:
    """Collapse every GitHub expression to a single placeholder token."""
    return re.sub(r"\$\{\{[^}]+\}\}", "<expr>", name).strip()


def names_can_match(left: str, right: str) -> bool:
    """Report whether two artifact names can denote the same artifact.

    One side often templates what the other spells out: a matrix job uploads
    `spec-tests-coverage-${{ matrix.engine.name }}` while the consumer names
    `spec-tests-coverage-mysql84`. A placeholder therefore matches any
    non-empty run of characters, and the comparison is made in both directions.
    """
    if left == right:
        return True

    def as_pattern(value: str) -> re.Pattern[str]:
        parts = [re.escape(part) for part in value.split("<expr>")]
        return re.compile("^" + ".+".join(parts) + "$")

    if "<expr>" in left and as_pattern(left).match(right):
        return True

    return "<expr>" in right and bool(as_pattern(right).match(left))


class ArtifactHandoffTests(unittest.TestCase):
    """Every consumed artifact must have a producer somewhere in the graph."""

    def test_every_downloaded_artifact_is_produced(self) -> None:
        """Reject a download whose name no upload in any workflow emits."""
        produced: set[str] = set()
        consumed: dict[str, set[str]] = {}

        for path in sorted(WORKFLOW_ROOT.glob("*.yml")):
            text = path.read_text(encoding="utf-8")
            produced |= artifact_names(text, UPLOAD_STEP)
            downloads = artifact_names(text, DOWNLOAD_STEP)
            if downloads:
                consumed[path.name] = downloads

        self.assertTrue(produced, "No upload steps were discovered at all.")
        self.assertTrue(consumed, "No download steps were discovered at all.")

        for workflow, names in consumed.items():
            for name in names:
                with self.subTest(workflow=workflow, artifact=name):
                    self.assertTrue(
                        any(names_can_match(name, candidate) for candidate in produced),
                        f"{workflow} downloads '{name}', which no workflow uploads.",
                    )

    def test_coverage_gate_downloads_one_artifact_per_specification_engine(self) -> None:
        """Bind the coverage inputs to the engines the matrix actually runs.

        The generic producer/consumer check above cannot catch a typo here: the
        producer's name is templated on the matrix value, so any string sharing
        its prefix satisfies it. Resolving the matrix makes the check exact.
        """
        text = workflow_text("ci.yml")

        engines = re.findall(r"- \{ name: (?P<engine>[\w-]+), target:", text)
        self.assertEqual(
            3,
            len(engines),
            "The specification matrix is expected to cover three engines.",
        )

        downloads = artifact_names(text, DOWNLOAD_STEP)
        for engine in engines:
            with self.subTest(engine=engine):
                self.assertIn(
                    f"spec-tests-coverage-{engine}",
                    downloads,
                    f"The coverage gate does not download the {engine} evidence "
                    "that the specification matrix produces.",
                )

        stale = {
            name
            for name in downloads
            if name.startswith("spec-tests-coverage-")
            and name != "spec-tests-coverage-<expr>"
            and name.removeprefix("spec-tests-coverage-") not in engines
        }
        self.assertEqual(
            set(),
            stale,
            "The coverage gate downloads specification evidence for an engine "
            "the matrix does not run.",
        )

    def test_release_candidate_consumes_the_scorecard_artifacts(self) -> None:
        """Pin the handoff that carries qualified performance evidence."""
        candidate = workflow_text("release-candidate.yml")
        scorecard = workflow_text("benchmark-scorecard.yml")

        for target in ("mysql84", "mariadb118"):
            with self.subTest(target=target):
                self.assertIn(
                    f"name: benchmark-artifacts-{target}",
                    scorecard,
                    "The scorecard must publish this target's qualified evidence.",
                )

        self.assertIn("benchmark-artifacts-${{ matrix.target }}", candidate)
        self.assertIn("uses: ./.github/workflows/benchmark-scorecard.yml", candidate)


class CrossWorkflowDispatchTests(unittest.TestCase):
    """A dispatched input value must be one the target workflow accepts."""

    def test_dispatched_profile_values_are_declared_by_the_callee(self) -> None:
        """Reject a dispatch naming a profile the target workflow cannot select."""
        dispatches: list[tuple[str, str, str, str]] = []

        for path in sorted(WORKFLOW_ROOT.glob("*.yml")):
            text = path.read_text(encoding="utf-8")
            for line in text.splitlines():
                call = DISPATCH_CALL.search(line)
                if call is not None:
                    dispatches.append((path.name, call.group("workflow"), "", ""))

            for index, line in enumerate(text.splitlines()):
                field = DISPATCH_FIELD.search(line)
                if field is None:
                    continue
                target = None
                for previous in reversed(text.splitlines()[:index + 1]):
                    call = DISPATCH_CALL.search(previous)
                    if call is not None:
                        target = call.group("workflow")
                        break
                self.assertIsNotNone(
                    target,
                    f"{path.name} passes --field without a preceding workflow run call.",
                )
                dispatches.append(
                    (path.name, str(target), field.group("key"), field.group("value"))
                )

        parameterized = [entry for entry in dispatches if entry[2]]
        self.assertTrue(
            parameterized,
            "The baseline automation is expected to dispatch a restricted profile.",
        )

        for caller, target, key, value in parameterized:
            with self.subTest(caller=caller, target=target, key=key, value=value):
                callee = workflow_text(target)
                options = dispatch_choice_options(callee, key)
                self.assertTrue(
                    options,
                    f"{target} declares no workflow_dispatch choice input "
                    f"'{key}' for {caller} to set.",
                )
                self.assertIn(
                    value,
                    options,
                    f"{caller} dispatches {key}={value}, which {target} does not "
                    f"offer. Declared options: {sorted(options)}.",
                )


class DispatchOptionScopingTests(unittest.TestCase):
    """The option extractor must not read values belonging to another key."""

    WORKFLOW = """name: example

on:
  workflow_dispatch:
    inputs:
      profile:
        description: Pick a profile.
        required: true
        type: choice
        options:
          - full
          - baseline-proposal
      engine:
        description: Pick an engine.
        required: true
        type: choice
        options:
          - mysql84
          - mariadb118

jobs:
  build:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        target:
          - unrelated-matrix-value
    steps:
      - run: echo build
"""

    def test_each_input_returns_only_its_own_options(self) -> None:
        """Keep two choice inputs from sharing one option pool."""
        self.assertEqual(
            {"full", "baseline-proposal"},
            dispatch_choice_options(self.WORKFLOW, "profile"),
        )
        self.assertEqual(
            {"mysql84", "mariadb118"},
            dispatch_choice_options(self.WORKFLOW, "engine"),
        )

    def test_unrelated_list_values_are_not_collected(self) -> None:
        """Reject matrix legs and branch filters as if they were input options."""
        for name in ("profile", "engine"):
            with self.subTest(input=name):
                self.assertNotIn(
                    "unrelated-matrix-value",
                    dispatch_choice_options(self.WORKFLOW, name),
                )

    def test_unknown_input_yields_no_options(self) -> None:
        """Report an absent input as empty rather than as a permissive match."""
        self.assertEqual(set(), dispatch_choice_options(self.WORKFLOW, "missing"))

    def test_real_workflow_exposes_only_the_profile_options(self) -> None:
        """Pin the live contract the dispatch test depends on."""
        self.assertEqual(
            {"full", "baseline-proposal"},
            dispatch_choice_options(workflow_text("ci.yml"), "profile"),
        )


class BaselineRolloverTests(unittest.TestCase):
    """The chain that a contract-version bump has to travel end to end."""

    def test_rollover_chain_is_wired(self) -> None:
        """Walk resolver, scorecard, proposal, and release readiness in order."""
        benchmark = workflow_text("benchmark.yml")
        candidate = workflow_text("release-candidate.yml")

        # A main push reaches the resolver, which decides what the run must do.
        self.assertIn("python3 -m eng.performance.cli resolve-baseline-mode", benchmark)
        self.assertIn("python3 -m eng.performance.workflow_state", benchmark)

        # The resolver's verdict gates the expensive measurement.
        self.assertIn(
            "if: needs.resolve-baseline-mode.outputs.scorecard-required == 'true'",
            benchmark,
        )
        self.assertIn("uses: ./.github/workflows/benchmark-scorecard.yml", benchmark)

        # Measurement results become a reviewed proposal, never a direct push
        # to the default branch.
        self.assertIn(
            "if: needs.resolve-baseline-mode.outputs.proposal-required == 'true'",
            benchmark,
        )
        self.assertIn("gh pr create", benchmark)
        self.assertNotIn("git push origin HEAD:refs/heads/main", benchmark)

        # The candidate refuses to qualify until an accepted baseline exists for
        # the active contract, which is what makes the rollover mandatory.
        self.assertIn(
            "python3 -m eng.release.evidence validate-performance-baseline",
            candidate,
        )
        self.assertIn("--contract benchmarks/performance-contract.json", candidate)

    def test_proposal_branch_name_is_derived_from_the_contract_version(self) -> None:
        """Keep one open proposal per contract version rather than per run."""
        benchmark = workflow_text("benchmark.yml")
        self.assertIn(
            'baseline_branch="automation/performance-baseline-${contract_version}"',
            benchmark,
        )

    def test_release_readiness_names_the_pending_rollover(self) -> None:
        """Require the candidate to explain a rollover, not just to fail.

        The contract and the accepted baseline legitimately diverge between a
        contract bump and the merge of the proposal that answers it. That state
        must block a release candidate, which the preflight already does, and
        it must tell the operator how to leave the state.
        """
        candidate = workflow_text("release-candidate.yml")

        self.assertIn("Hosted performance baseline required", candidate)
        for instruction in ("benchmark workflow", "merge", "release"):
            with self.subTest(instruction=instruction):
                self.assertIn(instruction, candidate)


if __name__ == "__main__":
    unittest.main()
