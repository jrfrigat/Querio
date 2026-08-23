namespace Querio.Tests;

/// <summary>
/// The specs the checked-in corpus under <c>Corpus/</c> is generated from.
/// <para>
/// These are built by constructing the records directly rather than through <see cref="QueryBuilder"/>,
/// on purpose: the corpus exists to pin the serialized shape, and going through the builder would
/// make it pin whatever the builder happens to produce today instead. A document here is a specimen
/// of the format, not necessarily a query that validates - the format has to keep reading back what
/// somebody saved, including combinations a later schema or validator would reject.
/// </para>
/// </summary>
internal static class CorpusDocuments
{
    /// <summary>Every corpus document, keyed by the file name it is stored under.</summary>
    internal static IReadOnlyDictionary<string, QuerySpec> All { get; } = new Dictionary<string, QuerySpec>
    {
        ["minimal"] = Minimal(),
        ["joins"] = Joins(),
        ["aggregates"] = Aggregates(),
        ["grouping"] = Grouping(),
        ["filters"] = Filters(),
        ["relative-time"] = RelativeTime(),
        ["functions"] = Functions(),
        ["paging"] = Paging(),
    };

    // The smallest thing that is still a query. Guards the defaults: every collection member has to
    // serialize as an empty array rather than vanishing, or a reader on an older build sees null.
    private static QuerySpec Minimal() => new(new QuerySource("requests", "r"));

    // Every QueryJoinKind, plus the three shapes a join can take: a declared relation, a relation
    // disambiguated by From, and an explicit On for a join the schema does not declare.
    private static QuerySpec Joins() => new(new QuerySource("requests", "r"))
    {
        Joins =
        [
            new QueryJoin("apiKeys", "a") { Relation = "request_apiKey" },
            new QueryJoin("users", "u") { Relation = "apiKey_owner", From = "a", Kind = QueryJoinKind.Left },
            new QueryJoin("users", "m") { Relation = "user_manager", From = "u", Kind = QueryJoinKind.Right },
            new QueryJoin("orders", "o")
            {
                Kind = QueryJoinKind.Full,
                On =
                [
                    new QueryJoinCondition(new QueryFieldRef("r", "apiKeyId"), new QueryFieldRef("o", "tenantId")),
                ],
            },
            new QueryJoin("transfers", "t") { Kind = QueryJoinKind.Cross },
            // Composite key, traversed by relation: the pair of columns lives in the schema, so the
            // saved document carries only the relation key.
            new QueryJoin("orderLines", "ol") { Relation = "order_lines", From = "o" },
        ],
        Select = [new QuerySelect { Field = new QueryFieldRef("r", "route") }],
    };

    // Every QueryAggregate, the row count that carries no field at all, and the distinct flag.
    private static QuerySpec Aggregates() => new(new QuerySource("requests", "r"))
    {
        Select =
        [
            new QuerySelect { Aggregate = QueryAggregate.Count, Alias = "total" },
            new QuerySelect { Field = new QueryFieldRef("r", "id"), Aggregate = QueryAggregate.Count, Alias = "ids" },
            new QuerySelect
            {
                Field = new QueryFieldRef("r", "apiKeyId"),
                Aggregate = QueryAggregate.Count,
                Distinct = true,
                Alias = "keys",
            },
            new QuerySelect { Field = new QueryFieldRef("r", "durationMs"), Aggregate = QueryAggregate.Sum, Alias = "totalMs" },
            new QuerySelect { Field = new QueryFieldRef("r", "durationMs"), Aggregate = QueryAggregate.Avg, Alias = "meanMs" },
            new QuerySelect { Field = new QueryFieldRef("r", "durationMs"), Aggregate = QueryAggregate.Min, Alias = "fastest" },
            new QuerySelect { Field = new QueryFieldRef("r", "durationMs"), Aggregate = QueryAggregate.Max, Alias = "slowest" },
            new QuerySelect
            {
                Field = new QueryFieldRef("r", "durationMs"),
                Aggregate = QueryAggregate.Percentile,
                Percentile = 0.95,
                Alias = "p95",
            },
        ],
    };

    // Every QueryDateTruncation, on both sides of the query - the grouping key and the returned
    // column have to agree, so both carry the same truncation - plus a filter on a group.
    private static QuerySpec Grouping() => new(new QuerySource("requests", "r"))
    {
        Select =
        [
            new QuerySelect { Field = new QueryFieldRef("r", "timestamp"), Truncate = QueryDateTruncation.Day, Alias = "day" },
            new QuerySelect { Aggregate = QueryAggregate.Count, Alias = "total" },
        ],
        GroupBy =
        [
            new QueryGroupBy(new QueryFieldRef("r", "timestamp")) { Truncate = QueryDateTruncation.Minute, Alias = "minute" },
            new QueryGroupBy(new QueryFieldRef("r", "timestamp")) { Truncate = QueryDateTruncation.Hour, Alias = "hour" },
            new QueryGroupBy(new QueryFieldRef("r", "timestamp")) { Truncate = QueryDateTruncation.Day, Alias = "day" },
            new QueryGroupBy(new QueryFieldRef("r", "timestamp")) { Truncate = QueryDateTruncation.Week, Alias = "week" },
            new QueryGroupBy(new QueryFieldRef("r", "timestamp")) { Truncate = QueryDateTruncation.Month, Alias = "month" },
            new QueryGroupBy(new QueryFieldRef("r", "timestamp")) { Truncate = QueryDateTruncation.Quarter, Alias = "quarter" },
            new QueryGroupBy(new QueryFieldRef("r", "timestamp")) { Truncate = QueryDateTruncation.Year, Alias = "year" },
            new QueryGroupBy(new QueryFieldRef("r", "route")),
        ],
        Having = new QueryFilterGroup
        {
            Conditions = [new QueryCondition(null, QueryOperator.GreaterThan) { Select = "total", Value = QueryOperand.Literal("10") }],
        },
    };

    // Every QueryOperator and every QueryOperandKind, and a nested group so the AND/OR tree is not
    // flat. IsNull and IsNotNull carry no operand at all, which is the case a reader most easily
    // gets wrong.
    private static QuerySpec Filters() => new(new QuerySource("requests", "r"))
    {
        Joins = [new QueryJoin("apiKeys", "a") { Relation = "request_apiKey" }],
        Where = new QueryFilterGroup
        {
            Conditions =
            [
                new QueryCondition(new QueryFieldRef("r", "route"), QueryOperator.Contains) { Value = QueryOperand.Literal("/api") },
                new QueryCondition(new QueryFieldRef("r", "route"), QueryOperator.StartsWith) { Value = QueryOperand.Literal("/v1") },
                new QueryCondition(new QueryFieldRef("r", "route"), QueryOperator.EndsWith) { Value = QueryOperand.Literal(".json") },
                new QueryCondition(new QueryFieldRef("r", "error"), QueryOperator.Equals) { Value = QueryOperand.Literal("true") },
                new QueryCondition(new QueryFieldRef("r", "cacheHit"), QueryOperator.NotEquals) { Value = QueryOperand.Literal("false") },
                new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.GreaterThan) { Value = QueryOperand.Literal("399") },
                new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.GreaterThanOrEqual) { Value = QueryOperand.Literal("400") },
                new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.LessThan) { Value = QueryOperand.Literal("600") },
                new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.LessThanOrEqual) { Value = QueryOperand.Literal("599") },
                new QueryCondition(new QueryFieldRef("r", "durationMs"), QueryOperator.Between)
                {
                    Value = QueryOperand.Literal("100"),
                    Value2 = QueryOperand.Literal("500"),
                },
                new QueryCondition(new QueryFieldRef("r", "durationMs"), QueryOperator.NotBetween)
                {
                    Value = QueryOperand.Literal("0"),
                    Value2 = QueryOperand.Literal("10"),
                },
                new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.In)
                {
                    Value = QueryOperand.List(["500", "502", "503"]),
                },
                new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.NotIn)
                {
                    Value = QueryOperand.List(["200", "204"]),
                },
                // No operand at all: the null checks are the shape a reader most often mishandles.
                new QueryCondition(new QueryFieldRef("a", "name"), QueryOperator.IsNull),
                new QueryCondition(new QueryFieldRef("a", "ownerId"), QueryOperator.IsNotNull),
                // Field operand: comparing one participant against another.
                new QueryCondition(new QueryFieldRef("r", "apiKeyId"), QueryOperator.Equals)
                {
                    Value = QueryOperand.Of(new QueryFieldRef("a", "id")),
                },
                // Function operand.
                new QueryCondition(new QueryFieldRef("r", "route"), QueryOperator.Equals)
                {
                    Value = QueryOperand.Function(QueryFunctionCall.OfFields("upper", "r", "route")),
                },
            ],
            Groups =
            [
                new QueryFilterGroup
                {
                    Or = true,
                    Conditions =
                    [
                        new QueryCondition(new QueryFieldRef("r", "status"), QueryOperator.Equals) { Value = QueryOperand.Literal("500") },
                        new QueryCondition(new QueryFieldRef("r", "cacheHit"), QueryOperator.Equals) { Value = QueryOperand.Literal("true") },
                    ],
                    Groups =
                    [
                        new QueryFilterGroup
                        {
                            Conditions =
                            [
                                new QueryCondition(new QueryFieldRef("r", "route"), QueryOperator.Contains) { Value = QueryOperand.Literal("/health") },
                            ],
                        },
                    ],
                },
            ],
        },
    };

    // Every QueryTimeUnit, in both directions. A saved window has to stay relative: freezing it into
    // a timestamp at save time is the bug this whole operand kind exists to prevent.
    private static QuerySpec RelativeTime() => new(new QuerySource("requests", "r"))
    {
        Where = new QueryFilterGroup
        {
            Conditions =
            [
                Ago("timestamp", 15, QueryTimeUnit.Minute),
                Ago("timestamp", 6, QueryTimeUnit.Hour),
                Ago("timestamp", 30, QueryTimeUnit.Day),
                Ago("timestamp", 2, QueryTimeUnit.Week),
                Ago("timestamp", 3, QueryTimeUnit.Month),
                Ago("timestamp", 1, QueryTimeUnit.Quarter),
                Ago("timestamp", 1, QueryTimeUnit.Year),
                // Forward, so the sign is carried rather than assumed negative.
                new QueryCondition(new QueryFieldRef("r", "timestamp"), QueryOperator.LessThan)
                {
                    Value = QueryOperand.FromNow(7, QueryTimeUnit.Day),
                },
                new QueryCondition(new QueryFieldRef("r", "timestamp"), QueryOperator.Between)
                {
                    Value = QueryOperand.Ago(1, QueryTimeUnit.Day),
                    Value2 = QueryOperand.FromNow(1, QueryTimeUnit.Day),
                },
            ],
        },
    };

    private static QueryCondition Ago(string field, int amount, QueryTimeUnit unit)
        => new(new QueryFieldRef("r", field), QueryOperator.GreaterThanOrEqual)
        {
            Value = QueryOperand.Ago(amount, unit),
        };

    // Both function kinds, in every position one can stand: a table function as the root source and
    // as a join, a value function in select, group by, order by and a condition, and a nested call.
    private static QuerySpec Functions() => new(QuerySource.FromFunction(
        QueryFunctionCall.Of("activeUsers", QueryOperand.Ago(90, QueryTimeUnit.Day)), "au"))
    {
        Joins =
        [
            new QueryJoin(null, "au2")
            {
                Kind = QueryJoinKind.Left,
                Call = QueryFunctionCall.Of("activeUsers", QueryOperand.Literal("2026-01-01T00:00:00Z")),
                On = [new QueryJoinCondition(new QueryFieldRef("au", "id"), new QueryFieldRef("au2", "id"))],
            },
        ],
        Select =
        [
            new QuerySelect { Call = QueryFunctionCall.OfFields("upper", "au", "name"), Alias = "shout" },
            // A call nested inside a call, which is why arguments are operands rather than fields.
            new QuerySelect
            {
                Call = QueryFunctionCall.Of(
                    "calcTax",
                    QueryOperand.Function(QueryFunctionCall.OfFields("calcTax", "au", "id")),
                    QueryOperand.Literal("0.2")),
                Aggregate = QueryAggregate.Sum,
                Alias = "tax",
            },
        ],
        GroupBy = [new QueryGroupBy(null) { Call = QueryFunctionCall.OfFields("upper", "au", "name"), Alias = "shout" }],
        OrderBy = [new QuerySort { Call = QueryFunctionCall.OfFields("upper", "au", "name") }],
    };

    // Distinct, both paging bounds, and every way an ordering can name its target.
    private static QuerySpec Paging() => new(new QuerySource("requests", "r"))
    {
        Select =
        [
            new QuerySelect { Field = new QueryFieldRef("r", "route") },
            new QuerySelect { Aggregate = QueryAggregate.Count, Alias = "total" },
        ],
        GroupBy = [new QueryGroupBy(new QueryFieldRef("r", "route"))],
        OrderBy =
        [
            new QuerySort { Select = "total", Direction = QuerySortDirection.Descending },
            new QuerySort { Field = new QueryFieldRef("r", "route"), Direction = QuerySortDirection.Ascending },
        ],
        Distinct = true,
        Limit = 50,
        Offset = 100,
    };
}
