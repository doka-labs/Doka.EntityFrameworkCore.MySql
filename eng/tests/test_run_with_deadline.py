"""Regression tests for process-tree deadlines used by release qualification."""

from __future__ import annotations

import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import Mock, call, patch

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

    def test_normal_exit_waits_for_actual_descendant_group_absence(self) -> None:
        """Keep an ended leader's verdict only after its remaining group disappears."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.wait.return_value = 7
        process.poll.return_value = 7
        clock = [0.0]

        def sleep(seconds: float) -> None:
            """Advance cleanup time without depending on machine scheduling."""
            clock[0] += seconds

        # Act
        with (
            patch.object(run_with_deadline.subprocess, "Popen", return_value=process),
            patch.object(run_with_deadline.time, "monotonic", side_effect=lambda: clock[0]),
            patch.object(run_with_deadline.time, "sleep", side_effect=sleep),
            patch.object(
                run_with_deadline.os,
                "killpg",
                side_effect=[PermissionError("zombie"), PermissionError("zombie"), ProcessLookupError()],
            ) as killpg,
        ):
            exit_code = run_with_deadline.run_command(
                ["owned fixture"], seconds=5, label="owned fixture", termination_grace_seconds=0.2
            )

        # Assert
        self.assertEqual(7, exit_code)
        self.assertAlmostEqual(0.1, clock[0])
        self.assertEqual(
            [call(process.pid, signal.SIGTERM), call(process.pid, 0), call(process.pid, 0)],
            killpg.call_args_list,
        )

    def test_ended_leader_denial_is_fatal_after_the_existing_grace(self) -> None:
        """A denied group that never reaches ESRCH cannot be reported as cleaned."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = 0
        clock = [0.0]

        def sleep(seconds: float) -> None:
            """Consume only the existing cleanup budget."""
            clock[0] += seconds

        # Act
        with (
            patch.object(run_with_deadline.time, "monotonic", side_effect=lambda: clock[0]),
            patch.object(run_with_deadline.time, "sleep", side_effect=sleep),
            patch.object(run_with_deadline.os, "killpg", side_effect=PermissionError("denied descendants")),
        ):
            with self.assertRaisesRegex(PermissionError, "denied descendants"):
                run_with_deadline._terminate_process_group(process, 0.25)

        # Assert
        self.assertAlmostEqual(0.25, clock[0])
        process.wait.assert_not_called()

    def test_initial_signal_consumes_the_same_cleanup_grace(self) -> None:
        """Do not start a replacement budget after the first signal returns."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = 0
        clock = [0.0]

        def signal_group(_group: int, signum: int) -> None:
            """Spend part of the budget before encountering the denied group."""
            if signum == signal.SIGTERM:
                clock[0] += 0.15
                return

            raise PermissionError("denied descendants")

        def sleep(seconds: float) -> None:
            """Advance the remaining grace without a wall-clock assertion."""
            clock[0] += seconds

        # Act
        with (
            patch.object(run_with_deadline.time, "monotonic", side_effect=lambda: clock[0]),
            patch.object(run_with_deadline.time, "sleep", side_effect=sleep),
            patch.object(run_with_deadline.os, "killpg", side_effect=signal_group),
        ):
            with self.assertRaisesRegex(PermissionError, "denied descendants"):
                run_with_deadline._terminate_process_group(process, 0.2)

        # Assert
        self.assertAlmostEqual(0.2, clock[0])
        process.wait.assert_not_called()

    def test_accessible_group_probe_keeps_permission_failure_immediately_fatal(self) -> None:
        """A successful signal-zero probe must not hide denied termination."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = None

        # Act
        with (
            patch.object(run_with_deadline.time, "sleep") as sleep,
            patch.object(
                run_with_deadline.os, "killpg", side_effect=[PermissionError("live group denied"), None]
            ) as killpg,
        ):
            with self.assertRaisesRegex(PermissionError, "live group denied"):
                run_with_deadline._terminate_process_group(process, 5)

        # Assert
        sleep.assert_not_called()
        process.wait.assert_not_called()
        self.assertEqual([call(process.pid, signal.SIGTERM), call(process.pid, 0)], killpg.call_args_list)

    def test_not_yet_reapable_leader_waits_for_actual_group_absence(self) -> None:
        """An initial WNOHANG result cannot classify an exiting leader as live."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.side_effect = [None, None, -signal.SIGTERM]
        clock = [0.0]

        def sleep(seconds: float) -> None:
            """Advance the same cleanup budget until reaping completes."""
            clock[0] += seconds

        # Act
        with (
            patch.object(run_with_deadline.time, "monotonic", side_effect=lambda: clock[0]),
            patch.object(run_with_deadline.time, "sleep", side_effect=sleep),
            patch.object(
                run_with_deadline.os,
                "killpg",
                side_effect=[PermissionError("exiting"), PermissionError("exiting"), ProcessLookupError()],
            ) as killpg,
        ):
            run_with_deadline._terminate_process_group(process, 0.2)

        # Assert
        self.assertAlmostEqual(0.1, clock[0])
        self.assertEqual(3, process.poll.call_count)
        self.assertEqual(
            [call(process.pid, signal.SIGTERM), call(process.pid, 0), call(process.pid, 0)],
            killpg.call_args_list,
        )

    def test_persistent_not_reapable_denial_is_an_error_at_the_existing_deadline(self) -> None:
        """Bounded observation must never turn a persistent permission error into success."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.wait.return_value = 7
        process.poll.return_value = None
        clock = [0.0]

        def sleep(seconds: float) -> None:
            """Exhaust the existing grace without changing a live group's state."""
            clock[0] += seconds

        # Act
        with (
            patch.object(run_with_deadline.subprocess, "Popen", return_value=process),
            patch.object(run_with_deadline.time, "monotonic", side_effect=lambda: clock[0]),
            patch.object(run_with_deadline.time, "sleep", side_effect=sleep),
            patch.object(run_with_deadline.os, "killpg", side_effect=PermissionError("persistent denial")),
        ):
            exit_code = run_with_deadline.main([
                "--seconds", "5", "--label", "owned fixture", "--termination-grace-seconds", "0.25",
                "--", "owned fixture",
            ])

        # Assert
        self.assertEqual(2, exit_code)
        self.assertAlmostEqual(0.25, clock[0])

    def test_operator_status_survives_descendant_only_zombie_cleanup(self) -> None:
        """Return the original operator status after actual group disappearance."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = -signal.SIGTERM
        handlers = {}
        clock = [0.0]

        def remember_handler(number: int, handler: object) -> None:
            """Keep signal registration local to this deterministic test."""
            handlers.setdefault(number, handler)

        def receive_signal(*_args: object, **_kwargs: object) -> None:
            """Deliver the operator request at the existing wait boundary."""
            handlers[signal.SIGTERM](signal.SIGTERM, None)
            raise subprocess.TimeoutExpired("owned fixture", 0.1)

        def sleep(seconds: float) -> None:
            """Advance the one cleanup budget without real sleeping."""
            clock[0] += seconds

        process.wait.side_effect = receive_signal

        # Act
        with (
            patch.object(run_with_deadline.subprocess, "Popen", return_value=process),
            patch.object(run_with_deadline.signal, "signal", side_effect=remember_handler),
            patch.object(run_with_deadline.time, "monotonic", side_effect=lambda: clock[0]),
            patch.object(run_with_deadline.time, "sleep", side_effect=sleep),
            patch.object(
                run_with_deadline.os,
                "killpg",
                side_effect=[None, PermissionError("zombie"), PermissionError("zombie"), ProcessLookupError()],
            ) as killpg,
        ):
            exit_code = run_with_deadline.run_command(
                ["owned fixture"], seconds=5, label="owned fixture", termination_grace_seconds=0.2
            )

        # Assert
        self.assertEqual(128 + signal.SIGTERM, exit_code)
        self.assertAlmostEqual(0.1, clock[0])
        self.assertEqual([signal.SIGTERM], [item.args[1] for item in killpg.call_args_list if item.args[1]])

    def test_timeout_status_survives_descendant_only_zombie_cleanup(self) -> None:
        """Preserve the deadline verdict while waiting for actual ESRCH."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = -signal.SIGTERM
        clock = [0.0]

        def expire_wait(*_args: object, **_kwargs: object) -> None:
            """Exhaust the command deadline before entering group cleanup."""
            clock[0] = 0.1
            raise subprocess.TimeoutExpired("owned fixture", 0.1)

        def sleep(seconds: float) -> None:
            """Keep command time and cleanup time on one controlled clock."""
            clock[0] += seconds

        process.wait.side_effect = expire_wait

        # Act
        with (
            patch.object(run_with_deadline.subprocess, "Popen", return_value=process),
            patch.object(run_with_deadline.time, "monotonic", side_effect=lambda: clock[0]),
            patch.object(run_with_deadline.time, "sleep", side_effect=sleep),
            patch.object(
                run_with_deadline.os,
                "killpg",
                side_effect=[None, PermissionError("zombie"), PermissionError("zombie"), ProcessLookupError()],
            ),
        ):
            exit_code = run_with_deadline.run_command(
                ["owned fixture"], seconds=0.1, label="owned fixture", termination_grace_seconds=0.2
            )

        # Assert
        self.assertEqual(run_with_deadline.TIMEOUT_EXIT_CODE, exit_code)
        self.assertAlmostEqual(0.2, clock[0])

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

    def test_reaps_an_ended_leader_before_signalling_its_group(self) -> None:
        """Avoid Darwin EPERM from an owned but unreaped group leader."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        reaped = False

        def poll() -> int:
            """Model reaping the owned zombie without any surviving child."""
            nonlocal reaped
            reaped = True
            return -signal.SIGTERM

        def signal_group(_group: int, _signum: int) -> None:
            """Expose the original ordering defect deterministically."""
            if not reaped:
                raise PermissionError("owned zombie not reaped")

            raise ProcessLookupError("group absent after reap")

        process.poll.side_effect = poll

        # Act
        with patch.object(run_with_deadline.os, "killpg", side_effect=signal_group) as killpg:
            run_with_deadline._terminate_process_group(process, 0)

        # Assert
        killpg.assert_called_once_with(process.pid, signal.SIGTERM)
        process.wait.assert_not_called()

    def test_signal_permission_race_requires_actual_group_absence(self) -> None:
        """Recheck the owned leader after denied signalling; only ESRCH proves absence."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.side_effect = [None, -signal.SIGTERM]

        # Act
        with patch.object(
            run_with_deadline.os,
            "killpg",
            side_effect=[PermissionError("zombie transition"), ProcessLookupError("absent")],
        ) as killpg:
            run_with_deadline._terminate_process_group(process, 0)

        # Assert
        self.assertEqual([call(process.pid, signal.SIGTERM), call(process.pid, 0)], killpg.call_args_list)

    def test_missing_group_does_not_require_forced_termination(self) -> None:
        """A real ESRCH needs no additional signal or wait."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345

        # Act
        with patch.object(run_with_deadline.os, "killpg", side_effect=ProcessLookupError) as killpg:
            run_with_deadline._terminate_process_group(process, 0)

        # Assert
        killpg.assert_called_once_with(process.pid, signal.SIGTERM)
        process.wait.assert_not_called()

    def test_live_group_signal_permission_error_remains_fatal(self) -> None:
        """Do not disguise denied termination of a live owned process."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = None

        # Act
        with patch.object(run_with_deadline.os, "killpg", side_effect=PermissionError("denied")):
            with self.assertRaisesRegex(PermissionError, "denied"):
                run_with_deadline._terminate_process_group(process, 0)

        # Assert
        process.wait.assert_not_called()

    def test_ended_leader_does_not_hide_permission_error_for_surviving_group(self) -> None:
        """Reaping is insufficient when descendants still occupy the group."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = -signal.SIGTERM

        # Act
        with patch.object(
            run_with_deadline.os, "killpg", side_effect=[PermissionError("denied"), None]
        ) as killpg:
            with self.assertRaisesRegex(PermissionError, "denied"):
                run_with_deadline._terminate_process_group(process, 0)

        # Assert
        self.assertEqual([call(process.pid, signal.SIGTERM), call(process.pid, 0)], killpg.call_args_list)

    def test_live_group_probe_permission_error_is_not_group_absence(self) -> None:
        """A denied existence probe must not skip cooperative cleanup."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = None

        # Act
        with patch.object(
            run_with_deadline.os, "killpg", side_effect=[None, PermissionError("denied"), None]
        ):
            with self.assertRaisesRegex(PermissionError, "denied"):
                run_with_deadline._terminate_process_group(process, 0)

        # Assert
        process.wait.assert_not_called()

    def test_probe_permission_race_requires_actual_group_absence(self) -> None:
        """Recheck the owned leader after denied probing; only ESRCH proves absence."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.side_effect = [None, None, -signal.SIGTERM]

        # Act
        with patch.object(
            run_with_deadline.os,
            "killpg",
            side_effect=[None, PermissionError("zombie transition"), ProcessLookupError("absent")],
        ) as killpg:
            run_with_deadline._terminate_process_group(process, 0)

        # Assert
        self.assertEqual(
            [call(process.pid, signal.SIGTERM), call(process.pid, 0), call(process.pid, 0)],
            killpg.call_args_list,
        )

    def test_force_signal_permission_error_remains_fatal(self) -> None:
        """Do not report success when SIGKILL could not reach live children."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = None

        # Act
        with patch.object(
            run_with_deadline.os, "killpg", side_effect=[None, None, PermissionError("denied"), None]
        ) as killpg:
            with self.assertRaisesRegex(PermissionError, "denied"):
                run_with_deadline._terminate_process_group(process, 0)

        # Assert
        self.assertIn(call(process.pid, signal.SIGKILL), killpg.call_args_list)

    def test_ended_leader_does_not_skip_termination_of_live_descendants(self) -> None:
        """Retain TERM, existence checking, and KILL after the leader exits."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        process.poll.return_value = -signal.SIGTERM

        # Act
        with patch.object(run_with_deadline.os, "killpg") as killpg:
            run_with_deadline._terminate_process_group(process, 0)

        # Assert
        self.assertEqual(
            [call(process.pid, signal.SIGTERM), call(process.pid, 0), call(process.pid, signal.SIGKILL)],
            killpg.call_args_list,
        )
        process.wait.assert_not_called()

    def test_native_owned_zombie_is_reaped_before_group_cleanup(self) -> None:
        """Exercise the actual owned-zombie lifecycle on the native platform."""
        # Arrange
        process = subprocess.Popen([sys.executable, "-c", "pass"], start_new_session=True)
        try:
            ready_deadline = time.monotonic() + 5
            while True:
                state = subprocess.check_output(
                    ["ps", "-o", "stat=", "-p", str(process.pid)], text=True
                ).strip()
                if state.startswith("Z"):
                    break

                self.assertLess(time.monotonic(), ready_deadline, "Owned child did not exit.")
                time.sleep(0.01)

            # Act
            run_with_deadline._terminate_process_group(process, 0)

            # Assert
            self.assertEqual(0, process.poll())
            with self.assertRaises(ProcessLookupError):
                os.killpg(process.pid, 0)
        finally:
            if process.poll() is None:
                process.kill()

            process.wait()

    def test_operator_interrupt_is_forwarded_once_from_normal_cleanup(self) -> None:
        """Preserve SIGINT while keeping process operations outside handlers."""
        self._assert_operator_signal_is_forwarded_once(signal.SIGINT)

    def test_operator_termination_is_forwarded_once_from_normal_cleanup(self) -> None:
        """Preserve SIGTERM without a second handler-side termination."""
        self._assert_operator_signal_is_forwarded_once(signal.SIGTERM)

    def _assert_operator_signal_is_forwarded_once(self, signum: int) -> None:
        """Model a signal arriving while the owned process is being waited."""
        # Arrange
        process = Mock(spec=subprocess.Popen)
        process.pid = 12345
        handlers = {}

        def remember_signal_handler(number: int, handler: object) -> None:
            """Retain the registered handler without changing native handlers."""
            handlers.setdefault(number, handler)

        def receive_signal(*_args: object, **_kwargs: object) -> None:
            """Deliver the operator signal at a deterministic wait boundary."""
            handlers[signum](signum, None)
            raise subprocess.TimeoutExpired("owned fixture", 0.1)

        process.wait.side_effect = receive_signal

        # Act
        with (
            patch.object(run_with_deadline.subprocess, "Popen", return_value=process),
            patch.object(run_with_deadline.signal, "signal", side_effect=remember_signal_handler),
            patch.object(run_with_deadline.os, "killpg") as killpg,
            patch.object(run_with_deadline, "_terminate_process_group") as terminate,
        ):
            exit_code = run_with_deadline.run_command(
                ["owned fixture"], seconds=5, label="owned fixture", termination_grace_seconds=0.1
            )

        # Assert
        self.assertEqual(128 + signum, exit_code)
        killpg.assert_not_called()
        terminate.assert_called_once_with(process, 0.1, termination_signal=signum)

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
