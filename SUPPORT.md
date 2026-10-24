# Support

This document routes questions and reports to the channel that can handle them
safely and efficiently. It does not create a commercial support agreement or a
guaranteed response SLA.

## Choose the Correct Channel

| Request | Channel | Public? |
|---|---|---|
| Suspected security vulnerability | Follow [SECURITY.md](SECURITY.md) | No |
| Harassment or other conduct concern | Follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | No |
| Reproducible provider defect | [GitHub Issues][issues] | Yes |
| Feature or compatibility request | [GitHub Issues][issues] | Yes |
| Usage or configuration question | [GitHub Issues][issues] | Yes |

Do not send ordinary support requests to the private security or conduct
mailboxes. Those channels are reserved for reports that cannot safely be made
public.

## Before Opening an Issue

Check the [README](README.md), runnable [examples](examples/README.md), existing
issues, and the current engine support matrix. Then reduce the behavior to the
smallest reproducible case.

An actionable report normally includes:

- provider, EF Core, .NET, and MySqlConnector versions;
- database family and exact server version;
- relevant connection options with credentials removed;
- a minimal model, query, or migration that reproduces the behavior;
- expected and actual behavior;
- generated SQL when applicable; and
- a minimal repository or self-contained code sample when practical.

Use fenced code blocks for code, SQL, logs, and stack traces. Redact passwords,
tokens, connection strings, personal data, internal host names, and other
sensitive values before posting.

## Supported Scope

The supported runtime and engine matrix is summarized in the
[README](README.md#supported-engines) and governed by
[Supported Databases](docs/supported-databases.md). Unsupported versions may
be exercised only through the explicit compatibility mode and do not carry a
support guarantee.

Provider defects, documentation gaps, and regressions in the supported matrix
are in scope. Issues owned by EF Core, MySqlConnector, NetTopologySuite, MySQL,
or MariaDB may be redirected upstream after provider impact has been assessed.

Questions about application architecture, database administration, query
tuning unrelated to provider behavior, or unsupported deployment platforms may
receive community guidance but are not maintained provider contracts.

## Framework Lines and Servicing

The current stable provider targets .NET 10 / EF Core 10. The binding future
maintenance branch is `maintenance/10.x`; optional platform prereleases use
`preview/11.x`. These two routes remain inactive until separately approved
bootstrap, protected qualification and hosted authorization. The development
branch `feature/dotnet-11` is not a publication or support grant.

All activated lines use the same explicit release entry, existing `nuget`
environment and their own exact toolchain. Personal signed tags, the existing
single-maintainer approval arrangement and administrator bypass stay unchanged.
There is no new publisher-environment migration or transition-report procedure.

The intended 10.x servicing window follows Microsoft's .NET 10 support through
**2028-11-14**, on supported serviced patches, as verified against the
[official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
on 2026-10-22. This does not promise compatibility with obsolete or unserviced
patches, or a commercial response SLA. Refresh upstream policy when activating
or retiring the line; no unannounced .NET 11 GA lifecycle is assumed.

Applicable correctness and security fixes are reviewed for each maintained
major, linked through separate backport/forward-port PRs, and tested against
that line's own dependency and runtime graph. A port that does not apply must
say why; the complete platform upgrade is not merged into maintenance.
Daily branch-native [dependency security audits](docs/security/dependency-audits.md)
check active lines independently of default-branch Dependabot security PRs.
Maintenance accepts compatible fixes, supported patch/dependency servicing and
necessary security changes, not an implicit SDK/EF-major upgrade.

An independently published Doka platform RC is explicit opt-in to that exact
recorded upstream RC graph. Microsoft's upstream go-live policy does not prove
Doka readiness or extend stable Doka guarantees. At support end, stop the
line's schedules, dependency targets and new publication eligibility after
completing affected publication; preserve tags, releases and historical evidence.
See [Release Lines](docs/operations/release-lines.md) for the full procedure.

## Response and Lifecycle

Public support is maintained on a best-effort basis. Issue priority is based on
security impact, data integrity, supported-matrix regressions, reproducibility,
and affected user scope rather than submission order.

Maintainers may request a minimal reproducer or additional diagnostics. An
issue may be closed when it cannot be reproduced, lacks the information needed
to investigate, concerns an unsupported version, or belongs to an upstream
project. It may be reopened when new evidence changes that assessment.

Security reports use the acknowledgement and coordinated-disclosure targets in
[SECURITY.md](SECURITY.md); those targets do not apply to ordinary support.

[issues]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/issues
