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

    def test_hosted_workflows_do_not_select_moving_sdk_channels(self) -> None:
        """Require setup-dotnet to consume the exact repository SDK contract."""
        for path in sorted(self.workflows.glob("*.yml")):
            text = path.read_text(encoding="utf-8")

            if "actions/setup-dotnet@" not in text:
                continue

            with self.subTest(workflow=path.name):
                self.assertNotIn("dotnet-version:", text)
                self.assertIn("global-json-file: global.json", text)

    def test_publication_verifies_sdk_before_requesting_credentials(self) -> None:
        """Keep exact SDK enforcement ahead of the NuGet trusted-publishing step."""
        text = (self.workflows / "nuget-publish.yml").read_text(encoding="utf-8")

        self.assertLess(
            text.index("- name: Verify approved .NET SDK"),
            text.index("- name: Request short-lived NuGet.org key"),
        )

    def test_candidate_verifies_sdk_before_building_artifacts(self) -> None:
        """Keep exact SDK enforcement ahead of release-candidate qualification."""
        text = (self.workflows / "release-candidate.yml").read_text(encoding="utf-8")

        self.assertLess(
            text.index("- name: Verify approved .NET SDK"),
            text.index("- name: Run repo-local release-candidate flow"),
        )

    def test_sdk_contract_has_a_reviewed_update_channel(self) -> None:
        """Keep the exact SDK pin visible to scheduled dependency review."""
        text = (self.repo / ".github" / "dependabot.yml").read_text(encoding="utf-8")
        section_start = text.index("- package-ecosystem: dotnet-sdk")
        section_end = text.index("- package-ecosystem: nuget", section_start)
        section = text[section_start:section_end]

        self.assertIn("directory: /", section)
        self.assertIn("interval: weekly", section)
        self.assertIn("open-pull-requests-limit: 1", section)

    def test_github_release_finalization_follows_public_nuget_readback(self) -> None:
        """Confine repository write authority to the post-readback job."""
        text = (self.workflows / "nuget-publish.yml").read_text(encoding="utf-8")
        job_start = text.index("  finalize-github-release:")
        job = text[job_start:]

        self.assertEqual(1, text.count("contents: write"))
        self.assertIn("needs: publish-and-read-back", job)
        self.assertIn("actions: read", job)
        self.assertIn("contents: write", job)
        self.assertNotIn("id-token: write", job)
        self.assertNotIn("attestations: read", job)
        self.assertNotIn("environment:", job)
        self.assertLess(
            text.index("bash eng/test-nuget-readback.sh"),
            job_start,
        )

    def test_github_release_finalization_preserves_verified_evidence(self) -> None:
        """Require the final job to consume and retain both evidence domains."""
        text = (self.workflows / "nuget-publish.yml").read_text(encoding="utf-8")
        job = text[text.index("  finalize-github-release:") :]

        self.assertIn("nuget-publication-evidence-${{ inputs.release_tag }}", job)
        self.assertIn("python3 eng/github_release.py prepare", job)
        self.assertIn("python3 eng/github_release.py publish", job)
        self.assertIn("github-release-plan.json", job)
        self.assertIn("github-release-readback.json", job)
        self.assertIn("github-release-evidence-${{ inputs.release_tag }}", job)

    def test_github_release_helper_cannot_create_tags_or_replace_assets(self) -> None:
        """Keep tag creation and destructive asset replacement out of scope."""
        text = (self.repo / "eng" / "github_release.py").read_text(encoding="utf-8")

        self.assertIn('"--verify-tag"', text)
        self.assertNotIn('"--clobber"', text)
        self.assertNotIn('"--target"', text)


if __name__ == "__main__":
    unittest.main()
