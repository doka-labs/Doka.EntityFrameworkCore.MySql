"""Prove the driver measures the profile it is asked for.

The resolver recognized `scorecard` and `stress` and sent everything else to
`smoke`. `paired-block` therefore measured the smoke subset -- fifteen of
fifty-five workloads, one row count instead of three, one entity count instead
of four -- while the evidence it produced named `paired-block` and the policy
claimed `complete-matrix`. Nothing compared the claim to the run.

These tests execute the driver's own workload listing per profile. They are
skipped when the Release driver has not been built, and the skip is explicit so
an unbuilt driver is visible rather than silently green.
"""

from __future__ import annotations

import json
import os
import subprocess
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
PROFILES_SOURCE = (
    REPOSITORY_ROOT
    / "benchmarks"
    / "Doka.EntityFrameworkCore.MySql.Benchmarks"
    / "BenchmarkProfiles.cs"
)


def driver_path() -> Path | None:
    """Return the built Release driver, or None when it is absent."""
    candidate = (
        REPOSITORY_ROOT
        / "artifacts"
        / "bin"
        / "Doka.EntityFrameworkCore.MySql.Benchmarks"
        / "release"
        / "Doka.EntityFrameworkCore.MySql.Benchmarks.dll"
    )

    return candidate if candidate.is_file() else None


def list_workloads(profile: str) -> list[str]:
    """Return the workload identifiers the driver applies for one profile."""
    driver = driver_path()
    environment = dict(os.environ, DOKA_BENCHMARK_PROFILE=profile)
    result = subprocess.run(
        ["dotnet", str(driver), "--list-workloads"],
        cwd=REPOSITORY_ROOT,
        capture_output=True,
        text=True,
        check=True,
        env=environment,
    )

    return [line for line in result.stdout.splitlines() if line.strip()]


class ProfileResolverSourceTests(unittest.TestCase):
    """Prove every registered profile is named in the resolver.

    This runs without a build, so a contract that grows a profile the driver
    would silently downgrade fails here immediately.
    """

    def test_every_contract_profile_is_recognized(self) -> None:
        """Reject a registered profile the resolver does not name."""
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        source = PROFILES_SOURCE.read_text(encoding="utf-8")

        for profile in sorted(contract["profiles"]):
            with self.subTest(profile=profile):
                self.assertIn(
                    f'"{profile}"',
                    source,
                    f"the driver does not name '{profile}' and would fall back "
                    "to the smoke subset while reporting this profile",
                )

    def test_the_paired_profile_measures_the_complete_matrix(self) -> None:
        """Bind the policy's scope claim to the resolver's own grouping."""
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        scope = contract["pairedPolicy"]["primaryFamily"]["workloadScope"]
        self.assertEqual("complete-matrix", scope)

        source = PROFILES_SOURCE.read_text(encoding="utf-8")
        grouping = source[
            source.index("s_completeMatrixProfiles") : source.index("];", source.index(
                "s_completeMatrixProfiles"
            ))
        ]

        self.assertIn("PairedBlockProfile", grouping)


@unittest.skipIf(
    driver_path() is None,
    "the Release benchmark driver is not built; build it to run these",
)
class ProfileWorkloadCoverageTests(unittest.TestCase):
    """Prove the applied workload set matches the profile's declared scope."""

    def test_the_paired_profile_applies_the_complete_matrix(self) -> None:
        """Execute the listing rather than reasoning about the resolver."""
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        registered = {workload["id"] for workload in contract["workloads"]}

        applied = set(list_workloads("paired-block"))

        self.assertEqual(registered, applied)

    def test_the_paired_profile_matches_the_scorecard_matrix(self) -> None:
        """Keep the paired comparison on the same matrix the release measures.

        A paired run on a narrower matrix would qualify a provider against
        coverage the release never had.
        """
        self.assertEqual(
            sorted(list_workloads("scorecard")), sorted(list_workloads("paired-block"))
        )

    def test_the_smoke_profile_still_narrows(self) -> None:
        """Establish that the comparison above is not vacuous.

        If smoke applied the complete matrix too, the previous fallback would
        have been harmless and these tests would prove nothing.
        """
        smoke = list_workloads("smoke")
        paired = list_workloads("paired-block")

        self.assertLess(len(smoke), len(paired))
        self.assertTrue(set(smoke).issubset(set(paired)))


if __name__ == "__main__":
    unittest.main()
