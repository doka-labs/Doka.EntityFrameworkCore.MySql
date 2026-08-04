"""Regression tests for hosted release-workflow security boundaries."""

from __future__ import annotations

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

    def test_named_workflow_steps_have_one_action_binding(self) -> None:
        """Reject duplicate uses keys that permissive YAML loaders overwrite."""
        for path in sorted(self.workflows.glob("*.yml")):
            text = path.read_text(encoding="utf-8")

            for index, step in enumerate(
                text.split("\n      - name: ")[1:],
                start=1,
            ):
                step = step.split("\n      - name: ", 1)[0]
                step_name = step.splitlines()[0].strip()
                action_bindings = [
                    line
                    for line in step.splitlines()
                    if line.startswith("        uses: ")
                ]

                with self.subTest(
                    workflow=path.name,
                    step=index,
                    name=step_name,
                ):
                    self.assertLessEqual(len(action_bindings), 1)

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

        self.assertIn("bash ./eng/verify-dotnet.sh", preflight)
        self.assertIn("bash -n ./eng/release-candidate.sh", preflight)
        self.assertIn(
            "bash -n ./eng/restore-release-stage-artifacts.sh",
            preflight,
        )
        self.assertIn("eng.tests.test_release_artifact_resolver", preflight)
        self.assertIn("eng.tests.test_release_stage_checkpoint", preflight)
        self.assertIn("eng.tests.test_release_workflow_policy", preflight)
        self.assertLess(
            text.index("- name: Verify release tooling contracts"),
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
            readback.index("bash eng/test-nuget-readback.sh"),
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
        self.assertIn("python3 eng/github_release.py prepare", finalize)
        self.assertIn("python3 eng/github_release.py publish", finalize)
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
        text = (self.repo / "eng" / "github_release.py").read_text(
            encoding="utf-8"
        )

        self.assertIn('"--verify-tag"', text)
        self.assertNotIn('"--clobber"', text)
        self.assertNotIn('"--target"', text)


if __name__ == "__main__":
    unittest.main()
