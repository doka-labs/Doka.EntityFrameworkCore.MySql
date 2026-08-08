"""Tests for immutable NuGet graph materialization in the SBOM job."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from eng.release import sbom as materialize_sbom_assets


class MaterializeSbomAssetsTests(unittest.TestCase):
    """Keep cross-job SBOM inputs bound to the package-stage restore graph."""

    def setUp(self) -> None:
        """Create an isolated repository-shaped test tree."""
        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-sbom-assets-"
        )
        self.repository = Path(self.temporary_directory.name)
        self.project = self.repository / "src" / "Provider" / "Provider.csproj"
        self.assets = self.repository / "candidate" / "project.assets.json"
        self.output = self.repository / "artifacts" / "obj" / "Provider"
        self.project.parent.mkdir(parents=True)
        self.assets.parent.mkdir(parents=True)
        self.project.write_text("<Project />\n", encoding="ascii")

    def tearDown(self) -> None:
        """Remove the isolated repository tree."""
        self.temporary_directory.cleanup()

    def write_assets(
        self,
        *,
        project_path: Path | None = None,
        project_unique_name: Path | None = None,
        output_path: Path | None = None,
    ) -> bytes:
        """Write the minimum NuGet restore metadata required by the helper."""
        payload = {
            "version": 3,
            "project": {
                "restore": {
                    "projectPath": str(project_path or self.project),
                    "projectUniqueName": str(
                        project_unique_name or project_path or self.project
                    ),
                    "outputPath": f"{output_path or self.output}/",
                }
            },
        }
        encoded = (json.dumps(payload, indent=2) + "\n").encode("ascii")
        self.assets.write_bytes(encoded)
        return encoded

    def test_materializes_the_exact_candidate_graph(self) -> None:
        """Copy bytes unchanged after every recorded path matches the contract."""
        expected = self.write_assets()

        destination = materialize_sbom_assets.materialize_assets(
            self.repository,
            self.assets,
            self.project,
            self.output,
        )

        self.assertEqual(
            self.output.resolve() / "project.assets.json",
            destination,
        )
        self.assertEqual(expected, destination.read_bytes())

    def test_rejects_a_graph_for_another_project(self) -> None:
        """Prevent a valid graph from being attributed to the wrong package."""
        other_project = self.repository / "src" / "Other" / "Other.csproj"
        self.write_assets(project_path=other_project)

        with self.assertRaisesRegex(
            materialize_sbom_assets.AssetsMaterializationError,
            "project identity",
        ):
            materialize_sbom_assets.materialize_assets(
                self.repository,
                self.assets,
                self.project,
                self.output,
            )

        self.assertFalse((self.output / "project.assets.json").exists())

    def test_rejects_a_graph_with_an_unexpected_output_path(self) -> None:
        """Prevent stale restore metadata from recreating an unrelated obj tree."""
        other_output = self.repository / "artifacts" / "obj" / "Other"
        self.write_assets(output_path=other_output)

        with self.assertRaisesRegex(
            materialize_sbom_assets.AssetsMaterializationError,
            "output path",
        ):
            materialize_sbom_assets.materialize_assets(
                self.repository,
                self.assets,
                self.project,
                self.output,
            )

        self.assertFalse((self.output / "project.assets.json").exists())

    def test_rejects_materialization_outside_the_repository(self) -> None:
        """Keep the helper from turning assets metadata into an arbitrary write."""
        self.write_assets()

        with tempfile.TemporaryDirectory(prefix="doka-sbom-outside-") as directory:
            with self.assertRaisesRegex(
                materialize_sbom_assets.AssetsMaterializationError,
                "below repository root",
            ):
                materialize_sbom_assets.materialize_assets(
                    self.repository,
                    self.assets,
                    self.project,
                    Path(directory),
                )

    def test_reports_an_unwritable_output_shape_without_a_traceback(self) -> None:
        """Turn an invalid artifact layout into an actionable domain failure."""
        self.write_assets()
        self.output.parent.mkdir(parents=True)
        self.output.write_text("not a directory\n", encoding="ascii")

        with self.assertRaisesRegex(
            materialize_sbom_assets.AssetsMaterializationError,
            "could not be materialized",
        ):
            materialize_sbom_assets.materialize_assets(
                self.repository,
                self.assets,
                self.project,
                self.output,
            )


if __name__ == "__main__":
    unittest.main()
