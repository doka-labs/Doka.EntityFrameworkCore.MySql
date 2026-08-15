"""Resolve the live specification matrix from its reviewed contract."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


TARGET_PATTERN = re.compile(r"^[a-z0-9]+$")


def load_supported_targets(contract_path: Path) -> tuple[str, ...]:
    """Return the ordered, unique target identifiers from a disposition contract."""
    document = json.loads(contract_path.read_text(encoding="ascii"))
    if not isinstance(document, dict):
        raise ValueError("the specification disposition contract must be an object")

    targets = document.get("supportedTargets")
    if (
        not isinstance(targets, list)
        or not targets
        or not all(
            isinstance(value, str) and TARGET_PATTERN.fullmatch(value)
            for value in targets
        )
    ):
        raise ValueError(
            "supportedTargets must be a non-empty array of lowercase ASCII "
            "target identifiers"
        )

    if len(set(targets)) != len(targets):
        raise ValueError("supportedTargets must not contain duplicate target identifiers")

    return tuple(targets)


def main(arguments: list[str]) -> int:
    """Print one resolved target per line for the shell orchestrator."""
    if len(arguments) != 1:
        print("Usage: spec_matrix.py <SpecDispositions.json>", file=sys.stderr)
        return 2

    try:
        targets = load_supported_targets(Path(arguments[0]))
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        print(f"Specification target resolution failed: {error}", file=sys.stderr)
        return 1

    print(*targets, sep="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
