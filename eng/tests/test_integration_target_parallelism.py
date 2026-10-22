"""Regression tests for bounded integration-target process scheduling."""

from __future__ import annotations

import json
import os
import signal
import subprocess
import tempfile
import time
import unittest
from pathlib import Path


class IntegrationTargetParallelismTests(unittest.TestCase):
    """Exercise the real matrix script with a deterministic dotnet substitute."""

    def setUp(self) -> None:
        """Give each case independent tool, event, and evidence paths."""
        temporary_directory = tempfile.TemporaryDirectory(prefix="doka-integration-targets-")
        self.addCleanup(temporary_directory.cleanup)

        self.root = Path(temporary_directory.name)
        self.repository = Path(__file__).resolve().parents[2]
        self.artifacts = self.root / "artifacts"
        self.events = self.root / "events.txt"
        self.barrier = self.root / "barrier"
        self.barrier.mkdir()

        tool_directory = self.root / "bin"
        tool_directory.mkdir()
        build_directory = self.root / "build"
        build_directory.mkdir()
        self.test_assembly = (
            build_directory / "Doka.EntityFrameworkCore.MySql.IntegrationTests.dll"
        )
        self.test_assembly.write_bytes(b"isolated-test-assembly")

        dotnet = tool_directory / "dotnet"
        dotnet.write_text(
            """#!/usr/bin/env bash
set -euo pipefail

case "${1:-}" in
    --version)
        echo "${FAKE_DOTNET_VERSION:?}"
        ;;
    restore|build)
        exit 0
        ;;
    msbuild)
        printf '%s\\n' "${FAKE_TEST_ASSEMBLY_PATH:?}"
        ;;
    test)
        target="${DOKA_INTEGRATION_TARGETS:?}"
        shift
        test_module=""
        test_root=""

        while (( $# > 0 )); do
            case "$1" in
                --test-modules)
                    test_module="${2:?}"
                    shift 2
                    ;;
                --root-directory)
                    test_root="${2:?}"
                    shift 2
                    ;;
                --project|--output)
                    # In MTP, --output controls verbosity rather than the
                    # test assembly path. Reject the former VSTest invocation.
                    exit 34
                    ;;
                *)
                    shift
                    ;;
            esac
        done

        copied_assembly="${test_root}/${test_module}"
        if [[ "${test_module}" != "$(basename "${FAKE_TEST_ASSEMBLY_PATH:?}")" \
            || -z "${test_root}" || "${copied_assembly}" == "${FAKE_TEST_ASSEMBLY_PATH}" \
            || ! -f "${copied_assembly}" ]]; then
            exit 31
        fi

        cmp -s "${FAKE_TEST_ASSEMBLY_PATH}" "${copied_assembly}" || exit 32
        # A hard link would still let Coverlet rewrite the shared build.
        [[ ! "${FAKE_TEST_ASSEMBLY_PATH}" -ef "${copied_assembly}" ]] || exit 33
        printf '%s\\n' "${test_root}" > "${FAKE_BARRIER_DIR:?}/${target}.output"
        printf 'start:%s\\n' "${target}" >> "${FAKE_EVENT_PATH:?}"

        if [[ "${FAKE_WAIT_FOR_SIGNAL:-0}" == "1" ]]; then
            printf '%s\\n' "$$" > "${FAKE_BARRIER_DIR:?}/${target}.pid"
            exec python3 -c '
import signal
import sys
import time

target, event_path = sys.argv[1:]

def stop(_signum, _frame):
    with open(event_path, "a", encoding="ascii") as events:
        events.write(f"terminated:{target}\\n")
    raise SystemExit(0)

signal.signal(signal.SIGTERM, stop)
with open(event_path, "a", encoding="ascii") as events:
    events.write(f"ready:{target}\\n")

while True:
    time.sleep(0.1)
' "${target}" "${FAKE_EVENT_PATH}"
        fi

        touch "${FAKE_BARRIER_DIR:?}/${target}"

        # Both first targets must start before either finishes. A serial
        # scheduler therefore fails the contract instead of merely running
        # slower on an unloaded machine.
        if [[ "${target}" == "mysql84" || "${target}" == "mariadb1011" ]]; then
            for (( attempt = 0; attempt < 500; attempt++ )); do
                if [[ -f "${FAKE_BARRIER_DIR}/mysql84" \
                    && -f "${FAKE_BARRIER_DIR}/mariadb1011" ]]; then
                    break
                fi

                sleep 0.01
            done

            if [[ ! -f "${FAKE_BARRIER_DIR}/mysql84" \
                || ! -f "${FAKE_BARRIER_DIR}/mariadb1011" ]]; then
                exit 29
            fi
        fi

        if [[ "${target}" != "${FAKE_MISSING_EVIDENCE_TARGET:-}" ]]; then
            printf '{"generatedUtc":"2026-10-14T00:00:00Z","lifecycleState":"cleanup-completed","targets":[{"targetId":"%s"}]}\\n' \
                "${target}" > "${DOKA_TEST_DATABASE_EVIDENCE_FILE:?}"
        fi

        printf 'end:%s\\n' "${target}" >> "${FAKE_EVENT_PATH}"

        if [[ "${target}" == "${FAKE_FAIL_TARGET:-}" ]]; then
            exit 17
        fi
        ;;
    *)
        exit 30
        ;;
esac
""",
            encoding="ascii",
        )
        dotnet.chmod(0o755)

        self.environment = os.environ.copy()
        self.environment.update(
            {
                "PATH": f"{tool_directory}{os.pathsep}{self.environment['PATH']}",
                "FAKE_DOTNET_VERSION": json.loads(
                    (self.repository / "global.json").read_text(encoding="utf-8")
                )["sdk"]["version"],
                "FAKE_TEST_ASSEMBLY_PATH": str(self.test_assembly),
                "FAKE_EVENT_PATH": str(self.events),
                "FAKE_BARRIER_DIR": str(self.barrier),
                "DOKA_INTEGRATION_TARGETS": "mysql84,mariadb1011,mariadb114",
                "DOKA_INTEGRATION_ARTIFACTS_DIR": str(self.artifacts),
                "DOKA_COVERAGE_RESULTS_DIR": str(self.root / "coverage"),
            }
        )
        for variable in (
            "DOKA_TEST_DATABASE_EVIDENCE_FILE",
            "DOKA_INTEGRATION_TEST_FILTER",
            "DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX",
            "FAKE_FAIL_TARGET",
            "FAKE_MISSING_EVIDENCE_TARGET",
            "FAKE_WAIT_FOR_SIGNAL",
        ):
            self.environment.pop(variable, None)

    def test_runs_two_targets_concurrently_without_exceeding_the_limit(self) -> None:
        """Overlap independent targets and preserve complete merged evidence."""
        # Act
        result = self._run()

        # Assert
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self._assert_bounded_events()
        self._assert_isolated_outputs()
        evidence = self._read_matrix_evidence()
        self.assertEqual(0, evidence["testExitCode"])
        self.assertEqual(
            ["mariadb1011", "mariadb114", "mysql84"],
            [target["targetId"] for target in evidence["testDatabase"]["targets"]],
        )

    def test_failed_target_does_not_skip_other_targets_or_report_success(self) -> None:
        """Wait for every child and retain its failure after evidence merging."""
        # Arrange
        self.environment["FAKE_FAIL_TARGET"] = "mariadb1011"

        # Act
        result = self._run()

        # Assert
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self._assert_bounded_events()
        self._assert_isolated_outputs()
        evidence = self._read_matrix_evidence()
        self.assertEqual(1, evidence["testExitCode"])
        self.assertEqual(3, len(evidence["testDatabase"]["targets"]))

    def test_missing_target_evidence_fails_closed(self) -> None:
        """Neither process success nor stale files replace current evidence."""
        # Arrange
        self.environment["FAKE_MISSING_EVIDENCE_TARGET"] = "mariadb1011"
        stale_target = (
            self.artifacts
            / "database-targets"
            / "mariadb1011"
            / "test-database-evidence.json"
        )
        stale_target.parent.mkdir(parents=True)
        stale_target.write_text(
            '{"generatedUtc":"2026-10-13T00:00:00Z","lifecycleState":"cleanup-completed",'
            '"targets":[{"targetId":"mariadb1011"}]}\n',
            encoding="ascii",
        )
        (self.artifacts / "test-database-evidence.json").write_text(
            '{"schemaVersion":1,"lifecycleState":"cleanup-completed","targets":[]}\n',
            encoding="ascii",
        )

        # Act
        result = self._run()

        # Assert
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("Missing target database evidence", result.stderr)
        evidence = self._read_matrix_evidence()
        self.assertEqual(1, evidence["testExitCode"])
        self.assertIsNone(evidence["testDatabase"])

    def test_termination_stops_both_active_target_processes(self) -> None:
        """Do not leave database-test workers running after cancellation."""
        # Arrange
        self.environment["FAKE_WAIT_FOR_SIGNAL"] = "1"
        process = subprocess.Popen(
            ["bash", str(self.repository / "eng" / "testing" / "test-integration.sh")],
            cwd=self.repository,
            env=self.environment,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )

        try:
            deadline = time.monotonic() + 5
            while True:
                events = self.events.read_text(encoding="ascii") if self.events.exists() else ""
                if "ready:mysql84" in events and "ready:mariadb1011" in events:
                    break

                self.assertIsNone(process.poll(), "Matrix exited before both workers became ready.")
                self.assertLess(time.monotonic(), deadline, "Workers did not become ready.")
                time.sleep(0.01)

            # Act
            process.terminate()
            stdout, stderr = process.communicate(timeout=5)

            # Assert
            self.assertEqual(143, process.returncode, stdout + stderr)
            events = self.events.read_text(encoding="ascii")
            self.assertIn("terminated:mysql84", events)
            self.assertIn("terminated:mariadb1011", events)
            self.assertNotIn("start:mariadb114", events)
        finally:
            if process.poll() is None:
                process.kill()
                process.communicate(timeout=5)

            for pid_file in self.barrier.glob("*.pid"):
                try:
                    os.kill(int(pid_file.read_text(encoding="ascii")), signal.SIGKILL)
                except ProcessLookupError:
                    pass

    def _run(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["bash", str(self.repository / "eng" / "testing" / "test-integration.sh")],
            cwd=self.repository,
            env=self.environment,
            capture_output=True,
            text=True,
            timeout=20,
            check=False,
        )

    def _read_matrix_evidence(self) -> dict[str, object]:
        return json.loads(
            (self.artifacts / "compatibility-matrix-evidence.json").read_text(
                encoding="utf-8"
            )
        )

    def _assert_bounded_events(self) -> None:
        events = self.events.read_text(encoding="ascii").splitlines()
        active_targets: set[str] = set()
        max_active = 0

        for event in events:
            action, target = event.split(":", 1)
            if action == "start":
                self.assertNotIn(target, active_targets)
                active_targets.add(target)
                max_active = max(max_active, len(active_targets))
            else:
                self.assertEqual("end", action)
                self.assertIn(target, active_targets)
                active_targets.remove(target)

        self.assertEqual(2, max_active)
        self.assertEqual(3, sum(event.startswith("start:") for event in events))
        self.assertFalse(active_targets)

    def _assert_isolated_outputs(self) -> None:
        output_directories = [
            Path((self.barrier / f"{target}.output").read_text(encoding="ascii").strip())
            for target in ("mysql84", "mariadb1011", "mariadb114")
        ]

        self.assertEqual(3, len(set(output_directories)))
        self.assertTrue(
            all(
                directory.is_relative_to(self.repository / "artifacts")
                for directory in output_directories
            )
        )
        self.assertTrue(all(not directory.exists() for directory in output_directories))


if __name__ == "__main__":
    unittest.main()
