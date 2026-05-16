# Security Policy

## Supported Versions

| Version | Supported | Notes |
|---------|-----------|-------|
| 10.0.x  | ✓         | Current; receives security patches under the 90-day coordinated-disclosure window described below. |
| < 10.0  | ✗         | No security backports. Consumers must upgrade to the supported line. |

The supported line tracks the active major of `Microsoft.EntityFrameworkCore.Relational`. EF Core minor bumps (10.1.x, 10.2.x, ...) require a deliberate provider response per ADR D-009 and are evaluated case by case.

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues, pull requests, or discussions.**

Two private channels are available:

1. **GitHub Security Advisories** (preferred for technical disclosures with reproducer):
   [https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/security/advisories/new](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/security/advisories/new)

2. **Email** (when GHSA is not available to the reporter, e.g. external researcher without a GitHub account):
   `kdominic@gmx.de`

Include as much of the following as possible:

- A description of the vulnerability and its potential impact.
- Steps to reproduce or a proof-of-concept (PoC) repository / Gist.
- Affected provider versions and affected engines (MySQL or MariaDB, specific server version, Galera / MaxScale / pooler topology if applicable).
- Any known mitigations the consumer can apply before a fix lands.
- Whether you intend to publish the finding yourself and on what schedule.

### PGP / encrypted reporting

A pre-published long-lived PGP key is not currently maintained for this project. If your disclosure requires PGP-encrypted transport, open the initial contact through GHSA or send a short clear-text email signalling the intent to encrypt; the maintainer responds with a freshly minted public key fingerprint on the disclosure thread and from that point all subsequent attachments and follow-ups stay encrypted. The bilateral-key-exchange path keeps the key material scoped to the active disclosure, avoids the stale-fingerprint problem long-lived keys carry, and works for every reporter regardless of whether they already use PGP day-to-day.

## Coordinated Disclosure Timeline

| Stage | Target SLA | Notes |
|-------|-----------|-------|
| Acknowledgement of report | 5 business days | The maintainer confirms receipt and opens the disclosure thread (GHSA or email). |
| Triage outcome | 10 business days | Vendor-confirmed, vendor-rejected, or "needs more information" with a follow-up question. |
| Fix availability | 90 days from triage-confirmed | Earlier when feasible. Extensions are coordinated with the reporter when fix complexity warrants. |
| Public advisory + release | Aligned with the fix release | Reporter is credited unless they request otherwise. |

Embargo applies for the entire window from triage-confirmed to public-advisory. Reporters are asked to honor the embargo; the maintainer commits to the same direction (no early disclosure, no leak of reporter identity, no use of the report outside the fix path).

If the maintainer misses an SLA stage materially (no response after 10 business days following the acknowledgement window), the reporter is free to escalate publicly with no objection from the project.

## Out of Scope

- Vulnerabilities in `Microsoft.EntityFrameworkCore.*` core packages -- report those to Microsoft directly through the [MSRC Researcher Portal](https://msrc.microsoft.com/report).
- Vulnerabilities in `MySqlConnector` -- report to the [MySqlConnector project](https://github.com/mysql-net/MySqlConnector/security/advisories).
- Vulnerabilities in `NetTopologySuite` -- report to the [NTS project](https://github.com/NetTopologySuite/NetTopologySuite/security/advisories).
- Vulnerabilities in the MySQL or MariaDB server itself -- report to the upstream vendor.
- Issues in third-party hosting that are not caused by this provider.

Reports that turn out to be in scope of a sibling project after triage are forwarded to the right channel with the reporter copied; credit travels with the report.
