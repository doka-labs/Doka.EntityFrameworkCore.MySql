"""Hold the finalizer's evidence demands against what qualification produces.

The finalizer once demanded evidence the release workflow had stopped
producing. Every part passed its own test. Nothing compared the demands against
the artifact set the workflow actually assembles, so the mismatch stayed
invisible until a release candidate reached it -- which is after every
expensive gate has run.

The evidence comparison below holds stage transport. The executable
publication-readiness tests separately start without build outputs and prove
that the finalizer's semantic gate creates every assembly it consumes.
"""

from __future__ import annotations

import json
import os
import re
import shlex
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ORCHESTRATOR = REPOSITORY_ROOT / "eng" / "release" / "release-candidate.sh"
WORKFLOW = REPOSITORY_ROOT / ".github" / "workflows" / "release-candidate.yml"
CI_WORKFLOW = REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml"
TEST_ENTRY = REPOSITORY_ROOT / "eng" / "testing" / "test.sh"
MIGRATION_GATE = REPOSITORY_ROOT / "eng" / "testing" / "test-migration-deployment.sh"
MIGRATION_MODEL = REPOSITORY_ROOT / "eng" / "quality" / "check-migration-model.sh"
PUBLISHER_ACTION = REPOSITORY_ROOT / ".github/actions/publish-release/action.yml"

# Which stage produces the evidence behind each orchestrator path variable.
PRODUCERS = {
    "packages_dir": "package",
    "sbom_dir": "sbom",
    "migration_deployment_root": "migration-deployment",
    "runtime_dir": "runtime",
    "efcore_matrix_dir": "efcore-patch-matrix",
    "mysqlconnector_matrix_dir": "mysqlconnector-patch-matrix",
    "qualification_manifest_file": "finalize",
}


def shell_commands(path: Path, module: str) -> list[list[str]]:
    """Read literal module invocations without executing workflow shell code."""
    body = re.sub(r"\\\n\s*", " ", path.read_text(encoding="ascii"))
    commands = []
    prefix = f"python3 -m eng.release.{module} "
    for line in body.splitlines():
        if not line.strip().startswith(prefix):
            continue

        arguments = shlex.split(line.strip())[3:]
        # WHY: The optional preflight publication flag is expanded by the shell,
        # not interpreted by the Python argument parser.
        commands.append([item for item in arguments if item not in {
            "${authorization_arguments[@]}",
        }])

    return commands


class ReleaseLineWorkflowSeamTests(unittest.TestCase):
    """Exercise the actual publisher/qualifier command and artifact boundaries."""

    def test_preparation_and_admission_are_visible_before_the_publisher_boundary(self) -> None:
        """Keep retry history able to distinguish preparation from frozen admission and writes."""
        # Arrange
        from eng.release import publication

        workflow = WORKFLOW.read_text(encoding="ascii")
        publisher_job = workflow.split("\n  publish:\n", 1)[1]
        action = PUBLISHER_ACTION.read_text(encoding="ascii")

        # Act
        steps = re.findall(r"^      - name: ([^\n]+)$", publisher_job, re.MULTILINE)

        # Assert
        self.assertEqual([
            "Checkout trusted workflow source", "Setup .NET",
            "Download immutable same-run candidate", "Download exhaustive stage receipts",
            "Download frozen line authorization", "Download portable SLSA provenance",
            "Revalidate source, evidence, and attestations", "Admit or recover the immutable publication",
            "Freeze publication admission for same-run recovery", publication.PUBLISHER_STEP_NAME,
        ], steps)
        self.assertNotIn("python3 -m eng.release.publication admit", action)
        self.assertNotIn("uses: actions/download-artifact@", action)
        self.assertIn('name: publication-admission-attempt-${{ github.run_attempt }}', publisher_job)
        self.assertIn("retention-days: 30", publisher_job)

    def test_admission_upload_is_a_required_step_before_the_write_action(self) -> None:
        """A failed preparation or failed upload must leave the actual publisher skipped."""
        # Arrange
        workflow = WORKFLOW.read_text(encoding="ascii")
        publisher_job = workflow.split("\n  publish:\n", 1)[1]

        # Act
        admission = publisher_job.split("      - name: Freeze publication admission for same-run recovery\n", 1)[1]
        upload, publisher = admission.split("      - name: Publish qualified immutable release\n", 1)

        # Assert
        self.assertIn("uses: actions/upload-artifact@", upload)
        self.assertIn("if-no-files-found: error", upload)
        self.assertIn("artifacts/release-publication/publication-admission.json", upload)
        self.assertIn("artifacts/release-publication/authorization-observation.json", upload)
        self.assertNotIn("        if:", upload)
        self.assertNotIn("continue-on-error:", publisher_job)
        self.assertNotIn("        if:", publisher)
        self.assertIn("uses: $/.github/actions/publish-release", publisher)

    def test_recovery_boundary_names_match_the_visible_workflow_steps(self) -> None:
        """A renamed or hidden boundary must fail before any release candidate starts."""
        # Arrange
        from eng.release import publication

        publisher_job = WORKFLOW.read_text(encoding="ascii").split("\n  publish:\n", 1)[1]

        # Act
        names = re.findall(r"^      - name: ([^\n]+)$", publisher_job, re.MULTILINE)

        # Assert
        self.assertEqual(1, names.count(publication.ADMISSION_STEP_NAME))
        self.assertEqual(1, names.count(publication.PUBLISHER_STEP_NAME))
        self.assertLess(names.index(publication.ADMISSION_STEP_NAME), names.index(publication.PUBLISHER_STEP_NAME))

    def test_shared_publisher_inputs_are_all_consumed(self) -> None:
        """Do not retain unused artifact inputs after moving preparation into its sole caller."""
        # Arrange
        action = PUBLISHER_ACTION.read_text(encoding="ascii")
        declaration = action.split("inputs:\n", 1)[1].split("runs:\n", 1)[0]

        # Act
        declared = set(re.findall(r"^  ([a-z_]+):$", declaration, re.MULTILINE))
        consumed = set(re.findall(r"inputs\.([a-z_]+)", action))

        # Assert
        self.assertEqual({"version", "nuget_user"}, declared)
        self.assertEqual(declared, consumed)

    def test_artifact_retention_respects_the_repository_limit(self) -> None:
        """Reject upload durations GitHub would clamp below the requested lifetime."""
        # Arrange
        # WHY: D-025 records the hosted 30-day cap; longer retention requires
        # an approved repository policy change before workflow values increase.
        maximum_days = 30
        paths = sorted(
            path
            for directory in (REPOSITORY_ROOT / ".github/workflows", REPOSITORY_ROOT / ".github/actions")
            for pattern in ("*.yml", "*.yaml")
            for path in directory.rglob(pattern)
        )

        # Act
        # WHY: Quoting or commenting a duration must not hide it from the cap;
        # non-literal values fail conversion instead of being silently ignored.
        retention = [
            (str(path.relative_to(REPOSITORY_ROOT)), int(value.partition("#")[0].strip().strip("'\"")))
            for path in paths
            for value in re.findall(r"(?m)^[ \t]*retention-days:[ \t]*([^\r\n]*)$", path.read_text(encoding="ascii"))
        ]

        excessive = [(path, days) for path, days in retention if days > maximum_days]

        # Assert
        self.assertTrue(retention)
        self.assertEqual([], excessive)

    def test_release_lines_add_no_handoff_controller_or_readiness_subsystem(self) -> None:
        """The ordinary release entry must not require the withdrawn operations framework."""
        # Arrange
        workflow = REPOSITORY_ROOT / ".github/workflows/release-lines-control.yml"
        controller = REPOSITORY_ROOT / "eng/release/control.py"
        readiness_tests = REPOSITORY_ROOT / "eng/tests/test_release_publication_readiness.py"
        test_entry = REPOSITORY_ROOT / "eng/release/test-tools.sh"

        # Act
        automated_entry_exists = workflow.exists() or controller.exists()
        readiness_tests_exist = readiness_tests.exists()
        selected_tests = test_entry.read_text(encoding="ascii")
        publisher = PUBLISHER_ACTION.read_text(encoding="ascii")

        # Assert
        self.assertFalse(automated_entry_exists)
        self.assertFalse(readiness_tests_exist)
        self.assertNotIn("eng.tests.test_release_line_control", selected_tests)
        self.assertNotIn("eng.tests.test_release_publication_readiness", selected_tests)
        self.assertNotIn("publication-admission-complete.json", publisher)
        self.assertIn("python3 -m eng.release.github complete", publisher)

    def test_publication_workflow_commands_match_the_actual_parser(self) -> None:
        """Reject CLI drift before an expensive qualification reaches publication."""
        # Arrange
        from eng.release import publication

        commands = [
            command
            for path in (WORKFLOW, PUBLISHER_ACTION)
            for command in shell_commands(path, "publication")
        ]
        recheck = (REPOSITORY_ROOT / "eng/release/recheck-publication.sh").read_text(encoding="ascii")
        recheck = re.sub(r"\\\n\s*", " ", recheck)
        command = next(line for line in recheck.splitlines() if line.startswith("python3 -m "))
        commands.append(shlex.split(command)[3:])

        # Act
        parsed = [publication.build_parser().parse_args(command) for command in commands]

        # Assert
        self.assertEqual(["preflight", "admit", "recheck"],
                         [item.command for item in parsed])

    def test_publisher_nuget_commands_match_the_actual_parser(self) -> None:
        """Keep all new authority arguments consumable by the retained publisher."""
        # Arrange
        from eng.release import nuget

        commands = shell_commands(PUBLISHER_ACTION, "nuget")

        # Act
        parsed = []
        for command in commands:
            with mock.patch("sys.argv", ["nuget", *command]):
                parsed.append(nuget.parse_arguments())

        # Assert
        self.assertEqual(["bind", "preflight", "readback"], [item.command for item in parsed])
        self.assertTrue(all(item.admission is not None and item.authority is not None for item in parsed))

    def test_publisher_github_commands_match_the_actual_parser(self) -> None:
        """Hold shared draft, promotion and completion argument contracts together."""
        # Arrange
        from eng.release import github

        commands = shell_commands(PUBLISHER_ACTION, "github")

        # Act
        parsed = [github.build_parser().parse_args(command) for command in commands]

        # Assert
        self.assertEqual(["prepare", "stage", "publish", "complete"], [item.command for item in parsed])
        self.assertTrue(all(item.admission is not None and item.authority is not None for item in parsed))

    def test_every_remote_authority_recheck_has_scoped_github_authentication(self) -> None:
        """A context token does not authenticate gh unless exposed to the step."""
        # Arrange
        action = PUBLISHER_ACTION.read_text(encoding="ascii")
        steps = re.split(r"(?m)^    - name: ", action)[1:]
        guarded = [step for step in steps if "bash ./eng/release/recheck-publication.sh" in step]

        # Act
        unauthenticated = [step.splitlines()[0] for step in guarded
                           if "GH_TOKEN: ${{ github.token }}" not in step.split("      run:", 1)[0]]

        # Assert
        self.assertEqual(11, len(guarded))
        self.assertEqual([], unauthenticated)

    def test_frozen_line_artifact_reaches_every_qualification_producer(self) -> None:
        """An independent authority snapshot must not exist only in preflight."""
        # Arrange
        workflow = WORKFLOW.read_text(encoding="ascii")
        jobs = dict(re.findall(r"(?ms)^  ([a-z][a-z-]+):\n(.*?)(?=^  [a-z][a-z-]+:\n|\Z)", workflow))
        producers = ("foundation", "engine-contracts", "sbom", "assemble", "attest")

        # Act
        missing = [name for name in producers
                   if "name: ${{ needs.preflight.outputs.line_artifact_name }}" not in jobs[name]
                   or "path: artifacts/release-lines" not in jobs[name]]

        # Assert
        self.assertEqual([], missing)
        self.assertIn("line_artifact_name:", workflow)
        self.assertIn("line_artifact_name: ${{ steps.authority.outputs.line_artifact_name }}", jobs["preflight"])

    def test_single_entry_keeps_publication_permissions_out_of_qualification(self) -> None:
        """A version-only invocation retains the existing protected publish path."""
        # Arrange
        qualifier = WORKFLOW.read_text(encoding="ascii")
        publisher = qualifier.split("\n  publish:\n", 1)[1]
        preflight = qualifier.split("\n  preflight:\n", 1)[1].split("\n  foundation:\n", 1)[0]

        # Act
        has_second_entry = (WORKFLOW.parent / "release-lines.yml").exists()

        # Assert
        self.assertFalse(has_second_entry)
        self.assertNotIn("workflow_call:", qualifier)
        self.assertIn("permissions:\n  contents: read\n", qualifier)
        self.assertNotIn("id-token: write", preflight)
        self.assertNotIn("    environment:", preflight)
        self.assertNotIn("publisher_environment", preflight)
        self.assertIn("name: nuget", publisher)
        self.assertIn("id-token: write", publisher)
        self.assertIn("!inputs.qualification_only", publisher)

    def test_qualified_summary_uses_frozen_toolchain_and_exact_run_links(self) -> None:
        """Execute the real summary without dispatching a second qualification."""
        for preparation in ("false", "true"):
            with self.subTest(qualification_only=preparation), tempfile.TemporaryDirectory() as directory:
                # Arrange
                workflow = WORKFLOW.read_text(encoding="ascii")
                step = workflow.split("      - name: Summarize qualified candidate and existing operator actions\n", 1)[1]
                script = textwrap.dedent(step.split("        run: |\n", 1)[1].split("\n  publish:\n", 1)[0])
                root = Path(directory)
                manifest = root / "artifacts/release-candidate/github-123/release-candidate-evidence.json"
                manifest.parent.mkdir(parents=True)
                manifest.write_text(json.dumps({"toolchain": {
                    "dotnetSdk": "10.0.402", "targetFramework": "net10.0",
                    "efCoreVersion": "[10.0.8, 10.1.0)", "testRunner": "VSTest", "upstreamStage": "ga",
                    "resolvedPackages": {"Microsoft.EntityFrameworkCore.Relational": "10.0.8", "MySqlConnector": "2.5.0"},
                }}), encoding="ascii")
                summary = root / "summary.md"
                environment = {**os.environ, "GITHUB_REF": "refs/heads/maintenance/10.x", "GITHUB_SHA": "a" * 40,
                    "GITHUB_SERVER_URL": "https://github.com", "GITHUB_REPOSITORY": "owner/repo", "GITHUB_RUN_ID": "123",
                    "GITHUB_STEP_SUMMARY": str(summary), "DOKA_RELEASE_CANDIDATE_RUN_ID": "github-123",
                    "DOKA_RELEASE_VERSION": "10.6.2", "QUALIFICATION_ONLY": preparation}

                # Act
                result = subprocess.run(["bash", "-e", "-o", "pipefail", "-c", script], cwd=root,
                                        env=environment, capture_output=True, text=True, check=False)

                # Assert
                self.assertEqual(0, result.returncode, result.stderr)
                rendered = summary.read_text(encoding="ascii")
                for value in ("maintenance/10.x", "a" * 40, "v10.6.2", "10.0.402", "net10.0",
                              "[10.0.8, 10.1.0)", "10.0.8", "2.5.0", "VSTest", "ga",
                              "https://github.com/owner/repo/actions/runs/123#artifacts",
                              f"https://github.com/owner/repo/commit/{'a' * 40}"):
                    self.assertIn(value, rendered)

                if preparation == "true":
                    self.assertIn("no publication environment, credential or tag", rendered)
                    self.assertNotIn("personally signed", rendered)
                else:
                    self.assertIn("personally signed annotated tag", rendered)
                    self.assertIn("Do not dispatch another candidate", rendered)


def finalization_body() -> str:
    """Return the reconciliation function the finalizer runs."""
    body = ORCHESTRATOR.read_text(encoding="utf-8")
    start = body.index("write_reconciliation() {")

    return body[start : body.index("\n}\n", start)]


def required_variables() -> set[str]:
    """Return every path variable the finalizer requires evidence under."""
    pattern = re.compile(r'require_evidence_\w+\s+\\?\s*"\$\{(\w+)\}')

    return set(pattern.findall(finalization_body()))


def restored_stages() -> set[str]:
    """Return every stage the assemble job restores before finalizing."""
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assemble = workflow[workflow.index("\n  assemble:") :]
    restore = assemble[
        assemble.index("Restore complete verified stage set") : assemble.index(
            "Assemble immutable release candidate"
        )
    ]

    return set(re.findall(r"--stage (\S+)", restore))


class FinalizationEvidenceContractTests(unittest.TestCase):
    """Prove the finalizer asks only for evidence this run produces."""

    def test_every_required_path_has_a_producer(self) -> None:
        """Reject a demand for evidence no stage in this run creates.

        This is the exact break: `audit` survived in the requirement list after
        the quality stage it came from stopped running on the tag.
        """
        for variable in sorted(required_variables()):
            with self.subTest(variable=variable):
                self.assertIn(
                    variable,
                    PRODUCERS,
                    f"{variable} is required by the finalizer but no stage in "
                    "this run is recorded as producing it",
                )

    def test_every_stage_producer_is_restored_by_the_assemble_job(self) -> None:
        """Reject a demand whose stage the assemble job never downloads.

        A stage can exist and still not reach the finalizer: the assemble job
        runs on its own runner and sees only what it restores.
        """
        restored = restored_stages()
        for variable in sorted(required_variables()):
            stage = PRODUCERS.get(variable)
            if stage in (None, "finalize"):
                continue
            with self.subTest(variable=variable, stage=stage):
                self.assertIn(
                    stage,
                    restored,
                    f"{variable} comes from stage '{stage}', which the assemble "
                    "job does not restore",
                )

    def test_the_required_stage_set_matches_the_candidate_stages(self) -> None:
        """Keep the receipt requirement and the workflow matrix pinned together.

        A stage the workflow runs but the receipt check omits ships unverified;
        one the check requires but the workflow never runs makes every release
        unfinishable.
        """
        body = ORCHESTRATOR.read_text(encoding="utf-8")
        start = body.index("local expected_stages=(")
        expected = set(
            body[start + len("local expected_stages=(") : body.index(")", start)].split()
        )

        workflow = WORKFLOW.read_text(encoding="utf-8")
        matrix_stages = set(re.findall(r"^\s+- stage: (\S+)$", workflow, re.M))
        candidate_stages = matrix_stages | {"package", "sbom"}

        self.assertEqual(candidate_stages, expected)

    def test_the_finalizer_requires_no_retired_evidence(self) -> None:
        """Name the directories this run stopped producing.

        These came from gates the protected branch now proves and the tag
        imports. Requiring one again would reintroduce the break rather than a
        new one, so they are listed by name.
        """
        body = finalization_body()
        for retired in (
            "audit_dir",
            "coverage_merged_dir",
            "coverage_input_dir",
            "specification_dir",
            "integration_dir",
            "performance_dir",
        ):
            with self.subTest(directory=retired):
                self.assertNotIn(f'"${{{retired}}}"', body)

    def test_publication_readiness_is_bound_to_matrix_resolved_versions(self) -> None:
        """Reject a fresh floating restore after the candidate matrices passed."""
        body = ORCHESTRATOR.read_text(encoding="utf-8")
        finalization = body[
            body.index("run_finalization_stage() {") : body.index(
                "\n}\n\nrun_named_stage()", body.index("run_finalization_stage() {")
            )
        ]

        self.assertIn(
            '"${efcore_matrix_dir}/latest-10-0/efcore-contract-evidence.json"',
            finalization,
        )
        self.assertIn(
            '"${mysqlconnector_matrix_dir}/latest-2-x/driver-contract-evidence.json"',
            finalization,
        )
        self.assertEqual(1, finalization.count("--ef-core-version"))
        self.assertEqual(1, finalization.count("--mysqlconnector-version"))

    def test_repository_tests_execute_the_self_contained_publication_gate(self) -> None:
        """Move a broken clean-runner boundary from RC time to pull-request CI."""
        test_entry = TEST_ENTRY.read_text(encoding="utf-8")

        self.assertEqual(1, test_entry.count("check-publication-readiness.sh"))
        self.assertEqual(1, test_entry.count("--ef-core-version"))
        self.assertEqual(1, test_entry.count("--mysqlconnector-version"))

    def test_migration_deployment_owns_its_release_build(self) -> None:
        """Prove the other no-build release consumer has an in-job producer."""
        migration_gate = MIGRATION_GATE.read_text(encoding="utf-8")
        migration_model = MIGRATION_MODEL.read_text(encoding="utf-8")

        build_call = 'bash "${repo_root}/eng/quality/check-migration-model.sh"'
        self.assertEqual(1, migration_gate.count(build_call))
        build_prefix = "dotnet build " + "\\" + "\n    "
        self.assertIn(build_prefix + '"${migration_project}"', migration_model)

    def test_runtime_stage_requires_clean_source_evidence(self) -> None:
        """Bind the runtime producer to the clean source its consumer requires."""
        body = ORCHESTRATOR.read_text(encoding="utf-8")
        start = body.index("run_runtime_posture_gate() {")
        runtime_gate = body[start : body.index("\n}\n", start)]

        self.assertEqual(
            1,
            runtime_gate.count("DOKA_RUNTIME_REQUIRE_CLEAN_SOURCE=1"),
        )

    def test_package_stage_requires_runtime_readback_evidence(self) -> None:
        """Reject a package checkpoint when the runtime readback made no evidence."""
        body = ORCHESTRATOR.read_text(encoding="utf-8")
        start = body.index("run_pack() {")
        pack = body[start : body.index("\n}\n", start)]
        readback = 'bash "${repo_root}/eng/testing/test-nuget-readback.sh"'
        evidence = '"${local_package_consumer_dir}/local-package-runtime.json"'

        self.assertLess(pack.index(readback), pack.index(evidence))
        self.assertEqual(1, pack.count("require_evidence_file"))

    def test_release_matrix_stages_copy_only_registered_legs(self) -> None:
        """Exclude stale scratch rows from immutable release evidence."""
        body = ORCHESTRATOR.read_text(encoding="utf-8")

        self.assertNotIn('artifacts/efcore-patch-matrix/."', body)
        self.assertNotIn('artifacts/mysqlconnector-patch-matrix/."', body)
        for leg in (
            "minimum-10-0-8",
            "latest-10-0",
            "minimum-2-6-1",
            "latest-2-x",
        ):
            with self.subTest(leg=leg):
                self.assertEqual(1, body.count(f'"{leg}"'))

    def test_release_efcore_matrix_uses_a_reviewed_exact_patch(self) -> None:
        """Do not let a new upstream patch invalidate a green release commit."""
        body = ORCHESTRATOR.read_text(encoding="utf-8")
        start = body.index("run_efcore_matrix_gate() {")
        matrix = body[start : body.index("\n}\n", start)]

        self.assertIn("SpecSuiteBaseline.json", matrix)
        self.assertIn("max_by(.components)", matrix)
        self.assertNotIn("10.0.*", matrix)
        self.assertIn(
            '"${qualified_version}:${qualified_pattern}:latest-10-0:full"',
            matrix,
        )
        self.assertIn(
            'ef-core-version: "10.0.*"',
            CI_WORKFLOW.read_text(encoding="utf-8"),
        )

    def test_latest_qualified_efcore_patch_has_complete_contracts(self) -> None:
        """Keep the release selector and exact contract files in one state."""
        contracts = (
            REPOSITORY_ROOT
            / "tests"
            / "Doka.EntityFrameworkCore.MySql.FunctionalTests"
            / "Specification"
            / "Contracts"
        )
        baseline = json.loads(
            (contracts / "SpecSuiteBaseline.json").read_text(encoding="utf-8")
        )
        latest = max(
            (
                version
                for version in baseline["efCoreVersions"]
                if version.startswith("10.0.")
            ),
            key=lambda version: tuple(map(int, version.split("."))),
        )

        result = subprocess.run(
            (
                "bash",
                str(REPOSITORY_ROOT / "eng/testing/check-spec-version-contract.sh"),
                latest,
            ),
            cwd=REPOSITORY_ROOT,
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)

    def test_release_run_id_cannot_escape_the_evidence_root(self) -> None:
        """Reject path traversal before an incomplete stage can replace evidence."""
        completed = subprocess.run(
            ["bash", str(ORCHESTRATOR), "--stage", "package"],
            check=False,
            capture_output=True,
            text=True,
            env={
                **os.environ,
                "DOKA_RELEASE_CANDIDATE_DEADLINE_ACTIVE": "1",
                "DOKA_RELEASE_CANDIDATE_RUN_ID": "../escape",
                "DOKA_RELEASE_VERSION": "10.0.0-test",
            },
        )

        self.assertNotEqual(0, completed.returncode)
        self.assertIn("path-safe ASCII identifier", completed.stderr)


if __name__ == "__main__":
    unittest.main()
