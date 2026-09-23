namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the ordinary collection translation used by relationship queries
/// on every supported database engine.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlCollectionDefaultIntegrationTests
{
    private const string ApprovalTableName = "IntCollectionDefaultApprovals";
    private const string ResponseTableName = "IntCollectionDefaultResponses";

    /// <summary>
    /// Verifies the default query shape and split include on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies the default query shape and split include on MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies the default query shape and split include on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies the default query shape and split include on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies the default query shape and split include on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies the default query shape and split include on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb123);

    /// <summary>
    /// Verifies that explicit JSON translation preserves the same result on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_json_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MySql84, useJsonParameter: true);

    /// <summary>
    /// Verifies that explicit JSON translation preserves the same result on MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_json_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MySql97, useJsonParameter: true);

    /// <summary>
    /// Verifies that explicit JSON translation preserves the same result on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_json_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb1011, useJsonParameter: true);

    /// <summary>
    /// Verifies that explicit JSON translation preserves the same result on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_json_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb114, useJsonParameter: true);

    /// <summary>
    /// Verifies that explicit JSON translation preserves the same result on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_json_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb118, useJsonParameter: true);

    /// <summary>
    /// Verifies that explicit JSON translation preserves the same result on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_json_collection_membership_with_split_include() =>
        AssertCollectionMembershipWithSplitIncludeAsync(IntegrationDatabaseTarget.MariaDb123, useJsonParameter: true);

    private static async Task AssertCollectionMembershipWithSplitIncludeAsync(
        IntegrationDatabaseTarget target,
        bool useJsonParameter = false
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<CollectionDefaultContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target),
            options =>
            {
                options.DefaultGuidFormat(MySqlGuidFormat.Char36);

                if (useJsonParameter)
                {
                    options.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter);
                }
            });
        await using var context = new CollectionDefaultContext(builder.Options);
        await DropTablesAsync(context);

        var includedRequestId = Guid.Parse("a671f743-7ac1-4273-9563-f9b5278e4790");
        var deletedRequestId = Guid.Parse("40fa3317-3e10-40b1-a777-95673252e29c");
        var unrelatedRequestId = Guid.Parse("9381e986-9175-4cfd-b582-70e655e7ad7e");

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.Approvals.AddRange(
                new CollectionApproval
                {
                    Id = Guid.Parse("6820c935-35b3-4b6d-b0de-9c86bc1eb5a2"),
                    RequestId = includedRequestId,
                    Responses =
                    [
                        new CollectionResponse { Id = 1 },
                        new CollectionResponse { Id = 2 },
                    ],
                },
                new CollectionApproval
                {
                    Id = Guid.Parse("de57cd5f-78f9-4024-a4f9-92a4212c11d8"),
                    RequestId = deletedRequestId,
                    IsDeleted = true,
                    Responses = [new CollectionResponse { Id = 3 }],
                },
                new CollectionApproval
                {
                    Id = Guid.Parse("09690e72-8b5e-4782-b51c-b196ca8986e1"),
                    RequestId = unrelatedRequestId,
                    Responses = [new CollectionResponse { Id = 4 }],
                });
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            var requestIds = new[] { includedRequestId, deletedRequestId };
            var query = context.Approvals
                .AsNoTracking()
                .Where(approval => !approval.IsDeleted)
                .Where(approval => requestIds.Contains(approval.RequestId))
                .Include(approval => approval.Responses)
                .AsSplitQuery();

            var sql = query.ToQueryString();
            var responses = await query.ToDictionaryAsync(
                approval => approval.RequestId,
                approval => approval.Responses,
                CancellationToken.None);

            Assert.Contains("@requestIds", sql, StringComparison.Ordinal);

            if (useJsonParameter)
            {
                Assert.Contains("JSON_TABLE", sql, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(" IN (", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
            }

            Assert.Single(responses);
            Assert.Equal([1, 2], responses[includedRequestId].Select(response => response.Id).Order());
            Assert.False(responses.ContainsKey(deletedRequestId));
            Assert.False(responses.ContainsKey(unrelatedRequestId));
        }
        finally
        {
            await DropTablesAsync(context);
        }
    }

    private static async Task DropTablesAsync(
        CollectionDefaultContext context
    )
    {
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{ResponseTableName}`",
            CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{ApprovalTableName}`",
            CancellationToken.None);
    }

    private sealed class CollectionDefaultContext(
        DbContextOptions<CollectionDefaultContext> options
    ) : DbContext(options)
    {
        public DbSet<CollectionApproval> Approvals => Set<CollectionApproval>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CollectionApproval>(approval =>
            {
                approval.ToTable(ApprovalTableName);
                approval.HasKey(row => row.Id);
                approval.HasMany(row => row.Responses)
                    .WithOne()
                    .HasForeignKey(response => response.ApprovalId);
            });

            modelBuilder.Entity<CollectionResponse>(response =>
            {
                response.ToTable(ResponseTableName);
                response.HasKey(row => row.Id);
            });
        }
    }

    private sealed class CollectionApproval
    {
        public Guid Id { get; set; }

        public Guid RequestId { get; set; }

        public bool IsDeleted { get; set; }

        public List<CollectionResponse> Responses { get; set; } = [];
    }

    private sealed class CollectionResponse
    {
        public int Id { get; set; }

        public Guid ApprovalId { get; set; }
    }
}
