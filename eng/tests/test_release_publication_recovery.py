"""Exercise same-run recovery through real on-disk candidate admission."""

from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
import os
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

from eng.release import lines, publication
from eng.tests.test_release_lines import initial_policy
from eng.tests.test_release_publication_admission import (
    authority,
    environment,
    failure,
    publisher_environment_client,
)


# WHY: pin the hosted upload name independently so production renames cannot
# silently rewrite the fixture protocol.
ADMISSION_STEP_NAME = "Freeze publication admission for same-run recovery"
RUN_ENDPOINT = f"/repos/{lines.REPOSITORY}/actions/runs/123"


def candidate_files(root: Path) -> argparse.Namespace:
    """Write actual manifest and candidate-receipt bytes for the admission command."""
    snapshot = lines.freeze_line(initial_policy(), "refs/heads/main", "10.6.2")
    documents = {
        root / "candidate" / "release-candidate-evidence.json": {
            "schemaVersion": 4, "releaseVersion": "10.6.2", "expectedReleaseTag": "v10.6.2",
            "source": {"ref": "refs/heads/main", "commit": "b" * 40}, "releaseLine": snapshot,
        },
        root / "candidate-receipt.json": {
            "repository": lines.REPOSITORY, "sourceRef": "refs/heads/main", "sourceCommit": "b" * 40,
            "releaseVersion": "10.6.2", "expectedReleaseTag": "v10.6.2", "releaseLine": snapshot,
            "candidateRunId": 123, "candidateRunAttempt": 1,
        },
    }
    for path, value in documents.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="ascii")

    return publication.build_parser().parse_args([
        "admit", "--repository", lines.REPOSITORY,
        "--candidate-root", str(root / "candidate"),
        "--candidate-receipt", str(root / "candidate-receipt.json"),
        "--output", str(root / "admission" / "publication-admission.json"),
        "--authority-output", str(root / "admission" / "authorization-observation.json"),
    ])


def skipped_boundaries() -> list[dict]:
    """Return runner-style skipped steps that still retain execution timestamps."""
    return [{
        "name": name, "status": "completed", "conclusion": "skipped",
        "started_at": "2026-10-22T12:00:00Z", "completed_at": "2026-10-22T12:00:00Z",
    } for name in (ADMISSION_STEP_NAME, publication.PUBLISHER_STEP_NAME)]


def publisher_job() -> dict:
    """Describe an ended publisher whose upload and actual publication were skipped."""
    return {
        "id": 100, "run_id": 123, "name": "publish", "status": "completed", "conclusion": "failure",
        "steps": skipped_boundaries(),
    }


def admission_archive(original: dict, observation: dict) -> tuple[dict, bytes, dict[str, bytes]]:
    """Package exact original evidence, including an optional publication receipt."""
    contents = {
        "publication-admission.json": (json.dumps(original, indent=2) + "\n").encode("ascii"),
        "authorization-observation.json": (json.dumps(observation, indent=2) + "\n").encode("ascii"),
        "release-publication-receipt.json": b'{"publicationState":"partial","workflowRunId":"123"}\n',
    }
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w") as archive:
        for name, raw in contents.items():
            archive.writestr(name, raw)

    raw = stream.getvalue()
    metadata = {
        "id": 12, "name": "publication-admission-attempt-1", "expired": False,
        "digest": f"sha256:{hashlib.sha256(raw).hexdigest()}",
        "workflow_run": {"id": 123, "head_sha": "b" * 40},
    }

    return metadata, raw, contents


class PublicationRecoveryTests(unittest.TestCase):
    """Preserve exact bytes while separating first admission from archive recovery."""

    def setUp(self) -> None:
        """Isolate real files and replace only external API observation transports."""
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name).resolve()
        self.args = candidate_files(self.root)
        self.paths = [self.args.candidate_root / "release-candidate-evidence.json", self.args.candidate_receipt]
        self.original_bytes = {path.name: path.read_bytes() for path in self.paths}
        snapshot = json.loads(self.original_bytes["candidate-receipt.json"])["releaseLine"]
        self.expected_identity = {
            "repository": lines.REPOSITORY, "sourceRef": "refs/heads/main", "sourceCommit": "b" * 40,
            "releaseVersion": "10.6.2", "releaseTag": "v10.6.2", "workflowPath": snapshot["workflowPath"],
            "workflowRunId": "123", "candidateRunAttempt": "1", "originalPublicationAttempt": "2",
            "manifestSha256": hashlib.sha256(self.original_bytes["release-candidate-evidence.json"]).hexdigest(),
            "candidateReceiptSha256": hashlib.sha256(self.original_bytes["candidate-receipt.json"]).hexdigest(),
            "releaseLineSha256": publication.canonical_digest(snapshot),
        }
        self.client, self.responses, self.collections = publisher_environment_client()
        self.job = publisher_job()
        self.responses[f"{RUN_ENDPOINT}/attempts/1"] = {"id": 123, "run_attempt": 1, "status": "completed"}
        self.collections[f"{RUN_ENDPOINT}/artifacts"] = []
        self.collections[f"{RUN_ENDPOINT}/attempts/1/jobs?filter=all"] = [self.job]
        self.observed = authority()
        hosted = environment()
        hosted["GITHUB_RUN_ATTEMPT"] = "2"
        for patcher in (
            mock.patch.object(publication, "GitHubClient", return_value=self.client),
            mock.patch.object(publication, "fetch_current_authority", return_value=self.observed),
            mock.patch.dict(os.environ, hosted, clear=True),
        ):
            patcher.start()
            self.addCleanup(patcher.stop)

    def test_first_attempt_admits_exact_candidate(self) -> None:
        """A first publication attempt needs no earlier attempt history."""
        # Arrange
        os.environ["GITHUB_RUN_ATTEMPT"] = "1"
        expected = dict(self.expected_identity, originalPublicationAttempt="1")
        self.responses.pop(f"{RUN_ENDPOINT}/attempts/1")
        self.collections.pop(f"{RUN_ENDPOINT}/attempts/1/jobs?filter=all")

        # Act
        publication.admit(self.args)

        # Assert
        self.assertEqual(expected, publication.read_json(self.args.output)["identity"])
        self.assertEqual(self.original_bytes, {path.name: path.read_bytes() for path in self.paths})

    def test_failure_before_upload_admits_unchanged_candidate_with_skipped_timestamps(self) -> None:
        """Setup, download and revalidation failures leave both durable boundaries skipped."""
        for failed_step in ("Set up approved .NET SDK", "Download immutable candidate", "Revalidate candidate"):
            with self.subTest(failed_step=failed_step):
                # Arrange
                self.args.output = self.root / f"retry-{failed_step.replace(' ', '-')}.json"
                self.job["steps"] = [{
                    "name": failed_step, "status": "completed", "conclusion": "failure",
                    "started_at": "2026-10-22T11:59:00Z", "completed_at": "2026-10-22T12:00:00Z",
                }, *skipped_boundaries()]

                # Act
                publication.admit(self.args)

                # Assert
                self.assertEqual(self.expected_identity, publication.read_json(self.args.output)["identity"])
                self.assertEqual(self.original_bytes, {path.name: path.read_bytes() for path in self.paths})

    def test_both_unstarted_boundaries_allow_first_admission(self) -> None:
        """An ended job may retain explicitly queued or pending unstarted boundaries."""
        for status in ("pending", "queued"):
            with self.subTest(status=status):
                # Arrange
                self.args.output = self.root / f"unstarted-{status}.json"
                self.job["conclusion"] = "cancelled"
                self.job["steps"] = [{"name": name, "status": status, "started_at": None} for name in (
                    ADMISSION_STEP_NAME, publication.PUBLISHER_STEP_NAME,
                )]

                # Act
                publication.admit(self.args)

                # Assert
                self.assertEqual(self.expected_identity, publication.read_json(self.args.output)["identity"])

    def test_entirely_skipped_publisher_needs_no_step_markers(self) -> None:
        """A conclusively skipped publisher job cannot have frozen or published bytes."""
        # Arrange
        self.job.update(conclusion="skipped", steps=[])

        # Act
        publication.admit(self.args)

        # Assert
        self.assertEqual(self.expected_identity, publication.read_json(self.args.output)["identity"])

    def test_started_upload_requires_original_archive_even_with_skipped_publisher(self) -> None:
        """A lost in-progress, failed or successful upload must never mint a replacement."""
        for status, conclusion in (("in_progress", None), ("completed", "failure"), ("completed", "success")):
            with self.subTest(status=status, conclusion=conclusion):
                # Arrange
                self.args.output = self.root / f"upload-{status}-{conclusion}.json"
                self.job["steps"] = skipped_boundaries()
                self.job["steps"][0].update(status=status, conclusion=conclusion)

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("refusing replacement identity", str(error))
                self.assertFalse(self.args.output.exists())

    def test_started_actual_publisher_requires_original_archive(self) -> None:
        """An actual publisher start remains irreversible regardless of its eventual result."""
        for status, conclusion in (("in_progress", None), ("completed", "failure"), ("completed", "success")):
            with self.subTest(status=status, conclusion=conclusion):
                # Arrange
                self.args.output = self.root / f"publisher-{status}-{conclusion}.json"
                self.job["steps"] = skipped_boundaries()
                self.job["steps"][1].update(status=status, conclusion=conclusion)

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("refusing replacement identity", str(error))
                self.assertFalse(self.args.output.exists())

    def test_missing_either_boundary_rejects_first_admission(self) -> None:
        """A single legacy marker cannot prove whether admission evidence was already frozen."""
        for retained in ((), (0,), (1,)):
            with self.subTest(retained=retained):
                # Arrange
                self.args.output = self.root / f"missing-{retained}.json"
                boundaries = skipped_boundaries()
                self.job["steps"] = [boundaries[index] for index in retained]

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("boundary", str(error))
                self.assertFalse(self.args.output.exists())

    def test_runner_setup_failure_or_markerless_cancellation_cannot_mint_admission(self) -> None:
        """Missing declared-step history remains insufficient proof of non-publication."""
        histories = (
            ("failure", [{"name": "Set up job", "status": "completed", "conclusion": "failure"}]),
            ("cancelled", []),
        )

        for conclusion, steps in histories:
            with self.subTest(conclusion=conclusion):
                # Arrange
                self.args.output = self.root / f"missing-history-{conclusion}.json"
                self.job.update(
                    status="completed", conclusion=conclusion, steps=steps,
                    runner_id=None, runner_name=None,
                )

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("no unambiguous admission boundary", str(error))
                self.assertFalse(self.args.output.exists())
                self.assertEqual(self.original_bytes, {path.name: path.read_bytes() for path in self.paths})

    def test_duplicate_either_boundary_rejects_first_admission(self) -> None:
        """Duplicate upload or publisher markers leave earlier admission history ambiguous."""
        for duplicate in (0, 1):
            with self.subTest(duplicate=duplicate):
                # Arrange
                self.args.output = self.root / f"duplicate-{duplicate}.json"
                boundaries = skipped_boundaries()
                self.job["steps"] = [*boundaries, copy.deepcopy(boundaries[duplicate])]

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("boundary", str(error))
                self.assertFalse(self.args.output.exists())

    def test_inconclusive_boundary_state_rejects_first_admission(self) -> None:
        """Missing outcomes and pending steps with a start time cannot prove non-admission."""
        for boundary in (0, 1):
            for status, started_at in (("completed", None), ("pending", "2026-10-22T12:00:00Z")):
                with self.subTest(boundary=boundary, status=status):
                    # Arrange
                    self.args.output = self.root / f"inconclusive-{boundary}-{status}.json"
                    self.job["steps"] = skipped_boundaries()
                    self.job["steps"][boundary].update(status=status, conclusion=None, started_at=started_at)

                    # Act
                    error = failure(lambda: publication.admit(self.args))

                    # Assert
                    self.assertIn("refusing replacement identity", str(error))
                    self.assertFalse(self.args.output.exists())

    def test_unfinished_publisher_cannot_authorize_replacement(self) -> None:
        """An ended attempt summary cannot override a still-waiting or running publisher."""
        for status in ("waiting", "queued", "in_progress", None):
            with self.subTest(status=status):
                # Arrange
                self.args.output = self.root / f"unfinished-{status}.json"
                self.job.update(status=status, conclusion=None)

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("unfinished", str(error))
                self.assertFalse(self.args.output.exists())

    def test_each_earlier_attempt_must_prove_no_admission(self) -> None:
        """A safe first attempt cannot hide a later upload whose original archive vanished."""
        # Arrange
        os.environ["GITHUB_RUN_ATTEMPT"] = "3"
        later_job = publisher_job()
        later_job["steps"][0].update(status="completed", conclusion="failure")
        self.responses[f"{RUN_ENDPOINT}/attempts/2"] = {"id": 123, "run_attempt": 2, "status": "completed"}
        self.collections[f"{RUN_ENDPOINT}/attempts/2/jobs?filter=all"] = [later_job]

        # Act
        error = failure(lambda: publication.admit(self.args))

        # Assert
        self.assertIn("refusing replacement identity", str(error))
        self.assertFalse(self.args.output.exists())

    def test_original_archive_restores_exact_bytes_before_history_proof(self) -> None:
        """Exact recovery preserves the original admission, authority, receipt and candidate."""
        # Arrange
        original = publication.create_admission(
            dict(self.expected_identity, originalPublicationAttempt="1"), authority()
        )
        metadata, raw, contents = admission_archive(original, authority())
        self.collections[f"{RUN_ENDPOINT}/artifacts"] = [metadata]
        self.client.download.return_value = raw
        # WHY: retained original proof takes precedence over unavailable marker history.
        self.responses.pop(f"{RUN_ENDPOINT}/attempts/1")
        self.collections.pop(f"{RUN_ENDPOINT}/attempts/1/jobs?filter=all")
        self.observed.update(authority(dict(initial_policy(), revision="2026-10-23.2")))

        # Act
        publication.admit(self.args)

        # Assert
        self.assertEqual(contents, {name: (self.args.output.parent / name).read_bytes() for name in contents})
        self.assertEqual(self.original_bytes, {path.name: path.read_bytes() for path in self.paths})
        self.client.download.assert_called_once_with(f"/repos/{lines.REPOSITORY}/actions/artifacts/12/zip")

    def test_original_archive_rejects_tampered_manifest_or_candidate_receipt(self) -> None:
        """Even valid JSON whitespace changes cannot replace originally admitted file bytes."""
        for changed_path in self.paths:
            with self.subTest(changed_path=changed_path.name):
                # Arrange
                for path in self.paths:
                    path.write_bytes(self.original_bytes[path.name])

                original = publication.create_admission(
                    dict(self.expected_identity, originalPublicationAttempt="1"), authority()
                )
                metadata, raw, _ = admission_archive(original, authority())
                self.collections[f"{RUN_ENDPOINT}/artifacts"] = [metadata]
                self.client.download.return_value = raw
                changed_path.write_bytes(changed_path.read_bytes() + b"\n")

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("originally admitted candidate bytes", str(error))
                self.assertFalse(self.args.output.exists())

    def test_expired_original_archive_cannot_be_replaced_by_skipped_history(self) -> None:
        """A retained expired archive is a hard failure before any non-admission proof."""
        # Arrange
        original = publication.create_admission(
            dict(self.expected_identity, originalPublicationAttempt="1"), authority()
        )
        metadata, _, _ = admission_archive(original, authority())
        metadata["expired"] = True
        self.collections[f"{RUN_ENDPOINT}/artifacts"] = [metadata]

        # Act
        error = failure(lambda: publication.admit(self.args))

        # Assert
        self.assertIn("expired", str(error))
        self.assertFalse(self.args.output.exists())
        self.client.request.assert_not_called()
        self.client.download.assert_not_called()

    def test_revoked_authority_rejects_first_admission_and_original_archive(self) -> None:
        """Neither safe skipped history nor original proof overrides current line revocation."""
        for restore in (False, True):
            with self.subTest(restore=restore):
                # Arrange
                if restore:
                    original = publication.create_admission(
                        dict(self.expected_identity, originalPublicationAttempt="1"), authority()
                    )
                    metadata, raw, _ = admission_archive(original, authority())
                    self.collections[f"{RUN_ENDPOINT}/artifacts"] = [metadata]
                    self.client.download.return_value = raw

                policy = initial_policy()
                policy["revision"] = "2026-10-23.2"
                policy["lines"][0]["state"] = "inactive"
                self.observed.update(authority(policy))

                # Act
                error = failure(lambda: publication.admit(self.args))

                # Assert
                self.assertIn("not eligible for publication", str(error))
                self.assertEqual(self.original_bytes, {path.name: path.read_bytes() for path in self.paths})


if __name__ == "__main__":
    unittest.main()
