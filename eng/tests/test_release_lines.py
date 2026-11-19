"""Contracts for exact release lines, frozen identity and branch toolchains."""

from __future__ import annotations

import copy
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from eng.release import lines


REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"
MAIN = "refs/heads/main"
MAINTENANCE = "refs/heads/maintenance/10.x"
PREVIEW = "refs/heads/preview/11.x"
INITIAL_POLICY_PATH = Path(__file__).with_name("fixtures") / "release-lines-initial.json"


def initial_policy() -> dict:
    """Return detached bootstrap test data, never the mutable current grant.

    WHY: a reviewed main-major rollover changes operational authorization,
    not the historical lifecycle and qualification scenarios under test.
    """
    return lines.load_policy(INITIAL_POLICY_PATH)


def ga_specification_baseline() -> dict[str, object]:
    """Return detached GA-10 patch-review data for historical source fixtures.

    WHY: the checked-out branch may prepare another upstream major; these
    lifecycle tests consume only the reviewed version set, not its full suite.
    """
    return {
        "schemaVersion": 1,
        "efCoreVersions": ["10.0.8", "10.0.10", "10.0.11", "10.0.12", "10.0.13"],
        "entries": [],
    }


def prepared_policy() -> dict:
    """Return detached qualification-only grants using the existing publisher."""
    policy = initial_policy()
    policy["lines"][1]["state"] = "prepared"
    policy["lines"][2]["state"] = "prepared"

    return policy


def shared_policy() -> dict:
    """Prepare secondary lines without introducing another publisher route."""
    return prepared_policy()


class PolicyTests(unittest.TestCase):
    """Reject ambiguous branch grants without changing publisher configuration."""

    def test_all_lines_share_existing_environment_without_publisher_migration(self) -> None:
        """Branch authorization never requires a credential or environment migration."""
        # Arrange
        policy = initial_policy()

        # Act
        validated = lines.validate_policy(policy)

        # Assert
        self.assertEqual({"nuget"}, {entry["publisherEnvironment"] for entry in validated["lines"]})
        self.assertNotIn("publisherMigration", validated)
        self.assertTrue(all("drainAdmissions" not in entry for entry in validated["lines"]))

    def test_fixture_helpers_do_not_follow_current_main_major_rollover(self) -> None:
        """Future current grants cannot rewrite fixed historical test inputs."""
        # Arrange
        future = shared_policy()
        future["revision"] = "2027-01-01.1"
        future["lines"][0]["packageMajor"] = 11
        future["lines"][1]["state"] = "active"
        future["lines"][2]["state"] = "inactive"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "current-policy.json"
            path.write_text(json.dumps(future), encoding="ascii")

            # Act
            with mock.patch.object(lines, "POLICY_PATH", path):
                current = lines.load_policy()
                initial = initial_policy()
                prepared = prepared_policy()

            # Assert
            self.assertEqual(11, current["lines"][0]["packageMajor"])
            self.assertEqual(10, initial["lines"][0]["packageMajor"])
            self.assertEqual(10, prepared["lines"][0]["packageMajor"])
            self.assertEqual({"nuget"}, {entry["publisherEnvironment"] for entry in current["lines"]})

    def test_shipped_policy_validates_its_current_authority_schema(self) -> None:
        """Validate live policy without permanently freezing its selected major."""
        # Arrange
        policy = lines.load_policy()

        # Act
        validated = lines.validate_policy(policy)

        # Assert
        self.assertEqual(policy, validated)
        self.assertEqual(1, validated["schemaVersion"])
        self.assertEqual(REPOSITORY, validated["repository"])
        self.assertEqual(MAIN, validated["authorityRef"])

    def test_initial_fixture_keeps_secondary_lines_inactive(self) -> None:
        """Supporting a line neither activates it nor introduces another workflow."""
        # Arrange
        policy = initial_policy()

        # Act
        active = [entry for entry in policy["lines"] if entry["state"] == "active"]

        # Assert
        self.assertEqual([MAIN], [entry["sourceRef"] for entry in active])
        self.assertEqual(["inactive", "inactive"], [entry["state"] for entry in policy["lines"][1:]])
        self.assertEqual({lines.LINE_WORKFLOW_PATH}, {entry["workflowPath"] for entry in policy["lines"]})
        self.assertEqual({"nuget"}, {entry["publisherEnvironment"] for entry in policy["lines"]})

    def test_every_line_rejects_an_unregistered_workflow_or_environment(self) -> None:
        """Line data must not redirect publication into another credential route."""
        for index in range(3):
            for field, value in (("workflowPath", ".github/workflows/other.yml"),
                                 ("publisherEnvironment", "another-publisher")):
                with self.subTest(index=index, field=field):
                    # Arrange
                    policy = initial_policy()
                    policy["lines"][index][field] = value

                    # Act
                    with self.assertRaises(lines.LinePolicyError) as failure:
                        lines.validate_policy(policy)

                    # Assert
                    self.assertIn("workflow" if field == "workflowPath" else "publisher", str(failure.exception))

    def test_policy_digest_is_order_independent_but_revision_sensitive(self) -> None:
        """Bind reviewed content rather than JSON property layout."""
        # Arrange
        policy = initial_policy()
        reordered = dict(reversed(tuple(policy.items())))
        changed = copy.deepcopy(policy)
        changed["revision"] = "2026-10-22.2"

        # Act
        original = lines.policy_digest(policy)
        equivalent = lines.policy_digest(reordered)
        revised = lines.policy_digest(changed)

        # Assert
        self.assertEqual(original, equivalent)
        self.assertNotEqual(original, revised)

    def test_policy_rejects_unknown_and_incorrectly_typed_fields(self) -> None:
        """Removed lifecycle mechanisms cannot be silently accepted as dead knobs."""
        mutations = (
            (None, "schemaVersion", True), (None, "schemaVersion", 2),
            (None, "revision", None), (None, "revision", " revision "),
            (None, "repository", "other/repository"),
            (None, "authorityRef", MAINTENANCE), (None, "lines", None), (None, "lines", []),
            (None, "publisherMigration", {}), (None, "unusedFutureKnob", True),
            (0, "packageMajor", True), (0, "packageMajor", 0), (0, "state", "enabled"),
            (0, "state", "draining"), (0, "state", "retired"),
            (0, "channel", "stable"), (0, "upstreamStage", "rc"), (0, "drainAdmissions", []),
        )

        for index, field, value in mutations:
            with self.subTest(index=index, field=field, value=value):
                # Arrange
                policy = initial_policy()
                target = policy if index is None else policy["lines"][index]
                target[field] = value

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.validate_policy(policy)

                # Assert
                self.assertIsInstance(failure.exception, lines.LinePolicyError)

    def test_missing_fields_are_rejected_at_every_policy_level(self) -> None:
        """Absent properties must never infer publication permission."""
        original = initial_policy()
        paths = ((), ("lines", 0))
        for path in paths:
            target = original if not path else original["lines"][0]
            for field in target:
                with self.subTest(path=path, field=field):
                    # Arrange
                    policy = copy.deepcopy(original)
                    changed = policy if not path else policy["lines"][0]
                    del changed[field]

                    # Act
                    with self.assertRaises(lines.LinePolicyError) as failure:
                        lines.validate_policy(policy)

                    # Assert
                    self.assertIn("exactly these fields", str(failure.exception))

    def test_role_aliases_are_rejected(self) -> None:
        """Literal approved branch names cannot become patterns or aliases."""
        for source_ref in ("refs/heads/version/10", "refs/heads/support/10.x", "refs/heads/maintenance/10.*"):
            with self.subTest(source_ref=source_ref):
                # Arrange
                policy = initial_policy()
                policy["lines"][1]["sourceRef"] = source_ref

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.validate_policy(policy)

                # Assert
                self.assertIn("exact approved identity", str(failure.exception))

    def test_duplicate_source_refs_are_rejected(self) -> None:
        """The same branch may not acquire two ambiguous grants."""
        # Arrange
        policy = initial_policy()
        policy["lines"].append(copy.deepcopy(policy["lines"][0]))

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.validate_policy(policy)

        # Assert
        self.assertIn("duplicate sourceRef", str(failure.exception))

    def test_shared_entry_supports_two_maintenance_lines_and_newer_main(self) -> None:
        """Each major may use its own branch while sharing the existing publisher."""
        # Arrange
        policy = shared_policy()
        policy["lines"][0]["packageMajor"] = 12
        policy["lines"][1]["state"] = "active"
        policy["lines"][2].update(sourceRef="refs/heads/preview/13.x", packageMajor=13, state="active")
        maintenance = copy.deepcopy(policy["lines"][1])
        maintenance.update(sourceRef="refs/heads/maintenance/11.x", packageMajor=11)
        policy["lines"].append(maintenance)

        # Act
        validated = lines.validate_policy(policy)

        # Assert
        self.assertEqual(
            {MAIN: 12, MAINTENANCE: 10, "refs/heads/maintenance/11.x": 11, "refs/heads/preview/13.x": 13},
            {entry["sourceRef"]: entry["packageMajor"] for entry in validated["lines"]},
        )
        self.assertTrue(all(entry["state"] == "active" for entry in validated["lines"]))
        self.assertEqual({"nuget"}, {entry["publisherEnvironment"] for entry in validated["lines"]})
        self.assertEqual({lines.LINE_WORKFLOW_PATH}, {entry["workflowPath"] for entry in validated["lines"]})

    def test_active_routes_for_same_major_are_mutually_exclusive(self) -> None:
        """Main and maintenance cannot both publish the same package major."""
        # Arrange
        policy = shared_policy()
        policy["lines"][1]["state"] = "active"

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.validate_policy(policy)

        # Assert
        self.assertIn("major 10", str(failure.exception))

    def test_main_and_maintenance_can_publish_distinct_majors(self) -> None:
        """A reviewed main upgrade does not require a publisher migration."""
        # Arrange
        policy = shared_policy()
        policy["lines"][0]["packageMajor"] = 11
        policy["lines"][1]["state"] = "active"
        policy["lines"][2]["state"] = "inactive"

        # Act
        validated = lines.validate_policy(policy)

        # Assert
        self.assertEqual(
            [(MAIN, 11), (MAINTENANCE, 10)],
            [(entry["sourceRef"], entry["packageMajor"])
             for entry in validated["lines"] if entry["state"] == "active"],
        )

    def test_loader_rejects_duplicate_json_keys(self) -> None:
        """Different parsers must not disagree about the selected schema."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "policy.json"
            path.write_text('{"schemaVersion":1,"schemaVersion":2}', encoding="ascii")

            # Act
            with self.assertRaises(lines.LinePolicyError) as failure:
                lines.load_policy(path)

            # Assert
            self.assertIn("duplicate", str(failure.exception))

    def test_loader_rejects_symbolic_links(self) -> None:
        """Policy identity must not depend on a moving link target."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "policy.json"
            path.symlink_to(lines.POLICY_PATH)

            # Act
            with self.assertRaises(lines.LinePolicyError) as failure:
                lines.load_policy(path)

            # Assert
            self.assertIn("regular", str(failure.exception))

    def test_loader_rejects_missing_malformed_or_nonobject_data(self) -> None:
        """Read failures must remain errors rather than implicit defaults."""
        for value in ("{", "null", "[]", "true"):
            with self.subTest(value=value), tempfile.TemporaryDirectory() as directory:
                # Arrange
                path = Path(directory) / "policy.json"
                path.write_text(value, encoding="ascii")

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.load_policy(path)

                # Assert
                self.assertIsInstance(failure.exception, lines.LinePolicyError)

    def test_loader_rejects_an_absent_policy(self) -> None:
        """An unavailable file cannot silently reuse another line's policy."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "absent.json"

            # Act
            with self.assertRaises(lines.LinePolicyError) as failure:
                lines.load_policy(path)

            # Assert
            self.assertIn("regular file", str(failure.exception))


class SelectionAndSnapshotTests(unittest.TestCase):
    """Separate frozen qualification from current publication permission."""

    def test_prepared_line_can_qualify_without_publication(self) -> None:
        """Preparation selects the full branch contract without granting writes."""
        # Arrange
        policy = prepared_policy()

        # Act
        selected = lines.select_line(policy, PREVIEW, "11.0.0-rc.7")

        # Assert
        self.assertEqual("prepared", selected["state"])
        self.assertEqual("nuget", selected["publisherEnvironment"])

    def test_prepared_line_cannot_publish(self) -> None:
        """Successful qualification-only preparation cannot admit publication."""
        # Arrange
        policy = prepared_policy()

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.select_line(policy, PREVIEW, "11.0.0-rc.7", purpose="publication")

        # Assert
        self.assertIn("not eligible for publication", str(failure.exception))

    def test_inactive_lines_never_qualify_or_publish(self) -> None:
        """Approval of a name grants neither candidate admission nor writes."""
        for source, version in ((MAINTENANCE, "10.7.0"), (PREVIEW, "11.0.0-rc.1")):
            for purpose in ("qualification", "publication"):
                with self.subTest(source=source, purpose=purpose):
                    # Arrange
                    policy = initial_policy()

                    # Act
                    with self.assertRaises(lines.LinePolicyError) as failure:
                        lines.select_line(policy, source, version, purpose=purpose)

                    # Assert
                    self.assertIn("not eligible", str(failure.exception))

    def test_inactive_lines_remain_inspectable(self) -> None:
        """Disabled publication does not erase reviewed line metadata."""
        # Arrange
        policy = initial_policy()

        # Act
        selected = lines.select_line(policy, MAINTENANCE, purpose="inspect")

        # Assert
        self.assertEqual("inactive", selected["state"])

    def test_active_maintenance_and_preview_lines_can_publish_their_own_versions(self) -> None:
        """All branches use the same publisher while retaining major/channel restrictions."""
        for source, version, main_major in ((MAINTENANCE, "10.7.0", 11), (PREVIEW, "11.0.0-rc.7", 10)):
            with self.subTest(source=source, version=version):
                # Arrange
                policy = shared_policy()
                policy["lines"][0]["packageMajor"] = main_major
                target = next(entry for entry in policy["lines"] if entry["sourceRef"] == source)
                target["state"] = "active"

                # Act
                selected = lines.select_line(policy, source, version, purpose="publication")

                # Assert
                self.assertEqual(source, selected["sourceRef"])
                self.assertEqual("active", selected["state"])
                self.assertEqual("nuget", selected["publisherEnvironment"])

    def test_snapshot_remains_verifiable_after_line_deactivation(self) -> None:
        """Deactivation revokes new writes without rewriting original provenance."""
        # Arrange
        policy = prepared_policy()
        snapshot = lines.freeze_line(policy, PREVIEW, "11.0.0-rc.7")
        policy["lines"][2]["state"] = "inactive"

        # Act
        historical = lines.validate_snapshot(snapshot, source_ref=PREVIEW, version="11.0.0-rc.7")

        # Assert
        self.assertEqual("prepared", historical["stateAtQualification"])
        self.assertEqual("prerelease", historical["channel"])
        self.assertEqual("rc", historical["upstreamStage"])
        self.assertEqual("2026-10-22.1", historical["authorizationRevision"])
        self.assertEqual(64, len(historical["authorizationDigest"]))
        self.assertEqual("inactive", policy["lines"][2]["state"])

    def test_frozen_main_snapshots_preserve_original_authorization_revision(self) -> None:
        """Policy changes do not relabel the environment of already qualified bytes."""
        # Arrange
        original = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        changed = initial_policy()
        changed["revision"] = "2026-10-24.1"
        current = lines.freeze_line(changed, MAIN, "10.7.0")

        # Act
        historical = lines.validate_snapshot(original, source_ref=MAIN, version="10.7.0")

        # Assert
        self.assertEqual(original, historical)
        self.assertEqual("nuget", historical["publisherEnvironment"])
        self.assertEqual("nuget", current["publisherEnvironment"])
        self.assertNotEqual(historical["authorizationDigest"], current["authorizationDigest"])

    def test_selection_returns_an_independent_copy(self) -> None:
        """A consumer cannot mutate the current grant through its selected object."""
        # Arrange
        policy = initial_policy()

        # Act
        selected = lines.select_line(policy, MAIN, "10.7.0")

        # Assert
        self.assertEqual(policy["lines"][0], selected)
        self.assertIsNot(policy["lines"][0], selected)

    def test_selection_rejects_unknown_purpose(self) -> None:
        """A misspelled permission mode must not relax the state boundary."""
        # Arrange
        policy = prepared_policy()

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.select_line(policy, MAIN, "10.7.0", purpose="publish")

        # Assert
        self.assertIn("purpose", str(failure.exception))

    def test_qualification_rejects_an_unregistered_signer(self) -> None:
        """A valid branch cannot select a different signing workflow."""
        # Arrange
        policy = prepared_policy()

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.freeze_line(
                policy, PREVIEW, "11.0.0-rc.7", workflow_path=".github/workflows/release-lines.yml",
            )

        # Assert
        self.assertIn("signer workflow", str(failure.exception))

    def test_every_frozen_snapshot_field_is_mandatory(self) -> None:
        """Historical consumers cannot infer absent qualification identity."""
        original = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        for key in original:
            with self.subTest(key=key):
                # Arrange
                snapshot = copy.deepcopy(original)
                del snapshot[key]

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.validate_snapshot(snapshot)

                # Assert
                self.assertIn("exactly these fields", str(failure.exception))

    def test_version_major_channel_and_exact_ref_are_load_bearing(self) -> None:
        """An upstream-GA preview still cannot publish a stable Doka package."""
        cases = (
            (PREVIEW, "10.7.0-rc.1"), (PREVIEW, "11.0.0"), (MAIN, "11.0.0"),
            ("feature/dotnet-11", "11.0.0-rc.1"),
        )

        for source, version in cases:
            with self.subTest(source=source, version=version):
                # Arrange
                policy = prepared_policy()
                policy["lines"][2]["upstreamStage"] = "ga"

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.freeze_line(policy, source, version)

                # Assert
                self.assertIsInstance(failure.exception, lines.LinePolicyError)

    def test_snapshot_rejects_tampering_and_unknown_fields(self) -> None:
        """A frozen identity is strict rather than a bag of optional hints."""
        original = lines.freeze_line(prepared_policy(), PREVIEW, "11.0.0-rc.7")
        mutations = {
            "schemaVersion": True, "role": "main", "sourceRef": "refs/heads/preview/11.*",
            "packageMajor": True, "channel": "stable", "upstreamStage": "future",
            "stateAtQualification": "inactive", "workflowPath": ".github/workflows/other.yml",
            "publisherEnvironment": "another-publisher", "authorizationDigest": "A" * 64, "extra": True,
        }

        for key, value in mutations.items():
            with self.subTest(key=key):
                # Arrange
                snapshot = copy.deepcopy(original)
                snapshot[key] = value

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.validate_snapshot(snapshot, version="11.0.0-rc.7")

                # Assert
                self.assertIsInstance(failure.exception, lines.LinePolicyError)

    def test_snapshot_rejects_mismatched_expected_identity(self) -> None:
        """Verification must bind independently supplied source and signer identities."""
        for arguments in ({"source_ref": MAIN}, {"version": "10.7.0"}, {"repository": "other/repo"},
                          {"workflow_path": ".github/workflows/other.yml"}):
            with self.subTest(arguments=arguments):
                # Arrange
                snapshot = lines.freeze_line(prepared_policy(), PREVIEW, "11.0.0-rc.7")

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.validate_snapshot(snapshot, **arguments)

                # Assert
                self.assertIn("identity" if "version" not in arguments else "major", str(failure.exception))

    def test_legacy_identity_is_main_only_without_new_major_policy(self) -> None:
        """Actual historical main releases retain their original package major."""
        # Arrange
        version = "1.2.3"

        # Act
        accepted = lines.validate_legacy_identity(MAIN, version, lines.LEGACY_WORKFLOW_PATH)

        # Assert
        self.assertIsNone(accepted)

    def test_legacy_identity_rejects_other_refs_and_unpublished_workflows(self) -> None:
        """Historical formats cannot grant another branch or signer new rights."""
        for source, workflow in ((MAINTENANCE, None), (MAIN, ".github/workflows/release-lines.yml")):
            with self.subTest(source=source, workflow=workflow):
                # Arrange
                version = "10.6.1"

                # Act
                with self.assertRaises(lines.LinePolicyError) as failure:
                    lines.validate_legacy_identity(source, version, workflow)

                # Assert
                self.assertIn("original main-only workflow identity", str(failure.exception))


class VersionTests(unittest.TestCase):
    """Use numeric canonical NuGet/SemVer precedence, never string ordering."""

    def test_canonical_stable_and_prerelease_versions_are_parsed(self) -> None:
        """Doka's RC sequence need not match the selected upstream RC."""
        # Arrange
        value = "11.0.0-rc.10"

        # Act
        parsed = lines.parse_version(value)

        # Assert
        self.assertEqual((11, 0, 0, ("rc", "10")),
                         (parsed.major, parsed.minor, parsed.patch, parsed.prerelease))
        self.assertTrue(parsed.is_prerelease)

    def test_noncanonical_or_nonstring_version_identities_are_rejected(self) -> None:
        """NuGet aliases must not produce conflicting tag or Latest identities."""
        # Arrange
        values = (None, True, 11, "v11.0.0", "11.0", "011.0.0", "11.00.0", "11.0.00",
                  "11.0.0-RC.1", "11.0.0-rc.01", "11.0.0+build", "11.0.0 ", "11.0.0-", "11.0.0-r\u00e9c.1")

        # Act
        rejected = 0
        for value in values:
            with self.subTest(value=value):
                with self.assertRaises(lines.LinePolicyError):
                    lines.parse_version(value)

            rejected += 1

        # Assert
        self.assertEqual(len(values), rejected)

    def test_semver_precedence_covers_numeric_identifiers_and_superseding_patches(self) -> None:
        """An older slow publication cannot demote a newer stable release."""
        # Arrange
        pairs = (("10.9.9", "11.0.0"), ("11.0.1", "11.0.2"), ("11.0.0-rc.9", "11.0.0-rc.10"),
                 ("11.0.0-rc.10", "11.0.0"), ("11.0.0-alpha", "11.0.0-alpha.1"),
                 ("11.0.0-1", "11.0.0-alpha"), ("11.0.0-beta", "11.0.0-rc"))

        # Act
        comparisons = [(lines.compare_versions(left, right), lines.compare_versions(right, left),
                        lines.compare_versions(left, left)) for left, right in pairs]

        # Assert
        self.assertEqual([(-1, 1, 0)] * len(pairs), comparisons)


class ToolchainTests(unittest.TestCase):
    """Keep source-owned framework qualification distinct from authorization."""

    def setUp(self) -> None:
        """Create a minimal branch-owned contract without a restore or install."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name)
        self.write_toolchain()
        self.baseline_path = self.repo / lines.SPECIFICATION_BASELINE_PATH
        self.baseline_path.parent.mkdir(parents=True)
        self.baseline_path.write_text(json.dumps(ga_specification_baseline()), encoding="ascii")

    def write_toolchain(
        self,
        *,
        sdk: str = "10.0.402",
        tfm: str = "net10.0",
        ef: str = "[10.0.8, 10.1.0)",
        prerelease: bool = False,
        runner: str | None = None,
    ) -> None:
        """Materialize the actual configuration shapes consumed by the reader."""
        global_json = {"sdk": {"version": sdk, "rollForward": "disable", "allowPrerelease": prerelease}}
        if runner is not None:
            global_json["test"] = {"runner": runner}

        (self.repo / "global.json").write_text(json.dumps(global_json), encoding="ascii")
        (self.repo / "Directory.Build.props").write_text(
            f"<Project><PropertyGroup><TargetFramework>{tfm}</TargetFramework></PropertyGroup></Project>",
            encoding="ascii",
        )
        (self.repo / "Directory.Packages.props").write_text(
            "<Project><PropertyGroup><DokaEfCoreVersion>" + ef + "</DokaEfCoreVersion></PropertyGroup>"
            "<ItemGroup><PackageVersion Include=\"Microsoft.EntityFrameworkCore.Relational\" "
            "Version=\"$(DokaEfCoreVersion)\" /></ItemGroup></Project>",
            encoding="ascii",
        )

    def test_current_branch_contract_matches_ga_line(self) -> None:
        """Build SDK is explicit without raising consumer runtime requirements."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")

        # Act
        result = lines.inspect_toolchain(self.repo, snapshot, resolved_packages={
            "Microsoft.EntityFrameworkCore.Design": "10.0.11",
            "Microsoft.EntityFrameworkCore.Relational": "10.0.11", "MySqlConnector": "2.5.0"})

        # Assert
        self.assertEqual({"approvedDotnetSdk": "10.0.402", "targetFramework": "net10.0",
                          "efCoreVersion": "[10.0.8, 10.1.0)", "testRunner": "VSTest", "upstreamStage": "ga"}, result)

    def test_ga_line_cannot_borrow_another_major_specification_baseline(self) -> None:
        """A matching new SDK/EF graph does not review a new major's patches."""
        # Arrange
        policy = shared_policy()
        policy["lines"][0]["packageMajor"] = 11
        self.write_toolchain(sdk="11.0.100", tfm="net11.0", ef="[11.0.0, 11.1.0)")
        snapshot = lines.freeze_line(policy, MAIN, "11.0.0")

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("selected stable EF major/minor", str(failure.exception))

    def test_future_ga_line_accepts_its_own_synthetic_reviewed_patch_baseline(self) -> None:
        """The source preflight is generic, not a permanent EF10 restriction."""
        # Arrange
        policy = shared_policy()
        policy["lines"][0]["packageMajor"] = 11
        self.write_toolchain(sdk="11.0.100", tfm="net11.0", ef="[11.0.0, 11.1.0)",
                             runner="Microsoft.Testing.Platform")
        baseline = json.loads(self.baseline_path.read_text(encoding="ascii"))
        # WHY: this disposable synthetic policy proves future-major admission,
        # not upstream GA availability or actual specification qualification.
        baseline["efCoreVersions"] = ["11.0.0"]
        for entry in baseline["entries"]:
            entry["efCoreVersions"] = ["11.0.0"]

        self.baseline_path.write_text(json.dumps(baseline), encoding="ascii")
        snapshot = lines.freeze_line(policy, MAIN, "11.0.0")

        # Act
        result = lines.inspect_toolchain(self.repo, snapshot, resolved_packages={
            "Microsoft.EntityFrameworkCore.Design": "11.0.0",
            "Microsoft.EntityFrameworkCore.Relational": "11.0.0"})

        # Assert
        self.assertEqual({"approvedDotnetSdk": "11.0.100", "targetFramework": "net11.0",
                          "efCoreVersion": "[11.0.0, 11.1.0)", "testRunner": "Microsoft.Testing.Platform",
                          "upstreamStage": "ga"}, result)

    def test_preview_role_with_ga_dependencies_requires_its_own_ga_baseline(self) -> None:
        """Prerelease Doka packages do not exempt a GA upstream toolchain."""
        # Arrange
        policy = prepared_policy()
        policy["lines"][2]["upstreamStage"] = "ga"
        self.write_toolchain(sdk="11.0.100", tfm="net11.0", ef="[11.0.0, 11.1.0)")
        snapshot = lines.freeze_line(policy, PREVIEW, "11.0.0-rc.7")

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("selected stable EF major/minor", str(failure.exception))

    def test_ga_line_requires_a_regular_branch_owned_specification_baseline(self) -> None:
        """Missing reviewed source policy cannot silently use the EF range."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        self.baseline_path.unlink()

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("GA specification patch baseline must be a regular file", str(failure.exception))

    def test_ga_line_rejects_a_symbolic_link_specification_baseline(self) -> None:
        """The policy is a checked-out source artifact, not an indirect alias."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        baseline_source = Path(__file__).resolve().parents[2] / lines.SPECIFICATION_BASELINE_PATH
        self.baseline_path.unlink()
        self.baseline_path.symlink_to(baseline_source)

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("GA specification patch baseline must be a regular file", str(failure.exception))

    def test_ga_line_rejects_malformed_or_empty_specification_patch_policies(self) -> None:
        """Malformed schema, JSON and patch lists do not become empty grants."""
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        cases = ("{", "null", "[]", "{}", '{"schemaVersion":true,"efCoreVersions":["10.0.8"]}',
                 '{"schemaVersion":2,"efCoreVersions":["10.0.8"]}',
                 '{"schemaVersion":1}', '{"schemaVersion":1,"efCoreVersions":[]}',
                 '{"schemaVersion":1,"efCoreVersions":"10.0.8"}')

        for payload in cases:
            with self.subTest(payload=payload):
                # Arrange
                self.baseline_path.write_text(payload, encoding="ascii")

                # Act
                try:
                    lines.inspect_toolchain(self.repo, snapshot)
                except lines.LinePolicyError as exception:
                    failure = exception
                else:
                    failure = None

                # Assert
                self.assertIsInstance(failure, lines.LinePolicyError)
                self.assertIn("GA specification patch baseline", str(failure))

    def test_ga_patch_policy_rejects_noncanonical_duplicate_and_incompatible_versions(self) -> None:
        """Every admitted patch must belong to the exact reviewed stable lane."""
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        versions = (None, True, {}, "10.0", "10.0.08", "10.0.8+build", "10.0.8-rc.1",
                    "10.0.8", "10.0.7", "10.1.0", "11.0.0")

        for version in versions:
            with self.subTest(version=version):
                # Arrange
                self.baseline_path.write_text(
                    json.dumps({"schemaVersion": 1, "efCoreVersions": ["10.0.8", version]}), encoding="ascii",
                )

                # Act
                try:
                    lines.inspect_toolchain(self.repo, snapshot)
                except lines.LinePolicyError as exception:
                    failure = exception
                else:
                    failure = None

                # Assert
                self.assertIsInstance(failure, lines.LinePolicyError)

    def test_ga_patch_policy_requires_the_declared_minimum_patch(self) -> None:
        """A newer registered patch cannot stand in for an unreviewed floor."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        self.baseline_path.write_text(
            json.dumps({"schemaVersion": 1, "efCoreVersions": ["10.0.10", "10.0.11"]}), encoding="ascii",
        )

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("include the selected EF minimum", str(failure.exception))

    def test_ga_exact_pin_accepts_its_only_reviewed_patch(self) -> None:
        """An exact stable source contract has no implicit patch expansion."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        self.write_toolchain(ef="10.0.8")
        self.baseline_path.write_text(
            json.dumps({"schemaVersion": 1, "efCoreVersions": ["10.0.8"]}), encoding="ascii",
        )

        # Act
        result = lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertEqual("10.0.8", result["efCoreVersion"])

    def test_ga_exact_pin_rejects_an_additional_admitted_patch(self) -> None:
        """Baseline membership cannot expand an exact EF dependency pin."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        self.write_toolchain(ef="10.0.8")
        self.baseline_path.write_text(
            json.dumps({"schemaVersion": 1, "efCoreVersions": ["10.0.8", "10.0.10"]}), encoding="ascii",
        )

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("outside the selected EF policy", str(failure.exception))

    def test_ga_interval_rejects_its_exclusive_upper_patch(self) -> None:
        """Same-minor versions must still obey the interval's upper boundary."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        self.write_toolchain(ef="[10.0.8, 10.0.11)")
        self.baseline_path.write_text(
            json.dumps({"schemaVersion": 1, "efCoreVersions": ["10.0.8", "10.0.11"]}), encoding="ascii",
        )

        # Act
        with self.assertRaises(lines.LinePolicyError) as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("outside the selected EF policy", str(failure.exception))

    def test_ga_interval_accepts_reviewed_floor_and_patch_before_upper_bound(self) -> None:
        """A bounded reviewed patch set passes preflight without floating latest."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        self.write_toolchain(ef="[10.0.8, 10.0.11)")
        self.baseline_path.write_text(
            json.dumps({"schemaVersion": 1, "efCoreVersions": ["10.0.8", "10.0.10"]}), encoding="ascii",
        )

        # Act
        result = lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertEqual("[10.0.8, 10.0.11)", result["efCoreVersion"])

    def test_preview_rc_uses_exact_own_sdk_ef_and_runner(self) -> None:
        """A platform RC is not qualified using stable main's patch matrix."""
        # Arrange
        self.write_toolchain(sdk="11.0.100-rc.2.26504.105", tfm="net11.0", ef="11.0.0-rc.2.26504.105",
                             prerelease=True, runner="Microsoft.Testing.Platform")
        snapshot = lines.freeze_line(prepared_policy(), PREVIEW, "11.0.0-rc.7")
        self.baseline_path.unlink()

        # Act
        result = lines.inspect_toolchain(self.repo, snapshot, resolved_packages={
            "Microsoft.EntityFrameworkCore.Design": "11.0.0-rc.2.26504.105",
            "Microsoft.EntityFrameworkCore.Relational": "11.0.0-rc.2.26504.105"})

        # Assert
        self.assertEqual("Microsoft.Testing.Platform", result["testRunner"])
        self.assertEqual("rc", result["upstreamStage"])
        self.assertEqual("11.0.0-rc.2.26504.105", result["efCoreVersion"])

    def test_preview_with_ga_dependencies_still_has_prerelease_package_channel(self) -> None:
        """Upstream GA is not permission to publish stable from preview."""
        # Arrange
        policy = prepared_policy()
        policy["lines"][2]["upstreamStage"] = "ga"
        self.write_toolchain(sdk="11.0.100", tfm="net11.0", ef="[11.0.0, 11.1.0)")
        snapshot = lines.freeze_line(policy, PREVIEW, "11.0.0-rc.7")
        toolchain = {"approvedDotnetSdk": "11.0.100", "targetFramework": "net11.0",
                     "efCoreVersion": "[11.0.0, 11.1.0)", "testRunner": "VSTest", "upstreamStage": "ga"}

        # Act
        result = lines.validate_toolchain(toolchain, snapshot)

        # Assert
        self.assertEqual("ga", result["upstreamStage"])
        self.assertEqual("prerelease", snapshot["channel"])

    def test_preview_stage_before_rc_is_supported_with_its_own_exact_pins(self) -> None:
        """Platform preview and platform RC use the same explicit contract."""
        # Arrange
        policy = prepared_policy()
        policy["lines"][2]["upstreamStage"] = "preview"
        self.write_toolchain(sdk="11.0.100-preview.7.26400.1", tfm="net11.0", ef="11.0.0-preview.7.26400.1",
                             prerelease=True, runner="Microsoft.Testing.Platform")
        snapshot = lines.freeze_line(policy, PREVIEW, "11.0.0-preview.1")
        self.baseline_path.unlink()

        # Act
        result = lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertEqual("preview", result["upstreamStage"])

    def test_portable_validator_reuses_source_inspector_semantics(self) -> None:
        """Portable evidence cannot silently weaken its frozen SDK/EF binding."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        toolchain = {"approvedDotnetSdk": "10.0.402", "targetFramework": "net10.0",
                     "efCoreVersion": "[10.0.8, 10.1.0)", "testRunner": "VSTest", "upstreamStage": "ga"}

        # Act
        validated = lines.validate_toolchain(toolchain, snapshot)

        # Assert
        self.assertEqual(toolchain, validated)
        self.assertIsNot(toolchain, validated)

    def test_portable_validator_rejects_unknown_missing_and_mismatched_properties(self) -> None:
        """A malformed toolchain cannot survive transport as historical proof."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        original = {"approvedDotnetSdk": "10.0.402", "targetFramework": "net10.0",
                    "efCoreVersion": "[10.0.8, 10.1.0)", "testRunner": "VSTest", "upstreamStage": "ga"}
        changes = {"approvedDotnetSdk": None, "targetFramework": None, "efCoreVersion": None,
                   "testRunner": None, "upstreamStage": "rc", "extra": True}

        # Act
        for key, value in changes.items():
            malformed = dict(original)
            malformed[key] = value
            with self.subTest(key=key):
                with self.assertRaises(lines.LinePolicyError):
                    lines.validate_toolchain(malformed, snapshot)

        # Assert
        for key in original:
            malformed = dict(original)
            del malformed[key]
            with self.subTest(missing=key):
                with self.assertRaises(lines.LinePolicyError):
                    lines.validate_toolchain(malformed, snapshot)

    def test_sdk_tfm_ef_stage_and_test_runner_mismatches_are_rejected(self) -> None:
        """Installed unrelated SDKs do not influence the checked-out contract."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        changes = ({"sdk": "11.0.100"}, {"tfm": "net11.0"}, {"ef": "[11.0.0, 11.1.0)"},
                   {"sdk": "10.0.100-rc.1", "prerelease": True}, {"ef": "10.0.0-rc.1"},
                   {"ef": "10.*"}, {"runner": "unreviewed-runner"}, {"prerelease": True})

        # Act
        rejected = 0
        for change in changes:
            self.write_toolchain(**change)
            with self.subTest(change=change):
                with self.assertRaises(lines.LinePolicyError):
                    lines.inspect_toolchain(self.repo, snapshot)

            rejected += 1

        # Assert
        self.assertEqual(len(changes), rejected)

    def test_preview_requires_exact_matching_stage_without_floating_dependencies(self) -> None:
        """A broad range must never float a reviewed platform prerelease."""
        # Arrange
        snapshot = lines.freeze_line(prepared_policy(), PREVIEW, "11.0.0-rc.7")
        versions = ("[11.0.0-rc.1, 11.0.0)", "11.0.0-preview.1", "11.0.0", "11.*")

        # Act
        rejected = 0
        for ef in versions:
            self.write_toolchain(sdk="11.0.100-rc.2.26504.105", tfm="net11.0", ef=ef, prerelease=True)
            with self.subTest(ef=ef):
                with self.assertRaises(lines.LinePolicyError):
                    lines.inspect_toolchain(self.repo, snapshot)

            rejected += 1

        # Assert
        self.assertEqual(len(versions), rejected)

    def test_resolved_packages_cannot_cross_the_reviewed_ef_bound(self) -> None:
        """Check actual restored identities as well as source declarations."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        versions = ("10.0.7", "10.1.0", "11.0.0", "10.0.9-rc.1")

        # Act
        rejected = 0
        for version in versions:
            packages = {"Microsoft.EntityFrameworkCore.Design": version,
                        "Microsoft.EntityFrameworkCore.Relational": version}
            with self.subTest(version=version):
                with self.assertRaises(lines.LinePolicyError):
                    lines.inspect_toolchain(self.repo, snapshot, resolved_packages=packages)

            rejected += 1

        # Assert
        self.assertEqual(len(versions), rejected)

    def test_resolved_ef_graph_must_be_complete_well_typed_and_coherent(self) -> None:
        """Individually allowed versions are not a qualified mixed EF graph."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        invalid = ({}, [], {"Microsoft.EntityFrameworkCore.Design": "10.0.11"},
                   {"Microsoft.EntityFrameworkCore.Design": "10.0.11",
                    "Microsoft.EntityFrameworkCore.Relational": "10.0.12"},
                   {"Microsoft.EntityFrameworkCore.Design": True,
                    "Microsoft.EntityFrameworkCore.Relational": "10.0.11"})

        # Act
        rejected = 0
        for packages in invalid:
            with self.subTest(packages=packages):
                with self.assertRaises(lines.LinePolicyError):
                    lines.inspect_toolchain(self.repo, snapshot, resolved_packages=packages)

            rejected += 1

        # Assert
        self.assertEqual(len(invalid), rejected)

    def test_rc_resolved_package_must_match_exact_reviewed_pin(self) -> None:
        """A newer RC build is not automatically interchangeable evidence."""
        # Arrange
        self.write_toolchain(sdk="11.0.100-rc.2.26504.105", tfm="net11.0", ef="11.0.0-rc.2.26504.105",
                             prerelease=True)
        snapshot = lines.freeze_line(prepared_policy(), PREVIEW, "11.0.0-rc.7")
        packages = {"Microsoft.EntityFrameworkCore.Design": "11.0.0-rc.2.26504.106",
                    "Microsoft.EntityFrameworkCore.Relational": "11.0.0-rc.2.26504.106"}

        # Act
        with self.assertRaisesRegex(lines.LinePolicyError, "exact pin") as failure:
            lines.inspect_toolchain(self.repo, snapshot, resolved_packages=packages)

        # Assert
        self.assertIn("Resolved EF", str(failure.exception))

    def test_sdk_rollforward_cannot_change_the_frozen_build_identity(self) -> None:
        """Only the declared exact SDK may produce candidate evidence."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        path = self.repo / "global.json"
        settings = json.loads(path.read_text(encoding="ascii"))
        settings["sdk"]["rollForward"] = "latestPatch"
        path.write_text(json.dumps(settings), encoding="ascii")

        # Act
        with self.assertRaisesRegex(lines.LinePolicyError, "roll-forward") as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("disable", str(failure.exception))

    def test_ambiguous_framework_property_is_rejected(self) -> None:
        """Textual inspection must not guess MSBuild conditional evaluation."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        (self.repo / "Directory.Build.props").write_text(
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
            "<TargetFramework Condition=\"'$(X)' == '1'\">net11.0</TargetFramework></PropertyGroup></Project>",
            encoding="ascii",
        )

        # Act
        with self.assertRaisesRegex(lines.LinePolicyError, "unambiguous") as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("TargetFramework", str(failure.exception))

    def test_parent_msbuild_conditions_cannot_hide_toolchain_selection(self) -> None:
        """A root property is not unconditional merely because its own tag is."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        (self.repo / "Directory.Build.props").write_text(
            "<Project><PropertyGroup Condition=\"'$(X)' == '1'\">"
            "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            encoding="ascii",
        )

        # Act
        with self.assertRaisesRegex(lines.LinePolicyError, "unambiguous") as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("TargetFramework", str(failure.exception))

    def test_reviewed_default_ef_property_guard_is_accepted(self) -> None:
        """Retain the real floor/latest matrix override contract."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        (self.repo / "Directory.Packages.props").write_text(
            "<Project><PropertyGroup><DokaEfCoreVersion Condition=\"'$(DokaEfCoreVersion)' == ''\">"
            "[10.0.8, 10.1.0)</DokaEfCoreVersion></PropertyGroup></Project>",
            encoding="ascii",
        )

        # Act
        result = lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertEqual("[10.0.8, 10.1.0)", result["efCoreVersion"])

    def test_unreviewed_ef_property_condition_is_rejected(self) -> None:
        """Never guess branch EF identity from a conditional configuration."""
        # Arrange
        snapshot = lines.freeze_line(initial_policy(), MAIN, "10.7.0")
        (self.repo / "Directory.Packages.props").write_text(
            "<Project><PropertyGroup><DokaEfCoreVersion Condition=\"'$(X)' == '1'\">"
            "[10.0.8, 10.1.0)</DokaEfCoreVersion></PropertyGroup></Project>",
            encoding="ascii",
        )

        # Act
        with self.assertRaisesRegex(lines.LinePolicyError, "unambiguous") as failure:
            lines.inspect_toolchain(self.repo, snapshot)

        # Assert
        self.assertIn("DokaEfCoreVersion", str(failure.exception))


if __name__ == "__main__":
    unittest.main()
