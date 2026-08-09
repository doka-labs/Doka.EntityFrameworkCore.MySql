"""Contract tests for the lint gate's tool-resolution modes.

The hosted lint gate failed once because hydration was tied to a variable no
workflow set. These tests pin the resolution contract itself: which mode a
given environment selects, and that a hydration failure ends the run instead of
being reported as a lint finding.

Every case runs against a throwaway repository tree with stub executables, so
no test downloads a tool or reaches the network.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPOSITORY_ROOT / "eng" / "quality" / "lint-workflows.sh"
REQUIREMENTS = REPOSITORY_ROOT / "eng" / "quality" / "zizmor-requirements.txt"

PINNED_ACTIONLINT = "1.7.12"
PINNED_ZIZMOR = "1.29.0"


def write_stub(path: Path, output: str, *, exit_code: int = 0) -> None:
    """Create an executable stub that prints one line and exits."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        f"#!/usr/bin/env bash\necho '{output}'\nexit {exit_code}\n",
        encoding="utf-8",
    )
    path.chmod(0o755)


class LintToolResolutionTests(unittest.TestCase):
    """Select the pinned toolchain on a runner and the local one elsewhere."""

    def setUp(self) -> None:
        """Build an isolated repository tree the script can run against."""
        self._directory = tempfile.TemporaryDirectory(prefix="doka-lint-contract-")
        self.root = Path(self._directory.name)
        self.addCleanup(self._directory.cleanup)

        (self.root / "eng" / "quality").mkdir(parents=True)
        (self.root / ".github" / "workflows").mkdir(parents=True)
        (self.root / ".githooks").mkdir()
        shutil.copy(SCRIPT, self.root / "eng" / "quality" / "lint-workflows.sh")
        shutil.copy(REQUIREMENTS, self.root / "eng" / "quality" / "zizmor-requirements.txt")

        # One trivial workflow and hook so the discovery step is non-empty.
        (self.root / ".github" / "workflows" / "example.yml").write_text(
            "name: example\non: workflow_dispatch\njobs:\n  a:\n    runs-on: ubuntu-latest\n"
            "    steps:\n      - run: echo hi\n",
            encoding="utf-8",
        )
        (self.root / ".githooks" / "pre-commit").write_text(
            "#!/usr/bin/env bash\nset -euo pipefail\n",
            encoding="utf-8",
        )

        self.stub_bin = self.root / "stub-bin"
        self.stub_bin.mkdir()
        write_stub(self.stub_bin / "shellcheck", "shellcheck stub")

        # PATH is rebuilt from an explicit tool list rather than inherited.
        # Inheriting it made the missing-shellcheck case pass only on machines
        # that happen not to ship shellcheck; a runner that does provide it
        # silently satisfied the gate the test meant to starve.
        self.system_bin = self.root / "system-bin"
        self.system_bin.mkdir()
        for name in (
            "bash", "env", "find", "sort", "sed", "awk", "python3", "uname",
            "tar", "curl", "rm", "mkdir", "cat", "head", "cp", "chmod",
            "dirname", "basename", "tr", "wc", "sha256sum", "shasum",
        ):
            resolved = shutil.which(name)
            if resolved is not None:
                (self.system_bin / name).symlink_to(resolved)

        self.tool_root = self.root / "artifacts" / "lint-tools"

    def prime_pinned_cache(self) -> None:
        """Place already-current tools where the hydrating mode looks for them."""
        write_stub(self.tool_root / "actionlint", PINNED_ACTIONLINT)
        write_stub(self.tool_root / "venv" / "bin" / "zizmor", f"zizmor {PINNED_ZIZMOR}")

    def place_drifted_tools_on_path(self) -> None:
        """Put versions on PATH that do not match the pin."""
        write_stub(self.stub_bin / "actionlint", "1.0.0")
        write_stub(self.stub_bin / "zizmor", "zizmor 0.1.0")

    def run_gate(self, **environment: str) -> subprocess.CompletedProcess[str]:
        """Run the copied gate with a controlled environment."""
        child = {
            "PATH": f"{self.stub_bin}:{self.system_bin}",
            "HOME": str(self.root),
            "PYTHONDONTWRITEBYTECODE": "1",
        }
        child.update(environment)

        return subprocess.run(
            ["bash", str(self.root / "eng" / "quality" / "lint-workflows.sh")],
            capture_output=True,
            text=True,
            check=False,
            cwd=self.root,
            env=child,
        )

    def test_ci_selects_the_pinned_toolchain_without_a_variable(self) -> None:
        """Reject a runner falling back to whatever the image happens to ship."""
        self.prime_pinned_cache()
        self.place_drifted_tools_on_path()

        result = self.run_gate(CI="true")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("from PATH", result.stderr)
        self.assertIn(f"Running actionlint {PINNED_ACTIONLINT}", result.stdout)
        self.assertIn(f"Running zizmor {PINNED_ZIZMOR}", result.stdout)

    def test_explicit_opt_out_keeps_a_runner_on_path(self) -> None:
        """Let the variable override the environment-derived default."""
        self.prime_pinned_cache()
        self.place_drifted_tools_on_path()

        result = self.run_gate(CI="true", DOKA_LINT_AUTO_INSTALL="0")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("using actionlint 1.0.0 from PATH", result.stderr)
        self.assertIn("using zizmor 0.1.0 from PATH", result.stderr)

    def test_local_run_uses_the_contributor_installation(self) -> None:
        """Keep a workstation on its own tools and report the drift."""
        self.place_drifted_tools_on_path()

        result = self.run_gate()

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("from PATH", result.stderr)

    def test_failed_hydration_is_a_toolchain_error(self) -> None:
        """Reject reporting a failed install as a lint finding.

        This is the regression that made the hosted failure unreadable: the
        install failed, the run continued, and the absent binary surfaced as
        'zizmor reported findings'.
        """
        self.prime_pinned_cache()
        # Remove the cached zizmor so the run has to hydrate it, and make the
        # interpreter that would build the environment fail.
        shutil.rmtree(self.tool_root / "venv")
        write_stub(self.stub_bin / "python3", "venv unavailable", exit_code=1)

        result = self.run_gate(CI="true")

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Could not create the linter virtual environment", result.stderr)
        self.assertNotIn("reported findings", result.stderr)

    def test_unrecognized_opt_out_value_is_rejected(self) -> None:
        """Reject a value that would otherwise degrade into the disabled branch."""
        self.prime_pinned_cache()

        for value in ("true", "yes", "TRUE", "2", "01", " "):
            with self.subTest(value=value):
                result = self.run_gate(CI="true", DOKA_LINT_AUTO_INSTALL=value)

                self.assertEqual(2, result.returncode, result.stderr)
                self.assertIn("must be 0 or 1", result.stderr)

    def test_empty_assignment_falls_back_to_the_environment_default(self) -> None:
        """Treat an empty assignment as unset rather than as a bad value."""
        self.prime_pinned_cache()
        self.place_drifted_tools_on_path()

        result = self.run_gate(CI="true", DOKA_LINT_AUTO_INSTALL="")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("from PATH", result.stderr)

    def test_missing_shellcheck_stops_the_run(self) -> None:
        """Refuse to report a passing shell contract that never ran."""
        self.prime_pinned_cache()
        (self.stub_bin / "shellcheck").unlink()

        result = self.run_gate(CI="true")

        self.assertNotEqual(0, result.returncode)
        self.assertIn("shellcheck", result.stderr)
        self.assertNotIn("contract passed", result.stdout)

    def test_stale_cached_build_is_replaced_not_reused(self) -> None:
        """Reject a cached binary left over from an earlier pin.

        Hydration is expected to start, so the run is allowed to fail here for
        lack of network; what matters is that the drifted cache was discarded
        rather than accepted as the pinned build.
        """
        write_stub(self.tool_root / "actionlint", "1.0.0")
        write_stub(self.tool_root / "venv" / "bin" / "zizmor", f"zizmor {PINNED_ZIZMOR}")

        result = self.run_gate(CI="true", DOKA_LINT_AUTO_INSTALL="1")

        self.assertNotIn(
            "Running actionlint 1.0.0",
            result.stdout,
            "A cached build whose version drifted from the pin was reused.",
        )


if __name__ == "__main__":
    unittest.main()
