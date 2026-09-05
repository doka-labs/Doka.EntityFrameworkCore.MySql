# Supported Databases

The provider supports every actively maintained MySQL and MariaDB LTS line in
the matrix below. Support attaches to the release line, while qualification is
performed against an exact, digest-pinned patch image. A patch pin moves only
after the complete repository contract has passed against the replacement.

## Active LTS Matrix

| Engine | Supported line | Qualified patch | GA | Vendor maintenance or support horizon | Test target |
| --- | --- | --- | --- | --- | --- |
| MySQL | 8.4 LTS | 8.4.11 | 2024-04-30 | Extended Support through Apr 2032 | `mysql84` |
| MySQL | 9.7 LTS | 9.7.2 | 2026-04-21 | 5 years Premier plus 3 years Extended; exact chart row pending | `mysql97` |
| MariaDB | 10.11 LTS | 10.11.19 | 2023-02-16 | Community maintenance through 2028-02-16 | `mariadb1011` |
| MariaDB | 11.4 LTS | 11.4.12 | 2024-05-29 | Community maintenance through 2029-05-29 | `mariadb114` |
| MariaDB | 11.8 LTS | 11.8.8 | 2025-06-04 | Community maintenance through 2028-06-04 | `mariadb118` |
| MariaDB | 12.3 LTS | 12.3.2 | 2026-05-29 | TBC by the MariaDB Foundation | `mariadb123` |

Oracle documents 8.4 and 9.7 as consecutive MySQL LTS series. The MariaDB
Foundation identifies 10.11, 11.4, 11.8, and 12.3 as LTS series; 12.3.2 is the
first 12.3 GA release. The support-series matrix was reconciled against the
primary sources on 2026-08-12; the MariaDB 10.11 qualified patch was updated
against the release history on 2026-09-05.

The horizon column preserves the vendors' distinct terminology. Oracle's
published chart gives month-level Premier and Extended Support dates for 8.4;
its LTS policy gives the same five-plus-three-year term for 9.7, but the chart
has not yet published a 9.7 row. MariaDB publishes Community maintenance dates
and still marks the newly released 12.3 horizon as TBC.

MySQL 8.0 and MariaDB 10.6 are legacy compatibility lines. They are not part of
the advertised support matrix even when a vendor offers a separate commercial
or extended-support arrangement. Other innovation, short-term, preview, RC,
and future lines remain unvalidated.

## Qualification Contract

The release contract prevents a documented line from becoming an untested
claim:

- unit contracts classify every supported, legacy, unvalidated, and future
  release line before provider options are accepted;
- upstream EF Core specification conformance and standalone live functional
  tests run for every target, exact discovery contracts bind every target at
  both supported EF Core patch endpoints, and the floating patch matrix repeats
  live behavior on one target from each engine family;
- live integration, migration deployment, and runnable-example matrices own
  isolated databases for all six targets;
- the scheduled container matrix repeats the full configuration and failure
  contract across all six exact images;
- the hosted performance matrix runs the complete workload contract against
  every exact LTS target, with same-run reference and candidate measurements;
  and
- a release candidate requires the complete six-target integration and example
  matrices before package evidence can be assembled.

The regular integration smoke uses MySQL 8.4 plus MariaDB 11.8 as fast
engine-family representatives. The scheduled benchmark smoke instead derives
all six exact LTS targets from the performance contract. It remains a short,
non-qualifying path and does not replace the hosted scorecard or release
qualification.

## Binary16 Default Metadata

The provider emits and stores a Binary16 Guid default in RFC 4122 byte order on
all six active targets. Catalog text is not a uniform semantic source:

| Targets | Stored 16-byte value | `COLUMN_DEFAULT` / `SHOW CREATE` text |
| --- | --- | --- |
| MySQL 8.4 / 9.7 | Complete | The catalog default is truncated; `SHOW CREATE` retains the complete hexadecimal default |
| MariaDB 10.11 / 11.4 | Complete | Connector-visible catalog and `SHOW CREATE` text do not expose a complete round-trippable hexadecimal literal |
| MariaDB 11.8 / 12.3 | Complete | The complete textual hexadecimal expression is preserved |

This is an engine metadata boundary, not a type-mapping defect. Provider tests
separately verify generated DDL, inserted bytes, catalog representation, and
`SHOW CREATE TABLE` on every target. Consumers performing strict schema
comparison must fail closed where metadata is lossy; they must not rewrite the
correct literal or infer equality from a truncated value.

Compose is the Dependabot-maintained source for the exact container names and
SHA-256 manifest-list digests. `TestDatabaseImages.cs` mirrors those pins for
test-owned containers, and the image-pin gate reconciles every applicable
workflow and performance-contract copy. Test results record the actual image
identity used by each run.

## Unsupported Execution

Provider configuration rejects a version outside the matrix by default.
Applications can deliberately select
`MySqlServerVersionCompatibilityMode.AllowUnsupported`, but that mode carries
no compatibility guarantee and emits the structured
`MySqlEventId.UnsupportedServerVersion` diagnostic. It is an explicit
diagnostic escape hatch, not an additional support tier.

Managed services are supported only when they expose a verified database line
and do not introduce service-specific behavioral differences. Azure Database
for MySQL and Amazon Aurora MySQL are not inferred to be compatible merely
from their version strings; their public contract remains outside this matrix
until provider-owned live canaries exist.

## Primary Sources

All sources were retrieved on 2026-08-11. The current MySQL patch downloads
and their exact release-note pages were reverified on 2026-08-12. MariaDB
10.11.19 release status was reverified on 2026-09-05.

- [MySQL release model and LTS policy](https://dev.mysql.com/doc/refman/9.7/en/mysql-releases.html)
- [MySQL 8.4.0 GA release notes](https://dev.mysql.com/doc/relnotes/mysql/8.4/en/news-8-4-0.html)
- [MySQL 8.4.11 release notes](https://dev.mysql.com/doc/relnotes/mysql/8.4/en/news-8-4-11.html)
- [MySQL 8.4 LTS downloads](https://dev.mysql.com/downloads/mysql/8.4.html)
- [MySQL 9.7.0 GA release notes](https://dev.mysql.com/doc/relnotes/mysql/9.7/en/news-9-7-0.html)
- [MySQL 9.7.2 release notes](https://dev.mysql.com/doc/relnotes/mysql/9.7/en/news-9-7-2.html)
- [MySQL 9.7 LTS downloads](https://dev.mysql.com/downloads/mysql/9.7.html)
- [MySQL 8.4 data type defaults](https://dev.mysql.com/doc/refman/8.4/en/data-type-defaults.html)
- [MySQL 8.4 `INFORMATION_SCHEMA.COLUMNS`](https://dev.mysql.com/doc/refman/8.4/en/information-schema-columns-table.html)
- [Oracle Lifetime Support Policy for Technology Products](https://www.oracle.com/us/support/library/lifetime-support-technology-069183.pdf)
- [MariaDB Server maintenance policy](https://mariadb.org/about/)
- [MariaDB Server release history](https://mariadb.org/mariadb/all-releases/)
- [MariaDB 12.3 LTS announcement](https://mariadb.org/mariadb-server-12-3-lts-released/)
- [MariaDB `INFORMATION_SCHEMA.COLUMNS`](https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-columns-table)
- [MariaDB `CREATE TABLE`](https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-table)
