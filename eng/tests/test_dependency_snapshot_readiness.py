"""Tests for fail-closed dependency-review snapshot readiness."""

from __future__ import annotations

import base64
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

from eng.quality import dependency_snapshot_readiness


_SNAPSHOT_HEADER = "x-github-dependency-graph-snapshot-warnings"


class DependencySnapshotReadinessTests(unittest.TestCase):
    """Prove bootstrap termination and canonical evidence symmetry."""

    BASE_SHA = "a" * 40
    HEAD_SHA = "b" * 40
    API_URL = "https://api.github.example"
    REPOSITORY = "doka-labs/provider"
    TOKEN = "secret-token"

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_main_push_is_canonical_without_querying_a_base_workflow(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Keep every pushed main revision on the canonical producer path."""

        result = dependency_snapshot_readiness.resolve_mode(
            "push",
            None,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
        )

        self.assertEqual("canonical", result["mode"])
        self.assertEqual("main-push", result["reason"])
        urlopen.assert_not_called()

    def test_pull_request_mode_follows_the_exact_base_workflow(self) -> None:
        """Make bootstrap unreachable after the submission job reaches main."""

        cases = (
            (
                "name: dependency-review\n\njobs:\n  dependency-review:\n",
                "bootstrap",
            ),
            (
                "name: dependency-review\n\njobs:\n"
                "  dependency-submission:\n    runs-on: ubuntu-latest\n",
                "canonical",
            ),
        )
        for workflow, expected in cases:
            with self.subTest(expected=expected), patch(
                "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
            ) as urlopen:
                urlopen.return_value = self.response(
                    {
                        "encoding": "base64",
                        "content": self.encoded_content(workflow),
                    }
                )

                result = dependency_snapshot_readiness.resolve_mode(
                    "pull_request",
                    self.BASE_SHA,
                    self.API_URL,
                    self.REPOSITORY,
                    self.TOKEN,
                )

                self.assertEqual(expected, result["mode"])
                request = urlopen.call_args.args[0]
                self.assertIn(
                    "/contents/.github/workflows/dependency-review.yml"
                    f"?ref={self.BASE_SHA}",
                    request.full_url,
                )

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_pull_request_mode_rejects_unreadable_base_content(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Do not guess bootstrap state from a malformed GitHub response."""

        urlopen.return_value = self.response(
            {"encoding": "base64", "content": "not base64"}
        )

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "unreadable base-workflow content",
        ):
            dependency_snapshot_readiness.resolve_mode(
                "pull_request",
                self.BASE_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
            )

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_canonical_readiness_binds_the_base_check_and_comparison(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Accept only the exact base receipt and a warning-free comparison."""

        urlopen.side_effect = [
            self.response(self.check_runs(self.successful_check())),
            self.response([], {}),
        ]

        result = dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            0,
        )

        self.assertEqual("ready", result["status"])
        self.assertEqual(17, result["baseCheck"]["id"])
        self.assertEqual([], result["snapshotWarnings"])
        check_request = urlopen.call_args_list[0].args[0]
        comparison_request = urlopen.call_args_list[1].args[0]
        self.assertIn(
            f"/commits/{self.BASE_SHA}/check-runs?",
            check_request.full_url,
        )
        self.assertIn("check_name=dependency-submission", check_request.full_url)
        self.assertIn(
            f"/dependency-graph/compare/{self.BASE_SHA}...{self.HEAD_SHA}"
            "?per_page=1",
            comparison_request.full_url,
        )
        for request in (check_request, comparison_request):
            self.assertEqual(
                "2026-03-10",
                request.get_header("X-github-api-version"),
            )
            self.assertEqual(
                "Bearer secret-token",
                request.get_header("Authorization"),
            )

    def test_canonical_readiness_rejects_identical_revisions(self) -> None:
        """Require an actual pull-request comparison before querying GitHub."""

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "must identify different commits",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.BASE_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
            )

    @patch(
        "eng.quality.dependency_snapshot_readiness."
        "_wait_for_warning_free_comparison",
        return_value=None,
    )
    @patch(
        "eng.quality.dependency_snapshot_readiness._wait_for_base_check"
    )
    @patch(
        "eng.quality.dependency_snapshot_readiness.time.monotonic",
        return_value=50.0,
    )
    def test_readiness_shares_one_deadline_across_both_phases(
        self,
        monotonic: MagicMock,
        wait_for_base_check: MagicMock,
        wait_for_comparison: MagicMock,
    ) -> None:
        """Prevent either readiness phase from receiving a fresh retry window."""

        wait_for_base_check.return_value = self.successful_check()

        dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            120,
        )

        monotonic.assert_called_once_with()
        self.assertEqual(170.0, wait_for_base_check.call_args.args[-1])
        self.assertEqual(170.0, wait_for_comparison.call_args.args[-1])

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_failed_base_submission_is_not_retried_or_masked(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Treat a terminal base producer failure as a failed evidence chain."""

        urlopen.return_value = self.response(
            self.check_runs(
                {
                    **self.successful_check(),
                    "conclusion": "failure",
                }
            )
        )

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "base dependency-submission check did not succeed",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                120,
            )

        self.assertEqual(1, urlopen.call_count)

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_missing_base_receipt_fails_with_rebase_guidance(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Make an absent or expired exact-SHA check actionable and fail closed."""

        urlopen.return_value = self.response(self.check_runs())

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "Rebase onto a current main commit",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.time.sleep")
    @patch("eng.quality.dependency_snapshot_readiness.time.monotonic")
    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_transient_snapshot_warning_is_retried_within_one_deadline(
        self,
        urlopen: MagicMock,
        monotonic: MagicMock,
        sleep: MagicMock,
    ) -> None:
        """Wait for graph propagation without delegating a soft timeout."""

        warning = base64.b64encode(b"head snapshot is still propagating").decode(
            "ascii"
        )
        urlopen.side_effect = [
            self.response(self.check_runs(self.successful_check())),
            self.response(
                [],
                {
                    "X-GitHub-Dependency-Graph-Snapshot-Warnings": warning,
                },
            ),
            self.response([], {}),
        ]
        monotonic.side_effect = [0.0, 0.0]

        result = dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            20,
        )

        self.assertEqual("ready", result["status"])
        sleep.assert_called_once_with(1.0)

    @patch("eng.quality.dependency_snapshot_readiness.time.sleep")
    @patch("eng.quality.dependency_snapshot_readiness.time.monotonic")
    def test_retry_uses_the_remaining_shared_window(
        self,
        monotonic: MagicMock,
        sleep: MagicMock,
    ) -> None:
        """Use a short residual window for one final propagation attempt."""

        monotonic.return_value = 17.0

        should_retry = dependency_snapshot_readiness._wait_before_retry(
            20.0,
            4,
        )

        self.assertTrue(should_retry)
        sleep.assert_called_once_with(3.0)

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_persistent_snapshot_warning_fails_closed(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Reject the incomplete comparison that the official retry accepts."""

        warning_text = "base snapshot is missing"
        warning = base64.b64encode(warning_text.encode("utf-8")).decode("ascii")
        urlopen.side_effect = [
            self.response(self.check_runs(self.successful_check())),
            self.response(
                [],
                {_SNAPSHOT_HEADER: warning},
            ),
        ]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            warning_text,
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
            )

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_malformed_snapshot_warning_is_not_treated_as_absent(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Reject an unreadable readiness signal instead of bypassing it."""

        urlopen.side_effect = [
            self.response(self.check_runs(self.successful_check())),
            self.response([], {_SNAPSHOT_HEADER: "not base64"}),
        ]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "unreadable snapshot-warning header",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
            )

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_readiness_rejects_an_unexpected_success_status(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Require the documented OK response instead of accepting any 2xx."""

        urlopen.return_value = self.response(
            self.check_runs(self.successful_check()),
            status=202,
        )

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "unexpected readiness HTTP status 202",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
            )

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_foreign_check_run_cannot_satisfy_the_base_receipt(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Bind the receipt to GitHub Actions, its name, and the exact SHA."""

        foreign_checks = (
            {**self.successful_check(), "head_sha": self.HEAD_SHA},
            {
                **self.successful_check(),
                "app": {"slug": "third-party-app"},
            },
            {**self.successful_check(), "name": "other-check"},
        )
        urlopen.return_value = self.response(self.check_runs(*foreign_checks))

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "Rebase onto a current main commit",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
            )

    @patch(
        "eng.quality.dependency_snapshot_readiness.urllib.request.urlopen"
    )
    def test_cli_writes_the_resolved_mode_receipt(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Keep the workflow-facing JSON handoff executable."""

        urlopen.return_value = self.response(
            {
                "encoding": "base64",
                "content": base64.b64encode(b"jobs:\n  dependency-review:\n").decode(
                    "ascii"
                ),
            }
        )
        with tempfile.TemporaryDirectory(
            prefix="doka-snapshot-readiness-"
        ) as directory, patch.dict(
            "os.environ",
            {"GITHUB_TOKEN": self.TOKEN},
        ):
            output = Path(directory) / "mode.json"
            exit_code = dependency_snapshot_readiness.main(
                [
                    "resolve-mode",
                    "--event-name",
                    "pull_request",
                    "--base-revision",
                    self.BASE_SHA,
                    "--api-url",
                    self.API_URL,
                    "--repository",
                    self.REPOSITORY,
                    "--output",
                    str(output),
                ]
            )

            self.assertEqual(0, exit_code)
            self.assertEqual("bootstrap", json.loads(output.read_text())["mode"])

    @classmethod
    def successful_check(cls) -> dict[str, object]:
        """Return one exact GitHub Actions submission receipt."""

        return {
            "id": 17,
            "name": "dependency-submission",
            "head_sha": cls.BASE_SHA,
            "status": "completed",
            "conclusion": "success",
            "app": {"slug": "github-actions"},
        }

    @staticmethod
    def check_runs(*checks: dict[str, object]) -> dict[str, object]:
        """Wrap check runs in the GitHub REST response shape."""

        return {"total_count": len(checks), "check_runs": list(checks)}

    @staticmethod
    def encoded_content(content: str) -> str:
        """Mirror the line-wrapped base64 returned by the Contents API."""

        encoded = base64.b64encode(content.encode("utf-8")).decode("ascii")
        return "\n".join(
            encoded[index:index + 20]
            for index in range(0, len(encoded), 20)
        )

    @staticmethod
    def response(
        document: object,
        headers: dict[str, str] | None = None,
        status: int = 200,
    ) -> MagicMock:
        """Return a context-managed HTTP response double."""

        response = MagicMock()
        response.status = status
        response.read.return_value = json.dumps(document).encode("utf-8")
        response.headers.items.return_value = (headers or {}).items()
        context = MagicMock()
        context.__enter__.return_value = response
        return context
if __name__ == "__main__":
    unittest.main()
