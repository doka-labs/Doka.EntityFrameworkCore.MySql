"""Tests for fail-closed automatic dependency-snapshot readiness."""

from __future__ import annotations

import base64
import json
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import MagicMock, patch

from eng.quality import dependency_snapshot_readiness


_SNAPSHOT_HEADER = "x-github-dependency-graph-snapshot-warnings"


class DependencySnapshotReadinessTests(unittest.TestCase):
    """Prove exact comparison binding, bounded retry, and fail-closed output."""

    BASE_SHA = "a" * 40
    HEAD_SHA = "b" * 40
    API_URL = "https://api.github.example"
    REPOSITORY = "doka-labs/provider"
    TOKEN = "secret-token"

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_warning_free_comparison_is_ready(self, urlopen: MagicMock) -> None:
        """Bind readiness to GitHub's exact base/head comparison response."""

        urlopen.return_value = self.response([], {})

        result = dependency_snapshot_readiness.verify_readiness(
            self.BASE_SHA,
            self.HEAD_SHA,
            self.API_URL,
            self.REPOSITORY,
            self.TOKEN,
            0,
        )

        self.assertEqual(2, result["schemaVersion"])
        self.assertEqual("ready", result["status"])
        self.assertEqual(
            {"status": "complete", "snapshotWarnings": []},
            result["dependencyComparison"],
        )
        request = urlopen.call_args.args[0]
        self.assertIn(
            f"/dependency-graph/compare/{self.BASE_SHA}...{self.HEAD_SHA}?per_page=1",
            request.full_url,
        )
        self.assertEqual(
            "2026-03-10",
            request.get_header("X-github-api-version"),
        )
        self.assertEqual(
            "Bearer secret-token",
            request.get_header("Authorization"),
        )

    @patch("eng.quality.dependency_snapshot_readiness.time.sleep")
    @patch("eng.quality.dependency_snapshot_readiness.time.monotonic")
    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_transient_warning_is_retried_with_exponential_backoff(
        self,
        urlopen: MagicMock,
        monotonic: MagicMock,
        sleep: MagicMock,
    ) -> None:
        """Wait for independently produced snapshots without accepting a warning."""

        warning = self.encoded_warning("head snapshot is still propagating")
        urlopen.side_effect = [
            self.response([], {_SNAPSHOT_HEADER: warning}),
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
        self.assertEqual(2, urlopen.call_count)
        sleep.assert_called_once_with(1.0)

    @patch("eng.quality.dependency_snapshot_readiness.time.monotonic")
    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_persistent_warning_fails_closed(
        self,
        urlopen: MagicMock,
        monotonic: MagicMock,
    ) -> None:
        """Reject an incomplete comparison when the retry budget expires."""

        warning_text = "base and head snapshot counts do not match"
        urlopen.return_value = self.response(
            [],
            {_SNAPSHOT_HEADER: self.encoded_warning(warning_text)},
        )
        monotonic.return_value = 0.0

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

        self.assertEqual(1, urlopen.call_count)

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_malformed_warning_is_not_treated_as_absent(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Reject an unreadable readiness signal instead of bypassing it."""

        urlopen.return_value = self.response(
            [],
            {_SNAPSHOT_HEADER: "not base64"},
        )

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

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_non_list_comparison_is_rejected(self, urlopen: MagicMock) -> None:
        """Validate the documented dependency-comparison response shape."""

        urlopen.return_value = self.response({"unexpected": "object"}, {})

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
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_unexpected_success_status_is_rejected(
        self,
        urlopen: MagicMock,
    ) -> None:
        """Require the documented OK response instead of accepting any 2xx."""

        urlopen.return_value = self.response([], {}, status=202)

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
                )

    def test_identical_revisions_and_negative_wait_are_rejected(self) -> None:
        """Require a real comparison and a bounded non-negative retry window."""

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
        with self.assertRaisesRegex(
            dependency_snapshot_readiness.DependencySnapshotReadinessError,
            "wait_seconds cannot be negative",
        ):
            dependency_snapshot_readiness.verify_readiness(
                self.BASE_SHA,
                self.HEAD_SHA,
                self.API_URL,
                self.REPOSITORY,
                self.TOKEN,
                -1,
            )

    @patch("eng.quality.dependency_snapshot_readiness.urllib.request.urlopen")
    def test_cli_writes_the_readiness_receipt(self, urlopen: MagicMock) -> None:
        """Keep the workflow-facing command and JSON handoff executable."""

        urlopen.return_value = self.response([], {})
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
                    "--wait-seconds",
                    "0",
                    "--output",
                    str(output),
                ]
            )

            self.assertEqual(0, exit_code)
            receipt = json.loads(output.read_text(encoding="ascii"))
            self.assertEqual(2, receipt["schemaVersion"])
            self.assertEqual("ready", receipt["status"])

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
