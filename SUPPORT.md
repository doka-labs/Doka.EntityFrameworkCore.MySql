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

The supported runtime and engine matrix is documented in the
[README](README.md#supported-engines). Unsupported versions may be exercised
only through the explicit compatibility mode and do not carry a support
guarantee.

Provider defects, documentation gaps, and regressions in the supported matrix
are in scope. Issues owned by EF Core, MySqlConnector, NetTopologySuite, MySQL,
or MariaDB may be redirected upstream after provider impact has been assessed.

Questions about application architecture, database administration, query
tuning unrelated to provider behavior, or unsupported deployment platforms may
receive community guidance but are not maintained provider contracts.

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
