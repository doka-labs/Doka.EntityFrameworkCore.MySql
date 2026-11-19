---
id: D-031
status: accepted
date: 2026-11-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Bounded EF11 compiler, command-policy, reader, indexed-window and design-time ownership seams"
supersedes: []
superseded-by: []
amends: [D-024]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-031 -- Keep query and direct-write execution behind bounded EF adapters

## Context and Problem Statement

The six query and direct-write contracts first shipped in 10.7.0 on .NET 10 /
EF Core 10. Their 11.0.0 implementation targets .NET 11 / EF Core 11 and needs
metadata and execution checks that EF11 cannot carry through unchanged. Replacing the complete EF compiler, SQL
cache
or reader pipeline would duplicate unrelated behavior. Keeping every base
execution path unchanged would instead lose required write, recursive-reader
or SQL-scope guarantees.

## Decision Drivers

- Reuse EF extraction, caching, shaping, diagnostics and ownership.
- Preserve opt-in scope, checked values and failure visibility without ordinary-query I/O.
- Keep version-coupled extension points explicit and replaceable.

## Considered Options

- Bounded opt-in adapters at actual EF seams
- Replace the complete compiler and execution pipeline
- Force every feature through unchanged base execution

## Decision Outcome

Chosen option: "Bounded opt-in adapters at actual EF seams", because it preserves
normal EF behavior while supplying the specific typed policies the new features
require.

### Consequences

- Good, because unchanged queries retain the existing compiler and ownership paths.
- Good, because typed invocation policies preserve cache and transaction boundaries.
- Bad, because EF internal seams require explicit regression coverage on upgrades.
- Bad, because recursive-reader ownership adds a separately tested opt-in lifecycle.

### Confirmation

- Run the complete applicable shared repository qualification and feature-specific controls.
- Use the named tests and requalification table below for precise boundary
  checks; source presence alone is not a passing run.

Keep small opt-in adapters at the actual EF11 seams. Ordinary queries and
nonqueries delegate to their existing base implementation. Statement policies
are typed metadata, copied or cleared when a pooled command is populated;
query tags remain diagnostic text. No parallel query cache, context registry,
SQL-text parser or entity reflection loop is introduced.

### JSON collection membership scope

Correlated scalar projections retain their complete expression inside the
membership count, including mapped user functions, arithmetic and logical
operators. Both comparison operands preserve precedence. Native JSON membership
is reserved for verified representation identities, including function provenance;
an unqualified decoder name alone cannot prove identity. Independent uncomposed
parameter rowsets retain their materializable `IN` form.

Aggregate/window expressions and scalar subqueries keep their original scope.
EF11 does not distinguish scalar and aggregate calls in `SqlFunctionExpression`,
so native aggregate names are checked separately from mapped scalar functions.
Unparsed SQL fragments remain excluded except the closed first-argument unit
grammar of the provider's builtin `TIMESTAMPDIFF` expression. No SQL parser,
visitor allocation or parallel query pipeline is introduced for these checks.

### Migration-only command construction

Migration commands do not carry query-rental policies. The default Doka
command builder first preserves EF's separate executable and redacted buffers,
then creates a typed query command. Applying that wrapper to every migration
command added approximately 34.8 KB to the complete 256-scope migration
allocation control, with identical SQL and command counts.

Only the migrations generator receives a copy of EF11's public dependency
record with an ordinary EF command-builder factory over the same dependencies.
This applies only to Doka's sealed default factory. Application-provided
factories keep their identity and behavior; the globally registered typed
factory still serves query rental and policy population. There is no SQL-text
classification or access to EF's private command buffers.

### Compiled INSERT SELECT target extraction

`MySqlQueryCompiler.ExtractParameters` changes only the exact compiled
`ExecuteInsertFromQueryAsync` terminal. It extracts an invocation-local scalar
target receipt before EF can replace a DbSet argument with a query root in a
DbSet-typed slot. The receipt verifies the actual target context/provider,
then disappears before normal EF extraction and cache-key processing.
EF11's four-argument extraction override delegates ordinary paths to the base
implementation. Source and target roots still pass through the same normal
extraction, preserving provider checks.

Shared model identity is not target-owner identity. Cached delegates must use
the current source parameters, target model/database and cancellation token;
no first context, DbSet or token source may survive through a cached receipt.

### One INSERT data command through the shaped compiler

`MySqlShapedQueryCompilingExpressionVisitor` recognizes only
`MySqlInsertSelectExpression`. It uses EF's existing `RelationalCommandCache`
because EF11's resolver-expression builder is private. It preserves parameter-
dependent SQL processing, tags and current query parameters, then calls the
small `MySqlInsertSelectExecutor`. The executor rents, populates and returns
EF's normal command wrapper, honors thread-safety settings and logs through
the existing diagnostics.

The data statement executes once, outside the execution strategy. An uncertain
write cannot safely inherit the base nonquery retry behavior. The existing
ordinary update/delete/compiler behavior remains unchanged. INSERT SELECT
pregenerated execution is explicitly rejected until its generated execution
policy transport is separately qualified.

### Direct-write ownership and transaction controls

Only commands marked `IsDirectWrite` use the extended nonquery cleanup path.
It preserves nonquery interception, native counts, cancellation and diagnostics,
while independently attempting parameter clearing, command disposal and release
of the one successfully acquired connection reference. A cleanup or diagnostic
failure must not replace a native primary failure. Ordinary command templates
clear this flag and retain base execution behavior.

The direct-write creation helper mirrors EF11's command-creation callback and
parameter-binding order, while publishing each acquired command to the caller
before those callbacks or bindings can fail. EF11's base creation method does
not release its local command on such a failure. Publishing ownership early
lets the same direct-write cleanup dispose it in the correct synchronous or
asynchronous mode and retain both setup and cleanup failures. Suppressed
creation and successful interceptor replacements retain EF's ownership
semantics; ordinary commands continue to use the base implementation. Recheck
this bounded creation sequence and its failure tests on every EF upgrade.

One INSERT SELECT data statement does not mean one total wire statement.
Borrowed local transactions need a fresh SAVEPOINT, immediate ROLLBACK TO and
RELEASE receipt. Multi-command UPSERT retains its validated marker as the
invocation boundary. These controls preserve prior caller work when the native
transaction survives; they cannot repair a native full rollback. Actual ambient
and emulated ambient liveness are separate receipts, not inferred from
`Transaction.Current` or from local savepoint evidence. For confirmed
`UseXaTransactions=false` enlistment, `MySqlAmbientTransactionGuard` sends one
fresh three-control batch and verifies the captured physical connection and
managed transaction before and after it. It does not claim that local savepoints
are valid XA controls.

Both engine families can acknowledge SAVEPOINT without creating a marker when
no native transaction is active. MariaDB documents that state and error 1305
for a missing marker; MySQL's transaction implementation has the same success
return before marker creation. MySqlConnector always permits batched command
statements. These primary-source premises support the receipt, not a claim
that every failed response proves whether the server accepted a control.
After an uncertain result, the unique marker still needs cleanup, and its
failure remains separate from the primary operation failure. A repeated native
1305 is not grounds to discard unrelated callback or disposal failures.

D-029 still governs connector configuration and ownership. This per-execution
transaction receipt is not per-command connection-string parsing or authority
to mutate, replace, commit or dispose a borrowed object.

### Recursive payload before EF buffering

Recursive SELECT SQL has two private control columns and a synthetic guard row.
`MySqlRecursiveDataReader` validates the exact trailing schema and current guard
before exposing payload rows. It hides controls from ordinals, names, schema,
bulk getters and typed getters; a real NULL payload remains distinguishable
from the synthetic empty row.

Named lookup retains the connector's case matching and genuine lookup failures.
A private alias is reported as a missing column with the ADO.NET
`IndexOutOfRangeException` contract, including a control-only result. Invalid
ordinals follow the same ADO.NET exception contract through the shared range
check, including asynchronous getters. This adds no lookup table or allocation
to successful name resolution.

The command first obtains an unbuffered base result, installs the guarded reader,
then reinstates EF's actual `BufferedDataReader` only when the original
`ReaderColumns` required buffering. Ordinary streaming does not acquire a
whole-result buffer. This order prevents EF from buffering private controls or
materializing a synthetic row before validation.

`MySqlRecursiveRelationalDataReader` adopts the successful base result's reader,
command and single open reference. EF11's private ownership fields and cleanup
ordering prevent safe reuse of base disposal after an injected reader failure.

Its `Initialize` override retains EF's reusable-owner contract. Reinitializing
after disposal requires a fresh, already acquired execution reference; it does
not acquire that reference itself. Disposal releases only that execution's
reference and preserves any separately owned caller reference. Reinstalling a
buffer before disposal remains part of the same execution.
The opt-in owner tracks its own disposal progress, attempts later cleanup after
an earlier failure, honors diagnostic suppression and releases the acquired
reference at most once. Buffer input-disposal failures are captured only for
that buffer-initialization scope and immediately combined with the read error.
No reflection into EF private fields or global resource registry is used.

The finite recursive text proof also needs original column collations, which
EF11 removes from runtime property metadata. The runtime convention and actual
compiled-model generator retain a primitive explicit-value/known-absence marker,
including table-specific property overrides. Query compilation consumes actual
physical-column lineage and retained facets; it neither queries the database
nor treats a missing marker in an older supplied model as proved inheritance.
Compatible different columns remain supported. Design models and snapshots do
not acquire these runtime-only markers.

### Window and indexed-selector SQL scope

`MySqlIndexedSelectLowering` runs after EF normalization and before navigation
expansion. Its exact unary same-element completion marker forces EF to retain
a completed projection before later filtering, grouping or paging can inline a
window into an invalid or semantically different scope. Grouped indexed
selectors retain partition keys, independent member ordering/filter/page state
and the enclosing aggregate shape through explicit carriers.

The SQL translator uses native window expressions and the existing checked
Int32 materialization path for zero-based LINQ indexes. It rejects missing
ordering and directly nested/unprojected window operands. The helper closes
only recognized LINQ/provider methods; it is not an arbitrary expression compiler.

For an indexed group with one CLR nullable/reference key leaf, an internal
null-class component keeps EF11's grouping correlation distinct from primitive
LINQ equijoin null semantics. The public key and window partition remain the
original key; no colliding sentinel or CLR string normalization is used.
Composite keys, ordinary non-indexed queries and explicit joins keep their
existing paths. Required string keys may carry an always-false component;
this is not a zero-overhead claim or a general GroupBy rewrite.

### Quotation is separate from pregenerated/AOT execution

Provider SQL nodes reconstruct their full structural state through `Quote`,
including frames, target/projection ordering and parameter-policy identity.
Expression reconstruction, separate-assembly generated C#, runtime compiled
queries and generated runtime models are distinct proofs.

None alone establishes full pregenerated query execution or NativeAOT support.
INSERT and locking reads have explicit provider pregeneration rejection. For
`EF.Functions.Window()`, the current EF11 precompiler rejects its structural
`[NotParameterized]` functions receiver before Doka translation. Record that
actual upstream boundary; do not invent a provider-wide window guard or disable
ordinary `SupportsPrecompiledQuery`. D-017 remains authoritative for the runtime-
posture support claim.

### Native EF11 ROW_NUMBER quotation

The .NET 10 line uses `MySqlQuotableRowNumberExpression` to repair EF Core
10.0.8's incomplete native quotation. EF Core 11 provides the complete native
`RowNumberExpression.Quote()` contract, so this line uses that expression
instead of carrying the EF10 adapter into a platform which no longer needs it.

Structural round trips and separate-assembly generated C# must retain the
complete partitions, orderings and type mapping. These controls remain required
on every admitted EF patch; native API availability alone is not qualification.

### Design-time logger ownership

EF11's design-time helper creates one LoggerFactory immediately and returns it
from a scoped registration. Resolving and disposing one scope therefore
disposes the same factory that a later or overlapping scaffolding scope needs.
Doka changes only the helper's newly appended scoped factory descriptor to a
singleton, preserving its exact factory delegate. The root provider owns and
disposes that factory; operation scopes do not. No logger wrapper or fallback
hides disposal failures.

When the official tooling has already registered an IOperationReporter instance,
Doka passes that same instance into its required helper call. This keeps the
factory's captured reporter aligned with the existing reporter service instead
of silently routing warnings into a new default reporter. Keyed registrations
do not override ordinary reporter resolution. This instance-preservation rule
does not infer the value of an arbitrary factory registration before DI builds.
Custom ILoggerFactory overrides registered afterward retain normal DI lifetime
and instance-ownership rules. D-001 remains authoritative for the design-time
composition boundary.

## Pros and Cons of the Options

### Bounded opt-in adapters at actual EF seams

- Good, because each policy is consumed at the required opt-in boundary.
- Bad, because internal extension points must be requalified when EF changes.

### Replace the complete compiler and execution pipeline

- Good, because every operation would be under provider control.
- Bad, because this duplicates normal extraction, caching, shaping and lifetime semantics.

### Force every feature through unchanged base execution

- Good, because it minimizes provider-owned execution code.
- Bad, because it loses required target identity, no-retry and guarded-reader semantics.

## More Information

### Rejected correctness alternatives

- Whole compiler/cache fork: duplicates normal EF extraction, ownership checks,
  parameter-sensitive SQL and invalidation rather than preserving them.
- Treat model identity as target ownership: two contexts may share a model while
  owning different target sets, services or databases.
- Use base retrying nonquery execution: can replay a committed insert after an
  uncertain response; native affected rows are not an idempotency receipt.
- Use SQL tags or rewrite SQL text: loses typed scope, quoting, cache-policy
  identity and exact command semantics.
- Buffer the recursive raw reader first: exposes control columns/synthetic rows
  to EF shaping and can lose a primary error during buffer cleanup.
- Call both base and adopted-reader disposal: private base ownership can skip
  resources after failure or release a borrowed connection reference twice.
- Inline indexed/window calculations into later predicates: changes the completed
  row set or produces illegal nested/window SQL.
- Treat successful quotation as AOT qualification: bypasses runtime command
  policies and says nothing about generated query execution or native resources.

### Re-evaluation Triggers

Named tests are source references, not substitute evidence of a fresh run.
Every supported EF patch change and connector change must rerun applicable
checks against rebuilt binaries; a test rename must preserve this mapping.

| Seam | Required concrete controls | Replace or requalify when |
| --- | --- | --- |
| Migration-only factory | `Migration_factory_builds_plain_commands_with_sensitive_text_redaction`; `Migration_generation_keeps_the_global_query_factory_and_rented_policy_typed`; `Custom_command_factory_is_preserved_for_migration_generation`; `Generate256DdlCommentSqlModeScopes` | EF changes the dependency record or command-factory/logging contract; retain custom factory identity, typed query rental and the unchanged allocation ceiling. |
| Target extraction and cache ownership | `Compiled_insert_rejects_a_captured_foreign_target_before_io`; `Compiled_insert_rejects_a_captured_foreign_source_before_io`; `Compiled_insert_does_not_retain_the_first_context_or_parameter`; `Compiled_insert_reads_the_current_cancellation_token`; `Ordinary_compiled_query_still_uses_the_standard_pipeline` | EF changes DbSet funcletization, extraction order/signature or provider checks; replace the adapter if EF exposes a target-preserving terminal seam. |
| Shaped nonquery compilation | `Insert_does_not_execute_a_configured_execution_strategy`; `Same_source_with_a_different_target_has_a_distinct_command_cache_identity`; `Compiled_insert_rebinds_context_parameters_and_token_on_each_execution` | EF changes command resolution, cache identity, cancellation or native nonquery compilation; adopt a supported resolver seam when it preserves the no-retry contract. |
| Direct command ownership | `Direct_write_preserves_failure_when_diagnostics_and_cleanup_also_fail`; `Direct_write_retains_the_original_native_exception_identity`; `Ordinary_command_template_clears_the_direct_write_policy`; `Ordinary_nonquery_keeps_existing_execution_behavior` | EF fixes base failure-preserving cleanup or changes interception/open-reference behavior; remove only the adapter portion made unnecessary after the same adversarial tests pass. |
| Transaction liveness | `Local_transaction_receipt_proves_liveness_without_changing_caller_ownership`; `Local_receipt_retains_primary_and_marker_cleanup_failures`; `Externally_aborted_transaction_rejects_direct_and_compiled_insert_before_data_write`; `Native_timeout_in_a_later_batch_preserves_only_the_work_the_engine_can_preserve`; `Emulated_ambient_upsert_checks_native_liveness_and_preserves_borrowed_pool_ownership` | Connector transaction-state/enlistment behavior or server rollback policy changes; a replacement must distinguish full rollback from surviving statement failure and still protect external prior work. |
| Hidden recursive controls and buffering | `Hidden_control_ordinals_are_not_accessible`; `Hidden_control_names_are_not_accessible`; `Empty_payload_does_not_expose_control_names`; `Native_recursive_hidden_and_missing_names_have_the_same_contract`; `Bulk_getters_do_not_copy_control_values`; `Valid_empty_payload_skips_the_synthetic_row`; `Buffer_read_and_input_disposal_failures_are_preserved_together`; `Buffer_success_still_propagates_input_disposal_failure` | EF reader-column/buffer initialization contracts or recursive result shape changes; replace the internal buffer seam if EF exposes an equivalent interception point. |
| Recursive physical text metadata | `Runtime_property_retains_original_column_collation_evidence`; `Generated_model_preserves_compatible_recursive_copy_and_rejects_incompatible_copy`; `Different_physical_charsets_do_not_silently_corrupt_a_recursive_text_copy` | EF retains the required property/override collations in runtime and generated models; remove private markers only after equal compatibility, unknown-metadata and snapshot-isolation controls pass. |
| Recursive resource owner | `Ordinary_to_recursive_reuse_transfers_exactly_one_open_reference`; `Repeated_disposal_preserves_the_callers_open_reference`; `Reinitialization_after_disposal_owns_only_the_fresh_execution_reference`; `Command_disposal_failure_still_attempts_connection_close`; `Native_close_failure_is_reported_without_a_second_reference_release` | EF makes its base owner safely extensible or changes disposal progress/suppression; returning to the base owner requires the same injected-failure and reuse proofs. |
| Window scope and ordinal materialization | `Global_index_is_calculated_after_the_completed_source_page`; `Grouped_indexed_selector_preserves_six_aggregate_results`; `Filtered_group_member_page_keeps_a_distinct_zero_based_index_scope`; `Materialized_native_index_overflow_is_visible`; `Ordinary_projection_does_not_add_an_index_or_window_boundary` | EF normalization/navigation/group shaping or connector Int64-to-Int32 materialization changes; use a native EF indexed/window hook only if these scopes and checked overflow survive. |
| Native EF11 ROW_NUMBER quotation | `Index_conversion_quotation_preserves_the_complete_native_expression`; `Index_conversion_generated_csharp_preserves_the_public_reconstruction_path` | EF changes native partition/order/type-mapping quotation; retain the native path only while update/visitor identity and actual generated-C# execution preserve the same public reconstruction contract. |
| Quotation/pregeneration separation | `Index_conversion_generated_csharp_preserves_the_public_reconstruction_path`; `Sql_terminal_quote_round_trips_target_projection_tags_and_token`; `Pregenerated_window_boundary_preserves_the_ordinary_provider_contract`; `Generated_compiled_model_executes_native_upsert_and_reads_back_exact_values` | EF exposes a qualified generated command-policy transport or changes `[NotParameterized]` precompilation; lift a feature guard only after real generated-consumer execution, current parameters, cleanup and runtime-posture proof. |

### Decision History

- 2026-11-17: Decision recorded with status accepted.
- 2026-11-17: Recorded the selected EF10 opt-in execution boundaries and their
  regression/replacement requirements; combined qualification remains in progress.
- 2026-11-18: Adapted the selected feature seams to the .NET 10 / EF Core 10
  release line. EF10 extraction, quotation and generated consumers require their
  own qualification; results from the .NET 11 line do not establish that proof.
  Publication remains separate.
- 2026-11-18: Recorded joint local EF11 qualification, including migration-only
  command-factory isolation under the unchanged allocation ceiling. That
  historical result does not qualify later rebased source.
- 2026-11-19: Aligned current metadata with the shared notification audience and
  last-update date contract; retained the original decision and status history.
- 2026-11-19: Reconciled the shared 10.7.0 fixes with the .NET 11 line, retaining
  EF11 four-argument parameter extraction, the bounded command-cache adapter and
  native ROW_NUMBER quotation. Platform qualification remains independent.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Compilation/MySqlQueryCompiler.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Compilation/MySqlShapedQueryCompilingExpressionVisitor.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Update/MySqlInsertSelectExecutor.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/MySqlRelationalCommand.DirectWrite.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/MySqlRelationalCommand.DirectWriteCreation.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/MySqlAmbientTransactionGuard.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/Processing/MySqlIndexedSelectLowering.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query/SqlGeneration/MySqlQuerySqlGenerator.Json.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Query/MySqlIndependentCollectionMembershipSqlTests.ScalarProjections.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Query/MySqlJsonMembershipFunctionIdentityIntegrationTests.cs`

The installed EF11 XML contract names
`CoreStrings.NotParameterizedAttributeWithNonConstantNotSupportedInPrecompiledQueries`.
The real precompiler test pins that observed boundary, rather than an inferred
provider-owned guard.

### Sources

- [MariaDB SAVEPOINT behavior and missing-marker errors](https://mariadb.com/docs/server/reference/sql-statements/transactions/savepoint) (primary source; retrieved 2026-11-18)
- [MySQL trans_savepoint transaction check](https://github.com/mysql/mysql-server/blob/mysql-8.4.13/sql/transaction.cc) (primary source; retrieved 2026-11-18)
- [MySqlConnector batch statement support](https://mysqlconnector.net/connection-options/#unsupported-options) (primary source; retrieved 2026-11-18)
- [EF10 public migration dependency record](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationsSqlGeneratorDependencies.cs) (primary source; retrieved 2026-11-18)
- [EF10 core preprocessing visitor order](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/QueryTranslationPreprocessor.cs) (primary source; retrieved 2026-11-18)
- [EF10 inherited relational metadata and root normalization](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/RelationalQueryTranslationPreprocessor.cs) (primary source; retrieved 2026-11-18)
- [EF10 design-time services and shared logger factory registration](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Design/Design/DesignTimeServiceCollectionExtensions.cs) (primary source; retrieved 2026-11-18)
- [EF10 ROW_NUMBER quotation](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/SqlExpressions/RowNumberExpression.cs) (primary source; retrieved 2026-11-18)
- [EF10 QueryCompiler and ExtractParameters](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/Internal/QueryCompiler.cs) (primary source; retrieved 2026-11-18)
- [EF10 relational shaped nonquery compilation and private resolver](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/RelationalShapedQueryCompilingExpressionVisitor.cs) (primary source; retrieved 2026-11-18)
- [EF10 relational command execution and cleanup](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Storage/RelationalCommand.cs) (primary source; retrieved 2026-11-18)
- [EF10 relational reader ownership](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Storage/RelationalDataReader.cs) (primary source; retrieved 2026-11-18)
- [MySQL temporal function grammar](https://dev.mysql.com/doc/refman/8.4/en/date-and-time-functions.html#function_timestampdiff) (primary source; retrieved 2026-11-18)
- [MariaDB TIMESTAMPDIFF units](https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/timestampdiff) (primary source; retrieved 2026-11-18)
- [EF11 public migration dependency record](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore.Relational/Migrations/MigrationsSqlGeneratorDependencies.cs) (primary source; retrieved 2026-11-18)
- [EF11 core preprocessing visitor order](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore/Query/QueryTranslationPreprocessor.cs) (primary source; retrieved 2026-11-17)
- [EF11 inherited relational metadata and root normalization](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore.Relational/Query/RelationalQueryTranslationPreprocessor.cs) (primary source; retrieved 2026-11-17)
- [EF11 design-time services and shared logger factory registration](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore.Design/Design/DesignTimeServiceCollectionExtensions.cs) (primary source; retrieved 2026-11-17)
- [EF11 QueryCompiler and ExtractParameters](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore/Query/Internal/QueryCompiler.cs) (primary source; retrieved 2026-11-17)
- [EF11 relational shaped nonquery compilation and private resolver](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore.Relational/Query/RelationalShapedQueryCompilingExpressionVisitor.cs) (primary source; retrieved 2026-11-17)
- [EF11 relational command execution and cleanup](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore.Relational/Storage/RelationalCommand.cs) (primary source; retrieved 2026-11-17)
- [EF11 relational reader ownership](https://github.com/dotnet/efcore/blob/v11.0.0/src/EFCore.Relational/Storage/RelationalDataReader.cs) (primary source; retrieved 2026-11-17)
