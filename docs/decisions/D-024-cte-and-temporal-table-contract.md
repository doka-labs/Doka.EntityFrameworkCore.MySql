---
id: D-024
status: accepted
date: 2026-08-04
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Common-table-expression, directed-join, and temporal-table provider contracts"
supersedes: []
superseded-by: []
amends: [D-004]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-024 -- Support native CTEs, directed joins, and portable temporal tables

## Context and Problem Statement

Common table expressions and temporal tables are required provider features.
The repository previously exercised neither feature through a complete public
provider contract. A test named after a non-recursive CTE generated a `UNION`
query instead of a `WITH` expression, while temporal-table references covered
only ordinary date and time mappings.

The engines have different capabilities. MySQL and MariaDB both support
non-recursive and recursive CTE syntax. MariaDB also supports native
system-versioned tables. MySQL 8.4 does not provide the corresponding native
table feature, so portable provider behavior requires a provider-owned history
table and trigger implementation rather than an unsupported-provider result.

A later concrete consumer also needs the same completed SQL source in several
references: once for scalar bound aggregates and again for exact result pairs,
or in disjoint `UNION ALL` branches. The source can contain duplicates, paging,
and volatile computed values. Repeating its expression is not equivalent to
binding those occurrences once. Native MariaDB 11.8.9 controls found that an
ordinary CTE, including a window fence, can be cloned per reference; a recursive
CTE with an empty recursive arm retained the selected values. MySQL 8.4.11 also
retained those values with the empty-arm form. This semantic requirement is
separate from choosing indexes or proving a selective query plan.

The same consumer requires one chosen join input to precede the other. Source
binding alone cannot express that direction; a materialized relation can still
be scanned in a poor join order. A generic directed inner-join operator exposes
that separate engine capability while retaining EF's ordinary relational join
and result-shaping semantics. It does not encode the consumer's hierarchy or
its index policy in the provider.

## Decision Drivers

- The provider must not be the limiting factor when an engine can perform the
  requested operation.
- Engine limitations and provider implementation status must remain separate.
- CTE composition must retain EF Core parameterization, tracking, async, and
  query-pipeline behavior.
- Repeated references to an explicitly bound source must preserve its original
  occurrences, paging, native mappings, and stored volatile values.
- Bound entity references must preserve exact named mapping identity and the
  original temporal/application-time root semantics through navigation and
  entity set-operation validation.
- A consumed source-binding capability must not grow into a general CTE DSL,
  application data cache, session-table protocol, or domain-specific query API.
- One consumed join direction must remain local to its selected SQL edge,
  without a global join-order modifier or a second join/shaping implementation.
- Temporal queries need one public, engine-independent EF-style contract.
- MariaDB must use its native system-versioning syntax and semantics.
- MySQL must receive equivalent provider semantics through explicit emulation.
- Schema operations that cannot preserve temporal history safely must fail
  before destructive SQL is executed.
- Reverse engineering must reconstruct temporal metadata rather than silently
  returning an ordinary table model.

## Considered Options

- Native CTEs plus native-or-emulated temporal routing
- Expose only raw SQL and native MariaDB temporal tables
- Treat CTEs and temporal tables as unsupported provider features

## Decision Outcome

Chosen option: "Native CTEs plus native-or-emulated temporal routing", because
it preserves native engine behavior while presenting one complete provider
contract on every supported engine.

CTEs retain EF Core's existing composable SQL surface. The provider recognizes
`WITH` and `WITH RECURSIVE`, validates the configured engine boundary, and
retains parameter binding through `FromSql`, `FromSqlRaw`, `SqlQuery`, and
`SqlQueryRaw`.

For the concrete shared-source requirement, add one source-binding operator:
`MySqlQueryableExtensions.WithMaterializedSource<TSource, TResult>(
IQueryable<TSource>, Expression<Func<IQueryable<TSource>, IQueryable<TResult>>>)`.
The expression parameter gives references lexical identity without public CTE
names, arbitrary recursion, or a second query language. The original decision
against a general LINQ-to-named-CTE DSL remains: the added operator implements
a demonstrated evaluation contract that repeating an ordinary IQueryable does
not provide, rather than duplicating EF's query-shaping operators.

The source is a completed, flat SQL relation, including its filters, duplicate
occurrences, projected SQL values, and ordered page. Mapped entities and flat
scalar projections retain native type mappings, nullability, collations,
converters, and EF result identifiers. Reference filters do not alter other
references. Independent bindings remain independent. Needed volatile source
values are stored once for the bound occurrence; unused expressions and SQL
side effects have no evaluation-order guarantee. Composed result ordering must
be explicit, independently of the source page ordering. Bound entities retain
the exact `IEntityType`, including named shared mappings, and their original
query-root lineage. `TemporalAsOf` navigation retains the source instant;
incompatible temporal entity set roots remain invalid. Application-time
mutation/navigation and range-compatibility guards remain authoritative.

Construction performs no I/O and returns `IQueryable<TResult>`. Normal EF
terminal operators execute one parameterized statement per execution attempt,
with the caller's exact cancellation token, execution strategy, transaction,
and snapshot. There is no source preload, client evaluation fallback, extra
connection, or temporary-session-table lifetime. Foreign provider/context use,
escaped references, deferred client/collection source projections, contributing
collection/non-owned-reference eager-loading sources, and multi-command split
results fail before SQL execution. Supported owned-reference sources retain
their captured values. Reference `Include` on composed output remains valid,
including after binding; only collection `Include` directly on a bound entity
is rejected. Normal entity/reference/collection output and scalar rejoins to
mapped entities still follow EF's single-query shaping rules. The guide records
the supported source/output distinction in detail.

The provider lowers the binding to a native recursive CTE with an explicitly
typed empty recursive arm and `UNION ALL`. This keeps one selected source bag
without deduplicating its occurrences, and avoids MariaDB's observed cloning
of ordinary multi-reference CTEs. The lowering is internal; it guarantees no
particular physical temporary-table count, index use, or query complexity.
Every supported engine line requires its own live qualification.

For the independent direction requirement, add
`MySqlQueryableExtensions.StraightJoin<TOuter, TInner, TKey, TResult>` with the
ordinary EF `Join` sources, key selectors, and result selector. Both inputs
must use the same asynchronous EF query-provider instance. Construction is
synchronous and performs no I/O; normal EF terminal operators execute later.
The provider reuses EF's join translation, then replaces only its selected
inner-join edge with `MySqlStraightJoinExpression`. Matching, duplicate
multiplication, key/null semantics, filters, projections, paging, tracking, and
navigation shaping retain the corresponding EF `Join` contract. MySQL and
MariaDB emit `STRAIGHT_JOIN` for that edge; other edges remain ordinary joins.
No global `SELECT STRAIGHT_JOIN` modifier is added. Result ordering, snapshot,
source materialization, index choice, and performance are separate concerns.
The engine directive can still restrict optimizer transformations elsewhere:
MySQL's documented semijoin/antijoin eligibility excludes the join type in the
outer query. Qualification must inspect the complete consuming plan.

Explicit `EF.CompileQuery` delegates around `FromSql` or temporal `DbSet`
extensions are not part of this provider contract. EF Core replaces a `DbSet`
receiver with an `IQueryable` query root while funcletizing a compiled query,
then rejects the extension call before provider translation because its method
parameter remains `DbSet<T>`. The official EF Core 10 SQL Server temporal API
uses the same receiver contract. Normal query execution still uses EF Core's
query-shape compilation cache. That `DbSet` receiver boundary does not by itself
apply to the new `IQueryable`-based source-binding operator. Its compiled-query
shapes must be qualified against EF's own constraints. The binding uses normal
parameter extraction and deterministic statement-local identities and aliases;
it introduces no separate query/data cache or retained context state.

MySQL 8.4 / 9.7 also accept CTEs as read sources for `UPDATE` and `DELETE`.
MariaDB 12.3 provides the corresponding data-modification grammar; MariaDB
10.11 / 11.4 / 11.8 do not. The provider exposes the available behavior and
records the older MariaDB boundary as an engine limitation, not as missing
provider support.

Temporal tables use public model-builder extensions for history-table and
period-column metadata plus query roots for `AsOf`, `All`, `FromTo`, `Between`,
and `ContainedIn`. Temporal query roots are no-tracking because one result can
contain several historical versions with the same primary key.

MariaDB 10.11 / 11.4 / 11.8 / 12.3 use `WITH SYSTEM VERSIONING`, `PERIOD FOR
SYSTEM_TIME`, and `FOR SYSTEM_TIME` queries. MySQL 8.4 / 9.7 use provider-owned
history tables and triggers whose names, annotations, rollback behavior, and
scaffolding contract are deterministic. Both routes share the same public
metadata and query API.

MariaDB introduced the dedicated [PERIODS][mariadb-periods] and
[KEY_PERIOD_USAGE][mariadb-key-period-usage] information-schema catalogs in
11.4, after application-time periods themselves. Reverse engineering therefore
uses those catalogs on 11.4 and later. On 10.11 it reads `ROW START` / `ROW END`
from `GENERATION_EXPRESSION` and parses only grammar tokens from canonical
`SHOW CREATE TABLE` output. The missing bulk catalogs require one metadata
round trip per selected table on 10.11. `SET STATEMENT` fixes the rendering SQL
mode for each query without mutating the caller-owned session and clears
`ANSI_QUOTES`, so server-rendered identifiers cannot be mistaken for quoted
string literals.

MariaDB application-time periods and bitemporal tables use a separate typed
provider contract. Model-builder extensions configure the application period,
`WITHOUT OVERLAPS`, and both dimensions of a bitemporal table. Migrations,
reverse engineering, generated model code, and typed `FOR PORTION OF`
`ExecuteUpdate` / `ExecuteDelete` roots preserve that contract. MySQL rejects
these exact operations as engine limitations because MySQL 8.4 / 9.7 have no
application-time period grammar.

Action-based application-time configuration resolves caller-selected endpoints
before adding conventional defaults. This keeps explicit CLR or named period
properties as the exact physical schema while retaining `ValidFrom` or
`ValidTo` for any endpoint the callback intentionally leaves unspecified.

Migration snapshots and migration designer models emit system-time,
application-time, and bitemporal metadata through the provider annotation code
generator. That is the design-time seam EF Core invokes for both artifacts.
The generated public Fluent API is compiled and compared with the source model;
an unchanged temporal model must produce no migration operations.

System-time mapping belongs to the physical table. A convention-owned
`OwnsOne` mapped to the owner's table therefore inherits the owner's history
and period contract. Explicit conflicts remain invalid. A separately stored
owned type does not inherit the contract, and temporal entity materialization
rejects its implicit current-only expansion. Scalar projections that do not
materialize that separate owned navigation remain valid.

### Consequences

- Good, because supported engines expose one provider contract without hiding
  native-versus-emulated behavior.
- Good, because CTEs remain inside EF Core's parameterization and composition
  pipeline instead of introducing string concatenation or a parallel query DSL.
- Good, because temporal schema and query differences are isolated behind
  typed capabilities and annotations.
- Good, because one narrow source-binding contract preserves bags, paging,
  native values, and multi-reference evaluation without a general CTE DSL.
- Good, because one directed edge retains EF's join/shaping semantics without
  changing the SQL kind of unrelated joins or exposing a global directive.
- Bad, because a directed edge constrains the optimizer and can disable other
  transformations; it requires representative complete-query plan evidence.
- Bad, because forced materialization consumes server resources and does not
  make every consuming query faster or selective.
- Bad, because provider-owned relational expression handling depends on exact
  EF translation, cloning, and pruning behavior across the supported EF range.
- Good, because MariaDB application-time and bitemporal behavior is available
  without raw SQL and round-trips through migrations and scaffolding.
- Bad, because MySQL temporal emulation owns trigger correctness, schema-change
  safety, and history-table lifecycle.
- Bad, because reverse engineering must inspect both native metadata and the
  provider's emulation markers.

### Confirmation

- Run capability-boundary tests at MySQL 8.0.0/8.0.1, MariaDB 10.2.1/10.2.2,
  and MariaDB 10.3.3/10.3.4.
- Run live CTE contracts for non-recursive, recursive, multi-CTE, parameterized,
  sync, async, tracking, no-tracking, LINQ-composed, and server-aggregate use on
  every supported engine. Verify that the caller's exact cancellation token
  reaches the relational command boundary. Run data-modification use on MySQL
  and verify the documented MariaDB engine boundary independently.
- Run source-binding unit, functional, and all-six-engine integration contracts:
  volatile values and selection, bags, paging, independent bindings, empty
  sources, native mappings, supported model shapes, tracking, cache/parameter
  reuse, context lifetime, exact cancellation, and one command per attempt.
  Verify that foreign contexts, escaped references, client/collection source
  shapes, contributing collection/non-owned-reference eager-loading sources,
  and split results fail before a command. Distinguish preserved owned-reference
  sources, scalar projections that eliminate source Includes, reference Include
  after binding, and collection Include on an ordinary mapped rejoin target.
- Verify quoted SQL reconstruction, shared definition identity, independent
  top-level quotation scopes, aliases/annotations, and explicit cycle rejection.
  `Quote_preserves_binding_source_schema_and_annotations` must retain original
  string, Boolean, and null annotation values, including the null-valued entry.
  Also exercise EF's actual C# translation path: compiling the quotation as an
  expression alone does not establish generated-C# support. The
  `Actual_csharp_translation_*` cases retain required constructor unsafe
  accessors, parse the generated expression/declarations, reject cyclic graphs,
  and bound definition growth, generated size, and allocations at depths
  4/8/12 after warming the exact reference or owning-SELECT path. These are
  translation and syntax checks, not cross-assembly compilation. Separately,
  `Actual_quote_and_csharp_generator_preserve_the_dependent_source_dag` and
  `Completed_source_annotations_survive_actual_csharp_reconstruction` compile
  and execute the generated C# with Roslyn in a collectible load context under
  the provider's existing test friend-assembly identity. Verify actual source
  payloads, null-valued entries, shared dependency identity, directed-edge
  retention, and equivalent generated SQL. This does not establish arbitrary
  consumer cross-assembly or NativeAOT support. D-017 remains authoritative.
- Run `MySqlStraightJoinTests` against both configured engine families and
  both null-semantics modes: scalar/composite/nullable/converted/constant keys,
  duplicate-preserving projections, paging, mixed ordinary/directed edges,
  tracking and scalar-subquery composition. Verify null arguments,
  LINQ-to-Objects and different provider instances fail without a command.
  Retain synchronous/asynchronous compiled-query composition and dependency
  sharing with materialized sources. Clone, update, TRUE-predicate, and quote
  tests must retain the exact edge, annotations, and pruning metadata.
- Run `Raw_sql_name_collision_preserves_identifier_limit` and execute its
  generated statement on native MySQL and MariaDB. Long reserved-name prefixes
  in valid raw SQL must not force oversized CTE names; verify deterministic
  collision resolution, no physical-name collision, and unchanged ordinary names.
- Run `MySqlMaterializedSourceLineageTests` for exact named/shared mappings,
  identity/join/navigation selectors, AsOf instant propagation, matching and
  incompatible temporal entity set roots, source/outer and navigated-target
  query filters with explicit filter opt-out, and application-time navigation
  and range rejection. Translation tests establish those boundaries without
  I/O; they do not replace native execution qualification.
- Compare EF-generated statements with unbound references on native MariaDB and
  MySQL. Read full results; record plans, source evaluation, intermediate work,
  query-construction allocations, and server memory separately. Include broad,
  deep, balanced, forest, and disjoint-chain data, sparse and dense inputs, and
  one and 1,000 sources. Keep negative controls and timeouts visible; no
  host-dependent latency threshold is a CI gate.
- Run source-binding tests on EF Core 10.0.8 and 10.0.13 and repeat the existing
  runtime-posture checks; NativeAOT remains governed by D-017.
- Run temporal metadata validation and migration round trips for every supported
  engine route.
- Run temporal query-boundary contracts for every query root, UTC precision,
  empty history, repeated keys, invalid shapes, sync, async, server aggregates,
  and exact cancellation-token forwarding.
- Run scaffolding round trips for native MariaDB and provider-emulated MySQL
  schemas.
- Run MariaDB 10.11, 11.4, 11.8, and 12.3 bitemporal contracts through generated
  DDL, version-appropriate metadata readback, generated model code, and typed
  portion update and delete operations.
- Run the complete unit, functional, integration, documentation, trimming, and
  release-candidate verification gates.

## Pros and Cons of the Options

### Native CTEs plus native-or-emulated temporal routing

- Good, because it implements the complete requested provider surface.
- Good, because native engine facilities remain visible in generated SQL.
- Bad, because the MySQL emulation path requires more migration and lifecycle
  tests than a native-only implementation.
- Good, because the consumed source binder expresses one semantic requirement
  that ordinary repeated query expressions cannot preserve.
- Bad, because its native materialization and EF expression seams require
  version-specific regression and performance evidence.

### Expose only raw SQL and native MariaDB temporal tables

- Good, because it minimizes provider-owned SQL generation.
- Bad, because MySQL users receive no portable temporal contract and temporal
  metadata cannot round-trip across supported engines.

### Treat CTEs and temporal tables as unsupported provider features

- Good, because it avoids new provider code.
- Bad, because it violates the provider completeness contract even where the
  engine supports the required behavior.

## More Information

The CTE version boundaries are MySQL 8.0.1 and MariaDB 10.2.2. MariaDB native
system-versioned tables start at 10.3.4. The supported release matrix is newer,
but exact thresholds remain in `EngineProfileTable` so legacy compatibility
mode fails or routes deterministically.

MariaDB `BETWEEN` includes both bounds, while `FROM ... TO ...` excludes the
upper bound. The provider's temporal query expressions preserve those distinct
semantics instead of normalizing them into one approximate range operation.

### Source-Binding Alternatives and Framework Seams

A general named-CTE/recursive LINQ DSL remains rejected. It would introduce
consumer alias management and recursive query semantics that neither active
consumer needs. Repeating the source or using a normal/window-fenced CTE is
also rejected for this contract: the MariaDB native counterexample changes
volatile values between references. A key-only rejoin cannot preserve an
arbitrary projection or identical duplicate occurrences. Client preloading and
session temporary tables change command count, snapshot/lifetime, cancellation,
and memory behavior. A separate provider query cache would compete with EF's
parameter extraction and plan lifetime without solving those semantics.

The preprocessor replaces each bound parameter with a deterministic leaf query
root and records its completed source separately. Each definition is processed
once. Embedding the complete source expression beneath every reference was
rejected after nested-binding measurements exposed repeated traversal of the
same dependency graph. A leaf reference carries identity and row shape, not a
second copy of its source expression. Reference equality/hash work therefore
does not recursively walk that dependency graph.

Entity leaves also retain the exact `IEntityType` and a detached original query
root. Looking up only the CLR type would lose named/shared mapping identity;
replacing the root with a plain entity root would lose temporal semantics.
`SourceOrigin` retains the operation and its boundaries without retaining the
source query provider. `MySqlNavigationExpansionExtensibilityHelper` unwraps
that origin at all three EF hooks: `CreateQueryRoot`,
`ValidateQueryRootCreation`, and `AreQueryRootsCompatible`. AsOf navigation
therefore creates temporal targets at the same instant, while current versus
historical or different-instant entity set roots cannot pass compatibility
checks as ordinary current roots. The same unwrapping retains the existing
application-time mutation-only navigation guard and exact range checks.

Binding queries use EF Core 10's preprocessing sequence with a narrow
`NavigationExpandingExpressionVisitor` subclass. EF creates that visitor inside
`QueryTranslationPreprocessor.Process` and exposes no replacement factory at
that point; the provider mirrors the sequence to insert the bound-root filter
seam. One visitor handles definitions and the composition for the whole
statement. Separate visitors would create independent context-filter runtime
parameter namespaces, allowing different filters to collide. A completed
entity source has already applied its global filters: a query-compilation
context override suppresses that second application only while visiting its
bound entity root, then restores ordinary filtering for other roots. Tracking
and query-filter metadata are normalized in EF's statement-wide precedence
order before the definitions are separated.

The normal relational translator supplies projections, identifiers, mappings,
aliases, and shaping. A canonical source select is pushed down once; reference
clones need only their own aliases and retain the same definition. The CTE table
node participates in visiting, cloning, quoting, and SQL pruning. An
`IMySqlMaterializedSourceVisitor` traversal preserves the complete definition
schema: columns needed by another reference must not disappear when pruning
one use.

Source pushdown unwraps only owned-reference Include wrappers. The outer bound
entity root recreates EF's owned-reference shaping against the captured
navigation projection. Unwrapping an arbitrary non-owned reference Include
would instead drop its related data or reload it independently of the selected
source. Contributing non-owned-reference and collection eager-loading source
shapes therefore fail explicitly. An Include eliminated before a flat scalar
projection does not contribute such a source shape. This source boundary does
not prohibit normal reference Includes on composed output or collection
shaping on an ordinary entity query joined to the bound scalar values.
Unwrapping an owned Include can leave an entity shaper bound to EF's client
projection indexes. Before source pushdown, the translator clears that list
through EF's `ReplaceProjection` API and restores the captured structural
projection under a `ProjectionMember`. Keeping the stale client list would
make EF skip finalizing scalar projections in the composition. This restores
EF's member-projection state; it does not reevaluate the source or discard its
owned navigation values.

The shared SQL definitions form a directed acyclic graph. Each definition owns
a non-static `ConditionalWeakTable` keyed by the actual `ExpressionVisitor`,
memoizing only its immutable rewritten definition. Without that memo, walking
each reference recursively can repeat dependency rewrites exponentially. Weak
keys do not retain visitors after their normal EF lifetime. EF creates fresh
parameter/nullability visitors for execution processing; a result from another
attempt must not be reused. This is graph-traversal bookkeeping, not a query-plan
or row cache, and does not replace EF's compilation cache or share
parameter-sensitive rewrites between processing attempts. Parameter changes,
repeated execution, pooled-context lifetimes, and nested bindings require
explicit regression coverage.

Quotation preserves that same shared-definition graph during SQL-expression
reconstruction. Each top-level `Quote` call creates its own builder and local
definition variables, initialized in dependency order. Repeated references
within that scope reuse the variable; independent top-level calls share no
reconstruction state. An unfinished definition encountered again is an invalid
cycle and fails explicitly. A temporary table-expression quotation proxy lets
EF quote the ordinary select while replacing its bound references with those
local variables, retaining their aliases and annotations. No global factory,
context cache, or data cache owns this bookkeeping.

The reconstruction body is a typed factory lambda invoked through its delegate
`Invoke` method. EF Core 10's precompiled-query generator lifts constants before
C# translation, but the C# translator can call a relational expression's `Quote`
later while rendering an otherwise unknown constant. A bare block returned at
that point can leave declaration statements at top-level expression scope;
`Expression.Invoke` can also be inlined. The typed lambda/delegate method call
keeps declarations inside one reconstruction scope instead. It describes SQL
expression reconstruction, not a separately compiled result delegate or query
execution. Confirmation therefore requires semantic reconstruction and actual
EF C# translation as separate checks; neither establishes NativeAOT support.
D-017 remains unchanged.

`MySqlRelationalExpressionQuoting` reconstructs annotations on provider-owned
materialized-source and directed-join nodes. On both EF Core 10.0.8 and
10.0.13, `RelationalExpressionQuotingUtilities.QuoteAnnotations` requests a
constructor on the `IDictionary<string, IAnnotation>` interface and supplies
an `IAnnotation` wrapper as the newly constructed annotation's value. A local
concrete dictionary alone fixes neither actual C# translation nor payload
identity: EF's `LinqToCSharpSyntaxTranslator` also rejects list initializers
whose `Add` call has more than one argument. The shared provider helper uses
`Dictionary<string, IAnnotation>(IEnumerable<KeyValuePair<string, IAnnotation>>)`
over a list whose `Add` receives one reconstructed pair at a time. Each
annotation retains its original name and object value, including a null-valued
entry. Expression round trips cover string, Boolean, integer, and null
payloads; actual generated-C# compilation and execution cover annotated source
and directed-edge reconstruction. The helper is local to these provider-owned
nodes; it does not replace EF's annotation handling for unrelated expressions.

Generated CTE names use the deterministic binding-ID prefix already selected
for ordinary queries. If that name conflicts with physical names or appears in
trusted raw SQL, collision resolution adds a numeric suffix rather than an
ever-growing run of underscores. A valid raw SQL comment can contain many
underscore-prefixed candidates; repeatedly appending an underscore would let
that input drive the generated name beyond server identifier limits. The
numeric suffix bounds name growth by its numeric representation. Names without
collisions retain their ordinary prefix, and no global alias counter or random
name enters the query shape.

EF Core 10.0.8 and 10.0.13 expose `SelectExpression.SetTables` and `Clone`, and
`SqlTreePruner.PruneSelect` provides the projection-preserving traversal seam.
These are framework seams with explicit consumers: redirect aliased source
references without losing their identifier/type metadata, and traverse the
materialized source without pruning columns that another reference needs.
The preprocessing pass order, navigation visitor's filter/runtime-parameter
state, and fresh parameter-processing visitors are additional versioned EF
seams. Their upstream source, rather than public visibility alone, defines the
behavior the provider qualifies on its EF patch matrix.
The provider owns those narrow uses rather than reflection over EF private
fields, SQL string rewriting, or a parallel expression compiler. Their public
visibility does not promise a stable provider-independent API; changes require
the confirmation tests and re-evaluation below. D-017 remains authoritative for
the experimental NativeAOT boundary.

### Directed-Join Alternatives and Framework Seams

A global `SELECT STRAIGHT_JOIN` modifier is rejected because it would constrain
all joins in the query block. Repeating ordinary `Join` syntax cannot express
the engine direction contract. SQL text replacement would bypass EF's edge,
projection, nullability, and shaping metadata. A second join translator would
duplicate those responsibilities. The selected route lowers the public marker
through ordinary EF join processing, tracks only its marked inner input, and
replaces the resulting `InnerJoinExpression` with an immutable directed node.
Unrelated queries allocate no marker set. One lazy reference-identity set is
shared only by the translation visitor and its subquery visitors, then leaves
with that compilation; it is not model-, context-, or process-global state.

`SelectExpression.SetTables` is the same narrow EF seam already used by source
binding. The join path replaces a copied table array after ordinary
`TranslateJoin` completes, because the setter clears its backing list. Cloning,
visiting, annotation quotation, SQL printing, and pruning retain the directed
node. A TRUE join predicate remains a directed edge rather than becoming an
ordinary cross join. The direction operator and source binder compose without
coupling their contracts, sharing only the provider-owned relational
annotation quotation helper.

### Re-evaluation Triggers

- EF Core adds a provider-independent CTE expression or temporal metadata
  contract that can replace the provider-specific public surface.
- EF changes its preprocessing pass order or adds a navigation-visitor factory
  that permits replacing the mirrored sequence without changing source filters,
  metadata precedence, or statement-wide runtime-parameter names.
- EF changes navigation-filter expansion, parameter/nullability visitor lifetime,
  select cloning, table replacement, projection/identifier pruning, or quoting
  so source-binding, parameter-reuse, nested-allocation, or trim tests fail on a
  supported EF version. Reassess the weak per-visitor rewrite memo if visitors
  become stateful across attempts or require revisiting one node in new state.
- EF changes owned Include/client-projection normalization or
  `ReplaceProjection` semantics so captured owned values or composed scalar
  projections no longer survive member-projection restoration.
- EF changes relational quotation timing, constant lifting, lambda/delegate
  invocation translation, or declaration scoping so semantic reconstruction or
  actual C# translation fails. Replace the local factory seam only with a
  scope-preserving route that retains shared identity without global state.
- EF's annotation quotation helper constructs concrete storage and preserves
  actual payloads on every supported EF version. Prefer that upstream helper
  once string, Boolean, null, and actual C# translation checks prove it can
  replace this provider-node-local routine, including the C# translator's
  single-argument initializer constraint.
- EF changes ordinary join lowering, appended-edge identity, constant-key
  simplification, nullability, cloning, pruning, or subquery-visitor lifetime so
  directed and ordinary join equivalence or mixed-edge tests fail. Prefer an
  upstream directed-edge seam if it preserves the same local contract with
  less provider-owned handling.
- MySQL or MariaDB changes directed join semantics or optimizer restrictions,
  or a consuming native plan regresses; requalify the complete query rather
  than inferring performance from the presence of `STRAIGHT_JOIN` alone.
- Engine identifier rules or source naming change so physical/raw-SQL collision
  tests fail; retain deterministic bounded names rather than lengthening a name
  once per collision.
- An upstream shared-relation expression can replace the provider node while
  preserving bags, mappings, paging, and volatile values with less owned code.
- A qualified engine provides a simpler materialization primitive that passes
  the same repeated-reference tests and improves native work or server memory.
- Actual consuming queries exceed their established query-build allocation or
  native-work budgets; reassess projection width and lowering with paired
  evidence instead of weakening source semantics.
- EF changes entity-root mapping identity, provider detachment, or the three
  navigation-extension hooks so named/shared identity or temporal/application
  lineage tests fail. Restore the original mapping/root semantics rather than
  recreating a root solely from its CLR type.
- A real consumer needs a source shape outside the documented flat-relation
  boundary; decide its semantics and tests before expanding the API.
- MySQL adds native system-versioned tables compatible with the portable
  temporal contract.
- MariaDB changes native history retention or safe `ALTER TABLE` behavior.
- A supported engine cannot preserve temporal history for a required schema
  operation without an explicit migration procedure.

### Decision History

- 2026-08-04: Decision recorded with status accepted.
- 2026-08-05: Added the typed MariaDB application-time and bitemporal contract,
  including overlap constraints, reverse engineering, and portion mutations.
- 2026-08-11: Reconciled the six-line active-LTS matrix and admitted MariaDB
  12.3 CTE data modification while retaining the exact 10.11 / 11.4 / 11.8
  engine boundary.
- 2026-08-11: Closed the MariaDB 10.11 reverse-engineering gap with
  generation-expression metadata and a canonical table-definition parser.
- 2026-08-18: Bound temporal migration snapshots and designer models to the
  annotation-code-generator seam, added same-table owned inheritance, and made
  the separate current-owned materialization boundary explicit.
- 2026-08-18: Deferred application-time endpoint defaults until action-based
  configuration completes, preventing unconsumed conventional shadow columns.
- 2026-10-18: Added one consumed statement-local materialized-source operator,
  retaining the rejection of a general CTE DSL, and recorded the native
  empty-recursive-arm rationale, source/output boundaries, and EF seams.
- 2026-10-18: Replaced embedded definition expressions with leaf references and
  one-time preprocessing, retained one statement-wide navigation visitor, and
  bounded repeated SQL-DAG rewrites with weak per-visitor memoization.
- 2026-10-18: Retained exact named/shared entity metadata and detached temporal
  or application-time source roots on bound leaves; all navigation-extension
  hooks restore that lineage before propagation and compatibility checks.
- 2026-10-18: Restricted source Include unwrapping to captured owned references;
  distinguished rejected contributing non-owned-reference/collection sources
  from supported reference Includes on composed output and ordinary mapped
  rejoin collection shaping.
- 2026-10-18: Restored member projections after owned Include normalization and
  added local, dependency-ordered quotation reconstruction without global state;
  separated its semantic checks from EF C# translation and the D-017 AOT boundary.
- 2026-10-18: Preserved actual source annotation payloads with local concrete
  dictionary quotation, and bounded CTE collision names with numeric suffixes
  after long raw-SQL reserved-name prefixes exposed identifier growth.
- 2026-10-18: Qualified actual late-quotation C# translation on EF 10.0.8 and
  10.0.13 with an owner-approved private EF.Design unit-test reference; retained
  unsafe-accessor declarations and separated syntax from cross-assembly/AOT
  compilation.

- 2026-10-19: Added one generic directed inner-join edge using ordinary EF join
  processing, with explicit optimizer consequences, source-binding independence,
  mixed-edge/compiled-query coverage, and versioned replacement seams.
- 2026-10-19: Shared provider-node annotation quotation through concrete
  dictionary construction over single-argument pair initializers; recorded
  actual generated-C# compilation/execution under the existing friend identity
  separately from earlier syntax checks and the unchanged D-017 AOT boundary.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/EngineProfileTable.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Metadata`
- `src/Doka.EntityFrameworkCore.MySql/MySqlDbSetExtensions.cs`
- `src/Doka.EntityFrameworkCore.MySql/MySqlQueryableExtensions.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Processing/MySqlQueryTranslationPreprocessor.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Compilation/MySqlQueryCompilationContext.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Expressions/MySqlMaterializedSourceDefinition.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Expressions/MySqlMaterializedSourceQuoting.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Translation/MySqlQueryableMethodTranslatingExpressionVisitor.MaterializedSources.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Expressions/MySqlMaterializedQueryRootExpression.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Translation/MySqlNavigationExpansionExtensibilityHelper.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Query/MySqlMaterializedSourceLineageTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Query/MySqlMaterializedSourceTranslationTests.Names.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Query/MySqlMaterializedSourceExpressionTests.cs`
- `src/Doka.EntityFrameworkCore.MySql/MySqlQueryableExtensions.StraightJoin.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Processing/MySqlStraightJoinLowering.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Translation/MySqlQueryableMethodTranslatingExpressionVisitor.StraightJoins.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Expressions/MySqlStraightJoinExpression.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Expressions/MySqlRelationalExpressionQuoting.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/SqlGeneration/MySqlQuerySqlGenerator.StraightJoins.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Query/MySqlStraightJoinTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Query/MySqlStraightJoinTests.Compiled.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Query/MySqlStraightJoinTests.Tracking.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Query/MySqlStraightJoinExpressionTests.cs`
- `docs/ctes.md`
- `src/Doka.EntityFrameworkCore.MySql/MySqlTableBuilderExtensions.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Query`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests`

### Sources

- [MySQL 8.4 common table expressions](https://dev.mysql.com/doc/refman/8.4/en/with.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 8.4 DELETE statement](https://dev.mysql.com/doc/refman/8.4/en/delete.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 8.4 trigger syntax](https://dev.mysql.com/doc/refman/8.4/en/trigger-syntax.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 8.4 stored-program restrictions](https://dev.mysql.com/doc/refman/8.4/en/stored-program-restrictions.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 9.7 common table expressions](https://dev.mysql.com/doc/refman/9.7/en/with.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 9.7 DELETE statement](https://dev.mysql.com/doc/refman/9.7/en/delete.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 9.7 trigger syntax](https://dev.mysql.com/doc/refman/9.7/en/trigger-syntax.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 9.7 stored-program restrictions](https://dev.mysql.com/doc/refman/9.7/en/stored-program-restrictions.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 8.0.1 release notes](https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-1.html)
  (primary source; retrieved 2026-08-04)
- [MariaDB common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with)
  (primary source; retrieved 2026-08-04)
- [MariaDB recursive common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/recursive-common-table-expressions-overview)
  (primary source; retrieved 2026-08-04)
- [MariaDB 10.2.2 release notes](https://mariadb.com/docs/release-notes/community-server/old-releases/release-notes-mariadb-10-2-series/mariadb-1022-release-notes)
  (primary source; retrieved 2026-08-04)
- [MariaDB UPDATE statement](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update)
  (primary source; retrieved 2026-08-04)
- [MariaDB system-versioned tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/system-versioned-tables)
  (primary source; retrieved 2026-08-04)
- [MariaDB application-time periods](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/application-time-periods)
  (primary source; retrieved 2026-08-04)
- [MariaDB bitemporal tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/bitemporal-tables)
  (primary source; retrieved 2026-08-05)
- [MariaDB PERIODS information-schema table][mariadb-periods]
  (primary source; retrieved 2026-08-24)
- [MariaDB KEY_PERIOD_USAGE information-schema table][mariadb-key-period-usage]
  (primary source; retrieved 2026-08-24)
- [MariaDB SHOW CREATE TABLE](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/show/show-create-table)
  (primary source; retrieved 2026-08-11)
- [MariaDB ALTER TABLE](https://mariadb.com/docs/server/reference/sql-statements/data-definition/alter/alter-table)
  (primary source; retrieved 2026-08-04)
- [MariaDB SET STATEMENT](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/set-commands/set-statement)
  (primary source; retrieved 2026-08-04)
- [EF Core SQL Server temporal tables](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables)
  (primary source; retrieved 2026-08-04)
- [EF Core SQL queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
  (primary source; retrieved 2026-08-04)
- [EF Core advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
  (primary source; retrieved 2026-08-04)
- [EF Core async programming](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async)
  (primary source; retrieved 2026-08-04)
- [EF Core 10.0.10 relational SQL query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs)
  (primary source; retrieved 2026-08-04)
- [EF Core 10.0.10 SQL Server temporal query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Extensions/SqlServerDbSetExtensions.cs)
  (primary source; retrieved 2026-08-04)
- [EF Core 10.0.11 snapshot generator](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Design/Migrations/Design/CSharpSnapshotGenerator.cs)
  (primary source; retrieved 2026-08-18)
- [EF Core 10.0.10 SQL Server temporal annotation generator](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Design/Internal/SqlServerAnnotationCodeGenerator.cs)
  (primary source; retrieved 2026-08-18)
- [EF Core eager loading and owned-navigation behavior](https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager)
  (primary source; retrieved 2026-08-18)
- [EF Core issue 29303: temporal table splitting](https://github.com/dotnet/efcore/issues/29303)
  (primary source; retrieved 2026-08-18)

- [MySQL 8.4 derived-table and CTE materialization](https://dev.mysql.com/doc/refman/8.4/en/derived-table-optimization.html)
  (primary source; retrieved 2026-10-18)
- [MariaDB CTE cloning, MDEV-35357](https://jira.mariadb.org/browse/MDEV-35357)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.13 SelectExpression source](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.13 SqlTreePruner source](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/SqlTreePruner.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 SelectExpression source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 SqlTreePruner source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/SqlTreePruner.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core client and server evaluation](https://learn.microsoft.com/en-us/ef/core/querying/client-eval)
  (primary source; retrieved 2026-10-18)
- [EF Core single and split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries)
  (primary source; retrieved 2026-10-18)

- [EF Core 10.0.13 query preprocessor](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/QueryTranslationPreprocessor.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.13 navigation expansion](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/NavigationExpandingExpressionVisitor.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.13 parameter-based SQL processor](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalParameterBasedSqlProcessor.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 query preprocessor](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/QueryTranslationPreprocessor.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 navigation expansion](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/Internal/NavigationExpandingExpressionVisitor.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 parameter-based SQL processor](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/RelationalParameterBasedSqlProcessor.cs)
  (primary source; retrieved 2026-10-18)

- [EF Core 10.0.13 entity query root](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/EntityQueryRootExpression.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.13 navigation extension hooks](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/NavigationExpansionExtensibilityHelper.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 entity query root](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/EntityQueryRootExpression.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 navigation extension hooks](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/NavigationExpansionExtensibilityHelper.cs)
  (primary source; retrieved 2026-10-18)

- [EF Core 10.0.13 LINQ-to-C# translation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Design/Query/Internal/LinqToCSharpSyntaxTranslator.cs)
  (primary source; retrieved 2026-10-19)
- [EF Core 10.0.13 precompiled-query generation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Design/Query/Internal/PrecompiledQueryCodeGenerator.cs)
  (primary source; retrieved 2026-10-18)
- [EF Core 10.0.8 LINQ-to-C# translation](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Design/Query/Internal/LinqToCSharpSyntaxTranslator.cs)
  (primary source; retrieved 2026-10-19)
- [EF Core 10.0.8 precompiled-query generation](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Design/Query/Internal/PrecompiledQueryCodeGenerator.cs)
  (primary source; retrieved 2026-10-18)

- [EF Core 10.0.13 relational expression quotation utilities](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalExpressionQuotingUtilities.cs)
  (primary source; retrieved 2026-10-19)
- [MySQL 8.4 identifier length limits](https://dev.mysql.com/doc/refman/8.4/en/identifier-length.html)
  (primary source; retrieved 2026-10-18)
- [MariaDB identifier names and length limits](https://mariadb.com/docs/server/reference/sql-structure/sql-language-structure/identifier-names)
  (primary source; retrieved 2026-10-18)

- [MySQL 8.4 join syntax and direction](https://dev.mysql.com/doc/refman/8.4/en/join.html)
  (primary source; retrieved 2026-10-19)
- [MySQL 8.4 semijoin and antijoin restrictions](https://dev.mysql.com/doc/refman/8.4/en/semijoins-antijoins.html)
  (primary source; retrieved 2026-10-19)
- [MariaDB join syntax](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins/join-syntax)
  (primary source; retrieved 2026-10-19)
- [MariaDB join-order and index directives](https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/query-optimizations/index-hints-how-to-force-query-plans)
  (primary source; retrieved 2026-10-19)
- [EF Core complex query operators](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)
  (primary source; retrieved 2026-10-19)
- [EF Core 10.0.8 relational annotation quotation](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/RelationalExpressionQuotingUtilities.cs)
  (primary source; retrieved 2026-10-19)

[mariadb-periods]:
  https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-periods-table
[mariadb-key-period-usage]:
  https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-key_period_usage-table
