#!/usr/bin/env python3
"""Validate repository-owned Markdown links and GitHub-style anchors.

The contract is intentionally dependency-free so local hooks and hosted quality
gates execute the same deterministic validation without restoring extra tools.
"""

from __future__ import annotations

import argparse
import dataclasses
import html
import re
from collections.abc import Iterable, Iterator
from pathlib import Path
from urllib.parse import unquote, urlsplit


_FENCE_PATTERN = re.compile(r"^\s*(?P<marker>`{3,}|~{3,})")
_HEADING_PATTERN = re.compile(
    r"^\s{0,3}#{1,6}\s+(?P<title>.+?)\s*#*\s*$"
)
_EXPLICIT_ANCHOR_PATTERN = re.compile(
    r"<a\s+[^>]*?(?:id|name)=[\"'](?P<anchor>[^\"']+)[\"'][^>]*>",
    re.IGNORECASE,
)
_INLINE_LINK_PATTERN = re.compile(
    r"!?\[[^\]]*\]\((?P<target>[^)\n]+)\)"
)
_REFERENCE_DEFINITION_PATTERN = re.compile(
    r"^\s{0,3}\[[^\]]+\]:\s*(?P<target>\S+)"
)
_HTML_TAG_PATTERN = re.compile(r"<[^>]+>")
_INLINE_MARKUP_PATTERN = re.compile(r"[`*_~]")


@dataclasses.dataclass(frozen=True)
class DocumentationError:
    """Describe one invalid local documentation reference."""

    source: Path
    line: int
    target: str
    reason: str


@dataclasses.dataclass(frozen=True)
class ValidationResult:
    """Summarize the deterministic repository documentation scan."""

    document_count: int
    link_count: int
    errors: tuple[DocumentationError, ...]


def discover_documents(root: Path) -> tuple[Path, ...]:
    """Return the authored Markdown surfaces covered by the quality contract."""

    candidates: set[Path] = set(root.glob("*.md"))

    for directory in (".github", "docs", "examples", "tests"):
        path = root / directory

        if path.is_dir():
            candidates.update(path.rglob("*.md"))

    return tuple(sorted(candidates))


def validate_repository(root: Path) -> ValidationResult:
    """Validate every local Markdown link and fragment below the repository root."""

    repository_root = root.resolve()
    documents = discover_documents(repository_root)
    errors: list[DocumentationError] = []
    anchors_by_document: dict[Path, frozenset[str]] = {}
    link_count = 0

    for document in documents:
        for line_number, raw_target in _document_links(document):
            target = _destination(raw_target)

            if target is None or _is_external(target):
                continue

            link_count += 1
            error = _validate_target(
                repository_root,
                document,
                line_number,
                target,
                anchors_by_document,
            )

            if error is not None:
                errors.append(error)

    return ValidationResult(
        document_count=len(documents),
        link_count=link_count,
        errors=tuple(errors),
    )


def _document_links(document: Path) -> Iterator[tuple[int, str]]:
    for line_number, line in _authored_lines(document):
        for match in _INLINE_LINK_PATTERN.finditer(line):
            yield line_number, match.group("target")

        definition = _REFERENCE_DEFINITION_PATTERN.match(line)

        if definition is not None:
            yield line_number, definition.group("target")


def _authored_lines(document: Path) -> Iterator[tuple[int, str]]:
    """Exclude fenced examples because their links are illustrative source text."""

    fence_character: str | None = None
    fence_length = 0

    for line_number, line in enumerate(
        document.read_text(encoding="utf-8").splitlines(),
        start=1,
    ):
        fence = _FENCE_PATTERN.match(line)

        if fence is not None:
            marker = fence.group("marker")

            if fence_character is None:
                fence_character = marker[0]
                fence_length = len(marker)
            elif marker[0] == fence_character and len(marker) >= fence_length:
                fence_character = None
                fence_length = 0

            continue

        if fence_character is None:
            yield line_number, line


def _destination(raw_target: str) -> str | None:
    target = raw_target.strip()

    if not target:
        return None

    if target.startswith("<"):
        closing_bracket = target.find(">")

        if closing_bracket < 0:
            return target

        return target[1:closing_bracket]

    # Markdown permits an optional title after an unquoted destination. Paths
    # containing spaces must use angle brackets or percent-encoding.
    return target.split(maxsplit=1)[0]


def _is_external(target: str) -> bool:
    if target.startswith("//"):
        return True

    return bool(urlsplit(target).scheme)


def _validate_target(
    repository_root: Path,
    source: Path,
    line_number: int,
    target: str,
    anchors_by_document: dict[Path, frozenset[str]],
) -> DocumentationError | None:
    parsed = urlsplit(target)
    relative_path = unquote(parsed.path)
    fragment = unquote(parsed.fragment)
    if not relative_path:
        destination = source
    elif relative_path.startswith("/"):
        destination = (repository_root / relative_path.lstrip("/")).resolve()
    else:
        destination = (source.parent / relative_path).resolve()

    try:
        destination.relative_to(repository_root)
    except ValueError:
        return DocumentationError(
            source=source,
            line=line_number,
            target=target,
            reason="target escapes the repository root",
        )

    if destination.is_dir():
        destination = destination / "README.md"

    if not destination.is_file():
        return DocumentationError(
            source=source,
            line=line_number,
            target=target,
            reason="target file does not exist",
        )

    if not fragment:
        return None

    if destination.suffix.lower() != ".md":
        return DocumentationError(
            source=source,
            line=line_number,
            target=target,
            reason="fragments can only be verified for Markdown targets",
        )

    anchors = anchors_by_document.setdefault(
        destination,
        _document_anchors(destination),
    )

    if fragment not in anchors:
        return DocumentationError(
            source=source,
            line=line_number,
            target=target,
            reason=f"anchor '#{fragment}' does not exist",
        )

    return None


def _document_anchors(document: Path) -> frozenset[str]:
    anchors: set[str] = set()
    heading_counts: dict[str, int] = {}

    for _, line in _authored_lines(document):
        for match in _EXPLICIT_ANCHOR_PATTERN.finditer(line):
            anchors.add(match.group("anchor"))

        heading = _HEADING_PATTERN.match(line)

        if heading is None:
            continue

        base_anchor = _github_heading_anchor(heading.group("title"))
        duplicate_index = heading_counts.get(base_anchor, 0)
        heading_counts[base_anchor] = duplicate_index + 1
        anchors.add(
            base_anchor
            if duplicate_index == 0
            else f"{base_anchor}-{duplicate_index}"
        )

    return frozenset(anchors)


def _github_heading_anchor(title: str) -> str:
    title = html.unescape(_HTML_TAG_PATTERN.sub("", title))
    title = _INLINE_MARKUP_PATTERN.sub("", title).strip().lower()
    characters: Iterable[str] = (
        character
        for character in title
        if character.isalnum() or character in {" ", "-", "_"}
    )
    return re.sub(r"\s+", "-", "".join(characters))


def _parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate repository-owned Markdown links and anchors."
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path.cwd(),
        help="Repository root. Defaults to the current working directory.",
    )
    return parser.parse_args()


def main() -> int:
    """Run the command-line contract and return a process exit code."""

    arguments = _parse_arguments()
    root = arguments.root.resolve()
    result = validate_repository(root)

    if result.errors:
        for error in result.errors:
            source = error.source.relative_to(root)
            print(
                f"{source}:{error.line}: {error.reason}: {error.target}"
            )

        print(
            f"Documentation validation failed with {len(result.errors)} error(s)."
        )
        return 1

    print(
        "Validated "
        f"{result.link_count} local links across "
        f"{result.document_count} Markdown documents."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
