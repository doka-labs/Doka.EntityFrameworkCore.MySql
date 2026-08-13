"""Tests for canonical NuGet dependency-submission snapshots."""

from __future__ import annotations

import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

from eng.quality import dependency_snapshot


class DependencySnapshotTests(unittest.TestCase):
    """Prove graph completeness, scope propagation, and API submission."""

    COMMIT_SHA = "a" * 40
    GIT_REF = "refs/heads/feature/dependency-change"
    CORRELATOR = "dependency-review-nuget"

    def setUp(self) -> None:
        """Create an isolated repository and restore-output tree."""

        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-dependency-snapshot-"
        )
        self.root = Path(self.temporary_directory.name)
        self.assets = self.root / "artifacts" / "obj"
        self.project = self.root / "src" / "Example" / "Example.csproj"
        self.project.parent.mkdir(parents=True)
        self.project.write_text("<Project />\n", encoding="ascii")

    def tearDown(self) -> None:
        """Dispose the synthetic repository."""

        self.temporary_directory.cleanup()

    def test_snapshot_preserves_relationships_scopes_and_exact_revision(
        self,
    ) -> None:
        """Derive directness and transitive scope from the resolved graph."""

        self.write_assets()
        scanned_at = dt.datetime(2026, 8, 13, 10, 30, tzinfo=dt.UTC)

        snapshot = dependency_snapshot.create_snapshot(
            self.root,
            self.assets,
            self.COMMIT_SHA,
            self.GIT_REF,
            self.CORRELATOR,
            "12345",
            scanned_at,
        )

        self.assertEqual(self.COMMIT_SHA, snapshot["sha"])
        self.assertEqual(self.GIT_REF, snapshot["ref"])
        self.assertEqual(self.CORRELATOR, snapshot["job"]["correlator"])
        self.assertEqual("2026-08-13T10:30:00Z", snapshot["scanned"])
        manifest = snapshot["manifests"]["src/Example/Example.csproj"]
        resolved = manifest["resolved"]

        runtime = resolved["pkg:nuget/Runtime.Root@1.0.0"]
        shared = resolved["pkg:nuget/Shared.Library@2.0.0"]
        development = resolved["pkg:nuget/Development.Tool@3.0.0"]
        development_child = resolved["pkg:nuget/Development.Child@4.0.0"]

        self.assertEqual(
            ("direct", "runtime"),
            (
                runtime["relationship"],
                runtime["scope"],
            ),
        )
        self.assertEqual(
            ("indirect", "runtime"),
            (
                shared["relationship"],
                shared["scope"],
            ),
        )
        self.assertEqual(
            ("direct", "development"),
            (
                development["relationship"],
                development["scope"],
            ),
        )
        self.assertEqual(
            ("indirect", "development"),
            (
                development_child["relationship"],
                development_child["scope"],
            ),
        )
        self.assertEqual(
            ["pkg:nuget/Shared.Library@2.0.0"],
            runtime["dependencies"],
        )

    def test_snapshot_rejects_invalid_revision_identity(self) -> None:
        """Reject identities that cannot name one concrete branch revision."""

        invalid_identities = (
            (
                "commit SHA",
                "A" * 40,
                self.GIT_REF,
                self.CORRELATOR,
                "12345",
                "commit_sha must be a lowercase 40-character Git SHA",
            ),
            (
                "branch ref",
                self.COMMIT_SHA,
                "refs/pull/42/merge",
                self.CORRELATOR,
                "12345",
                "git_ref must identify a concrete branch below refs/heads/",
            ),
            (
                "correlator",
                self.COMMIT_SHA,
                self.GIT_REF,
                "dependency review/42",
                "12345",
                "correlator must contain 1-100 safe identifier characters",
            ),
            (
                "run ID",
                self.COMMIT_SHA,
                self.GIT_REF,
                self.CORRELATOR,
                " ",
                "run_id must not be empty",
            ),
        )

        for (
            name,
            commit_sha,
            git_ref,
            correlator,
            run_id,
            expected_error,
        ) in invalid_identities:
            with self.subTest(identity=name):
                with self.assertRaisesRegex(
                    dependency_snapshot.DependencySnapshotError,
                    expected_error,
                ):
                    dependency_snapshot.create_snapshot(
                        self.root,
                        self.assets,
                        commit_sha,
                        git_ref,
                        correlator,
                        run_id,
                    )

    def test_runtime_reachability_wins_for_a_shared_development_package(
        self,
    ) -> None:
        """Keep a shared transitive dependency in the stricter runtime scope."""

        self.write_assets(development_depends_on_shared=True)

        snapshot = dependency_snapshot.create_snapshot(
            self.root,
            self.assets,
            self.COMMIT_SHA,
            self.GIT_REF,
            self.CORRELATOR,
            "12345",
        )

        resolved = snapshot["manifests"]["src/Example/Example.csproj"]["resolved"]
        self.assertEqual(
            "runtime",
            resolved["pkg:nuget/Shared.Library@2.0.0"]["scope"],
        )

    def test_project_reference_packages_remain_indirect_runtime_assets(
        self,
    ) -> None:
        """Classify a referenced project's packages without making them direct."""

        self.write_assets(runtime_through_project=True)

        snapshot = dependency_snapshot.create_snapshot(
            self.root,
            self.assets,
            self.COMMIT_SHA,
            self.GIT_REF,
            self.CORRELATOR,
            "12345",
        )

        resolved = snapshot["manifests"]["src/Example/Example.csproj"]["resolved"]
        runtime = resolved["pkg:nuget/Runtime.Root@1.0.0"]
        shared = resolved["pkg:nuget/Shared.Library@2.0.0"]
        self.assertEqual(
            ("indirect", "runtime"),
            (
                runtime["relationship"],
                runtime["scope"],
            ),
        )
        self.assertEqual(
            ("indirect", "runtime"),
            (
                shared["relationship"],
                shared["scope"],
            ),
        )

    def test_unresolved_transitive_dependency_fails_closed(self) -> None:
        """Reject a snapshot that would silently truncate a package edge."""

        self.write_assets(unresolved_dependency=True)

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "references unresolved package 'Missing.Library'",
        ):
            dependency_snapshot.create_snapshot(
                self.root,
                self.assets,
                self.COMMIT_SHA,
                self.GIT_REF,
                self.CORRELATOR,
                "12345",
            )

    def test_project_path_outside_repository_fails_closed(self) -> None:
        """Prevent restore metadata from naming a foreign manifest path."""

        self.write_assets(project_path=self.root.parent / "Foreign.csproj")

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "points outside the repository",
        ):
            dependency_snapshot.create_snapshot(
                self.root,
                self.assets,
                self.COMMIT_SHA,
                self.GIT_REF,
                self.CORRELATOR,
                "12345",
            )

    def test_empty_restore_output_fails_closed(self) -> None:
        """Never submit an empty graph as successful dependency evidence."""

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "No project.assets.json files",
        ):
            dependency_snapshot.create_snapshot(
                self.root,
                self.assets,
                self.COMMIT_SHA,
                self.GIT_REF,
                self.CORRELATOR,
                "12345",
            )

    def test_unknown_project_assets_schema_fails_closed(self) -> None:
        """Require review when the pinned SDK changes the consumed schema."""

        self.write_assets(assets_version=5)

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "unsupported project.assets.json version '5'. Expected 4",
        ):
            dependency_snapshot.create_snapshot(
                self.root,
                self.assets,
                self.COMMIT_SHA,
                self.GIT_REF,
                self.CORRELATOR,
                "12345",
            )

    def test_nested_artifact_projects_are_not_authored_inputs(self) -> None:
        """Exclude generated projects below every artifacts directory."""

        self.write_assets()
        generated_project = (
            self.root / "src" / "Example" / "artifacts" / "Generated.csproj"
        )
        generated_project.parent.mkdir(parents=True)
        generated_project.write_text("<Project />\n", encoding="ascii")

        snapshot = dependency_snapshot.create_snapshot(
            self.root,
            self.assets,
            self.COMMIT_SHA,
            self.GIT_REF,
            self.CORRELATOR,
            "12345",
        )

        self.assertEqual(
            ["src/Example/Example.csproj"],
            list(snapshot["manifests"]),
        )

    def test_missing_authored_project_restore_fails_closed(self) -> None:
        """Require every authored project to contribute a resolved manifest."""

        self.write_assets()
        extra_project = self.root / "src" / "Missing" / "Missing.csproj"
        extra_project.parent.mkdir(parents=True)
        extra_project.write_text("<Project />\n", encoding="ascii")

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "missing src/Missing/Missing.csproj",
        ):
            dependency_snapshot.create_snapshot(
                self.root,
                self.assets,
                self.COMMIT_SHA,
                self.GIT_REF,
                self.CORRELATOR,
                "12345",
            )

    @patch("eng.quality.dependency_snapshot.urllib.request.urlopen")
    def test_submission_uses_the_repository_endpoint_and_bearer_token(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Submit canonical JSON without placing the token in the URL or body."""

        response = MagicMock()
        response.status = 201
        response.read.return_value = b'{"result":"SUCCESS"}'
        urlopen.return_value.__enter__.return_value = response
        snapshot = {"version": 0, "sha": self.COMMIT_SHA}

        dependency_snapshot.submit_snapshot(
            snapshot,
            "https://api.github.example",
            "doka-labs/provider",
            "secret-token",
        )

        request = urlopen.call_args.args[0]
        self.assertEqual(
            "https://api.github.example/repos/doka-labs/provider/"
            "dependency-graph/snapshots",
            request.full_url,
        )
        self.assertEqual("Bearer secret-token", request.get_header("Authorization"))
        self.assertEqual("2026-03-10", request.get_header("X-github-api-version"))
        self.assertNotIn(b"secret-token", request.data)
        self.assertEqual(snapshot, json.loads(request.data))

    @patch("eng.quality.dependency_snapshot.urllib.request.urlopen")
    def test_submission_accepts_the_live_api_acceptance_result(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Accept the asynchronous result observed from the live GitHub API."""

        response = MagicMock()
        response.status = 201
        response.read.return_value = b'{"result":"ACCEPTED"}'
        urlopen.return_value.__enter__.return_value = response

        dependency_snapshot.submit_snapshot(
            {"version": 0},
            "https://api.github.example",
            "doka-labs/provider",
            "secret-token",
        )

    def test_submission_rejects_an_invalid_repository_identity(self) -> None:
        """Require the API path to identify one repository as owner/name."""

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "repository must use the 'owner/name' form",
        ):
            dependency_snapshot.submit_snapshot(
                {"version": 0},
                "https://api.github.example",
                "doka-labs",
                "secret-token",
            )

    def test_submission_rejects_an_empty_bearer_token(self) -> None:
        """Fail locally instead of issuing an unauthenticated API request."""

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "GITHUB_TOKEN is required for submission",
        ):
            dependency_snapshot.submit_snapshot(
                {"version": 0},
                "https://api.github.example",
                "doka-labs/provider",
                " ",
            )

    def test_submission_rejects_a_non_https_api_endpoint(self) -> None:
        """Prevent token submission to a downgraded or malformed endpoint."""

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "api_url must be an HTTPS origin",
        ):
            dependency_snapshot.submit_snapshot(
                {"version": 0},
                "http://api.github.example",
                "doka-labs/provider",
                "secret-token",
            )

    @patch("eng.quality.dependency_snapshot.urllib.request.urlopen")
    def test_submission_rejects_an_unexpected_success_status(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Require the documented Created response instead of any 2xx code."""

        response = MagicMock()
        response.status = 202
        response.read.return_value = b'{"result":"SUCCESS"}'
        urlopen.return_value.__enter__.return_value = response

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "unexpected HTTP status 202",
        ):
            dependency_snapshot.submit_snapshot(
                {"version": 0},
                "https://api.github.example",
                "doka-labs/provider",
                "secret-token",
            )

    @patch("eng.quality.dependency_snapshot.urllib.request.urlopen")
    def test_submission_rejects_an_unsuccessful_result(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Reject a result outside the explicit submission-success contract."""

        response = MagicMock()
        response.status = 201
        response.read.return_value = b'{"result":"ERROR"}'
        urlopen.return_value.__enter__.return_value = response

        with self.assertRaisesRegex(
            dependency_snapshot.DependencySnapshotError,
            "dependency-submission result 'ERROR'",
        ):
            dependency_snapshot.submit_snapshot(
                {"version": 0},
                "https://api.github.example",
                "doka-labs/provider",
                "secret-token",
            )

    def write_assets(
        self,
        *,
        assets_version: int = 4,
        development_depends_on_shared: bool = False,
        unresolved_dependency: bool = False,
        project_path: Path | None = None,
        runtime_through_project: bool = False,
    ) -> None:
        """Write one representative project.assets.json fixture."""

        runtime_dependency = (
            "Missing.Library" if unresolved_dependency else "Shared.Library"
        )
        development_dependencies = {"Development.Child": "4.0.0"}
        if development_depends_on_shared:
            development_dependencies["Shared.Library"] = "2.0.0"

        runtime_target = (
            {
                "Referenced.Project/1.0.0": {
                    "type": "project",
                    "dependencies": {"Runtime.Root": "1.0.0"},
                }
            }
            if runtime_through_project
            else {}
        )
        runtime_framework_dependency = (
            {}
            if runtime_through_project
            else {
                "Runtime.Root": {
                    "target": "Package",
                    "version": "[1.0.0, )",
                }
            }
        )

        document = {
            "version": assets_version,
            "targets": {
                "net10.0": {
                    "Runtime.Root/1.0.0": {
                        "type": "package",
                        "dependencies": {runtime_dependency: "2.0.0"},
                    },
                    "Shared.Library/2.0.0": {"type": "package"},
                    "Development.Tool/3.0.0": {
                        "type": "package",
                        "dependencies": development_dependencies,
                    },
                    "Development.Child/4.0.0": {"type": "package"},
                    **runtime_target,
                }
            },
            "project": {
                "restore": {
                    "projectPath": str(project_path or self.project),
                },
                "frameworks": {
                    "net10.0": {
                        "dependencies": {
                            **runtime_framework_dependency,
                            "Development.Tool": {
                                "target": "Package",
                                "version": "[3.0.0, )",
                                "suppressParent": "All",
                            },
                        }
                    }
                },
            },
        }
        asset_file = self.assets / "Example" / "project.assets.json"
        asset_file.parent.mkdir(parents=True)
        asset_file.write_text(
            json.dumps(document, indent=2) + "\n",
            encoding="ascii",
        )


if __name__ == "__main__":
    unittest.main()
