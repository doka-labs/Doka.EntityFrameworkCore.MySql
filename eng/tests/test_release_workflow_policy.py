"""Regression tests for hosted release-workflow security boundaries."""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


class ReleaseWorkflowPolicyTests(unittest.TestCase):
    """Keep hosted release jobs on reviewed identity and permission contracts."""

    def setUp(self) -> None:
        """Resolve the repository workflow directory."""
        self.repo = Path(__file__).resolve().parents[2]
        self.workflows = self.repo / ".github" / "workflows"

    def workflow(self, name: str) -> str:
        """Load one workflow as text for indentation-sensitive policy checks."""
        return (self.workflows / name).read_text(encoding="utf-8")

    @staticmethod
    def duplicate_mapping_keys(workflow: str) -> list[str]:
        """Find duplicate keys without interpreting shell block scalars as YAML."""
        key_pattern = re.compile(
            r"^(?P<indent> *)(?P<sequence>- )?"
            r"(?P<key>[A-Za-z0-9_.-]+):(?:\s|$)",
        )
        block_scalar_pattern = re.compile(r":\s*[|>][+-]?\s*(?:#.*)?$")
        scopes: list[tuple[int, dict[str, int]]] = []
        duplicates: list[str] = []
        block_parent_indent: int | None = None
        block_content_indent: int | None = None

        for line_number, line in enumerate(workflow.splitlines(), start=1):
            if not line.strip():
                continue

            physical_indent = len(line) - len(line.lstrip(" "))
            if block_parent_indent is not None:
                if block_content_indent is None:
                    if physical_indent > block_parent_indent:
                        block_content_indent = physical_indent
                        continue
                elif physical_indent >= block_content_indent:
                    continue

                block_parent_indent = None
                block_content_indent = None

            match = key_pattern.match(line)
            if match is None:
                continue

            is_sequence_entry = match.group("sequence") is not None
            logical_indent = physical_indent + (2 if is_sequence_entry else 0)
            if is_sequence_entry:
                while scopes and scopes[-1][0] >= logical_indent:
                    scopes.pop()
            else:
                while scopes and scopes[-1][0] > logical_indent:
                    scopes.pop()

            if not scopes or scopes[-1][0] < logical_indent:
                scopes.append((logical_indent, {}))

            key = match.group("key")
            first_line = scopes[-1][1].get(key)
            if first_line is not None:
                duplicates.append(
                    f"{key!r} at lines {first_line} and {line_number}",
                )
            else:
                scopes[-1][1][key] = line_number

            if block_scalar_pattern.search(line):
                block_parent_indent = logical_indent

        return duplicates

    @staticmethod
    def job(workflow: str, name: str, next_name: str | None = None) -> str:
        """Slice one top-level job without requiring a YAML parser dependency."""
        start = workflow.index(f"  {name}:\n")
        if next_name is None:
            return workflow[start:]

        end = workflow.index(f"  {next_name}:\n", start)
        return workflow[start:end]

    def test_hosted_workflows_do_not_select_moving_sdk_channels(self) -> None:
        """Require setup-dotnet to consume the exact repository SDK contract."""
        for path in sorted(self.workflows.glob("*.yml")):
            text = path.read_text(encoding="utf-8")

            if "actions/setup-dotnet@" not in text:
                continue

            with self.subTest(workflow=path.name):
                self.assertNotIn("dotnet-version:", text)
                self.assertIn("global-json-file: global.json", text)

    def test_nuget_signature_verification_cannot_be_masked_by_a_pipeline(self) -> None:
        """Retain verifier output only after its direct exit status succeeds."""
        workflow = self.workflow("nuget-publish.yml")
        verification_start = workflow.index(
            "      - name: Verify public NuGet repository signatures\n"
        )
        verification_end = workflow.index(
            "      - name: Verify fresh consumer restore and runtime\n",
            verification_start,
        )
        verification = workflow[verification_start:verification_end]

        self.assertIn("dotnet nuget verify \\", verification)
        self.assertIn("--all \\", verification)
        self.assertIn("nuget-signature-verification.txt", verification)
        self.assertNotIn("| tee", verification)

    def test_named_workflow_steps_have_exactly_one_execution_binding(self) -> None:
        """Reject empty steps and duplicate bindings hidden by YAML loaders."""
        for path in sorted(self.workflows.glob("*.yml")):
            text = path.read_text(encoding="utf-8")

            for index, step in enumerate(
                text.split("\n      - name: ")[1:],
                start=1,
            ):
                step = step.split("\n      - name: ", 1)[0]
                step_name = step.splitlines()[0].strip()
                execution_bindings = [
                    line
                    for line in step.splitlines()
                    if line.startswith(("        uses: ", "        run: "))
                ]

                with self.subTest(
                    workflow=path.name,
                    step=index,
                    name=step_name,
                ):
                    self.assertEqual(1, len(execution_bindings))

    def test_workflows_do_not_repeat_mapping_keys(self) -> None:
        """Reject duplicate YAML keys before a permissive loader overwrites one."""
        for path in sorted(self.workflows.glob("*.yml")):
            duplicates = self.duplicate_mapping_keys(
                path.read_text(encoding="utf-8"),
            )

            with self.subTest(workflow=path.name):
                self.assertEqual([], duplicates)

    def test_workflows_do_not_contain_top_level_sequence_entries(self) -> None:
        """Reject shell text that accidentally escapes a YAML block scalar."""
        for path in sorted(self.workflows.glob("*.yml")):
            text = path.read_text(encoding="utf-8")

            with self.subTest(workflow=path.name):
                self.assertFalse(
                    any(line.startswith("- ") for line in text.splitlines()),
                )

    def test_scorecard_publishes_only_stable_latest_main_evidence(self) -> None:
        """Keep Scorecard on supported triggers and supersede stale scans."""
        text = self.workflow("scorecard.yml")
        triggers = text[text.index("on:\n") : text.index("\npermissions:")]
        analysis = self.job(text, "analysis")

        self.assertEqual(
            'on:\n  push:\n    branches:\n      - main\n  schedule:\n'
            '    - cron: "45 3 * * 1"\n',
            triggers,
        )
        self.assertNotIn("branch_protection_rule", text)
        self.assertNotIn("workflow_dispatch", text)
        self.assertIn("group: ${{ github.workflow }}-main", text)
        self.assertIn("cancel-in-progress: true", text)
        self.assertIn("security-events: write", analysis)
        self.assertIn("id-token: write", analysis)
        self.assertIn("publish_results: true", analysis)
        self.assertIn("if-no-files-found: error", analysis)
        self.assertLess(
            analysis.index("- name: Upload result artifact"),
            analysis.index("- name: Upload result to code scanning"),
        )

    def test_dependency_review_waits_for_complete_automatic_snapshots(
        self,
    ) -> None:
        """Separate exact producer completion from graph propagation."""
        text = self.workflow("dependency-review.yml")
        review = self.job(text, "dependency-review")
        triggers = text[text.index("on:\n") : text.index("\npermissions:")]

        self.assertEqual("on:\n  pull_request:\n", triggers)
        self.assertIn(
            "group: ${{ github.workflow }}-${{ github.event.pull_request.number }}",
            text,
        )
        self.assertIn("cancel-in-progress: true", text)
        self.assertNotIn("dependency-submission:", text)
        self.assertIn("contents: read", review)
        self.assertIn("checks: read", review)
        self.assertNotIn("contents: write", review)
        self.assertIn("timeout-minutes: 30", review)
        self.assertIn(
            "uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1",
            review,
        )
        trusted_condition = (
            "github.event.pull_request.head.repo.full_name == "
            "github.repository\n"
            "          && github.event.pull_request.user.login "
            "!= 'dependabot[bot]'"
        )
        self.assertEqual(2, review.count(trusted_condition))
        self.assertIn(
            "python3 -m eng.quality.dependency_snapshot_readiness verify",
            review,
        )
        self.assertIn('--base-revision "${DOKA_BASE_REVISION}"', review)
        self.assertIn('--head-revision "${DOKA_HEAD_REVISION}"', review)
        self.assertEqual(1, review.count("--producer-wait-seconds 300"))
        self.assertEqual(1, review.count("--propagation-wait-seconds 900"))
        self.assertNotIn("--wait-seconds", review)
        self.assertIn(".automaticSubmission.base.checkId", review)
        self.assertIn(".automaticSubmission.head.checkId", review)
        self.assertIn(".observedWaitSeconds.producer", review)
        self.assertIn(".observedWaitSeconds.propagation", review)
        self.assertEqual(1, review.count("show-openssf-scorecard:"))
        self.assertIn("show-openssf-scorecard: true", review)
        self.assertNotIn("show-openssf-scorecard: false", review)
        self.assertIn("retry-on-snapshot-warnings: true", review)
        self.assertIn("retry-on-snapshot-warnings-timeout: 180", review)
        self.assertNotIn("resolve-mode", review)
        self.assertNotIn("restore-dependency-snapshot", review)
        self.assertNotIn("eng.quality.dependency_snapshot ", review)

    def test_benchmark_resolves_baseline_before_allocating_the_matrix(self) -> None:
        """Resolve compatibility and duplicate proposals before costly runs."""
        text = self.workflow("benchmark.yml")
        resolver = self.job(
            text,
            "resolve-baseline-mode",
            "sync-baseline-proposal",
        )
        sync = self.job(
            text,
            "sync-baseline-proposal",
            "benchmark-scorecard",
        )
        scorecard = self.job(
            text,
            "benchmark-scorecard",
            "propose-baseline-update",
        )
        proposal = self.job(
            text,
            "propose-baseline-update",
            "ensure-baseline-auto-merge",
        )

        self.assertIn("baseline_mode:", text)
        self.assertIn(
            "REQUESTED_MODE: ${{ inputs.baseline_mode || 'auto' }}",
            resolver,
        )
        self.assertIn('--requested-mode "${REQUESTED_MODE}"', resolver)
        self.assertIn("resolve-baseline-mode", resolver)
        self.assertIn("python3 -m eng.performance.workflow_state", resolver)
        self.assertIn("scorecard-required", resolver)
        self.assertIn("sync-required", resolver)
        self.assertIn("proposal-required", resolver)
        self.assertIn(
            "needs.resolve-baseline-mode.outputs.sync-required == 'true'",
            sync,
        )
        self.assertIn("needs: resolve-baseline-mode", scorecard)
        self.assertIn(
            "needs.resolve-baseline-mode.outputs.scorecard-required == 'true'",
            scorecard,
        )
        self.assertNotIn("github.event_name != 'push'", scorecard)
        self.assertIn(
            "uses: ./.github/workflows/benchmark-scorecard.yml",
            scorecard,
        )
        self.assertNotIn("baseline_mode:", scorecard)
        self.assertIn(
            "needs.resolve-baseline-mode.outputs.proposal-required == 'true'",
            proposal,
        )
        self.assertNotIn("github.event_name != 'push'", proposal)
        self.assertIn("- benchmark-scorecard", proposal)
        self.assertIn("pattern: benchmark-artifacts-*", proposal)
        self.assertIn(".requiredTargets | keys[]", proposal)
        self.assertIn("python3 -m eng.performance.cli seed", proposal)
        self.assertNotIn("python3 -m eng.performance.cli promote", proposal)
        self.assertIn("validate-baseline", proposal)
        self.assertIn("compare-baselines", proposal)
        self.assertIn(
            "steps.candidate.outputs.baseline-changed == 'true'",
            proposal,
        )
        self.assertIn(
            "steps.candidate.outputs.baseline-changed != 'true'",
            proposal,
        )

    def test_baseline_proposal_has_bounded_write_authority(self) -> None:
        """Pin the App boundary and Actor shape observed on baseline PR 42."""
        text = self.workflow("benchmark.yml")
        resolver = self.job(
            text,
            "resolve-baseline-mode",
            "sync-baseline-proposal",
        )
        sync = self.job(
            text,
            "sync-baseline-proposal",
            "benchmark-scorecard",
        )
        scorecard = self.job(
            text,
            "benchmark-scorecard",
            "propose-baseline-update",
        )
        proposal = self.job(
            text,
            "propose-baseline-update",
            "ensure-baseline-auto-merge",
        )
        auto_merge = self.job(text, "ensure-baseline-auto-merge")

        job_contents_write = re.compile(
            r"^      contents: write$",
            re.MULTILINE,
        )
        job_pull_requests_write = re.compile(
            r"^      pull-requests: write$",
            re.MULTILINE,
        )
        self.assertIsNone(job_contents_write.search(resolver))
        self.assertIsNone(job_contents_write.search(scorecard))
        self.assertIsNone(job_contents_write.search(auto_merge))
        self.assertIsNone(job_pull_requests_write.search(resolver))
        self.assertIsNone(job_pull_requests_write.search(sync))
        self.assertIsNone(job_pull_requests_write.search(scorecard))
        self.assertIsNone(job_pull_requests_write.search(auto_merge))
        self.assertNotIn("actions: write", resolver)
        self.assertNotIn("actions: write", scorecard)
        self.assertNotIn("actions: write", auto_merge)
        self.assertIn("actions: write", sync)
        self.assertIn("actions: write", proposal)
        self.assertEqual(2, text.count("actions: write"))
        self.assertEqual(
            2,
            len(job_contents_write.findall(text)),
        )
        self.assertEqual(
            1,
            len(job_pull_requests_write.findall(text)),
        )
        self.assertIn("contents: write", sync)
        self.assertIn("gh pr create", proposal)
        self.assertIn("gh api", proposal)
        self.assertNotIn("gh pr edit", proposal)
        self.assertEqual(2, text.count("gh workflow run ci.yml"))
        for update_job in (sync, proposal):
            self.assertIn("gh workflow run ci.yml", update_job)
            self.assertIn('--ref "${BASELINE_BRANCH}"', update_job)
            self.assertIn("--field profile=baseline-proposal", update_job)
        self.assertEqual(
            2,
            text.count(
                "uses: actions/create-github-app-token@"
                "bcd2ba49218906704ab6c1aa796996da409d3eb1 # v3"
            ),
        )
        app_contract = (
            "client-id: ${{ vars.DOKA_AUTOMATION_APP_CLIENT_ID }}",
            "private-key: "
            "${{ secrets.DOKA_AUTOMATION_APP_PRIVATE_KEY }}",
            "owner: ${{ github.repository_owner }}",
            "repositories: ${{ github.event.repository.name }}",
            "permission-contents: write",
            "permission-pull-requests: write",
        )
        for entry in app_contract:
            self.assertEqual(2, text.count(entry), entry)
            self.assertIn(entry, resolver)
            self.assertIn(entry, auto_merge)
            self.assertNotIn(entry, sync)
            self.assertNotIn(entry, proposal)
            self.assertNotIn(entry, scorecard)
        self.assertIn(
            "BASELINE_AUTOMATION_LOGIN: app/"
            "${{ steps.baseline-automation-token.outputs.app-slug }}",
            auto_merge,
        )
        self.assertIn(
            "steps.resolve.outputs.sync-required == 'true'",
            resolver,
        )
        self.assertIn(
            "steps.resolve.outputs.proposal-required == 'true'",
            resolver,
        )
        self.assertIn(
            "needs.sync-baseline-proposal.result == 'success'",
            auto_merge,
        )
        self.assertIn(
            "needs.propose-baseline-update.outputs.baseline-changed "
            "== 'true'",
            auto_merge,
        )
        self.assertIn(
            "baseline-changed: "
            "${{ steps.candidate.outputs.baseline-changed }}",
            proposal,
        )
        self.assertNotIn("baseline-automation-token", sync)
        self.assertNotIn("baseline-automation-token", proposal)
        self.assertNotIn(
            "token: ${{ steps.baseline-automation-token.outputs.token }}",
            text,
        )
        self.assertEqual(
            2,
            auto_merge.count(
                'GH_TOKEN="${BASELINE_AUTOMATION_TOKEN}" gh pr merge'
            ),
        )
        self.assertIn("--disable-auto", auto_merge)
        self.assertEqual(1, auto_merge.count("--auto"))
        self.assertIn("--squash", auto_merge)
        self.assertIn("--match-head-commit", auto_merge)
        self.assertEqual(2, auto_merge.count("--json autoMergeRequest"))
        self.assertIn("--json autoMergeRequest,headRefOid", auto_merge)
        self.assertIn("GH_TOKEN: ${{ github.token }}", auto_merge)
        self.assertIn(
            '"${auto_merge_actor}" == "app/github-actions"',
            auto_merge,
        )
        self.assertIn(
            "Remove this transition branch after the first App-owned "
            "baseline",
            auto_merge,
        )
        self.assertNotIn('"github-actions[bot]"', auto_merge)
        self.assertIn(
            "--json autoMergeRequest,mergedBy,state",
            auto_merge,
        )
        self.assertIn(
            '"${auto_merge_actor}" != "${BASELINE_AUTOMATION_LOGIN}"',
            auto_merge,
        )
        self.assertIn(
            '"${proposal_state}" == "OPEN"',
            auto_merge,
        )
        self.assertIn(
            '"${proposal_state}" == "MERGED"',
            auto_merge,
        )
        self.assertIn(
            "The App-owned auto-merge registration could not be verified",
            auto_merge,
        )
        self.assertIn(
            "The immediate baseline merge was not performed by the App",
            auto_merge,
        )
        self.assertIn(
            "existing auto-merge actor could not be verified",
            auto_merge,
        )
        self.assertIn(
            "Acceptance: maintainer approval and protected checks",
            auto_merge,
        )
        self.assertNotIn("gh pr merge", proposal)
        self.assertNotIn("gh pr merge", sync)
        self.assertNotIn("--admin", auto_merge)
        self.assertNotIn("gh pr review", text)
        self.assertNotIn("--force", auto_merge)
        self.assertEqual(2, text.count("secrets."))

    def test_baseline_runbook_closes_review_and_actor_transitions(self) -> None:
        """Keep approval renewal and legacy-Actor retirement actionable."""
        runbook = " ".join(
            (
                self.repo / "docs" / "operations" / "performance-evidence.md"
            )
            .read_text(encoding="utf-8")
            .split()
        )
        decision = " ".join(
            (
                self.repo
                / "docs"
                / "decisions"
                / "D-019-performance-gate-architecture.md"
            )
            .read_text(encoding="utf-8")
            .split()
        )

        for required in (
            "dismisses an approval",
            "most recent reviewable push",
            "approve that revision again",
            "keeps the existing auto-merge request active",
            "no workflow rerun, artifact download, or manual merge",
        ):
            with self.subTest(runbook_contract=required):
                self.assertIn(required, runbook)

        for required in (
            "transition mechanism, not a permanent compatibility surface",
            "first baseline proposal registered by the dedicated App",
            "no open baseline proposal whose Actor is",
            "must cite that pull request and its workflow run",
        ):
            with self.subTest(removal_contract=required):
                self.assertIn(required, decision)

    @staticmethod
    def jobs(workflow: str) -> dict[str, str]:
        """Split one workflow into its top-level jobs.

        The existing `job` helper needs the next job's name, which makes it
        unusable for a check that must cover every job including ones added
        later. This splits on the job indentation instead.
        """
        body = workflow[workflow.index("\njobs:") :]
        found: dict[str, str] = {}
        current: str | None = None
        collected: list[str] = []
        for line in body.splitlines():
            match = re.match(r"^  ([a-z][a-z0-9-]*):\s*$", line)
            if match:
                if current is not None:
                    found[current] = "\n".join(collected)
                current = match.group(1)
                collected = []
                continue
            if current is not None:
                collected.append(line)
        if current is not None:
            found[current] = "\n".join(collected)

        return found

    def test_every_job_reaching_the_actions_api_declares_actions_read(self) -> None:
        """Bind the permission to the code that needs it, not to one job.

        An explicit `permissions` block inherits nothing, so a job that calls an
        Actions resource without declaring `actions: read` is refused by the API
        at its first request. The trust root is the case that shipped that way:
        it resolves a check run down to its workflow run, which is an Actions
        resource, and the job that runs it declared only `contents: read`.
        """
        reaching = {
            "eng.release.trust",
            "eng.release.gate_results",
            "eng/release/restore-release-stage-artifacts.sh",
            "actions/runs",
            "gh api",
        }

        for name in ("release-candidate.yml", "nuget-publish.yml"):
            workflow = self.workflow(name)
            for job, body in self.jobs(workflow).items():
                if not any(marker in body for marker in reaching):
                    continue
                with self.subTest(workflow=name, job=job):
                    self.assertIn(
                        "actions: read",
                        body,
                        f"{job} reaches the Actions API without declaring "
                        "actions: read",
                    )

    def test_benchmark_measurement_isolated_from_the_control_plane(self) -> None:
        """Keep orchestration edits from allocating service-container jobs."""
        control_plane = self.workflow("benchmark.yml")
        scorecard_workflow = self.workflow("benchmark-scorecard.yml")
        target_workflow = self.workflow("benchmark-target.yml")
        scorecard_call = self.job(
            control_plane,
            "benchmark-scorecard",
            "propose-baseline-update",
        )

        self.assertIn("  workflow_call:\n", scorecard_workflow)
        self.assertNotIn("      baseline_mode:", scorecard_workflow)
        comparison_input = scorecard_workflow[
            scorecard_workflow.index(
                "      comparison_mode:"
            ) : scorecard_workflow.index("\n\npermissions:")
        ]
        self.assertIn("required: true", comparison_input)
        self.assertNotIn("default:", comparison_input)
        self.assertIn(
            "uses: ./.github/workflows/benchmark-scorecard.yml",
            scorecard_call,
        )
        self.assertIn(
            "comparison_mode: "
            "${{ needs.resolve-baseline-mode.outputs.comparison-mode }}",
            scorecard_call,
        )
        self.assertNotIn("runs-on:", scorecard_call)
        self.assertNotIn("services:", control_plane)
        self.assertNotIn("bash ./eng/benchmark.sh --test-only", control_plane)
        self.assertIn(".requiredTargets | keys", scorecard_workflow)
        self.assertIn("target: ${{ matrix.target }}", scorecard_workflow)
        self.assertNotIn("services:", target_workflow)
        baseline_mode_binding = (
            "DOKA_BENCHMARK_BASELINE_MODE: "
            "${{ inputs.comparison_mode == 'historical' "
            "&& 'seed' || 'compare' }}"
        )
        self.assertEqual(
            2,
            target_workflow.count(baseline_mode_binding),
        )
        self.assertEqual(
            2,
            target_workflow.count(
                '--comparison-mode "${COMPARISON_MODE}"',
            ),
        )
        self.assertIn(
            "bash ./eng/benchmark.sh --up-run-down",
            target_workflow,
        )
        self.assertNotIn("image:", target_workflow)
        # The retry decision is read from the attempt receipt. Comparing
        # against a state name here is what silently disabled the retry: the
        # vocabulary moved to `measurement-inconclusive` and the condition,
        # still matching on `inconclusive`, simply stopped firing.
        self.assertEqual(1, target_workflow.count("outputs.retry == 'true'"))
        self.assertNotIn("outputs.status ==", target_workflow)
        self.assertEqual(1, target_workflow.count("if: always()"))
        # Each attempt publishes its short-lived raw evidence and one small,
        # long-lived drift observation; selection publishes the chosen target.
        self.assertEqual(5, target_workflow.count("actions/upload-artifact@"))
        self.assertIn("benchmark-artifacts-${{ inputs.target }}", target_workflow)

    def test_scheduled_smoke_derives_every_target_from_the_contract(self) -> None:
        """Keep the short smoke path aligned with every supported LTS target."""
        ci = self.workflow("ci.yml")
        smoke = self.workflow("benchmark-smoke.yml")
        contract = json.loads(
            (self.repo / "benchmarks" / "performance-contract.json").read_text(
                encoding="utf-8",
            )
        )

        self.assertIn("uses: ./.github/workflows/benchmark-smoke.yml", ci)
        self.assertIn("  workflow_call:\n", smoke)
        self.assertIn(".requiredTargets | keys", smoke)
        self.assertEqual(
            1,
            smoke.count(
                "target: ${{ fromJSON(needs.resolve-targets.outputs.targets) }}",
            ),
        )
        self.assertEqual(1, smoke.count("DOKA_BENCHMARK_PROFILE: smoke"))
        self.assertEqual(
            1,
            smoke.count("DOKA_BENCHMARK_BASELINE_MODE: compare"),
        )
        self.assertEqual(
            1,
            smoke.count("DOKA_BENCHMARK_COMPARISON_MODE: historical"),
        )
        self.assertIn("bash ./eng/benchmark.sh --up-run-down", smoke)
        self.assertNotIn("services:", smoke)
        for target in contract["requiredTargets"]:
            with self.subTest(target=target):
                self.assertNotIn(target, smoke)

    def test_direct_candidate_performance_is_explicitly_paired(self) -> None:
        """Keep local release qualification on the same verdict as hosted CI."""
        script = (self.repo / "eng" / "release" / "release-candidate.sh").read_text(
            encoding="utf-8"
        )
        function_start = script.index("run_performance_engine() {")
        function_end = script.index(
            "\nrequired_performance_targets() {",
            function_start,
        )
        function = script[function_start:function_end]

        self.assertEqual(
            1,
            function.count("DOKA_BENCHMARK_BASELINE_MODE=compare"),
        )
        self.assertEqual(
            1,
            function.count("DOKA_BENCHMARK_COMPARISON_MODE=paired"),
        )
        self.assertIn(
            '"${repo_root}/eng/performance/benchmark.sh" --up-run-down',
            function,
        )

    def test_all_main_pushes_reach_the_cheap_benchmark_resolver(self) -> None:
        """Classify every push with the local measurement-input policy."""
        text = self.workflow("benchmark.yml")
        push_paths = text[text.index("  push:") : text.index("  workflow_dispatch:")]
        resolver = (self.repo / "eng" / "performance" / "workflow_state.py").read_text(
            encoding="utf-8"
        )
        inputs = (self.repo / "eng" / "performance" / "inputs.py").read_text(
            encoding="utf-8"
        )

        self.assertNotIn("paths:", push_paths)
        self.assertIn("if is_performance_input(path)", resolver)
        self.assertNotIn("release_evidence.is_performance_input(path)", resolver)
        self.assertIn(
            '"benchmarks/baselines/doka-benchmark-baseline.json"',
            inputs,
        )
        self.assertNotIn('".github/workflows/benchmark.yml"', inputs)
        self.assertNotIn('"eng/performance/workflow_state.py"', inputs)

    def test_proposal_state_cannot_override_event_relevance(self) -> None:
        """Keep stale proposal repair from starting unrelated measurements."""
        resolver = (self.repo / "eng" / "performance" / "workflow_state.py").read_text(
            encoding="utf-8"
        )

        self.assertIn("return bool(changes), changes", resolver)
        self.assertIn("if not event_requires_fresh_evidence:", resolver)
        self.assertIn('proposal.disposition == "current"', resolver)
        self.assertNotIn(
            "event_requires_fresh_evidence or proposal.disposition",
            resolver,
        )

    def test_benchmark_schedules_one_monthly_drift_measurement(self) -> None:
        """Bound unattended hosted scorecard consumption to one run per month."""
        text = self.workflow("benchmark.yml")
        schedule = text[text.index("  schedule:") : text.index("\n\npermissions:")]

        self.assertIn('cron: "15 2 1 * *"', schedule)
        self.assertNotIn('cron: "15 2 * * 0"', schedule)

    def test_unrelated_pushes_do_not_cancel_running_scorecards(self) -> None:
        """Preserve expensive evidence while later pushes queue cheaply."""
        text = self.workflow("benchmark.yml")

        self.assertIn("cancel-in-progress: false", text)
        self.assertNotIn("cancel-in-progress: true", text)

    def test_baseline_proposal_dispatches_only_required_checks(self) -> None:
        """Bind trusted required checks to the exact automation-branch head."""
        benchmark = self.workflow("benchmark.yml")
        ci = self.workflow("ci.yml")
        sync = self.job(
            benchmark,
            "sync-baseline-proposal",
            "benchmark-scorecard",
        )
        proposal = self.job(
            benchmark,
            "propose-baseline-update",
            "ensure-baseline-auto-merge",
        )

        self.assertIn("  pull_request:\n", ci)
        self.assertIn("  workflow_dispatch:\n", ci)
        self.assertIn("profile:", ci)
        self.assertIn("- baseline-proposal", ci)
        self.assertEqual(2, benchmark.count("gh workflow run ci.yml"))
        for update_job in (sync, proposal):
            self.assertEqual(1, update_job.count("gh workflow run ci.yml"))
            self.assertIn("--field profile=baseline-proposal", update_job)

        expensive_jobs = (
            ("migration-deployment", "repo-tests"),
            ("efcore-patch-matrix", "mysqlconnector-patch-matrix"),
            ("mysqlconnector-patch-matrix", "spec-test-suite"),
            ("spec-test-suite", "coverage-gate"),
            ("coverage-gate", "integration-smoke"),
            ("runtime-posture", "benchmark-smoke"),
            ("benchmark-smoke", None),
        )
        for job_name, next_job_name in expensive_jobs:
            with self.subTest(job=job_name):
                job = self.job(ci, job_name, next_job_name)
                self.assertIn("inputs.profile != 'baseline-proposal'", job)

        cheap_jobs = (
            ("quality-gates", "migration-deployment"),
            ("repo-tests", "efcore-patch-matrix"),
            ("integration-smoke", "runtime-posture"),
        )
        for job_name, next_job_name in cheap_jobs:
            with self.subTest(job=job_name):
                job = self.job(ci, job_name, next_job_name)
                self.assertNotIn("inputs.profile", job)

        self.assertIn(
            "Pull-request checks: explicitly dispatched for the proposal head",
            proposal,
        )
        self.assertIn(
            "Acceptance: maintainer approval and protected checks",
            proposal,
        )
        self.assertNotIn("Approve workflows to run", proposal)

    def test_baseline_proposal_rejects_unexpected_paths(self) -> None:
        """Keep fresh proposal commits confined to the canonical baseline."""
        text = self.workflow("benchmark.yml")
        resolver = self.job(
            text,
            "resolve-baseline-mode",
            "sync-baseline-proposal",
        )
        proposal = self.job(
            text,
            "propose-baseline-update",
            "ensure-baseline-auto-merge",
        )

        self.assertIn('proposal_base="$(', resolver)
        self.assertIn("git merge-base \\", resolver)
        self.assertIn('"origin/${baseline_branch}"', resolver)
        self.assertIn("Refusing to inspect unexpected proposal path", resolver)
        self.assertIn('git diff --name-only "${GITHUB_SHA}" HEAD', proposal)
        self.assertIn('git diff --name-only "${GITHUB_SHA}"', proposal)
        self.assertIn("Refusing to update unexpected proposal path", proposal)
        self.assertIn("Refusing to commit unexpected proposal path", proposal)

    def test_missing_proposal_baseline_is_regenerated(self) -> None:
        """Route missing review evidence through the tested invalid state."""
        text = self.workflow("benchmark.yml")
        resolver = self.job(
            text,
            "resolve-baseline-mode",
            "sync-baseline-proposal",
        )

        self.assertIn("if ! git show \\", resolver)
        self.assertIn(': > "${proposed_baseline}"', resolver)
        self.assertLess(
            resolver.index("if ! git show \\"),
            resolver.index("python3 -m eng.performance.workflow_state"),
        )

    def test_baseline_proposal_sync_rejects_unexpected_paths(self) -> None:
        """Keep the cheap refresh confined to the canonical baseline file."""
        text = self.workflow("benchmark.yml")
        sync = self.job(
            text,
            "sync-baseline-proposal",
            "benchmark-scorecard",
        )

        self.assertIn('git diff --name-only "${GITHUB_SHA}" HEAD', sync)
        self.assertIn("Refusing to synchronize unexpected proposal path", sync)
        self.assertIn("validate-baseline", sync)
        self.assertNotIn("benchmark.sh", sync)

    def test_candidate_identity_survives_selective_job_reruns(self) -> None:
        """Exclude the mutable run attempt from the stable candidate identity."""
        text = self.workflow("release-candidate.yml")

        self.assertIn(
            "DOKA_RELEASE_CANDIDATE_RUN_ID: github-${{ github.run_id }}",
            text,
        )
        self.assertIn(
            "DOKA_RELEASE_RUN_ATTEMPT: ${{ github.run_attempt }}",
            text,
        )
        self.assertNotIn(
            "DOKA_RELEASE_CANDIDATE_RUN_ID: "
            "github-${{ github.run_id }}-${{ github.run_attempt }}",
            text,
        )

    def test_candidate_preflight_validates_resume_tooling(self) -> None:
        """Fail before costly jobs when the resume boundary is not reviewable."""
        text = self.workflow("release-candidate.yml")
        preflight = self.job(text, "preflight", "foundation")

        self.assertIn("bash ./eng/common/verify-dotnet.sh", preflight)
        self.assertIn("bash -n ./eng/release-candidate.sh", preflight)
        self.assertIn(
            "bash -n ./eng/release/restore-release-stage-artifacts.sh",
            preflight,
        )
        self.assertIn("eng.tests.test_release_artifact_resolver", preflight)
        self.assertIn("eng.tests.test_release_stage_checkpoint", preflight)
        self.assertIn("eng.tests.test_release_workflow_policy", preflight)
        self.assertIn("eng.tests.test_materialize_sbom_assets", preflight)
        self.assertLess(
            text.index("- name: Verify release tooling contracts"),
            text.index("- name: Run ${{ matrix.stage }} stage"),
        )

    def test_candidate_rejects_an_untrusted_tag_before_the_matrix(self) -> None:
        """Fail cheaply, and explain, when the tag cannot be trusted.

        This replaces the accepted-baseline preflight. Under a paired
        comparison no accepted baseline is a release precondition, but the
        reason that preflight existed is unchanged and now weighs more: the
        cheapest check must decide first, and its failure must tell the
        operator how to leave the state rather than only that they are in it.
        """
        text = self.workflow("release-candidate.yml")
        preflight = self.job(text, "preflight", "foundation")

        self.assertIn("- name: Establish the release tag trust root", preflight)
        self.assertIn("python3 -m eng.release.trust verify", preflight)
        self.assertIn("--policy eng/release/evidence-policy.json", preflight)
        self.assertIn("Release tag trust root required", preflight)
        for instruction in (
            "Sign the tag",
            "reachable from protected main",
            "repository-qualification",
        ):
            with self.subTest(instruction=instruction):
                self.assertIn(instruction, preflight)

        self.assertIn(
            "needs: preflight", self.job(text, "foundation", "engine-contracts")
        )
        self.assertLess(
            text.index("- name: Establish the release tag trust root"),
            text.index("- name: Run ${{ matrix.stage }} stage"),
        )

    def test_the_candidate_measures_performance_exactly_once(self) -> None:
        """Reuse one measurement instead of measuring or classifying twice.

        The intent is unchanged from the import model it replaces and is
        sharper here: the tag performs one paired comparison, and no second
        job re-classifies the same reports under another run identity.
        """
        text = self.workflow("release-candidate.yml")

        self.assertEqual(
            1, text.count("uses: ./.github/workflows/benchmark-scorecard.yml")
        )
        self.assertIn("comparison_mode: paired", text)
        for removed in ("performance-import:", "performance-scorecard:"):
            with self.subTest(job=removed):
                self.assertNotIn(f"\n  {removed}", text)
        self.assertNotIn("--stage performance-mysql84", text)
        self.assertNotIn("--stage performance-mariadb118", text)

    def test_candidate_assembles_the_exact_required_stage_set(self) -> None:
        """Bind finalization to every independent qualification receipt."""
        text = self.workflow("release-candidate.yml")
        assemble = self.job(text, "assemble", "attest")
        # The set is exactly what the tagged commit produced for itself.
        # Branch-verified gates are imported and must not reappear here; a
        # stage restored twice, or one silently dropped, is the defect that
        # made the release coverage gate merge two of five inputs.
        required_stages = (
            "migration-deployment",
            "runtime",
            "efcore-patch-matrix",
            "mysqlconnector-patch-matrix",
            "package",
            "sbom",
        )
        imported_stages = (
            "quality",
            "repository-tests",
            "specification",
            "integration",
            "coverage",
        )

        self.assertEqual(1, assemble.count("--output artifacts"))
        for stage in required_stages:
            with self.subTest(stage=stage):
                self.assertEqual(1, assemble.count(f"--stage {stage}"))
        for stage in imported_stages:
            with self.subTest(imported=stage):
                self.assertEqual(0, assemble.count(f"--stage {stage}"))

        self.assertIn(
            "bash ./eng/release-candidate.sh --stage finalize",
            assemble,
        )

    def test_release_artifacts_are_immutable_and_attempt_qualified(self) -> None:
        """Prevent a rerun from overwriting evidence created by another attempt."""
        for workflow_name in ("release-candidate.yml", "nuget-publish.yml"):
            text = self.workflow(workflow_name)

            with self.subTest(workflow=workflow_name):
                self.assertNotIn("overwrite: true", text)

        candidate = self.workflow("release-candidate.yml")
        publication = self.workflow("nuget-publish.yml")
        self.assertIn("release-stage-${{ matrix.stage }}-attempt-", candidate)
        self.assertIn("release-candidate-artifacts-attempt-", candidate)
        self.assertIn("nuget-validation-evidence-attempt-", publication)
        self.assertIn("nuget-publish-evidence-attempt-", publication)
        self.assertIn("nuget-readback-evidence-attempt-", publication)

    def test_candidate_attestation_alone_receives_oidc_write(self) -> None:
        """Confine candidate OIDC authority to post-assembly attestation."""
        text = self.workflow("release-candidate.yml")
        attest = self.job(text, "attest")

        self.assertEqual(1, text.count("id-token: write"))
        self.assertIn("id-token: write", attest)
        self.assertIn("attestations: write", attest)
        self.assertIn("artifact-metadata: write", attest)

    def test_publication_verifies_sdk_before_requesting_credentials(self) -> None:
        """Keep exact SDK enforcement ahead of the NuGet OIDC exchange."""
        text = self.workflow("nuget-publish.yml")

        self.assertLess(
            text.index("- name: Verify approved .NET SDK"),
            text.index("- name: Request short-lived NuGet.org key"),
        )

    def test_publication_oidc_is_confined_to_the_publish_job(self) -> None:
        """Keep the protected environment and OIDC grant out of validation."""
        text = self.workflow("nuget-publish.yml")
        validate = self.job(text, "validate-candidate", "publish")
        publish = self.job(text, "publish", "readback")
        readback = self.job(text, "readback", "finalize-github-release")
        finalize = self.job(text, "finalize-github-release")

        self.assertEqual(1, text.count("id-token: write"))
        self.assertEqual(1, text.count("environment:\n"))
        self.assertNotIn("id-token: write", validate)
        self.assertNotIn("environment:\n", validate)
        self.assertIn("environment:\n      name: nuget", publish)
        self.assertIn("id-token: write", publish)
        self.assertNotIn("id-token: write", readback)
        self.assertNotIn("environment:\n", readback)
        self.assertNotIn("id-token: write", finalize)
        self.assertNotIn("environment:\n", finalize)

    def test_authoritative_preflight_immediately_precedes_nuget_oidc(self) -> None:
        """Request the one-hour key only after retry-safe remote-state checks."""
        text = self.workflow("nuget-publish.yml")
        publish = self.job(text, "publish", "readback")

        preflight = publish.index(
            "- name: Check NuGet.org immediately before publication"
        )
        login = publish.index("- name: Request short-lived NuGet.org key")
        first_push = publish.index("- name: Publish provider package")
        self.assertLess(preflight, login)
        self.assertLess(login, first_push)
        self.assertIn(
            "if: steps.preflight.outputs.publication_required == 'true'",
            publish,
        )

    def test_github_release_finalization_follows_public_nuget_readback(self) -> None:
        """Confine repository write authority to the post-readback job."""
        text = self.workflow("nuget-publish.yml")
        readback = self.job(text, "readback", "finalize-github-release")
        finalize = self.job(text, "finalize-github-release")

        self.assertEqual(1, text.count("contents: write"))
        self.assertIn("- readback", finalize)
        self.assertIn("actions: read", finalize)
        self.assertIn("contents: write", finalize)
        self.assertNotIn("id-token: write", finalize)
        self.assertNotIn("attestations: read", finalize)
        self.assertLess(
            readback.index("bash eng/testing/test-nuget-readback.sh"),
            len(readback),
        )

    def test_public_readback_cryptographically_verifies_repository_signatures(self) -> None:
        """Do not treat an unverified signature ZIP entry as provenance."""
        text = self.workflow("nuget-publish.yml")
        readback = self.job(text, "readback", "finalize-github-release")

        payload_readback = readback.index("python3 -m eng.release.nuget readback")
        signature_verification = readback.index("dotnet nuget verify")
        runtime_readback = readback.index("bash eng/testing/test-nuget-readback.sh")

        self.assertLess(payload_readback, signature_verification)
        self.assertLess(signature_verification, runtime_readback)
        self.assertEqual(1, readback.count("--all"))
        self.assertIn("nuget-signature-verification.txt", readback)

    def test_github_release_finalization_preserves_verified_evidence(self) -> None:
        """Require the final job to consume and retain both evidence domains."""
        text = self.workflow("nuget-publish.yml")
        finalize = self.job(text, "finalize-github-release")

        self.assertIn(
            "needs.readback.outputs.readback_evidence_artifact_name",
            finalize,
        )
        self.assertIn("python3 -m eng.release.github prepare", finalize)
        self.assertIn("python3 -m eng.release.github publish", finalize)
        self.assertIn("github-release-plan.json", finalize)
        self.assertIn("github-release-readback.json", finalize)
        self.assertIn("github-release-evidence-${{ inputs.release_tag }}", finalize)

    def test_sdk_contract_has_a_reviewed_update_channel(self) -> None:
        """Keep the exact SDK pin visible to scheduled dependency review."""
        text = (self.repo / ".github" / "dependabot.yml").read_text(encoding="utf-8")
        section_start = text.index("- package-ecosystem: dotnet-sdk")
        section_end = text.index("- package-ecosystem: nuget", section_start)
        section = text[section_start:section_end]

        self.assertIn("directory: /", section)
        self.assertIn("interval: weekly", section)
        self.assertIn("open-pull-requests-limit: 1", section)

    def test_github_release_helper_cannot_create_tags_or_replace_assets(self) -> None:
        """Keep tag creation and destructive asset replacement out of scope."""
        text = (self.repo / "eng" / "release" / "github.py").read_text(encoding="utf-8")

        self.assertIn('"--verify-tag"', text)
        self.assertNotIn('"--clobber"', text)
        self.assertNotIn('"--target"', text)


class StageSetAgreementTests(unittest.TestCase):
    """Prove the orchestrator and the workflow agree on the stage set.

    The two declare the same thing in two places: the workflow decides which
    stages run at the tag, and the orchestrator decides which receipts
    finalization requires. When they drift, finalization either demands a
    receipt nothing produces, or accepts a candidate assembled from fewer gates
    than the workflow ran. The release coverage gate failed in exactly that
    shape -- one side merged five inputs, the other two -- so this agreement is
    pinned rather than assumed.
    """

    def setUp(self) -> None:
        """Read both declarations once per case."""
        self.workflow = self.workflow_text("release-candidate.yml")
        self.script = (
            Path(__file__).resolve().parents[2]
            / "eng"
            / "release"
            / "release-candidate.sh"
        ).read_text(encoding="utf-8")
        contract = json.loads(
            (
                Path(__file__).resolve().parents[2]
                / "benchmarks"
                / "performance-contract.json"
            ).read_text(encoding="utf-8")
        )
        self.performance_targets = tuple(sorted(contract["requiredTargets"]))

    @staticmethod
    def workflow_text(name: str) -> str:
        """Return one workflow file."""
        return (
            Path(__file__).resolve().parents[2] / ".github" / "workflows" / name
        ).read_text(encoding="utf-8")

    def workflow_stages(self) -> set[str]:
        """Return the stages the tag workflow actually runs.

        Reading the matrix alone would miss a stage invoked by its own job,
        which is how `sbom` runs. The question is which stages execute, not
        how they happen to be declared.
        """
        matrix = set(re.findall(r"^\s+- stage: ([a-z-]+)$", self.workflow, re.M))
        # Only an invocation of the orchestrator runs a stage. The restore
        # helper takes the same `--stage` flag to fetch receipts, so counting
        # every occurrence would let a stage removed from the matrix keep
        # appearing through the list that merely consumes it.
        direct = set(
            re.findall(r"release-candidate\.sh --stage ([a-z-]+)", self.workflow)
        )
        direct -= {"finalize"}

        return matrix | direct

    def required_stages(self) -> set[str]:
        """Return the stages finalization requires a receipt for."""
        block = re.search(
            r"local expected_stages=\(\n(?P<body>.*?)\n\s*\)",
            self.script,
            re.S,
        )
        self.assertIsNotNone(block, "the orchestrator declares no expected stages")

        return {
            line.strip() for line in block.group("body").splitlines() if line.strip()
        }

    def test_finalization_requires_exactly_what_the_tag_produces(self) -> None:
        """Reject any drift between the two declarations."""
        self.assertEqual(self.workflow_stages(), self.required_stages())

    def test_no_imported_gate_is_required_as_a_receipt(self) -> None:
        """Keep branch-verified gates out of the receipt requirement.

        Requiring a receipt for a gate the tag never runs would make every
        release unfinishable; accepting one silently would let an old artifact
        stand in for this commit.
        """
        for imported in (
            "quality",
            "repository-tests",
            "specification",
            "integration",
            "coverage",
            *(f"performance-{target}" for target in self.performance_targets),
        ):
            with self.subTest(stage=imported):
                self.assertNotIn(imported, self.required_stages())


if __name__ == "__main__":
    unittest.main()
