<p align="center">
  <a href="https://github.com/jrfrigat/Querio">
    <img src="assets/banner.svg" alt="Querio - store-agnostic relational query model for .NET" width="860">
  </a>
</p>

# Querio - one query, every backend

<p align="center">🌐 <b>English</b> · <a href="README.ru.md">Русский</a></p>

[![CI](https://github.com/jrfrigat/Querio/actions/workflows/ci.yml/badge.svg)](https://github.com/jrfrigat/Querio/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Querio.svg)](https://www.nuget.org/packages/Querio/)
[![Downloads](https://img.shields.io/nuget/dt/Querio.svg)](https://www.nuget.org/packages/Querio/)
[![.NET](https://img.shields.io/badge/.NET-standard2.0%20%7C%208%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Querio builds queries and never runs them. You describe a schema - entities, fields, foreign keys,
functions - and compose a query against it. What comes out is a plain serializable object, not a
string of SQL. Something else decides what that object becomes.

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

That one object renders to any of these, unchanged:

| Target | Package | What you get |
| --- | --- | --- |
| **SQL** | `Querio.Sql` | Parameterized SQL for SQL Server, PostgreSQL or SQLite |
| **1C** | `Querio.OneC` | The 1C query language, which is not SQL-shaped at all |
| **LINQ** | `Querio.Linq` | Real expression trees - hand them to EF Core, or run them over objects |
| **URL** | `Querio.Http` | A query string that reads back into the same query |
| **Words** | `Querio.Text` | A sentence a person can check, which also reads back |
| **Text** | `Querio.Language` | SQL-shaped text with foreign-key navigation, plus editor completion |

---

## Why this exists

Most query builders are SQL builders wearing a coat. They assume joins look like `JOIN`, that
paging looks like `LIMIT`, that a percentile is a function call. The moment a backend disagrees -
1C's query language disagrees about nearly all of it - the abstraction tears.

Querio's model is **semantic, not syntactic**:

- **Values travel as data.** A condition holds a value, never a fragment of text, so no renderer
  ever concatenates one into a query. Parameterisation belongs to the target.
- **Names are logical.** `QueryEntity.Source` and `QueryField.Column` carry the physical names, so
  the same saved query runs against `dbo.RequestLog` here and something else named differently
  there.
- **Aggregates and periods are enums**, not function names. Each target maps `Percentile` or
  `Truncate(Day)` onto whatever it actually has.
- **Relative time stays relative.** A saved "last 30 days" still means the last 30 days next year,
  because the offset is stored, not the date it resolved to.
- **A target declares what it cannot do.** `Querio.Sql`'s SQLite dialect has no percentile;
  `Querio.OneC` has no `OFFSET`. Asking for one throws with a reason rather than quietly answering
  a narrower question.

That last point is the one that pays. An abstraction that silently approximates is worse than no
abstraction, because you find out in production.

---

## Quick start

```sh
dotnet add package Querio
dotnet add package Querio.Sql      # and whichever other targets you need
```

Describe what can be queried:

```csharp
var schema = new QuerySchema(
    [
        new QueryEntity("requests", "Requests",
        [
            new QueryField("id",        "Id",           QueryFieldType.Guid),
            new QueryField("route",     "Route",        QueryFieldType.Text),
            new QueryField("timestamp", "Timestamp",    QueryFieldType.DateTime),
            new QueryField("error",     "Error",        QueryFieldType.Boolean),
            new QueryField("apiKeyId",  "API key",      QueryFieldType.Guid) { Column = "api_key_id" },
        ])
        { Source = "dbo.RequestLog", PrimaryKey = ["id"] },

        new QueryEntity("apiKeys", "API keys",
        [
            new QueryField("id",   "Id",   QueryFieldType.Guid),
            new QueryField("name", "Name", QueryFieldType.Text),
        ])
        { Source = "dbo.ApiKeys", PrimaryKey = ["id"] },
    ],
    [
        QueryRelation.Simple("request_apiKey", "requests", "apiKeyId", "apiKeys", "id"),
    ]);
```

Render it:

```csharp
var result = SqlRenderer.Render(spec, schema, SqlServerDialect.Instance);
// result.Sql        -> SELECT TOP (20) [r].[route], COUNT(*) AS [total] FROM [dbo].[RequestLog] ...
// result.Parameters -> @p0 = True
```

---

## The query language

`Querio.Language` reads SQL-shaped text. It looks like SQL because that is what people already
know, but it is not SQL, and the difference is the point - a field can be reached **through a
foreign key**, any number of hops:

```sql
select [r].[route], [r].[apiKeyId].[name] as [key], count(*) as [total]
from [dbo].[RequestLog] as [r]
where [r].[timestamp] >= now - 30 day and [r].[error] = true
group by [r].[route], [r].[apiKeyId].[name]
order by [total] desc
limit 20
```

Each hop becomes a join, which the target then renders however suits it - an explicit `JOIN` in
SQL, a dotted reference in 1C. The joins are outer on purpose: travelling a key that happens to be
empty must not make the row disappear.

Nothing stops at the first mistake. Every problem comes back with the exact span it occupies, plus
whatever query could still be built from the rest - which is what an editor needs while somebody is
halfway through typing:

```csharp
var result = QueryLanguage.Read(text, schema);
result.Spec;         // the partial query, or null
result.Diagnostics;  // every problem, each with Start/Length/Message
```

`QueryCompletion.Suggest(text, caret, schema)` answers what could be written next, which is how the
editor offers fields, functions and the keys that can be travelled further.

---

## Knowing what may be asked next

A query is built one step at a time, and each step narrows the next. `QueryChoices` answers that,
in the core, so a visual designer, a command line and a tool a model calls all agree about what is
allowed:

```csharp
var choices = QueryChoices.For(spec, schema, SqliteDialect.Instance);

choices.Fields;                 // what may be selected, given what the query reaches
choices.Joins;                  // what may be brought in next, and through which relation
choices.OperatorsFor(field);    // narrowed to what the target can express
choices.SortTargets;            // fields, plus the names of things already selected
```

Pass an `IQueryCapabilities` and the answers narrow to one backend: choose SQLite and the
percentile aggregate stops being offered, choose 1C and cross joins do. Nothing is offered that
would only fail once rendered.

---

## Round-tripping

Two targets read back what they wrote, which is what makes them safe to store a query in:

```csharp
// A URL that survives a bookmark
var url  = QueryHttp.ToUri(spec, schema, "/reports");
var back = QueryHttp.ParseUri(url, schema);

// A sentence somebody can check before it runs
var said = QueryDescriber.Describe(spec, schema);
var read = QueryDescriber.Parse(said, schema);
```

Reading is deliberately **not** the mirror image of writing. Writing is total - every query can be
written down. Reading is partial, because text is free to say things the model has no room for, so
a reader either recovers exactly what was written or refuses and says where. It never salvages the
half it understood.

---

## Packages

| Package | Depends on | Purpose |
| --- | --- | --- |
| [`Querio`](https://www.nuget.org/packages/Querio/) | nothing | Schema, query model, fluent builder, validator, `QueryChoices` |
| [`Querio.Sql`](https://www.nuget.org/packages/Querio.Sql/) | `Querio` | SQL Server / PostgreSQL / SQLite |
| [`Querio.OneC`](https://www.nuget.org/packages/Querio.OneC/) | `Querio` | The 1C query language |
| [`Querio.Linq`](https://www.nuget.org/packages/Querio.Linq/) | `Querio` | Expression trees; runs over `IQueryable` or over objects |
| [`Querio.Http`](https://www.nuget.org/packages/Querio.Http/) | `Querio` | Query string, both directions |
| [`Querio.Text`](https://www.nuget.org/packages/Querio.Text/) | `Querio` | Plain words, both directions |
| [`Querio.Language`](https://www.nuget.org/packages/Querio.Language/) | `Querio` | SQL-shaped text + completion |

**No package takes a third-party dependency.** Not one - the whole set references only the base
class library. That is enforced by a test, not by intent, so a query model can be added to
something that already exists without an argument about what it drags in.

Entity Framework needs no wrapper: `Querio.Linq` builds BCL expression trees, which EF consumes
directly. Dapper needs none either - it takes the text and parameters `Querio.Sql` already returns.

---

## Target frameworks

`netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. The reach back to netstandard2.0 is deliberate: a
saved query is a contract, and the thing reading it may be an old service nobody intends to
retarget.

---

## Documentation

- [Getting started](docs/en/getting-started.md)
- [Architecture](docs/en/architecture.md) - why the model is semantic rather than SQL-shaped
- [The query language](docs/en/query-language.md)
- [Targets](docs/en/targets.md) - what each one can and cannot express
- [Working with AI agents](docs/en/ai-agents.md)
- [API reference](https://jrfrigat.github.io/Querio/)

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). In short: fork, branch off `main`, add tests, keep the build
and the test suite green.

## License

[MIT](LICENSE)
