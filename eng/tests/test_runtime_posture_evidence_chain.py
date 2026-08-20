"""Exercise runtime-posture evidence across its release-consumer boundary."""

from __future__ import annotations

import json
import os
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path

from eng.release.evidence import validate_runtime_posture


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
RUNTIME_POSTURE = REPOSITORY_ROOT / "eng" / "testing" / "test-runtime-posture.sh"
SOURCE_COMMIT = "5" * 40
TARGET_IMAGE = f"mysql:8.4.11@sha256:{'8' * 64}"


class RuntimePostureEvidenceChainTests(unittest.TestCase):
    """Keep source identity valid from the runtime producer to finalization."""

    def setUp(self) -> None:
        """Create controlled command boundaries and isolated evidence roots."""
        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-runtime-posture-contract-",
        )
        self.root = Path(self.temporary_directory.name)
        self.bin_directory = self.root / "bin"
        self.bin_directory.mkdir()
        self.git_state = self.root / "git-state.txt"
        self.git_state.write_text("clean\n", encoding="ascii")
        self.command_log = self.root / "dotnet-commands.txt"
        self.evidence_root = self.root / "candidate"
        self.publish_directory = self.root / "trimmed"
        self._write_fake_git()
        self._write_fake_dotnet()
        self._write_fake_uname()

    def tearDown(self) -> None:
        """Remove all controlled command and evidence state."""
        self.temporary_directory.cleanup()

    def test_isolated_rid_publish_produces_release_acceptable_evidence(self) -> None:
        """Prove the actual producer survives RID restore and its real consumer."""
        result = self._run_posture()

        self.assertEqual(0, result.returncode, result.stderr)
        evidence = validate_runtime_posture(
            self.evidence_root,
            "test-run",
            SOURCE_COMMIT,
        )
        self.assertEqual("linux-x64", evidence["runtimeIdentifier"])
        self.assertEqual("clean", self.git_state.read_text(encoding="ascii").strip())

        commands = self.command_log.read_text(encoding="ascii").splitlines()
        self.assertEqual(
            ["restore", "restore", "restore", "run", "publish"],
            [line.split("|", 1)[0] for line in commands],
        )
        restore_commands = commands[:3]
        expected_lock_names = {
            "Doka.EntityFrameworkCore.MySql.packages.lock.json",
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite.packages.lock.json",
            "Doka.EntityFrameworkCore.MySql.RuntimeSmoke.packages.lock.json",
        }
        actual_lock_names = set()
        for command in restore_commands:
            restore_arguments = command.split("|")[1:]
            self.assertIn("--runtime", restore_arguments)
            self.assertEqual("linux-x64", restore_arguments[restore_arguments.index("--runtime") + 1])
            self.assertIn("-p:RestoreRecursive=false", restore_arguments)
            lock_argument = next(
                argument
                for argument in restore_arguments
                if argument.startswith("-p:NuGetLockFilePath=")
            )
            actual_lock_names.add(Path(lock_argument.split("=", 1)[1]).name)
        self.assertEqual(expected_lock_names, actual_lock_names)

        for command in commands[3:]:
            with self.subTest(no_restore_command=command):
                self.assertIn("--no-restore", command.split("|")[1:])
        for command in commands:
            with self.subTest(command=command):
                self.assertIn("-p:ArtifactsPath=", command)

        artifact_paths = {
            argument.removeprefix("-p:ArtifactsPath=")
            for command in commands
            for argument in command.split("|")[1:]
            if argument.startswith("-p:ArtifactsPath=")
        }
        self.assertEqual(1, len(artifact_paths))
        self.assertFalse(Path(artifact_paths.pop()).exists())

    def test_dirty_source_is_rejected_before_dotnet_executes(self) -> None:
        """Do not let generated evidence relabel an initially dirty tree clean."""
        self.git_state.write_text("dirty\n", encoding="ascii")

        result = self._run_posture()

        self.assertNotEqual(0, result.returncode)
        self.assertIn("clean source tree", result.stderr)
        self.assertFalse(self.command_log.exists())
        self.assertFalse(self._evidence_file().exists())

    def test_publish_side_effect_is_rejected_before_evidence_is_written(self) -> None:
        """Fail closed if an isolated publish still changes the source tree."""
        result = self._run_posture(force_dirty_after_publish=True)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("changed the source tree", result.stderr)
        self.assertFalse(self._evidence_file().exists())

    def _run_posture(self, *, force_dirty_after_publish: bool = False) -> subprocess.CompletedProcess[str]:
        environment = os.environ.copy()
        environment.update(
            {
                "PATH": f"{self.bin_directory}{os.pathsep}{environment['PATH']}",
                "DOKA_RUNTIME_POSTURE_RUN_ID": "test-run",
                "DOKA_RUNTIME_POSTURE_EVIDENCE_DIR": str(self.evidence_root / "runtime"),
                "DOKA_RUNTIME_POSTURE_PUBLISH_DIR": str(self.publish_directory),
                "DOKA_RUNTIME_REQUIRE_CLEAN_SOURCE": "1",
                "DOKA_RUNTIME_TARGET_IMAGE": TARGET_IMAGE,
                "DOKA_TEST_DOTNET_LOG": str(self.command_log),
                "DOKA_TEST_GIT_STATE": str(self.git_state),
                "DOKA_TEST_FORCE_DIRTY_AFTER_PUBLISH": (
                    "1" if force_dirty_after_publish else "0"
                ),
                "TMPDIR": str(self.root),
            }
        )
        return subprocess.run(
            ("bash", str(RUNTIME_POSTURE), "--test-only"),
            cwd=REPOSITORY_ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
        )

    def _evidence_file(self) -> Path:
        return self.evidence_root / "runtime" / "runtime-posture-evidence.json"

    def _write_fake_git(self) -> None:
        fake_git = self.bin_directory / "git"
        fake_git.write_text(
            f"""#!/usr/bin/env bash
set -euo pipefail

if [[ "$1" == "-C" ]]; then
    shift 2
fi

case "$1" in
    rev-parse)
        printf '%s\n' '{SOURCE_COMMIT}'
        ;;
    status)
        if [[ "$(< "${{DOKA_TEST_GIT_STATE}}")" == "dirty" ]]; then
            printf ' M src/Doka.EntityFrameworkCore.MySql/packages.lock.json\n'
        fi
        ;;
    *)
        echo "Unexpected fake git command: $*" >&2
        exit 7
        ;;
esac
""",
            encoding="ascii",
        )
        fake_git.chmod(fake_git.stat().st_mode | stat.S_IXUSR)

    def _write_fake_dotnet(self) -> None:
        approved_sdk = json.loads(
            (REPOSITORY_ROOT / "global.json").read_text(encoding="utf-8")
        )["sdk"]["version"]
        fake_dotnet = self.bin_directory / "dotnet"
        fake_dotnet.write_text(
            f"""#!/usr/bin/env bash
set -euo pipefail

if [[ "$1" == "--version" ]]; then
    printf '%s\n' '{approved_sdk}'
    exit 0
fi

command_name="$1"
shift
{{
    printf '%s' "${{command_name}}"
    printf '|%s' "$@"
    printf '\n'
}} >> "${{DOKA_TEST_DOTNET_LOG}}"

case "${{command_name}}" in
    restore)
        isolated_lock=0
        non_recursive=0
        for argument in "$@"; do
            case "${{argument}}" in
                -p:NuGetLockFilePath=*)
                    isolated_lock=1
                    ;;
                -p:RestoreRecursive=false)
                    non_recursive=1
                    ;;
            esac
        done
        if [[ "${{isolated_lock}}" == "0" || "${{non_recursive}}" == "0" ]]; then
            printf 'dirty\n' > "${{DOKA_TEST_GIT_STATE}}"
        fi
        exit 0
        ;;
    run)
        exit 0
        ;;
    publish)
        output=""
        isolated_artifacts=0
        while (( $# > 0 )); do
            case "$1" in
                -o)
                    output="$2"
                    shift 2
                    ;;
                -p:ArtifactsPath=*)
                    isolated_artifacts=1
                    shift
                    ;;
                *)
                    shift
                    ;;
            esac
        done
        if [[ "${{DOKA_TEST_FORCE_DIRTY_AFTER_PUBLISH}}" == "1" \
            || "${{isolated_artifacts}}" == "0" ]]; then
            printf 'dirty\n' > "${{DOKA_TEST_GIT_STATE}}"
        fi
        mkdir -p "${{output}}"
        executable="${{output}}/Doka.EntityFrameworkCore.MySql.RuntimeSmoke"
        printf '#!/usr/bin/env bash\nexit 0\n' > "${{executable}}"
        chmod +x "${{executable}}"
        ;;
    *)
        echo "Unexpected fake dotnet command: ${{command_name}}" >&2
        exit 8
        ;;
esac
""",
            encoding="ascii",
        )
        fake_dotnet.chmod(fake_dotnet.stat().st_mode | stat.S_IXUSR)

    def _write_fake_uname(self) -> None:
        fake_uname = self.bin_directory / "uname"
        fake_uname.write_text(
            """#!/usr/bin/env bash
set -euo pipefail

case "$1" in
    -s)
        printf 'Linux\n'
        ;;
    -m)
        printf 'x86_64\n'
        ;;
    *)
        exit 9
        ;;
esac
""",
            encoding="ascii",
        )
        fake_uname.chmod(fake_uname.stat().st_mode | stat.S_IXUSR)


if __name__ == "__main__":
    unittest.main()
