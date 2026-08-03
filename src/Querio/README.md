# Querio

The core: a schema, a serializable query, a fluent builder, a validator, and the shared walk every
target renders through. It builds queries and never runs them.

```csharp
var spec = QueryBuilder.From(schema, "requests", "r")
    .Select("r", "route")
    .CountRows("total")
    .Where(f => f.Since("r", "timestamp", 30, QueryTimeUnit.Day))
    .GroupBy("r", "route")
    .OrderBySelectDescending("total")
    .Build();
```

What comes out is a plain object. Add a target package to turn it into something:

| | |
| --- | --- |
| `Querio.Sql` | SQL Server, PostgreSQL, SQLite |
| `Querio.OneC` | the 1C query language |
| `Querio.Linq` | expression trees for EF Core, or execution over objects |
| `Querio.Http` | a query string, both directions |
| `Querio.Text` | a sentence, both directions |
| `Querio.Language` | SQL-shaped text with foreign-key navigation |

**No dependencies.** This package references only the base class library.

Values travel as data and are parameterised by the renderer, so nothing is ever spliced into a
query. Names are logical, so the same saved query survives a rename in storage. A target declares
what it cannot express and fails loudly instead of approximating.

Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`.

[Documentation](https://github.com/jrfrigat/Querio) - [MIT](https://github.com/jrfrigat/Querio/blob/main/LICENSE)
