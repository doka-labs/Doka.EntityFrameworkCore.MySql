"""Architecture contracts for the repository engineering system."""

from __future__ import annotations

import ast
import json
import os
import re
import unittest
from pathlib import Path
from typing import Any


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ENGINEERING_ROOT = REPOSITORY_ROOT / "eng"
ARCHITECTURE_PATH = ENGINEERING_ROOT / "architecture.json"

ROOT_CALL_PATTERN = re.compile(
    r'^[ \t]*(?:(?:bash|exec)[ \t]+)?'
    r'["\']?(?:\$\{?repo_root\}?/|\./)eng/'
    r'(?P<command>[^/"\'\s]+\.sh)'
)
HEREDOC_PATTERN = re.compile(
    r"<<-?[ \t]*[\"']?(?P<delimiter>[A-Za-z_][A-Za-z0-9_]*)[\"']?"
)
REQUIRED_MANIFEST_FIELDS = frozenset(
    {
        "schemaVersion",
        "domains",
        "rootEntrypoints",
    }
)
REQUIRED_DOMAIN_FIELDS = frozenset(
    {
        "owner",
        "responsibility",
        "allowedDependencies",
    }
)
REQUIRED_ENTRYPOINT_FIELDS = frozenset(
    {
        "path",
        "target",
        "classification",
        "owner",
        "cost",
        "purpose",
        "inputs",
        "outputs",
        "sideEffects",
        "consumers",
    }
)
SUPPORT_DIRECTORIES = frozenset({"__pycache__", "templates", "tests"})


class EngineeringStructureTests(unittest.TestCase):
    """Prevent implementation and ownership drift across domain boundaries."""

    @classmethod
    def setUpClass(cls) -> None:
        """Load the reviewed architecture contract once for all structural checks."""
        cls.architecture = json.loads(ARCHITECTURE_PATH.read_text(encoding="ascii"))
        cls.domains: dict[str, dict[str, Any]] = cls.architecture["domains"]
        cls.entrypoints: list[dict[str, Any]] = cls.architecture["rootEntrypoints"]

    def test_architecture_manifest_is_complete_and_consistent(self) -> None:
        """Require ownership and operational impact for every public command."""
        self.assertEqual(REQUIRED_MANIFEST_FIELDS, set(self.architecture))
        self.assertEqual(1, self.architecture["schemaVersion"])
        self.assertEqual(sorted(self.domains), list(self.domains))
        self.assertEqual(
            set(self.domains),
            self._observed_domain_directories(),
            "Every implementation directory must have one manifest owner.",
        )

        for domain, contract in self.domains.items():
            with self.subTest(domain=domain):
                self.assertEqual(REQUIRED_DOMAIN_FIELDS, set(contract))
                self._assert_non_empty_string(contract["owner"], "owner")
                self._assert_non_empty_string(
                    contract["responsibility"],
                    "responsibility",
                )
                dependencies = contract["allowedDependencies"]
                self.assertIsInstance(dependencies, list)
                self.assertEqual(sorted(set(dependencies)), dependencies)
                self.assertIn(domain, dependencies)

        entrypoint_paths: list[str] = []
        targets: list[str] = []

        for entrypoint in self.entrypoints:
            with self.subTest(entrypoint=entrypoint.get("path", "<missing>")):
                self.assertTrue(
                    REQUIRED_ENTRYPOINT_FIELDS.issubset(entrypoint),
                    "Entrypoint metadata must stay explicit and reviewable.",
                )
                self.assertTrue(
                    set(entrypoint).issubset(
                        REQUIRED_ENTRYPOINT_FIELDS | {"sunset"}
                    )
                )
                self.assertIn(
                    entrypoint["classification"],
                    {"operator-command", "compatibility-command"},
                )
                self.assertIn(entrypoint["cost"], {"low", "medium", "high"})
                for field in {
                    "classification",
                    "owner",
                    "cost",
                    "purpose",
                    "inputs",
                    "outputs",
                    "sideEffects",
                }:
                    self._assert_non_empty_string(entrypoint[field], field)

                consumers = entrypoint["consumers"]
                self.assertIsInstance(consumers, list)
                self.assertTrue(consumers)
                self.assertEqual(sorted(set(consumers)), consumers)
                for consumer in consumers:
                    self._assert_repository_path(consumer, "consumer")

                if entrypoint["classification"] == "compatibility-command":
                    self.assertIn("sunset", entrypoint)
                    self._assert_non_empty_string(entrypoint["sunset"], "sunset")
                else:
                    self.assertNotIn("sunset", entrypoint)

                entrypoint_path = self._assert_repository_path(
                    entrypoint["path"],
                    "path",
                )
                target_path = self._assert_repository_path(
                    entrypoint["target"],
                    "target",
                )
                self.assertEqual(2, len(entrypoint_path.parts))
                self.assertEqual("eng", entrypoint_path.parts[0])
                self.assertEqual(".sh", entrypoint_path.suffix)
                self.assertGreaterEqual(len(target_path.parts), 3)
                self.assertEqual("eng", target_path.parts[0])
                self.assertEqual(".sh", target_path.suffix)

                entrypoint_paths.append(entrypoint["path"])
                targets.append(entrypoint["target"])

                target_domain = target_path.parts[1]
                self.assertIn(target_domain, self.domains)
                self.assertEqual(
                    self.domains[target_domain]["owner"],
                    entrypoint["owner"],
                )

        self.assertEqual(sorted(entrypoint_paths), entrypoint_paths)
        self.assertEqual(len(entrypoint_paths), len(set(entrypoint_paths)))
        self.assertEqual(len(targets), len(set(targets)))

    def test_root_contains_only_manifested_operator_commands(self) -> None:
        """Keep the root as a deliberately small shell command interface."""
        observed_python = {path.name for path in ENGINEERING_ROOT.glob("*.py")}
        observed_shell = {path.name for path in ENGINEERING_ROOT.glob("*.sh")}
        expected_shell = {
            Path(entrypoint["path"]).name for entrypoint in self.entrypoints
        }

        self.assertEqual(set(), observed_python)
        self.assertEqual(expected_shell, observed_shell)

        for entrypoint in self.entrypoints:
            with self.subTest(path=entrypoint["path"]):
                path = REPOSITORY_ROOT / entrypoint["path"]
                target = REPOSITORY_ROOT / entrypoint["target"]
                relative_target = target.relative_to(ENGINEERING_ROOT).as_posix()
                content = path.read_text(encoding="ascii")

                self.assertTrue(path.is_file())
                self.assertFalse(path.is_symlink())
                self.assertLessEqual(len(content.splitlines()), 12)
                self.assertTrue(content.startswith("#!/usr/bin/env bash\n"))
                self.assertIn("set -euo pipefail", content)
                self.assertIn(
                    f'exec "${{eng_root}}/{relative_target}" "$@"',
                    content,
                )
                self.assertTrue(os.access(path, os.X_OK))
                self.assertTrue(target.is_file())
                self.assertFalse(target.is_symlink())

    def test_manifested_commands_have_live_external_consumers(self) -> None:
        """Reject facades retained without a concrete operator or CI consumer."""
        for entrypoint in self.entrypoints:
            for consumer in entrypoint["consumers"]:
                with self.subTest(entrypoint=entrypoint["path"], consumer=consumer):
                    consumer_path = REPOSITORY_ROOT / consumer
                    self.assertTrue(consumer_path.is_file())
                    self.assertFalse(consumer_path.is_symlink())
                    self.assertIn(
                        entrypoint["path"],
                        consumer_path.read_text(encoding="ascii"),
                    )

    def test_declared_consumers_cover_every_executing_caller(self) -> None:
        """Reject a manifest that hides the automation actually running a command.

        The forward check above proves each declared consumer is real. Without
        this reverse check a maintainer reading the manifest to assess impact
        sees only documentation and concludes a command has no CI caller.
        """
        executing_surfaces = sorted(
            path
            for path in (
                list((REPOSITORY_ROOT / ".github" / "workflows").glob("*.yml"))
                + list((REPOSITORY_ROOT / ".githooks").iterdir())
            )
            if path.is_file()
        )

        for entrypoint in self.entrypoints:
            command = entrypoint["path"]
            declared = set(entrypoint["consumers"])

            for surface in executing_surfaces:
                relative = surface.relative_to(REPOSITORY_ROOT).as_posix()
                if command not in surface.read_text(encoding="utf-8"):
                    continue

                with self.subTest(entrypoint=command, caller=relative):
                    self.assertIn(
                        relative,
                        declared,
                        f"{relative} runs {command} without being declared "
                        "as one of its consumers.",
                    )

    def test_python_domains_follow_the_dependency_direction(self) -> None:
        """Reject cross-domain imports that bypass the reviewed dependency graph."""
        for domain, contract in self.domains.items():
            allowed_domains = frozenset(contract["allowedDependencies"])
            domain_root = ENGINEERING_ROOT / domain

            self.assertTrue(domain_root.is_dir())
            self.assertIn(domain, allowed_domains)

            for path in sorted(domain_root.rglob("*.py")):
                with self.subTest(path=path.relative_to(REPOSITORY_ROOT)):
                    tree = ast.parse(path.read_text(encoding="ascii"))

                    for node in ast.walk(tree):
                        if isinstance(node, ast.ImportFrom):
                            self._assert_import_allowed(
                                domain,
                                allowed_domains,
                                node.module,
                                node.level,
                            )
                        elif isinstance(node, ast.Import):
                            for alias in node.names:
                                self._assert_import_allowed(
                                    domain,
                                    allowed_domains,
                                    alias.name,
                                    0,
                                )

    def test_domain_shells_do_not_call_root_facades(self) -> None:
        """Keep internal composition direct even when public commands stay stable."""
        root_commands = {
            Path(entrypoint["path"]).name for entrypoint in self.entrypoints
        }

        for domain in self.domains:
            for path in sorted((ENGINEERING_ROOT / domain).rglob("*.sh")):
                root_calls = {
                    match.group("command")
                    for line in self._executable_shell_lines(
                        path.read_text(encoding="ascii")
                    )
                    if (match := ROOT_CALL_PATTERN.search(line)) is not None
                    and match.group("command") in root_commands
                }

                self.assertFalse(
                    root_calls,
                    f"{path.relative_to(REPOSITORY_ROOT)} calls root facade(s): "
                    f"{sorted(root_calls)}",
                )

    @staticmethod
    def _executable_shell_lines(content: str) -> list[str]:
        """Exclude heredoc payloads before inspecting executable shell lines."""
        executable_lines: list[str] = []
        heredoc_delimiter: str | None = None

        for line in content.splitlines():
            if heredoc_delimiter is not None:
                if line.strip() == heredoc_delimiter:
                    heredoc_delimiter = None
                continue

            executable_lines.append(line)
            heredoc_match = HEREDOC_PATTERN.search(line)
            if heredoc_match is not None:
                heredoc_delimiter = heredoc_match.group("delimiter")

        return executable_lines

    def _observed_domain_directories(self) -> set[str]:
        """Return implementation directories that contain owned source files."""
        observed: set[str] = set()

        for path in ENGINEERING_ROOT.iterdir():
            if not path.is_dir() or path.name in SUPPORT_DIRECTORIES:
                continue

            contains_source = any(
                child.is_file()
                and child.suffix != ".pyc"
                and "__pycache__" not in child.parts
                for child in path.rglob("*")
            )
            if contains_source:
                observed.add(path.name)

        return observed

    def _assert_repository_path(self, value: Any, field: str) -> Path:
        """Validate one canonical repository-relative manifest path."""
        self._assert_non_empty_string(value, field)
        path = Path(value)
        self.assertFalse(path.is_absolute(), field)
        self.assertNotIn("..", path.parts, field)
        self.assertEqual(path.as_posix(), value, field)

        return path

    def _assert_non_empty_string(self, value: Any, field: str) -> None:
        """Reject metadata whose type makes an empty value look populated."""
        self.assertIsInstance(value, str, field)
        self.assertTrue(value.strip(), field)

    def _assert_import_allowed(
        self,
        domain: str,
        allowed_domains: frozenset[str],
        module: str | None,
        level: int,
    ) -> None:
        """Validate one import against the manifest-owned domain graph."""
        imported_domain: str | None = None

        if level >= 2 and module:
            imported_domain = module.split(".", maxsplit=1)[0]
        elif level == 0 and module == "eng":
            self.fail(f"{domain} imports the engineering root package directly")
        elif level == 0 and module and module.startswith("eng."):
            imported_domain = module.split(".", maxsplit=2)[1]

        if imported_domain is None:
            return

        self.assertIn(
            imported_domain,
            self.domains,
            f"{domain} imports unowned root module {imported_domain}",
        )
        self.assertIn(
            imported_domain,
            allowed_domains,
            f"{domain} must not depend on {imported_domain}",
        )


if __name__ == "__main__":
    unittest.main()
