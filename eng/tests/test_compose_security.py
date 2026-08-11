"""Verify that bundled database services remain local-only by default."""

from __future__ import annotations

import unittest
from pathlib import Path


class ComposeSecurityTests(unittest.TestCase):
    """Pin the repository-owned network boundary for known test credentials."""

    def test_database_ports_are_bound_to_ipv4_loopback(self) -> None:
        """Reject wildcard publication for every bundled database service."""
        repository_root = Path(__file__).resolve().parents[2]
        compose_path = repository_root / "docker" / "compose.yml"
        compose_lines = compose_path.read_text(encoding="utf-8").splitlines()
        published_database_ports = {
            line.strip().removeprefix("- ").strip('"')
            for line in compose_lines
            if line.strip().startswith('- "') and line.strip().endswith(':3306"')
        }

        self.assertEqual(
            {
                "127.0.0.1:${DOKA_MYSQL84_PORT:-33068}:3306",
                "127.0.0.1:${DOKA_MYSQL97_PORT:-33070}:3306",
                "127.0.0.1:${DOKA_MARIADB1011_PORT:-33066}:3306",
                "127.0.0.1:${DOKA_MARIADB114_PORT:-33067}:3306",
                "127.0.0.1:${DOKA_MARIADB118_PORT:-33069}:3306",
                "127.0.0.1:${DOKA_MARIADB123_PORT:-33071}:3306",
            },
            published_database_ports,
        )


if __name__ == "__main__":
    unittest.main()
