# Getting started

## Install

```sh
dotnet add package Querio          # the model, the builder, the validator
dotnet add package Querio.Sql      # and whichever targets you need
```

Nothing else comes with them - the packages reference only the base class library.

## 1. Describe what can be queried

A `QuerySchema` is the vocabulary a query is allowed to use. It is yours to write: Querio does not
read a database, and deliberately so, because the set of things a user may query is rarely the same
as the set of tables that exist.

```csharp
var schema = new QuerySchema(
    entities:
    [
        new QueryEntity("requests", "Requests",
        [
            new QueryField("id",        "Id",         QueryFieldType.Guid),
            new QueryField("route",     "Route",      QueryFieldType.Text),
            new QueryField("timestamp", "Timestamp",  QueryFieldType.DateTime),
            new QueryField("durationMs","Duration, ms", QueryFieldType.Number) { Column = "duration_ms" },
            new QueryField("error",     "Error",      QueryFieldType.Boolean),
            new QueryField("apiKeyId",  "API key",    QueryFieldType.Guid) { Column = "api_key_id" },
        ])
        { Source = "dbo.RequestLog", PrimaryKey = ["id"] },

        new QueryEntity("apiKeys", "API keys",
        [
            new QueryField("id",   "Id",   QueryFieldType.Guid),
            new QueryField("name", "Name", QueryFieldType.Text),
        ])
        { Source = "dbo.ApiKeys", PrimaryKey = ["id"] },
    ],
    relations:
    [
        QueryRelation.Simple("request_apiKey", "requests", "apiKeyId", "apiKeys", "id"),
    ]);
```

Three things to notice:

- **`Key` is logical, `Source`/`Column` is physical.** The query says `requests` and `apiKeyId`; the
  SQL says `dbo.RequestLog` and `api_key_id`. Rename the column later and no saved query breaks.
- **`Label` is for people.** Pickers and generated descriptions use it.
- **`QueryFieldType` is semantic.** It decides which operators and aggregates are offered by
  default, and how a stored value is read back.

You can also narrow what a field allows:

```csharp
new QueryField("secret", "Secret", QueryFieldType.Text) { Filterable = false, Groupable = false }
```

## 2. Build a query

```csharp
var spec = QueryBuilder.From(schema, "requests", "r")
    .Join("apiKeys", "k")                       // the relation is inferred when only one reaches
    .Select("r", "route", "route")
    .Select("k", "name", "key")
    .CountRows("total")
    .Where(f => f
        .Since("r", "timestamp", 30, QueryTimeUnit.Day)
        .Equal("r", "error", true))
    .GroupBy("r", "route")
    .GroupBy("k", "name")
    .Having(f => f.SelectGreaterThan("total", 100))
    .OrderBySelectDescending("total")
    .Limit(20)
    .Build();
```

Aliases are mandatory on sources. That is what lets the same entity appear twice - a self-join, or
two foreign keys reaching the same target - without the two occurrences becoming ambiguous.

## 3. Check it

```csharp
var result = spec.Validate(schema);
if (!result.IsValid)
{
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.Path}: {error.Message}");
}
```

Validation is about coherence against the schema, not about any particular backend. Whether a given
target can *express* the query is a separate question, answered when you render it.

## 4. Render it

```csharp
var sql = SqlRenderer.Render(spec, schema, SqlServerDialect.Instance);

Console.WriteLine(sql.Sql);
foreach (var p in sql.Parameters)
    Console.WriteLine($"{p.Name} = {p.Value}");
```

```
SELECT TOP (20) [r].[route] AS [route], [k].[name] AS [key], COUNT(*) AS [total]
FROM [dbo].[RequestLog] AS [r]
INNER JOIN [dbo].[ApiKeys] AS [k] ON [r].[api_key_id] = [k].[id]
WHERE [r].[timestamp] >= DATEADD(day, -30, SYSUTCDATETIME()) AND [r].[error] = @p0
GROUP BY [r].[route], [k].[name]
HAVING COUNT(*) > @p1
ORDER BY [total] DESC

@p0 = True
@p1 = 100
```

Hand the text and the parameters to whatever you already use - Dapper, ADO.NET, anything. Querio
opens no connections.

## 5. Save it and read it back

The query is a plain object, so any serializer will do:

```csharp
var json = JsonSerializer.Serialize(spec, JsonOptions);
var back = JsonSerializer.Deserialize<QuerySpec>(json, JsonOptions)!;
```

Or keep it somewhere a person can read:

```csharp
var url  = QueryHttp.ToUri(spec, schema, "/reports");   // survives a bookmark
var said = QueryDescriber.Describe(spec, schema);       // "From Requests (r), showing Route ..."
```

Both read back into the same query.

## When a target cannot do it

```csharp
try
{
    SqlRenderer.Render(spec, schema, SqliteDialect.Instance);
}
catch (QueryRenderException error)
{
    // error.Feature == QueryFeature.Percentile
    Console.WriteLine(error.Message);
}
```

This is intentional. A target that approximated instead would answer a different question and never
tell you.

To know in advance - to grey out an option in a UI, say - ask before building:

```csharp
var choices = QueryChoices.For(spec, schema, SqliteDialect.Instance);
choices.AggregatesFor(new QueryFieldRef("r", "durationMs"));   // Percentile is not in the list
```

## Next

- [Architecture](architecture.md) - why the model looks like this
- [Targets](targets.md) - what each one can and cannot express
- [The query language](query-language.md) - writing a query as text instead of code
