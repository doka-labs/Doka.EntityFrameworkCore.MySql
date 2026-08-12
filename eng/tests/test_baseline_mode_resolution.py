"""Contract tests for the baseline-mode resolver on the checked-in evidence.

The hosted benchmark workflow starts by asking this resolver what to do with
the accepted baseline. Everything after it -- the measurement matrix, the
proposal, the release candidate -- depends on that answer, and the ordinary
quality gates never ask the question.

That gap is the reason for this module. A contract edited without a new
contract version leaves the baseline claiming the same version while carrying
different bytes. The resolver then validates instead of reseeding, the
validation correctly fails, and the workflow stops before it measures
anything. Locally every gate stays green.

The resolver is invoked exactly as `.github/workflows/benchmark.yml` invokes
it, against the files the repository actually ships.
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from eng.performance.contract import (
    PerformanceEvidenceError,
    validate_contract_version,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CONTRACT = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
BASELINE = REPOSITORY_ROOT / "benchmarks" / "baselines" / "doka-benchmark-baseline.json"

# Only these two answers let a hosted run proceed. Anything else means the
# workflow stops before the matrix.
VALID_MODES = {"compare", "seed"}


def resolve(contract: Path, baseline: Path, output: Path) -> subprocess.CompletedProcess[str]:
    """Run the resolver the way the hosted benchmark workflow runs it."""
    return subprocess.run(
        [
            sys.executable,
            "-m",
            "eng.performance.cli",
            "resolve-baseline-mode",
            "--contract",
            str(contract),
            "--baseline",
            str(baseline),
            "--profile",
            "scorecard",
            "--runner-class",
            "github-ubuntu-latest-x64",
            "--requested-mode",
            "auto",
            "--output",
            str(output),
        ],
        capture_output=True,
        text=True,
        check=False,
        cwd=REPOSITORY_ROOT,
    )


class CheckedInBaselineModeTests(unittest.TestCase):
    """Prove the shipped contract and baseline resolve to a usable mode."""

    def setUp(self) -> None:
        """Give each case its own output path."""
        self._directory = tempfile.TemporaryDirectory(prefix="doka-baseline-mode-")
        self.output = Path(self._directory.name) / "baseline-mode.json"
        self.addCleanup(self._directory.cleanup)

    def test_the_repository_resolves_to_compare_or_seed(self) -> None:
        """Reject a state where the hosted benchmark stops before measuring.

        A contract revision without a new version is the way this breaks: the
        baseline claims the same version while carrying different bytes, so the
        resolver validates rather than reseeds, and the validation fails.
        """
        result = resolve(CONTRACT, BASELINE, self.output)

        self.assertEqual(0, result.returncode, result.stderr)
        resolved = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertIn(resolved["mode"], VALID_MODES, resolved)

    def test_the_contract_version_matches_the_baseline_or_supersedes_it(self) -> None:
        """Keep the two versions in one of the two states the resolver expects.

        Equal versions mean the baseline belongs to this contract and must
        validate against it. Different versions mean a reseed. There is no
        third state in which a hosted run can proceed.
        """
        contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
        baseline = json.loads(BASELINE.read_text(encoding="utf-8"))

        resolved = resolve(CONTRACT, BASELINE, self.output)
        self.assertEqual(0, resolved.returncode, resolved.stderr)
        mode = json.loads(self.output.read_text(encoding="utf-8"))

        if contract["contractVersion"] == baseline["contractVersion"]:
            self.assertEqual("compare", mode["mode"], mode)
        else:
            self.assertEqual("seed", mode["mode"], mode)
            self.assertEqual("contract-version-mismatch", mode["baselineDisposition"])


class ContractVersionFormatTests(unittest.TestCase):
    """Prove a contract version denotes a real point in time.

    The version is evidence identity: it seeds a baseline, names a proposal
    branch, and persists into release evidence. A value that merely looks like
    a date would carry all of that while denoting a day that never existed.
    """

    ACCEPTED = (
        "2026-08-09",
        "2026-08-09.2",
        "2026-08-09.10",
        "2024-02-29",
    )
    REJECTED = (
        "2026-02-29",
        "2026-13-40",
        "0000-00-00",
        "2026-08-09.0",
        "2026-08-09.1",
        "2026-08-09.02",
        "2026-8-9",
        "2026-08-09.",
        "yesterday",
    )

    def test_real_dates_and_same_day_revisions_are_accepted(self) -> None:
        """Accept a calendar date, with or without a revision from two upward."""
        for value in self.ACCEPTED:
            with self.subTest(version=value):
                validate_contract_version(value)

    def test_impossible_dates_and_revisions_are_rejected(self) -> None:
        """Reject a value that is not a real date or not a valid revision.

        2026 has no 29 February; a first revision carries no suffix, so a
        counter starts at two; and a padded counter is a second spelling of a
        version that already exists.
        """
        for value in self.REJECTED:
            with self.subTest(version=value):
                with self.assertRaises(PerformanceEvidenceError):
                    validate_contract_version(value)

    def test_the_shipped_contract_version_is_valid(self) -> None:
        """Keep the checked-in contract inside its own format."""
        shipped = json.loads(CONTRACT.read_text(encoding="utf-8"))

        validate_contract_version(shipped["contractVersion"])


class WorkflowEntryTests(unittest.TestCase):
    """Prove the hosted workflow reaches the resolver at all.

    The workflow step runs under `bash -e`, so anything before the resolver
    that rejects the contract stops the run without a diagnostic from the
    evidence tooling. A second version pattern living in that step is exactly
    such a thing: it can disagree with the validator and fail a contract the
    tooling considers valid.
    """

    WORKFLOW = REPOSITORY_ROOT / ".github" / "workflows" / "benchmark.yml"

    def test_the_workflow_declares_no_second_version_contract(self) -> None:
        """Keep one definition of the version format in the tooling.

        The whole file is searched rather than the part preceding the resolver
        call: the job is named after the command, so splitting on that name
        cuts above the step and leaves the step itself unexamined.
        """
        offenders = [
            f"line {number}: {line.strip()}"
            for number, line in enumerate(
                self.WORKFLOW.read_text(encoding="utf-8").splitlines(), start=1
            )
            if "[0-9]{4}-[0-9]{2}-[0-9]{2}" in line
        ]

        self.assertEqual([], offenders)

    def test_the_shipped_version_survives_the_workflow_entry(self) -> None:
        """Reject a contract version the workflow would reject before measuring.

        The check is the resolver itself plus the read-back the step performs,
        which is what the workflow does once the duplicated pattern is gone.
        """
        with tempfile.TemporaryDirectory(prefix="doka-workflow-entry-") as directory:
            output = Path(directory) / "baseline-mode.json"
            resolved = resolve(CONTRACT, BASELINE, output)
            self.assertEqual(0, resolved.returncode, resolved.stderr)

            read_back = subprocess.run(
                ["jq", "-er", ".contractVersion", str(output)],
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(0, read_back.returncode, read_back.stderr)
        contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
        self.assertEqual(contract["contractVersion"], read_back.stdout.strip())

    def test_the_version_forms_a_usable_branch_name(self) -> None:
        """Keep the proposal branch derivable from the version.

        The workflow names the baseline proposal branch after the contract
        version, so a version that is not a valid ref component would fail
        after the matrix rather than before it.
        """
        contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
        branch = f"automation/performance-baseline-{contract['contractVersion']}"

        result = subprocess.run(
            ["git", "check-ref-format", "--branch", branch],
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, result.returncode, branch)


class BaselineModeDriftTests(unittest.TestCase):
    """Prove a contract edited without a new version is caught, not hidden."""

    def setUp(self) -> None:
        """Copy the contract and baseline into a throwaway directory."""
        self._directory = tempfile.TemporaryDirectory(prefix="doka-baseline-drift-")
        self.root = Path(self._directory.name)
        self.addCleanup(self._directory.cleanup)

        self.contract = self.root / "performance-contract.json"
        self.baseline = self.root / "baseline.json"
        self.output = self.root / "baseline-mode.json"
        shutil.copy(CONTRACT, self.contract)
        shutil.copy(BASELINE, self.baseline)

    def align_versions(self) -> None:
        """Make the contract claim the version the baseline was accepted under."""
        contract = json.loads(self.contract.read_text(encoding="utf-8"))
        baseline = json.loads(self.baseline.read_text(encoding="utf-8"))
        contract["contractVersion"] = baseline["contractVersion"]
        self.contract.write_text(json.dumps(contract, indent=2), encoding="utf-8")

    def complete_baseline_control_matrix(self) -> None:
        """Keep this drift probe focused when the contract adds a control.

        A stale baseline naturally lacks controls introduced by a new contract
        revision. This test targets the more dangerous state where the version
        was not revised, so its temporary baseline must first be structurally
        complete enough to reach the contract-byte binding.
        """
        contract = json.loads(self.contract.read_text(encoding="utf-8"))
        baseline = json.loads(self.baseline.read_text(encoding="utf-8"))

        for entry in baseline["baselines"]:
            controls = entry["benchmarkDotNetControls"]
            existing_ids = {control["id"] for control in controls}

            for expected in contract["benchmarkDotNetControls"]:
                if expected["id"] in existing_ids:
                    continue

                controls.append(
                    {
                        "id": expected["id"],
                        "metric": expected["metric"],
                        "actual": 0,
                        "maximum": expected["maximum"],
                        "passed": True,
                    }
                )

        self.baseline.write_text(json.dumps(baseline, indent=2), encoding="utf-8")

    def test_same_version_with_different_bytes_stops_the_run(self) -> None:
        """Reject the state this module exists for.

        The contract carries an image the baseline was never measured with,
        while both claim the same version. The resolver has to fail rather than
        let the matrix measure against evidence that does not describe it.
        """
        self.align_versions()
        self.complete_baseline_control_matrix()

        result = resolve(self.contract, self.baseline, self.output)

        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn("different contract bytes", result.stderr)

    def test_a_new_version_reseeds_instead_of_failing(self) -> None:
        """Accept a contract revision as a reseed rather than as corruption."""
        contract = json.loads(self.contract.read_text(encoding="utf-8"))
        contract["contractVersion"] = "2999-01-01"
        self.contract.write_text(json.dumps(contract, indent=2), encoding="utf-8")

        result = resolve(self.contract, self.baseline, self.output)

        self.assertEqual(0, result.returncode, result.stderr)
        resolved = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertEqual("seed", resolved["mode"])
        self.assertEqual("contract-version-mismatch", resolved["baselineDisposition"])


if __name__ == "__main__":
    unittest.main()
