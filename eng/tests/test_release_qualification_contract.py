"""Run the qualification chain end to end on synthetic evidence.

Every other module here tests one function against inputs it was handed. That
leaves the seams untested, and the seams are where this has failed: a derived
result whose field names the selector does not read, a manifest whose inventory
covers a tree the verifier is never pointed at, a gate declared in the policy
that nothing produces.

These tests build a complete run on disk -- stage receipts, resolved artifacts,
paired evaluations, packages -- then drive derivation, assembly, and
verification through their real entry points. A break anywhere along the chain
surfaces here rather than in a release.
"""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any

from eng.release import gate_results, qualification


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
POLICY_PATH = REPOSITORY_ROOT / "eng" / "release" / "evidence-policy.json"
REPOSITORY = "kdominic89/Doka.EntityFrameworkCore.MySql"
RELEASE_TAG = "v10.0.0-contract"
RELEASE_VERSION = "10.0.0-contract"
WORKFLOW_RUN_ID = 4242
RUN_ATTEMPT = 1

STAGE_DIRECTORIES = {
    "migration-deployment": "migration-deployment",
    "runtime": "runtime",
    "efcore-patch-matrix": "efcore-patch-matrix",
    "mysqlconnector-patch-matrix": "mysqlconnector-patch-matrix",
}

DEPENDENCY_STAGES = ("efcore-patch-matrix", "mysqlconnector-patch-matrix")
DEPENDENCY_LEGS = ("minimum", "latest")


class QualificationChain:
    """Materialize one complete release run under a temporary root."""

    def __init__(self, root: Path, commit: str, tree: str) -> None:
        """Lay out the directories the chain reads from."""
        self.root = root
        self.commit = commit
        self.tree = tree
        self.evidence_root = root / "release-candidate"
        self.checkpoints = root / "checkpoints"
        self.selections = self.evidence_root / "artifact-selections"
        self.packages = self.evidence_root / "packages"
        for directory in (
            self.evidence_root,
            self.checkpoints,
            self.selections,
            self.packages,
        ):
            directory.mkdir(parents=True, exist_ok=True)

    def write_stage(self, stage: str) -> None:
        """Write one stage receipt plus the evidence it claims to have made."""
        (self.checkpoints / f"{stage}.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 3,
                    "kind": "release-stage-checkpoint",
                    "runId": f"github-{WORKFLOW_RUN_ID}",
                    "stage": stage,
                    "sourceCommit": self.commit,
                    "sourceRef": "refs/heads/main",
                    "expectedReleaseTag": RELEASE_TAG,
                    "runAttempt": RUN_ATTEMPT,
                }
            ),
            encoding="utf-8",
        )
        directory = self.evidence_root / STAGE_DIRECTORIES[stage]
        directory.mkdir(parents=True, exist_ok=True)
        (directory / f"{stage}-evidence.json").write_text(
            json.dumps({"stage": stage, "commit": self.commit}), encoding="utf-8"
        )
        if stage in DEPENDENCY_STAGES:
            # The real matrix gates run several legs, one per pinned dependency
            # version, and each leg writes its own resolved graph into its own
            # directory. The fixture mirrors that layout, because a derivation
            # that only looked one level deep would pass against a flat one.
            for leg in DEPENDENCY_LEGS:
                leg_directory = directory / leg
                leg_directory.mkdir(parents=True, exist_ok=True)
                (leg_directory / "resolved-packages.json").write_text(
                    json.dumps({"projects": [], "stage": stage, "leg": leg}),
                    encoding="utf-8",
                )

    def write_selection(self) -> Path:
        """Write the resolved artifact identities for every stage."""
        path = self.selections / "assemble-input-artifacts.json"
        path.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "workflowRunId": WORKFLOW_RUN_ID,
                    "maximumRunAttempt": RUN_ATTEMPT,
                    "artifacts": [
                        {
                            "stage": stage,
                            "attempt": RUN_ATTEMPT,
                            "id": 9000 + index,
                            "name": f"release-stage-{stage}-attempt-1",
                            "sha256": hashlib.sha256(stage.encode()).hexdigest(),
                        }
                        for index, stage in enumerate(sorted(STAGE_DIRECTORIES))
                    ],
                }
            ),
            encoding="utf-8",
        )

        return path

    def write_packages(self) -> None:
        """Write the payload the manifest inventory binds."""
        for name in ("Doka.EntityFrameworkCore.MySql.10.0.0.nupkg",
                     "Doka.EntityFrameworkCore.MySql.NetTopologySuite.10.0.0.nupkg"):
            (self.packages / name).write_bytes(name.encode("utf-8"))

    def complete(self) -> None:
        """Produce a run in which every gate has succeeded."""
        for stage in STAGE_DIRECTORIES:
            self.write_stage(stage)
        self.write_selection()
        self.write_packages()


def fake_check_receipt(commit: str) -> dict[str, Any]:
    """Return the resolved protected-check receipt the forge would produce."""
    return {
        "name": "repository-qualification",
        "id": 5001,
        "checkSuiteId": 7001,
        "conclusion": "success",
        "workflowPath": ".github/workflows/ci.yml",
        "workflowRunId": 3131,
        "runAttempt": 1,
        "event": "push",
        "headBranch": "main",
        "commit": commit,
        "workflowConclusion": "success",
    }


class ReleaseQualificationChainTests(unittest.TestCase):
    """Prove derivation, assembly, and verification agree with each other."""

    def setUp(self) -> None:
        """Build a git repository and a complete synthetic run inside it."""
        self.directory = tempfile.TemporaryDirectory()
        root = Path(self.directory.name)
        self.repo = root / "repository"
        self.repo.mkdir()

        def git(*arguments: str) -> str:
            return subprocess.run(
                ["git", "-C", str(self.repo), *arguments],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip()

        git("init", "--initial-branch=main")
        git("config", "user.email", "probe@example.test")
        git("config", "user.name", "Probe")
        (self.repo / "README.md").write_text("probe\n", encoding="utf-8")
        git("add", "README.md")
        git("-c", "commit.gpgsign=false", "commit", "-m", "probe")
        self.commit = git("rev-parse", "HEAD")
        self.tree = git("rev-parse", "HEAD^{tree}")

        self.chain = QualificationChain(root / "run", self.commit, self.tree)
        self.policy = qualification.load_policy(POLICY_PATH)

        # The forge is the one input this test cannot produce, so it is
        # replaced at the single seam that reaches it. Everything else is real.
        self.original_fetch = gate_results.fetch_qualification_receipt
        gate_results.fetch_qualification_receipt = (
            lambda repository, commit, check_name: fake_check_receipt(commit)
        )

    def tearDown(self) -> None:
        """Restore the forge seam and release the fixture."""
        gate_results.fetch_qualification_receipt = self.original_fetch
        self.directory.cleanup()

    def derive(self) -> list[dict[str, Any]]:
        """Run derivation through its real entry point."""
        output = Path(self.directory.name) / "gate-results.json"
        exit_code = gate_results.main(
            [
                "derive",
                "--repo", str(self.repo),
                "--repository", REPOSITORY,
                "--commit", self.commit,
                "--selection", str(self.chain.selections / "assemble-input-artifacts.json"),
                "--checkpoint-directory", str(self.chain.checkpoints),
                "--evidence-root", str(self.chain.evidence_root),
                "--assembling-attempt", str(RUN_ATTEMPT),
                "--policy", str(POLICY_PATH),
                "--output", str(output),
            ]
        )
        self.assertEqual(0, exit_code)

        return json.loads(output.read_text(encoding="utf-8"))

    def assemble(self) -> Path:
        """Run assembly through its real entry point."""
        output = Path(self.directory.name) / "manifest.json"
        results = Path(self.directory.name) / "gate-results.json"
        exit_code = qualification.main(
            [
                "assemble",
                "--result", str(results),
                "--commit", self.commit,
                "--tree-id", self.tree,
                "--repository", REPOSITORY,
                "--expected-release-tag", RELEASE_TAG,
                "--release-version", RELEASE_VERSION,
                "--assembling-attempt", str(RUN_ATTEMPT),
                "--root", str(self.chain.packages),
                "--policy", str(POLICY_PATH),
                "--output", str(output),
            ]
        )
        self.assertEqual(0, exit_code)

        return output

    def verify(self, manifest: Path, *, root: Path | None = None) -> int:
        """Run verification through its real entry point."""
        arguments = [
            "verify",
            "--manifest", str(manifest),
            "--policy", str(POLICY_PATH),
            "--expected-repository", REPOSITORY,
            "--expected-commit", self.commit,
            "--expected-tree-id", self.tree,
            "--expected-release-tag", RELEASE_TAG,
        ]
        if root is not None:
            arguments += ["--root", str(root)]

        return qualification.main(arguments)

    def test_a_complete_run_derives_assembles_and_verifies(self) -> None:
        """Walk the whole chain once and let every seam prove itself."""
        self.chain.complete()

        results = self.derive()
        self.assertEqual(
            sorted(gate["id"] for gate in self.policy["gates"]),
            sorted(result["gate"] for result in results),
        )

        manifest = self.assemble()
        self.assertEqual(0, self.verify(manifest, root=self.chain.packages))

    def test_every_declared_gate_has_a_producer(self) -> None:
        """Reject a policy that declares a gate nothing in this run produces.

        A gate added to the policy without a producer would make every release
        unassemblable. Failing here costs a test run; failing in the release
        costs a version number.
        """
        self.chain.complete()

        produced = {result["gate"] for result in self.derive()}

        self.assertEqual({gate["id"] for gate in self.policy["gates"]}, produced)

    def test_every_bound_identity_is_actually_carried(self) -> None:
        """Prove derivation fills every identity the policy binds.

        The manifest raises on a missing identity, so this asserts against the
        derived results directly: a field the policy names and derivation never
        writes would otherwise only surface at assembly time in a release.
        """
        self.chain.complete()
        results = {result["gate"]: result for result in self.derive()}

        for gate in self.policy["gates"]:
            for field in gate["boundIdentities"]:
                with self.subTest(gate=gate["id"], field=field):
                    self.assertIn(field, results[gate["id"]])
                    self.assertNotIn(results[gate["id"]][field], (None, ""))

    def test_a_missing_stage_receipt_stops_the_chain(self) -> None:
        """Refuse to describe a gate that left no receipt."""
        self.chain.complete()
        (self.chain.checkpoints / "runtime.json").unlink()

        with self.assertRaises((OSError, SystemExit)) as captured:
            result = gate_results.main(
                [
                    "derive",
                    "--repo", str(self.repo),
                    "--repository", REPOSITORY,
                    "--commit", self.commit,
                    "--selection",
                    str(self.chain.selections / "assemble-input-artifacts.json"),
                    "--checkpoint-directory", str(self.chain.checkpoints),
                    "--evidence-root", str(self.chain.evidence_root),
                    "--assembling-attempt", str(RUN_ATTEMPT),
                    "--policy", str(POLICY_PATH),
                    "--output", str(Path(self.directory.name) / "out.json"),
                ]
            )
            self.assertEqual(1, result)
            raise SystemExit(1)

        self.assertTrue(captured.exception)

    def test_a_receipt_for_another_commit_stops_the_chain(self) -> None:
        """Refuse evidence produced for a different commit."""
        self.chain.complete()
        path = self.chain.checkpoints / "runtime.json"
        receipt = json.loads(path.read_text(encoding="utf-8"))
        receipt["sourceCommit"] = "9" * 40
        path.write_text(json.dumps(receipt), encoding="utf-8")

        self.assertEqual(1, self._derive_exit_code())

    def test_a_missing_dependency_snapshot_stops_the_chain(self) -> None:
        """Refuse a floating-dependency gate that recorded no resolved graph."""
        self.chain.complete()
        for leg in DEPENDENCY_LEGS:
            (self.chain.evidence_root / "efcore-patch-matrix" / leg
             / "resolved-packages.json").unlink()

        self.assertEqual(1, self._derive_exit_code())

    def test_every_dependency_leg_reaches_the_gate_identity(self) -> None:
        """Prove the identity spans every leg, not whichever one was found first.

        Digesting one leg would describe a fraction of what the gate proved: a
        rerun on a day the upstream published a new patch for the other leg
        would produce the same identity for different evidence.
        """
        self.chain.complete()
        first = {result["gate"]: result for result in self.derive()}

        leg = self.chain.evidence_root / "efcore-patch-matrix" / DEPENDENCY_LEGS[-1]
        (leg / "resolved-packages.json").write_text(
            json.dumps({"projects": [{"changed": True}]}), encoding="utf-8"
        )
        second = {result["gate"]: result for result in self.derive()}

        self.assertEqual(
            len(DEPENDENCY_LEGS),
            first["efcore-patch-matrix"]["dependencySnapshotCount"],
        )
        self.assertNotEqual(
            first["efcore-patch-matrix"]["dependencySnapshotDigest"],
            second["efcore-patch-matrix"]["dependencySnapshotDigest"],
        )

    def test_a_tampered_package_fails_verification(self) -> None:
        """Prove the inventory digests are load-bearing at publication.

        This is the case `--root` exists for: without it the manifest verifies
        against itself while the bytes it describes have changed.
        """
        self.chain.complete()
        self.derive()
        manifest = self.assemble()

        target = next(self.chain.packages.iterdir())
        target.write_bytes(target.read_bytes() + b"tampered")

        self.assertEqual(1, self.verify(manifest, root=self.chain.packages))
        # Without the payload root the same tampering goes unnoticed, which is
        # exactly why the publication step must pass it.
        self.assertEqual(0, self.verify(manifest))

    def test_an_added_package_fails_verification(self) -> None:
        """Reject a payload that grew a file the candidate never qualified."""
        self.chain.complete()
        self.derive()
        manifest = self.assemble()

        (self.chain.packages / "extra.nupkg").write_bytes(b"extra")

        self.assertEqual(1, self.verify(manifest, root=self.chain.packages))

    def test_a_manifest_for_another_tag_fails_verification(self) -> None:
        """Reject a manifest that verifies internally but names another release."""
        self.chain.complete()
        self.derive()
        manifest = self.assemble()
        document = json.loads(manifest.read_text(encoding="utf-8"))
        document["expectedReleaseTag"] = "v9.9.9"
        manifest.write_text(json.dumps(document), encoding="utf-8")

        self.assertEqual(1, self.verify(manifest))

    def test_a_duplicated_gate_fails_verification(self) -> None:
        """Reject a manifest that pins one gate twice.

        Two entries for one gate can name two different runs, and a consumer
        reading the first would disagree with one reading the last.
        """
        self.chain.complete()
        self.derive()
        manifest = self.assemble()
        document = json.loads(manifest.read_text(encoding="utf-8"))
        document["gates"].append(dict(document["gates"][0]))
        manifest.write_text(json.dumps(document), encoding="utf-8")

        self.assertEqual(1, self.verify(manifest))

    def test_a_gate_pinned_under_the_wrong_kind_fails_verification(self) -> None:
        """Reject a candidate-produced gate presented as a protected check."""
        self.chain.complete()
        self.derive()
        manifest = self.assemble()
        document = json.loads(manifest.read_text(encoding="utf-8"))
        for entry in document["gates"]:
            if entry["kind"] == "candidate-produced":
                entry["kind"] = "protected-check"
                break
        manifest.write_text(json.dumps(document), encoding="utf-8")

        self.assertEqual(1, self.verify(manifest))

    def test_a_gate_pinning_another_commit_fails_verification(self) -> None:
        """Reject a manifest whose gates do not all describe the candidate commit."""
        self.chain.complete()
        self.derive()
        manifest = self.assemble()
        document = json.loads(manifest.read_text(encoding="utf-8"))
        document["gates"][0]["commit"] = "7" * 40
        manifest.write_text(json.dumps(document), encoding="utf-8")

        self.assertEqual(1, self.verify(manifest))

    def test_a_dropped_gate_fails_verification(self) -> None:
        """Reject a manifest that quietly omits a required gate."""
        self.chain.complete()
        self.derive()
        manifest = self.assemble()
        document = json.loads(manifest.read_text(encoding="utf-8"))
        document["gates"].pop()
        manifest.write_text(json.dumps(document), encoding="utf-8")

        self.assertEqual(1, self.verify(manifest))

    def _derive_exit_code(self) -> int:
        """Run derivation and return its exit code without asserting success."""
        return gate_results.main(
            [
                "derive",
                "--repo", str(self.repo),
                "--repository", REPOSITORY,
                "--commit", self.commit,
                "--selection",
                str(self.chain.selections / "assemble-input-artifacts.json"),
                "--checkpoint-directory", str(self.chain.checkpoints),
                "--evidence-root", str(self.chain.evidence_root),
                "--assembling-attempt", str(RUN_ATTEMPT),
                "--policy", str(POLICY_PATH),
                "--output", str(Path(self.directory.name) / "out.json"),
            ]
        )


if __name__ == "__main__":
    sys.exit(unittest.main())
