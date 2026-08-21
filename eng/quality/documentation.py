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

_CANONICAL_GUIDE_SECTIONS: dict[str, tuple[str, ...]] = {
    "GOVERNANCE.md": (
        "Project Stewardship",
        "Roles and Responsibilities",
        "Continuity and Succession",
        "Primary Sources",
    ),
    "ROADMAP.md": (
        "Direction Through July 2027",
        "Explicit Non-Goals Through July 2027",
        "Review and Change Process",
        "Primary Source",
    ),
    "docs/architecture.md": (
        "System Context",
        "Runtime Composition",
        "Architectural Invariants",
        "Primary Sources",
    ),
    "docs/README.md": (
        "Use the Provider",
        "Operate the Provider",
        "Maintain the Provider",
        "Document Ownership",
        "Documentation Contract",
    ),
    "docs/complex-types.md": (
        "Support Matrix",
        "Verification",
        "Primary Sources",
    ),
    "docs/ctes.md": (
        "Support Matrix",
        "Runnable Verification",
        "Related Limitations",
        "Primary Sources",
    ),
    "docs/host-integration-examples.md": (
        "Local Validation",
        "Primary Sources",
    ),
    "docs/ide-integration.md": (
        "Repository Verification",
        "Primary Sources",
    ),
    "docs/migration-operation-handlers.md": (
        "Contract at a Glance",
        "Package Author Verification",
        "Primary Sources",
    ),
    "docs/openssf-best-practices.md": (
        "Official Project State",
        "Silver Documentation Evidence",
        "Gold Preparation",
        "Update Procedure",
        "Primary Sources",
    ),
    "docs/operations/paired-performance-methodology.md": (
        "What a Paired Run Measures",
        "Registered Sensitivity",
        "What the Contract Controls",
        "Primary Sources",
    ),
    "docs/operations/performance-baseline-operations.md": (
        "Accept an Engine Image Update",
        "Seed an Accepted Baseline",
        "Hosted Runner Baseline",
        "Primary Sources",
    ),
    "docs/operations/performance-evidence-reference.md": (
        "Profiles",
        "Evidence Layout",
        "Measurement Quality and Termination",
        "Soak Interpretation",
        "Primary Sources",
    ),
    "docs/operations/performance-evidence.md": (
        "Choose the Right Document",
        "Run One Target",
        "Failure Triage",
    ),
    "docs/operations/resilience-and-topology.md": (
        "Connection Pooler / Load Balancer Compatibility",
        "Primary Sources",
    ),
    "docs/security/assurance-case.md": (
        "Scope and Method",
        "Residual Risk and Ownership",
        "Review and Re-evaluation",
        "Primary Source",
    ),
    "docs/security/release-verification.md": (
        "Verify the Source Tag",
        "Verify SLSA Provenance",
        "Verify NuGet Repository Signatures",
        "Primary Sources",
    ),
    "docs/provider-configuration.md": (
        "Connection and Server Configuration",
        "Context Options",
        "Model Configuration",
        "Runnable Verification",
        "Primary Sources",
    ),
    "docs/query-functions.md": (
        "Function Matrix",
        "Runnable Verification",
        "Primary Sources",
    ),
    "docs/supported-databases.md": (
        "Active LTS Matrix",
        "Qualification Contract",
        "Primary Sources",
    ),
    "docs/temporal-tables.md": (
        "Support Matrix",
        "Runnable Verification",
        "Related Limitations",
        "Primary Sources",
    ),
}

_QUERY_EXTENSION_TYPES = frozenset(
    {
        "MySqlDbFunctionsExtensions",
        "MySqlNetTopologySuiteDbFunctionsExtensions",
    }
)
_CONFIGURATION_EXTENSION_TYPES = frozenset(
    {
        "MySqlDbContextOptionsBuilderExtensions",
        "MySqlEntityTypeBuilderExtensions",
        "MySqlIndexBuilderExtensions",
        "MySqlModelBuilderExtensions",
        "MySqlNetTopologySuiteDbContextOptionsBuilderExtensions",
        "MySqlNetTopologySuiteIndexBuilderExtensions",
        "MySqlNetTopologySuitePropertyBuilderExtensions",
        "MySqlNetTopologySuiteServiceCollectionExtensions",
        "MySqlPropertyBuilderExtensions",
        "MySqlServiceCollectionExtensions",
    }
)
_CONFIGURATION_INSTANCE_TYPES = frozenset(
    {
        "MySqlDbContextOptionsBuilder",
        "MySqlReverseEngineeringOptionsBuilder",
    }
)
_CONFIGURATION_STATIC_TYPES = frozenset({"MySqlServerVersion"})
_DOCUMENTATION_SUPPORT_FILES = frozenset(
    {
        "docs/decisions/MADR-PROFILE.md",
        "docs/decisions/adr-template.md",
    }
)


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


def validate_package_readme_links(readme: Path) -> tuple[DocumentationError, ...]:
    """Reject links that NuGet.org cannot resolve from a packaged README."""

    errors: list[DocumentationError] = []

    for line_number, raw_target in _document_links(readme):
        target = _destination(raw_target)

        if target is None or _is_external(target):
            continue

        parsed = urlsplit(target)

        if not parsed.path and parsed.fragment:
            continue

        errors.append(
            DocumentationError(
                source=readme,
                line=line_number,
                target=target,
                reason="packaged README links must be absolute or in-document anchors",
            )
        )

    return tuple(errors)


def validate_canonical_guides(
    root: Path,
    contracts: dict[str, tuple[str, ...]] | None = None,
) -> tuple[DocumentationError, ...]:
    """Require every canonical guide to retain its task-specific evidence sections."""

    errors: list[DocumentationError] = []

    for relative_path, required_sections in (contracts or _CANONICAL_GUIDE_SECTIONS).items():
        document = root / relative_path

        if not document.is_file():
            errors.append(
                DocumentationError(
                    source=document,
                    line=1,
                    target=relative_path,
                    reason="canonical guide does not exist",
                )
            )
            continue

        headings = {
            match.group("title")
            for _, line in _authored_lines(document)
            if (match := _HEADING_PATTERN.match(line)) is not None
        }

        for required_section in required_sections:
            if required_section not in headings:
                errors.append(
                    DocumentationError(
                        source=document,
                        line=1,
                        target=required_section,
                        reason="canonical guide is missing a required section",
                    )
                )

    return tuple(errors)


def validate_public_api_documentation(root: Path) -> tuple[DocumentationError, ...]:
    """Bind public query and configuration entry points to their canonical guides."""

    public_api_files = (
        root / "src" / "Doka.EntityFrameworkCore.MySql" / "PublicAPI.Unshipped.txt",
        root
        / "src"
        / "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
        / "PublicAPI.Unshipped.txt",
    )
    query_methods: set[str] = set()
    configuration_methods: set[str] = set()

    for public_api_file in public_api_files:
        for line in public_api_file.read_text(encoding="ascii").splitlines():
            parsed = _public_method(line)

            if parsed is None:
                continue

            type_name, method_name = parsed

            if type_name in _QUERY_EXTENSION_TYPES:
                query_methods.add(method_name)

            if (
                type_name in _CONFIGURATION_EXTENSION_TYPES
                or type_name in _CONFIGURATION_INSTANCE_TYPES
                or (
                    line.startswith("static ")
                    and type_name in _CONFIGURATION_STATIC_TYPES
                )
            ):
                configuration_methods.add(method_name)

    return (
        *_missing_api_identifiers(
            root / "docs" / "query-functions.md",
            query_methods,
        ),
        *_missing_api_identifiers(
            root / "docs" / "provider-configuration.md",
            configuration_methods,
        ),
    )


def validate_document_navigation(root: Path) -> tuple[DocumentationError, ...]:
    """Reject public documents that are orphaned from the documentation index."""

    repository_root = root.resolve()
    documentation_root = repository_root / "docs"
    pending = [documentation_root / "README.md"]
    reachable: set[Path] = set()

    while pending:
        document = pending.pop().resolve()

        if document in reachable or not document.is_file():
            continue

        reachable.add(document)

        for _, raw_target in _document_links(document):
            target = _destination(raw_target)

            if target is None or _is_external(target):
                continue

            parsed = urlsplit(target)

            if not parsed.path:
                continue

            destination = (document.parent / unquote(parsed.path)).resolve()

            try:
                destination.relative_to(documentation_root)
            except ValueError:
                continue

            if destination.is_dir():
                destination /= "README.md"

            if destination.suffix.lower() == ".md":
                pending.append(destination)

    errors: list[DocumentationError] = []
    support_files = {repository_root / path for path in _DOCUMENTATION_SUPPORT_FILES}

    for document in sorted(documentation_root.rglob("*.md")):
        if (
            document.resolve() not in reachable
            and document not in support_files
        ):
            errors.append(
                DocumentationError(
                    source=document,
                    line=1,
                    target=str(document.relative_to(repository_root)),
                    reason="public document is not reachable from docs/README.md",
                )
            )

    return tuple(errors)


def _public_method(line: str) -> tuple[str, str] | None:
    match = re.match(
        r"^(?:override |static )?Doka\.EntityFrameworkCore\.MySql\."
        r"(?P<type>MySql[A-Za-z0-9]+)\."
        r"(?P<method>[A-Za-z][A-Za-z0-9]*)(?:<[^>]+>)?\(",
        line,
    )

    if match is None or match.group("type") == match.group("method"):
        return None

    return match.group("type"), match.group("method")


def _missing_api_identifiers(
    document: Path,
    method_names: Iterable[str],
) -> tuple[DocumentationError, ...]:
    content = document.read_text(encoding="ascii") if document.is_file() else ""
    documented_methods = set(
        re.findall(
            r"`(?:[A-Za-z][A-Za-z0-9]*\.)?"
            r"(?P<method>[A-Za-z][A-Za-z0-9]*)"
            r"(?:<[^>]+>)?\s*\(",
            content,
        )
    )

    return tuple(
        DocumentationError(
            source=document,
            line=1,
            target=method_name,
            reason="public API method is missing from its canonical guide",
        )
        for method_name in sorted(method_names)
        if method_name not in documented_methods
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
    package_readme_errors = validate_package_readme_links(root / "README.md")
    canonical_guide_errors = validate_canonical_guides(root)
    public_api_errors = validate_public_api_documentation(root)
    navigation_errors = validate_document_navigation(root)
    errors = (
        result.errors
        + package_readme_errors
        + canonical_guide_errors
        + public_api_errors
        + navigation_errors
    )

    if errors:
        for error in errors:
            source = error.source.relative_to(root)
            print(
                f"{source}:{error.line}: {error.reason}: {error.target}"
            )

        print(
            f"Documentation validation failed with {len(errors)} error(s)."
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
