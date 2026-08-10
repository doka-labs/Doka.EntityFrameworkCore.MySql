"""Prove every entry point the release path names can actually be invoked.

The rest of the engineering suite checks functions in isolation and looks for
expected text inside workflows. Both pass while the chain between them is
broken: a workflow can name a stage the orchestrator rejects, a script can call
a subcommand that was never added, and a mode can be threaded through a
workflow into an environment variable nothing reads. Every one of those shipped
green.

These tests execute the entry points instead of describing them. They are
deliberately cheap -- a rejected stage name and an unknown subcommand both fail
in milliseconds, long before any measurement or build would start.
"""

from __future__ import annotations

import os
import re
import subprocess
import sys
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_ROOT = REPOSITORY_ROOT / ".github" / "workflows"


def run(*arguments: str, env: dict[str, str] | None = None) -> subprocess.CompletedProcess[str]:
    """Run one command from the repository root without raising."""
    environment = dict(os.environ)
    environment.update(env or {})
    return subprocess.run(
        arguments,
        cwd=REPOSITORY_ROOT,
        capture_output=True,
        text=True,
        check=False,
        env=environment,
    )


class OrchestratorStageTests(unittest.TestCase):
    """Prove the orchestrator accepts every stage the workflows dispatch."""

    def dispatched_stages(self) -> set[str]:
        """Return every stage name a workflow asks the orchestrator to run."""
        stages: set[str] = set()
        for workflow in WORKFLOW_ROOT.glob("*.yml"):
            text = workflow.read_text(encoding="utf-8")
            stages |= set(
                re.findall(r"release-candidate\.sh --stage ([a-z-]+)", text)
            )
            if "release-candidate.sh --stage \"${{ matrix.stage }}\"" in text:
                stages |= set(re.findall(r"^\s+- stage: ([a-z-]+)$", text, re.M))

        return stages

    def test_every_dispatched_stage_is_accepted(self) -> None:
        """Reject a stage name the orchestrator refuses to run.

        The orchestrator validates the stage name before doing any work, so an
        unknown name fails immediately. That is exactly why this is worth
        executing: the failure is free, and no other test sees it.
        """
        dispatched = self.dispatched_stages()
        self.assertTrue(dispatched, "no dispatched stages found in the workflows")

        for stage in sorted(dispatched):
            with self.subTest(stage=stage):
                result = run(
                    "bash", "eng/release-candidate.sh", "--stage", stage,
                    env={
                        # Bound a regressed probe through the orchestrator's
                        # own process-group deadline. This keeps the contract
                        # cheap even if the early probe exit is moved or lost.
                        "DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS": "5",
                        "DOKA_RELEASE_REQUIRE_TAG": "0",
                        "DOKA_RELEASE_VERSION": "0.0.0-chain-probe",
                        "DOKA_RELEASE_CHAIN_PROBE": "1",
                    },
                )
                combined = f"{result.stdout}\n{result.stderr}"
                self.assertEqual(
                    0,
                    result.returncode,
                    f"the orchestrator could not probe dispatched stage '{stage}':\n{combined}",
                )
                self.assertIn(
                    f"Accepted release-candidate stage '{stage}'.",
                    result.stdout,
                    f"the orchestrator did not acknowledge dispatched stage '{stage}'",
                )


class ReferencedSubcommandTests(unittest.TestCase):
    """Prove every CLI subcommand a script invokes exists."""

    MODULES = {
        "eng.performance.cli",
        "eng.release.evidence",
        "eng.release.trust",
        "eng.release.qualification",
        "eng.release.gate_results",
        "eng.release.nuget",
    }

    def referenced(self) -> set[tuple[str, str]]:
        """Return every (module, subcommand) pair the repository invokes."""
        pattern = re.compile(
            r"python3 -m (?P<module>[\w.]+)\s+\\?\s*(?P<command>[a-z][a-z0-9-]*)"
        )
        found: set[tuple[str, str]] = set()
        for root in ("eng", ".github/workflows"):
            for path in (REPOSITORY_ROOT / root).rglob("*"):
                if path.suffix not in (".sh", ".yml") or not path.is_file():
                    continue
                for match in pattern.finditer(path.read_text(encoding="utf-8")):
                    module = match.group("module")
                    if module in self.MODULES:
                        found.add((module, match.group("command")))

        return found

    REQUIRED_ARGUMENT = re.compile(r"^\s+(--[a-z][a-z0-9-]*) [A-Z_]+$", re.M)

    def test_every_referenced_subcommand_exists(self) -> None:
        """Reject an invocation of a subcommand that was never added.

        A script calling a missing subcommand fails at the moment it runs,
        which in the release path is after the expensive work that precedes it.
        """
        references = self.referenced()
        self.assertTrue(references, "no CLI invocations found")

        for module, command in sorted(references):
            with self.subTest(module=module, command=command):
                result = run(sys.executable, "-m", module, command, "--help")
                self.assertNotIn(
                    "invalid choice",
                    f"{result.stdout}\n{result.stderr}",
                    f"{module} has no subcommand '{command}'",
                )

    def test_every_referenced_subcommand_can_enter_its_handler(self) -> None:
        """Reject a handler that cannot resolve its own names.

        `--help` proves the subcommand is registered and nothing more: argparse
        exits before the handler body runs. A handler calling a function the
        module never imported therefore passed every check and failed on the
        first real invocation -- after the measurement it was meant to record.

        Each subcommand is given its required arguments pointing at paths that
        do not exist. The run is expected to fail; what it must not do is fail
        with a name that could not be resolved.
        """
        unresolved = ("NameError", "ImportError", "ModuleNotFoundError",
                      "AttributeError")

        for module, command in sorted(self.referenced()):
            help_text = run(sys.executable, "-m", module, command, "--help").stdout
            arguments: list[str] = []
            for flag in dict.fromkeys(self.REQUIRED_ARGUMENT.findall(help_text)):
                arguments += [flag, "/nonexistent/chain-probe"]

            with self.subTest(module=module, command=command):
                result = run(sys.executable, "-m", module, command, *arguments)
                combined = f"{result.stdout}\n{result.stderr}"
                for failure in unresolved:
                    self.assertNotIn(
                        failure,
                        combined,
                        f"{module} {command} cannot resolve a name it uses",
                    )


class ReferencedScriptTests(unittest.TestCase):
    """Prove every script a workflow or script invokes exists and can run."""

    def referenced_scripts(self) -> set[str]:
        """Return every repository-relative shell script that is invoked."""
        pattern = re.compile(r"(?:bash|exec)\s+[\"']?(?:\./)?(eng/[\w./-]+\.sh)")
        found: set[str] = set()
        for root in ("eng", ".github/workflows"):
            for path in (REPOSITORY_ROOT / root).rglob("*"):
                if path.suffix not in (".sh", ".yml") or not path.is_file():
                    continue
                found |= set(pattern.findall(path.read_text(encoding="utf-8")))

        return found

    def test_every_referenced_script_exists(self) -> None:
        """Reject a reference to a script that was moved or removed."""
        for relative in sorted(self.referenced_scripts()):
            with self.subTest(script=relative):
                self.assertTrue(
                    (REPOSITORY_ROOT / relative).is_file(),
                    f"{relative} is invoked but does not exist",
                )

    def test_every_referenced_script_parses(self) -> None:
        """Reject a script that cannot be parsed by the shell that runs it."""
        for relative in sorted(self.referenced_scripts()):
            path = REPOSITORY_ROOT / relative
            if not path.is_file():
                continue
            with self.subTest(script=relative):
                result = run("bash", "-n", relative)
                self.assertEqual(0, result.returncode, result.stderr)


class ComparisonModeRoutingTests(unittest.TestCase):
    """Prove a mode threaded through a workflow reaches something that reads it.

    `comparison_mode: paired` travelled from the release workflow into an
    environment variable that no script consumed. Every layer looked correct in
    isolation, and the paired comparison simply never ran.
    """

    VARIABLE = "DOKA_BENCHMARK_COMPARISON_MODE"

    def test_the_workflow_variable_has_a_consumer(self) -> None:
        """Reject an exported mode that no shell or Python module reads."""
        exported = any(
            self.VARIABLE in path.read_text(encoding="utf-8")
            for path in WORKFLOW_ROOT.glob("*.yml")
        )
        self.assertTrue(exported, f"{self.VARIABLE} is not exported by any workflow")

        consumers = [
            path.relative_to(REPOSITORY_ROOT).as_posix()
            for path in (REPOSITORY_ROOT / "eng").rglob("*")
            if path.is_file()
            and path.suffix in (".sh", ".py")
            and self.VARIABLE in path.read_text(encoding="utf-8")
        ]
        self.assertTrue(
            consumers,
            f"{self.VARIABLE} is exported but no script or module reads it",
        )

    def test_the_paired_entry_point_is_reachable(self) -> None:
        """Reject a paired script nothing can start."""
        paired = REPOSITORY_ROOT / "eng" / "performance" / "paired-benchmark.sh"
        self.assertTrue(paired.is_file())

        callers = [
            path.relative_to(REPOSITORY_ROOT).as_posix()
            for path in REPOSITORY_ROOT.rglob("*")
            if path.is_file()
            and path.suffix in (".sh", ".yml")
            and path != paired
            and "paired-benchmark.sh" in path.read_text(
                encoding="utf-8", errors="ignore"
            )
        ]
        self.assertTrue(
            callers, "paired-benchmark.sh is never invoked by anything"
        )


if __name__ == "__main__":
    unittest.main()
