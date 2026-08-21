"""Regression tests for the repository documentation contract."""

from __future__ import annotations

import re
import tempfile
import unittest
from pathlib import Path

from eng.quality import documentation as documentation_contract


class DocumentationContractTests(unittest.TestCase):
    """Prove valid navigation and fail-closed path and anchor handling."""

    def setUp(self) -> None:
        """Create an isolated synthetic documentation tree."""

        self._temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-documentation-contract-"
        )
        self.root = Path(self._temporary_directory.name)

    def tearDown(self) -> None:
        """Dispose the synthetic repository."""

        self._temporary_directory.cleanup()

    def test_valid_links_cover_generated_duplicate_and_explicit_anchors(self) -> None:
        """Accept GitHub duplicate suffixes, explicit IDs, and reference links."""

        docs = self.root / "docs"
        docs.mkdir()
        (docs / "guide.md").write_text(
            """# Guide

## Repeated Heading

## Repeated Heading

<a id="stable-anchor"></a>
""",
            encoding="ascii",
        )
        (self.root / "README.md").write_text(
            """# Project

[Duplicate](docs/guide.md#repeated-heading-1)
[Explicit](docs/guide.md#stable-anchor)
[Guide][guide]

[guide]: docs/guide.md
""",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(3, result.link_count)
        self.assertEqual((), result.errors)

    def test_missing_files_and_anchors_fail_with_precise_reasons(self) -> None:
        """Report independently missing paths and stale fragments."""

        docs = self.root / "docs"
        docs.mkdir()
        (docs / "guide.md").write_text("# Guide\n", encoding="ascii")
        (self.root / "README.md").write_text(
            """# Project

[Missing file](docs/missing.md)
[Missing anchor](docs/guide.md#missing)
""",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(
            [
                "target file does not exist",
                "anchor '#missing' does not exist",
            ],
            [error.reason for error in result.errors],
        )

    def test_fenced_examples_do_not_create_false_navigation_failures(self) -> None:
        """Ignore Markdown-looking links that are intentionally shown as source."""

        (self.root / "README.md").write_text(
            """# Project

```markdown
[Illustrative](missing.md)
```
""",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(0, result.link_count)
        self.assertEqual((), result.errors)

    def test_paths_outside_the_repository_are_rejected(self) -> None:
        """Keep authored navigation inside the versioned repository boundary."""

        (self.root / "README.md").write_text(
            "# Project\n\n[Outside](../outside.md)\n",
            encoding="ascii",
        )

        result = documentation_contract.validate_repository(self.root)

        self.assertEqual(1, len(result.errors))
        self.assertEqual(
            "target escapes the repository root",
            result.errors[0].reason,
        )

    def test_package_readme_accepts_absolute_links_and_local_anchors(self) -> None:
        """Keep links portable across GitHub and the NuGet package page."""

        readme = self.root / "README.md"
        readme.write_text(
            "# Project\n\n[Guide](https://example.test/guide)\n[Local](#project)\n",
            encoding="ascii",
        )

        errors = documentation_contract.validate_package_readme_links(readme)

        self.assertEqual((), errors)

    def test_package_readme_rejects_a_repository_relative_link(self) -> None:
        """Do not let NuGet.org render repository navigation as an empty href."""

        readme = self.root / "README.md"
        readme.write_text(
            "# Project\n\n[Guide](docs/guide.md)\n",
            encoding="ascii",
        )

        errors = documentation_contract.validate_package_readme_links(readme)

        self.assertEqual(1, len(errors))
        self.assertEqual("docs/guide.md", errors[0].target)

    def test_canonical_guide_rejects_a_missing_evidence_section(self) -> None:
        """Do not let a canonical guide lose the evidence its role requires."""

        docs = self.root / "docs"
        docs.mkdir()
        (docs / "guide.md").write_text(
            "# Guide\n\n## Contract\n",
            encoding="ascii",
        )

        errors = documentation_contract.validate_canonical_guides(
            self.root,
            {"docs/guide.md": ("Contract", "Primary Sources")},
        )

        self.assertEqual(1, len(errors))
        self.assertEqual("Primary Sources", errors[0].target)

    def test_public_api_methods_require_a_canonical_document_owner(self) -> None:
        """Fail when a new query or configuration method is undocumented."""

        core = self.root / "src" / "Doka.EntityFrameworkCore.MySql"
        spatial = self.root / "src" / "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
        docs = self.root / "docs"
        core.mkdir(parents=True)
        spatial.mkdir(parents=True)
        docs.mkdir()
        (core / "PublicAPI.Unshipped.txt").write_text(
            "static Doka.EntityFrameworkCore.MySql.MySqlDbFunctionsExtensions.JsonDepth(this object! functions, string! json) -> int\n"
            "Doka.EntityFrameworkCore.MySql.MySqlDbContextOptionsBuilder.CommandTimeout(int commandTimeout) -> object!\n",
            encoding="ascii",
        )
        (spatial / "PublicAPI.Unshipped.txt").write_text(
            "static Doka.EntityFrameworkCore.MySql.MySqlNetTopologySuiteDbFunctionsExtensions.DistanceSphere(this object! functions, object! left, object! right) -> double\n",
            encoding="ascii",
        )
        (docs / "query-functions.md").write_text(
            "# Query Functions\n\n`JsonDepth()`\n",
            encoding="ascii",
        )
        (docs / "provider-configuration.md").write_text(
            "# Provider Configuration\n\n`CommandTimeout()`\n",
            encoding="ascii",
        )

        errors = documentation_contract.validate_public_api_documentation(self.root)

        self.assertEqual(1, len(errors))
        self.assertEqual("DistanceSphere", errors[0].target)

    def test_performance_entry_point_routes_every_specialized_contract(self) -> None:
        """Keep the split runbook discoverable without restoring the monolith."""

        repository_root = Path(__file__).resolve().parents[2]
        entry_point = (
            repository_root / "docs" / "operations" / "performance-evidence.md"
        ).read_text(encoding="ascii")
        routed_documents = (
            "performance-evidence-reference.md",
            "paired-performance-methodology.md",
            "performance-baseline-operations.md",
        )
        compatibility_anchors = (
            '<a id="profiles"></a>',
            '<a id="evidence-layout"></a>',
            '<a id="measurement-quality-and-termination"></a>',
            '<a id="accept-an-engine-image-update"></a>',
            '<a id="seed-an-accepted-baseline"></a>',
            '<a id="compare-with-the-accepted-baseline"></a>',
            '<a id="hosted-runner-baseline"></a>',
            '<a id="soak-interpretation"></a>',
            '<a id="paired-scorecard-use"></a>',
            '<a id="what-the-contract-controls"></a>',
        )

        for routed_document in routed_documents:
            with self.subTest(routed_document=routed_document):
                self.assertIn(routed_document, entry_point)

        for compatibility_anchor in compatibility_anchors:
            with self.subTest(compatibility_anchor=compatibility_anchor):
                self.assertEqual(1, entry_point.count(compatibility_anchor))

        self.assertLessEqual(len(entry_point.splitlines()), 250)

    def test_document_navigation_rejects_an_orphaned_public_page(self) -> None:
        """Require every public document to be reachable from the docs index."""

        docs = self.root / "docs"
        docs.mkdir()
        (docs / "README.md").write_text(
            "# Documentation\n\n[Guide](guide.md)\n",
            encoding="ascii",
        )
        (docs / "guide.md").write_text("# Guide\n", encoding="ascii")
        (docs / "orphan.md").write_text("# Orphan\n", encoding="ascii")

        errors = documentation_contract.validate_document_navigation(self.root)

        self.assertEqual(1, len(errors))
        self.assertEqual("docs/orphan.md", errors[0].target)


class PullRequestTemplateContractTests(unittest.TestCase):
    """Keep every review obligation explicit without ambiguous checkboxes."""

    ROOT = Path(__file__).resolve().parents[2]

    def setUp(self) -> None:
        """Read the repository-owned pull-request review contract."""

        self.template = (self.ROOT / ".github" / "pull_request_template.md").read_text(
            encoding="ascii"
        )

    def test_every_conditional_gate_has_an_explicit_disposition(self) -> None:
        """Require statuses instead of intentionally unchecked alternatives."""

        self.assertNotIn("- [ ]", self.template)
        self.assertEqual(2, self.template.count("`unchanged` or `changed`"))
        self.assertIn("`passed`, `not applicable`, or `pending`", self.template)

    def test_validation_table_keeps_every_repository_gate_visible(self) -> None:
        """Prevent template simplification from hiding a qualification path."""

        required_commands = (
            "./eng/test.sh",
            "./eng/test-integration.sh",
            "./eng/test-runtime-posture.sh --up-test-down",
            "./eng/benchmark.sh --up-smoke-down",
            "./eng/release-candidate.sh",
        )

        for command in required_commands:
            with self.subTest(command=command):
                self.assertGreaterEqual(self.template.count(f"`{command}`"), 2)


class MaintenanceIssueFormContractTests(unittest.TestCase):
    """Keep maintenance reviews complete and mutually exclusive."""

    ROOT = Path(__file__).resolve().parents[2]
    TEMPLATE_ROOT = ROOT / ".github" / "ISSUE_TEMPLATE"

    @staticmethod
    def form_element(form: str, element_id: str) -> str:
        """Return one issue-form element by its stable field identifier."""

        marker = f"    id: {element_id}\n"
        marker_index = form.index(marker)
        start = form.rfind("  - type: ", 0, marker_index)
        end = form.find("\n  - type: ", marker_index)
        return form[start:] if end == -1 else form[start:end]

    def setUp(self) -> None:
        """Read the two repository-maintenance forms and governance contract."""

        self.compatibility = (
            self.TEMPLATE_ROOT / "compatibility-review.yml"
        ).read_text(encoding="ascii")
        self.upstream = (self.TEMPLATE_ROOT / "upstream-triage.yml").read_text(
            encoding="ascii"
        )
        self.governance = (self.ROOT / "docs" / "release-governance.md").read_text(
            encoding="ascii"
        )
        self.supported_databases = (
            self.ROOT / "docs" / "supported-databases.md"
        ).read_text(encoding="ascii")

    def test_legacy_maintenance_templates_are_replaced_by_required_forms(
        self,
    ) -> None:
        """Reject optional Markdown stubs that permit incomplete reviews."""

        self.assertFalse((self.TEMPLATE_ROOT / "compatibility-review.md").exists())
        self.assertFalse((self.TEMPLATE_ROOT / "upstream-triage.md").exists())

        for name, form in (
            ("compatibility-review", self.compatibility),
            ("upstream-triage", self.upstream),
        ):
            with self.subTest(form=name):
                self.assertIn("name:", form)
                self.assertIn("description:", form)
                self.assertIn("body:\n", form)
                for element in form.split("\n  - type: ")[1:]:
                    if element.startswith("markdown\n"):
                        continue

                    self.assertIn("required: true", element)
                    if element.startswith("dropdown\n"):
                        self.assertNotIn("multiple: true", element)

    def test_compatibility_review_requires_every_supported_lts_target(self) -> None:
        """Keep the monthly matrix aligned with the advertised support lines."""

        supported_target_ids = set(
            re.findall(
                r"\| `(mysql\d+|mariadb\d+)` \|$",
                self.supported_databases,
                flags=re.MULTILINE,
            )
        )
        form_target_ids = set(
            re.findall(
                r"^    id: ((?:mysql|mariadb)\d+)$",
                self.compatibility,
                flags=re.MULTILINE,
            )
        )
        expected_options = (
            "- qualified",
            "- follow-up required",
            "- not qualified",
        )

        self.assertEqual(supported_target_ids, form_target_ids)
        for target_id in sorted(supported_target_ids):
            with self.subTest(target=target_id):
                element = self.form_element(self.compatibility, target_id)
                self.assertTrue(element.startswith("  - type: dropdown\n"))
                self.assertIn("required: true", element)
                for option in expected_options:
                    self.assertIn(option, element)

        supported_release_lines = set(
            re.findall(
                r"^\| (?:MySQL|MariaDB) \| ([0-9.]+) LTS \|",
                self.supported_databases,
                flags=re.MULTILINE,
            )
        )
        for release_line in sorted(supported_release_lines):
            with self.subTest(documented_release=release_line):
                self.assertIn(f"`{release_line}`", self.governance)

    def test_upstream_triage_requires_one_disposition_and_impact_vocabulary(
        self,
    ) -> None:
        """Replace ambiguous alternatives with required single selections."""

        disposition = self.form_element(self.upstream, "disposition")
        self.assertTrue(disposition.startswith("  - type: dropdown\n"))
        self.assertIn("required: true", disposition)
        for option in (
            "- code change required",
            "- reviewed no-op",
            "- backlog follow-up required",
        ):
            self.assertIn(option, disposition)

        for impact_id in (
            "public-api-impact",
            "engine-difference-impact",
            "diagnostics-impact",
            "supported-engine-impact",
        ):
            with self.subTest(impact=impact_id):
                element = self.form_element(self.upstream, impact_id)
                self.assertTrue(element.startswith("  - type: dropdown\n"))
                self.assertIn("- unchanged", element)
                self.assertIn("- changed", element)
                self.assertIn("required: true", element)

        self.assertNotIn("type: checkboxes", self.upstream)


class ReleaseRunbookAgreementTests(unittest.TestCase):
    """Prove the runbook describes the release path the workflows implement.

    An operator follows the runbook, not the YAML. When the two drift, the
    procedure silently teaches a sequence that no longer exists.
    """

    ROOT = Path(__file__).resolve().parents[2]

    def setUp(self) -> None:
        """Read the runbook and the workflows it describes."""
        self.runbook = (
            self.ROOT / "docs" / "operations" / "release-publication.md"
        ).read_text(encoding="utf-8")
        self.candidate = (
            self.ROOT / ".github" / "workflows" / "release-candidate.yml"
        ).read_text(encoding="utf-8")
        self.pre_tag = (
            self.ROOT / "eng" / "release" / "pre-tag-check.sh"
        ).read_text(encoding="utf-8")
        self.decision = (
            self.ROOT
            / "docs"
            / "decisions"
            / "D-026-release-qualification-and-paired-performance.md"
        ).read_text(encoding="utf-8")

    def test_the_runbook_names_the_required_aggregator(self) -> None:
        """Point the operator at the check candidate assembly imports."""
        self.assertIn("repository-qualification", self.runbook)

    def test_the_runbook_names_the_pre_tag_check(self) -> None:
        """Keep the documented preparation step pointing at a real command."""
        self.assertIn("./eng/pre-tag-check.sh", self.runbook)
        self.assertTrue((self.ROOT / "eng" / "pre-tag-check.sh").is_file())

    def test_the_pre_tag_check_does_not_claim_to_start_the_candidate(self) -> None:
        """Keep the helper aligned with qualification-before-tag ordering."""
        self.assertIn("ready for untagged hosted qualification", self.pre_tag)
        self.assertNotIn("Creating it starts the candidate", self.pre_tag)

    def test_the_runbook_requires_repository_release_immutability(self) -> None:
        """Keep a load-bearing GitHub setting in the operator preflight."""
        self.assertRegex(self.runbook, r"Enable release\s+immutability")
        self.assertIn("repos/${repo}/immutable-releases", self.runbook)

    def test_the_runbook_describes_untagged_manual_qualification(self) -> None:
        """Keep the reversible candidate phase ahead of tag creation."""
        self.assertIn("Start one untagged candidate run", self.runbook)
        self.assertIn("workflow_dispatch:", self.candidate)
        self.assertNotIn("push:", self.candidate)
        self.assertLess(
            self.runbook.index("Start one untagged candidate run"),
            self.runbook.index("Create the signed immutable identity"),
        )

    def test_publication_revalidates_the_frozen_check_attempt(self) -> None:
        """Keep rerun selection outside the immutable publication boundary."""
        for document in (self.runbook, self.decision):
            with self.subTest(document=document[:40]):
                self.assertIn("exact check-run", document)
                self.assertIn("attempt", document)
        self.assertIn("--qualification-manifest", self.candidate)

    def test_the_runbook_states_the_receipt_count_the_workflow_produces(self) -> None:
        """Reject a documented stage count the workflow cannot satisfy."""
        stages = set(
            re.findall(r"release-candidate\.sh --stage ([a-z-]+)", self.candidate)
        ) - {"finalize"}
        stages |= set(re.findall(r"^\s+- stage: ([a-z-]+)$", self.candidate, re.M))

        self.assertEqual(6, len(stages))
        self.assertIn("exactly six required stage", self.runbook)

    def test_the_runbook_no_longer_promises_a_local_rehearsal(self) -> None:
        """Keep a removed command out of the documented procedure."""
        self.assertNotIn("./eng/rehearse-release.sh", self.runbook)


if __name__ == "__main__":
    unittest.main()
