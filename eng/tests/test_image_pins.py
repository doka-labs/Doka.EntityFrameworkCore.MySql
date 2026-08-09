"""Contract tests for the database image pins and the gate that holds them.

Dependabot only edits the Compose stack, while the same pin also lives in two
workflows, the performance contract, and a C# constant. Without a gate, an
accepted image update leaves those behind and a candidate measures one engine
version while claiming another.

The gate has to be closed on both sides, which is what most of these cases are
about. Comparing only the digest pins it can find would let a reference that
lost its digest disappear from the comparison instead of failing it, and a
count that is merely "at least one" hides an occurrence going missing behind
its siblings.
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
GATE = REPOSITORY_ROOT / "eng" / "quality" / "check-image-pins.py"

COMPOSE = "docker/compose.yml"
CSHARP = "tests/Doka.EntityFrameworkCore.MySql.TestUtilities/TestDatabaseImages.cs"
CI = ".github/workflows/ci.yml"
SCORECARD = ".github/workflows/benchmark-scorecard.yml"
CONTRACT = "benchmarks/performance-contract.json"
MIRRORS = (CI, SCORECARD, CONTRACT, CSHARP)

TARGETS = ("mysql84", "mariadb114", "mariadb118")
DIGEST_PIN = re.compile(r"(?:mysql|mariadb):[0-9][0-9A-Za-z.-]*@sha256:[0-9a-f]{64}")


def a_different_pin(pin: str) -> str:
    """Return a complete, well-formed pin that differs from the given one.

    Derived rather than written down: naming a patch version would stop
    matching after the next accepted update, and the mutation would quietly
    become a no-op that still passes.
    """
    return pin[:-1] + ("0" if pin[-1] != "0" else "1")


def compose_pin(target: str) -> str:
    """Return the pin the Compose stack currently declares for one target.

    Read rather than duplicated: a copy here would fall behind the next
    accepted image update, and the negative cases would then mutate a string
    the files no longer contain. They would still pass, having changed nothing.
    """
    text = (REPOSITORY_ROOT / COMPOSE).read_text(encoding="utf-8")
    section = re.search(
        rf"^  {re.escape(target)}:\n(?:.*\n)*?\s+image:\s*(?P<image>\S+)\s*$",
        text,
        re.MULTILINE,
    )
    if section is None:
        raise AssertionError(f"{COMPOSE} declares no image for '{target}'.")

    return section.group("image")


class ImagePinContractTests(unittest.TestCase):
    """Prove the checked-in matrix is complete, pinned, and consistent."""

    def test_every_required_target_is_pinned_by_digest(self) -> None:
        """Reject a target whose image could move under a floating tag."""
        compose = (REPOSITORY_ROOT / COMPOSE).read_text(encoding="utf-8")
        contract = json.loads((REPOSITORY_ROOT / CONTRACT).read_text(encoding="utf-8"))

        for target in TARGETS:
            with self.subTest(target=target):
                self.assertIn(f"  {target}:", compose)

        for target, definition in contract["requiredTargets"].items():
            with self.subTest(target=target):
                self.assertRegex(definition["serverImage"], DIGEST_PIN)

    def test_the_repository_passes_its_own_pin_gate(self) -> None:
        """Keep the checked-in copies in agreement with the Compose stack."""
        result = subprocess.run(
            [sys.executable, str(GATE), "--repo", str(REPOSITORY_ROOT)],
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, result.returncode, result.stderr)


class ImagePinDriftTests(unittest.TestCase):
    """Prove the gate rejects every way a copy can depart from the source."""

    def setUp(self) -> None:
        """Copy the files the gate reads into a throwaway repository."""
        self._directory = tempfile.TemporaryDirectory(prefix="doka-pins-")
        self.root = Path(self._directory.name)
        self.addCleanup(self._directory.cleanup)

        for relative in (COMPOSE, *MIRRORS):
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(REPOSITORY_ROOT / relative, target)

    def run_gate(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        """Run the gate against the throwaway repository."""
        return subprocess.run(
            [sys.executable, str(GATE), "--repo", str(self.root), *arguments],
            capture_output=True,
            text=True,
            check=False,
        )

    def swap_two_pins(self, relative: str, first: str, second: str) -> None:
        """Exchange two complete pins inside one copy."""
        path = self.root / relative
        text = path.read_text(encoding="utf-8")
        path.write_text(
            text.replace(first, "\0").replace(second, first).replace("\0", second),
            encoding="utf-8",
        )

    def edit(self, relative: str, old: str, new: str, count: int = -1) -> None:
        """Replace text in one copy, and fail if there was nothing to replace.

        A negative case that mutates nothing still passes: the gate rejects the
        unchanged fixture for no reason, or accepts it and the case proves the
        opposite of what it claims. Every mutation here therefore has to land.
        """
        path = self.root / relative
        text = path.read_text(encoding="utf-8")
        if old not in text:
            raise AssertionError(f"{relative} does not contain {old!r} to replace.")

        path.write_text(text.replace(old, new, count), encoding="utf-8")

    def assertRejected(self, expected: str) -> None:
        """Assert the gate fails and says why."""
        result = self.run_gate()
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn(expected, result.stderr)

    def test_the_unchanged_copy_set_passes(self) -> None:
        """Establish the fixture is clean before drift is injected."""
        result = self.run_gate()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_a_different_digest_pin_is_rejected(self) -> None:
        """Reject a copy left on the version the Compose stack moved off."""
        for relative in MIRRORS:
            with self.subTest(mirror=relative):
                self.setUp()
                current = compose_pin("mysql84")
                self.edit(relative, current, a_different_pin(current))
                self.assertRejected("disagree with the Compose stack")

    def test_a_reference_without_a_digest_is_rejected(self) -> None:
        """Reject a pin that lost its digest and could move under its tag.

        This is what a gate comparing only well-formed pins cannot see: the
        reference stops matching and drops out of the comparison entirely.
        """
        current = compose_pin("mysql84")
        self.edit(CI, current, current.split("@")[0], 1)
        self.assertRejected("without a digest")

    def test_a_removed_occurrence_is_rejected(self) -> None:
        """Reject one occurrence going missing while its siblings remain."""
        self.edit(CI, compose_pin("mysql84"), "postgres:17", 1)
        self.assertRejected("expected 3")

    def test_an_added_occurrence_is_rejected(self) -> None:
        """Reject an occurrence the contract does not account for."""
        lines = (self.root / CI).read_text(encoding="utf-8").splitlines(keepends=True)
        for index, line in enumerate(lines):
            if compose_pin("mariadb118") in line:
                lines.insert(index + 1, line)
                break
        (self.root / CI).write_text("".join(lines), encoding="utf-8")

        self.assertRejected("expected 1")

    def test_an_unpinned_engine_reference_is_rejected(self) -> None:
        """Reject an engine image the Compose stack does not declare."""
        self.edit(CI, compose_pin("mariadb118"), "mariadb:11.9.0@sha256:" + "b" * 64, 1)
        self.assertRejected("does not pin")

    def test_a_missing_csharp_constant_is_rejected(self) -> None:
        """Reject a constant disappearing from the test infrastructure."""
        path = self.root / CSHARP
        path.write_text(
            re.sub(
                r"    public const string MariaDb114 =\n[^;]+;\n\n",
                "",
                path.read_text(encoding="utf-8"),
            ),
            encoding="utf-8",
        )

        self.assertRejected("MariaDb114")

    def test_a_missing_contract_target_is_rejected(self) -> None:
        """Reject a required target losing its server image."""
        path = self.root / CONTRACT
        contract = json.loads(path.read_text(encoding="utf-8"))
        contract["requiredTargets"].pop("mysql84")
        path.write_text(json.dumps(contract, indent=2), encoding="utf-8")

        self.assertRejected("mysql84")

    def test_a_compose_stack_without_a_digest_is_rejected(self) -> None:
        """Refuse a source pin that could move under a floating tag."""
        current = compose_pin("mysql84")
        self.edit(COMPOSE, current, current.split("@")[0])
        self.assertRejected("carries no digest")

    def test_a_missing_mirror_is_rejected(self) -> None:
        """Report an absent copy instead of passing over it."""
        (self.root / CSHARP).unlink()
        self.assertRejected("is missing")

    def test_swapped_csharp_pins_are_rejected(self) -> None:
        """Reject two constants that hold each other's engine.

        Every pin stays complete and every count stays right, so only a
        target-by-target comparison catches it. Callers would silently reach
        the other engine.
        """
        self.swap_two_pins(CSHARP, compose_pin("mysql84"), compose_pin("mariadb118"))
        self.assertRejected("but docker/compose.yml pins")

    def test_swapped_contract_pins_are_rejected(self) -> None:
        """Reject required targets that hold each other's engine image."""
        path = self.root / CONTRACT
        contract = json.loads(path.read_text(encoding="utf-8"))
        required = contract["requiredTargets"]
        required["mysql84"]["serverImage"] = compose_pin("mariadb118")
        required["mariadb118"]["serverImage"] = compose_pin("mysql84")
        path.write_text(json.dumps(contract, indent=2), encoding="utf-8")

        self.assertRejected("but docker/compose.yml pins")

    def test_a_malformed_digest_is_rejected(self) -> None:
        """Reject every digest that is not exactly sixty-four lowercase hex.

        A pattern that only matched well-formed pins would stop matching here
        and drop the reference from the comparison instead of failing it.
        """
        pin = compose_pin("mysql84")
        cases = {
            "one character too long": pin + "a",
            "one character too short": pin[:-1],
            "uppercase digest": pin[:-4] + "ABCD",
            "trailing text": pin + "-suffix",
            "trailing path segment": pin + "/suffix",
            "trailing underscore": pin + "_suffix",
            "trailing plus": pin + "+suffix",
        }

        for description, malformed in cases.items():
            with self.subTest(case=description):
                self.setUp()
                self.edit(CI, pin, malformed, 1)
                self.assertRejected("disagree with the Compose stack")

    def test_fix_carries_the_source_into_every_copy(self) -> None:
        """Repair all copies from the source rather than one at a time."""
        current = compose_pin("mysql84")
        for relative in MIRRORS:
            self.edit(relative, current, a_different_pin(current))
        self.assertNotEqual(0, self.run_gate().returncode)

        repaired = self.run_gate("--fix")

        self.assertEqual(0, repaired.returncode, repaired.stderr)
        self.assertEqual(0, self.run_gate().returncode)

    def test_fix_reports_what_it_cannot_repair(self) -> None:
        """Refuse to report success when a copy needs a decision, not a rewrite.

        A rewrite can carry a version forward. It cannot invent a constant that
        was deleted, so the run has to end unsuccessfully and say so.
        """
        path = self.root / CSHARP
        path.write_text(
            re.sub(
                r"    public const string MariaDb114 =\n[^;]+;\n\n",
                "",
                path.read_text(encoding="utf-8"),
            ),
            encoding="utf-8",
        )

        result = self.run_gate("--fix")

        self.assertNotEqual(0, result.returncode)
        self.assertIn("need a decision", result.stderr)


if __name__ == "__main__":
    unittest.main()
