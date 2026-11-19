namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public partial class DirectWriteBenchmark
{
    private static void SetNarrowColumns(
        MySqlInsertSettersBuilder<NarrowSource, NarrowTarget> setters
    ) => setters
        .SetProperty(row => row.Id, row => row.Id)
        .SetProperty(row => row.Value1, row => row.Value1)
        .SetProperty(row => row.Value2, row => row.Value2);

    private static void SetWideColumns(
        MySqlInsertSettersBuilder<WideSource, WideTarget> setters
    ) => setters
        .SetProperty(row => row.Id, row => row.Id)
        .SetProperty(row => row.C01, row => row.C01)
        .SetProperty(row => row.C02, row => row.C02)
        .SetProperty(row => row.C03, row => row.C03)
        .SetProperty(row => row.C04, row => row.C04)
        .SetProperty(row => row.C05, row => row.C05)
        .SetProperty(row => row.C06, row => row.C06)
        .SetProperty(row => row.C07, row => row.C07)
        .SetProperty(row => row.C08, row => row.C08)
        .SetProperty(row => row.C09, row => row.C09)
        .SetProperty(row => row.C10, row => row.C10)
        .SetProperty(row => row.C11, row => row.C11)
        .SetProperty(row => row.C12, row => row.C12)
        .SetProperty(row => row.C13, row => row.C13)
        .SetProperty(row => row.C14, row => row.C14)
        .SetProperty(row => row.C15, row => row.C15)
        .SetProperty(row => row.C16, row => row.C16)
        .SetProperty(row => row.C17, row => row.C17)
        .SetProperty(row => row.C18, row => row.C18)
        .SetProperty(row => row.C19, row => row.C19)
        .SetProperty(row => row.C20, row => row.C20)
        .SetProperty(row => row.C21, row => row.C21)
        .SetProperty(row => row.C22, row => row.C22)
        .SetProperty(row => row.C23, row => row.C23)
        .SetProperty(row => row.C24, row => row.C24)
        .SetProperty(row => row.C25, row => row.C25)
        .SetProperty(row => row.C26, row => row.C26)
        .SetProperty(row => row.C27, row => row.C27)
        .SetProperty(row => row.C28, row => row.C28)
        .SetProperty(row => row.C29, row => row.C29)
        .SetProperty(row => row.C30, row => row.C30)
        .SetProperty(row => row.C31, row => row.C31)
        .SetProperty(row => row.C32, row => row.C32)
        .SetProperty(row => row.C33, row => row.C33)
        .SetProperty(row => row.C34, row => row.C34)
        .SetProperty(row => row.C35, row => row.C35)
        .SetProperty(row => row.C36, row => row.C36)
        .SetProperty(row => row.C37, row => row.C37)
        .SetProperty(row => row.C38, row => row.C38)
        .SetProperty(row => row.C39, row => row.C39)
        .SetProperty(row => row.C40, row => row.C40)
        .SetProperty(row => row.C41, row => row.C41)
        .SetProperty(row => row.C42, row => row.C42)
        .SetProperty(row => row.C43, row => row.C43)
        .SetProperty(row => row.C44, row => row.C44)
        .SetProperty(row => row.C45, row => row.C45)
        .SetProperty(row => row.C46, row => row.C46)
        .SetProperty(row => row.C47, row => row.C47)
        .SetProperty(row => row.C48, row => row.C48)
        .SetProperty(row => row.C49, row => row.C49);

    private static WideTarget NewWideInput(
        int id
    ) => new()
    {
        Id = id,
        C01 = checked(id * 100 + 1),
        C02 = checked(id * 100 + 2),
        C03 = checked(id * 100 + 3),
        C04 = checked(id * 100 + 4),
        C05 = checked(id * 100 + 5),
        C06 = checked(id * 100 + 6),
        C07 = checked(id * 100 + 7),
        C08 = checked(id * 100 + 8),
        C09 = checked(id * 100 + 9),
        C10 = checked(id * 100 + 10),
        C11 = checked(id * 100 + 11),
        C12 = checked(id * 100 + 12),
        C13 = checked(id * 100 + 13),
        C14 = checked(id * 100 + 14),
        C15 = checked(id * 100 + 15),
        C16 = checked(id * 100 + 16),
        C17 = checked(id * 100 + 17),
        C18 = checked(id * 100 + 18),
        C19 = checked(id * 100 + 19),
        C20 = checked(id * 100 + 20),
        C21 = checked(id * 100 + 21),
        C22 = checked(id * 100 + 22),
        C23 = checked(id * 100 + 23),
        C24 = checked(id * 100 + 24),
        C25 = checked(id * 100 + 25),
        C26 = checked(id * 100 + 26),
        C27 = checked(id * 100 + 27),
        C28 = checked(id * 100 + 28),
        C29 = checked(id * 100 + 29),
        C30 = checked(id * 100 + 30),
        C31 = checked(id * 100 + 31),
        C32 = checked(id * 100 + 32),
        C33 = checked(id * 100 + 33),
        C34 = checked(id * 100 + 34),
        C35 = checked(id * 100 + 35),
        C36 = checked(id * 100 + 36),
        C37 = checked(id * 100 + 37),
        C38 = checked(id * 100 + 38),
        C39 = checked(id * 100 + 39),
        C40 = checked(id * 100 + 40),
        C41 = checked(id * 100 + 41),
        C42 = checked(id * 100 + 42),
        C43 = checked(id * 100 + 43),
        C44 = checked(id * 100 + 44),
        C45 = checked(id * 100 + 45),
        C46 = checked(id * 100 + 46),
        C47 = checked(id * 100 + 47),
        C48 = checked(id * 100 + 48),
        C49 = checked(id * 100 + 49),
    };

    private sealed class NarrowSource
    {
        public int Id { get; set; }
        public int Value1 { get; set; }
        public int Value2 { get; set; }
    }

    private sealed class NarrowTarget
    {
        public int Id { get; set; }
        public int Value1 { get; set; }
        public int Value2 { get; set; }
    }

    private sealed class WideSource
    {
        public int Id { get; set; }
        public int C01 { get; set; }
        public int C02 { get; set; }
        public int C03 { get; set; }
        public int C04 { get; set; }
        public int C05 { get; set; }
        public int C06 { get; set; }
        public int C07 { get; set; }
        public int C08 { get; set; }
        public int C09 { get; set; }
        public int C10 { get; set; }
        public int C11 { get; set; }
        public int C12 { get; set; }
        public int C13 { get; set; }
        public int C14 { get; set; }
        public int C15 { get; set; }
        public int C16 { get; set; }
        public int C17 { get; set; }
        public int C18 { get; set; }
        public int C19 { get; set; }
        public int C20 { get; set; }
        public int C21 { get; set; }
        public int C22 { get; set; }
        public int C23 { get; set; }
        public int C24 { get; set; }
        public int C25 { get; set; }
        public int C26 { get; set; }
        public int C27 { get; set; }
        public int C28 { get; set; }
        public int C29 { get; set; }
        public int C30 { get; set; }
        public int C31 { get; set; }
        public int C32 { get; set; }
        public int C33 { get; set; }
        public int C34 { get; set; }
        public int C35 { get; set; }
        public int C36 { get; set; }
        public int C37 { get; set; }
        public int C38 { get; set; }
        public int C39 { get; set; }
        public int C40 { get; set; }
        public int C41 { get; set; }
        public int C42 { get; set; }
        public int C43 { get; set; }
        public int C44 { get; set; }
        public int C45 { get; set; }
        public int C46 { get; set; }
        public int C47 { get; set; }
        public int C48 { get; set; }
        public int C49 { get; set; }
    }

    private sealed class WideTarget
    {
        public int Id { get; set; }
        public int C01 { get; set; }
        public int C02 { get; set; }
        public int C03 { get; set; }
        public int C04 { get; set; }
        public int C05 { get; set; }
        public int C06 { get; set; }
        public int C07 { get; set; }
        public int C08 { get; set; }
        public int C09 { get; set; }
        public int C10 { get; set; }
        public int C11 { get; set; }
        public int C12 { get; set; }
        public int C13 { get; set; }
        public int C14 { get; set; }
        public int C15 { get; set; }
        public int C16 { get; set; }
        public int C17 { get; set; }
        public int C18 { get; set; }
        public int C19 { get; set; }
        public int C20 { get; set; }
        public int C21 { get; set; }
        public int C22 { get; set; }
        public int C23 { get; set; }
        public int C24 { get; set; }
        public int C25 { get; set; }
        public int C26 { get; set; }
        public int C27 { get; set; }
        public int C28 { get; set; }
        public int C29 { get; set; }
        public int C30 { get; set; }
        public int C31 { get; set; }
        public int C32 { get; set; }
        public int C33 { get; set; }
        public int C34 { get; set; }
        public int C35 { get; set; }
        public int C36 { get; set; }
        public int C37 { get; set; }
        public int C38 { get; set; }
        public int C39 { get; set; }
        public int C40 { get; set; }
        public int C41 { get; set; }
        public int C42 { get; set; }
        public int C43 { get; set; }
        public int C44 { get; set; }
        public int C45 { get; set; }
        public int C46 { get; set; }
        public int C47 { get; set; }
        public int C48 { get; set; }
        public int C49 { get; set; }
    }

    private sealed class DirectWriteContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public DirectWriteContext(
            DbContextOptions<DirectWriteContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            Configure<NarrowSource>(modelBuilder, NarrowSourceTable);
            Configure<NarrowTarget>(modelBuilder, NarrowTargetTable);
            Configure<WideSource>(modelBuilder, WideSourceTable);
            Configure<WideTarget>(modelBuilder, WideTargetTable);
        }

        private static void Configure<TEntity>(
            ModelBuilder modelBuilder,
            string table
        )
            where TEntity : class
        {
            var entity = modelBuilder.Entity<TEntity>();
            entity.ToTable(table);
            entity.HasKey("Id");
            entity
                .Property<int>("Id")
                .ValueGeneratedNever();
        }
    }
}
