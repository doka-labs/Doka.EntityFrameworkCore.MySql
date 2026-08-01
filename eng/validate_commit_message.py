#!/usr/bin/env python3
"""Validate the repository commit-message contract.

The validator keeps local history consistent without adding a package-manager
dependency. It checks only deterministic structure: Conventional Commit
subject syntax, one rationale bullet, a separated change-bullet block, ASCII
content, and the repository's 72-column commit-message limit. Human review
remains responsible for whether the rationale actually explains the change.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path
from typing import Sequence


MAX_LINE_LENGTH = 72
SUBJECT_PATTERN = re.compile(
    r"^(?:build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)"
    r"(?:\([a-z0-9][a-z0-9.-]*\))?!?: [a-z0-9].+$"
)
TRAILER_PATTERN = re.compile(r"^[A-Za-z][A-Za-z0-9-]*: \S.*$")


def validate_commit_message(message: str) -> list[str]:
    """Return every deterministic contract violation in ``message``."""
    lines = _committed_lines(message)
    errors: list[str] = []

    if not lines:
        return ["the commit message is empty"]

    for line_number, line in enumerate(lines, start=1):
        if not line.isascii():
            errors.append(f"line {line_number} must contain ASCII characters only")

        if line.rstrip() != line:
            errors.append(f"line {line_number} contains trailing whitespace")

        if len(line) > MAX_LINE_LENGTH:
            errors.append(
                f"line {line_number} exceeds the {MAX_LINE_LENGTH}-character limit"
            )

    subject = lines[0]

    if not SUBJECT_PATTERN.fullmatch(subject):
        errors.append(
            "the subject must use '<type>(<scope>): <lower-case summary>' "
            "with an approved Conventional Commit type"
        )

    if subject.endswith("."):
        errors.append("the subject must not end with a period")

    if len(lines) == 1:
        errors.append("the subject must be followed by a rationale and change bullets")
        return errors

    if lines[1] != "":
        errors.append("the subject must be followed by one blank line")
        return errors

    body_lines = lines[2:]

    if not body_lines:
        errors.append("the body must contain a rationale and change bullets")
        return errors

    if any(left == right == "" for left, right in zip(body_lines, body_lines[1:])):
        errors.append("body sections must be separated by exactly one blank line")

    paragraphs = _split_paragraphs(body_lines)

    if len(paragraphs) < 2:
        errors.append(
            "the rationale bullet and change bullets must be separated by one blank line"
        )
        return errors

    _validate_rationale(paragraphs[0], errors)
    _validate_changes(paragraphs[1], errors)

    for trailer_block in paragraphs[2:]:
        for line in trailer_block:
            if not TRAILER_PATTERN.fullmatch(line):
                errors.append(
                    "content after the change bullets must contain Git trailers only"
                )
                break

    return errors


def _committed_lines(message: str) -> list[str]:
    """Normalize line endings and discard Git comment lines and trailing blanks."""
    normalized = message.replace("\r\n", "\n").replace("\r", "\n")
    lines = [
        line
        for line in normalized.split("\n")
        if not line.lstrip().startswith("#")
    ]

    while lines and lines[-1] == "":
        lines.pop()

    return lines


def _split_paragraphs(lines: Sequence[str]) -> list[list[str]]:
    """Split body lines without erasing the bullet continuation layout."""
    paragraphs: list[list[str]] = []
    current: list[str] = []

    for line in lines:
        if line == "":
            if current:
                paragraphs.append(current)
                current = []

            continue

        current.append(line)

    if current:
        paragraphs.append(current)

    return paragraphs


def _validate_rationale(paragraph: Sequence[str], errors: list[str]) -> None:
    """Require exactly one possibly wrapped bullet in the rationale paragraph."""
    if not paragraph[0].startswith("- ") or paragraph[0] == "- ":
        errors.append("the rationale section must start with one non-empty bullet")

    for line in paragraph[1:]:
        if line.startswith("- "):
            errors.append("the rationale section must contain exactly one bullet")
        elif not line.startswith("  "):
            errors.append("wrapped rationale lines must start with two spaces")


def _validate_changes(paragraph: Sequence[str], errors: list[str]) -> None:
    """Require one or more non-empty bullets with consistently indented wraps."""
    bullet_count = 0

    for line in paragraph:
        if line.startswith("- "):
            bullet_count += 1

            if line == "- ":
                errors.append("change bullets must not be empty")
        elif not line.startswith("  "):
            errors.append("wrapped change lines must start with two spaces")

    if bullet_count == 0:
        errors.append("the change section must contain at least one bullet")


def _parse_args(arguments: Sequence[str] | None) -> argparse.Namespace:
    """Parse the commit message path supplied by Git's ``commit-msg`` hook."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("message_path", type=Path)
    return parser.parse_args(arguments)


def main(arguments: Sequence[str] | None = None) -> int:
    """Validate one message file and print an actionable rejection summary."""
    args = _parse_args(arguments)

    try:
        message = args.message_path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        print(f"Unable to read commit message: {error}", file=sys.stderr)
        return 1

    errors = validate_commit_message(message)

    if not errors:
        return 0

    print("Commit message rejected:", file=sys.stderr)

    for error in errors:
        print(f"- {error}", file=sys.stderr)

    print(
        "\nExpected shape:\n\n"
        "  fix(provider): summarize the change\n\n"
        "  - Explain why the change is required.\n\n"
        "  - Describe the implemented change.\n"
        "  - Describe its verification.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
