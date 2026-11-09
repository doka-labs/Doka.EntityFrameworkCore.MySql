namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the MariaDB pre-11.4 application-time metadata fallback.
/// </summary>
public sealed class MariaDbTemporalDefinitionParserTests
{
    /// <summary>
    /// Covers the canonical bitemporal definition returned by MariaDB 10.11.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_reads_period_and_primary_constraint()
    {
        // Arrange
        const string input = """
            CREATE TABLE `IntProbe` (
              `Id` int(11) NOT NULL,
              `BusinessValidFrom` datetime(6) NOT NULL,
              `BusinessValidTo` datetime(6) NOT NULL,
              `SystemValidFrom` timestamp(6) GENERATED ALWAYS AS ROW START,
              `SystemValidTo` timestamp(6) GENERATED ALWAYS AS ROW END,
              PERIOD FOR `BusinessValidity` (`BusinessValidFrom`, `BusinessValidTo`),
              PRIMARY KEY (`Id`,`SystemValidTo`,`BusinessValidity` WITHOUT OVERLAPS),
              PERIOD FOR SYSTEM_TIME (`SystemValidFrom`, `SystemValidTo`)
            ) ENGINE=InnoDB WITH SYSTEM VERSIONING
            """;

        // Act
        var definition = MariaDbTemporalDefinitionParser.ParseApplicationTime(
            input);

        // Assert
        Assert.NotNull(definition);
        Assert.Equal("BusinessValidity", definition.PeriodName);
        Assert.Equal("BusinessValidFrom", definition.StartColumnName);
        Assert.Equal("BusinessValidTo", definition.EndColumnName);
        Assert.Equal(["PRIMARY"], definition.WithoutOverlapsConstraints);
    }

    /// <summary>
    /// Proves that escaped identifiers survive the server-rendered grammar.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_unescapes_quoted_identifiers()
    {
        // Arrange
        const string input = """
            CREATE TABLE `quoted` (
              `start``value` datetime(6) NOT NULL,
              `end``value` datetime(6) NOT NULL,
              PERIOD FOR `business``period` (`start``value`, `end``value`),
              UNIQUE KEY `UX``Period` (`business``period` WITHOUT OVERLAPS)
            ) ENGINE=InnoDB
            """;

        // Act
        var definition = MariaDbTemporalDefinitionParser.ParseApplicationTime(
            input);

        // Assert
        Assert.NotNull(definition);
        Assert.Equal("business`period", definition.PeriodName);
        Assert.Equal("start`value", definition.StartColumnName);
        Assert.Equal("end`value", definition.EndColumnName);
        Assert.Equal(["UX`Period"], definition.WithoutOverlapsConstraints);
    }

    /// <summary>
    /// Ensures defaults and comments cannot be interpreted as table clauses.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_ignores_literals_and_system_time()
    {
        // Arrange
        const string input = """
            CREATE TABLE `system_only` (
              `Payload` varchar(128) DEFAULT 'PERIOD FOR fake (a, b)',
              `RowStart` timestamp(6) GENERATED ALWAYS AS ROW START,
              `RowEnd` timestamp(6) GENERATED ALWAYS AS ROW END,
              PERIOD FOR SYSTEM_TIME (`RowStart`, `RowEnd`)
            ) COMMENT='WITHOUT OVERLAPS' WITH SYSTEM VERSIONING
            """;

        // Act
        var definition = MariaDbTemporalDefinitionParser.ParseApplicationTime(
            input);

        // Assert
        Assert.Null(definition);
    }

    /// <summary>
    /// Rejects a key that points at a different period instead of guessing.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_rejects_unknown_without_overlaps_period()
    {
        // Arrange
        Action act = () =>
            MariaDbTemporalDefinitionParser.ParseApplicationTime(
                """
                CREATE TABLE `invalid` (
                  `ValidFrom` datetime(6) NOT NULL,
                  `ValidTo` datetime(6) NOT NULL,
                  PERIOD FOR `BusinessValidity` (`ValidFrom`, `ValidTo`),
                  PRIMARY KEY (`OtherPeriod` WITHOUT OVERLAPS)
                ) ENGINE=InnoDB
                """);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("references unknown period", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Preserves commas inside types and index prefixes while splitting clauses.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_handles_nested_parentheses()
    {
        // Arrange
        const string input = """
            CREATE TABLE `nested` (
              `Code` varchar(32) NOT NULL,
              `Amount` decimal(10,2) NOT NULL,
              `ValidFrom` datetime(6) NOT NULL,
              `ValidTo` datetime(6) NOT NULL,
              PERIOD FOR `BusinessValidity` (`ValidFrom`, `ValidTo`),
              UNIQUE KEY `UX_Nested` (`Code`(10),`BusinessValidity` WITHOUT OVERLAPS)
            ) ENGINE=InnoDB
            """;

        // Act
        var definition = MariaDbTemporalDefinitionParser.ParseApplicationTime(
            input);

        // Assert
        Assert.NotNull(definition);
        Assert.Equal(["UX_Nested"], definition.WithoutOverlapsConstraints);
    }

    /// <summary>
    /// Rejects ambiguous metadata instead of choosing one application period.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_rejects_multiple_application_periods()
    {
        // Arrange
        Action act = () =>
            MariaDbTemporalDefinitionParser.ParseApplicationTime(
                """
                CREATE TABLE `multiple_periods` (
                  `FirstStart` datetime(6) NOT NULL,
                  `FirstEnd` datetime(6) NOT NULL,
                  `SecondStart` datetime(6) NOT NULL,
                  `SecondEnd` datetime(6) NOT NULL,
                  PERIOD FOR `FirstValidity` (`FirstStart`, `FirstEnd`),
                  PERIOD FOR `SecondValidity` (`SecondStart`, `SecondEnd`)
                ) ENGINE=InnoDB
                """);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("more than one application-time period", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects overlap metadata when the table has no application-time period.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_rejects_without_overlaps_without_period()
    {
        // Arrange
        Action act = () =>
            MariaDbTemporalDefinitionParser.ParseApplicationTime(
                """
                CREATE TABLE `missing_period` (
                  `Validity` datetime(6) NOT NULL,
                  PRIMARY KEY (`Validity` WITHOUT OVERLAPS)
                ) ENGINE=InnoDB
                """);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("without an application-time period", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures quoted identifiers cannot be promoted to grammar keywords.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_does_not_treat_quoted_tokens_as_keywords()
    {
        // Arrange
        const string input = """
            CREATE TABLE `quoted_keywords` (
              `PERIOD` `FOR` (`start`, `end`),
              `WITHOUT` `OVERLAPS`
            ) ENGINE=InnoDB
            """;

        // Act
        var definition = MariaDbTemporalDefinitionParser.ParseApplicationTime(
            input);

        // Assert
        Assert.Null(definition);
    }

    /// <summary>
    /// Fails closed when server output contains an unterminated string literal.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_rejects_unterminated_literal()
    {
        // Arrange
        Action act = () =>
            MariaDbTemporalDefinitionParser.ParseApplicationTime(
                "CREATE TABLE `invalid` (`Value` varchar(32) DEFAULT 'unterminated);");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("unterminated quoted literal", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fails closed when server output contains an unterminated quoted identifier.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_rejects_unterminated_identifier()
    {
        // Arrange
        Action act = () =>
            MariaDbTemporalDefinitionParser.ParseApplicationTime("CREATE TABLE `unterminated (id int);");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("unterminated quoted identifier", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects input that has no table-definition body.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_rejects_missing_table_definition()
    {
        // Arrange
        Action act = () =>
            MariaDbTemporalDefinitionParser.ParseApplicationTime("CREATE TABLE `invalid`;");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("does not contain a table definition", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects a table body whose closing parenthesis is absent.
    /// </summary>
    [Fact]
    public void ParseApplicationTime_rejects_unterminated_table_definition()
    {
        // Arrange
        Action act = () =>
            MariaDbTemporalDefinitionParser.ParseApplicationTime("CREATE TABLE `invalid` (`Id` int(11)");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("unterminated table definition", exception.Message, StringComparison.Ordinal);
    }
}
