using System.Xml.Linq;
using Doka.EntityFrameworkCore.MySql.SpecificationContract;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public partial class SpecificationContractTests
{
    /// <summary>Pairs literal discovery identities with independently specified native reporter payloads.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> NativeTrxDisplayCases()
    {
        yield return new(".Quote(\"alpha\")", ".Quote(\\\"alpha\\\")");
        yield return new(@".Path(C:\temp\leaf)", @".Path(C:\\temp\\leaf)");
        yield return new(@".Literal(\u2764)", @".Literal(\\u2764)");
        yield return new(@".Literal(\ud83d\udc4d)", @".Literal(\\ud83d\\udc4d)");
        yield return new(".Unicode(\U0001F44D)", ".Unicode(\U0001F44D)");
        yield return new(".Invalid(\uFFFE)", @".Invalid(\xfffe)");
        yield return new(".Control(\0\a\b\f\n\r\t\v\u0001)", @".Control(\0\a\b\f\n\r\t\v\x01)");
    }

    /// <summary>Reverses only the identified reporter's transport without altering literal test data.</summary>
    /// <param name="display">The exact committed discovery suffix.</param>
    /// <param name="transport">The corresponding native TRX payload suffix.</param>
    [Theory]
    [MemberData(nameof(NativeTrxDisplayCases))]
    public void Native_trx_preserves_exact_display_identity(
        string display,
        string transport
    )
    {
        // Arrange
        var expected = Fixture + display;
        var trx = NativeTrx((Fixture + transport, "Passed"));

        // Act
        var report = ValidateNativeTrx(trx, expected);

        // Assert
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
    }

    /// <summary>Literal escapes and the characters they resemble remain two independently required results.</summary>
    [Fact]
    public void Native_trx_does_not_merge_literal_and_decoded_looking_identities()
    {
        // Arrange
        var literal = Fixture + @".Unicode(\ud83d\udc4d)";
        var actual = Fixture + ".Unicode(\U0001F44D)";
        var trx = NativeTrx(
            (Fixture + @".Unicode(\\ud83d\\udc4d)", "Passed"),
            (actual, "Passed"));

        // Act
        var report = ValidateNativeTrx(trx, literal, actual);

        // Assert
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
    }

    /// <summary>Native payloads cannot bypass encoding by matching another ID's literal text.</summary>
    [Fact]
    public void Native_trx_rejects_a_raw_identity_alias()
    {
        // Arrange
        var literal = Fixture + ".Quote(\\\"alpha\\\")";
        var trx = NativeTrx((literal, "Passed"));

        // Act
        var exception = Record.Exception(() => ValidateNativeTrx(trx, literal));

        // Assert
        Assert.IsType<InvalidDataException>(exception);
    }

    /// <summary>Metadata disagreements cannot change the format or ownership of a native result.</summary>
    /// <param name="mutation">The structural identity defect.</param>
    [Theory]
    [InlineData("missing-definition")]
    [InlineData("different-definition-name")]
    [InlineData("duplicate-definition")]
    [InlineData("different-adapter-run")]
    public void Native_trx_rejects_invalid_result_definition_linkage(
        string mutation
    )
    {
        // Arrange
        var trx = XDocument.Parse(NativeTrx((PassedTestId, "Passed")));
        var result = trx
            .Descendants("UnitTestResult")
            .Single();

        var definition = trx
            .Descendants("UnitTest")
            .Single();

        switch (mutation)
        {
            case "missing-definition":
                result.SetAttributeValue("testId", "missing");
                break;
            case "different-definition-name":
                definition.SetAttributeValue("name", MissingTestId);
                break;
            case "duplicate-definition":
                definition.Parent!.Add(new XElement(definition));
                break;
            case "different-adapter-run":
                definition.Element("TestMethod")!.SetAttributeValue(
                    "adapterTypeName",
                    "executor://other-run/xunit.v3/4.0.3");

                break;
        }

        // Act
        var exception = Record.Exception(() => ValidateNativeTrx(trx.ToString(), PassedTestId));

        // Assert
        Assert.IsType<InvalidDataException>(exception);
    }

    /// <summary>One reporter result cannot satisfy the same expected ID twice.</summary>
    [Fact]
    public void Native_trx_rejects_duplicate_outcomes_after_exact_matching()
    {
        // Arrange
        var trx = NativeTrx((PassedTestId, "Passed"), (PassedTestId, "Passed"));

        // Act
        var report = ValidateNativeTrx(trx, PassedTestId);

        // Assert
        Assert.Contains(report.Errors, error => error.Contains("2 outcomes", StringComparison.Ordinal));
    }

    /// <summary>The transport rule is not guessed from escape characters in legacy VSTest names.</summary>
    [Fact]
    public void Legacy_trx_does_not_adopt_native_display_name_encoding()
    {
        // Arrange
        var expected = Fixture + ".Quote(\"alpha\")";
        var trx = NativeTrx((Fixture + ".Quote(\\\"alpha\\\")", "Passed"))
            .Replace("/xunit.v3/4.0.3", "/legacy/1.0", StringComparison.Ordinal);

        // Act
        var report = ValidateNativeTrx(trx, expected);

        // Assert
        Assert.False(report.IsValid);
    }

    /// <summary>Uses the existing exact discovery and disposition validator over a real XML file.</summary>
    private static TrxContractReport ValidateNativeTrx(
        string trx,
        params string[] expected
    )
    {
        var directory = Directory.CreateTempSubdirectory("doka-spec-native-trx-");
        try
        {
            var trxPath = Path.Combine(directory.FullName, "native.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            File.WriteAllText(trxPath, trx);
            File.WriteAllText(dispositionPath, EmptyDispositions());

            return TrxContract.Validate(Discovery(expected), "mysql84", [trxPath], dispositionPath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Models the linked native xUnit reporter format without using the production encoder.</summary>
    private static string NativeTrx(
        params (string TestId, string Outcome)[] outcomes
    )
    {
        const string runId = "46526298-02e3-4a16-bc65-5bd1379b28cd";
        var results = new XElement("Results");
        var definitions = new XElement("TestDefinitions");
        foreach (var (testId, outcome) in outcomes)
        {
            var id = Guid
                .NewGuid()
                .ToString();

            results.Add(new XElement("UnitTestResult",
                new XAttribute("testId", id),
                new XAttribute("testName", testId),
                new XAttribute("outcome", outcome)));

            definitions.Add(new XElement("UnitTest",
                new XAttribute("id", id),
                new XAttribute("name", testId),
                new XElement("TestMethod",
                    new XAttribute("adapterTypeName", $"executor://{runId}/xunit.v3/4.0.3"))));
        }

        return new XElement("TestRun", new XAttribute("id", runId), results, definitions).ToString();
    }
}
