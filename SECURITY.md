# Security Policy

## Repository Security Contract

This policy applies to the entire repository and all three shipped packages:
`Doka.EntityFrameworkCore.MySql`,
`Doka.EntityFrameworkCore.MySql.NetTopologySuite`, and the standalone
`Doka.Caching.MySql` package. The cache does not depend on EF Core.

Security-sensitive changes must preserve these properties:

- Ordinary query and entity values remain parameterized.
- Identifiers and literals use their context-specific provider encoders.
- Non-parameterizable SQL grammar tokens pass bounded validation before SQL
  emission.
- Hostile database metadata cannot become executable SQL or generated source.
- Provider-owned telemetry excludes credentials, SQL, raw database and object
  names, exception messages, stack traces, and exception objects.
- Cache telemetry excludes cache keys, values and connection strings; cache
  operations preserve expiration, cancellation and buffer-ownership boundaries.
- Retry, cancellation, connection, transaction, and migration-lock behavior
  cannot silently duplicate unsafe operations or leak invalid state.
- CI and release evidence remains bound to the source revision it represents.

The maintained [threat model](docs/security/threat-model.md) defines the assets,
trust boundaries, attacker stories, assumptions, and re-evaluation triggers for
these properties.

The [security assurance case](docs/security/assurance-case.md) maps each
published requirement to its design argument, implementation controls, common
weaknesses, and executable evidence. Users can independently verify release
identity through the [release verification procedure](docs/security/release-verification.md).

## Supported Versions

| Version | Supported | Notes |
|---|---|---|
| Doka 10.x | Yes | Maintained .NET 10 package line; upgrade to its latest serviced release for security fixes. |
| Doka 11.x | Prepared | .NET 11 GA source is being qualified; support begins only after approved cutover and publication. |
| Other package majors | No | No published supported line is declared here; a development branch is not a support grant. |

Doka package versions and EF Core versions are separate identities. The Doka
10.x provider and spatial extension accept EF Core `>= 10.0.8` and `< 10.1.0`;
this is not a restriction to Doka package version 10.0.x. The standalone cache
has no EF Core requirement. The prepared 11.x provider/spatial line accepts
EF Core `>= 11.0.0` and `< 11.1.0` and targets .NET 11; its prepared policy
does not alter actual protected-main authority or establish a publication.
All three packages require MySqlConnector
`>= 2.6.1` and `< 3.0.0`.

Maintained majors are serviced independently under the
[support policy](SUPPORT.md#framework-lines-and-servicing), including after a
later framework line becomes main. Publication still requires current active
release-line authority; preparing `maintenance/10.x` does not activate it, and
.NET 11 development does not declare a published Doka 11.x line. EF Core minor
or major upgrades require a deliberate provider response per
[D-009](docs/decisions/D-009-efcore-patch-coupling.md).

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
