"""Contracts for the release package dependency closure."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


class ReleasePackageLockTests(unittest.TestCase):
    """Keep release restores bound to the reviewed transitive package graph."""

    def setUp(self) -> None:
        """Resolve both entry points, the shared gate, and shipped projects."""
        self.repository_root = Path(__file__).resolve().parents[2]
        self.orchestrator = (
            self.repository_root / "eng" / "release" / "release-candidate.sh"
        ).read_text(encoding="ascii")
        self.quality_gates = (
            self.repository_root / "eng" / "quality" / "quality-gates.sh"
        ).read_text(encoding="ascii")
        self.lock_gate = (
            self.repository_root
            / "eng"
            / "quality"
            / "verify-release-package-locks.sh"
        ).read_text(encoding="ascii")
        self.lock_files = (
            self.repository_root
            / "src"
            / "Doka.EntityFrameworkCore.MySql"
            / "packages.lock.json",
            self.repository_root
            / "src"
            / "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
            / "packages.lock.json",
            self.repository_root / "src" / "Doka.Caching.MySql" / "packages.lock.json",
        )

    def test_candidate_package_restores_use_locked_mode(self) -> None:
        """Route candidate assembly through the shared dependency gate."""
        run_pack_start = self.orchestrator.index("run_pack()")
        run_pack_end = self.orchestrator.index("\n}\n", run_pack_start)
        run_pack = self.orchestrator[run_pack_start:run_pack_end]

        self.assertIn(
            '"${repo_root}/eng/quality/verify-release-package-locks.sh"',
            run_pack,
        )

    def test_shared_gate_restores_all_shipped_projects_in_locked_mode(self) -> None:
        """Fail every qualification path when a shipped closure has drifted."""
        self.assertIn(
            'dotnet restore "${runtime_project}" --locked-mode --tl:off',
            self.lock_gate,
        )
        self.assertIn(
            'dotnet restore "${spatial_project}" --locked-mode --tl:off',
            self.lock_gate,
        )
        self.assertIn('dotnet restore "${cache_project}" --locked-mode --tl:off', self.lock_gate)
        self.assertEqual(3, self.lock_gate.count("--locked-mode"))

    def test_quality_gate_checks_locks_before_the_solution_restore(self) -> None:
        """Catch stale committed locks before an ordinary restore can rewrite them."""
        lock_gate = (
            '"${repo_root}/eng/quality/verify-release-package-locks.sh"'
        )
        solution_restore = 'dotnet restore "${solution}" --tl:off'

        self.assertIn(lock_gate, self.quality_gates)
        self.assertLess(
            self.quality_gates.index(lock_gate),
            self.quality_gates.index(solution_restore),
        )

    def test_shared_gate_rejects_uncommitted_lock_rewrites(self) -> None:
        """Keep an ordinary restore from silently repairing reviewed evidence."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            gate = root / "eng" / "quality" / "verify-release-package-locks.sh"
            gate.parent.mkdir(parents=True)
            gate.write_text(self.lock_gate, encoding="ascii")
            gate.chmod(0o755)

            for project_name in (
                "Doka.EntityFrameworkCore.MySql",
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
                "Doka.Caching.MySql",
            ):
                project_root = root / "src" / project_name
                project_root.mkdir(parents=True)
                (project_root / f"{project_name}.csproj").write_text(
                    "<Project />\n",
                    encoding="ascii",
                )
                (project_root / "packages.lock.json").write_text(
                    '{"version":2,"dependencies":{"net10.0":{}}}\n',
                    encoding="ascii",
                )

            executable_root = root / "bin"
            executable_root.mkdir()
            dotnet = executable_root / "dotnet"
            dotnet.write_text("#!/bin/sh\nexit 0\n", encoding="ascii")
            dotnet.chmod(0o755)
            git = executable_root / "git"
            git.write_text("#!/bin/sh\nexit 1\n", encoding="ascii")
            git.chmod(0o755)

            environment = dict(os.environ)
            environment["PATH"] = (
                f"{executable_root}{os.pathsep}{environment['PATH']}"
            )
            result = subprocess.run(
                [str(gate)],
                cwd=root,
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "Release package locks contain uncommitted restore changes.",
            result.stderr,
        )

    def test_both_release_projects_carry_versioned_lock_files(self) -> None:
        """Require a NuGet v2 lock with one net10.0 dependency closure."""
        for lock_file in self.lock_files:
            with self.subTest(lock_file=lock_file):
                payload = json.loads(lock_file.read_text(encoding="ascii"))

                self.assertEqual(2, payload["version"])
                self.assertEqual(["net10.0"], list(payload["dependencies"]))
                self.assertNotEqual({}, payload["dependencies"]["net10.0"])


if __name__ == "__main__":
    unittest.main()
