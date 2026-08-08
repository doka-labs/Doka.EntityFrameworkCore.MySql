# Engineering system

The `eng` tree contains the repository's build, test, quality, performance,
and release automation. Its root is a deliberately small operator interface.
Implementations and internal composition belong to one owned domain so the
public command surface stays stable without turning the root into a second
module system.

## Layout

| Path | Responsibility |
| --- | --- |
| `eng/common/` | Shared process, SDK, and deadline primitives. |
| `eng/development/` | Local contributor setup. |
| `eng/performance/` | Benchmark execution, evidence, policy, and reports. |
| `eng/quality/` | Static quality, documentation, coverage, and ADR gates. |
| `eng/release/` | Release artifacts, publication, SBOM, and evidence. |
| `eng/testing/` | Repository, integration, specification, and runtime tests. |
| `eng/tools/` | Compiled engineering-only utilities. |
| `eng/tests/` | Unit and architecture tests for the engineering system. |
| `eng/templates/` | Versioned templates consumed by engineering automation. |

`eng/architecture.json` is the source of truth for root commands and domain
dependencies. Every root command declares its owner, operational cost, input,
output, side effects, and real external consumers. Root Python modules are not
allowed. Shell commands at the root must be executable delegators to exactly
one domain implementation.

Domain implementations call each other directly and never call a root command;
the architecture test enforces that direction. Hooks, workflows, and
documentation are external consumers and use a root command whenever one is
manifested for that operation, because those callers depend on a stable public
surface rather than on internal composition.

A root command exists only when a maintainer needs a stable command for an
end-to-end operation. A temporary compatibility command additionally needs an
explicit removal condition in the architecture manifest. Every executing
caller of a root command -- each workflow and each Git hook -- must appear in
that command's `consumers` list; the architecture test rejects an undeclared
caller so the manifest stays usable for impact analysis.

## Dependency direction

Dependencies flow inward toward shared primitives:

```text
operator command -> domain implementation -> common
release -> performance
```

`common` cannot depend on another domain. `quality` and `performance` remain
independent. `release` may consume performance, quality, and testing contracts,
but those domains must not depend on release. Domain implementations must not
call root commands. The architecture test validates the manifest, the actual
filesystem, live consumers, imports, and shell composition together.

## Adding or changing automation

1. Choose the domain that owns the behavior.
2. Put implementation and focused tests in that domain and `eng/tests/`.
3. Add a root command only when a stable operator-facing end-to-end command is
   required, then declare its complete contract in `eng/architecture.json`.
4. Prefer `python3 -m eng.<domain>.<module>` and direct domain shell paths for
   internal composition.
5. Add external-input classification when the change can affect immutable
   performance or release evidence.
6. Run the engineering unit tests and Shell syntax validation before the
   wider repository gates.

```bash
python3 -m unittest discover -s eng/tests -p 'test_*.py'
./eng/quality/lint-workflows.sh
```

`lint-workflows.sh` owns the shell and workflow contract: shell syntax plus
shellcheck across `eng/` and `.githooks/`, then actionlint and zizmor across
`.github/workflows/`. It resolves tools from `PATH` and hydrates pinned,
checksum-verified versions only when `DOKA_LINT_AUTO_INSTALL=1` is set, which
is how CI runs it. The commit hook runs `--shell-only` so it stays offline.

Performance measurements and release workflows remain explicit higher-cost
operations. They are not part of the structural verification above.

## Orchestrator boundaries

File size alone does not determine a split. A shell orchestrator may retain the
steps that share traps, temporary resources, checkpoint state, and failure
semantics. It must delegate policy, parsing, and evidence transformation to
focused domain modules. Python modules are split when validation, host
admission, comparison, publication, or persistence can be tested and owned as a
separate responsibility.

Comments document these non-obvious ownership boundaries and external
constraints. They do not narrate commands, repeat identifiers, or compensate
for unclear names. Public Python functions that define an engineering contract
carry concise docstrings; private helpers are documented only when their
invariant is not apparent from their name and types.
