# Security Policy

## Repository Security Contract

This policy applies to the entire repository and both shipped packages:
`Doka.EntityFrameworkCore.MySql` and
`Doka.EntityFrameworkCore.MySql.NetTopologySuite`.

Security-sensitive changes must preserve these properties:

- Ordinary query and entity values remain parameterized.
- Identifiers and literals use their context-specific provider encoders.
- Non-parameterizable SQL grammar tokens pass bounded validation before SQL
  emission.
- Hostile database metadata cannot become executable SQL or generated source.
- Provider-owned telemetry excludes credentials, SQL, raw database and object
  names, exception messages, stack traces, and exception objects.
- Retry, cancellation, connection, transaction, and migration-lock behavior
  cannot silently duplicate unsafe operations or leak invalid state.
- CI and release evidence remains bound to the source revision it represents.

The maintained [threat model](docs/security/threat-model.md) defines the assets,
trust boundaries, attacker stories, assumptions, and re-evaluation triggers for
these properties.

## Supported Versions

| Version | Supported | Notes |
|---|---|---|
| 10.0.x | Yes | Current; receives security patches under the 90-day coordinated-disclosure window described below. |
| Earlier | No | No security backports. Consumers must upgrade to the supported line. |

The supported line tracks the active major of
`Microsoft.EntityFrameworkCore.Relational`. EF Core minor bumps such as 10.1.x
or 10.2.x require a deliberate provider response per ADR D-009 and are
evaluated case by case.

## Reporting a Vulnerability

Please do not report security vulnerabilities through public GitHub issues,
pull requests, or discussions.

Two private channels are available:

1. [GitHub Security Advisories](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/security/advisories/new)
   is preferred for technical disclosures with a reproducer.
2. Email `doka-labs@tuta.com` when GHSA is not available to the reporter, such as
   for an external researcher without a GitHub account.

Include as much of the following as possible:

- A description of the vulnerability and its potential impact.
- Steps to reproduce or a proof-of-concept repository or Gist.
- Affected provider versions and engines, including the specific MySQL or
  MariaDB server version and any Galera, MaxScale, or pooler topology.
- Any known mitigations a consumer can apply before a fix lands.
- Whether you intend to publish the finding and on what schedule.

### PGP and encrypted reporting

A pre-published long-lived PGP key is not currently maintained for this
project. If a disclosure requires PGP-encrypted transport, open the initial
contact through GHSA or send a short clear-text email signalling the intent to
encrypt. The maintainer responds with a freshly minted public-key fingerprint
on the disclosure thread. All subsequent attachments and follow-ups remain
encrypted.

This bilateral key-exchange path keeps key material scoped to the active
disclosure, avoids stale long-lived fingerprints, and remains available to
reporters regardless of whether they already use PGP day to day.

## Coordinated Disclosure Timeline

| Stage | Target SLA | Notes |
|---|---|---|
| Acknowledgement | 5 business days | The maintainer confirms receipt and opens the GHSA or email thread. |
| Triage outcome | 10 business days | Vendor-confirmed, vendor-rejected, or needs more information with a follow-up question. |
| Fix availability | 90 days from confirmed triage | Earlier when feasible; extensions are coordinated with the reporter. |
| Public advisory and release | Aligned with the fix release | The reporter is credited unless they request otherwise. |

The embargo applies from confirmed triage through the public advisory.
Reporters are asked to honor it. The maintainer likewise does not disclose
early, leak the reporter's identity, or use the report outside the fix path.

If the maintainer materially misses an SLA stage, including no response within
10 business days after the acknowledgement window, the reporter may escalate
publicly without objection from the project.

## Out of Scope

- Vulnerabilities in `Microsoft.EntityFrameworkCore.*` core packages. Report
  those through the [MSRC Researcher Portal](https://msrc.microsoft.com/report).
- Vulnerabilities in `MySqlConnector`. Report those to the
  [MySqlConnector project](https://github.com/mysql-net/MySqlConnector/security/advisories).
- Vulnerabilities in `NetTopologySuite`. Report those to the
  [NetTopologySuite project](https://github.com/NetTopologySuite/NetTopologySuite/security/advisories).
- Vulnerabilities in the MySQL or MariaDB server itself. Report those to the
  upstream vendor.
- Issues in third-party hosting that are not caused by this provider.

Reports that are in scope of a sibling project after triage are forwarded to
the appropriate channel with the reporter copied. Credit travels with the
report.
