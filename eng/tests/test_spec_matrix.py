"""Behavior contracts for the local specification-matrix entrypoint."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from eng.testing.spec_matrix import load_supported_targets


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


class SpecificationMatrixTests(unittest.TestCase):
    """Keep target resolution dynamic and fail closed at the file boundary."""

    def test_target_resolution_follows_the_supplied_contract(self) -> None:
        """Prove that a support-line change changes the resolved matrix."""
        contract = self.write_contract(["mysql100", "mariadb130"])

        self.assertEqual(
            ("mysql100", "mariadb130"),
            load_supported_targets(contract),
        )

    def test_invalid_target_contracts_are_rejected(self) -> None:
        """Reject shapes that could create ambiguous environments or paths."""
        invalid_targets = (
            [],
            ["mysql84", "mysql84"],
            ["mysql84", "../mariadb118"],
            ["mysql84", "MariaDb118"],
            ["mysql84", 118],
        )

        for targets in invalid_targets:
            with self.subTest(targets=targets):
                with self.assertRaises(ValueError):
                    load_supported_targets(self.write_contract(targets))

    def test_shell_runner_consumes_the_contract_resolver(self) -> None:
        """Prevent the operator command from returning to a hard-coded matrix."""
        script = (
            REPOSITORY_ROOT / "eng" / "testing" / "test-spec-matrix.sh"
        ).read_text(encoding="ascii")
        normalized_script = " ".join(script.replace("\\\n", "").split())

        self.assertIn(
            'python3 "${repo_root}/eng/testing/spec_matrix.py" "${target_contract}"',
            normalized_script,
        )
        for target in load_supported_targets(
            REPOSITORY_ROOT
            / "tests"
            / "Doka.EntityFrameworkCore.MySql.FunctionalTests"
            / "Specification"
            / "SpecDispositions.json"
        ):
            with self.subTest(target=target):
                self.assertNotIn(target, script)

    def write_contract(self, targets: object) -> Path:
        """Write a temporary disposition-shaped document and return its path."""
        temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(temporary_directory.cleanup)
        contract_path = Path(temporary_directory.name) / "SpecDispositions.json"
        contract_path.write_text(
            json.dumps({"supportedTargets": targets}),
            encoding="ascii",
        )
        return contract_path


if __name__ == "__main__":
    unittest.main()
