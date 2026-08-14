"""Tests for fail-closed automatic dependency-snapshot readiness."""

from __future__ import annotations

import base64
import json
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import MagicMock, call, patch

from eng.quality import dependency_snapshot_readiness


_SNAPSHOT_HEADER = "x-github-dependency-graph-snapshot-warnings"


class DependencySnapshotReadinessTests(unittest.TestCase):
    """Prove producer binding, phase isolation, and fail-closed output."""

    BASE_SHA = "a" * 40
    HEAD_SHA = "b" * 40
    API_URL = "https://api.github.example"
    REPOSITORY = "doka-labs/provider"
    TOKEN = "secret-token"

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_exact_producers_and_complete_comparison_are_ready(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Bind readiness to both producers and the exact graph comparison."""

        urlopen.side_effect = self.ready_responses()

        result = dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            300,
            900,
        )

        self.assertEqual(3, result["schemaVersion"])
        self.assertEqual("ready", result["status"])
        self.assertEqual(
            {
                "base": self.expected_receipt(101, self.BASE_SHA),
                "head": self.expected_receipt(102, self.HEAD_SHA),
            },
            result["automaticSubmission"],
        )
        self.assertEqual(
            {"status": "complete", "snapshotWarnings": []},
            result["dependencyComparison"],
        )
        self.assertEqual(
            {"producer": 300, "propagation": 900},
            result["waitBudgetsSeconds"],
        )

        requests = [record.args[0] for record in urlopen.call_args_list]
        self.assertIn(
            f"/commits/{self.BASE_SHA}/check-runs?check_name=submit-nuget",
            requests[0].full_url,
        )
        self.assertIn(
            f"/commits/{self.HEAD_SHA}/check-runs?check_name=submit-nuget",
            requests[1].full_url,
        )
        self.assertIn(
            f"/dependency-graph/compare/{self.BASE_SHA}...{self.HEAD_SHA}",
            requests[2].full_url,
        )
        self.assertEqual(
            "2026-03-10",
            requests[0].get_header("X-github-api-version"),
        )
        self.assertEqual(
            "Bearer secret-token",
            requests[0].get_header("Authorization"),
        )

    @patch("eng.quality.dependency_snapshot_readiness.time.sleep")
    @patch("eng.quality.dependency_snapshot_readiness.time.monotonic")
    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_producer_time_does_not_consume_the_propagation_budget(
        self,
        urlopen: MagicMock,
        monotonic: MagicMock,
        sleep: MagicMock,
    ) -> None:
        """Start a fresh propagation deadline only after both producers pass."""

        warning = self.encoded_warning("head snapshot is still propagating")
        urlopen.side_effect = [
            self.check_response([self.check(101, self.BASE_SHA)]),
            self.check_response(
                [self.check(102, self.HEAD_SHA, status="in_progress", conclusion=None)]
            ),
            self.check_response([self.check(102, self.HEAD_SHA)]),
            self.response([], {_SNAPSHOT_HEADER: warning}),
            self.response([], {}),
        ]
        # The producer consumes its complete budget. Propagation still receives
        # a fresh deadline at t=5 instead of inheriting the producer deadline.
        monotonic.side_effect = [0.0, 4.0, 5.0, 5.0, 6.0]

        result = dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            5,
            1,
        )

        self.assertEqual("ready", result["status"])
        self.assertEqual(
            {"producer": 5.0, "propagation": 1.0},
            result["observedWaitSeconds"],
        )
        self.assertEqual(5, urlopen.call_count)
        self.assertEqual([call(1.0), call(1.0)], sleep.call_args_list)

    @patch("eng.quality.dependency_snapshot_readiness.time.sleep")
    @patch("eng.quality.dependency_snapshot_readiness.time.monotonic")
    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_base_and_head_share_one_producer_budget(
        self,
        urlopen: MagicMock,
        monotonic: MagicMock,
        sleep: MagicMock,
    ) -> None:
        """Prevent sequential producer waits from doubling the job budget."""

        urlopen.side_effect = [
            self.check_response([]),
            self.check_response([self.check(101, self.BASE_SHA)]),
            self.check_response([]),
            self.check_response([]),
        ]
        monotonic.side_effect = [0.0, 4.0, 4.5, 5.0]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotProducerUnavailableError,
            "exact head.*was not found",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                5,
                20,
            )

        self.assertEqual([call(1.0), call(0.5)], sleep.call_args_list)

    @patch("eng.quality.dependency_snapshot_readiness.time.sleep")
    @patch(
        "eng.quality.dependency_snapshot_readiness.time.monotonic",
        return_value=0.0,
    )
    def test_retry_caps_match_each_phase_latency(
        self,
        monotonic: MagicMock,
        sleep: MagicMock,
    ) -> None:
        """Poll producer state promptly without flooding graph propagation."""

        producer_waited = dependency_snapshot_readiness._wait_before_retry(
            100.0,
            10,
            maximum_delay_seconds=(
                dependency_snapshot_readiness._PRODUCER_RETRY_MAX_SECONDS
            ),
        )
        propagation_waited = dependency_snapshot_readiness._wait_before_retry(
            100.0,
            10,
            maximum_delay_seconds=(
                dependency_snapshot_readiness._PROPAGATION_RETRY_MAX_SECONDS
            ),
        )

        self.assertTrue(producer_waited)
        self.assertTrue(propagation_waited)
        self.assertEqual([call(10.0), call(30.0)], sleep.call_args_list)
        self.assertEqual(2, monotonic.call_count)

    @patch(
        "eng.quality.dependency_snapshot_readiness._wait_before_retry",
        return_value=False,
    )
    @patch("eng.quality.dependency_snapshot_readiness._request_json")
    def test_each_phase_uses_its_registered_retry_cap(
        self,
        request_json: MagicMock,
        wait_before_retry: MagicMock,
    ) -> None:
        """Bind producer and propagation callers to their distinct cadence."""

        request_json.side_effect = [
            (self.check_response_document([]), {}),
            (
                [],
                {
                    _SNAPSHOT_HEADER: self.encoded_warning(
                        "head snapshot is still propagating"
                    )
                },
            ),
        ]
        with self.assertRaises(
            dependency_snapshot_readiness.DependencySnapshotProducerUnavailableError
        ):
            dependency_snapshot_readiness._wait_for_automatic_submission(
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                self.BASE_SHA,
                "base",
                100.0,
            )

        warning = dependency_snapshot_readiness._wait_for_warning_free_comparison(
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            self.BASE_SHA,
            self.HEAD_SHA,
            200.0,
        )

        self.assertEqual("head snapshot is still propagating", warning)
        self.assertEqual(
            [
                call(
                    100.0,
                    0,
                    maximum_delay_seconds=(
                        dependency_snapshot_readiness._PRODUCER_RETRY_MAX_SECONDS
                    ),
                ),
                call(
                    200.0,
                    0,
                    maximum_delay_seconds=(
                        dependency_snapshot_readiness._PROPAGATION_RETRY_MAX_SECONDS
                    ),
                ),
            ],
            wait_before_retry.call_args_list,
        )

    def test_only_the_exact_github_actions_producer_is_accepted(self) -> None:
        """Reject a wrong name, SHA, or app even when that check succeeded."""

        checks = [
            self.check(1, self.HEAD_SHA, name="dependency-submission"),
            self.check(2, self.BASE_SHA),
            self.check(3, self.HEAD_SHA, app_slug="third-party"),
            self.check(4, self.HEAD_SHA),
        ]

        result = dependency_snapshot_readiness._automatic_submission_checks(
            {"total_count": len(checks), "check_runs": checks},
            self.HEAD_SHA,
        )

        self.assertEqual([4], [check["id"] for check in result])

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_latest_successful_exact_producer_is_recorded(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Record the newest successful rerun when a SHA has several checks."""

        urlopen.side_effect = [
            self.check_response(
                [self.check(100, self.BASE_SHA), self.check(101, self.BASE_SHA)]
            ),
            self.check_response(
                [self.check(102, self.HEAD_SHA), self.check(103, self.HEAD_SHA)]
            ),
            self.response([], {}),
        ]

        result = dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            0,
            0,
        )

        self.assertEqual(101, result["automaticSubmission"]["base"]["checkId"])
        self.assertEqual(103, result["automaticSubmission"]["head"]["checkId"])

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_missing_base_producer_fails_as_unavailable(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Name an absent base producer instead of collapsing into graph drift."""

        urlopen.return_value = self.check_response([])

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotProducerUnavailableError,
            "exact base.*was not found",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_missing_head_producer_fails_as_unavailable(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Require a producer receipt for the exact pull-request head."""

        urlopen.side_effect = [
            self.check_response([self.check(101, self.BASE_SHA)]),
            self.check_response([]),
        ]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotProducerUnavailableError,
            "exact head.*was not found",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_active_producer_timeout_is_not_reported_as_failure(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Keep producer timeout distinct from a terminal producer failure."""

        urlopen.side_effect = [
            self.check_response([self.check(101, self.BASE_SHA)]),
            self.check_response(
                [self.check(102, self.HEAD_SHA, status="in_progress", conclusion=None)]
            ),
        ]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotProducerUnavailableError,
            "exact head.*did not complete",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_terminal_producer_failure_fails_immediately(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Report a failed exact producer without waiting for graph propagation."""

        urlopen.side_effect = [
            self.check_response([self.check(101, self.BASE_SHA)]),
            self.check_response(
                [self.check(102, self.HEAD_SHA, conclusion="failure")]
            ),
        ]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotProducerFailedError,
            "exact head.*failed.*failure",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                300,
                900,
            )

        self.assertEqual(2, urlopen.call_count)

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_persistent_warning_fails_as_incomplete_comparison(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Reject graph propagation that exhausts its independent budget."""

        warning_text = "base and head snapshot counts do not match"
        urlopen.side_effect = [
            *self.ready_producer_responses(),
            self.response(
                [],
                {_SNAPSHOT_HEADER: self.encoded_warning(warning_text)},
            ),
        ]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotComparisonIncompleteError,
            warning_text,
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_present_but_empty_warning_header_is_complete(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Treat GitHub's empty warning value as ready without shell artifacts."""

        urlopen.side_effect = [
            *self.ready_producer_responses(),
            self.response([], {_SNAPSHOT_HEADER: ""}),
        ]

        result = dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            0,
            0,
        )

        self.assertEqual("ready", result["status"])

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_malformed_warning_is_not_treated_as_absent(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Reject an unreadable readiness signal instead of bypassing it."""

        urlopen.side_effect = [
            *self.ready_producer_responses(),
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
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_invalid_producer_response_is_rejected(self, urlopen: MagicMock) -> None:
        """Validate the documented check-run collection response shape."""

        urlopen.return_value = self.response([], {})

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "invalid automatic-submission check response",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_invalid_producer_receipt_is_rejected(self, urlopen: MagicMock) -> None:
        """Require an auditable ID and completion time from each producer."""

        invalid_checks = (
            self.check(0, self.BASE_SHA),
            self.check(101, self.BASE_SHA, completed_at=""),
        )
        for invalid_check in invalid_checks:
            with self.subTest(check=invalid_check):
                urlopen.reset_mock()
                urlopen.side_effect = [
                    self.check_response([invalid_check]),
                    self.check_response([self.check(102, self.HEAD_SHA)]),
                    self.response([], {}),
                ]
                with self.assertRaises(
                    dependency_snapshot_readiness.DependencySnapshotReadinessError
                ):
                    dependency_snapshot_readiness.verify_readiness(
                        self.BASE_SHA,
                        self.HEAD_SHA,
                        self.API_URL,
                        self.REPOSITORY,
                        self.TOKEN,
                        0,
                        0,
                    )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_non_list_comparison_is_rejected(self, urlopen: MagicMock) -> None:
        """Validate the documented dependency-comparison response shape."""

        urlopen.side_effect = [
            *self.ready_producer_responses(),
            self.response({"unexpected": "object"}, {}),
        ]

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "invalid dependency-comparison response",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_unexpected_success_status_is_rejected(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Require the documented OK response instead of accepting any 2xx."""

        urlopen.return_value = self.response({}, {}, status=202)

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
                0,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_transport_failure_is_contextualized(self, urlopen: MagicMock) -> None:
        """Keep GitHub transport failures distinct from incomplete evidence."""

        urlopen.side_effect = urllib.error.URLError("offline")

        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "dependency readiness could not be reached",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                0,
                0,
            )

    def test_invalid_trust_boundary_inputs_are_rejected(self) -> None:
        """Reject identities that could redirect or detach the API comparison."""

        cases = (
            ("short", self.HEAD_SHA, self.API_URL, self.REPOSITORY, self.TOKEN),
            (self.BASE_SHA, "B" * 40, self.API_URL, self.REPOSITORY, self.TOKEN),
            (
                self.BASE_SHA,
                self.HEAD_SHA,
                "http://api.github.test",
                self.REPOSITORY,
                self.TOKEN,
            ),
            (self.BASE_SHA, self.HEAD_SHA, self.API_URL, "missing-slash", self.TOKEN),
            (self.BASE_SHA, self.HEAD_SHA, self.API_URL, self.REPOSITORY, " "),
        )
        for base, head, api_url, repository, token in cases:
            with (
                self.subTest(
                    base=base,
                    head=head,
                    api_url=api_url,
                    repository=repository,
                ),
                self.assertRaises(
                    dependency_snapshot_readiness.DependencySnapshotReadinessError
                ),
            ):
                dependency_snapshot_readiness.verify_readiness(
                    base,
                    head,
                    api_url,
                    repository,
                    token,
                    0,
                    0,
                )

    def test_identical_revisions_and_invalid_budgets_are_rejected(self) -> None:
        """Require a real comparison and two finite non-negative windows."""

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
                0,
            )
        for producer, propagation, parameter in (
            (-1, 0, "producer_wait_seconds"),
            (0, -1, "propagation_wait_seconds"),
            (float("nan"), 0, "producer_wait_seconds"),
            (0, float("inf"), "propagation_wait_seconds"),
        ):
            with (
                self.subTest(parameter=parameter),
                self.assertRaisesRegex(
                    dependency_snapshot_readiness.DependencySnapshotReadinessError,
                    parameter,
                ),
            ):
                dependency_snapshot_readiness.verify_readiness(
                    self.BASE_SHA,
                    self.HEAD_SHA,
                    self.API_URL,
                    self.REPOSITORY,
                    self.TOKEN,
                    producer,
                    propagation,
                )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_cli_writes_the_phase_receipts(self, urlopen: MagicMock) -> None:
        """Keep the workflow-facing command and JSON handoff executable."""

        urlopen.side_effect = self.ready_responses()
        with (
            tempfile.TemporaryDirectory(prefix="doka-snapshot-readiness-") as directory,
            patch.dict(
                "os.environ",
                {"GITHUB_TOKEN": self.TOKEN},
            ),
        ):
            output = Path(directory) / "readiness.json"
            exit_code = dependency_snapshot_readiness.main(
                [
                    "verify",
                    "--base-revision",
                    self.BASE_SHA,
                    "--head-revision",
                    self.HEAD_SHA,
                    "--api-url",
                    self.API_URL,
                    "--repository",
                    self.REPOSITORY,
                    "--producer-wait-seconds",
                    "300",
                    "--propagation-wait-seconds",
                    "900",
                    "--output",
                    str(output),
                ]
            )

            self.assertEqual(0, exit_code)
            receipt = json.loads(output.read_text(encoding="ascii"))
            self.assertEqual(3, receipt["schemaVersion"])
            self.assertEqual("ready", receipt["status"])
            self.assertEqual(101, receipt["automaticSubmission"]["base"]["checkId"])
            self.assertEqual(102, receipt["automaticSubmission"]["head"]["checkId"])

    def ready_responses(self) -> list[MagicMock]:
        """Return successful base, head, and comparison API responses."""

        return [*self.ready_producer_responses(), self.response([], {})]

    def ready_producer_responses(self) -> list[MagicMock]:
        """Return successful exact base and head producer responses."""

        return [
            self.check_response([self.check(101, self.BASE_SHA)]),
            self.check_response([self.check(102, self.HEAD_SHA)]),
        ]

    @staticmethod
    def check(
        check_id: int,
        revision: str,
        *,
        name: str = "submit-nuget",
        app_slug: str = "github-actions",
        status: str = "completed",
        conclusion: str | None = "success",
        completed_at: str = "2026-08-14T20:46:13Z",
    ) -> dict[str, object]:
        """Build the documented check-run fields used by the synchronizer."""

        return {
            "id": check_id,
            "name": name,
            "head_sha": revision,
            "status": status,
            "conclusion": conclusion,
            "completed_at": completed_at,
            "app": {"slug": app_slug},
        }

    @classmethod
    def expected_receipt(cls, check_id: int, revision: str) -> dict[str, object]:
        """Return the canonical successful-producer receipt."""

        return {
            "appSlug": "github-actions",
            "checkId": check_id,
            "checkName": "submit-nuget",
            "completedAt": "2026-08-14T20:46:13Z",
            "conclusion": "success",
            "revision": revision,
        }

    @classmethod
    def check_response(cls, checks: list[dict[str, object]]) -> MagicMock:
        """Return a documented check-run collection response."""

        return cls.response(
            cls.check_response_document(checks),
            {},
        )

    @staticmethod
    def check_response_document(
        checks: list[dict[str, object]],
    ) -> dict[str, object]:
        """Return the decoded check-run collection document."""

        return {"total_count": len(checks), "check_runs": checks}

    @staticmethod
    def encoded_warning(message: str) -> str:
        """Encode the warning header exactly as GitHub documents it."""

        return base64.b64encode(message.encode("utf-8")).decode("ascii")

    @staticmethod
    def response(
        document: object,
        headers: dict[str, str],
        status: int = 200,
    ) -> MagicMock:
        """Return a context-managed HTTP response double."""

        response = MagicMock()
        response.status = status
        response.read.return_value = json.dumps(document).encode("utf-8")
        response.headers.items.return_value = headers.items()
        context = MagicMock()
        context.__enter__.return_value = response
        return context


if __name__ == "__main__":
    unittest.main()
