#!/usr/bin/env bash

# README examples are part of the provider's public contract. This gate extracts
# the owned snippet verbatim and compiles it against the current source project
# so documentation drift fails before publication.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readme_file="${repo_root}/README.md"
runtime_project="${repo_root}/src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj"
snippet_start='<!-- readme-autodetect-snippet begin -->'
snippet_end='<!-- readme-autodetect-snippet end -->'
scratch_dir="$(mktemp -d "${TMPDIR:-/tmp}/doka-readme-snippet.XXXXXX")"

cleanup() {
    rm -rf "${scratch_dir}"
}

trap cleanup EXIT

# Exact markers keep prose and unrelated code fences out of the compilation
# unit. A missing closing marker fails extraction instead of compiling a
# silently truncated example.
awk \
    -v start="${snippet_start}" \
    -v end="${snippet_end}" \
    '
        $0 == start { in_snippet = 1; next }
        $0 == end { in_snippet = 0; found_end = 1; next }
        in_snippet && $0 !~ /^```/ { print }
        END {
            if (!found_end) {
                exit 1
            }
        }
    ' \
    "${readme_file}" > "${scratch_dir}/Program.cs"

if [[ ! -s "${scratch_dir}/Program.cs" ]]; then
    echo "The README AutoDetect snippet is missing or empty." >&2
    exit 1
fi

cat > "${scratch_dir}/ReadmeSnippet.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(ProviderProject)" />
  </ItemGroup>
</Project>
EOF

# A project reference exercises the API from this checkout, while treating
# warnings as errors also catches stale nullable and analyzer-facing examples.
dotnet build "${scratch_dir}/ReadmeSnippet.csproj" \
    -p:ProviderProject="${runtime_project}" \
    --tl:off \
    -m:1 > /dev/null

echo "README snippets compile successfully."
