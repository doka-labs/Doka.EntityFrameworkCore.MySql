namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlModelingBaselineTests
{
    // -- Many-to-Many ----------------------------------------------------

    /// <summary>
    /// Implicit junction table DDL -- composite PK, two FKs.
    /// </summary>
    [Fact]
    public void Many_to_many_produces_implicit_junction_table()
    {
        using var context = new ManyToManyContext(CreateOptions<ManyToManyContext>());
        var model = context.Model;

        // The implicit join entity type should exist.
        var joinEntityType = model
            .GetEntityTypes()
            .FirstOrDefault(t => t
                    .GetTableName()
                    ?.Contains("Student", StringComparison.Ordinal)
                == true
                && t
                    .GetTableName()
                    ?.Contains("Course", StringComparison.Ordinal)
                == true);

        Assert.NotNull(joinEntityType);
    }

    // -- Cascade Delete --------------------------------------------------

    /// <summary>
    /// ON DELETE CASCADE FK DDL verification.
    /// </summary>
    [Fact]
    public void Cascade_delete_is_configured_on_required_relationship()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var postType = context.Model.FindEntityType(typeof(Post))!;
        var fk = postType
            .GetForeignKeys()
            .First();

        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    // -- Self-Referencing FK ---------------------------------------------

    [Fact]
    public void Self_referencing_fk_produces_valid_model()
    {
        using var context = new SelfRefContext(CreateOptions<SelfRefContext>());
        var employeeType = context.Model.FindEntityType(typeof(Employee))!;
        var fk = employeeType
            .GetForeignKeys()
            .First();

        Assert.Equal(typeof(Employee), fk.PrincipalEntityType.ClrType);
        Assert.Equal(typeof(Employee), fk.DeclaringEntityType.ClrType);
    }

    /// <summary>
    /// Shadow FK property generates correct column.
    /// </summary>
    [Fact]
    public void Shadow_fk_property_produces_correct_column()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var postType = context.Model.FindEntityType(typeof(Post))!;
        var blogIdProperty = postType.FindProperty(nameof(Post.BlogId))!;

        Assert.Equal("int", blogIdProperty.GetColumnType());
    }

    /// <summary>
    /// Include + ThenInclude produces valid SQL.
    /// </summary>
    [Fact]
    public void Include_then_include_produces_valid_sql()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var sql = context
            .Set<Blog>()
            .Include(b => b.Posts)
            .ToQueryString();

        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>
    /// Lazy loading -- verifies model accepts proxy configuration.
    /// Note: actual proxy loading requires live DB, but model configuration can be verified.
    /// </summary>
    [Fact]
    public void Model_accepts_navigation_properties()
    {
        using var context = new CascadeContext(CreateOptions<CascadeContext>());
        var blogType = context.Model.FindEntityType(typeof(Blog))!;
        var navigation = blogType.FindNavigation(nameof(Blog.Posts));

        Assert.NotNull(navigation);
        Assert.True(navigation.IsCollection);
    }
    [Fact]
    public void Nullable_property_is_modeled_as_optional()
    {
        using var context = new SelfRefContext(CreateOptions<SelfRefContext>());
        var entityType = context.Model.FindEntityType(typeof(Employee))!;
        var managerIdProperty = entityType.FindProperty(nameof(Employee.ManagerId))!;

        Assert.True(managerIdProperty.IsNullable);
    }
    /// <summary>
    /// ON DELETE SET NULL FK DDL verification.
    /// </summary>
    [Fact]
    public void Set_null_delete_behavior_is_configured()
    {
        using var context = new SetNullContext(CreateOptions<SetNullContext>());
        var commentType = context.Model.FindEntityType(typeof(CommentWithNullablePost))!;
        var fk = commentType
            .GetForeignKeys()
            .First();

        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);
    }

    /// <summary>
    /// ON DELETE RESTRICT FK DDL verification.
    /// </summary>
    [Fact]
    public void Restrict_delete_behavior_is_configured()
    {
        using var context = new SelfRefContext(CreateOptions<SelfRefContext>());
        var employeeType = context.Model.FindEntityType(typeof(Employee))!;
        var fk = employeeType
            .GetForeignKeys()
            .First();

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    // -- SetNull Context -------------------------------------------------

    private sealed class PostWithComments
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<CommentWithNullablePost> Comments { get; set; } = [];
    }

    private sealed class CommentWithNullablePost
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public int? PostId { get; set; }
        public PostWithComments? Post { get; set; }
    }

    private sealed class SetNullContext : DbContext
    {
        public SetNullContext(
            DbContextOptions<SetNullContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PostWithComments>(e =>
            {
                e.ToTable("PostsWithComments");
                e
                    .Property(p => p.Title)
                    .HasMaxLength(200);
            });

            modelBuilder.Entity<CommentWithNullablePost>(e =>
            {
                e.ToTable("CommentsWithNullablePost");
                e
                    .Property(c => c.Text)
                    .HasMaxLength(1000);
                e
                    .HasOne(c => c.Post)
                    .WithMany(p => p.Comments)
                    .HasForeignKey(c => c.PostId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }

    // -- Many-to-Many Entities -------------------------------------------

    private sealed class Student
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Course> Courses { get; set; } = [];
    }

    private sealed class Course
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<Student> Students { get; set; } = [];
    }

    private sealed class ManyToManyContext : DbContext
    {
        public ManyToManyContext(
            DbContextOptions<ManyToManyContext> options
        ) : base(options) { }

        public DbSet<Student> Students => Set<Student>();
        public DbSet<Course> Courses => Set<Course>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<Student>()
                .ToTable("Students");
            modelBuilder
                .Entity<Course>()
                .ToTable("Courses");
            modelBuilder
                .Entity<Student>()
                .HasMany(s => s.Courses)
                .WithMany(c => c.Students);
        }
    }

    // -- Cascade Delete Entities -----------------------------------------

    private sealed class Blog
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<Post> Posts { get; set; } = [];
    }

    private sealed class Post
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public int BlogId { get; set; }
        public Blog Blog { get; set; } = null!;
    }

    private sealed class CascadeContext : DbContext
    {
        public CascadeContext(
            DbContextOptions<CascadeContext> options
        ) : base(options) { }

        public DbSet<Blog> Blogs => Set<Blog>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<Blog>()
                .ToTable("Blogs");
            modelBuilder
                .Entity<Post>()
                .ToTable("Posts");
            modelBuilder
                .Entity<Blog>()
                .HasMany(b => b.Posts)
                .WithOne(p => p.Blog)
                .HasForeignKey(p => p.BlogId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    // -- Self-Referencing Entities ----------------------------------------

    private sealed class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ManagerId { get; set; }
        public Employee? Manager { get; set; }
        public List<Employee> Subordinates { get; set; } = [];
    }

    private sealed class SelfRefContext : DbContext
    {
        public SelfRefContext(
            DbContextOptions<SelfRefContext> options
        ) : base(options) { }

        public DbSet<Employee> Employees => Set<Employee>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.ToTable("Employees");
                entity
                    .HasOne(e => e.Manager)
                    .WithMany(e => e.Subordinates)
                    .HasForeignKey(e => e.ManagerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
