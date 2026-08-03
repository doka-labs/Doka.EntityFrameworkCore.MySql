"""Regression tests for process-tree deadlines used by release qualification."""

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from types import ModuleType


def load_module() -> ModuleType:
    """Load the repository helper without requiring eng to be a package."""
    script = Path(__file__).resolve().parents[1] / "run_with_deadline.py"
    spec = importlib.util.spec_from_file_location("run_with_deadline", script)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {script}.")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


run_with_deadline = load_module()


class RunWithDeadlineTests(unittest.TestCase):
    """Prove success propagation and complete timeout cleanup."""

    def test_returns_the_child_exit_code(self) -> None:
        """Preserve a completed command's exact verdict."""
        exit_code = run_with_deadline.run_command(
            [sys.executable, "-c", "raise SystemExit(7)"],
            seconds=5,
            label="exit-code fixture",
        )

        self.assertEqual(7, exit_code)

    def test_timeout_terminates_the_complete_process_group(self) -> None:
        """Prevent a timed-out descendant from surviving to write later output."""
        with tempfile.TemporaryDirectory(prefix="doka-deadline-") as directory:
            marker = Path(directory) / "orphaned-child.txt"
            grandchild = (
                "import pathlib,time; "
                "time.sleep(0.5); "
                f"pathlib.Path({str(marker)!r}).write_text('orphaned')"
            )
            child = (
                "import pathlib,subprocess,sys,time; "
                f"subprocess.Popen([sys.executable, '-c', {grandchild!r}]); "
                "time.sleep(5)"
            )

            started = time.monotonic()
            exit_code = run_with_deadline.run_command(
                [sys.executable, "-c", child],
                seconds=0.1,
                label="timeout fixture",
                termination_grace_seconds=0.1,
            )
            elapsed = time.monotonic() - started
            time.sleep(0.6)

            self.assertEqual(run_with_deadline.TIMEOUT_EXIT_CODE, exit_code)
            self.assertLess(elapsed, 2)
            self.assertFalse(marker.exists())

    def test_timeout_kills_descendant_after_group_leader_exits(self) -> None:
        """Do not mistake an exited shell for complete process-tree cleanup."""
        with tempfile.TemporaryDirectory(prefix="doka-deadline-leader-") as directory:
            marker = Path(directory) / "orphaned-child.txt"
            grandchild = (
                "import pathlib,signal,time; "
                "signal.signal(signal.SIGTERM, signal.SIG_IGN); "
                "time.sleep(0.5); "
                f"pathlib.Path({str(marker)!r}).write_text('orphaned')"
            )
            child = (
                "import subprocess,sys,time; "
                f"subprocess.Popen([sys.executable, '-c', {grandchild!r}]); "
                "time.sleep(5)"
            )

            exit_code = run_with_deadline.run_command(
                [sys.executable, "-c", child],
                seconds=0.1,
                label="exited leader fixture",
                termination_grace_seconds=0.1,
            )
            time.sleep(0.6)

            self.assertEqual(run_with_deadline.TIMEOUT_EXIT_CODE, exit_code)
            self.assertFalse(marker.exists())

    def test_operator_termination_is_forwarded_to_the_child_group(self) -> None:
        """Prevent Ctrl-C or an outer deadline from orphaning descendants."""
        with tempfile.TemporaryDirectory(prefix="doka-deadline-signal-") as directory:
            marker = Path(directory) / "orphaned-child.txt"
            grandchild = (
                "import pathlib,time; "
                "time.sleep(0.5); "
                f"pathlib.Path({str(marker)!r}).write_text('orphaned')"
            )
            child = (
                "import subprocess,sys,time; "
                f"subprocess.Popen([sys.executable, '-c', {grandchild!r}]); "
                "time.sleep(5)"
            )
            helper = Path(__file__).resolve().parents[1] / "run_with_deadline.py"
            process = subprocess.Popen(
                [
                    sys.executable,
                    str(helper),
                    "--seconds",
                    "5",
                    "--label",
                    "signal fixture",
                    "--",
                    sys.executable,
                    "-c",
                    child,
                ]
            )
            time.sleep(0.1)
            process.terminate()
            exit_code = process.wait(timeout=2)
            time.sleep(0.6)

            self.assertEqual(128 + 15, exit_code)
            self.assertFalse(marker.exists())

    def test_operator_termination_force_stops_a_signal_ignoring_group(self) -> None:
        """Escalate after grace when a child ignores the forwarded signal."""
        with tempfile.TemporaryDirectory(prefix="doka-deadline-ignore-") as directory:
            marker = Path(directory) / "orphaned-child.txt"
            grandchild = (
                "import pathlib,signal,time; "
                "signal.signal(signal.SIGTERM, signal.SIG_IGN); "
                "time.sleep(0.5); "
                f"pathlib.Path({str(marker)!r}).write_text('orphaned')"
            )
            child = (
                "import signal,subprocess,sys,time; "
                "signal.signal(signal.SIGTERM, signal.SIG_IGN); "
                f"subprocess.Popen([sys.executable, '-c', {grandchild!r}]); "
                "time.sleep(5)"
            )
            helper = Path(__file__).resolve().parents[1] / "run_with_deadline.py"
            driver = (
                "import importlib.util,sys; "
                f"spec=importlib.util.spec_from_file_location('deadline', {str(helper)!r}); "
                "module=importlib.util.module_from_spec(spec); "
                "spec.loader.exec_module(module); "
                "raise SystemExit(module.run_command("
                f"[sys.executable, '-c', {child!r}], "
                "seconds=5, label='ignored signal fixture', "
                "termination_grace_seconds=0.1))"
            )
            process = subprocess.Popen([sys.executable, "-c", driver])
            time.sleep(0.2)
            process.terminate()
            exit_code = process.wait(timeout=2)
            time.sleep(0.6)

            self.assertEqual(128 + 15, exit_code)
            self.assertFalse(marker.exists())


if __name__ == "__main__":
    unittest.main()
