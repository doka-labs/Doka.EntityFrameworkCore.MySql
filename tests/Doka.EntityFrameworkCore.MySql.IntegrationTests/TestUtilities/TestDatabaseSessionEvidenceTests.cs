using System.IO;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies failed lifecycle evidence publication removes temporary files.</summary>
[IntegrationDatabaseTest]
[Collection(ProcessWideIntegrationTestGroup.Name)]
public sealed class TestDatabaseSessionEvidenceTests
{
    /// <summary>Gets the selected supported engines for the startup contract.</summary>
    public static IEnumerable<object[]> SupportedTargets => IntegrationTestEnvironment
        .GetSupportedTargets()
        .Where(IntegrationTestEnvironment.IsTargetSelected)
        .Select(target => new object[] { target.ToString() });

    /// <summary>Verifies an atomic rename failure leaves no partial ready evidence.</summary>
    /// <param name="targetName">The live engine used to verify the external endpoint.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SupportedTargets))]
    public async Task Failed_evidence_publication_removes_the_temporary_file(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        const string evidenceVariable = "DOKA_TEST_DATABASE_EVIDENCE_FILE";
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"doka-evidence-{Guid.NewGuid():N}");
        var evidencePath = Path.Combine(temporaryRoot, "state.json");
        var endpointVariable = $"DOKA_EVIDENCE_ENDPOINT_{Guid.NewGuid():N}";
        var previousEvidenceFile = Environment.GetEnvironmentVariable(evidenceVariable);
        var previousEndpoint = Environment.GetEnvironmentVariable(endpointVariable);
        var request = IntegrationTestEnvironment.CreateRequest(target) with
        {
            Image = null,
            ConnectionStringEnvironmentVariable = endpointVariable,
        };

        // WHY: A directory at the destination deterministically rejects the final rename after serialization.
        Directory.CreateDirectory(evidencePath);
        Environment.SetEnvironmentVariable(evidenceVariable, evidencePath);
        Environment.SetEnvironmentVariable(
            endpointVariable,
            IntegrationTestEnvironment.GetBaseConnectionString(target));

        try
        {
            // Act
            var exception =
                await Record.ExceptionAsync(() => TestDatabaseSession.StartAsync([request], CancellationToken.None));

            // Assert
            Assert.IsAssignableFrom<IOException>(exception);
            Assert.True(Directory.Exists(evidencePath));
            Assert.Empty(Directory.EnumerateFiles(temporaryRoot, "*.tmp"));
        }
        finally
        {
            // WHY: The real qualification evidence path must be restored before fixture disposal records cleanup.
            Environment.SetEnvironmentVariable(evidenceVariable, previousEvidenceFile);
            Environment.SetEnvironmentVariable(endpointVariable, previousEndpoint);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
