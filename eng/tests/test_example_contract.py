"""Regression tests for the executable-documentation contract."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from types import ModuleType


def load_module() -> ModuleType:
    """Load the validator without requiring eng to be a Python package."""
    script = Path(__file__).resolve().parents[1] / "example_contract.py"
    spec = importlib.util.spec_from_file_location("example_contract", script)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {script}.")

    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


example_contract = load_module()


class ExampleContractTests(unittest.TestCase):
    """Prove that meaningful source and operator instructions are required."""

    def setUp(self) -> None:
        """Create one isolated example fixture."""
        self.temporary_directory = tempfile.TemporaryDirectory(prefix="doka-example-contract-")
        self.examples_root = Path(self.temporary_directory.name) / "examples"
        self.directory = self.examples_root / "Example"
        self.directory.mkdir(parents=True)
        self.contract = example_contract.ExampleContract(
            "Example",
            "Example.csproj",
            ("RequiredProviderApi",),
            invariant_checking=False,
        )
        (self.directory / "Example.csproj").write_text("<Project />\n", encoding="ascii")
        (self.directory / "README.md").write_text(
            "# Example\n\n```bash\ndotnet run --project examples/Example/Example.csproj\n```\n",
            encoding="ascii",
        )
        (self.directory / "Program.cs").write_text(
            "RequiredProviderApi();\n",
            encoding="ascii",
        )

    def tearDown(self) -> None:
        """Remove the isolated fixture."""
        self.temporary_directory.cleanup()

    def test_complete_example_passes(self) -> None:
        """Accept a declared API scenario with an exact run command."""
        errors = example_contract.validate_example(self.examples_root, self.contract)

        self.assertEqual([], errors)

    def test_missing_readme_fails(self) -> None:
        """Reject runnable code without operator-facing instructions."""
        (self.directory / "README.md").unlink()

        errors = example_contract.validate_example(self.examples_root, self.contract)

        self.assertIn("Example: missing README.md", errors)

    def test_placeholder_source_fails(self) -> None:
        """Reject the historical compile-only placeholder shape."""
        (self.directory / "Program.cs").write_text(
            'RequiredProviderApi();\nConsole.WriteLine("see README.md for usage instructions");\n',
            encoding="ascii",
        )

        errors = example_contract.validate_example(self.examples_root, self.contract)

        self.assertTrue(any("placeholder marker remains" in error for error in errors))

    def test_missing_scenario_api_fails(self) -> None:
        """Reject an example whose defining provider call disappeared."""
        (self.directory / "Program.cs").write_text("Console.WriteLine();\n", encoding="ascii")

        errors = example_contract.validate_example(self.examples_root, self.contract)

        self.assertIn("Example: required scenario token is missing: RequiredProviderApi", errors)

    def test_undeclared_project_fails_inventory(self) -> None:
        """Require new example projects to acquire an explicit contract."""
        undeclared = self.examples_root / "Undeclared"
        undeclared.mkdir()
        (undeclared / "Undeclared.csproj").write_text("<Project />\n", encoding="ascii")

        errors = example_contract.validate_inventory(self.examples_root, (self.contract,))

        self.assertIn("example project has no reviewed contract: Undeclared", errors)


if __name__ == "__main__":
    unittest.main()
