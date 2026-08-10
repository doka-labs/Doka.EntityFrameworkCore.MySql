"""Execute the guards that were only ever proven by hand.

Three properties of the paired comparison were demonstrated once in a shell and
never guarded: the outer deadline classifying as a measurement condition, the
driver hash binding the contents of untracked files, and the reference publish
leaving the ordinary build intact. A property proven once is a property that
regresses silently.

These tests run the real mechanisms, and none of them writes into the
repository they run in. The build-isolation case performs the complete cycle --
both packs, both publishes, and an ordinary build afterwards -- because the
property under test is whether the reference publish leaves that build intact,
and only running it answers that. Its cost is the reason it is the one case
gated on a restore being available.
"""

from __future__ import annotations

import io
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
BENCHMARK_SCRIPT = REPOSITORY_ROOT / "eng" / "performance" / "benchmark.sh"
PAIRED_SCRIPT = REPOSITORY_ROOT / "eng" / "performance" / "paired-benchmark.sh"
CONTRACT_PATH = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"
BENCHMARK_PROJECT = (
    REPOSITORY_ROOT
    / "benchmarks"
    / "Doka.EntityFrameworkCore.MySql.Benchmarks"
    / "Doka.EntityFrameworkCore.MySql.Benchmarks.csproj"
)


class OuterDeadlineClassificationTests(unittest.TestCase):
    """Prove a run the clock cut short stays retryable.

    The deadline helper reports its own timeout as 124. The attempt recorder
    does not know that code and files it as invalid evidence, which no retry
    can clear -- so a slow runner would end a release the same way a genuine
    regression does.
    """

    def test_the_helper_reports_its_own_timeout_code(self) -> None:
        """Establish the code the translation has to catch."""
        result = subprocess.run(
            [
                "python3", "-m", "eng.common.deadline",
                "--seconds", "1",
                "--label", "probe",
                "--", "sleep", "30",
            ],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
        )

        self.assertEqual(124, result.returncode)

    def test_the_benchmark_entry_point_translates_it(self) -> None:
        """Run the real entry point against a budget it cannot meet.

        The contract is copied to a temporary tree, its paired budget set to
        one second, and the copy handed to the entry point by path. Overwriting
        the repository's own contract for the duration of a subprocess left the
        checkout damaged for anything that read it concurrently, and for good
        if the process was killed.
        """
        with tempfile.TemporaryDirectory() as directory:
            contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
            contract["pairedPolicy"]["durations"]["maximumPairedRunSeconds"] = 1
            replacement = Path(directory) / "performance-contract.json"
            replacement.write_text(json.dumps(contract), encoding="utf-8")

            result = subprocess.run(
                ["bash", "eng/benchmark.sh", "--test-only"],
                cwd=REPOSITORY_ROOT,
                capture_output=True,
                text=True,
                env=dict(
                    os.environ,
                    DOKA_BENCHMARK_TARGET="mariadb118",
                    DOKA_BENCHMARK_PROFILE="smoke",
                    DOKA_BENCHMARK_COMPARISON_MODE="paired",
                    DOKA_BENCHMARK_CONTRACT_PATH=str(replacement),
                ),
            )

        self.assertEqual(75, result.returncode, result.stderr[-400:])

    def test_that_code_classifies_as_a_retryable_measurement_condition(self) -> None:
        """State what the translation buys."""
        from eng.performance import attempts

        self.assertEqual("invalid-evidence", attempts.classify_exit_code(124))
        self.assertEqual("measurement-inconclusive", attempts.classify_exit_code(75))
        self.assertTrue(attempts.is_retryable("measurement-inconclusive"))


class DriverHashBindsUntrackedContentTests(unittest.TestCase):
    """Prove a new file's contents reach the recorded driver identity.

    Untracked files never appear in a diff, so folding in `git status` alone
    binds their names and not their bytes: two runs with the same new file name
    and different contents would claim the same driver.

    Every case runs in a repository this test creates. An earlier version wrote
    its probes into the checkout it was running in and restored them in a
    `finally`, which survives an assertion failure and nothing else: a kill, an
    interpreter crash, a parallel reader, or a benchmark running beside it all
    see the damaged tree. The clean-tree case was worse than unreliable -- it
    read whatever state the checkout happened to be in, so on a dirty tree it
    silently tested the dirty path twice.
    """

    def setUp(self) -> None:
        """Create a repository with a committed benchmarks tree."""
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.tracked = self.root / "benchmarks" / "performance-contract.json"
        self.tracked.parent.mkdir(parents=True)
        self.tracked.write_text('{"probe": true}\n', encoding="utf-8")
        self.untracked = self.root / "benchmarks" / "probe.txt"

        self.git("init", "--quiet")
        self.git("add", "benchmarks")
        self.git(
            "-c", "user.email=probe@example.invalid",
            "-c", "user.name=probe",
            "-c", "commit.gpgsign=false",
            "commit", "--quiet", "--message", "probe",
        )

    def git(self, *arguments: str) -> None:
        """Run one git command inside the temporary repository."""
        subprocess.run(
            ["git", "-C", str(self.root), *arguments],
            check=True,
            capture_output=True,
            text=True,
        )

    def hash_expression(self) -> str:
        """Return the digest the runner computes for a dirty worktree."""
        body = PAIRED_SCRIPT.read_text(encoding="utf-8")
        start = body.index("benchmarks_tree_hash() {")
        # The closing brace belongs to the extract: slicing to the index of the
        # newline before it would hand the shell an unterminated function.
        end = body.index("\n}\n", start) + len("\n}")

        return body[start:end]

    def compute(self) -> str:
        """Run the runner's own expression against the temporary repository."""
        script = self.hash_expression() + '\nbenchmarks_tree_hash "$PWD/benchmarks/x"\n'
        result = subprocess.run(
            ["bash", "-c", script],
            cwd=self.root,
            capture_output=True,
            text=True,
            check=True,
        )

        return result.stdout.strip()

    def accepted(self, value: str) -> bool:
        """Whether the evaluator's provenance check accepts this value."""
        from eng.performance.paired import _DIGEST_SHAPE

        return bool(_DIGEST_SHAPE.fullmatch(value))

    def test_a_clean_tree_yields_an_accepted_identifier(self) -> None:
        """Prove the committed-tree path produces what the evaluator takes.

        The repository is clean by construction here, which is what makes this
        a test of the committed-tree path rather than of whichever state a
        developer's checkout happened to be in.
        """
        observed = self.compute()

        self.assertTrue(self.accepted(observed), observed)

    def test_every_dirty_shape_yields_an_accepted_identifier(self) -> None:
        """Prove the producer and the evaluator agree on the working-tree form.

        They did not: the producer emitted `worktree-` plus sixteen hex
        characters while the evaluator required forty or sixty-four. A local
        paired run would have measured for an hour and then discarded the
        evidence it had just produced.
        """
        with self.subTest(shape="modified tracked file"):
            self.tracked.write_text('{"probe": false}\n', encoding="utf-8")
            observed = self.compute()
            self.assertTrue(self.accepted(observed), observed)
        self.tracked.write_text('{"probe": true}\n', encoding="utf-8")

        with self.subTest(shape="new untracked file"):
            self.untracked.write_text("probe\n", encoding="utf-8")
            observed = self.compute()
            self.assertTrue(self.accepted(observed), observed)

    def test_changing_an_untracked_file_changes_the_hash(self) -> None:
        """Reject a digest that only sees the file's name."""
        self.untracked.write_text("content A\n", encoding="utf-8")
        first = self.compute()
        self.untracked.write_text("content B, same name\n", encoding="utf-8")
        second = self.compute()

        self.assertNotEqual(first, second)
        for observed in (first, second):
            self.assertTrue(self.accepted(observed), observed)

    def test_a_dirty_tree_differs_from_the_committed_one(self) -> None:
        """Never present a working-tree build as the committed tree."""
        clean = self.compute()
        self.untracked.write_text("probe\n", encoding="utf-8")
        dirty = self.compute()

        self.assertNotEqual(clean, dirty)


class RepositoryIsolationTests(unittest.TestCase):
    """Prove the runtime guards leave the checkout they run in untouched.

    The guards used to write their probes into this repository and undo them in
    a `finally`. That covers an assertion failure and nothing else, so this
    checks the property directly -- including along a path where a case fails
    partway through, which is exactly when a restore-on-exit is least
    trustworthy.
    """

    def status(self) -> str:
        """Return the full working-tree status of this repository."""
        result = subprocess.run(
            ["git", "status", "--porcelain", "--untracked-files=all"],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=True,
        )

        return result.stdout

    def test_the_guards_leave_no_trace_even_when_one_fails(self) -> None:
        """Run the driver-hash guards, and a failing one, over this checkout."""
        class FailingProbe(DriverHashBindsUntrackedContentTests):
            """Damage its own repository and fail before any cleanup could run."""

            def test_a_clean_tree_yields_an_accepted_identifier(self) -> None:
                """Write probes and then fail outright."""
                self.untracked.write_text("probe\n", encoding="utf-8")
                self.tracked.write_text("damaged\n", encoding="utf-8")
                self.fail("deliberate failure, to test the isolation")

        loader = unittest.TestLoader()
        suite = unittest.TestSuite()
        suite.addTests(
            loader.loadTestsFromTestCase(DriverHashBindsUntrackedContentTests)
        )
        suite.addTest(
            FailingProbe("test_a_clean_tree_yields_an_accepted_identifier")
        )

        before = self.status()
        result = unittest.TextTestRunner(
            stream=io.StringIO(), verbosity=0
        ).run(suite)

        self.assertEqual(1, len(result.failures), result.failures)
        self.assertEqual([], result.errors)
        self.assertEqual(before, self.status())


class ReferenceBuildIsolationTests(unittest.TestCase):
    """Prove a paired comparison cannot break the next ordinary build.

    Both sides publish the same project. Sharing one intermediate tree let the
    reference restore leave package references behind that the next ordinary
    build imported alongside the project references, failing with CS1704 until
    someone restored again.
    """

    def setUp(self) -> None:
        """Read the runner."""
        self.runner = PAIRED_SCRIPT.read_text(encoding="utf-8")

    def test_the_reference_version_carries_its_commit(self) -> None:
        """Keep the package cache from serving an earlier reference revision."""
        self.assertIn(
            'reference_version="0.0.0-paired-${reference_commit:0:12}"', self.runner
        )

    def test_the_reference_provenance_is_verified(self) -> None:
        """Prove the reference side ran the provider this run packed.

        The local feed is added to the configured sources rather than replacing
        them, so a restore could resolve elsewhere. Comparing the published
        assembly against the packed one is what closes that.
        """
        self.assertIn("did not publish the ${assembly} this run packed", self.runner)

    def test_the_isolation_does_not_use_a_global_intermediate_path(self) -> None:
        """Reject the redirect that broke the candidate publish.

        `BaseIntermediateOutputPath` is a global property: it reaches the
        benchmark project and every project reference it pulls in, collapsing
        the provider, the spatial package, and the driver into one intermediate
        directory whose restore output overwrites itself. The candidate publish
        then fails with CS0246 on types the provider's own references define.
        """
        # The usage form, not the bare name: the runner names both properties
        # in the comment that explains why it does not pass them.
        for forbidden in ("BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath"):
            with self.subTest(property=forbidden):
                self.assertNotIn(f"-p:{forbidden}", self.runner)

        for side in ("candidate", "reference"):
            with self.subTest(side=side):
                self.assertIn(
                    f'-p:ArtifactsPath="${{{side}_artifacts}}"', self.runner
                )

    def test_the_repository_separates_artifacts_by_project(self) -> None:
        """State why an artifacts root is a safe global property.

        It is only safe because the repository already asks for the project
        name in the path; without that, per-side roots would collide exactly
        the way the intermediate paths did.
        """
        props = (REPOSITORY_ROOT / "Directory.Build.props").read_text(encoding="utf-8")

        self.assertIn("<UseArtifactsOutput>true</UseArtifactsOutput>", props)
        self.assertIn(
            "<IncludeProjectNameInArtifactsPaths>true</IncludeProjectNameInArtifactsPaths>",
            props,
        )

    @unittest.skipIf(shutil.which("dotnet") is None, "the .NET SDK is unavailable")
    def test_the_full_cycle_leaves_the_ordinary_build_green(self) -> None:
        """Run what a paired comparison actually builds, in order.

        Reading the project file cannot see this: the ordinary build never
        takes the packaged-provider path, and the previous isolation passed
        every text assertion while making the candidate publish fail. This runs
        in the ordinary suite rather than behind an opt-in flag, because a
        check nothing sets is the same as no check -- and it is the only one
        that executes the thing under test.
        """
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            feed = root / "feed"
            feed.mkdir()
            version = "0.0.0-paired-cycle-probe"

            for project in (
                "Doka.EntityFrameworkCore.MySql",
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
            ):
                self.run_dotnet(
                    "pack", f"src/{project}/{project}.csproj",
                    "--configuration", "Release", "--tl:off",
                    f"-p:Version={version}", "--output", str(feed),
                )

            self.run_dotnet(
                "publish", str(BENCHMARK_PROJECT),
                "--configuration", "Release", "--tl:off",
                "--output", str(root / "candidate"),
                f"-p:ArtifactsPath={root / 'artifacts-candidate'}",
            )
            self.run_dotnet(
                "publish", str(BENCHMARK_PROJECT),
                "--configuration", "Release", "--tl:off",
                "--output", str(root / "reference"),
                f"-p:DokaBenchmarkProviderVersion={version}",
                f"-p:RestoreAdditionalProjectSources={feed}",
                f"-p:ArtifactsPath={root / 'artifacts-reference'}",
            )

            for assembly in (
                "Doka.EntityFrameworkCore.MySql",
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
            ):
                with self.subTest(assembly=assembly):
                    self.assertTrue(
                        (root / "reference" / f"{assembly}.dll").is_file()
                    )

            # The property the earlier isolation was written for, and the one
            # its replacement must keep: an ordinary build that restores
            # nothing still succeeds after a paired comparison.
            self.run_dotnet(
                "build", str(BENCHMARK_PROJECT),
                "--configuration", "Release", "--tl:off", "--no-restore",
            )

    def run_dotnet(self, *arguments: str) -> None:
        """Run one dotnet command and fail with its output."""
        result = subprocess.run(
            ["dotnet", *arguments],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(
            0, result.returncode, f"dotnet {arguments[0]} failed:\n{result.stdout[-2000:]}"
        )


if __name__ == "__main__":
    unittest.main()
