#!/usr/bin/env bash

# README C# fences are customer-facing source, not illustrative pseudocode.
# This gate extracts every fence, maps compiler diagnostics back to the README
# line, and compiles them together against both current provider projects.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readme_file="${repo_root}/README.md"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
spatial_project_root="${repo_root}/src/Doka.EntityFrameworkCore.MySql.NetTopologySuite"
spatial_project="${spatial_project_root}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.csproj"
scratch_dir="$(mktemp -d "${TMPDIR:-/tmp}/doka-readme-snippets.XXXXXX")"
snippet_dir="${scratch_dir}/snippets"
manifest_file="${scratch_dir}/manifest.tsv"
type_manifest_file="${scratch_dir}/type-manifest.tsv"
program_file="${scratch_dir}/ReadmeSnippets.cs"

cleanup() {
    rm -rf "${scratch_dir}"
}

trap cleanup EXIT
mkdir -p "${snippet_dir}"
: > "${manifest_file}"
: > "${type_manifest_file}"

# A single pass rejects an unclosed fence and records each original line so a
# compiler error points at README.md instead of an opaque generated file.
awk \
    -v output_directory="${snippet_dir}" \
    -v manifest="${manifest_file}" \
    '
        $0 == "```csharp" {
            if (inside) {
                exit 2
            }

            count++
            start_line = NR + 1
            file = sprintf("%s/snippet-%03d.cs", output_directory, count)
            print file "\t" start_line >> manifest
            inside = 1
            next
        }

        inside && $0 == "```" {
            close(file)
            inside = 0
            next
        }

        inside {
            print > file
        }

        END {
            if (inside || count == 0) {
                exit 1
            }
        }
    ' \
    "${readme_file}"

cat > "${scratch_dir}/ReadmeSnippets.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="ReadmeSnippets.cs" />
    <ProjectReference Include="$(ProviderProject)" />
    <ProjectReference Include="$(SpatialProject)" />
  </ItemGroup>
</Project>
EOF

cat > "${program_file}" <<'EOF'
using System;
using System.Data.Common;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using NetTopologySuite.Geometries;

internal static class ReadmeSnippetHarness
{
    private static readonly string connectionString =
        "Server=localhost;Database=readme;User ID=app;Password=secret;";
    private static readonly MySqlServerVersion serverVersion =
        MySqlServerVersion.MySql(new Version(8, 4, 0));
    private static readonly DbContextOptionsBuilder<AppDbContext> options = new();
    private static readonly IServiceCollection services = new ServiceCollection();
    private static readonly DbConnection myConnection = new MySqlConnection(connectionString);
    private static readonly ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
    private static readonly ModelBuilder modelBuilder = new(new ConventionSet());
    private static readonly Point origin = new(13.4050, 52.5200) { SRID = 4326 };
    private static readonly ReadmeContext context = new(
        new DbContextOptionsBuilder<ReadmeContext>()
            .UseMySql(connectionString, serverVersion)
            .Options);
EOF

snippet_index=0
while IFS=$'\t' read -r snippet_file start_line; do
    snippet_index=$(( snippet_index + 1 ))
    body_file="${scratch_dir}/body-${snippet_index}.cs"

    # Namespace imports belong at compilation-unit scope. `using var` remains
    # in the body because the capitalized-name pattern matches directives only.
    sed -E '/^using [A-Z][A-Za-z0-9_.]*;$/d' "${snippet_file}" > "${body_file}"

    if grep -Eq '^(public|internal|protected|private).*(class|record|struct|interface) ' "${body_file}"; then
        printf '%s\t%s\n' "${body_file}" "${start_line}" >> "${type_manifest_file}"
        continue
    fi

    {
        echo
        echo "    private static async Task Snippet${snippet_index}Async()"
        echo "    {"
        echo "#line ${start_line} \"${readme_file}\""
        sed 's/^/        /' "${body_file}"
        echo "#line default"
        echo "#line hidden"
        # Comment-only fences still produce a valid async method and therefore
        # remain part of the exhaustive fence inventory.
        echo "        await Task.CompletedTask;"
        echo "    }"
    } >> "${program_file}"
done < "${manifest_file}"

cat >> "${program_file}" <<'EOF'
}
EOF

while IFS=$'\t' read -r type_file type_line; do
    {
        echo
        echo "#line ${type_line} \"${readme_file}\""
        cat "${type_file}"
        echo "#line default"
        echo "#line hidden"
    } >> "${program_file}"
done < "${type_manifest_file}"

cat >> "${program_file}" <<'EOF'

internal sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
}

internal sealed class ReadmeContext : DbContext
{
    public ReadmeContext(DbContextOptions<ReadmeContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Place> Places => Set<Place>();
}

internal sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

internal sealed class Article
{
    public int Id { get; set; }
    public string Body { get; set; } = string.Empty;
}

internal sealed class Order
{
    public long Id { get; set; }
}

internal sealed class OrderWithGuid
{
    public Guid Id { get; set; }
}

internal sealed class User
{
    public int Id { get; set; }
    public string InternalNotes { get; set; } = string.Empty;
}

internal sealed class Place
{
    public int Id { get; set; }
    public Point Location { get; set; } = null!;
}
EOF

dotnet build "${scratch_dir}/ReadmeSnippets.csproj" \
    --configuration Release \
    -p:ProviderProject="${runtime_project}" \
    -p:SpatialProject="${spatial_project}" \
    --tl:off \
    -m:1

echo "Compiled ${snippet_index} README C# snippets against the current provider packages."
