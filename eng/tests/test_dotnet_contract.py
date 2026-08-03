"""Regression tests for the exact repository .NET SDK contract."""

from __future__ import annotations

import json
import os
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path


class DotNetContractTests(unittest.TestCase):
    """Prove the shell guard accepts only the global.json SDK identity."""

    def setUp(self) -> None:
        """Resolve the repository contract and create an isolated fake CLI."""
        self.repo = Path(__file__).resolve().parents[2]
        self.script = self.repo / "eng" / "verify-dotnet.sh"
        self.approved_version = json.loads(
            (self.repo / "global.json").read_text(encoding="utf-8")
        )["sdk"]["version"]
        self.temporary_directory = tempfile.TemporaryDirectory(prefix="doka-dotnet-contract-")
        self.bin_directory = Path(self.temporary_directory.name)

    def tearDown(self) -> None:
        """Remove the isolated fake CLI."""
        self.temporary_directory.cleanup()

    def test_exact_sdk_passes(self) -> None:
        """Accept the single repository-approved SDK identity."""
        result = self._run_with_version(self.approved_version)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f"Using .NET SDK {self.approved_version}", result.stdout)

    def test_different_patch_sdk_fails(self) -> None:
        """Reject even a patch-level SDK drift before build or publication."""
        result = self._run_with_version("10.0.999")

        self.assertNotEqual(0, result.returncode)
        self.assertIn(f"requires the exact .NET SDK {self.approved_version}", result.stderr)

    def _run_with_version(self, version: str) -> subprocess.CompletedProcess[str]:
        """Execute the real guard with a deterministic dotnet identity probe."""
        fake_dotnet = self.bin_directory / "dotnet"
        fake_dotnet.write_text(
            f"#!/usr/bin/env bash\nprintf '%s\\n' '{version}'\n",
            encoding="ascii",
        )
        fake_dotnet.chmod(fake_dotnet.stat().st_mode | stat.S_IXUSR)
        environment = os.environ.copy()
        environment["PATH"] = f"{self.bin_directory}{os.pathsep}{environment['PATH']}"

        return subprocess.run(
            ("bash", str(self.script)),
            cwd=self.repo,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
        )


if __name__ == "__main__":
    unittest.main()
