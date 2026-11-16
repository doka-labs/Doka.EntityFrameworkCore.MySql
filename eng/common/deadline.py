#!/usr/bin/env python3
"""Run one command in an owned process group with a hard wall-clock deadline.

The helper exists because macOS does not provide the GNU ``timeout`` command
by default. Starting an owned process group is important: terminating only the
direct shell would leave BenchmarkDotNet or database client children running.
"""

from __future__ import annotations

import argparse
import os
import signal
import subprocess
import sys
import time
from collections.abc import Sequence

TIMEOUT_EXIT_CODE = 124
TERMINATION_GRACE_SECONDS = 5.0
SIGNAL_POLL_SECONDS = 0.1


def run_command(
    command: Sequence[str],
    *,
    seconds: float,
    label: str,
    termination_grace_seconds: float = TERMINATION_GRACE_SECONDS,
) -> int:
    """Run ``command`` and terminate its complete process group on timeout."""
    if not command:
        raise ValueError("A deadline requires a command.")
    if seconds <= 0:
        raise ValueError("A deadline must be greater than zero seconds.")
    if termination_grace_seconds < 0:
        raise ValueError("The termination grace period cannot be negative.")

    process = subprocess.Popen(list(command), start_new_session=True)
    forwarded_signal: int | None = None
    previous_handlers: dict[int, signal.Handlers] = {}

    def forward_signal(signum: int, _frame: object) -> None:
        """Record the first operator signal for normal process-group cleanup."""
        nonlocal forwarded_signal
        # WHY: Signalling here and again during cleanup races the owned zombie
        # transition. The normal path forwards the original signal once.
        if forwarded_signal is None:
            forwarded_signal = signum

    for signum in (signal.SIGINT, signal.SIGTERM):
        previous_handlers[signum] = signal.getsignal(signum)
        signal.signal(signum, forward_signal)

    deadline = time.monotonic() + seconds

    try:
        while True:
            if forwarded_signal is not None:
                _terminate_process_group(
                    process,
                    termination_grace_seconds,
                    termination_signal=forwarded_signal,
                )
                return 128 + forwarded_signal

            remaining = deadline - time.monotonic()
            if remaining <= 0:
                print(
                    f"Timed out {label} after {seconds:g} seconds; "
                    "terminating its process group.",
                    file=sys.stderr,
                    flush=True,
                )
                _terminate_process_group(process, termination_grace_seconds)
                return TIMEOUT_EXIT_CODE

            try:
                exit_code = process.wait(
                    timeout=min(SIGNAL_POLL_SECONDS, remaining)
                )
                # WHY: Normal completion can leave descendants. Its first
                # group probe must share the same bounded cleanup as signals.
                _terminate_process_group(
                    process,
                    termination_grace_seconds,
                    termination_signal=forwarded_signal or signal.SIGTERM,
                )
                return (
                    128 + forwarded_signal
                    if forwarded_signal is not None
                    else exit_code
                )
            except subprocess.TimeoutExpired:
                continue
    finally:
        for signum, previous_handler in previous_handlers.items():
            signal.signal(signum, previous_handler)


def _terminate_process_group(
    process: subprocess.Popen[bytes],
    termination_grace_seconds: float,
    *,
    termination_signal: int = signal.SIGTERM,
) -> None:
    """Terminate cooperatively first, then force-stop the owned group."""
    deadline = time.monotonic() + termination_grace_seconds
    if not _signal_process_group(process, termination_signal, cleanup_deadline=deadline):
        return

    while True:
        if not _process_group_exists(process, cleanup_deadline=deadline):
            return

        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break

        time.sleep(min(SIGNAL_POLL_SECONDS, remaining))

    _signal_process_group(process, signal.SIGKILL, cleanup_deadline=deadline)

    if process.poll() is None:
        process.wait()


def _process_group_exists(
    process: subprocess.Popen[bytes], *, cleanup_deadline: float
) -> bool:
    """Return whether any process still belongs to the owned process group."""
    return _signal_process_group(process, 0, cleanup_deadline=cleanup_deadline)


def _signal_process_group(
    process: subprocess.Popen[bytes], signum: int, *, cleanup_deadline: float
) -> bool:
    """Signal or probe the group, distinguishing absence from denied access."""
    # WHY: Darwin can deny a signal while the owned leader is an unreaped
    # zombie. Reap it first, but never equate that leader's exit with group exit.
    process.poll()

    try:
        os.killpg(process.pid, signum)
    except ProcessLookupError:
        return False
    except PermissionError as denied:
        # WHY: A dying leader can briefly remain unreapable, and an ended
        # leader can leave zombie descendants. Darwin EPERM cannot distinguish
        # those transitions from a genuinely denied group.
        # Only actual ESRCH within the original cleanup budget proves absence.
        while True:
            process.poll()
            try:
                os.killpg(process.pid, 0)
            except ProcessLookupError:
                return False
            except PermissionError:
                pass
            else:
                raise denied

            remaining = cleanup_deadline - time.monotonic()
            if remaining <= 0:
                raise denied

            time.sleep(min(SIGNAL_POLL_SECONDS, remaining))

    return True


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    """Parse the deadline and preserve the command without shell expansion."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seconds", required=True, type=float)
    parser.add_argument("--label", required=True)
    parser.add_argument(
        "--termination-grace-seconds",
        default=TERMINATION_GRACE_SECONDS,
        type=float,
    )
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args(argv)

    if args.command[:1] == ["--"]:
        args.command = args.command[1:]
    if not args.command:
        parser.error("a command is required after '--'")

    return args


def main(argv: Sequence[str] | None = None) -> int:
    """Execute the requested command with the configured deadline."""
    args = parse_args(sys.argv[1:] if argv is None else argv)

    try:
        return run_command(
            args.command,
            seconds=args.seconds,
            label=args.label,
            termination_grace_seconds=args.termination_grace_seconds,
        )
    except (OSError, ValueError) as error:
        print(f"Unable to run {args.label}: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
