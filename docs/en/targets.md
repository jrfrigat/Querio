# Targets

One query object; six ways to turn it into something else. Every target declares what it cannot
express, and throws `QueryRenderException` rather than approximating.

## Querio.Sql

Parameterized SQL for three engines. Values never reach the text.

```csharp
var result = SqlRenderer.Render(spec, schema, PostgreSqlDialect.Instance);
result.Sql;          // the statement
result.Parameters;   // name/value pairs to hand your client
```

| | SQL Server | PostgreSQL | SQLite |
| --- | --- | --- | --- |
| Row cap | `TOP (n)`, or `OFFSET/FETCH` with a skip | `LIMIT/OFFSET` | `LIMIT/OFFSET` |
| Case-insensitive contains | `LIKE` | `ILIKE` | `LIKE` |
| Truncate to a day | `DATEADD(day, DATEDIFF(day, 0, x), 0)` | `date_trunc('day', x)` | `date(x)` |
| Percentile | ungrouped only | yes | **no** |
| Table functions | yes | yes | **no** |
| Quarter truncation | yes | yes | **no** |

Two details worth knowing:

- **Paging without an order.** `OFFSET` needs something to skip in, so SQL Server gets
  `ORDER BY (SELECT NULL)` when the query has no ordering of its own.
- **Grouped percentiles on SQL Server.** `PERCENTILE_CONT` there is a window function, not an
  aggregate, so it cannot be combined with `GROUP BY` in one statement. The dialect refuses and says
  so, rather than emitting something that returns a different shape.

Built-in functions are emitted bare (`UPPER(x)`); a qualified name is a routine and is quoted part
by part (`[dbo].[CalcTax](x)`). Quoting a built-in would send the engine looking for a routine that
does not exist.

## Querio.OneC

The 1C query language - the target that keeps the whole model honest, because it is not SQL-shaped.

```csharp
var result = OneCRenderer.Render(spec, schema);
result.Query;        // Cyrillic keywords
result.Parameters;   // &p0 style
```

Cannot express: percentiles, `OFFSET`, cross joins, table functions. Each is declared, not
discovered.

1C cannot quote an identifier at all, so a name that is not a valid 1C identifier is **refused**
rather than escaped. That is the honest answer - there is no escaping to do.

Relative windows are resolved to a parameter at render time, since 1C has no equivalent of
`DATEADD`. The query still *stores* the offset; only this target pins it down.

## Querio.Linq

Real expression trees, not an interpreter.

```csharp
// Hand EF Core something it can translate
var predicate = QueryPredicate.For<RequestRow>(spec, schema);
var rows = dbContext.Requests.Where(predicate).ToList();

// Or run the whole query over objects
var result = QueryExecutor.Execute(spec, schema, new QuerySources()
    .Add("requests", requests)
    .Add("apiKeys", apiKeys));
```

**No Entity Framework dependency, and none wanted.** `System.Linq.Expressions` is in the base class
library, and EF consumes those trees directly.

The trees are real operators: a fixed value is narrowed to the member's CLR type while the query is
built, so a condition comes out as `x.DurationMs > 100` with a typed constant, and set membership
comes out as a single `List<T>.Contains` node rather than a chain of equality tests. A provider can
read all of it.

Cannot express: right and full outer joins. A sequence of objects has no natural shape for them.

Aggregates over no rows yield null rather than zero, matching what a store returns.

Functions a schema declares have no implementation until you give them one:

```csharp
var functions = new QueryFunctionLibrary()
    .Register<string, string>("upper", text => text.ToUpperInvariant())
    .RegisterTable("activeUsers", args => users.Where(u => u.LastSeen > (DateTime)args[0]!));
```

Calling one nobody implemented throws. A schema says a function *exists*; it never says what it
does.

## Querio.Http

A query string, both directions.

```csharp
var text = QueryHttp.Render(spec, schema);    // readable: store it, log it, show it
var url  = QueryHttp.ToUri(spec, schema, "/reports");   // percent-encoded: put it in a link
var back = QueryHttp.ParseUri(url, schema);
```

```
from=requests:r
&select=r.route as route,count() as total
&where=r.timestamp ge -30d and r.error eq 'true'
&groupby=r.route
&orderby=total desc
&top=20
```

Keys are logical, so the same text still means the same query against a differently-named store.
Values are written back in the exact stored form, so reading returns the same value rather than one
that merely looks the same - quotes, commas and brackets inside a value survive.

## Querio.Text

A query as a sentence, both directions.

```csharp
QueryDescriber.Describe(spec, schema);
// From Requests (r), showing Route and the number of rows called total,
// where (Timestamp is at least the last 30 days and Error is "true"),
// grouped by Route, ordered by total descending, first 20
```

Written in the labels a person chose, not the names a store uses. Every connective is swappable, so
a query can be described - and read back - in another language:

```csharp
var labels = QueryDescriptionLabels.Default with { From = "из", Showing = "показываем", Where = "где" };
var said   = QueryDescriber.Describe(spec, schema, labels);
var read   = QueryDescriber.Parse(said, schema, labels);
```

Fields are qualified with their alias only when the query reaches more than one source, so the
single-table case a report usually is stays short. Reading applies the same rule, so the two cannot
disagree.

## Querio.Language

SQL-shaped text, with the one thing SQL cannot write. See [the language](query-language.md).

## Choosing between them

- Storing a query where a person may read or edit it -> `Querio.Http` or `Querio.Text`
- Showing a query for approval -> `Querio.Text`
- Letting someone write one -> `Querio.Language`
- Running it against a database -> `Querio.Sql` or `Querio.OneC`
- Running it against objects, or through EF -> `Querio.Linq`

They compose: describe the query in words for a confirmation dialog, render it to SQL to run, and
keep the URL in the audit log. Same object each time.
