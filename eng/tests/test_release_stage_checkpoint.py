"""Regression tests for resumable release-stage evidence."""

from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

from eng.release import checkpoint as release_stage_checkpoint


class ReleaseStageCheckpointTests(unittest.TestCase):
    """Prove exact artifact, source, and workflow-attempt binding."""

    def setUp(self) -> None:
        """Create one isolated repository and candidate root."""
        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-release-checkpoint-"
        )
        self.repository = Path(self.temporary_directory.name) / "repository"
        self.root = self.repository / "candidate"
        self.checkpoints = self.repository / "checkpoints"
        self.root.mkdir(parents=True)
        subprocess.run(["git", "init", "-q", str(self.repository)], check=True)
        subprocess.run(
            ["git", "-C", str(self.repository), "config", "user.name", "Test"],
            check=True,
        )
        subprocess.run(
            ["git", "-C", str(self.repository), "config", "commit.gpgsign", "false"],
            check=True,
        )
        subprocess.run(
            [
                "git",
                "-C",
                str(self.repository),
                "config",
                "user.email",
                "test@example.invalid",
            ],
            check=True,
        )
        tracked = self.repository / "tracked.txt"
        tracked.write_text("source\n", encoding="ascii")
        subprocess.run(["git", "-C", str(self.repository), "add", "tracked.txt"], check=True)
        subprocess.run(
            ["git", "-C", str(self.repository), "commit", "-q", "-m", "initial"],
            check=True,
        )
        self.run_id = "github-123"
        self.source_ref = "refs/tags/v10.0.0-rc.1"
        self.release_tag = "v10.0.0-rc.1"
        self.run_attempt = 2

    def tearDown(self) -> None:
        """Remove the isolated repository."""
        self.temporary_directory.cleanup()

    def write(
        self,
        stage: str,
        artifact: Path,
    ) -> Path:
        """Write one receipt through the production identity contract."""
        started = datetime.now(timezone.utc) - timedelta(seconds=1)
        return release_stage_checkpoint.write_checkpoint(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id=self.run_id,
            stage=stage,
            source_ref=self.source_ref,
            release_tag=self.release_tag,
            run_attempt=self.run_attempt,
            runner_identity="GitHub Actions 7",
            started_utc=started.isoformat().replace("+00:00", "Z"),
            artifacts=[artifact],
        )

    def verify(
        self,
        stage: str,
        *,
        source_ref: str | None = None,
        release_tag: str | None = None,
        maximum_run_attempt: int | None = None,
    ) -> Path:
        """Verify one receipt while allowing one identity field to vary."""
        return release_stage_checkpoint.verify_checkpoint(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id=self.run_id,
            stage=stage,
            source_ref=source_ref or self.source_ref,
            release_tag=release_tag or self.release_tag,
            maximum_run_attempt=maximum_run_attempt or self.run_attempt,
        )

    def create_artifact(self, name: str = "evidence.json") -> Path:
        """Create one deterministic candidate artifact."""
        artifact = self.root / name
        artifact.parent.mkdir(parents=True, exist_ok=True)
        artifact.write_text("evidence\n", encoding="ascii")
        return artifact

    def test_round_trip_records_execution_identity_and_duration(self) -> None:
        """Accept unchanged evidence and retain the complete execution context."""
        path = self.write("quality", self.create_artifact())

        verified = self.verify("quality")
        payload = json.loads(path.read_text(encoding="ascii"))

        self.assertEqual(path, verified)
        self.assertEqual(self.source_ref, payload["sourceRef"])
        self.assertEqual(self.release_tag, payload["releaseTag"])
        self.assertEqual(self.run_attempt, payload["runAttempt"])
        self.assertEqual("GitHub Actions 7", payload["runnerIdentity"])
        self.assertGreaterEqual(payload["durationSeconds"], 0)

    def test_modified_artifact_rejects_resume(self) -> None:
        """Reject a completed marker after any retained byte changes."""
        artifact = self.create_artifact("coverage.xml")
        self.write("coverage", artifact)
        artifact.write_text("changed\n", encoding="ascii")

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "does not match its digest",
        ):
            self.verify("coverage")

    def test_different_source_commit_rejects_resume(self) -> None:
        """Reject a valid artifact receipt after the repository advances."""
        self.write("package", self.create_artifact("package.nupkg"))
        tracked = self.repository / "tracked.txt"
        tracked.write_text("advanced\n", encoding="ascii")
        subprocess.run(["git", "-C", str(self.repository), "add", "tracked.txt"], check=True)
        subprocess.run(
            ["git", "-C", str(self.repository), "commit", "-q", "-m", "advance"],
            check=True,
        )

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "invalid sourceCommit",
        ):
            self.verify("package")

    def test_different_source_ref_rejects_resume(self) -> None:
        """Reject a receipt restored into a different tag or branch ref."""
        self.write("quality", self.create_artifact())

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "invalid sourceRef",
        ):
            self.verify("quality", source_ref="refs/tags/v10.0.0-rc.2")

    def test_different_release_tag_rejects_resume(self) -> None:
        """Reject a receipt whose release-semantic identity changed."""
        self.write("quality", self.create_artifact())

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "invalid releaseTag",
        ):
            self.verify("quality", release_tag="v10.0.0-rc.2")

    def test_newer_run_attempt_rejects_restored_receipt(self) -> None:
        """Never consume evidence written by a logically later attempt."""
        self.write("quality", self.create_artifact())

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "newer runAttempt",
        ):
            self.verify("quality", maximum_run_attempt=1)

    def test_exact_set_accepts_only_complete_expected_receipts(self) -> None:
        """Accept the canonical set after every receipt verifies independently."""
        self.write("quality", self.create_artifact("quality/report.json"))
        self.write("package", self.create_artifact("packages/provider.nupkg"))

        verified = release_stage_checkpoint.verify_checkpoint_set(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id=self.run_id,
            source_ref=self.source_ref,
            release_tag=self.release_tag,
            maximum_run_attempt=self.run_attempt,
            expected_stages=["quality", "package"],
        )

        self.assertEqual(2, len(verified))

    def test_exact_set_rejects_missing_or_unexpected_receipts(self) -> None:
        """Fail closed when the assembled DAG is incomplete or contaminated."""
        self.write("quality", self.create_artifact("quality/report.json"))

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "is missing: package",
        ):
            release_stage_checkpoint.verify_checkpoint_set(
                repository=self.repository,
                root=self.root,
                checkpoint_directory=self.checkpoints,
                run_id=self.run_id,
                source_ref=self.source_ref,
                release_tag=self.release_tag,
                maximum_run_attempt=self.run_attempt,
                expected_stages=["quality", "package"],
            )

        for selection_name in (
            "assemble-input-artifacts",
            "sbom-input-artifacts",
        ):
            with self.subTest(selection=selection_name):
                selection_path = self.checkpoints / f"{selection_name}.json"
                selection_path.write_text("{}\n", encoding="utf-8")
                with self.assertRaisesRegex(
                    release_stage_checkpoint.CheckpointError,
                    f"is unexpected: {selection_name}",
                ):
                    release_stage_checkpoint.verify_checkpoint_set(
                        repository=self.repository,
                        root=self.root,
                        checkpoint_directory=self.checkpoints,
                        run_id=self.run_id,
                        source_ref=self.source_ref,
                        release_tag=self.release_tag,
                        maximum_run_attempt=self.run_attempt,
                        expected_stages=["quality"],
                    )
                selection_path.unlink()

    def test_symlink_artifact_rejects_checkpoint_creation(self) -> None:
        """Do not bind a receipt to a path whose identity can redirect later."""
        target = self.create_artifact("target.json")
        link = self.root / "linked.json"
        link.symlink_to(target)

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "must not use a symbolic link",
        ):
            self.write("linked", link)

    def test_symlink_replacement_rejects_resume(self) -> None:
        """Reject an artifact replaced by an equal-content symlink after success."""
        artifact = self.create_artifact("manifest.json")
        self.write("manifest", artifact)
        replacement = self.create_artifact("replacement.json")
        artifact.unlink()
        artifact.symlink_to(replacement)

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "must not use a symbolic link",
        ):
            self.verify("manifest")


if __name__ == "__main__":
    unittest.main()
