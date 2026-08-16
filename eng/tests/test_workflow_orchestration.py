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

import os
import re
import unittest
from pathlib import Path

from eng.release import trust


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

    def test_coverage_gate_downloads_one_artifact_per_specification_target(self) -> None:
        """Bind the coverage inputs to the targets the matrix actually runs.

        The generic producer/consumer check above cannot catch a typo here: the
        producer's name is templated on the matrix value, so any string sharing
        its prefix satisfies it. Resolving the matrix makes the check exact.
        """
        text = workflow_text("ci.yml")

        targets = re.findall(r"- \{ name: (?P<target>[\w-]+), target:", text)
        self.assertEqual(
            6,
            len(targets),
            "The specification matrix must cover every active LTS target.",
        )

        downloads = artifact_names(text, DOWNLOAD_STEP)
        for target in targets:
            with self.subTest(target=target):
                self.assertIn(
                    f"spec-tests-coverage-{target}",
                    downloads,
                    f"The coverage gate does not download the {target} evidence "
                    "that the specification matrix produces.",
                )

        stale = {
            name
            for name in downloads
            if name.startswith("spec-tests-coverage-")
            and name != "spec-tests-coverage-<expr>"
            and name.removeprefix("spec-tests-coverage-") not in targets
        }
        self.assertEqual(
            set(),
            stale,
            "The coverage gate downloads specification evidence for a target "
            "the matrix does not run.",
        )

    def test_release_candidate_does_not_consume_scorecard_artifacts(self) -> None:
        """Keep benchmark evidence independent from release qualification."""
        candidate = workflow_text("release-candidate.yml")
        scorecard = workflow_text("benchmark-scorecard.yml")
        target_workflow = workflow_text("benchmark-target.yml")

        self.assertIn(".requiredTargets | keys", scorecard)
        self.assertIn("target: ${{ matrix.target }}", scorecard)
        self.assertIn("name: benchmark-artifacts-${{ inputs.target }}", target_workflow)
        self.assertIn("qualify-paired-scorecard:", scorecard)
        self.assertIn("python3 -m eng.performance.cli qualify-scorecard", scorecard)
        self.assertIn("name: benchmark-scorecard-qualification", scorecard)
        self.assertIn(
            "name: benchmark-dispersion-${{ inputs.target }}-1",
            target_workflow,
        )
        self.assertIn(
            "name: benchmark-dispersion-${{ inputs.target }}-2",
            target_workflow,
        )
        self.assertEqual(2, target_workflow.count("retention-days: 90"))
        self.assertEqual(
            2,
            target_workflow.count("title=Benchmark dispersion drift"),
        )

        for excluded in (
            "performance-qualification",
            "benchmark-scorecard.yml",
            "benchmark-scorecard-qualification",
            "paired-scorecard-qualification.json",
        ):
            with self.subTest(excluded=excluded):
                self.assertNotIn(excluded, candidate)


class CompatibilityMatrixTests(unittest.TestCase):
    """The scheduled compatibility lane must enforce the advertised matrix."""

    def test_container_matrix_requires_every_active_lts_target(self) -> None:
        """Reject a partial scheduled run presented as full qualification."""
        text = workflow_text("container-matrix.yml")
        selection = re.search(
            r"DOKA_INTEGRATION_TARGETS:\s*>-\s*\n\s*(?P<targets>[^\n]+)",
            text,
        )

        self.assertIsNotNone(selection)
        self.assertEqual(
            {
                "mysql84",
                "mysql97",
                "mariadb1011",
                "mariadb114",
                "mariadb118",
                "mariadb123",
            },
            set(selection.group("targets").split(",")),
        )
        self.assertIn("DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX: 1", text)


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
        self.assertIn(
            "comparison_mode: "
            "${{ needs.resolve-baseline-mode.outputs.comparison-mode }}",
            benchmark,
        )

        # Measurement results become a reviewed proposal, never a direct push
        # to the default branch.
        self.assertIn(
            "if: needs.resolve-baseline-mode.outputs.proposal-required == 'true'",
            benchmark,
        )
        self.assertIn("gh pr create", benchmark)
        self.assertNotIn("git push origin HEAD:refs/heads/main", benchmark)

        # The chain ends on the default branch. The candidate does not consume
        # it, so an accepted baseline is not a release precondition.
        self.assertNotIn(
            "python3 -m eng.release.evidence validate-performance-baseline",
            candidate,
        )

    def test_proposal_branch_name_is_derived_from_the_contract_version(self) -> None:
        """Keep one open proposal per contract version rather than per run."""
        benchmark = workflow_text("benchmark.yml")
        self.assertIn(
            'baseline_branch="automation/performance-baseline-${contract_version}"',
            benchmark,
        )


class GateImplementationOwnershipTests(unittest.TestCase):
    """Prove every release-path gate has exactly one shared implementation.

    D-026 runs migration deployment, runtime posture, and both patch matrices
    against the tagged commit while their scheduled counterparts keep running
    on the default branch. Two callers of one gate are only safe while both
    reach the same script. A gate whose command lives inline in a workflow
    cannot be shared, so the tag would get a second copy -- the defect class
    that made the release coverage gate unsatisfiable while the branch gate
    passed on the same commit.
    """

    GATES = {
        "migration-deployment": "test-migration-deployment.sh",
        "runtime-posture": "test-runtime-posture.sh",
        "efcore-patch-matrix": "test-efcore-matrix.sh",
        "mysqlconnector-patch-matrix": "test-mysqlconnector-matrix.sh",
    }

    def job_body(self, workflow: str, job: str) -> list[str]:
        """Return the lines of one job, excluding the following job."""
        lines = (WORKFLOW_ROOT / workflow).read_text(encoding="utf-8").splitlines()
        start = next(
            index for index, line in enumerate(lines) if line.strip() == f"{job}:"
        )
        indent = len(lines[start]) - len(lines[start].lstrip())
        for index in range(start + 1, len(lines)):
            line = lines[index]
            if (
                line.strip()
                and line.strip().endswith(":")
                and (len(line) - len(line.lstrip())) == indent
            ):
                return lines[start:index]
        return lines[start:]

    def test_every_gate_delegates_to_its_shared_script(self) -> None:
        """Reject a gate whose command is written inline in the workflow."""
        for job, script in self.GATES.items():
            with self.subTest(gate=job):
                body = self.job_body("ci.yml", job)
                self.assertTrue(
                    any(script in line for line in body),
                    f"{job} does not call {script}",
                )

    def test_no_gate_carries_an_inline_dotnet_command(self) -> None:
        """Reject a gate that reintroduces its own build or test invocation.

        A shared script plus a leftover inline `dotnet test` is two
        implementations again, and only one of them would move to the tag.
        """
        for job in self.GATES:
            with self.subTest(gate=job):
                offenders = [
                    line.strip()
                    for line in self.job_body("ci.yml", job)
                    if re.search(r"\bdotnet\s+(test|restore|build|package)\b", line)
                ]
                self.assertEqual([], offenders)

    def test_every_gate_script_exists_and_is_executable(self) -> None:
        """Keep the shared entry points present and runnable."""
        for job, script in self.GATES.items():
            with self.subTest(gate=job):
                candidates = [
                    REPOSITORY_ROOT / "eng" / "testing" / script,
                    REPOSITORY_ROOT / "eng" / script,
                ]
                found = [path for path in candidates if path.is_file()]
                self.assertTrue(found, f"{script} is missing")
                self.assertTrue(
                    os.access(found[0], os.X_OK),
                    f"{found[0]} is not executable",
                )


class RequiredAggregatorTests(unittest.TestCase):
    """Prove the required status check cannot pass by omission.

    GitHub counts a conditionally skipped job as successful and skips the
    dependents of a failed one. A required check that merely listed the gates
    in `needs:` would therefore report success in exactly the situations it
    exists to catch, which is why this one runs with `always()` and inspects
    each dependency result explicitly.
    """

    AGGREGATOR = "repository-qualification"
    COMMIT_EXACT_GATES = (
        "quality-gates",
        "repo-tests",
        "spec-test-suite",
        "integration-smoke",
        "coverage-gate",
    )

    def setUp(self) -> None:
        """Read the aggregator job body once per case."""
        lines = (WORKFLOW_ROOT / "ci.yml").read_text(encoding="utf-8").splitlines()
        start = next(
            index
            for index, line in enumerate(lines)
            if line.strip() == f"{self.AGGREGATOR}:"
        )
        indent = len(lines[start]) - len(lines[start].lstrip())
        end = len(lines)
        for index in range(start + 1, len(lines)):
            line = lines[index]
            if (
                line.strip()
                and line.strip().endswith(":")
                and (len(line) - len(line.lstrip())) == indent
            ):
                end = index
                break
        self.body = lines[start:end]

    def test_the_aggregator_runs_even_when_a_dependency_failed(self) -> None:
        """Require `always()` so a failed gate does not skip the check."""
        self.assertIn(
            "always()",
            "\n".join(self.body),
            "the required aggregator must run with always()",
        )

    def test_the_aggregator_only_skips_where_evidence_is_unusable(self) -> None:
        """Bound every skip condition to a run that can never be imported.

        A skipped job counts as success to branch protection, so a skip is a
        silent pass. The one legitimate skip is the baseline-proposal profile,
        which runs none of the gates -- and it is reachable only by dispatch,
        which the release trust root refuses as branch evidence. Any other skip
        condition would hand branch protection a pass nobody checked.
        """
        condition = "\n".join(self.body[: self.body.index("    needs:")])

        if "baseline-proposal" not in condition:
            return

        self.assertIn("github.event_name != 'workflow_dispatch'", condition)

        # The skip is only safe because the other half of the pair holds. This
        # executes that half rather than describing it.
        with self.assertRaises(trust.TrustRootError):
            trust.verify_branch_evidence_origin(
                {
                    "conclusion": "success",
                    "event": "workflow_dispatch",
                    "headBranch": "main",
                    "commit": "a" * 40,
                    "workflowPath": ".github/workflows/ci.yml",
                    "workflowRunId": 1,
                    "runAttempt": 1,
                },
                commit="a" * 40,
                expected_branch="main",
                expected_workflow=".github/workflows/ci.yml",
            )

    def test_every_commit_exact_gate_is_a_dependency(self) -> None:
        """Keep the aggregator's coverage equal to the commit-exact set."""
        for gate in self.COMMIT_EXACT_GATES:
            with self.subTest(gate=gate):
                self.assertTrue(
                    any(line.strip() == f"- {gate}" for line in self.body),
                    f"{gate} is not a dependency of the aggregator",
                )

    def test_every_dependency_result_is_inspected(self) -> None:
        """Reject an aggregator that depends on a gate without reading it.

        A dependency that is never inspected contributes nothing: its failure
        would be invisible to the check that is supposed to gate on it.
        """
        body = "\n".join(self.body)
        for gate in self.COMMIT_EXACT_GATES:
            with self.subTest(gate=gate):
                self.assertIn(f"needs.{gate}.result", body)

    def test_the_aggregator_does_not_depend_on_full_profile_jobs(self) -> None:
        """Keep jobs that deliberately do not run per event out of the check.

        Migration deployment, runtime posture, and the patch matrices run on a
        schedule. Requiring them here would make the check unsatisfiable on
        every pull request.
        """
        body = "\n".join(self.body)
        for gate in ("migration-deployment", "runtime-posture",
                     "efcore-patch-matrix", "mysqlconnector-patch-matrix",
                     "benchmark-smoke"):
            with self.subTest(gate=gate):
                self.assertNotIn(f"- {gate}", body)


class ReleaseCandidateShapeTests(unittest.TestCase):
    """Prove the tag workflow runs only what a tag must decide for itself.

    Repeating a gate that already ran on the default branch is what produced a
    release coverage gate that merged two of five inputs while the branch gate
    merged all five and passed on the same commit. The job set below is the
    structural answer: imported evidence is verified, never re-measured.
    """

    WORKFLOW = "release-candidate.yml"

    def setUp(self) -> None:
        """Read the workflow once per case."""
        self.text = (WORKFLOW_ROOT / self.WORKFLOW).read_text(encoding="utf-8")
        self.stages = re.findall(r"^\s+- stage: ([a-z-]+)$", self.text, re.M)

    def test_a_signed_tag_push_starts_the_workflow(self) -> None:
        """Require the tag push trigger the release procedure depends on."""
        self.assertRegex(self.text, r"on:\n(?:.*\n)*?  push:\n    tags:\n      - \"v\*\"")

    def test_the_trust_root_runs_before_any_expensive_step(self) -> None:
        """Reject a workflow that spends runner time before verifying the tag."""
        trust = self.text.index("eng.release.trust")
        for later in ("release-candidate.sh --stage",):
            with self.subTest(step=later):
                self.assertLess(trust, self.text.index(later))

    def test_imported_gates_are_not_repeated_at_the_tag(self) -> None:
        """Keep commit-exact branch gates out of the tag workflow.

        Each of these has one implementation that already runs on the default
        branch. A second execution here is a second implementation in waiting.
        """
        for imported in ("quality", "repository-tests", "specification",
                         "integration", "coverage"):
            with self.subTest(stage=imported):
                self.assertNotIn(imported, self.stages)

    def test_every_tag_produced_gate_is_present(self) -> None:
        """Require the gates whose evidence only the tagged commit can produce."""
        for produced in ("package", "migration-deployment", "runtime",
                         "efcore-patch-matrix", "mysqlconnector-patch-matrix"):
            with self.subTest(stage=produced):
                self.assertIn(produced, self.stages)

    def test_performance_has_no_release_authority(self) -> None:
        """Ensure benchmark execution and evidence cannot block a tag."""
        for excluded in (
            "comparison_mode:",
            "baseline_mode:",
            "benchmark-scorecard.yml",
            "performance-qualification",
            "benchmark-artifacts-",
        ):
            with self.subTest(excluded=excluded):
                self.assertNotIn(excluded, self.text)

    def test_no_historical_baseline_gate_remains(self) -> None:
        """Reject a leftover import of the historical comparison result."""
        for removed in (
            "performance-import",
            "performance-scorecard",
            "performance-qualification",
        ):
            with self.subTest(job=removed):
                self.assertNotRegex(self.text, rf"^  {removed}:$")


class PairedProviderBindingTests(unittest.TestCase):
    """Prove one benchmark driver can measure two provider revisions.

    A paired comparison is only about the provider if both sides are measured
    by the same benchmark driver. With a project reference alone, building the reference
    side from its own commit would rebuild the benchmark driver too, and the comparison
    would silently be between benchmark driver-and-provider pairs. The benchmark project
    therefore has to accept a packaged provider without changing anything for
    the ordinary build.
    """

    PROJECT = (
        REPOSITORY_ROOT
        / "benchmarks"
        / "Doka.EntityFrameworkCore.MySql.Benchmarks"
        / "Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
    )

    def setUp(self) -> None:
        """Read the benchmark project file once per case."""
        self.text = self.PROJECT.read_text(encoding="utf-8")

    def test_the_default_build_keeps_the_project_reference(self) -> None:
        """Leave the ordinary benchmark build exactly as it was."""
        self.assertIn(
            "Condition=\"'$(DokaBenchmarkUsesPackagedProvider)' == 'false'\"",
            self.text,
        )
        self.assertIn(
            "src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj",
            self.text,
        )

    def test_a_provider_version_switches_to_the_packaged_reference(self) -> None:
        """Bind the same benchmark driver to a packaged provider when asked."""
        self.assertIn(
            "Condition=\"'$(DokaBenchmarkUsesPackagedProvider)' == 'true'\"",
            self.text,
        )
        # VersionOverride, not Version: this repository uses Central Package
        # Management, under which a PackageReference carrying a Version fails
        # restore with NU1008. Only the reference side takes this path, so the
        # ordinary build stayed green while every paired run died publishing
        # its reference driver.
        self.assertIn(
            'VersionOverride="$(DokaBenchmarkProviderVersion)"', self.text
        )
        self.assertNotIn('Version="$(DokaBenchmarkProviderVersion)"', self.text)

    def test_both_provider_packages_switch_together(self) -> None:
        """Keep the provider and its spatial companion on one revision.

        Mixing a packaged provider with a project-referenced companion would
        measure two revisions at once and attribute the difference to neither.
        """
        packaged = self.text.split("== 'true'")[1]
        for package in ("Doka.EntityFrameworkCore.MySql",
                        "Doka.EntityFrameworkCore.MySql.NetTopologySuite"):
            with self.subTest(package=package):
                self.assertIn(f'Include="{package}"', packaged)


if __name__ == "__main__":
    unittest.main()
