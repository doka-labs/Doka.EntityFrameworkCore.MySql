using System.IO;
using System.Security;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Compiles scaffolded source in an isolated temporary project and executes its
/// schema-reconstruction, CRUD, generated-value, and relationship-fixup contract.
/// </summary>
internal static class GeneratedContextRuntimeVerifier
{
    private const string ConnectionStringVariable = "DOKA_GENERATED_CONTEXT_CONNECTION";
    private const string ServerVersionVariable = "DOKA_GENERATED_CONTEXT_SERVER_VERSION";
    private const string SuccessMarker = "DOKA_GENERATED_CONTEXT_OK";

    public static async Task VerifyAsync(
        ScaffoldedModel scaffoldedModel,
        string connectionString,
        string serverVersionText
    )
    {
        ArgumentNullException.ThrowIfNull(scaffoldedModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverVersionText);

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "doka-generated-context-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            await WriteProjectAsync(temporaryDirectory, scaffoldedModel)
                .ConfigureAwait(false);

            var result = await RunProjectAsync(
                    temporaryDirectory,
                    connectionString,
                    serverVersionText)
                .ConfigureAwait(false);

            if (result.ExitCode != 0
                || !result.StandardOutput.Contains(SuccessMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated context execution exited with code {result.ExitCode}."
                    + Environment.NewLine
                    + $"stdout: {result.StandardOutput}"
                    + Environment.NewLine
                    + $"stderr: {result.StandardError}");
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task WriteProjectAsync(
        string temporaryDirectory,
        ScaffoldedModel scaffoldedModel
    )
    {
        var repositoryRoot = FindRepositoryRoot();
        var providerProject = EscapeXml(
            Path.Combine(
                repositoryRoot,
                "src",
                "Doka.EntityFrameworkCore.MySql",
                "Doka.EntityFrameworkCore.MySql.csproj"));

        var spatialProject = EscapeXml(
            Path.Combine(
                repositoryRoot,
                "src",
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"));

        var efCoreVersion = GetEfCoreVersion();
        var projectCode =
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>disable</ImplicitUsings>
                <WarningsAsErrors>true</WarningsAsErrors>
                <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                <NuGetAudit>false</NuGetAudit>
                <DokaEfCoreVersion>{efCoreVersion}</DokaEfCoreVersion>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{providerProject}" />
                <ProjectReference Include="{spatialProject}" />
              </ItemGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "GeneratedContext.csproj"),
                projectCode)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "Program.cs"),
                CreateProgramCode())
            .ConfigureAwait(false);
        await WriteScaffoldedFileAsync(temporaryDirectory, scaffoldedModel.ContextFile)
            .ConfigureAwait(false);

        foreach (var additionalFile in scaffoldedModel.AdditionalFiles)
        {
            await WriteScaffoldedFileAsync(temporaryDirectory, additionalFile)
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteScaffoldedFileAsync(
        string temporaryDirectory,
        ScaffoldedFile scaffoldedFile
    )
    {
        var fileName = Path.GetFileName(scaffoldedFile.Path);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("A scaffolded source file did not have a valid file name.");
        }

        await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, fileName),
                scaffoldedFile.Code)
            .ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunProjectAsync(
        string temporaryDirectory,
        string connectionString,
        string serverVersionText
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(temporaryDirectory, "GeneratedContext.csproj"));
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(Path.Combine(temporaryDirectory, "artifacts"));
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        startInfo.Environment[ConnectionStringVariable] = connectionString;
        startInfo.Environment[ServerVersionVariable] = serverVersionText;
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated context process could not be started.");

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        try
        {
            await process
                .WaitForExitAsync(timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            process.Kill(entireProcessTree: true);
            await process
                .WaitForExitAsync()
                .ConfigureAwait(false);

            throw new TimeoutException(
                "Generated context compilation and execution exceeded two minutes.",
                exception);
        }

        string[] output;

        try
        {
            output = await Task
                .WhenAll(standardOutput, standardError)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                "Generated context build output streams did not close after the process exited.",
                exception);
        }

        return new ProcessResult(process.ExitCode, output[0], output[1]);
    }

    private static string GetEfCoreVersion()
    {
        var version = typeof(DbContext).Assembly.GetName().Version
            ?? throw new InvalidOperationException("The EF Core assembly has no version.");

        return FormattableString.Invariant($"{version.Major}.{version.Minor}.{version.Build}");
    }

    private static string EscapeXml(
        string value
    ) => SecurityElement.Escape(value)
        ?? throw new InvalidOperationException("A project path could not be escaped for XML.");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the integration-test output path.");
    }

    private static string CreateProgramCode() =>
        """
        using System;
        using System.Collections;
        using System.Linq;
        using System.Threading.Tasks;
        using Doka.EntityFrameworkCore.MySql;
        using Doka.Scaffolding;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Design;
        using Microsoft.EntityFrameworkCore.Infrastructure;
        using Microsoft.EntityFrameworkCore.Metadata;
        using Microsoft.EntityFrameworkCore.Migrations;

        var connectionString = Environment.GetEnvironmentVariable("DOKA_GENERATED_CONTEXT_CONNECTION")
            ?? throw new InvalidOperationException("The generated context connection string is missing.");
        var serverVersionText = Environment.GetEnvironmentVariable("DOKA_GENERATED_CONTEXT_SERVER_VERSION")
            ?? throw new InvalidOperationException("The generated context server version is missing.");
        var serverVersion = MySqlServerVersion.Parse(serverVersionText);
        var options = new DbContextOptionsBuilder<RuntimeSchemaContext>()
            .UseMySql(
                connectionString,
                serverVersion,
                providerOptions => providerOptions.UseNetTopologySuite())
            .Options;

        await using var context = new RuntimeSchemaContext(options);
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var modelDiffer = context.GetService<IMigrationsModelDiffer>();
        var sqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        var operations = modelDiffer.GetDifferences(
            null,
            designTimeModel.GetRelationalModel());

        foreach (var command in sqlGenerator.Generate(operations, designTimeModel))
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        }

        var parentEntityType = context.Model.GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "doka_scaffold_core_parent");
        var childEntityType = context.Model.GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "doka_scaffold_core_child");
        var parent = Activator.CreateInstance(parentEntityType.ClrType)
            ?? throw new InvalidOperationException("The generated parent could not be created.");

        SetProperty(parent, "Code", "runtime-parent");
        SetProperty(parent, "OptionalCount", 9);
        context.Add(parent);
        await context.SaveChangesAsync();
        await context.Entry(parent).ReloadAsync();

        var parentId = GetInt32Property(parent, "Id");
        var computedCount = GetInt32Property(parent, "ComputedCount");

        if (parentId <= 0 || computedCount != 10)
        {
            throw new InvalidOperationException("Generated parent values were not read back correctly.");
        }

        var child = Activator.CreateInstance(childEntityType.ClrType)
            ?? throw new InvalidOperationException("The generated child could not be created.");

        SetProperty(child, "ParentId", parentId);
        context.Add(child);
        await context.SaveChangesAsync();
        var childId = GetInt32Property(child, "Id");

        if (childId <= 0)
        {
            throw new InvalidOperationException("The generated child key was not populated.");
        }

        context.ChangeTracker.Clear();

        var loadedChild = await context.FindAsync(
                childEntityType.ClrType,
                new object?[] { childId })
            ?? throw new InvalidOperationException("The generated child could not be read.");
        var loadedParent = await context.FindAsync(
                parentEntityType.ClrType,
                new object?[] { parentId })
            ?? throw new InvalidOperationException("The generated parent could not be read.");
        var foreignKey = childEntityType.GetForeignKeys().Single();
        var dependentNavigation = foreignKey.DependentToPrincipal
            ?? throw new InvalidOperationException("The dependent navigation is missing.");
        var principalNavigation = foreignKey.PrincipalToDependent
            ?? throw new InvalidOperationException("The principal navigation is missing.");
        var fixedUpParent = dependentNavigation.PropertyInfo?.GetValue(loadedChild);
        var fixedUpChildren = principalNavigation.PropertyInfo?.GetValue(loadedParent) as IEnumerable;

        if (!ReferenceEquals(fixedUpParent, loadedParent)
            || fixedUpChildren is null
            || !fixedUpChildren.Cast<object>().Contains(loadedChild))
        {
            throw new InvalidOperationException("Generated relationship fixup did not preserve both navigations.");
        }

        await context.Database.OpenConnectionAsync();
        await using var richTableCommand = context.Database.GetDbConnection().CreateCommand();
        richTableCommand.CommandText = "SELECT COUNT(*) FROM `doka_scaffold_index_store`;";
        _ = await richTableCommand.ExecuteScalarAsync();

        Console.WriteLine("DOKA_GENERATED_CONTEXT_OK");

        static void SetProperty(
            object instance,
            string propertyName,
            object value
        )
        {
            var property = instance.GetType().GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Generated property '{propertyName}' is missing.");
            property.SetValue(instance, value);
        }

        static int GetInt32Property(
            object instance,
            string propertyName
        )
        {
            var property = instance.GetType().GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Generated property '{propertyName}' is missing.");

            return Convert.ToInt32(property.GetValue(instance), System.Globalization.CultureInfo.InvariantCulture);
        }
        """;

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
