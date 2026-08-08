"""Regression tests for hosted release-workflow security boundaries."""

from __future__ import annotations

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
        proposal = self.job(text, "propose-baseline-update")

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
        self.assertIn(
            "baseline_mode: ${{ needs.resolve-baseline-mode.outputs.mode }}",
            scorecard,
        )
        self.assertIn(
            "needs.resolve-baseline-mode.outputs.proposal-required == 'true'",
            proposal,
        )
        self.assertNotIn("github.event_name != 'push'", proposal)
        self.assertIn("- benchmark-scorecard", proposal)
        self.assertIn("benchmark-artifacts-mysql84", proposal)
        self.assertIn("benchmark-artifacts-mariadb118", proposal)
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
        """Confine mutations to the two bounded proposal-update jobs."""
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
        proposal = self.job(text, "propose-baseline-update")

        self.assertNotIn("contents: write", resolver)
        self.assertNotIn("contents: write", scorecard)
        self.assertNotIn("pull-requests: write", resolver)
        self.assertNotIn("pull-requests: write", sync)
        self.assertNotIn("pull-requests: write", scorecard)
        self.assertNotIn("actions: write", resolver)
        self.assertNotIn("actions: write", scorecard)
        self.assertIn("actions: write", sync)
        self.assertIn("actions: write", proposal)
        self.assertEqual(2, text.count("actions: write"))
        self.assertEqual(2, text.count("contents: write"))
        self.assertEqual(1, text.count("pull-requests: write"))
        self.assertIn("contents: write", sync)
        self.assertIn("gh pr create", proposal)
        self.assertIn("gh api", proposal)
        self.assertNotIn("gh pr edit", proposal)
        self.assertEqual(2, text.count("gh workflow run ci.yml"))
        for update_job in (sync, proposal):
            self.assertIn("gh workflow run ci.yml", update_job)
            self.assertIn("--ref \"${BASELINE_BRANCH}\"", update_job)
            self.assertIn("--field profile=baseline-proposal", update_job)
        self.assertIn("gh pr merge", proposal)
        self.assertIn("--auto", proposal)
        self.assertIn("--squash", proposal)
        self.assertIn("--match-head-commit", proposal)
        self.assertIn("--json autoMergeRequest", proposal)
        self.assertIn("unexpected ${auto_merge_method} auto-merge policy", proposal)
        self.assertNotIn("--admin", proposal)
        self.assertNotIn("gh pr review", proposal)
        self.assertNotIn("--force", proposal)
        self.assertNotIn("secrets.", proposal)

    def test_benchmark_measurement_isolated_from_the_control_plane(self) -> None:
        """Keep orchestration edits from allocating service-container jobs."""
        control_plane = self.workflow("benchmark.yml")
        scorecard_workflow = self.workflow("benchmark-scorecard.yml")
        scorecard_call = self.job(
            control_plane,
            "benchmark-scorecard",
            "propose-baseline-update",
        )

        self.assertIn("  workflow_call:\n", scorecard_workflow)
        self.assertIn("baseline_mode:", scorecard_workflow)
        self.assertIn(
            "uses: ./.github/workflows/benchmark-scorecard.yml",
            scorecard_call,
        )
        self.assertNotIn("runs-on:", scorecard_call)
        self.assertNotIn("services:", control_plane)
        self.assertNotIn("bash ./eng/benchmark.sh --test-only", control_plane)
        self.assertIn("services:", scorecard_workflow)
        self.assertIn(
            "DOKA_BENCHMARK_BASELINE_MODE: ${{ inputs.baseline_mode }}",
            scorecard_workflow,
        )
        self.assertIn(
            "bash ./eng/benchmark.sh --test-only",
            scorecard_workflow,
        )
        self.assertEqual(4, scorecard_workflow.count("image:"))
        self.assertEqual(
            2,
            scorecard_workflow.count("outputs.status == 'inconclusive'"),
        )
        self.assertEqual(2, scorecard_workflow.count("if: always()"))
        self.assertEqual(6, scorecard_workflow.count("actions/upload-artifact@"))
        self.assertIn("benchmark-artifacts-mysql84", scorecard_workflow)
        self.assertIn("benchmark-artifacts-mariadb118", scorecard_workflow)

    def test_release_candidate_imports_the_qualified_scorecard(self) -> None:
        """Reuse one digest-bound result instead of measuring it twice."""
        workflow = self.workflow("release-candidate.yml")
        engine_contracts = self.job(
            workflow,
            "engine-contracts",
            "performance-scorecard",
        )
        scorecard = self.job(
            workflow,
            "performance-scorecard",
            "performance-import",
        )
        performance_import = self.job(
            workflow,
            "performance-import",
            "coverage",
        )
        assembly = self.job(workflow, "assemble", "attest")
        script = (
            self.repo / "eng" / "release" / "release-candidate.sh"
        ).read_text(encoding="utf-8")

        self.assertNotIn("performance-mysql84", engine_contracts)
        self.assertNotIn("performance-mariadb118", engine_contracts)
        self.assertIn(
            "uses: ./.github/workflows/benchmark-scorecard.yml",
            scorecard,
        )
        self.assertIn("baseline_mode: compare", scorecard)
        self.assertIn("benchmark-artifacts-${{ matrix.target }}", performance_import)
        self.assertIn("--stage \"${{ matrix.stage }}\"", performance_import)
        self.assertNotIn("eng/benchmark", performance_import)
        self.assertIn("performance-import", assembly)
        self.assertIn(
            "DOKA_RELEASE_CANDIDATE_PERFORMANCE_ARTIFACT_ROOT",
            script,
        )
        self.assertIn("import-attempt-selection", script)
        self.assertIn("verify-imported-attempt", script)
        self.assertIn("--expected-target", script)
        self.assertIn("--expected-commit", script)

    def test_all_main_pushes_reach_the_cheap_benchmark_resolver(self) -> None:
        """Classify every push with the local measurement-input policy."""
        text = self.workflow("benchmark.yml")
        push_paths = text[text.index("  push:") : text.index("  workflow_dispatch:")]
        resolver = (
            self.repo / "eng" / "performance" / "workflow_state.py"
        ).read_text(encoding="utf-8")
        inputs = (
            self.repo / "eng" / "performance" / "inputs.py"
        ).read_text(encoding="utf-8")

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
        resolver = (
            self.repo / "eng" / "performance" / "workflow_state.py"
        ).read_text(encoding="utf-8")

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
        proposal = self.job(benchmark, "propose-baseline-update")

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
        proposal = self.job(text, "propose-baseline-update")

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

        self.assertIn('if ! git show \\', resolver)
        self.assertIn(': > "${proposed_baseline}"', resolver)
        self.assertLess(
            resolver.index('if ! git show \\'),
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

    def test_candidate_rejects_an_incompatible_baseline_before_the_matrix(
        self,
    ) -> None:
        """Fail cheaply when strict hosted comparison cannot be performed."""
        text = self.workflow("release-candidate.yml")
        preflight = self.job(text, "preflight", "foundation")

        self.assertIn(
            "- name: Verify accepted hosted performance baseline",
            preflight,
        )
        self.assertIn("validate-performance-baseline", preflight)
        self.assertIn("--repo .", preflight)
        self.assertIn("--profile scorecard", preflight)
        self.assertIn("--runner-class github-ubuntu-latest-x64", preflight)
        self.assertIn("artifacts/release-baseline-readiness.json", preflight)
        self.assertIn("Hosted performance baseline required", preflight)
        self.assertIn("review and merge", preflight)
        self.assertIn("needs: preflight", self.job(text, "foundation", "sbom"))
        self.assertLess(
            text.index("- name: Verify accepted hosted performance baseline"),
            text.index("- name: Run ${{ matrix.stage }} stage"),
        )

    def test_candidate_assembles_the_exact_required_stage_set(self) -> None:
        """Bind finalization to every independent qualification receipt."""
        text = self.workflow("release-candidate.yml")
        assemble = self.job(text, "assemble", "attest")
        required_stages = (
            "quality",
            "repository-tests",
            "specification",
            "integration",
            "migration-deployment",
            "runtime",
            "coverage",
            "package",
            "sbom",
            "performance-mysql84",
            "performance-mariadb118",
        )

        self.assertEqual(1, assemble.count("--output artifacts"))
        for stage in required_stages:
            with self.subTest(stage=stage):
                self.assertEqual(1, assemble.count(f"--stage {stage}"))

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
        text = (self.repo / ".github" / "dependabot.yml").read_text(
            encoding="utf-8"
        )
        section_start = text.index("- package-ecosystem: dotnet-sdk")
        section_end = text.index("- package-ecosystem: nuget", section_start)
        section = text[section_start:section_end]

        self.assertIn("directory: /", section)
        self.assertIn("interval: weekly", section)
        self.assertIn("open-pull-requests-limit: 1", section)

    def test_github_release_helper_cannot_create_tags_or_replace_assets(self) -> None:
        """Keep tag creation and destructive asset replacement out of scope."""
        text = (self.repo / "eng" / "release" / "github.py").read_text(
            encoding="utf-8"
        )

        self.assertIn('"--verify-tag"', text)
        self.assertNotIn('"--clobber"', text)
        self.assertNotIn('"--target"', text)


if __name__ == "__main__":
    unittest.main()
