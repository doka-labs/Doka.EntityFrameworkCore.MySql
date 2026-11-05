namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlNetTopologySuiteContractIntegrationTests
{
    /// <summary>Creates the original mixed geometry table and its related child table.</summary>
    /// <param name="context">The context used for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static async Task CreateMaterializationTablesAsync(
        SpatialMaterializationContext context,
        CancellationToken cancellationToken
    )
    {
        await context
            .Database
            .ExecuteSqlRawAsync(
                $"""
                 CREATE TABLE `{MaterializationTable}` (
                     `Id` int NOT NULL,
                     `Point` point NOT NULL,
                     `LineString` linestring NOT NULL,
                     `Polygon` polygon NOT NULL,
                     `MultiPoint` multipoint NOT NULL,
                     `MultiLineString` multilinestring NOT NULL,
                     `MultiPolygon` multipolygon NOT NULL,
                     `GeometryCollection` geometrycollection NOT NULL,
                     `Geometry` geometry NOT NULL,
                     CONSTRAINT `PK_{MaterializationTable}` PRIMARY KEY (`Id`)
                 );
                 CREATE TABLE `{MaterializationChildTable}` (
                     `Id` int NOT NULL,
                     `ParentId` int NOT NULL,
                     CONSTRAINT `PK_{MaterializationChildTable}` PRIMARY KEY (`Id`),
                     CONSTRAINT `FK_{MaterializationChildTable}` FOREIGN KEY (`ParentId`)
                         REFERENCES `{MaterializationTable}` (`Id`) ON DELETE CASCADE
                 );
                 """,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static SpatialMaterializationEntity CreateMaterializationEntity(
        SpatialGeometrySet expected
    ) => new()
    {
        Id = 1,
        Point = expected.Point,
        LineString = expected.LineString,
        Polygon = expected.Polygon,
        MultiPoint = expected.MultiPoint,
        MultiLineString = expected.MultiLineString,
        MultiPolygon = expected.MultiPolygon,
        GeometryCollection = expected.GeometryCollection,
        Geometry = expected.Geometry,
        Children = [new SpatialMaterializationChild { Id = 1 }],
    };

    /// <summary>Creates the geometry-operand table with the requested nullability.</summary>
    /// <param name="context">The context used for this operation.</param>
    /// <param name="nullable">The nullable used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    private static Task<int> CreateCrossesTableAsync(
        SpatialCrossesContext context,
        bool nullable,
        CancellationToken cancellationToken
    ) => context.Database.ExecuteSqlRawAsync(
        $"""
         CREATE TABLE `{CrossesTable}` (
             `Id` int NOT NULL,
             `LeftGeometry` geometry {(nullable ? "NULL" : "NOT NULL")},
             `RightGeometry` geometry {(nullable ? "NULL" : "NOT NULL")},
             CONSTRAINT `PK_{CrossesTable}` PRIMARY KEY (`Id`)
         );
         """,
        cancellationToken: cancellationToken);

    private enum SpatialDataSet
    {
        Materialization,
        Crosses,
        Functions,
    }

    /// <summary>Owns one lazily seeded read-only spatial dataset per requested engine and contract.</summary>
    public sealed class SpatialFixture : IAsyncLifetime
    {
        private readonly Dictionary<(IntegrationDatabaseTarget Target, SpatialDataSet DataSet), Task> _seeds = [];

        /// <inheritdoc />
        public Task InitializeAsync() => Task.CompletedTask;

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            // WHY: Fixture teardown must release every engine dataset after a cancelled test.
            List<Exception>? failures = null;

            foreach (var key in _seeds.Keys)
            {
                try
                {
                    await DropContractTablesAsync(
                            GetConnectionString(key.Target, key.DataSet.ToString()),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // WHY: A partially failed engine must not prevent teardown
                    // of other engine/dataset pairs initialized by this class.
                    (failures ??= []).Add(
                        new InvalidOperationException(
                            $"Could not clean {key.DataSet} spatial data on {key.Target}.",
                            exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Spatial contract fixture cleanup failed.", failures);
            }
        }

        /// <summary>Creates a fresh context over the immutable geometry/child row.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        internal async Task<SpatialMaterializationContext> CreateMaterializationContextAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            await EnsureSeededAsync(target, SpatialDataSet.Materialization, cancellationToken)
                .ConfigureAwait(false);

            return new SpatialMaterializationContext(
                CreateOptions<SpatialMaterializationContext>(
                    GetConnectionString(target, nameof(SpatialDataSet.Materialization)),
                    target));
        }

        /// <summary>Creates a fresh context over the dimension and null-operand matrix.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        internal async Task<SpatialCrossesContext> CreateCrossesContextAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            await EnsureSeededAsync(target, SpatialDataSet.Crosses, cancellationToken)
                .ConfigureAwait(false);

            return CreateCrossesContext(target, SpatialDataSet.Crosses);
        }

        /// <summary>Creates a fresh context over the supported scalar/aggregate input row.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        internal async Task<SpatialCrossesContext> CreateFunctionContextAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            await EnsureSeededAsync(target, SpatialDataSet.Functions, cancellationToken)
                .ConfigureAwait(false);

            return CreateCrossesContext(target, SpatialDataSet.Functions);
        }

        /// <summary>Initializes the selected dataset once and retains its outcome for later cases.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="dataSet">The data set used by this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        private Task EnsureSeededAsync(
            IntegrationDatabaseTarget target,
            SpatialDataSet dataSet,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: xUnit serializes this class. Retain seed failures per dataset
            // so filtered cases neither repeat partial DDL nor affect another engine.
            if (!_seeds.TryGetValue((target, dataSet), out var seed))
            {
                seed = SeedAsync(target, dataSet, cancellationToken);
                _seeds.Add((target, dataSet), seed);
            }

            return seed;
        }

        /// <summary>Creates and seeds the requested read-only spatial dataset.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="dataSet">The data set used by this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        private static async Task SeedAsync(
            IntegrationDatabaseTarget target,
            SpatialDataSet dataSet,
            CancellationToken cancellationToken
        )
        {
            var connectionString = GetConnectionString(target, dataSet.ToString());
            await DropContractTablesAsync(connectionString, cancellationToken)
                .ConfigureAwait(false);

            if (dataSet == SpatialDataSet.Materialization)
            {
                await using var context = new SpatialMaterializationContext(
                    CreateOptions<SpatialMaterializationContext>(connectionString, target));

                await CreateMaterializationTablesAsync(context, cancellationToken)
                    .ConfigureAwait(false);

                // WHY: Explicit fixture keys need no asynchronous value generation.
                // ReSharper disable once MethodHasAsyncOverload
                context.Entities.Add(CreateMaterializationEntity(CreateGeometrySet()));
                await context
                    .SaveChangesAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            await using var crosses = CreateCrossesContext(target, dataSet);
            await CreateCrossesTableAsync(crosses, dataSet == SpatialDataSet.Crosses, cancellationToken)
                .ConfigureAwait(false);

            if (dataSet == SpatialDataSet.Crosses)
            {
                var cases = CreateCrossesCases();

                // WHY: Explicit fixture keys need no asynchronous value generation.
                // ReSharper disable once MethodHasAsyncOverload
                crosses.Rows.AddRange(
                    cases.Select((pair, index) => new SpatialCrossesRow
                    {
                        Id = index + 1,
                        LeftGeometry = pair.Left,
                        RightGeometry = pair.Right,
                    }));

                // WHY: Explicit fixture keys need no asynchronous value generation.
                // ReSharper disable once MethodHasAsyncOverload
                crosses.Rows.AddRange(
                    new SpatialCrossesRow
                    {
                        Id = cases.Length + 1,
                        RightGeometry = cases[0].Right,
                    },
                    new SpatialCrossesRow
                    {
                        Id = cases.Length + 2,
                        LeftGeometry = cases[0].Left,
                    },
                    new SpatialCrossesRow { Id = cases.Length + 3 });
            }
            else
            {
                // WHY: Explicit fixture keys need no asynchronous value generation.
                // ReSharper disable once MethodHasAsyncOverload
                crosses.Rows.Add(
                    new SpatialCrossesRow
                    {
                        Id = 1,
                        LeftGeometry = ReadGeometry("LINESTRING (0 0, 2 0)", spatialReferenceSystemId: 0),
                        RightGeometry = ReadGeometry("LINESTRING (1 -1, 1 1)", spatialReferenceSystemId: 0),
                    });
            }

            await crosses
                .SaveChangesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static SpatialCrossesContext CreateCrossesContext(
            IntegrationDatabaseTarget target,
            SpatialDataSet dataSet
        ) => new(CreateOptions<SpatialCrossesContext>(GetConnectionString(target, dataSet.ToString()), target));
    }
}
