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

## Primary Sources

Retrieved 2026-08-21:

- [JetBrains Rider 2026.2 inspection configuration](https://www.jetbrains.com/help/rider/Code_Analysis__Configuring_Warnings.html)
- [JetBrains Rider 2026.2 inspection settings](https://www.jetbrains.com/help/rider/Settings_Inspection_Settings.html)
- [JetBrains Rider 2026.2 ReSharper settings layers](https://www.jetbrains.com/help/rider/Settings_Tools_ReSharper.html)
