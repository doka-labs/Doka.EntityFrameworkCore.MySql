"""Regression tests for immutable release-stage artifact restoration."""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from eng import release_artifact_resolver


class ReleaseArtifactResolverTests(unittest.TestCase):
    """Keep rerun selection and archive extraction fail closed."""

    def artifact(
        self,
        stage: str,
        attempt: int,
        *,
        artifact_id: int,
        run_id: int = 123,
        expired: bool = False,
    ) -> dict[str, object]:
        """Build one representative GitHub artifact API object."""
        return {
            "id": artifact_id,
            "name": f"release-stage-{stage}-attempt-{attempt}",
            "expired": expired,
            "digest": "sha256:" + ("a" * 64),
            "workflow_run": {"id": run_id},
        }

    def test_selects_newest_completed_stage_from_same_run(self) -> None:
        """Reuse successful old jobs and prefer a newer repaired stage."""
        payload = {
            "artifacts": [
                self.artifact("quality", 1, artifact_id=1),
                self.artifact("integration", 1, artifact_id=2),
                self.artifact("integration", 2, artifact_id=3),
            ]
        }

        result = release_artifact_resolver.resolve_artifacts(
            payload,
            run_id=123,
            maximum_attempt=2,
            required_stages=["quality", "integration"],
        )

        self.assertEqual([1, 3], [item["id"] for item in result["artifacts"]])
        self.assertEqual([1, 2], [item["attempt"] for item in result["artifacts"]])

    def test_ignores_other_runs_future_attempts_and_expired_artifacts(self) -> None:
        """Prevent unrelated or unavailable evidence from satisfying a stage."""
        payload = {
            "artifacts": [
                self.artifact("quality", 1, artifact_id=1),
                self.artifact("quality", 2, artifact_id=2, run_id=999),
                self.artifact("quality", 3, artifact_id=3),
                self.artifact("quality", 2, artifact_id=4, expired=True),
            ]
        }

        result = release_artifact_resolver.resolve_artifacts(
            payload,
            run_id=123,
            maximum_attempt=2,
            required_stages=["quality"],
        )

        self.assertEqual(1, result["artifacts"][0]["id"])

    def test_missing_stage_fails(self) -> None:
        """Require a receipt for every declared stage before assembly."""
        with self.assertRaisesRegex(
            release_artifact_resolver.ArtifactResolutionError,
            "required stage 'integration'",
        ):
            release_artifact_resolver.resolve_artifacts(
                {"artifacts": [self.artifact("quality", 1, artifact_id=1)]},
                run_id=123,
                maximum_attempt=1,
                required_stages=["quality", "integration"],
            )

    def test_duplicate_latest_stage_artifacts_fail(self) -> None:
        """Reject ambiguous evidence rather than relying on API ordering."""
        payload = {
            "artifacts": [
                self.artifact("quality", 2, artifact_id=1),
                self.artifact("quality", 2, artifact_id=2),
            ]
        }

        with self.assertRaisesRegex(
            release_artifact_resolver.ArtifactResolutionError,
            "ambiguous",
        ):
            release_artifact_resolver.resolve_artifacts(
                payload,
                run_id=123,
                maximum_attempt=2,
                required_stages=["quality"],
            )

    def test_required_stage_set_rejects_duplicates(self) -> None:
        """Keep the assembly contract exhaustive and unambiguous."""
        with self.assertRaisesRegex(
            release_artifact_resolver.ArtifactResolutionError,
            "without duplicates",
        ):
            release_artifact_resolver.resolve_artifacts(
                {"artifacts": []},
                run_id=123,
                maximum_attempt=1,
                required_stages=["quality", "quality"],
            )

    def write_archive(
        self,
        root: Path,
        members: dict[str, bytes],
    ) -> tuple[Path, str]:
        """Create one hosted-artifact-shaped ZIP and its expected digest."""
        archive = root / "artifact.zip"
        with zipfile.ZipFile(archive, "w") as stream:
            for name, content in members.items():
                stream.writestr(name, content)
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        return archive, digest

    def test_restore_verifies_digest_and_merges_identical_files(self) -> None:
        """Permit shared candidate files only when their bytes agree."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            destination = root / "output"
            archive, digest = self.write_archive(
                root,
                {"candidate/evidence.json": b"{}\n"},
            )

            release_artifact_resolver.restore_archive(
                archive,
                destination,
                digest,
            )
            release_artifact_resolver.restore_archive(
                archive,
                destination,
                digest,
            )

            self.assertEqual(
                b"{}\n",
                (destination / "candidate" / "evidence.json").read_bytes(),
            )

    def test_restore_rejects_traversal_and_symlinks(self) -> None:
        """Block archive paths that could escape or redirect extraction."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            traversal, traversal_digest = self.write_archive(
                root,
                {"../outside.txt": b"unsafe"},
            )
            with self.assertRaises(
                release_artifact_resolver.ArtifactResolutionError
            ):
                release_artifact_resolver.restore_archive(
                    traversal,
                    root / "traversal-output",
                    traversal_digest,
                )

            symlink = root / "symlink.zip"
            with zipfile.ZipFile(symlink, "w") as stream:
                info = zipfile.ZipInfo("redirect")
                info.create_system = 3
                info.external_attr = 0o120777 << 16
                stream.writestr(info, "target")
            symlink_digest = hashlib.sha256(symlink.read_bytes()).hexdigest()
            with self.assertRaises(
                release_artifact_resolver.ArtifactResolutionError
            ):
                release_artifact_resolver.restore_archive(
                    symlink,
                    root / "symlink-output",
                    symlink_digest,
                )

    def test_restore_rejects_hosted_digest_mismatch(self) -> None:
        """Never extract bytes that differ from GitHub's artifact digest."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive, _ = self.write_archive(root, {"file.txt": b"content"})

            with self.assertRaisesRegex(
                release_artifact_resolver.ArtifactResolutionError,
                "hosted digest",
            ):
                release_artifact_resolver.restore_archive(
                    archive,
                    root / "output",
                    "0" * 64,
                )


if __name__ == "__main__":
    unittest.main()
