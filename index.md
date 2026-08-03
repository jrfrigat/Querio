# Querio

**One query, every backend.** Querio builds queries and never runs them: a schema goes in, a plain
serializable object comes out, and something else decides what that object becomes.

```csharp
var spec = QueryBuilder.From(schema, "requests", "r")
    .Select("r", "route")
    .CountRows("total")
    .Where(f => f.Since("r", "timestamp", 30, QueryTimeUnit.Day).Equal("r", "error", true))
    .GroupBy("r", "route")
    .OrderBySelectDescending("total")
    .Limit(20)
    .Build();
```

That object renders, unchanged, to parameterized SQL (SQL Server, PostgreSQL, SQLite), the 1C query
language, LINQ expression trees, a URL, or a sentence a person can read.

## Where to start

- **[Getting started](docs/en/getting-started.md)** - describe a schema, build a query, render it
- **[Architecture](docs/en/architecture.md)** - why the model is semantic rather than SQL-shaped
- **[The query language](docs/en/query-language.md)** - SQL-shaped text that can walk a foreign key
- **[Targets](docs/en/targets.md)** - what each one can and cannot express
- **[API reference](api/index.md)**

Русская документация: [быстрый старт](docs/ru/getting-started.md).

## What makes it different

A target **declares what it cannot do** and throws rather than approximating. An abstraction that
quietly answers a narrower question is worse than no abstraction, because you find out in
production.

**No shipped package takes a third-party dependency** - the whole set references only the base class
library, enforced by a test. Entity Framework needs no wrapper, and neither does Dapper.

**Values travel as data**, never as text, so nothing is ever spliced into a query in any target.
