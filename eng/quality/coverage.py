#!/usr/bin/env python3
"""Validate one merged Cobertura report against the repository coverage policy."""

from __future__ import annotations

import json
import sys
import time
import xml.etree.ElementTree as element_tree
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


@dataclass(frozen=True)
class CoverageMetrics:
    """Store exact line and branch counters and expose their percentages."""

    lines_covered: int
    lines_valid: int
    branches_covered: int
    branches_valid: int

    @property
    def line_percent(self) -> float:
        """Return line coverage as a percentage."""
        if self.lines_valid == 0:
            return 0.0
        return 100.0 * self.lines_covered / self.lines_valid

    @property
    def branch_percent(self) -> float:
        """Return branch coverage as a percentage."""
        if self.branches_valid == 0:
            return 0.0
        return 100.0 * self.branches_covered / self.branches_valid


def _metrics(lines: Iterable[element_tree.Element]) -> CoverageMetrics:
    """Calculate counters from Cobertura line elements without trusting summaries."""
    line_elements = list(lines)
    lines_covered = sum(
        int(line.get("hits", "0")) > 0
        for line in line_elements
    )
    branches_covered = 0
    branches_valid = 0

    for line in line_elements:
        condition_coverage = line.get("condition-coverage", "")
        if "(" not in condition_coverage or "/" not in condition_coverage:
            continue
        fraction = condition_coverage.split("(", 1)[1].rstrip(")")
        covered_text, valid_text = fraction.split("/", 1)
        branches_covered += int(covered_text)
        branches_valid += int(valid_text)

    return CoverageMetrics(
        lines_covered,
        len(line_elements),
        branches_covered,
        branches_valid,
    )


def _validate_threshold(
    label: str,
    metrics: CoverageMetrics,
    minimum_line_percent: float,
    minimum_branch_percent: float | None,
) -> tuple[str, list[str]]:
    """Render one result line and return every threshold violation."""
    errors: list[str] = []
    if metrics.lines_valid == 0:
        errors.append(f"{label} has no instrumented lines.")
    elif metrics.line_percent < minimum_line_percent:
        errors.append(
            f"{label} line coverage {metrics.line_percent:.2f}% is below "
            f"{minimum_line_percent:.2f}%."
        )

    if minimum_branch_percent is None:
        minimum_branch_display = "N/A"
        if metrics.branches_valid > 0:
            errors.append(
                f"{label} has instrumented branches but declares no branch floor."
            )
    else:
        minimum_branch_display = f"{minimum_branch_percent:.2f}%"
        if minimum_branch_percent <= 0:
            errors.append(
                f"{label} branch floor must be greater than zero or null for "
                "a branch-free surface."
            )
        elif metrics.branches_valid == 0:
            errors.append(f"{label} has no instrumented branches.")
        elif metrics.branch_percent < minimum_branch_percent:
            errors.append(
                f"{label} branch coverage {metrics.branch_percent:.2f}% is below "
                f"{minimum_branch_percent:.2f}%."
            )

    result = (
        f"{label}: lines {metrics.lines_covered}/{metrics.lines_valid} "
        f"({metrics.line_percent:.2f}%, minimum {minimum_line_percent:.2f}%); "
        f"branches {metrics.branches_covered}/{metrics.branches_valid} "
        f"({metrics.branch_percent:.2f}%, minimum {minimum_branch_display})"
    )
    return result, errors


def _freshness_errors(
    root: element_tree.Element,
    maximum_age: int,
    now_timestamp: int,
) -> list[str]:
    """Return timestamp violations for one raw or merged Cobertura report."""
    report_timestamp = int(root.get("timestamp", "0"))
    age = now_timestamp - report_timestamp
    if report_timestamp <= 0:
        return ["Coverage report has no positive timestamp."]
    if age < -300:
        return ["Coverage report timestamp is more than five minutes in the future."]
    if age > maximum_age:
        return [
            f"Coverage report is {age} seconds old; maximum age is {maximum_age} seconds."
        ]
    return []


def evaluate_freshness(
    report_paths: Iterable[Path],
    policy_path: Path,
    *,
    now_timestamp: int | None = None,
) -> list[str]:
    """Validate every raw input timestamp before a new merged report is created."""
    policy = json.loads(policy_path.read_text(encoding="utf-8"))
    if policy.get("schemaVersion") != 1:
        return ["Coverage policy schemaVersion must be 1."]

    now = int(time.time()) if now_timestamp is None else now_timestamp
    maximum_age = int(policy["evidenceMaxAgeSeconds"])
    errors: list[str] = []
    for report_path in report_paths:
        root = element_tree.parse(report_path).getroot()
        for error in _freshness_errors(root, maximum_age, now):
            errors.append(f"{report_path}: {error}")
    return errors


def evaluate(
    report_path: Path,
    policy_path: Path,
    *,
    now_timestamp: int | None = None,
) -> tuple[list[str], list[str]]:
    """Evaluate report freshness, assembly floors, and critical-class floors."""
    policy = json.loads(policy_path.read_text(encoding="utf-8"))
    if policy.get("schemaVersion") != 1:
        return [], ["Coverage policy schemaVersion must be 1."]

    root = element_tree.parse(report_path).getroot()
    now = int(time.time()) if now_timestamp is None else now_timestamp
    maximum_age = int(policy["evidenceMaxAgeSeconds"])
    freshness_errors = _freshness_errors(root, maximum_age, now)
    if freshness_errors:
        return [], freshness_errors

    packages: dict[str, element_tree.Element] = {}
    errors: list[str] = []
    for package in root.iter("package"):
        name = package.get("name", "")
        if name in packages:
            errors.append(f"Coverage report contains duplicate assembly '{name}'.")
            continue
        packages[name] = package

    lines: list[str] = []
    declared_assemblies: set[str] = set()
    for assembly in policy["assemblies"]:
        name = assembly["name"]
        if name in declared_assemblies:
            errors.append(f"Coverage policy repeats assembly '{name}'.")
            continue
        declared_assemblies.add(name)
        package = packages.get(name)
        if package is None:
            errors.append(f"Coverage report is missing shipped assembly '{name}'.")
            continue
        result, threshold_errors = _validate_threshold(
            f"assembly {name}",
            _metrics(package.iter("line")),
            float(assembly["minimumLinePercent"]),
            float(assembly["minimumBranchPercent"]),
        )
        lines.append(result)
        errors.extend(threshold_errors)

    declared_classes: set[tuple[str, str]] = set()
    for critical_class in policy["criticalClasses"]:
        assembly_name = critical_class["assembly"]
        class_name = critical_class["name"]
        identity = (assembly_name, class_name)
        if identity in declared_classes:
            errors.append(
                f"Coverage policy repeats critical class '{class_name}'."
            )
            continue
        declared_classes.add(identity)
        package = packages.get(assembly_name)
        if package is None:
            continue
        matches = [
            class_element
            for class_element in package.iter("class")
            if class_element.get("name") == class_name
        ]
        if not matches:
            errors.append(
                f"Coverage report is missing critical class '{class_name}'."
            )
            continue

        # Cobertura emits one class node per source file for partial classes.
        # Distinct filenames make aggregating those fragments unambiguous.
        source_files = [
            class_element.get("filename", "")
            for class_element in matches
        ]
        if len(matches) > 1 and (
            any(not source_file for source_file in source_files)
            or len(set(source_files)) != len(source_files)
        ):
            errors.append(
                f"Coverage report contains ambiguous source fragments for "
                f"critical class '{class_name}'."
            )
            continue

        branch_floor = critical_class["minimumBranchPercent"]
        result, threshold_errors = _validate_threshold(
            f"critical class {class_name}",
            _metrics(
                line
                for class_element in matches
                for line in class_element.iter("line")
            ),
            float(critical_class["minimumLinePercent"]),
            None if branch_floor is None else float(branch_floor),
        )
        lines.append(result)
        errors.extend(threshold_errors)

    return lines, errors


def main(arguments: list[str]) -> int:
    """Run the command-line coverage gate."""
    if len(arguments) >= 4 and arguments[1] == "freshness":
        try:
            errors = evaluate_freshness(
                [Path(path) for path in arguments[3:]],
                Path(arguments[2]),
            )
        except (
            KeyError,
            OSError,
            TypeError,
            ValueError,
            json.JSONDecodeError,
            element_tree.ParseError,
        ) as error:
            print(f"Coverage freshness contract is malformed: {error}", file=sys.stderr)
            return 2

        for error in errors:
            print(error, file=sys.stderr)
        if errors:
            print("Coverage input freshness policy not met.", file=sys.stderr)
            return 1
        print(f"Coverage input freshness met for {len(arguments) - 3} report(s).")
        return 0

    if len(arguments) != 3 or arguments[1] == "freshness":
        print(
            "Usage: coverage_policy.py <merged-cobertura.xml> <coverage-policy.json>\n"
            "       coverage_policy.py freshness <coverage-policy.json> <report> [report...]",
            file=sys.stderr,
        )
        return 2

    report_path = Path(arguments[1])
    policy_path = Path(arguments[2])
    try:
        lines, errors = evaluate(report_path, policy_path)
    except (
        KeyError,
        OSError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
        element_tree.ParseError,
    ) as error:
        print(f"Coverage contract is malformed: {error}", file=sys.stderr)
        return 2

    for line in lines:
        print(line)
    for error in errors:
        print(error, file=sys.stderr)

    if errors:
        print("Coverage policy not met.", file=sys.stderr)
        return 1

    print("Coverage policy met.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
