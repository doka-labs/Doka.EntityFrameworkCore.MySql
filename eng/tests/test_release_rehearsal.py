"""Contract tests for the local release rehearsal.

The rehearsal exists so a defect in the qualification path costs a local run
instead of a version number, because a pushed tag can never be reused. These
tests pin what it forwards to the orchestrator: without the tag requirement
lifted and the version supplied it would qualify something other than the
candidate it stands in for.

Every case runs against a throwaway repository with a stub orchestrator, so no
test builds, packs, or starts a container.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WRAPPER = REPOSITORY_ROOT / "eng" / "rehearse-release.sh"
SCRIPT = REPOSITORY_ROOT / "eng" / "release" / "rehearse-release.sh"


class ReleaseRehearsalTests(unittest.TestCase):
    """Prove the rehearsal stands in for a tagged candidate faithfully."""

    def setUp(self) -> None:
        """Build an isolated repository whose orchestrator only reports."""
        self._directory = tempfile.TemporaryDirectory(prefix="doka-rehearsal-")
        self.root = Path(self._directory.name)
        self.addCleanup(self._directory.cleanup)

        release = self.root / "eng" / "release"
        release.mkdir(parents=True)
        shutil.copy(WRAPPER, self.root / "eng" / "rehearse-release.sh")
        shutil.copy(SCRIPT, release / "rehearse-release.sh")

        # The stub records the environment and arguments it was handed instead
        # of running any gate, so the test observes the contract, not a build.
        self.receipt = self.root / "orchestrator-receipt.json"
        stub = release / "release-candidate.sh"
        stub.write_text(
            "#!/usr/bin/env bash\n"
            "python3 - \"$@\" <<'PY'\n"
            "import json, os, sys\n"
            f"open({str(self.receipt)!r}, 'w').write(json.dumps({{\n"
            "    'arguments': sys.argv[1:],\n"
            "    'requireTag': os.environ.get('DOKA_RELEASE_REQUIRE_TAG'),\n"
            "    'version': os.environ.get('DOKA_RELEASE_VERSION'),\n"
            "    'runnerIdentity': os.environ.get('DOKA_RELEASE_RUNNER_IDENTITY'),\n"
            "    'received': {k: v for k, v in os.environ.items()\n"
            "                 if k.startswith(('DOKA_RELEASE', 'DOKA_BENCHMARK'))},\n"
            "}))\n"
            "PY\n",
            encoding="utf-8",
        )
        stub.chmod(0o755)

        self._git("init", "--initial-branch=main")
        self._git("config", "user.email", "rehearsal@example.invalid")
        self._git("config", "user.name", "Rehearsal")
        # A contributor whose global configuration signs every commit has no
        # signing key inside this throwaway repository.
        self._git("config", "commit.gpgsign", "false")
        self._git("add", "--all")
        self._git("commit", "--message", "seed")

    def _git(self, *arguments: str) -> None:
        """Run one fixture-local Git command without leaking output."""
        subprocess.run(
            ("git", "-C", str(self.root), *arguments),
            check=True,
            capture_output=True,
            text=True,
        )

    def rehearse(
        self,
        *arguments: str,
        inject: dict[str, str] | None = None,
    ) -> subprocess.CompletedProcess[str]:
        """Invoke the copied wrapper inside the throwaway repository.

        The caller's release and benchmark variables are cleared first so the
        same test runs the same way on a workstation and on a release runner.
        What a case wants the wrapper to face, it passes through `inject`:
        removing everything up front would leave the wrapper's own handling
        untested, which is exactly how a leak stayed invisible before.
        """
        environment = {
            name: value
            for name, value in os.environ.items()
            if not name.startswith(("DOKA_RELEASE", "DOKA_BENCHMARK"))
        }
        environment.update(inject or {})

        return subprocess.run(
            ["bash", str(self.root / "eng" / "rehearse-release.sh"), *arguments],
            capture_output=True,
            text=True,
            check=False,
            cwd=self.root,
            env=environment,
        )

    def test_rehearsal_lifts_the_tag_requirement_and_supplies_the_version(self) -> None:
        """Reject a rehearsal that would qualify a differently versioned package.

        Without the version the orchestrator packs the bare version prefix, so
        the rehearsal would qualify an artifact the real candidate never builds.
        """
        result = self.rehearse("10.0.0-rc.6")

        self.assertEqual(0, result.returncode, result.stderr)
        receipt = json.loads(self.receipt.read_text(encoding="utf-8"))
        self.assertEqual("0", receipt["requireTag"])
        self.assertEqual("10.0.0-rc.6", receipt["version"])
        self.assertEqual("local-rehearsal", receipt["runnerIdentity"])

    def test_stage_selection_reaches_the_orchestrator(self) -> None:
        """Keep a single gate rehearsable without running the whole path."""
        result = self.rehearse("10.0.0-rc.6", "--stage", "quality")

        self.assertEqual(0, result.returncode, result.stderr)
        receipt = json.loads(self.receipt.read_text(encoding="utf-8"))
        self.assertEqual(["--stage", "quality"], receipt["arguments"])

    def test_leftover_state_does_not_reach_the_orchestrator(self) -> None:
        """Reject state from an earlier run deciding what a rehearsal answers.

        A shell that already ran a rehearsal still holds its variables. Passed
        on, they change the run: a foreign baseline is compared, measurement is
        skipped, or the deadline marker keeps the orchestrator from arming its
        own timeout. None of these are inputs a rehearsal accepts.
        """
        leftovers = {
            "DOKA_RELEASE_CANDIDATE_DEADLINE_ACTIVE": "1",
            "DOKA_RELEASE_CANDIDATE_REUSE_PERFORMANCE_FROM": "/tmp/foreign",
            "DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS": "1",
            "DOKA_RELEASE_CANDIDATE_PERFORMANCE_ARTIFACT_ROOT": "/tmp/foreign",
            "DOKA_BENCHMARK_BASELINE_PATH": "/tmp/foreign-baseline.json",
            "DOKA_BENCHMARK_BASELINE_MODE": "seed",
            "DOKA_BENCHMARK_DEADLINE_ACTIVE": "1",
            "DOKA_BENCHMARK_PROFILE": "smoke",
        }

        result = self.rehearse("10.0.0-rc.6", inject=leftovers)

        self.assertEqual(0, result.returncode, result.stderr)
        received = json.loads(self.receipt.read_text(encoding="utf-8"))["received"]
        for name in leftovers:
            with self.subTest(variable=name):
                self.assertNotIn(name, received)

    def test_the_supported_inputs_are_forwarded(self) -> None:
        """Keep the documented inputs working, so stages can share a directory.

        Rehearsing stage by stage depends on them: without a shared run
        identifier each stage writes its own evidence directory and a later
        stage cannot find what an earlier one produced.
        """
        supported = {
            "DOKA_RELEASE_CANDIDATE_RUN_ID": "rehearsal-chain",
            "DOKA_RELEASE_CANDIDATE_RESUME": "1",
            "DOKA_BENCHMARK_RUNNER_CLASS": "local-test-runner",
        }

        result = self.rehearse("10.0.0-rc.6", inject=supported)

        self.assertEqual(0, result.returncode, result.stderr)
        received = json.loads(self.receipt.read_text(encoding="utf-8"))["received"]
        for name, value in supported.items():
            with self.subTest(variable=name):
                self.assertEqual(value, received.get(name))

    def test_an_inherited_runner_identity_cannot_relabel_the_rehearsal(self) -> None:
        """Keep a rehearsal identifiable as one in the evidence it produces.

        A release runner exports its own identity. Inherited, it would label
        local evidence as if a hosted runner had produced it.
        """
        result = self.rehearse(
            "10.0.0-rc.6",
            inject={"DOKA_RELEASE_RUNNER_IDENTITY": "github/1000/repository-tests"},
        )

        self.assertEqual(0, result.returncode, result.stderr)
        receipt = json.loads(self.receipt.read_text(encoding="utf-8"))
        self.assertEqual("local-rehearsal", receipt["runnerIdentity"])

    def test_one_passing_stage_is_not_reported_as_a_passing_path(self) -> None:
        """Reject the reading that one green stage qualifies the candidate.

        The rehearsal exists to remove false confidence, so reporting a single
        stage in the words of a complete run would defeat its purpose.
        """
        result = self.rehearse("10.0.0-rc.6", "--stage", "quality")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Stage 'quality' passed", result.stdout)
        self.assertIn("still unproven", result.stdout)

    def test_a_baseline_without_this_runner_class_is_announced_up_front(self) -> None:
        """Announce the unusable comparison before the workloads are measured.

        The comparison runs last, so discovering it afterwards costs the whole
        measurement. The rehearsal still proceeds: measuring the workloads is
        worth something on its own.
        """
        baseline = self.root / "benchmarks" / "baselines"
        baseline.mkdir(parents=True)
        (baseline / "doka-benchmark-baseline.json").write_text(
            json.dumps({"baselines": [{"runnerClass": "github-ubuntu-latest-x64"}]}, indent=2),
            encoding="utf-8",
        )
        self._git("add", "--all")
        self._git("commit", "--message", "baseline")

        result = self.rehearse("10.0.0-rc.6")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("holds no entry for runner class", result.stdout)
        self.assertIn("--stage quality", result.stdout)

    def test_a_tag_prefixed_version_is_rejected(self) -> None:
        """Reject the tag spelling, which would pack a version nobody requested."""
        result = self.rehearse("v10.0.0-rc.6")

        self.assertEqual(2, result.returncode)
        self.assertIn("without the leading", result.stderr)
        self.assertFalse(self.receipt.exists())

    def test_a_missing_version_stops_before_the_orchestrator(self) -> None:
        """Refuse to guess which candidate the rehearsal stands in for."""
        result = self.rehearse()

        self.assertEqual(2, result.returncode)
        self.assertIn("Usage:", result.stderr)
        self.assertFalse(self.receipt.exists())

    def test_a_dirty_worktree_stops_the_rehearsal(self) -> None:
        """Refuse to qualify state no tag could ever point at."""
        (self.root / "stray.txt").write_text("uncommitted", encoding="utf-8")

        result = self.rehearse("10.0.0-rc.6")

        self.assertEqual(1, result.returncode)
        self.assertIn("clean worktree", result.stderr)
        self.assertFalse(self.receipt.exists())

    def test_a_failing_gate_is_reported_without_spending_a_tag(self) -> None:
        """Surface the orchestrator's exit code instead of masking it."""
        stub = self.root / "eng" / "release" / "release-candidate.sh"
        stub.write_text("#!/usr/bin/env bash\nexit 3\n", encoding="utf-8")
        stub.chmod(0o755)
        self._git("add", "--all")
        self._git("commit", "--message", "failing gate")

        result = self.rehearse("10.0.0-rc.6")

        self.assertEqual(3, result.returncode)
        self.assertIn("Rehearsal failed", result.stdout)
        self.assertIn("No tag was spent", result.stdout)


if __name__ == "__main__":
    unittest.main()
