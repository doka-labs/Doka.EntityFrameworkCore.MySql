using System.Diagnostics;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class QualificationStageExecutionTests
{
    private const string StubSourceCommit = "0000000000000000000000000000000000000001";

    private const string StubDatabaseImage =
        "mysql:8.4.13@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly string[] s_packageIds =
    [
        "Doka.EntityFrameworkCore.MySql", "Doka.EntityFrameworkCore.MySql.NetTopologySuite", "Doka.Caching.MySql",
    ];

    private static ProcessResult Run(
        string root,
        string script,
        IReadOnlyDictionary<string, string>? environment = null,
        params string[] arguments
    )
    {
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start '{script}'.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        // WHY: These synchronous contract tests wait for completion while both redirected pipes drain asynchronously.
        // ReSharper disable once MethodHasAsyncOverload
        process.WaitForExit();

        return new ProcessResult(
            process.ExitCode,
            stdout
                .GetAwaiter()
                .GetResult(),
            stderr
                .GetAwaiter()
                .GetResult());
    }

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

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    /// <summary>Requires exactly one option and returns its unsplit argument value.</summary>
    private static string ArgumentAfter(
        string[] arguments,
        string option
    )
    {
        var index = Array.IndexOf(arguments, option);
        Assert.Equal(1, arguments.Count(argument => argument == option));
        Assert.InRange(index, 0, arguments.Length - 2);

        return arguments[index + 1];
    }

    /// <summary>Checks positional VSTest and named MTP project selection without flattening arguments.</summary>
    private static void AssertNativeTestArguments(
        string[] arguments,
        string runner,
        string project
    )
    {
        Assert.Equal("test", arguments[0]);
        Assert.Contains("--no-build", arguments);
        Assert.Contains("--no-restore", arguments);
        if (runner == "VSTest")
        {
            Assert.Equal(project, arguments[1]);
            Assert.DoesNotContain("--project", arguments);
        }
        else
        {
            Assert.Equal("--project", arguments[1]);
            Assert.Equal(project, ArgumentAfter(arguments, "--project"));
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );

    /// <summary>Runs delivered recipes in a private checkout while replacing only external side effects.</summary>
    private sealed class StageRepository : IDisposable
    {
        /// <summary>Copies recipes and installs private stubs without touching shared build outputs.</summary>
        public StageRepository()
        {
            Root = Path.Combine(Path.GetTempPath(), $"doka-qualification-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);

            // WHY: Recipes resolve symlinks with pwd -P; exact path assertions need the same checkout identity.
            var canonicalRoot = Run(Root, "-c", null, "pwd -P");
            Assert.Equal(0, canonicalRoot.ExitCode);
            Root = canonicalRoot.StandardOutput.Trim();
            var python = Run(Root, "-c", null, "command -v python3");
            Assert.Equal(0, python.ExitCode);
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] =
                    Path.Combine(Root, "bin") + Path.PathSeparator + System.Environment.GetEnvironmentVariable("PATH"),
                ["DOKA_STUB_ROOT"] = Root,
                ["DOKA_REAL_PYTHON"] = python.StandardOutput.Trim(),
            };

            foreach (var script in new[]
                {
                    "qualify-packages.sh", "generate-package-sbom.sh", "qualify-engine.sh", "test-spec-target.sh",
                })
            {
                Write(
                    $"eng/testing/{script}",
                    File.ReadAllText(Path.Combine(FindRepositoryRoot(), "eng/testing", script)));
            }

            WriteEnginePolicy(hasEfMatrix: true);

            foreach (var id in s_packageIds)
            {
                Write($"artifacts/obj/{id}/project.assets.json", $"{{\"qualified\":\"{id}\"}}");
            }

            Write(
                "eng/quality/verify-release-package-locks.sh",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'locks\n' >> "${DOKA_STUB_ROOT}/events"
                exit "${DOKA_STUB_LOCK_EXIT:-0}"
                """,
                executable: true);

            Write(
                "eng/common/verify-dotnet.sh",
                """
                #!/usr/bin/env bash
                printf 'verify-dotnet\n' >> "${DOKA_STUB_ROOT}/events"
                """,
                executable: true);

            Write(
                "eng/testing/check-spec-contract.sh",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'spec-contract\n' >> "${DOKA_STUB_ROOT}/events"
                if [[ "${DOKA_STUB_FAIL:-}" == contract ]]; then exit 31; fi
                """);

            Write(
                "eng/testing/check-spec-discovery.sh",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'spec-discovery\n' >> "${DOKA_STUB_ROOT}/events"
                if [[ "${DOKA_STUB_FAIL:-}" == discovery ]]; then exit 31; fi
                """);

            Write(
                "eng/testing/check-spec-results.sh",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'reconcile|%s|%s\n' "$1" "$2" >> "${DOKA_STUB_ROOT}/events"
                if [[ "${DOKA_STUB_FAIL:-}" == reconciliation ]]; then exit 31; fi
                """);

            Write(
                "eng/testing/test-local-package-consumer.sh",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'call\n' >> "${DOKA_STUB_ROOT}/consumer-arguments"
                printf 'argument|%s\n' "$@" >> "${DOKA_STUB_ROOT}/consumer-arguments"
                printf 'consumer|%s|%s|%s\n' "$1" "$2" "$3" >> "${DOKA_STUB_ROOT}/events"
                if [[ "${DOKA_STUB_FAIL:-}" == consumer ]]; then exit 31; fi
                mkdir -p "$3"
                if [[ "${DOKA_STUB_MISSING:-}" != consumer ]]; then
                    printf '{"consumer":true}' > "$3/local-package-consumer.json"
                fi
                """);

            Write(
                "eng/testing/test-nuget-readback.sh",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'call\n' >> "${DOKA_STUB_ROOT}/readback-arguments"
                printf 'argument|%s\n' "$@" >> "${DOKA_STUB_ROOT}/readback-arguments"
                printf 'readback|%s|%s|%s|%s|%s|%s\n' "$@" >> "${DOKA_STUB_ROOT}/events"
                if [[ "${DOKA_STUB_FAIL:-}" == readback ]]; then exit 31; fi
                if [[ "${DOKA_STUB_MISSING:-}" != readback ]]; then
                    printf '{"runtime":true}' > "$5/local-package-runtime.json"
                fi
                """);

            Write("bin/git", "#!/usr/bin/env bash\nprintf '%040d\\n' 1\n", executable: true);
            Write(
                "bin/docker",
                """
                #!/usr/bin/env bash
                printf '{"services":{"mysql84":{"image":"mysql:8.4.13@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}}\n'
                """,
                executable: true);

            Write(
                "bin/python3",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                if [[ "${1:-}" == - ]]; then exec "${DOKA_REAL_PYTHON}" "$@"; fi
                if [[ "${1:-}" != -m ]]; then exit 42; fi
                module="$2"
                shift 2
                case "${module}" in
                    eng.release.nuget)
                        printf 'version|%s\n' "$*" >> "${DOKA_STUB_ROOT}/events"
                        ;;
                    eng.release.sbom)
                        printf 'materialize|%s\n' "$*" >> "${DOKA_STUB_ROOT}/events"
                        if [[ "${DOKA_STUB_FAIL:-}" == materialize ]]; then exit 31; fi
                        ;;
                    eng.common.deadline)
                        while [[ "$1" != -- ]]; do shift; done
                        shift
                        exec "$@"
                        ;;
                    *) exit 42 ;;
                esac
                """,
                executable: true);

            // WHY: Preserve each argument separately so a flattened log cannot hide broken quoting.
            Write(
                "bin/dotnet",
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'call\n' >> "${DOKA_STUB_ROOT}/dotnet-arguments"
                printf 'argument|%s\n' "$@" >> "${DOKA_STUB_ROOT}/dotnet-arguments"
                printf 'dotnet|%s\n' "$*" >> "${DOKA_STUB_ROOT}/events"
                command="$1"
                shift
                case "${command}" in
                    build|pack)
                        project="$1"
                        id="$(basename "${project}" .csproj)"
                        if [[ "${DOKA_STUB_FAIL:-}" == "${command}:${id}" ]]; then exit 31; fi
                        version=""
                        output=""
                        for argument in "$@"; do
                            case "${argument}" in
                                -p:PackageVersion=*) version="${argument#-p:PackageVersion=}" ;;
                            esac
                        done
                        if [[ -z "${version}" ]]; then exit 43; fi
                        if [[ "${command}" == build ]]; then
                            printf 'changed-by-build' > "${DOKA_STUB_ROOT}/artifacts/obj/${id}/project.assets.json"
                        else
                            while [[ "$1" != --output ]]; do shift; done
                            output="$2"
                            mkdir -p "${output}"
                            if [[ "${DOKA_STUB_MISSING:-}" != package || "${id}" != Doka.Caching.MySql ]]; then
                                printf 'package:%s' "${id}" > "${output}/${id}.${DOKA_STUB_PACK_VERSION:-${version}}.nupkg"
                            fi
                            if [[ -n "${DOKA_STUB_EXTRA_PACKAGE:-}" ]]; then
                                printf 'extra-package' > "${output}/${DOKA_STUB_EXTRA_PACKAGE}"
                            fi
                        fi
                        ;;
                    package)
                        if [[ "${DOKA_STUB_FAIL:-}" == graph ]]; then exit 31; fi
                        printf '{"projects":[{"path":"provider"}]}\n'
                        ;;
                    test)
                        printf 'test-runner|%s\n' "${DOTNET_TEST_RUNNER}" >> "${DOKA_STUB_ROOT}/events"
                        printf 'spec-env|%s|%s\n' "${DOKA_SPEC_TEST_TARGET}" \
                            "${DOKA_TEST_DATABASE_EVIDENCE_FILE}" >> "${DOKA_STUB_ROOT}/events"
                        if [[ "$*" == *"FullyQualifiedName~SpecDispositionContractTests"* ]]; then
                            if [[ "${DOKA_STUB_FAIL:-}" == disposition ]]; then exit 31; fi
                        elif [[ "${DOKA_STUB_FAIL:-}" == specification ]]; then
                            exit 31
                        fi
                        ;;
                    tool)
                        if [[ "$1" == restore ]]; then
                            if [[ "${DOKA_STUB_FAIL:-}" == tool-restore ]]; then exit 31; fi
                            exit 0
                        fi
                        if [[ "$1" != run || "$2" != sbom-tool ]]; then exit 42; fi
                        if [[ "${DOKA_STUB_FAIL:-}" == sbom ]]; then exit 31; fi
                        while [[ "$1" != -m ]]; do shift; done
                        if [[ "${DOKA_STUB_MISSING:-}" != sbom ]]; then
                            mkdir -p "$2"
                            printf '{"sbom":true}' > "$2/manifest.spdx.json"
                        fi
                        ;;
                    *) exit 42 ;;
                esac
                """,
                executable: true);
        }

        /// <summary>Gets the isolated checkout root.</summary>
        public string Root { get; }

        /// <summary>Gets the package and SBOM qualification output.</summary>
        public string Output => Path.Combine(Root, "artifacts/qualification");

        /// <summary>Gets process settings controlling external stub outcomes.</summary>
        public Dictionary<string, string> Environment { get; }

        /// <summary>Gets side effects recorded by external stubs in execution order.</summary>
        public string[] Events => File.Exists(Path.Combine(Root, "events"))
            ? File.ReadAllLines(Path.Combine(Root, "events"))
            : [];

        /// <summary>Gets dotnet invocations with the original argument boundaries preserved.</summary>
        public string[][] DotnetCalls => ReadArgumentCalls("dotnet");

        /// <summary>Gets the exact arguments passed to the isolated package consumer.</summary>
        public string[] ConsumerArguments => Assert.Single(ReadArgumentCalls("consumer"));

        /// <summary>Gets the exact arguments passed to package runtime readback.</summary>
        public string[] ReadbackArguments => Assert.Single(ReadArgumentCalls("readback"));

        /// <summary>Removes only this test's private checkout.</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);

        /// <summary>Runs one copied delivered recipe with this fixture's process environment.</summary>
        public ProcessResult RunStage(
            string script,
            params string[] arguments
        ) => Run(Root, Path.Combine(Root, "eng/testing", script), Environment, arguments);

        /// <summary>Creates a complete three-package input set independent of the package recipe.</summary>
        public void PrepareFrozenInputs()
        {
            for (var index = 0; index < s_packageIds.Length; index++)
            {
                var role = new[] { "runtime", "spatial", "cache" }[index];
                Write(
                    $"artifacts/qualification/sbom-components/{role}/project.assets.json",
                    $"{{\"frozen\":\"{role}\"}}");

                Write($"artifacts/qualification/packages/{s_packageIds[index]}.10.0.8-test.1.nupkg", "package");
            }
        }

        /// <summary>Creates stable or preview branch policy and its required producer files.</summary>
        /// <param name="hasEfMatrix">Whether policy and scripts include the stable EF patch matrix.</param>
        public void WriteEnginePolicy(
            bool hasEfMatrix
        )
        {
            var gates = new List<object>
            {
                new
                {
                    id = "repository-qualification",
                    kind = "protected-check",
                },
            };

            foreach (var (id, script) in new[]
                {
                    ("migration-deployment", "test-migration-deployment.sh"),
                    ("runtime-posture", "test-runtime-posture.sh"),
                    ("efcore-patch-matrix", "test-efcore-matrix.sh"),
                    ("mysqlconnector-patch-matrix", "test-mysqlconnector-matrix.sh"),
                })
            {
                var implementation = $"eng/testing/{script}";
                if (!hasEfMatrix
                    && id == "efcore-patch-matrix")
                {
                    File.Delete(Path.Combine(Root, implementation));
                    continue;
                }

                gates.Add(
                    new
                    {
                        id,
                        kind = "candidate-produced",
                        implementation,
                    });

                Write(implementation, "#!/usr/bin/env bash\nexit 99\n");
            }

            Write(
                "eng/release/evidence-policy.json",
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        policyVersion = "test",
                        gates,
                    }));
        }

        /// <summary>Writes an isolated fixture file, optionally marking it executable.</summary>
        public void Write(
            string relative,
            string content,
            bool executable = false
        )
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            if (executable && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        /// <summary>Reads per-argument records without splitting arguments that contain spaces.</summary>
        private string[][] ReadArgumentCalls(
            string tool
        )
        {
            var path = Path.Combine(Root, $"{tool}-arguments");
            if (!File.Exists(path))
            {
                return [];
            }

            var calls = new List<List<string>>();
            foreach (var line in File.ReadLines(path))
            {
                if (line == "call")
                {
                    calls.Add([]);
                }
                else
                {
                    Assert.StartsWith("argument|", line, StringComparison.Ordinal);
                    Assert.NotEmpty(calls);
                    calls[calls.Count - 1]
                        .Add(line["argument|".Length..]);
                }
            }

            return calls
                .Select(static call => call.ToArray())
                .ToArray();
        }
    }
}
