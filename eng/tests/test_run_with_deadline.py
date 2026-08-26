"""Regression tests for process-tree deadlines used by release qualification."""

from __future__ import annotations

import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

from eng.common import deadline as run_with_deadline


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
        self._assert_operator_termination(ignore_signal=False)

    def test_operator_termination_force_stops_a_signal_ignoring_group(self) -> None:
        """Escalate after grace when a child ignores the forwarded signal."""
        self._assert_operator_termination(ignore_signal=True)

    def _assert_operator_termination(self, *, ignore_signal: bool) -> None:
        with tempfile.TemporaryDirectory(prefix="doka-deadline-signal-") as directory:
            marker = Path(directory) / "orphaned-child.txt"
            handler_ready = Path(directory) / "handler-ready"
            descendant_ready = Path(directory) / "descendant-ready"
            observation_started = Path(directory) / "observation-started"
            signal_setup = (
                "signal.signal(signal.SIGTERM, signal.SIG_IGN)\n"
                if ignore_signal
                else ""
            )
            grandchild = (
                "import pathlib,signal,time\n"
                f"{signal_setup}"
                f"pathlib.Path({str(descendant_ready)!r}).touch()\n"
                f"while not pathlib.Path({str(observation_started)!r}).exists():\n"
                "    time.sleep(0.01)\n"
                "time.sleep(0.5)\n"
                f"pathlib.Path({str(marker)!r}).write_text('orphaned')"
            )
            child = (
                "import signal,subprocess,sys,time\n"
                f"{signal_setup}"
                f"subprocess.Popen([sys.executable, '-c', {grandchild!r}])\n"
                "time.sleep(5)"
            )
            helper = Path(__file__).resolve().parents[1] / "common" / "deadline.py"
            arguments = [
                str(helper),
                "--seconds",
                "5",
                "--label",
                "signal fixture",
                "--termination-grace-seconds",
                "0.1" if ignore_signal else "5",
                "--",
                sys.executable,
                "-c",
                child,
            ]
            driver = (
                "import pathlib,runpy,signal,sys\n"
                "original_signal = signal.signal\n"
                "def signal_and_report(signum, handler):\n"
                "    previous = original_signal(signum, handler)\n"
                "    if signum == signal.SIGTERM and callable(handler):\n"
                f"        pathlib.Path({str(handler_ready)!r}).touch()\n"
                "    return previous\n"
                "signal.signal = signal_and_report\n"
                f"sys.argv = {arguments!r}\n"
                "runpy.run_path(sys.argv[0], run_name='__main__')"
            )
            process = subprocess.Popen([sys.executable, "-c", driver])
            try:
                ready_deadline = time.monotonic() + 5
                while not (handler_ready.exists() and descendant_ready.exists()):
                    self.assertIsNone(process.poll(), "Signal fixture exited before readiness.")
                    self.assertLess(time.monotonic(), ready_deadline, "Signal fixture did not become ready.")
                    time.sleep(0.01)

                observation_started.touch()
                process.terminate()
                exit_code = process.wait(timeout=2)
            finally:
                if process.poll() is None:
                    process.terminate()
                    process.wait(timeout=7)
            time.sleep(0.6)

            self.assertEqual(128 + 15, exit_code)
            self.assertFalse(marker.exists())


if __name__ == "__main__":
    unittest.main()
