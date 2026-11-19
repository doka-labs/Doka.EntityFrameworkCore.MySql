namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexedSelectTests
{
    private static readonly bool[] s_contractDialects = [false, true];
    private static readonly bool[] s_contractFlags = [false, true];
    private static readonly string[] s_groupAggregates = ["Count", "LongCount", "Sum", "Average", "Min", "Max"];
    private static readonly string[] s_groupKeyModes = ["Composite", "Converted"];

    /// <summary>Reports each existing aggregate both alone and beside actual indexed group members.</summary>
    public static TheoryData<bool, string, bool> AggregateCases
    {
        get
        {
            var cases = new TheoryData<bool, string, bool>();

            foreach (var mariaDb in s_contractDialects)
            {
                foreach (var aggregate in s_groupAggregates)
                {
                    foreach (var indexed in s_contractFlags)
                    {
                        cases.Add(mariaDb, aggregate, indexed);
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>Reports each key representation under both native SQL dialects.</summary>
    public static TheoryData<bool, string> GroupKeyCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in s_contractDialects)
            {
                foreach (var mode in s_groupKeyModes)
                {
                    cases.Add(mariaDb, mode);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports independent flag variants under each dialect without combining their acts inside one test.</summary>
    public static TheoryData<bool, bool> DialectFlags
    {
        get
        {
            var cases = new TheoryData<bool, bool>();

            foreach (var mariaDb in s_contractDialects)
            {
                foreach (var flag in s_contractFlags)
                {
                    cases.Add(mariaDb, flag);
                }
            }

            return cases;
        }
    }

    /// <summary>Exercises exact operators on the sequence of complete groups in each SQL dialect.</summary>
    public static TheoryData<bool, string> GroupSequenceCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in s_contractDialects)
            {
                foreach (var shape in new[] { "ordered", "filtered", "paged" })
                {
                    cases.Add(mariaDb, shape);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports nullable scalar keys and one-field composite wrappers independently for each dialect.</summary>
    public static TheoryData<bool, string> NullableGroupKeyCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in s_contractDialects)
            {
                foreach (var mode in new[] { "text", "number", "wrapped-text", "wrapped-number" })
                {
                    cases.Add(mariaDb, mode);
                }
            }

            return cases;
        }
    }

    /// <summary>NULL-key membership is preserved by grouping correlation without adding the null-class bit to its window partition.</summary>
    [Theory]
    [MemberData(nameof(NullableGroupKeyCases))]
    public void Nullable_indexed_group_key_uses_grouping_equality_with_the_original_partition(
        bool mariaDb,
        string mode
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        IQueryable query = mode switch
        {
            "text" => context
                .Rows
                .GroupBy(row => row.Name)
                .Select(group => new
                {
                    group.Key,
                    Items = group
                        .OrderBy(row => row.Id)
                        .Select((row, index) => new
                        {
                            row.Id,
                            Index = index,
                        })
                        .ToList(),
                }),
            "number" => context
                .Rows
                .GroupBy(row => row.AccountId)
                .Select(group => new
                {
                    group.Key,
                    Items = group
                        .OrderBy(row => row.Id)
                        .Select((row, index) => new
                        {
                            row.Id,
                            Index = index,
                        })
                        .ToList(),
                }),
            "wrapped-text" => context
                .Rows
                .GroupBy(row => new { row.Name })
                .Select(group => new
                {
                    group.Key.Name,
                    Items = group
                        .OrderBy(row => row.Id)
                        .Select((row, index) => new
                        {
                            row.Id,
                            Index = index,
                        })
                        .ToList(),
                }),
            "wrapped-number" => context
                .Rows
                .GroupBy(row => new { row.AccountId })
                .Select(group => new
                {
                    group.Key.AccountId,
                    Items = group
                        .OrderBy(row => row.Id)
                        .Select((row, index) => new
                        {
                            row.Id,
                            Index = index,
                        })
                        .ToList(),
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown nullable key representation."),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LATERAL", sql, StringComparison.Ordinal);
        var partitionStart = sql.IndexOf("PARTITION BY ", StringComparison.Ordinal);
        Assert.True(partitionStart >= 0, sql);
        var partitionEnd = sql.IndexOf(" ORDER BY", partitionStart, StringComparison.Ordinal);
        Assert.True(partitionEnd > partitionStart, sql);
        Assert.DoesNotContain("IS NULL", sql[partitionStart..partitionEnd], StringComparison.Ordinal);
    }

    /// <summary>The indexed-group correction does not change ordinary scalar LINQ join NULL exclusion.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Ordinary_nullable_join_does_not_acquire_grouping_null_equality(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Join(context.Rows,
            left => left.Name,
            right => right.Name,
            (left, right) => new
            {
                Left = left.Id,
                Right = right.Id,
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER()", sql, StringComparison.Ordinal);
    }

    /// <summary>An ordinary grouped collection retains its existing translation when no indexed selector is requested.</summary>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void Ordinary_nullable_group_does_not_acquire_an_indexed_internal_key(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .GroupBy(row => row.Name)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER()", sql, StringComparison.Ordinal);
    }

    /// <summary>Whole-group operators cannot hide the partition scope and accidentally require a correlated lateral table.</summary>
    [Theory]
    [MemberData(nameof(GroupSequenceCases))]
    public void Group_sequence_operators_preserve_partitioned_index_lowering(
        bool mariaDb,
        string mode
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var groups = context.Rows.GroupBy(row => row.AccountId);
        var scoped = mode switch
        {
            "ordered" => groups
                .OrderBy(group => group.Key)
                .ThenByDescending(group => group.Count()),
            "filtered" => groups
                .Where(group => group.Count() > 1)
                .OrderByDescending(group => group.Key),
            "paged" => groups
                .Where(group => group.Count() > 1)
                .OrderByDescending(group => group.Key)
                .Skip(1)
                .Take(2),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown group sequence contract."),
        };

        var query = scoped.Select(group => new
        {
            group.Key,
            Count = group.Count(),
            Items = group
                .OrderBy(row => row.Id)
                .Select((row, index) => new
                {
                    row.Id,
                    Index = index,
                })
                .ToList(),
        });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LATERAL", sql, StringComparison.Ordinal);

        if (mode == "paged")
        {
            Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
            Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        }
    }

    /// <summary>The original group aggregate remains composable when its entity rows acquire a separate member index.</summary>
    [Theory]
    [MemberData(nameof(AggregateCases))]
    public void Each_group_aggregate_preserves_the_original_row_contract(
        bool mariaDb,
        string aggregate,
        bool indexed
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = GroupAggregateQuery(context, aggregate, indexed);
        var native = aggregate switch
        {
            "Count" or "LongCount" => "COUNT(*)",
            "Average" => "AVG(",
            _ => aggregate.ToUpperInvariant() + "(",
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(native, sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Equal(indexed, sql.Contains("ROW_NUMBER()", StringComparison.Ordinal));
    }

    /// <summary>A constant GroupBy keeps its existing aggregate/element shape, including an explicitly empty source.</summary>
    [Theory]
    [MemberData(nameof(DialectFlags))]
    public void Constant_group_keeps_the_existing_linq_first_path(
        bool mariaDb,
        bool empty
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = empty
            ? context.Rows.Where(row => false)
            : context.Rows;

        var query = source
            .GroupBy(row => 1)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Values = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Amount)
                    .ToList(),
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER()", sql, StringComparison.Ordinal);
    }

    /// <summary>The member ordinal uses the same converted or composite partition as the actual grouping key.</summary>
    [Theory]
    [MemberData(nameof(GroupKeyCases))]
    public void Group_member_index_preserves_key_representation(
        bool mariaDb,
        string keyMode
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        IQueryable query = keyMode switch
        {
            "Composite" => context
                .Rows
                .GroupBy(row => new
                {
                    row.AccountId,
                    row.Stamp,
                })
                .Select(group => new
                {
                    group.Key,
                    Items = group
                        .OrderBy(row => row.Id)
                        .Select((row, index) => new
                        {
                            row.Id,
                            Index = index,
                        })
                        .ToList(),
                }),
            "Converted" => context
                .Rows
                .GroupBy(row => row.Bucket)
                .Select(group => new
                {
                    group.Key,
                    Items = group
                        .OrderBy(row => row.Id)
                        .Select((row, index) => new
                        {
                            row.Id,
                            Index = index,
                        })
                        .ToList(),
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(keyMode), keyMode, "Unknown key representation."),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        var start = sql.IndexOf("PARTITION BY ", StringComparison.Ordinal);
        Assert.True(start >= 0, sql);
        var end = sql.IndexOf(" ORDER BY", start, StringComparison.Ordinal);
        Assert.True(end > start, sql);
        var partition = sql[start..end];
        Assert.Contains(keyMode == "Composite"
                ? "AccountId"
                : "Bucket",
            partition,
            StringComparison.Ordinal);

        if (keyMode == "Composite")
        {
            Assert.Contains("Stamp", partition, StringComparison.Ordinal);
        }
    }

    /// <summary>Root query filters retain their normal scope, and an explicit opt-out remains effective.</summary>
    [Theory]
    [MemberData(nameof(DialectFlags))]
    public void Global_index_preserves_root_filter_and_explicit_opt_out(
        bool mariaDb,
        bool ignoreFilters
    )
    {
        // Arrange
        using var context = CreateFilteredContext(mariaDb);
        var source = ignoreFilters
            ? context.Rows.IgnoreQueryFilters()
            : context.Rows;

        var query = source
            .OrderBy(row => row.Id)
            .Select((row, index) => new
            {
                row.Id,
                Index = index,
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ROW_NUMBER()", sql, StringComparison.Ordinal);
        Assert.Equal(!ignoreFilters, sql.Contains("WHERE", StringComparison.Ordinal));
    }

    /// <summary>Indexed projection retains the actual entity shaper and every mapped property under both tracking modes.</summary>
    [Theory]
    [MemberData(nameof(DialectFlags))]
    public void Indexed_entity_projection_retains_the_mapped_entity_shape(
        bool mariaDb,
        bool noTracking
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var source = noTracking
            ? context.Rows.AsNoTracking()
            : context.Rows;

        var query = source
            .OrderBy(row => row.Id)
            .Select((row, index) => new
            {
                Entity = row,
                Index = index,
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ROW_NUMBER()", sql, StringComparison.Ordinal);
        Assert.Contains("`Id`", sql, StringComparison.Ordinal);
        Assert.Contains("`AccountId`", sql, StringComparison.Ordinal);
        Assert.Contains("`Stamp`", sql, StringComparison.Ordinal);
        Assert.Contains("`Amount`", sql, StringComparison.Ordinal);
        Assert.Contains("`Bucket`", sql, StringComparison.Ordinal);
    }
}
