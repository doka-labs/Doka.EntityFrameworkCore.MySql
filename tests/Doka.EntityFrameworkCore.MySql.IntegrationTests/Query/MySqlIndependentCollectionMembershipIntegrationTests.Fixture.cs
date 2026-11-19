namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Owns the deterministic indexed corpus independently for each selected engine.</summary>
public sealed partial class MySqlIndependentCollectionMembershipIntegrationTests
{
    /// <summary>Seeds once per engine and cleans partially initialized stores after failure.</summary>
    public sealed class SeededDatabaseFixture : IAsyncLifetime
    {
        private readonly Dictionary<IntegrationDatabaseTarget, Task> _seeds = [];

        /// <inheritdoc />
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var target in _seeds.Keys)
            {
                try
                {
                    await using var context = CreateContext(target);

                    // WHY: Fixture cleanup deliberately cannot be cancelled so a
                    // cancelled query cannot strand another engine's owned table.
                    await DropTableAsync(context, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(
                        new InvalidOperationException($"Could not drop {TableName} on {target}.", exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Independent membership fixture cleanup failed.", failures);
            }
        }

        /// <summary>Returns the first seed outcome without retrying partial setup.</summary>
        /// <param name="target">The selected live engine.</param>
        /// <param name="cancellationToken">The token to observe during database work.</param>
        internal Task EnsureSeededAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: xUnit serializes methods in this class. Retaining the task
            // also retains its failure and prevents duplicate seeds after partial DDL.
            if (!_seeds.TryGetValue(target, out var seed))
            {
                seed = SeedAsync(target, cancellationToken);
                _seeds.Add(target, seed);
            }

            return seed;
        }

        /// <summary>Creates 14,000 rows while bounding tracked seed objects to one batch.</summary>
        /// <param name="target">The selected live engine.</param>
        /// <param name="cancellationToken">The token to observe during database work.</param>
        private static async Task SeedAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            var context = CreateContext(target);
            Exception? primaryFailure = null;

            try
            {
                await DropTableAsync(context, cancellationToken);
                await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken);

                // WHY: The corpus must preserve the incident cardinality without
                // retaining all 14,000 EF tracking entries during every engine seed.
                foreach (var batch in Enumerable
                    .Range(0, CorpusRowCount)
                    .Chunk(512))
                {
                    // WHY: Assigned, ValueGeneratedNever keys require no asynchronous value generation.
                    // ReSharper disable once MethodHasAsyncOverload
                    context.Items.AddRange(batch.Select(CreateEntity));
                    await context.SaveChangesAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                }
            }
            catch (Exception exception)
            {
                primaryFailure = exception;

                try
                {
                    await DropTableAsync(context, CancellationToken.None);
                }
                catch (Exception cleanupFailure)
                {
                    // WHY: Preserve the seed failure; cleanup is secondary evidence.
                    primaryFailure.Data[$"{nameof(SeededDatabaseFixture)}.DropTable"] = cleanupFailure;
                }

                throw;
            }
            finally
            {
                // WHY: Disposing a failed connection can also throw. Keep both
                // cleanup failures without replacing the original seed failure.
                try
                {
                    await context.DisposeAsync();
                }
                catch (Exception cleanupFailure) when (primaryFailure is not null)
                {
                    primaryFailure.Data[$"{nameof(SeededDatabaseFixture)}.DisposeContext"] = cleanupFailure;
                }
            }
        }

        private static MembershipEntity CreateEntity(
            int index
        ) => new()
        {
            Id = index + 1,
            BinaryId = CreateGuid(index),
            CharId = CreateGuid(index),
            NativeId = CreateGuid(index),
            OptionalBinaryId = index == 1
                ? null
                : CreateGuid(index),
            OptionalCharId = index == 1
                ? null
                : CreateGuid(index),
            OptionalNativeId = index == 1
                ? null
                : CreateGuid(index),
            Name = index switch
            {
                0 => "Hallo 'Test'",
                1 => null,
                2 => "\"\\q\"",
                3 => string.Empty,
                _ => $"value-{index}",
            },
            Status = index switch
            {
                0 => MembershipStatus.Active,
                1 => null,
                _ => MembershipStatus.Inactive,
            },
        };
    }
}
