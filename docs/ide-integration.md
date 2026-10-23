# IDE Integration

This guide records provider-specific editor behavior that does not affect
runtime translation or the public provider contract.

## JetBrains Rider and ReSharper

Rider and ReSharper can flag provider-specific `EF.Functions` extensions such
as `Regexp`, `Match`, or `DistanceSphere` with the following inspection:

> Function is not convertible to SQL and must not be called in the database
> context.

For Doka methods this is a static-analysis false positive. The inspection does
not recognize the provider's `IMethodCallTranslatorPlugin`, while runtime query
translation emits the engine-specific SQL.

Use the narrowest suppression appropriate for the consumer project.

### One call site

```csharp
// ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
var results = context.Articles
    .Where(article =>
        EF.Functions.MatchInBooleanMode(article.Body, "+mysql -aurora"))
    .ToList();
```

### One file

```csharp
// ReSharper disable EntityFramework.UnsupportedServerSideFunctionCall
```

### One project

Place a `<ProjectName>.csproj.DotSettings` file beside each consumer project
that contains provider-translated LINQ queries:

```xml
<wpf:ResourceDictionary
    xml:space="preserve"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:s="clr-namespace:System;assembly=mscorlib"
    xmlns:ss="urn:shemas-jetbrains-com:settings-storage-xaml"
    xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <s:String
        x:Key="/Default/CodeInspection/Highlighting/InspectionSeverities/=EntityFramework_002EUnsupportedServerSideFunctionCall/@EntryIndexedValue">DO_NOT_SHOW</s:String>
</wpf:ResourceDictionary>
```

The project-level form keeps the inspection active for unrelated code. This
repository carries that setting only in projects that exercise its translated
extensions.

## Repository Verification

The functional, integration, runtime-smoke, and benchmark projects each carry a
project-local `.csproj.DotSettings` entry for this inspection. The scope is
intentional: unrelated consumer projects retain the normal inspection, while
projects that execute Doka translation markers do not report a false positive.
The provider's functional and integration tests remain the executable proof
that each documented marker translates or fails through EF Core.

## Local specification-test target

The functional-test project selects MariaDB 12.3 (`mariadb123`) through
`LocalMariaDb123.runsettings` when `DOKA_SPEC_TEST_TARGET` is not set. This is
the version-controlled local execution default, including for Rider runs that
use the project's runsettings. The fixture starts its own database container;
no separately installed MariaDB server is required.

To select another supported profile, set `DOKA_SPEC_TEST_TARGET`, for example
`mysql84`, before the runner evaluates the project. An explicit target bypasses
the local settings file. A manually selected runsettings file can also change
the runner's settings. The six-engine CI and release matrix is unchanged; one
local profile is not complete matrix evidence.

## Integration test parallelism in Rider

The integration test assembly runs independent classes as separate xUnit
collections, with at most four active collections. One test-run-owned server
session supplies the selected engines; each class receives its own database on
each engine it uses. Test methods in one class remain serial. The runner drops
the class databases and then stops its containers, including for a filtered
Rider run. Classes that inspect process-wide diagnostics or clear all driver
connection pools, as well as the separately owned TLS fixture, stay nonparallel.

The database owner is the test class's source file. Keep one live test class
per file; a shared test helper that obtains a connection must forward its
caller's file path, as the cache test store does.

Running one integration test in Rider still starts the selected database
targets. Set `DOKA_INTEGRATION_TARGETS` in the Rider test-runner environment to
restrict a local run to a particular target, for example `mysql84`. This does
not change the six-engine release matrix. A coverage run may disable parallel
execution in Rider independently of the xUnit configuration.

## Primary Sources

Retrieved 2026-08-21 (inspection settings) and 2026-10-14 (test parallelism):

- [JetBrains Rider 2026.2 inspection configuration](https://www.jetbrains.com/help/rider/Code_Analysis__Configuring_Warnings.html)
- [JetBrains Rider 2026.2 inspection settings](https://www.jetbrains.com/help/rider/Settings_Inspection_Settings.html)
- [JetBrains Rider 2026.2 ReSharper settings layers](https://www.jetbrains.com/help/rider/Settings_Tools_ReSharper.html)
- [xUnit.net parallel test collections](https://xunit.net/docs/running-tests-in-parallel)
- [JetBrains Rider unit testing](https://www.jetbrains.com/help/rider/Unit_Testing__Index.html)
- [JetBrains Rider coverage-run parallelism](https://www.jetbrains.com/help/rider/Cover_Testing_Platform_Tests.html)
