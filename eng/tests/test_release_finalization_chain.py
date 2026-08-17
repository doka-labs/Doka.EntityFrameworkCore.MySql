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

import os
import re
import subprocess
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ORCHESTRATOR = REPOSITORY_ROOT / "eng" / "release" / "release-candidate.sh"
WORKFLOW = REPOSITORY_ROOT / ".github" / "workflows" / "release-candidate.yml"
TEST_ENTRY = REPOSITORY_ROOT / "eng" / "testing" / "test.sh"
MIGRATION_GATE = REPOSITORY_ROOT / "eng" / "testing" / "test-migration-deployment.sh"
MIGRATION_MODEL = REPOSITORY_ROOT / "eng" / "quality" / "check-migration-model.sh"

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
            "minimum-2-5-0",
            "latest-2-x",
        ):
            with self.subTest(leg=leg):
                self.assertEqual(1, body.count(f'"{leg}"'))

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
