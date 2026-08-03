#!/usr/bin/env python3
"""Validate the repository's executable-documentation contract.

Compilation proves that an example consumes the current public API. This
validator covers the complementary failure modes that compilation cannot see:
placeholder programs, missing run instructions, undeclared examples, and an
advertised scenario whose defining API calls disappeared during a refactor.
"""

from __future__ import annotations

import argparse
import dataclasses
from pathlib import Path
from typing import Iterable


class ExampleContractError(RuntimeError):
    """Raised when one or more example-contract checks fail."""


@dataclasses.dataclass(frozen=True)
class ExampleContract:
    """Declares the source evidence that makes one example meaningful."""

    directory: str
    project: str
    required_tokens: tuple[str, ...]
    invariant_checking: bool = False


CONTRACTS = (
    ExampleContract(
        "BulkOperations",
        "BulkOperations.csproj",
        ("MaxBatchSize", "ExecuteUpdateAsync", "ExecuteDeleteAsync"),
        invariant_checking=True,
    ),
    ExampleContract(
        "CharSetAndCollation",
        "CharSetAndCollation.csproj",
        ("HasCharSet", "UseStorageEngine", "UseCollation", "HasPrefixLength"),
        invariant_checking=True,
    ),
    ExampleContract(
        "CrudOperations",
        "CrudOperations.csproj",
        ("EnsureCreated", "SaveChanges", "EnsureDeleted"),
    ),
    ExampleContract(
        "DockerIntegration",
        "DockerIntegration.csproj",
        ("CanConnectAsync", "SELECT VERSION()", "SaveChangesAsync"),
        invariant_checking=True,
    ),
    ExampleContract(
        "Doka.EntityFrameworkCore.MySql.HostExamples",
        "Doka.EntityFrameworkCore.MySql.HostExamples.csproj",
        ("AddOpenTelemetry", "AddSerilog", "HasMySqlGuidFormat"),
    ),
    ExampleContract(
        "GeneratedColumns",
        "GeneratedColumns.csproj",
        ("HasComputedColumnSql", "stored: true", "stored: false"),
        invariant_checking=True,
    ),
    ExampleContract(
        "GettingStarted",
        "GettingStarted.csproj",
        ("UseMySql", "EnsureCreated", "SaveChanges"),
    ),
    ExampleContract(
        "GuidFormats",
        "GuidFormats.csproj",
        ("Binary16", "Char36", "UseMySqlClientGuidValueGeneration"),
        invariant_checking=True,
    ),
    ExampleContract(
        "InheritancePatterns",
        "InheritancePatterns.csproj",
        ("HasDiscriminator", "OwnsOne", "OfType<Dog>"),
    ),
    ExampleContract(
        "JsonColumns",
        "JsonColumns.csproj",
        ("JsonObject", "JsonContains", "JsonDepth", "DeepEquals"),
        invariant_checking=True,
    ),
    ExampleContract(
        "MigrationsWorkflow",
        "MigrationsWorkflow.csproj",
        ("MigrationWorkflowCommand", "MigrationWorkflowContext", "MigrationWorkflowPauseInterceptor"),
    ),
    ExampleContract(
        "MultiTenancy",
        "MultiTenancy.csproj",
        (
            "HasQueryFilter",
            "IgnoreQueryFilters",
            "EnforceTenantOwnership",
            "public override int SaveChanges(",
            "public override Task<int> SaveChangesAsync(",
            "AssertMismatchedTenantRejected",
        ),
        invariant_checking=True,
    ),
    ExampleContract(
        "PerformanceBestPractices",
        "PerformanceBestPractices.csproj",
        ("MaxBatchSize", "AsNoTracking", "CompileAsyncQuery"),
        invariant_checking=True,
    ),
    ExampleContract(
        "Relationships",
        "Relationships.csproj",
        ("Include", "HasMany", "WithMany"),
    ),
    ExampleContract(
        "RetryAndResilience",
        "RetryAndResilience.csproj",
        ("EnableRetryOnFailure", "maxRetryCount", "maxRetryDelay"),
    ),
    ExampleContract(
        "SpatialQueries",
        "SpatialQueries.csproj",
        ("UseNetTopologySuite", "HasSrid", "IsSpatial", "DistanceSphere"),
        invariant_checking=True,
    ),
)

PLACEHOLDER_MARKERS = (
    "see README.md for usage instructions",
    "This example demonstrates DockerIntegration patterns",
    "This example demonstrates GeneratedColumns patterns",
    "This example demonstrates JsonColumns patterns",
)


def read_sources(directory: Path) -> str:
    """Return every authored C# source file as one searchable contract body."""
    source_files = sorted(directory.rglob("*.cs"))
    if not source_files:
        return ""

    return "\n".join(path.read_text(encoding="utf-8") for path in source_files)


def validate_example(examples_root: Path, contract: ExampleContract) -> list[str]:
    """Validate one example without hiding independent failures."""
    errors: list[str] = []
    directory = examples_root / contract.directory
    project = directory / contract.project
    readme = directory / "README.md"
    program = directory / "Program.cs"

    for required_path in (directory, project, readme, program):
        if not required_path.exists():
            errors.append(f"{contract.directory}: missing {required_path.name}")

    if errors:
        return errors

    readme_text = readme.read_text(encoding="utf-8")
    source_text = read_sources(directory)

    if "dotnet run --project" not in readme_text or contract.project not in readme_text:
        errors.append(f"{contract.directory}: README omits its exact dotnet run command")

    for marker in PLACEHOLDER_MARKERS:
        if marker in source_text:
            errors.append(f"{contract.directory}: placeholder marker remains: {marker}")

    for token in contract.required_tokens:
        if token not in source_text:
            errors.append(f"{contract.directory}: required scenario token is missing: {token}")

    if "EnsureDeleted" in source_text and "ExampleDatabaseConfiguration.Create" not in source_text:
        errors.append(
            f"{contract.directory}: destructive lifecycle bypasses the shared database isolation"
        )

    if contract.invariant_checking:
        for token in (
            "ExampleDatabaseConfiguration.Create",
            "EnsureDeletedAsync",
            "EnsureCreatedAsync",
            "InvalidOperationException",
        ):
            if token not in source_text:
                errors.append(f"{contract.directory}: live invariant token is missing: {token}")

    return errors


def validate_inventory(examples_root: Path, contracts: Iterable[ExampleContract]) -> list[str]:
    """Require every project directory to have one reviewed contract entry."""
    expected = {contract.directory for contract in contracts}
    actual = {
        project.parent.name
        for project in examples_root.glob("*/*.csproj")
        if project.is_file()
    }
    errors: list[str] = []

    for missing in sorted(expected - actual):
        errors.append(f"declared example project is missing: {missing}")
    for undeclared in sorted(actual - expected):
        errors.append(f"example project has no reviewed contract: {undeclared}")

    return errors


def validate_repository(repository: Path) -> None:
    """Validate the complete example corpus and its shared safety boundary."""
    examples_root = repository / "examples"
    errors = validate_inventory(examples_root, CONTRACTS)
    root_readme = examples_root / "README.md"
    shared_configuration = examples_root / "ExampleDatabaseConfiguration.cs"

    if not root_readme.is_file():
        errors.append("examples/README.md is missing")
    else:
        root_readme_text = root_readme.read_text(encoding="utf-8")
        for contract in CONTRACTS:
            if contract.directory not in root_readme_text:
                errors.append(f"examples/README.md omits {contract.directory}")

    if not shared_configuration.is_file():
        errors.append("shared example database configuration is missing")
    else:
        configuration_text = shared_configuration.read_text(encoding="utf-8")
        for token in (
            "DOKA_EXAMPLE_DATABASE_TARGET",
            "DOKA_EXAMPLE_CONNECTION_STRING",
            "Database = databaseName",
            '"mysql84"',
            '"mariadb114"',
            '"mariadb118"',
        ):
            if token not in configuration_text:
                errors.append(f"shared example configuration omits: {token}")

    for contract in CONTRACTS:
        errors.extend(validate_example(examples_root, contract))

    if errors:
        rendered = "\n".join(f"- {error}" for error in errors)
        raise ExampleContractError(f"Example contract validation failed:\n{rendered}")


def parse_arguments() -> argparse.Namespace:
    """Parse the repository root used by local and hosted gates."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True, help="Repository root")
    return parser.parse_args()


def main() -> int:
    """Run the contract and emit one stable success summary."""
    arguments = parse_arguments()

    try:
        validate_repository(arguments.root.resolve())
    except ExampleContractError as error:
        print(error)
        return 1

    invariant_count = sum(contract.invariant_checking for contract in CONTRACTS)
    print(
        f"Validated {len(CONTRACTS)} runnable examples, including "
        f"{invariant_count} self-validating scenario contracts."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
