#!/usr/bin/env python3
"""Keep Dependabot version-update targets equal to active release lines.

The existing default-branch entries are the templates: rendering preserves
their grouping, image policy and package floors without another templating
dependency or a second set of update-policy constants.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

if not __package__:  # pragma: no cover - direct CLI execution
    sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from eng.release import lines


ECOSYSTEMS = frozenset(("docker-compose", "github-actions", "dotnet-sdk", "nuget"))
ENTRY = re.compile(r"^  - package-ecosystem: ([a-z-]+)\s*$", re.MULTILINE)
TARGET = re.compile(r"^    target-branch: ([^\n]+)$", re.MULTILINE)
SDK_MAJOR_BOUNDARY = (
    "    ignore:\n"
    '      - dependency-name: "*"\n'
    "        update-types:\n"
    "          - version-update:semver-major\n"
)


class DependencyPolicyError(ValueError):
    """Version-update targets differ from the active-line authority."""


def _entries(text: str) -> list[tuple[str, str, str]]:
    """Read the intentionally bounded update-entry shape without parsing YAML."""
    if not text.startswith("version: 2\nupdates:\n"):
        raise DependencyPolicyError("Dependabot must retain the version 2 update-list contract.")

    matches = list(ENTRY.finditer(text))
    if len(matches) != len(re.findall(r"package-ecosystem\s*:", text)):
        raise DependencyPolicyError("Dependabot update entries must use the retained canonical shape.")

    result = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        block = text[match.start():end]
        targets = TARGET.findall(block)
        if len(targets) != len(re.findall(r"target-branch\s*:", block)) or len(targets) > 1:
            raise DependencyPolicyError("Dependabot entry repeats target-branch.")

        target = targets[0].strip().strip('"\'') if targets else "main"
        result.append((match[1], target, block))

    return result


def expected_targets(policy: dict) -> set[tuple[str, str]]:
    """Return exactly four update ecosystems per active exact source branch."""
    return {(ecosystem, line["sourceRef"].removeprefix("refs/heads/"))
            for line in policy["lines"] if line["state"] == "active"
            for ecosystem in ECOSYSTEMS}


def validate_configuration(policy: dict, text: str) -> None:
    """Reject inactive/missing/duplicate targets and major-boundary drift."""
    entries = _entries(text)
    observed = [(ecosystem, target) for ecosystem, target, _ in entries]
    if len(set(observed)) != len(observed) or set(observed) != expected_targets(policy):
        raise DependencyPolicyError("Dependabot targets must match all active release lines exactly once.")

    for ecosystem, _, block in entries:
        if ecosystem == "dotnet-sdk" and SDK_MAJOR_BOUNDARY not in block:
            raise DependencyPolicyError("Each SDK target must reject implicit major-version upgrades.")

        if ecosystem == "nuget" and not re.search(
                r'dependency-name: "Microsoft\.EntityFrameworkCore\.\*"\n'
                r"        update-types:\n"
                r"          - version-update:semver-major\n"
                r"          - version-update:semver-minor\n"
                r"          - version-update:semver-patch", block):
            raise DependencyPolicyError("Each EF target must preserve the deliberate floor/latest policy.")

        if ecosystem == "docker-compose" and any(
                f"dependency-name: {engine}\n        update-types:\n"
                "          - version-update:semver-major\n"
                "          - version-update:semver-minor" not in block
                for engine in ("mysql", "mariadb")):
            raise DependencyPolicyError("Each image target must preserve the qualified engine series.")


def render_configuration(policy: dict, text: str) -> str:
    """Render extra active targets from the retained main update contracts."""
    entries = _entries(text)
    defaults = [(ecosystem, block) for ecosystem, target, block in entries if target == "main"]
    if len(defaults) != 4 or {ecosystem for ecosystem, _ in defaults} != ECOSYSTEMS:
        raise DependencyPolicyError("Rendering needs one main template for each supported ecosystem.")

    prefix = text[:ENTRY.search(text).start()]
    blocks = [block for _, block in defaults]
    blocks[-1] = blocks[-1].rstrip() + "\n"
    for line in policy["lines"]:
        if line["state"] != "active" or line["sourceRef"] == policy["authorityRef"]:
            continue

        branch = line["sourceRef"].removeprefix("refs/heads/")
        for ecosystem, template in defaults:
            # Template comments can name main's current EF range. Retain the
            # policy itself, not stale version prose on another release line.
            body = "\n".join(row for row in template.splitlines()
                             if not row.lstrip().startswith("#"))
            header, remainder = body.split("\n", 1)
            blocks.append(f"\n{header}\n    target-branch: {branch}\n{remainder.rstrip()}\n")

    rendered = prefix + "".join(blocks)
    validate_configuration(policy, rendered)

    return rendered


def main(argv: list[str] | None = None) -> int:
    """Validate or prepare a reviewable Dependabot update for activation."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("render", "validate"))
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--policy", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args(argv)
    try:
        policy = lines.load_policy(args.policy or args.root / "eng/release/lines-policy.json")
        text = (args.root / ".github/dependabot.yml").read_text(encoding="utf-8")
        if args.command == "validate":
            validate_configuration(policy, text)
            print("Dependabot targets match the active release-line policy.")

            return 0

        rendered = render_configuration(policy, text)
        if args.output:
            args.output.write_text(rendered, encoding="ascii")
        else:
            print(rendered, end="")

        return 0
    except (DependencyPolicyError, lines.LinePolicyError, OSError, json.JSONDecodeError) as error:
        print(f"Release-line dependency policy failed: {error}", file=sys.stderr)

        return 1


if __name__ == "__main__":
    raise SystemExit(main())
