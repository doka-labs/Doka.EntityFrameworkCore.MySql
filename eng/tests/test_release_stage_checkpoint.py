"""Regression tests for resumable release-stage evidence."""

from __future__ import annotations

import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path
from types import ModuleType


def load_module() -> ModuleType:
    """Load the checkpoint helper without requiring eng to be a package."""
    script = Path(__file__).resolve().parents[1] / "release_stage_checkpoint.py"
    spec = importlib.util.spec_from_file_location("release_stage_checkpoint", script)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {script}.")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


release_stage_checkpoint = load_module()


class ReleaseStageCheckpointTests(unittest.TestCase):
    """Prove exact artifact and source binding before a stage may be skipped."""

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

    def tearDown(self) -> None:
        """Remove the isolated repository."""
        self.temporary_directory.cleanup()

    def test_round_trip_verifies_every_directory_file(self) -> None:
        """Accept an unchanged stage directory under the same source commit."""
        artifact_directory = self.root / "performance"
        artifact_directory.mkdir()
        (artifact_directory / "mysql.json").write_text("mysql\n", encoding="ascii")
        (artifact_directory / "mariadb.json").write_text("mariadb\n", encoding="ascii")

        release_stage_checkpoint.write_checkpoint(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id="run-1",
            stage="performance",
            artifacts=[artifact_directory],
        )

        verified = release_stage_checkpoint.verify_checkpoint(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id="run-1",
            stage="performance",
        )

        self.assertEqual(self.checkpoints / "performance.json", verified)

    def test_modified_artifact_rejects_resume(self) -> None:
        """Reject a completed marker after any retained byte changes."""
        artifact = self.root / "coverage.xml"
        artifact.write_text("before\n", encoding="ascii")
        release_stage_checkpoint.write_checkpoint(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id="run-1",
            stage="coverage",
            artifacts=[artifact],
        )
        artifact.write_text("after\n", encoding="ascii")

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "does not match its digest",
        ):
            release_stage_checkpoint.verify_checkpoint(
                repository=self.repository,
                root=self.root,
                checkpoint_directory=self.checkpoints,
                run_id="run-1",
                stage="coverage",
            )

    def test_different_source_commit_rejects_resume(self) -> None:
        """Reject a valid artifact receipt after the repository advances."""
        artifact = self.root / "package.nupkg"
        artifact.write_bytes(b"package")
        release_stage_checkpoint.write_checkpoint(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id="run-1",
            stage="package",
            artifacts=[artifact],
        )
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
            release_stage_checkpoint.verify_checkpoint(
                repository=self.repository,
                root=self.root,
                checkpoint_directory=self.checkpoints,
                run_id="run-1",
                stage="package",
            )

    def test_symlink_artifact_rejects_checkpoint_creation(self) -> None:
        """Do not bind a receipt to a path whose identity can redirect later."""
        target = self.root / "target.json"
        target.write_text("evidence\n", encoding="ascii")
        link = self.root / "linked.json"
        link.symlink_to(target)

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "must not use a symbolic link",
        ):
            release_stage_checkpoint.write_checkpoint(
                repository=self.repository,
                root=self.root,
                checkpoint_directory=self.checkpoints,
                run_id="run-1",
                stage="linked",
                artifacts=[link],
            )

    def test_symlink_replacement_rejects_resume(self) -> None:
        """Reject an artifact replaced by an equal-content symlink after success."""
        artifact = self.root / "manifest.json"
        artifact.write_text("evidence\n", encoding="ascii")
        release_stage_checkpoint.write_checkpoint(
            repository=self.repository,
            root=self.root,
            checkpoint_directory=self.checkpoints,
            run_id="run-1",
            stage="manifest",
            artifacts=[artifact],
        )
        replacement = self.root / "replacement.json"
        replacement.write_text("evidence\n", encoding="ascii")
        artifact.unlink()
        artifact.symlink_to(replacement)

        with self.assertRaisesRegex(
            release_stage_checkpoint.CheckpointError,
            "must not use a symbolic link",
        ):
            release_stage_checkpoint.verify_checkpoint(
                repository=self.repository,
                root=self.root,
                checkpoint_directory=self.checkpoints,
                run_id="run-1",
                stage="manifest",
            )


if __name__ == "__main__":
    unittest.main()
