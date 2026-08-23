using System.Globalization;

namespace Querio.Sql.IntegrationTests;

/// <summary>
/// The same queries, run against every engine, asserting the rows that come back.
/// <para>
/// The cases are chosen where the dialects diverge most, because that is where a text-only test is
/// weakest: a percentile that is a window function on one engine and an aggregate on another, a
/// truncation each spells differently, a relative window resolved by the server's clock, a row cap
/// that is <c>LIMIT</c> here and <c>TOP</c> there, and an outer join through a key that finds
/// nothing. Each of those reads plausibly in every dialect and can still be wrong in one.
/// </para>
/// </summary>
public abstract class SqlExecutionTests
{
    private static readonly QuerySchema Schema = IntegrationSchema.Build();

    /// <summary>The engine this run is against.</summary>
    protected abstract SqlEngine Engine { get; }

    [Fact]
    public void ARelativeWindowKeepsTheRowsInsideIt()
    {
        // Four of the five rows are inside thirty days; the fifth is forty days old. The window is
        // resolved by the server, so this also proves the engine's own clock agrees with the seed.
        var spec = QueryBuilder.From(Schema, "requests", "r")
            .Select("r", "route", "route")
            .Where(f => f.Since("r", "timestamp", 30, QueryTimeUnit.Day))
            .Build();

        Assert.Equal(4, Run(spec).Count);
    }

    [Fact]
    public void TruncatingToADayPutsTheRowsThatShareOneInTheSameGroup()
    {
        var spec = QueryBuilder.From(Schema, "requests", "r")
            .SelectPeriod("r", "timestamp", QueryDateTruncation.Day, "day")
            .CountRows("total")
            .GroupByPeriod("r", "timestamp", QueryDateTruncation.Day)
            .Build();

        // Two rows were logged the same number of days ago, so exactly one bucket holds two.
        var counts = Run(spec)
            .Select(row => Convert.ToInt64(row["total"], CultureInfo.InvariantCulture))
            .OrderByDescending(count => count)
            .ToArray();

        Assert.Equal([2L, 1L, 1L, 1L], counts);
    }

    [Fact]
    public void CappingRowsReturnsTheSlowestOnesInOrder()
    {
        // LIMIT on two of these engines, TOP on the third, and the answer has to be the same.
        var spec = QueryBuilder.From(Schema, "requests", "r")
            .Select("r", "durationMs", "ms")
            .OrderByDescending("r", "durationMs")
            .Limit(2)
            .Build();

        var durations = Run(spec)
            .Select(row => Convert.ToInt32(row["ms"], CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal([500, 400], durations);
    }

    [Fact]
    public void AnOuterJoinKeepsTheRowWhoseKeyFindsNothing()
    {
        // The /health row carries a key that no api_keys row has. An inner join would drop it; the
        // point of the outer one is that it survives with an empty name.
        var spec = QueryBuilder.From(Schema, "requests", "r")
            .LeftJoin("apiKeys", "a")
            .Select("r", "route", "route")
            .Select("a", "name", "key")
            .Where(f => f.Equal("r", "route", "/health"))
            .Build();

        var rows = Run(spec);

        var row = Assert.Single(rows);
        Assert.Equal("/health", row["route"]);
        Assert.Null(row["key"]);
    }

    [Fact]
    public void MatchingPartOfATextFindsEveryRowThatContainsIt()
    {
        var spec = QueryBuilder.From(Schema, "requests", "r")
            .Select("r", "route", "route")
            .Where(f => f.Contains("r", "route", "/api"))
            .Build();

        Assert.Equal(4, Run(spec).Count);
    }

    [Fact]
    public void AggregatesOverAGroupAgreeWithTheRowsInIt()
    {
        var spec = QueryBuilder.From(Schema, "requests", "r")
            .Select("r", "route", "route")
            .CountRows("total")
            .Sum("r", "durationMs", "totalMs")
            .GroupBy("r", "route")
            .OrderBy("r", "route")
            .Build();

        var rows = Run(spec);

        Assert.Equal(3, rows.Count);
        Assert.Equal("/api/a", rows[0]["route"]);
        Assert.Equal(2L, Convert.ToInt64(rows[0]["total"], CultureInfo.InvariantCulture));
        Assert.Equal(300L, Convert.ToInt64(rows[0]["totalMs"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void APercentileIsComputedWhereTheEngineHasOneAndRefusedWhereItDoesNot()
    {
        var spec = QueryBuilder.From(Schema, "requests", "r")
            .Percentile("r", "durationMs", 0.5, "median")
            .Build();

        if (!Engine.Dialect.Supports(QueryFeature.Percentile))
        {
            // Refused with a reason rather than approximated, which is the whole capability contract.
            Assert.SkipWhen(Engine.Unavailable is not null, Engine.Unavailable ?? string.Empty);
            Assert.Throws<QueryRenderException>(() => Engine.Run(spec, Schema));
            return;
        }

        // The durations are 100..500, so the median is 300 wherever it is computed. SQL Server's is a
        // window function and repeats the answer per row, which is why the distinct value is asserted
        // rather than the row count - the number is the contract, the shape is the engine's business.
        var medians = Run(spec)
            .Select(row => Convert.ToDouble(row["median"], CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();

        Assert.Equal([300d], medians);
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Run(QuerySpec spec)
    {
        Assert.SkipWhen(Engine.Unavailable is not null, Engine.Unavailable ?? string.Empty);
        return Engine.Run(spec, Schema);
    }
}

/// <summary>SQLite runs without a container, so these are the ones that always run.</summary>
public sealed class SqliteExecutionTests(SqliteEngine engine) : SqlExecutionTests, IClassFixture<SqliteEngine>
{
    /// <inheritdoc/>
    protected override SqlEngine Engine { get; } = engine;
}

/// <summary>PostgreSQL, in a container. Skipped where Docker is not running.</summary>
public sealed class PostgreSqlExecutionTests(PostgreSqlEngine engine) : SqlExecutionTests, IClassFixture<PostgreSqlEngine>
{
    /// <inheritdoc/>
    protected override SqlEngine Engine { get; } = engine;
}

/// <summary>SQL Server, in a container. Skipped where Docker is not running.</summary>
public sealed class SqlServerExecutionTests(SqlServerEngine engine) : SqlExecutionTests, IClassFixture<SqlServerEngine>
{
    /// <inheritdoc/>
    protected override SqlEngine Engine { get; } = engine;
}
