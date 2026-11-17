#!/usr/bin/env python3
"""Keep Dependabot version-update targets equal to active release lines.

The existing default-branch entries are the templates: rendering preserves
their grouping, image policy and package floors without another templating
dependency or a second set of update-policy constants.
"""

from __future__ import annotations

import argparse
import fnmatch
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
NUGET_ROUTINE_GROUP = (
    "    groups:\n"
    "      nuget-dependencies:\n"
    "        patterns:\n"
    '          - "*"\n'
    "        update-types:\n"
    "          - minor\n"
    "          - patch\n"
)
FRAMEWORK_PACKAGE_FAMILIES = ("Microsoft.Extensions.*", "System.IO.Hashing")
INDEPENDENT_MICROSOFT_TOOLS = (
    "Microsoft.NET.Test.Sdk", "Microsoft.CodeAnalysis.PublicApiAnalyzers")
# WHY: Match balanced scalar quotes before normalization, so damaged package
# names cannot satisfy a required guard after their quotes are stripped.
IGNORE_RULE = re.compile(
    r'''^      - dependency-name: ("[^"\n]+"|'[^'\n]+'|[^'"\n]+)\n((?: {8,}[^\n]*\n)*)''', re.MULTILINE)


def _validate_nuget_boundary(block: str, target: str) -> None:
    """Keep each branch's routine proposals separate from platform transitions.

    WHY: Target parity alone accepts Microsoft 11 dependencies on the 10.x
    line. Relative SemVer guards also survive a later 11.x main rollover.
    Only the existing canonical block shape is inspected, not arbitrary YAML.
    """
    normalized = "\n".join(row for row in block.splitlines()
                           if row.strip() and not row.lstrip().startswith("#")) + "\n"
    groups = re.search(r"^    groups:\n(?: {6,}[^\n]*\n)*", normalized, re.MULTILINE)
    if (len(re.findall(r'''^    ["']?groups["']?\s*:''', normalized, re.MULTILINE)) != 1
            or not groups or groups[0] != NUGET_ROUTINE_GROUP):
        raise DependencyPolicyError(
            f"NuGet target '{target}' must group only routine minor/patch updates.")

    ignore = re.search(r"^    ignore:\n((?: {6,}[^\n]*\n|\n)*)", normalized, re.MULTILINE)
    matches = IGNORE_RULE.findall(ignore[1] if ignore else "")
    rules = [(name.strip().strip('\"\''), body)
             for name, body in matches]
    if (len(re.findall(r'''^    ["']?ignore["']?\s*:''', normalized, re.MULTILINE)) != 1
            or not ignore
            or "".join(f"      - dependency-name: {name}\n{body}" for name, body in matches) != ignore[1]
            or len(rules) != len(re.findall(r"dependency-name\s*:", normalized))):
        raise DependencyPolicyError(
            f"NuGet target '{target}' must retain one canonical ignore mapping.")

    # WHY: Dependabot combines exclusions. One valid rule cannot compensate
    # for a second rule that silently freezes servicing or hides a tool major.
    if len({name.casefold() for name, _ in rules}) != len(rules):
        raise DependencyPolicyError(f"NuGet target '{target}' repeats a dependency ignore rule.")

    for pattern, _ in rules:
        if any(fnmatch.fnmatchcase(tool.casefold(), pattern.casefold())
               for tool in INDEPENDENT_MICROSOFT_TOOLS):
            raise DependencyPolicyError(
                f"NuGet target '{target}' must not suppress independently versioned Microsoft tools.")

    major_only = "        update-types:\n          - version-update:semver-major\n"
    for family in FRAMEWORK_PACKAGE_FAMILIES:
        if (family, major_only) not in rules:
            raise DependencyPolicyError(
                f"NuGet target '{target}' must retain the relative major boundary for {family}.")

    # WHY: Admitting 3.x does not raise the 2.6.1 floor. Bound the exception
    # exactly so a reviewed line cannot hide a future 4.x compatibility decision.
    # The native NuGet updater parses comparisons, not NuGet package intervals.
    driver_rule = (
        "        update-types:\n"
        "          - version-update:semver-minor\n"
        "          - version-update:semver-patch\n"
        "        versions:\n"
        '          - ">= 3.0.0, < 4.0.0"\n')
    if ("MySqlConnector", driver_rule) not in rules:
        raise DependencyPolicyError(
            f"NuGet target '{target}' must preserve driver patch ownership and individual major proposals.")

    ef_types = (
        "        update-types:\n"
        "          - version-update:semver-major\n"
        "          - version-update:semver-minor\n"
        "          - version-update:semver-patch\n")
    if ("Microsoft.EntityFrameworkCore.*", ef_types) not in rules:
        raise DependencyPolicyError("Each EF target must preserve the deliberate floor/latest policy.")

    expected_names = {*FRAMEWORK_PACKAGE_FAMILIES, "Microsoft.EntityFrameworkCore.*", "MySqlConnector"}
    if {name for name, _ in rules} != expected_names:
        raise DependencyPolicyError(f"NuGet target '{target}' contains an unreviewed dependency ignore rule.")

    if (len(re.findall(r'''^    ["']?open-pull-requests-limit["']?\s*:''', normalized, re.MULTILINE)) != 1
            or re.findall(r"^    open-pull-requests-limit: ([^\n]+)$", normalized, re.MULTILINE) != ["2"]):
        raise DependencyPolicyError(
            f"NuGet target '{target}' must permit routine and independent major proposals together.")


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

    for ecosystem, target, block in entries:
        if ecosystem == "dotnet-sdk" and SDK_MAJOR_BOUNDARY not in block:
            raise DependencyPolicyError("Each SDK target must reject implicit major-version upgrades.")

        if ecosystem == "nuget":
            _validate_nuget_boundary(block, target)

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
