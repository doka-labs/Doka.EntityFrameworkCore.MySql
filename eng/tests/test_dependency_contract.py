"""Repository contracts for centrally managed dependency consumption."""

from __future__ import annotations

import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SPECIFICATION_PACKAGE = (
    "Microsoft.EntityFrameworkCore.Relational.Specification.Tests"
)
RUNNER_PACKAGE = "xunit.runner.visualstudio"


class DependencyContractTests(unittest.TestCase):
    """Keep centrally reviewed versions from activating unintended assets."""

    def test_non_test_specification_consumers_pin_runner_without_assets(self) -> None:
        """Replace the EF floor's runner version without loading its adapter."""
        consumers = []

        for project in sorted(REPOSITORY_ROOT.rglob("*.csproj")):
            if "artifacts" in project.parts:
                continue

            root = ET.parse(project).getroot()
            package_references = root.findall(".//PackageReference")
            package_ids = {
                reference.attrib.get("Include") for reference in package_references
            }
            if SPECIFICATION_PACKAGE not in package_ids:
                continue

            is_test_project = root.findtext(".//IsTestProject", default="false")
            if is_test_project.strip().lower() == "true":
                continue

            consumers.append(project.relative_to(REPOSITORY_ROOT).as_posix())
            runner_references = [
                reference
                for reference in package_references
                if reference.attrib.get("Include") == RUNNER_PACKAGE
            ]

            with self.subTest(project=project):
                self.assertEqual(1, len(runner_references))
                runner = runner_references[0]
                self.assertNotIn("Version", runner.attrib)
                self.assertNotIn("VersionOverride", runner.attrib)
                self.assertEqual("all", runner.findtext("PrivateAssets"))
                self.assertEqual("all", runner.findtext("ExcludeAssets"))

        self.assertEqual(
            [
                "eng/tools/Doka.EntityFrameworkCore.MySql.SpecificationContract/"
                "Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj",
                "tests/Doka.EntityFrameworkCore.MySql.SpecificationAdapters/"
                "SpecificationAdapters.csproj",
            ],
            consumers,
        )


if __name__ == "__main__":
    unittest.main()
