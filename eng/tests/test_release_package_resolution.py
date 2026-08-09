"""Contract tests for resolving a packed version from the release directory.

One package id is a prefix of the other: the spatial package is named after the
provider. A glob that only anchors on the id therefore matches both, and which
one answers depends on directory order, which differs between filesystems. The
release candidate compares the two versions against each other, so the mix-up
surfaces as a version mismatch between packages that were built correctly.

The tests exercise the shell function itself, extracted from the orchestrator,
so they fail if the resolution loses its anchor again.
"""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ORCHESTRATOR = REPOSITORY_ROOT / "eng" / "release" / "release-candidate.sh"

PROVIDER = "Doka.EntityFrameworkCore.MySql"
SPATIAL = "Doka.EntityFrameworkCore.MySql.NetTopologySuite"


def extract_function() -> str:
    """Return the resolver as written in the orchestrator."""
    lines = ORCHESTRATOR.read_text(encoding="utf-8").splitlines()
    start = next(
        index
        for index, line in enumerate(lines)
        if line.startswith("package_version_from_file()")
    )
    end = next(
        index for index in range(start, len(lines)) if lines[index] == "}"
    )

    return "\n".join(lines[start : end + 1])


class ReleasePackageResolutionTests(unittest.TestCase):
    """Prove the resolver answers for the requested package id only."""

    def setUp(self) -> None:
        """Create a package directory shaped like a real release build."""
        self._directory = tempfile.TemporaryDirectory(prefix="doka-packages-")
        self.packages = Path(self._directory.name) / "packages"
        self.packages.mkdir()
        self.addCleanup(self._directory.cleanup)

    def place(self, *names: str) -> None:
        """Create empty package files with the given names."""
        for name in names:
            (self.packages / name).write_bytes(b"")

    def resolve(self, package_name: str) -> subprocess.CompletedProcess[str]:
        """Run the extracted resolver against the fixture directory."""
        script = "\n".join(
            [
                "set -euo pipefail",
                f'packages_dir="{self.packages}"',
                extract_function(),
                f'package_version_from_file "{package_name}"',
            ]
        )

        return subprocess.run(
            ["bash", "-c", script],
            capture_output=True,
            text=True,
            check=False,
        )

    def test_a_longer_package_id_does_not_answer_for_a_shorter_one(self) -> None:
        """Reject the spatial package standing in for the provider package."""
        self.place(
            f"{PROVIDER}.10.0.0-rc.6.nupkg",
            f"{SPATIAL}.10.0.0-rc.6.nupkg",
        )

        result = self.resolve(PROVIDER)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("10.0.0-rc.6", result.stdout.strip())

    def test_the_longer_package_id_still_resolves(self) -> None:
        """Keep the spatial package resolvable under its own id."""
        self.place(
            f"{PROVIDER}.10.0.0-rc.6.nupkg",
            f"{SPATIAL}.10.0.0-rc.6.nupkg",
        )

        result = self.resolve(SPATIAL)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("10.0.0-rc.6", result.stdout.strip())

    def test_symbol_packages_are_not_mistaken_for_the_package(self) -> None:
        """Keep the symbol package out of the version answer."""
        self.place(
            f"{PROVIDER}.10.0.0-rc.6.nupkg",
            f"{PROVIDER}.10.0.0-rc.6.snupkg",
            f"{PROVIDER}.10.0.0-rc.6.symbols.nupkg",
        )

        result = self.resolve(PROVIDER)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("10.0.0-rc.6", result.stdout.strip())

    def test_two_builds_of_one_package_are_rejected(self) -> None:
        """Refuse to pick a version when the directory holds two candidates.

        Selecting the first entry would make the qualified version depend on
        directory order rather than on what was built.
        """
        self.place(
            f"{PROVIDER}.10.0.0-rc.6.nupkg",
            f"{PROVIDER}.10.0.0-rc.7.nupkg",
        )

        result = self.resolve(PROVIDER)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Multiple packages match", result.stderr)

    def test_a_missing_package_is_reported(self) -> None:
        """Report an empty directory rather than answering with nothing."""
        self.place(f"{SPATIAL}.10.0.0-rc.6.nupkg")

        result = self.resolve(PROVIDER)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Unable to locate package", result.stderr)


if __name__ == "__main__":
    unittest.main()
