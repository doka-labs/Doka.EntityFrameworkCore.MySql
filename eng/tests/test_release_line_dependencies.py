"""Prove active-line parity without enabling inactive Dependabot targets."""

from __future__ import annotations

import unittest
from pathlib import Path

from eng.release import dependencies, lines


ROOT = Path(__file__).resolve().parents[2]
BOOTSTRAP_CONFIGURATION = Path(__file__).parent / "fixtures/dependabot-initial.yml"


class DependencyTargetTests(unittest.TestCase):
    """Keep four ecosystems complete and inactive refs absent."""

    def test_current_configuration_matches_active_policy(self) -> None:
        """Delivered targets follow current activation rather than bootstrap state."""
        # Arrange
        policy = lines.load_policy(ROOT / "eng/release/lines-policy.json")
        text = (ROOT / ".github/dependabot.yml").read_text()
        expected = dependencies.expected_targets(policy)
        inactive = {line["sourceRef"].removeprefix("refs/heads/")
                    for line in policy["lines"] if line["state"] != "active"}

        # Act
        rendered = dependencies.render_configuration(policy, text)
        observed = {(ecosystem, target)
                    for ecosystem, target, _ in dependencies._entries(rendered)}

        # Assert
        self.assertEqual(text, rendered)
        self.assertEqual(expected, observed)
        self.assertTrue(inactive.isdisjoint(target for _, target in observed))

    def test_activation_renders_all_four_additional_ecosystems(self) -> None:
        """One line transition must not leave SDK or Actions maintenance behind."""
        # Arrange
        policy = policy_fixture()
        policy["lines"][1]["state"] = "active"
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")

        # Act
        rendered = dependencies.render_configuration(policy, text)

        # Assert
        self.assertEqual(4, rendered.count("target-branch: maintenance/10.x"))
        self.assertNotIn("target-branch: preview/11.x", rendered)
        self.assertEqual(rendered, dependencies.render_configuration(policy, rendered))

    def test_inactive_target_is_rejected(self) -> None:
        """A proposed YAML target cannot activate a line by itself."""
        # Arrange
        policy = policy_fixture()
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii").replace(
            "  - package-ecosystem: nuget\n",
            "  - package-ecosystem: nuget\n    target-branch: preview/11.x\n")

        # Act
        with self.assertRaisesRegex(dependencies.DependencyPolicyError, "exactly once") as failure:
            dependencies.validate_configuration(policy, text)

        # Assert
        self.assertIn("active release lines", str(failure.exception))

    def test_unbounded_sdk_upgrade_is_rejected(self) -> None:
        """The ten-line update policy must not silently propose SDK eleven."""
        # Arrange
        policy = policy_fixture()
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii").replace(
            dependencies.SDK_MAJOR_BOUNDARY, "")

        # Act
        with self.assertRaisesRegex(dependencies.DependencyPolicyError, "major-version") as failure:
            dependencies.validate_configuration(policy, text)

        # Assert
        self.assertIn("SDK", str(failure.exception))

    def test_preview_activation_renders_its_own_exact_targets(self) -> None:
        """A preview update never borrows maintenance's target branch."""
        # Arrange
        policy = policy_fixture()
        policy["lines"][2]["state"] = "active"
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")

        # Act
        rendered = dependencies.render_configuration(policy, text)

        # Assert
        self.assertEqual(4, rendered.count("target-branch: preview/11.x"))
        self.assertNotIn("target-branch: maintenance/10.x", rendered)

    def test_duplicate_ecosystem_target_is_rejected(self) -> None:
        """Dependabot must not create competing updates for the same scope."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        duplicate = text + "\n  - package-ecosystem: github-actions\n    directory: /\n"

        # Act
        with self.assertRaisesRegex(dependencies.DependencyPolicyError, "exactly once") as failure:
            dependencies.validate_configuration(policy_fixture(), duplicate)

        # Assert
        self.assertIn("targets", str(failure.exception))

    def test_missing_ecosystem_target_is_rejected(self) -> None:
        """An incomplete active line cannot appear fully maintained."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        incomplete = text[:text.index("  - package-ecosystem: nuget")]

        # Act
        with self.assertRaisesRegex(dependencies.DependencyPolicyError, "exactly once") as failure:
            dependencies.validate_configuration(policy_fixture(), incomplete)

        # Assert
        self.assertIn("targets", str(failure.exception))

    def test_ef_matrix_boundary_cannot_be_removed(self) -> None:
        """All release lines retain deliberate EF floor/latest qualification."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii").replace(
            'dependency-name: "Microsoft.EntityFrameworkCore.*"', 'dependency-name: "Other.*"')

        # Act
        with self.assertRaisesRegex(dependencies.DependencyPolicyError, "floor/latest") as failure:
            dependencies.validate_configuration(policy_fixture(), text)

        # Assert
        self.assertIn("EF target", str(failure.exception))

    def test_image_series_boundary_cannot_be_removed(self) -> None:
        """Rendering does not weaken the pinned qualified database matrix."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii").replace(
            "dependency-name: mariadb", "dependency-name: otherdb")

        # Act
        with self.assertRaisesRegex(dependencies.DependencyPolicyError, "engine series") as failure:
            dependencies.validate_configuration(policy_fixture(), text)

        # Assert
        self.assertIn("image target", str(failure.exception))

    def test_noncanonical_target_cannot_hide_inactive_branch(self) -> None:
        """Unsupported YAML shapes fail closed instead of disappearing in parsing."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii").replace(
            "  - package-ecosystem: nuget\n",
            "  - package-ecosystem: nuget\n    target-branch : preview/11.x\n")

        # Act
        with self.assertRaisesRegex(dependencies.DependencyPolicyError, "target-branch") as failure:
            dependencies.validate_configuration(policy_fixture(), text)

        # Assert
        self.assertIn("entry", str(failure.exception))


def policy_fixture() -> dict:
    """Return the target-selection portion of the authority policy."""
    return {"authorityRef": "refs/heads/main", "lines": [
        {"sourceRef": "refs/heads/main", "state": "active"},
        {"sourceRef": "refs/heads/maintenance/10.x", "state": "inactive"},
        {"sourceRef": "refs/heads/preview/11.x", "state": "inactive"}]}


if __name__ == "__main__":
    unittest.main()
