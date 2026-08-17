"""Behavior contracts for the local specification-matrix entrypoint."""

from __future__ import annotations

import json
import os
import subprocess
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

    def test_version_preflight_rejects_every_incomplete_contract_layer(self) -> None:
        """Fail before expensive tests when a resolved patch is not fully registered."""
        contracts = self.write_version_contracts()
        script = REPOSITORY_ROOT / "eng" / "testing" / "check-spec-version-contract.sh"

        completed = subprocess.run(
            ["bash", str(script), "10.0.11"],
            check=False,
            capture_output=True,
            text=True,
            env={**os.environ, "DOKA_SPEC_CONTRACTS_ROOT": str(contracts)},
        )
        self.assertEqual(0, completed.returncode, completed.stderr)

        mutations = (
            ("SpecSuiteInventory.10.0.11.json", "schemaVersion", 2),
            ("SpecSuiteInventory.10.0.11.json", "efCoreVersion", "10.0.10"),
            ("SpecSuiteInventory.10.0.11.json", "testMethods", []),
            ("SpecSuiteBaseline.json", "efCoreVersions", ["10.0.8"]),
            (
                "SpecSuiteBaseline.json",
                "entries",
                [{"efCoreVersions": ["10.0.8"]}],
            ),
            (
                "SpecSuiteInventory.10.0.11.json",
                "baseClasses",
                [{"id": "DifferentBase", "suiteDomain": "query"}],
            ),
            ("SpecDiscovery.10.0.11.json", "efCoreVersion", "10.0.10"),
            ("SpecDiscovery.10.0.11.json", "targets", []),
        )
        for filename, field, value in mutations:
            with self.subTest(filename=filename, field=field):
                path = contracts / filename
                original = path.read_text(encoding="ascii")
                payload = json.loads(original)
                payload[field] = value
                path.write_text(json.dumps(payload), encoding="ascii")
                failed = subprocess.run(
                    ["bash", str(script), "10.0.11"],
                    check=False,
                    capture_output=True,
                    text=True,
                    env={
                        **os.environ,
                        "DOKA_SPEC_CONTRACTS_ROOT": str(contracts),
                    },
                )
                self.assertNotEqual(0, failed.returncode)
                self.assertIn("Generate and review", failed.stderr)
                path.write_text(original, encoding="ascii")

    def test_release_matrix_binds_floor_behavior_to_the_protected_check(self) -> None:
        """Keep the floor graph cheap and the latest patch fully qualified."""
        matrix = (
            REPOSITORY_ROOT / "eng" / "testing" / "test-efcore-matrix.sh"
        ).read_text(encoding="ascii")
        release = (
            REPOSITORY_ROOT / "eng" / "release" / "release-candidate.sh"
        ).read_text(encoding="ascii")
        workflow = (
            REPOSITORY_ROOT / ".github" / "workflows" / "release-candidate.yml"
        ).read_text(encoding="ascii")

        self.assertLess(
            matrix.index("check-spec-version-contract.sh"),
            matrix.index('bash "${repo_root}/eng/testing/test.sh"'),
        )
        self.assertIn('validation_scope="${DOKA_EF_CORE_VALIDATION_SCOPE:-full}"', matrix)
        self.assertIn('"10.0.8:^10[.]0[.]8$:minimum-10-0-8:dependency-graph"', release)
        self.assertIn('"10.0.*:^10[.]0[.][0-9]+$:latest-10-0:full"', release)
        self.assertIn('DOKA_EF_CORE_VALIDATION_SCOPE="${scope}"', release)
        self.assertIn(
            'DOKA_INTEGRATION_ARTIFACTS_DIR="${integration_evidence_dir}"',
            matrix,
        )
        self.assertIn(
            '"integration": "integration/compatibility-matrix-evidence.json"',
            matrix,
        )
        efcore_row = workflow[workflow.index("          - stage: efcore-patch-matrix") :]
        efcore_row = efcore_row[: efcore_row.index("          - stage:", 12)]
        self.assertIn("timeout_minutes: 60", efcore_row)
        self.assertIn("deadline_seconds: 3300", efcore_row)

    def test_dependency_matrix_rows_replace_stale_local_evidence(self) -> None:
        """Keep repeated local rows isolated from earlier TRX and JSON files."""
        for script_name in (
            "test-efcore-matrix.sh",
            "test-mysqlconnector-matrix.sh",
        ):
            with self.subTest(script=script_name):
                script = (
                    REPOSITORY_ROOT / "eng" / "testing" / script_name
                ).read_text(encoding="ascii")
                remove = 'rm -rf -- "${evidence_dir}"'
                create = 'mkdir -p "${evidence_dir}'

                self.assertIn(
                    '"${artifact_suffix}" =~ ^[a-z0-9]+(-[a-z0-9]+)*$',
                    script,
                )
                self.assertLess(script.index(remove), script.index(create))

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

    def write_version_contracts(self) -> Path:
        """Write the smallest coherent three-layer specification contract."""
        temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(temporary_directory.cleanup)
        root = Path(temporary_directory.name)
        version = "10.0.11"
        (root / f"SpecSuiteInventory.{version}.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "efCoreVersion": version,
                    "testMethods": ["Test"],
                    "baseClasses": [{"id": "Base", "suiteDomain": "query"}],
                }
            ),
            encoding="ascii",
        )
        (root / "SpecSuiteBaseline.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "efCoreVersions": [version],
                    "supportedTargets": ["mysql84"],
                    "entries": [
                        {
                            "upstreamBaseId": "Base",
                            "efCoreVersions": [version],
                            "suiteDomain": "query",
                            "targets": ["mysql84"],
                        }
                    ],
                }
            ),
            encoding="ascii",
        )
        (root / f"SpecDiscovery.{version}.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "efCoreVersion": version,
                    "providerAssembly": "Doka.EntityFrameworkCore.MySql.FunctionalTests",
                    "targets": [
                        {
                            "target": "mysql84",
                            "fixtureTypes": ["Fixture"],
                            "minimumTestCount": 1,
                            "testIds": ["Test"],
                        }
                    ],
                }
            ),
            encoding="ascii",
        )
        return root


if __name__ == "__main__":
    unittest.main()
