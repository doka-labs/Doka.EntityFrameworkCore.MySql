using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.MySql.Examples;
using Microsoft.EntityFrameworkCore;

var database = ExampleDatabaseConfiguration.Create("doka_example_docker_integration");
var cancellationToken = CancellationToken.None;
var options = new DbContextOptionsBuilder<DockerContext>()
    .UseMySql(database.ConnectionString, database.ServerVersion)
    .Options;

await using var context = new DockerContext(options);
await context.Database.EnsureDeletedAsync(cancellationToken);

try
{
    await context.Database.EnsureCreatedAsync(cancellationToken);

    // CanConnect probes the configured catalog, not only the database server.
    // Running it after EnsureDeleted would therefore report a false negative.
    if (!await context.Database.CanConnectAsync(cancellationToken))
    {
        throw new InvalidOperationException($"The configured {database.Target} container is not reachable.");
    }

    context.Probes.Add(new ContainerProbe { Message = "provider connection succeeded" });
    await context.SaveChangesAsync(cancellationToken);

    await context.Database.OpenConnectionAsync(cancellationToken);
    await using var command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = "SELECT VERSION()";
    var detectedVersion = Convert.ToString(
        await command.ExecuteScalarAsync(cancellationToken),
        System.Globalization.CultureInfo.InvariantCulture);

    var probeCount = await context.Probes.AsNoTracking().CountAsync(cancellationToken);

    if (string.IsNullOrWhiteSpace(detectedVersion) || probeCount != 1)
    {
        throw new InvalidOperationException("The container round-trip did not return the expected values.");
    }

    Console.WriteLine($"{database.Target}: server={detectedVersion}, rows={probeCount}");
}
finally
{
    await context.Database.CloseConnectionAsync();
    await context.Database.EnsureDeletedAsync(cancellationToken);
}

internal sealed class DockerContext : DbContext
{
    public DockerContext(
        DbContextOptions<DockerContext> options
    ) : base(options) { }

    public DbSet<ContainerProbe> Probes => Set<ContainerProbe>();
}

internal sealed class ContainerProbe
{
    public int Id { get; set; }

    public string Message { get; set; } = string.Empty;
}
