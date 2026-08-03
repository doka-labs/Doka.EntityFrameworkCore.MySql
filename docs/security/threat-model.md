# Threat Model

- **System:** `Doka.EntityFrameworkCore.MySql`
- **Scope:** Provider repository and shipped NuGet package code
- **Last reviewed:** 2026-08-01
- **Review cadence:** Every supported EF Core major, new engine family,
  security-boundary change, or confirmed vulnerability

## Purpose

`Doka.EntityFrameworkCore.MySql` is an Entity Framework Core relational
provider and optional NetTopologySuite extension for MySQL and MariaDB. It is a
library embedded in a consumer process, not a hosted service. It does not own
application authentication, authorization, tenant isolation, ingress, or
network policy.

The provider must preserve the trust decisions made by EF Core and the
consuming application. Values must remain parameterized, identifiers and
literals must be escaped for MySQL syntax, non-parameterizable grammar tokens
must pass bounded validation, hostile database metadata must not become
executable generated code, and provider-owned telemetry must not expand access
to credentials, SQL, customer identifiers, or database object names.

## Protected Assets

- Confidentiality and integrity of data reachable through the application's
  database principal.
- Integrity of generated SQL, migrations, reverse-engineered models, and
  generated C# source.
- Availability and single-execution semantics of transactions, retries,
  migration locks, and connection lifecycle operations.
- Confidentiality of connection credentials, SQL, parameter values, database
  names, object names, and exception payloads crossing telemetry boundaries.
- Integrity of source, dependencies, CI evidence, packages, checksums, SBOMs,
  and attestations produced by repository workflows.

## Trust Boundaries

### Application and EF Core to provider

Application configuration, model metadata, LINQ expression trees, entity
state, migration operations, connection strings, `DbConnection`, and
`MySqlDataSource` values enter the provider. Ordinary entity and query values
may contain attacker-controlled application data. Model and migration metadata
are normally developer-controlled, but the provider still treats every value
according to its SQL context.

Intentional raw SQL APIs such as `FromSqlRaw`, `ExecuteSqlRaw`, and migration
`SqlOperation` transfer SQL ownership to the application. The provider cannot
make intentionally raw SQL safe after that boundary is crossed.

### Provider and connector to database server

Generated commands cross into MySqlConnector and a database server under an
operator-selected identity. Database endpoint selection, TLS policy,
authentication material, and server privileges remain operator
responsibilities. The provider must not broaden the configured privileges or
weaken connector security options.

A compromised or untrusted database can return adversarial values and
metadata. Reverse engineering must treat names, comments, defaults, computed
expressions, and constraints as data until a trusted developer reviews the
generated model and source.

### Reverse engineering to generated source

Database metadata crosses a design-time code-generation boundary. Metadata
used as C# identifiers must pass EF Core identifier generation. Metadata used
as C# strings must pass `ICSharpHelper` or an equivalent literal encoder.
Provider post-processing must not reintroduce unescaped metadata.

### Provider to telemetry exporters

Provider logs, traces, and metrics cross into application-selected exporters,
which can have broader access and retention than the database. The canonical
field and privacy policy is `docs/operations/observability-contract.json`.

Provider-owned telemetry excludes SQL, query parameters, connection strings,
raw database, schema, and object names, usernames, exception messages, stack
traces, and exception objects. Object-bearing diagnostics use stable opaque
scope identifiers. Invalid-configuration diagnostics use bounded reason and
connection-path vocabularies. Detailed validation information remains in the
exception returned to application code.

### Repository to local and hosted automation

Repository content crosses into local hooks, build tools, package restore, and
GitHub Actions. Pull-request contributors can modify scripts and project files
executed by CI. Untrusted validation workflows therefore require read-only
repository permissions, immutable third-party action references, and no access
to release credentials. Release-write permissions belong only to separately
controlled publication workflows. NuGet publication runs manually from trusted
`main`, obtains only a short-lived OIDC credential, and accepts a candidate run
only when its repository, workflow, successful attempt, source commit, semantic
tag, manifest, package metadata, and hosted attestations agree. Attestation
verification pins the signer workflow, signer and source commit, tagged source
ref, and GitHub-hosted runner class rather than trusting repository ownership
alone. Public readback independently derives the Portable PDB lookup key and
SHA-256 checksum from each candidate assembly; it does not trust upload success
or primary-package visibility as proof that NuGet.org indexed matching symbols.

## Security Invariants and Controls

### SQL generation

- Ordinary query and entity values remain parameters or use the owning
  relational type mapping's literal encoder.
- Identifiers pass the centralized backtick-delimiting and escaping helpers.
- Charsets, storage engines, and query, table, and column collations pass the
  shared ASCII grammar-token validator before SQL emission.
- JSON and spatial paths use context-specific escaping rather than generic
  string replacement.
- Raw SQL surfaces remain explicit and application-owned.

### Migrations and advisory locks

- Identifiers are delimited and non-parameterizable tokens are validated.
- `GET_LOCK` and `RELEASE_LOCK` values are parameters.
- Migration lock names are database-scoped and length-bounded.
- Telemetry uses a pseudonymous lock scope, never the raw lock or database
  name.
- Lock ownership uses a dedicated non-pooled connection and serialized
  lifecycle operations.
- Retry and commit-unknown behavior must never silently duplicate unsafe
  writes or schema operations.

### Reverse engineering

- Scaffolding filters are parameters.
- Metadata-derived identifiers and literals use EF Core code-generation
  services.
- Missing objects and unsupported metadata produce bounded diagnostics without
  exposing raw names.
- Generated code and migrations remain reviewable artifacts; silent executable
  source generation from hostile metadata is forbidden.

### Database result materialization

- Database-backed JSON, spatial values, and metadata remain untrusted after the
  connector has decoded the wire representation.
- Spatial WKT and WKB cross one iterative provider guard before recursive
  NetTopologySuite parsing. The guard permits at most 256 structural parser
  frames, including the leaf geometry, and rejects deeper input without echoing
  the payload.
- Process-wide provider caches have explicit cardinality bounds. Exact engine-
  version profiles retain at most 128 entries while preserving the requested
  version on every returned profile.

### Connections, authentication, and transport

- The provider preserves supported MySqlConnector TLS, authentication,
  certificate, pooling, reset, failover, cancellation, and timeout options.
- Exactly one connection path is active.
- Process-wide HiLo state includes connector protocol, protocol-specific
  endpoint, server, port, database, and user identity without retaining a
  password.
- Connection strings and exception payloads do not enter provider-owned
  telemetry.
- Cancellation and timeout exceptions are not classified as transient retries
  by default.

### Build and dependency integrity

- Dependencies are centrally governed and audited for known vulnerabilities.
- GitHub Actions and database images use immutable references.
- Workflow permissions follow least privilege.
- Builds, packages, SBOMs, checksums, and release evidence are reproducible and
  cross-checked before publication.
- Candidate qualification and NuGet publication are separate manual workflows;
  publication rejects stale, side-branch, failed, cross-repository, or
  conflicting same-version artifacts before requesting an OIDC credential.
- Public-package readback compares canonical payload content, downloads the
  exact checksum-bound Portable PDBs from NuGet.org's symbol server, restores
  both exact package versions into an empty cache, and executes the provider
  and spatial runtime contract against the pinned MySQL 8.4 image.
- Test, benchmark, and generated evidence never substitute for the shipped
  source revision they claim to validate.
- Bundled database services publish repository-known test credentials on IPv4
  loopback only; wider host-network exposure requires an explicit override.

## Attacker Stories

The security test and review inventory includes:

- Attacker-controlled query values escaping parameterization.
- A crafted collation, charset, storage engine, identifier, JSON path, or
  literal changing SQL grammar.
- Hostile database metadata becoming executable generated C# or migration SQL.
- Credentials, SQL, customer identifiers, or raw object names entering logs,
  traces, metrics, exception attachments, or unbounded tags.
- Retry, cancellation, pool reset, failover, or commit-unknown handling
  duplicating a write or leaking an unsafe connection state.
- Concurrent or killed migrators retaining a lock or allowing overlapping
  schema changes.
- A mutable dependency, action, image, or artifact replacing repository or
  package evidence.
- A replayed candidate run ID, moved tag, stale `main` commit, or partial NuGet
  publication acquiring authority for different package bytes.

## Assumptions and Residual Ownership

- Applications grant database accounts only the privileges they need and
  select an appropriate transport-security mode.
- Operators protect credentials, certificate material, networks, database
  servers, and telemetry backends.
- EF Core and MySqlConnector enforce their documented contracts. Provider
  misuse of those contracts is in scope; vulnerabilities inside the upstream
  implementation are routed upstream.
- Developers review reverse-engineered code and migrations before production
  execution. The provider must still preserve that review boundary.
- Application-enabled EF Core sensitive-data logging and connector-owned
  diagnostics are separate opt-in surfaces controlled by the application.
- Test credentials and certificates are valid only for isolated test-owned
  resources and are never production secrets.

## Severity Calibration

### Critical

- Reliable code execution in a consuming application or build from ordinary
  runtime input or database metadata without a separate trusted decision.
- Broad release-channel compromise that can replace packages or attestations.

### High

- SQL injection from ordinary parameterized query or entity values with major
  confidentiality or integrity impact under a typical application principal.
- Provider telemetry reliably exposing credentials or private key material.
- Retry or lock behavior reproducibly duplicating destructive operations under
  normal failure handling.

### Medium

- Injection through developer-controlled metadata that bypasses the normal
  migration or generated-code review expectation.
- Provider telemetry expanding access to SQL, database names, or customer
  identifiers without directly exposing credentials.
- Unbounded parsing, allocation, recursion, or retry behavior reachable from
  ordinary application data.

### Low

- Security hardening gaps limited to local tooling, test fixtures, or unusual
  operator-controlled configuration with no production-default impact.
- Diagnostic detail that becomes useful only after database or host compromise
  and does not cross a stronger boundary.

## Out of Scope

- Browser-only threats such as XSS, CSRF, clickjacking, open redirects, and
  cookie policy unless provider output directly creates the unsafe condition.
- Authentication or authorization errors owned entirely by a consuming
  application.
- MySQL, MariaDB, EF Core, MySqlConnector, or NetTopologySuite vulnerabilities
  that the provider neither introduces nor integrates unsafely.
- Operator-selected plaintext transport, over-privileged database accounts, or
  insecure network policy that the provider does not override.

## Re-evaluation Triggers

Re-evaluate this model when any of the following changes:

- A supported EF Core major, database engine family, connector major, or target
  framework.
- SQL generation, raw SQL, migration, scaffolding, code generation, retry,
  transaction, connection, authentication, TLS, or telemetry boundaries.
- Package publication, signing, attestation, workflow permissions, or secret
  access.
- The public support matrix or security reporting policy.
- A confirmed vulnerability, bypass, privacy incident, or material upstream
  advisory affecting an in-scope boundary.
