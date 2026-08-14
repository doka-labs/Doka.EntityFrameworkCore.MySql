"""Hold the finalizer's evidence demands against what the tag run produces.

The finalizer demanded evidence the release workflow had stopped producing: an
audit tree from a stage that no longer runs, two matrices proven on the branch,
and a historical performance layout the paired comparison replaced. Every part
passed its own test. Nothing compared the demands against the artifact set the
workflow actually assembles, so the mismatch stayed invisible until a release
candidate reached it -- which is after every expensive gate has run.

Running the finalizer itself is out of reach here: it needs a clean worktree
and a compiled Release assembly for publication readiness, neither of which
this suite produces. What is reachable, and what would have caught the break,
is the comparison. Every path the finalizer requires is extracted from the
orchestrator and matched against the stage that produces it and the workflow
step that restores it.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ORCHESTRATOR = REPOSITORY_ROOT / "eng" / "release" / "release-candidate.sh"
WORKFLOW = REPOSITORY_ROOT / ".github" / "workflows" / "release-candidate.yml"

# Which stage produces the evidence behind each orchestrator path variable.
# `benchmark` is the paired comparison, restored into the assemble job from the
# scorecard rather than from a release stage.
PRODUCERS = {
    "packages_dir": "package",
    "sbom_dir": "sbom",
    "migration_deployment_root": "migration-deployment",
    "runtime_dir": "runtime",
    "efcore_matrix_dir": "efcore-patch-matrix",
    "mysqlconnector_matrix_dir": "mysqlconnector-patch-matrix",
    "performance_dir": "benchmark",
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
            if stage in (None, "benchmark", "finalize"):
                continue
            with self.subTest(variable=variable, stage=stage):
                self.assertIn(
                    stage,
                    restored,
                    f"{variable} comes from stage '{stage}', which the assemble "
                    "job does not restore",
                )

    def test_the_paired_evidence_is_restored_before_finalizing(self) -> None:
        """Prove the one non-stage input reaches the job that consumes it."""
        workflow = WORKFLOW.read_text(encoding="utf-8")
        assemble = workflow[workflow.index("\n  assemble:") :]
        restore = assemble[: assemble.index("Assemble immutable release candidate")]

        self.assertIn("pattern: benchmark-artifacts-*", restore)
        self.assertIn(".requiredTargets | keys[]", restore)

    def test_the_required_stage_set_matches_the_dispatched_stages(self) -> None:
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
        dispatched = set(re.findall(r"^\s+- stage: (\S+)$", workflow, re.M))

        self.assertEqual(dispatched | {"sbom"}, expected)

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
        ):
            with self.subTest(directory=retired):
                self.assertNotIn(f'"${{{retired}}}"', body)


if __name__ == "__main__":
    unittest.main()
