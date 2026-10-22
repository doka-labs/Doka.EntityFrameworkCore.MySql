namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Shares the models responsibility without changing the test fixture.</summary>
public sealed partial class MySqlQueryTranslationCoverageTests
{
    private sealed class CoverageEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? OptionalText { get; set; }
        public string Category { get; set; } = string.Empty;
        public double Score { get; set; }
        public double AltScore { get; set; }
        public int AltId { get; set; }
        public int SignedValue { get; set; }
        public long SignedLongValue { get; set; }
        public uint UnsignedValue { get; set; }
        public ulong UnsignedLongValue { get; set; }
        public int ShiftCount { get; set; }
        public Guid Token { get; set; }
        public Guid TextToken { get; set; }
        public Guid? OptionalToken { get; set; }
        public int? OptionalNumber { get; set; }
        public DateTime? OptionalCreatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateOnly BirthDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsActive { get; set; }
        public byte[] BinaryData { get; set; } = [];
        public UnsupportedLikeValue CustomValue { get; set; }
    }

    private enum UnsupportedLikeValue
    {
        First,
        Second,
    }

    private sealed class CoverageContext : DbContext
    {
        public CoverageContext(
            DbContextOptions<CoverageContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CoverageEntity>(e =>
            {
                e.ToTable("CoverageEntities");
                e.HasKey(x => x.Id);
                e.Property(x => x.CustomValue).HasConversion<int>();
                e
                    .Property(x => x.TextToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            });
        }
    }
}
