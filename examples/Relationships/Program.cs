using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;

// ── Relationships with Doka.EntityFrameworkCore.MySql ──
//
// Demonstrates: 1:N (Blog → Posts), M:N (Student ↔ Course),
// self-referencing (Employee → Manager), Include/ThenInclude.

var connectionString = Environment.GetEnvironmentVariable("DOKA_MYSQL_CONNECTION_STRING")
    ?? "Server=localhost;Port=33068;Database=relationships_example;User ID=root;Password=root_password;";

var options = new DbContextOptionsBuilder<RelContext>()
    .UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)))
    .Options;

using var context = new RelContext(options);
context.Database.EnsureCreated();

// ── 1:N — Blog with Posts ──
var blog = new Blog
{
    Title = "EF Core with MySQL",
    Posts =
    {
        new Post { Content = "Getting started with Doka provider" },
        new Post { Content = "Advanced query translation" },
    },
};
context.Blogs.Add(blog);
context.SaveChanges();

// Eager-load posts via Include.
var blogWithPosts = context.Blogs
    .Include(b => b.Posts)
    .First();
Console.WriteLine($"Blog '{blogWithPosts.Title}' has {blogWithPosts.Posts.Count} posts.");

// ── M:N — Students and Courses ──
var student = new Student { Name = "Alice" };
var course1 = new Course { Title = "Databases" };
var course2 = new Course { Title = "Algorithms" };
student.Courses.Add(course1);
student.Courses.Add(course2);
context.Students.Add(student);
context.SaveChanges();

var loadedStudent = context.Students
    .Include(s => s.Courses)
    .First(s => s.Name == "Alice");
Console.WriteLine($"Student '{loadedStudent.Name}' enrolled in: {string.Join(", ", loadedStudent.Courses.Select(c => c.Title))}");

// ── Self-referencing — Employee hierarchy ──
var ceo = new Employee { Name = "CEO" };
var vp = new Employee { Name = "VP Engineering", Manager = ceo };
var dev = new Employee { Name = "Developer", Manager = vp };
context.Employees.AddRange(ceo, vp, dev);
context.SaveChanges();

var tree = context.Employees
    .Include(e => e.Subordinates)
    .First(e => e.Name == "CEO");
Console.WriteLine($"CEO has {tree.Subordinates.Count} direct reports.");

context.Database.EnsureDeleted();
Console.WriteLine("Relationships example completed successfully.");

// ── Entities ──

public class Blog
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<Post> Posts { get; set; } = [];
}

public class Post
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
}

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Course> Courses { get; set; } = [];
}

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<Student> Students { get; set; } = [];
}

public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }
    public List<Employee> Subordinates { get; set; } = [];
}

// ── DbContext ──

public class RelContext : DbContext
{
    public RelContext(DbContextOptions<RelContext> options) : base(options) { }

    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>(e => { e.ToTable("Blogs"); e.Property(b => b.Title).HasMaxLength(200); });
        modelBuilder.Entity<Post>(e => { e.ToTable("Posts"); e.Property(p => p.Content).HasMaxLength(2000); });

        modelBuilder.Entity<Student>(e => { e.ToTable("Students"); e.Property(s => s.Name).HasMaxLength(100); });
        modelBuilder.Entity<Course>(e => { e.ToTable("Courses"); e.Property(c => c.Title).HasMaxLength(200); });
        modelBuilder.Entity<Student>().HasMany(s => s.Courses).WithMany(c => c.Students);

        modelBuilder.Entity<Employee>(e =>
        {
            e.ToTable("Employees");
            e.Property(emp => emp.Name).HasMaxLength(100);
            e.HasOne(emp => emp.Manager).WithMany(m => m.Subordinates).HasForeignKey(emp => emp.ManagerId);
        });
    }
}
