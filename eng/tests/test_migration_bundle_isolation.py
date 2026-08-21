"""Exercise migration-bundle restore isolation across its real shell boundary."""

from __future__ import annotations

import json
import os
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
BUNDLE_BUILDER = REPOSITORY_ROOT / "eng" / "testing" / "build-migration-bundle.sh"
MIGRATION_PROJECT = REPOSITORY_ROOT / "examples" / "MigrationsWorkflow" / "MigrationsWorkflow.csproj"
PROVIDER_PROJECT = (
    REPOSITORY_ROOT
    / "src"
    / "Doka.EntityFrameworkCore.MySql"
    / "Doka.EntityFrameworkCore.MySql.csproj"
)


class MigrationBundleIsolationTests(unittest.TestCase):
    """Keep the bundle's implicit RID restore outside product lock files."""

    def setUp(self) -> None:
        """Create isolated command boundaries and a fake source-state probe."""
        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-migration-bundle-contract-",
        )
        self.root = Path(self.temporary_directory.name)
        self.bin_directory = self.root / "bin"
        self.bin_directory.mkdir()
        self.git_state = self.root / "git-state.txt"
        self.git_state.write_text("clean\n", encoding="ascii")
        self.command_log = self.root / "dotnet-command.json"
        self.bundle_path = self.root / "efbundle"
        self._write_fake_git()
        self._write_fake_dotnet()

    def tearDown(self) -> None:
        """Remove all controlled command and source state."""
        self.temporary_directory.cleanup()

    def test_bundle_uses_per_project_disposable_locks_and_preserves_source(self) -> None:
        """Prove the real helper isolates the restore and removes its lock root."""
        result = self._run_builder()

        self.assertEqual(0, result.returncode, result.stderr)
        command = json.loads(self.command_log.read_text(encoding="ascii"))
        lock_root = Path(command["lockRoot"])

        self.assertEqual("clean", self.git_state.read_text(encoding="ascii").strip())
        self.assertFalse(lock_root.exists())
        self.assertEqual(
            [
                "tool",
                "run",
                "dotnet-ef",
                "--",
                "migrations",
                "bundle",
            ],
            command["arguments"][:6],
        )
        self.assertIn("--no-build", command["arguments"])
        self.assertIn("--force", command["arguments"])
        self.assertEqual(
            str(self.bundle_path),
            command["arguments"][command["arguments"].index("--output") + 1],
        )

    def test_bundle_side_effect_fails_before_the_gate_can_qualify(self) -> None:
        """Reject a bundle subprocess that still changes repository state."""
        result = self._run_builder(force_dirty=True)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("changed the source tree", result.stderr)

    def test_isolated_lock_property_resolves_per_project(self) -> None:
        """Bind every recursively restored project to its own disposable lock."""
        lock_root = self.root / "msbuild-locks"
        environment = os.environ.copy()
        environment["DokaIsolatedNuGetLockRoot"] = str(lock_root)

        actual_paths = {
            self._read_lock_path(project, environment)
            for project in (MIGRATION_PROJECT, PROVIDER_PROJECT)
        }

        self.assertEqual(
            {
                lock_root / "MigrationsWorkflow.packages.lock.json",
                lock_root / "Doka.EntityFrameworkCore.MySql.packages.lock.json",
            },
            actual_paths,
        )

    def _run_builder(self, *, force_dirty: bool = False) -> subprocess.CompletedProcess[str]:
        environment = os.environ.copy()
        environment.update(
            {
                "PATH": f"{self.bin_directory}{os.pathsep}{environment['PATH']}",
                "DOKA_TEST_DOTNET_LOG": str(self.command_log),
                "DOKA_TEST_GIT_STATE": str(self.git_state),
                "DOKA_TEST_FORCE_DIRTY": "1" if force_dirty else "0",
                "TMPDIR": str(self.root),
            }
        )

        return subprocess.run(
            ("bash", str(BUNDLE_BUILDER), str(self.bundle_path)),
            cwd=REPOSITORY_ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
        )

    @staticmethod
    def _read_lock_path(project: Path, environment: dict[str, str]) -> Path:
        result = subprocess.run(
            (
                "dotnet",
                "msbuild",
                str(project),
                "-getProperty:NuGetLockFilePath",
            ),
            cwd=REPOSITORY_ROOT,
            env=environment,
            check=True,
            capture_output=True,
            text=True,
        )
        return Path(result.stdout.strip())

    def _write_fake_git(self) -> None:
        fake_git = self.bin_directory / "git"
        fake_git.write_text(
            """#!/usr/bin/env bash
set -euo pipefail

if [[ "$1" == "-C" ]]; then
    shift 2
fi

if [[ "$1" != "status" ]]; then
    echo "Unexpected fake git command: $*" >&2
    exit 7
fi

if [[ "$(< "${DOKA_TEST_GIT_STATE}")" == "dirty" ]]; then
    printf ' M src/Doka.EntityFrameworkCore.MySql/packages.lock.json\n'
fi
""",
            encoding="ascii",
        )
        fake_git.chmod(fake_git.stat().st_mode | stat.S_IXUSR)

    def _write_fake_dotnet(self) -> None:
        fake_dotnet = self.bin_directory / "dotnet"
        fake_dotnet.write_text(
            """#!/usr/bin/env bash
set -euo pipefail

python3 -c 'import json, os, sys; json.dump({"arguments": sys.argv[1:], "lockRoot": os.environ.get("DokaIsolatedNuGetLockRoot", "")}, open(os.environ["DOKA_TEST_DOTNET_LOG"], "w", encoding="ascii"))' "$@"

if [[ -z "${DokaIsolatedNuGetLockRoot:-}" || "${DOKA_TEST_FORCE_DIRTY}" == "1" ]]; then
    printf 'dirty\n' > "${DOKA_TEST_GIT_STATE}"
fi
""",
            encoding="ascii",
        )
        fake_dotnet.chmod(fake_dotnet.stat().st_mode | stat.S_IXUSR)


if __name__ == "__main__":
    unittest.main()
