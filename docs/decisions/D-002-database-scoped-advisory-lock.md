# D-002 — Database-scoped Advisory-Lock-Naming

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `MySqlHistoryRepository` migration serialization
- **Related work:** Welle 1 / PR 1.1 (workflow phase id=10)

## Context

`SELECT GET_LOCK(name, timeout)` in MySQL and MariaDB acquires a session-scoped advisory lock keyed by `name`. The name itself is **server-instance-global**: two unrelated applications running on the same MySQL server can collide on the same lock name even when they target separate databases.

Until this decision the provider used the constant `__ef_migrations_lock` for every consumer, regardless of database name. A multi-tenant deployment that shares a MySQL host between several databases observed two failure modes:

1. Migration on tenant A pinned the lock; tenant B's `MigrateAsync()` waited for the full lock timeout, then surfaced a `TimeoutException` even though the two tenants had no functional overlap.
2. A misbehaving consumer that held the lock indefinitely effectively denied service to every other tenant on the same host.

This is the BLOCKER B1 from the v1.0 review.

## Decision

The advisory lock name is now derived per database from the connection string:

```
__ef_migrations_lock:{databaseName}
```

When the combined length would exceed MySQL's 64-character `GET_LOCK` limit, the database name is replaced by the lower-case hex representation of the first 8 bytes of its SHA-256 digest:

```
__ef_migrations_lock:{sha256_first8_hex}
```

The derivation lives in `MySqlAdvisoryLockNaming.BuildLockName(string? connectionString)` (`Internal/Migrations/MySqlAdvisoryLockNaming.cs`) so it can be reused from tests that need to observe the same lock name on a separate session.

In addition, both `GET_LOCK` and `RELEASE_LOCK` are now invoked with parameterized SQL (`@name` + `@timeout`) instead of string interpolation. This eliminates the lurking injection risk that would otherwise surface as soon as the lock name became user-derived.

## Consequences

### Positive

- Multi-tenant deployments no longer block each other during migrations.
- The migration lock surface is parameterized and no longer dependent on string interpolation for safety.
- The SHA-256 fallback keeps the lock name valid for arbitrarily long database names, including the cases where the database name itself is generated.

### Negative

- Existing applications upgrading from a pre-v1.0 release will, during the upgrade window, hold the legacy global lock alongside the new database-scoped lock. Concurrent migrations during that window may still serialize globally; the recommendation is to roll the upgrade through a single migration run before resuming concurrent rollouts.
- The lock name now depends on the connection-string-supplied database name. Connection strings without a database name fall back to the prefix alone (`__ef_migrations_lock:`) so the failure mode is greppable rather than silently re-introducing the legacy global behavior.

### Neutral

- The lock name remains observable through `IS_USED_LOCK(name)` and `GET_LOCK(name, 0)` on a separate session; tests use `MySqlAdvisoryLockNaming.BuildLockName(connectionString)` to reproduce the name.

## Re-evaluation triggers

- Microsoft releases an EF Core patch that changes the database-name resolution path consumed by `MySqlHistoryRepository.Dependencies.Connection.DbConnection.ConnectionString`.
- Operator feedback during the v1.0 beta documents a migration-lock-collision scenario the database-scoped name does not cover (for example, sharded deployments where multiple application instances share a single database name).
- Future support for a connection-pooler that re-routes the dedicated migration connection to a different backend session; in that case the lock would need to move to a transaction-scoped or table-row-locking mechanism that survives session migration.

## Alternatives considered

- **Keep the global lock and document the multi-tenant limitation.** Rejected: the failure mode is silent and surfaces as a hard `TimeoutException` only after the lock timeout elapses, which is a poor operator experience.
- **Make the lock name user-configurable via `MySqlDbContextOptionsBuilder.UseMigrationsLockName(string)`.** Rejected as a default: convention-over-configuration is preferable, and the user-configurable knob can be added later without breaking existing consumers. The current API still leaves room for that knob if a future ADR introduces it.
- **Use an `INFORMATION_SCHEMA` row lock or a dedicated row in `__EFMigrationsHistory` as the serialization point.** Rejected: row-locking semantics require an open transaction for the lock lifetime, which conflicts with EF Core's `IHistoryRepository` contract that expects the lock to outlive a transaction.
