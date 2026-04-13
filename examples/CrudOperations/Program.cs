using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;

// ── CRUD Operations with Doka.EntityFrameworkCore.MySql ──
//
// Demonstrates: Add, Update, Delete, Query with filtering, sorting, and pagination.

var connectionString = Environment.GetEnvironmentVariable("DOKA_MYSQL_CONNECTION_STRING")
    ?? "Server=localhost;Port=33068;Database=crud_example;User ID=root;Password=root_password;";

var options = new DbContextOptionsBuilder<CrudContext>()
    .UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)))
    .Options;

using var context = new CrudContext(options);
context.Database.EnsureCreated();

// ── CREATE ──
context.Tasks.AddRange(
    new TaskItem { Title = "Buy groceries", Priority = 1, IsCompleted = false },
    new TaskItem { Title = "Write documentation", Priority = 3, IsCompleted = false },
    new TaskItem { Title = "Fix bug #42", Priority = 2, IsCompleted = true },
    new TaskItem { Title = "Deploy release", Priority = 1, IsCompleted = false },
    new TaskItem { Title = "Review PR", Priority = 2, IsCompleted = true }
);
context.SaveChanges();
Console.WriteLine($"Created {context.Tasks.Count()} tasks.");

// ── READ with filtering ──
var openTasks = context.Tasks
    .Where(t => !t.IsCompleted)
    .OrderBy(t => t.Priority)
    .ToList();
Console.WriteLine($"Open tasks (sorted by priority): {openTasks.Count}");

// ── READ with pagination ──
var page = context.Tasks
    .OrderBy(t => t.Id)
    .Skip(2)
    .Take(2)
    .ToList();
Console.WriteLine($"Page 2 (2 items): {string.Join(", ", page.Select(t => t.Title))}");

// ── UPDATE ──
var taskToUpdate = context.Tasks.First(t => t.Title == "Fix bug #42");
taskToUpdate.IsCompleted = false;
taskToUpdate.Priority = 1;
context.SaveChanges();
Console.WriteLine($"Updated '{taskToUpdate.Title}' — IsCompleted={taskToUpdate.IsCompleted}");

// ── DELETE ──
var taskToDelete = context.Tasks.First(t => t.Title == "Review PR");
context.Tasks.Remove(taskToDelete);
context.SaveChanges();
Console.WriteLine($"Deleted '{taskToDelete.Title}'. Remaining: {context.Tasks.Count()}");

context.Database.EnsureDeleted();
Console.WriteLine("CRUD operations example completed successfully.");

// ── DbContext ──

public class CrudContext : DbContext
{
    public CrudContext(DbContextOptions<CrudContext> options) : base(options) { }
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("Tasks");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Title).HasMaxLength(200);
        });
    }
}

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsCompleted { get; set; }
}
