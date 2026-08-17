"""Regression tests for clean-runner publication readiness."""

from __future__ import annotations

import json
import os
import re
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path


class PublicationReadinessTests(unittest.TestCase):
    """Prove the release boundary owns every build artifact it consumes."""

    def setUp(self) -> None:
        """Create an isolated CLI and temporary output root."""
        self.repo = Path(__file__).resolve().parents[2]
        self.script = self.repo / "eng" / "release" / "check-publication-readiness.sh"
        self.approved_sdk = json.loads(
            (self.repo / "global.json").read_text(encoding="utf-8")
        )["sdk"]["version"]
        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-publication-readiness-contract-"
        )
        self.root = Path(self.temporary_directory.name)
        self.bin_directory = self.root / "bin"
        self.bin_directory.mkdir()
        self.command_log = self.root / "dotnet-commands.txt"
        self._write_fake_dotnet()

    def tearDown(self) -> None:
        """Remove the isolated CLI and its command log."""
        self.temporary_directory.cleanup()

    def test_gate_builds_and_executes_both_assemblies_in_one_isolated_tree(self) -> None:
        """Reject reliance on an assembly left by a previous job or developer build."""
        environment = os.environ.copy()
        environment["PATH"] = f"{self.bin_directory}{os.pathsep}{environment['PATH']}"
        environment["TMPDIR"] = str(self.root)
        environment["DOKA_TEST_DOTNET_LOG"] = str(self.command_log)

        result = subprocess.run(
            (
                "bash",
                str(self.script),
                "--ef-core-version",
                "10.0.11",
                "--mysqlconnector-version",
                "2.6.1",
            ),
            cwd=self.repo,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        commands = self.command_log.read_text(encoding="utf-8").splitlines()
        self.assertEqual(2, sum(command.startswith("restore|") for command in commands))
        self.assertEqual(2, sum(command.startswith("build|") for command in commands))
        executions = [
            command
            for command in commands
            if "Doka.EntityFrameworkCore.MySql.SpecificationContract.dll" in command
        ]
        self.assertEqual(1, len(executions), commands)

        artifact_paths = {
            match.group(1)
            for command in commands
            if command.startswith(("restore|", "build|"))
            for match in [re.search(r"-p:ArtifactsPath=([^|]+)", command)]
            if match is not None
        }
        self.assertEqual(1, len(artifact_paths), commands)
        artifact_path = Path(artifact_paths.pop())
        self.assertEqual(self.root, artifact_path.parent)
        self.assertRegex(artifact_path.name, r"^doka-publication-readiness[.]")
        self.assertFalse(artifact_path.exists(), "The isolated build tree was not removed.")

        execution = executions[0]
        self.assertIn(str(artifact_path), execution)
        self.assertNotIn(str(self.repo / "artifacts" / "bin"), execution)

        lock_paths = {
            match.group(1)
            for command in commands
            if command.startswith(("restore|", "build|"))
            for match in [re.search(r"-p:NuGetLockFilePath=([^|]+)", command)]
            if match is not None
        }
        self.assertEqual(
            {str(artifact_path / "locks" / "$(MSBuildProjectName).packages.lock.json")},
            lock_paths,
        )

    def test_gate_rejects_a_floating_ef_core_version_before_dotnet(self) -> None:
        """Require the finalizer to bind the check to one matrix-resolved patch."""
        result = subprocess.run(
            (
                "bash",
                str(self.script),
                "--ef-core-version",
                "10.0.*",
                "--mysqlconnector-version",
                "2.6.1",
            ),
            cwd=self.repo,
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(2, result.returncode)
        self.assertIn("one exact EF Core 10.0 patch", result.stderr)

    def test_gate_rejects_a_floating_connector_version_before_dotnet(self) -> None:
        """Require the finalizer to bind the check to one driver-matrix patch."""
        result = subprocess.run(
            (
                "bash",
                str(self.script),
                "--ef-core-version",
                "10.0.11",
                "--mysqlconnector-version",
                "2.*",
            ),
            cwd=self.repo,
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(2, result.returncode)
        self.assertIn("one exact MySqlConnector 2.x patch", result.stderr)

    def _write_fake_dotnet(self) -> None:
        """Write a deterministic CLI that materializes only requested build outputs."""
        fake_dotnet = self.bin_directory / "dotnet"
        fake_dotnet.write_text(
            f"""#!/usr/bin/env bash
set -euo pipefail

if [[ "$1" == "--version" ]]; then
    printf '%s\n' '{self.approved_sdk}'
    exit 0
fi

command_name="$1"
shift
printf '%s|' "${{command_name}}" >> "${{DOKA_TEST_DOTNET_LOG}}"
printf '%s|' "$@" >> "${{DOKA_TEST_DOTNET_LOG}}"
printf '\n' >> "${{DOKA_TEST_DOTNET_LOG}}"

case "${{command_name}}" in
    restore)
        exit 0
        ;;
    build)
        project="$1"
        artifact_path=""
        for argument in "$@"; do
            case "${{argument}}" in
                -p:ArtifactsPath=*)
                    artifact_path="${{argument#-p:ArtifactsPath=}}"
                    ;;
            esac
        done
        if [[ -z "${{artifact_path}}" ]]; then
            echo "The fake build requires an isolated ArtifactsPath." >&2
            exit 3
        fi
        project_name="$(basename "${{project}}" .csproj)"
        output="${{artifact_path}}/bin/${{project_name}}/release/${{project_name}}.dll"
        mkdir -p "$(dirname "${{output}}")"
        : > "${{output}}"
        ;;
    *.dll)
        [[ "${{command_name}}" == *Doka.EntityFrameworkCore.MySql.SpecificationContract.dll ]]
        [[ "$1" == "publication" ]]
        while (( $# > 0 )); do
            if [[ "$1" == "--provider" ]]; then
                [[ -f "$2" ]]
                exit 0
            fi
            shift
        done
        echo "The publication command did not receive a provider assembly." >&2
        exit 4
        ;;
    *)
        echo "Unexpected fake dotnet command: ${{command_name}}" >&2
        exit 5
        ;;
esac
""",
            encoding="ascii",
        )
        fake_dotnet.chmod(fake_dotnet.stat().st_mode | stat.S_IXUSR)


if __name__ == "__main__":
    unittest.main()
