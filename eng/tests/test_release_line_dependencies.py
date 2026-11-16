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

    def test_framework_major_boundaries_survive_main_rollover(self) -> None:
        """The same relative guards preserve main/10 and later main/11 with maintenance/10."""
        for major in (10, 11):
            with self.subTest(main_major=major):
                # Arrange
                policy = policy_fixture()
                policy["lines"][0]["packageMajor"] = major
                policy["lines"][1]["packageMajor"] = 10
                policy["lines"][1]["state"] = "active"
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")

                # Act
                rendered = dependencies.render_configuration(policy, text)

                # Assert
                self.assertEqual(2, rendered.count('dependency-name: "Microsoft.Extensions.*"'))
                self.assertEqual(2, rendered.count("dependency-name: System.IO.Hashing"))
                self.assertEqual(2, rendered.count("open-pull-requests-limit: 2"))
                self.assertEqual(2, rendered.count("        versions:\n"))
                self.assertEqual(2, rendered.count('          - "[3.0.0, 4.0.0)"\n'))

    def test_framework_major_boundary_cannot_be_removed(self) -> None:
        """Both runtime-aligned families must keep their own-major exclusion."""
        cases = (("Microsoft.Extensions.*", '"Microsoft.Extensions.*"'),
                 ("System.IO.Hashing", "System.IO.Hashing"))
        for family, scalar in cases:
            with self.subTest(family=family):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                guard = (f"      - dependency-name: {scalar}\n"
                         "        update-types:\n          - version-update:semver-major\n")
                changed = text.replace(guard, "")

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertIn(f"relative major boundary for {family}", str(failure.exception))

    def test_framework_guards_cannot_hide_minor_or_patch_updates(self) -> None:
        """A family guard must neither freeze servicing nor hardcode the next main major."""
        for extra in ("          - version-update:semver-minor\n",
                      "          - version-update:semver-patch\n",
                      '        versions: ["11.*"]\n'):
            with self.subTest(extra=extra):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                guard = ('      - dependency-name: "Microsoft.Extensions.*"\n'
                         "        update-types:\n          - version-update:semver-major\n")
                changed = text.replace(guard, guard + extra)

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertIn("relative major boundary for Microsoft.Extensions.*", str(failure.exception))

    def test_independently_versioned_microsoft_tools_cannot_be_suppressed(self) -> None:
        """A broad Microsoft or wildcard major exclusion also hides unrelated tool releases."""
        for pattern in ("Microsoft.*", "*", "Microsoft.NET.*", "Microsoft.CodeAnalysis.*", "microsoft.*"):
            with self.subTest(pattern=pattern):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                guard = (f'      - dependency-name: "{pattern}"\n'
                         "        update-types:\n          - version-update:semver-major\n")
                before, header, nuget = text.partition("  - package-ecosystem: nuget\n")
                changed = before + header + nuget.replace("    ignore:\n", "    ignore:\n" + guard, 1)

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertIn("independently versioned Microsoft tools", str(failure.exception))

    def test_framework_rule_on_another_ecosystem_cannot_cover_nuget(self) -> None:
        """An Actions rule must not satisfy a missing NuGet package boundary."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        guard = ('      - dependency-name: "Microsoft.Extensions.*"\n'
                 "        update-types:\n          - version-update:semver-major\n")
        changed = text.replace(guard, "").replace(
            "  - package-ecosystem: github-actions\n",
            "  - package-ecosystem: github-actions\n    ignore:\n" + guard)

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("relative major boundary for Microsoft.Extensions.*", str(failure.exception))

    def test_additional_untyped_or_versioned_tool_exclusions_are_rejected(self) -> None:
        """An omitted update-types or a versions filter still suppresses matching updates."""
        guards = ('      - dependency-name: "Microsoft.*"\n',
                  '      - dependency-name: "Microsoft.NET.Test.Sdk"\n'
                  '        versions: ["18.*"]\n')
        for guard in guards:
            with self.subTest(guard=guard):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                before, header, nuget = text.partition("  - package-ecosystem: nuget\n")
                changed = before + header + nuget.replace("    ignore:\n", "    ignore:\n" + guard, 1)

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertIn("independently versioned Microsoft tools", str(failure.exception))

    def test_duplicate_rules_cannot_override_valid_boundaries(self) -> None:
        """All matching rules participate; reject a conflicting second family or driver rule."""
        cases = (("MySqlConnector", "major"), ('"Microsoft.Extensions.*"', "patch"))
        for package, update_type in cases:
            with self.subTest(package=package, update_type=update_type):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                before, header, nuget = text.partition("  - package-ecosystem: nuget\n")
                guard = (f"      - dependency-name: {package}\n"
                         f"        update-types:\n          - version-update:semver-{update_type}\n")
                changed = before + header + nuget.replace("    ignore:\n", "    ignore:\n" + guard, 1)

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertIn("repeats a dependency ignore rule", str(failure.exception))

    def test_unknown_ignore_rule_requires_deliberate_policy_review(self) -> None:
        """A new exclusion cannot silently hide another independent major."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        before, header, nuget = text.partition("  - package-ecosystem: nuget\n")
        guard = ("      - dependency-name: coverlet.collector\n"
                 "        update-types:\n          - version-update:semver-major\n")
        changed = before + header + nuget.replace("    ignore:\n", "    ignore:\n" + guard, 1)

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("unreviewed dependency ignore rule", str(failure.exception))

    def test_duplicate_nuget_mappings_cannot_replace_reviewed_rules(self) -> None:
        """A second groups/ignore/limit field must not change the mapping interpreted by YAML."""
        for extra in ("    groups: {}\n", "    ignore: []\n", "    open-pull-requests-limit: 1\n",
                      '    "groups": {}\n', '    "ignore": []\n',
                      '    "open-pull-requests-limit": 1\n'):
            with self.subTest(extra=extra):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                changed = text + extra

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertIn("NuGet target", str(failure.exception))

    def test_noncanonical_ignore_entry_cannot_hide_an_additional_exclusion(self) -> None:
        """Quoted keys must not disappear from the bounded canonical reader."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        before, header, nuget = text.partition("  - package-ecosystem: nuget\n")
        guard = '      - "dependency-name": "Microsoft.*"\n'
        changed = before + header + nuget.replace("    ignore:\n", "    ignore:\n" + guard, 1)

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("one canonical ignore mapping", str(failure.exception))

    def test_ef_version_filter_cannot_narrow_the_matrix_owned_exclusion(self) -> None:
        """An EF version filter must not re-enable automatic framework transitions."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        guard = '      - dependency-name: "Microsoft.EntityFrameworkCore.*"\n'
        changed = text.replace(guard, guard + '        versions: ["10.*"]\n')

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("EF target must preserve", str(failure.exception))

    def test_malformed_package_quotes_cannot_satisfy_a_required_boundary(self) -> None:
        """Quote removal must not turn a damaged scalar into a valid package family."""
        for scalar in ('"Microsoft.Extensions.*', "'Microsoft.Extensions.*",
                       'Microsoft.Extensions.*"', "Microsoft.Extensions.*'",
                       '""Microsoft.Extensions.*""'):
            with self.subTest(scalar=scalar):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                changed = text.replace('dependency-name: "Microsoft.Extensions.*"',
                                       f"dependency-name: {scalar}")

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertIn("one canonical ignore mapping", str(failure.exception))

    def test_maintenance_guard_cannot_borrow_main_rule(self) -> None:
        """Validate the secondary NuGet block even when main retains a valid guard."""
        # Arrange
        policy = policy_fixture()
        policy["lines"][0]["packageMajor"] = 11
        policy["lines"][1]["state"] = "active"
        rendered = dependencies.render_configuration(policy, BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii"))
        guard = ('      - dependency-name: "Microsoft.Extensions.*"\n'
                 "        update-types:\n          - version-update:semver-major\n")
        before, _, after = rendered.rpartition(guard)
        changed = before + after

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy, changed)

        # Assert
        self.assertIn("NuGet target 'maintenance/10.x'", str(failure.exception))

    def test_routine_group_cannot_accept_major_updates(self) -> None:
        """An added major cannot hide beside the two permitted update types."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        changed = text.replace("          - patch\n", "          - patch\n          - major\n")

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("group only routine minor/patch", str(failure.exception))

    def test_group_update_types_do_not_use_ignore_syntax(self) -> None:
        """Dependabot groups use minor/patch names, not ignore-rule prefixes."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        changed = text.replace("          - minor\n", "          - version-update:semver-minor\n")

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("group only routine minor/patch", str(failure.exception))

    def test_individual_major_proposals_leave_room_for_routine_group(self) -> None:
        """One unreviewed driver major must not consume the sole allowed proposal slot."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        changed = text.replace("open-pull-requests-limit: 2", "open-pull-requests-limit: 1")

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("permit routine and independent major", str(failure.exception))

    def test_driver_matrix_ownership_does_not_suppress_major_proposals(self) -> None:
        """The admitted 3.x line must not hide a future unqualified major."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        guard = ("      - dependency-name: MySqlConnector\n"
                 "        update-types:\n")
        changed = text.replace(guard, guard + "          - version-update:semver-major\n")

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy_fixture(), changed)

        # Assert
        self.assertIn("driver patch ownership and individual major", str(failure.exception))

    def test_admitted_driver_line_does_not_raise_the_shipping_minimum(self) -> None:
        """Only the already-admitted 3.x interval is excluded from floor proposals."""
        # Arrange
        text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
        policy = policy_fixture()

        # Act
        rendered = dependencies.render_configuration(policy, text)

        # Assert
        self.assertEqual(text, rendered)
        self.assertEqual(1, rendered.count('          - "[3.0.0, 4.0.0)"\n'))
        driver = rendered.split("      - dependency-name: MySqlConnector\n", 1)[1]
        self.assertNotIn("version-update:semver-major", driver)

    def test_driver_exclusion_cannot_omit_or_change_the_admitted_interval(self) -> None:
        """Missing or altered bounds either restore floor churn or hide future majors."""
        excluded = '        versions:\n          - "[3.0.0, 4.0.0)"\n'
        cases = ("", '        versions: []\n',
                 '        versions:\n          - "[3.0.0]"\n',
                 '        versions:\n          - "[3.0.0, 4.0.0]"\n',
                 '        versions:\n          - "[3.0.0, 5.0.0)"\n',
                 '        versions:\n          - "[3.0.0,)"\n',
                 '        versions:\n          - "[2.0.0, 4.0.0)"\n',
                 '        versions:\n          - "[4.0.0, 5.0.0)"\n',
                 '        versions:\n          - "*"\n',
                 excluded + '          - "[4.0.0, 5.0.0)"\n')
        for replacement in cases:
            with self.subTest(replacement=replacement):
                # Arrange
                text = BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii")
                changed = text.replace(excluded, replacement)

                # Act
                with self.assertRaises(dependencies.DependencyPolicyError) as failure:
                    dependencies.validate_configuration(policy_fixture(), changed)

                # Assert
                self.assertNotEqual(text, changed)
                self.assertIn("driver patch ownership and individual major", str(failure.exception))

    def test_maintenance_driver_exclusion_cannot_borrow_main_rule(self) -> None:
        """A correct main rule must not mask a missing admitted-line guard on maintenance."""
        # Arrange
        policy = policy_fixture()
        policy["lines"][0]["packageMajor"] = 11
        policy["lines"][1]["state"] = "active"
        rendered = dependencies.render_configuration(policy, BOOTSTRAP_CONFIGURATION.read_text(encoding="ascii"))
        excluded = '        versions:\n          - "[3.0.0, 4.0.0)"\n'
        before, _, after = rendered.rpartition(excluded)
        changed = before + after

        # Act
        with self.assertRaises(dependencies.DependencyPolicyError) as failure:
            dependencies.validate_configuration(policy, changed)

        # Assert
        self.assertIn("NuGet target 'maintenance/10.x'", str(failure.exception))
        self.assertIn("driver patch ownership and individual major", str(failure.exception))

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
        self.assertEqual(2, rendered.count('          - "[3.0.0, 4.0.0)"\n'))

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
