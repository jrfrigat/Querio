using System.Collections.Generic;
using System.Linq;

namespace Querio;

/// <summary>
/// Something a query may ask for that a particular store may not be able to do. Stores differ:
/// SQLite computes no percentiles, older engines have no full outer join, some query languages have
/// no row offset at all. A renderer declares what it supports so an unsupported request fails
/// loudly - quietly substituting something close would produce a query that runs and answers a
/// different question, which is far worse than an error.
/// </summary>
public enum QueryFeature
{
    /// <summary>Keeping rows that find no match on the other side of a join.</summary>
    LeftJoin,

    /// <summary>Keeping rows of the joined entity that find no match.</summary>
    RightJoin,

    /// <summary>Keeping unmatched rows from both sides of a join.</summary>
    FullJoin,

    /// <summary>Pairing every row with every other row.</summary>
    CrossJoin,

    /// <summary>Counting or summarising values over a group.</summary>
    Aggregates,

    /// <summary>Aggregating each repeated value once, as in a distinct count.</summary>
    DistinctAggregate,

    /// <summary>Computing a percentile of a group.</summary>
    Percentile,

    /// <summary>Collapsing rows into groups.</summary>
    Grouping,

    /// <summary>Filtering groups after aggregation.</summary>
    Having,

    /// <summary>Collapsing a timestamp to the start of its period.</summary>
    DateTruncation,

    /// <summary>Comparing against an offset from the current moment.</summary>
    RelativeTime,

    /// <summary>Dropping duplicate rows from the result.</summary>
    Distinct,

    /// <summary>Capping how many rows come back.</summary>
    Limit,

    /// <summary>Skipping rows before returning any.</summary>
    Offset,

    /// <summary>Comparing one field against another rather than against a value.</summary>
    FieldComparison,

    /// <summary>Testing membership of a set of values.</summary>
    SetOperators,

    /// <summary>Testing whether a value falls within a range.</summary>
    RangeOperators,

    /// <summary>Matching part of a text value.</summary>
    TextSearch,

    /// <summary>Calling a declared function that yields a value.</summary>
    ValueFunctions,

    /// <summary>Drawing rows from a declared function that yields a table.</summary>
    TableFunctions,
}

/// <summary>
/// What a render target can do. A visual designer takes one of these to grey out what the chosen
/// backend cannot deliver, without having to reference any renderer.
/// </summary>
public interface IQueryCapabilities
{
    /// <summary>Whether the target can do the given thing.</summary>
    /// <param name="feature">The capability being asked about.</param>
    bool Supports(QueryFeature feature);
}

/// <summary>
/// A refinement for a target that collapses a moment to some periods but not all of them.
/// <para>
/// <see cref="QueryFeature.DateTruncation"/> is deliberately coarse - it is the flag a designer greys
/// a whole control out by - but an engine can perfectly well have a month and no quarter. Without
/// this the flag promises more than the renderer delivers, and the gap is discovered as a render
/// error after somebody picked the period. A target implements this alongside
/// <see cref="IQueryCapabilities"/>; <see cref="QueryChoices"/> then offers only what it confirms.
/// </para>
/// </summary>
public interface IQueryPeriodCapabilities
{
    /// <summary>Whether a moment can be collapsed to the start of this period.</summary>
    /// <param name="period">The period being asked about.</param>
    bool SupportsPeriod(QueryDateTruncation period);
}

/// <summary>
/// A refinement for a target whose support for an aggregate depends on whether the query groups.
/// <para>
/// Some engines compute a percentile only as a window function, which cannot be combined with a
/// GROUP BY in one statement - so the aggregate is available, but not everywhere. Declaring it
/// unconditionally would let a builder offer it in exactly the query that cannot run.
/// </para>
/// </summary>
public interface IQueryAggregateCapabilities
{
    /// <summary>Whether the aggregate can be computed in a query that does or does not group.</summary>
    /// <param name="aggregate">The aggregate being asked about.</param>
    /// <param name="grouped">Whether the query collapses rows into groups.</param>
    bool SupportsAggregate(QueryAggregate aggregate, bool grouped);
}

/// <summary>A capability set built from an explicit list of features.</summary>
public sealed class QueryCapabilities : IQueryCapabilities
{
    private readonly HashSet<QueryFeature> _supported;

    /// <summary>Builds a capability set over exactly the features listed.</summary>
    /// <param name="supported">The features the target can do.</param>
    public QueryCapabilities(IEnumerable<QueryFeature> supported)
        => _supported = new HashSet<QueryFeature>(supported ?? []);

    /// <summary>A set in which everything is supported, as a starting point to subtract from.</summary>
    public static QueryCapabilities All
        => new(Enum.GetValues(typeof(QueryFeature)).Cast<QueryFeature>());

    /// <summary>Returns a copy of this set with the given features removed.</summary>
    /// <param name="features">The features the target cannot do.</param>
    public QueryCapabilities Without(params QueryFeature[] features)
        => new(_supported.Where(feature => !features.Contains(feature)));

    /// <inheritdoc/>
    public bool Supports(QueryFeature feature) => _supported.Contains(feature);
}

/// <summary>
/// Thrown when a query cannot be rendered for a particular target - most often because it asks for
/// something the target cannot do.
/// </summary>
public sealed class QueryRenderException : Exception
{
    /// <summary>Builds the exception with a description of what went wrong.</summary>
    /// <param name="message">What could not be rendered, and why.</param>
    public QueryRenderException(string message) : base(message)
    {
    }

    /// <summary>Builds the exception for a capability the target does not have.</summary>
    /// <param name="message">What could not be rendered, and why.</param>
    /// <param name="feature">The capability the target lacks.</param>
    public QueryRenderException(string message, QueryFeature feature) : base(message)
        => Feature = feature;

    /// <summary>The capability the target lacks, when that is what caused the failure.</summary>
    public QueryFeature? Feature { get; }
}
