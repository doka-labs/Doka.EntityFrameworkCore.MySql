using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies boxed scalar formatting and null materialization on every supported engine.</summary>
[IntegrationDatabaseTest]
public sealed class MySqlBoxedStringConversionIntegrationTests
{
    private const string TableName = "IntBoxedStringRows";
    private const string GuidText = "00112233-4455-6677-8899-aabbccddeeff";

    private static readonly System.Reflection.MethodInfo s_convertToString = typeof(Convert).GetMethod(
        nameof(Convert.ToString),
        [typeof(object)])!;

    /// <summary>Includes nullable scalars and each engine's supported GUID representations.</summary>
    public static IEnumerable<object[]> ConversionCases()
    {
        foreach (var target in IntegrationTestEnvironment.GetSupportedTargets().Where(IntegrationTestEnvironment.IsTargetSelected))
        {
            yield return [target.ToString(), MySqlGuidFormat.Binary16, nameof(ConversionRow.StringRole), "Admin"];
            yield return [target.ToString(), MySqlGuidFormat.Binary16, nameof(ConversionRow.NumericRole), "Admin"];
            yield return [target.ToString(), MySqlGuidFormat.Binary16, nameof(ConversionRow.Number), "42"];
            yield return [target.ToString(), MySqlGuidFormat.Binary16, nameof(ConversionRow.Enabled), "True"];
            yield return [target.ToString(), MySqlGuidFormat.Binary16, nameof(ConversionRow.Text), "value"];
            yield return [target.ToString(), MySqlGuidFormat.Binary16, nameof(ConversionRow.Token), GuidText];
            yield return [target.ToString(), MySqlGuidFormat.Char36, nameof(ConversionRow.Token), GuidText];

            if (target is IntegrationDatabaseTarget.MariaDb1011 or IntegrationDatabaseTarget.MariaDb114
                or IntegrationDatabaseTarget.MariaDb118 or IntegrationDatabaseTarget.MariaDb123)
            {
                yield return [target.ToString(), MySqlGuidFormat.Uuid, nameof(ConversionRow.Token), GuidText];
            }
        }
    }

    /// <summary>Reads one non-null and one boxed-null value through the same server projection.</summary>
    /// <param name="targetName">The selected engine.</param>
    /// <param name="format">The configured GUID storage representation.</param>
    /// <param name="propertyName">The scalar column under test.</param>
    /// <param name="expected">The canonical non-null representation.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ConversionCases))]
    public async Task Boxed_scalar_projection_preserves_value_and_empty_null_result(
        string targetName,
        MySqlGuidFormat format,
        string propertyName,
        string expected
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var builder = IntegrationTestDbContextOptions.Create<ConversionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.DefaultGuidFormat(format));

        await using var context = new ConversionContext(builder.Options);
        await DropTableAsync(context, CancellationToken.None);
        Exception? primaryFailure = null;

        try
        {
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);

            // WHY: Explicit integer keys need no asynchronous value generation;
            // SaveChangesAsync below owns the database work.
            // ReSharper disable once MethodHasAsyncOverload
            context.Rows.AddRange(
                new ConversionRow
                {
                    Id = 1,
                    StringRole = Role.Admin,
                    NumericRole = Role.Admin,
                    Number = 42,
                    Enabled = true,
                    Text = "value",
                    Token = new Guid(GuidText),
                },
                new ConversionRow { Id = 2 });

            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            // WHY: Build the exact one-argument object overload; executing a
            // CLR conversion here or choosing IFormatProvider tests another API.
            var row = Expression.Parameter(typeof(ConversionRow), "row");
            var conversion = Expression.Call(
                s_convertToString,
                Expression.Convert(Expression.Property(row, propertyName), typeof(object)));

            var projection = Expression.Lambda<Func<ConversionRow, string?>>(conversion, row);
            // WHY: Filtering the converted text also requires server translation;
            // a correct-looking client-only projection cannot satisfy this probe.
            var query = context.Rows.AsNoTracking().OrderBy(value => value.Id).Select(projection)
                .Where(value => value == expected || value == string.Empty);

            // Act
            var actual = await query.ToArrayAsync(CancellationToken.None);

            // Assert
            // WHY: Both maintained xUnit generations must avoid the nullable
            // string constraint on their differing span equality overloads.
            Assert.Collection(
                actual,
                value => Assert.Equal(expected, value),
                value => Assert.Equal(string.Empty, value));
        }
        catch (Exception exception)
        {
            primaryFailure = exception;

            throw;
        }
        finally
        {
            // WHY: Teardown must finish independently of a failed or cancelled query.
            try
            {
                await DropTableAsync(context, CancellationToken.None);
            }
            catch (Exception cleanupFailure) when (primaryFailure is not null)
            {
                // WHY: Retain cleanup evidence without replacing the original
                // query/assertion exception or its stack trace.
                primaryFailure.Data[nameof(MySqlBoxedStringConversionIntegrationTests)] = cleanupFailure;
            }
        }
    }

    /// <summary>Removes the class-owned table using the caller's cancellation policy.</summary>
    private static Task<int> DropTableAsync(
        ConversionContext context,
        CancellationToken cancellationToken
    ) => context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;", cancellationToken);

    private sealed class ConversionContext(DbContextOptions<ConversionContext> options) : DbContext(options)
    {
        public DbSet<ConversionRow> Rows => Set<ConversionRow>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<ConversionRow>();
            entity.ToTable(TableName);
            entity.Property(row => row.StringRole).HasConversion<string>();
        }
    }

    private sealed class ConversionRow
    {
        public int Id { get; set; }
        public Role? StringRole { get; set; }
        public Role? NumericRole { get; set; }
        public int? Number { get; set; }
        public bool? Enabled { get; set; }
        public string? Text { get; set; }
        public Guid? Token { get; set; }
    }

    private enum Role
    {
        User,
        Admin,
    }
}
